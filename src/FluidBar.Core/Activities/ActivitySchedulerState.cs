using System.Collections.Immutable;

namespace FluidBar.Core.Activities;

/// <summary>
/// Immutable scheduler snapshot: the visible stack (index 0 = primary), the bounded background
/// queue and session counters. All collections are bounded by <see cref="ActivityStackPolicy"/>.
/// </summary>
public sealed record ActivitySchedulerState
{
    /// <summary>Empty snapshot: no activities, idle pill.</summary>
    public static ActivitySchedulerState Empty { get; } = new();

    /// <summary>Visible stack, sorted primary-first (pinned first, then priority, then arrival).</summary>
    public ImmutableArray<ActivityRecord> Visible { get; init; } = [];

    /// <summary>Live activities that are not currently visible (preempted persistent kinds / overflow), bounded.</summary>
    public ImmutableArray<ActivityRecord> Background { get; init; } = [];

    /// <summary>Same-priority replacements observed since the session started (spec 2.3 rule 3).</summary>
    public long ReplacedCount { get; init; }

    /// <summary>Activities removed by TTL since the session started.</summary>
    public long ExpiredCount { get; init; }

    /// <summary>Activities discarded by stack/queue limits since the session started.</summary>
    public long DroppedCount { get; init; }

    /// <summary>Primary-slot changes since the session started.</summary>
    public long SwitchedCount { get; init; }

    /// <summary>Current primary activity, or <see langword="null"/> when only the idle surface is shown.</summary>
    public ActivityRecord? Primary => Visible.Length > 0 ? Visible[0] : null;

    /// <summary>Total live activities (visible + background).</summary>
    public int ActivityCount => Visible.Length + Background.Length;

    /// <summary>Number of hidden live activities; surfaced as the <c>+N</c> badge (spec 2.5).</summary>
    public int BadgeCount => Background.Length;

    /// <summary>Kind of the primary activity, or <see langword="null"/> for the idle surface.</summary>
    public ActivityKind? PrimaryKind => Primary?.Kind;

    /// <summary>Page the primary activity naturally opens on, or <see cref="IslandPage.Home"/>.</summary>
    public IslandPage PrimaryPage => Primary?.Payload.PreferredPage ?? Primary?.Kind.ToPage() ?? IslandPage.Home;

    /// <summary>Whether any activity is live.</summary>
    public bool HasActivities => Visible.Length + Background.Length > 0;

    /// <summary>Finds a live activity by key (visible first, then background).</summary>
    public ActivityRecord? Find(ActivityKey key)
    {
        foreach (var record in Visible)
        {
            if (record.Key.Equals(key))
            {
                return record;
            }
        }

        foreach (var record in Background)
        {
            if (record.Key.Equals(key))
            {
                return record;
            }
        }

        return null;
    }

    /// <summary>Whether <paramref name="key"/> is currently live.</summary>
    public bool Contains(ActivityKey key) => Find(key) is not null;

    /// <summary>Bounded, always-in-sync sequence watermark map (one entry per live activity).</summary>
    public ImmutableDictionary<ActivityKey, long> LastSequences
    {
        get
        {
            if (Visible.IsDefaultOrEmpty && Background.IsDefaultOrEmpty)
            {
                return ImmutableDictionary<ActivityKey, long>.Empty;
            }

            var builder = ImmutableDictionary.CreateBuilder<ActivityKey, long>();
            foreach (var record in Visible)
            {
                if (record.Sequence != 0)
                {
                    builder[record.Key] = record.Sequence;
                }
            }

            foreach (var record in Background)
            {
                if (record.Sequence != 0)
                {
                    builder[record.Key] = record.Sequence;
                }
            }

            return builder.ToImmutable();
        }
    }
}
