using SkiaSharp;

namespace FluidBar.Island.Tests;

public sealed class LyricGlowRenderingTests
{
    private static SKBitmap Render(float strength, double progress = 0.5)
    {
        var bitmap = new SKBitmap(500, 90);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);
        using var engine = new TextEngine();
        KaraokeText.Draw(new ScaledText(engine), canvas, "AAAA BBBB", progress, IslandType.Lyric,
            new SKRect(20, 20, 480, 70), SKColors.Gray, SKColors.White, TextAlign.Left, 1, strength);
        return bitmap;
    }

    private static long Energy(SKBitmap bitmap) => bitmap.Pixels.Sum(pixel => (long)pixel.Red + pixel.Green + pixel.Blue);

    [Fact]
    public void Halo_strength_increases_energy_without_changing_unsung_pixels()
    {
        using var off = Render(0);
        using var reduced = Render(0.7f);
        using var original = Render(1);
        using var unsung = Render(0, 0);
        Assert.True(Energy(off) > Energy(unsung), "Disabling halo must retain sung text highlighting.");
        Assert.True(Energy(reduced) > Energy(off));
        Assert.True(Energy(original) > Energy(reduced));
        using var engine = new TextEngine();
        var text = new ScaledText(engine);
        var unsungStart = (int)Math.Ceiling(20 + text.Measure("AAAA BBBB", IslandType.Lyric) / 2);
        var unsungInk = 0;
        for (var x = unsungStart; x < off.Width; x++)
        {
            for (var y = 0; y < off.Height; y++)
            {
                var pixel = off.GetPixel(x, y);
                Assert.Equal(pixel, original.GetPixel(x, y));
                if (pixel.Red > 0) unsungInk++;
            }
        }
        Assert.True(unsungInk > 0, "The unchanged region must contain actual unsung glyphs, not just background.");
    }

    [Theory]
    [InlineData(float.NaN, 0.7f)]
    [InlineData(float.PositiveInfinity, 0.7f)]
    [InlineData(-1, 0)]
    [InlineData(2, 1)]
    public void Invalid_strength_is_normalized(float input, float expected)
    {
        using var actual = Render(input);
        using var normalized = Render(expected);
        Assert.Equal(normalized.Pixels, actual.Pixels);
    }

    [Fact]
    public void Both_models_default_to_seventy_percent()
    {
        Assert.Equal(0.7f, new CompactModel().LyricGlowStrength);
        Assert.Equal(0.7f, new MediaPageModel().LyricGlowStrength);
    }
}
