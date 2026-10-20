using System.Collections.Immutable;
using FluidBar.App.State;
using FluidBar.Core.Activities;
using FluidBar.Island;

namespace FluidBar.Ui.Tests;

/// <summary>
/// The karaoke sweep must advance every rendered frame from an extrapolated position; waiting for the next
/// position sample made it step several characters at a time.
/// </summary>
public sealed class KaraokeSweepTests
{
    private static IslandContent MediaContent(double positionMs, bool playing) => new()
    {
        Media = new MediaSessionData
        {
            Title = "Dragonhearted",
            Artist = "TryHardNinja",
            Source = "KuGou",
            LyricLine = "Together we stand 30 feet tall",
            NextLyricLine = "We got a fire that burns within",
            LyricHighlight = 0,
            PositionMs = positionMs,
            IsPlaying = playing,
            HasTimeline = true,
            ProgressPercent = 10,
            Karaoke = KaraokeTrack.Create(
                lineStartMs: 0,
                lineDurationMs: 0,
                ImmutableArray.Create(new KaraokeSpan(0, 10), new KaraokeSpan(1000, 10), new KaraokeSpan(2000, 10)))!,
        },
    };

    private static ControllerFixture StartMedia(bool playing = true, double positionMs = 0)
    {
        var fixture = ControllerFixture.Create();
        fixture.Controller.PublishActivity(
            ActivityKind.Media,
            new ActivityPayload
            {
                Title = "Dragonhearted",
                Subtitle = "TryHardNinja",
                IsActive = true,
                ShowsAudioWave = true,
                LyricLine = "Together we stand 30 feet tall",
                SecondaryLyricLine = "We got a fire that burns within",
                ProgressPercent = 10,
            },
            sequence: 1);
        fixture.Data.Update(_ => MediaContent(positionMs, playing));
        fixture.Settle();
        return fixture;
    }

    [Fact]
    public Task Karaoke_sweep_advances_on_every_frame_while_playing()
    {
        return AvaloniaTest.Run(() =>
        {
            using var fixture = StartMedia();
            fixture.ExpandAndSettle();
            fixture.GoToPage(1);

            var first = fixture.Controller.RenderState.Pages.Media.LyricHighlight;
            Assert.True(fixture.Controller.IsKaraokeAnimating, "a playing per-word line has to keep frames coming");
            Assert.True(fixture.Controller.IsAnimating, "IsAnimating drives the host frame loop");

            // One host frame 40 ms later: the sweep has to move without a new position sample.
            fixture.Time.Advance(TimeSpan.FromMilliseconds(40));
            fixture.Controller.Advance(TimeSpan.FromMilliseconds(16));

            var second = fixture.Controller.RenderState.Pages.Media.LyricHighlight;
            Assert.True(second > first + 1e-6, $"{second} should advance past {first}");
            Assert.True(second - first < 0.05, $"one frame must not jump ({first} -> {second})");

            // ...and it keeps advancing frame after frame.
            fixture.Time.Advance(TimeSpan.FromMilliseconds(40));
            fixture.Controller.Advance(TimeSpan.FromMilliseconds(16));
            var third = fixture.Controller.RenderState.Pages.Media.LyricHighlight;
            Assert.True(third > second + 1e-6, $"{third} should advance past {second}");
        });
    }

    [Fact]
    public Task Karaoke_sweep_freezes_while_paused()
    {
        return AvaloniaTest.Run(() =>
        {
            using var fixture = StartMedia(playing: false, positionMs: 500);
            fixture.ExpandAndSettle();
            fixture.GoToPage(1);

            var first = fixture.Controller.RenderState.Pages.Media.LyricHighlight;
            fixture.Time.Advance(TimeSpan.FromMilliseconds(200));
            fixture.Controller.Advance(TimeSpan.FromMilliseconds(16));

            Assert.Equal(first, fixture.Controller.RenderState.Pages.Media.LyricHighlight, 6);
            Assert.False(fixture.Controller.IsKaraokeAnimating);
        });
    }
}
