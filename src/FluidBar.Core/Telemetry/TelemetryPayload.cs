using System.Text.Json;

namespace FluidBar.Core.Telemetry;

/// <summary>
/// Serialises one upload batch.
/// </summary>
/// <remarks>
/// The writer is hand-rolled on purpose: the wire format is a closed whitelist of properties, so a future field
/// added to <see cref="TelemetryRun"/> cannot leak into the payload by accident. Any change here must also update
/// docs/privacy/telemetry.md and the Worker validator.
/// </remarks>
public static class TelemetryPayload
{
    /// <summary>Payload schema version; the Worker rejects anything else.</summary>
    public const int SchemaVersion = 1;

    /// <summary>Largest batch the sink is allowed to send (the Worker rejects bigger bodies).</summary>
    public const int MaxBytes = 32 * 1024;

    /// <summary>Serialises the environment plus a batch of runs into the wire format.</summary>
    /// <param name="installId">Anonymous install identifier shared by every run in the batch.</param>
    /// <param name="environment">Machine/build description.</param>
    /// <param name="runs">Sessions to report.</param>
    /// <param name="sentAt">Batch timestamp (UTC).</param>
    /// <param name="dropped">How many older runs the local queue had to drop (back-pressure signal).</param>
    /// <returns>UTF-8 JSON body, never larger than <see cref="MaxBytes"/>.</returns>
    public static byte[] Serialize(
        string installId,
        TelemetryEnvironment environment,
        IReadOnlyList<TelemetryRun> runs,
        DateTimeOffset sentAt,
        int dropped = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installId);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(runs);

        using var buffer = new MemoryStream(1024);
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema", SchemaVersion);
            writer.WriteString("installId", installId);
            writer.WriteString("sentAt", sentAt.ToUniversalTime());
            writer.WriteStartObject("environment");
            writer.WriteString("version", environment.AppVersion);
            writer.WriteString("channel", environment.Channel);
            writer.WriteBoolean("selfContained", environment.SelfContained);
            writer.WriteString("os", environment.OsRelease);
            writer.WriteString("arch", environment.Arch);
            writer.WriteString("locale", environment.Locale);
            writer.WriteEndObject();
            writer.WriteStartArray("runs");
            foreach (var run in runs)
            {
                if (run is null)
                {
                    continue;
                }

                writer.WriteStartObject();
                writer.WriteString("startedAt", run.StartedAt.ToUniversalTime());
                writer.WriteNumber("uptimeSeconds", run.UptimeSeconds);
                writer.WriteNumber("startupMs", Math.Max(0, run.StartupMs));
                writer.WriteBoolean("firstRun", run.FirstRun);
                writer.WriteString("reason", run.Reason switch
                {
                    TelemetryStopReason.System => "system",
                    TelemetryStopReason.Crash => "crash",
                    _ => "user",
                });
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteNumber("dropped", Math.Max(0, dropped));
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }
}
