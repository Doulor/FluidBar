using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluidBar.Sdk.Localization;

namespace FluidBar.Sdk.Manifest;

/// <summary>
/// A localizable manifest string. The wire form is either a plain string (default locale) or an object
/// mapping BCP-47 tags to translations. The marketplace index generator reads the object form; runtime UI
/// strings should prefer message keys so translators can own the bundle.
/// </summary>
public sealed class LocalizedString : IEquatable<LocalizedString>
{
    private static readonly ImmutableDictionary<string, string> EmptyTranslations =
        ImmutableDictionary.Create<string, string>(StringComparer.OrdinalIgnoreCase);

    private readonly ImmutableDictionary<string, string> _translations;

    public LocalizedString(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
        _translations = EmptyTranslations;
    }

    public LocalizedString(IReadOnlyDictionary<string, string> translations)
    {
        ArgumentNullException.ThrowIfNull(translations);
        if (translations.Count == 0)
        {
            throw new ArgumentException("At least one translation is required.", nameof(translations));
        }

        _translations = translations.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase);
        Value = null;
    }

    private LocalizedString(ImmutableDictionary<string, string> translations, string? value)
    {
        _translations = translations;
        Value = value;
    }

    /// <summary>The untagged string form, when the manifest used a plain string.</summary>
    public string? Value { get; }

    /// <summary>The locale map form, when the manifest used an object. Empty for the plain string form.</summary>
    public IReadOnlyDictionary<string, string> Translations => _translations;

    public bool IsEmpty => Value is null && _translations.Count == 0;

    public IReadOnlyCollection<string> AvailableLocales => _translations.Keys.ToArray();

    /// <summary>Creates a plain string localized value.</summary>
    public static LocalizedString From(string value) => new(value);

    /// <summary>Creates a locale-map localized value.</summary>
    public static LocalizedString FromMap(IReadOnlyDictionary<string, string> translations) => new(translations);

    /// <summary>
    /// Resolves the best value for <paramref name="locale"/> using the exact tag, its parent tags, the
    /// supplied fallback chain and finally the first available translation. Never throws.
    /// </summary>
    public string Resolve(string? locale, params string?[] fallbackLocales)
    {
        if (Value is not null)
        {
            return Value;
        }

        if (_translations.Count == 0)
        {
            return string.Empty;
        }

        foreach (var candidate in LocaleTags.EnumerateCandidates(locale, fallbackLocales))
        {
            if (_translations.TryGetValue(candidate, out var translated))
            {
                return translated;
            }
        }

        // Source-language preference keeps manifest metadata stable across locales.
        if (_translations.TryGetValue("en-US", out var sourceLanguage))
        {
            return sourceLanguage;
        }

        return _translations.Values.First();
    }

    /// <summary>Returns <see langword="true"/> when a translation exists for the exact tag or a parent tag.</summary>
    public bool HasTranslationFor(string? locale)
    {
        foreach (var candidate in LocaleTags.EnumerateCandidates(locale))
        {
            if (_translations.ContainsKey(candidate))
            {
                return true;
            }
        }

        return false;
    }

    public bool Equals(LocalizedString? other)
    {
        if (other is null)
        {
            return false;
        }

        if (!string.Equals(Value, other.Value, StringComparison.Ordinal))
        {
            return false;
        }

        if (_translations.Count != other._translations.Count)
        {
            return false;
        }

        foreach (var pair in _translations)
        {
            if (!other._translations.TryGetValue(pair.Key, out var otherValue) ||
                !string.Equals(pair.Value, otherValue, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as LocalizedString);

    public override int GetHashCode()
    {
        if (Value is not null)
        {
            return HashCode.Combine(Value);
        }

        var hash = new HashCode();
        foreach (var pair in _translations)
        {
            hash.Add(pair.Key, StringComparer.OrdinalIgnoreCase);
            hash.Add(pair.Value, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    public override string ToString() => Value ?? string.Join(", ", _translations.Select(static pair => $"{pair.Key}={pair.Value}"));
}

/// <summary>JSON converter for the <see cref="LocalizedString"/> string-or-map wire form.</summary>
public sealed class LocalizedStringJsonConverter : JsonConverter<LocalizedString>
{
    public override LocalizedString Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                var value = reader.GetString();
                if (string.IsNullOrWhiteSpace(value))
                {
                    throw new JsonException("Localized string must not be empty.");
                }

                return LocalizedString.From(value);
            case JsonTokenType.StartObject:
            {
                var translations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject)
                    {
                        break;
                    }

                    if (reader.TokenType != JsonTokenType.PropertyName)
                    {
                        throw new JsonException("Expected a locale property name in a localized string object.");
                    }

                    var locale = reader.GetString() ?? throw new JsonException("Locale tag must not be null.");
                    if (!reader.Read() || reader.TokenType != JsonTokenType.String)
                    {
                        throw new JsonException($"Localized string for locale '{locale}' must be a string.");
                    }

                    var translated = reader.GetString();
                    if (string.IsNullOrWhiteSpace(translated))
                    {
                        throw new JsonException($"Localized string for locale '{locale}' must not be empty.");
                    }

                    translations[locale] = translated;
                }

                if (translations.Count == 0)
                {
                    throw new JsonException("Localized string locale map must contain at least one entry.");
                }

                return LocalizedString.FromMap(translations);
            }

            default:
                throw new JsonException($"Expected a string or an object for a localized string, got {reader.TokenType}.");
        }
    }

    public override void Write(Utf8JsonWriter writer, LocalizedString value, JsonSerializerOptions options)
    {
        if (value.Value is not null)
        {
            writer.WriteStringValue(value.Value);
            return;
        }

        writer.WriteStartObject();
        foreach (var pair in value.Translations.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            writer.WriteString(pair.Key, pair.Value);
        }

        writer.WriteEndObject();
    }
}
