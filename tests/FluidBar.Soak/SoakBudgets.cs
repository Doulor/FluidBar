namespace FluidBar.Soak;

/// <summary>
/// Fixed M6 budgets. Every value is a CI acceptance threshold that the soak harness and the Core
/// regression tests enforce. Throughput/memory budgets are deliberately loose (≥5x measured
/// headroom on the dev machine) so CI variance cannot cause false failures.
/// </summary>
public static class SoakBudgets
{
    /// <summary>Scenario A: mixed events pushed through the full Core pipeline.</summary>
    public const int EventStormEventCount = 100_000;

    /// <summary>Scenario A: minimum sustained throughput (events/s).</summary>
    public const double EventStormMinEventsPerSecond = 10_000;

    /// <summary>Scenario A: managed allocations per event.</summary>
    public const long EventStormMaxAllocatedBytesPerEvent = 32 * 1024;

    /// <summary>Scenario A: peak working set ceiling.</summary>
    public const long EventStormMaxPeakWorkingSetBytes = 512L * 1024 * 1024;

    /// <summary>Scenario A: retained-activity growth ceiling after the storm.</summary>
    public const int MaxVisibleActivities = 3;

    /// <summary>Scenario A/C: background queue ceiling.</summary>
    public const int MaxBackgroundActivities = 8;

    /// <summary>Scenario A/C: total retained live activities (visible + background).</summary>
    public const int MaxRetainedActivities = MaxVisibleActivities + MaxBackgroundActivities;

    /// <summary>Scenario B: incremental media updates (progress/lyrics).</summary>
    public const int IncrementalUpdateCount = 10_000;

    /// <summary>Scenario B: managed allocation per incremental update.</summary>
    public const long IncrementalMaxAllocatedBytesPerUpdate = 32 * 1024;

    /// <summary>Scenario C: New/Replacement/Retract churn events.</summary>
    public const int ChurnEventCount = 10_000;

    /// <summary>Scenario C: managed heap growth after warm-up (GC.GetTotalMemory forced).</summary>
    public const long ChurnMaxHeapGrowthBytes = 32L * 1024 * 1024;

    /// <summary>Scenario D: allowed deviation from the RK4 reference settle time.</summary>
    public const double MotionSettleTolerance = 0.20;

    /// <summary>Scenario D: allocations during 10,000 active spring substeps after warm-up.</summary>
    public const long MotionMaxAllocatedBytes = 64 * 1024;

    /// <summary>Scenario D: max frames to settle one token (<c>t₂%</c> at 1/300s ≈ 90 frames).</summary>
    public const int MotionMaxFramesToSettle = 2_000;

    /// <summary>Scenario E: atomic settings saves.</summary>
    public const int SettingsSaveCount = 10_000;

    /// <summary>Scenario E: managed heap growth after warm-up.</summary>
    public const long SettingsMaxHeapGrowthBytes = 16L * 1024 * 1024;

    /// <summary>Recording/replay fixtures that must exist and validate.</summary>
    public const int FixtureCount = 3;

    /// <summary>Total wall-clock budget for the whole harness (CI job must stay well below 60 s).</summary>
    public const double TotalRuntimeBudgetMs = 55_000;
}
