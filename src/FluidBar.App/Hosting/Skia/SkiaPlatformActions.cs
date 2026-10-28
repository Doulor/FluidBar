using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluidBar.App.Island;
using FluidBar.App.Localization;
using FluidBar.App.Settings;
using FluidBar.App.State;
using FluidBar.Core.Configuration;
using FluidBar.Core.Diagnostics;
using System.Collections.Immutable;
using FluidBar.Platform.Windows.Agent;
using FluidBar.Platform.Windows.Calendar;
using FluidBar.Platform.Windows.Clipboard;
using FluidBar.Platform.Windows.CoreIntegration;
using FluidBar.Platform.Windows.Devices;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Focus;
using FluidBar.Platform.Windows.Media;
using FluidBar.Platform.Windows.Media.Lyrics;
using FluidBar.Platform.Windows.Monitors;
using FluidBar.Platform.Windows.Notifications;
using FluidBar.Platform.Windows.Radios;
using FluidBar.Platform.Windows.Tasks;
using FluidBar.Platform.Windows.Threading;
using FluidBar.Platform.Windows.Weather;

namespace FluidBar.App.Hosting.Skia;

/// <summary>
/// Production <see cref="IIslandPlatformActions"/> for the Skia island: the only App-side place that
/// turns a semantic page-control action into a Platform.Windows service call or an App-owned
/// deferred event.
/// <para>
/// Composition is narrow on purpose: it owns one message host plus the volume, brightness, GSMTC
/// media, clipboard and quick-toggle services (Wi-Fi/Bluetooth radios + focus) needed by the controls —
/// not the full platform graph (display, battery, notifications, tray/hotkeys stay with their existing
/// owners). Every service start is best-effort: a failed service leaves its controls reporting
/// <c>control-unsupported</c> with the exact missing service instead of pretending to work.
/// </para>
/// <para>
/// Nothing here blocks the UI thread: service calls are async and fire-and-forget with logged
/// failures, and <see cref="Invoke"/> only reads a snapshot or dispatches a task.
/// </para>
/// </summary>
public sealed class SkiaPlatformActions : IIslandPlatformActions, IIslandDataSource, IAsyncDisposable
{
    private readonly SettingsController _settings;
    private readonly ILocalizer? _localizer;
    private readonly TimeProvider _time;
    private readonly IAppLogger _logger;

    private long _lastSyncTextLogMs;
    private readonly IPlatformLogger _platformLogger;
    private readonly PlatformCounters _counters;
    private readonly PlatformMessageHost _messageHost;
    private readonly VolumeMonitor _volume;
    private readonly BatteryMonitor _battery;
    private readonly BrightnessMonitor _brightness;
    private readonly NetworkMonitor _network;
    private readonly GsmtcMediaSessionService _media;
    private readonly ClipboardService _clipboard;
    private readonly UsbDeviceMonitor _usbDevices;
    private readonly BluetoothDeviceMonitor _bluetoothDevices;
    private readonly AgentInboxWatcher _agentInbox;
    private readonly AgentProviderRegistry _agentNative;
    private readonly AgentEventSourceAggregator _agentSources;
    private readonly AgentHookInstaller _agentHooks;
    private readonly IRadioToggleService _wifi;
    private readonly IRadioToggleService _bluetooth;
    private readonly IFocusAssistService _focus;
    private readonly ArtworkCache _artworkCache;
    private readonly ArtworkTempStore _artworkTempStore;
    private readonly HttpClient _lyricsHttp;
    private readonly LyricsCache _lyricsCache;
    private readonly LyricsService _lyrics;
    private readonly SkiaSystemStats _systemStats;
    private readonly NotificationService _notifications;
    private readonly WeatherService _weather;
    private readonly TaskListService _tasks;
    private readonly CalendarEventService _calendarEvents;
    private readonly HashSet<string> _pinnedClipboard = new(StringComparer.Ordinal);
    private readonly Dictionary<uint, string> _pinKeyCache = new();
    private ClipboardFilter _clipboardFilter = ClipboardFilter.All;
    private string? _clipboardSearchQuery;
    private readonly string _clipboardPinsPath;
    private bool _weatherCityLockedByEnvironment;
    private INotificationSource? _pluginNotificationSource;
    private DateOnly _calendarMonth;
    private ITimer? _clockTimer;
    private ITimer? _mediaTick;
    private TimeSpan _mediaTickInterval;
    private LyricsDocument? _lyricsDocument;
    private string _lyricsTrackKey = string.Empty;

    /// <summary>Whether the current lyric document came from the player's own file (authoritative).</summary>
    private bool _lyricsServedLocally;

    /// <summary>Last time the local lyric file was re-probed for the current track.</summary>
    private DateTimeOffset _lyricsProbeAt;

    /// <summary>How often a provisional online document is re-checked against the player's file.</summary>
    private static readonly TimeSpan LyricsLocalReprobeInterval = TimeSpan.FromSeconds(5);
    private int _started;
    private int _disposed;

    /// <summary>Rows the island notification page keeps (five are visible; the rest stay bounded).</summary>
    private const int PageNotificationLimit = NotificationContentMapper.DefaultMaxItems;

    /// <summary>Creates the adapter; call <see cref="StartAsync"/> or <see cref="StartInBackground"/> once.</summary>
    /// <param name="settings">Live App settings (theme quick toggle + persistence).</param>
    /// <param name="logger">App logger for control/service failures.</param>
    /// <param name="timeProvider">Injected clock (tests/diagnostics).</param>
    public SkiaPlatformActions(SettingsController settings, IAppLogger? logger = null, TimeProvider? timeProvider = null,
        ILocalizer? localizer = null, string? weatherCity = null)
        : this(settings, logger, timeProvider, quickToggles: null, localizer, weatherCity: weatherCity)
    {
    }

    /// <summary>
    /// Composition/test overload: inject the three platform quick-toggle services. The adapter owns
    /// them and disposes them with the rest of the seam; tests pass fakes so no hardware is touched.
    /// </summary>
    /// <param name="settings">Live App settings (theme quick toggle + persistence).</param>
    /// <param name="logger">App logger for control/service failures.</param>
    /// <param name="timeProvider">Injected clock (tests/diagnostics).</param>
    /// <param name="quickToggles">Quick-toggle services, or null to build the production ones.</param>
    /// <param name="weatherOptions">Weather monitor options (env seams by default; tests inject a fake handler/cache).</param>
    /// <param name="taskOptions">Task file options (env seam by default; tests point at a temp file).</param>
    /// <param name="weatherCity">Settings-persisted weather city (empty = automatic); the environment variable still wins.</param>
    public SkiaPlatformActions(SettingsController settings, IAppLogger? logger, TimeProvider? timeProvider,
        IslandQuickToggleServices? quickToggles, ILocalizer? localizer = null,
        WeatherServiceOptions? weatherOptions = null, TaskListOptions? taskOptions = null,
        string? weatherCity = null, string? clipboardPinsPath = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _localizer = localizer;
        _clipboardPinsPath = clipboardPinsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FluidBar", "clipboard-pins.json");
        _logger = logger ?? NullAppLogger.Instance;
        LoadClipboardPins();
        _platformLogger = new AppLoggerPlatformAdapter(_logger);
        _counters = new PlatformCounters();
        var time = timeProvider ?? TimeProvider.System;
        _time = time;

        _messageHost = new PlatformMessageHost(_platformLogger, _counters);
        _volume = new VolumeMonitor(_platformLogger, _counters, time);
        _battery = new BatteryMonitor(_messageHost, _platformLogger, _counters, null, time);
        _brightness = new BrightnessMonitor(_platformLogger, _counters, time);
        _network = new NetworkMonitor(_platformLogger, _counters, time);
        _artworkCache = new ArtworkCache();
        _artworkTempStore = new ArtworkTempStore(
            new ArtworkTempStoreOptions { DirectoryPath = Path.Combine(Path.GetTempPath(), "FluidBar", "art") },
            _platformLogger,
            time);
        _media = new GsmtcMediaSessionService(_platformLogger, _counters, _artworkCache, time,
            powerModeProvider: () => _settings.Current.PowerMode);
        _clipboard = new ClipboardService(_messageHost, _platformLogger, _counters, null, time);
        _usbDevices = new UsbDeviceMonitor(_messageHost, _platformLogger, _counters, time);
        _bluetoothDevices = new BluetoothDeviceMonitor(_platformLogger, _counters, time);

        // Agent inbox: hook scripts drop one JSON file per Claude Code / Codex event here. Read-only
        // watcher (no polling); the hook installer only writes when the user runs the consent command.
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FluidBar");
        var inboxDirectory = Path.Combine(dataDirectory, "agent-events");
        _agentInbox = new AgentInboxWatcher(new AgentInboxOptions { InboxDirectory = inboxDirectory },
            _platformLogger, _counters, time);
        _agentHooks = new AgentHookInstaller(new HookInstallOptions
        {
            SettingsFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json"),
            HookCommand = BuildAgentHookCommand(inboxDirectory),
            BackupDirectory = Path.Combine(dataDirectory, "backups", "agent-hooks"),
        }, _platformLogger);
        // Native, read-only agent providers: DSH, Claude Code, Codex, Copilot CLI and Aider are observed
        // purely by reading the local files they already write. No plugin, no script, no configuration.
        _agentNative = new AgentProviderRegistry(NativeAgentProviders.CreateDefault(),
            new AgentProviderRegistryOptions(), _platformLogger, _counters, time);
        _agentSources = new AgentEventSourceAggregator(_agentInbox, _agentNative);
        _agentSources.SnapshotChanged += OnSnapshotChanged;
        _lyricsHttp = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        _lyricsCache = new LyricsCache(timeProvider: time, counters: _counters);
        _lyrics = new LyricsService(
            new ILyricsProvider[]
            {
                // The player's own cache wins: it is offline and exactly the lyric the player shows,
                // which removes the "wrong song" matches the online catalogues produce for KuGou.
                new KugouLocalLyricsProvider(timeProvider: time, logger: _platformLogger),
                new NeteaseLyricsProvider(_lyricsHttp, _platformLogger),
                new KugouLyricsProvider(_lyricsHttp, _platformLogger),
            },
            _lyricsCache,
            _platformLogger,
            _counters);
        _systemStats = new SkiaSystemStats(time);

        // The island page needs the toast list (bounded recent + close sync), not just the last event:
        // this is the app's SINGLE listener (seeding enabled). AppBootstrap hands the arrivals-only view
        // (PluginNotificationSource) to the notifications plugin, so the plugin keeps its own semantics
        // without a second read loop.
        _notifications = new NotificationService(_platformLogger, _counters, new NotificationServiceOptions
        {
            MaxRecentNotifications = PageNotificationLimit,
            SeedRecentFromExisting = true,
            PollInterval = TimeSpan.FromMilliseconds(2000),
        }, time);
        _notifications.SnapshotChanged += OnNotificationsChanged;

        // Widgets page real data: Open-Meteo (no key) with a %APPDATA% cache, and the local tasks.json.
        // Both are monitors: they run on their own background pollers and never block the UI thread.
        var resolvedWeatherOptions = weatherOptions
            ?? WeatherServiceOptions.FromEnvironment(localizer?.Culture);
        if (weatherOptions is null)
        {
            // "FLUIDBAR_WEATHER_CITY" stays the explicit override; otherwise the settings page value wins.
            _weatherCityLockedByEnvironment =
                !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FLUIDBAR_WEATHER_CITY"));
            var configuredCity = string.IsNullOrWhiteSpace(weatherCity) ? settings.Current.WeatherCity : weatherCity;
            if (!_weatherCityLockedByEnvironment && !string.IsNullOrWhiteSpace(configuredCity))
            {
                resolvedWeatherOptions = resolvedWeatherOptions with { City = configuredCity.Trim() };
            }
        }

        _weather = new WeatherService(_platformLogger, _counters, resolvedWeatherOptions, time);
        _weather.SnapshotChanged += OnWidgetDataChanged;
        _settings.Changed += OnSettingsChanged;
        _tasks = new TaskListService(_platformLogger, _counters,
            taskOptions ?? TaskListOptions.FromEnvironment(), time);
        _tasks.SnapshotChanged += OnWidgetDataChanged;
        _calendarEvents = new CalendarEventService(_platformLogger, time);
        _calendarEvents.Changed += OnCalendarEventsChanged;

        var today = time.GetLocalNow();
        _calendarMonth = new DateOnly(today.Year, today.Month, 1);

        _media.SnapshotChanged += OnSnapshotChanged;
        _clipboard.SnapshotChanged += OnSnapshotChanged;
        _battery.SnapshotChanged += OnSnapshotChanged;
        _volume.SnapshotChanged += OnSnapshotChanged;
        _brightness.SnapshotChanged += OnSnapshotChanged;
        _network.SnapshotChanged += OnSnapshotChanged;

        var toggles = quickToggles ?? IslandQuickToggleServices.CreateDefault(_platformLogger, _counters, time);
        _wifi = toggles.Wifi;
        _bluetooth = toggles.Bluetooth;
        _focus = toggles.Focus;
    }

    /// <summary>GSMTC media source exposed to first-party plugins (same instance the island controls use).</summary>
    public IMediaSessionSource MediaSource => _media;

    /// <summary>Clipboard source exposed to first-party plugins.</summary>
    public IClipboardSource ClipboardSource => _clipboard;

    /// <summary>USB device change source exposed to the first-party devices plugin.</summary>
    public IDeviceEventSource UsbDeviceSource => _usbDevices;

    /// <summary>Bluetooth device change source exposed to the first-party devices plugin.</summary>
    public IDeviceEventSource BluetoothDeviceSource => _bluetoothDevices;

    /// <summary>Agent event inbox exposed to first-party plugins (Claude Code / Codex hooks).</summary>
    public IAgentInboxSource AgentInboxSource => _agentSources;

    /// <summary>Consent-gated agent hook installer exposed to first-party plugins.</summary>
    public IAgentHookInstaller AgentHookInstaller => _agentHooks;

    /// <summary>Bounded artwork materializer exposed to the media plugin (<c>AlbumArtPath</c>).</summary>
    public ArtworkTempStore ArtworkTempStore => _artworkTempStore;

    /// <summary>Dedicated platform message host (window messages for the App-composed services).</summary>
    public PlatformMessageHost MessageHost => _messageHost;

    /// <summary>Diagnostics counters shared by the platform services.</summary>
    public PlatformCounters Counters => _counters;

    /// <summary>Platform logger used by the App-composed services.</summary>
    public IPlatformLogger PlatformLogger => _platformLogger;

    /// <summary>Weather monitor (widgets page; Open-Meteo + local cache). Exposed for diagnostics/tests.</summary>
    public WeatherService WeatherSource => _weather;

    /// <summary>Task file service (widgets page; <c>%APPDATA%\\FluidBar\\tasks.json</c>).</summary>
    public TaskListService TaskSource => _tasks;

    /// <summary>
    /// The app's only toast listener: the notification page maps it, and the plugin path consumes the
    /// arrivals-only view below. Both consumers share this instance (one read loop, one disposal).
    /// </summary>
    public INotificationSource NotificationSource => _notifications;

    /// <summary>
    /// Plugin view over the same listener: hides the backlog the page seeds, so the compact notification
    /// activity still only fires for toasts that arrive while the app runs.
    /// </summary>
    public INotificationSource PluginNotificationSource =>
        _pluginNotificationSource ??= _notifications.CreateNewArrivalsView();

    /// <summary>Latest Wi-Fi radio snapshot (null before the service starts).</summary>
    public RadioSnapshot? WifiState => _wifi.Current;

    /// <summary>Latest Bluetooth radio snapshot (null before the service starts).</summary>
    public RadioSnapshot? BluetoothState => _bluetooth.Current;

    /// <summary>Latest focus/quiet-hours snapshot (null before the service starts).</summary>
    public FocusAssistSnapshot? FocusState => _focus.Current;

    /// <summary>Observed Wi-Fi power state, or null while the service has not read it yet.</summary>
    public bool? IsWifiEnabled => _wifi.IsEnabled;

    /// <summary>Observed Bluetooth power state, or null while the service has not read it yet.</summary>
    public bool? IsBluetoothEnabled => _bluetooth.IsEnabled;

    /// <summary>Whether a Windows focus session is active, or null before the first observation.</summary>
    public bool? IsFocusActive => _focus.IsActive;

    /// <summary>Requests Wi-Fi on/off. Never blocks the caller; the result is explicit (no fake success).</summary>
    /// <param name="enabled">True for On, false for Off.</param>
    /// <param name="cancellationToken">Cancellation for the WinRT request.</param>
    public Task<RadioToggleResult> SetWifiEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
        => _wifi.SetEnabledAsync(enabled, cancellationToken);

    /// <summary>Requests Bluetooth on/off. Never blocks the caller; the result is explicit.</summary>
    /// <param name="enabled">True for On, false for Off.</param>
    /// <param name="cancellationToken">Cancellation for the WinRT request.</param>
    public Task<RadioToggleResult> SetBluetoothEnabledAsync(bool enabled,
        CancellationToken cancellationToken = default)
        => _bluetooth.SetEnabledAsync(enabled, cancellationToken);

    /// <summary>Requests a focus session start/end. Never blocks the caller; the result is explicit.</summary>
    /// <param name="active">True to start a focus session, false to end it.</param>
    /// <param name="cancellationToken">Cancellation for the WinRT request.</param>
    public Task<FocusToggleResult> SetFocusActiveAsync(bool active, CancellationToken cancellationToken = default)
        => _focus.SetActiveAsync(active, cancellationToken);

    /// <summary>
    /// Raised for actions that belong to a later App owner (notification activation/dismiss, agent
    /// activation, task toggle, calendar navigation, clipboard pin). Subscribers must be fast; the
    /// controller already reported the action as deferred.
    /// </summary>
    public event Action<IslandPlatformAction>? DeferredActionRequested;

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <summary>Raised when the user asks for the clipboard search window.</summary>
    public event EventHandler? ClipboardSearchRequested;

    /// <inheritdoc />
    public IslandContent Snapshot()
    {
        var dumpEnabled = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FLUIDBAR_VERIFY_DUMP"));
        var battery = _battery.Current;
        var focus = _focus.Current;
        var network = _network.Current;
        var media = _media.Current;
        var volume = _volume.Current;
        var brightness = _brightness.Current;
        var clipboard = _clipboard.Current?.History ?? ImmutableArray<ClipboardEntry>.Empty;
        PrunePinKeyCache(clipboard);
        var now = _time.GetLocalNow();
        var culture = _localizer?.CurrentCulture ?? CultureInfo.CurrentCulture;
        int? batteryPercent = battery is { HasBattery: true } ? battery.Percent : null;
        var today = DateOnly.FromDateTime(now.DateTime);
        // Widgets page: real weather (fresh or last-known cached) and the local task file. Both mappers
        // return explicit empty values when nothing real exists, so the cards never show invented data.
        var weather = WidgetContentMapper.MapWeather(_weather.Current, now, _localizer);
        var tasks = WidgetContentMapper.MapTasks(_tasks.Current, _localizer, _tasks.FilePath);
        var snapshot = new IslandContent
        {
            Now = now,
            BatteryPercent = batteryPercent,
            IsCharging = battery?.IsCharging ?? false,
            // Real WlanAPI SSID when the primary adapter is Wi-Fi; otherwise the adapter name, or
            // an explicit "Disconnected". The old code never mapped the monitor at all, so the home
            // tile always said the generic "Wi-Fi".
            NetworkName = NetworkAdapterSelector.DisplayName(
                network, Localized(LocKeys.Common.Disconnected, "Disconnected")),
            IsOnline = network?.IsConnected ?? true,
            IsWifiEnabled = _wifi.Current?.IsEnabled,
            IsBluetoothEnabled = _bluetooth.Current?.IsEnabled,
            FocusOn = focus?.IsActive ?? false,
            FocusAvailable = focus?.CanToggle ?? false,
            VolumePercent = volume is { HasDevice: true } ? volume.Percent : null,
            IsMuted = volume?.IsMuted ?? false,
            BrightnessPercent = brightness is { IsSupported: true } ? brightness.Percent : null,
            Media = media is { IsEmpty: false } ? MapMedia(media) : null,
            Clipboard = DisplayedClipboard().Select(MapClipboard).ToImmutableArray(),
            ClipboardFilterLabel = _clipboard.GetHistory().Count > 0 ? ClipboardFilterLabel() : null,
            ClipboardSearchQuery = _clipboardSearchQuery,
            // Real Windows toast listener (see the ctor): seeded with the current list, kept in sync by
            // arrival and by the user closing a toast in Action Center.
            Notifications = NotificationContentMapper.Map(
                _notifications.Current, PageNotificationLimit, now, _localizer, _notifications.ReadIds),
            Agents = MapAgents(_agentSources.Current),
            Tasks = tasks.Tasks,
            TasksEmptyHint = tasks.EmptyHint,
            TasksMoreText = tasks.MoreText,
            TasksProgressText = tasks.ProgressText,
            WeatherTemp = weather.TemperatureText,
            WeatherConditionText = weather.ConditionText,
            WeatherHighLow = weather.HighLowText,
            WeatherWind = weather.WindText,
            WeatherGlyph = weather.Glyph,
            // The widgets page owns the calendar locally: the month grid needs no platform service, so
            // the previous empty card was a missing mapping rather than missing data.
            CalendarMonthTitle = FormatMonthTitle(_calendarMonth, culture),
            WeekdayHeaders = BuildWeekdayHeaders(culture),
            CalendarDays = BuildCalendarDays(_calendarMonth, today, _calendarEvents.Counts),
            Stats = _systemStats.Build(batteryPercent, Localized(LocKeys.Battery.Title, "Battery")),
            UnreadCount = Math.Clamp(
                _notifications.Current?.Recent.Count(entry => !_notifications.ReadIds.Contains(entry.Id)) ?? 0,
                0,
                99),
        };
        if (dumpEnabled)
        {
            WriteVerifyRecord(snapshot, media is { IsEmpty: false } ? media : null);
        }

        return snapshot;
    }

    /// <inheritdoc />
    public void Start() => StartInBackground();

    /// <inheritdoc />
    public void Stop()
    {
        // Lifecycle is owned by the App composition; DisposeAsync stops every service.
    }

    private void OnSnapshotChanged<T>(object? sender, SnapshotEventArgs<T> args) where T : class, IEquatable<T>
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        EnsureLyricsForCurrentSession();
        EnsureMediaTick();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Settings changed: the widgets weather follows the "weather city" field live, and the theme
    /// toggle must repaint immediately (otherwise the button flips a stored value the island never
    /// renders again, which reads as a dead button).
    /// </summary>
    private void OnSettingsChanged(AppSettings previous, AppSettings current)
    {
        if (previous.PowerMode != current.PowerMode)
        {
            EnsureMediaTick();
        }

        if (previous.Theme != current.Theme)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        if (_weatherCityLockedByEnvironment
            || string.Equals(previous.WeatherCity, current.WeatherCity, StringComparison.Ordinal))
        {
            return;
        }

        _weather.SetCity(current.WeatherCity);
    }

    /// <summary>Weather or task data changed (widgets page repaint).</summary>
    private void OnCalendarEventsChanged(object? sender, EventArgs e)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnWidgetDataChanged<T>(object? sender, SnapshotEventArgs<T> args) where T : class, IEquatable<T>
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.Warning(LogCategories.Platform, "island.actions.widgets-changed-failed",
                "Refreshing the island after a widgets data change failed.", ex);
        }
    }

    /// <summary>Toast list changed (new toast, closed toast, or the service's polling fallback tick).</summary>
    private void OnNotificationsChanged(object? sender, SnapshotEventArgs<NotificationsSnapshot> args)
    {
        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            // The plugin path subscribes to the same listener: a page-side consumer failure is logged and
            // must never break the notification activity (and vice versa).
            _logger.Warning(LogCategories.Platform, "island.actions.notifications-changed-failed",
                "Refreshing the island after a notification change failed; the plugin path is unaffected.", ex);
        }
    }

    /// <summary>
    /// Starts (or stops) the 1 Hz media refresh. While a session is playing the progress bar and the
    /// lyric line must advance without a platform event, so the island re-renders once per second;
    /// with no playing session the timer does not exist (zero idle cost).
    /// </summary>
    private void EnsureMediaTick()
    {
        var media = _media.Current;
        var playing = media is { IsEmpty: false, IsPlaying: true };
        var interval = _settings.Current.PowerMode == IslandPowerMode.Saver
            ? TimeSpan.FromSeconds(2)
            : TimeSpan.FromSeconds(1);
        if (playing)
        {
            if (_mediaTick is not null && _mediaTickInterval == interval)
            {
                return;
            }

            _mediaTick?.Dispose();
            _mediaTickInterval = interval;
            _mediaTick = _time.CreateTimer(
                static state => ((SkiaPlatformActions)state!).OnMediaTick(),
                this,
                interval,
                interval);
            return;
        }

        _mediaTick?.Dispose();
        _mediaTick = null;
        _mediaTickInterval = TimeSpan.Zero;
    }

    private void OnMediaTick()
    {
        if (Volatile.Read(ref _disposed) != 0 || Volatile.Read(ref _started) == 0)
        {
            return;
        }

        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.Warning(LogCategories.Platform, "island.actions.media-tick-failed",
                "Refreshing the island media progress failed.", ex);
        }

        EnsureMediaTick();
    }

    /// <summary>Kicks a lyrics lookup when the track changed (single-flight, cached, failure-silent).</summary>
    private void EnsureLyricsForCurrentSession()
    {
        var media = _media.Current;
        if (media is null || media.IsEmpty || string.IsNullOrWhiteSpace(media.TrackKey))
        {
            _lyricsDocument = null;
            _lyricsTrackKey = string.Empty;
            return;
        }

        if (string.Equals(_lyricsTrackKey, media.TrackKey, StringComparison.Ordinal))
        {
            // The player downloads its .krc a while after the track starts, so an online document obtained at
            // track start is provisional. Re-check every few seconds until the local file wins (file index only,
            // no network: LyricsService re-probes just the local providers on a cache hit).
            if (!_lyricsServedLocally
                && _time.GetUtcNow() - _lyricsProbeAt >= LyricsLocalReprobeInterval)
            {
                _lyricsProbeAt = _time.GetUtcNow();
                _ = LoadLyricsAsync(media);
            }

            return;
        }

        _lyricsTrackKey = media.TrackKey;
        _lyricsDocument = null;
        _lyricsServedLocally = false;
        _lyricsProbeAt = _time.GetUtcNow();

        // Browser media sessions are usually videos; matching their title/artist against lyric
        // catalogs produced unrelated lines (e.g. a video's "下一步" caption). Only browsers that
        // expose album metadata (music sites) are treated as music.
        if (IsBrowserSource(media.SourceAppUserModelId) && string.IsNullOrWhiteSpace(media.Album))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(media.Title) || string.IsNullOrWhiteSpace(media.Artist))
        {
            return;
        }

        _lyrics.CancelPending(media.TrackKey);
        _ = LoadLyricsAsync(media);
    }

    /// <summary>Whether the media source is a browser (video-first unless it exposes an album).</summary>
    private static bool IsBrowserSource(string? appUserModelId)
    {
        if (string.IsNullOrWhiteSpace(appUserModelId))
        {
            return false;
        }

        foreach (var browser in BrowserSources)
        {
            if (appUserModelId.Contains(browser, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static readonly string[] BrowserSources =
    [
        "msedge", "chrome", "firefox", "opera", "brave", "vivaldi", "chromium", "safari",
    ];

    private async Task LoadLyricsAsync(MediaSnapshot media)
    {
        try
        {
            var document = await _lyrics.GetLyricsAsync(
                new LyricsQuery(media.Title, media.Artist, media.Duration, media.TrackKey)).ConfigureAwait(false);
            if (document is not { IsEmpty: false }
                || !string.Equals(_lyricsTrackKey, media.TrackKey, StringComparison.Ordinal))
            {
                return;
            }

            _lyricsDocument = document;
            _lyricsServedLocally = document.Source.StartsWith("kugou-l", StringComparison.Ordinal)
                || document.Source.StartsWith("kugou-k", StringComparison.Ordinal);
            // What the island is actually about to paint, so a "wrong lyrics" report can be checked against the
            // document that was chosen instead of guessed at (rare: once per track, plus once when the local file lands).
            FluidBar.Platform.Windows.Media.MediaDiagnostics.LogAlways(
                $"lyrics.shown title=\"{media.Title}\" artist=\"{media.Artist}\" source={document.Source} " +
                $"lines={document.Lines.Length} first=\"{(document.Lines.Length > 0 ? document.Lines[0].Text : string.Empty)}\"" +
                $" duration={document.Duration}");
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            // Lyrics are a nicety: a provider outage must never surface as a broken island.
            _logger.Debug(LogCategories.Platform, "island.actions.lyrics-failed",
                "Lyrics lookup failed for the current track: " + ex.Message);
        }
    }

    /// <summary>True when the lyric document describes the track that is playing.</summary>
    private static bool LyricsBelongToTrack(LyricsDocument document, MediaSnapshot media)
    {
        var title = (document.Title ?? string.Empty).Trim();
        var artist = (document.Artist ?? string.Empty).Trim();
        if (title.Length == 0)
        {
            return true;
        }

        var wanted = (media.Title ?? string.Empty).Trim();
        var wantedArtist = (media.Artist ?? string.Empty).Trim();
        var titleOk = Mentions(title, wanted) || Mentions(wanted, title);
        var artistOk = artist.Length == 0 || wantedArtist.Length == 0
            || Mentions(artist, wantedArtist) || Mentions(wantedArtist, artist);
        return titleOk && artistOk;
    }

    /// <summary>
    /// True when the lyric document can plausibly belong to the track: their lengths agree within
    /// <paramref name="tolerance"/> (10 s or 8 %, whichever is larger). The player now reports its exact
    /// duration, so a document describing a different recording is caught even when the title matches —
    /// this is what stopped instrumentals from scrolling some other song's words.
    /// </summary>
    private static bool LyricFitsTrackDuration(FluidBar.Platform.Windows.Media.Lyrics.LyricsDocument document,
        MediaSnapshot media)
    {
        if (document.Duration <= TimeSpan.Zero || media.Duration <= TimeSpan.Zero)
        {
            return true;
        }

        // Generous: a lyric file can legitimately end well before the track (long outros). The point is to
        // catch a document describing a different recording, not to police the outro length.
        var tolerance = TimeSpan.FromSeconds(Math.Max(20, media.Duration.TotalSeconds * 0.10));
        return (document.Duration - media.Duration).Duration() <= tolerance;
    }

    private static bool Mentions(string haystack, string needle)
        => needle.Length > 0 && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private (string? Current, string? Next, double Highlight, FluidBar.Island.KaraokeTrack? Karaoke) ResolveLyrics(
        MediaSnapshot media, TimeSpan position)
    {
        // Instrumentals have nothing to sing: show the title and artist instead of whatever the catalogue
        // matched for that title (user report 2026-10-22: "纯音乐随机取了别的歌的歌词").
        if (FluidBar.Platform.Windows.Media.Lyrics.InstrumentalDetector.LooksInstrumental(media.Title))
        {
            return (null, null, -1, null);
        }

        // The player's own KRC file comes FIRST: it is exactly what KuGou is showing (word-level timing)
        // and cannot be another song. It used to be consulted after the "no online document" early
        // return, so a track without an online lyric never reached it (user report 2026-10-21).
        var localDocument = FluidBar.Platform.Windows.Media.Lyrics.KugouLyricCache.TryFind(media.Title, media.Artist);
        if (FluidBar.Platform.Windows.Media.Lyrics.InstrumentalDetector.IsPlaceholder(localDocument))
        {
            // The player's own lyric for this file is the "纯音乐，请欣赏" placeholder: treat it as no lyric.
            return (null, null, -1, null);
        }

        if (localDocument is { IsEmpty: false } && !LyricFitsTrackDuration(localDocument, media))
        {
            // Another song's lyric file: an instrumental with a short/absent artist name used to pick the
            // nearest file by name and scroll a stranger's words (user report 2026-10-23).
            _logger.Information(LogCategories.Platform, "lyrics.duration-mismatch",
                $"Rejected local lyric '{localDocument.Title}' ({localDocument.Duration.TotalSeconds:0}s) for '{media.Title}' ({media.Duration.TotalSeconds:0}s).");
            return (null, null, -1, null);
        }

        if (localDocument is { IsEmpty: false })
        {
            var localSelection = LyricsTimelineSelector.SelectDetailed(localDocument, position, media.Title, media.Artist);
            _logger.Information(LogCategories.Platform, "lyrics.local",
                $"Local lyric used: '{localDocument.Title}' by '{localDocument.Artist}' lines={localDocument.Lines.Length} track='{media.Title}'.");
            return (localSelection.Current, localSelection.Next, localSelection.Highlight,
                ToKaraokeTrack(localSelection.Line, localDocument));
        }

        var document = _lyricsDocument;
        if (document is null || document.IsEmpty
            || !string.Equals(_lyricsTrackKey, media.TrackKey, StringComparison.Ordinal))
        {
            return (null, null, -1, null);
        }

        // SelectDetailed also returns the karaoke sweep of the current line (KRC word timeline).
        // The catalogue search is fuzzy: a cover, a same-titled single or a mistyped window title
        // returns another song's lyrics, which then scroll against the wrong track. Reject a document
        // whose metadata does not describe this track (user report 2026-10-21).
        if (!LyricFitsTrackDuration(document, media))
        {
            _logger.Information(LogCategories.Platform, "lyrics.duration-mismatch",
                $"Rejected lyric '{document.Title}' ({document.Duration.TotalSeconds:0}s) for '{media.Title}' ({media.Duration.TotalSeconds:0}s).");
            return (null, null, -1, null);
        }

        if (!LyricsBelongToTrack(document, media))
        {
            _logger.Information(LogCategories.Platform, "lyrics.rejected",
                $"Rejected lyrics '{document.Title}' by '{document.Artist}' ({document.Source}) for '{media.Title}'.");
            return (null, null, -1, null);
        }

        var selection = LyricsTimelineSelector.SelectDetailed(document, position, media.Title, media.Artist);
        _logger.Information(LogCategories.Platform, "lyrics.online",
            $"Online lyric used: '{document.Title}' by '{document.Artist}' ({document.Source}, {document.Duration.TotalSeconds:0}s) lines={document.Lines.Length} track='{media.Title}'.");
        return (selection.Current, selection.Next, selection.Highlight, ToKaraokeTrack(selection.Line, document));
    }

    /// <summary>
    /// Portable timeline for the island (which advances the sweep per frame). Word timing wins; a
    /// whole-line-only lyric source falls back to one span spanning the line's real duration so the
    /// user still sees a progressive fill instead of no effect at all.
    /// </summary>
    private static FluidBar.Island.KaraokeTrack? ToKaraokeTrack(
        LyricsLine? line,
        LyricsDocument document)
    {
        if (line is null || line.Text.Length == 0)
        {
            return null;
        }

        var lineStart = (int)Math.Round(line.Time.TotalMilliseconds);
        if (line.HasWordTiming)
        {
            var spans = System.Collections.Immutable.ImmutableArray.CreateBuilder<FluidBar.Island.KaraokeSpan>(
                line.Words.Length);
            var lastOffset = 0;
            foreach (var word in line.Words)
            {
                spans.Add(new FluidBar.Island.KaraokeSpan(word.OffsetMs, word.Length));
                lastOffset = Math.Max(lastOffset, Math.Max(0, word.OffsetMs));
            }

            // Some real KRC sources repeat the same offset for every word (notably repeated
            // "Na na na" lines); the scalar highlight then sits at 1 and the user sees no sweep.
            // Only trust word timing when it actually spans time.
            if (lastOffset >= MinWordTimingSpanMs)
            {
                return FluidBar.Island.KaraokeTrack.Create(
                    lineStart,
                    lastOffset + KaraokeTailMs,
                    spans.MoveToImmutable());
            }
        }

        // Whole-line fallback: duration = next timed line (or the document's track length). No
        // duration means no honest fill, so keep the plain line instead of inventing one.
        var lineEnd = document.Duration;
        foreach (var candidate in document.Lines)
        {
            if (candidate.Time > line.Time)
            {
                lineEnd = candidate.Time;
                break;
            }
        }

        var durationMs = (int)Math.Round((lineEnd - line.Time).TotalMilliseconds);
        if (durationMs <= 0)
        {
            // Last line of a source without a track duration: infer a conservative reading time from
            // the text so the whole-line fallback still animates instead of freezing.
            durationMs = Math.Clamp(line.Text.Length * 160, 900, 6000);
        }

        return FluidBar.Island.KaraokeTrack.Create(
            lineStart,
            durationMs,
            System.Collections.Immutable.ImmutableArray.Create(
                new FluidBar.Island.KaraokeSpan(0, line.Text.Length)));
    }

    /// <summary>Trailing duration given to the last span when the file carries no line duration.</summary>
    private const int KaraokeTailMs = 400;

    /// <summary>Word timings that never advance (all offsets 0) are treated as line-only.</summary>
    private const int MinWordTimingSpanMs = 250;

    /// <summary>
    /// Track length for sources that expose none of their own (the window-title fallback). The lyric
    /// source for the same track knows it, which is real data for this song — not an invented number.
    /// </summary>
    private TimeSpan LyricDurationFor(MediaSnapshot media)
    {
        var document = _lyricsDocument;
        return document is not null
               && document.Duration > TimeSpan.Zero
               && string.Equals(_lyricsTrackKey, media.TrackKey, StringComparison.Ordinal)
            ? document.Duration
            : TimeSpan.Zero;
    }

    private MediaSessionData MapMedia(MediaSnapshot media)
    {
        var duration = media.Duration > TimeSpan.Zero ? media.Duration : LyricDurationFor(media);

        // The track length from the player's own clock is the least reliable number we have: the text it
        // comes from freezes with the window. The lyric timeline (local KRC: last line end; online: the
        // duration the provider reports) is stable and cannot be minutes off, so it wins whenever the two
        // disagree by more than a tolerance (user report 2026-10-21: "时长不匹配").
        var lyricDuration = LyricDurationFor(media);
        if (lyricDuration > TimeSpan.Zero
            && (duration <= TimeSpan.Zero || Math.Abs((duration - lyricDuration).TotalSeconds) > 3))
        {
            duration = lyricDuration;
        }

        // Optimistic play state (see SetOptimisticPlaying): until the source agrees, publish the flip.
        var sourcePlaying = media.IsPlaying;
        var isPlaying = _optimisticPlay.Resolve(sourcePlaying, _time.GetUtcNow());
        if (isPlaying != _publishedPlaying || _optimisticPlay.IsPending)
        {
            // Play-state provenance: what the island paints, what the source reported, which channel the
            // reading came from (window-title fallback = MSAA glyph, otherwise the GSMTC session) and
            // whether an optimistic flip is still pending.
            _logger.Debug(LogCategories.Platform, "media.play-state",
                $"island={(isPlaying ? "playing" : "paused")} " +
                $"source={(sourcePlaying ? "playing" : "paused")} " +
                $"channel={(media.TrackKey.StartsWith("win:", StringComparison.Ordinal) ? "msaa-fallback" : "gsmtc")} " +
                $"optimistic={(_optimisticPlay.IsPending ? "pending" : "settled")} track='{media.Title}'");
        }

        _publishedPlaying = isPlaying;

        if (_optimisticPlay.LastResolveRolledBack)
        {
            // The player never confirmed the flip inside the optimism window: re-read the source once
            // instead of keeping a stale reading (this is what made the icon disagree with the audio).
            RequestSourceReread();
        }

        var position = ResolvePosition(media, duration, isPlaying);

        // Anchor provenance: what the platform sampled and what we published. One line per poll makes a
        // "the time is off" report attributable without guesswork.
        if (Environment.TickCount64 - _lastSyncTextLogMs > 5000)
        {
            _lastSyncTextLogMs = Environment.TickCount64;
            FluidBar.Platform.Windows.Media.MediaDiagnostics.LogAlways(
                $"sync.island {position.TotalSeconds,7:0.0}s text='{(int)(position.TotalSeconds / 60)}:{(int)(position.TotalSeconds % 60):00}' title='{media.Title}'");
        }

        _logger.Information(LogCategories.Platform, "position.anchor",
            $"sampled={media.Position.TotalMilliseconds:0}ms published={position.TotalMilliseconds:0}ms " +
            $"duration={duration.TotalMilliseconds:0}ms playing={isPlaying} title='{media.Title}'");

        var progress = duration.TotalMilliseconds > 0
            ? (int)Math.Clamp(position.TotalMilliseconds / duration.TotalMilliseconds * 100, 0, 100)
            : 0;

        var artwork = _artworkCache.TryGet(media.TrackKey, out var artworkEntry) && artworkEntry is not null
            ? artworkEntry.Data
            : null;
        if (artwork is not null)
        {
            _lastArtworkTitle = CoreTitle(media.Title);
            _lastArtworkEntry = artworkEntry;
        }
        else if (_lastArtworkEntry is not null
                 && _lastArtworkTitle.Length > 0
                 && string.Equals(_lastArtworkTitle, CoreTitle(media.Title), StringComparison.OrdinalIgnoreCase))
        {
            // The identity key can change for a cosmetic reason (caption refinement, artist field filled in
            // a beat later). Reusing the cover we already decoded keeps it on screen instead of flashing
            // placeholder -> cover -> placeholder ("封面获取到了又丢失了", user report 2026-10-22).
            artwork = _lastArtworkEntry.Data;
        }

        // Island-side provenance: whether the pill actually received cover bytes for this track. The fetch side logs
        // "artwork.origin" (what the chain produced); this line says whether the island saw it (2026-10-28 report:
        // "还是总是获取不到音频封面" while the fetch side claimed success).
        var hasArt = artwork is { Length: > 0 };
        if (!string.Equals(_lastArtworkLogKey, media.TrackKey, StringComparison.Ordinal)
            || hasArt != _lastArtworkLogHasArt)
        {
            _lastArtworkLogKey = media.TrackKey;
            _lastArtworkLogHasArt = hasArt;
            FluidBar.Platform.Windows.Media.MediaDiagnostics.LogAlways(
                $"artwork.island title=\"{media.Title}\" hasArt={hasArt} bytes={artwork?.Length ?? 0} " +
                $"sticky={artwork is not null && _artworkCache.TryGet(media.TrackKey, out _) == false}");
        }

        var (lyricLine, nextLyricLine, lyricHighlight, karaoke) = ResolveLyrics(media, position);

        // A fallback source can know the position without knowing the track length (KuGou persists the
        // position but no duration): show the elapsed time instead of pretending nothing is known.
        var hasDuration = duration > TimeSpan.Zero;
        var hasElapsed = hasDuration || position > TimeSpan.Zero;

        return new MediaSessionData
        {
            Title = media.Title,
            Artist = media.Artist,
            Album = media.Album,
            Source = media.SourceDisplayName ?? string.Empty,
            ProgressPercent = progress,
            PositionText = hasElapsed ? FormatTime(position) : "—",
            DurationText = hasDuration ? FormatTime(duration) : "—",
            HasTimeline = hasDuration,
            CanSeek = media.CanSeek && hasDuration,
            IsPlaying = isPlaying,
            Artwork = artwork,
            ArtworkKey = artwork is null ? null : media.TrackKey,
            LyricLine = lyricLine,
            NextLyricLine = nextLyricLine,
            LyricHighlight = lyricHighlight,
            Karaoke = karaoke,
            // The island extrapolates this sample with its own frame clock; the sample time is "now" here
            // because the position above was already interpolated to the current instant.
            PositionMs = position.TotalMilliseconds,
            DurationMs = duration.TotalMilliseconds,
            TrackIndex = 0,
            TrackCount = 0,
        };
    }

    private string? _lastArtworkLogKey;
    private bool _lastArtworkLogHasArt;

    /// <summary>Last artwork shown, reused while the track (by title core) is unchanged.</summary>
    private string _lastArtworkTitle = string.Empty;
    private ArtworkEntry? _lastArtworkEntry;

    /// <summary>Title without decorations ("Song (Live)" -> "song"), used to recognise the same track.</summary>
    private static string CoreTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var text = title.Trim();
        var cut = text.IndexOfAny(['(', '（', '[', '【']);
        if (cut > 0)
        {
            text = text[..cut];
        }

        return text.Trim().ToLowerInvariant();
    }

    /// <summary>Monotonic playback position shared by every media mapping (progress, lyrics, karaoke).</summary>
    /// <summary>Monotonic playback position shared by every media mapping (progress, lyrics, karaoke).</summary>
    private readonly FluidBar.Island.MediaPositionClock _positionClock = new();

    private string? _positionTrackKey;
    private TimeSpan _positionSample = TimeSpan.MinValue;
    private DateTimeOffset _positionSampleAt = DateTimeOffset.MinValue;
    private long _lastRereadAtMs = -1;
    private long _lastVerifyDumpMs = -1;

    /// <summary>
    /// End-to-end verification record: what the platform reported versus what the island publishes
    /// (artwork presence/hash included), so cover, play-state and progress mismatches can be attributed
    /// after a real playback session.
    /// </summary>
    private void WriteVerifyRecord(IslandContent? content, MediaSnapshot? media)
    {
        if (media is null)
        {
            return;
        }

        var nowMs = (long)(_time.GetTimestamp() * 1000.0 / _time.TimestampFrequency);
        if (_lastVerifyDumpMs >= 0 && nowMs - _lastVerifyDumpMs < 100)
        {
            return;
        }

        _lastVerifyDumpMs = nowMs;
        try
        {
            var directory = Environment.GetEnvironmentVariable("FLUIDBAR_VERIFY_DUMP");
            if (string.IsNullOrEmpty(directory))
            {
                return;
            }

            Directory.CreateDirectory(directory);
            var artwork = _artworkCache.TryGet(media.TrackKey, out var entry) ? entry?.Data : null;
            var hash = artwork is { Length: > 0 }
                ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(artwork), 0, 6)
                : null;
            var record = new
            {
                at = DateTimeOffset.UtcNow,
                trackKey = media.TrackKey,
                title = media.Title,
                artist = media.Artist,
                channel = media.TrackKey.StartsWith("win:", StringComparison.Ordinal) ? "msaa-fallback" : "gsmtc",
                hasArtwork = artwork is { Length: > 0 },
                artworkHash = hash,
                artworkBytes = artwork?.Length ?? 0,
                platformPlaying = media.IsPlaying,
                platformPositionMs = media.Position.TotalMilliseconds,
                platformDurationMs = media.Duration.TotalMilliseconds,
                publishedPlaying = _publishedPlaying,
                publishedPositionMs = _positionClock.PositionAt(nowMs),
                mappedPositionMs = content?.Media?.PositionMs ?? -1,
                mappedDurationMs = content?.Media?.DurationMs ?? -1,
                mappedProgress = content?.Media?.ProgressPercent ?? -1,
                optimisticPending = _optimisticPlay.IsPending,
            };
            File.AppendAllText(Path.Combine(directory, "content.jsonl"),
                System.Text.Json.JsonSerializer.Serialize(record) + "\n");
        }
        catch (Exception)
        {
        }
    }

    /// <summary>
    /// Playback position at "now".
    /// </summary>
    /// <remarks>
    /// The source publishes a position once per poll (and the accessibility fallback sometimes not at all
    /// once the player hides its time label). Anchoring the sample and extrapolating with the monotonic
    /// clock keeps the progress bar and lyric sweep moving in between; the value is bounded by the track
    /// length and a new sample eases out any drift instead of jumping the picture backwards.
    /// </remarks>
    private TimeSpan ResolvePosition(MediaSnapshot media, TimeSpan duration, bool isPlaying)
    {
        var nowMs = _time.GetTimestamp() * 1000.0 / _time.TimestampFrequency;
        if (!string.Equals(_positionTrackKey, media.TrackKey, StringComparison.Ordinal))
        {
            _positionTrackKey = media.TrackKey;
            _positionClock.Reset();
            _positionSample = TimeSpan.MinValue;
            _positionSampleAt = DateTimeOffset.MinValue;
        }

        _positionClock.SetLimit(duration.TotalMilliseconds);
        var freshSample = media.Position != _positionSample || media.PositionUpdatedAtUtc != _positionSampleAt;
        if (freshSample)
        {
            _positionSample = media.Position;
            _positionSampleAt = media.PositionUpdatedAtUtc;
            // The sample carries the instant it was taken. Anchoring it at "now" adds the transport delay
            // (platform poll + dispatch) to every frame, which is what kept the island a second or two
            // ahead of the player. Age the sample forward instead.
            var sampleAgeMs = media.PositionUpdatedAtUtc > DateTimeOffset.MinValue
                ? Math.Clamp((DateTimeOffset.UtcNow - media.PositionUpdatedAtUtc).TotalMilliseconds, 0, 5000)
                : 0;
            var sampledPositionMs = media.Position.TotalMilliseconds + (isPlaying ? sampleAgeMs : 0);
            _positionClock.Reconcile(sampledPositionMs, nowMs, isPlaying);
        }
        else
        {
            // No new sample: keep the play state in step and let the clock carry the position forward.
            _positionClock.SetPlaying(isPlaying, nowMs);
        }

        return TimeSpan.FromMilliseconds(_positionClock.PositionAt(nowMs));
    }

    /// <summary>Asks the media service to re-read its source (rate-limited to once every two seconds).</summary>
    /// <param name="force">Ignore the rate limit (a user tap must always be confirmed promptly).</param>
    private void RequestSourceReread(bool force = false)
    {
        var nowMs = (long)(_time.GetTimestamp() * 1000.0 / _time.TimestampFrequency);
        if (!force && _lastRereadAtMs >= 0 && nowMs - _lastRereadAtMs < 2000)
        {
            return;
        }

        _lastRereadAtMs = nowMs;
        _logger.Debug(LogCategories.Platform, "island.actions.media-reread",
            "The optimistic play state expired without confirmation; re-reading the media source.");
        _ = RunAsync(() => _media.RefreshAsync(), "island.actions.media-reread");
    }

    private ClipboardItemData MapClipboard(ClipboardEntry entry)
    {
        var preview = ClipboardPreview(entry);
        return new ClipboardItemData
        {
            Preview = preview,
            Meta = RelativeTime(entry.CapturedAtUtc),
            // Pin identity is a content hash, never the preview text: previews collide and must not
            // persist clipboard contents on disk.
            Pinned = _pinnedClipboard.Contains(PinKey(entry)),
            IsImage = entry.Kind == ClipboardContentKind.Image,
            IsFiles = entry.Kind == ClipboardContentKind.Files,
            ImagePng = entry.ImagePng,
            ImageKey = entry.ImagePng is { Length: > 0 } ? $"clip:{entry.Sequence}" : null,
        };
    }

    /// <summary>
    /// Stable, privacy-preserving pin identity. Only a SHA-256 digest is persisted; clipboard text and
    /// file paths never leave the process. Image hashes are cached by sequence because a 4K DIB is
    /// tens of megabytes and the page snapshot is rebuilt on every change.
    /// </summary>
    private string PinKey(ClipboardEntry entry)
    {
        if (_pinKeyCache.TryGetValue(entry.Sequence, out var cached))
        {
            return cached;
        }

        var payload = entry.Kind switch
        {
            ClipboardContentKind.Text => entry.Text ?? string.Empty,
            ClipboardContentKind.Files => string.Join('\u001F', entry.FilePaths),
            ClipboardContentKind.Image => entry.ImageDib is { Length: > 0 } dib
                ? $"{dib.Length}:{Convert.ToHexString(SHA256.HashData(dib))}"
                : string.Empty,
            _ => string.Empty,
        };
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{(int)entry.Kind}:{payload}")));
        if (_pinKeyCache.Count > 256)
        {
            _pinKeyCache.Clear();
        }

        _pinKeyCache[entry.Sequence] = key;
        return key;
    }

    private void LoadClipboardPins()
    {
        try
        {
            if (!File.Exists(_clipboardPinsPath))
            {
                return;
            }

            var keys = JsonSerializer.Deserialize<string[]>(File.ReadAllText(_clipboardPinsPath));
            if (keys is null)
            {
                return;
            }

            foreach (var key in keys)
            {
                if (!string.IsNullOrWhiteSpace(key))
                {
                    _pinnedClipboard.Add(key);
                }
            }
        }
        catch (Exception ex)
        {
            // A corrupt/old pin file must never prevent the island from starting.
            _logger.Warning(LogCategories.Platform, "island.clipboard-pins.load-failed",
                "Could not load clipboard pins.", ex);
        }
    }

    private void SaveClipboardPins()
    {
        try
        {
            var directory = Path.GetDirectoryName(_clipboardPinsPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_clipboardPinsPath, JsonSerializer.Serialize(_pinnedClipboard.ToArray()));
        }
        catch (Exception ex)
        {
            _logger.Warning(LogCategories.Platform, "island.clipboard-pins.save-failed",
                "Could not save clipboard pins.", ex);
        }
    }

    private static string ClipboardPreview(ClipboardEntry entry)
    {
        var preview = entry.Kind switch
        {
            ClipboardContentKind.Text when !string.IsNullOrWhiteSpace(entry.Text) => entry.Text!,
            ClipboardContentKind.Files => string.Join(", ", entry.FilePaths.Take(3)),
            ClipboardContentKind.Image => "Image",
            _ => "(empty)",
        };
        return preview.ReplaceLineEndings(" ").Trim();
    }

    private IslandActionResult ToggleClipboardPin(int index)
    {
        var history = DisplayedClipboard();
        if (index < 0 || index >= history.Count)
        {
            return Unsupported("the clipboard row no longer exists");
        }

        var key = PinKey(history[index]);
        if (!_pinnedClipboard.Add(key))
        {
            _pinnedClipboard.Remove(key);
        }
        SaveClipboardPins();

        Changed?.Invoke(this, EventArgs.Empty);
        return IslandActionResult.Handled;
    }

    private static string FormatTime(TimeSpan value)
        => value.TotalHours >= 1 ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}" : $"{value.Minutes}:{value.Seconds:00}";

    private string RelativeTime(DateTimeOffset captured)
    {
        var delta = _time.GetUtcNow() - captured;
        if (delta.TotalSeconds < 60) return "now";
        if (delta.TotalMinutes < 60) return $"{(int)delta.TotalMinutes} min";
        if (delta.TotalHours < 24) return $"{(int)delta.TotalHours} h";
        return $"{(int)delta.TotalDays} d";
    }

    /// <summary>Moves the widgets calendar by one month and republishes the island content.</summary>
    private IslandActionResult NavigateCalendar(int delta)
    {
        _calendarMonth = _calendarMonth.AddMonths(delta);
        Changed?.Invoke(this, EventArgs.Empty);
        return IslandActionResult.Handled;
    }

    /// <summary>Culture-aware month header ("2026年10月" for zh, "October 2026" otherwise).</summary>
    private static string FormatMonthTitle(DateOnly month, CultureInfo culture)
    {
        var date = month.ToDateTime(TimeOnly.MinValue);
        return culture.TwoLetterISOLanguageName == "zh"
            ? date.ToString("yyyy年M月", culture)
            : date.ToString("MMMM yyyy", culture);
    }

    /// <summary>Seven weekday headers, Monday first (the calendar grid is Monday-first).</summary>
    private static ImmutableArray<string> BuildWeekdayHeaders(CultureInfo culture)
    {
        var names = culture.DateTimeFormat.AbbreviatedDayNames;
        var builder = ImmutableArray.CreateBuilder<string>(7);
        for (var i = 0; i < 7; i++)
        {
            builder.Add(names[((int)DayOfWeek.Monday + i) % 7]);
        }

        return builder.ToImmutable();
    }

    /// <summary>42 Monday-first calendar cells for a month (current day highlighted, no event source yet).</summary>
    private static ImmutableArray<CalendarDayData> BuildCalendarDays(
        DateOnly month, DateOnly today, IReadOnlyDictionary<DateOnly, int>? eventCounts = null)
    {
        var first = month.ToDateTime(TimeOnly.MinValue);
        var offset = ((int)first.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        var start = first.AddDays(-offset);
        var todayDate = today.ToDateTime(TimeOnly.MinValue);
        var builder = ImmutableArray.CreateBuilder<CalendarDayData>(42);
        for (var i = 0; i < 42; i++)
        {
            var day = start.AddDays(i);
            builder.Add(new CalendarDayData
            {
                Day = day.Day,
                InMonth = day.Month == month.Month && day.Year == month.Year,
                IsToday = day.Date == todayDate,
                HasEvents = eventCounts?.ContainsKey(DateOnly.FromDateTime(day)) == true,
            });
        }

        return builder.ToImmutable();
    }

    /// <summary>Whether the best-effort start has been kicked off.</summary>
    public bool IsStarted => Volatile.Read(ref _started) != 0;

    /// <summary>Starts the platform services on the thread pool (never blocks the UI thread).</summary>
    public void StartInBackground() => _ = StartSafelyAsync();

    /// <summary>Starts the message host and each control service; individual failures are logged, not thrown.</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        await StartOneAsync("message-host", () => _messageHost.StartAsync(cancellationToken)).ConfigureAwait(false);
        await StartOneAsync("volume", () => _volume.StartAsync(cancellationToken)).ConfigureAwait(false);
        await StartOneAsync("battery", () => _battery.StartAsync(cancellationToken)).ConfigureAwait(false);
        await StartOneAsync("brightness", () => _brightness.StartAsync(cancellationToken)).ConfigureAwait(false);
        await StartOneAsync("network", () => _network.StartAsync(cancellationToken)).ConfigureAwait(false);
        await StartOneAsync("wifi-radio", () => _wifi.StartAsync(cancellationToken)).ConfigureAwait(false);
        await StartOneAsync("bluetooth-radio", () => _bluetooth.StartAsync(cancellationToken)).ConfigureAwait(false);
        await StartOneAsync("focus-assist", () => _focus.StartAsync(cancellationToken)).ConfigureAwait(false);
        await StartOneAsync("media", () => _media.StartAsync(cancellationToken)).ConfigureAwait(false);
        await StartOneAsync("clipboard", () => _clipboard.StartAsync(cancellationToken)).ConfigureAwait(false);
        await StartOneAsync("usb-devices", () => _usbDevices.StartAsync(cancellationToken)).ConfigureAwait(false);
        await StartOneAsync("bluetooth-devices", () => _bluetoothDevices.StartAsync(cancellationToken)).ConfigureAwait(false);
        await StartOneAsync("agent-inbox", () => _agentInbox.StartAsync(cancellationToken)).ConfigureAwait(false);
        await StartOneAsync("agent-native", () => _agentNative.StartAsync(cancellationToken)).ConfigureAwait(false);
        await StartOneAsync("notifications", () => _notifications.StartAsync(cancellationToken)).ConfigureAwait(false);
        await StartOneAsync("weather", () => _weather.StartAsync(cancellationToken)).ConfigureAwait(false);
        await StartOneAsync("tasks", () => _tasks.StartAsync(cancellationToken)).ConfigureAwait(false);
        await StartOneAsync("calendar-events", () => _calendarEvents.StartAsync(cancellationToken)).ConfigureAwait(false);
        _systemStats.Prime();
        ScheduleClockTick();
    }

    /// <summary>
    /// Arms the minute boundary tick that refreshes the idle clock. Nothing else raises
    /// <see cref="Changed"/> while the island is idle, so without it the compact pill keeps the time
    /// it was first painted with.
    /// </summary>
    private void ScheduleClockTick()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        var now = _time.GetLocalNow();
        var delay = TimeSpan.FromSeconds(60 - now.Second) - TimeSpan.FromMilliseconds(now.Millisecond);
        if (delay < TimeSpan.FromSeconds(1))
        {
            delay = TimeSpan.FromSeconds(1);
        }

        _clockTimer?.Dispose();
        _clockTimer = _time.CreateTimer(
            static state => ((SkiaPlatformActions)state!).OnClockTick(),
            this,
            delay,
            Timeout.InfiniteTimeSpan);
    }

    private void OnClockTick()
    {
        if (Volatile.Read(ref _disposed) != 0 || Volatile.Read(ref _started) == 0)
        {
            return;
        }

        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.Warning(LogCategories.Platform, "island.actions.clock-tick-failed",
                "Refreshing the island clock failed.", ex);
        }

        ScheduleClockTick();
    }

    // Optimistic play/pause: a fallback player (KuGou) reports its glyph state through the accessibility
    // poll, so waiting for it made the island icon lag the tap by up to a poll period. The toggle flips
    // the published state immediately; the real state wins as soon as it agrees (or after 2 s), and a
    // refused command rolls the flip back instead of leaving a lie on screen.
    private readonly OptimisticPlayState _optimisticPlay = new();

    /// <summary>Play state the island currently shows (the toggle flips this when the source has no snapshot).</summary>
    private bool _publishedPlaying;

    /// <inheritdoc />
    public IslandActionResult Invoke(in IslandPlatformAction action)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return IslandActionResult.Unsupported("the platform action seam has been disposed");
        }

        return action.Kind switch
        {
            IslandPlatformActionKind.MediaTogglePlay => DispatchMedia(
                () => _media.TogglePlayPauseAsync(), "island.actions.media-toggle",
                optimisticPlaying: !(_media.Current is { IsEmpty: false } current ? current.IsPlaying : _publishedPlaying)),
            IslandPlatformActionKind.MediaPrevious => DispatchMedia(
                () => _media.PreviousAsync(), "island.actions.media-previous"),
            IslandPlatformActionKind.MediaNext => DispatchMedia(
                () => _media.NextAsync(), "island.actions.media-next"),
            IslandPlatformActionKind.MediaSeek => Seek(action.Value),
            IslandPlatformActionKind.VolumeSet => SetVolume(action.Value),
            IslandPlatformActionKind.VolumeMute => ToggleVolumeMute(),
            IslandPlatformActionKind.BrightnessSet => SetBrightness(action.Value),
            IslandPlatformActionKind.QuickToggle => Toggle(action.Toggle),
            IslandPlatformActionKind.MediaOutput => OpenSoundSettings(),
            IslandPlatformActionKind.MediaQueue => OpenMediaPlayer(),
            IslandPlatformActionKind.NotificationActivate => ActivateNotification(action.Index),
            IslandPlatformActionKind.NotificationDismiss => DismissNotification(action.Index),
            IslandPlatformActionKind.NotificationCopyCode => CopyNotificationCode(action),
            IslandPlatformActionKind.NotificationMarkAllRead => MarkAllNotificationsRead(),
            IslandPlatformActionKind.NotificationClearAll => ClearNotifications(),
            IslandPlatformActionKind.ClipboardCopy => CopyClipboard(action),
            IslandPlatformActionKind.ClipboardPin => ToggleClipboardPin(action.Index),
            IslandPlatformActionKind.ClipboardClearAll => ClearClipboard(),
            IslandPlatformActionKind.ClipboardRemove => RemoveClipboard(action.Index),
            IslandPlatformActionKind.ClipboardCycleFilter => CycleClipboardFilter(),
            IslandPlatformActionKind.ClipboardSearch => RequestClipboardSearch(),
            IslandPlatformActionKind.AgentActivate => ActivateAgent(action.Index),
            IslandPlatformActionKind.TaskToggle => ToggleTask(action.Index),
            IslandPlatformActionKind.CalendarNavigate => NavigateCalendar(action.Value < 0 ? -1 : 1),
            _ => Unsupported($"unknown island action {action.Kind}"),
        };
    }

    private IslandActionResult DispatchMedia(Func<Task<bool>> command, string eventId, bool? optimisticPlaying = null)
    {
        if (_media.State != PlatformMonitorState.Running)
        {
            return Unsupported("GsmtcMediaSessionService is not running (no media session source)");
        }

        // An empty snapshot is not "no media": on the window-title fallback the island's content comes
        // from the plugin lane, and refusing the command there made the tap do nothing at all.
        if (optimisticPlaying is { } optimistic)
        {
            SetOptimisticPlaying(optimistic);
        }

        _ = RunAsync(async () =>
        {
            var ok = await command().ConfigureAwait(false);
            if (ok || optimisticPlaying is null)
            {
                return;
            }

            if (_media.CurrentTransportRoute.Route == TransportRoute.MediaKey)
            {
                // A system media key cannot report whether the player acted on it: rolling the flip back
                // here is what made the icon snap back to "playing" after the user had paused. Trust the
                // intent and let the two-consistent-reads rule (or the hold window) settle it.
                _logger.Information(LogCategories.Platform, "media.play-state",
                    "Media-key transport could not confirm the command; keeping the optimistic flip.");
                return;
            }

            ClearOptimisticPlaying();
            _logger.Warning(LogCategories.Platform, eventId,
                "The media transport command was refused; the optimistic play state was rolled back.");
        }, eventId);
        return IslandActionResult.Handled;
    }

    private void SetOptimisticPlaying(bool playing)
    {
        _optimisticPlay.Set(playing, _time.GetUtcNow());
        Changed?.Invoke(this, EventArgs.Empty);

        // Confirm early instead of waiting for the next poll: the first accessibility read after the tap
        // is usually the pre-tap glyph, and re-reading now means the source can agree (or disagree) while
        // the flip is still inside its stale-read guard.
        _ = ConfirmSoonAsync();
    }

    private async Task ConfirmSoonAsync()
    {
        try
        {
            await Task.Delay(900).ConfigureAwait(false);
            RequestSourceReread(force: true);
        }
        catch (Exception)
        {
        }
    }

    private void ClearOptimisticPlaying()
    {
        if (!_optimisticPlay.IsPending)
        {
            return;
        }

        _optimisticPlay.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private IslandActionResult Seek(double fraction)
    {
        if (_media.State != PlatformMonitorState.Running)
        {
            return Unsupported("GsmtcMediaSessionService is not running (no media session source)");
        }

        var snapshot = _media.Current;
        if (snapshot is null || snapshot.IsEmpty)
        {
            return Unsupported(
                "no active GSMTC media session; seek fallback: the slider is ignored until a session exists");
        }

        if (!snapshot.CanSeek)
        {
            return Unsupported("the active media session reports CanSeek=false; seek fallback: the slider is ignored");
        }

        if (snapshot.Duration <= TimeSpan.Zero)
        {
            return Unsupported(
                "the active media session has no known duration; seek fallback: a fraction cannot be converted to a position");
        }

        var position = TimeSpan.FromTicks((long)Math.Round(snapshot.Duration.Ticks * Math.Clamp(fraction, 0, 1)));
        _ = RunAsync(() => _media.SeekAsync(position), "island.actions.media-seek");
        return IslandActionResult.Handled;
    }

    private IslandActionResult SetVolume(double fraction)
    {
        if (_volume.State != PlatformMonitorState.Running)
        {
            return Unsupported("VolumeMonitor is not running (no default audio endpoint)");
        }

        var percent = ToPercent(fraction);
        _ = RunAsync(() => _volume.SetVolumeAsync(percent), "island.actions.volume-set");
        return IslandActionResult.Handled;
    }

    /// <summary>
    /// Toggles the default endpoint mute through <c>IAudioEndpointVolume.SetMute</c> (WASAPI). The
    /// media footer speaker used to be a painted glyph only; this is the action its hit target and
    /// UIA Invoke pattern now dispatch.
    /// </summary>
    private IslandActionResult ToggleVolumeMute()
    {
        if (_volume.State != PlatformMonitorState.Running)
        {
            return Unsupported("VolumeMonitor is not running (no default audio endpoint)");
        }

        var snapshot = _volume.Current;
        if (snapshot is null || !snapshot.HasDevice)
        {
            return Unsupported("no default audio endpoint is available for mute control");
        }

        var target = !snapshot.IsMuted;
        _ = RunAsync(() => _volume.SetMuteAsync(target), "island.actions.volume-mute");
        return IslandActionResult.Handled;
    }

    private IslandActionResult SetBrightness(double fraction)
    {
        if (_brightness.State != PlatformMonitorState.Running)
        {
            return Unsupported("BrightnessMonitor is not running (no DDC/CI or WMI provider)");
        }

        var percent = ToPercent(fraction);
        _ = RunAsync(() => _brightness.SetPercentAsync(percent), "island.actions.brightness-set");
        return IslandActionResult.Handled;
    }

    private IslandActionResult Toggle(IslandQuickToggle toggle)
    {
        switch (toggle)
        {
            case IslandQuickToggle.Wifi:
                return ToggleRadio(_wifi, "Wi-Fi", "island.actions.wifi-toggle");
            case IslandQuickToggle.Bluetooth:
                return ToggleRadio(_bluetooth, "Bluetooth", "island.actions.bluetooth-toggle");
            case IslandQuickToggle.Theme:
                try
                {
                    var next = _settings.Current.Theme == AppTheme.Light ? AppTheme.Dark : AppTheme.Light;
                    _settings.Update(settings => settings with { Theme = next });
                    return IslandActionResult.Handled;
                }
                catch (ObjectDisposedException)
                {
                    return Unsupported("SettingsController is disposed; the theme cannot be toggled");
                }

            default:
                return Unsupported($"unknown quick toggle {toggle}");
        }
    }

    /// <summary>
    /// Flips one radio through its platform service. The outcome is decided from the last snapshot
    /// (never by pretending the radio exists): absent hardware, a hardware switch or denied access
    /// reports <c>control-unsupported</c> with the exact reason before any call is dispatched.
    /// </summary>
    private IslandActionResult ToggleRadio(IRadioToggleService service, string label, string eventId)
    {
        if (service.State != PlatformMonitorState.Running)
        {
            return Unsupported($"{label} radio service is not running (it failed to start or was stopped)");
        }

        var snapshot = service.Current;
        if (snapshot is null)
        {
            return Unsupported($"{label} radio state is not available yet");
        }

        if (!snapshot.CanToggle)
        {
            return Unsupported(snapshot.Reason ?? $"{label} radio is unavailable ({snapshot.Availability})");
        }

        var target = !snapshot.IsEnabled;
        _ = RunAsync(() => service.SetEnabledAsync(target), eventId);
        return IslandActionResult.Handled;
    }

    /// <summary>
    /// Flips the Windows focus session through its platform service. The documented write API is a
    /// Limited Access Feature; when it is locked the service snapshot carries
    /// <see cref="FocusAssistAvailability.LimitedAccessDenied"/> and the toggle reports that exact
    /// reason instead of claiming success.
    /// </summary>
    private IslandActionResult ToggleFocus()
    {
        if (_focus.State != PlatformMonitorState.Running)
        {
            return Unsupported("FocusAssistService is not running (it failed to start or was stopped)");
        }

        var snapshot = _focus.Current;
        if (snapshot is null)
        {
            return Unsupported("Windows focus state is not available yet");
        }

        if (!snapshot.CanToggle)
        {
            return Unsupported(snapshot.Reason ?? $"Windows focus sessions are unavailable ({snapshot.Availability})");
        }

        var target = !snapshot.IsActive;
        _ = RunAsync(() => _focus.SetActiveAsync(target), "island.actions.focus-toggle");
        return IslandActionResult.Handled;
    }

    /// <summary>Maps the bounded agent inbox snapshot into island page rows (newest first).</summary>
    private ImmutableArray<AgentItemData> MapAgents(AgentEventSnapshot? snapshot)
        => AgentContentMapper.Map(snapshot, _time.GetLocalNow(), _localizer);

    private string Localized(string key, string fallback) => _localizer?.Get(key) ?? fallback;

    /// <summary>
    /// User-facing network label: the connected Wi-Fi SSID (WlanAPI), the wired adapter name, or the
    /// localized disconnected state. Null while the monitor has not published its first snapshot.
    /// </summary>
    private string? NetworkDisplayName(NetworkSnapshot? network)
        => NetworkAdapterSelector.DisplayName(
            network, Localized(LocKeys.Common.Disconnected, "Disconnected"));

    /// <summary>
    /// PowerShell hook command written by the explicit installer: it appends one JSON event file to the
    /// inbox so the island can show the agent session without any polling.
    /// </summary>
    private static string BuildAgentHookCommand(string inboxDirectory)
        => "pwsh -NoProfile -Command \"$j=@{schemaVersion=1;agent='claude-code';event='Stop';title='Claude Code';timestamp=(Get-Date).ToString('o')}|ConvertTo-Json -Compress;Set-Content -LiteralPath (Join-Path '" + inboxDirectory + "' ('claude-'+[guid]::NewGuid().ToString('N')+'.json')) -Value $j\"";

    /// <summary>
    /// Copies a notification's detected verification code to the clipboard. The code travels in
    /// <see cref="IslandPlatformAction.Text"/> so the platform seam never needs the toast snapshot.
    /// </summary>
    private IslandActionResult MarkAllNotificationsRead()
    {
        _notifications.MarkAllRead();
        return IslandActionResult.Handled;
    }

    private IslandActionResult ActivateAgent(int index)
    {
        var agents = MapAgents(_agentSources.Current);
        if (index < 0 || index >= agents.Length)
        {
            return Unsupported("the agent row no longer exists");
        }

        var agent = agents[index];
        return AgentWindowFocus.TryActivate(agent.SessionTitle, agent.Project)
            ? IslandActionResult.Handled
            : Defer(new IslandPlatformAction(IslandPlatformActionKind.AgentActivate, index),
                "no unique agent terminal window matched the session title or project");
    }

    private IslandActionResult OpenMediaPlayer()
    {
        var aumid = _media.Current?.SourceAppUserModelId;
        if (string.IsNullOrWhiteSpace(aumid))
        {
            return Unsupported("there is no active media session to open");
        }

        try
        {
            Process.Start(new ProcessStartInfo($"shell:AppsFolder\\{aumid}") { UseShellExecute = true });
            return IslandActionResult.Handled;
        }
        catch
        {
            return Unsupported("the current media app could not be launched");
        }
    }

    private IslandActionResult OpenSoundSettings()
    {
        try
        {
            // Windows has no public per-app output picker API here. The system Sound settings page is
            // the honest fallback: it is where the user can actually change the default endpoint.
            Process.Start(new ProcessStartInfo("ms-settings:sound") { UseShellExecute = true });
            return IslandActionResult.Handled;
        }
        catch
        {
            return Unsupported("could not open the Windows sound settings page");
        }
    }

    private IslandActionResult ClearNotifications()
    {
        _notifications.ClearRecent();
        return IslandActionResult.Handled;
    }

    private IslandActionResult CycleClipboardFilter()
    {
        _clipboardFilter = _clipboardFilter switch
        {
            ClipboardFilter.All => ClipboardFilter.Text,
            ClipboardFilter.Text => ClipboardFilter.Image,
            ClipboardFilter.Image => ClipboardFilter.Files,
            _ => ClipboardFilter.All,
        };
        Changed?.Invoke(this, EventArgs.Empty);
        return IslandActionResult.Handled;
    }

    private string ClipboardFilterLabel() => _localizer?.Get(_clipboardFilter switch
    {
        ClipboardFilter.Text => LocKeys.Clipboard.Text,
        ClipboardFilter.Image => LocKeys.Clipboard.Image,
        ClipboardFilter.Files => LocKeys.Clipboard.File,
        _ => LocKeys.Clipboard.All,
    }) ?? _clipboardFilter.ToString();

    private void PrunePinKeyCache(IReadOnlyList<ClipboardEntry> history)
    {
        if (_pinKeyCache.Count == 0)
        {
            return;
        }

        var live = history.Select(entry => entry.Sequence).ToHashSet();
        foreach (var key in _pinKeyCache.Keys.Where(sequence => !live.Contains(sequence)).ToArray())
        {
            _pinKeyCache.Remove(key);
        }
    }

    /// <summary>Current clipboard search query (null when not searching).</summary>
    public string? ClipboardSearchQuery => _clipboardSearchQuery;

    /// <summary>Updates the text query used by the clipboard page; null/empty clears it.</summary>
    public void SetClipboardSearchQuery(string? query)
    {
        var next = string.IsNullOrWhiteSpace(query) ? null : query.Trim();
        if (string.Equals(_clipboardSearchQuery, next, StringComparison.Ordinal))
        {
            return;
        }

        _clipboardSearchQuery = next;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Pure clipboard query matcher (text content or file names, case-insensitive).</summary>
    public static bool MatchesClipboardSearch(ClipboardEntry entry, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var text = entry.Kind switch
        {
            ClipboardContentKind.Text => entry.Text,
            ClipboardContentKind.Files => string.Join(' ', entry.FilePaths),
            _ => null,
        };
        return text is not null
            && text.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private IslandActionResult RequestClipboardSearch()
    {
        ClipboardSearchRequested?.Invoke(this, EventArgs.Empty);
        return IslandActionResult.Handled;
    }

    private IslandActionResult RemoveClipboard(int index)
    {
        var history = DisplayedClipboard();
        if (index < 0 || index >= history.Count)
        {
            return Unsupported("the clipboard row no longer exists");
        }

        var entry = history[index];
        _pinnedClipboard.Remove(PinKey(entry));
        _pinKeyCache.Remove(entry.Sequence);
        SaveClipboardPins();
        return _clipboard.Remove(entry.Sequence)
            ? IslandActionResult.Handled
            : Unsupported("the clipboard row no longer exists");
    }

    private IslandActionResult ClearClipboard()
    {
        // Clearing history also clears its pins; otherwise a pinned row would come back the next
        // time the same content is copied, which makes "clear" feel ineffective.
        _pinnedClipboard.Clear();
        _pinKeyCache.Clear();
        _clipboardFilter = ClipboardFilter.All;
        SaveClipboardPins();
        _clipboard.ClearHistory();
        return IslandActionResult.Handled;
    }

    private IslandActionResult CopyNotificationCode(in IslandPlatformAction action)
    {
        if (string.IsNullOrWhiteSpace(action.Text))
        {
            return Unsupported("the notification row has no verification code to copy");
        }

        var copyText = action.Text;
        _ = RunAsync(() => _clipboard.SetTextAsync(copyText), "island.actions.notification-copy-code");
        return IslandActionResult.Handled;
    }

    /// <summary>
    /// Opens the notification's originating app/action. WinRT cannot activate another app's toast,
    /// but the toast XML carries the app's <c>launch</c> protocol URI (QQ uses a ntqq:// deep link);
    /// use it first, then fall back to the running app's executable.
    /// </summary>
    private IslandActionResult ActivateNotification(int index)
    {
        var recent = _notifications.Current?.Recent ?? ImmutableArray<NotificationEntry>.Empty;
        if (index < 0 || index >= Math.Min(PageNotificationLimit, recent.Length))
        {
            return Unsupported("the notification row no longer exists");
        }

        var entry = recent[index];
        _notifications.MarkRead(entry.Id);
        var launch = NotificationToastImageReader.TryReadLaunch(entry.Id);
        if (!string.IsNullOrWhiteSpace(launch))
        {
            try
            {
                Process.Start(new ProcessStartInfo(launch) { UseShellExecute = true });
                return IslandActionResult.Handled;
            }
            catch (Exception ex)
            {
                _logger.Warning(LogCategories.Platform, "island.actions.notification-launch-failed",
                    $"Launching notification URI '{launch}' failed; falling back to the app executable.", ex);
            }
        }

        try
        {
            foreach (var process in Process.GetProcesses())
            {
                if (!process.ProcessName.Equals(entry.AppName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var path = process.MainModule?.FileName;
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                    return IslandActionResult.Handled;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(LogCategories.Platform, "island.actions.notification-app-launch-failed",
                "Opening the notification app executable failed.", ex);
        }

        return Unsupported("the notification has no launch URI and no running app executable");
    }

    /// <summary>
    /// Removes one notification row. Windows has no public API to dismiss another app's toast, so this
    /// removes the row from the island list only; closing it in Action Center then keeps it gone (the
    /// service's close-sync prune).
    /// </summary>
    private IslandActionResult DismissNotification(int index)
    {
        var recent = _notifications.Current?.Recent ?? ImmutableArray<NotificationEntry>.Empty;
        if (index < 0 || index >= Math.Min(PageNotificationLimit, recent.Length))
        {
            return Unsupported("the notification row no longer exists");
        }

        var entry = recent[index];
        return _notifications.RemoveLocally(entry.Id)
            ? IslandActionResult.Handled
            : Unsupported("the notification is no longer in the listener list");
    }

    /// <summary>
    /// Flips one task checkbox in <c>tasks.json</c> (atomic write on the thread pool). Reports unsupported
    /// when the file is missing/invalid or the row does not exist; the card repaints from the new snapshot.
    /// </summary>
    private IslandActionResult ToggleTask(int index)
    {
        var snapshot = _tasks.Current;
        if (snapshot is null || !snapshot.IsAvailable || index < 0 || index >= Math.Min(WidgetContentMapper.MaxRenderedTasks, snapshot.Count))
        {
            return Unsupported("tasks.json has no toggleable row at that index (missing or invalid file)");
        }

        _ = RunAsync(() => _tasks.ToggleAsync(index), "island.actions.task-toggle");
        return IslandActionResult.Handled;
    }

    private IslandActionResult CopyClipboard(in IslandPlatformAction action)
    {
        if (action.Index < 0)
        {
            if (string.IsNullOrWhiteSpace(action.Text))
            {
                return Unsupported("the clipboard row has no text to copy");
            }

            var fallbackText = action.Text;
            _ = RunAsync(() => _clipboard.SetTextAsync(fallbackText), "island.actions.clipboard-copy");
            return IslandActionResult.Handled;
        }

        var history = DisplayedClipboard();
        if (action.Index >= history.Count)
        {
            return Unsupported("the clipboard row no longer exists");
        }

        var entry = history[action.Index];
        switch (entry.Kind)
        {
            case ClipboardContentKind.Text:
                if (string.IsNullOrWhiteSpace(entry.Text))
                {
                    return Unsupported("the clipboard row has no text to copy");
                }

                _ = RunAsync(() => _clipboard.SetTextAsync(entry.Text), "island.actions.clipboard-copy");
                return IslandActionResult.Handled;

            case ClipboardContentKind.Files:
                if (entry.FilePaths.Length == 0)
                {
                    return Unsupported("the clipboard file row has no paths");
                }

                _ = RunAsync(() => _clipboard.SetFilesAsync(entry.FilePaths), "island.actions.clipboard-copy-files");
                return IslandActionResult.Handled;

            case ClipboardContentKind.Image:
                if (entry.ImageDib is not { Length: > 0 })
                {
                    return Unsupported("the clipboard image has no DIB payload");
                }

                _ = RunAsync(() => _clipboard.SetImageAsync(entry.ImageDib), "island.actions.clipboard-copy-image");
                return IslandActionResult.Handled;

            default:
                return Unsupported("the clipboard row has no copyable payload");
        }
    }

    /// <summary>Rows exactly as painted: pinned first, then newest; capped at the page limit.</summary>
    private IReadOnlyList<ClipboardEntry> DisplayedClipboard()
        => _clipboard.GetHistory()
            .Where(entry => MatchesClipboardSearch(entry, _clipboardSearchQuery))
            .Where(entry => _clipboardFilter switch
            {
                ClipboardFilter.Text => entry.Kind == ClipboardContentKind.Text,
                ClipboardFilter.Image => entry.Kind == ClipboardContentKind.Image,
                ClipboardFilter.Files => entry.Kind == ClipboardContentKind.Files,
                _ => true,
            })
            .OrderByDescending(entry => _pinnedClipboard.Contains(PinKey(entry)))
            .ThenByDescending(entry => entry.CapturedAtUtc)
            .Take(8)
            .ToArray();

    private IslandActionResult Defer(in IslandPlatformAction action, string reason)
    {
        DeferredActionRequested?.Invoke(action);
        return IslandActionResult.Deferred(reason);
    }

    private static IslandActionResult Unsupported(string reason) => IslandActionResult.Unsupported(reason);

    private static int ToPercent(double fraction)
        => (int)Math.Round(Math.Clamp(double.IsFinite(fraction) ? fraction : 0, 0, 1) * 100);

    private async Task StartSafelyAsync()
    {
        try
        {
            await StartAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warning(LogCategories.Platform, "island.actions.start-failed",
                "The page-control platform services could not be started; controls will report unsupported.", ex);
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
            _logger.Warning(LogCategories.Platform, $"island.actions.{name}-start-failed",
                $"Platform service '{name}' failed to start; its controls will report unsupported.", ex);
        }
    }

    private async Task RunAsync(Func<Task> work, string eventId)
    {
        try
        {
            // Invoke runs on the UI thread; every service call is dispatched to the thread pool so no
            // COM/WinRT/registry work can ever happen on the UI thread (contract §1).
            await Task.Run(work).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warning(LogCategories.Platform, eventId + "-failed",
                "A page-control platform call failed; the island stays responsive.", ex);
        }
    }

    /// <summary>Disposes in the background (App exit is synchronous); failures are logged.</summary>
    public void DisposeInBackground() => _ = DisposeSafelyAsync();

    private async Task DisposeSafelyAsync()
    {
        try
        {
            await DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warning(LogCategories.Platform, "island.actions.dispose-failed",
                "The page-control platform services did not stop cleanly.", ex);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _settings.Changed -= OnSettingsChanged;
        _calendarEvents.Changed -= OnCalendarEventsChanged;
        await StopOneAsync("clipboard", () => _clipboard.DisposeAsync().AsTask()).ConfigureAwait(false);
        await StopOneAsync("usb-devices", () => _usbDevices.DisposeAsync().AsTask()).ConfigureAwait(false);
        await StopOneAsync("bluetooth-devices", () => _bluetoothDevices.DisposeAsync().AsTask()).ConfigureAwait(false);
        await StopOneAsync("agent-inbox", () => _agentInbox.StopAsync()).ConfigureAwait(false);
        await StopOneAsync("agent-native", () => _agentNative.StopAsync()).ConfigureAwait(false);
        _agentSources.Dispose();
        await StopOneAsync("notifications", () => _notifications.DisposeAsync().AsTask()).ConfigureAwait(false);
        await StopOneAsync("weather", () => _weather.DisposeAsync().AsTask()).ConfigureAwait(false);
        await StopOneAsync("tasks", () => _tasks.DisposeAsync().AsTask()).ConfigureAwait(false);
        await StopOneAsync("calendar-events", () => _calendarEvents.DisposeAsync().AsTask()).ConfigureAwait(false);
        await StopOneAsync("media", () => _media.DisposeAsync().AsTask()).ConfigureAwait(false);
        await StopOneAsync("focus-assist", () => _focus.DisposeAsync().AsTask()).ConfigureAwait(false);
        await StopOneAsync("bluetooth-radio", () => _bluetooth.DisposeAsync().AsTask()).ConfigureAwait(false);
        await StopOneAsync("wifi-radio", () => _wifi.DisposeAsync().AsTask()).ConfigureAwait(false);
        await StopOneAsync("network", () => _network.DisposeAsync().AsTask()).ConfigureAwait(false);
        await StopOneAsync("brightness", () => _brightness.DisposeAsync().AsTask()).ConfigureAwait(false);
        await StopOneAsync("volume", () => _volume.DisposeAsync().AsTask()).ConfigureAwait(false);
        await StopOneAsync("battery", () => _battery.DisposeAsync().AsTask()).ConfigureAwait(false);
        await StopOneAsync("message-host", () => _messageHost.DisposeAsync().AsTask()).ConfigureAwait(false);
        _clockTimer?.Dispose();
        _clockTimer = null;
        _mediaTick?.Dispose();
        _mediaTick = null;
        await StopOneAsync("lyrics", () => _lyrics.DisposeAsync().AsTask()).ConfigureAwait(false);
        _lyricsHttp.Dispose();
        _artworkCache.Dispose();
        _artworkTempStore.Dispose();
    }

    private enum ClipboardFilter
    {
        All,
        Text,
        Image,
        Files,
    }

    private async Task StopOneAsync(string name, Func<Task> stop)
    {
        try
        {
            await stop().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warning(LogCategories.Platform, $"island.actions.{name}-stop-failed",
                $"Platform service '{name}' failed to stop cleanly.", ex);
        }
    }
}