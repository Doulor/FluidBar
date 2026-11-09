using FluidBar.App.Hosting.Skia;
using FluidBar.Island;

namespace FluidBar.Ui.Tests;

public sealed class NotificationCardInteractionTests
{
    [Fact]
    public void Filtered_row_actions_use_stable_id_and_detail_copy_keeps_original_body()
    {
        using var fixture = ControllerFixture.Create();
        fixture.ExpandAndSettle();
        fixture.GoToPage(2);
        var item = new NotificationItemModel { Id = 97, Body = "summary", FullBody = "full\n  original body", VerificationCode = "123456" };
        var state = fixture.Controller.RenderState;
        state = state with { Pages = state.Pages with { Notifications = new NotificationsPageModel { Filter = 2, Items = [item] } } };
        foreach (var control in new[] { IslandPageControl.Row, IslandPageControl.Dismiss, IslandPageControl.Copy })
        {
            var action = SkiaIslandControlRouter.Map(new IslandHitTarget(IslandHitKind.PageControl, 2, control, 0), state, 0);
            Assert.NotNull(action);
            Assert.Equal((uint)97, action.Value.NotificationId);
        }
        state = state with { Pages = state.Pages with { Notifications = state.Pages.Notifications with { Detail = item } } };
        var copy = SkiaIslandControlRouter.Map(new IslandHitTarget(IslandHitKind.PageControl, 2, IslandPageControl.NotificationCopyBody), state, 0);
        Assert.Equal(item.FullBody, copy!.Value.Text);
        var open = SkiaIslandControlRouter.Map(new IslandHitTarget(IslandHitKind.PageControl, 2, IslandPageControl.NotificationOpenSource), state, 0);
        Assert.Equal((uint)97, open!.Value.NotificationId);
    }

    [Fact]
    public void Entering_detail_resets_scroll_and_back_restores_list_scroll()
    {
        using var fixture = ControllerFixture.Create();
        fixture.ExpandAndSettle();
        fixture.GoToPage(2);
        var controller = fixture.Controller;
        var scroll = (double[])typeof(SkiaIslandController).GetField("_pageScroll", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(controller)!;
        var apply = typeof(SkiaIslandController).GetMethod("ApplyPageScroll", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var pages = controller.RenderState.Pages;
        scroll[2] = 70;
        var detail = new NotificationItemModel { Id = 99, FullBody = new string('文', 4000) };
        var entered = (ExpandedPageContent)apply.Invoke(controller, [pages with { Notifications = pages.Notifications with { Detail = detail } }])!;
        Assert.Equal(0, entered.Notifications.ScrollOffset);
        scroll[2] = 180;
        var returned = (ExpandedPageContent)apply.Invoke(controller, [pages])!;
        Assert.Equal(70, returned.Notifications.ScrollOffset);
    }
}
