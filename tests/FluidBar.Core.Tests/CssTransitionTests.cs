using FluidBar.Core.Motion;

namespace FluidBar.Core.Tests;

/// <summary>
/// The prototype2 shell is a CSS transition, not a spring. These tests pin the browser-measured
/// <c>Animation.currentTime</c>/computed-value pairs captured with Playwright (Edge, 1440×900, DSF 1)
/// against <see cref="CubicBezierEasing"/> and <see cref="CssTransition"/>, so a future refactor cannot
/// silently change the shell trajectory.
/// </summary>
public sealed class CssTransitionTests
{
    /// <summary>Prototype2 width transition (132 → 440 px, 380 ms, cubic-bezier(.3,.8,.25,1)).</summary>
    public static TheoryData<double, double> ShellWidthSamples => new()
    {
        { 17.5, 170.594 },
        { 122.3, 374.625 },
        { 145.6, 395.562 },
        { 168.2, 409.391 },
        { 191.2, 419.141 },
        { 214.3, 426.000 },
        { 237.8, 430.906 },
        { 262.5, 434.500 },
        { 287.2, 436.922 },
        { 310.4, 438.422 },
        { 334.0, 439.359 },
        { 356.0, 439.828 },
        { 379.0, 439.984 },
    };

    /// <summary>Prototype2 height transition (36 → 284 px, 380 ms, same curve).</summary>
    public static TheoryData<double, double> ShellHeightSamples => new()
    {
        { 30.4, 90.688 },
        { 76.7, 174.156 },
        { 99.7, 207.469 },
        { 146.9, 248.984 },
        { 191.1, 267.172 },
        { 236.8, 276.531 },
        { 282.1, 281.172 },
        { 328.0, 283.328 },
        { 374.8, 283.984 },
    };

    /// <summary>Prototype2 expanded-layer fade (220 ms ease-out, 90 ms delay).</summary>
    public static TheoryData<double, double> PanelFadeSamples => new()
    {
        { 98.3, 0.147 },
        { 120.8, 0.488 },
        { 143.8, 0.710 },
        { 165.8, 0.832 },
        { 189.4, 0.907 },
        { 212.3, 0.950 },
        { 234.0, 0.974 },
        { 279.9, 0.997 },
        { 303.5, 1.000 },
    };

    /// <summary>Prototype2 chrome fade (160 ms ease-out, 90 ms delay).</summary>
    public static TheoryData<double, double> ChromeFadeSamples => new()
    {
        { 100.0, 0.239 },
        { 122.8, 0.640 },
        { 145.7, 0.835 },
        { 168.9, 0.927 },
        { 192.2, 0.971 },
        { 214.1, 0.991 },
        { 237.1, 0.999 },
    };

    /// <summary>Prototype2 compact-layer fade-in on collapse (140 ms ease-out, 40 ms delay).</summary>
    public static TheoryData<double, double> CompactFadeInSamples => new()
    {
        { 54.8, 0.387 },
        { 78.4, 0.754 },
        { 100.9, 0.898 },
        { 124.3, 0.963 },
        { 147.5, 0.990 },
        { 170.9, 0.999 },
    };

    [Theory]
    [MemberData(nameof(ShellWidthSamples))]
    public void Shell_curve_matches_the_browser_width_samples(double milliseconds, double expectedWidth)
    {
        var progress = MillisecondsToProgress(milliseconds, ShellTransition.ShellDurationSeconds);
        var eased = ShellTransition.ShellEasing.Evaluate(progress);
        var actual = 132 + (308 * eased);
        Assert.Equal(expectedWidth, actual, 0.05);
    }

    [Theory]
    [MemberData(nameof(ShellHeightSamples))]
    public void Shell_curve_matches_the_browser_height_samples(double milliseconds, double expectedHeight)
    {
        var progress = MillisecondsToProgress(milliseconds, ShellTransition.ShellDurationSeconds);
        var eased = ShellTransition.ShellEasing.Evaluate(progress);
        var actual = 36 + (248 * eased);
        Assert.Equal(expectedHeight, actual, 0.05);
    }

    [Theory]
    [MemberData(nameof(PanelFadeSamples))]
    public void Expanded_layer_easing_matches_the_browser_samples(double milliseconds, double expected)
    {
        var local = milliseconds - (ShellTransition.ExpandedFadeInDelaySeconds * 1000);
        var progress = MillisecondsToProgress(local, ShellTransition.ExpandedFadeInSeconds);
        var actual = ShellTransition.EaseOut.Evaluate(progress);
        Assert.Equal(expected, actual, 0.006);
    }

    [Theory]
    [MemberData(nameof(ChromeFadeSamples))]
    public void Chrome_easing_matches_the_browser_samples(double milliseconds, double expected)
    {
        var local = milliseconds - (ShellTransition.ChromeFadeInDelaySeconds * 1000);
        var progress = MillisecondsToProgress(local, ShellTransition.ChromeFadeSeconds);
        var actual = ShellTransition.EaseOut.Evaluate(progress);
        Assert.Equal(expected, actual, 0.006);
    }

    [Theory]
    [MemberData(nameof(CompactFadeInSamples))]
    public void Compact_layer_easing_matches_the_browser_samples(double milliseconds, double expected)
    {
        var local = milliseconds - (ShellTransition.CompactFadeInDelaySeconds * 1000);
        var progress = MillisecondsToProgress(local, ShellTransition.CompactFadeInSeconds);
        var actual = ShellTransition.EaseOut.Evaluate(progress);
        Assert.Equal(expected, actual, 0.006);
    }

    [Fact]
    public void Easing_is_clamped_at_the_endpoints_and_monotonic()
    {
        Assert.Equal(0, ShellTransition.ShellEasing.Evaluate(-1), 9);
        Assert.Equal(0, ShellTransition.ShellEasing.Evaluate(0), 9);
        Assert.Equal(1, ShellTransition.ShellEasing.Evaluate(1), 9);
        Assert.Equal(1, ShellTransition.ShellEasing.Evaluate(2), 9);

        var previous = 0.0;
        for (var i = 0; i <= 200; i++)
        {
            var value = ShellTransition.ShellEasing.Evaluate(i / 200.0);
            Assert.True(value >= previous - 1e-9, $"curve must not regress at x={i / 200.0}");
            previous = value;
        }
    }

    [Fact]
    public void Transition_holds_the_from_value_during_the_delay()
    {
        var transition = new CssTransition(0);
        transition.Retarget(1, ShellTransition.ExpandedFadeInSeconds, ShellTransition.EaseOut,
            ShellTransition.ExpandedFadeInDelaySeconds);

        Assert.True(transition.Step(0.05));
        Assert.Equal(0, transition.Value, 9);
        Assert.True(transition.Step(0.039));
        Assert.Equal(0, transition.Value, 9);
    }

    [Fact]
    public void Transition_reaches_the_exact_target_after_delay_plus_duration()
    {
        var transition = new CssTransition(1);
        transition.Retarget(0, ShellTransition.CompactFadeOutSeconds, ShellTransition.EaseIn);
        var elapsed = 0.0;
        while (!transition.IsAtRest && elapsed < 1)
        {
            transition.Step(1.0 / 120.0);
            elapsed += 1.0 / 120.0;
        }

        Assert.True(transition.IsAtRest);
        Assert.Equal(0, transition.Value, 9);
        Assert.True(elapsed < 0.11, $"90 ms fade settled at {elapsed:0.###} s");
    }

    [Fact]
    public void Retarget_from_mid_flight_continues_from_the_current_value()
    {
        var transition = new CssTransition(132);
        transition.Retarget(440, ShellTransition.ShellDurationSeconds, ShellTransition.ShellEasing);
        for (var i = 0; i < 12; i++)
        {
            transition.Step(1.0 / 120.0);
        }

        var midway = transition.Value;
        Assert.InRange(midway, 133, 439);
        transition.Retarget(132, ShellTransition.ShellDurationSeconds, ShellTransition.ShellEasing);
        Assert.True(transition.Step(1.0 / 120.0));
        Assert.True(transition.Value < midway, "the reversed transition must start from the live value");

        var elapsed = 0.0;
        while (!transition.IsAtRest && elapsed < 1)
        {
            transition.Step(1.0 / 120.0);
            elapsed += 1.0 / 120.0;
        }

        Assert.Equal(132, transition.Value, 6);
    }

    [Fact]
    public void Retarget_to_the_same_target_does_not_restart_the_transition()
    {
        var transition = new CssTransition(132);
        transition.Retarget(440, ShellTransition.ShellDurationSeconds, ShellTransition.ShellEasing);
        for (var i = 0; i < 24; i++)
        {
            transition.Step(1.0 / 120.0);
        }

        var before = transition.Value;
        transition.Retarget(440, 1.0, ShellTransition.EaseOut);
        Assert.True(transition.Step(1.0 / 120.0));
        Assert.True(transition.Value > before, "the original 380 ms trajectory must keep running");

        var elapsed = 0.0;
        while (!transition.IsAtRest && elapsed < 1)
        {
            transition.Step(1.0 / 120.0);
            elapsed += 1.0 / 120.0;
        }

        Assert.True(elapsed < 0.45, $"a same-target retarget must not restart the clock (settled after {elapsed:0.###} s)");
        Assert.Equal(440, transition.Value, 6);
    }

    [Fact]
    public void Non_positive_duration_snaps()
    {
        var transition = new CssTransition(10);
        transition.Retarget(20, 0, ShellTransition.ShellEasing);
        Assert.True(transition.IsAtRest);
        Assert.Equal(20, transition.Value, 9);
    }

    [Fact]
    public void Css_transition_is_frame_rate_independent()
    {
        double Run(double delta)
        {
            // Step a fixed 200 ms of wall time with a partial final step, so every frame rate samples the
            // exact same instant (the easing itself is a pure function of elapsed time).
            var transition = new CssTransition(132);
            transition.Retarget(440, ShellTransition.ShellDurationSeconds, ShellTransition.ShellEasing);
            var remaining = 0.2;
            while (remaining > 1e-12)
            {
                var step = Math.Min(delta, remaining);
                transition.Step(step);
                remaining -= step;
            }

            return transition.Value;
        }

        var at60 = Run(1.0 / 60.0);
        var at144 = Run(1.0 / 144.0);
        var at165 = Run(1.0 / 165.0);
        Assert.Equal(at60, at144, 1e-9);
        Assert.Equal(at60, at165, 1e-9);
    }

    [Fact]
    public void Tokens_are_the_prototype2_values()
    {
        Assert.Equal(0.380, ShellTransition.ShellDurationSeconds, 9);
        Assert.Equal(0.260, ShellTransition.RadiusDurationSeconds, 9);
        Assert.Equal(0.090, ShellTransition.CompactFadeOutSeconds, 9);
        Assert.Equal(0.140, ShellTransition.CompactFadeInSeconds, 9);
        Assert.Equal(0.040, ShellTransition.CompactFadeInDelaySeconds, 9);
        Assert.Equal(0.220, ShellTransition.ExpandedFadeInSeconds, 9);
        Assert.Equal(0.090, ShellTransition.ExpandedFadeInDelaySeconds, 9);
        Assert.Equal(0.140, ShellTransition.ExpandedFadeOutSeconds, 9);
        Assert.Equal(0.160, ShellTransition.ChromeFadeSeconds, 9);
        Assert.Equal(0.090, ShellTransition.ChromeFadeInDelaySeconds, 9);
        Assert.Equal(0.3, ShellTransition.ShellEasing.X1, 9);
        Assert.Equal(0.8, ShellTransition.ShellEasing.Y1, 9);
        Assert.Equal(0.25, ShellTransition.ShellEasing.X2, 9);
        Assert.Equal(1.0, ShellTransition.ShellEasing.Y2, 9);
    }

    private static double MillisecondsToProgress(double milliseconds, double durationSeconds)
    {
        var seconds = Math.Clamp(milliseconds / 1000.0, 0, durationSeconds);
        return durationSeconds <= 0 ? 1 : seconds / durationSeconds;
    }
}
