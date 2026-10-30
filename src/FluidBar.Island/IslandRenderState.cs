using FluidBar.Core.Activities;
using SkiaSharp;
using FluidBar.Core.Layout;
using FluidBar.Core.Interaction;

namespace FluidBar.Island;

/// <summary>One split-island preview: activity identity plus the minimal glyph/label the ball paints.</summary>
/// <param name="Kind">Activity class (used by UIA and click navigation).</param>
/// <param name="Icon">Icon glyph identity.</param>
/// <param name="Label">Localized short activity label (2-4 characters).</param>
/// <param name="Title">Activity title, kept for UIA help text.</param>
/// <param name="Page">Expanded page opened when the ball is activated.</param>
/// <param name="AccentArgb">0xAARRGGBB accent for the icon ring, or 0 for the theme accent.</param>
public sealed record IslandSplitIslet(
    ActivityKind Kind,
    IconKind Icon,
    string Label,
    string? Title,
    PageRef Page,
    uint AccentArgb,
    string? Badge = null,
    bool ShowAction = false,
    byte[]? Artwork = null,
    string? ArtworkKey = null,
    CompactStatus Status = CompactStatus.Info);

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

    /// <summary>
    /// Whether the shell is mid-morph (spring running). The renderer uses coarser shadow buckets while
    /// morphing so a 300 ms expand/collapse cannot bake a fresh blurred shadow image on every frame;
    /// settled frames still use the exact size and stay pixel-stable for goldens.
    /// </summary>
    public bool ShellMorphing { get; init; }

    /// <summary>Shell top-left inside the host surface, in DIP.</summary>
    public VisualPoint ShellOrigin { get; init; } = new(LayoutTokens.WindowPadding, LayoutTokens.WindowPadding);

    /// <summary>Whole-shell opacity (hide fade); 0 hides and disables hit-testing.</summary>
    public double Opacity { get; init; } = 1;

    /// <summary>Legacy combined content opacity (host diagnostics/compat; layers use their own fields).</summary>
    public double ContentOpacity { get; init; } = 1;

    /// <summary>
    /// Compact layer opacity from the host motion driver (prototype2 <c>.compact-layer</c>: fades out
    /// over 90 ms ease-in when the panel opens, back over 140 ms ease-out + 40 ms delay when it closes).
    /// <see langword="null"/> keeps the legacy <see cref="Form"/>-based visibility (fixtures/tests).
    /// </summary>
    public double? CompactLayerOpacity { get; init; }

    /// <summary>
    /// Expanded layer opacity from the host motion driver (prototype2 <c>.expanded-layer</c>: 220 ms
    /// ease-out with a 90 ms delay on expand, 140 ms ease-out on collapse). <see langword="null"/> keeps
    /// the legacy <see cref="Form"/>-based visibility.
    /// </summary>
    public double? ExpandedLayerOpacity { get; init; }

    /// <summary>
    /// Chrome (page number/title/settings/collapse) opacity from the host motion driver
    /// (prototype2 <c>.pager-title</c> / <c>.pager-collapse</c>: 160 ms ease-out, 90 ms delay on expand).
    /// <see langword="null"/> keeps the legacy <see cref="Form"/>-based visibility.
    /// </summary>
    public double? ChromeOpacity { get; init; }

    /// <summary>
    /// Previous backdrop capture, cross-faded with <see cref="Backdrop"/> so a desktop that changes
    /// brightness does not swap the glass content in one frame.
    /// </summary>
    public SKImage? PreviousBackdrop { get; init; }

    /// <summary>Cross-fade weight of <see cref="Backdrop"/> against <see cref="PreviousBackdrop"/> (0..1).</summary>
    public double BackdropBlend { get; init; } = 1.0;

    /// <summary>
    /// Yield progress 0..1 (0 = interactive, 1 = fully yielded). Drives the "soften" animation that is
    /// material-agnostic: the composited scene blurs, desaturates and fades.
    /// </summary>
    public double YieldProgress { get; init; }

    /// <summary>Resolved compact layer opacity (legacy fallback: visible unless fully expanded).</summary>
    public double ResolvedCompactLayerOpacity => Math.Clamp(
        CompactLayerOpacity ?? (Form == ShellForm.Expanded ? 0 : 1), 0, 1);

    /// <summary>Resolved expanded layer opacity (legacy fallback: visible only when fully expanded).</summary>
    public double ResolvedExpandedLayerOpacity => Math.Clamp(
        ExpandedLayerOpacity ?? (Form == ShellForm.Expanded ? 1 : 0), 0, 1);

    /// <summary>Resolved chrome opacity (legacy fallback: visible only when fully expanded).</summary>
    public double ResolvedChromeOpacity => Math.Clamp(
        ChromeOpacity ?? (Form == ShellForm.Expanded ? 1 : 0), 0, 1);

    /// <summary>Transient vertical nudge in DIP (new-activity kick); 0 when reduced motion.</summary>
    public double NudgeOffsetY { get; init; }

    /// <summary>Notification-card drag offset in DIP (negative = pushed up); 0 for every other state.</summary>
    public double NotificationDragOffsetY { get; init; }

    /// <summary>Press scale about the shell centre (1 = not pressed).</summary>
    public double PressScale { get; init; } = 1;

    /// <summary>Control under the pointer (drives the per-button hover feedback).</summary>
    public IslandPageControl HoverControl { get; init; }

    /// <summary>Page that owns <see cref="HoverControl"/>.</summary>
    public int HoverPage { get; init; } = -1;

    /// <summary>Control currently held down (drives the per-button press feedback).</summary>
    public IslandPageControl PressedControl { get; init; }

    /// <summary>Page that owns <see cref="PressedControl"/>.</summary>
    public int PressedPage { get; init; } = -1;

    /// <summary>Row/index of the pressed control (quick toggles, list buttons).</summary>
    public int PressedIndex { get; init; } = -1;

    /// <summary>Pointer position in host-local DIP (hover feedback); NaN when the pointer is outside.</summary>
    public double PointerX { get; init; } = double.NaN;

    /// <summary>Pointer Y in host-local DIP; NaN when the pointer is outside.</summary>
    public double PointerY { get; init; } = double.NaN;

    /// <summary>Whether the pointer is over the island (hover transitions).</summary>
    public bool HasPointer { get; init; }

    /// <summary>
    /// Stacked activity count behind the primary (0–2). Data/stacking only: prototype2 has no ghost
    /// pill element, so the renderer paints nothing behind the shell for this value.
    /// </summary>
    public int GhostCount { get; init; }

    /// <summary>
    /// Activities that did not fit in the visible stack (scheduler background queue). When greater than
    /// zero the compact pill reserves <see cref="LayoutTokens.OverflowBadgeReserve"/> at its right edge
    /// and paints the "+N" chip.
    /// </summary>
    public int OverflowCount { get; init; }

    /// <summary>
    /// Split-island previews (cell division): one small islet per extra visible activity, ordered by
    /// priority. Empty for every other stacking strategy.
    /// </summary>
    public IslandSplitIslet[] SplitIslets { get; init; } = [];

    /// <summary>0 = islets merged into the pill, 1 = fully separated (spring driven).</summary>
    public double SplitProgress { get; init; }

    /// <summary>Compact primary height the split capsules match (DIP), frozen while expanding.</summary>
    public double SplitIsletHeight { get; init; }

    /// <summary>One-shot 0..1 ghost-exchange phase after a wheel rotation of the stacked order.</summary>
    public double StackSpin { get; init; }

    /// <summary>Continuous pager position, committed page and gesture flags.</summary>
    public PagerRenderState Pager { get; init; }

    /// <summary>
    /// Island font scale from settings → appearance (0.8–1.6; 1.0 = the approved prototype sizes).
    /// Renderers scale every text style through <see cref="ScaledText"/> and grow their containers by
    /// the measured line-height delta so text never overflows or overlaps.
    /// </summary>
    public double FontScale { get; init; } = 1.0;

    /// <summary>Compact activity; null renders the idle pill.</summary>
    public CompactActivity? Compact { get; init; }

    /// <summary>
    /// Secondary compact notification island (drawn below the primary pill so a notification can appear
    /// without displacing the current activity). Null when there is no notification to surface this way.
    /// </summary>
    public CompactActivity? SecondaryCompact { get; init; }

    /// <summary>Expanded page content (home/media models + page titles).</summary>
    public ExpandedPageContent Pages { get; init; }

    /// <summary>Resolved theme (palette + material).</summary>
    public IslandTheme Theme { get; init; }

    /// <summary>
    /// Theme the shell is cross-fading *from* after a material switch (null when no swap is running).
    /// The renderer paints both themes with premultiplied additive compositing so the 180 ms swap is a
    /// true cross-fade instead of a pop (spec 3.4).
    /// </summary>
    public IslandTheme? PreviousTheme { get; init; }

    /// <summary>Material swap progress 0..1 (1 = no swap). Only read when <see cref="PreviousTheme"/> is set.</summary>
    public double MaterialBlend { get; init; } = 1;

    /// <summary>
    /// Blurred snapshot of the desktop behind the host surface (liquid glass). When null the material
    /// falls back to its tinted fill. The image covers the whole host surface in DIP.
    /// </summary>
    public SkiaSharp.SKImage? Backdrop { get; init; }

    /// <summary>
    /// Mean luminance 0..1 of <see cref="Backdrop"/>. Drives the adaptive tint: bright desktops get a
    /// heavier tint so white labels keep their contrast, dark ones get less so the glass stays clear.
    /// </summary>
    public double BackdropLuminance { get; init; } = 0.5;

    /// <summary>Lens magnification of the backdrop inside the rim band (1.0 = no refraction).</summary>
    public double RefractionScale { get; init; } = LiquidGlassTokens.RefractionScale;

    /// <summary>
    /// Whether liquid glass may flip the palette to follow the desktop luminance. Explicit dark/light
    /// theme choices turn this off so the user's pick is what they see.
    /// </summary>
    public bool FollowBackdropPalette { get; init; } = true;

    /// <summary>Width of the refraction band in DIP (0 = scale with the shell height).</summary>
    public double RefractionBandDip { get; init; }

    /// <summary>Detached mini-island strip state.</summary>
    public ArchipelagoState Archipelago { get; init; }

    /// <summary>Reduced motion: no parallax/scale on incoming pages, no waveform/pulse animation.</summary>
    public bool ReducedMotion { get; init; }

    /// <summary>Host-driven animation phase in seconds (waveform/pulse); ignored when reduced motion.</summary>
    public double MotionPhase { get; init; }

    /// <summary>
    /// Host-driven agent animation clock in seconds (ring spin / indeterminate progress). It only
    /// advances while an agent is running, so an idle island requests no frames.
    /// </summary>
    public double AgentAnimationSeconds { get; init; }

    /// <summary>
    /// Agent ring angle (degrees) frozen at the moment the activity left Running/Waiting. While the
    /// status stays live the renderer uses <see cref="AgentAnimationSeconds"/>; once the activity is
    /// Completed/Failed/Cancelled the arc is removed and this value keeps the last painted angle so it
    /// can never jump back to 0.
    /// </summary>
    public double? AgentRingFrozenDegrees { get; init; }

    /// <summary>
    /// Logical keyboard/UIA focus ring in host-local DIP (null when nothing is focused). The renderer
    /// paints a 2 DIP informational outline; the host never activates for pointer clicks.
    /// </summary>
    public VisualRect? FocusRing { get; init; }

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
        ShellForm form = ShellForm.Compact,
        double fontScale = 1.0)
    {
        var size = LayoutTokens.CompactSizeFor(activity?.Kind, mode, fontScale);
        var radii = LayoutTokens.RadiiFor(form, mode, size.Height, kind: activity?.Kind);
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
            FontScale = fontScale,
        };
    }

    /// <summary>Creates an expanded state (uniform 440x284 shell) with the given page content.</summary>
    public static IslandRenderState Expanded(
        IslandMode mode,
        PagerRenderState pager,
        ExpandedPageContent pages,
        IslandTheme theme,
        ArchipelagoState archipelago = default,
        double fontScale = 1.0)
    {
        var host = HostSurface.ForMode(mode);
        return new IslandRenderState
        {
            Mode = mode,
            Form = ShellForm.Expanded,
            ShellWidth = LayoutTokens.ExpandedWidth,
            ShellHeight = LayoutTokens.ExpandedHeight,
            ShellRadii = LayoutTokens.RadiiFor(
                ShellForm.Expanded, mode, LayoutTokens.ExpandedHeight, IslandMetrics.ExpandedPanelRadius),
            ShellOrigin = host.ShellLocalOffset,
            Pager = pager,
            Pages = pages,
            Theme = theme,
            Archipelago = archipelago,
            FontScale = fontScale,
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
    /// <summary>Creates the default pager state (the field initializers need an explicit ctor).</summary>
    public PagerRenderState()
    {
    }

    /// <summary>Continuous page position (0..5); the strip is offset by <c>PagePosition × 400</c> DIP.</summary>
    public double PagePosition { get; init; }

    /// <summary>Committed page index (rounded position).</summary>
    public int PageIndex { get; init; }

    /// <summary>Number of pages represented by the pager (set by the live page registry).</summary>
    public int PageCount { get; init; }

    /// <summary>Whether the pointer is dragging the pager.</summary>
    public bool IsDragging { get; init; }

    /// <summary>Whether a snap animation is running.</summary>
    public bool IsSnapping { get; init; }

    /// <summary>Whether an interactive page control owns the current gesture.</summary>
    public bool ControlOwnsGesture { get; init; }

    /// <summary>Creates a settled state for a page.</summary>
    public static PagerRenderState At(int page, int pageCount) => new()
    {
        PageIndex = PagePagerPolicy.ClampPage(page, pageCount),
        PagePosition = PagePagerPolicy.ClampPage(page, pageCount),
        PageCount = pageCount,
    };

    /// <summary>Creates a state from a fractional page position (committed page derived).</summary>
    public static PagerRenderState FromPosition(
        double pagePosition,
        int pageCount,
        bool isDragging = false,
        bool isSnapping = false)
    {
        var count = Math.Max(1, pageCount);
        var position = double.IsFinite(pagePosition) ? Math.Clamp(pagePosition, 0, count - 1) : 0;
        return new PagerRenderState
        {
            PagePosition = position,
            PageIndex = PagePagerPolicy.OffsetToPage(position * PagerConstants.PagePitchDip, count, PagerConstants.PagePitchDip),
            PageCount = count,
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

    /// <summary>
    /// Whether the host wants the always-on faint page preview (dots + current-page capsule) drawn
    /// while the panel is expanded. Host-driven so design/golden states can keep it off.
    /// </summary>
    public bool ShowIdlePreview { get; init; }

    /// <summary>Vertical translate in DIP (host spring; 0 = settled).</summary>
    public double TranslateYDip { get; init; }

    /// <summary>Scale about the strip centre (host spring; 1 = settled).</summary>
    public double Scale { get; init; } = 1;

    /// <summary>Continuous page position used to pick the current/prev/next islands.</summary>
    public double PagePosition { get; init; }

    /// <summary>Total page count represented by the strip (resolved from the live registry).</summary>
    public int PageCount { get; init; }

    /// <summary>
    /// Animated rail channels driven by the host's <see cref="MiniRailMotion"/>: capsule width, pill and
    /// dot cluster positions/opacities, label cross-fade progress and the three content pulses.
    /// <c>Active == false</c> means the host does not animate the rail, so the layout falls back to the
    /// settled geometry derived from the pager position (offscreen renders and goldens).
    /// </summary>
    public MiniRailMotionValues Rail { get; init; }

    /// <summary>
    /// Slot under the pointer (prototype `.mini-bg:hover`). Only the two neighbour glasses are
    /// interactive; the current capsule resolves to <see cref="MiniRailSlot.None"/>.
    /// </summary>
    public MiniRailSlot RailHover { get; init; }

    /// <summary>Slot held down (prototype `.mini-bg:active`).</summary>
    public MiniRailSlot RailPressed { get; init; }

    /// <summary>Slot with keyboard/UI Automation focus (prototype `:focus-visible` accent ring).</summary>
    public MiniRailSlot RailFocus { get; init; }

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

    /// <summary>
    /// Whether the left cluster has more far pages than the three rendered dots. The prototype draws
    /// the last dot 7 DIP wide (`.far-dot.wide`) and only that dot names the hidden count.
    /// </summary>
    public bool ResolvedFarLeftOverflow => AvailableDots(PagePosition, left: true) > MaxDotsPerSide;

    /// <summary>Whether the right cluster overflows its three rendered dots (wide last dot).</summary>
    public bool ResolvedFarRightOverflow => AvailableDots(PagePosition, left: false) > MaxDotsPerSide;

    private int ResolveDots(int requested, double pagePosition, bool left)
    {
        var available = AvailableDots(pagePosition, left);
        var count = requested >= 0 ? requested : available;
        return Math.Clamp(count, 0, Math.Min(MaxDotsPerSide, available));
    }

    private int AvailableDots(double pagePosition, bool left)
    {
        var count = Math.Max(1, PageCount);
        var page = PagePagerPolicy.OffsetToPage(
            (double.IsFinite(pagePosition) ? Math.Clamp(pagePosition, 0, count - 1) : 0)
            * PagerConstants.PagePitchDip, count);
        return left
            ? Math.Max(0, page - 1)
            : Math.Max(0, (count - 1) - (page + 1));
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

    /// <summary>Relative timestamp for the phone-style notification card ("now", "3 min").</summary>
    public string? TimeText { get; init; }

    /// <summary>Trailing badge/chip text.</summary>
    public string? Badge { get; init; }

    /// <summary>Notification source app name.</summary>
    public string? AppName { get; init; }

    /// <summary>Real app logo bytes for the phone-style notification tile (null = generic icon).</summary>
    public byte[]? AppIconPng { get; init; }

    /// <summary>Stable cache key for <see cref="AppIconPng"/>.</summary>
    public string? AppIconKey { get; init; }

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

    /// <summary>Whether the notification card keeps the reference repo's right-side audio spectrum.</summary>
    public bool ShowsAudioWave { get; init; }

    /// <summary>Whether the HUD is muted.</summary>
    public bool IsMuted { get; init; }

    /// <summary>Whether the device is charging.</summary>
    public bool IsCharging { get; init; }

    /// <summary>Progress percentage (0..100); negative = no progress bar.</summary>
    public double ProgressPercent { get; init; } = -1;

    /// <summary>Optional accent colour hex for the activity icon/artwork.</summary>
    public string? AccentHex { get; init; }

    /// <summary>Cover art bytes for the compact media pill (null = gradient placeholder).</summary>
    public byte[]? Artwork { get; init; }

    /// <summary>Stable cache key for <see cref="Artwork"/>.</summary>
    public string? ArtworkKey { get; init; }

    /// <summary>
    /// Live lyric line for the compact lyric layout (null = no lyric available, so the pill keeps the
    /// title/artist form instead of showing an empty row).
    /// </summary>
    public string? LyricLine { get; init; }

    /// <summary>Whether the media pill paints the lyric layout (cover + lyric line + wave, no title).</summary>
    public bool ShowLyrics { get; init; }

    /// <summary>Fade-in progress of the current lyric line (0..1) so a line change is not a hard cut.</summary>
    public float LyricFade { get; init; } = 1f;

    /// <summary>Karaoke sweep of the compact lyric line (0..1); negative = no per-word timing.</summary>
    public double LyricHighlight { get; init; } = -1;

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

    /// <summary>Extra host-rendered plugin pages appended after the six built-in pages (M0 compatibility).</summary>
    public IReadOnlyList<PluginPageModel> PluginPages { get; init; } = [];

    /// <summary>Ordered page slots; the controller fills this from the registry (built-ins + plugins).</summary>
    public IReadOnlyList<PageSlot> PageSlots { get; init; } = DefaultPageSlots;

    /// <summary>True when a controller/registry supplied an ordered slot list.</summary>
    public bool HasCustomPageSlots => !ReferenceEquals(PageSlots, DefaultPageSlots);

    /// <summary>Resolves a stable page reference to the current display index, or -1.</summary>
    public int IndexOfPage(PageRef page)
    {
        var slots = PageSlots ?? DefaultPageSlots;
        for (var i = 0; i < slots.Count; i++)
        {
            if (string.Equals(slots[i].PageId, page.PageId, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Total page count: six built-in pages plus registered plugin pages when slots are default.</summary>
    public int PageCount
    {
        get
        {
            var slots = PageSlots ?? DefaultPageSlots;
            var plugins = PluginPages?.Count ?? 0;
            return !HasCustomPageSlots && plugins > 0 ? slots.Count + plugins : slots.Count;
        }
    }

    /// <summary>Returns the slot at a page index (or null).</summary>
    public PageSlot? PageSlotAt(int page)
        => page >= 0 && page < PageSlots.Count ? PageSlots[page] : null;

    /// <summary>Returns the plugin page at a total page index, or null for a built-in page.</summary>
    public PluginPageModel? PluginPageAt(int page)
    {
        return PageSlotAt(page)?.PluginPage;
    }

    /// <summary>Default ordered built-in slots (used by tests/headless fixtures before the registry fills them).</summary>
    private static IReadOnlyList<PageSlot>? _defaultPageSlots;

    public static IReadOnlyList<PageSlot> DefaultPageSlots => _defaultPageSlots ??=
    [
        new PageSlot { PageId = "builtin.home", Kind = PageSlotKind.Home, TemplateId = "builtin.home", Title = "Control center", ShortTitle = "Home", IconToken = "home" },
        new PageSlot { PageId = "builtin.media", Kind = PageSlotKind.Media, TemplateId = "builtin.media", Title = "Now playing", ShortTitle = "Media", IconToken = "media" },
        new PageSlot { PageId = "builtin.notifications", Kind = PageSlotKind.Notifications, TemplateId = "list", Title = "Notifications", ShortTitle = "Notifications", IconToken = "bell" },
        new PageSlot { PageId = "builtin.clipboard", Kind = PageSlotKind.Clipboard, TemplateId = "list", Title = "Clipboard", ShortTitle = "Clipboard", IconToken = "clipboard" },
        new PageSlot { PageId = "builtin.agent", Kind = PageSlotKind.Agent, TemplateId = "list", Title = "Agent activity", ShortTitle = "Agent", IconToken = "agent" },
        new PageSlot { PageId = "builtin.widgets", Kind = PageSlotKind.Widgets, TemplateId = "calendar", Title = "Widgets", ShortTitle = "Widgets", IconToken = "calendar" },
    ];

    /// <summary>Localized page titles (six); missing entries fall back to the defaults.</summary>
    public IReadOnlyList<string> PageTitles { get; init; } = DefaultPageTitles;

    /// <summary>Optional short labels for the detached strip (six); falls back to <see cref="PageTitles"/>.</summary>
    public IReadOnlyList<string>? ShortTitles { get; init; }

    /// <summary>Default English page titles.</summary>
    public static string[] DefaultPageTitles { get; } =
    [
        "Control center", "Now playing", "Notifications", "Clipboard", "Agent activity", "Widgets",
    ];

    private string PageSlotTitleFallback(int page)
        => DefaultPageTitles[Math.Clamp(page, 0, DefaultPageTitles.Length - 1)];

    /// <summary>Returns the title for a page index (clamped, never null).</summary>
    public string Title(int page)
    {
        if (HasCustomPageSlots && PageSlotAt(page) is { } slot)
        {
            return string.IsNullOrWhiteSpace(slot.Title) ? PageSlotTitleFallback(page) : slot.Title;
        }

        if (PluginPageAt(page) is { } plugin)
        {
            return plugin.Title;
        }

        var index = Math.Clamp(page, 0, DefaultPageTitles.Length - 1);
        var titles = PageTitles;
        return titles is not null && index < titles.Count && !string.IsNullOrEmpty(titles[index])
            ? titles[index]!
            : DefaultPageTitles[index];
    }

    /// <summary>Returns the short strip label for a page index (clamped, never null).</summary>
    public string ShortTitle(int page)
    {
        if (HasCustomPageSlots && PageSlotAt(page) is { } customSlot)
        {
            return string.IsNullOrWhiteSpace(customSlot.ShortTitle) ? customSlot.Title : customSlot.ShortTitle;
        }

        if (PluginPageAt(page) is { } plugin)
        {
            return string.IsNullOrWhiteSpace(plugin.ShortTitle) ? plugin.Title : plugin.ShortTitle;
        }

        var index = Math.Clamp(page, 0, DefaultPageTitles.Length - 1);
        var titles = ShortTitles;
        return titles is not null && index < titles.Count && !string.IsNullOrEmpty(titles[index])
            ? titles[index]!
            : Title(index);
    }
}

/// <summary>Home page (control center) model.</summary>
public sealed record HomePageModel
{
    /// <summary>Vertical scroll offset in DIP (content can exceed the 232 DIP body at large font scales).</summary>
    public double ScrollOffset { get; init; }

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

    /// <summary>Whether a real volume level is known (false renders an em dash, not a fake value).</summary>
    public bool HasVolume { get; init; } = true;

    /// <summary>Brightness level 0..100.</summary>
    public int Brightness { get; init; } = 78;

    /// <summary>Whether a real brightness level is known (false renders an em dash).</summary>
    public bool HasBrightness { get; init; } = true;

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

    /// <summary>Default quick toggles (Wi-Fi on, Bluetooth off, Focus unavailable, Dark on).</summary>
    public static HomeToggleModel[] DefaultToggles { get; } =
    [
        new(IconKind.Wifi, "Wi-Fi", true),
        new(IconKind.Bluetooth, "Bluetooth", false),
        new(IconKind.Focus, "Focus", false, IsEnabled: false, DisabledReason: "Focus sessions are unavailable"),
        new(IconKind.Moon, "Dark", true),
    ];
}

/// <summary>
/// One home quick toggle. <paramref name="IsEnabled"/> = false is drawn greyed and has no hit target,
/// so an unsupported OS control never looks like a live button that silently does nothing.
/// </summary>
public readonly record struct HomeToggleModel(
    IconKind Icon,
    string Label,
    bool IsOn,
    bool IsEnabled = true,
    string? DisabledReason = null);

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

    /// <summary>Whether a real timeline is known (false hides the progress bar).</summary>
    public bool HasTimeline { get; init; } = true;

    /// <summary>Optional artwork accent hex.</summary>
    public string? AccentHex { get; init; }

    /// <summary>Decoded-artwork source bytes (PNG/JPEG) when the platform supplied a cover.</summary>
    public byte[]? Artwork { get; init; }

    /// <summary>Stable artwork cache key (track identity); null disables artwork caching.</summary>
    public string? ArtworkKey { get; init; }
}

/// <summary>Expanded media page model.</summary>
public sealed record MediaPageModel
{
    /// <summary>Vertical scroll offset in DIP (content can exceed the 232 DIP body at large font scales).</summary>
    public double ScrollOffset { get; init; }

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

    /// <summary>
    /// Karaoke sweep of the current line (0..1) from the KRC word timeline; negative means the line has
    /// no per-word timing and must be painted as a plain line.
    /// </summary>
    public double LyricHighlight { get; init; } = -1;

    /// <summary>
    /// Whether this track carries a karaoke timeline at all. The split style must follow the TRACK, not the
    /// instantaneous sweep value: a single frame that arrived without one used to repaint the whole line in
    /// the plain foreground colour ("整句突然变白一下").
    /// </summary>
    public bool HasKaraokeSweep { get; init; }

    /// <summary>Elapsed time text.</summary>
    public string? PositionText { get; init; }

    /// <summary>Duration/remaining text.</summary>
    public string? DurationText { get; init; }

    /// <summary>Progress percentage 0..100.</summary>
    public double ProgressPercent { get; init; } = 46;

    /// <summary>Whether the session is playing.</summary>
    public bool IsPlaying { get; init; } = true;

    /// <summary>Whether a real timeline is known (false hides the seek row).</summary>
    public bool HasTimeline { get; init; } = true;

    /// <summary>Whether the source can seek; false paints the row read-only (no dead slider).</summary>
    public bool CanSeek { get; init; } = true;

    /// <summary>Optional artwork accent hex.</summary>
    public string? AccentHex { get; init; }

    /// <summary>Decoded-artwork source bytes (PNG/JPEG) when the platform supplied a cover.</summary>
    public byte[]? Artwork { get; init; }

    /// <summary>Stable artwork cache key (track identity); null disables artwork caching.</summary>
    public string? ArtworkKey { get; init; }

    /// <summary>System output volume 0..100, or null when unknown.</summary>
    public int? VolumePercent { get; init; }

    /// <summary>Whether a real output volume is known (false renders an em dash).</summary>
    public bool HasVolume { get; init; }

    /// <summary>Whether the system output is muted (the media volume button toggles this).</summary>
    public bool IsMuted { get; init; }

    /// <summary>Output device label.</summary>
    public string? OutputName { get; init; }

    /// <summary>Queue count (0 shows the light "no queue" placeholder instead of a count).</summary>
    public int QueueCount { get; init; }

    /// <summary>Light localized placeholder for the output slot when the source exposes no endpoint.</summary>
    public string? OutputPlaceholder { get; init; }

    /// <summary>Whether the output picker is usable (false: the row is greyed, inert and says why).</summary>
    public bool OutputAvailable { get; init; }

    /// <summary>Whether the queue view is usable (false: the row is greyed, inert and says why).</summary>
    public bool QueueAvailable { get; init; }

    /// <summary>Light localized placeholder for the queue slot when the source exposes no queue.</summary>
    public string? QueuePlaceholder { get; init; }

    /// <summary>Short localized queue label ("Queue" / "队列").</summary>
    public string? QueueLabel { get; init; }

    /// <summary>Light caption inside the lyric card when the source has no lyric line.</summary>
    public string? NoLyricsText { get; init; }

    /// <summary>Empty-state title (no session).</summary>
    public string? EmptyTitle { get; init; }

    /// <summary>Empty-state hint.</summary>
    public string? EmptyHint { get; init; }
}

/// <summary>
/// Expanded notifications page (page 2): a scrollable list of notification cards. Cards are 72 DIP at
/// scale 1.0 and carry an app glyph, app pill, bold title, two body lines, a bottom-right timestamp and
/// action chips; extra cards scroll by <see cref="ScrollOffset"/>.
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

    /// <summary>Localized dismiss-chip label ("Close" / "关闭").</summary>
    public string? DismissLabel { get; init; }

    /// <summary>Localized fallback label for the copy-code chip ("Copy code" / "复制验证码").</summary>
    public string? CopyCodeLabel { get; init; }

    /// <summary>Localized clear-all label; null hides the header action.</summary>
    public string? ClearLabel { get; init; }
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

    /// <summary>Localized chip caption for <see cref="VerificationCode"/> ("Code 418302"); falls back to the code.</summary>
    public string? CodeChipLabel { get; init; }

    /// <summary>Optional app accent colour hex for the leading glyph.</summary>
    public string? AccentHex { get; init; }

    /// <summary>Optional leading icon override (defaults to the bell).</summary>
    public IconKind? Icon { get; init; }

    /// <summary>Whether the row is unread (informational).</summary>
    public bool IsUnread { get; init; } = true;

    /// <summary>Real app logo bytes for the leading glyph; null falls back to the bell/icon.</summary>
    public byte[]? AppIconPng { get; init; }

    /// <summary>Stable cache key for <see cref="AppIconPng"/>.</summary>
    public string? AppIconKey { get; init; }
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

    /// <summary>Image preview.</summary>
    Image,

    /// <summary>File list (CF_HDROP).</summary>
    Files,
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

    /// <summary>Localized label for the active type filter (null hides the header chip).</summary>
    public string? FilterLabel { get; init; }

    /// <summary>Localized label for the search button (null hides the header chip).</summary>
    public string? SearchLabel { get; init; }
}

/// <summary>One clipboard history row.</summary>
public sealed record ClipboardItemModel
{
    /// <summary>Preview text (single line, ellipsised by the renderer).</summary>
    public string? Preview { get; init; }

    /// <summary>Relative time label ("2 min").</summary>
    public string? TimeText { get; init; }

    /// <summary>Optional localized type label override ("Text"/"Code"/"Link"/"Image"/"File").</summary>
    public string? TypeLabel { get; init; }

    /// <summary>PNG thumbnail for an image row; null falls back to the type icon.</summary>
    public byte[]? ImagePng { get; init; }

    /// <summary>Stable cache key for <see cref="ImagePng"/>.</summary>
    public string? ImageKey { get; init; }

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
    /// <summary>Vertical scroll offset in DIP (widgets content can exceed the 232 DIP body at large scales).</summary>
    public double ScrollOffset { get; init; }

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

    /// <summary>
    /// Weather glyph chosen from the WMO weather code (Sun/Moon/CloudSun of the existing icon set). Null
    /// keeps the historical cloud-sun glyph, so models built before this field render unchanged.
    /// </summary>
    public IconKind? WeatherIcon { get; init; }

    /// <summary>Localized "unavailable" caption painted when the weather source has no data.</summary>
    public string? WeatherUnavailableText { get; init; }

    /// <summary>System stat rows (up to three).</summary>
    public IReadOnlyList<WidgetStatModel> Stats { get; init; } = [];

    /// <summary>Localized "unavailable" caption painted when no system stat could be sampled.</summary>
    public string? StatsEmptyText { get; init; }

    /// <summary>Task card title ("Today").</summary>
    public string? TasksTitle { get; init; }

    /// <summary>Task progress label ("1/3 done"); supplied pre-formatted to avoid per-frame strings.</summary>
    public string? TasksProgressText { get; init; }

    /// <summary>Task rows (up to three are painted).</summary>
    public IReadOnlyList<WidgetTaskModel> Tasks { get; init; } = [];

    /// <summary>Localized "unavailable" caption painted when no task source is available.</summary>
    public string? TasksEmptyText { get; init; }

    /// <summary>Localized "N more" line painted after the last visible row when the file has more tasks.</summary>
    public string? TasksOverflowText { get; init; }

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

