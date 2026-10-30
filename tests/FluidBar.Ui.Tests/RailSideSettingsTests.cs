using FluidBar.App.Settings;
using FluidBar.Core.Layout;

namespace FluidBar.Ui.Tests;

/// <summary>Settings contract for the in-card page rail side.</summary>
public sealed class RailSideSettingsTests
{
    [Fact]
    public void Default_is_left()
    {
        Assert.Equal(PageRailSide.Left, AppSettings.Default.RailSide);
    }

    [Fact]
    public void Json_round_trips_the_right_side()
    {
        var json = (AppSettings.Default with { RailSide = PageRailSide.Right }).ToJson();
        var parsed = AppSettings.TryParse(json, out var error);

        Assert.Null(error);
        Assert.NotNull(parsed);
        Assert.Equal(PageRailSide.Right, parsed!.RailSide);
    }
}
