using System.Collections.Immutable;
using FluidBar.App.Island;
using FluidBar.App.Localization;
using FluidBar.App.Theme;
using FluidBar.Core.Presentation;

namespace FluidBar.App.State;

/// <summary>One media session snapshot shown on the compact pill and expanded media page.</summary>
public sealed record MediaSessionData
{
    /// <summary>Track title (user data, never translated).</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Artist name.</summary>
    public string Artist { get; init; } = string.Empty;

    /// <summary>Album name.</summary>
    public string Album { get; init; } = string.Empty;

    /// <summary>Source application.</summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>Progress 0..100.</summary>
    public int ProgressPercent { get; init; }

    /// <summary>Elapsed time display text.</summary>
    public string PositionText { get; init; } = "0:00";

    /// <summary>Total duration display text.</summary>
    public string DurationText { get; init; } = "0:00";

    /// <summary>
    /// Whether a real timeline (position + duration) is known. False for window-title fallback sources
    /// (players without SMTC), where the island must hide the progress bar instead of faking it.
    /// </summary>
    public bool HasTimeline { get; init; } = true;

    /// <summary>Whether playback is active.</summary>
    public bool IsPlaying { get; init; }

    /// <summary>Artwork-derived accent hex.</summary>
    public string? AccentHex { get; init; }

    /// <summary>Raw cover-art bytes (PNG/JPEG) from the platform artwork cache, or null.</summary>
    public byte[]? Artwork { get; init; }

    /// <summary>Stable key for the artwork (track identity); null when no cover is available.</summary>
    public string? ArtworkKey { get; init; }

    /// <summary>Current lyric line.</summary>
    public string? LyricLine { get; init; }

    /// <summary>Next lyric line.</summary>
    public string? NextLyricLine { get; init; }

    /// <summary>
    /// Karaoke sweep of <see cref="LyricLine"/> (0..1) from the KRC word timeline; negative when the
    /// lyric source has no per-word timing.
    /// </summary>
    public double LyricHighlight { get; init; } = -1;

    /// <summary>
    /// Per-word timeline of <see cref="LyricLine"/> so the island can advance the sweep every frame from its
    /// own extrapolated position instead of only when a new position sample arrives. Null when the lyric
    /// source has no per-word timing.
    /// </summary>
    public FluidBar.Island.KaraokeTrack? Karaoke { get; init; }

    /// <summary>Sampled playback position in milliseconds (the anchor for the island's extrapolation).</summary>
    public double PositionMs { get; init; }

    /// <summary>Track duration in milliseconds, or 0 when the source does not know it.</summary>
    public double DurationMs { get; init; }

    /// <summary>Track index for "n of m" displays, 1-based; 0 when unknown.</summary>
    public int TrackIndex { get; init; }

    /// <summary>Playlist size; 0 when unknown.</summary>
    public int TrackCount { get; init; }
}

/// <summary>One notification row on the expanded notifications page.</summary>
public sealed record NotificationItemData
{
    /// <summary>Notification title.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Body text.</summary>
    public string Body { get; init; } = string.Empty;

    /// <summary>Localized relative time.</summary>
    public string TimeText { get; init; } = string.Empty;

    /// <summary>Detected verification code.</summary>
    public string? VerificationCode { get; init; }

    /// <summary>Source application name.</summary>
    public string Source { get; init; } = string.Empty;
}

/// <summary>One clipboard history row.</summary>
public sealed record ClipboardItemData
{
    /// <summary>Text preview or image label.</summary>
    public string Preview { get; init; } = string.Empty;

    /// <summary>Localized metadata (relative time, size).</summary>
    public string Meta { get; init; } = string.Empty;

    /// <summary>Whether the item is pinned.</summary>
    public bool Pinned { get; init; }

    /// <summary>Whether the item is an image.</summary>
    public bool IsImage { get; init; }
}


/// <summary>One widget task row.</summary>
public sealed record WidgetTaskData
{
    /// <summary>Localized task title.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Whether the task is checked.</summary>
    public bool Done { get; init; }
}

/// <summary>Visual tone of a widgets system stat bar (maps onto the renderer's own tone enum).</summary>
public enum WidgetStatKind
{
    /// <summary>Secondary/neutral.</summary>
    Neutral,

    /// <summary>User accent (CPU).</summary>
    Accent,

    /// <summary>Informational blue (RAM).</summary>
    Info,

    /// <summary>Success (battery).</summary>
    Success,
}

/// <summary>One system stat row on the widgets page (CPU/RAM/battery), sampled by the platform seam.</summary>
/// <param name="Label">Short label ("CPU", "RAM", localized battery label).</param>
/// <param name="Value">Percentage 0..100.</param>
/// <param name="Kind">Bar tone.</param>
public readonly record struct WidgetStatData(string Label, int Value, WidgetStatKind Kind = WidgetStatKind.Neutral);

/// <summary>One calendar cell on the widgets page.</summary>
public sealed record CalendarDayData
{
    /// <summary>Day of month.</summary>
    public int Day { get; init; }

    /// <summary>Whether the day belongs to the displayed month.</summary>
    public bool InMonth { get; init; }

    /// <summary>Whether the day is today.</summary>
    public bool IsToday { get; init; }

    /// <summary>Whether the day has events.</summary>
    public bool HasEvents { get; init; }

    /// <summary>Whether the day is selected.</summary>
    public bool IsSelected { get; init; }
}

/// <summary>
/// All non-activity island content (system status + page data). Activities arrive through the Core
/// event hub; this snapshot carries everything that is not an activity.
/// </summary>
public sealed record IslandContent
{
    /// <summary>Snapshot time.</summary>
    public DateTimeOffset Now { get; init; } = DateTimeOffset.Now;

    /// <summary>Battery percentage 0..100, or null when unknown.</summary>
    public int? BatteryPercent { get; init; }

    /// <summary>Whether the battery is charging.</summary>
    public bool IsCharging { get; init; }

    /// <summary>Localized network name.</summary>
    public string? NetworkName { get; init; }

    /// <summary>Whether a network is connected.</summary>
    public bool IsOnline { get; init; } = true;

    /// <summary>Whether focus assist is on.</summary>
    public bool FocusOn { get; init; }

    /// <summary>Whether Windows allows writing the focus state (false = the toggle is inert).</summary>
    public bool FocusAvailable { get; init; }

    /// <summary>System output volume 0..100, or null when no audio endpoint is available.</summary>
    public int? VolumePercent { get; init; }

    /// <summary>Whether the system output volume is muted.</summary>
    public bool IsMuted { get; init; }

    /// <summary>Display brightness 0..100, or null when the platform cannot control it.</summary>
    public int? BrightnessPercent { get; init; }

    /// <summary>Localized input method label (e.g. EN / 中).</summary>
    public string? InputMethod { get; init; }

    /// <summary>Active media session, if any.</summary>
    public MediaSessionData? Media { get; init; }

    /// <summary>Notification rows, newest first.</summary>
    public ImmutableArray<NotificationItemData> Notifications { get; init; } = [];

    /// <summary>Clipboard rows, newest first.</summary>
    public ImmutableArray<ClipboardItemData> Clipboard { get; init; } = [];

    /// <summary>Widget task rows.</summary>
    public ImmutableArray<WidgetTaskData> Tasks { get; init; } = [];

    /// <summary>Widgets weather: current temperature ("18°"); null when no real reading exists.</summary>
    public string? WeatherTemp { get; init; }

    /// <summary>Widgets weather secondary line: age of the cached reading when it is stale, else null.</summary>
    public string? WeatherConditionText { get; init; }

    /// <summary>Widgets weather: today's high/low ("21° / 11°").</summary>
    public string? WeatherHighLow { get; init; }

    /// <summary>Widgets weather: wind line ("NW 12 km/h").</summary>
    public string? WeatherWind { get; init; }

    /// <summary>Widgets weather glyph chosen from the WMO code (existing icon set).</summary>
    public WidgetWeatherGlyph WeatherGlyph { get; init; } = WidgetWeatherGlyph.CloudSun;

    /// <summary>Localized caption for the task card when the task file is missing/invalid/empty.</summary>
    public string? TasksEmptyHint { get; init; }

    /// <summary>Localized "N more" line when the task file has more rows than the card paints.</summary>
    public string? TasksMoreText { get; init; }

    /// <summary>"done/total" header label for the task card (file totals, not the painted subset).</summary>
    public string? TasksProgressText { get; init; }

    /// <summary>System stat rows for the widgets card (CPU/RAM/battery); empty when unsampleable.</summary>
    public ImmutableArray<WidgetStatData> Stats { get; init; } = [];

    /// <summary>Unread notification count.</summary>
    public int UnreadCount { get; init; }

    /// <summary>Localized calendar month title.</summary>
    public string CalendarMonthTitle { get; init; } = string.Empty;

    /// <summary>Calendar cells for the displayed month (6×7 grid).</summary>
    public ImmutableArray<CalendarDayData> CalendarDays { get; init; } = [];

    /// <summary>Localized weekday header labels, Monday first.</summary>
    public ImmutableArray<string> WeekdayHeaders { get; init; } = [];
}

/// <summary>Source of non-activity island content; the host replaces the design source with platform data later.</summary>
public interface IIslandDataSource
{
    /// <summary>Current snapshot.</summary>
    IslandContent Snapshot();

    /// <summary>Raised when the snapshot changed (raised on the UI thread).</summary>
    event EventHandler? Changed;

    /// <summary>Starts producing content (idempotent).</summary>
    void Start();

    /// <summary>Stops producing content (idempotent).</summary>
    void Stop();
}

/// <summary>
/// Deterministic design-time/development data source (clock + realistic sample content). Used by the
/// preview/headless tests and as the fallback until Platform event sources are wired by the host.
/// </summary>
public sealed class DesignIslandDataSource : IIslandDataSource, IDisposable
{
    private readonly TimeProvider _timeProvider;
    private readonly ILocalizer _localizer;
    private readonly Action<Action> _uiPost;
    private ITimer? _minuteTimer;
    private bool _started;
    private IslandContent _content;

    /// <summary>Creates the design data source.</summary>
    /// <param name="localizer">Localizer for host labels.</param>
    /// <param name="timeProvider">Injected time source (tests).</param>
    /// <param name="uiPost">Dispatcher for <see cref="Changed"/>; defaults to the Avalonia UI thread.</param>
    public DesignIslandDataSource(ILocalizer localizer, TimeProvider? timeProvider = null, Action<Action>? uiPost = null)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _uiPost = uiPost ?? (static action => Avalonia.Threading.Dispatcher.UIThread.Post(action));
        _content = BuildSnapshot(_timeProvider.GetLocalNow());
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public IslandContent Snapshot() => _content;

    /// <inheritdoc />
    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        ScheduleMinuteTick();
    }

    /// <inheritdoc />
    public void Stop()
    {
        _started = false;
        _minuteTimer?.Dispose();
        _minuteTimer = null;
    }

    /// <summary>Refreshes the snapshot at an explicit time (tests).</summary>
    public void Refresh(DateTimeOffset now)
    {
        _content = BuildSnapshot(now);
        RaiseChanged();
    }

    /// <summary>Replaces parts of the content (developer preview hooks).</summary>
    public void Update(Func<IslandContent, IslandContent> mutate)
    {
        _content = mutate(_content);
        RaiseChanged();
    }

    /// <summary>
    /// Raises <see cref="Changed"/> on the UI thread. Timer callbacks (clock minute tick) run on the
    /// thread pool, so raising directly would let the host touch Avalonia controls off-thread
    /// (fixed idle crash: InvalidOperationException "calling thread cannot access this object").
    /// </summary>
    private void RaiseChanged()
    {
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        _uiPost(() => Changed?.Invoke(this, EventArgs.Empty));
    }

    /// <inheritdoc />
    public void Dispose() => Stop();

    private void ScheduleMinuteTick()
    {
        var now = _timeProvider.GetLocalNow();
        var nextMinute = now.AddMinutes(1);
        var delay = TimeSpan.FromSeconds(60 - now.Second) - TimeSpan.FromMilliseconds(now.Millisecond);
        if (delay < TimeSpan.FromSeconds(1))
        {
            delay = TimeSpan.FromSeconds(1);
        }

        _minuteTimer?.Dispose();
        _minuteTimer = _timeProvider.CreateTimer(
            static state =>
            {
                var source = (DesignIslandDataSource)state!;
                var clock = source._timeProvider.GetLocalNow();
                source.Refresh(clock);
                if (source._started)
                {
                    source.ScheduleMinuteTick();
                }
            },
            this,
            delay,
            Timeout.InfiniteTimeSpan);
    }

    private IslandContent BuildSnapshot(DateTimeOffset now)
    {
        var culture = _localizer.CurrentCulture;
        var media = new MediaSessionData
        {
            Title = "Midnight City",
            Artist = "M83",
            Album = "Hurry Up, We're Dreaming",
            Source = "Spotify",
            ProgressPercent = 42,
            PositionText = "1:52",
            DurationText = "4:03",
            IsPlaying = true,
            AccentHex = "#F06AB0",
            LyricLine = "Waiting for a car to drive me home",
            NextLyricLine = "The city is my church",
            TrackIndex = 3,
            TrackCount = 12,
        };

        var notifications = ImmutableArray.Create(
            new NotificationItemData
            {
                Title = "Doulor",
                Body = "The v2 branch is green again — pull before you start.",
                TimeText = "2 min",
                Source = "Teams",
            },
            new NotificationItemData
            {
                Title = "GitHub",
                Body = "Your verification code is 418302",
                TimeText = "14 min",
                VerificationCode = "418302",
                Source = "Edge",
            },
            new NotificationItemData
            {
                Title = "Windows Update",
                Body = "Restart to finish installing updates.",
                TimeText = "1 h",
                Source = "Windows",
            });

        var clipboardItems = ImmutableArray.Create(
            new ClipboardItemData { Preview = "docs/rewrite/specs/2026-10-18-fluidbar-v2-design.md", Meta = "2 min", Pinned = true },
            new ClipboardItemData { Preview = "The island stays obsidian in every theme.", Meta = "12 min" },
            new ClipboardItemData { Preview = _localizer.Get(LocKeys.Clipboard.Image), Meta = "1 h", IsImage = true });

        var tasks = ImmutableArray.Create(
            new WidgetTaskData { Title = _localizer.Get(LocKeys.Page.Widgets.Task.Review), Done = true },
            new WidgetTaskData { Title = _localizer.Get(LocKeys.Page.Widgets.Task.Ship) },
            new WidgetTaskData { Title = _localizer.Get(LocKeys.Page.Widgets.Task.Inbox) });

        return new IslandContent
        {
            Now = now,
            BatteryPercent = 82,
            IsCharging = false,
            NetworkName = "Home Wi-Fi",
            IsOnline = true,
            FocusOn = false,
            // Design-time levels only: the production source maps the real WASAPI/DDC-CI monitors and
            // reports null when they are unavailable (the renderer then draws an em dash).
            VolumePercent = 62,
            IsMuted = false,
            BrightnessPercent = 78,
            InputMethod = "EN",
            Media = media,
            Notifications = notifications,
            Clipboard = clipboardItems,
            Tasks = tasks,
            UnreadCount = 3,
            CalendarMonthTitle = now.ToString("MMMM yyyy", culture),
            WeekdayHeaders = BuildWeekdayHeaders(culture),
            CalendarDays = BuildCalendar(now, culture),
        };
    }

    private static ImmutableArray<string> BuildWeekdayHeaders(System.Globalization.CultureInfo culture)
    {
        var builder = ImmutableArray.CreateBuilder<string>(7);
        // Monday-first short weekday names derived from the culture's shortest day names.
        var firstDay = culture.DateTimeFormat.FirstDayOfWeek;
        var names = culture.DateTimeFormat.AbbreviatedDayNames;
        for (var i = 0; i < 7; i++)
        {
            builder.Add(names[((int)firstDay + i) % 7]);
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<CalendarDayData> BuildCalendar(DateTimeOffset now, System.Globalization.CultureInfo culture)
    {
        var firstOfMonth = new DateTime(now.Year, now.Month, 1);
        var offset = ((int)firstOfMonth.DayOfWeek - (int)culture.DateTimeFormat.FirstDayOfWeek + 7) % 7;
        var start = firstOfMonth.AddDays(-offset);
        var builder = ImmutableArray.CreateBuilder<CalendarDayData>(42);
        for (var i = 0; i < 42; i++)
        {
            var day = start.AddDays(i);
            builder.Add(new CalendarDayData
            {
                Day = day.Day,
                InMonth = day.Month == now.Month,
                IsToday = day.Date == now.Date,
                HasEvents = day.Day % 7 == 3,
                IsSelected = day.Date == now.Date,
            });
        }

        return builder.ToImmutable();
    }
}
