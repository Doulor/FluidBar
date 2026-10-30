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
        var shell = IslandMetrics.ShellRect(state);
        var hit = Hit(state, shell.MidX, shell.MidY);
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
        Assert.Equal(316, state.ShellOrigin.Y);
        Assert.Equal(IslandHitKind.Compact, Hit(state, 244, 342).Kind);
    }

    [Fact]
    public void Expanded_home_hits_page_controls_titles_and_shell_background()
    {
        var state = IslandTestFixtures.Expanded(0);
        var shell = IslandMetrics.ShellRect(state);
        var content = IslandMetrics.ContentRect(shell);
        var ox = shell.Left - 24;
        var oy = shell.Top - 24;

        var clock = Hit(state, content.Left + 4, content.Top + 40);
        Assert.Equal(IslandHitKind.Page, clock.Kind);
        Assert.Equal(0, clock.PageIndex);

        var toggle = Hit(state, PageLayout.HomeToggle(IslandMetrics.PageRect(state, 0), 0).MidX, PageLayout.HomeToggle(IslandMetrics.PageRect(state, 0), 0).MidY);
        Assert.Equal(IslandHitKind.PageControl, toggle.Kind);
        Assert.Equal(IslandPageControl.QuickToggle, toggle.Control);
        Assert.Equal(0, toggle.Index);

        var toggle3 = Hit(state, PageLayout.HomeToggle(IslandMetrics.PageRect(state, 0), 2).MidX,
            PageLayout.HomeToggle(IslandMetrics.PageRect(state, 0), 2).MidY);
        Assert.Equal(IslandPageControl.QuickToggle, toggle3.Control);
        Assert.Equal(2, toggle3.Index);

        var mediaPlay = Hit(state, PageMetricsLayout.HomeMediaPlay(IslandMetrics.PageRect(state, 0), FontScaleMetrics.One).MidX, PageMetricsLayout.HomeMediaPlay(IslandMetrics.PageRect(state, 0), FontScaleMetrics.One).MidY);
        Assert.Equal(IslandHitKind.PageControl, mediaPlay.Kind);
        Assert.Equal(IslandPageControl.PlayPause, mediaPlay.Control);

        var mediaCard = Hit(state, PageMetricsLayout.HomeMediaCard(IslandMetrics.PageRect(state, 0), FontScaleMetrics.One).MidX, PageMetricsLayout.HomeMediaCard(IslandMetrics.PageRect(state, 0), FontScaleMetrics.One).MidY);
        Assert.Equal(IslandPageControl.MediaCard, mediaCard.Control);

        var volume = Hit(state, PageMetricsLayout.HomeVolumeTrack(IslandMetrics.PageRect(state, 0), FontScaleMetrics.One).MidX, PageMetricsLayout.HomeVolumeTrack(IslandMetrics.PageRect(state, 0), FontScaleMetrics.One).MidY);
        Assert.Equal(IslandPageControl.VolumeSlider, volume.Control);

        var brightness = Hit(state, PageMetricsLayout.HomeBrightnessTrack(IslandMetrics.PageRect(state, 0), FontScaleMetrics.One).MidX, PageMetricsLayout.HomeBrightnessTrack(IslandMetrics.PageRect(state, 0), FontScaleMetrics.One).MidY);
        Assert.Equal(IslandPageControl.BrightnessSlider, brightness.Control);

        var collapse = Hit(state, PageLayout.CollapseRect(content).MidX, PageLayout.CollapseRect(content).MidY);
        Assert.Equal(IslandHitKind.Collapse, collapse.Kind);

        var title = Hit(state, PageLayout.PageTitleRect(content).MidX, PageLayout.PageTitleRect(content).MidY);
        Assert.Equal(IslandHitKind.Title, title.Kind);
        Assert.False(title.IsInteractive);

        var background = Hit(state, shell.MidX, shell.Top + 2);
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
        var metrics = FontScaleMetrics.From(state.FontScale);

        var play = Hit(state, PageMetricsLayout.MediaPlay(page, metrics).MidX,
            PageMetricsLayout.MediaPlay(page, metrics).MidY);
        Assert.Equal(IslandPageControl.PlayPause, play.Control);

        var previous = Hit(state, PageMetricsLayout.MediaPrevious(page, metrics).MidX,
            PageMetricsLayout.MediaPrevious(page, metrics).MidY);
        Assert.Equal(IslandPageControl.Previous, previous.Control);

        var next = Hit(state, PageMetricsLayout.MediaNext(page, metrics).MidX,
            PageMetricsLayout.MediaNext(page, metrics).MidY);
        Assert.Equal(IslandPageControl.Next, next.Control);

        var seek = Hit(state, PageLayout.MediaSeekRow(page).MidX, PageLayout.MediaSeekRow(page).MidY);
        Assert.Equal(IslandPageControl.Seek, seek.Control);

        var output = Hit(state, PageMetricsLayout.MediaOutput(page, metrics).MidX,
            PageMetricsLayout.MediaOutput(page, metrics).MidY);
        Assert.Equal(IslandPageControl.Output, output.Control);

        var queue = Hit(state, PageMetricsLayout.MediaQueue(page, metrics).MidX,
            PageMetricsLayout.MediaQueue(page, metrics).MidY);
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
    public void DetachedStrip_IsNotInteractive()
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var state = IslandTestFixtures.Expanded(2, strip: true);
        var layout = renderer.MeasureStrip(state);
        Assert.Equal(IslandHitKind.None, renderer.HitTest(state, layout.Current.MidX, layout.Current.MidY).Kind);
        Assert.Equal(IslandHitKind.None, renderer.HitTest(state, layout.Next.MidX, layout.Next.MidY).Kind);
        Assert.Equal(IslandHitKind.None, renderer.HitTest(state, layout.DotsRight[0].MidX, layout.DotsRight[0].MidY).Kind);
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
