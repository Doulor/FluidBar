using FluidBar.Island;
using SkiaSharp;

namespace FluidBar.Island.Tests;

/// <summary>
/// Multi-island stacking: the compact shell paints up to two ghost pills (LayoutTokens.Ghost*) above
/// the primary pill when the scheduler reports a ghost stack, and paints none for "latest only".
/// </summary>
public sealed class GhostStackTests
{
    [Fact]
    public void Stacked_strategy_paints_ghost_pills_above_the_primary_shell()
    {
        // Disable the baked shadow: its soft ink also reaches above the shell and would mask the ghosts.
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme, null,
            new IslandRendererOptions { EnableShadow = false });
        var baseState = IslandTestFixtures.Clipboard();
        var shell = IslandMetrics.ShellRect(baseState);

        using var withoutGhosts = renderer.RenderToBitmap(baseState with { GhostCount = 0 },
            IslandTestFixtures.Scale);
        using var twoGhosts = renderer.RenderToBitmap(baseState with { GhostCount = 2 },
            IslandTestFixtures.Scale);
        using var oneGhost = renderer.RenderToBitmap(baseState with { GhostCount = 1 },
            IslandTestFixtures.Scale);

        var none = CountInkAboveShell(withoutGhosts, shell);
        var one = CountInkAboveShell(oneGhost, shell);
        var two = CountInkAboveShell(twoGhosts, shell);

        Assert.Equal(0, none);
        Assert.True(one > 200, "one ghost layer did not paint: " + one);
        Assert.True(two > one, "the second ghost layer did not add ink: " + two);
    }

    [Fact]
    public void Latest_only_strategy_paints_no_ghost_layers()
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme, null,
            new IslandRendererOptions { EnableShadow = false });
        var state = IslandTestFixtures.MediaCompact() with { GhostCount = 0 };
        var shell = IslandMetrics.ShellRect(state);
        using var bitmap = renderer.RenderToBitmap(state, IslandTestFixtures.Scale);
        Assert.Equal(0, CountInkAboveShell(bitmap, shell));
    }

    private static int CountInkAboveShell(SKBitmap bitmap, SKRect shellDip)
    {
        var scale = IslandTestFixtures.Scale;
        var shellTop = Math.Max(0, (int)Math.Floor(shellDip.Top * scale));
        var left = Math.Max(0, (int)Math.Floor((shellDip.Left - 20) * scale));
        var right = Math.Min(bitmap.Width, (int)Math.Ceiling((shellDip.Right + 20) * scale));
        var pixels = bitmap.Pixels;
        var count = 0;
        for (var y = 0; y < shellTop; y++)
        {
            var row = y * bitmap.Width;
            for (var x = left; x < right; x++)
            {
                var pixel = pixels[row + x];
                if (pixel.Alpha > 40)
                {
                    count++;
                }
            }
        }

        return count;
    }
}
