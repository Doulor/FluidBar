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

    /// <summary>Fixed per-monitor host surface width (488 DIP) for every mode.</summary>
    public const double HostSurfaceWidth = ExpandedWidth + (2 * WindowPadding);

    /// <summary>Float/flush host surface width (1263 DIP): keeps the established host envelope; the card itself is capped at 810 DIP.</summary>
    public const double HostSurfaceFloatWidth = 1263;

    /// <summary>
    /// Fixed float/flush host surface height (488x384 DIP): shell envelope 332 + the detached
    /// Page Archipelago band 52 (gap 8 + strip 32 + bottom padding 12). The host surface is the
    /// only thing the layered window sizes itself to; the shell stays 440x284.
    /// </summary>
    public const double HostSurfaceFloatHeight = ExpandedHeight + (2 * WindowPadding) + ArchipelagoBandHeight;

    /// <summary>
    /// Fixed dock host surface height (488x444 DIP): the float surface plus the 52 DIP dock strip
    /// and its 8 DIP gap above the expanded panel (52 + 8 = 60).
    /// </summary>
    public const double HostSurfaceDockHeight = HostSurfaceFloatHeight + DockStripHeight + DockExpandedGap;

    /// <summary>Legacy shell-envelope host height (488x332 DIP) before the detached archipelago band.</summary>
    /// <remarks>
    /// Kept for source compatibility with the pre-archipelago App/Platform seams. New code must use
    /// <see cref="HostSurface"/> (float/flush <see cref="HostSurfaceFloatHeight"/>, dock
    /// <see cref="HostSurfaceDockHeight"/>) and <see cref="WindowPlacement.WindowSizeDip"/>.
    /// </remarks>
    public const double WindowHeight = ExpandedHeight + (2 * WindowPadding);

    /// <summary>Fixed per-monitor host surface width (488 DIP); alias of <see cref="HostSurfaceWidth"/>.</summary>
    public const double WindowWidth = HostSurfaceWidth;

    /// <summary>Dock strip height (spec 3.1: 52).</summary>
    public const double DockStripHeight = 52;

    /// <summary>Top/side transparent padding of the host surface (shadow + antialiasing bleed).</summary>
    public const double HostTopPadding = WindowPadding;

    /// <summary>
    /// Bottom padding below the detached archipelago strip (36 DIP): 24 DIP shadow bleed plus a
    /// 12 DIP safety margin. The host is asymmetric on purpose: 24 top/side, 36 bottom.
    /// </summary>
    public const double HostBottomPadding = WindowPadding + 12;

    /// <summary>
    /// Gap between the expanded shell bottom and the detached archipelago strip (8 DIP; the
    /// prototype2 <c>.mini-rail</c> sits at <c>top: calc(100% + 8px)</c>).
    /// </summary>
    public const double ArchipelagoGap = 8;

    /// <summary>
    /// Detached archipelago strip height (32 DIP; the prototype2 <c>.mini-rail</c> height that
    /// centres the 26 DIP neighbour circles and the 28 DIP current capsule).
    /// </summary>
    public const double ArchipelagoStripHeight = 32;

    /// <summary>
    /// Total bottom allowance of the float/flush host surface below the expanded shell
    /// (gap 8 + strip 32 + bottom padding 36 = 76 DIP).
    /// </summary>
    public const double HostFloatBottomAllowance = ArchipelagoGap + ArchipelagoStripHeight + HostBottomPadding;

    /// <summary>
    /// Total bottom allowance of the dock host surface below the expanded panel
    /// (dock gap 8 + dock strip 52 + archipelago gap 8 + strip 32 + bottom padding 36 = 136 DIP).
    /// </summary>
    public const double HostDockBottomAllowance = DockExpandedGap + DockStripHeight
        + ArchipelagoGap + ArchipelagoStripHeight + HostBottomPadding;

    // ---- Page Archipelago (detached mini-islands below the expanded shell) -------------------

    /// <summary>Number of detached mini-islands (one per pager page).</summary>
    public const int ArchipelagoSlotCount = 6;

    /// <summary>Inactive mini-island width (48 DIP).</summary>
    public const double ArchipelagoMiniWidth = 48;

    /// <summary>Inactive mini-island height (26 DIP).</summary>
    public const double ArchipelagoMiniHeight = 26;

    /// <summary>Active mini-island width (72 DIP).</summary>
    public const double ArchipelagoActiveWidth = 72;

    /// <summary>Active mini-island height (30 DIP).</summary>
    public const double ArchipelagoActiveHeight = 30;

    /// <summary>Gap between mini-islands (8 DIP).</summary>
    public const double ArchipelagoSlotGap = 8;

    /// <summary>Distance between two consecutive mini-island origins (48 + 8 = 56 DIP).</summary>
    public const double ArchipelagoSlotPitch = ArchipelagoMiniWidth + ArchipelagoSlotGap;

    /// <summary>Mini-island corner radius (13 DIP).</summary>
    public const double ArchipelagoRadius = 13;

    /// <summary>
    /// Total strip width with exactly one active island: 6x48 + 5x8 + (72-48) = 352 DIP.
    /// </summary>
    public const double ArchipelagoTrackWidth = (ArchipelagoSlotCount * ArchipelagoMiniWidth)
        + ((ArchipelagoSlotCount - 1) * ArchipelagoSlotGap)
        + (ArchipelagoActiveWidth - ArchipelagoMiniWidth);

    /// <summary>Height of the bottom hot zone that reveals the archipelago (12 DIP).</summary>
    public const double ArchipelagoHotZoneHeight = 12;

    /// <summary>Dwell in the hot zone before the archipelago reveals (300 ms).</summary>
    public const int ArchipelagoRevealDelayMs = 300;

    /// <summary>Idle time after the last drag/wheel activity before the archipelago hides (1200 ms).</summary>
    public const int ArchipelagoHideDelayMs = 900;

    /// <summary>
    /// Total vertical band the detached archipelago adds below the legacy 332 DIP host
    /// (gap 8 + strip 32 + bottom padding 12 = 52 DIP).
    /// </summary>
    public const double ArchipelagoBandHeight = ArchipelagoGap + ArchipelagoStripHeight + (HostBottomPadding - WindowPadding);

    /// <summary>Dock strip width (full shell width).</summary>
    public const double DockStripWidth = ExpandedWidth;

    /// <summary>Gap between the dock strip and the expanded panel above it.</summary>
    public const double DockExpandedGap = 8;

    /// <summary>Default float vertical offset from the work-area edge.</summary>
    public const double DefaultFloatOffsetY = 12;

    /// <summary>Default dock host-bottom offset above the work-area bottom edge (4 DIP).</summary>
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

    // Overflow (+N) badge tokens (spec 2.5: the +N chip on the primary pill).
    /// <summary>Width reserved at the right edge of the compact pill when the +N badge is visible.</summary>
    public const double OverflowBadgeReserve = 26;

    /// <summary>Painted width of the +N chip (fits inside <see cref="OverflowBadgeReserve"/>).</summary>
    public const double OverflowBadgeWidth = 22;

    /// <summary>Painted height of the +N chip.</summary>
    public const double OverflowBadgeHeight = 16;

    /// <summary>Maximum compact-pill width while split previews are visible (DIP). The preview row
    /// needs two capsules beside the pill; over-long lyric pills are ellipsised instead of pushing a
    /// capsule off the fixed host surface.</summary>
    public const double SplitPrimaryMaxWidth = 300;

    private static readonly VisualSize IdleSize = new(132, 36);
    private static readonly VisualSize MediaSize = new(226, 40);
    private static readonly VisualSize HudSize = new(208, 40);
    // NetSpeed-Dynamic message card (screenshot4.png measured): 360×64 DIP, ~21 DIP radius,
    // i.e. clearly squarer than the 35 DIP stadium a 70-high card would produce.
    private static readonly VisualSize NotificationSize = new(360, 64);

    /// <summary>Minimum notification-card width (short, unclipped messages stay here).</summary>
    /// <summary>
    /// Widest the media pill may grow when it shows a lyric line. The lyric form widens past the expanded
    /// shell so a whole line fits instead of being ellipsised; this is the ceiling for that growth
    /// (expanded width + 1/2, requested 2026-10-26 because lyric lines were still being cut off at 440).
    /// </summary>
    public const double MediaMaxWidth = ExpandedWidth * 1.5;

    public const double NotificationMinWidth = 360;

    /// <summary>Maximum notification-card width after the requested -1/3 from 1215 (810 DIP).</summary>
    public const double NotificationMaxWidth = 810;

    /// <summary>Squarer notification-card radius measured from the reference screenshot (~43 px at 2×).</summary>
    public const double NotificationCornerRadius = 21;
    private static readonly VisualSize ClipboardSize = new(264, 36);
    // Prototype2 .island[data-view="agent"]: 236×36 with an action/session title, not the brand name.
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

    /// <summary>Minimum supported island font scale (settings → appearance).</summary>
    public const double MinFontScale = 0.8;

    /// <summary>Maximum supported island font scale (settings → appearance).</summary>
    public const double MaxFontScale = 1.6;

    /// <summary>
    /// Font-scale aware compact size: the prototype token is multiplied by the clamped scale and capped
    /// at the expanded width/height, so the pill grows with its wider text (no truncation or overlap)
    /// while still fitting the fixed 488 DIP host surface. Scale 1.0 returns the token unchanged.
    /// </summary>
    /// <param name="kind">Activity kind (null = idle).</param>
    /// <param name="mode">Presentation mode.</param>
    /// <param name="fontScale">Island font scale (0.8–1.6).</param>
    public static VisualSize CompactSizeFor(Activities.ActivityKind? kind, IslandMode mode, double fontScale)
    {
        var size = CompactSize(kind, mode);
        var scale = double.IsFinite(fontScale) ? Math.Clamp(fontScale, MinFontScale, MaxFontScale) : 1.0;
        return Math.Abs(scale - 1.0) < 1e-9
            ? size
            : new VisualSize(
                Math.Min(ExpandedWidth, Math.Round(size.Width * scale, 2)),
                Math.Min(ExpandedHeight, Math.Round(size.Height * scale, 2)));
    }

    /// <summary>Expanded size; uniform for every page (lead decision).</summary>
    public static VisualSize ExpandedSize() => new(ExpandedWidth, ExpandedHeight);

    /// <summary>
    /// Corner radii for a shell form and mode. Compact pills are stadiums (h/2 capped at 44);
    /// flush has square top corners; dock uses the 12/16 radii.
    /// </summary>
    public static CornerRadii RadiiFor(ShellForm form, IslandMode mode, double height,
        double expandedRadius = ExpandedRadius, Activities.ActivityKind? kind = null)
    {
        if (kind == Activities.ActivityKind.Notification && mode == IslandMode.Float
            && (form is ShellForm.Compact or ShellForm.Peek))
        {
            return CornerRadii.Uniform(NotificationCornerRadius);
        }

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
                _ => CornerRadii.Uniform(expandedRadius),
            },
            _ => CornerRadii.Uniform(compactRadius),
        };
    }

    /// <summary>Concentric card radius: <c>outer - inset</c> clamped at zero (spec 3.1).</summary>
    public static double ConcentricRadius(double outerRadius, double inset)
        => Math.Max(0, outerRadius - inset);
}
