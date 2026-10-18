using System.Collections.Immutable;
using System.Text.Json;
using FluidBar.Sdk.Json;

namespace FluidBar.Sdk.Localization;

/// <summary>A parsed <c>locales/&lt;bcp47&gt;.json</c> bundle shipped inside a plugin package.</summary>
public sealed record LocaleBundle
{
    [System.Text.Json.Serialization.JsonPropertyName("$schema")]
    public string? Schema { get; init; }

    public string Locale { get; init; } = string.Empty;

    public int Version { get; init; } = 1;

    public IReadOnlyDictionary<string, string> Messages { get; init; } =
        ImmutableDictionary.Create<string, string>(StringComparer.Ordinal);
}

/// <summary>Limits that keep a hostile or accidentally huge locale bundle bounded.</summary>
public static class LocaleBundleLimits
{
    public const int MaxBundleBytes = 512 * 1024;
    public const int MaxMessagesPerBundle = 10_000;
    public const int MaxMessageKeyLength = 128;
    public const int MaxMessageValueLength = 4_000;
    public const int MaxLocalesPerPlugin = 64;
    public const int MaxCatalogEntries = 200_000;
}

/// <summary>Thrown when a locale bundle is malformed.</summary>
public sealed class LocaleBundleException : Exception
{
    public LocaleBundleException(string message)
        : base(message)
    {
    }

    public LocaleBundleException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Parses, validates and serializes plugin locale bundles (JSON dictionaries, en-US required by policy).</summary>
public static class LocaleBundleLoader
{
    private static readonly JsonSerializerOptions Options = PluginJson.CreateStrictOptions();

    /// <summary>Parses a locale bundle; throws <see cref="LocaleBundleException"/> with a readable reason.</summary>
    public static LocaleBundle Parse(string json) =>
        TryParse(json, out var bundle, out var errors) && bundle is not null
            ? bundle
            : throw new LocaleBundleException($"Invalid locale bundle: {string.Join("; ", errors)}");

    /// <summary>Parses a locale bundle without throwing.</summary>
    public static bool TryParse(string json, out LocaleBundle? bundle, out IReadOnlyList<string> errors)
    {
        bundle = null;
        var errorList = new List<string>();
        if (string.IsNullOrWhiteSpace(json))
        {
            errors = ["Locale bundle is empty."];
            return false;
        }

        if (json.Length > LocaleBundleLimits.MaxBundleBytes)
        {
            errors = [$"Locale bundle exceeds {LocaleBundleLimits.MaxBundleBytes} characters."];
            return false;
        }

        LocaleBundle? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<LocaleBundle>(json, Options);
        }
        catch (JsonException exception)
        {
            errors = [$"Locale bundle is not valid JSON: {exception.Message}"];
            return false;
        }

        if (parsed is null)
        {
            errors = ["Locale bundle deserialized to null."];
            return false;
        }

        if (!LocaleTags.IsValid(parsed.Locale))
        {
            errorList.Add($"locale '{parsed.Locale}' is not a canonical BCP-47 tag.");
        }

        if (parsed.Version != 1)
        {
            errorList.Add($"Unsupported locale bundle version {parsed.Version}; expected 1.");
        }

        if (parsed.Messages is null || parsed.Messages.Count == 0)
        {
            errorList.Add("messages must contain at least one entry.");
        }
        else
        {
            if (parsed.Messages.Count > LocaleBundleLimits.MaxMessagesPerBundle)
            {
                errorList.Add($"messages exceeds the {LocaleBundleLimits.MaxMessagesPerBundle} entry limit.");
            }

            foreach (var pair in parsed.Messages)
            {
                if (pair.Key.Length is 0 or > LocaleBundleLimits.MaxMessageKeyLength ||
                    !char.IsAsciiLetter(pair.Key[0]) ||
                    pair.Key.Any(static character => !(char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-')))
                {
                    errorList.Add($"message key '{pair.Key}' is not a valid key.");
                }

                if (string.IsNullOrWhiteSpace(pair.Value))
                {
                    errorList.Add($"message '{pair.Key}' has an empty value.");
                }
                else if (pair.Value.Length > LocaleBundleLimits.MaxMessageValueLength)
                {
                    errorList.Add($"message '{pair.Key}' exceeds {LocaleBundleLimits.MaxMessageValueLength} characters.");
                }
                else if (!MessageFormat.TryValidate(pair.Value, out var formatErrors))
                {
                    errorList.Add($"message '{pair.Key}' has an invalid ICU template: {string.Join(", ", formatErrors)}");
                }
            }
        }

        errors = errorList;
        if (errorList.Count > 0)
        {
            return false;
        }

        bundle = parsed;
        return true;
    }

    /// <summary>Serializes a bundle deterministically (sorted keys) so packages/tests can hash it.</summary>
    public static string Serialize(LocaleBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        using var stream = new MemoryStream();
        using (var writer = new System.Text.Json.Utf8JsonWriter(stream, new System.Text.Json.JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            if (bundle.Schema is not null)
            {
                writer.WriteString("$schema", bundle.Schema);
            }

            writer.WriteString("locale", bundle.Locale);
            writer.WriteNumber("version", bundle.Version);
            writer.WriteStartObject("messages");
            foreach (var pair in bundle.Messages.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                writer.WriteString(pair.Key, pair.Value);
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }
}

/// <summary>
/// Immutable, plugin-scoped message catalog. Resolution order (from docs/rewrite/contracts/plugin.md):
/// requested locale exact → each parent tag → host/app fallback locales → plugin default locale → key.
/// Missing keys never throw; the host logs them once per key and shows the default-locale value.
/// </summary>
public sealed class MessageCatalog
{
    private readonly ImmutableDictionary<string, ImmutableDictionary<string, string>> _messagesByLocale;
    private readonly ImmutableArray<string> _fallbackChain;

    public MessageCatalog(
        string defaultLocale,
        IEnumerable<LocaleBundle> bundles,
        IEnumerable<string>? hostFallbackLocales = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultLocale);
        ArgumentNullException.ThrowIfNull(bundles);

        DefaultLocale = defaultLocale;
        var localeMap = new Dictionary<string, ImmutableDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var bundle in bundles)
        {
            if (!localeMap.TryAdd(bundle.Locale, bundle.Messages.ToImmutableDictionary(StringComparer.Ordinal)))
            {
                throw new ArgumentException($"Duplicate locale bundle '{bundle.Locale}'.", nameof(bundles));
            }

            if (localeMap.Count > LocaleBundleLimits.MaxLocalesPerPlugin)
            {
                throw new ArgumentException($"A plugin may ship at most {LocaleBundleLimits.MaxLocalesPerPlugin} locales.", nameof(bundles));
            }
        }

        if (!localeMap.ContainsKey(defaultLocale))
        {
            throw new ArgumentException($"Default locale '{defaultLocale}' has no bundle.", nameof(bundles));
        }

        _messagesByLocale = localeMap.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase);
        AvailableLocales = localeMap.Keys.OrderBy(static locale => locale, StringComparer.OrdinalIgnoreCase).ToImmutableArray();
        _fallbackChain = (hostFallbackLocales ?? []).Where(static locale => !string.IsNullOrWhiteSpace(locale))
            .SelectMany(static locale => LocaleTags.EnumerateParents(locale.Trim()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray();
    }

    public string DefaultLocale { get; }

    public ImmutableArray<string> AvailableLocales { get; }

    /// <summary>Resolves a key for the requested locale; returns the key itself when nothing matches.</summary>
    public string Get(string key, string? locale) =>
        TryGet(key, locale, out var value) ? value : key;

    /// <summary>Resolves a key and formats it with the ICU subset.</summary>
    public string Format(string key, IReadOnlyDictionary<string, object?> arguments, string? locale)
    {
        var template = Get(key, locale);
        if (!MessageFormat.TryFormat(template, arguments, locale, out var formatted, out _))
        {
            // A malformed translation must not break the host: show the raw template (already the value).
            return template;
        }

        return formatted;
    }

    /// <summary>Attempts to resolve a message, applying the documented fallback chain.</summary>
    public bool TryGet(string key, string? locale, out string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        foreach (var candidate in LocaleTags.EnumerateCandidates(locale))
        {
            if (TryGetFromLocale(candidate, key, out value))
            {
                return true;
            }
        }

        foreach (var candidate in _fallbackChain)
        {
            if (TryGetFromLocale(candidate, key, out value))
            {
                return true;
            }
        }

        if (TryGetFromLocale(DefaultLocale, key, out value))
        {
            return true;
        }

        value = key;
        return false;
    }

    /// <summary>Returns the bundle for an exact locale, if present.</summary>
    public LocaleBundle? GetBundle(string locale) =>
        _messagesByLocale.TryGetValue(locale, out var messages)
            ? new LocaleBundle { Locale = locale, Messages = messages }
            : null;

    private bool TryGetFromLocale(string locale, string key, out string value)
    {
        if (_messagesByLocale.TryGetValue(locale, out var messages) && messages.TryGetValue(key, out var message))
        {
            value = message;
            return true;
        }

        value = string.Empty;
        return false;
    }
}
