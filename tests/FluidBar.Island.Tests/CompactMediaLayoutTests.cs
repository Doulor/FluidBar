namespace FluidBar.Island.Tests;

/// <summary>
/// Compact media pill geometry: the play/pause button was removed, the pill width is the fixed chrome
/// (artwork + paddings + wave) plus the measured text, clamped to the token floor and the surface ceiling.
/// </summary>
public sealed class CompactMediaLayoutTests
{
    private const float TokenWidth = 226f;
    private const float SurfaceWidth = 440f;

    [Fact]
    public void Fixed_chrome_is_artwork_gaps_wave_and_paddings()
    {
        Assert.Equal(45, CompactMediaLayout.TextLeft, 0.001);
        Assert.Equal(39, CompactMediaLayout.TextToTrailingEdge, 0.001);
        Assert.Equal(84, CompactMediaLayout.FixedWidth, 0.001);
    }

    [Fact]
    public void Width_for_text_adds_the_measured_line_to_the_fixed_chrome()
    {
        // Fixed chrome + slack so the shaper never ellipsises a line that "exactly" fits.
        Assert.Equal(84 + CompactMediaLayout.TextSlack, CompactMediaLayout.WidthForText(0), 0.001);
        Assert.Equal(206, CompactMediaLayout.WidthForText(120), 0.001);
        Assert.Equal(84 + CompactMediaLayout.TextSlack, CompactMediaLayout.WidthForText(float.NaN), 0.001);
    }

    [Fact]
    public void Short_line_keeps_the_compact_token_width()
    {
        // 40 DIP of text would need 124 DIP; the pill stays at the token so the contract never shrinks.
        Assert.Equal(TokenWidth, CompactMediaLayout.AdaptiveWidth(40, TokenWidth, SurfaceWidth), 0.001);
    }

    [Fact]
    public void Long_line_grows_the_pill_to_fit_the_whole_line()
    {
        var width = CompactMediaLayout.AdaptiveWidth(200, TokenWidth, SurfaceWidth);

        Assert.Equal(286, width, 0.001);
        Assert.True(width > TokenWidth);
        // The measured text still fits between the artwork and the wave, with the slack on top.
        Assert.True(CompactMediaLayout.TextRight(width) - CompactMediaLayout.TextLeft > 200);
    }

    [Fact]
    public void Overlong_line_is_clamped_to_the_surface_width()
    {
        Assert.Equal(SurfaceWidth, CompactMediaLayout.AdaptiveWidth(2000, TokenWidth, SurfaceWidth), 0.001);
    }

    [Fact]
    public void No_text_falls_back_to_the_token_width()
    {
        Assert.Equal(TokenWidth, CompactMediaLayout.AdaptiveWidth(0, TokenWidth, SurfaceWidth), 0.001);
        Assert.Equal(TokenWidth, CompactMediaLayout.AdaptiveWidth(-5, TokenWidth, SurfaceWidth), 0.001);
    }

    [Fact]
    public void Rects_stay_ordered_and_inside_the_pill()
    {
        foreach (var width in new[] { 226f, 284f, 440f })
        {
            var art = CompactMediaLayout.ArtworkRect(40);
            var wave = CompactMediaLayout.WaveRect(width, 40);
            var textRight = CompactMediaLayout.TextRight(width);

            Assert.True(art.Right < CompactMediaLayout.TextLeft + 0.001f);
            Assert.True(textRight > CompactMediaLayout.TextLeft);
            Assert.True(textRight < wave.Left);
            Assert.Equal(width - CompactMediaLayout.RightPadding, wave.Right, 0.001);
            Assert.Equal((40 - CompactMediaLayout.WaveHeight) / 2, wave.Top, 0.001);
        }
    }
}
