using System.Diagnostics;
using FluidBar.Core.Activities;
using FluidBar.Core.Diagnostics;
using FluidBar.Core.Events;

namespace FluidBar.Soak.Scenarios;

/// <summary>
/// Scenario A — event storm: 100,000 mixed events through EventHub → ActivityScheduler →
/// IslandStateMachine → IslandLayout. Measures throughput, coalescing, drops, allocations and
/// retained-state bounds.
/// </summary>
public static class EventStormScenario
{
    private const int DrainBatchSize = 128;

    public static SoakScenarioResult Run(int seed)
    {
        var builder = new ScenarioResultBuilder("A", "event storm (100k mixed events)");
        var stopwatch = Stopwatch.StartNew();
        var counters = new DiagnosticCounters();
        using var pipeline = SoakHarness.CreatePipeline(counters);
        var generator = new MixedEventGenerator(seed);
        var baseTime = SoakHarness.Origin;

        var maxVisible = 0;
        var maxBackground = 0;
        var maxRetained = 0;
        var layoutCount = 0;
        var layoutViolations = 0;
        var delivered = 0;

        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        var gcBefore = GcSnapshot.Capture();

        for (var i = 0; i < SoakBudgets.EventStormEventCount; i++)
        {
            var evt = generator.Next(i);
            pipeline.Hub.TryPublish(evt);

            if (i % DrainBatchSize != DrainBatchSize - 1)
            {
                continue;
            }

            var now = baseTime.AddMilliseconds(generator.NowMs);
            delivered += pipeline.Pump(now);
            pipeline.Tick(now);

            var state = pipeline.State;
            maxVisible = Math.Max(maxVisible, state.Visible.Length);
            maxBackground = Math.Max(maxBackground, state.Background.Length);
            maxRetained = Math.Max(maxRetained, state.ActivityCount);

            if (layoutCount < 64 && (i / DrainBatchSize) % 8 == 0)
            {
                var layout = SoakHarness.Layout(pipeline.Controller.Current);
                layoutCount++;
                if (!layout.IsVisible
                    || layout.Shell.Width <= 0
                    || layout.Shell.Right > SoakHarness.Monitor.WorkAreaWidthDip + 0.001
                    || layout.Shell.Bottom > SoakHarness.Monitor.WorkAreaHeightDip + 0.001)
                {
                    layoutViolations++;
                }
            }
        }

        pipeline.Pump(baseTime.AddMilliseconds(generator.NowMs));
        pipeline.Tick(baseTime.AddMilliseconds(generator.NowMs + 60_000));
        var gcAfter = GcSnapshot.Capture();
        stopwatch.Stop();

        var metrics = pipeline.Hub.Metrics;
        var allocated = gcAfter.AllocatedSince(gcBefore);
        var throughput = SoakBudgets.EventStormEventCount / Math.Max(0.001, stopwatch.Elapsed.TotalSeconds);
        var peakWorkingSet = SoakMeasure.PeakWorkingSetBytes();

        builder.Metric("events", SoakBudgets.EventStormEventCount);
        builder.Metric("delivered", delivered);
        builder.Metric("published", metrics.Published);
        builder.Metric("coalesced", metrics.Coalesced);
        builder.Metric("dropped", metrics.Dropped);
        builder.Metric("throughputEventsPerSecond", throughput);
        builder.Metric("allocatedBytes", allocated);
        builder.Metric("allocatedBytesPerEvent", (double)allocated / SoakBudgets.EventStormEventCount);
        builder.Metric("gen0Collections", gcAfter.Gen0Since(gcBefore));
        builder.Metric("gen1Collections", gcAfter.Gen1Since(gcBefore));
        builder.Metric("gen2Collections", gcAfter.Gen2Since(gcBefore));
        builder.Metric("peakWorkingSetBytes", peakWorkingSet);
        builder.Metric("maxVisible", maxVisible);
        builder.Metric("maxBackground", maxBackground);
        builder.Metric("maxRetained", maxRetained);
        builder.Metric("layouts", layoutCount);
        builder.Metric("hubCoalescedCounter", counters.Get(CounterKind.EventsCoalesced));
        builder.Metric("hubDroppedCounter", counters.Get(CounterKind.EventsDropped));

        builder.Check("eventsDropped", metrics.Dropped, 0, "==");
        builder.Check("coalescedEvents", metrics.Coalesced, 1, ">=");
        builder.Check("throughputEventsPerSecond", throughput, SoakBudgets.EventStormMinEventsPerSecond, ">=");
        builder.Check("allocatedBytesPerEvent", (double)allocated / SoakBudgets.EventStormEventCount,
            SoakBudgets.EventStormMaxAllocatedBytesPerEvent, "<=");
        builder.Check("peakWorkingSetBytes", peakWorkingSet, SoakBudgets.EventStormMaxPeakWorkingSetBytes, "<=");
        builder.Check("maxVisible", maxVisible, SoakBudgets.MaxVisibleActivities, "<=");
        builder.Check("maxBackground", maxBackground, SoakBudgets.MaxBackgroundActivities, "<=");
        builder.Check("maxRetained", maxRetained, SoakBudgets.MaxRetainedActivities, "<=");
        builder.Check("layoutViolations", layoutViolations, 0, "==");
        builder.Check("deliveredEvents", delivered, 1, ">=");

        return builder.Build(stopwatch.Elapsed.TotalMilliseconds);
    }
}
