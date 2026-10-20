using FluidBar.App.Hosting.Pages;
using FluidBar.Sdk.Canvas;

namespace FluidBar.Ui.Tests;

/// <summary>M3 hit-area semantic mapping at the SDK/Core boundary.</summary>
public sealed class CanvasHitAreaTests
{
    [Fact]
    public void HitAreaFlagsAndSliderRangeSurviveMapping()
    {
        var host = IslandCanvasAdapter.ToHost(new PageCanvasData
        {
            Commands = [],
            HitAreas =
            [
                new PageHitArea
                {
                    Id = "ring", Kind = PageHitAreaKind.Button, X = 1, Y = 2, Width = 30, Height = 40,
                    CommandId = "sample.hit", AccessibleName = "Ring", LongPress = true, DoubleClick = true,
                },
                new PageHitArea
                {
                    Id = "slider", Kind = PageHitAreaKind.Slider, X = 0, Y = 50, Width = 100, Height = 8,
                    CommandId = "sample.slide", AccessibleName = "Value", Min = 10, Max = 90, Value = 50,
                },
            ],
        });

        Assert.Equal(2, host.HitAreas.Count);
        Assert.True(host.HitAreas[0].LongPress);
        Assert.True(host.HitAreas[0].DoubleClick);
        Assert.Equal("Ring", host.HitAreas[0].AccessibleName);
        Assert.Equal(10, host.HitAreas[1].Min);
        Assert.Equal(90, host.HitAreas[1].Max);
        Assert.Equal(50, host.HitAreas[1].Value);
    }
}

