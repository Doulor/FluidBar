using System.Globalization;
using System.Net.Http.Headers;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Threading;

namespace FluidBar.Platform.Windows.Weather;

/// <summary>Configuration for <see cref="WeatherService"/> (no API key: Open-Meteo + a public IP-location lookup).</summary>
public sealed class WeatherServiceOptions
{
    /// <summary>Default cache file (<c>%APPDATA%\\FluidBar\\weather.json</c>).</summary>
    public static string DefaultCachePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FluidBar", "weather.json");

    /// <summary>
    /// Optional city override ("Shanghai"). Empty keeps the automatic path (IP geolocation). The App has
    /// no editable settings slot for this yet (the settings window is owned by another workstream), so the
    /// composition reads the <c>FLUIDBAR_WEATHER_CITY</c> environment variable; a future settings item only
    /// has to assign this property.
    /// </summary>
    public string? City { get; init; }

    /// <summary>Cache file used for the "last known reading + time" offline degradation.</summary>
    public string CachePath { get; init; } = DefaultCachePath;

    /// <summary>Open-Meteo API root.</summary>
    public string ForecastBaseUrl { get; init; } = "https://api.open-meteo.com";

    /// <summary>Open-Meteo geocoding root (only used when <see cref="City"/> is set).</summary>
    public string GeocodingBaseUrl { get; init; } = "https://geocoding-api.open-meteo.com";

    /// <summary>Public IP-geolocation endpoint (no key) used when no city is configured.</summary>
    public string IpLocationUrl { get; init; } = "https://ipwho.is/";

    /// <summary>Refresh cadence. The first fetch runs immediately when the service starts.</summary>
    public TimeSpan RefreshInterval { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>Per-request timeout (the shared HttpClient keeps an infinite timeout).</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Geocoding language token ("en"/"zh").</summary>
    public string GeocodingLanguage { get; init; } = "en";

    /// <summary>Injected HTTP client (tests); when null the service owns one and disposes it.</summary>
    public HttpClient? Http { get; init; }

    /// <summary>
    /// Reads the environment seams used by the App composition and by the offline evidence run:
    /// <c>FLUIDBAR_WEATHER_CITY</c>, <c>FLUIDBAR_WEATHER_API_BASE</c>, <c>FLUIDBAR_WEATHER_GEO_BASE</c>,
    /// <c>FLUIDBAR_WEATHER_IP_URL</c>, <c>FLUIDBAR_WEATHER_CACHE</c>, <c>FLUIDBAR_WEATHER_INTERVAL_SECONDS</c>.
    /// </summary>
    /// <param name="uiCulture">UI culture tag; the geocoding language follows it when it starts with "zh".</param>
    public static WeatherServiceOptions FromEnvironment(string? uiCulture = null)
    {
        var interval = TimeSpan.FromMinutes(15);
        var seconds = Environment.GetEnvironmentVariable("FLUIDBAR_WEATHER_INTERVAL_SECONDS");
        if (int.TryParse(seconds, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= 10)
        {
            interval = TimeSpan.FromSeconds(parsed);
        }

        return new WeatherServiceOptions
        {
            City = Environment.GetEnvironmentVariable("FLUIDBAR_WEATHER_CITY"),
            CachePath = Environment.GetEnvironmentVariable("FLUIDBAR_WEATHER_CACHE") is { Length: > 0 } cache
                ? cache
                : DefaultCachePath,
            ForecastBaseUrl = TrimSlash(Environment.GetEnvironmentVariable("FLUIDBAR_WEATHER_API_BASE"))
                              ?? "https://api.open-meteo.com",
            GeocodingBaseUrl = TrimSlash(Environment.GetEnvironmentVariable("FLUIDBAR_WEATHER_GEO_BASE"))
                               ?? "https://geocoding-api.open-meteo.com",
            IpLocationUrl = Environment.GetEnvironmentVariable("FLUIDBAR_WEATHER_IP_URL") is { Length: > 0 } ip
                ? ip
                : "https://ipwho.is/",
            RefreshInterval = interval,
            GeocodingLanguage = uiCulture?.StartsWith("zh", StringComparison.OrdinalIgnoreCase) == true ? "zh" : "en",
        };
    }

    private static string? TrimSlash(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.TrimEnd('/');
}

/// <summary>
/// Real weather monitor: Open-Meteo (no API key) on a background poll, cached to
/// <c>%APPDATA%\\FluidBar\\weather.json</c>. Offline it publishes the last successful reading with its
/// fetch time (<see cref="WeatherStatus.Cached"/>) or an explicit unavailable snapshot — it never invents
/// a temperature.
/// </summary>
public sealed class WeatherService : PlatformMonitor<WeatherSnapshot>
{
    private readonly WeatherServiceOptions _options;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private BackgroundPoller? _poller;
    private WeatherCacheDocument? _cache;
    private int _fetching;
    private int _disposed;

    public WeatherService(IPlatformLogger logger, PlatformCounters counters,
        WeatherServiceOptions? options = null, TimeProvider? timeProvider = null)
        : base("weather", logger, counters, timeProvider ?? TimeProvider.System)
    {
        _options = options ?? new WeatherServiceOptions();
        _http = _options.Http ?? CreateHttpClient();
        _ownsHttp = _options.Http is null;
    }

    /// <summary>Options in effect (cache path/city/interval).</summary>
    public WeatherServiceOptions Options => _options;

    /// <summary>The shared HTTP client (callers own nothing; kept for tests/diagnostics).</summary>
    public static HttpClient CreateHttpClient() => new()
    {
        Timeout = Timeout.InfiniteTimeSpan,
        DefaultRequestHeaders =
        {
            UserAgent = { new ProductInfoHeaderValue("FluidBar", "2.0") },
            Accept = { new MediaTypeWithQualityHeaderValue("application/json") },
        },
    };

    protected override ValueTask OnStartAsync(CancellationToken cancellationToken)
    {
        LoadCacheAndPublish();
        _poller ??= new BackgroundPoller("weather", _options.RefreshInterval, PollTick, Logger);
        _poller.Start();
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnStopAsync(CancellationToken cancellationToken)
    {
        _poller?.Dispose();
        _poller = null;
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0 && _ownsHttp)
        {
            _http.Dispose();
        }

        await base.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>Asks the poll thread to refresh now (used by diagnostics/manual refresh).</summary>
    public void RequestRefresh() => _poller?.TriggerNow();

    private void PollTick()
    {
        if (State != PlatformMonitorState.Running)
        {
            return;
        }

        _ = RefreshAsync(CancellationToken.None);
    }

    private void LoadCacheAndPublish()
    {
        _cache = ReadCache();
        var snapshot = _cache is null
            ? WeatherSnapshot.Unavailable("no cached reading yet")
            : FromCache(_cache, "cached reading loaded on start");
        Publish(snapshot);
        if (_cache is not null)
        {
            Logger.LogInfo(PlatformLogCategory.Monitor, "weather.cache-loaded",
                "Weather cache loaded; showing the last successful reading until the first fetch succeeds.",
                new Dictionary<string, object?>
                {
                    ["fetchedAt"] = _cache.FetchedAt,
                    ["temperatureC"] = _cache.TemperatureC,
                });
        }
    }

    /// <summary>
    /// Fetches the current weather once. Returns the published snapshot; network/parse/permission
    /// failures degrade to the cached reading or an explicit unavailable snapshot (never a guess).
    /// </summary>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<WeatherSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _fetching, 1) != 0)
        {
            return _cache is null ? WeatherSnapshot.Unavailable("refresh already in flight")
                : FromCache(_cache, "refresh already in flight");
        }

        try
        {
            // A direct RefreshAsync (tests/diagnostics) must see the cache even when the monitor was not
            // started; the degraded snapshot then carries the last known reading instead of "unavailable".
            _cache ??= ReadCache();
            var location = await ResolveLocationAsync(cancellationToken).ConfigureAwait(false);
            if (location is null)
            {
                return Degrade("location-unavailable");
            }

            var reading = await FetchForecastAsync(location, cancellationToken).ConfigureAwait(false);
            if (reading is null)
            {
                return Degrade("forecast-failed");
            }

            var snapshot = new WeatherSnapshot(
                WeatherStatus.Ok,
                WeatherCodeMap.ToCondition(reading.Value.WeatherCode),
                reading.Value.WeatherCode,
                reading.Value.TemperatureC,
                reading.Value.HighC,
                reading.Value.LowC,
                reading.Value.WindKph,
                reading.Value.WindDirectionDeg,
                reading.Value.IsDay,
                TimeProvider.GetUtcNow(),
                location.Label,
                null);
            WriteCache(snapshot);
            Publish(snapshot);
            Logger.LogInfo(PlatformLogCategory.Monitor, "weather.updated",
                "Weather fetched from Open-Meteo.",
                new Dictionary<string, object?>
                {
                    ["temperatureC"] = snapshot.TemperatureC,
                    ["weatherCode"] = snapshot.Code,
                    ["location"] = snapshot.LocationLabel,
                });
            return snapshot;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(PlatformLogCategory.Monitor, "weather.refresh-failed",
                "Weather refresh failed; keeping the last known reading if there is one.", ex);
            return Degrade($"refresh-failed: {ex.GetType().Name}");
        }
        finally
        {
            Volatile.Write(ref _fetching, 0);
        }
    }

    private WeatherSnapshot Degrade(string reason)
    {
        var snapshot = _cache is null
            ? WeatherSnapshot.Unavailable(reason)
            : FromCache(_cache, reason);
        Publish(snapshot);
        Logger.LogWarning(PlatformLogCategory.Monitor, "weather.degraded",
            "Weather is unavailable from the network; the cached reading is shown with its age instead.",
            data: new Dictionary<string, object?>
            {
                ["reason"] = reason,
                ["hasCache"] = _cache is not null,
            });
        return snapshot;
    }

    private static WeatherSnapshot FromCache(WeatherCacheDocument cache, string reason) => new(
        WeatherStatus.Cached,
        WeatherCodeMap.ToCondition(cache.WeatherCode),
        cache.WeatherCode,
        cache.TemperatureC,
        cache.HighC,
        cache.LowC,
        cache.WindKph,
        cache.WindDirectionDeg,
        cache.IsDay,
        cache.FetchedAt,
        cache.LocationLabel,
        reason);

    private async Task<GeoLocation?> ResolveLocationAsync(CancellationToken cancellationToken)
    {
        var city = _options.City?.Trim();
        if (!string.IsNullOrEmpty(city))
        {
            var url = $"{_options.GeocodingBaseUrl}/v1/search?name={Uri.EscapeDataString(city)}" +
                      $"&count=1&language={_options.GeocodingLanguage}&format=json";
            var json = await GetStringAsync(url, cancellationToken).ConfigureAwait(false);
            var location = WeatherJson.ParseGeocoding(json);
            if (location is null)
            {
                Logger.LogWarning(PlatformLogCategory.Monitor, "weather.city-not-found",
                    "The configured weather city could not be geocoded.", data: new Dictionary<string, object?>
                    {
                        ["city"] = city,
                    });
            }

            return location;
        }

        var ipJson = await GetStringAsync(_options.IpLocationUrl, cancellationToken).ConfigureAwait(false);
        var automatic = WeatherJson.ParseIpLocation(ipJson);
        if (automatic is null)
        {
            Logger.LogWarning(PlatformLogCategory.Monitor, "weather.location-failed",
                "IP geolocation returned no usable location.");
        }

        return automatic;
    }

    private async Task<OpenMeteoReading?> FetchForecastAsync(GeoLocation location, CancellationToken cancellationToken)
    {
        var url = $"{_options.ForecastBaseUrl}/v1/forecast" +
                  $"?latitude={WeatherJson.FormatCoordinate(location.Latitude)}" +
                  $"&longitude={WeatherJson.FormatCoordinate(location.Longitude)}" +
                  "&current=temperature_2m,weather_code,is_day,wind_speed_10m,wind_direction_10m" +
                  "&daily=temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=1";
        var json = await GetStringAsync(url, cancellationToken).ConfigureAwait(false);
        var reading = WeatherJson.ParseOpenMeteo(json);
        if (reading is null)
        {
            Logger.LogWarning(PlatformLogCategory.Monitor, "weather.parse-failed",
                "The Open-Meteo response did not contain a current reading.");
        }

        return reading;
    }

    private async Task<string?> GetStringAsync(string url, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.RequestTimeout);
        try
        {
            using var response = await _http
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Logger.LogWarning(PlatformLogCategory.Monitor, "weather.http-error",
                    "Weather HTTP request failed.", data: new Dictionary<string, object?>
                    {
                        ["status"] = (int)response.StatusCode,
                        ["url"] = url,
                    });
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            if (body.Length > 256 * 1024)
            {
                Logger.LogWarning(PlatformLogCategory.Monitor, "weather.response-too-large",
                    "Weather response was larger than the 256 KiB cap.");
                return null;
            }

            return body;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(PlatformLogCategory.Monitor, "weather.request-failed",
                "Weather HTTP request threw.", ex, new Dictionary<string, object?>
                {
                    ["url"] = url,
                });
            return null;
        }
    }

    private WeatherCacheDocument? ReadCache()
    {
        try
        {
            if (!File.Exists(_options.CachePath))
            {
                return null;
            }

            var json = File.ReadAllText(_options.CachePath);
            var cache = WeatherCacheDocument.Parse(json);
            if (cache is null)
            {
                Logger.LogWarning(PlatformLogCategory.Monitor, "weather.cache-invalid",
                    "The weather cache file is not valid; it is ignored (no reading is shown as fresh).",
                    data: new Dictionary<string, object?>
                    {
                        ["path"] = _options.CachePath,
                    });
            }

            return cache;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(PlatformLogCategory.Monitor, "weather.cache-read-failed",
                "The weather cache could not be read.", ex);
            return null;
        }
    }

    private void WriteCache(WeatherSnapshot snapshot)
    {
        var document = new WeatherCacheDocument(
            snapshot.TemperatureC ?? 0,
            snapshot.Code,
            snapshot.IsDay,
            snapshot.HighC,
            snapshot.LowC,
            snapshot.WindKph,
            snapshot.WindDirectionDeg,
            snapshot.UpdatedAt ?? TimeProvider.GetUtcNow(),
            snapshot.LocationLabel);
        _cache = document;
        try
        {
            var directory = Path.GetDirectoryName(_options.CachePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temporary = _options.CachePath + ".tmp";
            File.WriteAllText(temporary, document.ToJson());
            File.Move(temporary, _options.CachePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(PlatformLogCategory.Monitor, "weather.cache-write-failed",
                "The weather cache could not be written (the reading itself is still shown).", ex);
        }
    }
}
