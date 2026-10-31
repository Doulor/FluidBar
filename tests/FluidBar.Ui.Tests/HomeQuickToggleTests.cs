using FluidBar.App.Hosting.Skia;
using FluidBar.App.Localization;
using FluidBar.App.Settings;
using FluidBar.App.State;
using FluidBar.Core.Activities;
using FluidBar.Core.Presentation;
using FluidBar.Island;
using Xunit;

namespace FluidBar.Ui.Tests;

/// <summary>
/// The home page shows three quick tiles: Wi-Fi, Bluetooth and the Windows dark/light switch.
/// </summary>
public class HomeQuickToggleTests
{
    [Fact]
    public Task Home_page_has_the_three_requested_tiles_in_order() => AvaloniaTest.Run(() =>
    {
        var localizer = TestFixtures.CreateLocalizer("zh-CN");
        var pages = SkiaIslandStateBuilder.BuildPages(
            new IslandContent { NetworkName = "WLAN", IsWifiEnabled = true, IsBluetoothEnabled = false },
            EmptyIsland(),
            localizer);

        Assert.Equal(3, pages.Home.Toggles.Count);
        Assert.Equal(IconKind.Wifi, pages.Home.Toggles[0].Icon);
        Assert.Equal(IconKind.Bluetooth, pages.Home.Toggles[1].Icon);
        Assert.Equal(IconKind.Moon, pages.Home.Toggles[2].Icon);
        Assert.True(pages.Home.Toggles[0].IsOn);
        Assert.False(pages.Home.Toggles[1].IsOn);
        Assert.All(pages.Home.Toggles, toggle => Assert.True(toggle.IsEnabled));
    });

    [Fact]
    public Task Theme_tile_follows_Windows_independently_of_the_app_setting() => AvaloniaTest.Run(() =>
    {
        var localizer = TestFixtures.CreateLocalizer("zh-CN");
        var content = new IslandContent();
        var island = EmptyIsland();

        var dark = SkiaIslandStateBuilder.BuildPages(content, island, localizer,
            new AppSettings { Theme = AppTheme.Light }, systemIsLight: false);
        var light = SkiaIslandStateBuilder.BuildPages(content, island, localizer,
            new AppSettings { Theme = AppTheme.Dark }, systemIsLight: true);

        Assert.Equal(IconKind.Moon, dark.Home.Toggles[2].Icon);
        Assert.False(dark.Home.Toggles[2].IsOn);
        Assert.Equal("深色", dark.Home.Toggles[2].Label);
        Assert.Equal(IconKind.Sun, light.Home.Toggles[2].Icon);
        Assert.True(light.Home.Toggles[2].IsOn);
        Assert.Equal("浅色", light.Home.Toggles[2].Label);
    });

    private static IslandViewModel EmptyIsland() => new()
    {
        StateKind = IslandStateKind.Compact,
        Page = PageRef.Home,
    };
}
