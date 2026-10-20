using FluidBar.App.Hosting.Skia;
using FluidBar.App.State;
using FluidBar.Core.Activities;
using FluidBar.Core.Layout;
using FluidBar.Island;

namespace FluidBar.Ui.Tests;

/// <summary>M4 performance scheduler: 8 DIP coalescing and full-frame reason propagation.</summary>
public sealed class FrameSchedulerTests
{
    [Fact]
    public void RectanglesWithinEightDipMerge()
    {
        var scheduler = new FrameScheduler();
        scheduler.InvalidateDirty(new VisualRect(0, 0, 20, 10));
        scheduler.InvalidateDirty(new VisualRect(27, 0, 20, 10));

        Assert.True(scheduler.TryTake(out var change));
        Assert.False(change.FullFrame);
        Assert.Single(change.DamageRects);
        Assert.Equal(0, change.DamageRects[0].X);
        Assert.Equal(47, change.DamageRects[0].Right);
    }

    [Fact]
    public void DistanceOfEightOrMoreStaysSeparate()
    {
        var scheduler = new FrameScheduler();
        scheduler.InvalidateDirty(new VisualRect(0, 0, 20, 10));
        scheduler.InvalidateDirty(new VisualRect(28, 0, 20, 10));

        Assert.True(scheduler.TryTake(out var change));
        Assert.Equal(2, change.DamageRects.Count);
    }

    [Fact]
    public void IntersectingRectanglesMergeAndFullReasonSurvives()
    {
        var scheduler = new FrameScheduler();
        scheduler.InvalidateDirty(new VisualRect(0, 0, 20, 20));
        scheduler.InvalidateDirty(new VisualRect(10, 10, 20, 20));
        Assert.True(scheduler.TryTake(out var dirty));
        Assert.Single(dirty.DamageRects);
        Assert.Equal(30, dirty.DamageRects[0].Right);

        scheduler.InvalidateFull("structural-signature");
        Assert.True(scheduler.TryTake(out var full));
        Assert.True(full.FullFrame);
        Assert.Equal("structural-signature", full.FullReason);
        Assert.Empty(full.EffectiveDamageRects);
    }

    [Fact]
    public Task Same_scene_never_emits_three_consecutive_full_frames()
    {
        return AvaloniaTest.Run(() =>
        {
            using var fixture = ControllerFixture.Create();
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
            fixture.Data.Update(content => content with
            {
                Media = new MediaSessionData
                {
                    Title = "Dragonhearted",
                    Artist = "TryHardNinja",
                    Album = "Perf scene",
                    IsPlaying = true,
                    HasTimeline = true,
                    DurationMs = 180_000,
                    PositionMs = 0,
                    ProgressPercent = 10,
                    LyricLine = "Together we stand 30 feet tall",
                    NextLyricLine = "We got a fire that burns within",
                },
            });
            fixture.Settle();
            fixture.ExpandAndSettle();
            fixture.GoToPage(1);
            fixture.Settle();

            // Drain setup invalidations before measuring the steady media scene.
            while (fixture.Controller.TakeInvalidation(out _))
            {
            }

            var fullStreak = 0;
            for (var frame = 0; frame < 180; frame++)
            {
                fixture.Time.Advance(TimeSpan.FromMilliseconds(16));
                fixture.Controller.PumpTimers();
                fixture.Controller.Advance(TimeSpan.FromMilliseconds(16));
                while (fixture.Controller.TakeInvalidation(out var change))
                {
                    if (change.FullFrame)
                    {
                        fullStreak++;
                        Assert.True(fullStreak < 3,
                            $"frame {frame} started full-frame streak {fullStreak} ({change.FullReason})");
                    }
                    else
                    {
                        fullStreak = 0;
                    }
                }
            }
        });
    }
}

