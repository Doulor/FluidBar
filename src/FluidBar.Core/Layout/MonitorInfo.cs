namespace FluidBar.Core.Layout;

/// <summary>
/// Immutable monitor snapshot. All physical values are Win32 pixels; the layout math uses the
/// <see cref="Scale"/>-normalized DIP work area. The core never calls Win32: Platform.Windows
/// fills this record from <c>MonitorInfo.rcWork</c>/<c>GetDpiForMonitor</c>.
/// </summary>
public sealed record MonitorInfo
{
    /// <summary>Stable monitor id (spec 2.6: <c>\\.\DISPLAYn</c> + EDID hash).</summary>
    public required string Id { get; init; }

    /// <summary>Physical monitor bounds in virtual-screen pixels.</summary>
    public required PixelRect Bounds { get; init; }

    /// <summary>Physical work area (taskbar excluded) in virtual-screen pixels.</summary>
    public required PixelRect WorkArea { get; init; }

    /// <summary>Effective DPI (96 = 100 %, 120 = 125 %, 144 = 150 %, 192 = 200 %).</summary>
    public uint Dpi { get; init; } = 96;

    /// <summary>Whether this is the primary monitor.</summary>
    public bool IsPrimary { get; init; }

    /// <summary>DIP scale factor (<see cref="Dpi"/> / 96).</summary>
    public double Scale => Dpi / 96.0;

    /// <summary>Work-area width in DIP.</summary>
    public double WorkAreaWidthDip => WorkArea.Width / Scale;

    /// <summary>Work-area height in DIP.</summary>
    public double WorkAreaHeightDip => WorkArea.Height / Scale;

    /// <summary>Work area expressed as a rectangle in work-area-relative DIP coordinates.</summary>
    public VisualRect WorkAreaDip => new(0, 0, WorkAreaWidthDip, WorkAreaHeightDip);

    /// <summary>Creates a monitor snapshot; bounds default to the work area when omitted.</summary>
    /// <param name="id">Stable monitor id.</param>
    /// <param name="workArea">Physical work area.</param>
    /// <param name="dpi">Effective DPI (defaults to 96).</param>
    /// <param name="bounds">Physical monitor bounds (defaults to <paramref name="workArea"/>).</param>
    /// <param name="isPrimary">Whether this monitor is primary.</param>
    public static MonitorInfo Create(
        string id,
        PixelRect workArea,
        uint dpi = 96,
        PixelRect? bounds = null,
        bool isPrimary = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentOutOfRangeException.ThrowIfLessThan(dpi, 48u);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(dpi, 768u);
        return new MonitorInfo
        {
            Id = id,
            WorkArea = workArea,
            Bounds = bounds ?? workArea,
            Dpi = dpi,
            IsPrimary = isPrimary,
        };
    }
}

/// <summary>Point in DIP, work-area relative.</summary>
/// <param name="X">Horizontal position.</param>
/// <param name="Y">Vertical position.</param>
public readonly record struct VisualPoint(double X, double Y);

/// <summary>
/// The single DIP -&gt; physical-pixel conversion (spec 5.5). Used only for window placement
/// (create/anchor/display change), never inside the animation path.
/// </summary>
public static class WindowPlacementConverter
{
    /// <summary>Transparent margin around the shell for shadows/antialiasing (spec 3.1: 24 DIP).</summary>
    public const double DefaultPadding = 24;

    /// <summary>
    /// Converts a work-area-relative DIP rectangle to absolute physical pixels.
    /// </summary>
    public static PixelRect ToPhysical(in VisualRect rect, MonitorInfo monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        var scale = monitor.Scale;
        return new PixelRect(
            Round(monitor.WorkArea.X + (rect.X * scale)),
            Round(monitor.WorkArea.Y + (rect.Y * scale)),
            Ceil(rect.Width * scale),
            Ceil(rect.Height * scale));
    }

    /// <summary>
    /// Places the fixed-size per-monitor host surface for a measured shell, using the mode's
    /// <see cref="Layout.HostSurface"/> (float/flush 488x384, dock 488x444). The shell sits at
    /// <see cref="WindowPlacement.ShellLocalOffset"/> inside the host; bottom/dock anchors are
    /// host-clamped so the detached archipelago strip stays inside the work area.
    /// <para>
    /// The padding is applied to <b>both</b> axes exactly once: the v1 bug (F-04) subtracted the
    /// bleed from X only, shifting every top-anchored pill +14 DIP down and clipping bottom anchors.
    /// </para>
    /// </summary>
    /// <param name="shell">Shell rectangle in work-area-relative DIP.</param>
    /// <param name="monitor">Target monitor.</param>
    /// <param name="hostSurface">Fixed host surface for the current mode.</param>
    /// <param name="form">Visual form (selects the dock compact/expanded local offset).</param>
    /// <param name="anchor">Anchor within the work area.</param>
    /// <param name="offsetY">Resolved vertical offset; NaN selects the mode default.</param>
    public static WindowPlacement PlaceWindow(
        in VisualRect shell,
        MonitorInfo monitor,
        HostSurface hostSurface,
        ShellForm form,
        IslandAnchor anchor,
        double offsetY = double.NaN)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(hostSurface);
        var placed = hostSurface.Place(shell, monitor.WorkAreaDip, form, anchor, offsetY);
        return CreatePlacement(placed, monitor, hostSurface);
    }

    /// <summary>
    /// Places the host surface for the hidden-state hover strip. The strip is pinned to the host
    /// edge (top or bottom) rather than inset by the 24 DIP shadow bleed.
    /// </summary>
    /// <param name="hoverStrip">Hover strip in work-area-relative DIP.</param>
    /// <param name="monitor">Target monitor.</param>
    /// <param name="hostSurface">Fixed host surface for the current mode.</param>
    /// <param name="anchor">Anchor within the work area.</param>
    public static WindowPlacement PlaceHoverStrip(
        in VisualRect hoverStrip,
        MonitorInfo monitor,
        HostSurface hostSurface,
        IslandAnchor anchor)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(hostSurface);
        var placed = hostSurface.PlaceHoverStrip(hoverStrip, monitor.WorkAreaDip, anchor);
        return CreatePlacement(placed, monitor, hostSurface);
    }

    /// <summary>
    /// Legacy placement overload: uses the float/flush host surface and an explicit bleed on both
    /// axes. Prefer the <see cref="HostSurface"/> overload for mode-aware placement.
    /// </summary>
    /// <param name="shell">Shell rectangle in work-area-relative DIP.</param>
    /// <param name="monitor">Target monitor.</param>
    /// <param name="padding">Bleed on each side; defaults to <see cref="DefaultPadding"/>.</param>
    public static WindowPlacement PlaceWindow(in VisualRect shell, MonitorInfo monitor, double padding = DefaultPadding)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentOutOfRangeException.ThrowIfNegative(padding);
        var hostSurface = HostSurface.Float;
        var windowVisual = new VisualRect(
            shell.X - padding,
            shell.Y - padding,
            hostSurface.Width,
            hostSurface.Height);
        var placed = new HostPlacement(
            Shell: shell,
            WindowVisual: windowVisual,
            ShellLocalOffset: new VisualPoint(padding, padding),
            ArchipelagoStrip: hostSurface.ArchipelagoStripLocal.Offset(windowVisual.X, windowVisual.Y),
            DockStrip: VisualRect.Empty,
            BottomBand: hostSurface.BottomBandLocal.Offset(windowVisual.X, windowVisual.Y),
            IsClamped: false);
        return CreatePlacement(placed, monitor, hostSurface, padding);
    }

    private static WindowPlacement CreatePlacement(
        in HostPlacement placed,
        MonitorInfo monitor,
        HostSurface hostSurface,
        double? padding = null)
    {
        var windowPixel = ToPhysical(placed.WindowVisual, monitor);
        return new WindowPlacement(
            Shell: placed.Shell,
            WindowVisual: placed.WindowVisual,
            WindowPixel: windowPixel,
            Scale: monitor.Scale,
            Padding: padding ?? hostSurface.SidePadding,
            ShellLocalOffset: placed.ShellLocalOffset,
            WindowSizeDip: hostSurface.Size)
        {
            HostSurface = hostSurface,
            ArchipelagoStrip = placed.ArchipelagoStrip,
            DockStrip = placed.DockStrip,
            BottomBand = placed.BottomBand,
            IsClamped = placed.IsClamped,
        };
    }

    /// <summary>Rounds half away from zero (DIP -&gt; px origin).</summary>
    public static int Round(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);

    /// <summary>Ceils to an integer pixel (DIP -&gt; px size).</summary>
    public static int Ceil(double value) => (int)Math.Ceiling(value);
}

/// <summary>
/// Result of the single visual-&gt;window conversion: where the fixed host surface goes, where the
/// shell lives inside it, and where the detached archipelago strip lands.
/// </summary>
/// <param name="Shell">Shell rectangle in work-area-relative DIP (after host clamping).</param>
/// <param name="WindowVisual">Host window in work-area-relative DIP (488x384 float/flush, 488x444 dock).</param>
/// <param name="WindowPixel">Absolute physical host window rectangle.</param>
/// <param name="Scale">Monitor DIP scale.</param>
/// <param name="Padding">Top/side transparent bleed (24 DIP).</param>
/// <param name="ShellLocalOffset">Shell top-left inside the host window, in DIP.</param>
/// <param name="WindowSizeDip">Host window size in DIP (per mode; see <see cref="HostSurface"/>).</param>
public sealed record WindowPlacement(
    VisualRect Shell,
    VisualRect WindowVisual,
    PixelRect WindowPixel,
    double Scale,
    double Padding,
    VisualPoint ShellLocalOffset,
    VisualSize WindowSizeDip)
{
    /// <summary>Host surface used for this placement.</summary>
    public HostSurface HostSurface { get; init; } = HostSurface.Float;

    /// <summary>Detached archipelago track envelope in work-area DIP (empty when hidden).</summary>
    public VisualRect ArchipelagoStrip { get; init; } = VisualRect.Empty;

    /// <summary>Dock strip in work-area DIP (empty for float/flush).</summary>
    public VisualRect DockStrip { get; init; } = VisualRect.Empty;

    /// <summary>
    /// Transparent band below the expanded shell envelope in work-area DIP (holds the strip;
    /// hit-test-through). Constant for the mode even when a compact form is showing.
    /// </summary>
    public VisualRect BottomBand { get; init; } = VisualRect.Empty;

    /// <summary>
    /// Whether the shell was host-adjusted (bottom/dock anchoring) or clamped to keep the surface
    /// inside the work area.
    /// </summary>
    public bool IsClamped { get; init; }

    /// <summary>Host bottom allowance below the expanded shell envelope in DIP (76 float/flush, 136 dock).</summary>
    public double BottomAllowance => HostSurface.BottomAllowance;
}
