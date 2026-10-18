using System.Collections.Immutable;

namespace FluidBar.Core.Diagnostics;

/// <summary>
/// Named counters exposed by the core for the diagnostics page/bundle (spec 9.3).
/// Only counters that are cheap to maintain belong here.
/// </summary>
public enum CounterKind
{
    /// <summary>Events accepted by <c>IEventBus.TryPublish</c>.</summary>
    EventsPublished = 0,

    /// <summary>Incremental events replaced in the bounded queue (latest wins).</summary>
    EventsCoalesced = 1,

    /// <summary>Events discarded because the bounded queue was full.</summary>
    EventsDropped = 2,

    /// <summary>Subscriber callbacks that threw and were isolated.</summary>
    EventSubscriberFaults = 3,

    /// <summary>Events delivered to subscribers.</summary>
    EventsDelivered = 4,

    /// <summary>Scheduler primary-activity changes.</summary>
    ActivitiesSwitched = 5,

    /// <summary>Same-priority replacements (spec 2.3 rule 3).</summary>
    ActivitiesReplaced = 6,

    /// <summary>Activities removed by TTL.</summary>
    ActivitiesExpired = 7,

    /// <summary>Activities discarded by stack/queue limits.</summary>
    ActivitiesDropped = 8,

    /// <summary>Activities retracted by their producer.</summary>
    ActivitiesRetracted = 9,

    /// <summary>Layout calculations performed.</summary>
    LayoutComputed = 10,

    /// <summary>Frame-loop iterations.</summary>
    MotionFrames = 11,

    /// <summary>Spring retargets with a direction change (velocity damped).</summary>
    MotionRetargets = 12,

    /// <summary>Springs that reached their at-rest threshold.</summary>
    MotionSettles = 13,

    /// <summary>Settings files loaded successfully.</summary>
    SettingsLoads = 14,

    /// <summary>Settings files saved successfully.</summary>
    SettingsSaves = 15,

    /// <summary>Settings save attempts that failed.</summary>
    SettingsSaveFailures = 16,

    /// <summary>Migration runs (any outcome).</summary>
    MigrationRuns = 17,

    /// <summary>Migration runs that failed.</summary>
    MigrationFailures = 18,

    /// <summary>State-machine transitions that changed the island state kind.</summary>
    StateTransitions = 19,

    /// <summary>Dispatched inputs queued while the island was in <c>Closing</c>.</summary>
    DeferredInputs = 20,
}

/// <summary>Immutable point-in-time view of all diagnostic counters.</summary>
public sealed record DiagnosticCountersSnapshot
{
    /// <summary>Counter values keyed by counter kind.</summary>
    public required IReadOnlyDictionary<CounterKind, long> Values { get; init; }

    /// <summary>Returns the value of <paramref name="kind"/>, or zero when unknown.</summary>
    public long this[CounterKind kind] => Values.TryGetValue(kind, out var value) ? value : 0L;
}

/// <summary>
/// Thread-safe counter set. All mutators use interlocked operations, so producers may
/// increment from arbitrary threads while the UI reads a snapshot.
/// </summary>
public sealed class DiagnosticCounters
{
    private static readonly int CounterCount = Enum.GetValues<CounterKind>().Max(v => (int)v) + 1;
    private readonly long[] _values = new long[CounterCount];

    /// <summary>Increments <paramref name="kind"/> by one.</summary>
    public void Increment(CounterKind kind) => Add(kind, 1);

    /// <summary>Increments <paramref name="kind"/> by <paramref name="delta"/>.</summary>
    public void Add(CounterKind kind, long delta)
    {
        var index = (int)kind;
        if ((uint)index < (uint)_values.Length)
        {
            Interlocked.Add(ref _values[index], delta);
        }
    }

    /// <summary>Reads the current value of <paramref name="kind"/>.</summary>
    public long Get(CounterKind kind)
    {
        var index = (int)kind;
        return (uint)index < (uint)_values.Length ? Interlocked.Read(ref _values[index]) : 0L;
    }

    /// <summary>Sets <paramref name="kind"/> to an absolute value (diagnostics/admin use).</summary>
    public void Set(CounterKind kind, long value)
    {
        var index = (int)kind;
        if ((uint)index < (uint)_values.Length)
        {
            Interlocked.Exchange(ref _values[index], value);
        }
    }

    /// <summary>Returns an immutable snapshot of every counter.</summary>
    public DiagnosticCountersSnapshot Snapshot()
    {
        var builder = ImmutableDictionary.CreateBuilder<CounterKind, long>();
        foreach (var kind in Enum.GetValues<CounterKind>())
        {
            builder[kind] = Get(kind);
        }

        return new DiagnosticCountersSnapshot { Values = builder.ToImmutable() };
    }

    /// <summary>Resets all counters to zero (tests / diagnostics reset).</summary>
    public void Reset()
    {
        for (var i = 0; i < _values.Length; i++)
        {
            Interlocked.Exchange(ref _values[i], 0);
        }
    }
}
