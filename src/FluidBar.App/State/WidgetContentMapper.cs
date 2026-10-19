using System.Collections.Immutable;
using System.Globalization;
using FluidBar.App.Localization;
using FluidBar.Platform.Windows.Tasks;
using FluidBar.Platform.Windows.Weather;

namespace FluidBar.App.State;

/// <summary>Weather glyph the island's icon set actually contains (the card picks one of these three).</summary>
public enum WidgetWeatherGlyph
{
    /// <summary>Cloud + sun (partly cloudy, overcast, rain, snow, storm, fog).</summary>
    CloudSun = 0,

    /// <summary>Sun (clear sky during the day).</summary>
    Sun = 1,

    /// <summary>Moon (clear sky at night).</summary>
    Moon = 2,
}

/// <summary>Weather card values; every string is null when no real reading exists.</summary>
/// <param name="TemperatureText">Current temperature ("18°").</param>
/// <param name="ConditionText">Localized condition ("Partly cloudy"), plus " · age" for a cached reading.</param>
/// <param name="HighLowText">Today's high/low ("21° / 11°").</param>
/// <param name="WindText">Wind line ("NW 12 km/h").</param>
/// <param name="Glyph">Icon chosen from the WMO weather code.</param>
public readonly record struct WidgetWeatherView(
    string? TemperatureText,
    string? ConditionText,
    string? HighLowText,
    string? WindText,
    WidgetWeatherGlyph Glyph)
{
    /// <summary>Whether a real temperature is available (fresh or cached).</summary>
    public bool HasReading => TemperatureText is not null;
}

/// <summary>Task card values.</summary>
/// <param name="Tasks">Rows to paint (first <see cref="WidgetContentMapper.MaxRenderedTasks"/> of the file).</param>
/// <param name="EmptyHint">Caption for the explicit empty state (missing/invalid file).</param>
/// <param name="DoneCount">Completed tasks in the file.</param>
/// <param name="MoreText">Localized "N more" line when the file has more rows than the card paints.</param>
/// <param name="ProgressText">"done/total" for the card header (file totals, not just the painted rows).</param>
/// <param name="TotalCount">Tasks in the file.</param>
public readonly record struct WidgetTaskView(
    ImmutableArray<WidgetTaskData> Tasks,
    string? EmptyHint,
    int DoneCount,
    string? MoreText = null,
    string? ProgressText = null,
    int TotalCount = 0);

/// <summary>
/// Maps the platform weather/task snapshots onto the island widgets page. Pure: formatting only, no I/O
/// and no synthesized values — a missing reading stays missing (the card then paints the unavailable
/// caption instead of a temperature).
/// </summary>
public static class WidgetContentMapper
{
    /// <summary>Rows the task card paints (the page scrolls when the card grows past the 232 DIP body).</summary>
    public const int MaxRenderedTasks = 8;

    /// <summary>Maps a weather snapshot to card text; all-null when there is no reading.</summary>
    /// <param name="snapshot">Weather snapshot (null before the service produced one).</param>
    /// <param name="now">Reference time for the cached-reading age.</param>
    /// <param name="localizer">Localizer for the age text; English fallbacks when null.</param>
    public static WidgetWeatherView MapWeather(WeatherSnapshot? snapshot, DateTimeOffset now, ILocalizer? localizer = null)
    {
        if (snapshot is null || snapshot.TemperatureC is not { } temperature)
        {
            return default;
        }

        var condition = FormatCondition(snapshot.Condition, localizer);
        // The secondary line always names the condition; a cached reading appends its age so it never
        // looks fresher than it is ("多云 · 2 小时前").
        var detail = snapshot.Status == WeatherStatus.Cached && snapshot.UpdatedAt is { } updatedAt
            ? $"{condition} · {FormatAge(updatedAt, now, localizer)}"
            : condition;

        return new WidgetWeatherView(
            FormatTemperature(temperature),
            detail,
            FormatHighLow(snapshot.HighC, snapshot.LowC),
            FormatWind(snapshot.WindKph, snapshot.WindDirectionDeg),
            ResolveGlyph(snapshot));
    }

    /// <summary>Maps a task snapshot to card rows (empty rows + hint when the file is missing/invalid/empty).</summary>
    /// <param name="snapshot">Task snapshot (null before the service produced one).</param>
    /// <param name="localizer">Localizer for the unavailable caption.</param>
    /// <param name="filePath">Path used for the hint when the snapshot carries none.</param>
    public static WidgetTaskView MapTasks(TaskListSnapshot? snapshot, ILocalizer? localizer = null, string? filePath = null)
    {
        if (snapshot is null || snapshot.Count == 0)
        {
            var unavailable = localizer?.Get(LocKeys.Settings.Plugins.StateUnavailable) ?? "Unavailable";
            var path = string.IsNullOrEmpty(snapshot?.FilePath) ? filePath : snapshot!.FilePath;
            var name = string.IsNullOrEmpty(path) ? "tasks.json" : Path.GetFileName(path);
            return new WidgetTaskView([], $"{unavailable} · {name}", 0);
        }

        var total = snapshot!.Count;
        var rows = ImmutableArray.CreateBuilder<WidgetTaskData>(Math.Min(MaxRenderedTasks, total));
        foreach (var item in snapshot.Items)
        {
            if (rows.Count >= MaxRenderedTasks)
            {
                break;
            }

            rows.Add(new WidgetTaskData { Title = item.Title, Done = item.Done });
        }

        var more = Math.Max(0, total - rows.Count);
        var moreText = more > 0
            ? localizer?.Format(LocKeys.Page.Widgets.TasksMore, ("count", more)) ?? $"{more} more"
            : null;
        return new WidgetTaskView(
            rows.MoveToImmutable(),
            null,
            snapshot.DoneCount,
            moreText,
            $"{snapshot.DoneCount}/{total}",
            total);
    }

    /// <summary>Localized condition label for the weather card ("Partly cloudy" / "多云").</summary>
    /// <param name="condition">Coarse condition from the WMO code.</param>
    /// <param name="localizer">Localizer; English fallbacks when null (tests/headless).</param>
    public static string FormatCondition(WeatherCondition condition, ILocalizer? localizer = null)
        => localizer?.Get(ConditionKey(condition)) ?? ConditionFallback(condition);

    /// <summary>Catalog key for one condition.</summary>
    /// <param name="condition">Coarse condition from the WMO code.</param>
    public static string ConditionKey(WeatherCondition condition) => condition switch
    {
        WeatherCondition.Clear => LocKeys.Page.Widgets.Weather.Clear,
        WeatherCondition.PartlyCloudy => LocKeys.Page.Widgets.Weather.PartlyCloudy,
        WeatherCondition.Cloudy => LocKeys.Page.Widgets.Weather.Cloudy,
        WeatherCondition.Fog => LocKeys.Page.Widgets.Weather.Fog,
        WeatherCondition.Drizzle => LocKeys.Page.Widgets.Weather.Drizzle,
        WeatherCondition.Rain => LocKeys.Page.Widgets.Weather.Rain,
        WeatherCondition.Snow => LocKeys.Page.Widgets.Weather.Snow,
        WeatherCondition.Thunderstorm => LocKeys.Page.Widgets.Weather.Thunderstorm,
        _ => LocKeys.Page.Widgets.Weather.Unknown,
    };

    private static string ConditionFallback(WeatherCondition condition) => condition switch
    {
        WeatherCondition.Clear => "Clear",
        WeatherCondition.PartlyCloudy => "Partly cloudy",
        WeatherCondition.Cloudy => "Overcast",
        WeatherCondition.Fog => "Fog",
        WeatherCondition.Drizzle => "Drizzle",
        WeatherCondition.Rain => "Rain",
        WeatherCondition.Snow => "Snow",
        WeatherCondition.Thunderstorm => "Thunderstorm",
        _ => "Weather",
    };

    /// <summary>Picks one of the island's three weather glyphs from the WMO-derived condition.</summary>
    /// <param name="snapshot">Weather snapshot.</param>
    public static WidgetWeatherGlyph ResolveGlyph(WeatherSnapshot snapshot)
        => snapshot.Condition == WeatherCondition.Clear
            ? snapshot.IsDay ? WidgetWeatherGlyph.Sun : WidgetWeatherGlyph.Moon
            : WidgetWeatherGlyph.CloudSun;

    /// <summary>Formats a Celsius temperature as the card shows it ("18°", "-3°").</summary>
    /// <param name="celsius">Temperature in °C.</param>
    public static string FormatTemperature(double celsius)
    {
        var rounded = Math.Round(celsius, MidpointRounding.AwayFromZero);
        return rounded.ToString("0°", CultureInfo.InvariantCulture);
    }

    /// <summary>Formats today's high/low ("21° / 11°"); null when both are missing.</summary>
    /// <param name="highC">Daily max in °C.</param>
    /// <param name="lowC">Daily min in °C.</param>
    public static string? FormatHighLow(double? highC, double? lowC)
    {
        if (highC is null && lowC is null)
        {
            return null;
        }

        var high = highC is { } h ? FormatTemperature(h) : "—";
        var low = lowC is { } l ? FormatTemperature(l) : "—";
        return $"{high} / {low}";
    }

    /// <summary>Formats the wind line ("NW 12 km/h"); null when Open-Meteo returned no wind speed.</summary>
    /// <param name="kilometersPerHour">Wind speed in km/h.</param>
    /// <param name="directionDegrees">Wind direction in degrees (0 = north).</param>
    public static string? FormatWind(double? kilometersPerHour, int? directionDegrees)
    {
        if (kilometersPerHour is not { } speed)
        {
            return null;
        }

        var rounded = Math.Round(speed, MidpointRounding.AwayFromZero);
        var value = rounded.ToString("0", CultureInfo.InvariantCulture);
        var direction = directionDegrees is { } degrees ? CompassPoint(degrees) : null;
        return direction is null ? $"{value} km/h" : $"{direction} {value} km/h";
    }

    /// <summary>Localized age of a cached reading ("just now" / "5 minutes ago" / "3 hours ago").</summary>
    /// <param name="updatedAt">Fetch time of the reading.</param>
    /// <param name="now">Reference time.</param>
    /// <param name="localizer">Localizer (existing Notifications relative-time keys); English fallback when null.</param>
    public static string FormatAge(DateTimeOffset updatedAt, DateTimeOffset now, ILocalizer? localizer = null)
    {
        var delta = now - updatedAt;
        if (delta < TimeSpan.Zero)
        {
            delta = TimeSpan.Zero;
        }

        if (delta.TotalSeconds < 60)
        {
            return localizer?.Get(LocKeys.Notifications.JustNow) ?? "now";
        }

        if (delta.TotalMinutes < 60)
        {
            var minutes = Math.Max(1, (int)delta.TotalMinutes);
            return localizer is null
                ? $"{minutes} min"
                : localizer.Format(LocKeys.Notifications.MinutesAgo, ("count", minutes));
        }

        var hours = Math.Max(1, (int)delta.TotalHours);
        return localizer is null
            ? $"{hours} h"
            : localizer.Format(LocKeys.Notifications.HoursAgo, ("count", hours));
    }

    private static string? CompassPoint(int degrees)
    {
        var normalized = ((degrees % 360) + 360) % 360;
        string[] points = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"];
        return points[(int)Math.Round(normalized / 45.0, MidpointRounding.AwayFromZero) % 8];
    }
}
