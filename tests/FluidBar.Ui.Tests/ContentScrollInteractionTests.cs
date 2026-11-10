using FluidBar.App.Hosting.Skia;
using FluidBar.App.Settings;
using FluidBar.App.State;
using FluidBar.Core.Activities;
using FluidBar.Island;

namespace FluidBar.Ui.Tests;

public sealed class ContentScrollInteractionTests
{
    [Fact]
    public void Captured_scrollbar_prevents_auto_collapse_until_release()
    {
        using var fixture = ControllerFixture.Create(AppSettings.Default with { FontScale = 1.6, AutoCollapseMs = 300, AutoHideMs = 1000 });
        fixture.ExpandAndSettle();
        fixture.GoToPage(3);
        var state = fixture.Controller.RenderState;
        var geometry = ContentScrollLayout.Measure(state.Pages, 3, IslandMetrics.PageRect(state, 3), FontScaleMetrics.From(state.FontScale));
        fixture.Controller.HandlePointerPressed(geometry.Thumb.MidX, geometry.Thumb.MidY);
        fixture.Controller.HandlePointerLeft();
        fixture.Time.Advance(TimeSpan.FromSeconds(3));
        fixture.Controller.PumpTimers();
        Assert.Equal(IslandStateKind.Expanded, fixture.Controller.State.Kind);
        Assert.True(fixture.Controller.HasContentScrollDrag);
        fixture.Controller.HandlePointerReleased(geometry.Thumb.MidX, geometry.Track.Bottom + 80);
        fixture.Controller.PumpTimers();
        Assert.False(fixture.Controller.HasContentScrollDrag);
        Assert.NotEqual(IslandStateKind.Expanded, fixture.Controller.State.Kind);
    }

    [Fact]
    public void Changing_page_cancels_a_scrollbar_drag()
    {
        using var fixture = ControllerFixture.Create(AppSettings.Default with { FontScale = 1.6, AutoCollapseMs = 0, AutoHideMs = 0 });
        fixture.ExpandAndSettle();
        fixture.GoToPage(3);
        var state = fixture.Controller.RenderState;
        var geometry = ContentScrollLayout.Measure(state.Pages, 3, IslandMetrics.PageRect(state, 3), FontScaleMetrics.From(state.FontScale));
        fixture.Controller.HandlePointerPressed(geometry.Thumb.MidX, geometry.Thumb.MidY);
        Assert.True(fixture.Controller.HasContentScrollDrag);
        fixture.GoToPage(2);
        Assert.False(fixture.Controller.HasContentScrollDrag);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(5, false)]
    public void Every_builtin_scrollbar_drags_to_both_ends_without_paging(int page, bool detail)
    {
        using var fixture = ControllerFixture.Create(AppSettings.Default with { FontScale = 1.6, AutoCollapseMs = 0, AutoHideMs = 0 });
        fixture.Data.Update(c => c with { Agents = [.. Enumerable.Range(0, 10).Select(i => new AgentItemData { Name = "Agent " + i })] });
        if (detail) fixture.Data.Update(c => c with { NotificationDetail = c.Notifications[0] with
        { Id = 42, FullBody = string.Concat(Enumerable.Repeat("完整的通知正文，需要滚动阅读。", 80)) } });
        fixture.ExpandAndSettle();
        fixture.GoToPage(page);
        var state = fixture.Controller.RenderState;
        var geometry = ContentScrollLayout.Measure(state.Pages, page, IslandMetrics.PageRect(state, page), FontScaleMetrics.From(state.FontScale));
        Assert.True(geometry.CanScroll);
        var hit = SkiaIslandHitTest.Hit(state, geometry.Thumb.MidX, geometry.Thumb.MidY);
        Assert.Equal(IslandPageControl.Scrollbar, hit.Control);
        using var renderer = new IslandRenderer();
        Assert.Equal(hit, renderer.HitTest(state, geometry.Thumb.MidX, geometry.Thumb.MidY));
        fixture.Controller.HandlePointerPressed(geometry.Thumb.MidX, geometry.Thumb.MidY);
        Assert.True(fixture.Controller.HasContentScrollDrag);
        fixture.Controller.HandlePointerMoved(geometry.Thumb.MidX, geometry.Track.Bottom + 200);
        Assert.Equal(geometry.Maximum, Current().Offset, 3);
        fixture.Controller.HandlePointerMoved(geometry.Thumb.MidX, geometry.Track.Top - 200);
        Assert.Equal(0, Current().Offset, 3);
        fixture.Controller.HandlePointerReleased(geometry.Thumb.MidX, geometry.Track.Top);
        Assert.False(fixture.Controller.HasContentScrollDrag);
        Assert.Equal(page, fixture.Controller.Pager.SelectedIndex);
        ContentScrollGeometry Current() => ContentScrollLayout.Measure(fixture.Controller.RenderState.Pages, page,
            IslandMetrics.PageRect(fixture.Controller.RenderState, page), FontScaleMetrics.From(1.6));
    }

    [Fact]
    public void Wheel_eases_monotonically_and_reversal_has_no_old_direction_backlog()
    {
        using var fixture = ControllerFixture.Create(AppSettings.Default with { ReducedMotion = ReducedMotionPreference.Off, AutoCollapseMs = 0, AutoHideMs = 0 });
        fixture.Data.Update(c => c with { Clipboard = [.. Enumerable.Range(0, 20).Select(i => new ClipboardItemData { Preview = "Item " + i })] });
        fixture.ExpandAndSettle();
        fixture.GoToPage(3);
        fixture.Controller.HandleWheel(60, false, false);
        Assert.InRange(Offset(), 1, 59);
        var previous = Offset();
        for (var i = 0; i < 60; i++)
        {
            fixture.Controller.Advance(TimeSpan.FromMilliseconds(16));
            Assert.InRange(Offset(), previous, 60);
            previous = Offset();
        }
        Assert.Equal(60, Offset(), 3);
        fixture.Controller.HandleWheel(120, false, false);
        var beforeReverse = Offset();
        fixture.Controller.HandleWheel(-60, false, false);
        Assert.True(Offset() < beforeReverse);
        fixture.RunToRest();
        Assert.True(Offset() < beforeReverse);
        Assert.False(fixture.Controller.IsContentScrollAnimating);
        double Offset() => fixture.Controller.RenderState.Pages.Clipboard.ScrollOffset;
    }
}
