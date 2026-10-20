using FluidBar.Island;
using SkiaSharp;

namespace FluidBar.Island.Tests;

/// <summary>Media page lyric card: prototype2 typography and the three font scales staying inside the shell.</summary>
public sealed class MediaLyricLayoutTests
{
    [Theory]
    [InlineData(0.8)]
    [InlineData(1.0)]
    [InlineData(1.2)]
    [InlineData(1.4)]
    [InlineData(1.6)]
    public void Media_transport_row_stays_inside_the_page_at_every_font_scale(double scale)
    {
        var state = IslandTestFixtures.WithFontScale(IslandTestFixtures.Expanded(1), (float)scale);
        var metrics = FontScaleMetrics.From((float)scale);
        var page = IslandMetrics.PageRect(state, 1);

        var card = PageMetricsLayout.MediaLyricCard(page, metrics);
        var seek = PageMetricsLayout.MediaSeekRow(page, metrics);
        var play = PageMetricsLayout.MediaPlay(page, metrics);

        Assert.True(card.Bottom > card.Top, $"card collapsed at {scale}");
        Assert.True(seek.Top >= card.Bottom, $"seek row overlaps the lyric card at {scale}");
        Assert.True(play.Top >= seek.Bottom, $"transport overlaps the seek row at {scale}");
        Assert.True(play.Bottom <= page.Bottom + 0.51,
            $"transport row bottom {play.Bottom:0.0} overflows the page bottom {page.Bottom:0.0} at {scale}");
    }

    [Fact]
    public void Media_lyric_card_matches_the_prototype_metrics_at_scale_one()
    {
        var state = IslandTestFixtures.Expanded(1);
        var metrics = FontScaleMetrics.From(1f);
        var page = IslandMetrics.PageRect(state, 1);
        var card = PageMetricsLayout.MediaLyricCard(page, metrics);

        // Readability-first 13 DIP lyric / 10.5 DIP next line fit the prototype's 46 DIP card
        // (17 DIP lyric + 14 DIP next line + 7/3/4 padding = 45); radius and 8/10 margins unchanged.
        Assert.Equal(46, card.Height, 1);
        Assert.Equal(8, card.Top - PageMetricsLayout.MediaTopBottom(page, metrics), 1);
        // `.seek-row`: margin-top 10, height 22.
        var seek = PageMetricsLayout.MediaSeekRow(page, metrics);
        Assert.Equal(10, seek.Top - card.Bottom, 1);
        Assert.Equal(22, seek.Height, 1);
    }

    [Fact]
    public void Media_lyric_line_is_medium_weight_like_the_prototype()
    {
        // Readability-first: keep the medium role but 13 DIP (prototype was 11) and 10.5 DIP next.
        Assert.Equal(FontRole.UiMedium, IslandType.Lyric.Role);
        Assert.Equal(13, IslandType.Lyric.Size, 3);
        Assert.Equal(FontRole.Ui, IslandType.LyricNext.Role);
        Assert.Equal(10.5, IslandType.LyricNext.Size, 3);
    }
}
