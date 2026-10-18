using System.Collections.Immutable;
using System.Globalization;
using FluidBar.PluginHost.Logging;
using FluidBar.Sdk;
using FluidBar.Sdk.Localization;
using FluidBar.Sdk.Manifest;

namespace FluidBar.PluginHost.Runtime;

/// <summary>Raised when a plugin declares locales but the required default bundle is missing or invalid.</summary>
public sealed class PluginLocalizationException : Exception
{
    public PluginLocalizationException(string message)
        : base(message)
    {
    }

    public PluginLocalizationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Plugin localization implementation: loads <c>locales/&lt;bcp47&gt;.json</c> bundles from the signed package
/// and resolves messages through the documented fallback chain. Missing keys are logged once per key with a
/// bounded dedupe set; a missing/broken default bundle fails the plugin load instead of showing raw keys.
/// </summary>
internal sealed class PluginLocalizer : IPluginLocalization
{
    private const int MaxLoggedMissingKeys = 512;
    private const int MaxLoggedFormatErrors = 256;

    private readonly string _pluginId;
    private readonly IPluginHostLogger _logger;
    private readonly MessageCatalog _catalog;
    private readonly HashSet<string> _loggedMissingKeys = new(StringComparer.Ordinal);
    private readonly HashSet<string> _loggedFormatErrors = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    internal PluginLocalizer(
        string pluginId,
        IPluginHostLogger logger,
        MessageCatalog catalog,
        string currentLocale)
    {
        _pluginId = pluginId;
        _logger = logger;
        _catalog = catalog;
        CurrentLocale = currentLocale;
    }

    public string CurrentLocale { get; }

    public string DefaultLocale => _catalog.DefaultLocale;

    public IReadOnlyList<string> AvailableLocales => _catalog.AvailableLocales;

    /// <summary>Loads and validates the locale bundles for a plugin package.</summary>
    internal static PluginLocalizer Create(
        string pluginId,
        string packageDirectory,
        PluginManifest manifest,
        IReadOnlyList<string> hostUiLocales,
        IPluginHostLogger logger,
        CancellationToken cancellationToken = default)
    {
        var locales = manifest.Locales ?? throw new PluginLocalizationException("Manifest has no locales block.");
        var relativeDirectory = string.IsNullOrWhiteSpace(locales.Path) ? "locales" : locales.Path.TrimEnd('/');
        if (!relativeDirectory.Equals("locales", StringComparison.OrdinalIgnoreCase))
        {
            throw new PluginLocalizationException($"locales.path '{locales.Path}' must be 'locales'.");
        }

        var localesDirectory = Path.Combine(packageDirectory, "locales");
        var declaredLocales = (locales.Available ?? []).ToList();
        if (declaredLocales.Count == 0)
        {
            throw new PluginLocalizationException("Manifest declares no available locales.");
        }

        if (declaredLocales.Count > LocaleBundleLimits.MaxLocalesPerPlugin)
        {
            throw new PluginLocalizationException($"Manifest declares more than {LocaleBundleLimits.MaxLocalesPerPlugin} locales.");
        }

        if (!declaredLocales.Contains(locales.Default, StringComparer.OrdinalIgnoreCase))
        {
            throw new PluginLocalizationException($"Default locale '{locales.Default}' is not in locales.available.");
        }

        var bundles = new List<LocaleBundle>(declaredLocales.Count);
        foreach (var locale in declaredLocales)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(localesDirectory, locale + ".json");
            if (!File.Exists(path))
            {
                if (string.Equals(locale, locales.Default, StringComparison.OrdinalIgnoreCase))
                {
                    throw new PluginLocalizationException($"Required default locale bundle 'locales/{locale}.json' is missing.");
                }

                logger.Warn(pluginId, $"Declared locale bundle 'locales/{locale}.json' is missing; the locale will be ignored.");
                continue;
            }

            var info = new FileInfo(path);
            if (info.Length > LocaleBundleLimits.MaxBundleBytes)
            {
                throw new PluginLocalizationException($"Locale bundle 'locales/{locale}.json' exceeds {LocaleBundleLimits.MaxBundleBytes} bytes.");
            }

            var json = File.ReadAllText(path);
            if (!LocaleBundleLoader.TryParse(json, out var bundle, out var errors) || bundle is null)
            {
                if (string.Equals(locale, locales.Default, StringComparison.OrdinalIgnoreCase))
                {
                    throw new PluginLocalizationException($"Default locale bundle is invalid: {string.Join("; ", errors)}");
                }

                logger.Warn(pluginId, $"Locale bundle 'locales/{locale}.json' is invalid and will be ignored: {string.Join("; ", errors)}");
                continue;
            }

            if (!string.Equals(bundle.Locale, locale, StringComparison.OrdinalIgnoreCase))
            {
                logger.Warn(pluginId, $"Locale bundle 'locales/{locale}.json' declares locale '{bundle.Locale}'.");
            }

            bundles.Add(bundle);
        }

        var fallbackLocales = hostUiLocales is { Count: > 0 } ? hostUiLocales : ["en-US"];
        MessageCatalog catalog;
        try
        {
            catalog = new MessageCatalog(locales.Default, bundles, fallbackLocales);
        }
        catch (ArgumentException exception)
        {
            throw new PluginLocalizationException($"Plugin locale catalog is invalid: {exception.Message}", exception);
        }

        var currentLocale = fallbackLocales[0];
        ValidatePlaceholderParity(pluginId, catalog, locales.Default, logger);
        return new PluginLocalizer(pluginId, logger, catalog, currentLocale);
    }

    private static void ValidatePlaceholderParity(
        string pluginId,
        MessageCatalog catalog,
        string defaultLocale,
        IPluginHostLogger logger)
    {
        var defaultBundle = catalog.GetBundle(defaultLocale);
        if (defaultBundle is null)
        {
            return;
        }

        foreach (var locale in catalog.AvailableLocales)
        {
            if (string.Equals(locale, defaultLocale, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var bundle = catalog.GetBundle(locale);
            if (bundle is null)
            {
                continue;
            }

            var mismatches = LocaleBundleConsistency.FindPlaceholderMismatches(defaultBundle, bundle);
            foreach (var mismatch in mismatches)
            {
                logger.Warn(pluginId, mismatch);
            }
        }
    }

    public string Get(string key)
    {
        if (_catalog.TryGet(key, CurrentLocale, out var value))
        {
            return value;
        }

        LogMissingKey(key);
        return key;
    }

    public bool TryGet(string key, out string value) => _catalog.TryGet(key, CurrentLocale, out value);

    public string Format(string key, IReadOnlyDictionary<string, object?> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var template = Get(key);
        if (!MessageFormat.TryFormat(template, arguments, CurrentLocale, out var formatted, out var errors))
        {
            LogFormatError(key, errors);
            return template;
        }

        return formatted;
    }

    public string Plural(string key, long count) => Format(key, new Dictionary<string, object?>(StringComparer.Ordinal) { ["count"] = count });

    private void LogMissingKey(string key)
    {
        lock (_gate)
        {
            if (_loggedMissingKeys.Count >= MaxLoggedMissingKeys || !_loggedMissingKeys.Add(key))
            {
                return;
            }
        }

        _logger.Warn(_pluginId, $"Missing translation key '{key}' for locale '{CurrentLocale}'; showing the key.");
    }

    private void LogFormatError(string key, IReadOnlyList<string> errors)
    {
        lock (_gate)
        {
            if (_loggedFormatErrors.Count >= MaxLoggedFormatErrors || !_loggedFormatErrors.Add(key))
            {
                return;
            }
        }

        _logger.Warn(_pluginId, $"Message '{key}' could not be formatted: {string.Join("; ", errors)}");
    }
}
