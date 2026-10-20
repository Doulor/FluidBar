using FluidBar.Platform.Windows.Hosting;

namespace FluidBar.Platform.Tests;

/// <summary>
/// Hook region routing: interactive rectangles consume clicks, watch-only rectangles (the approach
/// band below the island) only forward pointer movement, everything else belongs to the desktop.
/// </summary>
public sealed class MouseHookRegionTests
{
    private static readonly IslandMouseHook.Rect Shell = new(100, 100, 300, 200);
    private static readonly IslandMouseHook.Rect Band = new(80, 200, 320, 260);

    [Fact]
    public void InteractiveRegions_ConsumeInput()
    {
        var kind = IslandMouseHook.Classify([Shell, Band], [Band], 150, 150);
        Assert.Equal(IslandMouseHook.HookRegionKind.Interactive, kind);
    }

    [Fact]
    public void WatchRegions_ForwardMovesOnly()
    {
        // Inside the band but outside every interactive rectangle: the hook forwards the move and the
        // desktop keeps the click.
        var kind = IslandMouseHook.Classify([Shell], [Band], 150, 240);
        Assert.Equal(IslandMouseHook.HookRegionKind.Watch, kind);
    }

    [Fact]
    public void OutsideEveryRegion_IsDesktop()
    {
        Assert.Equal(IslandMouseHook.HookRegionKind.Outside, IslandMouseHook.Classify([Shell], [Band], 10, 10));
    }

    [Fact]
    public void RegionEdges_AreHalfOpen()
    {
        // Right/bottom edges belong to the neighbouring area, so the shell slack and the band never
        // double-report the same pixel row.
        Assert.Equal(IslandMouseHook.HookRegionKind.Interactive, IslandMouseHook.Classify([Shell], [], 299, 199));
        Assert.Equal(IslandMouseHook.HookRegionKind.Outside, IslandMouseHook.Classify([Shell], [], 300, 199));
        Assert.Equal(IslandMouseHook.HookRegionKind.Outside, IslandMouseHook.Classify([Shell], [], 200, 200));
        Assert.Equal(IslandMouseHook.HookRegionKind.Watch, IslandMouseHook.Classify([Shell], [Band], 200, 200));
    }

    [Fact]
    public void EmptyRegionLists_RouteToDesktop()
    {
        Assert.Equal(IslandMouseHook.HookRegionKind.Outside, IslandMouseHook.Classify([], [], 150, 150));
    }
}
