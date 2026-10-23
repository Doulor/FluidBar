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
/// The home page shows four quick tiles: Wi-Fi, Bluetooth, pomodoro (focus timer) and the dark/light
/// theme switch (user request 2026-10-24). These tests pin the tile order, labels and the pomodoro states.
/// </summary>
public class HomeQuickToggleTests
{
    [Fact]
    public Task Home_page_has_the_four_requested_tiles_in_order() => AvaloniaTest.Run(() =>
    {
        var localizer = TestFixtures.CreateLocalizer("zh-CN");
        var pages = SkiaIslandStateBuilder.BuildPages(
            new IslandContent { NetworkName = "WLAN", IsWifiEnabled = true, IsBluetoothEnabled = false },
            EmptyIsland(),
            localizer);

        Assert.Equal(4, pages.Home.Toggles.Count);
        Assert.Equal(IconKind.Wifi, pages.Home.Toggles[0].Icon);
        Assert.Equal(IconKind.Bluetooth, pages.Home.Toggles[1].Icon);
        Assert.Equal(IconKind.Timer, pages.Home.Toggles[2].Icon);
        Assert.Equal(IconKind.Moon, pages.Home.Toggles[3].Icon);
        Assert.True(pages.Home.Toggles[0].IsOn);
        Assert.False(pages.Home.Toggles[1].IsOn);
        Assert.All(pages.Home.Toggles, toggle => Assert.True(toggle.IsEnabled));
    });

    [Fact]
    public Task Theme_tile_follows_the_live_theme_setting() => AvaloniaTest.Run(() =>
    {
        var localizer = TestFixtures.CreateLocalizer("zh-CN");
        var content = new IslandContent();
        var island = EmptyIsland();

        var dark = SkiaIslandStateBuilder.BuildPages(content, island, localizer,
            new AppSettings { Theme = AppTheme.Dark });
        var light = SkiaIslandStateBuilder.BuildPages(content, island, localizer,
            new AppSettings { Theme = AppTheme.Light });

        Assert.Equal(IconKind.Moon, dark.Home.Toggles[3].Icon);
        Assert.False(dark.Home.Toggles[3].IsOn);
        Assert.Equal(IconKind.Sun, light.Home.Toggles[3].Icon);
        Assert.True(light.Home.Toggles[3].IsOn);
    });

    [Fact]
    public Task Pomodoro_tile_shows_the_countdown_while_running() => AvaloniaTest.Run(() =>
    {
        var localizer = TestFixtures.CreateLocalizer("zh-CN");
        var content = new IslandContent();
        var island = EmptyIsland();

        var timer = new PomodoroTimer();
        var now = new DateTimeOffset(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);

        var idle = SkiaIslandStateBuilder.BuildPages(content, island, localizer, null, timer.Snapshot);
        Assert.False(idle.Home.Toggles[2].IsOn);
        Assert.Equal("专注", idle.Home.Toggles[2].Label);

        timer.Toggle(now);
        Assert.True(timer.Tick(now + TimeSpan.FromSeconds(30)));
        var running = SkiaIslandStateBuilder.BuildPages(content, island, localizer, null, timer.Snapshot);
        Assert.True(running.Home.Toggles[2].IsOn);
        Assert.Equal("24:30", running.Home.Toggles[2].Label);
    });

    [Fact]
    public void Pomodoro_completes_after_its_duration_and_can_restart()
    {
        var timer = new PomodoroTimer(TimeSpan.FromMinutes(25));
        var now = new DateTimeOffset(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);

        timer.Toggle(now);
        Assert.True(timer.Snapshot.IsRunning);

        timer.Tick(now + TimeSpan.FromMinutes(10));
        Assert.True(timer.Snapshot.IsRunning);
        Assert.Equal("15:00", timer.Snapshot.Text);

        Assert.True(timer.Tick(now + TimeSpan.FromMinutes(25)));
        Assert.False(timer.Snapshot.IsRunning);
        Assert.True(timer.Snapshot.IsComplete);
        Assert.Equal("完成", timer.Snapshot.Text);

        timer.Toggle(now + TimeSpan.FromMinutes(26));
        Assert.True(timer.Snapshot.IsRunning);
        Assert.False(timer.Snapshot.IsComplete);
    }

    private static IslandViewModel EmptyIsland() => new()
    {
        StateKind = IslandStateKind.Compact,
        Page = PageRef.Home,
    };
}
