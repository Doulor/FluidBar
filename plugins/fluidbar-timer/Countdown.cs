namespace FluidBar.Plugins.Timer;

public readonly record struct CountdownSnapshot(TimeSpan Remaining, bool Running, bool Completed, double Progress);

/// <summary>Monotonic countdown state, independent of rendering and wall-clock corrections.</summary>
public sealed class Countdown(TimeProvider time)
{
    private readonly object _gate = new();
    private TimeSpan _remaining;
    private TimeSpan _duration;
    private long _started;
    private bool _running;
    private bool _completed;

    public void Start(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero || duration > TimeSpan.FromMinutes(180))
            throw new ArgumentOutOfRangeException(nameof(duration));
        lock (_gate)
        {
            _duration = _remaining = duration;
            _started = time.GetTimestamp();
            _running = true;
            _completed = false;
        }
    }

    public void Pause()
    {
        lock (_gate)
        {
            var snapshot = ReadCore();
            _remaining = snapshot.Remaining;
            _running = false;
        }
    }

    public void Resume()
    {
        lock (_gate)
        {
            if (_running || _remaining <= TimeSpan.Zero) return;
            _started = time.GetTimestamp();
            _running = true;
        }
    }

    public void Stop()
    {
        lock (_gate) { _remaining = _duration = TimeSpan.Zero; _running = _completed = false; }
    }

    public CountdownSnapshot Read() { lock (_gate) return ReadCore(); }

    private CountdownSnapshot ReadCore()
    {
        var remaining = _running ? _remaining - time.GetElapsedTime(_started, time.GetTimestamp()) : _remaining;
        if (_running && remaining <= TimeSpan.Zero)
        {
            remaining = _remaining = TimeSpan.Zero;
            _running = false;
            _completed = true;
        }
        return new(remaining, _running, _completed,
            _duration > TimeSpan.Zero ? Math.Clamp(1 - remaining.TotalSeconds / _duration.TotalSeconds, 0, 1) : 0);
    }
}
