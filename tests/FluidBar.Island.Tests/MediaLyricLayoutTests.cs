using FluidBar.Island;
using SkiaSharp;

namespace FluidBar.Island.Tests;

/// <summary>
/// Media page lyric band: the shipped geometry (PageVisual) must stay ordered and inside the page body at
/// every font scale, and the type must keep the readability-first sizes.
/// </summary>
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
        var page = IslandMetrics.PageRect(state, 1);
        var body = PageVisual.Body(page);

        var card = PageVisual.MediaLyricCard(page);
        var seek = PageVisual.MediaSeekRow(page);
        var play = PageVisual.MediaPlay(page);

        Assert.True(card.Bottom > card.Top, $"card collapsed at {scale}");
        Assert.True(seek.Top >= card.Bottom, $"seek row overlaps the lyric band at {scale}");
        Assert.True(play.Top >= seek.Bottom, $"transport overlaps the seek row at {scale}");
        Assert.True(play.Bottom <= body.Bottom + 0.51,
            $"transport row bottom {play.Bottom:0.0} overflows the body bottom {body.Bottom:0.0} at {scale}");
    }

    [Fact]
    public void Media_lyric_band_matches_the_244_layout_at_scale_one()
    {
        var state = IslandTestFixtures.Expanded(1);
        var page = IslandMetrics.PageRect(state, 1);
        var card = PageVisual.MediaLyricCard(page);
        var seek = PageVisual.MediaSeekRow(page);

        // 440 × 244 panel: lyric band 72..105 (18 + 15), seek row 112..126, both on the 8 DIP grid.
        Assert.Equal(32, card.Height, 1);
        Assert.Equal(64, card.Top - page.Top - PageVisual.BodyTop, 1);
        Assert.Equal(104, seek.Top - page.Top - PageVisual.BodyTop, 1);
        Assert.Equal(14, seek.Height, 1);
        Assert.Equal(8, seek.Top - card.Bottom, 1);
    }

    [Fact]
    public void Media_lyric_line_is_medium_weight_like_the_prototype()
    {
        // Readability-first: medium role at 13 DIP for the current line, 11.5 DIP for the next one.
        Assert.Equal(FontRole.UiMedium, PageVisual.MediaLyricStyle.Role);
        Assert.Equal(13, PageVisual.MediaLyricStyle.Size, 3);
        Assert.Equal(FontRole.Ui, PageVisual.MediaLyricNextStyle.Role);
        Assert.Equal(11.5, PageVisual.MediaLyricNextStyle.Size, 3);
    }
}
