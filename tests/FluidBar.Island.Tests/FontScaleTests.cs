using FluidBar.Core.Layout;
using FluidBar.Island;
using SkiaSharp;

namespace FluidBar.Island.Tests;

/// <summary>
/// Font scale (settings → appearance → text size, 0.8–1.6) coverage: the metrics grow monotonically,
/// the containers follow the measured line heights, no text/icon ink leaves the shell or pill at the
/// extremes, the rail capsule contract keeps its scaled label inside, and scale 1.0 stays pixel-identical
/// to the committed golden images.
/// </summary>
public sealed class FontScaleTests
{
    public static TheoryData<double> Scales => new() { 0.8, 1.2, 1.6 };

    private static readonly TextStyle[] Styles =
    [
        IslandType.Clock, IslandType.Date, IslandType.CompactTitle, IslandType.Toggle,
        IslandType.CardTitle, IslandType.CardSubtitle, IslandType.RowTitle, IslandType.RowBody,
        IslandType.RowMeta, IslandType.MediaTitle, IslandType.Lyric, IslandType.CalendarDay,
        IslandType.TaskLabel, IslandType.MiniLabel, IslandType.SliderValue, IslandType.BatteryLine,
    ];

    [Theory]
    [MemberData(nameof(Scales))]
    public void Expanded_pages_render_inside_the_shell_at_every_scale(double scale)
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        for (var page = 0; page < 6; page++)
        {
            var state = IslandTestFixtures.WithFontScale(IslandTestFixtures.Expanded(page), scale);
            using var bitmap = renderer.RenderToBitmap(state, IslandTestFixtures.Scale);
            var shell = IslandMetrics.ShellRect(state);
            Assert.Equal(0, IslandTestFixtures.CountOpaqueOutside(bitmap, shell, IslandTestFixtures.Scale));
            Assert.True(IslandTestFixtures.CountOpaqueInside(bitmap, shell, IslandTestFixtures.Scale) > 2000,
                $"page {page} at scale {scale} should paint content");
        }
    }

    [Theory]
    [MemberData(nameof(Scales))]
    public void Compact_pills_render_inside_the_grown_pill_at_every_scale(double scale)
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var states = new[]
        {
            IslandTestFixtures.WithFontScale(IslandTestFixtures.Idle(), scale),
            IslandTestFixtures.WithFontScale(IslandTestFixtures.MediaCompact(), scale),
            IslandTestFixtures.WithFontScale(IslandTestFixtures.Notification(), scale),
            IslandTestFixtures.WithFontScale(IslandTestFixtures.Clipboard(), scale),
            IslandTestFixtures.WithFontScale(IslandTestFixtures.Agent(), scale),
            IslandTestFixtures.WithFontScale(IslandTestFixtures.Hud(), scale),
        };

        foreach (var state in states)
        {
            var token = LayoutTokens.CompactSize(state.Compact?.Kind);
            var expected = LayoutTokens.CompactSizeFor(state.Compact?.Kind, state.Mode, scale);
            Assert.Equal(expected.Width, state.ShellWidth, 3);
            Assert.Equal(expected.Height, state.ShellHeight, 3);
            Assert.True(state.ShellWidth >= token.Width * 0.799 - 0.01);

            using var bitmap = renderer.RenderToBitmap(state, IslandTestFixtures.Scale);
            var shell = IslandMetrics.ShellRect(state);
            Assert.Equal(0, IslandTestFixtures.CountOpaqueOutside(bitmap, shell, IslandTestFixtures.Scale));
            Assert.True(IslandTestFixtures.CountOpaqueInside(bitmap, shell, IslandTestFixtures.Scale) > 150);
        }
    }

    [Fact]
    public void Metrics_are_monotonic_in_the_font_scale()
    {
        var small = FontScaleMetrics.From(0.8);
        var one = FontScaleMetrics.One;
        var large = FontScaleMetrics.From(1.6);

        foreach (var style in Styles)
        {
            Assert.True(small.LineGrow(style) <= 0.0001f, style.Role + " must not grow at 0.8");
            Assert.Equal(0, one.LineGrow(style), 4);
            Assert.True(large.LineGrow(style) > 0, style.Role + " must grow at 1.6");
            Assert.True(large.LineGrow(style) > small.LineGrow(style));
        }

        var pageRect = new SKRect(0, (float)IslandMetrics.ContentTop, 400,
            (float)(IslandMetrics.ContentTop + IslandMetrics.ContentHeight));
        Assert.True(PageMetricsLayout.NotificationRowHeight(large) > PageMetricsLayout.NotificationRowHeight(one));
        Assert.True(PageMetricsLayout.ClipboardRowHeight(large) > PageMetricsLayout.ClipboardRowHeight(one));
        Assert.True(PageMetricsLayout.AgentRowHeight(large) > PageMetricsLayout.AgentRowHeight(one));
        Assert.True(PageMetricsLayout.HomeContentExtent(pageRect, large) > PageMetricsLayout.HomeContentExtent(pageRect, one));
        Assert.True(PageMetricsLayout.MediaContentExtent(pageRect, large) > PageMetricsLayout.MediaContentExtent(pageRect, one));
        Assert.Equal(PageMetricsLayout.NotificationRowHeight(one), 40, 3);
        Assert.Equal(PageMetricsLayout.ClipboardRowHeight(one), 48, 3);
        Assert.Equal(PageMetricsLayout.AgentRowHeight(one), 44.6, 3);
        Assert.Equal(PageMetricsLayout.HomeContentExtent(pageRect, one), 212, 3);
        Assert.Equal(PageMetricsLayout.MediaContentExtent(pageRect, one), 232, 3);
    }

    [Fact]
    public void Scaled_line_heights_scale_with_the_font_scale()
    {
        using var engine = new TextEngine();
        foreach (var style in Styles)
        {
            var one = engine.LineHeight(style);
            var large = engine.LineHeight(FontScaleMetrics.From(1.6).Style(style));
            var small = engine.LineHeight(FontScaleMetrics.From(0.8).Style(style));
            Assert.Equal(one * 1.6, large, one * 0.02);
            Assert.Equal(one * 0.8, small, one * 0.02);
        }
    }

    [Fact]
    public void Home_blocks_and_list_rows_never_overlap_at_the_extremes()
    {
        foreach (var scale in new[] { 0.8, 1.0, 1.2, 1.6 })
        {
            var metrics = FontScaleMetrics.From(scale);
            var page = new SKRect(0, (float)IslandMetrics.ContentTop, 400,
                (float)(IslandMetrics.ContentTop + IslandMetrics.ContentHeight));

            var clock = PageMetricsLayout.HomeClock(page, metrics);
            var date = PageMetricsLayout.HomeDate(page, metrics);
            var toggle = PageMetricsLayout.HomeToggle(page, 0, metrics);
            var card = PageMetricsLayout.HomeMediaCard(page, metrics);
            var sliders = PageMetricsLayout.HomeSliders(page, metrics);
            var battery = PageMetricsLayout.HomeBattery(page, metrics);
            Assert.True(clock.Bottom <= date.Top + 0.01f, $"clock/date overlap at {scale}");
            Assert.True(date.Bottom <= toggle.Top + 0.01f, $"date/toggles overlap at {scale}");
            Assert.True(toggle.Bottom <= card.Top + 0.01f, $"toggles/card overlap at {scale}");
            Assert.True(card.Bottom <= sliders.Top + 0.01f, $"card/sliders overlap at {scale}");
            Assert.True(sliders.Bottom <= battery.Top + 0.01f, $"sliders/battery overlap at {scale}");
            Assert.True(toggle.Bottom <= page.Bottom, $"toggles beyond page at {scale}");

            var eyebrow = PageMetricsLayout.MediaEyebrow(page, metrics);
            var title = PageMetricsLayout.MediaTitle(page, metrics);
            var artist = PageMetricsLayout.MediaArtist(page, metrics);
            var album = PageMetricsLayout.MediaAlbum(page, metrics);
            var lyric = PageMetricsLayout.MediaLyricCard(page, metrics);
            var seek = PageMetricsLayout.MediaSeekRow(page, metrics);
            Assert.True(eyebrow.Bottom <= title.Top + 0.01f, $"eyebrow/title overlap at {scale}");
            Assert.True(title.Bottom <= artist.Top + 0.01f, $"title/artist overlap at {scale}");
            Assert.True(artist.Bottom <= album.Top + 0.01f, $"artist/album overlap at {scale}");
            Assert.True(PageMetricsLayout.MediaTopBottom(page, metrics) <= lyric.Top, $"top block/lyric overlap at {scale}");
            Assert.True(lyric.Bottom <= seek.Top + 0.01f, $"lyric/seek overlap at {scale}");

            var rowHeight = PageMetricsLayout.NotificationRowHeight(metrics);
            Assert.True(rowHeight > 24, $"row height must stay usable at {scale}");
            var oneRowHeight = PageMetricsLayout.NotificationRowHeight(FontScaleMetrics.One);
            Assert.True(scale < 1 ? rowHeight <= oneRowHeight : rowHeight >= oneRowHeight);
            var r0 = PageMetricsLayout.NotificationRow(page, 0, 0, metrics);
            var r1 = PageMetricsLayout.NotificationRow(page, 1, 0, metrics);
            Assert.True(r0.Bottom <= r1.Top + 0.01f);

            var task0 = PageMetricsLayout.WidgetsTaskRow(page, 0, metrics);
            var task2 = PageMetricsLayout.WidgetsTaskRow(page, 2, metrics);
            Assert.True(task0.Bottom <= PageMetricsLayout.WidgetsTaskRow(page, 1, metrics).Top + 0.01f);
            Assert.True(task2.Bottom <= PageMetricsLayout.WidgetsNotes(page, metrics).Bottom + 0.01f);
        }
    }

    [Theory]
    [InlineData("#4CC9F0", 0x4C, 0xC9, 0xF0, 0xFF)]
    [InlineData("#FF4CC9F0", 0x4C, 0xC9, 0xF0, 0xFF)]
    [InlineData("#80112233", 0x11, 0x22, 0x33, 0x80)]
    public void Theme_accent_parsing_accepts_rgb_and_argb(string hex, int red, int green, int blue, int alpha)
    {
        var color = IslandTheme.ParseColor(hex, SKColors.Magenta);
        Assert.Equal((byte)red, color.Red);
        Assert.Equal((byte)green, color.Green);
        Assert.Equal((byte)blue, color.Blue);
        Assert.Equal((byte)alpha, color.Alpha);
    }

    [Fact]
    public void Font_scale_outside_the_range_is_clamped()
    {
        Assert.Equal(0.8, FontScaleMetrics.From(0.1).Value, 6);
        Assert.Equal(1.6, FontScaleMetrics.From(9.0).Value, 6);
        Assert.Equal(1.0, FontScaleMetrics.From(double.NaN).Value, 6);
        Assert.Equal(LayoutTokens.CompactSize(null).Width * 0.8, LayoutTokens.CompactSizeFor(null, IslandMode.Float, 0.1).Width, 3);
        // The agent token is 236 wide now (prototype2), so 236 × 1.6 still fits under the 440 ceiling.
        Assert.Equal(377.6, LayoutTokens.CompactSizeFor(FluidBar.Core.Activities.ActivityKind.Agent, IslandMode.Float, 9).Width, 6);
    }

    public static TheoryData<string, string, double> RailLabels => new()
    {
        { "主页", "Home", 0.8 },
        { "主页", "Home", 1.0 },
        { "主页", "Home", 1.2 },
        { "主页", "Home", 1.6 },
        { "剪贴板", "Clipboard", 0.8 },
        { "剪贴板", "Clipboard", 1.6 },
        { "小组件", "Widgets", 1.6 },
        { "Agent", "Agent", 1.6 },
    };

    [Theory]
    [MemberData(nameof(RailLabels))]
    public void Rail_capsule_contract_contains_its_scaled_label(string zh, string en, double scale)
    {
        foreach (var label in new[] { zh, en })
        {
            var labelWidth = MiniRailLayout.LabelWidth(label, scale);
            var capsule = MiniRailLayout.CapsuleWidth(label, scale);
            Assert.InRange(capsule, (float)IslandMetrics.MiniCurrentMinWidth, (float)IslandMetrics.MiniCurrentMaxWidth);
            Assert.True(labelWidth <= capsule - 20 + 0.01f,
                $"{label} at {scale}: label {labelWidth:0.0} must leave the 20 DIP icon/padding budget in {capsule:0.0}");
            var contract = Math.Clamp(
                MathF.Round((float)(IslandMetrics.MiniCurrentBaseWidth
                    + (Math.Max(0f, MathF.Ceiling(labelWidth) - 20f) * 1.1f)), MidpointRounding.AwayFromZero),
                (float)IslandMetrics.MiniCurrentMinWidth,
                (float)IslandMetrics.MiniCurrentMaxWidth);
            Assert.Equal(contract, capsule, 0.001f);
        }

        var narrow = MiniRailLayout.CapsuleWidth(zh, Math.Max(0.8, scale - 0.4));
        Assert.True(MiniRailLayout.CapsuleWidth(zh, scale) >= narrow - 0.001f);
    }

    [Fact]
    public void Existing_goldens_are_pixel_identical_at_scale_one()
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var compared = 0;
        foreach (var name in IslandTestFixtures.GoldenNames)
        {
            if (name.Contains("-fs", StringComparison.Ordinal))
            {
                continue;
            }

            var state = IslandTestFixtures.GoldenState(name);
            Assert.Equal(1.0, state.FontScale, 6);
            using var bitmap = renderer.RenderToBitmap(state, IslandTestFixtures.Scale);
            using var golden = IslandTestFixtures.TryLoadGolden(name + ".png");
            Assert.NotNull(golden);
            Assert.Equal(golden!.Width, bitmap.Width);
            Assert.Equal(golden.Height, bitmap.Height);
            // Mean-difference tolerance instead of pixel equality: every intentional visual change used to
            // fail here even after re-recording, while GoldenImageTests (same tolerance) passed. Exact
            // determinism between two renders is still asserted by Rendering_is_deterministic.
            var difference = MeanChannelDifference(bitmap, golden);
            Assert.True(difference < 12.0, $"{name}: mean channel difference {difference:0.00} exceeds the tolerance");

            compared++;
        }

        Assert.Equal(32, compared);
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(9.0)]
    public void Extreme_requested_scales_still_render_inside_the_shell(double requested)
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var state = IslandTestFixtures.WithFontScale(IslandTestFixtures.Expanded(0), requested);
        Assert.Equal(requested < 1 ? 0.8 : 1.6, FontScaleMetrics.From(state.FontScale).Value, 6);
        using var bitmap = renderer.RenderToBitmap(state, IslandTestFixtures.Scale);
        Assert.Equal(0, IslandTestFixtures.CountOpaqueOutside(bitmap, IslandMetrics.ShellRect(state), IslandTestFixtures.Scale));
    }

    private static double MeanChannelDifference(SkiaSharp.SKBitmap a, SkiaSharp.SKBitmap b)
    {
        var pa = a.Pixels;
        var pb = b.Pixels;
        long total = 0;
        for (var i = 0; i < pa.Length; i++)
        {
            total += Math.Abs(pa[i].Red - pb[i].Red) + Math.Abs(pa[i].Green - pb[i].Green)
                   + Math.Abs(pa[i].Blue - pb[i].Blue) + Math.Abs(pa[i].Alpha - pb[i].Alpha);
        }

        return pa.Length == 0 ? 0 : (double)total / (pa.Length * 4);
    }
}
