using System.Diagnostics;
using ActivityKind = FluidBar.Core.Activities.ActivityKind;
using FluidBar.Core.Activities;
using FluidBar.Core.Events;

namespace FluidBar.Soak.Scenarios;

/// <summary>
/// Scenario B — incremental media: 10,000 same-key <c>Incremental</c> updates at a simulated
/// 10 Hz. Asserts the shell target is unchanged, the scheduler does not churn (same primary,
/// one visible layer) and allocations per update stay bounded.
/// </summary>
public static class IncrementalMediaScenario
{
    private const int WarmupUpdates = 100;

    public static SoakScenarioResult Run(int seed)
    {
        var builder = new ScenarioResultBuilder("B", "incremental media (10k updates)");
        var stopwatch = Stopwatch.StartNew();
        using var pipeline = SoakHarness.CreatePipeline();
        var key = new ActivityKey(ActivityKind.Media, "media:gsmtc");
        var baseTime = SoakHarness.Origin;

        var initial = new ActivityPayload
        {
            Title = "Soak track",
            Subtitle = "Soak artist",
            IsActive = true,
            ShowsAudioWave = true,
            EndTicks = 3_000_000_000,
        };
        pipeline.Hub.TryPublish(IslandEvent.New(key, initial, 1, TimeSpan.Zero));
        pipeline.Pump(baseTime);
        var firstLayout = SoakHarness.Layout(pipeline.Controller.Current);
        var firstKey = pipeline.State.Primary?.Key;
        var baselineSwitchedCount = pipeline.State.SwitchedCount;

        var primaryChanges = 0;
        var shapeViolations = 0;
        var kindViolations = 0;
        var layoutViolations = 0;
        var nonContentUpdates = 0;
        var delivered = 0;

        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        var gcBefore = GcSnapshot.Capture();
        var measuredUpdates = 0;

        for (var i = 0; i < SoakBudgets.IncrementalUpdateCount; i++)
        {
            var now = baseTime.AddMilliseconds(i * 100);
            var payload = new ActivityPayload
            {
                Title = "Soak track",
                Subtitle = "Soak artist",
                ProgressPercent = i % 101,
                LyricLine = "lyric line " + (i % 40),
                PositionTicks = i * 1_000_000L,
                EndTicks = 3_000_000_000,
                IsActive = true,
                ShowsAudioWave = true,
            };
            pipeline.Hub.TryPublish(IslandEvent.Incremental(key, payload, i + 2, TimeSpan.FromMilliseconds(i * 100)));
            delivered += pipeline.Pump(now);

            var state = pipeline.State;
            if (pipeline.Controller.Current.Kind != IslandStateKind.Compact)
            {
                kindViolations++;
            }

            if (state.ActivityCount != 1 || state.Visible.Length != 1)
            {
                shapeViolations++;
            }

            if (!Equals(state.Primary?.Key, firstKey))
            {
                primaryChanges++;
            }

            if (pipeline.LastUpdate.Kind != ActivityUpdateKind.ContentUpdated)
            {
                nonContentUpdates++;
            }

            if (i % 1_000 == 0)
            {
                var layout = SoakHarness.Layout(pipeline.Controller.Current);
                if (layout.Shell.Width != firstLayout.Shell.Width
                    || layout.Shell.Height != firstLayout.Shell.Height)
                {
                    layoutViolations++;
                }
            }

            if (i >= WarmupUpdates)
            {
                measuredUpdates++;
            }
        }

        var gcAfter = GcSnapshot.Capture();
        stopwatch.Stop();
        var allocated = gcAfter.AllocatedSince(gcBefore);
        var allocatedPerUpdate = (double)allocated / Math.Max(1, measuredUpdates);
        var metrics = pipeline.Hub.Metrics;

        builder.Metric("updates", SoakBudgets.IncrementalUpdateCount);
        builder.Metric("delivered", delivered);
        builder.Metric("coalesced", metrics.Coalesced);
        builder.Metric("dropped", metrics.Dropped);
        builder.Metric("allocatedBytesPerUpdate", allocatedPerUpdate);
        builder.Metric("primaryChanges", primaryChanges);
        builder.Metric("shapeViolations", shapeViolations);
        builder.Metric("kindViolations", kindViolations);
        builder.Metric("layoutViolations", layoutViolations);
        builder.Metric("nonContentUpdates", nonContentUpdates);
        builder.Metric("switchedCount", pipeline.State.SwitchedCount - baselineSwitchedCount);
        builder.Metric("replacedCount", pipeline.State.ReplacedCount);
        builder.Metric("elapsedMs", stopwatch.Elapsed.TotalMilliseconds);

        builder.Check("primaryChanges", primaryChanges, 0, "==");
        builder.Check("shapeViolations", shapeViolations, 0, "==");
        builder.Check("kindViolations", kindViolations, 0, "==");
        builder.Check("layoutViolations", layoutViolations, 0, "==");
        builder.Check("nonContentUpdates", nonContentUpdates, 0, "==");
        builder.Check("switchedCount", pipeline.State.SwitchedCount - baselineSwitchedCount, 0, "==");
        builder.Check("eventsDropped", metrics.Dropped, 0, "==");
        builder.Check("allocatedBytesPerUpdate", allocatedPerUpdate,
            SoakBudgets.IncrementalMaxAllocatedBytesPerUpdate, "<=");
        builder.Check("deliveredUpdates", delivered, SoakBudgets.IncrementalUpdateCount, ">=");

        return builder.Build(stopwatch.Elapsed.TotalMilliseconds);
    }
}
