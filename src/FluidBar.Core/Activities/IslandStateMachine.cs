using System.Collections.Immutable;

namespace FluidBar.Core.Activities;

/// <summary>
/// Pure island reducer implementing the transition table of spec 2.1:
/// <c>Reduce(state, input, options, now) -&gt; (state, effects[])</c>.
/// No UI, no timers, no I/O; deadlines are returned in the state so the host can schedule them.
/// </summary>
public static class IslandStateMachine
{
    /// <summary>Applies one input to <paramref name="state"/>.</summary>
    /// <param name="state">Current immutable state.</param>
    /// <param name="input">Input envelope.</param>
    /// <param name="options">Behaviour switches (from settings).</param>
    /// <param name="now">Current time; the host uses the injected <see cref="TimeProvider"/>.</param>
    public static IslandTransition Reduce(
        IslandState state,
        IslandInput input,
        IslandBehaviorOptions options,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Enabled)
        {
            return state.Kind == IslandStateKind.Hidden
                ? IslandTransition.None(state, IslandTransitionReason.SettingsChanged)
                : EnterHidden(state, IslandTransitionReason.SettingsChanged,
                    IslandEffectKind.HideWindow, IslandEffectKind.StopFrameLoop, IslandEffectKind.SyncClickThrough,
                    IslandEffectKind.UpdateTray);
        }

        return input.Kind switch
        {
            IslandInputKind.ActivityUpdate => ApplyActivityUpdate(state, input, options, now),
            IslandInputKind.PointerEntered => ApplyPointerEntered(state, options, now),
            IslandInputKind.PointerLeft => ApplyPointerLeft(state, options, now),
            IslandInputKind.HoverIntentElapsed => ApplyHoverIntent(state, options, now),
            IslandInputKind.LeaveDebounceElapsed => ApplyLeaveDebounce(state, options, now),
            IslandInputKind.PeekDwellElapsed => ApplyPeekDwell(state, options, now),
            IslandInputKind.PrimaryClick => ApplyPrimaryClick(state, options, now),
            IslandInputKind.OutsideClick => ApplyOutsideClick(state, options, now),
            IslandInputKind.Escape => ApplyEscape(state, options, now),
            IslandInputKind.ToggleHotkey => ApplyToggleHotkey(state, options, now),
            IslandInputKind.HideHotkey => ApplyHideHotkey(state, options, now),
            IslandInputKind.ShowHotkey => ApplyShowHotkey(state, options, now),
            IslandInputKind.AutoCollapseElapsed => ApplyAutoCollapse(state, options, now),
            IslandInputKind.AutoHideElapsed => ApplyAutoHide(state, options),
            IslandInputKind.CloseSettled => ApplyCloseSettled(state, options, now),
            IslandInputKind.SuppressionChanged => ApplySuppressionChanged(state, input, options, now),
            IslandInputKind.DisplayChanged => ApplyEnvironmentChanged(state, options, now, IslandTransitionReason.EnvironmentChanged),
            IslandInputKind.ModeChanged => ApplyEnvironmentChanged(state, options, now, IslandTransitionReason.EnvironmentChanged),
            IslandInputKind.SettingsChanged => ApplyEnvironmentChanged(state, options, now, IslandTransitionReason.SettingsChanged),
            IslandInputKind.PageRequested => ApplyPageRequested(state, input),
            IslandInputKind.PinnedChanged => ApplyPinnedChanged(state, input, options, now),
            IslandInputKind.DragStarted => ApplyDragStarted(state, options),
            IslandInputKind.DragEnded => ApplyDragEnded(state),
            _ => IslandTransition.None(state),
        };
    }

    private static IslandTransition ApplyActivityUpdate(
        IslandState state,
        in IslandInput input,
        IslandBehaviorOptions options,
        DateTimeOffset now)
    {
        var hasActivities = input.ActivityCount > 0;
        var contentKind = hasActivities ? input.ActivityKind ?? state.ContentKind : null;
        var page = hasActivities
            ? input.Page ?? contentKind?.ToPage() ?? state.Page
            : state.Page;
        var baseState = state with
        {
            PrimaryKey = hasActivities ? input.Key : null,
            ContentKind = contentKind,
            ActivityCount = input.ActivityCount,
            BadgeCount = input.BadgeCount,
            IsPinned = hasActivities && input.IsPinned,
            Page = state.Kind == IslandStateKind.Expanded ? state.Page : page,
        };

        switch (state.Kind)
        {
            case IslandStateKind.Hidden:
            {
                if (hasActivities
                    && contentKind is { } kind
                    && CollapsePolicy.ShouldRevealOnActivity(state, options, kind))
                {
                    var revealed = EnterCompact(baseState, options, now) with
                    {
                        HoverIntentDeadline = null,
                        LeaveDeadline = null,
                        PeekDwellDeadline = null,
                    };
                    return Transition(state, revealed, IslandTransitionReason.ActivityArrived,
                        Effect(IslandEffectKind.ShowWindow),
                        Effect(IslandEffectKind.Relayout),
                        Effect(IslandEffectKind.SyncClickThrough),
                        Effect(IslandEffectKind.UpdateTray),
                        Effect(IslandEffectKind.PlayNudge),
                        Effect(IslandEffectKind.StartFrameLoop));
                }

                return Transition(state, baseState with { Kind = IslandStateKind.Hidden },
                    IslandTransitionReason.ActivityQueued,
                    Effect(IslandEffectKind.UpdateTray));
            }

            case IslandStateKind.Compact:
            {
                var next = baseState with
                {
                    AutoHideDeadline = options.AutoHideMs > 0 ? now.AddMilliseconds(options.AutoHideMs) : null,
                };
                var effects = new List<IslandEffect> { Effect(IslandEffectKind.UpdateContent), Effect(IslandEffectKind.UpdateTray) };
                // Only a genuinely new/preempting activity nudges. Same-key content refreshes use
                // ReplacedInPlace; nudging those made a running agent bump the shell every heartbeat.
                if (input.UpdateKind is ActivityUpdateKind.Added or ActivityUpdateKind.Preempted)
                {
                    effects.Add(Effect(IslandEffectKind.PlayNudge));
                    effects.Add(Effect(IslandEffectKind.StartFrameLoop));
                }

                return Transition(state, next, IslandTransitionReason.ActivityUpdated, [.. effects]);
            }

            case IslandStateKind.Peek:
            {
                var effects = new List<IslandEffect> { Effect(IslandEffectKind.UpdateContent), Effect(IslandEffectKind.UpdateTray) };
                if (input.UpdateKind is ActivityUpdateKind.Added or ActivityUpdateKind.Preempted)
                {
                    effects.Add(Effect(IslandEffectKind.PlayNudge));
                    effects.Add(Effect(IslandEffectKind.StartFrameLoop));
                }

                return Transition(state, baseState with { AutoHideDeadline = state.AutoHideDeadline },
                    IslandTransitionReason.ActivityUpdated, [.. effects]);
            }

            case IslandStateKind.Expanded:
            {
                if (!hasActivities)
                {
                    if (state.IsHovering)
                    {
                        // The pointer is on the open panel: never yank it away because a transient
                        // activity expired. Content updates in place; leaving re-arms auto-collapse.
                        return Transition(state, baseState with { Page = state.Page },
                            IslandTransitionReason.ContentUpdated,
                            Effect(IslandEffectKind.UpdateContent),
                            Effect(IslandEffectKind.UpdateTray));
                    }

                    // Close from the cleared base state: otherwise ContentKind/ActivityCount survive the
                    // close and the next compact pill keeps the ended activity's width with idle content.
                    return StartClosing(baseState, options, now, IslandStateKind.Compact,
                        IslandTransitionReason.ActivityEnded,
                        Effect(IslandEffectKind.Retarget, IslandEffectData.Collapse),
                        Effect(IslandEffectKind.UpdateContent),
                        Effect(IslandEffectKind.UpdateTray),
                        Effect(IslandEffectKind.StartFrameLoop));
                }

                var effects = new List<IslandEffect> { Effect(IslandEffectKind.UpdateContent), Effect(IslandEffectKind.UpdateTray) };
                if (input.UpdateKind is ActivityUpdateKind.Added or ActivityUpdateKind.Preempted)
                {
                    effects.Add(Effect(IslandEffectKind.PlayNudge));
                    effects.Add(Effect(IslandEffectKind.StartFrameLoop));
                }

                // T11: an expanded shell never moves; only the content path runs.
                return Transition(state, baseState with { Page = state.Page },
                    IslandTransitionReason.ContentUpdated, [.. effects]);
            }

            case IslandStateKind.Closing:
                return Defer(state, input, options);

            default:
                return IslandTransition.None(state);
        }
    }

    private static IslandTransition ApplyPointerEntered(IslandState state, IslandBehaviorOptions options, DateTimeOffset now)
    {
        switch (state.Kind)
        {
            case IslandStateKind.Hidden:
                if (options.HoverReveal && !state.IsSuppressed)
                {
                    var revealed = EnterCompact(state, options, now) with { IsHovering = true };
                    return Transition(state, revealed, IslandTransitionReason.HoverReveal,
                        Effect(IslandEffectKind.ShowWindow),
                        Effect(IslandEffectKind.Relayout),
                        Effect(IslandEffectKind.SyncClickThrough),
                        Effect(IslandEffectKind.UpdateTray),
                        Effect(IslandEffectKind.StartFrameLoop));
                }

                return IslandTransition.None(state);

            case IslandStateKind.Compact:
            {
                var next = state with { IsHovering = true };
                if (options.HoverPeek && !state.IsPinned && !state.IsDragging)
                {
                    next = next with { HoverIntentDeadline = now.AddMilliseconds(options.HoverIntentMs) };
                    return Transition(state, next, IslandTransitionReason.HoverIntentArmed);
                }

                return Transition(state, next, IslandTransitionReason.HoverIntentArmed);
            }

            case IslandStateKind.Peek:
                return Transition(state, state with { IsHovering = true, LeaveDeadline = null }, IslandTransitionReason.HoverIntentArmed);

            case IslandStateKind.Expanded:
                // Hovering the open panel suspends auto-collapse; leaving re-arms it (see PointerLeft).
                return Transition(state, state with { IsHovering = true, AutoCollapseDeadline = null },
                    IslandTransitionReason.NoChange);

            case IslandStateKind.Closing:
                return Defer(state, IslandInput.PointerEntered(), options);

            default:
                return IslandTransition.None(state);
        }
    }

    private static IslandTransition ApplyPointerLeft(IslandState state, IslandBehaviorOptions options, DateTimeOffset now)
    {
        switch (state.Kind)
        {
            case IslandStateKind.Compact:
                return Transition(state, state with { IsHovering = false, HoverIntentDeadline = null },
                    IslandTransitionReason.NoChange);

            case IslandStateKind.Peek:
                return Transition(state, state with
                {
                    IsHovering = false,
                    LeaveDeadline = now.AddMilliseconds(options.LeaveDebounceMs),
                }, IslandTransitionReason.LeaveArmed);

            case IslandStateKind.Expanded:
                return Transition(state, state with
                {
                    IsHovering = false,
                    AutoCollapseDeadline = options.AutoCollapseMs > 0
                        ? now.AddMilliseconds(options.AutoCollapseMs)
                        : null,
                }, IslandTransitionReason.LeaveArmed);

            case IslandStateKind.Closing:
                return Defer(state, IslandInput.PointerLeft(), options);

            default:
                return IslandTransition.None(state);
        }
    }

    private static IslandTransition ApplyHoverIntent(IslandState state, IslandBehaviorOptions options, DateTimeOffset now)
    {
        if (!PeekPolicy.HoverIntentElapsed(state, options, now))
        {
            return IslandTransition.None(state);
        }

        var peek = state with
        {
            Kind = IslandStateKind.Peek,
            HoverIntentDeadline = null,
            LeaveDeadline = null,
            PeekDwellDeadline = now.AddMilliseconds(options.PeekDwellMs),
            AutoHideDeadline = null,
        };
        return Transition(state, peek, IslandTransitionReason.HoverPeek,
            Effect(IslandEffectKind.Retarget, IslandEffectData.Hover),
            Effect(IslandEffectKind.Relayout),
            Effect(IslandEffectKind.StartFrameLoop));
    }

    private static IslandTransition ApplyLeaveDebounce(IslandState state, IslandBehaviorOptions options, DateTimeOffset now)
    {
        if (!PeekPolicy.ShouldExitPeek(state, now))
        {
            return IslandTransition.None(state);
        }

        var compact = EnterCompact(state with { IsHovering = false }, options, now);
        return Transition(state, compact, IslandTransitionReason.PeekExit,
            Effect(IslandEffectKind.Retarget, IslandEffectData.Hover),
            Effect(IslandEffectKind.Relayout),
            Effect(IslandEffectKind.StartFrameLoop));
    }

    private static IslandTransition ApplyPeekDwell(IslandState state, IslandBehaviorOptions options, DateTimeOffset now)
    {
        if (!PeekPolicy.ShouldAutoRetractPeek(state, now))
        {
            return IslandTransition.None(state);
        }

        var compact = EnterCompact(state, options, now);
        return Transition(state, compact, IslandTransitionReason.PeekDwellElapsed,
            Effect(IslandEffectKind.Retarget, IslandEffectData.Hover),
            Effect(IslandEffectKind.Relayout),
            Effect(IslandEffectKind.StartFrameLoop));
    }

    private static IslandTransition ApplyPrimaryClick(IslandState state, IslandBehaviorOptions options, DateTimeOffset now)
    {
        switch (state.Kind)
        {
            case IslandStateKind.Compact:
            case IslandStateKind.Peek:
            {
                if (!options.ExpandOnClick)
                {
                    return IslandTransition.None(state);
                }

                var expanded = EnterExpanded(state, options, now);
                return Transition(state, expanded, IslandTransitionReason.PrimaryClick,
                    Effect(IslandEffectKind.Retarget, IslandEffectData.Expand),
                    Effect(IslandEffectKind.Retarget, IslandEffectData.ExpandHeight),
                    Effect(IslandEffectKind.Relayout),
                    Effect(IslandEffectKind.FocusRail),
                    Effect(IslandEffectKind.StartFrameLoop));
            }

            case IslandStateKind.Closing:
            {
                // T12: interrupting a collapse re-targets the springs, keeping velocity.
                var expanded = EnterExpanded(state with { ClosingReturnKind = IslandStateKind.Compact }, options, now);
                return Transition(state, expanded, IslandTransitionReason.InterruptExpand,
                    Effect(IslandEffectKind.Retarget, IslandEffectData.Expand),
                    Effect(IslandEffectKind.Retarget, IslandEffectData.ExpandHeight),
                    Effect(IslandEffectKind.Relayout),
                    Effect(IslandEffectKind.FocusRail),
                    Effect(IslandEffectKind.StartFrameLoop));
            }

            default:
                return IslandTransition.None(state);
        }
    }

    private static IslandTransition ApplyOutsideClick(IslandState state, IslandBehaviorOptions options, DateTimeOffset now)
    {
        if (state.Kind != IslandStateKind.Expanded)
        {
            return state.Kind == IslandStateKind.Closing ? Defer(state, IslandInput.OutsideClick(), options) : IslandTransition.None(state);
        }

        if (CollapsePolicy.BlocksOutsideClickClose(state))
        {
            return IslandTransition.None(state, IslandTransitionReason.PinnedBlocked);
        }

        return StartClosing(state, options, now, IslandStateKind.Compact, IslandTransitionReason.OutsideClick,
            Effect(IslandEffectKind.Retarget, IslandEffectData.Collapse),
            Effect(IslandEffectKind.StartFrameLoop));
    }

    private static IslandTransition ApplyEscape(IslandState state, IslandBehaviorOptions options, DateTimeOffset now)
    {
        switch (state.Kind)
        {
            case IslandStateKind.Expanded:
                return StartClosing(state, options, now, IslandStateKind.Compact, IslandTransitionReason.Escape,
                    Effect(IslandEffectKind.Retarget, IslandEffectData.Collapse),
                    Effect(IslandEffectKind.StartFrameLoop));

            case IslandStateKind.Peek:
            {
                var compact = EnterCompact(state, options, now);
                return Transition(state, compact, IslandTransitionReason.Escape,
                    Effect(IslandEffectKind.Retarget, IslandEffectData.Hover),
                    Effect(IslandEffectKind.Relayout),
                    Effect(IslandEffectKind.StartFrameLoop));
            }

            case IslandStateKind.Closing:
                return Defer(state, IslandInput.Escape(), options);

            default:
                return IslandTransition.None(state);
        }
    }

    private static IslandTransition ApplyToggleHotkey(IslandState state, IslandBehaviorOptions options, DateTimeOffset now)
    {
        switch (state.Kind)
        {
            case IslandStateKind.Hidden:
            {
                var expanded = EnterExpanded(state with { Page = state.ContentKind?.ToPage() ?? state.Page }, options, now);
                return Transition(state, expanded, IslandTransitionReason.HotkeyExpand,
                    Effect(IslandEffectKind.ShowWindow),
                    Effect(IslandEffectKind.Retarget, IslandEffectData.Expand),
                    Effect(IslandEffectKind.Retarget, IslandEffectData.ExpandHeight),
                    Effect(IslandEffectKind.Relayout),
                    Effect(IslandEffectKind.SyncClickThrough),
                    Effect(IslandEffectKind.FocusRail),
                    Effect(IslandEffectKind.StartFrameLoop));
            }

            case IslandStateKind.Compact:
            case IslandStateKind.Peek:
            {
                var expanded = EnterExpanded(state, options, now);
                return Transition(state, expanded, IslandTransitionReason.HotkeyExpand,
                    Effect(IslandEffectKind.Retarget, IslandEffectData.Expand),
                    Effect(IslandEffectKind.Retarget, IslandEffectData.ExpandHeight),
                    Effect(IslandEffectKind.Relayout),
                    Effect(IslandEffectKind.FocusRail),
                    Effect(IslandEffectKind.StartFrameLoop));
            }

            case IslandStateKind.Expanded:
                return StartClosing(state, options, now, IslandStateKind.Compact, IslandTransitionReason.HotkeyCollapse,
                    Effect(IslandEffectKind.Retarget, IslandEffectData.Collapse),
                    Effect(IslandEffectKind.StartFrameLoop));

            case IslandStateKind.Closing:
                return Defer(state, IslandInput.ToggleHotkey(), options);

            default:
                return IslandTransition.None(state);
        }
    }

    private static IslandTransition ApplyHideHotkey(IslandState state, IslandBehaviorOptions options, DateTimeOffset now)
    {
        switch (state.Kind)
        {
            case IslandStateKind.Expanded:
                return StartClosing(state, options, now, IslandStateKind.Hidden, IslandTransitionReason.HideRequested,
                    Effect(IslandEffectKind.Retarget, IslandEffectData.Collapse),
                    Effect(IslandEffectKind.StartFrameLoop));

            case IslandStateKind.Compact:
            case IslandStateKind.Peek:
                return EnterHidden(state, IslandTransitionReason.HideRequested,
                    IslandEffectKind.StopFrameLoop, IslandEffectKind.HideWindow,
                    IslandEffectKind.SyncClickThrough, IslandEffectKind.UpdateTray);

            case IslandStateKind.Closing:
                return Transition(state, state with { ClosingReturnKind = IslandStateKind.Hidden }, IslandTransitionReason.HideRequested);

            default:
                return IslandTransition.None(state);
        }
    }

    private static IslandTransition ApplyShowHotkey(IslandState state, IslandBehaviorOptions options, DateTimeOffset now)
    {
        if (state.Kind != IslandStateKind.Hidden || state.IsSuppressed)
        {
            return IslandTransition.None(state);
        }

        var compact = EnterCompact(state, options, now);
        return Transition(state, compact, IslandTransitionReason.ShowRequested,
            Effect(IslandEffectKind.ShowWindow),
            Effect(IslandEffectKind.Relayout),
            Effect(IslandEffectKind.SyncClickThrough),
            Effect(IslandEffectKind.UpdateTray),
            Effect(IslandEffectKind.StartFrameLoop));
    }

    private static IslandTransition ApplyAutoCollapse(IslandState state, IslandBehaviorOptions options, DateTimeOffset now)
    {
        if (state.Kind != IslandStateKind.Expanded || state.AutoCollapseDeadline is not { } deadline || now < deadline)
        {
            return IslandTransition.None(state);
        }

        if (CollapsePolicy.IsAutoCollapseBlockedByPin(state))
        {
            return Transition(state, state with { AutoCollapseDeadline = null },
                IslandTransitionReason.PinnedBlocked);
        }

        if (!CollapsePolicy.ShouldAutoCollapse(state, now))
        {
            return IslandTransition.None(state);
        }

        return StartClosing(state, options, now, IslandStateKind.Compact, IslandTransitionReason.AutoCollapse,
            Effect(IslandEffectKind.Retarget, IslandEffectData.Collapse),
            Effect(IslandEffectKind.StartFrameLoop));
    }

    private static IslandTransition ApplyAutoHide(IslandState state, IslandBehaviorOptions options)
    {
        if (!CollapsePolicy.ShouldAutoHide(state, options))
        {
            return IslandTransition.None(state);
        }

        return EnterHidden(state, IslandTransitionReason.AutoHide,
            IslandEffectKind.StopFrameLoop, IslandEffectKind.HideWindow,
            IslandEffectKind.SyncClickThrough, IslandEffectKind.UpdateTray);
    }

    private static IslandTransition ApplyCloseSettled(IslandState state, IslandBehaviorOptions options, DateTimeOffset now)
    {
        if (state.Kind != IslandStateKind.Closing)
        {
            return IslandTransition.None(state);
        }

        if (state.ClosingReturnKind == IslandStateKind.Hidden)
        {
            return EnterHidden(state with { DeferredInputs = [] }, IslandTransitionReason.CloseSettled,
                IslandEffectKind.StopFrameLoop, IslandEffectKind.HideWindow,
                IslandEffectKind.SyncClickThrough, IslandEffectKind.UpdateTray);
        }

        var compact = EnterCompact(state, options, now);
        var effects = new List<IslandEffect>
        {
            Effect(IslandEffectKind.Relayout),
            Effect(IslandEffectKind.UpdateContent),
            Effect(IslandEffectKind.UpdateTray),
            Effect(IslandEffectKind.SyncClickThrough),
            Effect(IslandEffectKind.StopFrameLoop),
        };

        var replay = compact with { DeferredInputs = [] };
        foreach (var deferred in state.DeferredInputs)
        {
            var transition = Reduce(replay, deferred, options, now);
            replay = transition.State;
            effects.AddRange(transition.Effects);
        }

        return Transition(state, replay, IslandTransitionReason.CloseSettled, Dedupe(effects));
    }

    private static IslandTransition ApplySuppressionChanged(
        IslandState state,
        in IslandInput input,
        IslandBehaviorOptions options,
        DateTimeOffset now)
    {
        if (input.Suppressed)
        {
            if (state.Kind == IslandStateKind.Hidden && state.IsSuppressed)
            {
                return IslandTransition.None(state);
            }

            return EnterHidden(state with { IsSuppressed = true }, IslandTransitionReason.SuppressionStarted,
                IslandEffectKind.HideWindow, IslandEffectKind.StopFrameLoop, IslandEffectKind.SyncClickThrough,
                IslandEffectKind.UpdateTray, IslandEffectKind.PauseProducers);
        }

        if (!state.IsSuppressed)
        {
            return IslandTransition.None(state);
        }

        var cleared = state with
        {
            IsSuppressed = false,
            HoverIntentDeadline = null,
            LeaveDeadline = null,
            PeekDwellDeadline = null,
        };

        if (state.Kind == IslandStateKind.Hidden)
        {
            var compact = EnterCompact(cleared, options, now);
            return Transition(state, compact, IslandTransitionReason.SuppressionEnded,
                Effect(IslandEffectKind.ResumeProducers),
                Effect(IslandEffectKind.ShowWindow),
                Effect(IslandEffectKind.Relayout),
                Effect(IslandEffectKind.SyncClickThrough),
                Effect(IslandEffectKind.UpdateTray),
                Effect(IslandEffectKind.StartFrameLoop));
        }

        return Transition(state, cleared, IslandTransitionReason.SuppressionEnded,
            Effect(IslandEffectKind.ResumeProducers),
            Effect(IslandEffectKind.Relayout));
    }

    private static IslandTransition ApplyEnvironmentChanged(
        IslandState state,
        IslandBehaviorOptions options,
        DateTimeOffset now,
        IslandTransitionReason reason)
    {
        if (state.IsSuppressed && state.Kind != IslandStateKind.Hidden)
        {
            return EnterHidden(state, IslandTransitionReason.SuppressionStarted,
                IslandEffectKind.HideWindow, IslandEffectKind.StopFrameLoop,
                IslandEffectKind.SyncClickThrough, IslandEffectKind.UpdateTray);
        }

        var next = state;
        if (reason == IslandTransitionReason.SettingsChanged && state.Kind == IslandStateKind.Peek && !options.HoverPeek)
        {
            next = EnterCompact(state, options, now);
        }

        return Transition(state, next, reason,
            Effect(IslandEffectKind.SnapSprings),
            Effect(IslandEffectKind.Relayout),
            Effect(IslandEffectKind.SyncClickThrough),
            Effect(IslandEffectKind.PersistPlacement));
    }

    private static IslandTransition ApplyPageRequested(IslandState state, in IslandInput input)
    {
        if (state.Kind != IslandStateKind.Expanded || input.Page is not { } page || page == state.Page)
        {
            return IslandTransition.None(state);
        }

        // The expanded shell is a uniform 440x284 for every page: no height change, content only.
        return Transition(state, state with { Page = page }, IslandTransitionReason.PageRequested,
            Effect(IslandEffectKind.UpdateContent),
            Effect(IslandEffectKind.Retarget, IslandEffectData.Tab));
    }

    private static IslandTransition ApplyPinnedChanged(
        IslandState state,
        in IslandInput input,
        IslandBehaviorOptions options,
        DateTimeOffset now)
    {
        var next = state with { IsPinned = input.IsPinned };
        if (input.IsPinned && state.Kind == IslandStateKind.Peek)
        {
            next = EnterCompact(state with { IsPinned = true }, options, now);
            return Transition(state, next, IslandTransitionReason.PinnedChanged,
                Effect(IslandEffectKind.Retarget, IslandEffectData.Hover),
                Effect(IslandEffectKind.Relayout),
                Effect(IslandEffectKind.UpdateTray));
        }

        if (input.IsPinned && state.Kind == IslandStateKind.Expanded)
        {
            next = next with { AutoCollapseDeadline = null };
        }

        return Transition(state, next, IslandTransitionReason.PinnedChanged,
            Effect(IslandEffectKind.UpdateTray));
    }

    private static IslandTransition ApplyDragStarted(IslandState state, IslandBehaviorOptions options)
    {
        if (state.Kind is not (IslandStateKind.Compact or IslandStateKind.Peek or IslandStateKind.Expanded))
        {
            return IslandTransition.None(state);
        }

        return Transition(state, state with
        {
            IsDragging = true,
            HoverIntentDeadline = null,
            LeaveDeadline = null,
            PeekDwellDeadline = null,
        }, IslandTransitionReason.DragStarted,
            Effect(IslandEffectKind.Retarget, IslandEffectData.Hover),
            Effect(IslandEffectKind.Relayout));
    }

    private static IslandTransition ApplyDragEnded(IslandState state)
    {
        if (!state.IsDragging)
        {
            return IslandTransition.None(state);
        }

        return Transition(state, state with { IsDragging = false }, IslandTransitionReason.DragEnded,
            Effect(IslandEffectKind.PersistPlacement),
            Effect(IslandEffectKind.SnapSprings),
            Effect(IslandEffectKind.Relayout));
    }

    private static IslandState EnterCompact(IslandState state, IslandBehaviorOptions options, DateTimeOffset now)
        => state with
        {
            Kind = IslandStateKind.Compact,
            HoverIntentDeadline = null,
            LeaveDeadline = null,
            PeekDwellDeadline = null,
            AutoCollapseDeadline = null,
            AutoHideDeadline = options.AutoHideMs > 0 ? now.AddMilliseconds(options.AutoHideMs) : null,
            DeferredInputs = [],
        };

    private static IslandState EnterExpanded(IslandState state, IslandBehaviorOptions options, DateTimeOffset now)
        => state with
        {
            Kind = IslandStateKind.Expanded,
            HoverIntentDeadline = null,
            LeaveDeadline = null,
            PeekDwellDeadline = null,
            AutoHideDeadline = null,
            // Auto-collapse only ever runs while the pointer is away: expanding from a hovered pill
            // (the normal click path) must not close the panel under the user's cursor. The deadline
            // is armed by PointerLeft once the pointer actually leaves the panel.
            AutoCollapseDeadline = options.AutoCollapseMs > 0 && !state.IsHovering
                ? now.AddMilliseconds(options.AutoCollapseMs)
                : null,
            DeferredInputs = [],
        };

    private static IslandTransition EnterHidden(IslandState state, IslandTransitionReason reason, params IslandEffectKind[] effectKinds)
    {
        var hidden = state with
        {
            Kind = IslandStateKind.Hidden,
            HoverIntentDeadline = null,
            LeaveDeadline = null,
            PeekDwellDeadline = null,
            AutoCollapseDeadline = null,
            AutoHideDeadline = null,
            DeferredInputs = [],
        };
        return Transition(state, hidden, reason, [.. effectKinds.Select(static k => new IslandEffect(k))]);
    }

    private static IslandTransition StartClosing(
        IslandState state,
        IslandBehaviorOptions options,
        DateTimeOffset now,
        IslandStateKind returnKind,
        IslandTransitionReason reason,
        params IslandEffect[] effects)
    {
        var closing = state with
        {
            Kind = IslandStateKind.Closing,
            ClosingReturnKind = returnKind,
            HoverIntentDeadline = null,
            LeaveDeadline = null,
            PeekDwellDeadline = null,
            AutoCollapseDeadline = null,
            AutoHideDeadline = null,
            DeferredInputs = [],
        };
        return Transition(state, closing, reason, effects);
    }

    private static IslandTransition Defer(IslandState state, IslandInput input, IslandBehaviorOptions options)
    {
        var deferred = state.DeferredInputs.Add(input);
        var max = Math.Max(1, options.MaxDeferredInputs);
        if (deferred.Length > max)
        {
            deferred = deferred.RemoveAt(0);
        }

        return Transition(state, state with { DeferredInputs = deferred }, IslandTransitionReason.InputDeferred);
    }

    private static IslandTransition Transition(
        IslandState previous,
        IslandState next,
        IslandTransitionReason reason,
        params IslandEffect[] effects)
        => new() { Previous = previous, State = next, Reason = reason, Effects = [.. effects] };

    private static IslandEffect Effect(IslandEffectKind kind) => new(kind);

    private static IslandEffect Effect(IslandEffectKind kind, string data) => new(kind, data);

    private static IslandEffect[] Dedupe(List<IslandEffect> effects)
    {
        var seen = new HashSet<IslandEffect>();
        var builder = ImmutableArray.CreateBuilder<IslandEffect>(effects.Count);
        foreach (var effect in effects)
        {
            if (seen.Add(effect))
            {
                builder.Add(effect);
            }
        }

        return [.. builder];
    }
}

/// <summary>
/// Thread-safe owner of the current <see cref="IslandState"/>: applies inputs via the pure reducer
/// and raises <see cref="Transitioned"/> for the host. Optionally marshals notifications through
/// an injected <see cref="IDispatcher"/>.
/// </summary>
public sealed class IslandStateController : IDisposable
{
    private readonly object _gate = new();
    private readonly IslandBehaviorOptions _options;
    private readonly IDispatcher? _dispatcher;
    private IslandState _current;
    private bool _disposed;

    /// <summary>Creates a controller starting in <see cref="IslandState.Hidden"/>.</summary>
    /// <param name="options">Behaviour switches.</param>
    /// <param name="initialState">Optional initial state (tests/restorage).</param>
    /// <param name="dispatcher">Optional dispatcher used to raise <see cref="Transitioned"/> on the UI thread.</param>
    public IslandStateController(
        IslandBehaviorOptions? options = null,
        IslandState? initialState = null,
        IDispatcher? dispatcher = null)
    {
        _options = options ?? IslandBehaviorOptions.Default;
        _current = initialState ?? IslandState.Hidden;
        _dispatcher = dispatcher;
    }

    /// <summary>Raised after every accepted input with the resulting transition.</summary>
    public event Action<IslandTransition>? Transitioned;

    /// <summary>Current immutable state snapshot.</summary>
    public IslandState Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    /// <summary>Applies one input at the given time and returns the transition.</summary>
    public IslandTransition Apply(IslandInput input, DateTimeOffset now)
    {
        IslandTransition transition;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            transition = IslandStateMachine.Reduce(_current, input, _options, now);
            _current = transition.State;
        }

        if (transition.Reason != IslandTransitionReason.NoChange || transition.Effects.Length > 0)
        {
            if (_dispatcher is null || _dispatcher.IsOnDispatcherThread)
            {
                Transitioned?.Invoke(transition);
            }
            else
            {
                _dispatcher.Post(() => Transitioned?.Invoke(transition));
            }
        }

        return transition;
    }

    /// <summary>Replaces the current snapshot without raising an event (restore/snap paths).</summary>
    public void Reset(IslandState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _current = state;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
        }
    }
}
