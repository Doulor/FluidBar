using FluidBar.App.Hosting.Skia;
using FluidBar.App.Settings;
using FluidBar.Core.Layout;
using CoreMonitorInfo = FluidBar.Core.Layout.MonitorInfo;

namespace FluidBar.Ui.Tests;

/// <summary>
/// Headless tests for the layered host's monitor targeting and add/remove diff
/// (<see cref="SkiaMonitorPlanner"/>). No HWND, no monitor service and no UI thread is required: the
/// planner is the pure seam the host applies on the UI thread.
/// </summary>
public sealed class SkiaMonitorPlannerTests
{
    [Fact]
    public void Plan_Primary_TargetsExactlyThePrimaryMonitor()
    {
        var primary = Monitor("\\\\.\\DISPLAY1", x: 0, isPrimary: true);
        var secondary = Monitor("\\\\.\\DISPLAY2", x: 1920);

        var plan = SkiaMonitorPlanner.Plan([], [primary, secondary], DisplayTarget.Primary);

        Assert.Equal(primary.Id, plan.Primary?.Id);
        Assert.Single(plan.Targets);
        Assert.Equal(primary.Id, plan.Targets[0].Id);
        Assert.Single(plan.Added);
        Assert.Empty(plan.Removed);
        Assert.Empty(plan.Retained);
    }

    [Fact]
    public void Plan_All_TargetsEveryMonitor_AndOrdersThePrimaryFirst()
    {
        var secondary = Monitor("\\\\.\\DISPLAY2", x: 1920);
        var primary = Monitor("\\\\.\\DISPLAY1", x: 0, isPrimary: true);
        var third = Monitor("\\\\.\\DISPLAY3", x: 3840);

        // Enumeration order is not guaranteed to put the primary first.
        var plan = SkiaMonitorPlanner.Plan([], [secondary, primary, third], DisplayTarget.All);

        Assert.Equal(3, plan.Targets.Length);
        Assert.Equal(primary.Id, plan.Targets[0].Id);
        Assert.Equal(new[] { secondary.Id, third.Id }, plan.Targets.Skip(1).Select(m => m.Id).ToArray());
        Assert.Equal(3, plan.Added.Length);
    }

    [Fact]
    public void Plan_AddRemove_ProducesTheExactWindowDiff()
    {
        var a = Monitor("\\\\.\\DISPLAY1", x: 0, isPrimary: true);
        var b = Monitor("\\\\.\\DISPLAY2", x: 1920);
        var c = Monitor("\\\\.\\DISPLAY3", x: 3840);

        var plan = SkiaMonitorPlanner.Plan([a, b], [a, c], DisplayTarget.All, previousPrimaryId: a.Id);

        Assert.Equal(a.Id, plan.Primary?.Id);
        Assert.Equal(new[] { a.Id, c.Id }, plan.Targets.Select(m => m.Id).ToArray());
        Assert.Single(plan.Added);
        Assert.Equal(c.Id, plan.Added[0].Id);
        Assert.Single(plan.Removed);
        Assert.Equal(b.Id, plan.Removed[0].Id);
        Assert.Single(plan.Retained);
        Assert.Equal(a.Id, plan.Retained[0].Id);
        Assert.True(plan.RequiresWindowChanges);
        Assert.False(plan.GeometryChanged);
    }

    [Fact]
    public void Plan_NoWindowChanges_WhenTheMonitorSetIsUnchanged()
    {
        var a = Monitor("\\\\.\\DISPLAY1", x: 0, isPrimary: true);
        var b = Monitor("\\\\.\\DISPLAY2", x: 1920);

        var plan = SkiaMonitorPlanner.Plan([a, b], [a, b], DisplayTarget.All, previousPrimaryId: a.Id);

        Assert.False(plan.RequiresWindowChanges);
        Assert.False(plan.GeometryChanged);
        Assert.Equal(2, plan.Retained.Length);
    }

    [Fact]
    public void Plan_WorkAreaChange_FlagsGeometryChanged_WithoutRecreatingWindows()
    {
        var before = Monitor("\\\\.\\DISPLAY1", x: 0, isPrimary: true);
        var after = before with { WorkArea = new PixelRect(0, 0, 1920, 980) };

        var plan = SkiaMonitorPlanner.Plan([before], [after], DisplayTarget.Primary, previousPrimaryId: before.Id);

        Assert.True(plan.GeometryChanged);
        Assert.False(plan.RequiresWindowChanges);
        Assert.Single(plan.Retained);
        Assert.Equal(980, plan.Retained[0].WorkArea.Height);
    }

    [Theory]
    [InlineData(120u)]
    [InlineData(144u)]
    [InlineData(192u)]
    public void Plan_DpiChange_FlagsGeometryChanged(uint newDpi)
    {
        var before = Monitor("\\\\.\\DISPLAY1", x: 0, isPrimary: true);
        var after = before with { Dpi = newDpi };

        var plan = SkiaMonitorPlanner.Plan([before], [after], DisplayTarget.Primary, previousPrimaryId: before.Id);

        Assert.True(plan.GeometryChanged);
        Assert.False(plan.RequiresWindowChanges);
        Assert.Equal(newDpi, plan.Retained[0].Dpi);
    }

    [Fact]
    public void Plan_PrimaryFlagChange_DoesNotCountAsGeometryChange()
    {
        var a = Monitor("\\\\.\\DISPLAY1", x: 0, isPrimary: true);
        var b = Monitor("\\\\.\\DISPLAY2", x: 1920);
        var aAfter = a with { IsPrimary = false };
        var bAfter = b with { IsPrimary = true };

        var plan = SkiaMonitorPlanner.Plan([a, b], [aAfter, bAfter], DisplayTarget.All, previousPrimaryId: a.Id);

        Assert.Equal(bAfter.Id, plan.Primary?.Id);
        Assert.Equal(bAfter.Id, plan.Targets[0].Id);
        Assert.False(plan.GeometryChanged);
        Assert.False(plan.RequiresWindowChanges);
    }

    [Fact]
    public void ResolvePrimary_WithoutAnOsFlag_KeepsThePreviousPrimary()
    {
        var a = Monitor("\\\\.\\DISPLAY1", x: 0);
        var b = Monitor("\\\\.\\DISPLAY2", x: 1920);

        var primary = SkiaMonitorPlanner.ResolvePrimary([a, b], previousPrimaryId: b.Id);

        Assert.Equal(b.Id, primary?.Id);
    }

    [Fact]
    public void ResolvePrimary_WithoutAFlagOrHistory_UsesTheFirstMonitor()
    {
        var a = Monitor("\\\\.\\DISPLAY1", x: 0);
        var b = Monitor("\\\\.\\DISPLAY2", x: 1920);

        Assert.Equal(a.Id, SkiaMonitorPlanner.ResolvePrimary([a, b])?.Id);
        Assert.Null(SkiaMonitorPlanner.ResolvePrimary([]));
    }

    [Fact]
    public void Plan_EmptySnapshot_RemovesEveryWindow()
    {
        var a = Monitor("\\\\.\\DISPLAY1", x: 0, isPrimary: true);
        var b = Monitor("\\\\.\\DISPLAY2", x: 1920);

        var plan = SkiaMonitorPlanner.Plan([a, b], [], DisplayTarget.All, previousPrimaryId: a.Id);

        Assert.Empty(plan.Targets);
        Assert.Null(plan.Primary);
        Assert.Equal(2, plan.Removed.Length);
        Assert.Empty(plan.Added);
        Assert.True(plan.RequiresWindowChanges);
    }

    [Fact]
    public void Plan_RemovedPrimary_PromotesTheFlaggedRemainingMonitor()
    {
        var primary = Monitor("\\\\.\\DISPLAY1", x: 0, isPrimary: true);
        var other = Monitor("\\\\.\\DISPLAY2", x: 1920);
        var otherPromoted = other with { IsPrimary = true };

        var plan = SkiaMonitorPlanner.Plan([primary, other], [otherPromoted], DisplayTarget.All,
            previousPrimaryId: primary.Id);

        Assert.Equal(otherPromoted.Id, plan.Primary?.Id);
        Assert.Single(plan.Targets);
        Assert.Single(plan.Removed);
        Assert.Equal(primary.Id, plan.Removed[0].Id);
        Assert.Single(plan.Retained);
    }

    [Fact]
    public void Plan_FollowMouse_FallsBackToThePrimaryMonitorUntilThePointerSeamExists()
    {
        var primary = Monitor("\\\\.\\DISPLAY1", x: 0, isPrimary: true);
        var secondary = Monitor("\\\\.\\DISPLAY2", x: 1920);

        var plan = SkiaMonitorPlanner.Plan([], [primary, secondary], DisplayTarget.FollowMouse);

        Assert.Single(plan.Targets);
        Assert.Equal(primary.Id, plan.Targets[0].Id);
    }

    private static CoreMonitorInfo Monitor(string id, int x, uint dpi = 96, bool isPrimary = false)
        => CoreMonitorInfo.Create(
            id,
            new PixelRect(x, 0, 1920, 1040),
            dpi,
            new PixelRect(x, 0, 1920, 1080),
            isPrimary);
}
