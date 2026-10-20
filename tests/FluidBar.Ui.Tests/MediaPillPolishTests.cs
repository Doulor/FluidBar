using System.Collections.Immutable;
using FluidBar.App.State;
using FluidBar.Core.Activities;
using FluidBar.Island;
using SkiaSharp;

namespace FluidBar.Ui.Tests;

/// <summary>
/// Task-41 media polish: content-fit lyric pill width, locally extrapolated seek progress, and the
/// track-change shrink/fade/spring transition.
/// </summary>
public sealed class MediaPillPolishTests
{
    private static MediaSessionData Media(
        string title,
        string lyric,
        double positionMs = 60000,
        double durationMs = 240000,
        bool playing = true,
        bool hasTimeline = true) => new()
    {
        Title = title,
        Artist = "Artist",
        LyricLine = lyric,
        PositionMs = positionMs,
        DurationMs = durationMs,
        ProgressPercent = hasTimeline ? (int)Math.Clamp(positionMs / durationMs * 100, 0, 100) : 0,
        HasTimeline = hasTimeline,
        IsPlaying = playing,
        Karaoke = KaraokeTrack.Create(0, 0,
            ImmutableArray.Create(new KaraokeSpan(0, 4), new KaraokeSpan(1000, 4)))!,
    };

    private static ControllerFixture StartMedia(MediaSessionData media)
    {
        var fixture = ControllerFixture.Create();
        fixture.Controller.PublishActivity(
            ActivityKind.Media,
            new ActivityPayload
            {
                Title = media.Title,
                Subtitle = media.Artist,
                IsActive = true,
                ShowsAudioWave = true,
                ProgressPercent = media.ProgressPercent,
                LyricLine = media.LyricLine,
            },
            sequence: 1);
        fixture.Data.Update(_ => new IslandContent { Media = media });
        fixture.Settle();
        // The design source starts with a placeholder track; let that first identity change (and any
        // residual shell motion) finish before the test records a baseline.
        for (var i = 0; i < 40; i++)
        {
            fixture.Time.Advance(TimeSpan.FromMilliseconds(16));
            fixture.Controller.Advance(TimeSpan.FromMilliseconds(16));
        }

        while (fixture.Controller.TakeInvalidation(out _))
        {
        }

        return fixture;
    }

    private static void Advance(ControllerFixture fixture, int milliseconds)
    {
        fixture.Time.Advance(TimeSpan.FromMilliseconds(milliseconds));
        fixture.Controller.Advance(TimeSpan.FromMilliseconds(16));
    }

    [Fact]
    public Task Short_lyric_hugs_its_text_and_long_lyric_grows_the_pill()
    {
        return AvaloniaTest.Run(() =>
        {
            using var shortPill = StartMedia(Media("一", "晚安"));
            var shortWidth = shortPill.Controller.RenderState.ShellWidth;

            using var longPill = StartMedia(
                Media("二", "夜色沿着城市的轮廓慢慢落下来又散开在很远的天边"));
            var longWidth = longPill.Controller.RenderState.ShellWidth;

            Assert.Equal(IslandStateKind.Compact, shortPill.Controller.State.Kind);
            Assert.True(shortWidth < 226,
                $"short lyric must not keep the 226 DIP media token (was {shortWidth})");
            Assert.True(shortWidth >= CompactMediaLayout.MinContentWidth,
                $"short lyric width {shortWidth} below fixed chrome");
            Assert.True(longWidth > shortWidth,
                $"long lyric {longWidth} must be wider than short {shortWidth}");
            SaveState(shortPill.Controller.RenderState with { ShellWidth = 226 }, "pill-short-before-226.png");
            SaveState(shortPill.Controller.RenderState, "pill-short-lyric.png");
            SaveState(longPill.Controller.RenderState, "pill-long-lyric.png");
            SaveText("pill-widths.json",
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    shortLyric = "晚安",
                    shortWidth,
                    longLyric = "夜色沿着城市的轮廓慢慢落下来又散开在很远的天边",
                    longWidth,
                    minContentWidth = CompactMediaLayout.MinContentWidth,
                }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        });
    }

    [Fact]
    public Task Seek_progress_advances_from_the_local_clock_between_samples()
    {
        return AvaloniaTest.Run(() =>
        {
            using var fixture = StartMedia(Media("Track", "line", positionMs: 60000));
            var before = fixture.Controller.RenderState.Compact!.Value.Model.ProgressPercent;

            // No new media sample: only the island clock advances.
            Advance(fixture, 5000);
            var after = fixture.Controller.RenderState.Compact!.Value.Model.ProgressPercent;
            Assert.True(after > before + 1.5, $"{after} should advance past {before}");
            Assert.True(fixture.Controller.IsMediaProgressAnimating);
            Assert.True(fixture.Controller.TakeInvalidation(out var change));
            Assert.False(change.FullFrame);

            // The expanded seek row uses the same extrapolated progress.
            fixture.ExpandAndSettle();
            fixture.GoToPage(1);
            var pageBefore = fixture.Controller.RenderState.Pages.Media.ProgressPercent;
            Advance(fixture, 3000);
            var pageAfter = fixture.Controller.RenderState.Pages.Media.ProgressPercent;
            Assert.True(pageAfter > pageBefore, $"{pageAfter} should advance past {pageBefore}");
        });
    }

    [Fact]
    public Task Progress_bar_moves_across_three_frames()
    {
        return AvaloniaTest.Run(() =>
        {
            using var fixture = StartMedia(Media("Track", "line", positionMs: 6000, durationMs: 30000));
            fixture.ExpandAndSettle();
            fixture.GoToPage(1);

            var samples = new List<object>();
            var progresses = new List<double>();
            for (var i = 0; i < 3; i++)
            {
                Advance(fixture, 1000);
                var state = fixture.Controller.RenderState;
                var progress = Math.Round(state.Pages.Media.ProgressPercent, 3);
                progresses.Add(progress);
                samples.Add(new
                {
                    frame = i,
                    progressPercent = progress,
                    positionText = state.Pages.Media.PositionText,
                });
                SaveState(state, $"progress-frame-{i}.png");
            }
            Assert.True(progresses[1] > progresses[0], $"{progresses[1]} > {progresses[0]}");
            Assert.True(progresses[2] > progresses[1], $"{progresses[2]} > {progresses[1]}");
            SaveText("progress-3-frames.json",
                System.Text.Json.JsonSerializer.Serialize(samples, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                }));
        });
    }

    [Fact]
    public Task Track_change_renders_a_visible_collapse_frame()
    {
        return AvaloniaTest.Run(() =>
        {
            using var fixture = StartMedia(Media("Track A", "line"));
            var before = fixture.Controller.RenderState;
            SaveState(before, "track-before.png");

            fixture.Data.Update(content => content with { Media = Media("Track B", "line") });
            Advance(fixture, 100);
            var collapse = fixture.Controller.RenderState;
            Assert.True(collapse.Opacity < 0.6, $"collapse opacity {collapse.Opacity:0.###}");
            Assert.True(collapse.PressScale < 0.97, $"collapse scale {collapse.PressScale:0.###}");
            SaveState(collapse, "track-collapse.png");

            for (var i = 0; i < 30; i++)
            {
                Advance(fixture, 16);
            }

            var after = fixture.Controller.RenderState;
            Assert.Equal(1.0, after.Opacity, 3);
            Assert.Equal(1.0, after.PressScale, 3);
            SaveState(after, "track-after.png");
        });
    }

    [Fact]
    public Task Unknown_timeline_does_not_draw_a_fake_empty_seek_bar()
    {
        return AvaloniaTest.Run(() =>
        {
            using var fixture = StartMedia(Media("Track", "line", durationMs: 0, hasTimeline: false));
            Assert.Equal(-1, fixture.Controller.RenderState.Compact!.Value.Model.ProgressPercent, 3);
            Assert.False(fixture.Controller.IsMediaProgressAnimating);

            fixture.ExpandAndSettle();
            fixture.GoToPage(1);
            var before = fixture.Controller.RenderState.Pages.Media.ProgressPercent;
            Advance(fixture, 5000);
            Assert.Equal(before, fixture.Controller.RenderState.Pages.Media.ProgressPercent, 6);
            Assert.False(fixture.Controller.RenderState.Pages.Media.HasTimeline);
        });
    }

    [Fact]
    public Task Track_change_shrinks_fades_then_springs_back()
    {
        return AvaloniaTest.Run(() =>
        {
            using var fixture = StartMedia(Media("Track A", "line"));
            var shell = fixture.Controller.RenderState;
            var opacity = new List<double>();
            var scale = new List<double>();

            fixture.Data.Update(content => content with { Media = Media("Track B", "line") });
            Assert.True(fixture.Controller.IsTrackTransitionAnimating);

            for (var i = 0; i < 40; i++)
            {
                Advance(fixture, 16);
                var state = fixture.Controller.RenderState;
                opacity.Add(state.Opacity);
                scale.Add(state.PressScale);
                Assert.Equal(shell.ShellWidth, state.ShellWidth, 6);
                Assert.Equal(shell.ShellHeight, state.ShellHeight, 6);
            }

            Assert.True(opacity.Min() < 0.05, $"opacity must reach zero during shrink (min {opacity.Min():0.###})");
            Assert.True(scale.Min() < 0.90, $"shell must shrink during collapse (min {scale.Min():0.###})");
            Assert.Equal(1.0, opacity[^1], 3);
            Assert.Equal(1.0, scale[^1], 3);
            Assert.False(fixture.Controller.IsTrackTransitionAnimating);
        });
    }

    private static void SaveState(IslandRenderState state, string name)
    {
        using var renderer = new IslandRenderer();
        using var bitmap = renderer.RenderToBitmap(state, 1.5f);
        var dir = Path.Combine(RepoRoot(), "build", "reports", "ui-audit", "media-fix");
        Directory.CreateDirectory(dir);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(Path.Combine(dir, name));
        data.SaveTo(stream);
    }

    private static void SaveText(string name, string contents)
    {
        var dir = Path.Combine(RepoRoot(), "build", "reports", "ui-audit", "media-fix");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, name), contents);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FluidBar.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    [Fact]
    public Task Rapid_track_changes_restart_a_single_timeline()
    {
        return AvaloniaTest.Run(() =>
        {
            using var fixture = StartMedia(Media("Track A", "line"));
            fixture.Data.Update(content => content with { Media = Media("Track B", "line") });
            Advance(fixture, 60);
            fixture.Data.Update(content => content with { Media = Media("Track C", "line") });
            Assert.True(fixture.Controller.IsTrackTransitionAnimating);

            // After the interrupt the shell must still settle back to full opacity/scale once.
            for (var i = 0; i < 40; i++)
            {
                Advance(fixture, 16);
            }

            Assert.Equal(1.0, fixture.Controller.RenderState.Opacity, 3);
            Assert.Equal(1.0, fixture.Controller.RenderState.PressScale, 3);
            Assert.False(fixture.Controller.IsTrackTransitionAnimating);
        });
    }

    [Fact]
    public Task Same_track_pause_resume_does_not_trigger_the_track_transition()
    {
        return AvaloniaTest.Run(() =>
        {
            using var fixture = StartMedia(Media("Track A", "line"));
            fixture.Data.Update(content => content with
            {
                Media = content.Media! with { IsPlaying = false, PositionMs = 60000 },
            });
            Assert.False(fixture.Controller.IsTrackTransitionAnimating);
            fixture.Data.Update(content => content with
            {
                Media = content.Media! with { IsPlaying = true, PositionMs = 60000 },
            });
            Assert.False(fixture.Controller.IsTrackTransitionAnimating);
        });
    }
}
