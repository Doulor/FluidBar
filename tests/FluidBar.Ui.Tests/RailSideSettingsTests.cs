using FluidBar.App.Settings;
using FluidBar.Core.Layout;

namespace FluidBar.Ui.Tests;

/// <summary>Settings contract for the in-card page rail side.</summary>
public sealed class RailSideSettingsTests
{
    [Fact]
    public void Defaults_are_left_and_side_rail()
    {
        Assert.Equal(PageRailSide.Left, AppSettings.Default.RailSide);
        Assert.Equal(TabBarStyle.SideRail, AppSettings.Default.TabBarStyle);
    }

    [Fact]
    public void Json_round_trips_the_right_side()
    {
        var json = (AppSettings.Default with { RailSide = PageRailSide.Right }).ToJson();
        var parsed = AppSettings.TryParse(json, out var error);

        Assert.Null(error);
        Assert.NotNull(parsed);
        Assert.Equal(PageRailSide.Right, parsed!.RailSide);
        Assert.Equal(TabBarStyle.SideRail, parsed.TabBarStyle);
    }

    [Fact]
    public void Json_round_trips_the_bottom_preview_style()
    {
        var json = (AppSettings.Default with { TabBarStyle = TabBarStyle.BottomPreview }).ToJson();
        var parsed = AppSettings.TryParse(json, out var error);

        Assert.Null(error);
        Assert.NotNull(parsed);
        Assert.Equal(TabBarStyle.BottomPreview, parsed!.TabBarStyle);
    }
}
