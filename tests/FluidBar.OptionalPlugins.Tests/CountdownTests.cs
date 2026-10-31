using FluidBar.Plugins.Timer;

namespace FluidBar.OptionalPlugins.Tests;

public sealed class CountdownTests
{
    [Fact]
    public void Pausing_freezes_remaining_time_and_resuming_finishes_once()
    {
        var time = new ManualTime();
        var timer = new Countdown(time);
        timer.Start(TimeSpan.FromSeconds(20));
        time.Advance(7);
        timer.Pause();
        time.Advance(50);
        Assert.Equal(TimeSpan.FromSeconds(13), timer.Read().Remaining);
        Assert.False(timer.Read().Running);
        timer.Resume();
        time.Advance(13);
        Assert.True(timer.Read().Completed);
        Assert.False(timer.Read().Running);
        Assert.Equal(TimeSpan.Zero, timer.Read().Remaining);
    }

    [Fact]
    public void Stop_and_restart_reset_progress()
    {
        var time = new ManualTime();
        var timer = new Countdown(time);
        timer.Start(TimeSpan.FromMinutes(5));
        time.Advance(20);
        timer.Stop();
        Assert.False(timer.Read().Running);
        Assert.False(timer.Read().Completed);
        timer.Start(TimeSpan.FromMinutes(2));
        Assert.Equal(TimeSpan.FromMinutes(2), timer.Read().Remaining);
        Assert.Equal(0, timer.Read().Progress);
    }

    private sealed class ManualTime : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;
        public void Advance(int seconds) => _timestamp += TimeSpan.FromSeconds(seconds).Ticks;
    }
}
