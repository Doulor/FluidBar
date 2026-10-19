using System.Collections.Immutable;
using FluidBar.App.State;
using FluidBar.Platform.Windows.Tasks;
using FluidBar.Platform.Windows.Weather;

namespace FluidBar.Ui.Tests;

/// <summary>Widgets page mapping: real weather/task data only, explicit empties otherwise.</summary>
public sealed class WidgetContentMapperTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 6, 0, 0, TimeSpan.Zero);

    private static WeatherSnapshot Reading(
        WeatherStatus status = WeatherStatus.Ok,
        WeatherCondition condition = WeatherCondition.Cloudy,
        bool isDay = true,
        DateTimeOffset? updatedAt = null,
        double temperature = 18.4,
        double? high = 24.1,
        double? low = 16.2,
        double? wind = 11.2,
        int? direction = 135)
        => new(status, condition, 3, temperature, high, low, wind, direction, isDay,
            updatedAt ?? Now, "Shanghai, China", status == WeatherStatus.Cached ? "offline" : null);

    private static TaskListSnapshot Tasks(params (string Title, bool Done)[] items)
        => new(items.Select(item => new TaskItemData(item.Title, item.Done)).ToImmutableArray(),
            TaskListStatus.Ok, @"C:\Users\me\AppData\Roaming\FluidBar\tasks.json", null);

    [Fact]
    public void MapWeather_FormatsTheRealReading()
    {
        var view = WidgetContentMapper.MapWeather(Reading(), Now);

        Assert.True(view.HasReading);
        Assert.Equal("18°", view.TemperatureText);
        Assert.Equal("24° / 16°", view.HighLowText);
        Assert.Equal("SE 11 km/h", view.WindText);
        Assert.Null(view.ConditionText);
        Assert.Equal(WidgetWeatherGlyph.CloudSun, view.Glyph);
    }

    [Fact]
    public void MapWeather_ClearSky_UsesTheSunByDayAndTheMoonAtNight()
    {
        Assert.Equal(WidgetWeatherGlyph.Sun,
            WidgetContentMapper.MapWeather(Reading(condition: WeatherCondition.Clear, isDay: true), Now).Glyph);
        Assert.Equal(WidgetWeatherGlyph.Moon,
            WidgetContentMapper.MapWeather(Reading(condition: WeatherCondition.Clear, isDay: false), Now).Glyph);
    }

    [Fact]
    public void MapWeather_CachedReading_ShowsItsAge()
    {
        var view = WidgetContentMapper.MapWeather(
            Reading(status: WeatherStatus.Cached, updatedAt: Now.AddHours(-3)), Now);

        Assert.Equal("18°", view.TemperatureText);
        Assert.Equal("3 h", view.ConditionText);
    }

    [Fact]
    public void MapWeather_NoReading_MapsToNothing()
    {
        Assert.False(WidgetContentMapper.MapWeather(null, Now).HasReading);
        Assert.False(WidgetContentMapper.MapWeather(WeatherSnapshot.Unavailable("offline"), Now).HasReading);
        Assert.Null(WidgetContentMapper.MapWeather(null, Now).TemperatureText);
    }

    [Fact]
    public void FormatTemperature_RoundsAndKeepsTheSign()
    {
        Assert.Equal("18°", WidgetContentMapper.FormatTemperature(18.4));
        Assert.Equal("-3°", WidgetContentMapper.FormatTemperature(-3.4));
        Assert.Equal("0°", WidgetContentMapper.FormatTemperature(0.2));
    }

    [Fact]
    public void FormatHighLowAndWind_AreIndependentOfMissingParts()
    {
        Assert.Null(WidgetContentMapper.FormatHighLow(null, null));
        Assert.Equal("24° / —", WidgetContentMapper.FormatHighLow(24.1, null));
        Assert.Null(WidgetContentMapper.FormatWind(null, null));
        Assert.Equal("11 km/h", WidgetContentMapper.FormatWind(11.2, null));
        Assert.Equal("N 5 km/h", WidgetContentMapper.FormatWind(5, 10));
        Assert.Equal("W 5 km/h", WidgetContentMapper.FormatWind(5, 270));
    }

    [Fact]
    public void MapTasks_RendersTheFirstThreeRowsAndCountsDone()
    {
        var view = WidgetContentMapper.MapTasks(
            Tasks(("One", true), ("Two", false), ("Three", true), ("Four", false)));

        Assert.Equal(3, view.Tasks.Length);
        Assert.Equal("One", view.Tasks[0].Title);
        Assert.True(view.Tasks[0].Done);
        Assert.Equal(2, view.DoneCount);
        Assert.Null(view.EmptyHint);
    }

    [Fact]
    public void MapTasks_MissingOrEmptyFile_ShowsTheExplicitHintWithTheFileName()
    {
        var missing = new TaskListSnapshot([], TaskListStatus.Missing,
            @"C:\Users\me\AppData\Roaming\FluidBar\tasks.json", "task file not found");

        var hint = WidgetContentMapper.MapTasks(missing);

        Assert.Empty(hint.Tasks);
        Assert.NotNull(hint.EmptyHint);
        Assert.Contains("tasks.json", hint.EmptyHint!, StringComparison.Ordinal);

        var empty = WidgetContentMapper.MapTasks(new TaskListSnapshot([], TaskListStatus.Ok,
            @"C:\Users\me\AppData\Roaming\FluidBar\tasks.json", null));
        Assert.NotNull(empty.EmptyHint);
    }

    [Fact]
    public void MapTasks_WithoutASnapshot_IsStillAnExplicitEmptyState()
    {
        var view = WidgetContentMapper.MapTasks(null);

        Assert.Empty(view.Tasks);
        Assert.NotNull(view.EmptyHint);
    }

    [Fact]
    public async Task MapWeather_LocalizedCachedAgeUsesTheCatalog()
    {
        var label = await AvaloniaTest.Run(() =>
        {
            var localizer = TestFixtures.CreateLocalizer("zh-Hans");
            return WidgetContentMapper.MapWeather(
                Reading(status: WeatherStatus.Cached, updatedAt: Now.AddMinutes(-5)), Now, localizer);
        });

        Assert.Equal("5 分钟前", label.ConditionText);
        await AvaloniaTest.Run(() => TestFixtures.Localizer.SetCulture("en-US"));
    }
}
