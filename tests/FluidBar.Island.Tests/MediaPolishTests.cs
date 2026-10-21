using FluidBar.Core.Activities;
using SkiaSharp;

namespace FluidBar.Island.Tests;

/// <summary>Artwork accent extraction, karaoke rendering and the press-feedback render state.</summary>
public sealed class MediaPolishTests
{
    private static byte[] SolidPng(byte r, byte g, byte b, int size = 32)
    {
        using var bitmap = new SKBitmap(size, size);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(new SKColor(r, g, b));
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Fact]
    public void Extract_ReturnsTheCoversOwnColour()
    {
        var hex = ArtworkAccent.Extract(SolidPng(220, 30, 40));

        Assert.NotNull(hex);
        Assert.StartsWith("#", hex);
        var value = Convert.ToInt32(hex![1..], 16);
        var red = (value >> 16) & 0xFF;
        var green = (value >> 8) & 0xFF;
        var blue = value & 0xFF;
        Assert.True(red > 150, $"red channel should dominate ({hex})");
        Assert.True(green < 90 && blue < 90, $"green/blue should stay low ({hex})");
    }

    [Fact]
    public void Extract_WithoutArtworkReturnsNullSoTheFallbackRuleStays()
    {
        Assert.Null(ArtworkAccent.Extract(null));
        Assert.Null(ArtworkAccent.Extract(Array.Empty<byte>()));
        Assert.Null(ArtworkAccent.Extract([1, 2, 3, 4]));
    }

    [Fact]
    public void For_IsStablePerArtworkKeyAndFollowsTheTrack()
    {
        ArtworkAccent.ClearCache();
        var red = SolidPng(220, 30, 40);
        var blue = SolidPng(30, 60, 220);

        var first = ArtworkAccent.For("track-a", red);
        var second = ArtworkAccent.For("track-a", red);
        var other = ArtworkAccent.For("track-b", blue);

        Assert.Equal(first, second);
        Assert.NotEqual(first, other);
    }

    [Fact]
    public void Karaoke_render_paints_more_highlight_as_the_sweep_advances()
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);

        using var none = renderer.RenderToBitmap(LyricPill(highlight: -1), IslandTestFixtures.Scale);
        using var early = renderer.RenderToBitmap(LyricPill(highlight: 0.15), IslandTestFixtures.Scale);
        using var late = renderer.RenderToBitmap(LyricPill(highlight: 0.85), IslandTestFixtures.Scale);

        Assert.NotEqual(Hash(none), Hash(early));
        Assert.NotEqual(Hash(early), Hash(late));
    }

    [Fact]
    public void Pressed_transport_shrinks_the_play_button()
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);

        var idle = IslandTestFixtures.Expanded(1);
        var pressed = idle with
        {
            PressedControl = IslandPageControl.PlayPause,
            PressedPage = 1,
        };

        using var a = renderer.RenderToBitmap(idle, IslandTestFixtures.Scale);
        using var b = renderer.RenderToBitmap(pressed, IslandTestFixtures.Scale);

        Assert.NotEqual(Hash(a), Hash(b));
    }

    [Fact]
    public void Hovering_a_transport_button_changes_its_pixels()
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var idle = IslandTestFixtures.Expanded(1);
        // Hover is resolved by the controller from the click hit test and carried in the render state.
        var hovered = idle with
        {
            HasPointer = true,
            HoverControl = IslandPageControl.Previous,
            HoverPage = 1,
        };

        using var a = renderer.RenderToBitmap(idle, IslandTestFixtures.Scale);
        using var b = renderer.RenderToBitmap(hovered, IslandTestFixtures.Scale);

        Assert.NotEqual(Hash(a), Hash(b));
    }

    [Fact]
    public void Unavailable_output_and_queue_are_greyed_out()
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var state = IslandTestFixtures.Expanded(1);
        var media = state.Pages.Media;
        var disabled = state with
        {
            Pages = state.Pages with { Media = media with { OutputAvailable = false, QueueAvailable = false } },
        };
        var enabled = state with
        {
            Pages = state.Pages with
            {
                Media = media with
                {
                    OutputAvailable = true,
                    OutputName = "Speakers",
                    QueueAvailable = true,
                    QueueCount = 12,
                },
            },
        };

        using var a = renderer.RenderToBitmap(disabled, IslandTestFixtures.Scale);
        using var b = renderer.RenderToBitmap(enabled, IslandTestFixtures.Scale);

        Assert.NotEqual(Hash(a), Hash(b));
    }

    [Fact]
    public void Play_icon_flips_between_playing_and_paused_in_the_glyph_box()
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var state = IslandTestFixtures.Expanded(1);
        var media = state.Pages.Media;
        var playing = state with { Pages = state.Pages with { Media = media with { IsPlaying = true } } };
        var paused = state with { Pages = state.Pages with { Media = media with { IsPlaying = false } } };

        using var a = renderer.RenderToBitmap(playing, IslandTestFixtures.Scale);
        using var b = renderer.RenderToBitmap(paused, IslandTestFixtures.Scale);

        // Only the glyph box: the pause bars and the play triangle must differ *there*, which is what the
        // real-machine acceptance shot has to show when a tap toggles the transport.
        var box = PlayButtonGlyphBox(IslandTestFixtures.Scale);
        Assert.True(GlyphPixels(a, box) != GlyphPixels(b, box), "the play/pause glyph must change with IsPlaying");
        // The circle is solid white now, so the glyph itself is the dark ink inside it.
        Assert.True(GlyphDarkInk(a, box) > 0, "the playing state must paint a glyph");
        Assert.True(GlyphDarkInk(b, box) > 0, "the paused state must paint a glyph");
    }

    private static SKRectI PlayButtonGlyphBox(float scale)
    {
        // Derived from the layout token, not a copied coordinate: the footer was re-laid-out once already
        // (play moved from page+185 to the 36 DIP centred circle) and a hard-coded box goes stale silently.
        var page = IslandMetrics.PageRect(IslandTestFixtures.Expanded(1), 1);
        var play = PageVisual.MediaPlay(page);
        var inset = play.Width * 0.35f;
        return new SKRectI(
            (int)((play.Left + inset) * scale),
            (int)((play.Top + inset) * scale),
            (int)((play.Right - inset) * scale),
            (int)((play.Bottom - inset) * scale));
    }

    private static ulong GlyphPixels(SKBitmap bitmap, SKRectI box)
    {
        ulong hash = 1469598103934665603UL;
        for (var y = box.Top; y < box.Bottom && y < bitmap.Height; y++)
        {
            for (var x = box.Left; x < box.Right && x < bitmap.Width; x++)
            {
                hash = (hash ^ (uint)bitmap.GetPixel(x, y)) * 1099511628211UL;
            }
        }

        return hash;
    }

    /// <summary>Dark (glyph) pixels inside the solid white play circle.</summary>
    private static int GlyphDarkInk(SKBitmap bitmap, SKRectI box)
    {
        var ink = 0;
        for (var y = box.Top; y < box.Bottom && y < bitmap.Height; y++)
        {
            for (var x = box.Left; x < box.Right && x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.Red + pixel.Green + pixel.Blue < 240)
                {
                    ink++;
                }
            }
        }

        return ink;
    }

    private static int GlyphInk(SKBitmap bitmap, SKRectI box)
    {
        var ink = 0;
        for (var y = box.Top; y < box.Bottom && y < bitmap.Height; y++)
        {
            for (var x = box.Left; x < box.Right && x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.Red + pixel.Green + pixel.Blue > 300)
                {
                    ink++;
                }
            }
        }

        return ink;
    }

    private static IslandRenderState LyricPill(double highlight) => IslandTestFixtures.Compact(
        new CompactActivity(ActivityKind.Media, new CompactModel
        {
            IsActive = true,
            ProgressPercent = 40,
            ShowLyrics = true,
            LyricLine = "I miss you a little all the time",
            LyricHighlight = highlight,
            AccentHex = "#DC1E28",
        }));

    private static ulong Hash(SKBitmap bitmap)
    {
        ulong hash = 1469598103934665603UL;
        for (var y = 0; y < bitmap.Height; y += 2)
        {
            for (var x = 0; x < bitmap.Width; x += 2)
            {
                hash = (hash ^ (uint)bitmap.GetPixel(x, y)) * 1099511628211UL;
            }
        }

        return hash;
    }
}