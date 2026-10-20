using FluidBar.Island;

namespace FluidBar.Island.Tests;

/// <summary>Extrapolating the 250 ms position sample into a per-frame playback position.</summary>
public sealed class MediaPositionClockTests
{
    [Fact]
    public void Position_advances_with_the_clock_while_playing()
    {
        var clock = new MediaPositionClock();
        clock.Anchor(10_000, nowMs: 1_000, playing: true);

        Assert.Equal(10_000, clock.PositionAt(1_000), 3);
        Assert.Equal(10_016, clock.PositionAt(1_016), 3);
        Assert.Equal(10_250, clock.PositionAt(1_250), 3);
        Assert.True(clock.IsAdvancing);
    }

    [Fact]
    public void Position_freezes_while_paused()
    {
        var clock = new MediaPositionClock();
        clock.Anchor(10_000, nowMs: 1_000, playing: false);

        Assert.Equal(10_000, clock.PositionAt(1_500), 3);
        Assert.Equal(10_000, clock.PositionAt(9_000), 3);
        Assert.False(clock.IsAdvancing);
    }

    [Fact]
    public void Pause_then_resume_does_not_jump()
    {
        var clock = new MediaPositionClock();
        clock.Anchor(10_000, nowMs: 1_000, playing: true);

        clock.SetPlaying(false, nowMs: 1_500);
        Assert.Equal(10_500, clock.PositionAt(2_000), 3);

        clock.SetPlaying(true, nowMs: 2_000);
        Assert.Equal(10_500, clock.PositionAt(2_000), 3);
        Assert.Equal(10_600, clock.PositionAt(2_100), 3);
    }

    [Fact]
    public void IsJump_detects_a_seek_and_ignores_small_drift()
    {
        var clock = new MediaPositionClock();
        clock.Anchor(10_000, nowMs: 1_000, playing: true);

        Assert.False(clock.IsJump(10_200, nowMs: 1_200));
        Assert.True(clock.IsJump(45_000, nowMs: 1_200));
        Assert.True(clock.IsJump(2_000, nowMs: 1_200));
    }

    [Fact]
    public void Anchor_after_a_seek_restarts_from_the_new_position()
    {
        var clock = new MediaPositionClock();
        clock.Anchor(10_000, nowMs: 1_000, playing: true);
        Assert.True(clock.IsJump(60_000, nowMs: 1_200));

        clock.Anchor(60_000, nowMs: 1_200, playing: true);
        Assert.Equal(60_000, clock.PositionAt(1_200), 3);
        Assert.Equal(60_050, clock.PositionAt(1_250), 3);
    }

    [Fact]
    public void Position_ignores_a_suspended_clock()
    {
        var clock = new MediaPositionClock();
        clock.Anchor(10_000, nowMs: 1_000, playing: true);

        // A machine that slept for an hour must not fast-forward the lyric sweep.
        Assert.Equal(10_000, clock.PositionAt(3_601_000), 3);
    }

    [Fact]
    public void Reset_drops_the_anchor()
    {
        var clock = new MediaPositionClock();
        clock.Anchor(10_000, nowMs: 1_000, playing: true);
        clock.Reset();

        Assert.False(clock.HasAnchor);
        Assert.False(clock.IsAdvancing);
        Assert.Equal(0, clock.PositionAt(2_000), 3);
    }

    [Fact]
    public void Position_is_capped_by_the_track_length()
    {
        var clock = new MediaPositionClock();
        clock.SetLimit(60_000);
        clock.Anchor(59_900, nowMs: 1_000, playing: true);

        Assert.Equal(60_000, clock.PositionAt(2_000), 3);
        Assert.Equal(60_000, clock.PositionAt(30_000), 3);
    }

    [Fact]
    public void Small_sample_disagreement_is_eased_out_instead_of_jumping_back()
    {
        var clock = new MediaPositionClock();
        clock.Anchor(10_000, nowMs: 1_000, playing: true);

        // The next sample is 200 ms behind the extrapolation (normal poll lag): not a seek.
        var snapped = clock.Reconcile(10_200, nowMs: 1_400, playing: true);
        Assert.False(snapped);
        var eased = clock.PositionAt(1_400);
        Assert.InRange(eased, 10_200, 10_500);
        // The drift is gone a decay window later, so the position does not stay offset.
        Assert.Equal(10_400 + 400, clock.PositionAt(1_800), 0);
    }

    [Fact]
    public void A_large_disagreement_snaps_and_is_reported_as_a_seek()
    {
        var clock = new MediaPositionClock();
        clock.Anchor(10_000, nowMs: 1_000, playing: true);

        var snapped = clock.Reconcile(45_000, nowMs: 1_200, playing: true);

        Assert.True(snapped);
        Assert.Equal(45_000, clock.PositionAt(1_200), 3);
    }

    [Fact]
    public void Extrapolation_keeps_running_while_the_source_stops_publishing()
    {
        // The player hides its time label: no new sample for 30 s, the position must keep moving.
        var clock = new MediaPositionClock();
        clock.Anchor(5_000, nowMs: 0, playing: true);

        Assert.Equal(35_000, clock.PositionAt(30_000), 3);
    }
}
