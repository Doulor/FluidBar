namespace FluidBar.Core.Activities;

/// <summary>
/// Immutable activity record held by the scheduler: identity, content snapshot, priority
/// and expiry bookkeeping. Records are replaced wholesale via <c>with</c> expressions.
/// </summary>
public sealed record ActivityRecord
{
    /// <summary>Activity identity.</summary>
    public required ActivityKey Key { get; init; }

    /// <summary>Activity class.</summary>
    public required ActivityKind Kind { get; init; }

    /// <summary>Current immutable content snapshot.</summary>
    public required ActivityPayload Payload { get; init; }

    /// <summary>Monotonic per-key producer sequence of the snapshot currently held.</summary>
    public long Sequence { get; init; }

    /// <summary>When the scheduler first admitted this activity (ordering tie-breaker).</summary>
    public DateTimeOffset ReceivedAt { get; init; }

    /// <summary>Absolute expiry time; <see langword="null"/> for persistent/manual activities.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>Pinned activities suppress preemption and TTL expiry (session-level pin).</summary>
    public bool IsPinned { get; init; }

    /// <summary>Cached priority rank (lower wins).</summary>
    public int PriorityRank { get; init; }

    /// <summary>Whether the activity survives preemption in the background layer.</summary>
    public bool IsPersistent { get; init; }

    /// <summary>Signature of the currently presented content (spec 2.7 rule 2).</summary>
    public ActivityContentSignature Signature { get; init; }

    /// <summary>Whether <paramref name="now"/> is at/after the expiry time and the record may expire.</summary>
    public bool IsExpired(DateTimeOffset now) => !IsPinned && ExpiresAt is { } expiry && expiry <= now;

    /// <summary>Creates a record and derives rank/persistence/TTL from the policy.</summary>
    public static ActivityRecord Create(
        ActivityKey key,
        ActivityPayload payload,
        long sequence,
        DateTimeOffset now,
        TimeSpan? ttlOverride = null)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var kind = key.Kind;
        var persistent = ActivityPriorityPolicy.IsPersistent(kind, payload);
        var ttl = persistent ? null : ttlOverride ?? payload.Ttl ?? ActivityPriorityPolicy.DefaultTtl(kind);
        var pinned = payload.IsPinned;
        return new ActivityRecord
        {
            Key = key,
            Kind = kind,
            Payload = payload,
            Sequence = sequence,
            ReceivedAt = now,
            ExpiresAt = ttl is { } duration ? now + duration : null,
            IsPinned = pinned,
            PriorityRank = ActivityPriorityPolicy.Rank(kind),
            IsPersistent = persistent,
            Signature = ContentSignaturePolicy.Compute(key, kind, payload),
        };
    }

    /// <summary>Returns a copy with a new content snapshot and refreshed expiry/signature.</summary>
    public ActivityRecord WithPayload(ActivityPayload payload, long sequence, DateTimeOffset now, TimeSpan? ttlOverride = null)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var pinned = payload.IsPinned || IsPinned;
        // Persistence is payload-sensitive: running Agent/Media is persistent, but a completed Agent
        // or paused Media must regain a finite TTL. Recompute it instead of inheriting the old flag.
        var persistent = ActivityPriorityPolicy.IsPersistent(Kind, payload);
        var ttl = persistent ? null : ttlOverride ?? payload.Ttl ?? ActivityPriorityPolicy.DefaultTtl(Kind);
        return this with
        {
            Payload = payload,
            Sequence = sequence,
            ExpiresAt = persistent ? null : ttl is { } duration ? now + duration : null,
            IsPersistent = persistent,
            IsPinned = pinned,
            Signature = ContentSignaturePolicy.Compute(Key, Kind, payload),
        };
    }

    /// <summary>Returns a copy with the session pin flag changed.</summary>
    public ActivityRecord WithPinned(bool pinned) => this with { IsPinned = pinned };

    /// <summary>Debug-friendly identity.</summary>
    public override string ToString() => $"{Key}#{Sequence}(rank {PriorityRank}{(IsPinned ? ", pinned" : string.Empty)})";
}
