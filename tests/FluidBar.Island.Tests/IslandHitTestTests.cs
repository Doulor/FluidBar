using FluidBar.Core.Activities;
using FluidBar.Core.Interaction;
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
    public void Compact_media_has_no_play_button_and_the_whole_pill_expands()
    {
        var state = IslandTestFixtures.MediaCompact();
        var shell = IslandMetrics.ShellRect(state);

        // The old play/pause control area on the right of the pill is gone: it is plain pill surface now,
        // so a click there expands the island instead of toggling playback.
        var oldPlay = Hit(state, shell.Right - 20, shell.MidY);
        Assert.Equal(IslandHitKind.Compact, oldPlay.Kind);
        Assert.Equal(IslandPageControl.None, oldPlay.Control);

        // Sweep the whole pill: no horizontal position exposes a control any more.
        for (var x = shell.Left + 4; x < shell.Right - 2; x += 8)
        {
            Assert.Equal(IslandPageControl.None, Hit(state, x, shell.MidY).Control);
        }

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
        Assert.Equal(276, state.ShellOrigin.Y);
        Assert.Equal(IslandHitKind.Compact, Hit(state, 244, (float)(state.ShellOrigin.Y + 20)).Kind);
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

        var background = Hit(state, 250, shell.Bottom - 8);
        Assert.Equal(IslandHitKind.Shell, background.Kind);

        Assert.Equal(IslandHitKind.None, Hit(state, 10, 10).Kind);
        Assert.True(content.Width > 0);
    }

    [Fact]
    public void Expanded_header_settings_gear_is_its_own_hit_target()
    {
        var state = IslandTestFixtures.Expanded(0);
        var content = IslandMetrics.ContentRect(IslandMetrics.ShellRect(state));
        var gear = PageLayout.SettingsRect(content);

        var hit = Hit(state, gear.MidX, gear.MidY);
        Assert.Equal(IslandHitKind.Settings, hit.Kind);
        Assert.True(hit.IsInteractive);
        Assert.True(hit.IsShell);

        // The gear, the chevron and the title must not overlap: each header element stays reachable.
        Assert.False(PageLayout.CollapseRect(content).Contains(gear.MidX, gear.MidY));
        Assert.False(PageLayout.PageTitleRect(content).Contains(gear.MidX, gear.MidY));
        Assert.False(gear.Contains(PageLayout.CollapseRect(content).MidX, PageLayout.CollapseRect(content).MidY));

        var title = PageLayout.PageTitleRect(content);
        Assert.Equal(IslandHitKind.Title, Hit(state, title.MidX, title.MidY).Kind);
        var chevron = PageLayout.CollapseRect(content);
        Assert.Equal(IslandHitKind.Collapse, Hit(state, chevron.MidX, chevron.MidY).Kind);

        // Every expanded page carries the same entry.
        for (var page = 0; page < FluidBar.Core.Activities.PageRef.BuiltinPages.Count; page++)
        {
            var pageState = IslandTestFixtures.Expanded(page);
            Assert.Equal(IslandHitKind.Settings, Hit(pageState, gear.MidX, gear.MidY).Kind);
        }
    }

    [Fact]
    public void Expanded_media_hits_transport_seek_output_and_queue()
    {
        var state = IslandTestFixtures.Expanded(1);
        var page = IslandMetrics.PageRect(state, 1);

        // Probe the centres of the rectangles the renderer actually paints (PageVisual), not the legacy
        // PageLayout geometry that the media page stopped using when the footer was re-laid-out.
        foreach (var (control, rect) in new (IslandPageControl Control, SKRect Rect)[]
                 {
                     (IslandPageControl.PlayPause, PageVisual.MediaPlay(page)),
                     (IslandPageControl.Previous, PageVisual.MediaPrevious(page)),
                     (IslandPageControl.Next, PageVisual.MediaNext(page)),
                     (IslandPageControl.Output, PageVisual.MediaOutput(page)),
                     (IslandPageControl.Queue, PageVisual.MediaQueue(page)),
                 })
        {
            var hit = Hit(state, rect.MidX, rect.MidY);
            Assert.Equal(control, hit.Control);
        }

        var seek = PageVisual.MediaSeekRow(page);
        Assert.Equal(IslandPageControl.Seek, Hit(state, seek.Left + 4, seek.MidY).Control);

        var volume = PageVisual.MediaVolume(page);
        Assert.Equal(IslandPageControl.VolumeMute, Hit(state, volume.Left + 4, volume.MidY).Control);

        var art = PageVisual.MediaArt(page);
        var artHit = Hit(state, art.MidX, art.MidY);
        Assert.Equal(IslandHitKind.Page, artHit.Kind);
        Assert.Equal(1, artHit.PageIndex);
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

        // Every dot in a cluster jumps to the farthest page in that direction (prototype far-dot rule).
        var rightDot = renderer.HitTest(state, layout.DotsRight[0].MidX, layout.DotsRight[0].MidY);
        Assert.Equal(IslandHitKind.DotRight, rightDot.Kind);
        Assert.Equal(5, rightDot.PageIndex);

        var farRight = renderer.HitTest(state, layout.DotsRight[1].MidX, layout.DotsRight[1].MidY);
        Assert.Equal(IslandHitKind.DotRight, farRight.Kind);
        Assert.Equal(5, farRight.PageIndex);

        // The strip background/gaps are transparent.
        var gap = renderer.HitTest(state, layout.Current.Right + 4, layout.Strip.MidY);
        Assert.Equal(IslandHitKind.None, gap.Kind);
    }

    [Fact]
    public void Transient_strip_keeps_its_geometry_during_the_reveal_fade()
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var baseState = IslandTestFixtures.Expanded(3, strip: true);
        var layout = renderer.MeasureStrip(baseState);

        // Prototype2 fades the rail in with an opacity transition only, so the pills never slide or
        // scale: the measured rects stay hit-testable while the reveal is still in flight.
        var fading = baseState with
        {
            Archipelago = baseState.Archipelago with { TranslateYDip = 6, Scale = 0.95, Opacity = 0.8 },
        };
        var hit = renderer.HitTest(fading, layout.Current.MidX, layout.Current.MidY);
        Assert.Equal(IslandHitKind.RailSlot, hit.Kind);
        Assert.Equal(3, hit.PageIndex);
        Assert.Equal(IslandHitKind.None, renderer.HitTest(fading, layout.Current.MidX, layout.Current.Top - 2).Kind);
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

        // The prototype hit area is 7×15: 7 DIP above the centre still hits, 8 DIP does not.
        Assert.Equal(IslandHitKind.DotRight, renderer.HitTest(state, dot.MidX, dot.MidY + 7).Kind);
        Assert.Equal(IslandHitKind.None, renderer.HitTest(state, dot.MidX, dot.MidY + 8).Kind);
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


