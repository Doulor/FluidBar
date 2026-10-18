using FluidBar.Core.Activities;
using FluidBar.Core.Layout;
using FluidBar.Island;
using SkiaSharp;

namespace FluidBar.Island.Tests;

public sealed class IslandHitTestTests
{
    private static IslandHitTarget Hit(in IslandRenderState state, float x, float y)
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        return renderer.HitTest(state, x, y);
    }

    [Fact]
    public void Compact_idle_center_hits_the_pill()
    {
        var state = IslandTestFixtures.Idle();
        var hit = Hit(state, 90, 42);
        Assert.Equal(IslandHitKind.Compact, hit.Kind);
        Assert.True(hit.IsInteractive);
        Assert.True(hit.IsShell);
    }

    [Fact]
    public void Compact_media_play_button_is_a_control_and_the_rest_expands()
    {
        var state = IslandTestFixtures.MediaCompact();
        var shell = IslandMetrics.ShellRect(state);

        var play = Hit(state, shell.Right - 20, shell.MidY);
        Assert.Equal(IslandHitKind.Compact, play.Kind);
        Assert.Equal(IslandPageControl.PlayPause, play.Control);

        var text = Hit(state, shell.Left + 60, shell.MidY);
        Assert.Equal(IslandHitKind.Compact, text.Kind);
        Assert.Equal(IslandPageControl.None, text.Control);
    }

    [Fact]
    public void Compact_dock_shell_origin_is_the_dock_strip()
    {
        var state = IslandTestFixtures.Compact(
            new CompactActivity(ActivityKind.Media, new CompactModel { Title = "Midnight City", Subtitle = "M83" }),
            IslandMode.Dock);

        Assert.Equal(24, state.ShellOrigin.X);
        Assert.Equal(316, state.ShellOrigin.Y);
        Assert.Equal(IslandHitKind.Compact, Hit(state, 244, 342).Kind);
    }

    [Fact]
    public void Expanded_home_hits_page_controls_titles_and_shell_background()
    {
        var state = IslandTestFixtures.Expanded(0);
        var shell = IslandMetrics.ShellRect(state);
        var content = IslandMetrics.ContentRect(shell);

        var clock = Hit(state, 60, 80);
        Assert.Equal(IslandHitKind.Page, clock.Kind);
        Assert.Equal(0, clock.PageIndex);

        var toggle = Hit(state, 60, 120);
        Assert.Equal(IslandHitKind.PageControl, toggle.Kind);
        Assert.Equal(IslandPageControl.QuickToggle, toggle.Control);
        Assert.Equal(0, toggle.Index);

        var toggle3 = Hit(state, PageLayout.HomeToggle(IslandMetrics.PageRect(state, 0), 2).MidX,
            PageLayout.HomeToggle(IslandMetrics.PageRect(state, 0), 2).MidY);
        Assert.Equal(IslandPageControl.QuickToggle, toggle3.Control);
        Assert.Equal(2, toggle3.Index);

        var mediaPlay = Hit(state, 420, 180);
        Assert.Equal(IslandHitKind.PageControl, mediaPlay.Kind);
        Assert.Equal(IslandPageControl.PlayPause, mediaPlay.Control);

        var mediaCard = Hit(state, 100, 180);
        Assert.Equal(IslandPageControl.MediaCard, mediaCard.Control);

        var volume = Hit(state, 100, 231);
        Assert.Equal(IslandPageControl.VolumeSlider, volume.Control);

        var brightness = Hit(state, 300, 231);
        Assert.Equal(IslandPageControl.BrightnessSlider, brightness.Control);

        var collapse = Hit(state, 430, 44);
        Assert.Equal(IslandHitKind.Collapse, collapse.Kind);

        var title = Hit(state, 100, 40);
        Assert.Equal(IslandHitKind.Title, title.Kind);
        Assert.False(title.IsInteractive);

        var background = Hit(state, 250, 300);
        Assert.Equal(IslandHitKind.Shell, background.Kind);

        Assert.Equal(IslandHitKind.None, Hit(state, 10, 10).Kind);
        Assert.True(content.Width > 0);
    }

    [Fact]
    public void Expanded_media_hits_transport_seek_output_and_queue()
    {
        var state = IslandTestFixtures.Expanded(1);
        var page = IslandMetrics.PageRect(state, 1);

        var play = Hit(state, PageLayout.MediaPlay(page).MidX, PageLayout.MediaPlay(page).MidY);
        Assert.Equal(IslandPageControl.PlayPause, play.Control);

        var previous = Hit(state, PageLayout.MediaPrevious(page).MidX, PageLayout.MediaPrevious(page).MidY);
        Assert.Equal(IslandPageControl.Previous, previous.Control);

        var next = Hit(state, PageLayout.MediaNext(page).MidX, PageLayout.MediaNext(page).MidY);
        Assert.Equal(IslandPageControl.Next, next.Control);

        var seek = Hit(state, PageLayout.MediaSeekRow(page).MidX, PageLayout.MediaSeekRow(page).MidY);
        Assert.Equal(IslandPageControl.Seek, seek.Control);

        var output = Hit(state, PageLayout.MediaOutput(page).MidX, PageLayout.MediaOutput(page).MidY);
        Assert.Equal(IslandPageControl.Output, output.Control);

        var queue = Hit(state, PageLayout.MediaQueue(page).MidX, PageLayout.MediaQueue(page).MidY);
        Assert.Equal(IslandPageControl.Queue, queue.Control);

        var art = Hit(state, PageLayout.MediaArt(page).MidX, PageLayout.MediaArt(page).MidY);
        Assert.Equal(IslandHitKind.Page, art.Kind);
        Assert.Equal(1, art.PageIndex);
    }

    [Fact]
    public void Shell_corner_outside_the_squircle_is_click_through()
    {
        var state = IslandTestFixtures.Expanded(0);
        Assert.Equal(IslandHitKind.None, Hit(state, 25, 25).Kind);
        Assert.Equal(IslandHitKind.None, Hit(state, 487, 383).Kind);
    }

    [Fact]
    public void Hidden_state_has_no_hit_targets()
    {
        var state = IslandRenderState.Hidden(IslandMode.Float, IslandTestFixtures.Theme);
        Assert.Equal(IslandHitKind.None, Hit(state, 244, 142).Kind);
        Assert.Equal(IslandHitKind.None, Hit(state, 244, 333).Kind);
    }

    [Fact]
    public void Transient_strip_hits_mini_islands_and_far_dots()
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var state = IslandTestFixtures.Expanded(2, strip: true);
        var layout = renderer.MeasureStrip(state);

        Assert.Equal(2, layout.Page);
        Assert.Single(layout.DotsLeft);
        Assert.Equal(2, layout.DotsRight.Length);

        var current = renderer.HitTest(state, layout.Current.MidX, layout.Current.MidY);
        Assert.Equal(IslandHitKind.RailSlot, current.Kind);
        Assert.Equal(2, current.PageIndex);

        var previous = renderer.HitTest(state, layout.Previous.MidX, layout.Previous.MidY);
        Assert.Equal(IslandHitKind.RailSlot, previous.Kind);
        Assert.Equal(1, previous.PageIndex);

        var next = renderer.HitTest(state, layout.Next.MidX, layout.Next.MidY);
        Assert.Equal(IslandHitKind.RailSlot, next.Kind);
        Assert.Equal(3, next.PageIndex);

        var leftDot = renderer.HitTest(state, layout.DotsLeft[0].MidX, layout.DotsLeft[0].MidY);
        Assert.Equal(IslandHitKind.DotLeft, leftDot.Kind);
        Assert.Equal(0, leftDot.PageIndex);

        var rightDot = renderer.HitTest(state, layout.DotsRight[0].MidX, layout.DotsRight[0].MidY);
        Assert.Equal(IslandHitKind.DotRight, rightDot.Kind);
        Assert.Equal(4, rightDot.PageIndex);

        var farRight = renderer.HitTest(state, layout.DotsRight[1].MidX, layout.DotsRight[1].MidY);
        Assert.Equal(IslandHitKind.DotRight, farRight.Kind);
        Assert.Equal(5, farRight.PageIndex);

        // The strip background/gaps are transparent.
        var gap = renderer.HitTest(state, layout.Current.Right + 4, layout.Strip.MidY);
        Assert.Equal(IslandHitKind.None, gap.Kind);
    }

    [Fact]
    public void Transient_strip_hit_test_inverts_the_reveal_transform()
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var baseState = IslandTestFixtures.Expanded(3, strip: true);
        var layout = renderer.MeasureStrip(baseState);

        var transformed = baseState with
        {
            Archipelago = baseState.Archipelago with { TranslateYDip = 6, Scale = 0.95, Opacity = 0.8 },
        };
        var hit = renderer.HitTest(transformed, layout.Current.MidX, layout.Current.MidY + 6);
        Assert.Equal(IslandHitKind.RailSlot, hit.Kind);
        Assert.Equal(3, hit.PageIndex);
    }

    [Fact]
    public void Dot_hit_targets_are_larger_than_the_three_pixel_dots()
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var state = IslandTestFixtures.Expanded(3, strip: true);
        var layout = renderer.MeasureStrip(state);
        var dot = layout.DotsRight[0];

        var hit = renderer.HitTest(state, dot.MidX, dot.MidY + 5);
        Assert.Equal(IslandHitKind.DotRight, hit.Kind);
        Assert.Equal(5, hit.PageIndex);
    }

    [Fact]
    public void Strip_layout_uses_three_islands_and_the_page_dot_counts()
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var layout = renderer.MeasureStrip(IslandTestFixtures.Expanded(0, strip: true));
        Assert.Equal(0, layout.Page);
        Assert.True(layout.Previous.IsEmpty);
        Assert.False(layout.Current.IsEmpty);
        Assert.False(layout.Next.IsEmpty);
        Assert.Empty(layout.DotsLeft);
        Assert.Equal(3, layout.DotsRight.Length);

        var last = renderer.MeasureStrip(IslandTestFixtures.Expanded(5, strip: true));
        Assert.True(last.Next.IsEmpty);
        Assert.Equal(3, last.DotsLeft.Length);
        Assert.Empty(last.DotsRight);
    }
}
