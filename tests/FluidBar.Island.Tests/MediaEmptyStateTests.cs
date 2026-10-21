using FluidBar.Island;
using SkiaSharp;

namespace FluidBar.Island.Tests;

/// <summary>
/// Media page empty states: the output/queue slots and the lyric band always read as intentional (light
/// localized placeholders instead of a blank gap) and every combination of no output / no queue / no
/// lyrics / no artwork stays inside the shell with both slots painted.
/// </summary>
public sealed class MediaEmptyStateTests
{
    private static readonly MediaPageModel Baseline = IslandTestFixtures.Expanded(1).Pages.Media with
    {
        OutputPlaceholder = "No output",
        QueuePlaceholder = "No queue",
        QueueLabel = "Queue",
        NoLyricsText = "No lyrics",
    };

    private static IslandRenderState With(MediaPageModel media)
    {
        var fixture = IslandTestFixtures.Expanded(1);
        return fixture with { Pages = fixture.Pages with { Media = media } };
    }

    private static MediaPageModel Variant(bool output, bool queue, bool lyrics, bool artwork) => Baseline with
    {
        // The capability bits gate the slots: values without the capability paint as the disabled caption.
        OutputAvailable = output,
        QueueAvailable = queue,
        OutputName = output ? Baseline.OutputName : null,
        QueueCount = queue ? Baseline.QueueCount : 0,
        LyricLine = lyrics ? Baseline.LyricLine : null,
        NextLyricLine = lyrics ? Baseline.NextLyricLine : null,
        NextLyricLabel = lyrics ? Baseline.NextLyricLabel : null,
        Artwork = artwork ? Baseline.Artwork : null,
        ArtworkKey = artwork ? Baseline.ArtworkKey : null,
    };

    [Fact]
    public void Every_empty_state_combination_paints_both_transport_slots_and_the_lyric_band()
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var state0 = IslandTestFixtures.Expanded(1);
        var page = IslandMetrics.PageRect(state0, 1);
        var metrics = FontScaleMetrics.One;
        // Everything comes from the PageVisual tokens the renderer paints, so a layout change cannot make
        // this test inspect a rectangle nothing is drawn in.
        var artwork = PageVisual.MediaArt(page);
        var lyric = PageVisual.MediaLyricCard(page);
        var output = PageVisual.MediaOutput(page);
        var queue = PageVisual.MediaQueue(page);

        for (var mask = 0; mask < 16; mask++)
        {
            var state = With(Variant((mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0, (mask & 8) != 0));
            using var bitmap = renderer.RenderToBitmap(state, IslandTestFixtures.Scale);
            Assert.Equal(0, IslandTestFixtures.CountOpaqueOutside(bitmap, IslandMetrics.ShellRect(state), IslandTestFixtures.Scale));

            Assert.True(CountInk(bitmap, artwork) > 200, $"comb {mask}: the artwork slot is blank");
            // Lyrics paint two text lines (no background by design); without them the band holds the soft
            // L1 empty-state card plus its caption, which is far dimmer than text ink.
            var lyricsInk = (mask & 4) != 0 ? 200 : 20;
            Assert.True(CountInk(bitmap, lyric) > lyricsInk, $"comb {mask}: the lyric band is blank (half-rendered page)");
            Assert.True(CountInk(bitmap, output) > 20, $"comb {mask}: the output slot is blank");
            Assert.True(CountInk(bitmap, queue) > 20, $"comb {mask}: the queue slot is blank");
        }
    }

    [Fact]
    public void Missing_output_queue_and_lyrics_show_the_light_placeholders()
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var empty = Variant(output: false, queue: false, lyrics: false, artwork: true);
        var withPlaceholders = With(empty);
        var withoutPlaceholders = With(empty with
        {
            OutputPlaceholder = null,
            QueuePlaceholder = null,
            NoLyricsText = null,
        });

        using var withInk = renderer.RenderToBitmap(withPlaceholders, IslandTestFixtures.Scale);
        using var bare = renderer.RenderToBitmap(withoutPlaceholders, IslandTestFixtures.Scale);

        var page = IslandMetrics.PageRect(withPlaceholders, 1);
        var metrics = FontScaleMetrics.One;
        var lyric = PageVisual.MediaLyricCard(page);
        var output = PageVisual.MediaOutput(page);
        var queue = PageVisual.MediaQueue(page);

        // The lyric band gets a real card + caption instead of staying a gap, and both transport slots
        // gain their light caption text next to the icon.
        Assert.True(CountInk(withInk, lyric) > CountInk(bare, lyric) + 100);
        Assert.True(CountInk(withInk, output) > CountInk(bare, output));
        Assert.True(CountInk(withInk, queue) > CountInk(bare, queue));
    }

    [Fact]
    public void No_lyrics_caption_is_centred_in_the_lyric_band()
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var state = With(Variant(output: true, queue: true, lyrics: false, artwork: true));
        using var bitmap = renderer.RenderToBitmap(state, IslandTestFixtures.Scale);

        var page = IslandMetrics.PageRect(state, 1);
        var lyric = PageVisual.MediaLyricCard(page);
        var (minX, maxX) = InkBoundsX(bitmap, lyric);
        Assert.True(maxX > minX, "the no-lyrics caption did not paint");
        var inkCenter = (minX + maxX) / 2f;
        var cardCenter = (lyric.Left + lyric.Right) / 2f * IslandTestFixtures.Scale;
        // Centred caption: the glyph band's midpoint sits within 6 physical pixels of the card midpoint.
        Assert.InRange(inkCenter, cardCenter - 6, cardCenter + 6);
    }

    [Fact]
    public void Output_and_queue_values_replace_the_placeholders()
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var withData = With(Variant(output: true, queue: true, lyrics: true, artwork: true));
        var withoutData = With(Variant(output: false, queue: false, lyrics: true, artwork: true));

        using var dataBitmap = renderer.RenderToBitmap(withData, IslandTestFixtures.Scale);
        using var emptyBitmap = renderer.RenderToBitmap(withoutData, IslandTestFixtures.Scale);

        var page = IslandMetrics.PageRect(withData, 1);
        var metrics = FontScaleMetrics.One;
        var output = PageVisual.MediaOutput(page);
        var queue = PageVisual.MediaQueue(page);

        // Real values ("Speakers", queue count) paint different ink than the placeholder captions.
        Assert.NotEqual(CountInk(dataBitmap, output), CountInk(emptyBitmap, output));
        Assert.NotEqual(CountInk(dataBitmap, queue), CountInk(emptyBitmap, queue));
    }

    private static (int MinX, int MaxX) InkBoundsX(SKBitmap bitmap, SKRect dipRect)
    {
        var left = Math.Max(0, (int)Math.Floor(dipRect.Left * IslandTestFixtures.Scale));
        var right = Math.Min(bitmap.Width, (int)Math.Ceiling(dipRect.Right * IslandTestFixtures.Scale));
        var top = Math.Max(0, (int)Math.Floor(dipRect.Top * IslandTestFixtures.Scale));
        var bottom = Math.Min(bitmap.Height, (int)Math.Ceiling(dipRect.Bottom * IslandTestFixtures.Scale));
        var pixels = bitmap.Pixels;
        var minX = int.MaxValue;
        var maxX = -1;
        for (var y = top; y < bottom; y++)
        {
            var row = y * bitmap.Width;
            for (var x = left; x < right; x++)
            {
                var pixel = pixels[row + x];
                if (pixel.Alpha > 200
                    && (Math.Abs(pixel.Red - 8) + Math.Abs(pixel.Green - 8) + Math.Abs(pixel.Blue - 10)) > 24)
                {
                    if (x < minX) { minX = x; }
                    if (x > maxX) { maxX = x; }
                }
            }
        }

        return maxX < minX ? (0, 0) : (minX, maxX);
    }

    private static int CountInk(SKBitmap bitmap, SKRect dipRect)
    {
        var left = Math.Max(0, (int)Math.Floor(dipRect.Left * IslandTestFixtures.Scale));
        var top = Math.Max(0, (int)Math.Floor(dipRect.Top * IslandTestFixtures.Scale));
        var right = Math.Min(bitmap.Width, (int)Math.Ceiling(dipRect.Right * IslandTestFixtures.Scale));
        var bottom = Math.Min(bitmap.Height, (int)Math.Ceiling(dipRect.Bottom * IslandTestFixtures.Scale));
        var pixels = bitmap.Pixels;
        var count = 0;
        for (var y = top; y < bottom; y++)
        {
            var row = y * bitmap.Width;
            for (var x = left; x < right; x++)
            {
                var pixel = pixels[row + x];
                if (pixel.Alpha > 200
                    && (Math.Abs(pixel.Red - 8) + Math.Abs(pixel.Green - 8) + Math.Abs(pixel.Blue - 10)) > 24)
                {
                    count++;
                }
            }
        }

        return count;
    }
}
