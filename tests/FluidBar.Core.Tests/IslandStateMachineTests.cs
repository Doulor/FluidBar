using System.Collections.Immutable;
using FluidBar.Core.Activities;
using FluidBar.Core.Events;
using FluidBar.Core.Tests.TestSupport;

namespace FluidBar.Core.Tests;

public class IslandStateMachineTests
{
    private static readonly IslandBehaviorOptions Defaults = IslandBehaviorOptions.Default with
    {
        AutoCollapseMs = 2600,
        AutoHideMs = 0,
    };

    private static IslandInput FromUpdate(ActivitySchedulerUpdate update) => IslandInput.ForActivityUpdate(update);

    private static IslandInput NotificationInput(string source = "notif", long sequence = 1)
    {
        var scheduler = new ActivityScheduler();
        var update = scheduler.Update(ActivitySchedulerState.Empty, TestData.Notification(source), TestData.Now);
        return FromUpdate(update);
    }

    private static IslandTransition Reduce(IslandState state, IslandInput input, IslandBehaviorOptions? options = null, DateTimeOffset? now = null)
        => IslandStateMachine.Reduce(state, input, options ?? Defaults, now ?? TestData.Now);

    [Fact]
    public void Hidden_ActivityArrival_RevealsCompact()
    {
        var transition = Reduce(IslandState.Hidden, NotificationInput());

        Assert.Equal(IslandStateKind.Compact, transition.State.Kind);
        Assert.Equal(IslandTransitionReason.ActivityArrived, transition.Reason);
        Assert.True(transition.KindChanged);
        Assert.Contains(transition.Effects, e => e.Kind == IslandEffectKind.ShowWindow);
        Assert.Contains(transition.Effects, e => e.Kind == IslandEffectKind.PlayNudge);
        Assert.Contains(transition.Effects, e => e.Kind == IslandEffectKind.SyncClickThrough);
    }

    [Fact]
    public void SameKeyRefresh_DoesNotNudgeTheCompactShell()
    {
        var scheduler = new ActivityScheduler();
        var first = scheduler.Update(ActivitySchedulerState.Empty, TestData.Media(), TestData.Now);
        var replacement = IslandEvent.New(
            new ActivityKey(ActivityKind.Media, "media:gsmtc"),
            TestData.Payload("new track"),
            2);
        var refresh = scheduler.Update(first.State, replacement, TestData.Now.AddSeconds(1));
        Assert.Equal(ActivityUpdateKind.ReplacedInPlace, refresh.Kind);

        var compact = IslandState.Hidden with { Kind = IslandStateKind.Compact, ActivityCount = 1 };
        var transition = Reduce(compact, FromUpdate(refresh));

        // A running agent refreshes its payload every heart-beat, which surfaces as
        // ReplacedInPlace. Nudging that made the island jump once a second.
        Assert.DoesNotContain(transition.Effects, e => e.Kind == IslandEffectKind.PlayNudge);
    }

    [Fact]
    public void Hidden_SuppressedActivity_StaysHidden()
    {
        var suppressed = IslandState.Hidden with { IsSuppressed = true };
        var transition = Reduce(suppressed, NotificationInput());

        Assert.Equal(IslandStateKind.Hidden, transition.State.Kind);
        Assert.Equal(IslandTransitionReason.ActivityQueued, transition.Reason);
        Assert.DoesNotContain(transition.Effects, e => e.Kind == IslandEffectKind.ShowWindow);
    }

    [Fact]
    public void Suppression_End_RevealsAndResumesProducers()
    {
        var suppressed = IslandState.Hidden with { IsSuppressed = true, ActivityCount = 1 };
        var transition = Reduce(suppressed, IslandInput.SuppressionChanged(false));

        Assert.Equal(IslandStateKind.Compact, transition.State.Kind);
        Assert.Equal(IslandTransitionReason.SuppressionEnded, transition.Reason);
        Assert.Contains(transition.Effects, e => e.Kind == IslandEffectKind.ResumeProducers);
        Assert.Contains(transition.Effects, e => e.Kind == IslandEffectKind.ShowWindow);
    }

    [Fact]
    public void Suppression_Start_HidesEverything()
    {
        var compact = IslandState.Hidden with { Kind = IslandStateKind.Compact, ActivityCount = 1 };
        var transition = Reduce(compact, IslandInput.SuppressionChanged(true));

        Assert.Equal(IslandStateKind.Hidden, transition.State.Kind);
        Assert.Equal(IslandTransitionReason.SuppressionStarted, transition.Reason);
        Assert.Contains(transition.Effects, e => e.Kind == IslandEffectKind.PauseProducers);
        Assert.Contains(transition.Effects, e => e.Kind == IslandEffectKind.HideWindow);
    }

    [Fact]
    public void Hidden_PointerEnter_RevealsThroughHoverStrip()
    {
        var transition = Reduce(IslandState.Hidden, IslandInput.PointerEntered());

        Assert.Equal(IslandStateKind.Compact, transition.State.Kind);
        Assert.Equal(IslandTransitionReason.HoverReveal, transition.Reason);
    }

    [Fact]
    public void Compact_PointerEnter_ArmsHoverIntent()
    {
        var compact = IslandState.Hidden with { Kind = IslandStateKind.Compact };
        var transition = Reduce(compact, IslandInput.PointerEntered());

        Assert.Equal(IslandStateKind.Compact, transition.State.Kind);
        Assert.Equal(IslandTransitionReason.HoverIntentArmed, transition.Reason);
        Assert.Equal(TestData.Now.AddMilliseconds(90), transition.State.HoverIntentDeadline);
    }

    [Fact]
    public void HoverIntent_BeforeDeadline_DoesNothing()
    {
        var compact = IslandState.Hidden with
        {
            Kind = IslandStateKind.Compact,
            IsHovering = true,
            HoverIntentDeadline = TestData.Now.AddMilliseconds(90),
        };

        var transition = Reduce(compact, IslandInput.HoverIntentElapsed());

        Assert.Equal(IslandStateKind.Compact, transition.State.Kind);
        Assert.Equal(IslandTransitionReason.NoChange, transition.Reason);
    }

    [Fact]
    public void HoverIntent_AfterDeadline_EntersPeek()
    {
        var compact = IslandState.Hidden with
        {
            Kind = IslandStateKind.Compact,
            IsHovering = true,
            HoverIntentDeadline = TestData.Now,
        };

        var transition = Reduce(compact, IslandInput.HoverIntentElapsed(), now: TestData.Now.AddMilliseconds(91));

        Assert.Equal(IslandStateKind.Peek, transition.State.Kind);
        Assert.Equal(IslandTransitionReason.HoverPeek, transition.Reason);
        Assert.Contains(transition.Effects, e => e.Kind == IslandEffectKind.Retarget && e.Data == IslandEffectData.Hover);
        Assert.NotNull(transition.State.PeekDwellDeadline);
    }

    [Fact]
    public void Peek_PointerLeft_ArmsLeaveDebounce()
    {
        var peek = IslandState.Hidden with { Kind = IslandStateKind.Peek, IsHovering = true };

        var transition = Reduce(peek, IslandInput.PointerLeft());

        Assert.Equal(IslandStateKind.Peek, transition.State.Kind);
        Assert.Equal(IslandTransitionReason.LeaveArmed, transition.Reason);
        Assert.Equal(TestData.Now.AddMilliseconds(120), transition.State.LeaveDeadline);
    }

    [Fact]
    public void Peek_LeaveDebounce_ReturnsCompact()
    {
        var peek = IslandState.Hidden with
        {
            Kind = IslandStateKind.Peek,
            IsHovering = false,
            LeaveDeadline = TestData.Now.AddMilliseconds(120),
        };

        var transition = Reduce(peek, IslandInput.LeaveDebounceElapsed(), now: TestData.Now.AddMilliseconds(121));

        Assert.Equal(IslandStateKind.Compact, transition.State.Kind);
        Assert.Equal(IslandTransitionReason.PeekExit, transition.Reason);
    }

    [Fact]
    public void PeekDwell_WhileHovering_DoesNotRetract()
    {
        var peek = IslandState.Hidden with
        {
            Kind = IslandStateKind.Peek,
            IsHovering = true,
            PeekDwellDeadline = TestData.Now,
        };

        var transition = Reduce(peek, IslandInput.PeekDwellElapsed(), now: TestData.Now.AddSeconds(5));

        Assert.Equal(IslandStateKind.Peek, transition.State.Kind);
    }

    [Fact]
    public void PeekDwell_AfterLeave_Retracts()
    {
        var peek = IslandState.Hidden with
        {
            Kind = IslandStateKind.Peek,
            IsHovering = false,
            PeekDwellDeadline = TestData.Now,
        };

        var transition = Reduce(peek, IslandInput.PeekDwellElapsed(), now: TestData.Now.AddSeconds(5));

        Assert.Equal(IslandStateKind.Compact, transition.State.Kind);
        Assert.Equal(IslandTransitionReason.PeekDwellElapsed, transition.Reason);
    }

    [Fact]
    public void PrimaryClick_ExpandsWithIndependentWidthAndHeightTargets()
    {
        var compact = IslandState.Hidden with { Kind = IslandStateKind.Compact, ActivityCount = 1, ContentKind = ActivityKind.Notification };
        var transition = Reduce(compact, IslandInput.PrimaryClick());

        Assert.Equal(IslandStateKind.Expanded, transition.State.Kind);
        Assert.Equal(IslandTransitionReason.PrimaryClick, transition.Reason);
        Assert.Contains(transition.Effects, e => e.Kind == IslandEffectKind.Retarget && e.Data == IslandEffectData.Expand);
        Assert.Contains(transition.Effects, e => e.Kind == IslandEffectKind.Retarget && e.Data == IslandEffectData.ExpandHeight);
        Assert.NotNull(transition.State.AutoCollapseDeadline);
    }

    [Fact]
    public void PrimaryClick_WhenExpandOnClickDisabled_DoesNothing()
    {
        var compact = IslandState.Hidden with { Kind = IslandStateKind.Compact };
        var options = Defaults with { ExpandOnClick = false };
        var transition = Reduce(compact, IslandInput.PrimaryClick(), options);

        Assert.Equal(IslandStateKind.Compact, transition.State.Kind);
        Assert.Equal(IslandTransitionReason.NoChange, transition.Reason);
    }

    [Fact]
    public void Escape_Expanded_StartsClosingToCompact()
    {
        var expanded = IslandState.Hidden with { Kind = IslandStateKind.Expanded, ActivityCount = 1 };
        var transition = Reduce(expanded, IslandInput.Escape());

        Assert.Equal(IslandStateKind.Closing, transition.State.Kind);
        Assert.Equal(IslandStateKind.Compact, transition.State.ClosingReturnKind);
        Assert.Equal(IslandTransitionReason.Escape, transition.Reason);
        Assert.Contains(transition.Effects, e => e.Kind == IslandEffectKind.Retarget && e.Data == IslandEffectData.Collapse);
    }

    [Fact]
    public void Closing_DefersOtherInputs()
    {
        var closing = IslandState.Hidden with { Kind = IslandStateKind.Closing, ActivityCount = 1 };
        var transition = Reduce(closing, IslandInput.Escape());

        Assert.Equal(IslandTransitionReason.InputDeferred, transition.Reason);
        Assert.Equal(IslandStateKind.Closing, transition.State.Kind);
        Assert.Single(transition.State.DeferredInputs);
    }

    [Fact]
    public void Closing_PrimaryClick_InterruptsToExpanded()
    {
        var closing = IslandState.Hidden with { Kind = IslandStateKind.Closing, ActivityCount = 1 };
        var transition = Reduce(closing, IslandInput.PrimaryClick());

        // T12: clicking while collapsing re-targets the springs instead of deferring.
        Assert.Equal(IslandTransitionReason.InterruptExpand, transition.Reason);
        Assert.Equal(IslandStateKind.Expanded, transition.State.Kind);
        Assert.Contains(transition.Effects, e => e.Kind == IslandEffectKind.Retarget && e.Data == IslandEffectData.Expand);
    }

    [Fact]
    public void CloseSettled_ReturnsCompactAndReplaysDeferredInputs()
    {
        var closing = IslandState.Hidden with
        {
            Kind = IslandStateKind.Closing,
            ClosingReturnKind = IslandStateKind.Compact,
            ActivityCount = 1,
            DeferredInputs = [NotificationInput("deferred")],
        };

        var transition = Reduce(closing, IslandInput.CloseSettled());

        Assert.Equal(IslandStateKind.Compact, transition.State.Kind);
        Assert.Equal(IslandTransitionReason.CloseSettled, transition.Reason);
        Assert.Empty(transition.State.DeferredInputs);
        Assert.Contains(transition.Effects, e => e.Kind == IslandEffectKind.UpdateContent);
    }

    [Fact]
    public void CloseSettled_ToHidden_Hides()
    {
        var closing = IslandState.Hidden with
        {
            Kind = IslandStateKind.Closing,
            ClosingReturnKind = IslandStateKind.Hidden,
        };

        var transition = Reduce(closing, IslandInput.CloseSettled());

        Assert.Equal(IslandStateKind.Hidden, transition.State.Kind);
        Assert.Contains(transition.Effects, e => e.Kind == IslandEffectKind.HideWindow);
    }

    [Fact]
    public void ToggleHotkey_CyclesStates()
    {
        var expanded = Reduce(IslandState.Hidden, IslandInput.ToggleHotkey());
        Assert.Equal(IslandStateKind.Expanded, expanded.State.Kind);

        var closing = Reduce(expanded.State, IslandInput.ToggleHotkey());
        Assert.Equal(IslandStateKind.Closing, closing.State.Kind);
    }

    [Fact]
    public void HideHotkey_FromExpanded_ClosesToHidden()
    {
        var expanded = IslandState.Hidden with { Kind = IslandStateKind.Expanded };
        var transition = Reduce(expanded, IslandInput.HideHotkey());

        Assert.Equal(IslandStateKind.Closing, transition.State.Kind);
        Assert.Equal(IslandStateKind.Hidden, transition.State.ClosingReturnKind);
    }

    [Fact]
    public void HideHotkey_FromCompact_HidesImmediately()
    {
        var compact = IslandState.Hidden with { Kind = IslandStateKind.Compact };
        var transition = Reduce(compact, IslandInput.HideHotkey());

        Assert.Equal(IslandStateKind.Hidden, transition.State.Kind);
        Assert.Contains(transition.Effects, e => e.Kind == IslandEffectKind.StopFrameLoop);
    }

    [Fact]
    public void AutoCollapse_RespectsPin()
    {
        var expanded = IslandState.Hidden with
        {
            Kind = IslandStateKind.Expanded,
            IsPinned = true,
            AutoCollapseDeadline = TestData.Now,
        };

        var blocked = Reduce(expanded, IslandInput.AutoCollapseElapsed(), now: TestData.Now.AddSeconds(1));
        Assert.Equal(IslandStateKind.Expanded, blocked.State.Kind);
        Assert.Equal(IslandTransitionReason.PinnedBlocked, blocked.Reason);

        var unpinned = expanded with { IsPinned = false };
        var closing = Reduce(unpinned, IslandInput.AutoCollapseElapsed(), now: TestData.Now.AddSeconds(1));
        Assert.Equal(IslandStateKind.Closing, closing.State.Kind);
        Assert.Equal(IslandTransitionReason.AutoCollapse, closing.Reason);
    }

    [Fact]
    public void OutsideClick_RespectsPin()
    {
        var expanded = IslandState.Hidden with { Kind = IslandStateKind.Expanded, IsPinned = true };
        Assert.Equal(IslandTransitionReason.PinnedBlocked, Reduce(expanded, IslandInput.OutsideClick()).Reason);

        var unpinned = expanded with { IsPinned = false };
        Assert.Equal(IslandStateKind.Closing, Reduce(unpinned, IslandInput.OutsideClick()).State.Kind);
    }

    [Fact]
    public void AutoHide_AfterDelay_GoesHidden()
    {
        var options = Defaults with { AutoHideMs = 100 };
        var revealed = Reduce(IslandState.Hidden, NotificationInput(), options);
        Assert.Equal(IslandStateKind.Compact, revealed.State.Kind);
        Assert.NotNull(revealed.State.AutoHideDeadline);

        var transition = Reduce(revealed.State, IslandInput.AutoHideElapsed(), options);
        Assert.Equal(IslandStateKind.Hidden, transition.State.Kind);
        Assert.Equal(IslandTransitionReason.AutoHide, transition.Reason);
    }

    [Fact]
    public void AutoHide_Disabled_KeepsPill()
    {
        var compact = IslandState.Hidden with { Kind = IslandStateKind.Compact };
        var transition = Reduce(compact, IslandInput.AutoHideElapsed(), Defaults with { AutoHideMs = 0 });
        Assert.Equal(IslandStateKind.Compact, transition.State.Kind);
    }

    [Fact]
    public void Expanded_ActivityUpdate_DoesNotMoveShell()
    {
        var expanded = IslandState.Hidden with
        {
            Kind = IslandStateKind.Expanded,
            Page = PageRef.Home,
            ActivityCount = 1,
            ContentKind = ActivityKind.Notification,
        };

        var transition = Reduce(expanded, NotificationInput("new"));

        Assert.Equal(IslandStateKind.Expanded, transition.State.Kind);
        Assert.Equal(IslandTransitionReason.ContentUpdated, transition.Reason);
        Assert.Contains(transition.Effects, e => e.Kind == IslandEffectKind.UpdateContent);
        Assert.DoesNotContain(transition.Effects, e => e.Kind == IslandEffectKind.Relayout);
        Assert.DoesNotContain(transition.Effects, e => e.Kind == IslandEffectKind.Retarget);
    }

    [Fact]
    public void Expanded_LastActivityEnds_Collapses()
    {
        var scheduler = new ActivityScheduler();
        var added = scheduler.Update(ActivitySchedulerState.Empty, TestData.Notification(), TestData.Now);
        var expanded = IslandState.Hidden with
        {
            Kind = IslandStateKind.Expanded,
            ActivityCount = added.State.ActivityCount,
            ContentKind = ActivityKind.Notification,
        };
        var cleared = ActivityScheduler.Clear(added.State);
        var transition = Reduce(expanded, FromUpdate(cleared));

        Assert.Equal(IslandStateKind.Closing, transition.State.Kind);
        Assert.Equal(IslandTransitionReason.ActivityEnded, transition.Reason);

        // The ended activity must not survive the close: a stale ContentKind made the next compact
        // pill keep the activity's width while showing idle content.
        Assert.Null(transition.State.ContentKind);
        Assert.Equal(0, transition.State.ActivityCount);

        var settled = Reduce(transition.State, IslandInput.CloseSettled());
        Assert.Equal(IslandStateKind.Compact, settled.State.Kind);
        Assert.Null(settled.State.ContentKind);
    }

    [Fact]
    public void Expanded_LastActivityEnds_WhileHovering_KeepsThePanelOpen()
    {
        var scheduler = new ActivityScheduler();
        var added = scheduler.Update(ActivitySchedulerState.Empty, TestData.Notification(), TestData.Now);
        var expanded = IslandState.Hidden with
        {
            Kind = IslandStateKind.Expanded,
            ActivityCount = added.State.ActivityCount,
            ContentKind = ActivityKind.Notification,
            IsHovering = true,
        };

        var transition = Reduce(expanded, FromUpdate(ActivityScheduler.Clear(added.State)));

        Assert.Equal(IslandStateKind.Expanded, transition.State.Kind);
        Assert.Equal(IslandTransitionReason.ContentUpdated, transition.Reason);
        Assert.Null(transition.State.ContentKind);
    }

    [Fact]
    public void Expanded_PointerEntered_SuspendsAutoCollapse()
    {
        var expanded = IslandState.Hidden with
        {
            Kind = IslandStateKind.Expanded,
            ActivityCount = 1,
            AutoCollapseDeadline = TestData.Now.AddSeconds(1),
        };

        var transition = Reduce(expanded, IslandInput.PointerEntered());

        Assert.Equal(IslandStateKind.Expanded, transition.State.Kind);
        Assert.True(transition.State.IsHovering);
        Assert.Null(transition.State.AutoCollapseDeadline);
    }

    [Fact]
    public void Expanded_PointerLeft_ReArmsAutoCollapse()
    {
        var expanded = IslandState.Hidden with
        {
            Kind = IslandStateKind.Expanded,
            ActivityCount = 1,
            IsHovering = true,
            AutoCollapseDeadline = null,
        };

        var transition = Reduce(expanded, IslandInput.PointerLeft());

        Assert.Equal(IslandStateKind.Expanded, transition.State.Kind);
        Assert.False(transition.State.IsHovering);
        Assert.Equal(TestData.Now.AddMilliseconds(Defaults.AutoCollapseMs), transition.State.AutoCollapseDeadline);
    }

    [Fact]
    public void PrimaryClick_FromHoveredCompact_DoesNotArmAutoCollapse()
    {
        var compact = IslandState.Hidden with
        {
            Kind = IslandStateKind.Compact,
            ActivityCount = 1,
            ContentKind = ActivityKind.Clipboard,
            IsHovering = true,
        };

        var transition = Reduce(compact, IslandInput.PrimaryClick());

        Assert.Equal(IslandStateKind.Expanded, transition.State.Kind);
        Assert.Null(transition.State.AutoCollapseDeadline);
    }

    [Fact]
    public void PageRequested_OnlyInExpandedAndContentOnly()
    {
        var compact = IslandState.Hidden with { Kind = IslandStateKind.Compact };
        Assert.Equal(IslandTransitionReason.NoChange, Reduce(compact, IslandInput.PageRequested(PageRef.Media)).Reason);

        var expanded = compact with { Kind = IslandStateKind.Expanded };
        var transition = Reduce(expanded, IslandInput.PageRequested(PageRef.Media));
        Assert.Equal(PageRef.Media, transition.State.Page);
        Assert.Equal(IslandTransitionReason.PageRequested, transition.Reason);
        Assert.Contains(transition.Effects, e => e.Kind == IslandEffectKind.UpdateContent);
        Assert.DoesNotContain(transition.Effects, e => e.Kind == IslandEffectKind.Relayout);
    }

    [Fact]
    public void PinnedChanged_WhilePeek_ReturnsCompact()
    {
        var peek = IslandState.Hidden with { Kind = IslandStateKind.Peek, IsHovering = true };
        var transition = Reduce(peek, IslandInput.PinnedChanged(true));

        Assert.Equal(IslandStateKind.Compact, transition.State.Kind);
        Assert.True(transition.State.IsPinned);
    }

    [Fact]
    public void EnvironmentChange_SnapsInPlace()
    {
        var expanded = IslandState.Hidden with { Kind = IslandStateKind.Expanded };
        var transition = Reduce(expanded, IslandInput.DisplayChanged());

        Assert.Equal(IslandStateKind.Expanded, transition.State.Kind);
        Assert.Equal(IslandTransitionReason.EnvironmentChanged, transition.Reason);
        Assert.Contains(transition.Effects, e => e.Kind == IslandEffectKind.SnapSprings);
        Assert.Contains(transition.Effects, e => e.Kind == IslandEffectKind.PersistPlacement);
    }

    [Fact]
    public void DisabledIsland_HidesEverything()
    {
        var compact = IslandState.Hidden with { Kind = IslandStateKind.Compact, ActivityCount = 2 };
        var transition = Reduce(compact, IslandInput.SettingsChanged(), Defaults with { Enabled = false });

        Assert.Equal(IslandStateKind.Hidden, transition.State.Kind);
        Assert.Contains(transition.Effects, e => e.Kind == IslandEffectKind.HideWindow);
    }

    [Fact]
    public void Drag_StartAndEnd_ToggleFlagAndPersist()
    {
        var compact = IslandState.Hidden with { Kind = IslandStateKind.Compact };
        var started = Reduce(compact, IslandInput.DragStarted());
        Assert.True(started.State.IsDragging);

        var ended = Reduce(started.State, IslandInput.DragEnded());
        Assert.False(ended.State.IsDragging);
        Assert.Contains(ended.Effects, e => e.Kind == IslandEffectKind.PersistPlacement);
    }

    [Fact]
    public void ContentUpdate_ToIdle_KeepsCompactSurface()
    {
        var expiring = IslandState.Hidden with
        {
            Kind = IslandStateKind.Compact,
            ActivityCount = 1,
            ContentKind = ActivityKind.Notification,
        };
        var scheduler = new ActivityScheduler();
        var update = scheduler.Update(ActivitySchedulerState.Empty, TestData.Notification(), TestData.Now);
        var clearedState = ActivityScheduler.Clear(update.State);
        var cleared = FromUpdate(clearedState);

        var transition = Reduce(expiring, cleared);

        Assert.Equal(IslandStateKind.Compact, transition.State.Kind);
        Assert.Null(transition.State.ContentKind);
        Assert.Equal(0, transition.State.ActivityCount);
    }

    [Fact]
    public void Controller_RaisesTransitionsAndIgnoresNoOps()
    {
        using var controller = new IslandStateController(Defaults);
        var transitions = new List<IslandTransition>();
        controller.Transitioned += transitions.Add;

        controller.Apply(NotificationInput(), TestData.Now);
        controller.Apply(IslandInput.PointerLeft(), TestData.Now);

        Assert.Single(transitions);
        Assert.Equal(IslandStateKind.Compact, controller.Current.Kind);
    }

    [Fact]
    public void Controller_ResetReplacesState()
    {
        using var controller = new IslandStateController(Defaults);
        var expanded = IslandState.Hidden with { Kind = IslandStateKind.Expanded };
        controller.Reset(expanded);
        Assert.Equal(IslandStateKind.Expanded, controller.Current.Kind);
    }

    [Fact]
    public void PriorityOrder_MatchesSpecContract()
    {
        var order = new[]
        {
            ActivityKind.Lookup, ActivityKind.Clipboard, ActivityKind.LockKeys, ActivityKind.Device,
            ActivityKind.Bluetooth, ActivityKind.Focus, ActivityKind.Volume, ActivityKind.Brightness,
            ActivityKind.Notification, ActivityKind.Agent, ActivityKind.BatteryLow, ActivityKind.Progress,
            ActivityKind.Timer, ActivityKind.Media, ActivityKind.Idle,
        };

        for (var i = 1; i < order.Length; i++)
        {
            Assert.True(ActivityPriorityPolicy.Rank(order[i - 1]) < ActivityPriorityPolicy.Rank(order[i]),
                $"{order[i - 1]} must outrank {order[i]}");
            Assert.True(ActivityPriorityPolicy.Rank(order[i]) <= ActivityPriorityPolicy.IdleRank);
        }
    }

    [Fact]
    public void PersistentKinds_MatchSpec()
    {
        Assert.True(ActivityPriorityPolicy.IsPersistent(ActivityKind.Media));
        Assert.True(ActivityPriorityPolicy.IsPersistent(ActivityKind.Timer));
        Assert.True(ActivityPriorityPolicy.IsPersistent(ActivityKind.Progress));
        Assert.True(ActivityPriorityPolicy.IsPersistent(ActivityKind.BatteryLow));
        Assert.True(ActivityPriorityPolicy.IsPersistent(ActivityKind.Focus));
        Assert.False(ActivityPriorityPolicy.IsPersistent(ActivityKind.Notification));
        Assert.False(ActivityPriorityPolicy.IsPersistent(ActivityKind.Clipboard));
    }

    [Fact]
    public void DefaultTtls_MatchSpec()
    {
        Assert.Equal(TimeSpan.FromSeconds(4), ActivityPriorityPolicy.DefaultTtl(ActivityKind.Clipboard));
        Assert.Equal(TimeSpan.FromSeconds(5), ActivityPriorityPolicy.DefaultTtl(ActivityKind.Notification));
        Assert.Equal(TimeSpan.FromSeconds(1.6), ActivityPriorityPolicy.DefaultTtl(ActivityKind.Volume));
        Assert.Equal(TimeSpan.FromSeconds(3), ActivityPriorityPolicy.DefaultTtl(ActivityKind.Bluetooth));
        Assert.Null(ActivityPriorityPolicy.DefaultTtl(ActivityKind.Media));
        Assert.Null(ActivityPriorityPolicy.DefaultTtl(ActivityKind.Lookup));
    }
}

