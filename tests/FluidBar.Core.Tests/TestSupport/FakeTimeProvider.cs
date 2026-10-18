namespace FluidBar.Core.Tests.TestSupport;

/// <summary>Deterministic <see cref="TimeProvider"/> for tests; time only moves when advanced.</summary>
public sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;
    private long _timestamp;

    /// <summary>Starts the clock at <paramref name="start"/>.</summary>
    public FakeTimeProvider(DateTimeOffset start)
    {
        _utcNow = start;
    }

    /// <summary>Creates a clock at the canonical test instant (2026-10-18T12:00:00Z).</summary>
    public static FakeTimeProvider StartingAtTestEpoch() => new(TestData.Now);

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => _utcNow;

    /// <inheritdoc />
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    /// <inheritdoc />
    public override long GetTimestamp() => _timestamp;

    /// <summary>Current fake time.</summary>
    public DateTimeOffset UtcNow => _utcNow;

    /// <summary>Advances the fake clock.</summary>
    public void Advance(TimeSpan delta)
    {
        _utcNow += delta;
        _timestamp += delta.Ticks;
    }

    /// <summary>Advances the fake clock by milliseconds.</summary>
    public void AdvanceMs(int milliseconds) => Advance(TimeSpan.FromMilliseconds(milliseconds));
}
