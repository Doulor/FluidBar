using SkiaSharp;

namespace FluidBar.Island.Tests;

/// <summary>The clipboard page can paint eight thumbnails without the image cache evicting the first row.</summary>
public sealed class ArtworkImageCacheTests
{
    [Fact]
    public void Eight_distinct_clipboard_images_stay_cached()
    {
        using var bitmap = new SKBitmap(2, 2);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 90);
        var png = encoded.ToArray();

        using var cache = new ArtworkImageCache();
        for (var i = 0; i < 8; i++)
        {
            Assert.NotNull(cache.Get($"clipboard-{i}", png));
        }

        Assert.Equal(8, cache.Count);
        Assert.Equal(8, cache.Builds);
    }
}
