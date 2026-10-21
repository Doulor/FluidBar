using FluidBar.Island;
using Xunit;

namespace FluidBar.Island.Tests;

/// <summary>
/// Motion polish for the split satellites: 120/160 ms hover fade + 1.02 pop, 80 ms release
/// overshoot, the fixed breathing-before-interaction composition order and the 60 ms birth stagger.
/// </summary>
public sealed class SplitSatelliteMotionPolishTests
{
    private const double Frame = 1.0 / 60.0;

    [Fact]
    public void Hover_fades_in_over_120ms_and_out_over_160ms()
    {
        var motion = Settled();
        motion.SetHover(true);

        Step(motion, 0.060);
        // 50 % through the 120 ms ease-out fade: 1 - (1 - 0.5)^2 = 0.75 of the 0.06 highlight.
        Assert.Equal(0.75 * SatelliteMotionTokens.HoverHighlight, motion.Values.Highlight, 0.004);
        var half = motion.Values;
        Assert.Equal(1 + ((SatelliteMotionTokens.HoverScale - 1) * 0.75), half.Scale / half.BreathScale, 0.004);

        Step(motion, 0.060);
        Assert.Equal(SatelliteMotionTokens.HoverHighlight, motion.Values.Highlight, 0.002);
        var full = motion.Values;
        Assert.Equal(SatelliteMotionTokens.HoverScale, full.Scale / full.BreathScale, 0.002);
        Assert.False(motion.IsAnimating);

        motion.SetHover(false);
        Step(motion, 0.080);
        Assert.Equal(0.75 * SatelliteMotionTokens.HoverHighlight, motion.Values.Highlight, 0.004);
        Step(motion, 0.080);
        Assert.Equal(0, motion.Values.Highlight, 0.002);
        var after = motion.Values;
        Assert.Equal(1, after.Scale / after.BreathScale, 0.002);
        Assert.Equal(SplitSatelliteStyle.HoverFadeOutSeconds > SplitSatelliteStyle.HoverFadeInSeconds, true);
    }

    [Fact]
    public void Release_overshoots_to_1_02_and_settles_within_80ms()
    {
        var motion = Settled();
        motion.Press();
        Step(motion, 0.30);
        Assert.Equal(0.94, motion.Values.Scale, 0.005);
        Assert.Equal(1, motion.Values.PressDepth, 0.01);

        motion.Release();
        var peak = 1.0;
        var frames = 0;
        while (frames < 8)
        {
            Step(motion, Frame);
            frames++;
            peak = System.Math.Max(peak, motion.Values.Scale);
        }

        Assert.InRange(peak, 1.015, 1.025);            // the 1.02 overshoot is actually reached
        Assert.Equal(1.0, motion.Values.Scale, 0.006); // and it has settled back by 80 ms
        Assert.Equal(0, motion.Values.PressDepth, 0.01); // the inner shadow faded out with it
    }

    [Fact]
    public void Breathing_is_applied_before_the_interaction_scale()
    {
        var motion = Settled();
        motion.SetHover(true);
        motion.Press();
        Step(motion, 0.60);

        var values = motion.Values;
        Assert.Equal(values.BreathScale * SatelliteMotionTokens.HoverScale * 0.94, values.Scale, 1e-6);
        Assert.True(System.Math.Abs(values.BreathScale - 1) > 1e-6, "idle breathing must be active");
    }

    [Fact]
    public void Two_satellites_stagger_their_birth_by_60ms()
    {
        var first = new SplitSatelliteMotion();
        var second = new SplitSatelliteMotion();
        first.BeginBirth(0, 0, 40, 0, 1, 0, order: 0);
        second.BeginBirth(0, 0, 40, 0, 1, 0, order: 1);

        // 90 ms in: the first satellite has finished its 60 ms preload and is already travelling,
        // the second is still inside its 60 ms stagger + 60 ms preload (i.e. has not moved at all).
        Step(first, 0.090);
        Step(second, 0.090);
        Assert.True(first.Values.X > 0.5, "the first satellite starts immediately");
        Assert.Equal(0, second.Values.X, 0.001);

        Step(first, 0.060);
        Step(second, 0.060);
        Assert.True(second.Values.X > 0.01, "the second satellite starts 60 ms later");
    }

    [Fact]
    public void Merges_collapse_in_reverse_birth_order()
    {
        var first = Settled();
        var second = Settled();
        first.BeginMerge(0, 0, order: 1);    // born first -> collapses last
        second.BeginMerge(0, 0, order: 0);   // born second -> collapses first

        Step(first, 0.030);
        Step(second, 0.030);
        Assert.True(second.Values.X < 39.5, "the later-born satellite folds in first");
        Assert.Equal(40, first.Values.X, 0.001);
    }

    private static void Step(SplitSatelliteMotion motion, double seconds)
    {
        var remaining = seconds;
        while (remaining > 0)
        {
            var step = System.Math.Min(Frame, remaining);
            motion.Update(step);
            remaining -= step;
        }
    }

    private static SplitSatelliteMotion Settled()
    {
        var motion = new SplitSatelliteMotion();
        motion.BeginBirth(0, 0, 40, 0, 1, 0);
        for (var i = 0; i < 120; i++)
        {
            motion.Update(Frame);
        }

        return motion;
    }
}