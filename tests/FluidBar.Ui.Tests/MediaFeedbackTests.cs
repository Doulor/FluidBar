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
        return PageMetricsLayout.MediaPlay(page, FontScaleMetrics.From(state.FontScale));
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
        var mediaVolume = PageMetricsLayout.MediaVolume(mediaPage, metrics);
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
    public void Optimistic_pause_survives_stale_glyph_reads_and_follows_four_consistent_ones()
    {
        // A fallback player repaints its transport glyph ~0.5-1 s after the tap, so the first polls after
        // a toggle still carry the PRE-tap state. They must not flip the icon back (reported bug: the
        // style changed to paused and snapped back to playing about a second later).
        var state = new OptimisticPlayState();
        var now = new DateTimeOffset(2026, 10, 20, 2, 0, 0, TimeSpan.Zero);
        state.Set(playing: false, now);

        // Inside the stale-read guard (1.2 s): ignored entirely.
        Assert.False(state.Resolve(actual: true, now.AddMilliseconds(80)));
        Assert.False(state.Resolve(actual: true, now.AddMilliseconds(900)));
        Assert.Equal(0, state.Disagreements);

        // Consistent disagreements after the guard: the intent holds for the first three.
        Assert.False(state.Resolve(actual: true, now.AddMilliseconds(1300)));
        Assert.False(state.Resolve(actual: true, now.AddMilliseconds(1550)));
        Assert.False(state.Resolve(actual: true, now.AddMilliseconds(1800)));
        Assert.Equal(3, state.Disagreements);
        Assert.False(state.LastResolveRolledBack);

        // Fourth consistent disagreement: the source really is playing, so follow it once.
        Assert.True(state.Resolve(actual: true, now.AddMilliseconds(2050)));
        Assert.True(state.LastResolveRolledBack);
        Assert.False(state.IsPending);
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

        // The hold window is 5-7 s (the player can take a second or two to repaint its glyph), but never
        // unbounded: a source that never agrees still wins in the end.
        Assert.InRange(OptimisticPlayState.HoldWindow, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(7));

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
}
