using FluidBar.App.Hosting.Skia;
using FluidBar.App.Settings;
using FluidBar.App.State;
using FluidBar.Core.Interaction;
using FluidBar.Island;

namespace FluidBar.Ui.Tests;

public sealed class SkiaWheelArbitrationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void All_builtin_pages_keep_vertical_wheel_inside_the_page(int page)
    {
        using var fixture = ControllerFixture.Create(AppSettings.Default with { FontScale = 1.6, AutoCollapseMs = 0, AutoHideMs = 0 });
        fixture.ExpandAndSettle();
        fixture.GoToPage(page);
        for (var i = 0; i < 20; i++)
        {
            var result = fixture.Controller.HandleWheel(60, false, false);
            Assert.True(result.ContentScroll);
            Assert.False(result.Paged);
            fixture.Time.Advance(TimeSpan.FromMilliseconds(250));
        }
        Assert.Equal(page, fixture.Controller.Pager.SelectedIndex);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(10)]
    public void Vertical_wheel_stays_on_content_page_even_at_both_edges(int count)
    {
        using var fixture = Create(count);
        for (var i = 0; i < 16; i++)
        {
            var result = fixture.Controller.HandleWheel(140, false, false);
            Assert.True(result.ContentScroll);
            Assert.False(result.Paged);
            fixture.Time.Advance(TimeSpan.FromMilliseconds(250));
        }
        fixture.RunToRest();
        var max = ContentCardLayout.ClipboardMaxScroll(fixture.Controller.RenderState.Pages.Clipboard, FontScaleMetrics.One);
        Assert.Equal(max, fixture.Controller.RenderState.Pages.Clipboard.ScrollOffset, 3);
        var bottom = fixture.Controller.RenderState.Pages.Clipboard.ScrollOffset;
        fixture.Controller.HandleWheel(-60, false, false);
        fixture.RunToRest();
        Assert.Equal(Math.Max(0, bottom - 60), fixture.Controller.RenderState.Pages.Clipboard.ScrollOffset, 3);
        for (var i = 0; i < 30; i++) fixture.Controller.HandleWheel(-140, false, false);
        fixture.RunToRest();
        Assert.Equal(0, fixture.Controller.RenderState.Pages.Clipboard.ScrollOffset);
        Assert.Equal(3, fixture.Controller.Pager.SelectedIndex);
    }

    [Fact]
    public void Shift_and_horizontal_wheel_still_offer_explicit_navigation()
    {
        using var fixture = Create(10);
        Assert.True(fixture.Controller.HandleWheel(140, false, true).Paged);
        Assert.Equal(4, fixture.Controller.Pager.SelectedIndex);
        fixture.Time.Advance(TimeSpan.FromMilliseconds(300));
        fixture.RunToRest();
        Assert.True(fixture.Controller.HandleWheel(-140, true, false).Paged);
        Assert.Equal(3, fixture.Controller.Pager.SelectedIndex);
    }

    [Fact]
    public void Side_rail_navigates_but_moving_into_body_scrolls_immediately()
    {
        using var fixture = Create(10);
        var state = fixture.Controller.RenderState;
        var content = IslandMetrics.ContentRect(IslandMetrics.ShellRect(state), state.RailSide, state.TabBarStyle);
        var tab = SideRailLayout.TabRect(content, 3, 0, state.Pages.PageCount);
        fixture.Controller.HandlePointerMoved(tab.MidX, tab.MidY);
        Assert.True(fixture.Controller.HandleWheel(-60, false, false).Paged);
        Assert.Equal(2, fixture.Controller.Pager.SelectedIndex);
        fixture.GoToPage(3);
        var page = IslandMetrics.PageRect(fixture.Controller.RenderState, 3);
        fixture.Controller.HandlePointerMoved(page.MidX, page.MidY);
        Assert.True(fixture.Controller.HandleWheel(60, false, false).ContentScroll);
        Assert.InRange(fixture.Controller.RenderState.Pages.Clipboard.ScrollOffset, 0.1, 59.9);
        fixture.RunToRest();
        Assert.Equal(60, fixture.Controller.RenderState.Pages.Clipboard.ScrollOffset, 3);
    }

    private static ControllerFixture Create(int count)
    {
        var fixture = ControllerFixture.Create(AppSettings.Default with { AutoCollapseMs = 0, AutoHideMs = 0 });
        fixture.Data.Update(content => content with { Clipboard = [.. Enumerable.Range(1, count).Select(i =>
            new ClipboardItemData { Preview = $"Clipboard item {i}", Meta = "1 min" })] });
        fixture.ExpandAndSettle();
        fixture.GoToPage(3);
        return fixture;
    }
}
