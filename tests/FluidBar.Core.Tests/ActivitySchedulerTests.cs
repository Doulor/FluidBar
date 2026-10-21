using FluidBar.Core.Activities;
using FluidBar.Core.Diagnostics;
using FluidBar.Core.Events;
using FluidBar.Core.Tests.TestSupport;

namespace FluidBar.Core.Tests;

public class ActivitySchedulerTests
{
    private readonly ActivityScheduler _scheduler = new();

    private ActivityKey Key(ActivityKind kind, string source = "s") => new(kind, source);

    [Fact]
    public void NewActivity_BecomesPrimary()
    {
        var update = _scheduler.Update(ActivitySchedulerState.Empty, TestData.Notification(), TestData.Now);

        Assert.Equal(ActivityUpdateKind.Added, update.Kind);
        Assert.True(update.PrimaryChanged);
        Assert.Equal(ActivityKind.Notification, update.State.PrimaryKind);
        Assert.Equal(1, update.State.ActivityCount);
    }

    [Fact]
    public void HigherPriorityActivity_PreemptsTransientPrimary()
    {
        var state = TestData.BuildState(_scheduler, TestData.Notification("notif", "notification"));
        var volume = IslandEvent.New(Key(ActivityKind.Volume), TestData.Payload("vol"), 1);

        var update = _scheduler.Update(state, volume, TestData.Now);

        Assert.Equal(ActivityUpdateKind.Preempted, update.Kind);
        Assert.Equal(ActivityKind.Volume, update.State.PrimaryKind);
        // A preempted transient activity is discarded (spec 2.3 rule 2), not shown as a ghost.
        Assert.Single(update.State.Visible);
        Assert.Empty(update.State.Background);
        Assert.True(update.State.DroppedCount >= 1);
    }

    [Fact]
    public void HigherPriorityActivity_DemotesPersistentPrimaryToGhost()
    {
        var state = TestData.BuildState(_scheduler, TestData.Media());
        var volume = IslandEvent.New(Key(ActivityKind.Volume), TestData.Payload("vol"), 1);

        var update = _scheduler.Update(state, volume, TestData.Now);

        Assert.Equal(ActivityKind.Volume, update.State.PrimaryKind);
        Assert.Contains(update.State.Visible, r => r.Kind == ActivityKind.Media);
        Assert.Equal(2, update.State.ActivityCount);
    }

    [Fact]
    public void LowerPriorityActivity_JoinsStackAsGhost()
    {
        var state = TestData.BuildState(_scheduler, TestData.Notification("notif", "notification"));
        var media = TestData.Media();

        var update = _scheduler.Update(state, media, TestData.Now);

        Assert.Equal(ActivityKind.Notification, update.State.PrimaryKind);
        Assert.Equal(ActivityUpdateKind.Added, update.Kind);
        Assert.Equal(2, update.State.Visible.Length);
        Assert.Equal(
            ActivityPriorityPolicy.Rank(ActivityKind.Media) - ActivityPriorityPolicy.Rank(ActivityKind.Notification),
            update.State.Visible[1].PriorityRank - update.State.Visible[0].PriorityRank);
    }

    [Fact]
    public void SameKeyNew_UpdatesInPlaceWithoutNewLayer()
    {
        var state = TestData.BuildState(_scheduler, TestData.Media());
        var replacement = IslandEvent.New(Key(ActivityKind.Media, "media:gsmtc"), TestData.Payload("new track"), 2);

        var update = _scheduler.Update(state, replacement, TestData.Now);

        Assert.Equal(ActivityUpdateKind.ReplacedInPlace, update.Kind);
        Assert.Single(update.State.Visible);
        Assert.Equal("new track", update.State.Primary!.Payload.Title);
    }

    [Fact]
    public void IncrementalEvent_MergesPayloadAndKeepsPosition()
    {
        var state = TestData.BuildState(_scheduler, TestData.Media());
        var key = Key(ActivityKind.Media, "media:gsmtc");
        var patch = new IslandEvent(key, IslandEventKind.Incremental, 2, default,
            new ActivityPayload { ProgressPercent = 42, Title = string.Empty });

        var update = _scheduler.Update(state, patch, TestData.Now);

        Assert.Equal(ActivityUpdateKind.ContentUpdated, update.Kind);
        Assert.True(update.ContentChanged);
        Assert.Equal(42, update.State.Primary!.Payload.ProgressPercent);
        Assert.Equal("Track", update.State.Primary.Payload.Title);
        Assert.Single(update.State.Visible);
    }

    [Fact]
    public void PlayingMediaStaysPersistentAndVisibleAcrossEightHeartbeats()
    {
        var key = TestData.Key(ActivityKind.Media, "media:gsmtc");
        var state = TestData.BuildState(_scheduler, TestData.Media());
        for (var beat = 1; beat <= 8; beat++)
        {
            var heartbeat = IslandEvent.New(key, TestData.Payload("Track") with { IsActive = true, ShowsAudioWave = true }, beat + 1);
            state = _scheduler.Update(state, heartbeat, TestData.Now.AddSeconds(beat * 2)).State;
            Assert.True(state.Primary!.IsPersistent, $"beat {beat}");
            Assert.Contains(state.Visible, r => r.Kind == ActivityKind.Media);
        }
    }

    [Fact]
    public void PlayingMediaWaveFallbackRemainsPersistentWhenIsActiveIsOmitted()
    {
        var key = TestData.Key(ActivityKind.Media, "media:wave");
        var media = IslandEvent.New(key, TestData.Payload("Track") with { IsActive = false, ShowsAudioWave = true }, 1);
        var state = TestData.BuildState(_scheduler, media);
        Assert.True(state.Primary!.IsPersistent);
        Assert.Contains(state.Visible, r => r.Kind == ActivityKind.Media);
    }

    [Fact]
    public void CompletedAgentExpiresAfterThreeSecondCompletionBeat()
    {
        var key = TestData.Key(ActivityKind.Agent, "agent:test");
        var running = IslandEvent.New(key, TestData.Payload("working") with { IsActive = true, Badge = "running" }, 1);
        var state = TestData.BuildState(_scheduler, running);
        Assert.True(state.Primary!.IsPersistent);

        var completed = IslandEvent.New(key, TestData.Payload("done") with { IsActive = false, Badge = "completed" }, 2);
        var update = _scheduler.Update(state, completed, TestData.Now.AddSeconds(1));
        Assert.False(update.State.Primary!.IsPersistent);
        Assert.NotNull(update.State.Primary.ExpiresAt);

        var expired = _scheduler.Tick(update.State, TestData.Now.AddSeconds(4.1));
        Assert.Empty(expired.State.Visible);
        Assert.Null(expired.State.Primary);
    }

    [Fact]
    public void IdenticalIncremental_ReportsNoContentChange()
    {
        var payload = TestData.Payload("same") with { ProgressPercent = 40 };
        var state = TestData.BuildState(_scheduler, IslandEvent.New(Key(ActivityKind.Progress), payload, 1));
        var patch = new IslandEvent(Key(ActivityKind.Progress), IslandEventKind.Incremental, 2, default,
            TestData.Payload("same") with { ProgressPercent = 41 });

        var update = _scheduler.Update(state, patch, TestData.Now);

        // 40 and 41 fall in the same 5-point bucket: no visible change.
        Assert.False(update.ContentChanged);
    }

    [Fact]
    public void EqualPriority_LaterReplacesFormer()
    {
        var first = IslandEvent.New(Key(ActivityKind.Notification, "notif:A"), TestData.Payload("a"), 1);
        var second = IslandEvent.New(Key(ActivityKind.Notification, "notif:B"), TestData.Payload("b"), 1);
        var state = TestData.BuildState(_scheduler, first);

        var update = _scheduler.Update(state, second, TestData.Now);

        Assert.Equal("notif:B", update.State.Primary!.Key.Source);
        Assert.Equal(1, update.State.ActivityCount);
        Assert.Equal(1, update.State.ReplacedCount);
    }

    [Fact]
    public void OldSequence_IsIgnored()
    {
        var state = TestData.BuildState(_scheduler, IslandEvent.New(Key(ActivityKind.Progress), TestData.Payload("p"), 5));
        var stale = new IslandEvent(Key(ActivityKind.Progress), IslandEventKind.Incremental, 4, default, TestData.Payload("old"));

        var update = _scheduler.Update(state, stale, TestData.Now);

        Assert.Equal(ActivityUpdateKind.None, update.Kind);
        Assert.Equal("p", update.State.Primary!.Payload.Title);
    }

    [Fact]
    public void Expiry_RemovesTransientActivity()
    {
        var state = TestData.BuildState(_scheduler, TestData.Notification());
        var later = TestData.Now.AddSeconds(6);

        var update = _scheduler.Tick(state, later);

        Assert.Equal(ActivityUpdateKind.Expired, update.Kind);
        Assert.Equal(1, update.RemovedCount);
        Assert.Empty(update.State.Visible);
        Assert.Equal(1, update.State.ExpiredCount);
    }

    [Fact]
    public void Expiry_DoesNotRemovePinnedActivity()
    {
        var pinned = IslandEvent.New(Key(ActivityKind.Notification), TestData.Payload("pinned").Pinned(), 1);
        var state = TestData.BuildState(_scheduler, pinned);

        var update = _scheduler.Tick(state, TestData.Now.AddMinutes(5));

        Assert.Equal(ActivityUpdateKind.None, update.Kind);
        Assert.Single(update.State.Visible);
    }

    [Fact]
    public void PolicyTtlOverride_IsApplied()
    {
        var policy = new ActivityStackPolicy
        {
            TtlOverrides = System.Collections.Immutable.ImmutableDictionary<ActivityKind, TimeSpan>.Empty
                .Add(ActivityKind.Notification, TimeSpan.FromMilliseconds(500)),
        };
        var scheduler = new ActivityScheduler(policy);
        var state = TestData.BuildState(scheduler, TestData.Notification());

        Assert.Equal(ActivityUpdateKind.Expired, scheduler.Tick(state, TestData.Now.AddSeconds(1)).Kind);
    }

    [Fact]
    public void Retract_RemovesActivityAndPromotesNext()
    {
        var state = TestData.BuildState(_scheduler, TestData.Notification("notif", "notification"));
        state = _scheduler.Update(state, TestData.Media(), TestData.Now).State;
        var retract = IslandEvent.Retract(Key(ActivityKind.Notification, "notif"), 2);

        var update = _scheduler.Update(state, retract, TestData.Now);

        Assert.Equal(ActivityUpdateKind.Retracted, update.Kind);
        Assert.Equal(ActivityKind.Media, update.State.PrimaryKind);
        Assert.Equal(1, update.State.ActivityCount);
    }

    [Fact]
    public void SetPinned_PinnedActivityStaysPrimary()
    {
        var state = TestData.BuildState(_scheduler, TestData.Media());
        var pinned = _scheduler.SetPinned(state, Key(ActivityKind.Media, "media:gsmtc"), true);
        Assert.Equal(ActivityUpdateKind.PinnedChanged, pinned.Kind);
        var clipboard = IslandEvent.New(Key(ActivityKind.Clipboard), TestData.Payload("copy"), 1);

        var update = _scheduler.Update(pinned.State, clipboard, TestData.Now);

        Assert.Equal(ActivityKind.Media, update.State.PrimaryKind);
        Assert.Equal(2, update.State.ActivityCount);
    }

    [Fact]
    public void Clear_RemovesAllActivities()
    {
        var state = TestData.BuildState(_scheduler, TestData.Notification(), TestData.Media("media:gsmtc", "t"));

        var update = ActivityScheduler.Clear(state);

        Assert.Equal(ActivityUpdateKind.Cleared, update.Kind);
        Assert.Equal(0, update.State.ActivityCount);
        Assert.Equal(2, update.RemovedCount);
    }

    [Fact]
    public void BackgroundQueue_IsBounded()
    {
        var policy = new ActivityStackPolicy { MaxVisible = 3, MaxBackground = 3 };
        var scheduler = new ActivityScheduler(policy);
        var state = ActivitySchedulerState.Empty;
        // Distinct priority ranks so no equal-rank replacement kicks in.
        var kinds = new[]
        {
            ActivityKind.Clipboard, ActivityKind.Notification, ActivityKind.Agent,
            ActivityKind.LockKeys, ActivityKind.Device, ActivityKind.Bluetooth,
            ActivityKind.Focus, ActivityKind.Timer,
        };
        for (var i = 0; i < kinds.Length; i++)
        {
            var evt = IslandEvent.New(Key(kinds[i], "src" + i), TestData.Payload("t" + i), 1);
            state = scheduler.Update(state, evt, TestData.Now).State;
        }

        Assert.InRange(state.Visible.Length, 1, 3);
        Assert.InRange(state.Background.Length, 0, 3);
        Assert.True(state.ActivityCount <= 6);
        Assert.True(state.DroppedCount >= 2);
    }

    [Fact]
    public void LatestOnly_KeepsSingleVisibleLayer()
    {
        var policy = new ActivityStackPolicy { Stacking = StackingStrategy.LatestOnly };
        var scheduler = new ActivityScheduler(policy);
        var state = TestData.BuildState(scheduler, TestData.Notification("notif", "notification"));
        state = scheduler.Update(state, TestData.Media(), TestData.Now).State;

        Assert.Single(state.Visible);
        Assert.Equal(1, state.BadgeCount);
        Assert.Equal(2, state.ActivityCount);
    }

    [Fact]
    public void Options_MediaPinningKeepsPreemptedMediaAlive()
    {
        var state = TestData.BuildState(_scheduler, TestData.Media());
        var lookup = IslandEvent.New(Key(ActivityKind.Lookup), TestData.Payload("search"), 1);

        var update = _scheduler.Update(state, lookup, TestData.Now);

        Assert.Equal(ActivityKind.Lookup, update.State.PrimaryKind);
        Assert.Contains(update.State.Visible, r => r.Kind == ActivityKind.Media);
    }

    [Fact]
    public void Diagnostics_CountersTrackSwitchesAndExpiry()
    {
        var counters = new DiagnosticCounters();
        var scheduler = new ActivityScheduler(counters: counters);
        var state = TestData.BuildState(scheduler, TestData.Notification());
        state = scheduler.Update(state, TestData.Media(), TestData.Now).State;
        scheduler.Tick(state, TestData.Now.AddSeconds(10));

        Assert.True(counters.Get(CounterKind.ActivitiesExpired) >= 1);
    }

    [Fact]
    public void Update_WithInvalidKey_IsIgnored()
    {
        var invalid = new IslandEvent(default, IslandEventKind.New, 1, default, TestData.Payload());
        var update = _scheduler.Update(ActivitySchedulerState.Empty, invalid, TestData.Now);
        Assert.Equal(ActivityUpdateKind.None, update.Kind);
    }

    [Fact]
    public void ContentSignature_DiffersOnTitleChange()
    {
        var payloadA = TestData.Payload("a");
        var payloadB = TestData.Payload("b");
        var key = Key(ActivityKind.Media);
        var a = ContentSignaturePolicy.Compute(key, ActivityKind.Media, payloadA);
        var b = ContentSignaturePolicy.Compute(key, ActivityKind.Media, payloadB);
        Assert.True(a.DiffersFrom(b));
        Assert.False(a.DiffersFrom(ContentSignaturePolicy.Compute(key, ActivityKind.Media, payloadA)));
    }

    [Fact]
    public void HeartbeatReplacement_PreservesTheSessionPin()
    {
        var initial = TestData.BuildState(_scheduler, TestData.Media());
        var key = initial.Primary!.Key;
        var pinned = _scheduler.SetPinned(initial, key, true).State;
        Assert.True(pinned.Primary!.IsPinned);

        var heartbeat = IslandEvent.New(key, TestData.Payload("heartbeat"), 2);
        var update = _scheduler.Update(pinned, heartbeat, TestData.Now.AddSeconds(2));

        Assert.Equal(ActivityUpdateKind.ReplacedInPlace, update.Kind);
        Assert.True(update.State.Primary!.IsPinned);
        Assert.Equal(key, update.State.Primary.Key);
    }
    [Fact]
    public void PlayingMedia_ReservesVisibleSlotAgainstThreeTransientActivities()
    {
        var state = TestData.BuildState(_scheduler,
            TestData.Media(),
            TestData.NewEvent(ActivityKind.Notification, "notif", "notice", 2),
            TestData.NewEvent(ActivityKind.Clipboard, "clip", "copy", 3),
            TestData.NewEvent(ActivityKind.LockKeys, "lock", "caps", 4));

        Assert.Contains(state.Visible, r => r.Kind == ActivityKind.Media);
        Assert.Equal(3, state.Visible.Length);
    }

    [Fact]
    public void PausedMedia_DoesNotReserveVisibleSlot()
    {
        var paused = IslandEvent.New(
            TestData.Key(ActivityKind.Media, "media:paused"),
            TestData.Payload("paused") with { IsActive = false }, 1);
        var state = TestData.BuildState(_scheduler,
            paused,
            TestData.NewEvent(ActivityKind.Notification, "notif", "notice", 2),
            TestData.NewEvent(ActivityKind.Clipboard, "clip", "copy", 3),
            TestData.NewEvent(ActivityKind.LockKeys, "lock", "caps", 4));

        Assert.DoesNotContain(state.Visible, r => r.Kind == ActivityKind.Media);
    }

    [Fact]
    public void PersistentOverflow_UsesPinnedThenRankThenArrival()
    {
        var pinnedTimer = IslandEvent.New(
            TestData.Key(ActivityKind.Timer, "timer"),
            TestData.Payload("timer").Pinned(), 1);
        var media = TestData.Media("media:one", "song", 2);
        var progress = TestData.NewEvent(ActivityKind.Progress, "progress", "export", 3);
        var focus = TestData.NewEvent(ActivityKind.Focus, "focus", "focus", 4);

        var state = TestData.BuildState(_scheduler, pinnedTimer, media, progress, focus);
        Assert.Equal(ActivityKind.Timer, state.Visible[0].Kind);
        Assert.True(state.Visible[0].IsPinned);
        Assert.Equal(3, state.Visible.Length);
    }
}
