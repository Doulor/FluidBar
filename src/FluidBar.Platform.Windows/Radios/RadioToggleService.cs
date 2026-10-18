using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Threading;
using Windows.Devices.Radios;

namespace FluidBar.Platform.Windows.Radios;

/// <summary>
/// Production radio backend over the documented <c>Windows.Devices.Radios</c> API
/// (<see cref="Radio.GetRadiosAsync"/>, <see cref="Radio.RequestAccessAsync"/>,
/// <see cref="Radio.SetStateAsync"/> and <see cref="Radio.StateChanged"/>).
/// </summary>
/// <remarks>
/// <para>Supported on Windows 10 1703 (10.0.15063) and later; the App's Windows 11 focus toggle is the
/// only piece with a higher OS floor. There is no polling: the state snapshot is refreshed on start,
/// after every request and whenever Windows raises <see cref="Radio.StateChanged"/>.</para>
/// <para>Failure modes are explicit: no radio → <see cref="RadioAvailability.NoAdapter"/>, denied
/// consent/policy → <see cref="RadioAvailability.AccessDenied"/>, hardware kill switch →
/// <see cref="RadioAvailability.HardwareDisabled"/>, driver not ready →
/// <see cref="RadioAvailability.Unavailable"/>. A state change returns
/// <see cref="RadioToggleOutcome"/> instead of a bool, so "accepted" can never be confused with
/// "changed".</para>
/// <para>Disposal unsubscribes <see cref="Radio.StateChanged"/> and drops the WinRT RCW reference;
/// <c>Radio</c> implements no <c>IClosable</c>, so dropping the last reference is the deterministic
/// release path (no polling, no finalizer dependency for correctness).</para>
/// </remarks>
public sealed class WindowsRadioBackend : IRadioBackend
{
    private readonly IPlatformLogger _logger;
    private readonly PlatformCounters _counters;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Radio? _radio;
    private RadioAccessStatus? _access;
    private int _disposed;

    /// <summary>Creates the backend for one radio kind.</summary>
    /// <param name="kind">Wi-Fi or Bluetooth.</param>
    /// <param name="logger">Platform logger (failures are logged, never thrown to callers).</param>
    /// <param name="counters">Diagnostics counters.</param>
    public WindowsRadioBackend(PlatformRadioKind kind, IPlatformLogger logger, PlatformCounters counters)
    {
        Kind = kind;
        _logger = logger ?? NullPlatformLogger.Instance;
        _counters = counters ?? new PlatformCounters();
    }

    /// <inheritdoc />
    public PlatformRadioKind Kind { get; }

    /// <inheritdoc />
    public event EventHandler? StateChanged;

    /// <inheritdoc />
    public async Task<RadioSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var radio = await GetRadioAsync(cancellationToken).ConfigureAwait(false);
            return radio is null ? RadioSnapshot.NoAdapter(Kind) : CreateSnapshot(radio);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _counters.IncrementComFailures();
            _logger.LogWarning(PlatformLogCategory.Platform, "radio.read-failed",
                $"Reading the {Kind} radio failed; reporting the radio as failed.", ex);
            ResetRadioCore();
            return RadioSnapshot.Failed(Kind, $"reading the {Kind} radio failed: {ex.Message}");
        }
        finally
        {
            ReleaseGate();
        }
    }

    /// <inheritdoc />
    public async Task<RadioToggleResult> SetEnabledAsync(bool enabled,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await SetEnabledCoreAsync(enabled, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            _counters.IncrementComFailures();
            _logger.LogWarning(PlatformLogCategory.Platform, "radio.access-denied",
                $"Windows denied access to the {Kind} radio.", ex);
            return RadioToggleResult.AccessDenied(
                $"Windows denied access to the {Kind} radio (0x{ex.HResult:X8}: {ex.Message}); " +
                "the radio was not changed", CurrentOrDefault());
        }
        catch (Exception ex)
        {
            _counters.IncrementComFailures();
            _logger.LogError(PlatformLogCategory.Platform, "radio.set-failed",
                $"Changing the {Kind} radio state failed.", ex);
            ResetRadioCore();
            return RadioToggleResult.Failed(
                $"changing the {Kind} radio state failed: {ex.Message}", CurrentOrDefault());
        }
        finally
        {
            ReleaseGate();
        }
    }

    private async Task<RadioToggleResult> SetEnabledCoreAsync(bool enabled, CancellationToken cancellationToken)
    {
        var radio = await GetRadioAsync(cancellationToken).ConfigureAwait(false);
        if (radio is null)
        {
            var missing = RadioSnapshot.NoAdapter(Kind);
            return RadioToggleResult.Unsupported(missing.Reason!, missing);
        }

        var before = CreateSnapshot(radio);
        if (before.Availability != RadioAvailability.Available)
        {
            // Hardware switch / unknown driver state: software must not claim it can fix this.
            return RadioToggleResult.Unsupported(before.Reason ?? $"{Kind} radio is not controllable", before);
        }

        var access = await EnsureAccessAsync(cancellationToken).ConfigureAwait(false);
        if (access != RadioAccessStatus.Allowed)
        {
            return RadioToggleResult.AccessDenied(
                $"Windows denied radio access (RadioAccessStatus.{access}); the {Kind} radio was not changed",
                before);
        }

        var target = enabled ? RadioState.On : RadioState.Off;
        var status = await radio.SetStateAsync(target).AsTask(cancellationToken).ConfigureAwait(false);
        var after = CreateSnapshot(radio);
        switch (status)
        {
            case RadioAccessStatus.Allowed:
                _logger.LogInfo(PlatformLogCategory.Platform, "radio.set-accepted",
                    $"The {Kind} radio accepted {target}.", new Dictionary<string, object?>
                    {
                        ["kind"] = Kind.ToString(),
                        ["requested"] = target.ToString(),
                        ["observed"] = radio.State.ToString(),
                    });
                return RadioToggleResult.Success(after);

            case RadioAccessStatus.DeniedByUser:
            case RadioAccessStatus.DeniedBySystem:
                _logger.LogWarning(PlatformLogCategory.Platform, "radio.set-denied",
                    $"Windows denied the {Kind} radio state change (RadioAccessStatus.{status}).");
                return RadioToggleResult.AccessDenied(
                    $"Windows denied the {Kind} radio state change (RadioAccessStatus.{status}); " +
                    "the user consent, system policy or app capability blocks it",
                    after);

            default:
                _counters.IncrementComFailures();
                _logger.LogWarning(PlatformLogCategory.Platform, "radio.set-unspecified",
                    $"SetStateAsync for the {Kind} radio returned RadioAccessStatus.{status}.");
                return RadioToggleResult.Failed(
                    $"SetStateAsync for the {Kind} radio returned RadioAccessStatus.{status}",
                    after);
        }
    }

    private async Task<RadioAccessStatus> EnsureAccessAsync(CancellationToken cancellationToken)
    {
        if (_access is { } cached)
        {
            return cached;
        }

        var status = await Radio.RequestAccessAsync().AsTask(cancellationToken).ConfigureAwait(false);
        if (status == RadioAccessStatus.Allowed)
        {
            _access = status;
        }

        return status;
    }

    private async Task<Radio?> GetRadioAsync(CancellationToken cancellationToken)
    {
        if (_radio is not null)
        {
            return _radio;
        }

        var radios = await Radio.GetRadiosAsync().AsTask(cancellationToken).ConfigureAwait(false);
        var matches = radios.Where(radio => radio.Kind == ToRadioKind(Kind)).ToList();
        // Prefer a software-controllable radio over one disabled by firmware/hardware.
        var match = matches.FirstOrDefault(radio => radio.State != RadioState.Disabled)
                    ?? matches.FirstOrDefault();
        if (match is null)
        {
            _logger.LogInfo(PlatformLogCategory.Platform, "radio.not-present",
                $"No {Kind} radio is present on this system.");
            return null;
        }

        match.StateChanged += OnRadioStateChanged;
        _radio = match;
        _logger.LogInfo(PlatformLogCategory.Platform, "radio.acquired",
            $"The {Kind} radio was acquired.", new Dictionary<string, object?>
            {
                ["name"] = match.Name,
                ["state"] = match.State.ToString(),
            });
        return match;
    }

    private RadioSnapshot CreateSnapshot(Radio radio)
    {
        var name = string.IsNullOrWhiteSpace(radio.Name) ? null : radio.Name;
        return radio.State switch
        {
            RadioState.On => new RadioSnapshot(Kind, RadioAvailability.Available, true, name),
            RadioState.Off => new RadioSnapshot(Kind, RadioAvailability.Available, false, name),
            RadioState.Disabled => new RadioSnapshot(Kind, RadioAvailability.HardwareDisabled, false, name,
                "the radio is disabled by a hardware switch or device firmware (RadioState.Disabled); " +
                "software cannot enable it"),
            _ => new RadioSnapshot(Kind, RadioAvailability.Unavailable, false, name,
                "the radio driver reports RadioState.Unknown; the state is not controllable right now"),
        };
    }

    private RadioSnapshot? CurrentOrDefault()
    {
        var radio = _radio;
        return radio is null ? null : CreateSnapshot(radio);
    }

    private void OnRadioStateChanged(Radio sender, object args)
    {
        try
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _counters.IncrementMonitorCallbackFailures();
            _logger.LogWarning(PlatformLogCategory.Platform, "radio.state-callback-failed",
                "A radio state-change subscriber failed.", ex);
        }
    }

    private void ResetRadioCore()
    {
        var radio = Interlocked.Exchange(ref _radio, null);
        if (radio is null)
        {
            return;
        }

        try
        {
            radio.StateChanged -= OnRadioStateChanged;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(PlatformLogCategory.Platform, "radio.unsubscribe-failed",
                $"Unsubscribing from the {Kind} radio failed: {ex.Message}.");
        }
    }

    private static RadioKind ToRadioKind(PlatformRadioKind kind) => kind switch
    {
        PlatformRadioKind.Wifi => RadioKind.WiFi,
        PlatformRadioKind.Bluetooth => RadioKind.Bluetooth,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown platform radio kind."),
    };

    private void ReleaseGate()
    {
        try
        {
            _gate.Release();
        }
        catch (ObjectDisposedException)
        {
            // Shutdown raced with an in-flight read; nothing left to release.
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }

        ResetRadioCore();
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Event-driven quick-toggle service for one radio kind. Wraps an <see cref="IRadioBackend"/> so the
/// whole service (lifecycle, change-only snapshot, explicit results) is testable with a fake and no
/// radio hardware. Starting never blocks: the first read is async and off the UI thread.
/// </summary>
public sealed class RadioToggleService : PlatformMonitor<RadioSnapshot>, IRadioToggleService
{
    private readonly IRadioBackend _backend;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private int _stopped;

    /// <summary>Creates the service over a backend (production: <see cref="WindowsRadioBackend"/>).</summary>
    public RadioToggleService(IRadioBackend backend, IPlatformLogger logger, PlatformCounters counters,
        TimeProvider? timeProvider = null)
        : base($"radio-{backend.Kind.ToString().ToLowerInvariant()}", logger, counters,
            timeProvider ?? TimeProvider.System)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public PlatformRadioKind Kind => _backend.Kind;

    /// <inheritdoc />
    public bool? IsEnabled => Current?.IsEnabled;

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
            Logger.LogError(PlatformLogCategory.Platform, "radio.refresh-failed",
                $"Refreshing the {Kind} radio state failed.", ex);
            Publish(RadioSnapshot.Failed(Kind, $"refreshing the {Kind} radio state failed: {ex.Message}"));
        }
        finally
        {
            ReleaseRefreshGate();
        }
    }

    /// <inheritdoc />
    public async Task<RadioToggleResult> SetEnabledAsync(bool enabled,
        CancellationToken cancellationToken = default)
    {
        RadioToggleResult result;
        try
        {
            result = await _backend.SetEnabledAsync(enabled, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Counters.IncrementComFailures();
            Logger.LogError(PlatformLogCategory.Platform, "radio.toggle-failed",
                $"Setting the {Kind} radio to {(enabled ? "on" : "off")} failed.", ex);
            return RadioToggleResult.Failed(
                $"setting the {Kind} radio to {(enabled ? "on" : "off")} failed: {ex.Message}");
        }

        if (result.Snapshot is not null)
        {
            Publish(result.Snapshot);
        }

        if (!result.Succeeded)
        {
            Logger.LogWarning(PlatformLogCategory.Platform, "radio.toggle-rejected",
                $"The {Kind} radio request was rejected ({result.Outcome}): {result.Reason}");
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
            Logger.LogError(PlatformLogCategory.Platform, "radio.state-refresh-failed",
                $"Refreshing the {Kind} radio after a state change failed.", ex);
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
