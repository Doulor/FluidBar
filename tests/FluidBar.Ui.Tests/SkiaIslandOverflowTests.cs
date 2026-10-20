using FluidBar.App.Hosting.Skia;
using FluidBar.Core.Activities;
using FluidBar.Core.Layout;
using FluidBar.Island;

namespace FluidBar.Ui.Tests;

/// <summary>
/// Compact "+N" overflow chip through the real controller: width reservation, hit target, expansion
/// to the oldest background activity's page, and UI Automation exposure/invoke.
/// </summary>
public sealed class SkiaIslandOverflowTests
{
    [Fact]
    public void Four_activities_reserve_the_chip_column_and_hit_the_chip()
    {
        var fixture = ControllerFixture.Create();
        using var _ = fixture;
        fixture.Settle();
        PublishFour(fixture);
        fixture.Settle();

        var state = fixture.Controller.RenderState;
        Assert.Equal(1, state.OverflowCount);
        Assert.Equal(1, fixture.Controller.SchedulerState.BadgeCount);
        Assert.Equal(PageRef.Media, fixture.Controller.SchedulerState.Background[0].Kind.ToPage());
        var expectedWidth = LayoutTokens.CompactSizeFor(state.Compact?.Kind, state.Mode, state.FontScale).Width
            + LayoutTokens.OverflowBadgeReserve;
        Assert.Equal(expectedWidth, state.ShellWidth, 0.001);

        var badge = IslandMetrics.OverflowBadgeRect(state);
        Assert.True(badge.Width > 0);
        Assert.Equal(IslandHitKind.OverflowBadge, SkiaIslandHitTest.Hit(state, badge.MidX, badge.MidY).Kind);
        var shell = IslandMetrics.ShellRect(state);
        Assert.Equal(IslandHitKind.Compact, SkiaIslandHitTest.Hit(state, shell.Left + 10, shell.MidY).Kind);
    }

    [Fact]
    public void Overflow_chip_expands_to_the_background_activity_page()
    {
        var fixture = ControllerFixture.Create();
        using var _ = fixture;
        fixture.Settle();
        PublishFour(fixture);
        fixture.Settle();

        Assert.True(fixture.Controller.ActivateOverflowBadge());
        fixture.Settle();

        Assert.Equal(IslandStateKind.Expanded, fixture.Controller.State.Kind);
        Assert.Equal((int)PageRef.Media, fixture.Controller.Pager.SelectedIndex);
        Assert.Equal(0, fixture.Controller.RenderState.OverflowCount);
    }

    private static void PublishFour(ControllerFixture fixture)
    {
        var controller = fixture.Controller;
        controller.PublishActivity(ActivityKind.Media,
            new ActivityPayload { Title = "Midnight City", Subtitle = "M83", IsActive = true }, 1, "test");
        controller.PublishActivity(ActivityKind.Timer,
            new ActivityPayload { Title = "Pomodoro", Level = 8 }, 2, "test");
        controller.PublishActivity(ActivityKind.BatteryLow,
            new ActivityPayload { Title = "Low battery", Level = 12 }, 3, "test");
        controller.PublishActivity(ActivityKind.Progress,
            new ActivityPayload { Title = "Export", ProgressPercent = 40 }, 4, "test");
    }
}

