using System.Collections.Immutable;
using FluidBar.Core.Diagnostics;
using FluidBar.Core.Events;

namespace FluidBar.Core.Activities;

/// <summary>
/// Pure activity scheduler (spec 2.3, 5.4): priority preemption, in-place replacement,
/// stacking with a bounded background queue, TTL expiry and retraction.
/// No UI, no timers, no I/O: callers pass <c>now</c> explicitly (injected <see cref="TimeProvider"/>).
/// </summary>
public sealed class ActivityScheduler
{
    private readonly ActivityStackPolicy _policy;
    private readonly DiagnosticCounters? _counters;

    /// <summary>Creates a scheduler.</summary>
    /// <param name="policy">Stacking/priority policy; defaults to <see cref="ActivityStackPolicy.Default"/>.</param>
    /// <param name="counters">Optional counters incremented on switches/drops/expiry.</param>
    public ActivityScheduler(ActivityStackPolicy? policy = null, DiagnosticCounters? counters = null)
    {
        _policy = policy ?? ActivityStackPolicy.Default;
        _counters = counters;
    }

    /// <summary>The policy in effect.</summary>
    public ActivityStackPolicy Policy => _policy;

    /// <summary>Applies one producer event to <paramref name="state"/>.</summary>
    /// <param name="state">Current immutable snapshot.</param>
    /// <param name="evt">Producer event.</param>
    /// <param name="now">Current time; never read ambiently inside the core.</param>
    public ActivitySchedulerUpdate Update(ActivitySchedulerState state, in IslandEvent evt, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!evt.Key.IsValid)
        {
            return ActivitySchedulerUpdate.None(state);
        }

        var existing = state.Find(evt.Key);
        if (existing is not null && evt.Sequence != 0 && evt.Sequence <= existing.Sequence)
        {
            // Out-of-order or duplicate producer sequence: discard (spec 5.3).
            return ActivitySchedulerUpdate.None(state);
        }

        return evt.Kind switch
        {
            IslandEventKind.Retract => Retract(state, evt),
            _ => Upsert(state, evt, existing, now),
        };
    }

    /// <summary>Removes expired (non-pinned) activities and promotes replacements.</summary>
    public ActivitySchedulerUpdate Tick(ActivitySchedulerState state, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!state.HasActivities)
        {
            return ActivitySchedulerUpdate.None(state);
        }

        var expiredVisible = state.Visible.Where(r => r.IsExpired(now)).ToList();
        var expiredBackground = state.Background.Where(r => r.IsExpired(now)).ToList();
        var expiredCount = expiredVisible.Count + expiredBackground.Count;
        if (expiredCount == 0)
        {
            return ActivitySchedulerUpdate.None(state);
        }

        var visible = state.Visible.ToList();
        var background = state.Background.ToList();
        visible.RemoveAll(r => r.IsPinned ? false : r.ExpiresAt is { } e && e <= now);
        background.RemoveAll(r => r.IsPinned ? false : r.ExpiresAt is { } e && e <= now);

        var previousPrimary = state.Primary;
        var normalized = Normalize(visible, background);
        var newState = state with
        {
            Visible = normalized.Visible,
            Background = normalized.Background,
            ExpiredCount = state.ExpiredCount + expiredCount,
            DroppedCount = state.DroppedCount + normalized.Dropped,
            SwitchedCount = state.SwitchedCount + (SameKey(previousPrimary, normalized.Visible) ? 0 : 1),
        };

        _counters?.Add(CounterKind.ActivitiesExpired, expiredCount);
        return new ActivitySchedulerUpdate
        {
            State = newState,
            Kind = ActivityUpdateKind.Expired,
            Key = previousPrimary?.Key,
            ActivityKind = previousPrimary?.Kind,
            ContentChanged = true,
            PrimaryChanged = !SameKey(previousPrimary, normalized.Visible),
            RemovedCount = expiredCount,
        };
    }

    /// <summary>Sets or clears the session pin on a live activity.</summary>
    /// <param name="state">Current snapshot.</param>
    /// <param name="key">Activity identity.</param>
    /// <param name="pinned">Desired pin value.</param>
    public ActivitySchedulerUpdate SetPinned(ActivitySchedulerState state, ActivityKey key, bool pinned)
    {
        ArgumentNullException.ThrowIfNull(state);
        var visible = state.Visible.ToList();
        var background = state.Background.ToList();
        var found = false;

        for (var i = 0; i < visible.Count; i++)
        {
            if (visible[i].Key.Equals(key))
            {
                visible[i] = visible[i].WithPinned(pinned);
                found = true;
            }
        }

        for (var i = 0; i < background.Count; i++)
        {
            if (background[i].Key.Equals(key))
            {
                background[i] = background[i].WithPinned(pinned);
                found = true;
            }
        }

        if (!found)
        {
            return ActivitySchedulerUpdate.None(state);
        }

        var previousPrimary = state.Primary;
        var normalized = Normalize(visible, background);
        var newState = state with
        {
            Visible = normalized.Visible,
            Background = normalized.Background,
            DroppedCount = state.DroppedCount + normalized.Dropped,
            SwitchedCount = state.SwitchedCount + (SameKey(previousPrimary, normalized.Visible) ? 0 : 1),
        };

        return new ActivitySchedulerUpdate
        {
            State = newState,
            Kind = ActivityUpdateKind.PinnedChanged,
            Key = key,
            ActivityKind = key.Kind,
            PrimaryChanged = !SameKey(previousPrimary, normalized.Visible),
        };
    }

    /// <summary>Dismisses every activity (user "clear all").</summary>
    public static ActivitySchedulerUpdate Clear(ActivitySchedulerState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!state.HasActivities)
        {
            return ActivitySchedulerUpdate.None(state);
        }

        var removed = state.ActivityCount;
        return new ActivitySchedulerUpdate
        {
            State = ActivitySchedulerState.Empty,
            Kind = ActivityUpdateKind.Cleared,
            ContentChanged = true,
            PrimaryChanged = state.Primary is not null,
            RemovedCount = removed,
        };
    }

    private ActivitySchedulerUpdate Upsert(
        ActivitySchedulerState state,
        in IslandEvent evt,
        ActivityRecord? existing,
        DateTimeOffset now)
    {
        var visible = state.Visible.ToList();
        var background = state.Background.ToList();
        var eventKey = evt.Key;

        // Heartbeats re-publish a fresh ActivityRecord; the scheduler is the owner of the session
        // pin, so a same-key snapshot must never silently clear it.
        var pinSource = existing
            ?? visible.FirstOrDefault(r => r.Key == eventKey)
            ?? background.FirstOrDefault(r => r.Key == eventKey);
        var inheritedPin = pinSource?.IsPinned ?? false;
        LogSchedulerUpsert(evt.Key, inheritedPin, visible, background);

        if (existing is not null)
        {
            var mergedPayload = evt.Kind == IslandEventKind.Incremental
                ? existing.Payload.Merge(evt.Payload)
                : evt.Payload;
            var updated = existing.WithPayload(
                mergedPayload,
                evt.Sequence == 0 ? existing.Sequence : evt.Sequence,
                now,
                _policy.ResolveTtl(evt.Key.Kind, mergedPayload));
            if (inheritedPin)
            {
                updated = updated.WithPinned(true);
            }
            var contentChanged = ContentSignaturePolicy.ShouldUpdateContent(existing, updated);
            ReplaceInList(visible, background, updated);

            var previousPrimary = state.Primary;
            var normalized = Normalize(visible, background);
            var updatedState = state with
            {
                Visible = normalized.Visible,
                Background = normalized.Background,
                DroppedCount = state.DroppedCount + normalized.Dropped,
                SwitchedCount = state.SwitchedCount + (SameKey(previousPrimary, normalized.Visible) ? 0 : 1),
            };

            return new ActivitySchedulerUpdate
            {
                State = updatedState,
                Kind = evt.Kind == IslandEventKind.Incremental
                    ? ActivityUpdateKind.ContentUpdated
                    : ActivityUpdateKind.ReplacedInPlace,
                Key = evt.Key,
                ActivityKind = evt.Key.Kind,
                ContentChanged = contentChanged,
                PrimaryChanged = !SameKey(previousPrimary, normalized.Visible),
                Sequence = evt.Sequence,
            };
        }

        var record = ActivityRecord.Create(
            evt.Key,
            evt.Payload,
            evt.Sequence,
            now,
            _policy.ResolveTtl(evt.Key.Kind, evt.Payload));
        if (inheritedPin)
        {
            record = record.WithPinned(true);
        }

        // Rule 3 (spec 2.3): equal priority replaces the older same-rank activity.
        var sameRankVisible = visible.FindIndex(r => r.PriorityRank == record.PriorityRank);
        var sameRankBackground = background.FindIndex(r => r.PriorityRank == record.PriorityRank);
        var hadSameRank = sameRankVisible >= 0 || sameRankBackground >= 0;
        if (sameRankVisible >= 0)
        {
            visible.RemoveAt(sameRankVisible);
        }

        if (sameRankBackground >= 0)
        {
            background.RemoveAt(sameRankBackground);
        }

        visible.Add(record);

        // Rule 2 (spec 2.3): the preempted primary is demoted to the background layer when it is
        // persistent, otherwise it is discarded. It must not linger as a ghost pill.
        var oldPrimary = state.Primary;
        var preemptDropped = 0;
        var isPreemptionCandidate = oldPrimary is not null
            && !oldPrimary.IsPinned
            && record.PriorityRank < oldPrimary.PriorityRank;
        if (isPreemptionCandidate)
        {
            visible.RemoveAll(r => r.Key.Equals(oldPrimary!.Key));
            background.RemoveAll(r => r.Key.Equals(oldPrimary!.Key));
            if (oldPrimary!.IsPersistent)
            {
                background.Add(oldPrimary);
            }
            else
            {
                preemptDropped = 1;
            }
        }

        var normalizedState = Normalize(visible, background);
        var primaryChanged = !SameKey(oldPrimary, normalizedState.Visible);
        var isPreemption = isPreemptionCandidate && primaryChanged;

        var dropped = normalizedState.Dropped + preemptDropped;
        var newState = state with
        {
            Visible = normalizedState.Visible,
            Background = normalizedState.Background,
            ReplacedCount = state.ReplacedCount + (hadSameRank ? 1 : 0),
            DroppedCount = state.DroppedCount + dropped,
            SwitchedCount = state.SwitchedCount + (primaryChanged ? 1 : 0),
        };

        var kind = isPreemption ? ActivityUpdateKind.Preempted : ActivityUpdateKind.Added;
        if (isPreemption)
        {
            _counters?.Increment(CounterKind.ActivitiesSwitched);
        }

        if (hadSameRank)
        {
            _counters?.Increment(CounterKind.ActivitiesReplaced);
        }

        if (dropped > 0)
        {
            _counters?.Add(CounterKind.ActivitiesDropped, dropped);
        }

        return new ActivitySchedulerUpdate
        {
            State = newState,
            Kind = kind,
            Key = evt.Key,
            ActivityKind = evt.Key.Kind,
            ContentChanged = true,
            PrimaryChanged = primaryChanged,
            RemovedCount = (hadSameRank ? 1 : 0) + preemptDropped,
            Sequence = evt.Sequence,
        };
    }

    private ActivitySchedulerUpdate Retract(ActivitySchedulerState state, in IslandEvent evt)
    {
        var key = evt.Key;
        var visible = state.Visible.ToList();
        var background = state.Background.ToList();
        var removedVisible = visible.RemoveAll(r => r.Key.Equals(key));
        var removedBackground = background.RemoveAll(r => r.Key.Equals(key));
        if (removedVisible == 0 && removedBackground == 0)
        {
            return ActivitySchedulerUpdate.None(state);
        }

        var previousPrimary = state.Primary;
        var normalized = Normalize(visible, background);
        var newState = state with
        {
            Visible = normalized.Visible,
            Background = normalized.Background,
            DroppedCount = state.DroppedCount + normalized.Dropped,
            SwitchedCount = state.SwitchedCount + (SameKey(previousPrimary, normalized.Visible) ? 0 : 1),
        };

        _counters?.Increment(CounterKind.ActivitiesRetracted);
        return new ActivitySchedulerUpdate
        {
            State = newState,
            Kind = ActivityUpdateKind.Retracted,
            Key = key,
            ActivityKind = key.Kind,
            ContentChanged = true,
            PrimaryChanged = !SameKey(previousPrimary, normalized.Visible),
            RemovedCount = removedVisible + removedBackground,
            Sequence = evt.Sequence,
        };
    }

    private static void ReplaceInList(List<ActivityRecord> visible, List<ActivityRecord> background, ActivityRecord record)
    {
        for (var i = 0; i < visible.Count; i++)
        {
            if (visible[i].Key.Equals(record.Key))
            {
                visible[i] = record;
                return;
            }
        }

        for (var i = 0; i < background.Count; i++)
        {
            if (background[i].Key.Equals(record.Key))
            {
                background[i] = record;
                return;
            }
        }

        visible.Add(record);
    }

    private static void LogSchedulerUpsert(
        ActivityKey key,
        bool pinned,
        IReadOnlyList<ActivityRecord> visible,
        IReadOnlyList<ActivityRecord> background)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("FLUIDBAR_PIN_DIAG"), "1", StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            var order = string.Join(",", visible.Select(r => r.Key.ToString()));
            File.AppendAllText(
                Path.Combine(Path.GetTempPath(), "fluidbar-pin-diag.log"),
                $"{DateTimeOffset.Now:O} | scheduler upsert {key} pinned={pinned} visible=[{order}] background={background.Count}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
    }

    private static bool SameKey(ActivityRecord? previousPrimary, ImmutableArray<ActivityRecord> visible)
    {
        var next = visible.Length > 0 ? visible[0] : null;
        if (previousPrimary is null || next is null)
        {
            return previousPrimary is null && next is null;
        }

        return previousPrimary.Key.Equals(next.Key);
    }

    /// <summary>Sorted, capacity-bounded snapshots plus the number of activities dropped by queue limits.</summary>
    private readonly record struct NormalizedStack(
        ImmutableArray<ActivityRecord> Visible,
        ImmutableArray<ActivityRecord> Background,
        int Dropped);

    /// <summary>
    /// Sorts live records deterministically (pinned first, then rank, then arrival), keeps
    /// <see cref="ActivityStackPolicy.VisibleCapacity"/> visible, and bounds the background queue.
    /// </summary>
    private NormalizedStack Normalize(List<ActivityRecord> visible, List<ActivityRecord> background)
    {
        var live = new List<ActivityRecord>(visible.Count + background.Count);
        live.AddRange(visible);
        live.AddRange(background);

        live.Sort(static (left, right) =>
        {
            var pinned = right.IsPinned.CompareTo(left.IsPinned);
            if (pinned != 0)
            {
                return pinned;
            }

            var rank = left.PriorityRank.CompareTo(right.PriorityRank);
            if (rank != 0)
            {
                return rank;
            }

            var arrival = left.ReceivedAt.CompareTo(right.ReceivedAt);
            return arrival != 0 ? arrival : left.Sequence.CompareTo(right.Sequence);
        });

        var capacity = _policy.VisibleCapacity;
        var newVisible = live.Count <= capacity ? live : live.GetRange(0, capacity);
        var newBackground = live.Count <= capacity ? [] : live.GetRange(capacity, live.Count - capacity);

        var dropped = 0;
        if (!_policy.KeepPreemptedPersistent)
        {
            dropped += newBackground.RemoveAll(r => r.IsPersistent);
        }

        var maxBackground = Math.Max(0, _policy.MaxBackground);
        while (newBackground.Count > maxBackground)
        {
            // Spec 2.3 rule 4: overflow drops the oldest non-persistent entry first.
            var victim = newBackground.FindIndex(r => !r.IsPersistent && !r.IsPinned);
            if (victim < 0)
            {
                victim = newBackground.Count - 1;
            }

            newBackground.RemoveAt(victim);
            dropped++;
        }

        return new NormalizedStack([.. newVisible], [.. newBackground], dropped);
    }
}
