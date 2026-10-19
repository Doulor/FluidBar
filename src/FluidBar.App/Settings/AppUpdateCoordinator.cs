using FluidBar.Core.Diagnostics;
using FluidBar.Platform.Windows.CoreIntegration;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Update;

namespace FluidBar.App.Settings;

/// <summary>
/// Read/write seam the settings About page uses for updates. Kept small so tests can substitute a fake
/// and the section builder never touches networking.
/// </summary>
public interface IUpdateUiService
{
    /// <summary>Latest state.</summary>
    UpdateStatusSnapshot Current { get; }

    /// <summary>Currently installed version.</summary>
    Version CurrentVersion { get; }

    /// <summary>Whether a release feed is configured (env override or the default public feed).</summary>
    bool IsConfigured { get; }

    /// <summary>Human-readable feed source for diagnostics (path/URL).</summary>
    string FeedDescription { get; }

    /// <summary>Whether a check or download is in flight.</summary>
    bool IsBusy { get; }

    /// <summary>Raised when the state changes (thread pool; UI marshals).</summary>
    event EventHandler? Changed;

    /// <summary>Runs an explicit update check.</summary>
    Task CheckNowAsync(CancellationToken cancellationToken = default);

    /// <summary>Downloads and verifies the installer for the current release.</summary>
    Task DownloadCurrentAsync(CancellationToken cancellationToken = default);

    /// <summary>Launches the verified installer (explicit user action).</summary>
    UpdateInstallResult Install();

    /// <summary>Discards the downloaded installer.</summary>
    void Discard();
}

/// <summary>
/// App composition of the update chain (spec §8.4/§10.1 #22): one <see cref="UpdateService"/> fed by the
/// user's auto-update policy, exposed to the settings About page and to the tray prompt.
/// <para>
/// Feed resolution: <c>FLUIDBAR_UPDATE_FEED</c> (absolute URI; <c>file:///…/index.json</c> is a valid
/// local test feed) overrides the default public GitHub Releases feed. The dev-only
/// <c>FLUIDBAR_UPDATE_ALLOW_UNSIGNED=1</c> switch is passed through so locally built artifacts can
/// exercise the chain; it is logged loudly and never enabled implicitly.
/// </para>
/// </summary>
public sealed class AppUpdateCoordinator : IUpdateUiService, IAsyncDisposable
{
    /// <summary>Environment variable overriding the release feed (URL or local file index).</summary>
    public const string FeedEnvironmentVariable = "FLUIDBAR_UPDATE_FEED";

    /// <summary>Development-only switch accepting SHA-256 without Authenticode.</summary>
    public const string UnsignedEnvironmentVariable = "FLUIDBAR_UPDATE_ALLOW_UNSIGNED";

    /// <summary>Environment variable overriding the installer asset pattern.</summary>
    public const string AssetPatternEnvironmentVariable = "FLUIDBAR_UPDATE_ASSET_PATTERN";

    /// <summary>Default production feed (GitHub Releases API, stable/beta channels).</summary>
    public static Uri DefaultFeedUrl { get; } = new("https://api.github.com/repos/Doulor/FluidBar/releases");

    private readonly SettingsController _settings;
    private readonly IAppLogger _logger;
    private readonly UpdateService _service;
    private readonly Uri _feedUrl;
    private readonly bool _feedFromEnvironment;
    private int _disposed;

    /// <summary>Creates the coordinator and the underlying update service.</summary>
    /// <param name="settings">Live settings (auto-update policy + channel).</param>
    /// <param name="logger">Structured logger.</param>
    /// <param name="timeProvider">Clock (tests).</param>
    /// <param name="verifier">Installer verifier override (tests); defaults to the production chain.</param>
    /// <param name="launcher">Installer launcher override (tests); never executes a real file in tests.</param>
    public AppUpdateCoordinator(SettingsController settings, IAppLogger logger,
        TimeProvider? timeProvider = null, IUpdatePackageVerifier? verifier = null,
        IUpdateInstallerLauncher? launcher = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? NullAppLogger.Instance;
        (_feedUrl, _feedFromEnvironment) = ResolveFeedUrl();
        var allowUnsigned = string.Equals(
            Environment.GetEnvironmentVariable(UnsignedEnvironmentVariable), "1", StringComparison.Ordinal);
        var assetPattern = Environment.GetEnvironmentVariable(AssetPatternEnvironmentVariable);

        CurrentVersion = typeof(AppUpdateCoordinator).Assembly.GetName().Version ?? new Version(1, 0, 0, 0);
        _service = new UpdateService(
            new UpdateServiceOptions
            {
                FeedUrl = _feedUrl,
                DownloadDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FluidBar", "updates"),
                CurrentVersion = CurrentVersion,
                InstallerAssetPattern = string.IsNullOrWhiteSpace(assetPattern)
                    ? @"FluidBar.*\.exe$"
                    : assetPattern.Trim(),
                AutoUpdate = settings.Current.AutoUpdate,
                Channel = ResolveChannel(settings.Current.Plugins.Channel),
                AllowUnsignedInstaller = allowUnsigned,
            },
            new AppLoggerPlatformAdapter(_logger),
            new PlatformCounters(),
            timeProvider,
            verifier,
            launcher);

        _service.Changed += OnServiceChanged;
        _settings.Changed += OnSettingsChanged;
        _logger.Information(LogCategories.Settings, "update.coordinator.created",
            "Update coordinator composed.",
            data: new Dictionary<string, object?>
            {
                ["feed"] = _feedUrl.ToString(),
                ["fromEnvironment"] = _feedFromEnvironment,
                ["autoUpdate"] = settings.Current.AutoUpdate,
                ["channel"] = settings.Current.Plugins.Channel,
                ["currentVersion"] = CurrentVersion.ToString(),
                ["allowUnsigned"] = allowUnsigned,
            });
    }

    /// <inheritdoc />
    public UpdateStatusSnapshot Current => _service.Current;

    /// <inheritdoc />
    public Version CurrentVersion { get; }

    /// <inheritdoc />
    public bool IsConfigured => _feedFromEnvironment || _feedUrl == DefaultFeedUrl;

    /// <inheritdoc />
    public string FeedDescription => _feedUrl.IsFile
        ? Uri.UnescapeDataString(_feedUrl.LocalPath)
        : _feedUrl.ToString();

    /// <inheritdoc />
    public bool IsBusy => Current.State is UpdateState.Checking or UpdateState.Downloading;

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <summary>Schedules the automatic checks (startup + interval).</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _service.ApplyPolicy(_settings.Current.AutoUpdate, _settings.Current.Plugins.Channel);
        return _service.StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task CheckNowAsync(CancellationToken cancellationToken = default)
    {
        var result = await _service.CheckNowAsync(cancellationToken).ConfigureAwait(false);
        if (result.Status == UpdateCheckStatus.Failed)
        {
            _logger.Warning(LogCategories.Settings, "update.check-failed",
                "The explicit update check failed.",
                data: new Dictionary<string, object?>
                {
                    ["feed"] = _feedUrl.ToString(),
                    ["reason"] = result.FailureReason,
                });
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public async Task DownloadCurrentAsync(CancellationToken cancellationToken = default)
    {
        var result = await _service.DownloadCurrentAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            _logger.Warning(LogCategories.Settings, "update.download-failed",
                "The update download failed.",
                data: new Dictionary<string, object?>
                {
                    ["reason"] = result.FailureReason,
                    ["verification"] = result.Verification.ToString(),
                });
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public UpdateInstallResult Install() => _service.Install();

    /// <inheritdoc />
    public void Discard() => _service.DiscardDownload();

    private void OnServiceChanged(object? sender, Platform.Windows.Threading.SnapshotEventArgs<UpdateStatusSnapshot> args)
        => Changed?.Invoke(this, EventArgs.Empty);

    private void OnSettingsChanged(AppSettings previous, AppSettings current)
    {
        if (string.Equals(previous.AutoUpdate, current.AutoUpdate, StringComparison.Ordinal)
            && string.Equals(previous.Plugins.Channel, current.Plugins.Channel, StringComparison.Ordinal))
        {
            return;
        }

        _service.ApplyPolicy(current.AutoUpdate, current.Plugins.Channel);
    }

    private static UpdateChannel ResolveChannel(string? channel)
        => string.Equals(channel, "beta", StringComparison.OrdinalIgnoreCase)
            ? UpdateChannel.Prerelease
            : UpdateChannel.Stable;

    private static (Uri Feed, bool FromEnvironment) ResolveFeedUrl()
    {
        var configured = Environment.GetEnvironmentVariable(FeedEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (Uri.TryCreate(configured.Trim(), UriKind.Absolute, out var uri))
            {
                return (uri, true);
            }

            return (DefaultFeedUrl, false);
        }

        return (DefaultFeedUrl, false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _settings.Changed -= OnSettingsChanged;
        _service.Changed -= OnServiceChanged;
        await _service.DisposeAsync().ConfigureAwait(false);
    }
}
