using FluidBar.App.Hosting.Skia;
using FluidBar.App.Settings;
using FluidBar.Core.Activities;
using FluidBar.Core.Interaction;
using FluidBar.Core.Layout;
using FluidBar.Island;

namespace FluidBar.Ui.Tests;

/// <summary>
/// Split-island (cell division) behavior through the real controller: the user-chosen primary rules,
/// preview balls, +N overflow and the click-to-merge navigation.
/// </summary>
public sealed class SplitIslandTests
{
    private static ControllerFixture Create() => ControllerFixture.Create(
        AppSettings.Default with { AutoCollapseMs = 0, AutoHideMs = 0 });

    private static void Publish(ControllerFixture fixture, ActivityKind kind, ActivityPayload payload, long seq,
        string source = "test")
        => fixture.Controller.PublishActivity(kind, payload, seq, source);

    private static void Settle(ControllerFixture fixture)
    {
        fixture.Time.Advance(TimeSpan.FromMilliseconds(16));
        fixture.Controller.PumpTimers();
        fixture.Settle();
    }

    [Fact]
    public void Media_plus_running_agent_keeps_media_primary_and_agent_ball()
    {
        using var fixture = Create();
        fixture.Settle();
        Publish(fixture, ActivityKind.Media, new ActivityPayload
        {
            Title = "Midnight City", IsActive = true, ShowsAudioWave = true,
        }, 1);
        Settle(fixture);
        Publish(fixture, ActivityKind.Agent, new ActivityPayload
        {
            Title = "Agent", Badge = "running", IsActive = true,
        }, 2);
        Settle(fixture);

        Assert.Equal(ActivityKind.Media, fixture.Controller.RenderState.Compact.Value.Kind);
        var islets = fixture.Controller.RenderState.SplitIslets;
        Assert.Single(islets);
        Assert.Equal(ActivityKind.Agent, islets[0].Kind);
    }

    [Fact]
    public void Waiting_agent_takes_primary_then_media_restores_when_it_resolves()
    {
        using var fixture = Create();
        fixture.Settle();
        Publish(fixture, ActivityKind.Media, new ActivityPayload { Title = "Song", IsActive = true }, 1);
        Settle(fixture);
        fixture.Time.Advance(TimeSpan.FromMilliseconds(2100));
        Settle(fixture);

        Publish(fixture, ActivityKind.Agent, new ActivityPayload
        {
            Title = "Agent", Badge = "waiting", IsActive = true,
        }, 2);
        Settle(fixture);
        Assert.Equal(ActivityKind.Agent, fixture.Controller.RenderState.Compact.Value.Kind);

        fixture.Controller.RetractActivity(ActivityKind.Agent, 3, "test");
        Settle(fixture);
        Assert.Equal(ActivityKind.Media, fixture.Controller.RenderState.Compact.Value.Kind);
    }

    [Fact]
    public void Clipboard_takes_primary_after_dwell_and_media_returns_after_ttl()
    {
        using var fixture = Create();
        fixture.Settle();
        Publish(fixture, ActivityKind.Media, new ActivityPayload { Title = "Song", IsActive = true }, 1);
        Settle(fixture);
        fixture.Time.Advance(TimeSpan.FromMilliseconds(2100));
        Settle(fixture);

        Publish(fixture, ActivityKind.Clipboard, new ActivityPayload { Title = "Copied" }, 2);
        Settle(fixture);
        Assert.Equal(ActivityKind.Clipboard, fixture.Controller.RenderState.Compact.Value.Kind);

        // Clipboard TTL is 4 s; once it expires the media pill comes back.
        fixture.Time.Advance(TimeSpan.FromSeconds(4.2));
        Settle(fixture);
        fixture.Controller.Advance(TimeSpan.FromMilliseconds(16));
        Assert.Equal(ActivityKind.Media, fixture.Controller.RenderState.Compact.Value.Kind);
    }

    [Fact]
    public void Consecutive_clipboard_copies_do_not_thrash_the_primary()
    {
        using var fixture = Create();
        fixture.Settle();
        Publish(fixture, ActivityKind.Media, new ActivityPayload { Title = "Song", IsActive = true }, 1);
        Settle(fixture);
        fixture.Time.Advance(TimeSpan.FromMilliseconds(2100));
        Settle(fixture);

        for (var i = 0; i < 3; i++)
        {
            Publish(fixture, ActivityKind.Clipboard, new ActivityPayload { Title = "copy" + i }, 2 + i);
            fixture.Time.Advance(TimeSpan.FromMilliseconds(150));
            Settle(fixture);
            Assert.Equal(ActivityKind.Clipboard, fixture.Controller.RenderState.Compact.Value.Kind);
        }
    }

    [Fact]
    public void Three_activities_show_two_balls_and_no_badge_and_four_add_the_badge()
    {
        using var fixture = Create();
        fixture.Settle();
        Publish(fixture, ActivityKind.Media, new ActivityPayload { Title = "Song", IsActive = true }, 1);
        fixture.Time.Advance(TimeSpan.FromMilliseconds(120));
        Publish(fixture, ActivityKind.Timer, new ActivityPayload { Title = "Pomodoro", Level = 8 }, 2);
        fixture.Time.Advance(TimeSpan.FromMilliseconds(120));
        Publish(fixture, ActivityKind.BatteryLow, new ActivityPayload { Title = "Low battery", Level = 12 }, 3);
        Settle(fixture);

        var visible = fixture.Controller.SchedulerState.Visible;
        Assert.True(visible.Length == 3,
            $"visible={visible.Length} kinds=[{string.Join(",", visible.Select(r => r.Kind))}]");
        Assert.Equal(ActivityKind.Media, fixture.Controller.RenderState.Compact.Value.Kind);
        var state = fixture.Controller.RenderState;
        Assert.True(state.SplitIslets.Length == 2,
            $"islets={state.SplitIslets.Length} ghosts={state.GhostCount} split={state.SplitProgress:0.00} " +
            $"visible={fixture.Controller.SchedulerState.Visible.Length} overflow={state.OverflowCount}");
        Assert.Equal(0, fixture.Controller.RenderState.OverflowCount);

        fixture.Time.Advance(TimeSpan.FromMilliseconds(120));
        Publish(fixture, ActivityKind.Progress, new ActivityPayload { Title = "Export", ProgressPercent = 40 }, 4);
        Settle(fixture);
        Assert.Equal(1, fixture.Controller.RenderState.OverflowCount);
        Assert.Equal(2, fixture.Controller.RenderState.SplitIslets.Length);
    }


    [Fact]
    public void Stacked_wheel_rotates_the_primary_through_the_visible_stack()
    {
        using var fixture = ControllerFixture.Create(
            AppSettings.Default with { AutoCollapseMs = 0, AutoHideMs = 0, Stacking = StackingStrategySetting.Stacked });
        fixture.Settle();
        Publish(fixture, ActivityKind.Media, new ActivityPayload { Title = "Song", IsActive = true }, 1);
        fixture.Time.Advance(TimeSpan.FromMilliseconds(120));
        Publish(fixture, ActivityKind.Timer, new ActivityPayload { Title = "Pomodoro", Level = 8 }, 2);
        fixture.Time.Advance(TimeSpan.FromMilliseconds(120));
        Publish(fixture, ActivityKind.BatteryLow, new ActivityPayload { Title = "Low battery", Level = 12 }, 3);
        Settle(fixture);

        var first = fixture.Controller.RenderState.Compact.Value.Kind;
        Assert.True(fixture.Controller.HandleWheel(-100, horizontal: false, shift: false).Consumed);
        Settle(fixture);
        var second = fixture.Controller.RenderState.Compact.Value.Kind;
        Assert.NotEqual(first, second);

        Assert.True(fixture.Controller.HandleWheel(-100, horizontal: false, shift: false).Consumed);
        Settle(fixture);
        Assert.True(fixture.Controller.HandleWheel(-100, horizontal: false, shift: false).Consumed);
        Settle(fixture);
        Assert.Equal(first, fixture.Controller.RenderState.Compact.Value.Kind);
    }

    [Fact]
    public void Stacked_wheel_pin_survives_heartbeats_and_new_ordinary_activity()
    {
        using var fixture = ControllerFixture.Create(AppSettings.Default with
        {
            AutoCollapseMs = 0,
            AutoHideMs = 0,
            Stacking = StackingStrategySetting.Stacked,
        });
        fixture.Settle();
        Publish(fixture, ActivityKind.Agent, new ActivityPayload { Title = "Agent", Badge = "running", IsActive = true }, 1, "agent");
        Settle(fixture);
        Publish(fixture, ActivityKind.Media, new ActivityPayload { Title = "Song", IsActive = true }, 2, "media");
        Settle(fixture);

        Assert.True(fixture.Controller.HandleWheel(-60, horizontal: false, shift: false).Consumed);
        Settle(fixture);
        Assert.Equal(ActivityKind.Media, fixture.Controller.RenderState.Compact.Value.Kind);

        for (var beat = 0; beat < 8; beat++)
        {
            fixture.Time.Advance(TimeSpan.FromSeconds(2));
            Publish(fixture, ActivityKind.Agent, new ActivityPayload
            {
                Title = "Agent", Badge = "running", IsActive = true, Body = "heartbeat " + beat,
            }, 10 + beat, "agent");
            Settle(fixture);
        }

        Assert.Equal(ActivityKind.Media, fixture.Controller.RenderState.Compact.Value.Kind);

        Publish(fixture, ActivityKind.Timer, new ActivityPayload { Title = "Pomodoro", Level = 8 }, 30, "timer");
        Settle(fixture);
        Assert.Equal(ActivityKind.Media, fixture.Controller.RenderState.Compact.Value.Kind);
    }

    [Fact]
    public void Wheel_pin_survives_activity_heartbeats_through_the_controller()
    {
        using var fixture = ControllerFixture.Create(AppSettings.Default with
        {
            AutoCollapseMs = 0,
            AutoHideMs = 0,
            Stacking = StackingStrategySetting.Split,
        });
        fixture.Settle();
        Publish(fixture, ActivityKind.Agent, new ActivityPayload { Title = "Agent", Badge = "running", IsActive = true }, 1, "agent");
        Settle(fixture);
        Publish(fixture, ActivityKind.Media, new ActivityPayload { Title = "Song", IsActive = true }, 2, "media");
        Settle(fixture);

        // Wheel until media owns the main pill.
        var mediaPinned = false;
        for (var i = 0; i < 4; i++)
        {
            fixture.Controller.HandleWheel(PagerConstants.WheelDipPerNotch, horizontal: false, shift: false);
            Settle(fixture);
            if (fixture.Controller.RenderState.Compact.Value.Kind == ActivityKind.Media)
            {
                mediaPinned = true;
                break;
            }
        }
        Assert.True(mediaPinned, "the wheel must be able to put media in the primary slot");
        var order = fixture.Controller.RenderState.SplitIslets.Select(i => i.Kind).ToArray();

        // Eight heartbeat re-publishes (16 s) of the same agent key: policy must keep the wheel pin.
        for (var beat = 0; beat < 8; beat++)
        {
            fixture.Time.Advance(TimeSpan.FromSeconds(2));
            Publish(fixture, ActivityKind.Agent, new ActivityPayload
            {
                Title = "Agent", Badge = "running", IsActive = true, Body = "heartbeat " + beat,
            }, 10 + beat, "agent");
            Settle(fixture);
        }

        Assert.Equal(ActivityKind.Media, fixture.Controller.RenderState.Compact.Value.Kind);
        Assert.Equal(order, fixture.Controller.RenderState.SplitIslets.Select(i => i.Kind).ToArray());

        // A new ordinary/persistent activity must not steal the wheel pin either.
        Publish(fixture, ActivityKind.Timer, new ActivityPayload { Title = "Pomodoro", Level = 8 }, 20, "timer");
        Settle(fixture);
        Assert.Equal(ActivityKind.Media, fixture.Controller.RenderState.Compact.Value.Kind);
    }

    [Fact]
    public void Only_the_ball_rectangles_are_interactive()
    {
        using var fixture = Create();
        fixture.Settle();
        Publish(fixture, ActivityKind.Media, new ActivityPayload { Title = "Song", IsActive = true }, 1);
        Publish(fixture, ActivityKind.Timer, new ActivityPayload { Title = "Pomodoro", Level = 8 }, 2);
        Settle(fixture);

        var state = fixture.Controller.RenderState;
        var balls = IslandMetrics.SplitIsletRects(state);
        Assert.NotEmpty(balls);
        var shell = IslandMetrics.ShellRect(state);
        // The transparent gap between the pill and the ball must stay click-through.
        var gapX = shell.Right + ((balls[0].Left - shell.Right) / 2);
        Assert.Equal(IslandHitKind.None, SkiaIslandHitTest.Hit(state, gapX, balls[0].MidY).Kind);
    }

    [Fact]
    public void Clicking_a_ball_merges_it_and_opens_its_page()
    {
        using var fixture = Create();
        fixture.Settle();
        Publish(fixture, ActivityKind.Media, new ActivityPayload { Title = "Song", IsActive = true }, 1);
        fixture.Time.Advance(TimeSpan.FromMilliseconds(120));
        Publish(fixture, ActivityKind.Timer, new ActivityPayload { Title = "Pomodoro", Level = 8 }, 2);
        fixture.Time.Advance(TimeSpan.FromMilliseconds(120));
        Publish(fixture, ActivityKind.BatteryLow, new ActivityPayload { Title = "Low battery", Level = 12 }, 3);
        Settle(fixture);

        var balls = IslandMetrics.SplitIsletRects(fixture.Controller.RenderState);
        Assert.Equal(2, balls.Length);
        Assert.Equal(IslandHitKind.SplitIslet, SkiaIslandHitTest.Hit(
            fixture.Controller.RenderState, balls[0].MidX, balls[0].MidY).Kind);

        fixture.Controller.HandlePointerPressed(balls[0].MidX, balls[0].MidY);
        fixture.Controller.HandlePointerReleased(balls[0].MidX, balls[0].MidY);
        fixture.Settle();

        Assert.Equal(IslandStateKind.Expanded, fixture.Controller.State.Kind);
        Assert.True(fixture.Controller.Pager.SelectedIndex >= 0);
        Assert.InRange(fixture.Controller.RenderState.SplitProgress, 0, 0.02);
    }
}


