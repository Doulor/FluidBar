using FluidBar.Island;
using SkiaSharp;

namespace FluidBar.Island.Tests;

/// <summary>
/// iOS Now Playing geometry pins: the media page is built on an 8 DIP grid, and these numbers come from
/// the design study (72 DIP cover, 3 DIP track, 36 DIP play circle). Pinning them here means a layout edit
/// that breaks the ratio fails loudly instead of quietly drifting.
/// </summary>
public sealed class MediaNowPlayingLayoutTests
{
    private static SKRect Page() => IslandMetrics.PageRect(IslandTestFixtures.Expanded(1), 1);

    [Fact]
    public void Cover_is_a_72_dip_square_with_a_12_dip_radius()
    {
        var page = Page();
        var art = PageVisual.MediaArt(page);

        Assert.Equal(72, art.Width, 3);
        Assert.Equal(72, art.Height, 3);
        Assert.Equal(12, PageVisual.MediaArtRadius, 3);

        // Vertically centred against the three-line text column (they share the same 72 DIP band).
        var textTop = PageVisual.MediaEyebrow(page).Top;
        var textBottom = PageVisual.MediaAlbum(page).Bottom;
        Assert.Equal(art.Top, textTop, 3);
        Assert.Equal(art.Bottom, textBottom, 3);

        // 8 DIP gap between the cover and the text column.
        Assert.Equal(art.Right + 8, PageVisual.MediaTitle(page).Left, 3);
    }

    [Fact]
    public void Lyric_band_uses_20_and_16_dip_line_boxes_without_a_background()
    {
        var page = Page();
        var line1 = PageVisual.MediaLyricLine1(page);
        var line2 = PageVisual.MediaLyricLine2(page);

        Assert.Equal(20, line1.Height, 3);
        Assert.Equal(16, line2.Height, 3);
        Assert.Equal(line1.Bottom, line2.Top, 3);
        Assert.Equal(line1.Top, PageVisual.MediaLyricCard(page).Top, 3);
        Assert.Equal(line2.Bottom, PageVisual.MediaLyricCard(page).Bottom, 3);
        Assert.Equal(13, PageVisual.MediaLyricStyle.Size, 3);
        Assert.Equal(11.5f, PageVisual.MediaLyricNextStyle.Size, 3);
    }

    [Fact]
    public void Progress_track_is_3_dip_and_flush_with_the_time_labels()
    {
        var page = Page();
        var track = PageVisual.MediaSeekTrack(page);
        var row = PageVisual.MediaSeekRow(page);

        Assert.Equal(3, track.Height, 3);
        Assert.Equal(track.MidY, row.MidY, 3);

        // Both clocks sit outside the track and end exactly where it starts/ends.
        Assert.Equal(row.Left + PageVisual.MediaTimeWidth, track.Left, 3);
        Assert.Equal(row.Right - PageVisual.MediaTimeWidth, track.Right, 3);
        Assert.Equal(48, PageVisual.MediaTimeWidth, 3);
        Assert.Equal(11, PageVisual.MediaSeekTimeStyle.Size, 3);
    }

    [Fact]
    public void Transport_is_a_36_dip_circle_between_two_28_dip_buttons_with_16_dip_gaps()
    {
        var page = Page();
        var previous = PageVisual.MediaPrevious(page);
        var play = PageVisual.MediaPlay(page);
        var next = PageVisual.MediaNext(page);

        Assert.Equal(36, play.Width, 3);
        Assert.Equal(36, play.Height, 3);
        Assert.Equal(28, previous.Width, 3);
        Assert.Equal(28, next.Width, 3);
        Assert.Equal(16, play.Left - previous.Right, 3);
        Assert.Equal(16, next.Left - play.Right, 3);
        Assert.Equal(page.MidX, play.MidX, 3);
        Assert.Equal(play.MidY, previous.MidY, 3);
        Assert.Equal(play.MidY, next.MidY, 3);
    }

    [Fact]
    public void Footer_row_is_aligned_with_the_progress_track()
    {
        var page = Page();
        var footer = PageVisual.MediaFooter(page);
        var track = PageVisual.MediaSeekTrack(page);

        Assert.Equal(track.Left, footer.Left, 3);
        Assert.Equal(track.Right, footer.Right, 3);
        Assert.Equal(16, footer.Height, 3);
        Assert.Equal(11, PageVisual.MediaFooterStyle.Size, 3);
        Assert.Equal(14, PageVisual.MediaFooterIcon, 3);
    }

    [Fact]
    public void Media_controls_stay_inside_the_page_body_and_on_the_8_dip_grid()
    {
        var page = Page();
        var body = PageVisual.Body(page);

        Assert.True(PageVisual.MediaArt(page).Bottom <= body.Bottom);
        Assert.True(PageVisual.MediaLyricCard(page).Bottom <= body.Bottom);
        Assert.True(PageVisual.MediaSeekRow(page).Bottom <= body.Bottom);
        Assert.True(PageVisual.MediaPlay(page).Bottom <= body.Bottom);
        Assert.True(PageVisual.MediaFooter(page).Bottom <= body.Bottom + 4);

        // Every media top edge sits on an 8 DIP step of the body.
        foreach (var top in new[]
                 {
                     PageVisual.MediaArt(page).Top, PageVisual.MediaLyricCard(page).Top,
                     PageVisual.MediaSeekRow(page).Top, PageVisual.MediaFooter(page).Top,
                 })
        {
            Assert.Equal(0, (top - body.Top) % 8, 3);
        }
    }
}
