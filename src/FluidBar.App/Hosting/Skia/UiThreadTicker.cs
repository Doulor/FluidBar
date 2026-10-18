using System.Diagnostics;

namespace FluidBar.App.Hosting.Skia;

/// <summary>
/// Small non-blocking ticker that delivers callbacks on the captured synchronization context (the
/// UI thread). It replaces <c>DispatcherTimer</c> so the Skia island path carries no Avalonia types.
/// The underlying <see cref="ITimer"/> never runs host code directly: it posts one callback at a
/// time and drops ticks while a callback is still pending, so a busy UI thread cannot build a
/// catch-up burst.
/// </summary>
public sealed class UiThreadTicker : IDisposable
{
    private readonly SynchronizationContext? _context;
    private readonly TimeProvider _time;
    private ITimer? _timer;
    private Action? _callback;
    private int _pending;
    private int _disposed;

    /// <summary>Creates a ticker bound to the calling thread.</summary>
    /// <param name="timeProvider">Clock used to create the timer (tests inject a virtual clock).</param>
    public UiThreadTicker(TimeProvider? timeProvider = null)
    {
        _time = timeProvider ?? TimeProvider.System;
        _context = SynchronizationContext.Current;
    }

    /// <summary>Whether the ticker is running.</summary>
    public bool IsRunning => _timer is not null;

    /// <summary>Current period.</summary>
    public TimeSpan Period { get; private set; } = TimeSpan.FromMilliseconds(50);

    /// <summary>Sets the period without starting the ticker (remembered for the next <see cref="Start"/>).</summary>
    /// <param name="period">Tick period; clamped to at least 1 ms.</param>
    public void SetPeriod(TimeSpan period)
    {
        if (period < TimeSpan.FromMilliseconds(1))
        {
            period = TimeSpan.FromMilliseconds(1);
        }

        if (Period == period)
        {
            return;
        }

        Period = period;
        if (_timer is not null && _callback is not null)
        {
            var callback = _callback;
            Stop();
            Start(period, callback);
        }
    }

    /// <summary>Starts (or restarts) the ticker with a period; idempotent per period.</summary>
    /// <param name="period">Tick period; clamped to at least 1 ms.</param>
    /// <param name="callback">Callback executed on the dispatcher thread.</param>
    public void Start(TimeSpan period, Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (period < TimeSpan.FromMilliseconds(1))
        {
            period = TimeSpan.FromMilliseconds(1);
        }

        _callback = callback;
        if (_timer is not null && Period == period)
        {
            return;
        }

        // Dispose the old timer without going through Stop(): Stop() clears the callback we just set.
        var previous = _timer;
        _timer = null;
        previous?.Dispose();
        Period = period;
        _timer = _time.CreateTimer(static state => ((UiThreadTicker)state!).OnTimer(), this, period, period);
    }

    /// <summary>Stops the ticker (idempotent).</summary>
    public void Stop()
    {
        var timer = _timer;
        _timer = null;
        _callback = null;
        timer?.Dispose();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Stop();
    }

    private void OnTimer()
    {
        var callback = _callback;
        if (callback is null || Interlocked.Exchange(ref _pending, 1) != 0)
        {
            return;
        }

        if (_context is null)
        {
            Run(callback);
            return;
        }

        _context.Post(static state =>
        {
            var (ticker, action) = ((UiThreadTicker, Action))state!;
            ticker.Run(action);
        }, (this, callback));
    }

    private void Run(Action callback)
    {
        Interlocked.Exchange(ref _pending, 0);
        if (Volatile.Read(ref _disposed) != 0 || !ReferenceEquals(callback, _callback))
        {
            return;
        }

        try
        {
            callback();
        }
        catch (Exception)
        {
            // A host callback must never unwind through the timer thread; the host logs its own
            // failures. Swallow here so one bad tick cannot kill the timer.
        }
    }
}

/// <summary>
/// Frame clock for the Skia island host: a <see cref="UiThreadTicker"/> plus a monotonic
/// <see cref="Stopwatch"/> delta. It never blocks the UI thread (no <c>DwmFlush</c>/waitable-timer
/// wait); the layered host's pacer is configured for stats/contract parity but
/// <c>WaitForNextFrame</c> is deliberately not called from the UI thread.
/// </summary>
public sealed class SkiaFrameClock : FluidBar.Core.Motion.IFrameClock, IDisposable
{
    private readonly UiThreadTicker _ticker;
    private Action<TimeSpan>? _onFrame;
    private long _lastTimestamp;
    private int _disposed;

    /// <summary>Creates the clock bound to the calling thread.</summary>
    /// <param name="timeProvider">Clock used for the ticker (tests inject a virtual clock).</param>
    public SkiaFrameClock(TimeProvider? timeProvider = null) => _ticker = new UiThreadTicker(timeProvider);

    /// <inheritdoc />
    public bool IsRunning => _onFrame is not null;

    /// <summary>Current frame interval.</summary>
    public TimeSpan Interval => _ticker.Period;

    /// <summary>Number of frame callbacks delivered (diagnostics).</summary>
    public long TickCount { get; private set; }

    /// <inheritdoc />
    public void Start(Action<TimeSpan> onFrame)
    {
        ArgumentNullException.ThrowIfNull(onFrame);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        _onFrame = onFrame;
        _lastTimestamp = Stopwatch.GetTimestamp();
        _ticker.Start(_ticker.Period, OnTick);
    }

    /// <summary>Changes the frame interval (60/120 Hz pacing) without stopping the loop.</summary>
    /// <param name="interval">Frame interval; clamped to [1 ms, 100 ms].</param>
    public void SetInterval(TimeSpan interval)
    {
        if (interval < TimeSpan.FromMilliseconds(1))
        {
            interval = TimeSpan.FromMilliseconds(1);
        }
        else if (interval > TimeSpan.FromMilliseconds(100))
        {
            interval = TimeSpan.FromMilliseconds(100);
        }

        _ticker.SetPeriod(interval);
    }

    /// <inheritdoc />
    public void Stop()
    {
        _onFrame = null;
        _ticker.Stop();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _onFrame = null;
        _ticker.Dispose();
    }

    private void OnTick()
    {
        var callback = _onFrame;
        if (callback is null)
        {
            return;
        }

        TickCount++;
        var now = Stopwatch.GetTimestamp();
        var elapsed = _lastTimestamp == 0
            ? TimeSpan.FromMilliseconds(1)
            : TimeSpan.FromSeconds((now - _lastTimestamp) / (double)Stopwatch.Frequency);
        _lastTimestamp = now;
        if (elapsed <= TimeSpan.Zero)
        {
            elapsed = TimeSpan.FromMilliseconds(1);
        }

        callback(elapsed);
    }
}
