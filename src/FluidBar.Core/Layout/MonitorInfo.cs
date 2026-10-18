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
    /// Places the fixed-size per-monitor host window so that the shell lands at its computed
    /// work-area position. The host is always <c>488x332 DIP</c> (spec 5.6); the shell sits at
    /// <see cref="WindowPlacement.ShellLocalOffset"/> inside it.
    /// <para>
    /// The padding is applied to <b>both</b> axes exactly once: the v1 bug (F-04) subtracted the
    /// bleed from X only, shifting every top-anchored pill +14 DIP down and clipping bottom anchors.
    /// </para>
    /// </summary>
    /// <param name="shell">Shell rectangle in work-area-relative DIP.</param>
    /// <param name="monitor">Target monitor.</param>
    /// <param name="padding">Bleed on each side; defaults to <see cref="DefaultPadding"/>.</param>
    public static WindowPlacement PlaceWindow(in VisualRect shell, MonitorInfo monitor, double padding = DefaultPadding)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentOutOfRangeException.ThrowIfNegative(padding);
        var scale = monitor.Scale;
        var windowOriginX = monitor.WorkArea.X + ((shell.X - padding) * scale);
        var windowOriginY = monitor.WorkArea.Y + ((shell.Y - padding) * scale);
        var windowPixel = new PixelRect(
            Round(windowOriginX),
            Round(windowOriginY),
            Ceil(LayoutTokens.WindowWidth * scale),
            Ceil(LayoutTokens.WindowHeight * scale));
        var windowVisual = new VisualRect(
            shell.X - padding,
            shell.Y - padding,
            LayoutTokens.WindowWidth,
            LayoutTokens.WindowHeight);
        return new WindowPlacement(
            Shell: shell,
            WindowVisual: windowVisual,
            WindowPixel: windowPixel,
            Scale: scale,
            Padding: padding,
            ShellLocalOffset: new VisualPoint(padding, padding),
            WindowSizeDip: new VisualSize(LayoutTokens.WindowWidth, LayoutTokens.WindowHeight));
    }

    /// <summary>Rounds half away from zero (DIP -&gt; px origin).</summary>
    public static int Round(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);

    /// <summary>Ceils to an integer pixel (DIP -&gt; px size).</summary>
    public static int Ceil(double value) => (int)Math.Ceiling(value);
}

/// <summary>
/// Result of the single visual-&gt;window conversion: where the fixed host window goes and where
/// the shell lives inside it.
/// </summary>
/// <param name="Shell">Shell rectangle in work-area-relative DIP.</param>
/// <param name="WindowVisual">Host window in work-area-relative DIP (fixed 488x332).</param>
/// <param name="WindowPixel">Absolute physical host window rectangle.</param>
/// <param name="Scale">Monitor DIP scale.</param>
/// <param name="Padding">Bleed applied per side.</param>
/// <param name="ShellLocalOffset">Shell top-left inside the host window, in DIP.</param>
/// <param name="WindowSizeDip">Host window size in DIP (always 488x332).</param>
public sealed record WindowPlacement(
    VisualRect Shell,
    VisualRect WindowVisual,
    PixelRect WindowPixel,
    double Scale,
    double Padding,
    VisualPoint ShellLocalOffset,
    VisualSize WindowSizeDip);
