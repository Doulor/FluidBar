using System.Text.Json;
using FluidBar.Core.Telemetry;

namespace FluidBar.Core.Tests.Telemetry;

/// <summary>
/// The wire format is a privacy contract: these tests fail the moment a field is added to the payload without a
/// deliberate decision, which is the only real defence against user content leaking into a batch.
/// </summary>
public class TelemetryPayloadTests
{
    private static TelemetryEnvironment Environment() => new()
    {
        AppVersion = "4.0.0-preview.4",
        Channel = "preview",
        SelfContained = true,
        OsRelease = "10.0.22631",
        Arch = "x64",
        Locale = "zh-Hans",
    };

    private static TelemetryRun Run() => new()
    {
        InstallId = new string('a', 32),
        StartedAt = new DateTimeOffset(2026, 10, 29, 23, 0, 0, TimeSpan.Zero),
        StoppedAt = new DateTimeOffset(2026, 10, 30, 0, 0, 0, TimeSpan.Zero),
        StartupMs = 240,
        Reason = TelemetryStopReason.Crash,
    };

    [Fact]
    public void Payload_HasExactlyTheWhitelistedTopLevelFields()
    {
        var json = TelemetryPayload.Serialize(
            new string('a', 32), Environment(), [Run()], DateTimeOffset.UtcNow);
        using var document = JsonDocument.Parse(json);
        var names = document.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray();

        Assert.Equal(
            new[] { "dropped", "environment", "installId", "runs", "schema", "sentAt" },
            names);
    }

    [Fact]
    public void Payload_HasExactlyTheWhitelistedEnvironmentAndRunFields()
    {
        var json = TelemetryPayload.Serialize(
            new string('a', 32), Environment(), [Run()], DateTimeOffset.UtcNow);
        using var document = JsonDocument.Parse(json);

        var environment = document.RootElement.GetProperty("environment").EnumerateObject()
            .Select(p => p.Name).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "arch", "channel", "locale", "os", "selfContained", "version" }, environment);

        var run = document.RootElement.GetProperty("runs")[0].EnumerateObject()
            .Select(p => p.Name).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "firstRun", "reason", "startedAt", "startupMs", "uptimeSeconds" }, run);
    }

    [Fact]
    public void Payload_ReportsClampedUptimeAndStopReason()
    {
        var run = Run() with { StoppedAt = Run().StartedAt.AddDays(30) };
        var json = TelemetryPayload.Serialize(new string('a', 32), Environment(), [run], DateTimeOffset.UtcNow);
        using var document = JsonDocument.Parse(json);
        var first = document.RootElement.GetProperty("runs")[0];

        Assert.Equal(TelemetryRun.MaxUptimeSeconds, first.GetProperty("uptimeSeconds").GetInt32());
        Assert.Equal("crash", first.GetProperty("reason").GetString());
        Assert.Equal(240, first.GetProperty("startupMs").GetInt32());
    }

    [Fact]
    public void Payload_NeverExceedsTheTransportBudget()
    {
        var runs = Enumerable.Range(0, 32).Select(i => Run() with { StartedAt = Run().StartedAt.AddMinutes(i) }).ToArray();
        var json = TelemetryPayload.Serialize(new string('a', 32), Environment(), runs, DateTimeOffset.UtcNow);

        Assert.True(json.Length < TelemetryPayload.MaxBytes, $"payload was {json.Length} bytes");
    }

    [Fact]
    public void Payload_SerialisesEmptyBatchWithoutRuns()
    {
        var json = TelemetryPayload.Serialize(new string('a', 32), Environment(), [], DateTimeOffset.UtcNow);
        using var document = JsonDocument.Parse(json);

        Assert.Equal(0, document.RootElement.GetProperty("runs").GetArrayLength());
        Assert.Equal(TelemetryPayload.SchemaVersion, document.RootElement.GetProperty("schema").GetInt32());
    }
}
