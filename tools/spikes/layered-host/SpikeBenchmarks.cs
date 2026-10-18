using System.Diagnostics;
using FluidBar.Platform.Windows.Hosting;

namespace FluidBar.Spikes.LayeredHost;

/// <summary>Distribution of a per-frame measurement, in microseconds.</summary>
internal readonly record struct TimingStats(
    string Name,
    int Count,
    double MeanUs,
    double P50Us,
    double P95Us,
    double P99Us,
    double MaxUs)
{
    /// <summary>One-line form used in the log and the results table.</summary>
    public string ToRow() => $"{Name,-28} n={Count,4}  mean={MeanUs,8:0.0}  p50={P50Us,8:0.0}  p95={P95Us,8:0.0}  " +
        $"p99={P99Us,8:0.0}  max={MaxUs,8:0.0} us";
}

/// <summary>Result of a frame loop (full-frame or dirty-rect).</summary>
internal readonly record struct FrameLoopResult(
    TimingStats Upload,
    TimingStats Frame,
    double AllocatedBytesPerFrame,
    double CpuPercentOfOneCore,
    double FramesPerSecond,
    int FallbackUpdates,
    bool AllUpdatesSucceeded,
    int LastWin32Error);

/// <summary>Result of a paced loop.</summary>
internal readonly record struct PacingResult(
    LayeredPacingMode Mode,
    double TargetFps,
    double EffectiveFps,
    double MeanIntervalMs,
    double P99IntervalMs,
    double MaxIntervalMs,
    long LateFrames,
    double UploadMeanUs);

/// <summary>Frame-loop and pacing measurements. Every buffer is pre-allocated before the loop, so the
/// allocation counter measures the host + renderer only.</summary>
internal static class SpikeBenchmarks
{
    private const int WarmupFrames = 24;

    /// <summary>Full-frame loop: redraw everything and upload the whole surface every frame.</summary>
    public static FrameLoopResult RunFullFrameLoop(LayeredWindowHost host, IslandRenderer renderer,
        LayeredHostMode mode, int frames)
        => RunLoop(host, renderer, mode, frames, fullFrame: true);

    /// <summary>Dirty-rect loop: two spatially separated dirty rects per frame.</summary>
    public static FrameLoopResult RunDirtyLoop(LayeredWindowHost host, IslandRenderer renderer,
        LayeredHostMode mode, int frames)
        => RunLoop(host, renderer, mode, frames, fullFrame: false);

    private static FrameLoopResult RunLoop(LayeredWindowHost host, IslandRenderer renderer, LayeredHostMode mode,
        int frames, bool fullFrame)
    {
        for (var i = 0; i < WarmupFrames; i++)
        {
            DrawOnce(host, renderer, mode, i * 0.016, fullFrame);
        }

        var upload = new double[frames];
        var total = new double[frames];
        var fallbacks = 0;
        var allOk = true;
        var lastError = 0;

        var allocatedBefore = GC.GetTotalAllocatedBytes(true);
        var cpuBefore = Process.GetCurrentProcess().TotalProcessorTime;
        var started = Stopwatch.GetTimestamp();

        for (var i = 0; i < frames; i++)
        {
            var frameStart = Stopwatch.GetTimestamp();
            var commit = DrawOnce(host, renderer, mode, i * 0.016, fullFrame);
            var frameEnd = Stopwatch.GetTimestamp();

            upload[i] = commit.UpdateMicroseconds;
            total[i] = (frameEnd - frameStart) * 1_000_000.0 / Stopwatch.Frequency;
            if (commit.UsedFallback)
            {
                fallbacks++;
            }

            allOk &= commit.Updated;
            if (!commit.Updated)
            {
                lastError = commit.Win32Error;
            }
        }

        var elapsed = Stopwatch.GetTimestamp() - started;
        var allocatedAfter = GC.GetTotalAllocatedBytes(true);
        var cpuAfter = Process.GetCurrentProcess().TotalProcessorTime;

        var wallMs = elapsed * 1000.0 / Stopwatch.Frequency;
        return new FrameLoopResult(
            Describe(fullFrame ? "ULW full frame" : "ULW dirty rects", upload, frames),
            Describe(fullFrame ? "frame total (full)" : "frame total (dirty)", total, frames),
            (allocatedAfter - allocatedBefore) / (double)frames,
            wallMs <= 0 ? 0 : (cpuAfter - cpuBefore).TotalMilliseconds / wallMs * 100.0,
            frames * Stopwatch.Frequency / (double)elapsed,
            fallbacks,
            allOk,
            lastError);
    }

    /// <summary>Paced loop: waits for the pacer, draws a dirty frame, commits.</summary>
    public static PacingResult RunPacedLoop(LayeredWindowHost host, IslandRenderer renderer, LayeredHostMode mode,
        LayeredPacingMode pacing, double targetFps, int frames)
    {
        host.ConfigurePacing(pacing, targetFps);
        var pacer = host.Pacer ?? throw new InvalidOperationException("Host has no pacer.");

        for (var i = 0; i < WarmupFrames; i++)
        {
            host.WaitForNextFrame();
            DrawOnce(host, renderer, mode, i * 0.016, fullFrame: false);
        }

        pacer.Reset();
        var uploadSum = 0.0;
        var started = Stopwatch.GetTimestamp();
        for (var i = 0; i < frames; i++)
        {
            host.WaitForNextFrame();
            var commit = DrawOnce(host, renderer, mode, i * 0.016, fullFrame: false);
            uploadSum += commit.UpdateMicroseconds;
        }

        var elapsed = Stopwatch.GetTimestamp() - started;
        var stats = pacer.GetStats();
        var effectiveFps = elapsed <= 0 ? 0 : frames * Stopwatch.Frequency / (double)elapsed;
        return new PacingResult(
            pacing,
            targetFps,
            effectiveFps,
            stats.MeanIntervalMicroseconds / 1000.0,
            stats.P99IntervalMicroseconds / 1000.0,
            stats.MaxIntervalMicroseconds / 1000.0,
            stats.LateFrames,
            uploadSum / frames);
    }

    private static LayeredCommitResult DrawOnce(LayeredWindowHost host, IslandRenderer renderer,
        LayeredHostMode mode, double seconds, bool fullFrame)
    {
        var frame = host.BeginFrame();
        if (fullFrame)
        {
            renderer.RenderFull(frame, mode, seconds);
            return host.CommitFrame();
        }

        Span<LayeredRect> dirty = stackalloc LayeredRect[2];
        var count = renderer.RenderDynamic(frame, mode, seconds, dirty);
        return host.CommitFrame(dirty[..count]);
    }

    /// <summary>Builds a stats record from raw microsecond samples (sorted copy, no allocation per frame).</summary>
    public static TimingStats Describe(string name, double[] samples, int count)
    {
        if (count <= 0)
        {
            return new TimingStats(name, 0, 0, 0, 0, 0, 0);
        }

        var sorted = samples.AsSpan(0, count).ToArray();
        Array.Sort(sorted);
        var sum = 0.0;
        foreach (var sample in sorted)
        {
            sum += sample;
        }

        return new TimingStats(
            name,
            count,
            sum / count,
            Percentile(sorted, 0.50),
            Percentile(sorted, 0.95),
            Percentile(sorted, 0.99),
            sorted[^1]);
    }

    private static double Percentile(double[] sorted, double percentile)
    {
        var index = Math.Clamp((int)Math.Ceiling(percentile * sorted.Length) - 1, 0, sorted.Length - 1);
        return sorted[index];
    }
}
