using System.Diagnostics;
using System.Runtime.InteropServices;
using FluidBar.Core.Diagnostics;
using FluidBar.Island;

namespace FluidBar.App.Hosting.Skia;

/// <summary>
/// Opt-in performance probe for the Skia island host. It records frame intervals, render times,
/// managed allocations, process memory and renderer cache accounting, then logs one compact
/// `island.skia.perf` line per second (plus `island.skia.first-frame` once).
/// <para>
/// Disabled (the default) every entry point is a single static bool check, so production behaviour is
/// unchanged; enable with `FLUIDBAR_PERF_PROBE=1` when measuring. Bucket histograms are fixed-size:
/// recording a frame allocates nothing.
/// </para>
/// </summary>
public sealed class SkiaHostPerfProbe
{
    /// <summary>Environment variable that enables the probe (`1`/`true`).</summary>
    public const string EnableEnvironmentVariable = "FLUIDBAR_PERF_PROBE";

    /// <summary>Frame interval considered a dropped frame for the 60 Hz target (21 ms).</summary>
    public const double DroppedFrameThresholdMs = 21.0;

    private const int Buckets = 400; // 0.5 ms per bucket → 200 ms range
    private const double BucketMs = 0.5;

    private readonly IAppLogger _logger;
    private readonly IslandRenderer _renderer;
    private readonly Func<int> _windowCount;
    private readonly Func<long> _surfaceBytes;
    private readonly TimeProvider _time;

    private readonly int[] _intervalBuckets = new int[Buckets];
    private readonly int[] _renderBuckets = new int[Buckets];
    private readonly int[] _activeIntervalBuckets = new int[Buckets];
    private long _activeFrames;

    private long _startTimestamp;
    private long _hostStartTimestamp;
    private long _lastDumpTimestamp;
    private long _lastFrameTimestamp;
    private long _frames;
    private long _fullFrames;
    private long _dirtyFrames;
    private long _dropped;
    private double _intervalSumMs;
    private double _intervalMaxMs;
    private double _renderSumMs;
    private double _renderMaxMs;
    private long _allocatedAtStart;
    private long _framesAtLastDump;
    private long _allocatedAtLastDump;
    private long _allocAdvanceWindow;
    private long _allocRenderWindow;
    private int _dumpCount;
    private bool _firstFrameLogged;

    /// <summary>Creates a probe bound to a host. Call only when <see cref="IsEnabled"/> is true.</summary>
    /// <param name="logger">Structured logger (probe lines are Information/Render).</param>
    /// <param name="renderer">Renderer whose caches are reported.</param>
    /// <param name="windowCount">Live layered-window count.</param>
    /// <param name="surfaceBytes">Retained DIB bytes across windows.</param>
    /// <param name="time">Clock used for timestamps.</param>
    public SkiaHostPerfProbe(IAppLogger logger, IslandRenderer renderer, Func<int> windowCount,
        Func<long> surfaceBytes, TimeProvider time)
    {
        _logger = logger;
        _renderer = renderer;
        _windowCount = windowCount;
        _surfaceBytes = surfaceBytes;
        _time = time;
        _startTimestamp = Stopwatch.GetTimestamp();
        _hostStartTimestamp = _startTimestamp;
        _lastDumpTimestamp = _startTimestamp;
        _allocatedAtStart = GC.GetTotalAllocatedBytes(precise: false);
        _allocatedAtLastDump = _allocatedAtStart;
    }

    /// <summary>Whether `FLUIDBAR_PERF_PROBE` asks for measurement.</summary>
    public static bool IsEnabled => IsEnabledToken(Environment.GetEnvironmentVariable(EnableEnvironmentVariable));

    /// <summary>Total frames recorded.</summary>
    public long Frames => Interlocked.Read(ref _frames);

    /// <summary>Frames recorded with a full-surface change.</summary>
    public long FullFrames => Interlocked.Read(ref _fullFrames);

    /// <summary>Frames recorded with a damage-rectangle change.</summary>
    public long DirtyFrames => Interlocked.Read(ref _dirtyFrames);

    /// <summary>Frames whose interval exceeded <see cref="DroppedFrameThresholdMs"/>.</summary>
    public long DroppedFrames => Interlocked.Read(ref _dropped);

    /// <summary>Frames that were part of a continuous animation run (interval ≤ 100 ms).</summary>
    public long ActiveFrames => Interlocked.Read(ref _activeFrames);

    /// <summary>Managed bytes allocated since the probe was created.</summary>
    public long AllocatedBytes => GC.GetTotalAllocatedBytes(precise: false) - _allocatedAtStart;

    /// <summary>Marks the moment the host started creating windows (for the startup split).</summary>
    public void MarkHostStarted()
    {
        _hostStartTimestamp = Stopwatch.GetTimestamp();
        _startTimestamp = _hostStartTimestamp;
        _lastDumpTimestamp = _hostStartTimestamp;
    }

    /// <summary>Records one rendered frame. Allocations: none.</summary>
    /// <param name="renderMs">Wall time spent rendering/uploading the frame.</param>
    /// <param name="full">Whether the frame repainted the whole surface.</param>
    /// <param name="allocAdvanceBytes">Managed bytes allocated inside the controller advance.</param>
    /// <param name="allocRenderBytes">Managed bytes allocated inside the render/commit path.</param>
    public void RecordFrame(double renderMs, bool full, long allocAdvanceBytes = 0, long allocRenderBytes = 0)
    {
        var now = Stopwatch.GetTimestamp();
        var intervalMs = _lastFrameTimestamp == 0
            ? 0
            : (now - _lastFrameTimestamp) * 1000.0 / Stopwatch.Frequency;
        _lastFrameTimestamp = now;
        RecordSample(intervalMs, renderMs, full, allocAdvanceBytes, allocRenderBytes);
    }

    /// <summary>
    /// Records one frame sample with an explicit interval. The host uses <see cref="RecordFrame"/>;
    /// tests feed deterministic intervals so the percentile math is verifiable without a clock.
    /// </summary>
    public void RecordSample(double intervalMs, double renderMs, bool full, long allocAdvanceBytes = 0,
        long allocRenderBytes = 0)
    {
        Interlocked.Add(ref _allocAdvanceWindow, allocAdvanceBytes);
        Interlocked.Add(ref _allocRenderWindow, allocRenderBytes);

        Interlocked.Increment(ref _frames);
        if (full)
        {
            Interlocked.Increment(ref _fullFrames);
        }
        else
        {
            Interlocked.Increment(ref _dirtyFrames);
        }

        if (intervalMs > 0)
        {
            if (intervalMs > DroppedFrameThresholdMs)
            {
                Interlocked.Increment(ref _dropped);
            }

            _intervalSumMs += intervalMs;
            _intervalMaxMs = Math.Max(_intervalMaxMs, intervalMs);
            _intervalBuckets[Bucket(intervalMs)]++;
            if (intervalMs <= 100)
            {
                // A continuous animation run: idle gaps between bursts (200 ms+) are excluded so the
                // animation p99 reflects frames that were actually requested back-to-back.
                _activeIntervalBuckets[Bucket(intervalMs)]++;
                _activeFrames++;
            }
        }

        _renderSumMs += renderMs;
        _renderMaxMs = Math.Max(_renderMaxMs, renderMs);
        _renderBuckets[Bucket(renderMs)]++;

        if (!_firstFrameLogged)
        {
            _firstFrameLogged = true;
            LogFirstFrame(Stopwatch.GetTimestamp());
        }
    }

    /// <summary>Logs one aggregated probe line (expected to be called about once per second).</summary>
    public void Dump()
    {
        var now = Stopwatch.GetTimestamp();
        var elapsedMs = (now - _lastDumpTimestamp) * 1000.0 / Stopwatch.Frequency;
        var elapsedSeconds = elapsedMs <= 0 ? 0 : elapsedMs / 1000.0;
        var frames = Interlocked.Read(ref _frames);
        var framesSinceDump = frames - _framesAtLastDump;
        var allocated = GC.GetTotalAllocatedBytes(precise: false);
        var allocatedSinceDump = allocated - _allocatedAtLastDump;
        var uptimeMs = (now - _hostStartTimestamp) * 1000.0 / Stopwatch.Frequency;
        var stats = _renderer.ResourceStats;
        var gc = GC.GetGCMemoryInfo();
        var memory = ProcessMemorySample.Sample();

        var data = new Dictionary<string, object?>
        {
            ["dump"] = ++_dumpCount,
            ["uptimeMs"] = Math.Round(uptimeMs, 1),
            ["windowSec"] = Math.Round(elapsedSeconds, 3),
            ["frames"] = frames,
            ["framesInWindow"] = framesSinceDump,
            ["redrawsPerSec"] = elapsedSeconds <= 0 ? 0 : Math.Round(framesSinceDump / elapsedSeconds, 2),
            ["fullFrames"] = Interlocked.Read(ref _fullFrames),
            ["dirtyFrames"] = Interlocked.Read(ref _dirtyFrames),
            ["droppedFrames"] = Interlocked.Read(ref _dropped),
            ["intervalP50Ms"] = Percentile(_intervalBuckets, 0.50),
            ["intervalP95Ms"] = Percentile(_intervalBuckets, 0.95),
            ["intervalP99Ms"] = Percentile(_intervalBuckets, 0.99),
            ["intervalMaxMs"] = Math.Round(_intervalMaxMs, 2),
            ["intervalMeanMs"] = frames == 0 ? 0 : Math.Round(_intervalSumMs / frames, 2),
            ["activeFrames"] = Interlocked.Read(ref _activeFrames),
            ["activeIntervalP50Ms"] = Percentile(_activeIntervalBuckets, 0.50),
            ["activeIntervalP95Ms"] = Percentile(_activeIntervalBuckets, 0.95),
            ["activeIntervalP99Ms"] = Percentile(_activeIntervalBuckets, 0.99),
            ["renderP50Ms"] = Percentile(_renderBuckets, 0.50),
            ["renderP95Ms"] = Percentile(_renderBuckets, 0.95),
            ["renderP99Ms"] = Percentile(_renderBuckets, 0.99),
            ["renderMaxMs"] = Math.Round(_renderMaxMs, 2),
            ["renderMeanMs"] = frames == 0 ? 0 : Math.Round(_renderSumMs / frames, 3),
            ["allocTotalBytes"] = allocated - _allocatedAtStart,
            ["allocWindowBytes"] = allocatedSinceDump,
            ["allocPerFrameBytes"] = framesSinceDump == 0 ? 0 : allocatedSinceDump / framesSinceDump,
            ["allocAdvanceBytes"] = Interlocked.Read(ref _allocAdvanceWindow),
            ["allocRenderBytes"] = Interlocked.Read(ref _allocRenderWindow),
            ["allocOtherBytes"] = Math.Max(0, allocatedSinceDump
                - Interlocked.Read(ref _allocAdvanceWindow)
                - Interlocked.Read(ref _allocRenderWindow)),
            ["gen0"] = GC.CollectionCount(0),
            ["gen1"] = GC.CollectionCount(1),
            ["gen2"] = GC.CollectionCount(2),
            ["managedHeapBytes"] = GC.GetTotalMemory(forceFullCollection: false),
            ["managedCommittedBytes"] = gc.TotalCommittedBytes,
            ["workingSetBytes"] = memory.WorkingSet,
            ["privateBytes"] = memory.PrivateBytes,
            ["handles"] = memory.Handles,
            ["windows"] = _windowCount(),
            ["surfaceBytes"] = _surfaceBytes(),
            ["shadowImages"] = stats.ShadowCacheCount,
            ["shadowBytes"] = stats.ShadowPixelBytes,
            ["shadowBuilds"] = stats.ShadowBuilds,
            ["liquidGlassImages"] = stats.LiquidGlassOverlayCount,
            ["liquidGlassBytes"] = stats.LiquidGlassOverlayBytes,
            ["artworkImages"] = stats.ArtworkCount,
            ["artworkBytes"] = stats.ArtworkPixelBytes,
            ["artworkBuilds"] = stats.ArtworkBuilds,
            ["pathCache"] = stats.PathCacheCount,
            ["pathBuilds"] = stats.PathBuilds,
            ["textCache"] = stats.TextCacheCount,
            ["textCacheBytes"] = stats.TextApproxBytes,
        };

        _logger.Information(LogCategories.Render, "island.skia.perf", "Island performance sample.", data: data);
        if (_dumpCount == 2)
        {
            LogMemoryBreakdown(memory);
        }
        _lastDumpTimestamp = now;
        _framesAtLastDump = frames;
        _allocatedAtLastDump = allocated;
        Interlocked.Exchange(ref _allocAdvanceWindow, 0);
        Interlocked.Exchange(ref _allocRenderWindow, 0);
    }

    /// <summary>Cheap process memory/handle counters (`GetProcessMemoryInfo` instead of Process.Refresh).</summary>
    private readonly record struct ProcessMemorySample(long WorkingSet, long PrivateBytes, int Handles)
    {
        public static ProcessMemorySample Sample()
        {
            var counters = new ProcessMemoryCounters { Size = (uint)Marshal.SizeOf<ProcessMemoryCounters>() };
            GetProcessMemoryInfo(GetCurrentProcess(), ref counters, counters.Size);
            GetProcessHandleCount(GetCurrentProcess(), out var handles);
            return new ProcessMemorySample(
                (long)counters.WorkingSetSize.ToUInt64(),
                (long)counters.PrivateUsage.ToUInt64(),
                handles);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessMemoryCounters
    {
        public uint Size;
        public uint PageFaultCount;
        public UIntPtr PeakWorkingSetSize;
        public UIntPtr WorkingSetSize;
        public UIntPtr QuotaPeakPagedPoolUsage;
        public UIntPtr QuotaPagedPoolUsage;
        public UIntPtr QuotaPeakNonPagedPoolUsage;
        public UIntPtr QuotaNonPagedPoolUsage;
        public UIntPtr PagefileUsage;
        public UIntPtr PeakPagefileUsage;
        public UIntPtr PrivateUsage;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool GetProcessMemoryInfo(IntPtr process, ref ProcessMemoryCounters counters, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessHandleCount(IntPtr process, out int handleCount);

    /// <summary>Cheap managed-bytes read used for allocation attribution around host phases.</summary>
    public static long CurrentAllocatedBytes() => GC.GetTotalAllocatedBytes(precise: false);

    /// <summary>
    /// One-shot working-set decomposition: loaded module working set (runtime/Skia/Avalonia/plugins),
    /// managed heap + committed GC memory, thread count, and the remainder that native Skia allocations
    /// (glyph cache, surfaces, shadow images) and unmanaged heaps occupy.
    /// </summary>
    private void LogMemoryBreakdown(ProcessMemorySample memory)
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            long moduleImageBytes = 0;
            var parts = new List<(string Name, long ImageBytes)>();
            foreach (ProcessModule module in process.Modules)
            {
                var name = module.ModuleName ?? "?";
                moduleImageBytes += module.ModuleMemorySize;
                parts.Add((name, module.ModuleMemorySize));
            }

            parts.Sort((a, b) => b.ImageBytes.CompareTo(a.ImageBytes));
            var top = new System.Text.StringBuilder();
            for (var i = 0; i < Math.Min(12, parts.Count); i++)
            {
                if (i > 0)
                {
                    top.Append("; ");
                }

                top.Append(parts[i].Name).Append('=').Append(Math.Round(parts[i].ImageBytes / 1048576.0, 1)).Append("MB");
            }

            var managedHeap = GC.GetTotalMemory(forceFullCollection: false);
            var gc = GC.GetGCMemoryInfo();
            _logger.Information(LogCategories.Render, "island.skia.memory", "Island memory decomposition.",
                data: new Dictionary<string, object?>
                {
                    ["workingSetBytes"] = memory.WorkingSet,
                    ["privateBytes"] = memory.PrivateBytes,
                    ["moduleCount"] = parts.Count,
                    ["modulesImageBytes"] = moduleImageBytes,
                    ["managedHeapBytes"] = managedHeap,
                    ["managedCommittedBytes"] = gc.TotalCommittedBytes,
                    ["threadCount"] = process.Threads.Count,
                    ["handles"] = memory.Handles,
                    ["unaccountedBytes"] = memory.WorkingSet - moduleImageBytes - gc.TotalCommittedBytes,
                    ["topModules"] = top.ToString(),
                });
        }
        catch (Exception)
        {
            // Diagnostics must never break the render loop.
        }
    }

    private void LogFirstFrame(long now)
    {
        var sinceProcessStart = ProcessUptimeMs();
        var sinceHostStart = (now - _hostStartTimestamp) * 1000.0 / Stopwatch.Frequency;
        _logger.Information(LogCategories.Render, "island.skia.first-frame",
            "Island first frame rendered.",
            data: new Dictionary<string, object?>
            {
                ["sinceProcessStartMs"] = double.IsNaN(sinceProcessStart) ? null : Math.Round(sinceProcessStart, 1),
                ["sinceHostStartMs"] = Math.Round(sinceHostStart, 1),
                ["managedHeapBytes"] = GC.GetTotalMemory(forceFullCollection: false),
            });
    }

    private static double ProcessUptimeMs()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            var age = DateTime.Now - process.StartTime;
            return age.TotalMilliseconds;
        }
        catch (Exception)
        {
            return double.NaN;
        }
    }

    private static int Bucket(double milliseconds)
    {
        if (!double.IsFinite(milliseconds) || milliseconds <= 0)
        {
            return 0;
        }

        var index = (int)(milliseconds / BucketMs);
        return index >= Buckets ? Buckets - 1 : index;
    }

    /// <summary>Histogram percentile in milliseconds (0 when no samples).</summary>
    private static double Percentile(int[] buckets, double percentile)
    {
        var total = 0;
        foreach (var count in buckets)
        {
            total += count;
        }

        if (total == 0)
        {
            return 0;
        }

        var target = (int)Math.Ceiling(percentile * total);
        var running = 0;
        for (var i = 0; i < buckets.Length; i++)
        {
            running += buckets[i];
            if (running >= target)
            {
                return Math.Round((i + 1) * BucketMs, 2);
            }
        }

        return Math.Round(buckets.Length * BucketMs, 2);
    }

    private static bool IsEnabledToken(string? token)
        => !string.IsNullOrWhiteSpace(token)
            && (token.Equals("1", StringComparison.Ordinal) || token.Equals("true", StringComparison.OrdinalIgnoreCase));
}
