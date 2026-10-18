using FluidBar.Platform.Windows.Hosting;

namespace FluidBar.Platform.Windows.Display;

/// <summary>
/// Conversions between the platform's Win32 DTOs and the Core layout contract
/// (<see cref="FluidBar.Core.Layout.MonitorInfo"/>, <see cref="FluidBar.Core.Layout.PixelRect"/>,
/// <see cref="FluidBar.Core.Layout.IslandAnchor"/>, <see cref="FluidBar.Core.Layout.ShellGeometry"/>).
/// </summary>
/// <remarks>
/// The two layers intentionally own separate types: Core must stay Win32-free and Platform must stay
/// layout-free. The App host is the only place that needs both, so this is the single conversion seam
/// (no implicit conversions, no duplicated math).
/// </remarks>
public static class CoreLayoutConversions
{
    /// <summary>Host-window padding on each side (Core <c>WindowPlacementConverter.DefaultPadding</c>).</summary>
    public const double DefaultHostPaddingDip = FluidBar.Core.Layout.WindowPlacementConverter.DefaultPadding;

    /// <summary>Platform physical rect → Core physical rect.</summary>
    public static FluidBar.Core.Layout.PixelRect ToCore(this PixelRect rect)
        => new(rect.X, rect.Y, rect.Width, rect.Height);

    /// <summary>Core physical rect → Platform physical rect.</summary>
    public static PixelRect ToPlatform(this FluidBar.Core.Layout.PixelRect rect)
        => new(rect.X, rect.Y, rect.Width, rect.Height);

    /// <summary>
    /// Platform monitor → Core monitor. Core carries a single DPI value; the platform keeps the
    /// X/Y pair (they only differ in exotic per-axis scaling, which Core does not model).
    /// </summary>
    /// <param name="monitor">Platform monitor.</param>
    /// <param name="stableId">
    /// Optional stable id override (spec 2.6: <c>\\.\DISPLAYn</c> + EDID hash). The platform uses the
    /// device name; the App may hash EDID and pass the stable form here.
    /// </param>
    public static FluidBar.Core.Layout.MonitorInfo ToCore(this MonitorInfo monitor, string? stableId = null)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        return new FluidBar.Core.Layout.MonitorInfo
        {
            Id = string.IsNullOrWhiteSpace(stableId) ? monitor.Id : stableId,
            Bounds = monitor.Bounds.ToCore(),
            WorkArea = monitor.WorkArea.ToCore(),
            Dpi = monitor.DpiX,
            IsPrimary = monitor.IsPrimary,
        };
    }

    /// <summary>Core monitor → Platform monitor (index is assigned by the caller's enumeration order).</summary>
    public static MonitorInfo ToPlatform(this FluidBar.Core.Layout.MonitorInfo monitor, int index = 0)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        return new MonitorInfo(monitor.Id, index, monitor.Bounds.ToPlatform(), monitor.WorkArea.ToPlatform(),
            monitor.Dpi, monitor.Dpi, monitor.IsPrimary);
    }

    /// <summary>Platform anchor → Core anchor (same six positions, separate enums on purpose).</summary>
    public static FluidBar.Core.Layout.IslandAnchor ToCore(this IslandAnchor anchor) => anchor switch
    {
        IslandAnchor.TopLeft => FluidBar.Core.Layout.IslandAnchor.TopLeft,
        IslandAnchor.TopCenter => FluidBar.Core.Layout.IslandAnchor.TopCenter,
        IslandAnchor.TopRight => FluidBar.Core.Layout.IslandAnchor.TopRight,
        IslandAnchor.BottomLeft => FluidBar.Core.Layout.IslandAnchor.BottomLeft,
        IslandAnchor.BottomCenter => FluidBar.Core.Layout.IslandAnchor.BottomCenter,
        IslandAnchor.BottomRight => FluidBar.Core.Layout.IslandAnchor.BottomRight,
        _ => throw new ArgumentOutOfRangeException(nameof(anchor), anchor, "Unknown anchor."),
    };

    /// <summary>Core anchor → Platform anchor.</summary>
    public static IslandAnchor ToPlatform(this FluidBar.Core.Layout.IslandAnchor anchor) => anchor switch
    {
        FluidBar.Core.Layout.IslandAnchor.TopLeft => IslandAnchor.TopLeft,
        FluidBar.Core.Layout.IslandAnchor.TopCenter => IslandAnchor.TopCenter,
        FluidBar.Core.Layout.IslandAnchor.TopRight => IslandAnchor.TopRight,
        FluidBar.Core.Layout.IslandAnchor.BottomLeft => IslandAnchor.BottomLeft,
        FluidBar.Core.Layout.IslandAnchor.BottomCenter => IslandAnchor.BottomCenter,
        FluidBar.Core.Layout.IslandAnchor.BottomRight => IslandAnchor.BottomRight,
        _ => throw new ArgumentOutOfRangeException(nameof(anchor), anchor, "Unknown anchor."),
    };

    /// <summary>
    /// Core shell geometry (work-area-relative DIP, per-corner radii, squircle exponent) → the hosting
    /// region geometry (window-client-local DIP, single radius). The region uses the largest corner
    /// radius: a larger cut is always click-through-safe (never swallows clicks outside the drawn shell).
    /// </summary>
    public static Hosting.ShellGeometry ToHostingShellGeometry(this FluidBar.Core.Layout.ShellGeometry geometry,
        double hostPaddingDip = DefaultHostPaddingDip)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentOutOfRangeException.ThrowIfNegative(hostPaddingDip);
        return new Hosting.ShellGeometry(
            XDip: hostPaddingDip,
            YDip: hostPaddingDip,
            WidthDip: geometry.Bounds.Width,
            HeightDip: geometry.Bounds.Height,
            CornerRadiusDip: Math.Max(0, geometry.Radii.Max));
    }
}
