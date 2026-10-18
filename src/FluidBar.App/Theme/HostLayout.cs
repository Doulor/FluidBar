namespace FluidBar.App.Theme;

/// <summary>
/// Host-window growth for the detached Page Archipelago (user feedback 2026-10-18: mini-islands float
/// BELOW the expanded island, over the desktop, never covering content). The host window becomes
/// 488×384 (float/flush) instead of 488×332; this file is the single configurable seam — when Platform
/// owns the host resize, only these numbers change (and the suspended `IslandWindow` dock constant).
/// </summary>
public static class HostLayout
{
    /// <summary>Extra host height below the expanded shell envelope (gap + strip + bottom margin).</summary>
    public const double ExtraBottomDip = 52;

    /// <summary>Gap between the expanded shell bottom and the floating archipelago (DIP).</summary>
    public const double ArchipelagoGap = 10;

    /// <summary>Bottom margin below the archipelago inside the host window (DIP).</summary>
    public const double ArchipelagoBottomMargin = 8;

    /// <summary>Design height of the expanded content layer (440×336: 284 shell + 52 archipelago band).</summary>
    public const double ExpandedLayerHeight = FluidBar.Core.Layout.LayoutTokens.ExpandedHeight + ExtraBottomDip;

    /// <summary>Float/flush host window height in DIP (488×384).</summary>
    public static double WindowHeight => FluidBar.Core.Layout.LayoutTokens.WindowHeight + ExtraBottomDip;

    /// <summary>Dock host window height once the dock strip/panel band grows by the archipelago (488×444).</summary>
    public static double DockWindowHeight => FluidBar.Core.Layout.LayoutTokens.WindowHeight + ExtraBottomDip + 60;

    /// <summary>Fixed host window width (unchanged: 488 DIP).</summary>
    public const double WindowWidth = FluidBar.Core.Layout.LayoutTokens.WindowWidth;
}
