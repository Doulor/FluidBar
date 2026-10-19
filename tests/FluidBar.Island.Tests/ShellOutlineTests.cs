using FluidBar.Island;
using SkiaSharp;

namespace FluidBar.Island.Tests;

/// <summary>
/// Regression coverage for the rejected accent rim: prototype2 declares <c>.island { border: 0 }</c>, so
/// the shell has no outline at all — the edge band must read as the near-neutral fill (never the user
/// accent) and the baked shadow must stay neutral. The tests sample the actual edge band of live
/// renders and of the stored goldens, one pixel inside the silhouette (it is anti-aliased, no rim).
/// </summary>
public sealed class ShellOutlineTests
{
    private const int NeutralSpread = 12;
    private const double MinimumAccentDistance = 40;

    public static TheoryData<string> Cases
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var name in IslandTestFixtures.GoldenNames)
            {
                data.Add(name);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Live_render_shell_edge_is_a_neutral_rimless_fill(string name)
    {
        var state = IslandTestFixtures.GoldenState(name);
        using var renderer = new IslandRenderer(state.Theme ?? IslandTestFixtures.Theme);
        using var bitmap = renderer.RenderToBitmap(state, IslandTestFixtures.Scale);

        var stats = SampleEdge(bitmap, IslandMetrics.ShellRect(state), IslandTestFixtures.Scale, state.Theme.Accent);
        Assert.True(stats.Count >= 12, $"{name}: expected shell-edge samples, found {stats.Count}");
        Assert.True(
            stats.MaxSpread <= NeutralSpread,
            $"{name}: shell edge is tinted (channel spread {stats.MaxSpread}); expected a neutral hairline");
        Assert.True(
            stats.MinAccentDistance > MinimumAccentDistance,
            $"{name}: shell edge is too close to the accent colour (distance {stats.MinAccentDistance:0.0})");
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Stored_golden_shell_edge_is_a_neutral_rimless_fill(string name)
    {
        using var golden = IslandTestFixtures.TryLoadGolden(name + ".png");
        if (golden is null)
        {
            return;
        }

        var state = IslandTestFixtures.GoldenState(name);
        var stats = SampleEdge(golden, IslandMetrics.ShellRect(state), IslandTestFixtures.Scale, state.Theme.Accent);
        Assert.True(stats.Count >= 12, $"{name}: expected shell-edge samples in the golden, found {stats.Count}");
        Assert.True(
            stats.MaxSpread <= NeutralSpread,
            $"{name}: stored golden has a tinted shell edge (channel spread {stats.MaxSpread}); regenerate it");
        Assert.True(
            stats.MinAccentDistance > MinimumAccentDistance,
            $"{name}: stored golden shell edge is too close to the accent colour (distance {stats.MinAccentDistance:0.0})");
    }

    [Fact]
    public void Dark_theme_hairline_and_shadow_tokens_are_neutral()
    {
        var theme = IslandTheme.Dark;
        Assert.True(Spread(theme.LineStrong) <= 2, $"LineStrong must be neutral, got {theme.LineStrong}");
        Assert.True(Spread(theme.Background) <= 4, $"Background must be neutral, got {theme.Background}");
        Assert.Equal(SKColors.Black, theme.ShadowColor);
        Assert.True(
            Distance(theme.LineStrong, theme.Accent) > MinimumAccentDistance,
            "the shell hairline token must not be the accent colour");
    }

    private static int Spread(SKColor color)
    {
        var max = Math.Max(color.Red, Math.Max(color.Green, color.Blue));
        var min = Math.Min(color.Red, Math.Min(color.Green, color.Blue));
        return max - min;
    }

    private static double Distance(SKColor a, SKColor b)
    {
        var dr = a.Red - b.Red;
        var dg = a.Green - b.Green;
        var db = a.Blue - b.Blue;
        return Math.Sqrt((dr * dr) + (dg * dg) + (db * db));
    }

    private static EdgeStats SampleEdge(SKBitmap bitmap, SKRect shell, float scale, SKColor accent)
    {
        var count = 0;
        var maxSpread = 0;
        var minDistance = double.MaxValue;

        var left = (int)Math.Round(shell.Left * scale);
        var top = (int)Math.Round(shell.Top * scale);
        var right = (int)Math.Round(shell.Right * scale);
        var bottom = (int)Math.Round(shell.Bottom * scale);
        var verticalInset = (int)Math.Round(Math.Min(24f, shell.Height * 0.25f) * scale);
        var horizontalInset = (int)Math.Round(Math.Min(24f, shell.Width * 0.25f) * scale);

        // Sample just inside the silhouette: prototype2 has no border, so the outermost pixel is the
        // anti-aliased fill edge and carries less alpha than the band one pixel in.
        for (var y = top + verticalInset; y <= bottom - verticalInset; y += 2)
        {
            for (var dx = 2; dx <= 4; dx++)
            {
                Accumulate(bitmap.GetPixel(left + dx, y));
            }
        }

        for (var x = left + horizontalInset; x <= right - horizontalInset; x += 2)
        {
            for (var dy = 2; dy <= 4; dy++)
            {
                Accumulate(bitmap.GetPixel(x, top + dy));
            }
        }

        return new EdgeStats(count, maxSpread, minDistance == double.MaxValue ? 0 : minDistance);

        void Accumulate(SKColor color)
        {
            if (color.Alpha < 120)
            {
                return;
            }

            count++;
            var max = Math.Max(color.Red, Math.Max(color.Green, color.Blue));
            var min = Math.Min(color.Red, Math.Min(color.Green, color.Blue));
            maxSpread = Math.Max(maxSpread, max - min);

            var dr = color.Red - accent.Red;
            var dg = color.Green - accent.Green;
            var db = color.Blue - accent.Blue;
            minDistance = Math.Min(minDistance, Math.Sqrt((dr * dr) + (dg * dg) + (db * db)));
        }
    }

    private readonly record struct EdgeStats(int Count, int MaxSpread, double MinAccentDistance);
}
