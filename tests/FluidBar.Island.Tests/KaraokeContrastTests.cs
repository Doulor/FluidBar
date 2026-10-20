using SkiaSharp;

namespace FluidBar.Island.Tests;

/// <summary>
/// Task-42: the karaoke boundary has to survive the real dark glass card. The sung prefix is a lifted
/// accent; the unsung remainder is the foreground at 47 % opacity. This test measures the effective
/// (composited) relative luminance difference, not just the raw colour values.
/// </summary>
public sealed class KaraokeContrastTests
{
    [Fact]
    public void Sung_and_unsung_lyrics_differ_by_more_than_30_luminance_levels()
    {
        var background = new SKColor(0x0B, 0x0B, 0x0E);
        var foreground = new SKColor(0xF5, 0xF5, 0xF7);
        var accent = new SKColor(0xF0, 0x6A, 0xB0);

        var unsung = BlendOver(KaraokeText.UnsungColor(foreground), background);
        var sung = KaraokeText.SungColor(accent);
        var delta = (KaraokeText.RelativeLuminance(sung) - KaraokeText.RelativeLuminance(unsung)) * 255;

        Assert.True(delta > 30, $"karaoke luminance delta is only {delta:0.0}");
    }

    private static SKColor BlendOver(SKColor foreground, SKColor background)
    {
        var alpha = foreground.Alpha / 255.0;
        byte Mix(byte fg, byte bg) => (byte)Math.Clamp((fg * alpha) + (bg * (1 - alpha)), 0, 255);
        return new SKColor(Mix(foreground.Red, background.Red),
            Mix(foreground.Green, background.Green), Mix(foreground.Blue, background.Blue));
    }
}
