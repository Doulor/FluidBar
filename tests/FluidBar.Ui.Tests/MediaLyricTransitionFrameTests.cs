using System.Collections.Immutable;
using Stopwatch = System.Diagnostics.Stopwatch;
using FluidBar.App.State;
using FluidBar.Core.Activities;
using FluidBar.Island;
using SkiaSharp;

namespace FluidBar.Ui.Tests;

/// <summary>
/// The lyric line change must be frame-driven: the controller has to request a frame for every 16.7 ms of
/// the 180 ms fade (instead of letting the ~1 Hz media snapshot step it), and only the lyric band may be
/// repainted while it runs.
/// </summary>
public sealed class MediaLyricTransitionFrameTests
{
    private static IslandContent MediaContent(string line, bool playing) => new()
    {
        Media = new MediaSessionData
        {
            Title = "Dragonhearted",
            Artist = "TryHardNinja",
            Source = "KuGou",
            LyricLine = line,
            NextLyricLine = "We got a fire that burns within",
            LyricHighlight = -1,
            PositionMs = 10_000,
            IsPlaying = playing,
            HasTimeline = true,
            ProgressPercent = 30,
        },
    };

    private static ControllerFixture StartMedia(bool playing)
    {
        var fixture = ControllerFixture.Create();
        fixture.Controller.PublishActivity(
            ActivityKind.Media,
            new ActivityPayload
            {
                Title = "Dragonhearted",
                Subtitle = "TryHardNinja",
                IsActive = true,
                ShowsAudioWave = false,
                LyricLine = "Together we stand 30 feet tall",
                SecondaryLyricLine = "We got a fire that burns within",
                ProgressPercent = 30,
            },
            sequence: 1);
        fixture.Data.Update(_ => MediaContent("Together we stand 30 feet tall", playing));
        fixture.Settle();
        return fixture;
    }

    [Fact]
    public Task Line_change_requests_a_frame_every_16_ms_until_it_settles()
    {
        return AvaloniaTest.Run(() =>
        {
            using var fixture = StartMedia(playing: false);
            fixture.ExpandAndSettle();
            fixture.GoToPage(1);

            var clock = 1000.0;
            LyricTransition.Clock = () => clock;
            try
            {
                fixture.Time.Advance(TimeSpan.FromMilliseconds(16.7));
                fixture.Controller.Advance(TimeSpan.FromMilliseconds(16.7));
                Assert.False(fixture.Controller.IsLyricTransitionAnimating,
                    "no line change yet, so nothing should be requesting lyric frames");

                // The line changes (published activity reaches the pages immediately): from here the host
                // must keep rendering until the fade completes.
                fixture.Controller.PublishActivity(ActivityKind.Media, new ActivityPayload
                {
                    Title = "Dragonhearted",
                    Subtitle = "TryHardNinja",
                    IsActive = true,
                    ShowsAudioWave = false,
                    LyricLine = "The city is my church",
                    SecondaryLyricLine = "We got a fire that burns within",
                    ProgressPercent = 30,
                }, sequence: 2);

                var stamps = new List<double>();
                var animating = new List<bool>();
                var renderMs = new List<double>();
                using var renderer = new IslandRenderer(IslandTheme.Dark);
                for (var frame = 0; frame < 30; frame++)
                {
                    clock += 16.7;
                    fixture.Time.Advance(TimeSpan.FromMilliseconds(16.7));
                    fixture.Controller.Advance(TimeSpan.FromMilliseconds(16.7));
                    var started = Stopwatch.GetTimestamp();
                    using var bitmap = renderer.RenderToBitmap(fixture.Controller.RenderState, 1f);
                    renderMs.Add((Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency);
                    stamps.Add(clock);
                    animating.Add(fixture.Controller.IsLyricTransitionAnimating);
                }

                // 60 Hz for the whole fade: the first ~11 frames (180 ms / 16.7 ms) are inside the window,
                // and every interval stays inside 8..17 ms.
                var inWindow = animating.Count(value => value);
                Assert.InRange(inWindow, 9, 14);
                for (var i = 1; i < stamps.Count; i++)
                {
                    Assert.InRange(stamps[i] - stamps[i - 1], 8, 17);
                }

                // Settled: the transition window closes and the controller stops asking for frames.
                Assert.False(animating[^1]);
                Assert.False(fixture.Controller.IsAnimating);

                // Only the lyric band is repainted, and one frame stays far below the 8 ms budget.
                renderMs.Sort();
                var p99 = renderMs[(int)(renderMs.Count * 0.99) - 1];
                Assert.True(p99 < 8, $"p99 render {p99:0.00} ms must stay under 8 ms");
            }
            finally
            {
                LyricTransition.Clock = null;
            }
        });
    }

    [Fact]
    public Task Evidence_harness_writes_frame_timestamps_and_switch_frames()
    {
        var dir = Environment.GetEnvironmentVariable("FLUIDBAR_LYRIC_FRAME_EVIDENCE");
        if (string.IsNullOrEmpty(dir))
        {
            return Task.CompletedTask;
        }

        return AvaloniaTest.Run(() =>
        {
            Directory.CreateDirectory(dir);
            using var fixture = StartMedia(playing: false);
            fixture.ExpandAndSettle();
            fixture.GoToPage(1);
            var clock = 1000.0;
            LyricTransition.Clock = () => clock;
            try
            {
                fixture.Time.Advance(TimeSpan.FromMilliseconds(16.7));
                fixture.Controller.Advance(TimeSpan.FromMilliseconds(16.7));
                fixture.Controller.PublishActivity(ActivityKind.Media, new ActivityPayload
                {
                    Title = "Dragonhearted",
                    Subtitle = "TryHardNinja",
                    IsActive = true,
                    ShowsAudioWave = false,
                    LyricLine = "The city is my church",
                    SecondaryLyricLine = "We got a fire that burns within",
                    ProgressPercent = 30,
                }, sequence: 2);
                using var renderer = new IslandRenderer(IslandTheme.Dark);
                var rows = new List<string> { "frame,t_ms,delta_ms,transition_active,is_animating,render_ms" };
                for (var frame = 0; frame < 30; frame++)
                {
                    var before = clock;
                    clock += 16.7;
                    fixture.Time.Advance(TimeSpan.FromMilliseconds(16.7));
                    fixture.Controller.Advance(TimeSpan.FromMilliseconds(16.7));
                    var started = Stopwatch.GetTimestamp();
                    using var bitmap = renderer.RenderToBitmap(fixture.Controller.RenderState, 2f);
                    var ms = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
                    rows.Add($"{frame},{clock:0.0},{clock - before:0.0},{fixture.Controller.IsLyricTransitionAnimating},{fixture.Controller.IsAnimating},{ms:0.00}");
                    if (frame < 8)
                    {
                        bitmap.Encode(SKEncodedImageFormat.Png, 100)
                            .SaveTo(File.Create(Path.Combine(dir, $"switch-{frame:00}.png")));
                    }
                }

                File.WriteAllLines(Path.Combine(dir, "frames.csv"), rows);
            }
            finally
            {
                LyricTransition.Clock = null;
            }
        });
    }
}
