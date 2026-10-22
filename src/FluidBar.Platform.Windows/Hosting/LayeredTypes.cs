using FluidBar.Core.Layout;

// "Float" (island float mode) and "Pointer" (mouse pointer) are the domain vocabulary of the design
// spec, not type names: CA1720 would force misleading renames such as "Floating"/"PointerState".
#pragma warning disable CA1720

namespace FluidBar.Platform.Windows.Hosting;

/// <summary>
/// Presentation mode of a layered island host. The mode selects the fixed surface size; it never
/// changes per frame (spec §5.6: "one window per monitor, created at its final size").
/// </summary>
public enum LayeredHostMode
{
    /// <summary>Floating pill/panel near the work-area anchor (surface 488x384 DIP).</summary>
    Float,

    /// <summary>Taskbar-replacement dock: 440x52 strip at the work-area bottom plus the panel above it (surface 488x444 DIP).</summary>
    Dock,
}

/// <summary>Cursor shown while the pointer is over an interactive part of the island.</summary>
public enum LayeredCursorKind
{
    /// <summary>Standard arrow.</summary>
    Arrow,

    /// <summary>Link/action hand.</summary>
    Hand,

    /// <summary>Text I-beam.</summary>
    IBeam,

    /// <summary>Move/drag cursor.</summary>
    SizeAll,

    /// <summary>Horizontal resize.</summary>
    SizeWE,

    /// <summary>Vertical resize.</summary>
    SizeNS,
}

/// <summary>Pixel size of a layered surface.</summary>
/// <param name="Width">Width in physical pixels.</param>
/// <param name="Height">Height in physical pixels.</param>
public readonly record struct LayeredPixelSize(int Width, int Height)
{
    /// <summary>Whether the size has no area.</summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <inheritdoc />
    public override string ToString() => $"{Width}x{Height}px";
}

/// <summary>
/// The two fixed per-mode host surfaces (DIP). Both are 488 DIP wide (440 shell + 2x24 bleed) and
/// follow Core's <see cref="FluidBar.Core.Layout.HostSurface"/> composition exactly:
/// <list type="bullet">
/// <item>Float 488x384 = 24 bleed + 284 panel + 8 gap + 32 archipelago strip + 36 bottom bleed.</item>
/// <item>Dock 488x444 = 24 bleed + 284 panel + 8 gap + 52 dock strip + 8 gap + 32 archipelago strip + 36 bottom bleed.</item>
/// </list>
/// In dock mode the strip sits <b>below</b> the panel (Core's composition), so the expanded panel is
/// always at (24,24) in both modes and only the compact dock shell is anchored at the strip top
/// (<see cref="DockStripTop"/>). The transparent bottom band is what makes the "extra strip is
/// click-through except the pills" contract possible: it belongs to the same HWND but has no hit
/// region unless a pill rect is set.
/// </summary>
public readonly record struct LayeredSurfaceSize(double WidthDip, double HeightDip)
{
    /// <summary>Host surface width for both modes (DIP).</summary>
    public const double Width = 488;

    /// <summary>Float surface width (858 DIP): room for the 810 DIP notification cap.</summary>
    public const double FloatWidth = 858;

    /// <summary>Float surface height (DIP): 24 bleed + 284 panel + 8 gap + 32 strip + 36 bleed.</summary>
    public const double FloatHeight = 384;

    /// <summary>Dock surface height (DIP): 24 + 284 panel + 8 gap + 52 strip + 8 gap + 32 strip + 36 bleed.</summary>
    public const double DockHeight = 444;

    /// <summary>Bleed around the shell for the soft shadow/antialiasing (DIP).</summary>
    public const double Padding = 24;

    /// <summary>Expanded panel width (DIP).</summary>
    public const double PanelWidth = 440;

    /// <summary>Expanded panel height (DIP).</summary>
    public const double PanelHeight = 284;

    /// <summary>Dock strip height (DIP).</summary>
    public const double DockStripHeight = 52;

    /// <summary>Gap between the dock strip and the panel above it (DIP).</summary>
    public const double DockGap = 8;

    /// <summary>Gap between the shell/dock strip and the archipelago strip (DIP); matches Core's <c>LayoutTokens.ArchipelagoGap</c>.</summary>
    public const double ArchipelagoGap = 8;

    /// <summary>Archipelago strip envelope height (DIP); matches Core's <c>LayoutTokens.ArchipelagoStripHeight</c>.</summary>
    public const double ArchipelagoStripHeight = 32;

    /// <summary>Transparent bottom bleed below the archipelago strip (DIP).</summary>
    public const double BottomPadding = 36;

    /// <summary>Dock strip top inside the surface (24 + 284 + 8 = 316); matches Core's <c>DockStripTopLocal</c>.</summary>
    public const double DockStripTop = Padding + PanelHeight + DockGap;

    /// <summary>Transparent band below the expanded panel in float mode (8 + 32 + 36 = 76 DIP).</summary>
    public const double DetachedLaneHeight = ArchipelagoGap + ArchipelagoStripHeight + BottomPadding;

    /// <summary>Transparent band below the expanded panel in dock mode (8 + 52 + 8 + 32 + 36 = 136 DIP).</summary>
    public const double DockBottomAllowance = DockHeight - Padding - PanelHeight;

    /// <summary>Float surface size (488x384 DIP).</summary>
    public static LayeredSurfaceSize Float { get; } = new(FloatWidth, FloatHeight);

    /// <summary>Dock surface size (488x444 DIP).</summary>
    public static LayeredSurfaceSize Dock { get; } = new(Width, DockHeight);

    /// <summary>Surface size for a mode.</summary>
    public static LayeredSurfaceSize ForMode(LayeredHostMode mode)
        => mode == LayeredHostMode.Dock ? Dock : Float;

    /// <summary>Archipelago strip top inside the surface (316 float, 376 dock), matching Core.</summary>
    public static double ArchipelagoTop(LayeredHostMode mode) => mode == LayeredHostMode.Dock
        ? DockStripTop + DockStripHeight + ArchipelagoGap
        : Padding + PanelHeight + ArchipelagoGap;

    /// <summary>
    /// Surface-local DIP offset of the expanded panel (24,24) for both modes. Core's
    /// <c>HostSurface.ShellLocalOffset</c> owns this value; the dock <b>compact</b> strip is anchored
    /// at <see cref="DockStripTop"/> instead (use the <see cref="FluidBar.Core.Layout.ShellForm"/>
    /// overload).
    /// </summary>
    public static VisualPoint ShellLocalOffset(LayeredHostMode mode)
        => new(mode == LayeredHostMode.Dock ? Padding : (FloatWidth - PanelWidth) / 2, Padding);

    /// <summary>
    /// Surface-local DIP offset of the anchor shell for a form: the expanded panel for every mode,
    /// the dock strip for dock compact/peek/closing/hidden. This mirrors Core
    /// <c>HostSurface.ResolveLocalY</c>.
    /// </summary>
    public static VisualPoint ShellLocalOffset(LayeredHostMode mode, FluidBar.Core.Layout.ShellForm form)
        => mode == LayeredHostMode.Dock && form != FluidBar.Core.Layout.ShellForm.Expanded
            ? new VisualPoint(Padding, DockStripTop)
            : mode == LayeredHostMode.Dock
                ? new VisualPoint(Padding, Padding)
                : new VisualPoint((FloatWidth - PanelWidth) / 2, Padding);

    /// <summary>Converts to physical pixels with the ceiling size rule (never clips a DIP).</summary>
    public LayeredPixelSize ToPixels(double scale)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(scale, 0.1);
        return new LayeredPixelSize(
            (int)Math.Ceiling(WidthDip * scale),
            (int)Math.Ceiling(HeightDip * scale));
    }
}

/// <summary>
/// Rectangle in physical (surface) pixels. The dirty-rect and hit-test currency of the host.
/// </summary>
/// <param name="X">Left edge.</param>
/// <param name="Y">Top edge.</param>
/// <param name="Width">Width.</param>
/// <param name="Height">Height.</param>
public readonly record struct LayeredRect(int X, int Y, int Width, int Height)
{
    /// <summary>Empty rectangle at the origin.</summary>
    public static LayeredRect Empty { get; } = new(0, 0, 0, 0);

    /// <summary>Right edge (exclusive).</summary>
    public int Right => X + Width;

    /// <summary>Bottom edge (exclusive).</summary>
    public int Bottom => Y + Height;

    /// <summary>Whether the rectangle has no area.</summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>Pixel area.</summary>
    public long Area => IsEmpty ? 0 : (long)Width * Height;

    /// <summary>Creates a rectangle from a DIP rect using the same rounding rules as placement.</summary>
    public static LayeredRect FromDip(in VisualRect rect, double scale)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(scale, 0.1);
        var x = (int)Math.Round(rect.X * scale, MidpointRounding.AwayFromZero);
        var y = (int)Math.Round(rect.Y * scale, MidpointRounding.AwayFromZero);
        var right = (int)Math.Ceiling(rect.Right * scale);
        var bottom = (int)Math.Ceiling(rect.Bottom * scale);
        return new LayeredRect(x, y, Math.Max(0, right - x), Math.Max(0, bottom - y));
    }

    /// <summary>Intersection with another rectangle; empty when disjoint.</summary>
    public LayeredRect Intersect(in LayeredRect other)
    {
        var left = Math.Max(X, other.X);
        var top = Math.Max(Y, other.Y);
        var right = Math.Min(Right, other.Right);
        var bottom = Math.Min(Bottom, other.Bottom);
        return right <= left || bottom <= top ? Empty : new LayeredRect(left, top, right - left, bottom - top);
    }

    /// <summary>Bounding union with another rectangle (empty rectangles are ignored).</summary>
    public LayeredRect Union(in LayeredRect other)
    {
        if (IsEmpty)
        {
            return other;
        }

        if (other.IsEmpty)
        {
            return this;
        }

        var left = Math.Min(X, other.X);
        var top = Math.Min(Y, other.Y);
        var right = Math.Max(Right, other.Right);
        var bottom = Math.Max(Bottom, other.Bottom);
        return new LayeredRect(left, top, right - left, bottom - top);
    }

    /// <summary>Clamps the rectangle to a surface of <paramref name="width"/> x <paramref name="height"/> pixels.</summary>
    public LayeredRect ClampTo(int width, int height)
        => Intersect(new LayeredRect(0, 0, Math.Max(0, width), Math.Max(0, height)));

    /// <summary>Whether the point lies inside (half-open).</summary>
    public bool Contains(int x, int y) => x >= X && x < Right && y >= Y && y < Bottom;

    /// <summary>Whether this rectangle fully covers a surface of the given pixel size.</summary>
    public bool Covers(int width, int height) => X <= 0 && Y <= 0 && Right >= width && Bottom >= height;

    /// <summary>Grows the rectangle by <paramref name="amount"/> pixels on every side.</summary>
    public LayeredRect Inflate(int amount) => new(X - amount, Y - amount, Width + (2 * amount), Height + (2 * amount));

    /// <inheritdoc />
    public override string ToString() => $"({X},{Y} {Width}x{Height})";
}

/// <summary>Pointer buttons that were down when an input event was produced.</summary>
[Flags]
public enum LayeredPointerButtons
{
    /// <summary>No button.</summary>
    None = 0,

    /// <summary>Left button.</summary>
    Left = 1,

    /// <summary>Right button.</summary>
    Right = 2,

    /// <summary>Middle button.</summary>
    Middle = 4,

    /// <summary>First extra button.</summary>
    XButton1 = 8,

    /// <summary>Second extra button.</summary>
    XButton2 = 16,
}

/// <summary>Keyboard modifiers that were down when an input event was produced.</summary>
[Flags]
public enum LayeredKeyModifiers
{
    /// <summary>No modifier.</summary>
    None = 0,

    /// <summary>Shift.</summary>
    Shift = 1,

    /// <summary>Control.</summary>
    Control = 2,

    /// <summary>Alt.</summary>
    Alt = 4,
}

/// <summary>
/// One pointer event in both coordinate systems the host owns: window client pixels (the currency of
/// <c>WM_*</c> messages and of the DIB) and client DIP (the currency of the layout/renderer).
/// </summary>
/// <param name="ClientX">Client X in physical pixels.</param>
/// <param name="ClientY">Client Y in physical pixels.</param>
/// <param name="ClientXDip">Client X in DIP.</param>
/// <param name="ClientYDip">Client Y in DIP.</param>
/// <param name="ScreenX">Screen X in physical pixels.</param>
/// <param name="ScreenY">Screen Y in physical pixels.</param>
/// <param name="Buttons">Buttons held down.</param>
/// <param name="Modifiers">Keyboard modifiers held down.</param>
/// <param name="Timestamp">Host timestamp of the message.</param>
public readonly record struct LayeredPointerEvent(
    int ClientX,
    int ClientY,
    double ClientXDip,
    double ClientYDip,
    int ScreenX,
    int ScreenY,
    LayeredPointerButtons Buttons,
    LayeredKeyModifiers Modifiers,
    long Timestamp);

/// <summary>Mouse-wheel event. <see cref="Delta"/> is a multiple of 120 (Win32 <c>WHEEL_DELTA</c>).</summary>
/// <param name="Pointer">Pointer position at the time of the wheel message.</param>
/// <param name="Delta">Wheel delta; positive is up/right.</param>
/// <param name="IsHorizontal">True for <c>WM_MOUSEHWHEEL</c> (shift-wheel / precision touchpad).</param>
public readonly record struct LayeredWheelEvent(LayeredPointerEvent Pointer, int Delta, bool IsHorizontal);

/// <summary>Phase of a drag gesture.</summary>
public enum LayeredDragPhase
{
    /// <summary>Button pressed; capture acquired.</summary>
    Started,

    /// <summary>Pointer moved while captured.</summary>
    Moved,

    /// <summary>Button released; capture released.</summary>
    Ended,

    /// <summary>Capture lost to another window (alt-tab, UAC); treat as cancelled.</summary>
    Cancelled,
}

/// <summary>
/// Drag gesture in screen pixels. <see cref="DeltaX"/>/<see cref="DeltaY"/> are cumulative from the
/// press point, <see cref="StepX"/>/<see cref="StepY"/> are the delta since the previous event.
/// </summary>
/// <param name="Phase">Drag phase.</param>
/// <param name="Pointer">Pointer position (client + screen).</param>
/// <param name="DeltaX">Cumulative screen X delta.</param>
/// <param name="DeltaY">Cumulative screen Y delta.</param>
/// <param name="StepX">Screen X delta since the previous drag event.</param>
/// <param name="StepY">Screen Y delta since the previous drag event.</param>
public readonly record struct LayeredDragEvent(
    LayeredDragPhase Phase,
    LayeredPointerEvent Pointer,
    int DeltaX,
    int DeltaY,
    int StepX,
    int StepY);

/// <summary>Raised after the host rebuilt its surface for a new monitor DPI.</summary>
/// <param name="OldDpi">Previous effective DPI (96 = 100%).</param>
/// <param name="NewDpi">New effective DPI.</param>
/// <param name="OldScale">Previous DIP scale.</param>
/// <param name="NewScale">New DIP scale.</param>
/// <param name="SurfaceSizePx">New surface size in physical pixels.</param>
public readonly record struct LayeredDpiChange(
    uint OldDpi,
    uint NewDpi,
    double OldScale,
    double NewScale,
    LayeredPixelSize SurfaceSizePx);

/// <summary>Result of a <c>CommitFrame</c> call.</summary>
public readonly struct LayeredCommitResult
{
    internal LayeredCommitResult(bool updated, bool wasFullFrame, int uploadedRectCount,
        double updateMicroseconds, int win32Error, bool usedFallback)
    {
        Updated = updated;
        WasFullFrame = wasFullFrame;
        UploadedRectCount = uploadedRectCount;
        UpdateMicroseconds = updateMicroseconds;
        Win32Error = win32Error;
        UsedFallback = usedFallback;
    }

    /// <summary>Whether <c>UpdateLayeredWindow</c> accepted the frame.</summary>
    public bool Updated { get; }

    /// <summary>True when the whole surface was uploaded instead of dirty rectangles.</summary>
    public bool WasFullFrame { get; }

    /// <summary>Number of <c>UpdateLayeredWindow</c> calls (1 for full or merged-dirty, N for per-rect).</summary>
    public int UploadedRectCount { get; }

    /// <summary>Wall time spent inside the Win32 update calls, in microseconds.</summary>
    public double UpdateMicroseconds { get; }

    /// <summary>Last Win32 error (0 when the update succeeded).</summary>
    public int Win32Error { get; }

    /// <summary>True when the indirect update failed and the canonical <c>UpdateLayeredWindow</c> was used.</summary>
    public bool UsedFallback { get; }

    /// <summary>An empty (nothing uploaded) result.</summary>
    public static LayeredCommitResult None { get; } = new(false, false, 0, 0, 0, false);
}

/// <summary>
/// The frame being drawn: a thin view over the host's persistent DIB. No allocation, no copy —
/// the renderer draws straight into <see cref="Pixels"/> and the host uploads the same memory.
/// </summary>
public readonly struct LayeredFrame
{
    internal LayeredFrame(LayeredSurface surface, double scale, long index)
    {
        Surface = surface;
        Scale = scale;
        Index = index;
    }

    /// <summary>Backing DIB.</summary>
    public LayeredSurface Surface { get; }

    /// <summary>DIP scale of this frame (matches the monitor DPI).</summary>
    public double Scale { get; }

    /// <summary>Monotonic frame index (starts at 1).</summary>
    public long Index { get; }

    /// <summary>Surface width in pixels.</summary>
    public int WidthPx => Surface.WidthPx;

    /// <summary>Surface height in pixels.</summary>
    public int HeightPx => Surface.HeightPx;

    /// <summary>Row stride in bytes (always <c>WidthPx * 4</c>).</summary>
    public int Stride => Surface.Stride;

    /// <summary>Pointer to the first pixel (top-left; the DIB is top-down).</summary>
    public IntPtr Bits => Surface.Bits;

    /// <summary>Writable premultiplied BGRA pixels (top-down).</summary>
    public unsafe Span<byte> Pixels => Surface.Pixels;

    /// <summary>Writable premultiplied BGRA pixels as 32-bit words.</summary>
    public unsafe Span<uint> Pixels32 => Surface.Pixels32;
}

/// <summary>Where the host put its window and how big its surface is.</summary>
/// <param name="WindowPixel">Absolute physical window rectangle.</param>
/// <param name="SurfaceSizePx">Surface size in physical pixels.</param>
/// <param name="SurfaceSizeDip">Surface size in DIP.</param>
/// <param name="Scale">DIP scale used for this placement.</param>
/// <param name="ShellLocalOffsetPx">Offset of the anchor shell rect inside the window, in pixels.</param>
public readonly record struct LayeredHostPlacement(
    LayeredRect WindowPixel,
    LayeredPixelSize SurfaceSizePx,
    LayeredSurfaceSize SurfaceSizeDip,
    double Scale,
    LayeredPixelSize ShellLocalOffsetPx);

/// <summary>Options for <see cref="LayeredWindowHost"/>.</summary>
public sealed class LayeredHostOptions
{
    /// <summary>Default options.</summary>
    public static LayeredHostOptions Default { get; } = new();

    /// <summary>Window title (never shown: the window is a tool window).</summary>
    public string WindowTitle { get; init; } = "FluidBar Island";

    /// <summary>
    /// Merge all dirty rectangles into their bounding box (one update call) instead of uploading each
    /// rectangle separately. Merging is almost always cheaper for a handful of nearby rects.
    /// </summary>
    public bool MergeDirtyRects { get; init; } = true;

    /// <summary>Upper bound on dirty rectangles per frame; extra rectangles are merged into the last one.</summary>
    public int MaxDirtyRects { get; init; } = 16;

    /// <summary>Upload dirty rectangles below this pixel area as part of the merged box only.</summary>
    public long MinDirtyAreaPixels { get; init; }

    /// <summary>Re-assert <c>HWND_TOPMOST</c> on the policy-gated cadence.</summary>
    public bool ReassertTopmost { get; init; } = true;

    /// <summary>Topmost re-assert cadence (the policy itself clamps to 1 Hz).</summary>
    public TimeSpan TopmostReassertInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Track drag gestures (capture + <c>Drag*</c> callbacks).</summary>
    public bool EnableDrag { get; init; } = true;

    /// <summary>Deliver wheel events.</summary>
    public bool EnableWheel { get; init; } = true;

    /// <summary>Cursor shown over interactive content.</summary>
    public LayeredCursorKind DefaultCursor { get; init; } = LayeredCursorKind.Arrow;

    /// <summary>Frame pacing strategy created with the host (see <see cref="LayeredFramePacer"/>).</summary>
    public LayeredPacingMode Pacing { get; init; } = LayeredPacingMode.Composition;

    /// <summary>Target frame rate for <see cref="LayeredPacingMode.TargetFrameRate"/>.</summary>
    public double TargetFramesPerSecond { get; init; } = 60;

    /// <summary>Log every committed frame (diagnostics only; never in production).</summary>
    public bool LogFrames { get; init; }
}

