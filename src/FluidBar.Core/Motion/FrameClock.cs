using FluidBar.Core.Diagnostics;

namespace FluidBar.Core.Motion;

/// <summary>
/// Frame-callback abstraction (spec 4.4): production uses the composition frame callback
/// (Avalonia compositor); the fallback is a render-priority dispatcher timer. Both must stop
/// requesting frames as soon as everything is at rest (idle = 0 frame callbacks).
/// </summary>
public interface IFrameClock
{
    /// <summary>Whether frames are currently being requested.</summary>
    bool IsRunning { get; }

    /// <summary>Starts frame callbacks with the elapsed time since the previous frame.</summary>
    void Start(Action<TimeSpan> onFrame);

    /// <summary>Stops frame callbacks. Idempotent.</summary>
    void Stop();
}

/// <summary>
/// Deterministic frame clock for tests, headless hosts and the M0 spike harness.
/// Time only advances when the caller pumps it, so motion tests are fully reproducible.
/// </summary>
public sealed class ManualFrameClock : IFrameClock
{
    private Action<TimeSpan>? _onFrame;

    /// <inheritdoc />
    public bool IsRunning => _onFrame is not null;

    /// <summary>Number of frames delivered since the clock was last started.</summary>
    public int FrameCount { get; private set; }

    /// <inheritdoc />
    public void Start(Action<TimeSpan> onFrame)
    {
        ArgumentNullException.ThrowIfNull(onFrame);
        _onFrame = onFrame;
        FrameCount = 0;
    }

    /// <inheritdoc />
    public void Stop() => _onFrame = null;

    /// <summary>Delivers one frame with the supplied delta; returns whether a callback ran.</summary>
    public bool Pump(TimeSpan delta)
    {
        var callback = _onFrame;
        if (callback is null)
        {
            return false;
        }

        FrameCount++;
        callback(delta);
        return true;
    }

    /// <summary>Pumps <paramref name="frameCount"/> frames of <paramref name="delta"/> each.</summary>
    public int Pump(int frameCount, TimeSpan delta)
    {
        var delivered = 0;
        for (var i = 0; i < frameCount; i++)
        {
            if (!Pump(delta))
            {
                break;
            }

            delivered++;
        }

        return delivered;
    }
}

/// <summary>
/// Drives a <see cref="SpringSet"/> from an <see cref="IFrameClock"/>: steps all springs once per
/// frame, raises <see cref="Stepped"/>, and stops the clock automatically when
/// <see cref="SpringSet.AllAtRest"/> becomes true (spec 4.4 "settle -&gt; cancel the frame request").
/// </summary>
public sealed class MotionLoop : IDisposable
{
    private readonly IFrameClock _clock;
    private readonly SpringSet _springs;
    private readonly DiagnosticCounters? _counters;
    private bool _disposed;

    /// <summary>Creates a motion loop.</summary>
    /// <param name="clock">Frame source (composition callback, dispatcher timer, manual clock).</param>
    /// <param name="springs">Optional spring set; a new one is created when omitted.</param>
    /// <param name="counters">Optional diagnostics counters.</param>
    public MotionLoop(IFrameClock clock, SpringSet? springs = null, DiagnosticCounters? counters = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
        _springs = springs ?? new SpringSet();
        _counters = counters;
    }

    /// <summary>Raised after every frame with the frame delta.</summary>
    public event Action<TimeSpan>? Stepped;

    /// <summary>The springs stepped by this loop.</summary>
    public SpringSet Springs => _springs;

    /// <summary>Whether the loop is requesting frames.</summary>
    public bool IsRunning => !_disposed && _clock.IsRunning;

    /// <summary>Starts requesting frames. Idempotent.</summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_clock.IsRunning)
        {
            return;
        }

        _clock.Start(OnFrame);
    }

    /// <summary>Stops requesting frames. Idempotent.</summary>
    public void Stop()
    {
        if (!_disposed)
        {
            _clock.Stop();
        }
    }

    /// <summary>
    /// Steps the spring set once with <paramref name="delta"/>. Returns whether motion continues
    /// (i.e. not all springs are at rest); stops the clock when everything settled.
    /// </summary>
    public bool Step(TimeSpan delta)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (delta <= TimeSpan.Zero)
        {
            return _springs.ActiveCount > 0;
        }

        _springs.Step(delta.TotalSeconds);
        _counters?.Increment(CounterKind.MotionFrames);
        Stepped?.Invoke(delta);
        if (_springs.AllAtRest)
        {
            _counters?.Add(CounterKind.MotionSettles, 1);
            _clock.Stop();
            return false;
        }

        return true;
    }

    private void OnFrame(TimeSpan delta) => Step(delta);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _clock.Stop();
    }
}
