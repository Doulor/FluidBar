using FluidBar.App.Hosting.Skia;
using FluidBar.Core.Activities;
using FluidBar.Core.Layout;
using FluidBar.Island;
using SkiaSharp;

namespace FluidBar.Ui.Tests;

/// <summary>Press/hover feedback, optimistic play state and the inert output/queue slots.</summary>
public sealed class MediaFeedbackTests
{
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
            Pager = PagerRenderState.FromPosition(1, false, false),
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
            Pager = PagerRenderState.FromPosition(1, false, false),
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

        var homeState = state with { Pager = PagerRenderState.FromPosition(0, false, false) };
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
    public void Optimistic_play_state_expires_instead_of_lying_forever()
    {
        var state = new OptimisticPlayState();
        var now = new DateTimeOffset(2026, 10, 20, 2, 0, 0, TimeSpan.Zero);

        state.Set(playing: false, now);
        var resolved = state.Resolve(actual: true, now + OptimisticPlayState.DefaultTtl + TimeSpan.FromMilliseconds(1));

        Assert.True(resolved);
        Assert.False(state.IsPending);
        // The expiry is a rollback: the caller has to re-read the source instead of trusting the old reading.
        Assert.True(state.LastResolveRolledBack);
    }

    [Fact]
    public void Optimistic_play_state_window_is_short_enough_for_two_polls()
    {
        // 600 ms ≈ two 250 ms accessibility polls: a flip the player never confirmed must not survive longer,
        // otherwise the icon keeps disagreeing with the audio.
        Assert.Equal(600, OptimisticPlayState.DefaultTtl.TotalMilliseconds, 1);

        var state = new OptimisticPlayState();
        var now = new DateTimeOffset(2026, 10, 20, 2, 0, 0, TimeSpan.Zero);
        state.Set(playing: true, now);

        // Still inside the window: the flip is painted.
        Assert.True(state.Resolve(actual: false, now.AddMilliseconds(400)));
        Assert.False(state.LastResolveRolledBack);
        // Past it: the truth wins and the rollback is reported once.
        Assert.False(state.Resolve(actual: false, now.AddMilliseconds(601)));
        Assert.True(state.LastResolveRolledBack);
        // The next resolve is a plain reading again.
        Assert.False(state.Resolve(actual: false, now.AddMilliseconds(700)));
        Assert.False(state.LastResolveRolledBack);
    }
}
