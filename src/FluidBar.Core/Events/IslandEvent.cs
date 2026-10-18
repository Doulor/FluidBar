namespace FluidBar.Core.Events;

/// <summary>How an <see cref="IslandEvent"/> updates an activity (spec 5.3).</summary>
public enum IslandEventKind
{
    /// <summary>A new activity with a new key; enters the priority/stacking policy.</summary>
    New,

    /// <summary>Full payload replacement for an existing key; keeps its stack position.</summary>
    Replacement,

    /// <summary>Partial payload update (progress/position/lyrics); coalesced latest-wins.</summary>
    Incremental,

    /// <summary>Producer withdraws the activity; it leaves the stack immediately.</summary>
    Retract,
}

/// <summary>
/// The single event type crossing the event hub (spec 5.3). Immutable by construction:
/// <see cref="Payload"/> is an immutable snapshot, and <see cref="IslandEvent"/> is a value type.
/// </summary>
/// <param name="Key">Activity identity (<c>kind + source</c>).</param>
/// <param name="Kind">Update semantics.</param>
/// <param name="Sequence">Monotonic per-key sequence; the scheduler discards older/out-of-order events.</param>
/// <param name="Timestamp">Producer timestamp (elapsed since app start, as specified in 5.3).</param>
/// <param name="Payload">Immutable content snapshot; <see cref="Activities.ActivityPayload.Empty"/> for retractions.</param>
public readonly record struct IslandEvent(
    Activities.ActivityKey Key,
    IslandEventKind Kind,
    long Sequence,
    TimeSpan Timestamp,
    Activities.ActivityPayload Payload)
{
    /// <summary>Creates a <see cref="IslandEventKind.New"/> event.</summary>
    public static IslandEvent New(Activities.ActivityKey key, Activities.ActivityPayload payload, long sequence, TimeSpan timestamp = default)
        => new(key, IslandEventKind.New, sequence, timestamp, payload);

    /// <summary>Creates a <see cref="IslandEventKind.Replacement"/> event.</summary>
    public static IslandEvent Replacement(Activities.ActivityKey key, Activities.ActivityPayload payload, long sequence, TimeSpan timestamp = default)
        => new(key, IslandEventKind.Replacement, sequence, timestamp, payload);

    /// <summary>Creates a <see cref="IslandEventKind.Incremental"/> event.</summary>
    public static IslandEvent Incremental(Activities.ActivityKey key, Activities.ActivityPayload payload, long sequence, TimeSpan timestamp = default)
        => new(key, IslandEventKind.Incremental, sequence, timestamp, payload);

    /// <summary>Creates a <see cref="IslandEventKind.Retract"/> event.</summary>
    public static IslandEvent Retract(Activities.ActivityKey key, long sequence, TimeSpan timestamp = default)
        => new(key, IslandEventKind.Retract, sequence, timestamp, Activities.ActivityPayload.Empty);

    /// <summary>Whether the event carries a content snapshot that may be merged/compared.</summary>
    public bool HasPayload => Kind != IslandEventKind.Retract;

    /// <summary>Compact debug string for structured logs.</summary>
    public override string ToString() => $"{Kind}#{Sequence} {Key}";
}
