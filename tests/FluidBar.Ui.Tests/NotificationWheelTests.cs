using FluidBar.App.Hosting.Skia;
using FluidBar.Core.Activities;
using FluidBar.Core.Interaction;
using FluidBar.Core.Layout;
using FluidBar.Island;

namespace FluidBar.Ui.Tests;

public sealed class NotificationWheelTests
{
    private static ControllerFixture Create(RecordingIslandPlatformActions actions)
    {
        var f = ControllerFixture.Create(actions: actions);
        f.Controller.PublishActivity(ActivityKind.Notification, new ActivityPayload
        { Title = "Toast", Body = "Keep history", SourceName = "App", Ttl = TimeSpan.FromMinutes(5) }, 1, "wheel-test");
        f.Settle();
        var shell = IslandMetrics.ShellRect(f.Controller.RenderState);
        f.Controller.HandlePointerMoved(shell.MidX, shell.MidY);
        return f;
    }

    private static void Step(ControllerFixture f, int frames)
    {
        for (var i = 0; i < frames; i++)
        {
            f.Time.Advance(TimeSpan.FromMilliseconds(16));
            f.Controller.Advance(TimeSpan.FromMilliseconds(16));
            f.Controller.PumpTimers();
        }
    }

    [Fact]
    public void First_notch_lifts_second_notch_retracts_banner_without_deleting_history()
    {
        var actions = new RecordingIslandPlatformActions();
        using var f = Create(actions);
        var history = f.Controller.RenderState.Pages.Notifications.Items.ToArray();
        f.Controller.HandleWheel(-PagerConstants.WheelDipPerNotch, false, false);
        Step(f, 12);
        Assert.True(f.Controller.RenderState.NotificationDragOffsetY < -4);
        Assert.Equal(ActivityKind.Notification, f.Controller.RenderState.Compact?.Kind);
        // The real pointer does not move after the first notch, even though the card moves away.
        f.Controller.HandleWheel(-PagerConstants.WheelDipPerNotch, false, false);
        Step(f, 90);
        Assert.NotEqual(ActivityKind.Notification, f.Controller.RenderState.Compact?.Kind);
        Assert.Equal(history, f.Controller.RenderState.Pages.Notifications.Items.ToArray());
        Assert.DoesNotContain(actions.Calls, a => a.Kind == IslandPlatformActionKind.NotificationDismiss);
        Assert.Equal(0, f.Controller.RenderState.NotificationDragOffsetY, 3);
    }

    [Fact]
    public void Single_notch_returns_and_does_not_count_toward_a_later_gesture()
    {
        using var f = Create(new RecordingIslandPlatformActions());
        f.Controller.HandleWheel(-PagerConstants.WheelDipPerNotch, false, false);
        Step(f, 100);
        Assert.Equal(0, f.Controller.RenderState.NotificationDragOffsetY, 3);
        f.Controller.HandleWheel(-PagerConstants.WheelDipPerNotch, false, false);
        Step(f, 10);
        Assert.True(f.Controller.RenderState.NotificationDragOffsetY < -4);
        Assert.Equal(ActivityKind.Notification, f.Controller.RenderState.Compact?.Kind);
    }

    [Fact]
    public void Fractional_wheel_steps_accumulate_but_reverse_scrolling_does_not_dismiss()
    {
        using var f = Create(new RecordingIslandPlatformActions());
        for (var i = 0; i < 4; i++) f.Controller.HandleWheel(-PagerConstants.WheelDipPerNotch / 4, false, false);
        Step(f, 10);
        Assert.True(f.Controller.RenderState.NotificationDragOffsetY < -4);
        f.Controller.HandleWheel(PagerConstants.WheelDipPerNotch, false, false);
        Step(f, 60);
        Assert.Equal(0, f.Controller.RenderState.NotificationDragOffsetY, 3);
        Assert.Equal(ActivityKind.Notification, f.Controller.RenderState.Compact?.Kind);
    }

    [Fact]
    public void Expanded_notifications_page_wheel_does_not_dismiss_banner()
    {
        using var f = Create(new RecordingIslandPlatformActions());
        f.Controller.ApplyInput(IslandInput.PrimaryClick());
        f.Controller.HandlePageRequest(PageRef.Notifications);
        f.Settle();
        Assert.Equal(ShellForm.Expanded, f.Controller.RenderState.Form);
        f.Controller.HandleWheel(-PagerConstants.WheelDipPerNotch * 2, false, false);
        Step(f, 60);
        Assert.Equal(0, f.Controller.RenderState.NotificationDragOffsetY, 3);
        Assert.Contains(f.Controller.SchedulerState.Visible, r => r.Kind == ActivityKind.Notification);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Opening_panel_cancels_pending_wheel_lift_or_exit(int notches)
    {
        using var f = Create(new RecordingIslandPlatformActions());
        f.Controller.HandleWheel(-PagerConstants.WheelDipPerNotch * notches, false, false);
        f.Controller.ApplyInput(IslandInput.PrimaryClick());
        f.Settle();
        Step(f, 80);
        Assert.Equal(ShellForm.Expanded, f.Controller.RenderState.Form);
        Assert.Equal(0, f.Controller.RenderState.NotificationDragOffsetY, 3);
        Assert.Contains(f.Controller.SchedulerState.Visible, r => r.Kind == ActivityKind.Notification);
    }

    [Fact]
    public void Identical_text_new_sequence_cancels_the_previous_gesture()
    {
        using var f = Create(new RecordingIslandPlatformActions());
        f.Controller.HandleWheel(-PagerConstants.WheelDipPerNotch * 2, false, false);
        f.Controller.PublishActivity(ActivityKind.Notification, new ActivityPayload
        { Title = "Toast", Body = "Keep history", SourceName = "App", Ttl = TimeSpan.FromMinutes(5) }, 2, "wheel-test");
        Step(f, 90);
        Assert.Equal(ActivityKind.Notification, f.Controller.RenderState.Compact?.Kind);
        Assert.Equal(2, f.Controller.SchedulerState.Visible.Single(r => r.Kind == ActivityKind.Notification).Sequence);
        Assert.Equal(0, f.Controller.RenderState.NotificationDragOffsetY, 3);
    }

    [Fact]
    public void New_notification_between_press_and_drag_threshold_is_not_dismissed()
    {
        using var f = Create(new RecordingIslandPlatformActions());
        var shell = IslandMetrics.ShellRect(f.Controller.RenderState);
        f.Controller.HandlePointerPressed(shell.MidX, shell.MidY);
        f.Controller.HandleDragStarted(shell.MidX, shell.MidY, 0);
        f.Controller.PublishActivity(ActivityKind.Notification, new ActivityPayload
        { Title = "New toast", Ttl = TimeSpan.FromMinutes(5) }, 2, "wheel-test");
        Assert.False(f.Controller.BeginNotificationDrag());
        Step(f, 90);
        Assert.Equal("New toast", f.Controller.RenderState.Compact?.Model.Title);
    }

    [Fact]
    public void Replacement_is_not_hidden_or_retracted_by_an_old_wheel_gesture()
    {
        using var f = Create(new RecordingIslandPlatformActions());
        f.Controller.HandleWheel(-PagerConstants.WheelDipPerNotch, false, false);
        Step(f, 8);
        f.Controller.PublishActivity(ActivityKind.Notification, new ActivityPayload
        { Title = "New toast", SourceName = "App", Ttl = TimeSpan.FromMinutes(5) }, 2, "wheel-test");
        Step(f, 60);
        Assert.Equal("New toast", f.Controller.RenderState.Compact?.Model.Title);
        Assert.Equal(0, f.Controller.RenderState.NotificationDragOffsetY, 3);
    }
}
