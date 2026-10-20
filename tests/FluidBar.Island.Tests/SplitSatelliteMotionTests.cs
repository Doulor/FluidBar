using FluidBar.Island;

namespace FluidBar.Island.Tests;

/// <summary>
/// Fixed-parameter contract for the split-satellite motion (task: iOS-grade satellite animation).
/// The numbers here are the design contract: total birth duration, damping, preload, hover/press
/// values, breathing amplitude/period and - most importantly - that interruptions continue from the
/// current visual values instead of jumping back to the start.
/// </summary>
public sealed class SplitSatelliteMotionTests
{
    private const double Frame = 1.0 / 240.0;

    private static SplitSatelliteMotion BornToIdle(out double duration)
    {
        var motion = new SplitSatelliteMotion();
        motion.BeginBirth(0, 0, 120, 40, 1, 0.33);
        var time = 0.0;
        while (motion.State != SplitSatelliteState.Idle && time < 3)
        {
            motion.Update(Frame);
            time += Frame;
        }

        duration = time;
        return motion;
    }

    [Fact]
    public void Birth_SettlesWithinFourHundredTwentyToFiveHundredMilliseconds()
    {
        _ = BornToIdle(out var duration);
        Assert.InRange(duration, SatelliteMotionTokens.MorphMinDurationSeconds,
            SatelliteMotionTokens.MorphMaxDurationSeconds);
    }

    [Fact]
    public void Birth_HoldsThePreloadScaleForSixtyMilliseconds()
    {
        var motion = new SplitSatelliteMotion();
        motion.BeginBirth(0, 0, 120, 40, 1, 0.33);
        Assert.Equal(SatelliteMotionTokens.PreloadScale, motion.Values.Scale, 6);

        for (var t = 0.0; t < 0.05; t += Frame)
        {
            motion.Update(Frame);
            Assert.Equal(SatelliteMotionTokens.PreloadScale, motion.Values.Scale, 6);
        }

        for (var t = 0.0; t < 0.08; t += Frame)
        {
            motion.Update(Frame);
        }

        Assert.True(motion.Values.Scale > SatelliteMotionTokens.PreloadScale);
    }

    [Fact]
    public void Birth_OvershootsTheSeatOnceWithTheDesignedSpring()
    {
        var motion = new SplitSatelliteMotion();
        motion.BeginBirth(0, 0, 120, 40, 1, 0.33);
        var maxX = double.MinValue;
        var minX = double.MaxValue;
        for (var i = 0; i < 300; i++)
        {
            motion.Update(Frame);
            maxX = Math.Max(maxX, motion.Values.X);
            minX = Math.Min(minX, motion.Values.X);
        }

        // Under-damped (zeta 0.68) landing: it must pass the seat by 1-10 % and settle back on it.
        Assert.InRange(maxX, 120 * 1.01, 120 * 1.10);
        Assert.Equal(120, motion.Values.X, 1);
        Assert.True(minX >= -0.5, "the satellite must not fly back past the parent edge");
    }

    [Fact]
    public void Merge_OvershootsTheLandingPointThenSettlesOnIt()
    {
        var motion = BornToIdle(out _);
        var startX = motion.Values.X;
        motion.BeginMerge(0, 0);
        var overshoot = 0.0;
        var time = 0.0;
        while (motion.State != SplitSatelliteState.Merged && time < 2)
        {
            motion.Update(Frame);
            time += Frame;
            overshoot = Math.Min(overshoot, motion.Values.X);
        }

        Assert.Equal(SplitSatelliteState.Merged, motion.State);
        Assert.True(overshoot < 0, "the merge must pass the landing point before settling");
        Assert.Equal(0, motion.Values.X, 3);
        Assert.True(startX > 0);
    }

    [Fact]
    public void Hover_And_Press_UseTheSharedMainIslandValues()
    {
        var motion = BornToIdle(out _);
        motion.SetHover(true);
        for (var i = 0; i < 120; i++)
        {
            motion.Update(Frame);
        }

        Assert.Equal(SatelliteMotionTokens.HoverScale, motion.Values.Scale / motion.Values.BreathScale, 3);
        Assert.True(motion.Values.Highlight > 0.3);

        motion.Press();
        for (var i = 0; i < 120; i++)
        {
            motion.Update(Frame);
        }

        Assert.Equal(SatelliteMotionTokens.PressScale * SatelliteMotionTokens.HoverScale,
            motion.Values.Scale / motion.Values.BreathScale, 3);
        Assert.Equal(SatelliteMotionTokens.PressBrightness, motion.Values.Brightness, 6);

        motion.Release();
        for (var i = 0; i < 240; i++)
        {
            motion.Update(Frame);
        }

        Assert.Equal(SatelliteMotionTokens.HoverScale, motion.Values.Scale / motion.Values.BreathScale, 3);
        Assert.Equal(0, motion.Values.Brightness, 6);
    }

    [Fact]
    public void Interruption_ContinuesFromTheCurrentVisualValues()
    {
        var motion = new SplitSatelliteMotion();
        motion.BeginBirth(0, 0, 120, 40, 1, 0.33);
        for (var t = 0.0; t < 0.18; t += Frame)
        {
            motion.Update(Frame);
        }

        var before = motion.Values;
        motion.BeginMerge(0, 0);
        var after = motion.Values;

        // No jump: the pose at the interrupt frame is the pose the merge starts from.
        Assert.Equal(before.X, after.X, 6);
        Assert.Equal(before.Y, after.Y, 6);
        Assert.Equal(before.Scale, after.Scale, 6);

        motion.Update(Frame);
        Assert.True(Math.Abs(motion.Values.X - before.X) < 3.0,
            "the first merge frame must move a normal frame step, not restart from the edge");
    }

    [Fact]
    public void RapidRetriggers_StayContinuousAndFinite()
    {
        var motion = new SplitSatelliteMotion();
        motion.BeginBirth(0, 0, 120, 40, 1, 0.33);
        var last = motion.Values;
        for (var i = 0; i < 240; i++)
        {
            if (i % 12 == 0)
            {
                motion.BeginBirth(0, 0, 120 + i, 40, 1, 0.33);
            }

            motion.Update(Frame);
            var now = motion.Values;
            Assert.True(double.IsFinite(now.X) && double.IsFinite(now.Scale));
            Assert.True(Math.Abs(now.X - last.X) < 12, "no teleporting between frames");
            last = now;
        }
    }

    [Fact]
    public void Breathing_IsPlusMinusOnePointTwoPercentOverFourSeconds()
    {
        var motion = BornToIdle(out _);
        var min = double.MaxValue;
        var max = double.MinValue;
        for (var t = 0.0; t < SatelliteMotionTokens.BreathPeriodSeconds; t += Frame)
        {
            motion.Update(Frame);
            min = Math.Min(min, motion.Values.BreathScale);
            max = Math.Max(max, motion.Values.BreathScale);
        }

        Assert.Equal(1 + SatelliteMotionTokens.BreathAmplitude, max, 3);
        Assert.Equal(1 - SatelliteMotionTokens.BreathAmplitude, min, 3);
    }

    [Fact]
    public void AgentRing_SpinsAtAConstantRateWhileIdle()
    {
        var motion = BornToIdle(out _);
        var start = motion.Values.RingAngleDegrees;
        for (var i = 0; i < 240; i++)
        {
            motion.Update(Frame);
        }

        var expected = 360 * SatelliteMotionTokens.AgentRingTurnsPerSecond;
        Assert.Equal(expected, motion.Values.RingAngleDegrees - start, 1);
    }

    /// <summary>
    /// Writes the sampled motion frames used by the visual report (build/reports/ui-audit/satellite).
    /// Skipped in normal runs; the audit runner sets FLUIDBAR_SATELLITE_EVIDENCE=1.
    /// </summary>
    [Fact]
    public void Emit_motion_frames()
    {
        if (Environment.GetEnvironmentVariable("FLUIDBAR_SATELLITE_EVIDENCE") != "1")
        {
            return;
        }

        var path = Environment.GetEnvironmentVariable("FLUIDBAR_SATELLITE_FRAMES")
            ?? Path.Combine(Path.GetTempPath(), "fluidbar-satellite-frames.csv");
        var lines = new List<string> { "phase,index,x,y,scale,stretchX,stretchY,highlight,ring" };
        void Sample(string phase, int index, SplitSatelliteValues v)
            => lines.Add(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0},{1},{2:0.###},{3:0.###},{4:0.####},{5:0.####},{6:0.####},{7:0.###},{8:0.#}",
                phase, index, v.X, v.Y, v.Scale, v.StretchX, v.StretchY, v.Highlight, v.RingAngleDegrees));

        // birth: 8 frames spread over the 0.47 s choreography
        var birth = new SplitSatelliteMotion();
        birth.BeginBirth(0, 0, 132, 26, 1, 0.2);
        var birthTimes = new[] { 0.0, 0.03, 0.06, 0.09, 0.13, 0.19, 0.27, 0.38 };
        var clock = 0.0;
        for (var i = 0; i < birthTimes.Length; i++)
        {
            while (clock < birthTimes[i])
            {
                birth.Update(Frame);
                clock += Frame;
            }

            Sample("birth", i, birth.Values);
        }

        // merge: 6 frames
        var merge = new SplitSatelliteMotion();
        merge.BeginBirth(0, 0, 132, 26, 1, 0.2);
        for (var i = 0; i < 400; i++)
        {
            merge.Update(Frame);
        }

        Sample("merge", 0, merge.Values);
        merge.BeginMerge(0, 0);
        var mergeTimes = new[] { 0.02, 0.08, 0.14, 0.20, 0.26 };
        clock = 0.0;
        for (var i = 0; i < mergeTimes.Length; i++)
        {
            while (clock < mergeTimes[i])
            {
                merge.Update(Frame);
                clock += Frame;
            }

            Sample("merge", i + 1, merge.Values);
        }

        // interruption: birth, then click at 0.14 s, then two rapid wheel retargets
        var rapid = new SplitSatelliteMotion();
        rapid.BeginBirth(0, 0, 132, 26, 1, 0.2);
        clock = 0.0;
        var cutTimes = new[] { 0.0, 0.06, 0.10, 0.14, 0.18, 0.24 };
        for (var i = 0; i < cutTimes.Length; i++)
        {
            while (clock < cutTimes[i])
            {
                rapid.Update(Frame);
                clock += Frame;
            }

            if (i == 3)
            {
                rapid.BeginMerge(0, 0);
            }
            else if (i == 4)
            {
                rapid.BeginBirth(0, 0, 150, 10, 1, -0.4);
            }

            Sample("interrupt", i, rapid.Values);
        }

        File.WriteAllLines(path, lines);
    }
}
