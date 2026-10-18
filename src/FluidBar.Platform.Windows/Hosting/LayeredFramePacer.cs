using System.Diagnostics;
using FluidBar.Platform.Windows.Interop;

namespace FluidBar.Platform.Windows.Hosting;

/// <summary>Frame-pacing strategy for a layered host.</summary>
public enum LayeredPacingMode
{
    /// <summary>No pacing: the caller renders as fast as it can (benchmark/spike only).</summary>
    None,

    /// <summary>Block on <c>DwmFlush</c>: the compositor's vblank, no CPU burn, matches the display refresh.</summary>
    Composition,

    /// <summary>Deadline scheduler for an explicit frame rate (60/120 fps) using a high-resolution waitable timer.</summary>
    TargetFrameRate,
}

/// <summary>Pacing counters for one measurement window.</summary>
/// <param name="Frames">Frames paced.</param>
/// <param name="LateFrames">Frames whose deadline had already passed.</param>
/// <param name="MeanIntervalMicroseconds">Mean interval between frames.</param>
/// <param name="P99IntervalMicroseconds">p99 interval between frames.</param>
/// <param name="MaxIntervalMicroseconds">Worst interval between frames.</param>
/// <param name="EffectiveFramesPerSecond">Frames per second over the measurement window.</param>
public readonly record struct LayeredPacingStats(
    long Frames,
    long LateFrames,
    double MeanIntervalMicroseconds,
    double P99IntervalMicroseconds,
    double MaxIntervalMicroseconds,
    double EffectiveFramesPerSecond);

/// <summary>
/// Frame pacing for <see cref="LayeredWindowHost"/>. Three strategies, all allocation-free after
/// construction:
/// <list type="bullet">
/// <item><see cref="LayeredPacingMode.Composition"/>: <c>DwmFlush</c> — blocks until DWM presents, which
/// is the vsync signal for a layered window. This is the production default.</item>
/// <item><see cref="LayeredPacingMode.TargetFrameRate"/>: a deadline clock at N fps, waited on with a
/// high-resolution waitable timer (Win10 1803+) and a short spin for the last sub-millisecond, so a
/// 120 fps loop does not burn a core and does not oversleep by 15.6 ms.</item>
/// <item><see cref="LayeredPacingMode.None"/>: no wait (measurement baseline).</item>
/// </list>
/// </summary>
/// <remarks>
/// The pacer only waits; it never renders, uploads or owns a thread. <see cref="WaitForNextFrame"/>
/// must be called from the host's owning (UI) thread. The pacer never uses <c>Thread.Sleep</c>, whose
/// default 15.6 ms granularity would cap a 120 fps loop at ~64 fps.
/// </remarks>
public sealed class LayeredFramePacer : IDisposable
{
    private const int IntervalSampleCapacity = 4096;
    private static readonly long MinimumWaitTicks = Stopwatch.Frequency / 2000; // 0.5 ms

    private readonly LayeredPacingMode _mode;
    private readonly double _targetFramesPerSecond;
    private readonly long _framePeriodTicks;
    private readonly long[] _intervals = new long[IntervalSampleCapacity];
    private IntPtr _timer;
    private bool _timerUnavailable;
    private long _startTicks;
    private long _frameIndex;
    private long _lastFrameTicks;
    private long _lateFrames;
    private long _intervalCount;
    private long _intervalTotalTicks;
    private long _maxIntervalTicks;
    private int _disposed;

    /// <summary>Creates a pacer.</summary>
    /// <param name="mode">Pacing strategy.</param>
    /// <param name="targetFramesPerSecond">Target rate for <see cref="LayeredPacingMode.TargetFrameRate"/>.</param>
    public LayeredFramePacer(LayeredPacingMode mode, double targetFramesPerSecond = 60)
    {
        if (mode == LayeredPacingMode.TargetFrameRate)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(targetFramesPerSecond, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(targetFramesPerSecond, 1000);
        }

        _mode = mode;
        _targetFramesPerSecond = mode == LayeredPacingMode.TargetFrameRate ? targetFramesPerSecond : 0;
        _framePeriodTicks = mode == LayeredPacingMode.TargetFrameRate
            ? Math.Max(1, (long)Math.Round(Stopwatch.Frequency / targetFramesPerSecond))
            : 0;
        Reset();
    }

    /// <summary>Pacing strategy.</summary>
    public LayeredPacingMode Mode => _mode;

    /// <summary>Target frame rate (0 unless <see cref="LayeredPacingMode.TargetFrameRate"/>).</summary>
    public double TargetFramesPerSecond => _targetFramesPerSecond;

    /// <summary>Nominal frame period in milliseconds (0 for non-deadline modes).</summary>
    public double FramePeriodMilliseconds => _framePeriodTicks == 0 ? 0 : 1000.0 / _targetFramesPerSecond;

    /// <summary>True when the high-resolution waitable timer is in use (false = spin fallback).</summary>
    public bool IsHighResolutionTimerAvailable => _timer != IntPtr.Zero;

    /// <summary>
    /// Waits until the next frame is due. Returns immediately (and counts a late frame) when the
    /// deadline has already passed, so a slow frame never accumulates a burst of catch-up frames.
    /// </summary>
    public void WaitForNextFrame()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        switch (_mode)
        {
            case LayeredPacingMode.None:
                return;

            case LayeredPacingMode.Composition:
                // DwmFlush blocks until the next composition pass; it is the layered window's vsync.
                // A non-zero HRESULT means composition is unavailable (remote session, DWM off):
                // fall through to a short yield instead of spinning.
                if (NativeMethods.DwmApi.DwmFlush() != 0)
                {
                    Thread.Yield();
                }

                return;

            case LayeredPacingMode.TargetFrameRate:
                WaitForDeadline();
                return;

            default:
                return;
        }
    }

    /// <summary>Resets the deadline clock and all counters (call after a resize/mode change).</summary>
    public void Reset()
    {
        _startTicks = Stopwatch.GetTimestamp();
        _lastFrameTicks = _startTicks;
        _frameIndex = 0;
        _lateFrames = 0;
        _intervalCount = 0;
        _intervalTotalTicks = 0;
        _maxIntervalTicks = 0;
    }

    /// <summary>Returns counters for the window since <see cref="Reset"/> and clears the interval samples.</summary>
    public LayeredPacingStats GetStats()
    {
        var frames = _frameIndex;
        var elapsedTicks = Stopwatch.GetTimestamp() - _startTicks;
        var meanTicks = _intervalCount == 0 ? 0 : _intervalTotalTicks / (double)_intervalCount;
        var p99 = PercentileTicks(0.99);
        var effectiveFps = elapsedTicks <= 0 ? 0 : frames * (double)Stopwatch.Frequency / elapsedTicks;
        return new LayeredPacingStats(
            frames,
            _lateFrames,
            TicksToMicroseconds(meanTicks),
            TicksToMicroseconds(p99),
            TicksToMicroseconds(_maxIntervalTicks),
            effectiveFps);
    }

    private void WaitForDeadline()
    {
        var now = Stopwatch.GetTimestamp();
        if (_frameIndex == 0)
        {
            _startTicks = now;
        }

        _frameIndex++;
        var deadline = _startTicks + (_frameIndex * _framePeriodTicks);
        var remaining = deadline - now;
        if (remaining <= 0)
        {
            _lateFrames++;
            RecordInterval(now);
            return;
        }

        if (remaining > MinimumWaitTicks)
        {
            WaitOnTimer(remaining);
        }

        // Spin the last fraction of a millisecond so the frame lands on the deadline rather than
        // after a scheduler quantum. This is bounded to <1 ms and only for deadline pacing.
        while (Stopwatch.GetTimestamp() < deadline)
        {
            Thread.SpinWait(64);
        }

        RecordInterval(Stopwatch.GetTimestamp());
    }

    private void WaitOnTimer(long remainingTicks)
    {
        if (_timerUnavailable)
        {
            Thread.Yield();
            return;
        }

        if (_timer == IntPtr.Zero && !TryCreateTimer())
        {
            _timerUnavailable = true;
            Thread.Yield();
            return;
        }

        // SetWaitableTimer takes a negative relative due time in 100 ns units.
        var dueTime = -(remainingTicks * 10_000_000L / Stopwatch.Frequency);
        if (dueTime >= 0)
        {
            return;
        }

        if (!NativeMethods.Kernel32.SetWaitableTimer(_timer, ref dueTime, 0, IntPtr.Zero, IntPtr.Zero, false))
        {
            _timerUnavailable = true;
            return;
        }

        if (NativeMethods.Kernel32.WaitForSingleObject(_timer, NativeMethods.INFINITE) != NativeMethods.WAIT_OBJECT_0)
        {
            // The timer was abandoned or the wait failed: degrade to yielding rather than spinning.
            _timerUnavailable = true;
        }
    }

    private bool TryCreateTimer()
    {
        _timer = NativeMethods.Kernel32.CreateWaitableTimerExW(IntPtr.Zero, null,
            NativeMethods.CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, NativeMethods.TIMER_ALL_ACCESS);
        return _timer != IntPtr.Zero;
    }

    private void RecordInterval(long nowTicks)
    {
        var interval = nowTicks - _lastFrameTicks;
        _lastFrameTicks = nowTicks;
        if (interval <= 0)
        {
            return;
        }

        _intervalTotalTicks += interval;
        _maxIntervalTicks = Math.Max(_maxIntervalTicks, interval);
        if (_intervalCount < IntervalSampleCapacity)
        {
            _intervals[_intervalCount] = interval;
        }

        _intervalCount++;
    }

    private double PercentileTicks(double percentile)
    {
        var count = (int)Math.Min(_intervalCount, IntervalSampleCapacity);
        if (count == 0)
        {
            return 0;
        }

        // Sort in place: GetStats ends a measurement window and Reset() discards the samples, so
        // mutating the buffer here keeps the diagnostics path allocation-free.
        var samples = _intervals.AsSpan(0, count);
        samples.Sort();
        var index = Math.Clamp((int)Math.Ceiling(percentile * count) - 1, 0, count - 1);
        return samples[index];
    }

    private static double TicksToMicroseconds(double ticks) => ticks * 1_000_000.0 / Stopwatch.Frequency;

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_timer != IntPtr.Zero)
        {
            NativeMethods.Kernel32.CloseHandle(_timer);
            _timer = IntPtr.Zero;
        }
    }
}
