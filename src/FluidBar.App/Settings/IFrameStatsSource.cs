namespace FluidBar.App.Settings;

/// <summary>
/// Immutable frame-timing sample exposed by the island host for the advanced "frame statistics"
/// setting (spec §9.3 <c>frame.p50/p99/dropped</c> diagnostics).
/// </summary>
/// <param name="Enabled">Whether recording is currently on.</param>
/// <param name="Frames">Number of recorded frames in the window.</param>
/// <param name="P50Ms">Median render time in milliseconds.</param>
/// <param name="P99Ms">99th percentile render time in milliseconds.</param>
/// <param name="LastMs">Most recent render time in milliseconds.</param>
/// <param name="MeanMs">Mean render time in milliseconds.</param>
public readonly record struct FrameStatsSnapshot(
    bool Enabled,
    long Frames,
    double P50Ms,
    double P99Ms,
    double LastMs,
    double MeanMs)
{
    /// <summary>Sample used before the host exists or while recording is off.</summary>
    public static FrameStatsSnapshot Disabled { get; } = new(false, 0, 0, 0, 0, 0);
}

/// <summary>Read-side seam for the settings diagnostics line; implemented by the island host.</summary>
public interface IFrameStatsSource
{
    /// <summary>Latest frame-timing snapshot (cheap, UI-thread safe).</summary>
    FrameStatsSnapshot FrameStats { get; }
}
