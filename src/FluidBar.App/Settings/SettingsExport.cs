using System.Globalization;
using System.Text;
using System.Text.Json;

namespace FluidBar.App.Settings;

/// <summary>
/// Settings export/import document (spec §8.3):
/// <c>{"schemaVersion":2,"exportedAt":"...","settings":{...}}</c>.
/// Import accepts the envelope or a raw settings document, so older exports keep working.
/// </summary>
public static class SettingsExport
{
    /// <summary>Schema version of the export envelope.</summary>
    public const int SchemaVersion = 2;

    /// <summary>Builds the export document for a settings snapshot.</summary>
    /// <param name="settings">Settings to export.</param>
    /// <param name="exportedAt">Export timestamp (tests inject it).</param>
    public static string Create(AppSettings settings, DateTimeOffset exportedAt)
    {
        ArgumentNullException.ThrowIfNull(settings);
        using var settingsDocument = JsonDocument.Parse(settings.ToJson());
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", SchemaVersion);
            writer.WriteString("exportedAt", exportedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            writer.WritePropertyName("settings");
            settingsDocument.RootElement.WriteTo(writer);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// Parses an export file or raw settings document. Returns false with a human-readable error for
    /// syntactically invalid JSON or a settings payload that cannot be used.
    /// </summary>
    /// <param name="text">File contents.</param>
    /// <param name="settings">Parsed settings on success.</param>
    /// <param name="error">Failure reason on failure.</param>
    public static bool TryParse(string text, out AppSettings? settings, out string? error)
    {
        settings = null;
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "The settings file is empty.";
            return false;
        }

        string payload = text;
        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("settings", out var inner)
                && inner.ValueKind == JsonValueKind.Object)
            {
                payload = inner.GetRawText();
            }
        }
        catch (JsonException ex)
        {
            error = "The settings file is not valid JSON: " + ex.Message;
            return false;
        }

        settings = AppSettings.TryParse(payload, out var parseError);
        if (settings is null)
        {
            error = parseError ?? "The settings payload is unusable.";
            return false;
        }

        return true;
    }
}
