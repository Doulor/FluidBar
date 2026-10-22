using FluidBar.Island;
using SkiaSharp;

namespace FluidBar.Island.Tests;

/// <summary>
/// Guards for the perf-audit changes: bounded shadow memory during morphs, downscaled artwork decode,
/// lazy emoji font loading and the resource accounting the host probe reads.
/// </summary>
public sealed class PerfAuditTests
{
    private static SKBitmap Canvas(out SKCanvas canvas)
    {
        var bitmap = new SKBitmap(new SKImageInfo(882, 576, SKColorType.Bgra8888, SKAlphaType.Premul));
        canvas = new SKCanvas(bitmap);
        return bitmap;
    }

    [Fact]
    public void Shadow_cache_is_bounded_and_reports_pixel_bytes()
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        using var bitmap = Canvas(out var canvas);
        for (var i = 0; i < 60; i++)
        {
            var state = IslandTestFixtures.MediaCompact() with
            {
                ShellWidth = 140 + (i * 7),
                ShellHeight = 36 + ((i % 9) * 5),
            };
            renderer.Render(canvas, state, IslandTestFixtures.Scale);
        }

        var stats = renderer.ResourceStats;
        Assert.InRange(stats.ShadowCacheCount, 1, renderer.Options.ShadowCacheCapacity);
        Assert.True(stats.ShadowPixelBytes > 0, "shadow cache did not account any pixels");
        Assert.True(
            stats.ShadowPixelBytes <= renderer.Options.ShadowCacheCapacity * 1100L * 1000L,
            $"shadow cache retained {stats.ShadowPixelBytes} B; expected <= capacity * ~1.1 MB");
    }

    [Fact]
    public void Morphing_frames_reuse_shadow_buckets()
    {
        using var exactRenderer = new IslandRenderer(IslandTestFixtures.Theme);
        using var morphRenderer = new IslandRenderer(IslandTestFixtures.Theme);
        using var bitmap = Canvas(out var canvas);

        for (var i = 0; i < 60; i++)
        {
            var width = 140 + (i * 5.5);
            var exact = IslandTestFixtures.MediaCompact() with { ShellWidth = width, ShellHeight = 40 + (i * 4) };
            renderer(exactRenderer, canvas, exact with { ShellMorphing = false });
            renderer(morphRenderer, canvas, exact with { ShellMorphing = true });
        }

        static void renderer(IslandRenderer renderer, SKCanvas canvas, IslandRenderState state)
            => renderer.Render(canvas, state, IslandTestFixtures.Scale);

        var exactBuilds = exactRenderer.ResourceStats.ShadowBuilds;
        var morphBuilds = morphRenderer.ResourceStats.ShadowBuilds;
        Assert.True(morphBuilds < exactBuilds,
            $"morph buckets did not reduce bakes (exact {exactBuilds}, morph {morphBuilds})");
        Assert.True(morphBuilds <= (exactBuilds / 2) + 4,
            $"morph built {morphBuilds} shadows vs exact {exactBuilds}; expected at least halved");
    }

    [Fact]
    public void Artwork_is_decoded_downscaled_and_bounded()
    {
        using var cover = new SKBitmap(new SKImageInfo(1024, 1024, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var coverCanvas = new SKCanvas(cover))
        {
            coverCanvas.Clear(new SKColor(0xff, 0x33, 0x66));
        }

        using var data = SKImage.FromBitmap(cover).Encode(SKEncodedImageFormat.Png, 90);
        var png = data.ToArray();

        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        using var bitmap = Canvas(out var canvas);
        var state = IslandTestFixtures.MediaCompact();
        var compact = state.Compact!.Value;
        renderer.Render(canvas, state with
        {
            Compact = compact with
            {
                Model = compact.Model with { Artwork = png, ArtworkKey = "perf-audit-cover" },
            },
        }, IslandTestFixtures.Scale);

        var stats = renderer.ResourceStats;
        Assert.Equal(1, stats.ArtworkCount);
        Assert.True(stats.ArtworkPixelBytes > 0, "artwork cache did not account any pixels");
        const long bounded = 256L * 256L * 4;
        Assert.True(stats.ArtworkPixelBytes <= bounded,
            $"1024x1024 cover retained {stats.ArtworkPixelBytes} B; expected <= {bounded} B (256 px long edge)");
    }

    [Fact]
    public void Emoji_face_is_loaded_lazily()
    {
        using var engine = new TextEngine();
        Assert.False(engine.EmojiFaceLoaded, "emoji HarfBuzz face must not be loaded by the constructor");

        using var bitmap = Canvas(out var canvas);
        engine.Draw(canvas, "\U0001F600", new TextStyle(FontRole.Ui, 14), new SKRect(0, 0, 200, 40), SKColors.White);

        var hasEmojiFont = File.Exists(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "seguiemj.ttf"));
        if (hasEmojiFont)
        {
            Assert.True(engine.EmojiFaceLoaded, "drawing emoji text should load the lazy face");
        }
    }
}
