using FluidBar.App.Hosting.Skia;
using FluidBar.Core.Activities;
using FluidBar.Core.Layout;
using FluidBar.Island;

namespace FluidBar.Ui.Tests;

/// <summary>
/// The notification card has no other way to disappear early. These tests pin the drag feel and the
/// complete gesture path: threshold, damped offset, spring exit and the platform dismiss action.
/// </summary>
public sealed class NotificationDismissGestureTests
{
    [Fact]
    public void Upward_travel_is_compressed_but_keeps_increasing()
    {
        Assert.Equal(0, NotificationDismissGesture.OffsetYDip(0));
        var twoRows = NotificationDismissGesture.OffsetYDip(NotificationDismissGesture.DismissThresholdDip);
        var longPull = NotificationDismissGesture.OffsetYDip(220);

        Assert.True(twoRows < 0);
        Assert.True(twoRows > -NotificationDismissGesture.DismissThresholdDip);
        Assert.True(longPull < twoRows);
        Assert.True(longPull > -220);
        Assert.InRange(NotificationDismissGesture.OffsetYDip(-100), 0, NotificationDismissGesture.MaxDownwardOffsetDip);
    }

    [Fact]
    public void Two_row_pull_or_a_short_upward_flick_dismisses()
    {
        Assert.True(NotificationDismissGesture.ShouldDismiss(NotificationDismissGesture.DismissThresholdDip, 0));
        Assert.False(NotificationDismissGesture.ShouldDismiss(NotificationDismissGesture.DismissThresholdDip - 1, 0));
        Assert.True(NotificationDismissGesture.ShouldDismiss(40, -800));
        Assert.False(NotificationDismissGesture.ShouldDismiss(40, -200));
        Assert.False(NotificationDismissGesture.ShouldDismiss(20, -900));
    }

    [Fact]
    public void Shell_rect_moves_with_the_notification_drag_offset()
    {
        var state = new IslandRenderState
        {
            ShellOrigin = new VisualPoint(10, 20),
            ShellWidth = 100,
            ShellHeight = 36,
            NotificationDragOffsetY = -50,
        };

        var shell = IslandMetrics.ShellRect(state);
        Assert.Equal(-30f, shell.Top, 3);
        Assert.Equal(-30f, shell.Bottom - 36f, 3);
    }

    [Fact]
    public void Dragging_a_notification_past_the_threshold_dismisses_it_after_the_exit_spring()
    {
        var actions = new RecordingIslandPlatformActions();
        using var fixture = ControllerFixture.Create(actions: actions);
        fixture.Controller.PublishActivity(ActivityKind.Notification, new ActivityPayload
        {
            Title = "GitHub",
            Body = "Run failed: Export Live Data",
            SourceName = "GitHub",
            Ttl = TimeSpan.FromMinutes(5),
        }, 1, "test");
        fixture.Settle();
        fixture.RunToRest();
        fixture.Settle();

        var state = fixture.Controller.RenderState;
        Assert.Equal(ActivityKind.Notification, state.Compact?.Kind);
        var shell = IslandMetrics.ShellRect(state);
        Assert.Equal(SkiaDragKind.NotificationCandidate,
            fixture.Controller.HandleDragStarted(shell.MidX, shell.MidY, 0));
        Assert.True(fixture.Controller.BeginNotificationDrag());

        fixture.Controller.HandleNotificationDragMoved(shell.MidY - 90, 100);
        fixture.Controller.HandleNotificationDragEnded(shell.MidY - 90, 120);
        fixture.Controller.Advance(TimeSpan.FromMilliseconds(16));
        Assert.True(fixture.Controller.RenderState.NotificationDragOffsetY < 0);

        for (var i = 0; i < 160 && actions.Calls.All(call => call.Kind != IslandPlatformActionKind.NotificationDismiss); i++)
        {
            fixture.Controller.Advance(TimeSpan.FromMilliseconds(16));
        }

        var dismiss = Assert.Single(actions.Calls.Where(call => call.Kind == IslandPlatformActionKind.NotificationDismiss));
        Assert.Equal(0, dismiss.Index);
        Assert.True(fixture.Controller.RenderState.NotificationDragOffsetY <= NotificationDismissGesture.DismissAtDip);
    }

    [Fact]
    public void A_short_drag_springs_back_and_keeps_the_notification()
    {
        var actions = new RecordingIslandPlatformActions();
        using var fixture = ControllerFixture.Create(actions: actions);
        fixture.Controller.PublishActivity(ActivityKind.Notification, new ActivityPayload
        {
            Title = "GitHub",
            Body = "Run failed: Export Live Data",
            SourceName = "GitHub",
            Ttl = TimeSpan.FromMinutes(5),
        }, 1, "test");
        fixture.Settle();
        fixture.RunToRest();
        fixture.Settle();

        var shell = IslandMetrics.ShellRect(fixture.Controller.RenderState);
        Assert.Equal(SkiaDragKind.NotificationCandidate,
            fixture.Controller.HandleDragStarted(shell.MidX, shell.MidY, 0));
        Assert.True(fixture.Controller.BeginNotificationDrag());
        fixture.Controller.HandleNotificationDragMoved(shell.MidY - 30, 100);
        fixture.Controller.HandleNotificationDragEnded(shell.MidY - 30, 120);

        for (var i = 0; i < 120; i++)
        {
            fixture.Controller.Advance(TimeSpan.FromMilliseconds(16));
        }

        Assert.Empty(actions.Calls);
        Assert.Equal(ActivityKind.Notification, fixture.Controller.RenderState.Compact?.Kind);
        Assert.Equal(0, fixture.Controller.RenderState.NotificationDragOffsetY, 3);
    }
}
