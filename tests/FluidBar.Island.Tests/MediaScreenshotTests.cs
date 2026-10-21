using FluidBar.Island;
using SkiaSharp;

namespace FluidBar.Island.Tests;

/// <summary>Developer screenshot: renders the expanded media page at 4× for the before/after comparison.</summary>
public sealed class MediaScreenshotTests
{
    [Fact]
    public void Render_media_page_at_4x()
    {
        var target = Environment.GetEnvironmentVariable("FLUIDBAR_MEDIA_SHOT");
        if (string.IsNullOrEmpty(target))
        {
            return;
        }

        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        using var bitmap = renderer.RenderToBitmap(IslandTestFixtures.Expanded(1), 4f);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(target, data.ToArray());
    }
}
