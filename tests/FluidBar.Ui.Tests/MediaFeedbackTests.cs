using FluidBar.App.Hosting.Skia;
using FluidBar.Core.Activities;
using FluidBar.Core.Layout;
using FluidBar.Island;
using SkiaSharp;

namespace FluidBar.Ui.Tests;

/// <summary>Press/hover feedback, optimistic play state and the inert output/queue slots.</summary>
public sealed class MediaFeedbackTests
{
    private const int PageCount = 6;

    private static SKRect PlayRect(IslandRenderState state)
    {
        var page = IslandMetrics.PageRect(state, 1);
        return PageVisual.MediaPlay(page);
    }

    [Fact]
    public void Pressing_the_transport_control_marks_it_pressed_until_release()
    {
        using var fixture = ControllerFixture.Create();
        fixture.ExpandAndSettle();
        fixture.GoToPage(1);

        var play = PlayRect(fixture.Controller.RenderState);
        fixture.Controller.HandlePointerPressed(play.MidX, play.MidY);
        // The pressed flag lands in the next frame's render state (the host renders continuously).
        fixture.Controller.Advance(TimeSpan.FromMilliseconds(16));

        Assert.Equal(IslandPageControl.PlayPause, fixture.Controller.RenderState.PressedControl);
        Assert.Equal(1, fixture.Controller.RenderState.PressedPage);

        fixture.Controller.HandlePointerReleased(play.MidX, play.MidY);
        fixture.Controller.Advance(TimeSpan.FromMilliseconds(16));
        Assert.Equal(IslandPageControl.None, fixture.Controller.RenderState.PressedControl);
    }

    [Fact]
    public void Hovering_the_transport_control_marks_it_hovered_and_leaving_clears_it()
    {
        using var fixture = ControllerFixture.Create();
        fixture.ExpandAndSettle();
        fixture.GoToPage(1);

        var play = PlayRect(fixture.Controller.RenderState);
        fixture.Controller.HandlePointerMoved(play.MidX, play.MidY);
        fixture.Controller.Advance(TimeSpan.FromMilliseconds(16));
        Assert.Equal(IslandPageControl.PlayPause, fixture.Controller.RenderState.HoverControl);

        // The page title area is a page target, not a control.
        fixture.Controller.HandlePointerMoved(4, 4);
        fixture.Controller.Advance(TimeSpan.FromMilliseconds(16));
        Assert.Equal(IslandPageControl.None, fixture.Controller.RenderState.HoverControl);
    }

    [Fact]
    public void Output_and_queue_slots_are_inert_when_the_capability_is_absent()
    {
        var state = new IslandRenderState
        {
            Mode = IslandMode.Float,
            Form = ShellForm.Expanded,
            ShellWidth = LayoutTokens.ExpandedWidth,
            ShellHeight = LayoutTokens.ExpandedHeight,
            Pager = PagerRenderState.FromPosition(1, PageCount, false, false),
            Pages = new ExpandedPageContent
            {
                Media = new MediaPageModel { HasSession = true, OutputAvailable = false, QueueAvailable = false },
            },
        };
        var page = IslandMetrics.PageRect(state, 1);
        var metrics = FontScaleMetrics.From(state.FontScale);
        var output = PageMetricsLayout.MediaOutput(page, metrics);
        var queue = PageMetricsLayout.MediaQueue(page, metrics);

        Assert.NotEqual(IslandPageControl.Output, SkiaIslandHitTest.Hit(state, output.MidX, output.MidY).Control);
        Assert.NotEqual(IslandPageControl.Queue, SkiaIslandHitTest.Hit(state, queue.MidX, queue.MidY).Control);
    }

    [Fact]
    public void Media_and_home_volume_speakers_are_real_mute_hit_targets()
    {
        var state = new IslandRenderState
        {
            Mode = IslandMode.Float,
            Form = ShellForm.Expanded,
            ShellWidth = LayoutTokens.ExpandedWidth,
            ShellHeight = LayoutTokens.ExpandedHeight,
            Pager = PagerRenderState.FromPosition(1, PageCount, false, false),
            Pages = new ExpandedPageContent
            {
                Home = new HomePageModel { HasVolume = true, Volume = 42, IsMuted = false },
                Media = new MediaPageModel { HasSession = true, HasVolume = true, VolumePercent = 42, IsMuted = false },
            },
        };
        var metrics = FontScaleMetrics.From(state.FontScale);

        var mediaPage = IslandMetrics.PageRect(state, 1);
        var mediaVolume = PageVisual.MediaVolume(mediaPage);
        var mediaHit = SkiaIslandHitTest.Hit(state, mediaVolume.MidX, mediaVolume.MidY);
        Assert.Equal(IslandPageControl.VolumeMute, mediaHit.Control);
        Assert.Equal(
            IslandPlatformActionKind.VolumeMute,
            SkiaIslandControlRouter.Map(mediaHit, state, mediaVolume.MidX)!.Value.Kind);

        var homeState = state with { Pager = PagerRenderState.FromPosition(0, PageCount, false, false) };
        var homePage = IslandMetrics.PageRect(homeState, 0);
        var homeVolume = PageMetricsLayout.HomeVolumeIcon(homePage, metrics);
        var homeHit = SkiaIslandHitTest.Hit(homeState, homeVolume.MidX, homeVolume.MidY);
        Assert.Equal(IslandPageControl.VolumeMute, homeHit.Control);
        Assert.Equal(
            IslandPlatformActionKind.VolumeMute,
            SkiaIslandControlRouter.Map(homeHit, homeState, homeVolume.MidX)!.Value.Kind);

        // No endpoint: both speakers are inert, not a dead button.
        var mutedState = state with
        {
            Pages = state.Pages with
            {
                Home = state.Pages.Home with { HasVolume = false },
                Media = state.Pages.Media with { HasVolume = false },
            },
        };
        var mutedHomeState = homeState with { Pages = mutedState.Pages };
        Assert.NotEqual(IslandPageControl.VolumeMute,
            SkiaIslandHitTest.Hit(mutedState, mediaVolume.MidX, mediaVolume.MidY).Control);
        Assert.NotEqual(IslandPageControl.VolumeMute,
            SkiaIslandHitTest.Hit(mutedHomeState, homeVolume.MidX, homeVolume.MidY).Control);
    }

    [Fact]
    public void Optimistic_play_state_wins_until_the_source_agrees()
    {
        var state = new OptimisticPlayState();
        var now = new DateTimeOffset(2026, 10, 20, 2, 0, 0, TimeSpan.Zero);

        // The tap flips the icon before the player has reported anything.
        state.Set(playing: false, now);
        Assert.False(state.Resolve(actual: true, now));
        Assert.True(state.IsPending);

        // The player catches up: the truth takes over and the overlay is gone.
        Assert.False(state.Resolve(actual: false, now.AddMilliseconds(200)));
        Assert.False(state.IsPending);

        // A refused command rolls the flip back immediately.
        state.Set(playing: true, now);
        state.Clear();
        Assert.True(state.Resolve(actual: true, now));
        Assert.False(state.IsPending);
    }

    [Fact]
    public void Optimistic_pause_ignores_one_stale_read_and_follows_two_consistent_ones()
    {
        // The accessibility poll keeps returning the pre-tap glyph for a while: a single stale read must
        // not flip the icon back (that is the reported "paused but still shows playing" bug).
        var state = new OptimisticPlayState();
        var now = new DateTimeOffset(2026, 10, 20, 2, 0, 0, TimeSpan.Zero);
        state.Set(playing: false, now);

        // Inside the stale-read guard: ignored entirely.
        Assert.False(state.Resolve(actual: true, now.AddMilliseconds(80)));
        Assert.Equal(0, state.Disagreements);

        // First consistent disagreement: still holding the user's intent.
        Assert.False(state.Resolve(actual: true, now.AddMilliseconds(320)));
        Assert.Equal(1, state.Disagreements);
        Assert.False(state.LastResolveRolledBack);

        // Second consistent disagreement: the source really is playing, so follow it once.
        Assert.True(state.Resolve(actual: true, now.AddMilliseconds(520)));
        Assert.True(state.LastResolveRolledBack);
        Assert.False(state.IsPending);
        Assert.True(state.Resolve(actual: true, now.AddMilliseconds(700)));
    }

    [Fact]
    public void Optimistic_play_state_clears_without_rollback_when_the_source_catches_up()
    {
        var state = new OptimisticPlayState();
        var now = new DateTimeOffset(2026, 10, 20, 2, 0, 0, TimeSpan.Zero);
        state.Set(playing: false, now);

        Assert.False(state.Resolve(actual: false, now.AddMilliseconds(200)));
        Assert.False(state.IsPending);
        Assert.False(state.LastResolveRolledBack);
    }

    [Fact]
    public void Optimistic_play_state_gives_up_after_the_hold_window()
    {
        var state = new OptimisticPlayState();
        var now = new DateTimeOffset(2026, 10, 20, 2, 0, 0, TimeSpan.Zero);
        state.Set(playing: true, now);

        // The hold window is 2–3 s (the user must see their intent for a moment), but never longer.
        Assert.InRange(OptimisticPlayState.HoldWindow, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3));

        // A single disagreeing read inside the window keeps the user's intent (the current rule:
        // one stale poll is ignored; two consistent reads are the truth)...
        Assert.True(state.Resolve(actual: false, now.AddMilliseconds(300)));
        Assert.False(state.Resolve(actual: false, now + OptimisticPlayState.HoldWindow + TimeSpan.FromMilliseconds(1)));
        Assert.True(state.LastResolveRolledBack);

        // ...and a read that only lands after the window gives up immediately, even alone.
        state.Set(playing: true, now);
        Assert.False(state.Resolve(actual: false,
            now + OptimisticPlayState.HoldWindow + OptimisticPlayState.StaleReadGuard));
        Assert.True(state.LastResolveRolledBack);
        Assert.False(state.IsPending);
    }

    [Fact]
    public void Pause_tap_holds_the_paused_icon_for_two_seconds_of_stale_reads()
    {
        // Five-frame evidence for the reported bug: after the pause tap the island must paint "paused" at
        // 0 ms and 100 ms even while the accessibility source keeps reporting the pre-tap "playing" glyph,
        // and it must not flash back to playing inside 2 s.
        {
            var optimistic = new OptimisticPlayState();
            var now = new DateTimeOffset(2026, 10, 21, 1, 0, 0, TimeSpan.Zero);
            var frames = new List<(int OffsetMs, bool IslandPlaying, bool Optimistic, bool RolledBack)>();

            bool Sample(int offsetMs, bool sourceReading)
            {
                var at = now.AddMilliseconds(offsetMs);
                var published = optimistic.Resolve(sourceReading, at);
                frames.Add((offsetMs, published, optimistic.IsPending, optimistic.LastResolveRolledBack));
                return published;
            }

            // The tap: pause, and the source still says "playing" for the next two reads (stale glyph).
            optimistic.Set(playing: false, now);
            Assert.False(Sample(0, sourceReading: true));

            Assert.False(Sample(100, sourceReading: true));
            Assert.False(Sample(300, sourceReading: true));       // first consistent disagreement: hold
            Assert.False(Sample(1000, sourceReading: false));    // source caught up: still paused
            Assert.False(Sample(2000, sourceReading: false));

            // 0/100 ms must be paused, nothing inside 2 s flipped back to playing, no rollback happened.
            Assert.False(frames[0].IslandPlaying);
            Assert.False(frames[1].IslandPlaying);
            Assert.All(frames, frame => Assert.False(frame.IslandPlaying));
            Assert.All(frames, frame => Assert.False(frame.RolledBack));
        }
    }

    [Fact]
    public void Play_tap_holds_the_playing_icon_while_the_source_lags()
    {
        var optimistic = new OptimisticPlayState();
        var now = new DateTimeOffset(2026, 10, 21, 1, 0, 0, TimeSpan.Zero);
        optimistic.Set(playing: true, now);

        // Same five sample points as the pause case: the resumed state must survive the lagging source.
        Assert.True(optimistic.Resolve(actual: false, now));
        Assert.True(optimistic.Resolve(actual: false, now.AddMilliseconds(100)));
        Assert.True(optimistic.Resolve(actual: false, now.AddMilliseconds(300)));
        Assert.True(optimistic.Resolve(actual: true, now.AddMilliseconds(1000)));
        Assert.False(optimistic.IsPending);
        Assert.False(optimistic.LastResolveRolledBack);
    }
}

