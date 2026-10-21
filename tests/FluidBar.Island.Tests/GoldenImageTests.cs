using FluidBar.Core.Layout;
using FluidBar.Island;
using SkiaSharp;

namespace FluidBar.Island.Tests;

public sealed class GoldenImageTests
{
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
    public void Golden_surface_matches_the_reference(string name)
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme, null, new IslandRendererOptions { SplitRingAngleDegrees = 0 });
        var state = IslandTestFixtures.GoldenState(name);
        using var bitmap = renderer.RenderToBitmap(state, IslandTestFixtures.Scale);

        IslandTestFixtures.SaveReportPng(bitmap, name + ".png");
        using var golden = IslandTestFixtures.TryLoadGolden(name + ".png");
        if (golden is null)
        {
            IslandTestFixtures.SaveGoldenPng(bitmap, name + ".png");
            return;
        }

        Assert.Equal(golden.Width, bitmap.Width);
        Assert.Equal(golden.Height, bitmap.Height);
        var difference = MeanChannelDifference(bitmap, golden);
        Assert.True(difference < 12.0, $"{name}: mean channel difference {difference:0.00} exceeds the golden tolerance");
    }

    [Fact]
    public void Rendering_is_deterministic()
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme, null, new IslandRendererOptions { SplitRingAngleDegrees = 0 });
        var state = IslandTestFixtures.Expanded(1);

        using var first = renderer.RenderToBitmap(state, IslandTestFixtures.Scale);
        using var second = renderer.RenderToBitmap(state, IslandTestFixtures.Scale);
        Assert.Equal(Encode(first), Encode(second));
    }

    private static double MeanChannelDifference(SKBitmap a, SKBitmap b)
    {
        var pa = a.Pixels;
        var pb = b.Pixels;
        long total = 0;
        for (var i = 0; i < pa.Length; i++)
        {
            total += Math.Abs(pa[i].Alpha - pb[i].Alpha);
            total += Math.Abs(pa[i].Red - pb[i].Red);
            total += Math.Abs(pa[i].Green - pb[i].Green);
            total += Math.Abs(pa[i].Blue - pb[i].Blue);
        }

        return total / (double)(pa.Length * 4);
    }

    private static byte[] Encode(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
