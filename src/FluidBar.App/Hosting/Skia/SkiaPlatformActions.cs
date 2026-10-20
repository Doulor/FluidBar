using System.Globalization;
using FluidBar.App.Island;
using FluidBar.App.Localization;
using FluidBar.App.Settings;
using FluidBar.App.State;
using FluidBar.Core.Diagnostics;
using System.Collections.Immutable;
using FluidBar.Platform.Windows.Agent;
using FluidBar.Platform.Windows.Clipboard;
using FluidBar.Platform.Windows.CoreIntegration;
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
    private readonly IPlatformLogger _platformLogger;
    private readonly PlatformCounters _counters;
    private readonly PlatformMessageHost _messageHost;
    private readonly VolumeMonitor _volume;
    private readonly BatteryMonitor _battery;
    private readonly BrightnessMonitor _brightness;
    private readonly NetworkMonitor _network;
    private readonly GsmtcMediaSessionService _media;
    private readonly ClipboardService _clipboard;
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
    private readonly HashSet<string> _pinnedClipboard = new(StringComparer.Ordinal);
    private bool _weatherCityLockedByEnvironment;
    private INotificationSource? _pluginNotificationSource;
    private DateOnly _calendarMonth;
    private ITimer? _clockTimer;
    private ITimer? _mediaTick;
    private LyricsDocument? _lyricsDocument;
    private string _lyricsTrackKey = string.Empty;
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
        string? weatherCity = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _localizer = localizer;
        _logger = logger ?? NullAppLogger.Instance;
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
        _media = new GsmtcMediaSessionService(_platformLogger, _counters, _artworkCache, time);
        _clipboard = new ClipboardService(_messageHost, _platformLogger, _counters, null, time);

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

    /// <inheritdoc />
    public IslandContent Snapshot()
    {
        var battery = _battery.Current;
        var focus = _focus.Current;
        var network = _network.Current;
        var media = _media.Current;
        var volume = _volume.Current;
        var brightness = _brightness.Current;
        var clipboard = _clipboard.Current?.History ?? ImmutableArray<ClipboardEntry>.Empty;
        var now = _time.GetLocalNow();
        var culture = _localizer?.CurrentCulture ?? CultureInfo.CurrentCulture;
        int? batteryPercent = battery is { HasBattery: true } ? battery.Percent : null;
        var today = DateOnly.FromDateTime(now.DateTime);
        // Widgets page: real weather (fresh or last-known cached) and the local task file. Both mappers
        // return explicit empty values when nothing real exists, so the cards never show invented data.
        var weather = WidgetContentMapper.MapWeather(_weather.Current, now, _localizer);
        var tasks = WidgetContentMapper.MapTasks(_tasks.Current, _localizer, _tasks.FilePath);
        return new IslandContent
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
            FocusOn = focus?.IsActive ?? false,
            FocusAvailable = focus?.CanToggle ?? false,
            VolumePercent = volume is { HasDevice: true } ? volume.Percent : null,
            IsMuted = volume?.IsMuted ?? false,
            BrightnessPercent = brightness is { IsSupported: true } ? brightness.Percent : null,
            Media = media is { IsEmpty: false } ? MapMedia(media) : null,
            Clipboard = clipboard.Take(8).Select(MapClipboard).ToImmutableArray(),
            // Real Windows toast listener (see the ctor): seeded with the current list, kept in sync by
            // arrival and by the user closing a toast in Action Center.
            Notifications = NotificationContentMapper.Map(
                _notifications.Current, PageNotificationLimit, now, _localizer),
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
            CalendarDays = BuildCalendarDays(_calendarMonth, today),
            Stats = _systemStats.Build(batteryPercent, Localized(LocKeys.Battery.Title, "Battery")),
            UnreadCount = Math.Clamp(_notifications.Current?.Recent.Length ?? 0, 0, 99),
        };
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
    private void OnWidgetDataChanged<T>(object? sender, SnapshotEventArgs<T> args) where T : class, IEquatable<T>
    {
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
        if (playing)
        {
            _mediaTick ??= _time.CreateTimer(
                static state => ((SkiaPlatformActions)state!).OnMediaTick(),
                this,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(1));
            return;
        }

        _mediaTick?.Dispose();
        _mediaTick = null;
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
            return;
        }

        _lyricsTrackKey = media.TrackKey;
        _lyricsDocument = null;

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
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            // Lyrics are a nicety: a provider outage must never surface as a broken island.
            _logger.Debug(LogCategories.Platform, "island.actions.lyrics-failed",
                "Lyrics lookup failed for the current track: " + ex.Message);
        }
    }

    private (string? Current, string? Next, double Highlight, FluidBar.Island.KaraokeTrack? Karaoke) ResolveLyrics(
        MediaSnapshot media, TimeSpan position)
    {
        var document = _lyricsDocument;
        if (document is null || document.IsEmpty
            || !string.Equals(_lyricsTrackKey, media.TrackKey, StringComparison.Ordinal))
        {
            return (null, null, -1, null);
        }

        // SelectDetailed also returns the karaoke sweep of the current line (KRC word timeline).
        var selection = LyricsTimelineSelector.SelectDetailed(document, position, media.Title, media.Artist);
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

        // Optimistic play state (see SetOptimisticPlaying): until the source agrees, publish the flip.
        var isPlaying = _optimisticPlay.Resolve(media.IsPlaying, _time.GetUtcNow());
        _publishedPlaying = isPlaying;

        // Interpolate from the last sampled position: the platform layer has no frame clock, so
        // between GSMTC events the position only advances because we add the elapsed wall time.
        var position = media.Position;
        if (isPlaying && media.PositionUpdatedAtUtc > DateTimeOffset.MinValue)
        {
            var elapsed = _time.GetUtcNow() - media.PositionUpdatedAtUtc;
            if (elapsed > TimeSpan.Zero && elapsed < TimeSpan.FromHours(1))
            {
                position += elapsed;
            }
        }

        if (position < TimeSpan.Zero)
        {
            position = TimeSpan.Zero;
        }

        if (duration > TimeSpan.Zero && position > duration)
        {
            position = duration;
        }

        var progress = duration.TotalMilliseconds > 0
            ? (int)Math.Clamp(position.TotalMilliseconds / duration.TotalMilliseconds * 100, 0, 100)
            : 0;

        var artwork = _artworkCache.TryGet(media.TrackKey, out var artworkEntry) && artworkEntry is not null
            ? artworkEntry.Data
            : null;

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

    private ClipboardItemData MapClipboard(ClipboardEntry entry)
    {
        var preview = ClipboardPreview(entry);
        return new ClipboardItemData
        {
            Preview = preview,
            Meta = RelativeTime(entry.CapturedAtUtc),
            // Pin state is App-owned (ClipboardService keeps only history), so the island remembers
            // pinned rows by preview text for this process lifetime.
            Pinned = _pinnedClipboard.Contains(preview),
            IsImage = entry.Kind == ClipboardContentKind.Image,
        };
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
        var history = _clipboard.Current?.History ?? ImmutableArray<ClipboardEntry>.Empty;
        if (index < 0 || index >= Math.Min(8, history.Length))
        {
            return Unsupported("the clipboard row no longer exists");
        }

        var preview = ClipboardPreview(history[index]);
        if (!_pinnedClipboard.Add(preview))
        {
            _pinnedClipboard.Remove(preview);
        }

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
    private static ImmutableArray<CalendarDayData> BuildCalendarDays(DateOnly month, DateOnly today)
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
                HasEvents = false,
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
        await StartOneAsync("agent-inbox", () => _agentInbox.StartAsync(cancellationToken)).ConfigureAwait(false);
        await StartOneAsync("agent-native", () => _agentNative.StartAsync(cancellationToken)).ConfigureAwait(false);
        await StartOneAsync("notifications", () => _notifications.StartAsync(cancellationToken)).ConfigureAwait(false);
        await StartOneAsync("weather", () => _weather.StartAsync(cancellationToken)).ConfigureAwait(false);
        await StartOneAsync("tasks", () => _tasks.StartAsync(cancellationToken)).ConfigureAwait(false);
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
            IslandPlatformActionKind.BrightnessSet => SetBrightness(action.Value),
            IslandPlatformActionKind.QuickToggle => Toggle(action.Toggle),
            IslandPlatformActionKind.MediaOutput => Unsupported(
                "no audio-output picker service exists (needs an IAudioOutputService over IMMDeviceEnumerator)"),
            IslandPlatformActionKind.MediaQueue => Unsupported(
                "no media queue service exists (the GSMTC snapshot exposes no queue)"),
            IslandPlatformActionKind.NotificationActivate => Defer(
                action, "no public Windows API activates another app's toast; open the toast from Action Center instead"),
            IslandPlatformActionKind.NotificationDismiss => DismissNotification(action.Index),
            IslandPlatformActionKind.ClipboardCopy => CopyClipboard(action),
            IslandPlatformActionKind.ClipboardPin => ToggleClipboardPin(action.Index),
            IslandPlatformActionKind.AgentActivate => Defer(
                action, "agent session activation is App-owned (no platform session-activate service)"),
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
            if (!ok && optimisticPlaying is not null)
            {
                ClearOptimisticPlaying();
                _logger.Warning(LogCategories.Platform, eventId,
                    "The media transport command was refused; the optimistic play state was rolled back.");
            }
        }, eventId);
        return IslandActionResult.Handled;
    }

    private void SetOptimisticPlaying(bool playing)
    {
        _optimisticPlay.Set(playing, _time.GetUtcNow());
        Changed?.Invoke(this, EventArgs.Empty);
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
            case IslandQuickToggle.Focus:
                return ToggleFocus();
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
        var text = action.Text;
        if (action.Index >= 0)
        {
            var history = _clipboard.GetHistory();
            if (action.Index < history.Count && !string.IsNullOrWhiteSpace(history[action.Index].Text))
            {
                text = history[action.Index].Text;
            }
        }
        if (string.IsNullOrWhiteSpace(text))
        {
            return Unsupported("the clipboard row has no text to copy (image/file rows are not supported)");
        }

        _ = RunAsync(() => _clipboard.SetTextAsync(text), "island.actions.clipboard-copy");
        return IslandActionResult.Handled;
    }

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
        await StopOneAsync("clipboard", () => _clipboard.DisposeAsync().AsTask()).ConfigureAwait(false);
        await StopOneAsync("agent-inbox", () => _agentInbox.StopAsync()).ConfigureAwait(false);
        await StopOneAsync("agent-native", () => _agentNative.StopAsync()).ConfigureAwait(false);
        _agentSources.Dispose();
        await StopOneAsync("notifications", () => _notifications.DisposeAsync().AsTask()).ConfigureAwait(false);
        await StopOneAsync("weather", () => _weather.DisposeAsync().AsTask()).ConfigureAwait(false);
        await StopOneAsync("tasks", () => _tasks.DisposeAsync().AsTask()).ConfigureAwait(false);
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