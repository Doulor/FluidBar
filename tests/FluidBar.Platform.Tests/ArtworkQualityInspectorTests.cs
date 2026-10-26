using System.Drawing;
using System.Drawing.Imaging;
using FluidBar.Platform.Windows.Media;

namespace FluidBar.Platform.Tests;

/// <summary>
/// A player can hand out a flat "no cover" tile as if it were artwork (KuGou: 120×120 grey, byte-identical across
/// songs). Accepting it painted a grey square and blocked the fallbacks, which looked exactly like a missing cover.
/// </summary>
public sealed class ArtworkQualityInspectorTests
{
    [Fact]
    public void A_flat_light_grey_tile_is_recognised_as_a_placeholder()
    {
        // The measured KuGou tile: two nearly identical light greys.
        var bytes = Png(120, 120, (x, y) => (x < 60 ? Color.FromArgb(203, 208, 217) : Color.FromArgb(206, 210, 219)));
        Assert.True(ArtworkQualityInspector.IsLikelyPlaceholder(bytes));
    }

    [Fact]
    public void The_measured_kugou_tile_is_recognised_even_though_it_is_a_subtle_gradient()
    {
        // The real asset is not a single flat colour: it shades from #C8CDD6 to #CFD3DC (spread 27, five quantised
        // colours), which is why a "one or two colours" rule missed it in the first cut.
        var bytes = Png(120, 120, (x, y) =>
        {
            var t = (x + y) / 238.0;
            return Color.FromArgb(
                (int)(0xC8 + (7 * t)),
                (int)(0xCD + (6 * t)),
                (int)(0xD6 + (6 * t)));
        });
        Assert.True(ArtworkQualityInspector.IsLikelyPlaceholder(bytes));
    }

    [Fact]
    public void A_real_cover_is_never_thrown_away()
    {
        var bytes = Png(120, 120, (x, y) => Color.FromArgb((x * 2) % 256, (y * 2) % 256, (x + y) % 256));
        Assert.False(ArtworkQualityInspector.IsLikelyPlaceholder(bytes));
    }

    [Fact]
    public void A_greyscale_cover_with_contrast_survives()
    {
        var bytes = Png(120, 120, (x, y) => { var v = (byte)((x * 255) / 120); return Color.FromArgb(v, v, v); });
        Assert.False(ArtworkQualityInspector.IsLikelyPlaceholder(bytes));
    }

    [Fact]
    public void A_dark_solid_cover_is_kept()
    {
        // Real album art is sometimes a solid dark square; only bright flat tiles are treated as "no cover".
        var bytes = Png(120, 120, (_, _) => Color.FromArgb(6, 6, 8));
        Assert.False(ArtworkQualityInspector.IsLikelyPlaceholder(bytes));
    }

    [Fact]
    public void Garbage_and_empty_inputs_are_not_placeholders()
    {
        Assert.False(ArtworkQualityInspector.IsLikelyPlaceholder(Array.Empty<byte>()));
        Assert.False(ArtworkQualityInspector.IsLikelyPlaceholder(new byte[] { 1, 2, 3, 4, 5 }));
    }

    [Fact]
    public void A_placeholder_hands_over_to_the_player_cache_and_then_the_catalogue()
    {
        // The exact chain the service runs: reject the tile, then KuGou's own cache, then the catalogue.
        var tile = Png(120, 120, (x, y) =>
        {
            var t = (x + y) / 238.0;
            return Color.FromArgb((int)(0xC8 + (7 * t)), (int)(0xCD + (6 * t)), (int)(0xD6 + (6 * t)));
        });
        Assert.True(ArtworkQualityInspector.IsLikelyPlaceholder(tile));

        const int bytesAfterRejection = 0;
        Assert.True(MediaArtworkFallbackPolicy.ShouldUsePlayerCoverCache("KugouMusic.exe", bytesAfterRejection));
        Assert.True(MediaArtworkFallbackPolicy.ShouldTryCatalogues(bytesAfterRejection));
    }

    private static byte[] Png(int width, int height, Func<int, int, Color> pixel)
    {
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                bitmap.SetPixel(x, y, pixel(x, y));
            }
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }
}
