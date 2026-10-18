using System.Diagnostics;
using FluidBar.Core.Activities;
using FluidBar.Core.Events;

namespace FluidBar.Soak.Scenarios;

/// <summary>
/// Scenario C — activity churn/expiry: 10,000 New/Replacement/Incremental/Retract events across
/// all activity kinds with TTL ticking. Asserts the scheduler's visible/background/sequence state
/// stays bounded (no unbounded collections, no leaked activities).
/// </summary>
public static class ActivityChurnScenario
{
    public static SoakScenarioResult Run(int seed)
    {
        var builder = new ScenarioResultBuilder("C", "activity churn/expiry (10k events)");
        var stopwatch = Stopwatch.StartNew();
        var scheduler = new ActivityScheduler();
        using var controller = new IslandStateController(
            IslandBehaviorOptions.Default with { AutoHideMs = 0, AutoCollapseMs = 0 });
        var generator = new MixedEventGenerator(seed);
        var baseTime = SoakHarness.Origin;
        var state = ActivitySchedulerState.Empty;
        var liveKeys = new List<ActivityKey>(SoakBudgets.MaxRetainedActivities);

        var maxVisible = 0;
        var maxBackground = 0;
        var maxRetained = 0;
        var invariantViolations = 0;
        var sequenceLeaks = 0;
        var tickChanges = 0;
        var liveHeapAfterWarmup = 0L;

        for (var i = 0; i < SoakBudgets.ChurnEventCount; i++)
        {
            var now = baseTime.AddMilliseconds(i * 1_000);
            if (i % 2 == 0)
            {
                var tick = scheduler.Tick(state, now);
                if (tick.HasChange)
                {
                    state = tick.State;
                    controller.Apply(IslandInput.ForActivityUpdate(tick), now);
                    tickChanges++;
                }
            }

            liveKeys.Clear();
            foreach (var record in state.Visible)
            {
                liveKeys.Add(record.Key);
            }

            foreach (var record in state.Background)
            {
                liveKeys.Add(record.Key);
            }

            var evt = generator.NextChurn(i, liveKeys);
            var update = scheduler.Update(state, evt, now);
            state = update.State;
            controller.Apply(IslandInput.ForActivityUpdate(update), now);

            maxVisible = Math.Max(maxVisible, state.Visible.Length);
            maxBackground = Math.Max(maxBackground, state.Background.Length);
            maxRetained = Math.Max(maxRetained, state.ActivityCount);

            if (state.Visible.Length > SoakBudgets.MaxVisibleActivities
                || state.Background.Length > SoakBudgets.MaxBackgroundActivities
                || state.ActivityCount > SoakBudgets.MaxRetainedActivities
                || state.ActivityCount != state.Visible.Length + state.Background.Length)
            {
                invariantViolations++;
            }

            if (i % 500 == 0 && state.LastSequences.Count > SoakBudgets.MaxRetainedActivities)
            {
                sequenceLeaks++;
            }

            if (i == 500)
            {
                liveHeapAfterWarmup = SoakMeasure.LiveHeapBytes();
            }
        }

        // Drain: retract everything live, then expire far in the future. The state must go empty.
        var finalKeys = state.Visible.Select(r => r.Key).Concat(state.Background.Select(r => r.Key)).ToArray();
        long drainSequence = 1_000_000;
        foreach (var key in finalKeys)
        {
            state = scheduler.Update(
                state,
                IslandEvent.Retract(key, ++drainSequence),
                baseTime.AddHours(1)).State;
        }

        var finalTick = scheduler.Tick(state, baseTime.AddDays(1));
        if (finalTick.HasChange)
        {
            state = finalTick.State;
            controller.Apply(IslandInput.ForActivityUpdate(finalTick), baseTime.AddDays(1));
        }

        var heapAfter = SoakMeasure.LiveHeapBytes();
        var heapGrowth = Math.Max(0, heapAfter - liveHeapAfterWarmup);
        stopwatch.Stop();

        builder.Metric("events", SoakBudgets.ChurnEventCount);
        builder.Metric("maxVisible", maxVisible);
        builder.Metric("maxBackground", maxBackground);
        builder.Metric("maxRetained", maxRetained);
        builder.Metric("invariantViolations", invariantViolations);
        builder.Metric("sequenceLeaks", sequenceLeaks);
        builder.Metric("tickChanges", tickChanges);
        builder.Metric("heapGrowthBytes", heapGrowth);
        builder.Metric("finalActivityCount", state.ActivityCount);
        builder.Metric("finalVisible", state.Visible.Length);
        builder.Metric("finalBackground", state.Background.Length);
        builder.Metric("expiredCount", state.ExpiredCount);
        builder.Metric("droppedCount", state.DroppedCount);

        builder.Check("invariantViolations", invariantViolations, 0, "==");
        builder.Check("sequenceLeaks", sequenceLeaks, 0, "==");
        builder.Check("maxVisible", maxVisible, SoakBudgets.MaxVisibleActivities, "<=");
        builder.Check("maxBackground", maxBackground, SoakBudgets.MaxBackgroundActivities, "<=");
        builder.Check("maxRetained", maxRetained, SoakBudgets.MaxRetainedActivities, "<=");
        builder.Check("heapGrowthBytes", heapGrowth, SoakBudgets.ChurnMaxHeapGrowthBytes, "<=");
        builder.Check("finalActivityCount", state.ActivityCount, 0, "==");
        builder.Check("tickChanges", tickChanges, 1, ">=");

        return builder.Build(stopwatch.Elapsed.TotalMilliseconds);
    }
}
