using FluidBar.Island;

namespace FluidBar.Island.Tests;

/// <summary>
/// Prototype2 agent motion sampled with playwright (Web Animations currentTime) from
/// <c>http://127.0.0.1:8123/index.html?state=expanded&amp;tab=agent&amp;arch=1</c>:
/// <list type="bullet">
/// <item><c>spin 900ms linear infinite</c> — clockwise, 360°/900 ms; the top+right border is a 180° arc.</item>
/// <item><c>progressPulse 2.4s ease-in-out infinite alternate</c> — width 48% → 72% → 48% (4.8 s round trip).</item>
/// </list>
/// </summary>
public sealed class AgentMotionTests
{
    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.1, 40.0)]
    [InlineData(0.2, 80.0)]
    [InlineData(0.3, 120.0)]
    [InlineData(0.45, 180.0)]
    [InlineData(0.9, 0.0)]
    [InlineData(1.35, 180.0)]
    [InlineData(-0.1, 320.0)]
    [InlineData(9.9, 0.0)]
    public void Ring_rotation_matches_the_900ms_linear_spin(double seconds, double expectedDegrees)
    {
        Assert.Equal(expectedDegrees, AgentMotion.RingRotationDegrees(seconds), 3);
    }

    [Fact]
    public void Ring_rotation_ignores_non_finite_values()
    {
        Assert.Equal(0.0, AgentMotion.RingRotationDegrees(double.NaN));
        Assert.Equal(0.0, AgentMotion.RingRotationDegrees(double.PositiveInfinity));
    }

    /// <summary>
    /// Width samples from playwright (row is 400 DIP wide): 0 ms 192 px, 100 ms 192.312 px, 200 ms
    /// 193.297 px, 300 ms 194.984 px, 450 ms 198.859 px, 900 ms 220.094 px, 1200 ms 240 px,
    /// 2400 ms 288 px (alternate end), 4800 ms 192 px.
    /// </summary>
    [Theory]
    [InlineData(0.0, 48.0)]
    [InlineData(0.1, 48.078)]
    [InlineData(0.2, 48.324)]
    [InlineData(0.3, 48.746)]
    [InlineData(0.45, 49.715)]
    [InlineData(0.9, 55.024)]
    [InlineData(1.2, 60.0)]
    [InlineData(2.4, 72.0)]
    [InlineData(4.8, 48.0)]
    [InlineData(7.2, 72.0)]
    public void Indeterminate_progress_matches_the_2_4s_alternate_pulse(double seconds, double expectedPercent)
    {
        // Sub-0.1% agreement with Chromium's sampled widths (the easing solver differs slightly from
        // the browser's, so compare position rather than exact floats).
        Assert.True(Math.Abs(expectedPercent - AgentMotion.IndeterminateProgressPercent(seconds)) < 0.1,
            $"t={seconds}s: expected {expectedPercent:0.###}%, got {AgentMotion.IndeterminateProgressPercent(seconds):0.###}%");
    }

    [Fact]
    public void Indeterminate_progress_is_symmetric_and_bounded()
    {
        for (var t = 0.0; t <= 4.8; t += 0.1)
        {
            var value = AgentMotion.IndeterminateProgressPercent(t);
            Assert.InRange(value, AgentMotion.ProgressMinFraction * 100, AgentMotion.ProgressMaxFraction * 100);
            Assert.Equal(value, AgentMotion.IndeterminateProgressPercent(4.8 - t), 2);
        }
    }

    [Fact]
    public void Motion_constants_match_the_measured_prototype()
    {
        Assert.Equal(0.9, AgentMotion.RingPeriodSeconds, 6);
        Assert.Equal(2.4, AgentMotion.ProgressHalfPeriodSeconds, 6);
        Assert.Equal(180.0, AgentMotion.ArcSweepDegrees, 6);
        Assert.Equal(1.5, AgentMotion.RingStrokeWidth, 6);
        Assert.Equal(0.48, AgentMotion.ProgressMinFraction, 6);
        Assert.Equal(0.72, AgentMotion.ProgressMaxFraction, 6);
        Assert.Equal(0.75, AgentMotion.ProgressOpacity, 6);
    }
}
