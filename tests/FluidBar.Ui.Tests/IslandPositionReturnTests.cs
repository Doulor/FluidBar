using FluidBar.App.Hosting.Skia;
using FluidBar.Core.Layout;

namespace FluidBar.Ui.Tests;

public sealed class IslandPositionReturnTests
{
    [Fact]
    public void HoldsThenReturnsWithSmallOvershootAndSettlesExactly()
    {
        var motion = new IslandPositionReturn();
        motion.Start(new VisualPoint(600, 400), reducedMotion: false);
        motion.Step(TimeSpan.FromSeconds(1), false);
        Assert.Equal(new VisualPoint(600, 400), motion.Offset);
        var crossed = false;
        var last = motion.Offset;
        for (var frame = 0; frame < 180; frame++)
        {
            motion.Step(TimeSpan.FromSeconds(1.0 / 60), false);
            crossed |= motion.Offset.X < 0;
            Assert.InRange(Math.Abs(motion.Offset.X - last.X), 0, 60);
            Assert.True(double.IsFinite(motion.Offset.X));
            last = motion.Offset;
        }
        Assert.True(crossed);
        Assert.False(motion.IsActive);
        Assert.Equal(new VisualPoint(0, 0), motion.Offset);
    }

    [Fact]
    public void ReducedMotionReturnsWithoutOvershoot()
    {
        var motion = new IslandPositionReturn();
        motion.Start(new VisualPoint(600, 400), reducedMotion: true);
        for (var frame = 0; frame < 240; frame++)
        {
            motion.Step(TimeSpan.FromSeconds(1.0 / 60), true);
            Assert.True(motion.Offset.X >= 0 && motion.Offset.Y >= 0);
        }
        Assert.False(motion.IsActive);
        Assert.Equal(new VisualPoint(0, 0), motion.Offset);
    }

    [Fact]
    public void SuspensionFreezesMotionAndResumingRestartsTheHold()
    {
        var motion = new IslandPositionReturn();
        motion.Start(new VisualPoint(180, 120), false);
        motion.Suspend();
        motion.Step(TimeSpan.FromSeconds(10), false);
        Assert.Equal(new VisualPoint(180, 120), motion.Offset);
        Assert.False(motion.NeedsFrames);
        motion.Resume();
        motion.Step(TimeSpan.FromSeconds(1.1), false);
        Assert.Equal(new VisualPoint(180, 120), motion.Offset);
        Assert.True(motion.NeedsFrames);
        motion.Step(TimeSpan.FromSeconds(0.2), false);
        Assert.True(motion.Offset.X < 180);
        motion.Cancel();
        Assert.False(motion.IsActive);
        Assert.Equal(new VisualPoint(0, 0), motion.Offset);
    }

    [Fact]
    public void TrajectoryIsConsistentAtSixtyAndOneHundredFortyFourFramesPerSecond()
    {
        static VisualPoint Sample(int fps)
        {
            var motion = new IslandPositionReturn();
            motion.Start(new VisualPoint(600, 400), false);
            motion.Step(TimeSpan.FromSeconds(IslandPositionReturn.HoldSeconds), false);
            for (var frame = 0; frame < fps / 2; frame++)
                motion.Step(TimeSpan.FromSeconds(1.0 / fps), false);
            return motion.Offset;
        }
        var sixty = Sample(60);
        var high = Sample(144);
        Assert.InRange(Math.Abs(sixty.X - high.X), 0, 0.5);
        Assert.InRange(Math.Abs(sixty.Y - high.Y), 0, 0.5);
    }
}
