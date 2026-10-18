using System.Diagnostics;
using FluidBar.Core.Motion;

namespace FluidBar.Soak.Scenarios;

/// <summary>
/// Scenario D — motion: settles all nine spring tokens at 1/300 s frames, compares the measured
/// settle time against the independent RK4 2 % reference (±20 %), measures allocations during
/// 10,000 active substeps after warm-up, and verifies <see cref="MotionLoop"/> stops at rest.
/// </summary>
public static class MotionScenario
{
    private const double FrameSeconds = 1.0 / 300.0;
    private const double EnvelopeFraction = 0.02;
    private const int AllocationSteps = 10_000;
    private const int WarmupSteps = 1_000;

    private static readonly MotionToken[] Tokens =
    [
        MotionTokens.Expand, MotionTokens.ExpandHeight, MotionTokens.Collapse, MotionTokens.Morph,
        MotionTokens.Hover, MotionTokens.Press, MotionTokens.Tab, MotionTokens.Nudge, MotionTokens.Hide,
    ];

    public static SoakScenarioResult Run(int seed)
    {
        _ = seed;
        var builder = new ScenarioResultBuilder("D", "motion settle/allocation (9 tokens)");
        var stopwatch = Stopwatch.StartNew();

        var maxDeviation = 0.0;
        var loopStops = 0;
        var allocationTotal = 0L;
        var frameSamples = new List<int>(Tokens.Length * 3);

        foreach (var token in Tokens)
        {
            var predicted = token.Spring.PredictedSettleSeconds;
            var times = new double[3];
            var amplitudes = new double[] { 20, 25, 30 };
            for (var run = 0; run < amplitudes.Length; run++)
            {
                var frames = SettleFrames(token, amplitudes[run], predicted, out var time);
                times[run] = time;
                frameSamples.Add(frames);
                var deviation = Math.Abs(time - predicted) / predicted;
                maxDeviation = Math.Max(maxDeviation, deviation);
                builder.Metric($"{token.Name}.settleMs", time * 1000);
                builder.Metric($"{token.Name}.frames", frames);
            }

            var mean = times.Average();
            var stddev = Math.Sqrt(times.Select(t => Math.Pow(t - mean, 2)).Average());
            builder.Metric($"{token.Name}.stddevMs", stddev * 1000);

            allocationTotal += MeasureAllocations(token);

            var loopFrames = RunLoopUntilRest(token);
            if (loopFrames > 0)
            {
                loopStops++;
            }

            builder.Metric($"{token.Name}.loopFrames", loopFrames);
        }

        var settledFramesMean = frameSamples.Average();
        var settledFramesStdDev = Math.Sqrt(frameSamples.Select(f => Math.Pow(f - settledFramesMean, 2)).Average());
        stopwatch.Stop();

        builder.Metric("tokens", Tokens.Length);
        builder.Metric("maxSettleDeviation", maxDeviation);
        builder.Metric("allocationTotalBytes", allocationTotal);
        builder.Metric("frameMean", settledFramesMean);
        builder.Metric("frameStdDev", settledFramesStdDev);
        builder.Metric("loopsStopped", loopStops);

        builder.Check("maxSettleDeviation", maxDeviation, SoakBudgets.MotionSettleTolerance, "<=");
        builder.Check("allocationTotalBytes", allocationTotal, SoakBudgets.MotionMaxAllocatedBytes, "<=");
        builder.Check("loopsStopped", loopStops, Tokens.Length, ">=");
        builder.Check("tokensSettled", Tokens.Length, 9, "==");

        return builder.Build(stopwatch.Elapsed.TotalMilliseconds);
    }

    private static int SettleFrames(MotionToken token, double amplitude, double predicted, out double time)
    {
        var spring = new Spring(token.Spring);
        spring.Retarget(amplitude, FrameSeconds);
        var lastOutsideFrames = 0;
        var frames = 0;
        while (!spring.IsAtRest && frames < SoakBudgets.MotionMaxFramesToSettle)
        {
            if (Math.Abs(spring.Value - amplitude) >= EnvelopeFraction * amplitude)
            {
                lastOutsideFrames = frames + 1;
            }

            spring.Step(FrameSeconds);
            frames++;
        }

        time = lastOutsideFrames * FrameSeconds;
        return lastOutsideFrames;
    }

    private static long MeasureAllocations(MotionToken token)
    {
        var spring = new Spring(token.Spring);
        spring.Retarget(25, FrameSeconds);
        for (var i = 0; i < WarmupSteps; i++)
        {
            if (i % 300 == 299)
            {
                spring.Retarget((i % 600 == 599) ? 25 : 10, FrameSeconds);
            }

            spring.Step(FrameSeconds);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < AllocationSteps; i++)
        {
            if (i % 300 == 299)
            {
                spring.Retarget((i % 600 == 599) ? 25 : 10, FrameSeconds);
            }

            spring.Step(FrameSeconds);
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static int RunLoopUntilRest(MotionToken token)
    {
        var clock = new ManualFrameClock();
        using var loop = new MotionLoop(clock);
        var spring = loop.Springs.Add(new Spring(token.Spring));
        spring.Retarget(25, FrameSeconds);
        loop.Start();
        var frames = 0;
        while (clock.IsRunning && frames < SoakBudgets.MotionMaxFramesToSettle)
        {
            clock.Pump(TimeSpan.FromSeconds(FrameSeconds));
            frames++;
        }

        return !clock.IsRunning && spring.IsAtRest ? frames : 0;
    }
}
