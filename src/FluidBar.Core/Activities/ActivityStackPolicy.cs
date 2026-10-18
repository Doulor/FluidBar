using System.Collections.Immutable;

namespace FluidBar.Core.Activities;

/// <summary>How multiple live activities are presented (spec 2.5).</summary>
public enum StackingStrategy
{
    /// <summary>Primary pill + up to two ghost pills behind it.</summary>
    Stacked,

    /// <summary>Only the latest/highest-priority activity is visible; no ghosts.</summary>
    LatestOnly,
}

/// <summary>
/// Pure configuration for priority/stacking/expiry behaviour (spec 2.3, 2.5).
/// Immutable; construct once and share.
/// </summary>
public sealed record ActivityStackPolicy
{
    /// <summary>Maximum visible layers, including the primary pill (spec 2.5: primary + 2 ghosts).</summary>
    public const int DefaultMaxVisible = 3;

    /// <summary>Maximum hidden background queue depth (spec 2.3 rule 4: queue depth 8).</summary>
    public const int DefaultMaxBackground = 8;

    /// <summary>Shared default policy.</summary>
    public static ActivityStackPolicy Default { get; } = new();

    /// <summary>Stacked or latest-only presentation.</summary>
    public StackingStrategy Stacking { get; init; } = StackingStrategy.Stacked;

    /// <summary>Visible layer count when <see cref="Stacking"/> is <see cref="StackingStrategy.Stacked"/>.</summary>
    public int MaxVisible { get; init; } = DefaultMaxVisible;

    /// <summary>Hidden background queue depth.</summary>
    public int MaxBackground { get; init; } = DefaultMaxBackground;

    /// <summary>
    /// "Media pinning": media stays live in the background instead of being dropped when
    /// preempted by higher-priority activity (media is persistent anyway; kept explicit).
    /// </summary>
    public bool PinMedia { get; init; } = true;

    /// <summary>Keep preempted persistent kinds (media/volume/pomodoro...) in the background layer.</summary>
    public bool KeepPreemptedPersistent { get; init; } = true;

    /// <summary>Per-kind TTL overrides from settings; empty means use spec defaults.</summary>
    public ImmutableDictionary<ActivityKind, TimeSpan> TtlOverrides { get; init; } =
        ImmutableDictionary<ActivityKind, TimeSpan>.Empty;

    /// <summary>Effective visible capacity for the current strategy.</summary>
    public int VisibleCapacity => Stacking == StackingStrategy.LatestOnly ? 1 : Math.Clamp(MaxVisible, 1, DefaultMaxVisible);

    /// <summary>Effective ghost capacity for the current strategy (0..2).</summary>
    public int GhostCapacity => Math.Max(0, VisibleCapacity - 1);

    /// <summary>Resolves the effective TTL for a payload; persistent kinds return <see langword="null"/>.</summary>
    public TimeSpan? ResolveTtl(ActivityKind kind, ActivityPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (ActivityPriorityPolicy.IsPersistent(kind))
        {
            return null;
        }

        if (payload.Ttl is { } payloadTtl)
        {
            return payloadTtl;
        }

        return TtlOverrides.TryGetValue(kind, out var configured) ? configured : ActivityPriorityPolicy.DefaultTtl(kind);
    }

    /// <summary>Returns a copy with the stacking strategy changed (settings apply live).</summary>
    public ActivityStackPolicy WithStacking(StackingStrategy strategy) => this with { Stacking = strategy };
}

/// <summary>Outcome class of one scheduler operation; used by the state machine and diagnostics.</summary>
public enum ActivityUpdateKind
{
    /// <summary>No live state changed (duplicate, stale sequence, released pin...).</summary>
    None,

    /// <summary>A new activity entered the live set.</summary>
    Added,

    /// <summary>An existing key was updated in place (no re-entry animation).</summary>
    ReplacedInPlace,

    /// <summary>Incremental content update (progress/lyrics); usually coalesced upstream.</summary>
    ContentUpdated,

    /// <summary>A higher-priority activity became primary.</summary>
    Preempted,

    /// <summary>A previously primary activity was demoted to the background layer.</summary>
    Demoted,

    /// <summary>One or more activities expired.</summary>
    Expired,

    /// <summary>One or more activities were retracted by their producer.</summary>
    Retracted,

    /// <summary>Session pin state changed.</summary>
    PinnedChanged,

    /// <summary>All activities were dismissed by the user.</summary>
    Cleared,
}

/// <summary>Result of one scheduler reduction: the new snapshot plus what changed.</summary>
public sealed record ActivitySchedulerUpdate
{
    /// <summary>The resulting immutable scheduler snapshot.</summary>
    public required ActivitySchedulerState State { get; init; }

    /// <summary>Outcome class.</summary>
    public ActivityUpdateKind Kind { get; init; } = ActivityUpdateKind.None;

    /// <summary>Key of the activity that caused the update, when applicable.</summary>
    public ActivityKey? Key { get; init; }

    /// <summary>Kind of the activity that caused the update, when applicable.</summary>
    public ActivityKind? ActivityKind { get; init; }

    /// <summary>Whether the presented content signature changed.</summary>
    public bool ContentChanged { get; init; }

    /// <summary>Whether the primary (front) activity changed.</summary>
    public bool PrimaryChanged { get; init; }

    /// <summary>Number of activities removed from the live set by this update.</summary>
    public int RemovedCount { get; init; }

    /// <summary>Actor sequence carried through for diagnostics (0 when not applicable).</summary>
    public long Sequence { get; init; }

    /// <summary>Whether anything at all changed.</summary>
    public bool HasChange => Kind != ActivityUpdateKind.None;

    /// <summary>Whether a one-shot nudge should be played for this update.</summary>
    public bool ShouldNudge => Kind is ActivityUpdateKind.Added or ActivityUpdateKind.Preempted
        or ActivityUpdateKind.ReplacedInPlace;

    /// <summary>Creates a no-op update for <paramref name="state"/>.</summary>
    public static ActivitySchedulerUpdate None(ActivitySchedulerState state) => new() { State = state };
}
