using System.Diagnostics;
using System.Text;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Threading;

namespace FluidBar.Platform.Windows.Update;

/// <summary>Auto-update policy token (<c>off</c>/<c>notify</c>/<c>auto</c>; spec §8.2 <c>general.autoUpdate</c>).</summary>
public enum AutoUpdateMode
{
    /// <summary>Never check automatically; a manual check from the settings page still works.</summary>
    Off,

    /// <summary>Check in the background and notify; never download without a user action.</summary>
    Notify,

    /// <summary>Check and download verified installers in the background; installing still needs the user.</summary>
    Automatic,
}

/// <summary>Composition inputs for <see cref="UpdateService"/>.</summary>
public sealed class UpdateServiceOptions
{
    /// <summary>Release feed URL. <c>https</c> is the GitHub Releases API shape; <c>file</c> reads a local index.</summary>
    public required Uri FeedUrl { get; init; }

    /// <summary>Directory that receives downloaded installers (created on demand).</summary>
    public required string DownloadDirectory { get; init; }

    /// <summary>Currently running application version.</summary>
    public required Version CurrentVersion { get; init; }

    /// <summary>Asset name pattern identifying the Windows installer.</summary>
    public string InstallerAssetPattern { get; init; } = @"FluidBar.*\.exe$";

    /// <summary>Delay before the first automatic check (spec §8.4: startup + 5 s).</summary>
    public TimeSpan StartupDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Interval between automatic checks (spec §8.4: every 24 h).</summary>
    public TimeSpan CheckInterval { get; init; } = TimeSpan.FromHours(24);

    /// <summary>Initial channel (<c>stable</c>/<c>beta</c>).</summary>
    public UpdateChannel Channel { get; init; } = UpdateChannel.Stable;

    /// <summary>Initial auto-update token (<c>off</c>/<c>notify</c>/<c>auto</c>).</summary>
    public string AutoUpdate { get; init; } = "notify";

    /// <summary>
    /// Development-only escape hatch: accept SHA-256 without Authenticode. Production stays on the
    /// composite verifier; this exists so the chain can be exercised with locally built (unsigned)
    /// artifacts and is flagged loudly in the log.
    /// </summary>
    public bool AllowUnsignedInstaller { get; init; }

    /// <summary>User-Agent sent with feed/download requests.</summary>
    public string UserAgent { get; init; } = "FluidBar-UpdateCheck";
}

/// <summary>Result of the explicit "restart to install" action.</summary>
/// <param name="Launched">Whether the verified installer process was started.</param>
/// <param name="InstallerPath">Installer that was (or would have been) started.</param>
/// <param name="FailureReason">Why the action was refused, when it was.</param>
public sealed record UpdateInstallResult(bool Launched, string? InstallerPath, string? FailureReason = null);

/// <summary>Launches the verified installer. Abstracted so tests never execute a downloaded binary.</summary>
public interface IUpdateInstallerLauncher
{
    /// <summary>Starts the installer; returns false when the process could not be created.</summary>
    /// <param name="installerPath">Absolute path of an already verified installer.</param>
    bool Launch(string installerPath);
}

/// <summary>ShellExecute-based installer launcher (explicit user action only).</summary>
public sealed class ProcessUpdateInstallerLauncher : IUpdateInstallerLauncher
{
    /// <inheritdoc />
    public bool Launch(string installerPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installerPath);
        if (TryLaunchSelfReplace(installerPath))
        {
            return true;
        }

        var info = new ProcessStartInfo(installerPath) { UseShellExecute = true };
        return Process.Start(info) is not null;
    }

    /// <summary>
    /// The preview ships a portable <c>FluidBar.exe</c>, not a setup package. Launching it directly
    /// while the old instance is still alive loses the race with the single-instance mutex, so a tiny
    /// PowerShell helper waits for this PID, copies the verified download over the running exe and
    /// starts it again. A future real installer still goes through ShellExecute.
    /// </summary>
    private static bool TryLaunchSelfReplace(string installerPath)
    {
        var current = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(current)
            || !string.Equals(Path.GetFileName(current), "FluidBar.exe", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetFileName(installerPath), "FluidBar.exe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetFullPath(current), Path.GetFullPath(installerPath), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var scriptPath = Path.Combine(Path.GetTempPath(), $"fluidbar-update-{Guid.NewGuid():N}.ps1");
            var script = BuildSelfReplaceScript(installerPath, current, Environment.ProcessId);
            // Windows PowerShell 5.1 reads a BOM-less .ps1 as ANSI; a Chinese/Unicode install path
            // would then be mojibake and Copy-Item would target the wrong file.
            File.WriteAllText(scriptPath, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            var info = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
            };
            return Process.Start(info) is not null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Builds the self-replace helper script; pure so escaping is unit-testable.</summary>
    internal static string BuildSelfReplaceScript(string installerPath, string currentPath, int pid)
        => string.Join(Environment.NewLine,
            "$ErrorActionPreference = 'Stop'",
            $"Wait-Process -Id {pid} -ErrorAction SilentlyContinue",
            "Start-Sleep -Milliseconds 800",
            $"Copy-Item -LiteralPath '{EscapePowerShellLiteral(installerPath)}' -Destination '{EscapePowerShellLiteral(currentPath)}' -Force",
            $"Start-Process -FilePath '{EscapePowerShellLiteral(currentPath)}'",
            "Remove-Item -LiteralPath $PSCommandPath -Force");

    private static string EscapePowerShellLiteral(string value) => value.Replace("'", "''");
}

/// <summary>
/// The single update service (spec §10.1 #22): owns the check client, applies the user's update
/// policy, schedules the startup + interval checks, downloads verifies installers and gates the
/// explicit install action behind a verified file. It never executes an installer on its own.
/// </summary>
/// <remarks>
/// States are published as an immutable <see cref="UpdateStatusSnapshot"/>; the settings About page and
/// the tray prompt consume <see cref="Changed"/>. Checks/downloads are single-flight inside the client;
/// the service only adds policy, scheduling and the install gate.
/// </remarks>
public sealed class UpdateService : IAsyncDisposable
{
    private readonly UpdateServiceOptions _options;
    private readonly IPlatformLogger _logger;
    private readonly PlatformCounters _counters;
    private readonly TimeProvider _timeProvider;
    private readonly UpdateCheckClient _client;
    private readonly HttpClient _httpClient;
    private readonly IUpdateInstallerLauncher _launcher;
    private readonly SnapshotPublisher<UpdateStatusSnapshot> _publisher;
    private readonly SemaphoreSlim _autoDownloadGate = new(1, 1);
    private readonly object _timerGate = new();

    private ITimer? _timer;
    private AutoUpdateMode _mode;
    private UpdateChannel _channel;
    private int _started;
    private int _disposed;
    private volatile bool _restartRequired;

    // Synchronous knowledge of the last useful result: the snapshot publisher dispatches on the
    // thread pool, so callers that just awaited a check/download must not observe a stale snapshot
    // (that race used to make an immediate download report "no release available").
    private UpdateRelease? _lastRelease;
    private string? _verifiedInstallerPath;
    private UpdateRelease? _verifiedRelease;
    private UpdateStatusSnapshot? _restartSnapshot;

    /// <summary>Creates the service; call <see cref="StartAsync"/> to schedule automatic checks.</summary>
    public UpdateService(UpdateServiceOptions options, IPlatformLogger logger,
        PlatformCounters? counters = null, TimeProvider? timeProvider = null,
        IUpdatePackageVerifier? verifier = null, IUpdateInstallerLauncher? launcher = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? NullPlatformLogger.Instance;
        _counters = counters ?? new PlatformCounters();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _launcher = launcher ?? new ProcessUpdateInstallerLauncher();
        _publisher = new SnapshotPublisher<UpdateStatusSnapshot>("update-service", _logger, _counters);

        if (options.AllowUnsignedInstaller)
        {
            _logger.LogWarning(PlatformLogCategory.Update, "update.unsigned-dev-mode",
                "Update service is running with the development SHA-256-only verifier; Authenticode is NOT enforced. Never ship this configuration.");
        }

        _httpClient = UpdateCheckClient.CreateHttpClient(new UpdateClientOptions
        {
            ReleaseFeedUrl = options.FeedUrl,
            UserAgent = options.UserAgent,
            DownloadDirectory = options.DownloadDirectory,
            InstallerAssetPattern = options.InstallerAssetPattern,
        });

        _client = new UpdateCheckClient(_httpClient, new UpdateClientOptions
        {
            ReleaseFeedUrl = options.FeedUrl,
            UserAgent = options.UserAgent,
            DownloadDirectory = options.DownloadDirectory,
            InstallerAssetPattern = options.InstallerAssetPattern,
        }, options.CurrentVersion,
            verifier ?? CreateDefaultVerifier(options.AllowUnsignedInstaller, logger),
            logger, _counters, _timeProvider);

        _mode = ParseMode(options.AutoUpdate);
        _channel = options.Channel;
        _client.Changed += OnClientChanged;
    }

    /// <summary>Raised (thread pool) whenever the update state changes.</summary>
    public event EventHandler<SnapshotEventArgs<UpdateStatusSnapshot>>? Changed
    {
        add => _publisher.Changed += value;
        remove => _publisher.Changed -= value;
    }

    /// <summary>Latest update state (synchronous; the coalescing publisher only drives events).</summary>
    public UpdateStatusSnapshot Current => Volatile.Read(ref _restartSnapshot) ?? _client.Current;

    /// <summary>Active auto-update policy.</summary>
    public AutoUpdateMode Mode => _mode;

    /// <summary>Active release channel.</summary>
    public UpdateChannel Channel => _channel;

    /// <summary>Currently installed version.</summary>
    public Version CurrentVersion => _options.CurrentVersion;

    /// <summary>Whether the last verified installer can be launched.</summary>
    public bool CanInstall
    {
        get
        {
            var snapshot = Current;
            if (snapshot.State == UpdateState.RestartRequired)
            {
                return false;
            }

            if (_verifiedInstallerPath is { Length: > 0 } verified && File.Exists(verified))
            {
                return true;
            }

            // The client publishes ready-to-install before the download method returns; accept its
            // verified path so an observer that just saw the state is not told "not installable".
            return snapshot.State == UpdateState.ReadyToInstall
                   && snapshot.Verification == UpdateVerificationStatus.Verified
                   && snapshot.DownloadedFilePath is { Length: > 0 } path
                   && File.Exists(path);
        }
    }

    /// <summary>Applies the user's settings (auto-update token + channel) and re-arms the schedule.</summary>
    /// <param name="autoUpdate">Auto-update token (<c>off</c>/<c>notify</c>/<c>auto</c>).</param>
    /// <param name="channel">Channel token (<c>stable</c>/<c>beta</c>).</param>
    public void ApplyPolicy(string? autoUpdate, string? channel)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var mode = ParseMode(autoUpdate);
        var nextChannel = string.Equals(channel, "beta", StringComparison.OrdinalIgnoreCase)
            ? UpdateChannel.Prerelease
            : UpdateChannel.Stable;
        if (mode == _mode && nextChannel == _channel)
        {
            return;
        }

        _mode = mode;
        _channel = nextChannel;
        _logger.LogInfo(PlatformLogCategory.Settings, "update.policy",
            "Update policy changed.",
            new Dictionary<string, object?>
            {
                ["autoUpdate"] = mode.ToString(),
                ["channel"] = nextChannel.ToString(),
            });
        RearmSchedule("policy-change");
    }

    /// <summary>Schedules the startup + interval checks (idempotent).</summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return Task.CompletedTask;
        }

        cancellationToken.ThrowIfCancellationRequested();
        RearmSchedule("startup");
        return Task.CompletedTask;
    }

    /// <summary>Stops the automatic schedule (manual checks remain available).</summary>
    public void Stop()
    {
        Interlocked.Exchange(ref _started, 0);
        lock (_timerGate)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    /// <summary>Runs one explicit check now (works even when automatic checks are off).</summary>
    public Task<UpdateCheckResult> CheckNowAsync(CancellationToken cancellationToken = default)
        => CheckCoreAsync("manual", cancellationToken);

    /// <summary>One check with a trigger label in the log (manual vs scheduled).</summary>
    private async Task<UpdateCheckResult> CheckCoreAsync(string trigger, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        _logger.LogInfo(PlatformLogCategory.Settings, "update.check-started",
            "Update check started.",
            new Dictionary<string, object?>
            {
                ["trigger"] = trigger,
                ["channel"] = _channel.ToString(),
                ["currentVersion"] = _options.CurrentVersion.ToString(),
            });

        var result = await _client.CheckAsync(
            new UpdateCheckRequest(_options.CurrentVersion, _channel), cancellationToken).ConfigureAwait(false);
        if (result.Latest is { } latest)
        {
            _lastRelease = latest;
        }

        var data = new Dictionary<string, object?>
        {
            ["trigger"] = trigger,
            ["status"] = result.Status.ToString(),
            ["latest"] = result.Latest?.Version.ToString(),
            ["reason"] = result.FailureReason,
        };
        if (result.Status == UpdateCheckStatus.Failed)
        {
            _logger.LogWarning(PlatformLogCategory.Settings, "update.check-finished",
                "Update check failed.", exception: null, data: data);
        }
        else
        {
            _logger.LogInfo(PlatformLogCategory.Settings, "update.check-finished",
                "Update check finished.", data: data);
        }

        if (result.Status == UpdateCheckStatus.UpdateAvailable && result.Latest is { } release
            && _mode == AutoUpdateMode.Automatic)
        {
            // Policy "auto": fetch + verify in the background. Installing still waits for the user.
            _ = DownloadInBackgroundAsync(release);
        }

        return result;
    }

    /// <summary>Downloads and verifies the installer for a release (explicit user action or auto policy).</summary>
    public async Task<UpdateDownloadResult> DownloadAsync(UpdateRelease release, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(release);
        var result = await _client.DownloadInstallerAsync(release, progress, cancellationToken).ConfigureAwait(false);
        if (result.CanInstall)
        {
            _verifiedInstallerPath = result.FilePath;
            _verifiedRelease = release;
        }

        return result;
    }

    /// <summary>Downloads the release carried by the current snapshot.</summary>
    public Task<UpdateDownloadResult> DownloadCurrentAsync(IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
        => (Current.AvailableRelease ?? _lastRelease ?? _verifiedRelease) is { } release
            ? DownloadAsync(release, progress, cancellationToken)
            : Task.FromResult(new UpdateDownloadResult(false, null, UpdateVerificationStatus.NotVerified, null,
                "No release is available to download."));

    /// <summary>
    /// Explicit install action: launches the verified installer and publishes
    /// <see cref="UpdateState.RestartRequired"/>. Never runs an unverified file.
    /// </summary>
    public UpdateInstallResult Install()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var snapshot = Current;
        _verifiedInstallerPath ??= snapshot.DownloadedFilePath;
        if (snapshot.State == UpdateState.RestartRequired)
        {
            return new UpdateInstallResult(true, snapshot.DownloadedFilePath);
        }

        if (!CanInstall)
        {
            return new UpdateInstallResult(false, snapshot.DownloadedFilePath,
                "No verified installer is ready to install.");
        }

        var path = _verifiedInstallerPath!;
        bool launched;
        try
        {
            launched = _launcher.Launch(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(PlatformLogCategory.Update, "update.install-failed",
                "The verified installer could not be started.", ex);
            return new UpdateInstallResult(false, path, "The installer could not be started.");
        }

        if (!launched)
        {
            return new UpdateInstallResult(false, path, "The installer process was not created.");
        }

        _restartRequired = true;
        var restart = new UpdateStatusSnapshot(UpdateState.RestartRequired, _options.CurrentVersion,
            snapshot.AvailableRelease, path, snapshot.Verification, snapshot.LastCheckedAtUtc,
            "Restart FluidBar to finish the update.");
        Volatile.Write(ref _restartSnapshot, restart);
        Publish(restart);
        _logger.LogInfo(PlatformLogCategory.Update, "update.restart-required",
            "Verified installer launched; restart required to finish the update.",
            new Dictionary<string, object?> { ["path"] = path });
        return new UpdateInstallResult(true, path);
    }

    /// <summary>Discards the cached download (user dismissed the update).</summary>
    public void DiscardDownload()
    {
        _client.ClearDownloadedInstaller();
        _verifiedInstallerPath = null;
        _verifiedRelease = null;
        Volatile.Write(ref _restartSnapshot, null);
        _restartRequired = false;
        Publish(new UpdateStatusSnapshot(UpdateState.Idle, _options.CurrentVersion, null, null,
            UpdateVerificationStatus.NotVerified, Current.LastCheckedAtUtc, null));
    }

    private async Task DownloadInBackgroundAsync(UpdateRelease release)
    {
        if (!await _autoDownloadGate.WaitAsync(0).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            await _client.DownloadInstallerAsync(release).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(PlatformLogCategory.Update, "update.auto-download-failed",
                "The automatic background download failed.", ex);
        }
        finally
        {
            _autoDownloadGate.Release();
        }
    }

    private void RearmSchedule(string reason)
    {
        var started = Volatile.Read(ref _started) != 0;
        var disabled = false;
        lock (_timerGate)
        {
            _timer?.Dispose();
            _timer = null;
            if (!started || _mode == AutoUpdateMode.Off)
            {
                disabled = started;
            }
            else
            {
                // First check after the startup delay, then every CheckInterval (spec §8.4).
                _timer = _timeProvider.CreateTimer(
                    static state => _ = ((UpdateService)state!).RunScheduledCheckAsync(),
                    this, _options.StartupDelay, _options.CheckInterval);
            }
        }

        if (!started)
        {
            return;
        }

        if (disabled)
        {
            _logger.LogInfo(PlatformLogCategory.Settings, "update.auto-check-disabled",
                "Automatic update checks are disabled by the user policy.",
                new Dictionary<string, object?> { ["reason"] = reason, ["policy"] = _mode.ToString() });
            return;
        }

        _logger.LogInfo(PlatformLogCategory.Settings, "update.auto-check-armed",
            "Automatic update checks armed.",
            new Dictionary<string, object?>
            {
                ["reason"] = reason,
                ["policy"] = _mode.ToString(),
                ["dueSeconds"] = _options.StartupDelay.TotalSeconds,
                ["intervalHours"] = _options.CheckInterval.TotalHours,
            });
    }

    private async Task RunScheduledCheckAsync()
    {
        if (Volatile.Read(ref _disposed) != 0 || _mode == AutoUpdateMode.Off || _restartRequired)
        {
            return;
        }

        try
        {
            await CheckCoreAsync("scheduled", CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(PlatformLogCategory.Settings, "update.scheduled-check-failed",
                "The scheduled update check failed.", ex);
        }
    }

    private void OnClientChanged(object? sender, SnapshotEventArgs<UpdateStatusSnapshot> args)
    {
        if (_restartRequired)
        {
            // The install action owns the visible state after the installer was launched.
            return;
        }

        Publish(args.Snapshot);
    }

    private void Publish(UpdateStatusSnapshot snapshot) => _publisher.Publish(snapshot);

    private static AutoUpdateMode ParseMode(string? token) => token?.Trim().ToLowerInvariant() switch
    {
        "auto" => AutoUpdateMode.Automatic,
        "off" => AutoUpdateMode.Off,
        _ => AutoUpdateMode.Notify,
    };

    private static IUpdatePackageVerifier CreateDefaultVerifier(bool allowUnsigned, IPlatformLogger logger)
        => allowUnsigned
            ? new Sha256UpdateVerifier()
            : new CompositeUpdateVerifier(new WinVerifyTrustSignatureVerifier(logger), logger);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Stop();
        _client.Changed -= OnClientChanged;
        _publisher.Dispose();
        await _client.DisposeAsync().ConfigureAwait(false);
        _autoDownloadGate.Dispose();
        _httpClient.Dispose();
    }
}
