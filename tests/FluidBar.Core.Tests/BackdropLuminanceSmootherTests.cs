using FluidBar.Core.Interaction;

namespace FluidBar.Core.Tests;

/// <summary>
/// Backdrop luminance smoothing: the glass tint must follow a desktop that changes brightness, not jump.
/// </summary>
public sealed class BackdropLuminanceSmootherTests
{
    [Fact]
    public void Value_moves_towards_the_target_gradually()
    {
        var smoother = new BackdropLuminanceSmoother(initial: 0, timeConstantMs: 160);
        var after = smoother.Step(1.0, 16);
        Assert.True(after > 0 && after < 0.2, $"first frame should move a little, got {after:0.000}");
        Assert.False(smoother.IsSettled);
    }

    [Fact]
    public void Value_settles_within_a_few_time_constants()
    {
        var smoother = new BackdropLuminanceSmoother(initial: 0, timeConstantMs: 160);
        for (var i = 0; i < 60; i++)
        {
            smoother.Step(1.0, 16);
        }

        Assert.True(smoother.IsSettled);
        Assert.Equal(1.0, smoother.Value, 3);
    }

    [Fact]
    public void Value_is_monotonic_and_frame_rate_independent()
    {
        var slow = new BackdropLuminanceSmoother(initial: 0, timeConstantMs: 160);
        var fast = new BackdropLuminanceSmoother(initial: 0, timeConstantMs: 160);

        for (var i = 0; i < 10; i++)
        {
            slow.Step(1.0, 33);
        }

        for (var i = 0; i < 20; i++)
        {
            fast.Step(1.0, 16.5);
        }

        Assert.True(Math.Abs(slow.Value - fast.Value) < 0.02, $"{slow.Value:0.000} vs {fast.Value:0.000}");
    }

    [Fact]
    public void Reset_jumps_straight_to_the_value()
    {
        var smoother = new BackdropLuminanceSmoother(initial: 0.1);
        smoother.Reset(0.9);
        Assert.Equal(0.9, smoother.Value, 3);
        Assert.True(smoother.IsSettled);
    }
}
