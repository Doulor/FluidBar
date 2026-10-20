using FluidBar.Core.Activities;
using SkiaSharp;

namespace FluidBar.Island.Tests;

/// <summary>
/// Compact pill lyric mode ("lyrics in the pill"): the cover + live line + wave replace the title and
/// artist, an overlong line is clipped to the text column, and a missing lyric keeps the title form.
/// </summary>
public sealed class CompactLyricPillRenderTests
{
    private const string Lyric = "I miss you a little all the time";

    private static IslandRenderState LyricPill(
        string? lyric, string? title = "Miss You a Little (feat. lovelytheband)", float fade = 1f) =>
        IslandTestFixtures.Compact(new CompactActivity(ActivityKind.Media, new CompactModel
        {
            Title = title,
            Subtitle = "Bryce Vine",
            IsActive = true,
            ProgressPercent = 42,
            ShowLyrics = lyric is not null,
            LyricLine = lyric,
            LyricFade = fade,
        }));

    /// <summary>Text column between the artwork and the wave (the compact pill has no play button).</summary>
    private static SKRect TextColumn(IslandRenderState state)
    {
        var shell = IslandMetrics.ShellRect(state);
        return new SKRect(
            shell.Left + CompactMediaLayout.TextLeft,
            shell.Top + 5.5f,
            shell.Left + CompactMediaLayout.TextRight((float)shell.Width),
            shell.Bottom - 5.5f);
    }

    [Fact]
    public void LyricPill_RendersInsideTheShell()
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var state = LyricPill(Lyric);
        using var bitmap = renderer.RenderToBitmap(state, IslandTestFixtures.Scale);

        var shell = IslandMetrics.ShellRect(state);
        Assert.Equal(0, IslandTestFixtures.CountOpaqueOutside(bitmap, shell, IslandTestFixtures.Scale));
        Assert.True(IslandTestFixtures.CountOpaqueInside(bitmap, shell, IslandTestFixtures.Scale) > 200);
        Assert.True(IslandTestFixtures.CountOpaqueInside(bitmap, TextColumn(state), IslandTestFixtures.Scale) > 40,
            "the lyric line should be painted in the text column");
    }

    [Fact]
    public void LyricPill_PaintsDifferentPixelsThanTheTitleForm()
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        using var lyric = renderer.RenderToBitmap(LyricPill(Lyric), IslandTestFixtures.Scale);
        using var title = renderer.RenderToBitmap(LyricPill(null), IslandTestFixtures.Scale);

        Assert.NotEqual(Hash(lyric), Hash(title));
    }

    [Fact]
    public void LyricPill_WithoutALyricKeepsExactlyTheTitleForm()
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var fallback = LyricPill(null);
        var titleForm = IslandTestFixtures.Compact(new CompactActivity(ActivityKind.Media, new CompactModel
        {
            Title = "Miss You a Little (feat. lovelytheband)",
            Subtitle = "Bryce Vine",
            IsActive = true,
            ProgressPercent = 42,
        }));

        using var a = renderer.RenderToBitmap(fallback, IslandTestFixtures.Scale);
        using var b = renderer.RenderToBitmap(titleForm, IslandTestFixtures.Scale);

        // No blank lyric row: the fallback is pixel-identical to the pre-existing title layout.
        Assert.Equal(Hash(b), Hash(a));
    }

    [Fact]
    public void LyricPill_ClipsAnOverlongLineToTheTextColumn()
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var longLyric = string.Join(' ', Enumerable.Repeat("超长歌词", 40));
        var longState = LyricPill(longLyric);
        var shortState = LyricPill(Lyric);
        using var longBitmap = renderer.RenderToBitmap(longState, IslandTestFixtures.Scale);
        using var shortBitmap = renderer.RenderToBitmap(shortState, IslandTestFixtures.Scale);

        var shell = IslandMetrics.ShellRect(longState);
        Assert.Equal(0, IslandTestFixtures.CountOpaqueOutside(longBitmap, shell, IslandTestFixtures.Scale));

        // The gutter between the text column and the wave must be identical for a long and a short
        // line: an overlong lyric is ellipsised + clipped, it can never run into the wave/play buttons.
        var gutter = new SKRect(
            shell.Left + CompactMediaLayout.TextRight((float)shell.Width),
            shell.Top + 4,
            shell.Left + CompactMediaLayout.WaveLeft((float)shell.Width),
            shell.Bottom - 8);
        Assert.Equal(0, DifferingPixels(longBitmap, shortBitmap, gutter));

        // The text column itself must differ (the long line is a different string).
        Assert.NotEqual(Hash(longBitmap), Hash(shortBitmap));
    }

    [Fact]
    public void LyricPill_FadesInFromNothing()
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var invisible = LyricPill(Lyric, fade: 0f);
        var visible = LyricPill(Lyric, fade: 1f);

        using var a = renderer.RenderToBitmap(invisible, IslandTestFixtures.Scale);
        using var b = renderer.RenderToBitmap(visible, IslandTestFixtures.Scale);

        // Alpha 0 must leave the text column untouched: the two frames cannot hash the same.
        Assert.NotEqual(Hash(b), Hash(a));
        var columnA = IslandTestFixtures.CountOpaqueInside(a, TextColumn(invisible), IslandTestFixtures.Scale, 250);
        var columnB = IslandTestFixtures.CountOpaqueInside(b, TextColumn(visible), IslandTestFixtures.Scale, 250);
        Assert.True(columnA < columnB, $"fade 0 should paint less bright text than fade 1 ({columnA} vs {columnB})");
    }

    /// <summary>Counts pixels that differ between two frames inside a DIP rectangle.</summary>
    private static int DifferingPixels(SKBitmap left, SKBitmap right, SKRect dipRect)
    {
        var scale = IslandTestFixtures.Scale;
        var x0 = Math.Max(0, (int)(dipRect.Left * scale));
        var y0 = Math.Max(0, (int)(dipRect.Top * scale));
        var x1 = Math.Min(left.Width, (int)Math.Ceiling(dipRect.Right * scale));
        var y1 = Math.Min(left.Height, (int)Math.Ceiling(dipRect.Bottom * scale));
        var differing = 0;
        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                if (left.GetPixel(x, y) != right.GetPixel(x, y))
                {
                    differing++;
                }
            }
        }

        return differing;
    }

    private static ulong Hash(SKBitmap bitmap)
    {
        ulong hash = 1469598103934665603UL;
        for (var y = 0; y < bitmap.Height; y += 2)
        {
            for (var x = 0; x < bitmap.Width; x += 2)
            {
                var pixel = (uint)bitmap.GetPixel(x, y);
                hash = (hash ^ pixel) * 1099511628211UL;
            }
        }

        return hash;
    }
}
