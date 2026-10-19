using System.Globalization;
using System.Text.Json;

namespace FluidBar.Platform.Windows.Weather;

/// <summary>Weather state exposed to the island: real reading, last-known cached reading, or unavailable.</summary>
public enum WeatherStatus
{
    /// <summary>No reading at all (no cache and the last refresh failed).</summary>
    Unavailable = 0,

    /// <summary>Fresh reading fetched from Open-Meteo in this session.</summary>
    Ok = 1,

    /// <summary>Last successful reading loaded from the local cache (the network refresh failed).</summary>
    Cached = 2,
}

/// <summary>Coarse condition derived from the WMO weather code (drives the icon and nothing else).</summary>
public enum WeatherCondition
{
    /// <summary>Unknown/unsupported code.</summary>
    Unknown = 0,

    /// <summary>Clear sky (WMO 0).</summary>
    Clear,

    /// <summary>Mainly clear / partly cloudy (WMO 1-2).</summary>
    PartlyCloudy,

    /// <summary>Overcast (WMO 3).</summary>
    Cloudy,

    /// <summary>Fog or depositing rime (WMO 45/48).</summary>
    Fog,

    /// <summary>Drizzle (WMO 51-57).</summary>
    Drizzle,

    /// <summary>Rain or rain showers (WMO 61-67, 80-82).</summary>
    Rain,

    /// <summary>Snow or snow grains/showers (WMO 71-77, 85-86).</summary>
    Snow,

    /// <summary>Thunderstorm (WMO 95-99).</summary>
    Thunderstorm,
}

/// <summary>
/// One weather snapshot. Never fabricated: every field is either fetched from Open-Meteo or read back
/// from the cache written by an earlier successful fetch (<see cref="WeatherStatus.Cached"/>).
/// </summary>
/// <param name="Status">Fresh / cached / unavailable.</param>
/// <param name="Condition">Coarse condition (icon choice).</param>
/// <param name="Code">Raw WMO weather code, or -1 when unknown.</param>
/// <param name="TemperatureC">Current temperature in °C.</param>
/// <param name="HighC">Today's high in °C (daily max).</param>
/// <param name="LowC">Today's low in °C (daily min).</param>
/// <param name="WindKph">Wind speed in km/h.</param>
/// <param name="WindDirectionDeg">Wind direction in degrees (0 = north).</param>
/// <param name="IsDay">Whether Open-Meteo reports daytime.</param>
/// <param name="UpdatedAt">When the reading was fetched (UTC).</param>
/// <param name="LocationLabel">Resolved place name ("Shanghai, China"), when known.</param>
/// <param name="FailureReason">Why the last refresh failed (diagnostics only; never rendered as data).</param>
public sealed record WeatherSnapshot(
    WeatherStatus Status,
    WeatherCondition Condition,
    int Code,
    double? TemperatureC,
    double? HighC,
    double? LowC,
    double? WindKph,
    int? WindDirectionDeg,
    bool IsDay,
    DateTimeOffset? UpdatedAt,
    string? LocationLabel,
    string? FailureReason)
{
    /// <summary>True when a temperature is available (fresh or cached).</summary>
    public bool HasReading => TemperatureC is not null;

    /// <summary>Snapshot with no data at all.</summary>
    /// <param name="reason">Why no reading exists (diagnostics).</param>
    public static WeatherSnapshot Unavailable(string reason)
        => new(WeatherStatus.Unavailable, WeatherCondition.Unknown, -1, null, null, null, null, null, true,
            null, null, reason);
}

/// <summary>One raw Open-Meteo reading (current + today's min/max).</summary>
/// <param name="TemperatureC">Current temperature in °C.</param>
/// <param name="WeatherCode">WMO weather code.</param>
/// <param name="IsDay">Open-Meteo's is_day flag.</param>
/// <param name="HighC">Today's daily max, when returned.</param>
/// <param name="LowC">Today's daily min, when returned.</param>
/// <param name="WindKph">Wind speed in km/h.</param>
/// <param name="WindDirectionDeg">Wind direction in degrees.</param>
public readonly record struct OpenMeteoReading(
    double TemperatureC,
    int WeatherCode,
    bool IsDay,
    double? HighC,
    double? LowC,
    double? WindKph,
    int? WindDirectionDeg);

/// <summary>A resolved location (coordinates + human label).</summary>
/// <param name="Latitude">Latitude in degrees.</param>
/// <param name="Longitude">Longitude in degrees.</param>
/// <param name="Label">Place label ("Shanghai, China"), when known.</param>
public sealed record GeoLocation(double Latitude, double Longitude, string? Label);

/// <summary>WMO weather code → coarse condition (icon set mapping lives in the App).</summary>
public static class WeatherCodeMap
{
    /// <summary>Maps an Open-Meteo WMO code onto the coarse condition.</summary>
    /// <param name="code">WMO weather code.</param>
    public static WeatherCondition ToCondition(int code) => code switch
    {
        0 => WeatherCondition.Clear,
        1 or 2 => WeatherCondition.PartlyCloudy,
        3 => WeatherCondition.Cloudy,
        45 or 48 => WeatherCondition.Fog,
        51 or 53 or 55 or 56 or 57 => WeatherCondition.Drizzle,
        61 or 63 or 65 or 66 or 67 => WeatherCondition.Rain,
        71 or 73 or 75 or 77 => WeatherCondition.Snow,
        80 or 81 or 82 => WeatherCondition.Rain,
        85 or 86 => WeatherCondition.Snow,
        95 or 96 or 99 => WeatherCondition.Thunderstorm,
        _ => WeatherCondition.Unknown,
    };
}

/// <summary>Cached weather document persisted to <c>%APPDATA%\\FluidBar\\weather.json</c>.</summary>
/// <param name="TemperatureC">Last successful current temperature.</param>
/// <param name="WeatherCode">Last successful WMO code.</param>
/// <param name="IsDay">Last successful is_day flag.</param>
/// <param name="HighC">Daily max at fetch time.</param>
/// <param name="LowC">Daily min at fetch time.</param>
/// <param name="WindKph">Wind speed at fetch time.</param>
/// <param name="WindDirectionDeg">Wind direction at fetch time.</param>
/// <param name="FetchedAt">When the cached reading was fetched.</param>
/// <param name="LocationLabel">Place label at fetch time.</param>
public sealed record WeatherCacheDocument(
    double TemperatureC,
    int WeatherCode,
    bool IsDay,
    double? HighC,
    double? LowC,
    double? WindKph,
    int? WindDirectionDeg,
    DateTimeOffset FetchedAt,
    string? LocationLabel)
{
    private const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Parses a cache file; malformed documents are treated as "no cache" (never guessed).</summary>
    /// <param name="json">Cache file content.</param>
    public static WeatherCacheDocument? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !TryGetDouble(root, "temperatureC", out var temperature)
                || !TryGetInt(root, "weatherCode", out var code)
                || !root.TryGetProperty("fetchedAt", out var fetchedAtElement)
                || !fetchedAtElement.TryGetDateTimeOffset(out var fetchedAt))
            {
                return null;
            }

            return new WeatherCacheDocument(
                temperature,
                code,
                !root.TryGetProperty("isDay", out var isDayElement) || isDayElement.ValueKind != JsonValueKind.False,
                TryGetDouble(root, "highC", out var high) ? high : null,
                TryGetDouble(root, "lowC", out var low) ? low : null,
                TryGetDouble(root, "windKph", out var wind) ? wind : null,
                TryGetInt(root, "windDirectionDeg", out var direction) ? direction : null,
                fetchedAt,
                root.TryGetProperty("location", out var location) && location.ValueKind == JsonValueKind.String
                    ? location.GetString()
                    : null);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Serializes the document (indented, so the file is diffable in evidence).</summary>
    public string ToJson()
        => JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["schemaVersion"] = SchemaVersion,
            ["temperatureC"] = TemperatureC,
            ["weatherCode"] = WeatherCode,
            ["isDay"] = IsDay,
            ["highC"] = HighC,
            ["lowC"] = LowC,
            ["windKph"] = WindKph,
            ["windDirectionDeg"] = WindDirectionDeg,
            ["fetchedAt"] = FetchedAt,
            ["location"] = LocationLabel,
        }, JsonOptions);

    private static bool TryGetDouble(JsonElement root, string name, out double value)
    {
        value = 0;
        return root.TryGetProperty(name, out var element)
               && element.ValueKind == JsonValueKind.Number
               && element.TryGetDouble(out value);
    }

    private static bool TryGetInt(JsonElement root, string name, out int value)
    {
        value = 0;
        return root.TryGetProperty(name, out var element)
               && element.ValueKind == JsonValueKind.Number
               && element.TryGetInt32(out value);
    }
}

/// <summary>Tolerant parsers for the two public JSON endpoints used by <see cref="WeatherService"/>.</summary>
public static class WeatherJson
{
    /// <summary>Parses an Open-Meteo <c>/v1/forecast</c> response; null when the payload has no current reading.</summary>
    /// <param name="json">Response body.</param>
    public static OpenMeteoReading? ParseOpenMeteo(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("current", out var current)
                || current.ValueKind != JsonValueKind.Object
                || !current.TryGetProperty("temperature_2m", out var temperatureElement)
                || !temperatureElement.TryGetDouble(out var temperature)
                || !current.TryGetProperty("weather_code", out var codeElement)
                || !codeElement.TryGetInt32(out var code))
            {
                return null;
            }

            double? high = null;
            double? low = null;
            if (root.TryGetProperty("daily", out var daily) && daily.ValueKind == JsonValueKind.Object)
            {
                high = FirstNumber(daily, "temperature_2m_max");
                low = FirstNumber(daily, "temperature_2m_min");
            }

            return new OpenMeteoReading(
                temperature,
                code,
                !current.TryGetProperty("is_day", out var isDay) || isDay.ValueKind != JsonValueKind.Number || isDay.GetInt32() != 0,
                high,
                low,
                TryGetDouble(current, "wind_speed_10m"),
                TryGetInt(current, "wind_direction_10m"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Parses an ipwho.is style IP-geolocation response; null when the call failed.</summary>
    /// <param name="json">Response body.</param>
    public static GeoLocation? ParseIpLocation(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var latitude = TryGetDouble(root, "latitude");
            var longitude = TryGetDouble(root, "longitude");
            if (root.ValueKind != JsonValueKind.Object
                || (root.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.False)
                || latitude is not { } lat
                || longitude is not { } lon)
            {
                return null;
            }

            return new GeoLocation(lat, lon, ComposeLabel(root, "city", "region", "country"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Parses an Open-Meteo geocoding response (first result wins); null when nothing matched.</summary>
    /// <param name="json">Response body.</param>
    public static GeoLocation? ParseGeocoding(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("results", out var results)
                || results.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var result in results.EnumerateArray())
            {
                var latitude = TryGetDouble(result, "latitude");
                var longitude = TryGetDouble(result, "longitude");
                if (result.ValueKind == JsonValueKind.Object
                    && latitude is { } lat
                    && longitude is { } lon)
                {
                    return new GeoLocation(lat, lon, ComposeLabel(result, "name", null, "country"));
                }
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ComposeLabel(JsonElement root, string first, string? second, string third)
    {
        var parts = new List<string>(2);
        foreach (var name in new[] { first, second, third })
        {
            if (name is null
                || !root.TryGetProperty(name, out var element)
                || element.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(element.GetString()))
            {
                continue;
            }

            var value = element.GetString()!.Trim();
            // IP lookups repeat the city as the region ("Shanghai, Shanghai, China"); keep one.
            if (!parts.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                parts.Add(value);
            }
        }

        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    private static double? FirstNumber(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Number && item.TryGetDouble(out var value))
            {
                return value;
            }
        }

        return null;
    }

    private static double? TryGetDouble(JsonElement root, string name)
        => root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.Number
           && element.TryGetDouble(out var value)
            ? value
            : null;

    private static int? TryGetInt(JsonElement root, string name)
        => root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.Number
           && element.TryGetInt32(out var value)
            ? value
            : null;

    /// <summary>Round-trips a coordinate for query strings (invariant culture, 5 decimals).</summary>
    /// <param name="value">Coordinate in degrees.</param>
    public static string FormatCoordinate(double value)
        => value.ToString("0.#####", CultureInfo.InvariantCulture);
}
