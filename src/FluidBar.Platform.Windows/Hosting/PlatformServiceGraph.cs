using System.Net.Http;
using FluidBar.Platform.Windows.Agent;
using FluidBar.Platform.Windows.Clipboard;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Devices;
using FluidBar.Platform.Windows.Display;
using FluidBar.Platform.Windows.Media;
using FluidBar.Platform.Windows.Media.Lyrics;
using FluidBar.Platform.Windows.Monitors;
using FluidBar.Platform.Windows.Notifications;
using FluidBar.Platform.Windows.Shell;
using FluidBar.Platform.Windows.Threading;
using FluidBar.Platform.Windows.Update;

namespace FluidBar.Platform.Windows.Hosting;

/// <summary>One tray-icon setup item for the composition root.</summary>
public sealed record PlatformTraySetup(TrayIconOptions Options, IReadOnlyList<TrayMenuItem> MenuItems);

/// <summary>One hotkey registration requested at start.</summary>
public sealed record PlatformHotkeyRegistration(string Name, string Gesture);

/// <summary>
/// Configuration for <see cref="PlatformServiceGraph"/>. Everything is optional; defaults produce the
/// full platform service set with file logging under <c>%APPDATA%\FluidBar\logs</c>.
/// </summary>
public sealed class PlatformServiceGraphOptions
{
    /// <summary>Directory for the rolling JSONL log sink; null disables file logging (a null logger is used).</summary>
    public string? LogDirectory { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FluidBar", "logs");

    /// <summary>App data root used for agent inbox/artwork temp (defaults to <c>%APPDATA%\FluidBar</c>).</summary>
    public string? DataDirectory { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FluidBar");

    /// <summary>Artwork temp directory (defaults to <c>%TEMP%\FluidBar\art</c>).</summary>
    public string? ArtworkTempDirectory { get; init; }

    /// <summary>Enable the display/monitor service.</summary>
    public bool EnableDisplay { get; init; } = true;

    /// <summary>Enable the system monitors (volume, brightness, battery, clock, IME, lock keys, network, theme).</summary>
    public bool EnableMonitors { get; init; } = true;

    /// <summary>Enable GSMTC media + artwork cache.</summary>
    public bool EnableMedia { get; init; } = true;

    /// <summary>Enable the bounded lyrics stack (requires network for the online providers).</summary>
    public bool EnableLyrics { get; init; } = true;

    /// <summary>Enable the clipboard listener.</summary>
    public bool EnableClipboard { get; init; } = true;

    /// <summary>Enable the Windows notification listener.</summary>
    public bool EnableNotifications { get; init; } = true;

    /// <summary>Enable the agent hook inbox watcher.</summary>
    public bool EnableAgentInbox { get; init; } = true;

    /// <summary>Enable the explicit agent-hook installer (requires <see cref="AgentSettingsFilePath"/>).</summary>
    public bool EnableAgentHooks { get; init; } = true;

    /// <summary>Agent settings JSON path (e.g. <c>%USERPROFILE%\.claude\settings.json</c>).</summary>
    public string? AgentSettingsFilePath { get; init; }

    /// <summary>Hook command registered by the installer when the user explicitly installs hooks.</summary>
    public string? AgentHookCommand { get; init; }

    /// <summary>Enable USB device notifications.</summary>
    public bool EnableUsb { get; init; } = true;

    /// <summary>Enable Bluetooth device notifications.</summary>
    public bool EnableBluetooth { get; init; } = true;

    /// <summary>Tray icon setup; null keeps the tray service constructed but not started.</summary>
    public PlatformTraySetup? Tray { get; init; }

    /// <summary>Hotkeys registered during <see cref="PlatformServiceGraph.StartAsync"/>; failures are surfaced, not thrown.</summary>
    public IReadOnlyList<PlatformHotkeyRegistration>? Hotkeys { get; init; }

    /// <summary>Enable the update-check client (requires <see cref="UpdateFeedUrl"/> + <see cref="CurrentVersion"/>).</summary>
    public bool EnableUpdateCheck { get; init; }

    /// <summary>Update feed URL (GitHub Releases API JSON).</summary>
    public Uri? UpdateFeedUrl { get; init; }

    /// <summary>Current application version used for update comparison.</summary>
    public Version? CurrentVersion { get; init; }

    /// <summary>Enable "start with Windows" registration wiring (ValueName defaults to FluidBar).</summary>
    public bool EnableStartupRegistration { get; init; }

    /// <summary>Executable path used when querying startup registration.</summary>
    public string? StartupExecutablePath { get; init; }

    /// <summary>Optional shared HttpClient for lyrics/update HTTP (not disposed by the graph when injected).</summary>
    public HttpClient? HttpClient { get; init; }

    /// <summary>Clock abstraction (tests virtualize time).</summary>
    public TimeProvider? TimeProvider { get; init; }
}

/// <summary>
/// One async composition root for the entire Windows platform layer. Builds the service graph once,
/// starts it in dependency order, stops it in reverse order and releases every resource — so the App can
/// consume a single object instead of hand-wiring services. Individual service constructors are unchanged
/// and remain usable stand-alone (tests/plugins keep using them).
/// </summary>
/// <remarks>
/// <para>Lifecycle: <see cref="StartAsync"/> / <see cref="StopAsync"/> / <see cref="DisposeAsync"/> are
/// idempotent and single-flight. A failing service is logged and does not prevent the remaining services
/// from starting; the graph stays usable in degraded form (the failure is also rethrown by
/// <see cref="StartAsync"/> so the host can surface it).</para>
/// <para>No UI references: every exposed member is an interface or an existing platform service.</para>
/// </remarks>
public sealed class PlatformServiceGraph : IAsyncDisposable
{
    private readonly PlatformServiceGraphOptions _options;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly List<(string Name, Func<CancellationToken, Task> Start, Func<Task> Stop)> _monitorActions = new();
    private readonly HashSet<string> _registeredHotkeys = new(StringComparer.Ordinal);
    private readonly HttpClient? _ownedHttpClient;
    private bool _started;
    private int _disposed;

    public PlatformServiceGraph(PlatformServiceGraphOptions? options = null)
    {
        _options = options ?? new PlatformServiceGraphOptions();
        var timeProvider = _options.TimeProvider ?? TimeProvider.System;
        TimeProvider = timeProvider;
        Counters = new PlatformCounters();

        if (_options.LogDirectory is not null)
        {
            LogSink = new RollingFileLogger(new RollingLogOptions { DirectoryPath = _options.LogDirectory },
                timeProvider, Counters);
        }

        Logger = LogSink is not null ? LogSink : NullPlatformLogger.Instance;
        Host = new PlatformMessageHost(Logger, Counters);

        if (_options.EnableDisplay)
        {
            Display = new MonitorService(Host, Logger, Counters, timeProvider);
        }

        if (_options.EnableMedia)
        {
            ArtworkCache = new ArtworkCache();
            Media = new GsmtcMediaSessionService(Logger, Counters, ArtworkCache, timeProvider);
        }

        var httpClient = _options.HttpClient;
        if (httpClient is null && (_options.EnableLyrics || _options.EnableUpdateCheck))
        {
            // Direct (system proxy bypassed) — see MediaHttpClientFactory: a local proxy client blackholes the
            // domestic lyric/artwork hosts and every lookup then times out.
            _ownedHttpClient = MediaHttpClientFactory.Create(Timeout.InfiniteTimeSpan, "FluidBar/2.0 (lyrics)");
            httpClient = _ownedHttpClient;
        }

        SharedHttpClient = httpClient;
        var dataDirectory = _options.DataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FluidBar");
        var artworkDirectory = _options.ArtworkTempDirectory
                               ?? Path.Combine(Path.GetTempPath(), "FluidBar", "art");
        ArtworkTempStore = new ArtworkTempStore(new ArtworkTempStoreOptions
        {
            DirectoryPath = artworkDirectory,
        }, Logger, timeProvider);

        if (_options.EnableLyrics && httpClient is not null)
        {
            LyricsCache = new LyricsCache(timeProvider: timeProvider, counters: Counters);
            Lyrics = new LyricsService(
                new ILyricsProvider[]
                {
                    new NeteaseLyricsProvider(httpClient, Logger),
                    new KugouLyricsProvider(httpClient, Logger),
                },
                LyricsCache, Logger, Counters);
        }

        if (_options.EnableMonitors)
        {
            Volume = new VolumeMonitor(Logger, Counters, timeProvider);
            Brightness = new BrightnessMonitor(Logger, Counters, timeProvider);
            Battery = new BatteryMonitor(Host, Logger, Counters, null, timeProvider);
            Clock = new ClockMonitor(Logger, Counters, timeProvider);
            InputMethod = new InputMethodMonitor(Host, Logger, Counters, timeProvider);
            LockKeys = new LockKeyMonitor(Host, Logger, Counters, timeProvider);
            Network = new NetworkMonitor(Logger, Counters, timeProvider);
            Theme = new SystemThemeMonitor(Host, Logger, Counters, timeProvider);
            RegisterMonitor("volume", Volume);
            RegisterMonitor("brightness", Brightness);
            RegisterMonitor("battery", Battery);
            RegisterMonitor("clock", Clock);
            RegisterMonitor("inputmethod", InputMethod);
            RegisterMonitor("lockkeys", LockKeys);
            RegisterMonitor("network", Network);
            RegisterMonitor("theme", Theme);
        }

        if (_options.EnableMedia && Media is not null)
        {
            _monitorActions.Add(("media",
                cancellationToken => Media.StartAsync(cancellationToken),
                () => Media.StopAsync()));
        }

        if (_options.EnableClipboard)
        {
            Clipboard = new ClipboardService(Host, Logger, Counters, null, timeProvider);
            _monitorActions.Add(("clipboard",
                cancellationToken => Clipboard.StartAsync(cancellationToken),
                () => Clipboard.StopAsync()));
        }

        if (_options.EnableNotifications)
        {
            Notifications = new NotificationService(Logger, Counters, null, timeProvider);
            _monitorActions.Add(("notifications",
                cancellationToken => Notifications.StartAsync(cancellationToken),
                () => Notifications.StopAsync()));
        }

        if (_options.EnableAgentInbox)
        {
            AgentInbox = new AgentInboxWatcher(new AgentInboxOptions
            {
                InboxDirectory = Path.Combine(dataDirectory, "agent-events"),
            }, Logger, Counters, timeProvider);
            _monitorActions.Add(("agent-inbox",
                cancellationToken => AgentInbox.StartAsync(cancellationToken),
                () => AgentInbox.StopAsync()));
        }

        if (_options.EnableAgentHooks
            && !string.IsNullOrWhiteSpace(_options.AgentSettingsFilePath)
            && !string.IsNullOrWhiteSpace(_options.AgentHookCommand))
        {
            AgentHookInstaller = new AgentHookInstaller(new HookInstallOptions
            {
                SettingsFilePath = _options.AgentSettingsFilePath,
                HookCommand = _options.AgentHookCommand,
                BackupDirectory = Path.Combine(dataDirectory, "backups", "agent-hooks"),
            }, Logger);
        }

        if (_options.EnableUsb)
        {
            UsbDevices = new UsbDeviceMonitor(Host, Logger, Counters, timeProvider);
            _monitorActions.Add(("usb",
                cancellationToken => UsbDevices.StartAsync(cancellationToken),
                () => UsbDevices.StopAsync()));
        }

        if (_options.EnableBluetooth)
        {
            BluetoothDevices = new BluetoothDeviceMonitor(Logger, Counters, timeProvider);
            _monitorActions.Add(("bluetooth",
                cancellationToken => BluetoothDevices.StartAsync(cancellationToken),
                () => BluetoothDevices.StopAsync()));
        }

        Hotkeys = new HotkeyService(Host, Logger, Counters);
        Tray = new TrayIconService(Host, Logger, Counters);
        StartupRegistration = new StartupRegistrationService(new NativeRegistryValueStore(), Logger);

        if (_options.EnableUpdateCheck
            && _options.UpdateFeedUrl is not null
            && _options.CurrentVersion is not null
            && httpClient is not null)
        {
            Update = new UpdateCheckClient(httpClient, new UpdateClientOptions
            {
                ReleaseFeedUrl = _options.UpdateFeedUrl,
                DownloadDirectory = Path.Combine(dataDirectory, "updates"),
            }, _options.CurrentVersion,
                new CompositeUpdateVerifier(new WinVerifyTrustSignatureVerifier(Logger), Logger),
                Logger, Counters, timeProvider);
        }
    }

    /// <summary>Clock abstraction used by every constructed service.</summary>
    public TimeProvider TimeProvider { get; }

    /// <summary>Structured log sink (null when file logging is disabled).</summary>
    public RollingFileLogger? LogSink { get; }

    /// <summary>Logger used by every service.</summary>
    public IPlatformLogger Logger { get; }

    /// <summary>Shared HttpClient used for lyrics/update HTTP (owned by the graph when it created it).</summary>
    public HttpClient? SharedHttpClient { get; }

    /// <summary>Shared lock-free diagnostics counters.</summary>
    public PlatformCounters Counters { get; }

    /// <summary>Dedicated platform message-pump host (windows/hotkeys/tray/notifications).</summary>
    public PlatformMessageHost Host { get; }

    /// <summary>Display/monitor service (null when disabled).</summary>
    public MonitorService? Display { get; }

    /// <summary>GSMTC media service (null when disabled).</summary>
    public GsmtcMediaSessionService? Media { get; }

    /// <summary>Bounded artwork byte cache (null when media is disabled).</summary>
    public ArtworkCache? ArtworkCache { get; }

    /// <summary>Bounded artwork temp-file materializer used for SDK <c>AlbumArtPath</c>.</summary>
    public ArtworkTempStore ArtworkTempStore { get; }

    /// <summary>Bounded lyrics cache (null when lyrics are disabled).</summary>
    public LyricsCache? LyricsCache { get; }

    /// <summary>Async single-flight lyrics service (null when lyrics are disabled).</summary>
    public LyricsService? Lyrics { get; }

    /// <summary>Volume monitor (null when monitors are disabled).</summary>
    public VolumeMonitor? Volume { get; }

    /// <summary>Brightness monitor (null when monitors are disabled).</summary>
    public BrightnessMonitor? Brightness { get; }

    /// <summary>Battery monitor (null when monitors are disabled).</summary>
    public BatteryMonitor? Battery { get; }

    /// <summary>Clock monitor (null when monitors are disabled).</summary>
    public ClockMonitor? Clock { get; }

    /// <summary>Input-method monitor (null when monitors are disabled).</summary>
    public InputMethodMonitor? InputMethod { get; }

    /// <summary>Lock-key monitor (null when monitors are disabled).</summary>
    public LockKeyMonitor? LockKeys { get; }

    /// <summary>Network monitor (null when monitors are disabled).</summary>
    public NetworkMonitor? Network { get; }

    /// <summary>System theme monitor (null when monitors are disabled).</summary>
    public SystemThemeMonitor? Theme { get; }

    /// <summary>Clipboard listener (null when disabled).</summary>
    public ClipboardService? Clipboard { get; }

    /// <summary>Notification listener (null when disabled).</summary>
    public NotificationService? Notifications { get; }

    /// <summary>Agent hook inbox watcher (null when disabled).</summary>
    public AgentInboxWatcher? AgentInbox { get; }

    /// <summary>Explicit consent-gated agent hook installer (null when unconfigured).</summary>
    public AgentHookInstaller? AgentHookInstaller { get; }

    /// <summary>USB device notifications (null when disabled).</summary>
    public UsbDeviceMonitor? UsbDevices { get; }

    /// <summary>Bluetooth device notifications (null when disabled).</summary>
    public BluetoothDeviceMonitor? BluetoothDevices { get; }

    /// <summary>Global hotkey service (started with the graph; registrations are optional).</summary>
    public HotkeyService Hotkeys { get; }

    /// <summary>Tray icon service (icon only added when <see cref="PlatformServiceGraphOptions.Tray"/> is set).</summary>
    public TrayIconService Tray { get; }

    /// <summary>HKCU Run registration helper (explicit user action only; never touches the registry on start).</summary>
    public StartupRegistrationService StartupRegistration { get; }

    /// <summary>Update-check client (null unless configured).</summary>
    public UpdateCheckClient? Update { get; }

    /// <summary>Starts the graph in dependency order. Idempotent; failure is logged and rethrown after cleanup.</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_started)
            {
                return;
            }

            Exception? failure = null;
            try
            {
                if (LogSink is not null)
                {
                    await LogSink.StartAsync(cancellationToken).ConfigureAwait(false);
                }

                await Host.StartAsync(cancellationToken).ConfigureAwait(false);
                await StartMonitorsAsync(cancellationToken).ConfigureAwait(false);
                await Hotkeys.StartAsync(cancellationToken).ConfigureAwait(false);
                await RegisterHotkeysAsync().ConfigureAwait(false);

                if (Tray is not null && _options.Tray is not null)
                {
                    try
                    {
                        await Tray.StartAsync(_options.Tray.Options, cancellationToken).ConfigureAwait(false);
                        Tray.SetMenuItems(_options.Tray.MenuItems);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(PlatformLogCategory.Shell, "graph.tray-failed",
                            "Tray icon could not be started; continuing without it.", ex);
                    }
                }

                _started = true;
                Logger.LogInfo(PlatformLogCategory.Platform, "graph.started", "Platform service graph started.",
                    new Dictionary<string, object?> { ["services"] = _monitorActions.Count + 2 });
            }
            catch (Exception ex)
            {
                failure = ex;
                Logger.LogCritical(PlatformLogCategory.Platform, "graph.start-failed",
                    "Platform service graph failed to start; cleaning up started services.", ex);
                await StopCoreAsync().ConfigureAwait(false);
            }

            if (failure is not null)
            {
                throw failure;
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <summary>Stops the graph in reverse order. Idempotent; individual failures are logged, never thrown.</summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_started)
            {
                return;
            }

            await StopCoreAsync().ConfigureAwait(false);
            _started = false;
            Logger.LogInfo(PlatformLogCategory.Platform, "graph.stopped", "Platform service graph stopped.");
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async Task StartMonitorsAsync(CancellationToken cancellationToken)
    {
        if (Display is not null)
        {
            await StartOneAsync("display", () => Display.StartAsync(cancellationToken)).ConfigureAwait(false);
        }

        foreach (var (name, start, _) in _monitorActions)
        {
            await StartOneAsync(name, () => start(cancellationToken)).ConfigureAwait(false);
        }
    }

    private async Task StartOneAsync(string name, Func<Task> start)
    {
        try
        {
            await start().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogError(PlatformLogCategory.Platform, $"graph.{name}-start-failed",
                $"Service '{name}' failed to start; continuing in degraded mode.", ex);
        }
    }

    private async Task RegisterHotkeysAsync()
    {
        if (_options.Hotkeys is null)
        {
            return;
        }

        foreach (var hotkey in _options.Hotkeys)
        {
            var result = await Hotkeys.RegisterAsync(hotkey.Name, hotkey.Gesture).ConfigureAwait(false);
            if (result.Success)
            {
                _registeredHotkeys.Add(hotkey.Name);
            }
            else
            {
                Logger.LogWarning(PlatformLogCategory.Shell, "graph.hotkey-failed",
                    $"Hotkey '{hotkey.Name}' ({hotkey.Gesture}) could not be registered: {result.Failure}.");
            }
        }
    }

    private async Task StopCoreAsync()
    {
        if (Tray is not null)
        {
            await StopOneAsync("tray", () => Tray.StopAsync()).ConfigureAwait(false);
        }

        if (_registeredHotkeys.Count > 0)
        {
            foreach (var name in _registeredHotkeys.ToArray())
            {
                await StopOneAsync($"hotkey:{name}", () => Hotkeys.UnregisterAsync(name)).ConfigureAwait(false);
            }

            _registeredHotkeys.Clear();
        }

        await StopOneAsync("hotkeys", () => Hotkeys.StopAsync()).ConfigureAwait(false);

        for (var index = _monitorActions.Count - 1; index >= 0; index--)
        {
            var action = _monitorActions[index];
            await StopOneAsync(action.Name, action.Stop).ConfigureAwait(false);
        }

        if (Display is not null)
        {
            await StopOneAsync("display", () => Display.StopAsync()).ConfigureAwait(false);
        }

        await StopOneAsync("host", async () =>
        {
            await Host.DisposeAsync().ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    private async Task StopOneAsync(string name, Func<Task> stop)
    {
        try
        {
            await stop().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(PlatformLogCategory.Platform, $"graph.{name}-stop-failed",
                $"Service '{name}' failed to stop cleanly.", ex);
        }
    }

    private void RegisterMonitor<TSnapshot>(string name, PlatformMonitor<TSnapshot> monitor)
        where TSnapshot : class, IEquatable<TSnapshot>
        => _monitorActions.Add((name, monitor.StartAsync, () => monitor.StopAsync()));

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(PlatformLogCategory.Platform, "graph.dispose-stop-failed",
                "Stopping the platform graph during disposal failed.", ex);
        }

        await DisposeOneAsync("update", () => Update?.DisposeAsync().AsTask() ?? Task.CompletedTask)
            .ConfigureAwait(false);
        await DisposeOneAsync("lyrics", () => Lyrics?.DisposeAsync().AsTask() ?? Task.CompletedTask)
            .ConfigureAwait(false);
        await DisposeOneAsync("artwork", () =>
        {
            ArtworkTempStore.Dispose();
            ArtworkCache?.Dispose();
            return Task.CompletedTask;
        }).ConfigureAwait(false);
        await DisposeOneAsync("logger", () => LogSink?.DisposeAsync().AsTask() ?? Task.CompletedTask)
            .ConfigureAwait(false);
        _ownedHttpClient?.Dispose();
        _lifecycleGate.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task DisposeOneAsync(string name, Func<Task> dispose)
    {
        try
        {
            await dispose().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(PlatformLogCategory.Platform, $"graph.{name}-dispose-failed",
                $"Resource '{name}' failed to dispose cleanly.", ex);
        }
    }
}
