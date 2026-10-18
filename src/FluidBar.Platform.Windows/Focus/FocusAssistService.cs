using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Threading;
using Windows.UI.Shell;

namespace FluidBar.Platform.Windows.Focus;

/// <summary>
/// Production focus/quiet-hours backend over the documented
/// <c>Windows.UI.Shell.FocusSessionManager</c> API (Windows 11 22H2 / build 22621,
/// UniversalApiContract v15).
/// </summary>
/// <remarks>
/// <para><b>Mechanism.</b> Windows exposes no public quiet-hours/Focus-Assist registry or WNF toggle;
/// the documented write path is <see cref="FocusSessionManager.TryStartFocusSession()"/> and
/// <see cref="FocusSessionManager.DeactivateFocus()"/>, with
/// <see cref="FocusSessionManager.IsFocusActive"/> and
/// <see cref="FocusSessionManager.IsFocusActiveChanged"/> for state. The API is a
/// <b>Limited Access Feature</b> (feature id <c>com.microsoft.windows.focussessionmanager.1</c>):
/// without a Microsoft-issued unlock token both write calls fail with <c>E_ACCESSDENIED</c>
/// (0x80070005). Verified on Windows 11 25H2 build 26200.9711. The service therefore reports
/// <see cref="FocusAssistAvailability.LimitedAccessDenied"/> with that exact reason instead of
/// pretending the toggle worked.</para>
/// <para><b>Capability probe.</b> When focus is inactive, <see cref="FocusSessionManager.DeactivateFocus"/>
/// is a no-op, so the first state read calls it once to detect the LAF gate without changing anything.
/// When focus is already active the feature is evidently working and the probe is skipped.</para>
/// <para><b>TODO (requires an external decision, not code):</b> apply for the FocusSessionManager LAF
/// unlock token through the Microsoft LAF Access Token Request Form
/// (https://go.microsoft.com/fwlink/?linkid=2271232). Once a token is provisioned, call
/// <c>LimitedAccessFeatures.TryUnlockFeature("com.microsoft.windows.focussessionmanager.1", token,
/// attestation)</c> during start-up and delete the <see cref="FocusAssistAvailability.LimitedAccessDenied"/>
/// short-circuit. Do not ship a hard-coded token.</para>
/// </remarks>
public sealed class WindowsFocusAssistBackend : IFocusAssistBackend
{
    /// <summary>First Windows 11 build with the UniversalApiContract v15 focus APIs.</summary>
    public const int MinimumWindowsBuild = 22621;

    private const string MechanismName =
        "Windows.UI.Shell.FocusSessionManager (UniversalApiContract v15, Windows 11 22H2+)";

    private const string LimitedAccessFeatureId = "com.microsoft.windows.focussessionmanager.1";

    private readonly IPlatformLogger _logger;
    private readonly PlatformCounters _counters;
    private readonly object _gate = new();
    private FocusSessionManager? _manager;
    private FocusAssistSnapshot? _unavailable;
    private bool _subscribed;
    private int _disposed;

    /// <summary>Creates the backend.</summary>
    /// <param name="logger">Platform logger (failures are logged, never thrown to callers).</param>
    /// <param name="counters">Diagnostics counters.</param>
    public WindowsFocusAssistBackend(IPlatformLogger logger, PlatformCounters counters)
    {
        _logger = logger ?? NullPlatformLogger.Instance;
        _counters = counters ?? new PlatformCounters();
    }

    /// <inheritdoc />
    public event EventHandler? StateChanged;

    /// <inheritdoc />
    public Task<FocusAssistSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
        => Task.Run(GetSnapshotCore, cancellationToken);

    /// <inheritdoc />
    public Task<FocusToggleResult> SetActiveAsync(bool active, CancellationToken cancellationToken = default)
        => Task.Run(() => SetActiveCore(active), cancellationToken);

    private FocusAssistSnapshot GetSnapshotCore()
    {
        lock (_gate)
        {
            if (_unavailable is not null)
            {
                return _unavailable;
            }

            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, MinimumWindowsBuild))
            {
                _unavailable = new FocusAssistSnapshot(false, FocusAssistAvailability.Unsupported,
                    $"focus sessions require Windows 11 22H2 (build {MinimumWindowsBuild}) or later; " +
                    $"this OS is {Environment.OSVersion.Version} and has no documented quiet-hours toggle API",
                    MechanismName);
                _logger.LogInfo(PlatformLogCategory.Platform, "focus.unsupported-os",
                    _unavailable.Reason!);
                return _unavailable;
            }

            try
            {
                if (!FocusSessionManager.IsSupported)
                {
                    _unavailable = new FocusAssistSnapshot(false, FocusAssistAvailability.Unsupported,
                        "FocusSessionManager.IsSupported is false on this device (focus sessions are disabled " +
                        "by policy or not present in this Windows edition)", MechanismName);
                    _logger.LogInfo(PlatformLogCategory.Platform, "focus.not-supported",
                        _unavailable.Reason!);
                    return _unavailable;
                }

                var manager = _manager ??= FocusSessionManager.GetDefault();
                Subscribe(manager);
                var isActive = manager.IsFocusActive;
                if (!isActive)
                {
                    // No-op when focus is inactive. On a non-LAF build this succeeds; on this build it
                    // throws UnauthorizedAccessException and proves the feature is locked.
                    manager.DeactivateFocus();
                }

                return new FocusAssistSnapshot(isActive, FocusAssistAvailability.Available,
                    null, MechanismName);
            }
            catch (UnauthorizedAccessException ex)
            {
                _counters.IncrementComFailures();
                _unavailable = CreateLimitedAccessSnapshot(ex);
                _logger.LogWarning(PlatformLogCategory.Platform, "focus.limited-access",
                    _unavailable.Reason!);
                return _unavailable;
            }
            catch (Exception ex)
            {
                _counters.IncrementComFailures();
                _logger.LogWarning(PlatformLogCategory.Platform, "focus.read-failed",
                    "Reading the Windows focus state failed.", ex);
                return new FocusAssistSnapshot(false, FocusAssistAvailability.Failed,
                    $"reading the Windows focus state failed: {ex.Message}", MechanismName);
            }
        }
    }

    private FocusToggleResult SetActiveCore(bool active)
    {
        lock (_gate)
        {
            if (_unavailable is not null)
            {
                return FocusToggleResult.Unsupported(_unavailable.Reason!, _unavailable);
            }

            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, MinimumWindowsBuild))
            {
                var unsupported = new FocusAssistSnapshot(false, FocusAssistAvailability.Unsupported,
                    $"focus sessions require Windows 11 22H2 (build {MinimumWindowsBuild}) or later; " +
                    $"this OS is {Environment.OSVersion.Version}", MechanismName);
                return FocusToggleResult.Unsupported(unsupported.Reason!, unsupported);
            }

            try
            {
                var manager = _manager ??= FocusSessionManager.GetDefault();
                Subscribe(manager);
                if (active)
                {
                    var session = manager.TryStartFocusSession();
                    if (session is null)
                    {
                        _logger.LogWarning(PlatformLogCategory.Platform, "focus.start-returned-null",
                            "TryStartFocusSession returned null; Windows did not start a focus session.");
                        return FocusToggleResult.Failed(
                            "TryStartFocusSession returned null; Windows did not start a focus session " +
                            "(another session may be active, or focus sessions are disabled by policy)",
                            ReadSnapshot(manager));
                    }
                }
                else
                {
                    manager.DeactivateFocus();
                }

                var snapshot = ReadSnapshot(manager);
                _logger.LogInfo(PlatformLogCategory.Platform, "focus.set-accepted",
                    $"The focus request ({active}) was accepted.", new Dictionary<string, object?>
                    {
                        ["requested"] = active,
                        ["observed"] = snapshot.IsActive,
                    });
                return FocusToggleResult.Success(snapshot);
            }
            catch (UnauthorizedAccessException ex)
            {
                _counters.IncrementComFailures();
                _unavailable = CreateLimitedAccessSnapshot(ex);
                _logger.LogWarning(PlatformLogCategory.Platform, "focus.limited-access",
                    _unavailable.Reason!);
                return FocusToggleResult.Unsupported(_unavailable.Reason!, _unavailable);
            }
            catch (Exception ex)
            {
                _counters.IncrementComFailures();
                _logger.LogError(PlatformLogCategory.Platform, "focus.set-failed",
                    "Changing the Windows focus state failed.", ex);
                return FocusToggleResult.Failed(
                    $"changing the Windows focus state failed: {ex.Message}", CurrentOrDefault());
            }
        }
    }

    private static FocusAssistSnapshot ReadSnapshot(FocusSessionManager manager) => new(
        manager.IsFocusActive, FocusAssistAvailability.Available, null, MechanismName);

    private FocusAssistSnapshot? CurrentOrDefault()
    {
        var manager = _manager;
        return manager is null ? null : ReadSnapshot(manager);
    }

    private static FocusAssistSnapshot CreateLimitedAccessSnapshot(UnauthorizedAccessException ex) => new(
        false,
        FocusAssistAvailability.LimitedAccessDenied,
        $"Windows focus sessions are a Limited Access Feature (feature id {LimitedAccessFeatureId}): " +
        $"FocusSessionManager.TryStartFocusSession/DeactivateFocus fail with E_ACCESSDENIED " +
        $"(0x{ex.HResult:X8}: {ex.Message}). Microsoft must issue an unlock token; no focus state was changed.",
        MechanismName);

    private void Subscribe(FocusSessionManager manager)
    {
        if (_subscribed)
        {
            return;
        }

        manager.IsFocusActiveChanged += OnIsFocusActiveChanged;
        _subscribed = true;
    }

    private void OnIsFocusActiveChanged(FocusSessionManager sender, object args)
    {
        try
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _counters.IncrementMonitorCallbackFailures();
            _logger.LogWarning(PlatformLogCategory.Platform, "focus.state-callback-failed",
                "A focus state-change subscriber failed.", ex);
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }

        lock (_gate)
        {
            var manager = _manager;
            _manager = null;
            if (manager is not null && _subscribed)
            {
                try
                {
                    manager.IsFocusActiveChanged -= OnIsFocusActiveChanged;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(PlatformLogCategory.Platform, "focus.unsubscribe-failed",
                        $"Unsubscribing from IsFocusActiveChanged failed: {ex.Message}.");
                }
            }

            _subscribed = false;
        }

        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Event-driven focus/quiet-hours quick-toggle service. Wraps an <see cref="IFocusAssistBackend"/> so
/// lifecycle, change-only snapshots and explicit unsupported results are testable with a fake and no
/// real focus session.
/// </summary>
public sealed class FocusAssistService : PlatformMonitor<FocusAssistSnapshot>, IFocusAssistService
{
    private readonly IFocusAssistBackend _backend;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private int _stopped;

    /// <summary>Creates the service over a backend (production: <see cref="WindowsFocusAssistBackend"/>).</summary>
    public FocusAssistService(IFocusAssistBackend backend, IPlatformLogger logger, PlatformCounters counters,
        TimeProvider? timeProvider = null)
        : base("focus-assist", logger, counters, timeProvider ?? TimeProvider.System)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public bool? IsActive => Current?.IsActive;

    /// <inheritdoc />
    protected override async ValueTask OnStartAsync(CancellationToken cancellationToken)
    {
        Volatile.Write(ref _stopped, 0);
        _backend.StateChanged += OnBackendStateChanged;
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override ValueTask OnStopAsync(CancellationToken cancellationToken)
    {
        Volatile.Write(ref _stopped, 1);
        _backend.StateChanged -= OnBackendStateChanged;
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!await _refreshGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            Publish(await _backend.GetSnapshotAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Counters.IncrementComFailures();
            Logger.LogError(PlatformLogCategory.Platform, "focus.refresh-failed",
                "Refreshing the Windows focus state failed.", ex);
            Publish(new FocusAssistSnapshot(false, FocusAssistAvailability.Failed,
                $"refreshing the Windows focus state failed: {ex.Message}"));
        }
        finally
        {
            ReleaseRefreshGate();
        }
    }

    /// <inheritdoc />
    public async Task<FocusToggleResult> SetActiveAsync(bool active,
        CancellationToken cancellationToken = default)
    {
        FocusToggleResult result;
        try
        {
            result = await _backend.SetActiveAsync(active, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Counters.IncrementComFailures();
            Logger.LogError(PlatformLogCategory.Platform, "focus.toggle-failed",
                $"Setting the Windows focus state to {(active ? "active" : "inactive")} failed.", ex);
            return FocusToggleResult.Failed(
                $"setting the Windows focus state to {(active ? "active" : "inactive")} failed: {ex.Message}");
        }

        if (result.Snapshot is not null)
        {
            Publish(result.Snapshot);
        }

        if (!result.Succeeded)
        {
            Logger.LogWarning(PlatformLogCategory.Platform, "focus.toggle-rejected",
                $"The focus request was rejected ({result.Outcome}): {result.Reason}");
        }

        return result;
    }

    private void OnBackendStateChanged(object? sender, EventArgs e)
    {
        if (Volatile.Read(ref _stopped) != 0)
        {
            return;
        }

        _ = RefreshSafeAsync();
    }

    private async Task RefreshSafeAsync()
    {
        try
        {
            await RefreshAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Counters.IncrementMonitorCallbackFailures();
            Logger.LogError(PlatformLogCategory.Platform, "focus.state-refresh-failed",
                "Refreshing the Windows focus state after a change failed.", ex);
        }
    }

    private void ReleaseRefreshGate()
    {
        try
        {
            _refreshGate.Release();
        }
        catch (ObjectDisposedException)
        {
            // Shutdown raced with an in-flight refresh; nothing left to release.
        }
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync().ConfigureAwait(false);
        await _backend.DisposeAsync().ConfigureAwait(false);
        _refreshGate.Dispose();
    }
}
