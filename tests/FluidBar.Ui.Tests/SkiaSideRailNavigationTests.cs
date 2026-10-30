using FluidBar.App.Hosting.Skia;
using FluidBar.Core.Interaction;
using FluidBar.Island;

namespace FluidBar.Ui.Tests;

/// <summary>Controller-level navigation and hover for the new in-card page rail.</summary>
public sealed class SkiaSideRailNavigationTests
{
    [Fact]
    public void ClickingAnyTab_NavigatesDirectlyToThatPage()
    {
        using var fixture = ControllerFixture.Create();
        fixture.GoToPage(0);
        fixture.GoToPage(0);
        fixture.ExpandAndSettle();
        var state = fixture.Controller.RenderState;
        var content = IslandMetrics.ContentRect(IslandMetrics.ShellRect(state));
        var tab = SideRailLayout.TabRect(content, 4);

        Assert.True(fixture.Controller.HandlePointerPressed(tab.MidX, tab.MidY));
        fixture.Controller.HandlePointerReleased(tab.MidX, tab.MidY);
        fixture.RunToRest();

        Assert.Equal(4, fixture.Controller.Pager.SelectedIndex);
    }

    [Fact]
    public void HoveringTab_ProjectsControlPageAndIndex()
    {
        using var fixture = ControllerFixture.Create();
        fixture.ExpandAndSettle();
        var state = fixture.Controller.RenderState;
        var content = IslandMetrics.ContentRect(IslandMetrics.ShellRect(state));
        var tab = SideRailLayout.TabRect(content, 2);
        fixture.Controller.HandlePointerMoved(content.MidX, content.MidY);
        fixture.Controller.HandlePointerMoved(tab.MidX, tab.MidY);

        state = fixture.Controller.RenderState;
        Assert.Equal(IslandPageControl.PageTab, state.HoverControl);
        Assert.Equal(2, state.HoverPage);
        Assert.Equal(2, state.HoverIndex);
    }

    [Fact]
    public void WheelOverRail_PagesInsteadOfHittingRemovedMiniIslands()
    {
        using var fixture = ControllerFixture.Create();
        fixture.ExpandAndSettle();
        var state = fixture.Controller.RenderState;
        var content = IslandMetrics.ContentRect(IslandMetrics.ShellRect(state));
        var tab = SideRailLayout.TabRect(content, 0);
        fixture.Controller.HandlePointerMoved(content.MidX, content.MidY);
        fixture.Controller.HandlePointerMoved(tab.MidX, tab.MidY);
        fixture.Time.Advance(TimeSpan.FromMilliseconds(1000));
        for (var i = 0; i < 120 && fixture.Controller.IsAnimating; i++)
        {
            fixture.Time.Advance(TimeSpan.FromMilliseconds(16));
            fixture.Controller.Advance(TimeSpan.FromMilliseconds(16));
        }
        fixture.Controller.Motion.SnapToTarget();
        fixture.Controller.Advance(TimeSpan.FromMilliseconds(16));
        var outcome = fixture.Controller.HandleWheel(60, horizontal: false, shift: false);
        Assert.True(outcome.Consumed || outcome.Paged);
        Assert.Equal(1, fixture.Controller.Pager.SelectedIndex);
        var pageRect = IslandMetrics.PageRect(fixture.Controller.RenderState, 1);
        Assert.Equal(IslandHitKind.PageTab, SkiaIslandHitTest.Hit(fixture.Controller.RenderState, SideRailLayout.TabRect(content, 1).MidX, SideRailLayout.TabRect(content, 1).MidY).Kind);
        Assert.True(pageRect.Left > 0);
        Assert.Equal(IslandHitKind.None,
            SkiaIslandHitTest.Hit(fixture.Controller.RenderState, content.MidX, IslandMetrics.ShellRect(state).Bottom + 16).Kind);
    }
}
