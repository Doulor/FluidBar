namespace FluidBar.Platform.Windows.Display;

/// <summary>
/// Pure DPI / placement math. This is the single place where DIP coordinates become physical pixels;
/// it is intentionally free of Win32 so it can be exhaustively unit tested (six anchors × three DPIs ×
/// work-area clamping) without touching a real display.
/// </summary>
public static class DisplayMetrics
{
    /// <summary>Converts a DIP rect inside a monitor's work area to physical pixels.</summary>
    /// <remarks>
    /// Rounding policy (matches the design contract): origin is rounded to the nearest pixel,
    /// size is ceiled so the physical rectangle always covers the visual rectangle (no 1 px clipping).
    /// </remarks>
    public static PixelRect ToPhysical(in DipRect rect, MonitorInfo monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        var scale = monitor.Scale;
        var x = (int)Math.Round(monitor.WorkArea.X + (rect.X * scale), MidpointRounding.AwayFromZero);
        var y = (int)Math.Round(monitor.WorkArea.Y + (rect.Y * scale), MidpointRounding.AwayFromZero);
        var w = (int)Math.Ceiling(rect.Width * scale);
        var h = (int)Math.Ceiling(rect.Height * scale);
        return new PixelRect(x, y, Math.Max(0, w), Math.Max(0, h));
    }

    /// <summary>Converts a physical pixel rect (work-area relative) back to DIPs.</summary>
    public static DipRect ToDips(in PixelRect rect, MonitorInfo monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        var scale = monitor.Scale;
        return new DipRect(
            (rect.X - monitor.WorkArea.X) / scale,
            (rect.Y - monitor.WorkArea.Y) / scale,
            rect.Width / scale,
            rect.Height / scale);
    }

    /// <summary>
    /// Places a DIP-sized visual at <paramref name="anchor"/> inside the monitor work area, with an
    /// optional DIP offset, then converts to physical pixels and clamps to the work area.
    /// This is the "anchor placement" used on monitor changes and drag release — never per frame.
    /// </summary>
    public static PixelRect PlaceAtAnchor(MonitorInfo monitor, IslandAnchor anchor, double widthDip, double heightDip,
        double offsetXDip = 0, double offsetYDip = 0)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        var work = monitor.WorkArea;
        var widthPhysical = (int)Math.Ceiling(Math.Max(0, widthDip) * monitor.Scale);
        var heightPhysical = (int)Math.Ceiling(Math.Max(0, heightDip) * monitor.Scale);
        var offsetX = (int)Math.Round(offsetXDip * monitor.Scale, MidpointRounding.AwayFromZero);
        var offsetY = (int)Math.Round(offsetYDip * monitor.Scale, MidpointRounding.AwayFromZero);

        int x;
        int y;
        switch (anchor)
        {
            case IslandAnchor.TopCenter:
                x = work.X + ((work.Width - widthPhysical) / 2);
                y = work.Y;
                break;
            case IslandAnchor.TopLeft:
                x = work.X;
                y = work.Y;
                break;
            case IslandAnchor.TopRight:
                x = work.Right - widthPhysical;
                y = work.Y;
                break;
            case IslandAnchor.BottomCenter:
                x = work.X + ((work.Width - widthPhysical) / 2);
                y = work.Bottom - heightPhysical;
                break;
            case IslandAnchor.BottomLeft:
                x = work.X;
                y = work.Bottom - heightPhysical;
                break;
            case IslandAnchor.BottomRight:
                x = work.Right - widthPhysical;
                y = work.Bottom - heightPhysical;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(anchor), anchor, "Unknown anchor.");
        }

        return ClampToWorkArea(new PixelRect(x + offsetX, y + offsetY, widthPhysical, heightPhysical), monitor);
    }

    /// <summary>
    /// Clamps a physical rect so it stays inside the monitor work area. The size is shrunk only when
    /// the rect is larger than the work area; the origin is moved so the full rect stays visible.
    /// </summary>
    public static PixelRect ClampToWorkArea(in PixelRect rect, MonitorInfo monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        var work = monitor.WorkArea;
        var width = Math.Min(Math.Max(0, rect.Width), Math.Max(0, work.Width));
        var height = Math.Min(Math.Max(0, rect.Height), Math.Max(0, work.Height));

        var x = rect.X;
        if (x + width > work.Right)
        {
            x = work.Right - width;
        }

        if (x < work.X)
        {
            x = work.X;
        }

        var y = rect.Y;
        if (y + height > work.Bottom)
        {
            y = work.Bottom - height;
        }

        if (y < work.Y)
        {
            y = work.Y;
        }

        return new PixelRect(x, y, width, height);
    }

    /// <summary>
    /// WM_DPICHANGED math: recompute placement from the anchor in the *new* monitor instead of scaling
    /// the old rectangle (scaling drifts, per the design contract §5.5). Returns work-area-relative DIPs.
    /// </summary>
    public static DipRect ReanchorForDpiChange(MonitorInfo newMonitor, IslandAnchor anchor, double widthDip,
        double heightDip, double offsetXDip = 0, double offsetYDip = 0)
    {
        var physical = PlaceAtAnchor(newMonitor, anchor, widthDip, heightDip, offsetXDip, offsetYDip);
        return ToDips(physical, newMonitor);
    }

    /// <summary>Extracts the new DPI values from a <c>WM_DPICHANGED</c> <c>wParam</c>.</summary>
    public static (uint DpiX, uint DpiY) DecodeDpiChangedWParam(IntPtr wParam)
    {
        var value = wParam.ToInt64();
        var rawX = (uint)(value & 0xFFFF);
        var rawY = (uint)((value >> 16) & 0xFFFF);
        var dpiX = rawX == 0 ? 96u : rawX;
        var dpiY = rawY == 0 ? dpiX : rawY;
        return (dpiX, dpiY);
    }

    /// <summary>
    /// Selects the monitor with the largest intersection with <paramref name="rect"/>; ties go to the
    /// lower enumeration index. Falls back to the primary monitor when nothing intersects.
    /// </summary>
    public static MonitorInfo SelectMonitor(MonitorSet monitors, in PixelRect rect)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        MonitorInfo? best = null;
        long bestArea = -1;
        foreach (var monitor in monitors.Monitors)
        {
            var area = IntersectionArea(monitor.Bounds, rect);
            if (area > bestArea)
            {
                best = monitor;
                bestArea = area;
            }
        }

        if (best is not null && bestArea > 0)
        {
            return best;
        }

        return monitors.Primary ?? throw new InvalidOperationException("Monitor set is empty.");
    }

    /// <summary>Finds the monitor whose bounds contain the physical point, if any.</summary>
    public static MonitorInfo? MonitorAtPoint(MonitorSet monitors, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        foreach (var monitor in monitors.Monitors)
        {
            if (x >= monitor.Bounds.X && x < monitor.Bounds.Right
                && y >= monitor.Bounds.Y && y < monitor.Bounds.Bottom)
            {
                return monitor;
            }
        }

        return null;
    }

    /// <summary>Intersection area of two physical rectangles (0 when they do not overlap).</summary>
    public static long IntersectionArea(in PixelRect a, in PixelRect b)
    {
        var left = Math.Max(a.X, b.X);
        var top = Math.Max(a.Y, b.Y);
        var right = Math.Min(a.Right, b.Right);
        var bottom = Math.Min(a.Bottom, b.Bottom);
        if (right <= left || bottom <= top)
        {
            return 0;
        }

        return (long)(right - left) * (bottom - top);
    }

    /// <summary>Rounds a scale factor from a DPI value, clamped to a sane 0.5–4.0 range.</summary>
    public static double ScaleFromDpi(uint dpi)
    {
        var scale = (dpi == 0 ? 96u : dpi) / 96.0;
        return Math.Clamp(scale, 0.5, 4.0);
    }
}
