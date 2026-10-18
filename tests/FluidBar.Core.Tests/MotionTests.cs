using FluidBar.Core.Diagnostics;
using FluidBar.Core.Motion;

namespace FluidBar.Core.Tests;

public class MotionTests
{
    public static TheoryData<string, double, double> TokenData()
    {
        var data = new TheoryData<string, double, double>();
        foreach (var token in MotionTokensNamed.All)
        {
            data.Add(token.Item1, token.Item2, token.Item3);
        }

        return data;
    }

    [Fact]
    public void Tokens_MatchSpecTable()
    {
        AssertSpring(MotionTokens.Expand, 0.36, 0.82);
        AssertSpring(MotionTokens.ExpandHeight, 0.39, 0.86);
        AssertSpring(MotionTokens.Collapse, 0.30, 1.00);
        AssertSpring(MotionTokens.Morph, 0.28, 0.85);
        AssertSpring(MotionTokens.Hover, 0.24, 0.70);
        AssertSpring(MotionTokens.Press, 0.18, 0.65);
        AssertSpring(MotionTokens.Tab, 0.34, 0.92);
        AssertSpring(MotionTokens.Nudge, 0.26, 0.55);
        AssertSpring(MotionTokens.Hide, 0.30, 0.95);

        Assert.Equal(9, MotionTokensNamed.All.Count);
    }

    private static void AssertSpring(MotionToken token, double period, double zeta)
    {
        Assert.Equal(period, token.Spring.PeriodSeconds, 1e-9);
        Assert.Equal(zeta, token.Spring.DampingRatio, 1e-9);
        Assert.Equal(Math.Pow(2 * Math.PI / period, 2), token.Spring.Stiffness, 0.5);
        Assert.Equal(4 * Math.PI * zeta / period, token.Spring.Damping, 0.1);
    }

    [Fact]
    public void Parameters_DeriveClassicalKAndC()
    {
        var expand = MotionTokens.Expand.Spring;
        Assert.Equal(304.6, expand.Stiffness, 0.5);
        Assert.Equal(28.6, expand.Damping, 0.1);

        var collapse = MotionTokens.Collapse.Spring;
        Assert.Equal(438.6, collapse.Stiffness, 0.5);
        Assert.Equal(41.9, collapse.Damping, 0.1);

        var press = MotionTokens.Press.Spring;
        Assert.Equal(1218.5, press.Stiffness, 1.5);
        Assert.Equal(45.4, press.Damping, 0.2);
    }

    [Fact]
    public void Parameters_RoundTripFromKAndC()
    {
        var expand = MotionTokens.Expand.Spring;
        var roundTrip = SpringParameters.FromStiffnessDamping(expand.Stiffness, expand.Damping);
        Assert.Equal(expand.PeriodSeconds, roundTrip.PeriodSeconds, 1e-6);
        Assert.Equal(expand.DampingRatio, roundTrip.DampingRatio, 1e-6);
    }

    [Theory]
    [MemberData(nameof(TokenData))]
    public void Spring_SettlesWithin20PercentOfPredictedEnvelope(string name, double period, double zeta)
    {
        Assert.False(string.IsNullOrWhiteSpace(name));
        var spring = new Spring(new SpringParameters(period, zeta));
        const double amplitude = 25;
        const double frame = 1.0 / 240.0;
        spring.Retarget(amplitude, frame);

        // Settling to the 2 % envelope: the last frame after which the spring never leaves
        // |x - target| < 0.5 DIP (0.5 = 2 % of the 25 DIP test amplitude).
        var elapsed = 0.0;
        var lastOutside = 0.0;
        while (!spring.IsAtRest && elapsed < 5)
        {
            var outside = Math.Abs(spring.Value - amplitude) >= 0.5;
            spring.Step(frame);
            elapsed += frame;
            if (outside)
            {
                lastOutside = elapsed;
            }
        }

        Assert.True(spring.IsAtRest);
        var predicted = spring.Parameters.PredictedSettleSeconds;
        Assert.InRange(lastOutside, predicted * 0.8, predicted * 1.2);
        Assert.True(lastOutside * 1000 < MotionDurations.Default.MaxAnimationMs + 1);
    }

    [Fact]
    public void Spring_EventuallySnapsAndStops()
    {
        var spring = new Spring(MotionTokens.Expand.Spring);
        spring.Retarget(440, 1.0 / 120.0);
        var frames = 0;
        while (!spring.IsAtRest && frames < 2000)
        {
            spring.Step(1.0 / 120.0);
            frames++;
        }

        Assert.True(spring.IsAtRest);
        Assert.Equal(440, spring.Value);
        Assert.Equal(0, spring.Velocity);
        Assert.InRange(frames, 2, 600);
    }

    [Fact]
    public void Spring_FixedSubsteps_DtClamped()
    {
        var spring = new Spring(MotionTokens.Expand.Spring);
        spring.Retarget(100, 1.0 / 120.0);
        var before = spring.Substeps;
        spring.Step(10.0); // clamped to 50 ms => 15 substeps of 1/300
        Assert.Equal(15, spring.Substeps - before);
        Assert.True(double.IsFinite(spring.Value));

        before = spring.Substeps;
        spring.Step(-1.0); // clamped to 1 ms => 1 substep
        Assert.Equal(1, spring.Substeps - before);
    }

    [Fact]
    public void Spring_FrameRateIndependent()
    {
        var sixty = new Spring(MotionTokens.Expand.Spring);
        var hundredFortyFour = new Spring(MotionTokens.Expand.Spring);
        sixty.Retarget(100, 1.0 / 60.0);
        hundredFortyFour.Retarget(100, 1.0 / 144.0);
        for (var i = 0; i < 18; i++)
        {
            sixty.Step(1.0 / 60.0);
        }

        for (var i = 0; i < 43; i++)
        {
            hundredFortyFour.Step(1.0 / 144.0);
        }

        Assert.InRange(Math.Abs(sixty.Value - hundredFortyFour.Value), 0, 2.0);
    }

    [Fact]
    public void Retarget_SameDirection_KeepsVelocity()
    {
        var spring = new Spring(MotionTokens.Expand.Spring, value: 0, velocity: 10);
        spring.Retarget(200, 1.0 / 120.0);
        Assert.Equal(10, spring.Velocity, 1e-9);
        Assert.Equal(200, spring.Target);
    }

    [Fact]
    public void Retarget_OppositeDirection_DampsVelocity()
    {
        var spring = new Spring(MotionTokens.Expand.Spring, value: 0, velocity: 10);
        spring.Retarget(-100, 1.0 / 120.0);
        Assert.Equal(10 * Spring.OppositeDirectionRetention, spring.Velocity, 1e-9);
    }

    [Fact]
    public void Retarget_ClampsVelocityNearTarget()
    {
        var spring = new Spring(MotionTokens.Expand.Spring, value: 0, velocity: 10);
        spring.Retarget(1, 0.05);
        const double expected = Spring.MaxDistanceRatioPerDelta * 1 / 0.05;
        Assert.Equal(expected, spring.Velocity, 1e-9);
    }

    [Fact]
    public void Retarget_SnapsWhenAlreadyThere()
    {
        var spring = new Spring(MotionTokens.Expand.Spring, value: 10, velocity: 0.2);
        spring.Retarget(10.2, 1.0 / 120.0);
        Assert.True(spring.IsAtRest);
        Assert.Equal(10.2, spring.Value);
    }

    [Fact]
    public void SnapTo_ResetsVelocity()
    {
        var spring = new Spring(MotionTokens.Expand.Spring);
        spring.Retarget(100, 1.0 / 120.0);
        spring.Step(1.0 / 60.0);
        spring.SnapTo(50);
        Assert.True(spring.IsAtRest);
        Assert.Equal(50, spring.Value);
        Assert.Equal(0, spring.Velocity);
    }

    [Fact]
    public void SpringSet_StepsOnlyActiveSpringsAndReportsAllAtRest()
    {
        var set = new SpringSet();
        var active = set.Add(new Spring(MotionTokens.Expand.Spring));
        var resting = set.Add(new Spring(MotionTokens.Hover.Spring, value: 5));
        active.Retarget(100, 1.0 / 120.0);

        Assert.False(set.AllAtRest);
        Assert.Equal(1, set.ActiveCount);
        set.Step(1.0 / 120.0);
        Assert.Equal(5, resting.Value);
        set.SnapAllToTargets();
        Assert.True(set.AllAtRest);
    }

    [Fact]
    public void ManualFrameClock_OnlyPumpsWhenRunning()
    {
        var clock = new ManualFrameClock();
        var frames = 0;
        Assert.False(clock.Pump(TimeSpan.FromMilliseconds(16)));
        clock.Start(_ => frames++);
        Assert.True(clock.Pump(TimeSpan.FromMilliseconds(16)));
        Assert.Equal(1, frames);
        clock.Stop();
        Assert.False(clock.Pump(TimeSpan.FromMilliseconds(16)));
    }

    [Fact]
    public void MotionLoop_StepsSpringsAndStopsAtRest()
    {
        var clock = new ManualFrameClock();
        var counters = new DiagnosticCounters();
        using var loop = new MotionLoop(clock, counters: counters);
        var spring = loop.Springs.Add(new Spring(MotionTokens.Hover.Spring));
        spring.Retarget(10, 1.0 / 120.0);
        loop.Start();
        Assert.True(loop.IsRunning);

        var frames = clock.Pump(240, TimeSpan.FromMilliseconds(1000.0 / 120));
        Assert.False(loop.IsRunning);
        Assert.True(frames > 0);
        Assert.Equal(10, spring.Value);
        Assert.True(counters.Get(CounterKind.MotionFrames) > 0);
        Assert.True(counters.Get(CounterKind.MotionSettles) >= 1);
    }

    [Fact]
    public void MotionLoop_DisposeStopsClock()
    {
        var clock = new ManualFrameClock();
        var loop = new MotionLoop(clock);
        loop.Start();
        loop.Dispose();
        Assert.False(clock.IsRunning);
    }

    [Fact]
    public void ContentOpacity_DerivedFromHeight()
    {
        Assert.Equal(0, ContentOpacityPolicy.ForExpandedContent(0.62 * 284, 284), 1e-9);
        Assert.Equal(1, ContentOpacityPolicy.ForExpandedContent(284, 284), 1e-9);
        Assert.Equal(0.5, ContentOpacityPolicy.ForExpandedContent((0.62 * 284) + (0.175 * 284), 284), 1e-9);
        Assert.Equal(1, ContentOpacityPolicy.ForExpandedContent(1000, 284), 1e-9);
        Assert.Equal(0, ContentOpacityPolicy.ForExpandedContent(10, 284), 1e-9);
        Assert.Equal(1, ContentOpacityPolicy.ForCompactContent(0, 284), 1e-9);
        Assert.Equal(0, ContentOpacityPolicy.ForCompactContent(284, 284), 1e-9);
    }

    [Fact]
    public void MotionProfile_ReducedMotionHalvesAndCriticallyDamps()
    {
        var reduced = new MotionProfile { ReducedMotion = true };
        var resolved = reduced.ResolveParameters(MotionTokens.Expand);
        Assert.Equal(0.18, resolved.PeriodSeconds, 1e-9);
        Assert.Equal(1.0, resolved.DampingRatio, 1e-9);

        Assert.Equal(0, reduced.NudgeAmplitude(6));
        Assert.False(reduced.AllowsBloom);
        Assert.False(reduced.AllowsSpin);
        Assert.Equal(110, reduced.ScaleDurationMs(220));

        var normal = new MotionProfile();
        Assert.Equal(6, normal.NudgeAmplitude(6));
        Assert.True(normal.AllowsBloom);
        Assert.Equal(220, normal.ScaleDurationMs(220));
    }

    [Fact]
    public void MotionProfile_StrengthScalesPeriod()
    {
        var fassst = new MotionProfile { Strength = 1.5 };
        var slow = new MotionProfile { Strength = 0.5 };
        Assert.Equal(0.36 / 1.5, fassst.ResolveParameters(MotionTokens.Expand).PeriodSeconds, 1e-9);
        Assert.Equal(0.36 / 0.5, slow.ResolveParameters(MotionTokens.Expand).PeriodSeconds, 1e-9);

        var clampedHigh = new MotionProfile { Strength = 99 };
        Assert.Equal(MotionProfile.MaxStrength, clampedHigh.NormalizedStrength, 1e-9);
    }

    [Fact]
    public void SquashStretch_ClampsToSpecBounds()
    {
        var rest = SquashStretchPolicy.FromWidthVelocity(0);
        Assert.Equal(1, rest.WidthScale, 1e-9);
        Assert.Equal(1, rest.HeightScale, 1e-9);

        var fast = SquashStretchPolicy.FromWidthVelocity(10_000);
        Assert.Equal(1 + SquashStretchPolicy.MaxWidthGrowth, fast.WidthScale, 1e-9);
        Assert.Equal(1 - SquashStretchPolicy.MaxHeightShrink, fast.HeightScale, 1e-9);

        var negative = SquashStretchPolicy.FromWidthVelocity(-10_000);
        Assert.Equal(1 - SquashStretchPolicy.MaxWidthShrink, negative.WidthScale, 1e-9);
        Assert.Equal(1 + SquashStretchPolicy.MaxHeightGrowth, negative.HeightScale, 1e-9);
    }

    [Fact]
    public void MotionDurations_EnforceHardCeiling()
    {
        var durations = MotionDurations.Default;
        Assert.Equal(TimeSpan.FromMilliseconds(2000), durations.Clamp(TimeSpan.FromSeconds(10)));
        Assert.Equal(TimeSpan.FromMilliseconds(180), durations.Clamp(TimeSpan.FromMilliseconds(180)));
        Assert.Equal(TimeSpan.Zero, durations.Clamp(TimeSpan.FromMilliseconds(-5)));
        Assert.Equal(5000, durations.MaxAnimationMs + 3000);
    }

    private static class MotionTokensNamed
    {
        /// <summary>Name/period/zeta tuples for the settle-time theory.</summary>
        public static readonly IReadOnlyList<(string, double, double)> All =
        [
            (MotionTokens.Expand.Name, MotionTokens.Expand.Spring.PeriodSeconds, MotionTokens.Expand.Spring.DampingRatio),
            (MotionTokens.ExpandHeight.Name, MotionTokens.ExpandHeight.Spring.PeriodSeconds, MotionTokens.ExpandHeight.Spring.DampingRatio),
            (MotionTokens.Collapse.Name, MotionTokens.Collapse.Spring.PeriodSeconds, MotionTokens.Collapse.Spring.DampingRatio),
            (MotionTokens.Morph.Name, MotionTokens.Morph.Spring.PeriodSeconds, MotionTokens.Morph.Spring.DampingRatio),
            (MotionTokens.Hover.Name, MotionTokens.Hover.Spring.PeriodSeconds, MotionTokens.Hover.Spring.DampingRatio),
            (MotionTokens.Press.Name, MotionTokens.Press.Spring.PeriodSeconds, MotionTokens.Press.Spring.DampingRatio),
            (MotionTokens.Tab.Name, MotionTokens.Tab.Spring.PeriodSeconds, MotionTokens.Tab.Spring.DampingRatio),
            (MotionTokens.Nudge.Name, MotionTokens.Nudge.Spring.PeriodSeconds, MotionTokens.Nudge.Spring.DampingRatio),
            (MotionTokens.Hide.Name, MotionTokens.Hide.Spring.PeriodSeconds, MotionTokens.Hide.Spring.DampingRatio),
        ];
    }
}
