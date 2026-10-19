using System.Collections.Immutable;
using System.Net;
using System.Text;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Tasks;
using FluidBar.Platform.Windows.Weather;

namespace FluidBar.Platform.Tests;

/// <summary>WMO mapping, Open-Meteo/IP-geo parsing, cache round-trip and the offline degradation rules.</summary>
public sealed class WeatherTests
{
    private const string ForecastJson = """
        {
          "latitude": 31.2222, "longitude": 121.4581, "timezone": "Asia/Shanghai",
          "current": {
            "time": "2026-10-20T06:00", "interval": 900,
            "temperature_2m": 21.4, "weather_code": 3, "is_day": 1,
            "wind_speed_10m": 11.2, "wind_direction_10m": 135
          },
          "daily": {
            "time": ["2026-10-20"],
            "temperature_2m_max": [24.1],
            "temperature_2m_min": [16.2]
          }
        }
        """;

    private const string IpLocationJson = """
        { "ip": "1.2.3.4", "success": true, "city": "Shanghai", "region": "Shanghai",
          "country": "China", "latitude": 31.22222, "longitude": 121.45806 }
        """;

    private const string GeocodingJson = """
        { "results": [ { "id": 1796236, "name": "Shanghai", "latitude": 31.22222,
          "longitude": 121.45806, "country": "China" } ] }
        """;

    [Theory]
    [InlineData(0, WeatherCondition.Clear)]
    [InlineData(1, WeatherCondition.PartlyCloudy)]
    [InlineData(2, WeatherCondition.PartlyCloudy)]
    [InlineData(3, WeatherCondition.Cloudy)]
    [InlineData(45, WeatherCondition.Fog)]
    [InlineData(48, WeatherCondition.Fog)]
    [InlineData(53, WeatherCondition.Drizzle)]
    [InlineData(63, WeatherCondition.Rain)]
    [InlineData(80, WeatherCondition.Rain)]
    [InlineData(71, WeatherCondition.Snow)]
    [InlineData(86, WeatherCondition.Snow)]
    [InlineData(95, WeatherCondition.Thunderstorm)]
    [InlineData(99, WeatherCondition.Thunderstorm)]
    [InlineData(-1, WeatherCondition.Unknown)]
    [InlineData(123, WeatherCondition.Unknown)]
    public void WeatherCodeMap_MapsTheWmoCodesOntoConditions(int code, WeatherCondition expected)
        => Assert.Equal(expected, WeatherCodeMap.ToCondition(code));

    [Fact]
    public void ParseOpenMeteo_ReadsTheCurrentReadingAndTheDailyRange()
    {
        var reading = WeatherJson.ParseOpenMeteo(ForecastJson);

        Assert.NotNull(reading);
        Assert.Equal(21.4, reading!.Value.TemperatureC, 3);
        Assert.Equal(3, reading.Value.WeatherCode);
        Assert.True(reading.Value.IsDay);
        Assert.Equal(24.1, reading.Value.HighC!.Value, 3);
        Assert.Equal(16.2, reading.Value.LowC!.Value, 3);
        Assert.Equal(11.2, reading.Value.WindKph!.Value, 3);
        Assert.Equal(135, reading.Value.WindDirectionDeg);
    }

    [Fact]
    public void ParseOpenMeteo_TreatsMissingOptionalFieldsAsNull()
    {
        var reading = WeatherJson.ParseOpenMeteo(
            """{ "current": { "temperature_2m": -3.5, "weather_code": 0 } }""");

        Assert.NotNull(reading);
        Assert.Equal(-3.5, reading!.Value.TemperatureC, 3);
        Assert.Null(reading.Value.HighC);
        Assert.Null(reading.Value.WindKph);
        Assert.Null(reading.Value.WindDirectionDeg);
        Assert.True(reading.Value.IsDay);
    }

    [Fact]
    public void ParseOpenMeteo_RejectsPayloadsWithoutACurrentReading()
    {
        Assert.Null(WeatherJson.ParseOpenMeteo(null));
        Assert.Null(WeatherJson.ParseOpenMeteo("not json"));
        Assert.Null(WeatherJson.ParseOpenMeteo("{}"));
        Assert.Null(WeatherJson.ParseOpenMeteo("""{ "current": { "weather_code": 3 } }"""));
        Assert.Null(WeatherJson.ParseOpenMeteo("""{ "hourly": { "temperature_2m": [1] } }"""));
    }

    [Fact]
    public void ParseIpLocation_And_Geocoding_ResolveCoordinates()
    {
        var ip = WeatherJson.ParseIpLocation(IpLocationJson);
        Assert.NotNull(ip);
        Assert.Equal(31.22222, ip!.Latitude, 4);
        Assert.Equal("Shanghai, China", ip.Label);

        var city = WeatherJson.ParseGeocoding(GeocodingJson);
        Assert.NotNull(city);
        Assert.Equal(121.45806, city!.Longitude, 4);
        Assert.Equal("Shanghai, China", city.Label);

        Assert.Null(WeatherJson.ParseIpLocation("""{ "success": false }"""));
        Assert.Null(WeatherJson.ParseGeocoding("""{ "results": [] }"""));
        Assert.Null(WeatherJson.ParseGeocoding("{}"));
    }

    [Fact]
    public void Cache_RoundTripsAndIgnoresGarbage()
    {
        var document = new WeatherCacheDocument(21.4, 3, true, 24.1, 16.2, 11.2, 135,
            DateTimeOffset.Parse("2026-10-20T06:00:00+08:00"), "Shanghai, China");

        var parsed = WeatherCacheDocument.Parse(document.ToJson());

        Assert.NotNull(parsed);
        Assert.Equal(document with { }, parsed);
        Assert.Null(WeatherCacheDocument.Parse("{ not json"));
        Assert.Null(WeatherCacheDocument.Parse("{}"));
        Assert.Null(WeatherCacheDocument.Parse(null));
    }

    [Fact]
    public async Task Refresh_PublishesTheRealReadingAndWritesTheCacheFile()
    {
        using var temp = new TempWeatherWorkspace();
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath.Contains("forecast", StringComparison.Ordinal)
            ? Json(ForecastJson)
            : Json(IpLocationJson));
        await using var service = CreateService(temp, handler);

        var snapshot = await service.RefreshAsync();

        Assert.Equal(WeatherStatus.Ok, snapshot.Status);
        Assert.Equal(21.4, snapshot.TemperatureC!.Value, 3);
        Assert.Equal(WeatherCondition.Cloudy, snapshot.Condition);
        Assert.Equal(135, snapshot.WindDirectionDeg);
        Assert.NotNull(snapshot.UpdatedAt);
        Assert.True(File.Exists(temp.CachePath), "the successful reading must be cached");

        var cached = WeatherCacheDocument.Parse(File.ReadAllText(temp.CachePath));
        Assert.NotNull(cached);
        Assert.Equal(21.4, cached!.TemperatureC, 3);
    }

    [Fact]
    public async Task Refresh_OfflineWithACache_ShowsTheLastKnownReadingAndTime()
    {
        using var temp = new TempWeatherWorkspace();
        var online = new StubHandler(request => request.RequestUri!.AbsolutePath.Contains("forecast", StringComparison.Ordinal)
            ? Json(ForecastJson)
            : Json(IpLocationJson));
        await using var first = CreateService(temp, online);
        await first.RefreshAsync();

        var offline = new StubHandler(static _ => throw new HttpRequestException("no network"));
        await using var second = CreateService(temp, offline);
        var snapshot = await second.RefreshAsync();

        Assert.Equal(WeatherStatus.Cached, snapshot.Status);
        Assert.Equal(21.4, snapshot.TemperatureC!.Value, 3);
        Assert.NotNull(snapshot.UpdatedAt);
        Assert.False(string.IsNullOrEmpty(snapshot.FailureReason));
    }

    [Fact]
    public async Task Refresh_OfflineWithoutACache_IsExplicitlyUnavailable()
    {
        using var temp = new TempWeatherWorkspace();
        var offline = new StubHandler(static _ => throw new HttpRequestException("no network"));
        await using var service = CreateService(temp, offline);

        var snapshot = await service.RefreshAsync();

        Assert.Equal(WeatherStatus.Unavailable, snapshot.Status);
        Assert.Null(snapshot.TemperatureC);
        Assert.False(snapshot.HasReading);
    }

    [Fact]
    public async Task StartedService_RefreshesAgainOnTheConfiguredCadence()
    {
        using var temp = new TempWeatherWorkspace();
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath.Contains("forecast", StringComparison.Ordinal)
            ? Json(ForecastJson)
            : Json(IpLocationJson));
        await using var service = CreateService(temp, handler, new WeatherServiceOptions
        {
            RefreshInterval = TimeSpan.FromSeconds(1),
            CachePath = temp.CachePath,
            Http = handler.CreateClient(),
        });

        await service.StartAsync();

        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (handler.ForecastCallCount < 2 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
        }

        await service.StopAsync();
        Assert.True(handler.ForecastCallCount >= 2,
            $"expected the weather monitor to refresh again, saw {handler.ForecastCallCount} forecast call(s)");
    }

    [Fact]
    public async Task Refresh_WithACityOverride_UsesTheGeocodingEndpoint()
    {
        using var temp = new TempWeatherWorkspace();
        var handler = new StubHandler(request =>
        {
            var uri = request.RequestUri!;
            return uri.AbsolutePath.Contains("search", StringComparison.Ordinal) ? Json(GeocodingJson)
                : uri.AbsolutePath.Contains("forecast", StringComparison.Ordinal) ? Json(ForecastJson)
                : Json("{}");
        });
        var options = new WeatherServiceOptions
        {
            City = "Shanghai",
            CachePath = temp.CachePath,
            Http = handler.CreateClient(),
        };
        await using var service = CreateService(temp, handler, options);

        var snapshot = await service.RefreshAsync();

        Assert.Equal(WeatherStatus.Ok, snapshot.Status);
        Assert.Contains(handler.Requests, url => url.Contains("search?name=Shanghai", StringComparison.Ordinal));
        Assert.Equal("Shanghai, China", snapshot.LocationLabel);
    }

    [Fact]
    public async Task Refresh_UnknownCity_IsUnavailableWithoutFabricatingCoordinates()
    {
        using var temp = new TempWeatherWorkspace();
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath.Contains("search", StringComparison.Ordinal)
            ? Json("""{ "results": [] }""")
            : Json(ForecastJson));
        var options = new WeatherServiceOptions
        {
            City = "Nowhere-12345",
            CachePath = temp.CachePath,
            Http = handler.CreateClient(),
        };
        await using var service = CreateService(temp, handler, options);

        var snapshot = await service.RefreshAsync();

        Assert.Equal(WeatherStatus.Unavailable, snapshot.Status);
        Assert.DoesNotContain(handler.Requests, url => url.Contains("forecast", StringComparison.Ordinal));
    }

    private static WeatherService CreateService(TempWeatherWorkspace temp, StubHandler handler, WeatherServiceOptions? options = null)
        => new(NullPlatformLogger.Instance, new PlatformCounters(),
            options ?? new WeatherServiceOptions
            {
                CachePath = temp.CachePath,
                Http = handler.CreateClient(),
            });

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        private readonly List<string> _requests = [];
        private int _forecastCalls;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        public IReadOnlyList<string> Requests
        {
            get
            {
                lock (_requests)
                {
                    return _requests.ToArray();
                }
            }
        }

        public int ForecastCallCount => Volatile.Read(ref _forecastCalls);

        public HttpClient CreateClient() => new(this, disposeHandler: false);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!.ToString();
            lock (_requests)
            {
                _requests.Add(uri);
            }

            if (uri.Contains("forecast", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _forecastCalls);
            }

            return Task.FromResult(_responder(request));
        }
    }

    private sealed class TempWeatherWorkspace : IDisposable
    {
        public TempWeatherWorkspace()
        {
            Directory = Path.Combine(Path.GetTempPath(), "fluidbar-weather-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Directory);
            CachePath = Path.Combine(Directory, "weather.json");
        }

        public string Directory { get; }

        public string CachePath { get; }

        public void Dispose()
        {
            try
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
            catch (IOException)
            {
                // Best effort: the OS cleans %TEMP% anyway.
            }
        }
    }
}

/// <summary>Task file parsing and the atomic checkbox write-back.</summary>
public sealed class TaskListTests
{
    [Fact]
    public void Parse_ReadsTheDocumentedShape()
    {
        var snapshot = TaskListService.Parse("""
            [ { "title": "Write evidence", "done": true }, { "title": "Ship the beta", "done": false } ]
            """, fileExists: true, "tasks.json");

        Assert.True(snapshot.IsAvailable);
        Assert.Equal(2, snapshot.Count);
        Assert.True(snapshot.Items[0].Done);
        Assert.Equal("Ship the beta", snapshot.Items[1].Title);
        Assert.Equal(1, snapshot.DoneCount);
    }

    [Fact]
    public void Parse_SkipsRowsWithoutATitleAndDefaultsMissingDone()
    {
        var snapshot = TaskListService.Parse("""
            [ { "title": "keep" }, { "done": true }, { "title": "   " }, { "title": 42 }, "nope" ]
            """, fileExists: true, "tasks.json");

        Assert.True(snapshot.IsAvailable);
        Assert.Single(snapshot.Items);
        Assert.Equal("keep", snapshot.Items[0].Title);
        Assert.False(snapshot.Items[0].Done);
    }

    [Fact]
    public void Parse_DistinguishesMissingInvalidAndEmptyFiles()
    {
        Assert.Equal(TaskListStatus.Missing, TaskListService.Parse(null, fileExists: false, "tasks.json").Status);
        Assert.Equal(TaskListStatus.Invalid, TaskListService.Parse("{ not json", fileExists: true, "tasks.json").Status);
        Assert.Equal(TaskListStatus.Invalid, TaskListService.Parse("""{ "title": "x" }""", fileExists: true, "tasks.json").Status);

        var empty = TaskListService.Parse("", fileExists: true, "tasks.json");
        Assert.Equal(TaskListStatus.Ok, empty.Status);
        Assert.Equal(0, empty.Count);
        Assert.Null(empty.FailureReason);
    }

    [Fact]
    public void Serialize_WritesIndentedTasksInTheDocumentedShape()
    {
        var json = TaskListService.Serialize([new TaskItemData("One", true), new TaskItemData("Two", false)]);

        Assert.Contains("\"title\": \"One\"", json, StringComparison.Ordinal);
        Assert.Contains("\"done\": true", json, StringComparison.Ordinal);
        Assert.EndsWith(Environment.NewLine, json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Toggle_WritesTheFileAtomicallyAndKeepsTheOtherRows()
    {
        using var workspace = new TempTaskWorkspace();
        File.WriteAllText(workspace.FilePath, """
            [ { "title": "Write evidence", "done": false }, { "title": "Ship the beta", "done": true } ]
            """);
        await using var service = CreateService(workspace);

        var result = await service.ToggleAsync(0);

        Assert.True(result.Succeeded);
        Assert.True(result.Snapshot.Items[0].Done);
        Assert.Equal(2, result.Snapshot.DoneCount);
        Assert.False(File.Exists(workspace.FilePath + ".tmp"));

        var written = TaskListService.Parse(File.ReadAllText(workspace.FilePath), fileExists: true, workspace.FilePath);
        Assert.True(written.IsAvailable);
        Assert.True(written.Items[0].Done);
        Assert.Equal("Ship the beta", written.Items[1].Title);
        Assert.True(written.Items[1].Done);
    }

    [Fact]
    public async Task Toggle_OutOfRangeOrMissingFile_FailsWithoutWriting()
    {
        using var workspace = new TempTaskWorkspace();
        File.WriteAllText(workspace.FilePath, """[ { "title": "Only", "done": false } ]""");
        var original = File.ReadAllText(workspace.FilePath);
        await using var service = CreateService(workspace);

        var outOfRange = await service.ToggleAsync(3);
        Assert.False(outOfRange.Succeeded);
        Assert.Equal(original, File.ReadAllText(workspace.FilePath));

        File.Delete(workspace.FilePath);
        var missing = await service.ToggleAsync(0);
        Assert.False(missing.Succeeded);
        Assert.Equal(TaskListStatus.Missing, missing.Snapshot.Status);
    }

    [Fact]
    public async Task Toggle_WriteFailure_LeavesTheOriginalFileIntact()
    {
        using var workspace = new TempTaskWorkspace();
        File.WriteAllText(workspace.FilePath, """[ { "title": "Only", "done": false } ]""");
        var original = File.ReadAllText(workspace.FilePath);
        await using var service = CreateService(workspace);

        // Another process is holding the staging file: the atomic temp+replace write cannot proceed, so the
        // toggle must report the failure and leave tasks.json untouched.
        var temporary = workspace.FilePath + ".tmp";
        using (var hold = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var result = await service.ToggleAsync(0);

            Assert.False(result.Succeeded);
            Assert.Contains("write-failed", result.FailureReason, StringComparison.Ordinal);
        }

        File.Delete(temporary);
        Assert.Equal(original, File.ReadAllText(workspace.FilePath));
    }

    [Fact]
    public async Task Load_MissingFile_IsExplicitlyEmpty()
    {
        using var workspace = new TempTaskWorkspace();
        await using var service = CreateService(workspace);

        var snapshot = service.Load();

        Assert.Equal(TaskListStatus.Missing, snapshot.Status);
        Assert.Equal(0, snapshot.Count);
        Assert.Equal(workspace.FilePath, snapshot.FilePath);
    }

    private static TaskListService CreateService(TempTaskWorkspace workspace)
        => new(NullPlatformLogger.Instance, new PlatformCounters(),
            new TaskListOptions
            {
                FilePath = workspace.FilePath,
                ReloadInterval = TimeSpan.FromSeconds(1),
            });

    private sealed class TempTaskWorkspace : IDisposable
    {
        public TempTaskWorkspace()
        {
            Directory = Path.Combine(Path.GetTempPath(), "fluidbar-tasks-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Directory);
            FilePath = Path.Combine(Directory, "tasks.json");
        }

        public string Directory { get; }

        public string FilePath { get; }

        public void Dispose()
        {
            try
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
            catch (IOException)
            {
                // Best effort.
            }
        }
    }
}
