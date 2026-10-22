using FluidBar.Island;
using SkiaSharp;

namespace FluidBar.Island.Tests;

/// <summary>
/// Lyric line-change timing: the incoming line must be visible on the exact frame its start time is
/// reached (no 40 ms stagger), while the outgoing line fades out in parallel.
/// </summary>
public sealed class MediaLyricTransitionTests
{
    [Fact]
    public void Transition_parameters_match_the_contract()
    {
        Assert.Equal(180f, LyricTransition.OutMs, 3);
        Assert.Equal(180f, LyricTransition.InMs, 3);
        Assert.Equal(0f, LyricTransition.StaggerMs, 3);
        Assert.Equal(0.3f, LyricTransition.StartOpacity, 3);
        Assert.Equal(6f, LyricTransition.SlideDip, 3);
    }

    [Fact]
    public void Incoming_line_is_visible_on_the_switch_frame()
    {
        var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var state = IslandTestFixtures.Expanded(1);
        var media = state.Pages.Media;
        var clock = 0.0;
        LyricTransition.Clock = () => clock;
        try
        {
            var a = state with { Pages = state.Pages with { Media = media with { LyricLine = "I was waiting for the car" } } };
            var b = state with { Pages = state.Pages with { Media = media with { LyricLine = "The city is my church" } } };
            using var settled = renderer.RenderToBitmap(a, 2f);
            clock = 500;
            using var atSwitch = renderer.RenderToBitmap(b, 2f);

            var settledInk = Ink(settled, a);
            var switchInk = Ink(atSwitch, b);
            Assert.True(switchInk > 0, "the switch frame must not be an empty lyric line");
            Assert.True(switchInk > settledInk * 0.2,
                $"switch ink {switchInk} must already be a visible fraction of the settled line {settledInk}");
        }
        finally
        {
            LyricTransition.Clock = null;
        }
    }

    [Fact]
    public void Evidence_harness_writes_boundary_csv_and_frames()
    {
        var dir = Environment.GetEnvironmentVariable("FLUIDBAR_LYRIC_BOUNDARY_EVIDENCE");
        if (string.IsNullOrEmpty(dir))
        {
            return;
        }

        Directory.CreateDirectory(dir);
        WriteBoundary(dir, "before", (200f, 260f, 40f, 0f));
        WriteBoundary(dir, "after", (180f, 180f, 0f, 0.3f));
    }

    private static void WriteBoundary(string dir, string tag,
        (float OutMs, float InMs, float StaggerMs, float StartOpacity) timing)
    {
        var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var state = IslandTestFixtures.Expanded(1);
        var media = state.Pages.Media;
        const double boundary = 2000;
        LyricTransition.Override = timing;
        try
        {
            var a = state with { Pages = state.Pages with { Media = media with { LyricLine = "Line A", NextLyricLine = "Line B" } } };
            var clock = boundary - 160;
            LyricTransition.Clock = () => clock;
            using (renderer.RenderToBitmap(a, 2f))
            {
            }

            var rows = new List<string> { "t_ms,phase,current_text,new_line_opacity,word_index" };
            var frame = 0;
            for (var i = 0; i <= 50; i++)
            {
                var t = boundary - 160 + (i * 8);
                clock = t;
                var isNew = t >= boundary;
                var current = isNew
                    ? state with { Pages = state.Pages with { Media = media with { LyricLine = "Line B" } } }
                    : a;
                using var bitmap = renderer.RenderToBitmap(current, 2f);
                var elapsed = t - boundary;
                var opacity = isNew ? Opacity(elapsed, timing) : 0;
                var word = elapsed < 0 ? -1 : (int)Math.Floor(Math.Max(0, elapsed) / 90);
                rows.Add($"{t:0},{tag},{(isNew ? "Line B" : "Line A")},{opacity:0.000},{word}");
                if (frame < 3 && t >= boundary && t <= boundary + 16)
                {
                    bitmap.Encode(SKEncodedImageFormat.Png, 100)
                        .SaveTo(File.Create(Path.Combine(dir, $"{tag}-frame-{frame}.png")));
                    frame++;
                }
            }

            File.WriteAllLines(Path.Combine(dir, $"{tag}.csv"), rows);
        }
        finally
        {
            LyricTransition.Override = null;
            LyricTransition.Clock = null;
        }
    }

    private static double Opacity(double elapsedMs,
        (float OutMs, float InMs, float StaggerMs, float StartOpacity) timing)
    {
        var t = Math.Clamp((elapsedMs - timing.StaggerMs) / timing.InMs, 0, 1);
        var eased = t * t * (3 - (2 * t));
        return timing.StartOpacity + ((1 - timing.StartOpacity) * eased);
    }

    private static int Ink(SKBitmap bitmap, IslandRenderState state)
    {
        var page = IslandMetrics.PageRect(state, 1);
        var card = PageMetricsLayout.MediaLyricCard(page, FontScaleMetrics.One);
        var ink = 0;
        for (var y = (int)(card.Top * 2); y < (int)(card.Bottom * 2); y++)
        {
            for (var x = (int)(card.Left * 2); x < (int)(card.Right * 2); x++)
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
}
