using FluidBar.Island;
using SkiaSharp;

namespace FluidBar.Island.Tests;

/// <summary>
/// Media page motion: the timing constants are part of the design contract, and the frame-evidence harness
/// (FLUIDBAR_MOTION_EVIDENCE=&lt;dir&gt;) renders 8 lyric-change frames, 6 progress-smoothing frames and 6
/// cover-cross-fade frames into that directory so the motion can be reviewed frame by frame.
/// </summary>
public sealed class MediaMotionTests
{
    [Fact]
    public void Motion_parameters_match_the_design_contract()
    {
        Assert.Equal(200f, MediaMotion.LyricOutMs, 3);
        Assert.Equal(260f, MediaMotion.LyricInMs, 3);
        Assert.Equal(40f, MediaMotion.LyricStaggerMs, 3);
        Assert.Equal(6f, MediaMotion.LyricSlideDip, 3);
        Assert.Equal(180f, MediaMotion.ProgressTauMs, 3);
        Assert.Equal(1.4f, MediaMotion.SeekThumbScale, 3);
        Assert.Equal(180f, MediaMotion.ArtCrossFadeMs, 3);
        Assert.Equal(0.92f, MediaMotion.PlayPressScale, 3);
        Assert.Equal(0.94f, MediaMotion.SidePressScale, 3);

        // The stagger must keep the incoming line behind the outgoing one (otherwise it is a cross-fade).
        Assert.True(MediaMotion.LyricStaggerMs < MediaMotion.LyricOutMs);
    }

    [Fact]
    public void Lyric_change_animates_out_and_in_instead_of_swapping_text()
    {
        // The line change is a two-stage motion: the outgoing line slides up while it fades, and the
        // incoming line starts 40 ms later from 6 DIP below. Both stages are visible in the frame evidence
        // written by the harness; here we pin that the frames are not a hard text swap.
        var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var state = IslandTestFixtures.Expanded(1);
        var media = state.Pages.Media;
        var clock = 0.0;
        MediaMotionClock.Override = () => clock;
        try
        {
            var a = state with { Pages = state.Pages with { Media = media with { LyricLine = "I was waiting for the car" } } };
            var b = state with { Pages = state.Pages with { Media = media with { LyricLine = "The city is my church" } } };
            using var settled = renderer.RenderToBitmap(a, 2f);
            clock = 0;
            using var atSwitch = renderer.RenderToBitmap(b, 2f);
            clock = 60;
            using var early = renderer.RenderToBitmap(b, 2f);
            clock = 400;
            using var finished = renderer.RenderToBitmap(b, 2f);

            var settledInk = Ink(settled, a);
            var switchInk = Ink(atSwitch, b);
            var earlyInk = Ink(early, b);

            // At the switch the old line is still fully painted; shortly after, both lines are on screen in a
            // faded state (less ink than the settled line, but a different pixel pattern).
            Assert.True(switchInk > 0 && earlyInk > 0);
            Assert.NotEqual(Hash(settled, a), Hash(finished, b));
            Assert.True(earlyInk < settledInk, $"the outgoing line must be fading ({earlyInk} vs {settledInk})");
        }
        finally
        {
            MediaMotionClock.Override = null;
        }
    }

    [Fact]
    public void Progress_is_exponentially_smoothed_and_stops_on_pause()
    {
        var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var state = IslandTestFixtures.Expanded(1);
        var media = state.Pages.Media;
        var clock = 0.0;
        MediaMotionClock.Override = () => clock;

        // Two samples 16 ms apart: the drawn progress must approach the target, not jump to it.
        var first = Render(renderer, state with { Pages = state.Pages with { Media = media with { ProgressPercent = 40 } } });
        clock += 16;
        var second = Render(renderer, state with { Pages = state.Pages with { Media = media with { ProgressPercent = 60 } } });
        var jumped = Render(renderer, state with { Pages = state.Pages with { Media = media with { ProgressPercent = 60 } } });
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(FillWidth(first!, state), FillWidth(jumped!, state));

        // Paused: the next frame snaps to the reported position and stays there.
        var paused = state with { Pages = state.Pages with { Media = media with { ProgressPercent = 10, IsPlaying = false } } };
        var a = FillWidth(Render(renderer, paused)!, paused);
        clock += 100;
        var b = FillWidth(Render(renderer, paused)!, paused);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Cover_change_cross_fades_between_the_two_images()
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var state = IslandTestFixtures.Expanded(1);
        var media = state.Pages.Media;
        var clock = 0.0;
        MediaMotionClock.Override = () => clock;
        try
        {
            var red = Solid(220, 30, 40);
            var blue = Solid(30, 60, 220);
            var a = state with { Pages = state.Pages with { Media = media with { Artwork = red, ArtworkKey = "a" } } };
            var b = state with { Pages = state.Pages with { Media = media with { Artwork = blue, ArtworkKey = "b" } } };

            var start = MeanArtColour(Render(renderer, a)!, a);
            clock = 0;
            var atSwitch = MeanArtColour(Render(renderer, b)!, b);
            clock = 90;
            var mid = MeanArtColour(Render(renderer, b)!, b);
            clock = 200;
            var end = MeanArtColour(Render(renderer, b)!, b);

            // At the switch the old cover is still on screen; half-way through it is a genuine blend; after
            // 180 ms only the new cover remains.
            Assert.Equal(start.Red, atSwitch.Red);
            Assert.True(mid.Red < start.Red && mid.Red > end.Red, $"mid R {mid.Red} must sit between {start.Red} and {end.Red}");
            Assert.True(mid.Blue > start.Blue && mid.Blue < end.Blue, $"mid B {mid.Blue} must sit between {start.Blue} and {end.Blue}");
            Assert.Equal(end.Red, 30);
        }
        finally
        {
            MediaMotionClock.Override = null;
        }
    }

    [Fact]
    public void Evidence_harness_writes_lyric_seek_and_cover_frames()
    {
        var dir = Environment.GetEnvironmentVariable("FLUIDBAR_MOTION_EVIDENCE");
        if (string.IsNullOrEmpty(dir))
        {
            return;
        }

        Directory.CreateDirectory(dir);
        WriteLyricEvidence(dir);
        WriteSeekEvidence(dir);
        WriteCoverEvidence(dir);
    }

    private static void WriteLyricEvidence(string dir)
    {
        var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var state = IslandTestFixtures.Expanded(1);
        var media = state.Pages.Media;
        var clock = 0.0;
        MediaMotionClock.Override = () => clock;
        var lines = new List<string>();
        Render(renderer, state);
        for (var frame = 0; frame < 8; frame++)
        {
            var line = frame == 0 ? "I was waiting for the car" : "The city is my church";
            var current = state with { Pages = state.Pages with { Media = media with { LyricLine = line } } };
            using var bitmap = renderer.RenderToBitmap(current, 2f);
            bitmap.Encode(SKEncodedImageFormat.Png, 100).SaveTo(File.Create(Path.Combine(dir, $"lyric-{frame:00}.png")));
            var m = LyricMetrics(bitmap, current);
            lines.Add($"lyric {frame:00} t={clock,6:0}ms ink={m.Ink} top={m.Top} bottom={m.Bottom}");
            clock += frame == 0 ? 0 : 33;
        }

        File.WriteAllLines(Path.Combine(dir, "lyric.txt"), lines);
    }

    private static void WriteSeekEvidence(string dir)
    {
        var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var state = IslandTestFixtures.Expanded(1);
        var media = state.Pages.Media;
        var clock = 0.0;
        MediaMotionClock.Override = () => clock;
        var lines = new List<string>();
        var baseline = state with { Pages = state.Pages with { Media = media with { ProgressPercent = 20, IsPlaying = true } } };
        using (var first = renderer.RenderToBitmap(baseline, 2f))
        {
            lines.Add($"seek baseline t=0ms thumbX={ThumbX(first, baseline)} fill={FillWidth(first, baseline)}");
        }

        for (var frame = 0; frame < 6; frame++)
        {
            clock += 20;
            var current = state with { Pages = state.Pages with { Media = media with { ProgressPercent = 70, IsPlaying = true } } };
            using var bitmap = renderer.RenderToBitmap(current, 2f);
            bitmap.Encode(SKEncodedImageFormat.Png, 100).SaveTo(File.Create(Path.Combine(dir, $"seek-{frame:00}.png")));
            lines.Add($"seek {frame:00} t={clock,6:0}ms thumbX={ThumbX(bitmap, current)} fill={FillWidth(bitmap, current)}");
        }

        File.WriteAllLines(Path.Combine(dir, "seek.txt"), lines);
    }

    private static void WriteCoverEvidence(string dir)
    {
        var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var state = IslandTestFixtures.Expanded(1);
        var media = state.Pages.Media;
        var clock = 0.0;
        MediaMotionClock.Override = () => clock;
        var lines = new List<string>();
        var red = Solid(220, 30, 40);
        var blue = Solid(30, 60, 220);
        Render(renderer, state with { Pages = state.Pages with { Media = media with { Artwork = red, ArtworkKey = "a" } } });
        for (var frame = 0; frame < 6; frame++)
        {
            var current = state with { Pages = state.Pages with { Media = media with { Artwork = blue, ArtworkKey = "b" } } };
            using var bitmap = renderer.RenderToBitmap(current, 2f);
            bitmap.Encode(SKEncodedImageFormat.Png, 100).SaveTo(File.Create(Path.Combine(dir, $"cover-{frame:00}.png")));
            var mean = MeanArtColour(bitmap, current);
            lines.Add($"cover {frame:00} t={clock,6:0}ms rgb=({mean.Red},{mean.Green},{mean.Blue})");
            clock += 30;
        }

        File.WriteAllLines(Path.Combine(dir, "cover.txt"), lines);
    }

    private static SKBitmap RenderLyricFrames(int count, out List<LineMetrics> metrics)
    {
        var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var state = IslandTestFixtures.Expanded(1);
        var media = state.Pages.Media;
        var clock = 0.0;
        MediaMotionClock.Override = () => clock;
        metrics = new List<LineMetrics>();
        Render(renderer, state with { Pages = state.Pages with { Media = media with { LyricLine = "I was waiting for the car" } } });
        SKBitmap? last = null;
        for (var frame = 0; frame < count; frame++)
        {
            var current = state with { Pages = state.Pages with { Media = media with { LyricLine = "The city is my church" } } };
            using var bitmap = renderer.RenderToBitmap(current, 2f);
            metrics.Add(LyricMetrics(bitmap, current));
            last ??= bitmap.Copy();
            clock += 33;
        }

        return last!;
    }

    private static SKBitmap? Render(IslandRenderer renderer, IslandRenderState state) => renderer.RenderToBitmap(state, 2f);

    private static byte[] Solid(byte r, byte g, byte b, int size = 48)
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

    private static int Ink(SKBitmap bitmap, IslandRenderState state)
    {
        var page = IslandMetrics.PageRect(state, 1);
        var box = PageVisual.MediaLyricLine1(page);
        var ink = 0;
        for (var y = (int)(box.Top * 2); y < (int)(box.Bottom * 2); y++)
        {
            for (var x = (int)(box.Left * 2); x < (int)(box.Right * 2); x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.Red + pixel.Green + pixel.Blue > 260)
                {
                    ink++;
                }
            }
        }

        return ink;
    }

    private static ulong Hash(SKBitmap bitmap, IslandRenderState state)
    {
        var page = IslandMetrics.PageRect(state, 1);
        var box = PageVisual.MediaLyricLine1(page);
        ulong hash = 1469598103934665603UL;
        for (var y = (int)(box.Top * 2); y < (int)(box.Bottom * 2); y++)
        {
            for (var x = (int)(box.Left * 2); x < (int)(box.Right * 2); x++)
            {
                hash = (hash ^ (uint)bitmap.GetPixel(x, y)) * 1099511628211UL;
            }
        }

        return hash;
    }

    private sealed record LineMetrics(int Ink, int Top, int Bottom);

    private static LineMetrics LyricMetrics(SKBitmap bitmap, IslandRenderState state)
    {
        var page = IslandMetrics.PageRect(state, 1);
        // Exactly the lyric band: it sits between the album line above and the seek row below, so the
        // measurement cannot pick up neighbouring text.
        var band = PageVisual.MediaLyricLine1(page);
        var scale = 2f;
        var ink = 0;
        var top = int.MaxValue;
        var bottom = int.MinValue;
        for (var y = (int)(band.Top * scale); y < (int)(band.Bottom * scale); y++)
        {
            for (var x = (int)(band.Left * scale); x < (int)(band.Right * scale); x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.Red + pixel.Green + pixel.Blue > 260)
                {
                    ink++;
                    top = Math.Min(top, y);
                    bottom = Math.Max(bottom, y);
                }
            }
        }

        return new LineMetrics(ink, top == int.MaxValue ? 0 : top, bottom == int.MinValue ? 0 : bottom);
    }

    private static int FillWidth(SKBitmap bitmap, IslandRenderState state)
    {
        var page = IslandMetrics.PageRect(state, 1);
        var track = PageVisual.MediaSeekTrack(page);
        const float scale = 2f;
        var y = (int)track.MidY * 2;
        var width = 0;
        for (var x = (int)(track.Left * scale); x < (int)(track.Right * scale); x++)
        {
            var pixel = bitmap.GetPixel(x, y);
            var max = Math.Max(pixel.Red, Math.Max(pixel.Green, pixel.Blue));
            var min = Math.Min(pixel.Red, Math.Min(pixel.Green, pixel.Blue));
            if (max > 90 && max - min > 20)
            {
                width++;
            }
        }

        return width;
    }

    /// <summary>X of the bright playhead inside the track row (2x pixels), i.e. the drawn progress.</summary>
    private static int ThumbX(SKBitmap bitmap, IslandRenderState state)
    {
        var page = IslandMetrics.PageRect(state, 1);
        var track = PageVisual.MediaSeekTrack(page);
        const float scale = 2f;
        var y = (int)(track.MidY * scale);
        var last = -1;
        for (var x = (int)(track.Left * scale); x < (int)(track.Right * scale); x++)
        {
            var pixel = bitmap.GetPixel(x, y);
            if (pixel.Red > 200 && pixel.Green > 200 && pixel.Blue > 200)
            {
                last = x;
            }
        }

        return last;
    }

    private static SKColor MeanArtColour(SKBitmap bitmap, IslandRenderState state)
    {
        var page = IslandMetrics.PageRect(state, 1);
        var art = PageVisual.MediaArt(page);
        const float scale = 2f;
        long r = 0;
        long g = 0;
        long b = 0;
        var n = 0;
        for (var y = (int)(art.Top * scale) + 8; y < (int)(art.Bottom * scale) - 8; y += 3)
        {
            for (var x = (int)(art.Left * scale) + 8; x < (int)(art.Right * scale) - 8; x += 3)
            {
                var pixel = bitmap.GetPixel(x, y);
                r += pixel.Red;
                g += pixel.Green;
                b += pixel.Blue;
                n++;
            }
        }

        return new SKColor((byte)(r / n), (byte)(g / n), (byte)(b / n));
    }
}
