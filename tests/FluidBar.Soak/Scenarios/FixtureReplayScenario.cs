using System.Diagnostics;
using FluidBar.Soak.Recording;

namespace FluidBar.Soak.Scenarios;

/// <summary>
/// Scenario F — recording/replay fixtures: replays the three deterministic event recordings
/// through the Core pipeline, validates every expected presentation snapshot, and replays twice
/// to prove determinism (same snapshots both runs).
/// </summary>
public static class FixtureReplayScenario
{
    public static SoakScenarioResult Run(int seed)
    {
        _ = seed;
        var builder = new ScenarioResultBuilder("F", "recording/replay fixtures");
        var stopwatch = Stopwatch.StartNew();

        var validated = 0;
        var mismatchCount = 0;
        var nondeterministic = 0;
        foreach (var name in EventRecordingCatalog.Names)
        {
            var recording = EventRecordingCatalog.Load(name);
            var failures = EventRecordingReplay.Validate(recording);
            mismatchCount += failures.Length;
            foreach (var failure in failures)
            {
                builder.Fail(failure);
            }

            var first = EventRecordingReplay.Replay(recording);
            var second = EventRecordingReplay.Replay(recording);
            var firstSnapshots = first.Select(EventRecordingReplay.Snapshot).ToArray();
            var secondSnapshots = second.Select(EventRecordingReplay.Snapshot).ToArray();
            if (!SnapshotsEqual(firstSnapshots, secondSnapshots))
            {
                nondeterministic++;
            }

            builder.Metric($"fixture.{name}.events", recording.Events.Length);
            builder.Metric($"fixture.{name}.expectedSnapshots", recording.Expected.Length);
            validated++;
        }

        stopwatch.Stop();
        builder.Metric("fixtures", validated);
        builder.Metric("mismatches", mismatchCount);
        builder.Metric("nondeterministicReplays", nondeterministic);
        builder.Check("fixtures", validated, SoakBudgets.FixtureCount, "==");
        builder.Check("mismatches", mismatchCount, 0, "==");
        builder.Check("nondeterministicReplays", nondeterministic, 0, "==");

        return builder.Build(stopwatch.Elapsed.TotalMilliseconds);
    }

    private static bool SnapshotsEqual(ExpectedSnapshot[] left, ExpectedSnapshot[] right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }

        return true;
    }
}
