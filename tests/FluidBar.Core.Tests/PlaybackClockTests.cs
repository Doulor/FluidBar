using FluidBar.Core.Media;

namespace FluidBar.Core.Tests;

/// <summary>
/// Contract of the single playback clock: one anchor, one rate, and re-anchoring only on evidence.
/// </summary>
public sealed class PlaybackClockTests
{
    [Fact]
    public void Advances_with_the_audio_clock()
    {
        var clock = new PlaybackClock();
        clock.Anchor(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(0));

        var position = clock.Advance(TimeSpan.FromSeconds(105), TimeSpan.FromSeconds(5), audioIsReliable: true);

        Assert.Equal(TimeSpan.FromSeconds(35), position);
    }

    [Fact]
    public void Stops_while_the_audio_clock_is_still()
    {
        var clock = new PlaybackClock();
        clock.Anchor(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(50), TimeSpan.FromSeconds(0));
        clock.Advance(TimeSpan.FromSeconds(52), TimeSpan.FromSeconds(2), audioIsReliable: true);

        var position = clock.Advance(TimeSpan.FromSeconds(52), TimeSpan.FromSeconds(20), audioIsReliable: true);

        Assert.Equal(TimeSpan.FromSeconds(12), position);
    }

    [Fact]
    public void Falls_back_to_wall_time_when_the_audio_clock_is_missing()
    {
        var clock = new PlaybackClock();
        clock.Anchor(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(1000));

        var position = clock.Advance(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(1007), audioIsReliable: false);

        Assert.Equal(TimeSpan.FromSeconds(12), position);
    }

    [Fact]
    public void Ignores_readings_inside_the_deadband()
    {
        var clock = new PlaybackClock();
        clock.Anchor(TimeSpan.FromSeconds(40), TimeSpan.Zero, TimeSpan.Zero);

        var applied = clock.TryReanchor(
            TimeSpan.FromSeconds(40.2), TimeSpan.Zero, TimeSpan.Zero,
            TimeSpan.FromSeconds(0.4), TimeSpan.FromSeconds(30));

        Assert.False(applied);
        Assert.Equal(TimeSpan.FromSeconds(40), clock.Position);
    }

    [Fact]
    public void Adopts_a_reading_that_proves_it_is_off()
    {
        var clock = new PlaybackClock();
        clock.Anchor(TimeSpan.FromSeconds(40), TimeSpan.Zero, TimeSpan.Zero);

        var applied = clock.TryReanchor(
            TimeSpan.FromSeconds(42), TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(7),
            TimeSpan.FromSeconds(0.4), TimeSpan.FromSeconds(30));

        Assert.True(applied);
        Assert.Equal(TimeSpan.FromSeconds(42), clock.Position);
        Assert.Equal(TimeSpan.FromSeconds(44), clock.Advance(TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(9), true));
    }

    [Fact]
    public void Rejects_a_reading_that_is_implausibly_far_away()
    {
        var clock = new PlaybackClock();
        clock.Anchor(TimeSpan.FromSeconds(40), TimeSpan.Zero, TimeSpan.Zero);

        var applied = clock.TryReanchor(
            TimeSpan.FromSeconds(400), TimeSpan.Zero, TimeSpan.Zero,
            TimeSpan.FromSeconds(0.4), TimeSpan.FromSeconds(30));

        Assert.False(applied);
    }

    [Fact]
    public void Starts_at_the_beginning_of_a_song()
    {
        var clock = new PlaybackClock();
        clock.AnchorToTrackStart(TimeSpan.FromSeconds(500), TimeSpan.FromSeconds(1000));

        Assert.Equal(TimeSpan.FromSeconds(3), clock.Advance(TimeSpan.FromSeconds(503), TimeSpan.FromSeconds(1003), true));
    }
}
