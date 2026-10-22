using FluidBar.Core.Activities;
using FluidBar.Core.Layout;

namespace FluidBar.Core.Tests;

/// <summary>
/// Pure drag-release placement (<see cref="IslandPlacementDrag"/>): anchor snapping, free-offset
/// persistence, mode axis policy and work-area clamping at 96/144/192 DPI. No HWND, no settings and
/// no UI thread: the helper is the only place a drop turns into a persisted anchor/offset.
/// </summary>
public class PlacementDragTests
{
    private const double Eps = 0.01;

    [Fact]
    public void Policy_FloatMovesBothAxes_FlushHorizontalOnly_DockFixed()
    {
        Assert.True(IslandPlacementDrag.AllowsDrag(IslandMode.Float));
        Assert.True(IslandPlacementDrag.AllowsDrag(IslandMode.Flush));
        Assert.False(IslandPlacementDrag.AllowsDrag(IslandMode.Dock));

        Assert.True(IslandPlacementDrag.AllowsVerticalDrag(IslandMode.Float));
        Assert.False(IslandPlacementDrag.AllowsVerticalDrag(IslandMode.Flush));
        Assert.False(IslandPlacementDrag.AllowsVerticalDrag(IslandMode.Dock));
    }

    [Theory]
    [InlineData(96u)]
    [InlineData(144u)]
    [InlineData(192u)]
    public void Solve_ReleaseNearAnAnchor_SnapsAndPersistsZeroOffsets(uint dpi)
    {
        var monitor = Monitor(dpi);
        var natural = NaturalWindow(monitor, IslandMode.Float, IslandAnchor.TopCenter, ActivityKind.Notification);

        // 12 DIP left and 9 DIP down of the natural position: inside the 24 DIP snap radius.
        var dropped = natural.Offset(-12, 9);
        var placement = IslandPlacementDrag.Solve(
            dropped, monitor, IslandMode.Float, ActivityKind.Notification, IslandAnchor.TopLeft);

        Assert.True(placement.Snapped);
        Assert.Equal(IslandAnchor.TopCenter, placement.Anchor);
        Assert.Equal(0, placement.OffsetX, Eps);
        Assert.Null(placement.OffsetY);

        // The persisted placement reproduces the anchor's natural window, not the jittered drop.
        AssertWindow(monitor, IslandMode.Float, ActivityKind.Notification, placement, natural);
    }

    [Theory]
    [InlineData(96u)]
    [InlineData(144u)]
    [InlineData(192u)]
    public void Solve_ReleaseFarFromEveryAnchor_KeepsTheFreeOffset(uint dpi)
    {
        var monitor = Monitor(dpi);
        var work = monitor.WorkAreaDip;
        var natural = NaturalWindow(monitor, IslandMode.Float, IslandAnchor.TopCenter, ActivityKind.Media);
        var dropped = natural.Offset(0, work.Height * 0.45);

        var placement = IslandPlacementDrag.Solve(
            dropped, monitor, IslandMode.Float, ActivityKind.Media, IslandAnchor.TopCenter);

        Assert.False(placement.Snapped);
        Assert.NotEqual(0, placement.OffsetY);
        AssertWindow(monitor, IslandMode.Float, ActivityKind.Media, placement, dropped);
    }

    [Fact]
    public void Solve_RightAnchor_FlipsTheHorizontalOffsetSign()
    {
        var monitor = Monitor(96);
        var natural = NaturalWindow(monitor, IslandMode.Float, IslandAnchor.TopRight, ActivityKind.Notification);
        var dropped = natural.Offset(-100, 0);

        var placement = IslandPlacementDrag.Solve(
            dropped, monitor, IslandMode.Float, ActivityKind.Notification, IslandAnchor.TopRight);

        Assert.False(placement.Snapped);
        Assert.Equal(IslandAnchor.TopRight, placement.Anchor);
        Assert.Equal(100, placement.OffsetX, Eps);
        AssertWindow(monitor, IslandMode.Float, ActivityKind.Notification, placement, dropped);
    }

    [Fact]
    public void Solve_LeftAnchor_PersistsTheAbsoluteOffset()
    {
        var monitor = Monitor(96);
        var natural = NaturalWindow(monitor, IslandMode.Float, IslandAnchor.TopLeft, ActivityKind.Notification);
        var dropped = natural.Offset(150, 0);

        var placement = IslandPlacementDrag.Solve(
            dropped, monitor, IslandMode.Float, ActivityKind.Notification, IslandAnchor.TopLeft);

        Assert.False(placement.Snapped);
        Assert.Equal(IslandAnchor.TopLeft, placement.Anchor);
        Assert.Equal(150, placement.OffsetX, Eps);
        AssertWindow(monitor, IslandMode.Float, ActivityKind.Notification, placement, dropped);
    }

    [Fact]
    public void Solve_BottomAnchor_PersistsAnUpwardOffset()
    {
        var monitor = Monitor(96);
        var natural = NaturalWindow(monitor, IslandMode.Float, IslandAnchor.BottomCenter, ActivityKind.Notification);
        var dropped = natural.Offset(0, -80);

        var placement = IslandPlacementDrag.Solve(
            dropped, monitor, IslandMode.Float, ActivityKind.Notification, IslandAnchor.BottomCenter);

        Assert.False(placement.Snapped);
        Assert.Equal(IslandAnchor.BottomCenter, placement.Anchor);
        Assert.NotNull(placement.OffsetY);
        AssertWindow(monitor, IslandMode.Float, ActivityKind.Notification, placement, dropped);
    }

    [Fact]
    public void Solve_Flush_KeepsTheVerticalHalf_AndNeverChangesY()
    {
        var monitor = Monitor(96);
        var natural = NaturalWindow(monitor, IslandMode.Flush, IslandAnchor.TopCenter, ActivityKind.Notification);

        // Dropped far down the screen: flush must keep a top anchor and pin Y.
        var dropped = natural.Offset(120, monitor.WorkAreaDip.Height * 0.7);
        var placement = IslandPlacementDrag.Solve(
            dropped, monitor, IslandMode.Flush, ActivityKind.Notification, IslandAnchor.TopCenter);

        Assert.False(placement.Snapped);
        Assert.True(placement.Anchor is IslandAnchor.TopLeft or IslandAnchor.TopCenter or IslandAnchor.TopRight);
        Assert.Null(placement.OffsetY);

        var layout = Layout(monitor, IslandMode.Flush, ActivityKind.Notification, placement);
        Assert.Equal(natural.Y, layout.Placement.WindowVisual.Y, Eps);
        Assert.Equal(dropped.X, layout.Placement.WindowVisual.X, Eps);
    }

    [Fact]
    public void Solve_Dock_KeepsTheAnchorAndNaturalOffsets()
    {
        var monitor = Monitor(96);
        var dropped = new VisualRect(400, 500, 488, 444);

        var placement = IslandPlacementDrag.Solve(
            dropped, monitor, IslandMode.Dock, ActivityKind.Notification, IslandAnchor.TopCenter);

        Assert.False(placement.Snapped);
        Assert.Equal(IslandAnchor.TopCenter, placement.Anchor);
        Assert.Equal(0, placement.OffsetX, Eps);
        Assert.Null(placement.OffsetY);
    }

    [Theory]
    [InlineData(96u)]
    [InlineData(144u)]
    [InlineData(192u)]
    public void Solve_ClampsADropOutsideTheWorkArea(uint dpi)
    {
        var monitor = Monitor(dpi);
        var work = monitor.WorkAreaDip;

        // Way outside the work area: the persisted placement must clamp the host window inside it.
        var dropped = new VisualRect(work.Right + 500, work.Bottom + 500, 488, 384);
        var placement = IslandPlacementDrag.Solve(
            dropped, monitor, IslandMode.Float, ActivityKind.Notification, IslandAnchor.TopCenter);

        var layout = Layout(monitor, IslandMode.Float, ActivityKind.Notification, placement);
        var window = layout.Placement.WindowVisual;

        // The shell stays inside the work area; the host window may overhang it by the 24 DIP
        // transparent shadow bleed (Core's documented top/right anchor behaviour).
        Assert.True(layout.Shell.X >= work.X - Eps);
        Assert.True(layout.Shell.Y >= work.Y - Eps);
        Assert.True(layout.Shell.Right <= work.Right + Eps);
        Assert.True(layout.Shell.Bottom <= work.Bottom + Eps);
        Assert.True(window.X >= work.X - 24 - Eps);
        Assert.True(window.Y >= work.Y - 24 - Eps);
        Assert.True(window.Right <= work.Right + 24 + Eps);
        Assert.True(window.Bottom <= work.Bottom + 24 + Eps);
    }

    [Theory]
    [InlineData(96u)]
    [InlineData(144u)]
    [InlineData(192u)]
    public void Solve_EveryFreeDrop_ReproducesTheDroppedWindow(uint dpi)
    {
        var monitor = Monitor(dpi);
        var work = monitor.WorkAreaDip;
        var offsets = new[] { 0.1, 0.25, 0.5, 0.75, 0.9 };
        foreach (var mode in new[] { IslandMode.Float, IslandMode.Flush })
        {
            foreach (var fraction in offsets)
            {
                var dropped = new VisualRect(
                    (work.Width - 1263) * fraction,
                    (work.Height - 384) * fraction,
                    488,
                    384);
                var placement = IslandPlacementDrag.Solve(
                    dropped, monitor, mode, ActivityKind.Notification, IslandAnchor.TopCenter);

                var window = Layout(monitor, mode, ActivityKind.Notification, placement).Placement.WindowVisual;
                if (mode == IslandMode.Flush)
                {
                    // Flush pins Y: only X is reproduced.
                    Assert.Equal(dropped.X, window.X, Eps);
                    continue;
                }

                Assert.Equal(dropped.X, window.X, Eps);
                Assert.Equal(dropped.Y, window.Y, Eps);
            }
        }
    }

    private static void AssertWindow(
        MonitorInfo monitor,
        IslandMode mode,
        ActivityKind? kind,
        IslandDragPlacement placement,
        VisualRect expected)
    {
        var layout = Layout(monitor, mode, kind, placement);
        Assert.Equal(expected.X, layout.Placement.WindowVisual.X, Eps);
        Assert.Equal(expected.Y, layout.Placement.WindowVisual.Y, Eps);
    }

    private static IslandLayoutResult Layout(
        MonitorInfo monitor,
        IslandMode mode,
        ActivityKind? kind,
        IslandDragPlacement placement)
        => IslandLayout.Calculate(
            new IslandLayoutRequest
            {
                Form = ShellForm.Compact,
                CompactKind = kind,
                Mode = mode,
                Anchor = placement.Anchor,
                OffsetX = placement.OffsetX,
                OffsetY = placement.OffsetY ?? double.NaN,
                HiddenHoverStrip = false,
            },
            monitor);

    private static VisualRect NaturalWindow(
        MonitorInfo monitor,
        IslandMode mode,
        IslandAnchor anchor,
        ActivityKind? kind)
        => IslandLayout.Calculate(
            new IslandLayoutRequest
            {
                Form = ShellForm.Compact,
                CompactKind = kind,
                Mode = mode,
                Anchor = anchor,
                HiddenHoverStrip = false,
            },
            monitor).Placement.WindowVisual;

    private static MonitorInfo Monitor(uint dpi) => dpi switch
    {
        96 => MonitorInfo.Create("m96", new PixelRect(0, 0, 1920, 1040), 96, isPrimary: true),
        144 => MonitorInfo.Create("m144", new PixelRect(1920, 0, 2560, 1400), 144),
        192 => MonitorInfo.Create("m192", new PixelRect(4480, 0, 3840, 2080), 192),
        _ => throw new ArgumentOutOfRangeException(nameof(dpi)),
    };
}
