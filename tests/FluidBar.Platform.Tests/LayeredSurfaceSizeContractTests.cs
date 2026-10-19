using FluidBar.Core.Layout;
using FluidBar.Platform.Windows.Hosting;

namespace FluidBar.Platform.Tests;

/// <summary>
/// Pins the layered host's per-mode surface snapshot to Core's <see cref="LayoutTokens"/> /
/// <see cref="HostSurface"/> composition. The constants in <see cref="LayeredSurfaceSize"/> are a
/// copy of Core's numbers; a silent drift between the two would move the detached archipelago band
/// (or mis-size the window) without failing any render test, so every value is asserted here.
/// </summary>
public sealed class LayeredSurfaceSizeContractTests
{
    [Fact]
    public void Constants_MatchCoreLayoutTokens()
    {
        Assert.Equal(LayoutTokens.HostSurfaceWidth, LayeredSurfaceSize.Width);
        Assert.Equal(LayoutTokens.HostSurfaceFloatHeight, LayeredSurfaceSize.FloatHeight);
        Assert.Equal(LayoutTokens.HostSurfaceDockHeight, LayeredSurfaceSize.DockHeight);
        Assert.Equal(LayoutTokens.WindowPadding, LayeredSurfaceSize.Padding);
        Assert.Equal(LayoutTokens.ExpandedWidth, LayeredSurfaceSize.PanelWidth);
        Assert.Equal(LayoutTokens.ExpandedHeight, LayeredSurfaceSize.PanelHeight);
        Assert.Equal(LayoutTokens.DockStripHeight, LayeredSurfaceSize.DockStripHeight);
        Assert.Equal(LayoutTokens.DockExpandedGap, LayeredSurfaceSize.DockGap);
        Assert.Equal(LayoutTokens.ArchipelagoGap, LayeredSurfaceSize.ArchipelagoGap);
        Assert.Equal(LayoutTokens.ArchipelagoStripHeight, LayeredSurfaceSize.ArchipelagoStripHeight);
        Assert.Equal(LayoutTokens.HostBottomPadding, LayeredSurfaceSize.BottomPadding);

        // 8 / 32 gap+strip are the current token values; 10 / 30 were the pre-prototype2 snapshot.
        Assert.Equal(8, LayeredSurfaceSize.ArchipelagoGap);
        Assert.Equal(32, LayeredSurfaceSize.ArchipelagoStripHeight);
    }

    [Fact]
    public void ArchipelagoTop_MatchesCoreHostSurface()
    {
        var floatSurface = HostSurface.ForMode(IslandMode.Float);
        var dockSurface = HostSurface.ForMode(IslandMode.Dock);

        Assert.Equal(floatSurface.ArchipelagoTopLocal, LayeredSurfaceSize.ArchipelagoTop(LayeredHostMode.Float));
        Assert.Equal(dockSurface.ArchipelagoTopLocal, LayeredSurfaceSize.ArchipelagoTop(LayeredHostMode.Dock));
        Assert.Equal(316, LayeredSurfaceSize.ArchipelagoTop(LayeredHostMode.Float));
        Assert.Equal(376, LayeredSurfaceSize.ArchipelagoTop(LayeredHostMode.Dock));
    }

    [Fact]
    public void BottomAllowances_MatchCoreComposition()
    {
        Assert.Equal(LayoutTokens.HostFloatBottomAllowance, LayeredSurfaceSize.DetachedLaneHeight);
        Assert.Equal(LayoutTokens.HostDockBottomAllowance, LayeredSurfaceSize.DockBottomAllowance);
        Assert.Equal(76, LayeredSurfaceSize.DetachedLaneHeight);
        Assert.Equal(136, LayeredSurfaceSize.DockBottomAllowance);
        Assert.Equal(LayeredSurfaceSize.DockStripTop, LayeredSurfaceSize.Padding + LayeredSurfaceSize.PanelHeight + LayeredSurfaceSize.DockGap);
        Assert.Equal(316, LayeredSurfaceSize.DockStripTop);
    }
}
