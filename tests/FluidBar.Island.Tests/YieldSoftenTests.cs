using FluidBar.Core.Layout;
using FluidBar.Island;
using SkiaSharp;

namespace FluidBar.Island.Tests;

/// <summary>
/// "Soften" yield animation (chosen from the web prototype): blur + desaturation + fade applied to the
/// composited scene, so all three materials behave identically.
/// </summary>
public sealed class YieldSoftenTests
{
    private static IslandTheme ThemedFor(IslandMaterial material)
    {
        var theme = IslandTestFixtures.Theme;
        return material switch
        {
            IslandMaterial.Obsidian => theme with { Material = IslandMaterial.Obsidian },
            IslandMaterial.SimulatedGlass => theme with { Material = IslandMaterial.SimulatedGlass },
            IslandMaterial.LiquidGlass => theme with { Material = IslandMaterial.LiquidGlass },
            _ => theme,
        };
    }

    [Theory]
    [InlineData(IslandMaterial.Obsidian)]
    [InlineData(IslandMaterial.SimulatedGlass)]
    [InlineData(IslandMaterial.LiquidGlass)]
    public void Yield_fades_every_material_to_about_a_quarter(IslandMaterial material)
    {
        var theme = ThemedFor(material);
        var state = IslandTestFixtures.MediaCompact() with { Theme = theme };
        using var renderer = new IslandRenderer(theme);

        using var sharp = renderer.RenderToBitmap(state, IslandTestFixtures.Scale);
        using var yielded = renderer.RenderToBitmap(state with { YieldProgress = 1 }, IslandTestFixtures.Scale);

        var shell = IslandMetrics.ShellRect(state);
        var x = (int)Math.Round(shell.MidX * IslandTestFixtures.Scale);
        var y = (int)Math.Round(shell.MidY * IslandTestFixtures.Scale);
        var sharpAlpha = sharp.GetPixel(x, y).Alpha;
        var yieldedAlpha = yielded.GetPixel(x, y).Alpha;

        Assert.True(sharpAlpha > 120, $"{material}: the interactive island must be visible, got {sharpAlpha}");
        Assert.InRange(yieldedAlpha, sharpAlpha * 0.15, sharpAlpha * 0.45);
    }

    [Theory]
    [InlineData(IslandMaterial.Obsidian)]
    [InlineData(IslandMaterial.SimulatedGlass)]
    [InlineData(IslandMaterial.LiquidGlass)]
    public void Yield_softens_the_edges_of_every_material(IslandMaterial material)
    {
        var theme = ThemedFor(material);
        var state = IslandTestFixtures.MediaCompact() with { Theme = theme };
        using var renderer = new IslandRenderer(theme);

        using var sharp = renderer.RenderToBitmap(state, IslandTestFixtures.Scale);
        using var yielded = renderer.RenderToBitmap(state with { YieldProgress = 1 }, IslandTestFixtures.Scale);

        var shell = IslandMetrics.ShellRect(state);
        var y = (int)Math.Round(shell.MidY * IslandTestFixtures.Scale);
        var sharpStep = MaxStep(sharp, y, (int)(shell.Left * IslandTestFixtures.Scale) - 10, 26);
        var yieldedStep = MaxStep(yielded, y, (int)(shell.Left * IslandTestFixtures.Scale) - 10, 26);

        Assert.True(sharpStep > 12, $"{material}: expected a crisp rim, got step {sharpStep:0.0}");
        Assert.True(
            yieldedStep < sharpStep * 0.75,
            $"{material}: the yielded island must be softer ({yieldedStep:0.0} vs {sharpStep:0.0})");
    }

    private static double MaxStep(SKBitmap bitmap, int y, int startX, int count)
    {
        var previous = double.NaN;
        var max = 0d;
        for (var i = 0; i < count; i++)
        {
            var pixel = bitmap.GetPixel(startX + i, y);
            var luma = (0.2126 * pixel.Red) + (0.7152 * pixel.Green) + (0.0722 * pixel.Blue);
            if (!double.IsNaN(previous))
            {
                max = Math.Max(max, Math.Abs(luma - previous));
            }

            previous = luma;
        }

        return max;
    }
}
