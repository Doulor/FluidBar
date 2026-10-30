using FluidBar.App.Hosting.Pages;
using FluidBar.App.Settings;
using FluidBar.App.Hosting.Skia;
using FluidBar.Core.Interaction;
using FluidBar.Core.Layout;
using FluidBar.Island;
using FluidBar.Sdk.Manifest;
using FluidBar.Sdk.Pages;

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
    public void SettingsAndCollapse_HaveImmediateHoverState()
    {
        using var fixture = ControllerFixture.Create();
        fixture.ExpandAndSettle();
        var content = IslandMetrics.ContentRect(IslandMetrics.ShellRect(fixture.Controller.RenderState));
        var settings = PageLayout.SettingsRect(content);
        fixture.Controller.HandlePointerMoved(content.MidX, content.MidY);
        fixture.Controller.HandlePointerMoved(settings.MidX, settings.MidY);
        Assert.Equal(IslandPageControl.Settings, fixture.Controller.RenderState.HoverControl);

        var collapse = PageLayout.CollapseRect(content);
        fixture.Controller.HandlePointerMoved(collapse.MidX, collapse.MidY);
        Assert.Equal(IslandPageControl.Collapse, fixture.Controller.RenderState.HoverControl);
    }

    [Fact]
    public void RightRailSide_PlacesTheRailAndContentOnTheRight()
    {
        using var fixture = ControllerFixture.Create(settings: AppSettings.Default with
        {
            AutoCollapseMs = 0,
            AutoHideMs = 0,
            RailSide = PageRailSide.Right,
        });
        fixture.ExpandAndSettle();
        var state = fixture.Controller.RenderState;
        Assert.Equal(PageRailSide.Right, state.RailSide);

        var content = IslandMetrics.ContentRect(IslandMetrics.ShellRect(state), state.RailSide);
        Assert.Equal(state.ShellOrigin.X + LayoutTokens.ContentRightInset, content.Left, 0.001);
        Assert.Equal(content.Left + LayoutTokens.ContentWidth, content.Right, 0.001);
        var tab = SideRailLayout.TabRect(content, 0, 0, state.Pages.PageCount, PageRailSide.Right);
        Assert.True(tab.Left > content.Right);
        Assert.Equal(IslandHitKind.PageTab, SkiaIslandHitTest.Hit(state, tab.MidX, tab.MidY).Kind);
    }

    [Fact]
    public void SeventhPage_ScrollsTheRailByOnePitch_AndKeepsItsTabVisible()
    {
        var registry = new PageRegistry();
        Assert.True(registry.RegisterPage("test-rail", new PageDescriptor
        {
            PageId = "test-rail.extra",
            Order = 100,
            Title = new LocalizedString("Extra"),
            ShortTitle = new LocalizedString("Extra"),
            IconToken = "list",
            TemplateId = PageTemplates.List,
        }, out var error), error);

        using var fixture = ControllerFixture.Create(pageRegistry: registry);
        fixture.ExpandAndSettle();
        Assert.Equal(7, fixture.Controller.RenderState.Pages.PageCount);

        fixture.Controller.Pager.SelectPage(6);
        fixture.Time.Advance(TimeSpan.FromMilliseconds(16));
        fixture.Controller.Advance(TimeSpan.FromMilliseconds(16));
        Assert.InRange(fixture.Controller.RenderState.Pages.RailScrollOffset, 0.1, 43.9);
        Assert.InRange(fixture.Controller.RenderState.RailSelectionPosition, 0.01, 5.99);
        fixture.RunToRest();
        var state = fixture.Controller.RenderState;
        var content = IslandMetrics.ContentRect(IslandMetrics.ShellRect(state));
        Assert.Equal(SideRailLayout.MaxScroll(7, content), state.Pages.RailScrollOffset, 0.001);
        Assert.Equal(6.0, state.RailSelectionPosition, 0.001);
        var tab = SideRailLayout.TabRect(content, 6, state.Pages.RailScrollOffset, 7);
        Assert.Equal(content.Bottom, tab.Bottom, 0.001);
        Assert.Equal(IslandHitKind.PageTab, SkiaIslandHitTest.Hit(state, tab.MidX, tab.MidY).Kind);
    }

    [Fact]
    public void WheelChangesPage_AndRailFollowsSelectionInsteadOfScrollingOnWheel()
    {
        using var fixture = ControllerFixture.Create();
        fixture.GoToPage(0);
        fixture.ExpandAndSettle();
        var content = IslandMetrics.ContentRect(IslandMetrics.ShellRect(fixture.Controller.RenderState));
        var tab = SideRailLayout.TabRect(content, 0);
        fixture.Controller.HandlePointerMoved(content.MidX, content.MidY);
        fixture.Controller.HandlePointerMoved(tab.MidX, tab.MidY);
        fixture.Time.Advance(TimeSpan.FromMilliseconds(1000));
        var outcome = fixture.Controller.HandleWheel(60, horizontal: false, shift: false);
        Assert.True(outcome.Consumed || outcome.Paged);
        Assert.Equal(1, fixture.Controller.Pager.SelectedIndex);
        Assert.Equal(0, fixture.Controller.RenderState.Pages.RailScrollOffset, 0.001);
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
