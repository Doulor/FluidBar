namespace FluidBar.Core.Layout;

/// <summary>Size in DIP.</summary>
/// <param name="Width">Width.</param>
/// <param name="Height">Height.</param>
public readonly record struct VisualSize(double Width, double Height);

/// <summary>
/// Geometry/spacing tokens (spec 3.1, 3.2, prototype README).
/// All values are DIP at 96 DPI; the expanded shell is a uniform 440x284 for every page
/// (lead decision 2026-10-18: per-page heights from spec 3.1 are superseded).
/// </summary>
public static class LayoutTokens
{
    /// <summary>Expanded shell width (content 400 DIP + 2 × 20 DIP horizontal inset).</summary>
    public const double ExpandedWidth = 440;

    /// <summary>Uniform expanded shell height for all pages.</summary>
    public const double ExpandedHeight = 284;

    /// <summary>
    /// Left vertical navigation rail width. <b>Superseded</b> by the horizontal pager
    /// (<see cref="Interaction.PagePagerPolicy"/>, user feedback 2026-10-18); kept for API
    /// compatibility only and no longer consumed by layout.
    /// </summary>
    [Obsolete("The left rail was rejected (2026-10-18). Navigation is the horizontal pager (PagePagerPolicy); this token is kept for API compatibility only.")]
    public const double RailWidth = 48;

    /// <summary>Expanded content width in DIP (440 − 2 × 20 = 400).</summary>
    public const double ContentWidth = ExpandedWidth - (2 * ContentHorizontalInset);

    /// <summary>Horizontal inset of the expanded content from the shell edge (20 DIP).</summary>
    public const double ContentHorizontalInset = 20;

    /// <summary>Vertical inset of the expanded content from the shell edge (16 DIP).</summary>
    public const double ContentVerticalInset = 16;

    /// <summary>Legacy alias of <see cref="ContentVerticalInset"/>.</summary>
    public const double ContentInset = ContentVerticalInset;

    /// <summary>Usable content width (400 DIP; the whole content rect is usable now that the rail is gone).</summary>
    public const double ContentUsableWidth = ContentWidth;

    /// <summary>Usable content height (284 − 2 × 16 = 252 DIP).</summary>
    public const double ContentUsableHeight = ExpandedHeight - (2 * ContentVerticalInset);

    /// <summary>Shadow/antialiasing bleed around the shell on every side.</summary>
    public const double WindowPadding = 24;

    /// <summary>Fixed per-monitor host window width (440 + 2*24).</summary>
    public const double WindowWidth = ExpandedWidth + (2 * WindowPadding);

    /// <summary>Fixed per-monitor host window height (284 + 2*24).</summary>
    public const double WindowHeight = ExpandedHeight + (2 * WindowPadding);

    /// <summary>Dock strip height (spec 3.1: 52).</summary>
    public const double DockStripHeight = 52;

    /// <summary>Dock strip width (full shell width).</summary>
    public const double DockStripWidth = ExpandedWidth;

    /// <summary>Gap between the dock strip and the expanded panel above it.</summary>
    public const double DockExpandedGap = 8;

    /// <summary>Default float vertical offset from the work-area edge.</summary>
    public const double DefaultFloatOffsetY = 12;

    /// <summary>Default dock vertical offset from the work-area bottom edge.</summary>
    public const double DefaultDockOffsetY = 4;

    /// <summary>Peek width delta (+10 DIP).</summary>
    public const double PeekDeltaWidth = 10;

    /// <summary>Peek height delta (+6 DIP).</summary>
    public const double PeekDeltaHeight = 6;

    /// <summary>Compact inset (spec: 12).</summary>
    public const double CompactInset = 12;

    /// <summary>Group inset (spec: 20).</summary>
    public const double GroupInset = 20;

    /// <summary>Row gap (spec: 4).</summary>
    public const double GapRow = 4;

    /// <summary>Line gap (spec: 8).</summary>
    public const double GapLine = 8;

    /// <summary>Artwork to text gap (spec: 10).</summary>
    public const double GapArtToText = 10;

    /// <summary>Group gap (spec: 16).</summary>
    public const double GapGroup = 16;

    /// <summary>Minimum pointer hit target (spec: 32).</summary>
    public const double HitMin = 32;

    /// <summary>Transport control hit target (spec: 36).</summary>
    public const double HitTransport = 36;

    /// <summary>Focus ring width (spec: 2).</summary>
    public const double FocusRingWidth = 2;

    /// <summary>Focus ring offset (spec: 2).</summary>
    public const double FocusRingOffset = 2;

    /// <summary>Hairline stroke width (spec: 1).</summary>
    public const double HairlineWidth = 1;

    /// <summary>Default shell fill opacity (spec: 0.96, min 0.85 for text-bearing states).</summary>
    public const double FillOpacity = 0.96;

    /// <summary>Minimum fill opacity for text-bearing states.</summary>
    public const double MinFillOpacity = 0.85;

    /// <summary>Shadow offset Y (spec: 8).</summary>
    public const double ShadowOffsetY = 8;

    /// <summary>Shadow blur radius (spec: 24).</summary>
    public const double ShadowBlur = 24;

    /// <summary>Shadow opacity (spec: 0.35).</summary>
    public const double ShadowOpacity = 0.35;

    /// <summary>Dock shadow offset Y (spec: 4).</summary>
    public const double DockShadowOffsetY = 4;

    /// <summary>Dock shadow blur (spec: 12).</summary>
    public const double DockShadowBlur = 12;

    /// <summary>Dock shadow opacity (spec: 0.28).</summary>
    public const double DockShadowOpacity = 0.28;

    /// <summary>Expanded shell radius (spec: 28).</summary>
    public const double ExpandedRadius = 28;

    /// <summary>Compact radius cap (spec: 44).</summary>
    public const double CompactRadiusCap = 44;

    /// <summary>Flush mode bottom radius (spec: 22).</summary>
    public const double FlushBottomRadius = 22;

    /// <summary>Dock compact radius (spec: 12).</summary>
    public const double DockCompactRadius = 12;

    /// <summary>Dock expanded radius (spec: 16).</summary>
    public const double DockExpandedRadius = 16;

    /// <summary>Card radius (spec: 12).</summary>
    public const double CardRadius = 12;

    /// <summary>Row radius (spec: 8).</summary>
    public const double RowRadius = 8;

    /// <summary>Button radius (spec: 10).</summary>
    public const double ButtonRadius = 10;

    /// <summary>Small artwork radius (spec: 8).</summary>
    public const double ArtSmallRadius = 8;

    /// <summary>Large artwork radius (spec: 12).</summary>
    public const double ArtLargeRadius = 12;

    /// <summary>Rail item visual size. <b>Superseded</b> by the horizontal pager; unused.</summary>
    [Obsolete("The left rail was rejected (2026-10-18); kept for API compatibility only.")]
    public const double RailItemSize = 32;

    /// <summary>Rail item hit size. <b>Superseded</b>; unused.</summary>
    [Obsolete("The left rail was rejected (2026-10-18); kept for API compatibility only.")]
    public const double RailItemHitSize = 36;

    /// <summary>Rail item gap. <b>Superseded</b>; unused.</summary>
    [Obsolete("The left rail was rejected (2026-10-18); kept for API compatibility only.")]
    public const double RailItemGap = 4;

    /// <summary>Rail vertical padding. <b>Superseded</b>; unused.</summary>
    [Obsolete("The left rail was rejected (2026-10-18); kept for API compatibility only.")]
    public const double RailPadding = 12;

    /// <summary>Rail item icon size. <b>Superseded</b>; unused.</summary>
    [Obsolete("The left rail was rejected (2026-10-18); kept for API compatibility only.")]
    public const double RailIconSize = 18;

    /// <summary>Hidden-state hover strip height (spec: 10).</summary>
    public const double HiddenHoverStripHeight = 10;

    /// <summary>Maximum ghost layers (spec 2.5: 2).</summary>
    public const int MaxGhosts = 2;

    // Ghost stack tokens (spec 2.5).
    /// <summary>First ghost vertical offset (-6 DIP).</summary>
    public const double GhostOffsetY1 = -6;

    /// <summary>Second ghost vertical offset (-12 DIP).</summary>
    public const double GhostOffsetY2 = -12;

    /// <summary>First ghost scale (0.96).</summary>
    public const double GhostScale1 = 0.96;

    /// <summary>Second ghost scale (0.92).</summary>
    public const double GhostScale2 = 0.92;

    /// <summary>First ghost opacity (0.55).</summary>
    public const double GhostOpacity1 = 0.55;

    /// <summary>Second ghost opacity (0.35).</summary>
    public const double GhostOpacity2 = 0.35;

    private static readonly VisualSize IdleSize = new(132, 36);
    private static readonly VisualSize MediaSize = new(226, 40);
    private static readonly VisualSize HudSize = new(208, 40);
    private static readonly VisualSize NotificationSize = new(248, 36);
    private static readonly VisualSize ClipboardSize = new(264, 36);
    private static readonly VisualSize AgentSize = new(236, 36);
    private static readonly VisualSize TimerSize = new(190, 36);
    private static readonly VisualSize BatterySize = new(208, 36);
    private static readonly VisualSize LookupSize = new(480, 44);
    private static readonly VisualSize LockKeysSize = new(180, 36);
    private static readonly VisualSize DeviceSize = new(240, 44);
    private static readonly VisualSize BluetoothSize = new(280, 44);
    private static readonly VisualSize FocusSize = new(220, 36);
    private static readonly VisualSize ProgressSize = new(236, 36);

    /// <summary>Compact size of an activity kind; <see langword="null"/> means the idle pill.</summary>
    public static VisualSize CompactSize(Activities.ActivityKind? kind) => kind switch
    {
        null or Activities.ActivityKind.Idle => IdleSize,
        Activities.ActivityKind.Media => MediaSize,
        Activities.ActivityKind.Volume => HudSize,
        Activities.ActivityKind.Brightness => HudSize,
        Activities.ActivityKind.Notification => NotificationSize,
        Activities.ActivityKind.Clipboard => ClipboardSize,
        Activities.ActivityKind.Agent => AgentSize,
        Activities.ActivityKind.Timer => TimerSize,
        Activities.ActivityKind.BatteryLow => BatterySize,
        Activities.ActivityKind.Lookup => LookupSize,
        Activities.ActivityKind.LockKeys => LockKeysSize,
        Activities.ActivityKind.Device => DeviceSize,
        Activities.ActivityKind.Bluetooth => BluetoothSize,
        Activities.ActivityKind.Focus => FocusSize,
        Activities.ActivityKind.Progress => ProgressSize,
        _ => IdleSize,
    };

    /// <summary>Compact size for a mode: dock always uses the 440x52 strip.</summary>
    public static VisualSize CompactSize(Activities.ActivityKind? kind, IslandMode mode)
        => mode == IslandMode.Dock ? new VisualSize(DockStripWidth, DockStripHeight) : CompactSize(kind);

    /// <summary>Expanded size; uniform for every page (lead decision).</summary>
    public static VisualSize ExpandedSize() => new(ExpandedWidth, ExpandedHeight);

    /// <summary>
    /// Corner radii for a shell form and mode. Compact pills are stadiums (h/2 capped at 44);
    /// flush has square top corners; dock uses the 12/16 radii.
    /// </summary>
    public static CornerRadii RadiiFor(ShellForm form, IslandMode mode, double height)
    {
        var compactRadius = Math.Min(height / 2, CompactRadiusCap);
        return form switch
        {
            ShellForm.Hidden => CornerRadii.Zero,
            ShellForm.Compact or ShellForm.Peek when mode == IslandMode.Dock => CornerRadii.Uniform(DockCompactRadius),
            ShellForm.Compact or ShellForm.Peek when mode == IslandMode.Flush
                => new CornerRadii(0, 0, FlushBottomRadius, FlushBottomRadius),
            ShellForm.Compact or ShellForm.Peek or ShellForm.Closing => CornerRadii.Uniform(compactRadius),
            ShellForm.Expanded => mode switch
            {
                IslandMode.Dock => CornerRadii.Uniform(DockExpandedRadius),
                IslandMode.Flush => new CornerRadii(0, 0, FlushBottomRadius, FlushBottomRadius),
                _ => CornerRadii.Uniform(ExpandedRadius),
            },
            _ => CornerRadii.Uniform(compactRadius),
        };
    }

    /// <summary>Concentric card radius: <c>outer - inset</c> clamped at zero (spec 3.1).</summary>
    public static double ConcentricRadius(double outerRadius, double inset)
        => Math.Max(0, outerRadius - inset);
}
