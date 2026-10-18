using System.Collections.Immutable;
using System.Text.Json;

namespace FluidBar.App.Localization;

/// <summary>
/// Parses one JSON locale catalog (spec 7.1). The parser is strict about shape (locale tag,
/// message dictionary) and tolerant of forward-compatible metadata (<c>$schema</c>, <c>_meta</c>).
/// </summary>
public static class JsonCatalogParser
{
    /// <summary>Parses <paramref name="json"/> into a catalog; throws <see cref="FormatException"/> when invalid.</summary>
    /// <param name="json">Catalog document text.</param>
    /// <param name="expectedLocale">Optional locale assertion (file name/locale mismatch is an error).</param>
    public static LocalizationCatalog Parse(string json, string? expectedLocale = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
        }
        catch (JsonException ex)
        {
            throw new FormatException($"Locale catalog is not valid JSON: {ex.Message}", ex);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("Locale catalog root must be a JSON object.");
            }

            if (!root.TryGetProperty("locale", out var localeElement) || localeElement.ValueKind != JsonValueKind.String)
            {
                throw new FormatException("Locale catalog is missing a string 'locale' property.");
            }

            var locale = localeElement.GetString()!.Trim();
            if (locale.Length == 0)
            {
                throw new FormatException("Locale catalog 'locale' must not be empty.");
            }

            if (expectedLocale is not null && !string.Equals(expectedLocale, locale, StringComparison.OrdinalIgnoreCase))
            {
                throw new FormatException($"Locale catalog says '{locale}' but was loaded as '{expectedLocale}'.");
            }

            var version = 1;
            if (root.TryGetProperty("version", out var versionElement) && versionElement.ValueKind == JsonValueKind.Number)
            {
                version = versionElement.GetInt32();
            }

            if (!root.TryGetProperty("messages", out var messagesElement) || messagesElement.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("Locale catalog is missing an object 'messages' property.");
            }

            var builder = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
            foreach (var property in messagesElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    throw new FormatException($"Locale message '{property.Name}' must be a string.");
                }

                var key = property.Name;
                ValidateKey(key);
                builder[key] = property.Value.GetString() ?? string.Empty;
            }

            return new LocalizationCatalog(locale, version, builder.ToImmutable());
        }
    }

    /// <summary>Validates the <c>Area.SubArea.Element</c> key convention (1–4 segments, no whitespace).</summary>
    public static void ValidateKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var segments = key.Split('.');
        if (segments.Length is < 1 or > 4)
        {
            throw new FormatException($"Locale key '{key}' must have 1–4 dot-separated segments.");
        }

        foreach (var segment in segments)
        {
            if (segment.Length == 0 || segment.Any(char.IsWhiteSpace))
            {
                throw new FormatException($"Locale key '{key}' has an invalid segment.");
            }
        }
    }
}
