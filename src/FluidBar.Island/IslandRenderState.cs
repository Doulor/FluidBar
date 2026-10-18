using FluidBar.Core.Activities;
using FluidBar.Core.Layout;
using FluidBar.Core.Interaction;

namespace FluidBar.Island;

/// <summary>
/// One immutable island frame: the animating shell geometry, the active surface, the page strip
/// position, the compact activity, the expanded page content and the detached strip state.
/// <para>
/// All geometry is host-local DIP. The host builds one of these per frame from Core layout/motion
/// output and calls <see cref="IslandRenderer.Render"/>; nothing here performs layout or reads a clock.
/// </para>
/// </summary>
public readonly record struct IslandRenderState
{
    /// <summary>Creates a default (hidden/empty) state; use the factories for real frames.</summary>
    public IslandRenderState()
    {
        Pages = ExpandedPageContent.Empty;
        Theme = IslandTheme.Dark;
    }

    /// <summary>Presentation mode (float/flush/dock); selects the fixed host surface and radii.</summary>
    public IslandMode Mode { get; init; }

    /// <summary>Visual form. <see cref="ShellForm.Peek"/> and <see cref="ShellForm.Closing"/> render
    /// the compact content; <see cref="ShellForm.Hidden"/> paints nothing.</summary>
    public ShellForm Form { get; init; }

    /// <summary>Shell width in DIP.</summary>
    public double ShellWidth { get; init; }

    /// <summary>Shell height in DIP.</summary>
    public double ShellHeight { get; init; }

    /// <summary>Per-corner shell radii in DIP.</summary>
    public CornerRadii ShellRadii { get; init; }

    /// <summary>Shell top-left inside the host surface, in DIP.</summary>
    public VisualPoint ShellOrigin { get; init; } = new(LayoutTokens.WindowPadding, LayoutTokens.WindowPadding);

    /// <summary>Whole-shell opacity (hide fade); 0 hides and disables hit-testing.</summary>
    public double Opacity { get; init; } = 1;

    /// <summary>Opacity of the compact/expanded content layer (host derives it from shell height).</summary>
    public double ContentOpacity { get; init; } = 1;

    /// <summary>Transient vertical nudge in DIP (new-activity kick); 0 when reduced motion.</summary>
    public double NudgeOffsetY { get; init; }

    /// <summary>Press scale about the shell centre (1 = not pressed).</summary>
    public double PressScale { get; init; } = 1;

    /// <summary>Number of ghost pills behind the compact shell (0–2).</summary>
    public int GhostCount { get; init; }

    /// <summary>Continuous pager position, committed page and gesture flags.</summary>
    public PagerRenderState Pager { get; init; }

    /// <summary>Compact activity; null renders the idle pill.</summary>
    public CompactActivity? Compact { get; init; }

    /// <summary>Expanded page content (home/media models + page titles).</summary>
    public ExpandedPageContent Pages { get; init; }

    /// <summary>Resolved theme (palette + material).</summary>
    public IslandTheme Theme { get; init; }

    /// <summary>Detached mini-island strip state.</summary>
    public ArchipelagoState Archipelago { get; init; }

    /// <summary>Reduced motion: no parallax/scale on incoming pages, no waveform/pulse animation.</summary>
    public bool ReducedMotion { get; init; }

    /// <summary>Host-driven animation phase in seconds (waveform/pulse); ignored when reduced motion.</summary>
    public double MotionPhase { get; init; }

    /// <summary>Committed page index (rounded pager position).</summary>
    public int PageIndex => Pager.PageIndex;

    /// <summary>Continuous page position (0..5).</summary>
    public double PageOffset => Pager.PagePosition;

    /// <summary>Whether the expanded panel is showing.</summary>
    public bool IsExpanded => Form == ShellForm.Expanded;

    /// <summary>Whether the shell is visible and interactive.</summary>
    public bool IsVisible => Form != ShellForm.Hidden && Opacity > 0.01 && ShellWidth > 0.5 && ShellHeight > 0.5;

    /// <summary>Creates a compact state at the exact Core token size for the activity kind.</summary>
    public static IslandRenderState ForCompact(
        IslandMode mode,
        CompactActivity? activity,
        IslandTheme theme,
        PagerRenderState pager = default,
        ArchipelagoState archipelago = default,
        ShellForm form = ShellForm.Compact)
    {
        var size = LayoutTokens.CompactSize(activity?.Kind, mode);
        var radii = LayoutTokens.RadiiFor(form, mode, size.Height);
        var host = HostSurface.ForMode(mode);
        var origin = mode == IslandMode.Dock
            ? new VisualPoint(host.SidePadding, host.DockStripTopLocal)
            : host.ShellLocalOffset;
        return new IslandRenderState
        {
            Mode = mode,
            Form = form,
            ShellWidth = size.Width,
            ShellHeight = size.Height,
            ShellRadii = radii,
            ShellOrigin = origin,
            Pager = pager,
            Compact = activity,
            Pages = ExpandedPageContent.Empty,
            Theme = theme,
            Archipelago = archipelago,
        };
    }

    /// <summary>Creates an expanded state (uniform 440x284 shell) with the given page content.</summary>
    public static IslandRenderState Expanded(
        IslandMode mode,
        PagerRenderState pager,
        ExpandedPageContent pages,
        IslandTheme theme,
        ArchipelagoState archipelago = default)
    {
        var host = HostSurface.ForMode(mode);
        return new IslandRenderState
        {
            Mode = mode,
            Form = ShellForm.Expanded,
            ShellWidth = LayoutTokens.ExpandedWidth,
            ShellHeight = LayoutTokens.ExpandedHeight,
            ShellRadii = LayoutTokens.RadiiFor(ShellForm.Expanded, mode, LayoutTokens.ExpandedHeight),
            ShellOrigin = host.ShellLocalOffset,
            Pager = pager,
            Pages = pages,
            Theme = theme,
            Archipelago = archipelago,
        };
    }

    /// <summary>Creates a hidden state (nothing painted, no hit targets).</summary>
    public static IslandRenderState Hidden(IslandMode mode, IslandTheme theme) => new()
    {
        Mode = mode,
        Form = ShellForm.Hidden,
        Opacity = 0,
        Theme = theme,
    };

    /// <summary>Returns a copy with a new pager state.</summary>
    public IslandRenderState WithPager(PagerRenderState pager) => this with { Pager = pager };

    /// <summary>Returns a copy with a new detached-strip state.</summary>
    public IslandRenderState WithArchipelago(ArchipelagoState archipelago) => this with { Archipelago = archipelago };

    /// <summary>Returns a copy with a new shell opacity.</summary>
    public IslandRenderState WithOpacity(double opacity) => this with { Opacity = opacity };
}

/// <summary>Continuous pager state consumed by the page strip (all values come from Core policies).</summary>
public readonly record struct PagerRenderState
{
    /// <summary>Continuous page position (0..5); the strip is offset by <c>PagePosition × 400</c> DIP.</summary>
    public double PagePosition { get; init; }

    /// <summary>Committed page index (rounded position).</summary>
    public int PageIndex { get; init; }

    /// <summary>Whether the pointer is dragging the pager.</summary>
    public bool IsDragging { get; init; }

    /// <summary>Whether a snap animation is running.</summary>
    public bool IsSnapping { get; init; }

    /// <summary>Whether an interactive page control owns the current gesture.</summary>
    public bool ControlOwnsGesture { get; init; }

    /// <summary>Creates a settled state for a page.</summary>
    public static PagerRenderState At(int page) => new()
    {
        PageIndex = PagePagerPolicy.ClampPage(page),
        PagePosition = PagePagerPolicy.ClampPage(page),
    };

    /// <summary>Creates a state from a fractional page position (committed page derived).</summary>
    public static PagerRenderState FromPosition(double pagePosition, bool isDragging = false, bool isSnapping = false)
    {
        var position = double.IsFinite(pagePosition) ? Math.Clamp(pagePosition, 0, PagerConstants.PageCount - 1) : 0;
        return new PagerRenderState
        {
            PagePosition = position,
            PageIndex = PagePagerPolicy.OffsetToPage(position * PagerConstants.PagePitchDip),
            IsDragging = isDragging,
            IsSnapping = isSnapping,
        };
    }
}

/// <summary>Detached mini-island strip state (the host drives the reveal/hide springs).</summary>
public readonly record struct ArchipelagoState
{
    /// <summary>Creates a default (hidden) strip state.</summary>
    public ArchipelagoState()
    {
    }

    /// <summary>Whether the strip is revealed at all.</summary>
    public bool Visible { get; init; }

    /// <summary>Reveal opacity 0..1 (host spring).</summary>
    public double Opacity { get; init; }

    /// <summary>Vertical translate in DIP (host spring; 0 = settled).</summary>
    public double TranslateYDip { get; init; }

    /// <summary>Scale about the strip centre (host spring; 1 = settled).</summary>
    public double Scale { get; init; } = 1;

    /// <summary>Continuous page position used to pick the current/prev/next islands.</summary>
    public double PagePosition { get; init; }

    /// <summary>Far-page dots on the left; -1 derives them from the page position.</summary>
    public int FarLeftDotCount { get; init; } = -1;

    /// <summary>Far-page dots on the right; -1 derives them from the page position.</summary>
    public int FarRightDotCount { get; init; } = -1;

    /// <summary>Maximum dots per side.</summary>
    public const int MaxDotsPerSide = 3;

    /// <summary>Creates a visible, settled strip state for a page position.</summary>
    public static ArchipelagoState Shown(double pagePosition, double opacity = 1, double translateY = 0, double scale = 1) => new()
    {
        Visible = true,
        Opacity = Math.Clamp(opacity, 0, 1),
        TranslateYDip = translateY,
        Scale = scale,
        PagePosition = pagePosition,
    };

    /// <summary>Creates a hidden strip state (no paint, no hit targets).</summary>
    public static ArchipelagoState HiddenState => new() { Visible = false, Opacity = 0, PagePosition = 0 };

    /// <summary>Resolved left far-page dot count (clamped to the available pages and the 3-dot cap).</summary>
    public int ResolvedFarLeftDots => ResolveDots(FarLeftDotCount, PagePosition, left: true);

    /// <summary>Resolved right far-page dot count (clamped to the available pages and the 3-dot cap).</summary>
    public int ResolvedFarRightDots => ResolveDots(FarRightDotCount, PagePosition, left: false);

    private static int ResolveDots(int requested, double pagePosition, bool left)
    {
        var page = PagePagerPolicy.OffsetToPage(
            (double.IsFinite(pagePosition) ? Math.Clamp(pagePosition, 0, PagerConstants.PageCount - 1) : 0)
            * PagerConstants.PagePitchDip);
        var available = left
            ? Math.Max(0, page - 1)
            : Math.Max(0, (PagerConstants.PageCount - 1) - (page + 1));
        var count = requested >= 0 ? requested : available;
        return Math.Clamp(count, 0, Math.Min(MaxDotsPerSide, available));
    }
}

/// <summary>One compact activity: the Core kind (drives the exact size token) plus its display model.</summary>
public readonly record struct CompactActivity(ActivityKind Kind, CompactModel Model)
{
    /// <summary>Creates an activity with an empty model.</summary>
    public static CompactActivity For(ActivityKind kind) => new(kind, new CompactModel());
}

/// <summary>Compact pill content. Every string is optional; missing values fall back to neutral copy.</summary>
public sealed record CompactModel
{
    /// <summary>Primary line.</summary>
    public string? Title { get; init; }

    /// <summary>Secondary line.</summary>
    public string? Subtitle { get; init; }

    /// <summary>Body/preview line (clipboard).</summary>
    public string? Body { get; init; }

    /// <summary>Trailing badge/chip text.</summary>
    public string? Badge { get; init; }

    /// <summary>Notification source app name.</summary>
    public string? AppName { get; init; }

    /// <summary>Idle clock text (HH:mm).</summary>
    public string? ClockText { get; init; }

    /// <summary>Idle date text.</summary>
    public string? DateText { get; init; }

    /// <summary>Status label (agent/battery/timer).</summary>
    public string? StatusText { get; init; }

    /// <summary>Elapsed/duration text (agent).</summary>
    public string? DurationText { get; init; }

    /// <summary>Countdown text (timer).</summary>
    public string? TimerText { get; init; }

    /// <summary>Verification code (notification chip).</summary>
    public string? VerificationCode { get; init; }

    /// <summary>HUD/battery level 0..100.</summary>
    public int? Level { get; init; }

    /// <summary>Battery percentage 0..100.</summary>
    public int? BatteryPercent { get; init; }

    /// <summary>Unread badge count (0 = hidden).</summary>
    public int UnreadCount { get; init; }

    /// <summary>Whether the media session is playing.</summary>
    public bool IsActive { get; init; }

    /// <summary>Whether the HUD is muted.</summary>
    public bool IsMuted { get; init; }

    /// <summary>Whether the device is charging.</summary>
    public bool IsCharging { get; init; }

    /// <summary>Progress percentage (0..100); negative = no progress bar.</summary>
    public double ProgressPercent { get; init; } = -1;

    /// <summary>Optional accent colour hex for the activity icon/artwork.</summary>
    public string? AccentHex { get; init; }

    /// <summary>Optional leading icon override.</summary>
    public IconKind? Icon { get; init; }

    /// <summary>Agent/status tone.</summary>
    public CompactStatus Status { get; init; }
}

/// <summary>Semantic status tone for compact surfaces (one accent per state).</summary>
public enum CompactStatus
{
    /// <summary>Neutral/secondary.</summary>
    Neutral,

    /// <summary>Informational/running.</summary>
    Info,

    /// <summary>Success/done.</summary>
    Success,

    /// <summary>Warning.</summary>
    Warning,

    /// <summary>Danger/error.</summary>
    Danger,
}

/// <summary>Expanded page content (home/media MVP + the six page titles used by the strip/overlay).</summary>
public sealed record ExpandedPageContent
{
    /// <summary>Empty content (defaults).</summary>
    public static ExpandedPageContent Empty { get; } = new();

    /// <summary>Home page model.</summary>
    public HomePageModel Home { get; init; } = new();

    /// <summary>Media page model.</summary>
    public MediaPageModel Media { get; init; } = new();

    /// <summary>Notifications page model (page 2).</summary>
    public NotificationsPageModel Notifications { get; init; } = new();

    /// <summary>Clipboard history page model (page 3).</summary>
    public ClipboardPageModel Clipboard { get; init; } = new();

    /// <summary>Agent activity page model (page 4).</summary>
    public AgentPageModel Agent { get; init; } = new();

    /// <summary>Widgets page model (page 5).</summary>
    public WidgetsPageModel Widgets { get; init; } = new();

    /// <summary>Localized page titles (six); missing entries fall back to the defaults.</summary>
    public IReadOnlyList<string> PageTitles { get; init; } = DefaultPageTitles;

    /// <summary>Optional short labels for the detached strip (six); falls back to <see cref="PageTitles"/>.</summary>
    public IReadOnlyList<string>? ShortTitles { get; init; }

    /// <summary>Default English page titles.</summary>
    public static string[] DefaultPageTitles { get; } =
    [
        "Control center", "Now playing", "Notifications", "Clipboard", "Agent activity", "Widgets",
    ];

    /// <summary>Returns the title for a page index (clamped, never null).</summary>
    public string Title(int page)
    {
        var index = Math.Clamp(page, 0, PagerConstants.PageCount - 1);
        var titles = PageTitles;
        return titles is not null && index < titles.Count && !string.IsNullOrEmpty(titles[index])
            ? titles[index]!
            : DefaultPageTitles[index];
    }

    /// <summary>Returns the short strip label for a page index (clamped, never null).</summary>
    public string ShortTitle(int page)
    {
        var index = Math.Clamp(page, 0, PagerConstants.PageCount - 1);
        var titles = ShortTitles;
        return titles is not null && index < titles.Count && !string.IsNullOrEmpty(titles[index])
            ? titles[index]!
            : Title(index);
    }
}

/// <summary>Home page (control center) model.</summary>
public sealed record HomePageModel
{
    /// <summary>Clock text (HH:mm).</summary>
    public string? ClockText { get; init; }

    /// <summary>Date line.</summary>
    public string? DateText { get; init; }

    /// <summary>Weather temperature ("18°").</summary>
    public string? WeatherTemp { get; init; }

    /// <summary>Weather condition ("Partly sunny").</summary>
    public string? WeatherCondition { get; init; }

    /// <summary>Whether to draw the weather chip.</summary>
    public bool ShowWeather { get; init; } = true;

    /// <summary>Quick toggles (up to 4).</summary>
    public IReadOnlyList<HomeToggleModel> Toggles { get; init; } = DefaultToggles;

    /// <summary>Now-playing summary card.</summary>
    public MediaSummaryModel NowPlaying { get; init; } = new();

    /// <summary>Volume level 0..100.</summary>
    public int Volume { get; init; } = 62;

    /// <summary>Brightness level 0..100.</summary>
    public int Brightness { get; init; } = 78;

    /// <summary>Whether the volume is muted.</summary>
    public bool IsMuted { get; init; }

    /// <summary>Battery level 0..100.</summary>
    public int BatteryPercent { get; init; } = 86;

    /// <summary>Whether the device is charging.</summary>
    public bool IsCharging { get; init; } = true;

    /// <summary>Battery status text ("Plugged in").</summary>
    public string? BatteryStatus { get; init; }

    /// <summary>Battery remaining text ("4h left").</summary>
    public string? BatteryRemaining { get; init; }

    /// <summary>Default quick toggles (Wi-Fi on, Bluetooth off, Focus off, Dark on).</summary>
    public static HomeToggleModel[] DefaultToggles { get; } =
    [
        new(IconKind.Wifi, "Wi-Fi", true),
        new(IconKind.Bluetooth, "Bluetooth", false),
        new(IconKind.Moon, "Focus", false),
        new(IconKind.Moon, "Dark", true),
    ];
}

/// <summary>One home quick toggle.</summary>
public readonly record struct HomeToggleModel(IconKind Icon, string Label, bool IsOn);

/// <summary>Compact now-playing summary used by the home card.</summary>
public sealed record MediaSummaryModel
{
    /// <summary>Track title.</summary>
    public string? Title { get; init; }

    /// <summary>Artist · album line.</summary>
    public string? Subtitle { get; init; }

    /// <summary>Progress percentage 0..100.</summary>
    public double ProgressPercent { get; init; } = 46;

    /// <summary>Whether the session is playing.</summary>
    public bool IsPlaying { get; init; } = true;

    /// <summary>Optional artwork accent hex.</summary>
    public string? AccentHex { get; init; }
}

/// <summary>Expanded media page model.</summary>
public sealed record MediaPageModel
{
    /// <summary>Whether a media session exists; false draws the empty state.</summary>
    public bool HasSession { get; init; } = true;

    /// <summary>Eyebrow label ("Now playing").</summary>
    public string? Eyebrow { get; init; }

    /// <summary>Track title.</summary>
    public string? Title { get; init; }

    /// <summary>Artist.</summary>
    public string? Artist { get; init; }

    /// <summary>Album · year.</summary>
    public string? Album { get; init; }

    /// <summary>Current lyric line.</summary>
    public string? LyricLine { get; init; }

    /// <summary>Next lyric line.</summary>
    public string? NextLyricLine { get; init; }

    /// <summary>Label in front of the next lyric ("Next").</summary>
    public string? NextLyricLabel { get; init; }

    /// <summary>Elapsed time text.</summary>
    public string? PositionText { get; init; }

    /// <summary>Duration/remaining text.</summary>
    public string? DurationText { get; init; }

    /// <summary>Progress percentage 0..100.</summary>
    public double ProgressPercent { get; init; } = 46;

    /// <summary>Whether the session is playing.</summary>
    public bool IsPlaying { get; init; } = true;

    /// <summary>Optional artwork accent hex.</summary>
    public string? AccentHex { get; init; }

    /// <summary>Output device label.</summary>
    public string? OutputName { get; init; }

    /// <summary>Queue count (0 hides the queue label).</summary>
    public int QueueCount { get; init; }

    /// <summary>Empty-state title (no session).</summary>
    public string? EmptyTitle { get; init; }

    /// <summary>Empty-state hint.</summary>
    public string? EmptyHint { get; init; }
}

/// <summary>
/// Expanded notifications page (page 2): a scrollable list of notification rows. The renderer paints
/// up to five 40 DIP rows in the 368×216 body band; extra rows scroll by <see cref="ScrollOffset"/>.
/// </summary>
public sealed record NotificationsPageModel
{
    /// <summary>Notification rows (newest first).</summary>
    public IReadOnlyList<NotificationItemModel> Items { get; init; } = [];

    /// <summary>Vertical scroll offset in DIP (clamped by the renderer to the list range).</summary>
    public double ScrollOffset { get; init; }

    /// <summary>Row index under the pointer (-1 = none); paints the hover background.</summary>
    public int HoverIndex { get; init; } = -1;

    /// <summary>Selected row index (-1 = none); paints the accent selection fill.</summary>
    public int SelectedIndex { get; init; } = -1;

    /// <summary>Empty-state title override.</summary>
    public string? EmptyTitle { get; init; }

    /// <summary>Empty-state hint override.</summary>
    public string? EmptyHint { get; init; }
}

/// <summary>One notification row.</summary>
public sealed record NotificationItemModel
{
    /// <summary>Source app name ("Teams").</summary>
    public string? AppName { get; init; }

    /// <summary>Notification title.</summary>
    public string? Title { get; init; }

    /// <summary>Body/preview line.</summary>
    public string? Body { get; init; }

    /// <summary>Relative time label ("3 min").</summary>
    public string? TimeText { get; init; }

    /// <summary>Optional verification code rendered as a chip.</summary>
    public string? VerificationCode { get; init; }

    /// <summary>Optional app accent colour hex for the leading glyph.</summary>
    public string? AccentHex { get; init; }

    /// <summary>Optional leading icon override (defaults to the bell).</summary>
    public IconKind? Icon { get; init; }

    /// <summary>Whether the row is unread (informational).</summary>
    public bool IsUnread { get; init; } = true;
}

/// <summary>Clipboard content type (drives the badge icon/tone and the default type label).</summary>
public enum ClipboardContentKind
{
    /// <summary>Plain text.</summary>
    Text,

    /// <summary>Code snippet.</summary>
    Code,

    /// <summary>URL.</summary>
    Link,

    /// <summary>Image/file preview.</summary>
    Image,
}

/// <summary>
/// Expanded clipboard page (page 3): a scrollable history of 48 DIP rows with pin/copy actions.
/// </summary>
public sealed record ClipboardPageModel
{
    /// <summary>Clipboard entries (pinned entries first).</summary>
    public IReadOnlyList<ClipboardItemModel> Items { get; init; } = [];

    /// <summary>Vertical scroll offset in DIP (clamped by the renderer to the list range).</summary>
    public double ScrollOffset { get; init; }

    /// <summary>Row index under the pointer (-1 = none).</summary>
    public int HoverIndex { get; init; } = -1;

    /// <summary>Selected row index (-1 = none).</summary>
    public int SelectedIndex { get; init; } = -1;

    /// <summary>Row index that was just copied (-1 = none); paints the copy confirmation.</summary>
    public int CopiedIndex { get; init; } = -1;

    /// <summary>Localized "Pinned" label (defaults to English).</summary>
    public string? PinnedLabel { get; init; }

    /// <summary>Empty-state title override.</summary>
    public string? EmptyTitle { get; init; }

    /// <summary>Empty-state hint override.</summary>
    public string? EmptyHint { get; init; }
}

/// <summary>One clipboard history row.</summary>
public sealed record ClipboardItemModel
{
    /// <summary>Preview text (single line, ellipsised by the renderer).</summary>
    public string? Preview { get; init; }

    /// <summary>Relative time label ("2 min").</summary>
    public string? TimeText { get; init; }

    /// <summary>Optional localized type label override ("Text"/"Code"/"Link"/"Image").</summary>
    public string? TypeLabel { get; init; }

    /// <summary>Content type.</summary>
    public ClipboardContentKind Kind { get; init; } = ClipboardContentKind.Text;

    /// <summary>Whether the row is pinned.</summary>
    public bool IsPinned { get; init; }

    /// <summary>Whether the preview is monospace (code/command).</summary>
    public bool IsMonospace { get; init; }
}

/// <summary>
/// Expanded agent page (page 4): a scrollable list of 48 DIP agent session rows with status tone,
/// duration and a running progress bar.
/// </summary>
public sealed record AgentPageModel
{
    /// <summary>Agent sessions.</summary>
    public IReadOnlyList<AgentItemModel> Items { get; init; } = [];

    /// <summary>Vertical scroll offset in DIP (clamped by the renderer to the list range).</summary>
    public double ScrollOffset { get; init; }

    /// <summary>Row index under the pointer (-1 = none).</summary>
    public int HoverIndex { get; init; } = -1;

    /// <summary>Selected row index (-1 = none).</summary>
    public int SelectedIndex { get; init; } = -1;

    /// <summary>Empty-state title override.</summary>
    public string? EmptyTitle { get; init; }

    /// <summary>Empty-state hint override.</summary>
    public string? EmptyHint { get; init; }
}

/// <summary>One agent session row.</summary>
public sealed record AgentItemModel
{
    /// <summary>Tool name ("Claude Code").</summary>
    public string? Tool { get; init; }

    /// <summary>Project name ("fluidbar").</summary>
    public string? Project { get; init; }

    /// <summary>Task description.</summary>
    public string? Task { get; init; }

    /// <summary>Localized status label ("Running").</summary>
    public string? StatusText { get; init; }

    /// <summary>Elapsed/duration label ("02:14").</summary>
    public string? DurationText { get; init; }

    /// <summary>Status tone (Info paints the running spinner and progress bar).</summary>
    public CompactStatus Tone { get; init; }

    /// <summary>Progress percentage 0..100; negative hides the progress bar.</summary>
    public double ProgressPercent { get; init; } = -1;
}

/// <summary>Expanded widgets page (page 5): month calendar, weather, system stats and a task list.</summary>
public sealed record WidgetsPageModel
{
    /// <summary>Calendar month title ("October 2026").</summary>
    public string? CalendarMonthTitle { get; init; }

    /// <summary>Seven weekday headers.</summary>
    public IReadOnlyList<string> WeekdayHeaders { get; init; } = DefaultWeekdayHeaders;

    /// <summary>Calendar cells (up to 42; Monday-first).</summary>
    public IReadOnlyList<CalendarDayModel> CalendarDays { get; init; } = [];

    /// <summary>Calendar footer event label ("Design review · 15:00").</summary>
    public string? CalendarEventText { get; init; }

    /// <summary>Calendar footer "today" label.</summary>
    public string? TodayLabel { get; init; }

    /// <summary>Weather temperature ("18°").</summary>
    public string? WeatherTemp { get; init; }

    /// <summary>Weather condition ("Partly sunny").</summary>
    public string? WeatherCondition { get; init; }

    /// <summary>High/low line ("18° / 11°").</summary>
    public string? WeatherHighLow { get; init; }

    /// <summary>Wind line ("NW 3").</summary>
    public string? WeatherWind { get; init; }

    /// <summary>System stat rows (up to three).</summary>
    public IReadOnlyList<WidgetStatModel> Stats { get; init; } = [];

    /// <summary>Task card title ("Today").</summary>
    public string? TasksTitle { get; init; }

    /// <summary>Task progress label ("1/3 done"); supplied pre-formatted to avoid per-frame strings.</summary>
    public string? TasksProgressText { get; init; }

    /// <summary>Task rows (up to three are painted).</summary>
    public IReadOnlyList<WidgetTaskModel> Tasks { get; init; } = [];

    /// <summary>Task row index under the pointer (-1 = none).</summary>
    public int HoverIndex { get; init; } = -1;

    /// <summary>Selected task row index (-1 = none).</summary>
    public int SelectedIndex { get; init; } = -1;

    /// <summary>Default English weekday headers (Monday-first).</summary>
    public static string[] DefaultWeekdayHeaders { get; } = ["Mo", "Tu", "We", "Th", "Fr", "Sa", "Su"];
}

/// <summary>One calendar cell.</summary>
public readonly record struct CalendarDayModel(int Day, bool InMonth = true, bool IsToday = false, bool HasEvents = false);

/// <summary>Stat tone for the widgets system card.</summary>
public enum WidgetStatTone
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

/// <summary>One system stat row (CPU/RAM/battery).</summary>
public readonly record struct WidgetStatModel(string Label, int Value, WidgetStatTone Tone = WidgetStatTone.Neutral);

/// <summary>One widget task row.</summary>
public readonly record struct WidgetTaskModel(string Title, bool Done = false);
