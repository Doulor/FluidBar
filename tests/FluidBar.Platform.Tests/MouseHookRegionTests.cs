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

    [Fact]
    public void PressedInside_ReleasedOutside_ProducesReleaseAndLeft_AndResetsPressed()
    {
        var gestures = new IslandMouseHook.GestureTracker();

        var down = gestures.Route(IslandMouseHook.HookRegionKind.Interactive, IslandMouseHook.WmLButtonDown);
        Assert.True(down.Consume);
        Assert.True(down.Pressed);
        Assert.True(gestures.IsPressed);

        var up = gestures.Route(IslandMouseHook.HookRegionKind.Outside, IslandMouseHook.WmLButtonUp);
        Assert.False(up.Consume);
        Assert.True(up.Released);
        Assert.True(up.Left);
        Assert.False(gestures.IsPressed);
        Assert.False(gestures.IsInside);
    }

    [Fact]
    public void PressedInside_ReleasedOutside_ThenPlainMoveDoesNotDrag()
    {
        var gestures = new IslandMouseHook.GestureTracker();
        gestures.Route(IslandMouseHook.HookRegionKind.Interactive, IslandMouseHook.WmLButtonDown);
        gestures.Route(IslandMouseHook.HookRegionKind.Outside, IslandMouseHook.WmLButtonUp);

        var move = gestures.Route(IslandMouseHook.HookRegionKind.Interactive, IslandMouseHook.WmMouseMove);
        Assert.True(move.Moved);
        Assert.False(move.Dragged); // the old code left _pressed set and emitted a phantom drag here
    }

    [Fact]
    public void PressStartingOnDesktop_NeverClaimsTheIsland()
    {
        var gestures = new IslandMouseHook.GestureTracker();
        var down = gestures.Route(IslandMouseHook.HookRegionKind.Outside, IslandMouseHook.WmLButtonDown);
        Assert.False(down.Pressed);
        Assert.False(gestures.IsPressed);

        var up = gestures.Route(IslandMouseHook.HookRegionKind.Outside, IslandMouseHook.WmLButtonUp);
        Assert.False(up.Released);
    }

    [Fact]
    public void PressInside_ReleaseInWatchBand_ProducesReleaseAndClearsPressed()
    {
        var gestures = new IslandMouseHook.GestureTracker();
        gestures.Route(IslandMouseHook.HookRegionKind.Interactive, IslandMouseHook.WmLButtonDown);

        var up = gestures.Route(IslandMouseHook.HookRegionKind.Watch, IslandMouseHook.WmLButtonUp);
        Assert.True(up.Released);
        Assert.False(gestures.IsPressed);
    }

    [Fact]
    public void WatchBandMoveWhilePressed_IsReportedAsDrag()
    {
        var gestures = new IslandMouseHook.GestureTracker();
        gestures.Route(IslandMouseHook.HookRegionKind.Interactive, IslandMouseHook.WmLButtonDown);

        var move = gestures.Route(IslandMouseHook.HookRegionKind.Watch, IslandMouseHook.WmMouseMove);
        Assert.True(move.Moved);
        Assert.True(move.Dragged);
    }

    [Fact]
    public void WheelRoutePreservesFractionalNotches()
    {
        var gestures = new IslandMouseHook.GestureTracker();
        var route = gestures.Route(IslandMouseHook.HookRegionKind.Interactive, IslandMouseHook.WmMouseWheel, 0.25);
        Assert.True(route.Consume);
        Assert.Equal(0.25, route.WheelNotches);
    }
}
