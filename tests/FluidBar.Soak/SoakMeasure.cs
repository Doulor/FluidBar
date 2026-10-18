using System.Diagnostics;

namespace FluidBar.Soak;

/// <summary>GC/allocation snapshot helper for budget measurements.</summary>
/// <param name="ThreadAllocatedBytes">Managed bytes allocated on the measuring thread.</param>
/// <param name="TotalMemoryBytes">Live heap after the measurement point.</param>
/// <param name="Gen0">Gen-0 collection count.</param>
/// <param name="Gen1">Gen-1 collection count.</param>
/// <param name="Gen2">Gen-2 collection count.</param>
public readonly record struct GcSnapshot(
    long ThreadAllocatedBytes,
    long TotalMemoryBytes,
    int Gen0,
    int Gen1,
    int Gen2)
{
    /// <summary>Captures current counters (no forcing by default).</summary>
    public static GcSnapshot Capture(bool forceFullCollection = false)
        => new(
            GC.GetAllocatedBytesForCurrentThread(),
            GC.GetTotalMemory(forceFullCollection),
            GC.CollectionCount(0),
            GC.CollectionCount(1),
            GC.CollectionCount(2));

    /// <summary>Managed bytes allocated between two snapshots on the measuring thread.</summary>
    public long AllocatedSince(GcSnapshot earlier) => ThreadAllocatedBytes - earlier.ThreadAllocatedBytes;

    /// <summary>Gen-0 collections since the earlier snapshot.</summary>
    public int Gen0Since(GcSnapshot earlier) => Gen0 - earlier.Gen0;

    /// <summary>Gen-1 collections since the earlier snapshot.</summary>
    public int Gen1Since(GcSnapshot earlier) => Gen1 - earlier.Gen1;

    /// <summary>Gen-2 collections since the earlier snapshot.</summary>
    public int Gen2Since(GcSnapshot earlier) => Gen2 - earlier.Gen2;
}

/// <summary>Process/environment measurement helpers shared by scenarios.</summary>
public static class SoakMeasure
{
    /// <summary>Captures the current process peak working set in bytes (0 when unavailable).</summary>
    public static long PeakWorkingSetBytes()
    {
        using var process = Process.GetCurrentProcess();
        try
        {
            return process.PeakWorkingSet64;
        }
        catch (Exception ex) when (ex is InvalidOperationException or PlatformNotSupportedException or NotSupportedException)
        {
            return 0;
        }
    }

    /// <summary>Current managed heap after a forced full collection.</summary>
    public static long LiveHeapBytes() => GC.GetTotalMemory(forceFullCollection: true);

    /// <summary>Runs one measured action, forcing a full GC before the baseline for stable numbers.</summary>
    public static (double DurationMs, GcSnapshot Before, GcSnapshot After) Measure(Action action)
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        var before = GcSnapshot.Capture();
        var stopwatch = Stopwatch.StartNew();
        action();
        stopwatch.Stop();
        var after = GcSnapshot.Capture();
        return (stopwatch.Elapsed.TotalMilliseconds, before, after);
    }
}
