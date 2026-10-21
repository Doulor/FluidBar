using System.Collections.Immutable;
using FluidBar.Core.Activities;

namespace FluidBar.Core.Tests;

/// <summary>
/// Split-island primary-selection rules chosen by the user (2026-10-20): actionable short events
/// take the pill, persistent content becomes balls, the earlier activity wins ties, and the pill is
/// protected by a 2 s residency window and a 10 s user lock.
/// </summary>
public sealed class SplitStackPolicyTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 20, 2, 0, 0, TimeSpan.Zero);

    private static ActivityRecord Record(ActivityKind kind, string source, DateTimeOffset received,
        ActivityPayload? payload = null)
        => ActivityRecord.Create(
            new ActivityKey(kind, source),
            payload ?? new ActivityPayload { Title = source },
            sequence: 1,
            now: received);

    [Fact]
    public void Media_plus_running_agent_keeps_media_as_primary_and_agent_as_ball()
    {
        var policy = new SplitStackPolicy();
        var media = Record(ActivityKind.Media, "media", T0);
        var agent = Record(ActivityKind.Agent, "agent", T0.AddSeconds(1),
            new ActivityPayload { Title = "Agent", IsActive = true, Badge = "running" });
        var visible = ImmutableArray.Create(media, agent);

        var ordered = policy.Order(visible, T0.AddSeconds(3));

        Assert.Equal(ActivityKind.Media, ordered[0].Kind);
        Assert.Equal(ActivityKind.Agent, ordered[1].Kind);
    }

    [Fact]
    public void Media_plus_waiting_agent_puts_agent_primary_then_restores_media_when_it_ends()
    {
        var policy = new SplitStackPolicy();
        var media = Record(ActivityKind.Media, "media", T0);
        var waiting = Record(ActivityKind.Agent, "agent", T0.AddSeconds(3),
            new ActivityPayload { Title = "Agent", IsActive = true, Badge = "waiting" });

        _ = policy.Order(ImmutableArray.Create(media), T0);
        var ordered = policy.Order(ImmutableArray.Create(media, waiting), T0.AddSeconds(3));

        Assert.Equal(ActivityKind.Agent, ordered[0].Kind);
        Assert.Equal(ActivityKind.Media, ordered[1].Kind);

        // The agent resolves/disappears: the previous persistent primary is restored.
        var restored = policy.Order(ImmutableArray.Create(media), T0.AddSeconds(4));
        Assert.Equal(ActivityKind.Media, restored[0].Kind);
    }

    [Fact]
    public void Clipboard_takes_the_pill_after_the_dwell_window_and_media_comes_back_after_its_ttl()
    {
        var policy = new SplitStackPolicy();
        var media = Record(ActivityKind.Media, "media", T0);
        _ = policy.Order(ImmutableArray.Create(media), T0);

        var copy = Record(ActivityKind.Clipboard, "clipboard", T0.AddSeconds(3),
            new ActivityPayload { Title = "Copied" });
        var ordered = policy.Order(ImmutableArray.Create(media, copy), T0.AddSeconds(3));
        Assert.Equal(ActivityKind.Clipboard, ordered[0].Kind);

        // The scheduler drops the clipboard after its 4 s TTL; the policy restores the media pill.
        var restored = policy.Order(ImmutableArray.Create(media), T0.AddSeconds(7.1));
        Assert.Equal(ActivityKind.Media, restored[0].Kind);
    }

    [Fact]
    public void Rapid_clipboard_copies_do_not_thrash_the_primary_inside_the_dwell_window()
    {
        var policy = new SplitStackPolicy();
        var media = Record(ActivityKind.Media, "media", T0);
        var first = Record(ActivityKind.Clipboard, "clipboard-1", T0.AddSeconds(3),
            new ActivityPayload { Title = "one" });
        _ = policy.Order(ImmutableArray.Create(media), T0);
        _ = policy.Order(ImmutableArray.Create(media, first), T0.AddSeconds(3));

        for (var i = 2; i <= 3; i++)
        {
            // The clipboard plugin reuses one activity key; each copy is an in-place payload update.
            var next = Record(ActivityKind.Clipboard, "clipboard-1",
                T0.AddSeconds(3).AddMilliseconds(200 * i), new ActivityPayload { Title = "copy" + i });
            var ordered = policy.Order(ImmutableArray.Create(media, next), T0.AddSeconds(3).AddMilliseconds(200 * i));
            Assert.Equal(ActivityKind.Clipboard, ordered[0].Kind);
            Assert.Equal("clipboard-1", ordered[0].Key.Source);
        }
    }

    [Fact]
    public void Three_persistent_activities_put_the_earliest_first()
    {
        var policy = new SplitStackPolicy();
        var media = Record(ActivityKind.Media, "media", T0);
        var timer = Record(ActivityKind.Timer, "timer", T0.AddSeconds(1));
        var battery = Record(ActivityKind.BatteryLow, "battery", T0.AddSeconds(2));

        var ordered = policy.Order(ImmutableArray.Create(media, timer, battery), T0.AddSeconds(3));

        Assert.Equal(ActivityKind.Media, ordered[0].Kind);
        // Balls are ordered by importance (low-battery rank 11 before timer rank 13).
        Assert.Equal(ActivityKind.BatteryLow, ordered[1].Kind);
        Assert.Equal(ActivityKind.Timer, ordered[2].Kind);
    }

    [Fact]
    public void Wheel_rotation_overrides_the_policy_and_cycles_the_primary()
    {
        var policy = new SplitStackPolicy();
        var media = Record(ActivityKind.Media, "media", T0);
        var timer = Record(ActivityKind.Timer, "timer", T0.AddSeconds(1));
        var visible = ImmutableArray.Create(media, timer);
        _ = policy.Order(visible, T0.AddSeconds(3));

        var forward = policy.Order(visible, T0.AddSeconds(3).AddMilliseconds(100), wheelSteps: 1);
        Assert.Equal(ActivityKind.Timer, forward[0].Kind);

        var backward = policy.Order(visible, T0.AddSeconds(3).AddMilliseconds(200), wheelSteps: -1);
        Assert.Equal(ActivityKind.Media, backward[0].Kind);
    }

    [Fact]
    public void User_interaction_locks_the_pill_for_ten_seconds()
    {
        var policy = new SplitStackPolicy();
        var media = Record(ActivityKind.Media, "media", T0);
        var timer = Record(ActivityKind.Timer, "timer", T0.AddSeconds(1));
        _ = policy.Order(ImmutableArray.Create(media, timer), T0.AddSeconds(3));

        policy.NoteUserInteraction(timer.Key, T0.AddSeconds(3));
        var ordered = policy.Order(ImmutableArray.Create(media, timer), T0.AddSeconds(4));
        Assert.Equal(ActivityKind.Timer, ordered[0].Kind);
        Assert.True(policy.IsUserLocked(timer.Key, T0.AddSeconds(12)));
        Assert.False(policy.IsUserLocked(timer.Key, T0.AddSeconds(13.1)));
    }

    [Fact]
    public void Wheel_pin_survives_the_user_lock_window_and_ordinary_reordering()
    {
        var policy = new SplitStackPolicy();
        var media = Record(ActivityKind.Media, "media", T0);
        var agent = Record(ActivityKind.Agent, "agent", T0.AddSeconds(1), new ActivityPayload { Title = "Agent", IsActive = true, Badge = "running" });
        var visible = ImmutableArray.Create(media, agent);
        _ = policy.Order(visible, T0.AddSeconds(2));
        _ = policy.Order(visible, T0.AddSeconds(2.1), wheelSteps: 1);
        Assert.Equal(agent.Key, policy.PinnedKey);

        var ordered = policy.Order(visible, T0.AddSeconds(20));
        Assert.Equal(ActivityKind.Agent, ordered[0].Kind);
        Assert.Equal(agent.Key, policy.PinnedKey);
    }

    [Fact]
    public void Wheeling_to_another_item_retargets_the_pin()
    {
        var policy = new SplitStackPolicy();
        var media = Record(ActivityKind.Media, "media", T0);
        var timer = Record(ActivityKind.Timer, "timer", T0.AddSeconds(1));
        var visible = ImmutableArray.Create(media, timer);
        _ = policy.Order(visible, T0.AddSeconds(2));
        _ = policy.Order(visible, T0.AddSeconds(2.1), wheelSteps: 1);
        Assert.Equal(timer.Key, policy.PinnedKey);
    }

    [Fact]
    public void Short_event_preempts_a_pin_then_returns_to_it()
    {
        var policy = new SplitStackPolicy();
        var media = Record(ActivityKind.Media, "media", T0);
        var agent = Record(ActivityKind.Agent, "agent", T0.AddSeconds(1), new ActivityPayload { Title = "Agent", IsActive = true, Badge = "running" });
        var notify = Record(ActivityKind.Notification, "notify", T0.AddSeconds(3), new ActivityPayload { Title = "Ping" });
        _ = policy.Order(ImmutableArray.Create(media, agent), T0.AddSeconds(2));
        _ = policy.Order(ImmutableArray.Create(media, agent), T0.AddSeconds(2.1), wheelSteps: 1);

        var withNotify = policy.Order(ImmutableArray.Create(media, agent, notify), T0.AddSeconds(4.2));
        Assert.Equal(ActivityKind.Notification, withNotify[0].Kind);

        var restored = policy.Order(ImmutableArray.Create(media, agent), T0.AddSeconds(5));
        Assert.Equal(ActivityKind.Agent, restored[0].Kind);
    }

    [Fact]
    public void Pin_releases_when_the_pinned_activity_ends()
    {
        var policy = new SplitStackPolicy();
        var media = Record(ActivityKind.Media, "media", T0);
        var timer = Record(ActivityKind.Timer, "timer", T0.AddSeconds(1));
        _ = policy.Order(ImmutableArray.Create(media, timer), T0.AddSeconds(2));
        _ = policy.Order(ImmutableArray.Create(media, timer), T0.AddSeconds(2.1), wheelSteps: 1);
        Assert.Equal(timer.Key, policy.PinnedKey);

        var afterEnd = policy.Order(ImmutableArray.Create(media), T0.AddSeconds(10));
        Assert.Equal(ActivityKind.Media, afterEnd[0].Kind);
        Assert.Null(policy.PinnedKey);
    }
}

