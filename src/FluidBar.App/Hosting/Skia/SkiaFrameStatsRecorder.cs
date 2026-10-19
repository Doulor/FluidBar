using FluidBar.App.Settings;

namespace FluidBar.App.Hosting.Skia;

/// <summary>
/// Bounded rolling frame-time recorder behind the advanced "frame statistics" setting. Recording is
/// opt-in: when disabled <see cref="Record"/> is a single branch and nothing is allocated.
/// </summary>
/// <remarks>
/// The window is a fixed-size ring (no per-frame allocation); percentiles are computed from a copy on
/// the (1 Hz) diagnostics read, never in the render path.
/// </remarks>
public sealed class SkiaFrameStatsRecorder
{
    private const int WindowSize = 256;

    private readonly object _gate = new();
    private readonly double[] _samples = new double[WindowSize];
    private int _count;
    private int _next;
    private double _sum;
    private double _last;
    private long _frames;

    /// <summary>Whether samples are being recorded.</summary>
    public bool Enabled { get; private set; }

    /// <summary>Enables or disables recording; disabling clears the window.</summary>
    public void SetEnabled(bool enabled)
    {
        lock (_gate)
        {
            if (Enabled == enabled)
            {
                return;
            }

            Enabled = enabled;
            _count = 0;
            _next = 0;
            _sum = 0;
            _last = 0;
        }
    }

    /// <summary>Records one frame render time (milliseconds).</summary>
    public void Record(double renderMilliseconds)
    {
        if (!Enabled || !double.IsFinite(renderMilliseconds) || renderMilliseconds < 0)
        {
            return;
        }

        lock (_gate)
        {
            if (_count == WindowSize)
            {
                _sum -= _samples[_next];
            }
            else
            {
                _count++;
            }

            _samples[_next] = renderMilliseconds;
            _sum += renderMilliseconds;
            _last = renderMilliseconds;
            _next = (_next + 1) % WindowSize;
            _frames++;
        }
    }

    /// <summary>Copies the current window into an immutable snapshot.</summary>
    public FrameStatsSnapshot Snapshot()
    {
        lock (_gate)
        {
            if (_count == 0)
            {
                return new FrameStatsSnapshot(Enabled, _frames, 0, 0, _last, 0);
            }

            var copy = new double[_count];
            Array.Copy(_samples, copy, _count);
            Array.Sort(copy);
            var p50 = Percentile(copy, 0.50);
            var p99 = Percentile(copy, 0.99);
            return new FrameStatsSnapshot(Enabled, _frames, p50, p99, _last, _sum / _count);
        }
    }

    private static double Percentile(double[] sorted, double percentile)
    {
        if (sorted.Length == 0)
        {
            return 0;
        }

        var index = (int)Math.Ceiling(percentile * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }
}
