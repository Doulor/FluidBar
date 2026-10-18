using System.Collections.Immutable;
using System.Globalization;

namespace FluidBar.Sdk.Localization;

/// <summary>
/// Cross-locale validation used by the plugin host, the marketplace packager and contributor tooling:
/// placeholder parity against the default bundle and unknown/extra keys. The same implementation runs in CI
/// and on the client, so validation cannot drift.
/// </summary>
public static class LocaleBundleConsistency
{
    /// <summary>Returns human-readable messages for placeholder/key mismatches against the default bundle.</summary>
    public static IReadOnlyList<string> FindPlaceholderMismatches(LocaleBundle defaultBundle, LocaleBundle translatedBundle)
    {
        ArgumentNullException.ThrowIfNull(defaultBundle);
        ArgumentNullException.ThrowIfNull(translatedBundle);

        var mismatches = new List<string>();
        var defaultKeys = new HashSet<string>(defaultBundle.Messages.Keys, StringComparer.Ordinal);
        foreach (var key in defaultKeys)
        {
            if (!translatedBundle.Messages.ContainsKey(key))
            {
                mismatches.Add($"Locale '{translatedBundle.Locale}' is missing message '{key}'.");
                continue;
            }

            var expected = MessageFormat.ExtractPlaceholders(defaultBundle.Messages[key])
                .Select(static placeholder => (placeholder.Name, placeholder.Kind))
                .ToImmutableHashSet();
            var actual = MessageFormat.ExtractPlaceholders(translatedBundle.Messages[key])
                .Select(static placeholder => (placeholder.Name, placeholder.Kind))
                .ToImmutableHashSet();
            if (!expected.SetEquals(actual))
            {
                mismatches.Add(
                    $"Locale '{translatedBundle.Locale}' message '{key}' has different placeholders than '{defaultBundle.Locale}'.");
            }
        }

        foreach (var key in translatedBundle.Messages.Keys)
        {
            if (!defaultKeys.Contains(key))
            {
                mismatches.Add($"Locale '{translatedBundle.Locale}' contains message '{key}' which is not in '{defaultBundle.Locale}'.");
            }
        }

        return mismatches;
    }

    /// <summary>Formats a value using the locale's culture (helper for host UI code).</summary>
    public static string FormatValue(object? value, string? locale) =>
        value switch
        {
            null => string.Empty,
            string text => text,
            IFormattable formattable => formattable.ToString(format: null, ResolveCulture(locale)) ?? string.Empty,
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        };

    private static CultureInfo ResolveCulture(string? locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
        {
            return CultureInfo.InvariantCulture;
        }

        try
        {
            return CultureInfo.GetCultureInfo(locale);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }
}
