using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Avalonia.Platform;

namespace FluidBar.App.Localization;

/// <summary>
/// JSON-catalog localization service (spec 7.1/7.2/7.6): exact → parent → <c>en-US</c> → key fallback,
/// runtime culture switch without restart (raises <see cref="Changed"/> and bumps <see cref="Version"/>),
/// ICU subset formatting, and a generated <c>qps-ploc</c> pseudo-locale.
/// </summary>
/// <remarks>
/// No I/O happens on the UI thread: <see cref="CreateDefault"/> reads the embedded <c>avares://</c>
/// catalogs once during composition (startup), and <see cref="Reload"/> is explicit.
/// </remarks>
public sealed class LocalizationService : ILocalizer, IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, LocalizationCatalog> _catalogs;
    private readonly bool _showMissingKeys;
    private IReadOnlyList<string> _availableCultures;
    private string _culture;
    private int _version;
    private bool _disposed;

    /// <summary>Creates a service over already-parsed catalogs (tests/plugin catalogs).</summary>
    /// <param name="catalogs">Locale catalogs; at least <c>en-US</c> must be present.</param>
    /// <param name="initialCulture">Initial culture tag, or <see langword="null"/> for <c>en-US</c>.</param>
    /// <param name="showMissingKeys">When true, unresolved keys render as <c>⟦key⟧</c> (Debug visibility).</param>
    public LocalizationService(
        IEnumerable<LocalizationCatalog> catalogs,
        string? initialCulture = null,
        bool showMissingKeys = false)
    {
        ArgumentNullException.ThrowIfNull(catalogs);
        _catalogs = new Dictionary<string, LocalizationCatalog>(StringComparer.OrdinalIgnoreCase);
        foreach (var catalog in catalogs)
        {
            ArgumentNullException.ThrowIfNull(catalog);
            _catalogs[catalog.Locale] = catalog;
        }

        if (!_catalogs.ContainsKey(Locales.Source))
        {
            throw new ArgumentException($"At least the '{Locales.Source}' catalog is required.", nameof(catalogs));
        }

        _showMissingKeys = showMissingKeys;
        _availableCultures = BuildAvailableCultures();
        _culture = ResolveCulture(initialCulture);
        ApplyThreadCultures(_culture);
    }

    /// <inheritdoc />
    public string Culture
    {
        get
        {
            lock (_gate)
            {
                return _culture;
            }
        }
    }

    /// <inheritdoc />
    public int Version
    {
        get
        {
            lock (_gate)
            {
                return _version;
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public IReadOnlyList<string> AvailableCultures
    {
        get
        {
            lock (_gate)
            {
                return _availableCultures;
            }
        }
    }

    /// <inheritdoc />
    public bool IsRightToLeft => ResolveCultureInfo(Culture).TextInfo.IsRightToLeft;

    /// <summary>The resolved <see cref="CultureInfo"/> for the active UI culture.</summary>
    public CultureInfo CurrentCulture => ResolveCultureInfo(Culture);


    /// <summary>Whether unresolved keys are rendered with pseudo-locale brackets.</summary>
    public bool ShowMissingKeys => _showMissingKeys;

    /// <summary>
    /// Loads the embedded app catalogs from <c>avares://FluidBar/Localization/&lt;locale&gt;.json</c>,
    /// generates the pseudo-locale from <c>en-US</c>, and returns a ready service.
    /// </summary>
    /// <param name="initialCulture">Initial culture; <see langword="null"/> resolves the system UI culture.</param>
    /// <param name="showMissingKeys">Debug diagnostics for missing keys.</param>
    /// <param name="enablePseudoLocale">Whether <c>qps-ploc</c> is available (env flag/setting).</param>
    public static LocalizationService CreateDefault(
        string? initialCulture = null,
        bool showMissingKeys = false,
        bool enablePseudoLocale = false)
    {
        var catalogs = new List<LocalizationCatalog>
        {
            LoadEmbedded(Locales.Source),
            LoadEmbedded(Locales.ChineseSimplified),
        };
        if (enablePseudoLocale)
        {
            catalogs.Add(PseudoCatalogBuilder.Build(catalogs[0]));
        }

        return new LocalizationService(catalogs, initialCulture, showMissingKeys);
    }

    /// <summary>Loads one embedded catalog by locale tag (<c>avares://</c> asset).</summary>
    public static LocalizationCatalog LoadEmbedded(string locale)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locale);
        var uri = new Uri($"avares://FluidBar/Localization/{locale}.json");
        using var stream = AssetLoader.Open(uri);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return JsonCatalogParser.Parse(reader.ReadToEnd(), locale);
    }

    /// <summary>Switches the UI culture at runtime (no restart) and raises <see cref="Changed"/>.</summary>
    /// <returns>True when the resolved culture changed.</returns>
    public bool SetCulture(string? cultureTag)
    {
        var resolved = ResolveCulture(cultureTag);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (string.Equals(resolved, _culture, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            _culture = resolved;
            _version++;
        }

        ApplyThreadCultures(resolved);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Replaces all catalogs (hot reload/dev builds) and raises <see cref="Changed"/>.</summary>
    public void Reload(IEnumerable<LocalizationCatalog> catalogs)
    {
        ArgumentNullException.ThrowIfNull(catalogs);
        var replacement = new Dictionary<string, LocalizationCatalog>(StringComparer.OrdinalIgnoreCase);
        foreach (var catalog in catalogs)
        {
            ArgumentNullException.ThrowIfNull(catalog);
            replacement[catalog.Locale] = catalog;
        }

        if (!replacement.ContainsKey(Locales.Source))
        {
            throw new ArgumentException($"At least the '{Locales.Source}' catalog is required.", nameof(catalogs));
        }

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _catalogs.Clear();
            foreach (var pair in replacement)
            {
                _catalogs[pair.Key] = pair.Value;
            }

            _availableCultures = BuildAvailableCultures();
            _culture = ResolveCulture(_culture);
            _version++;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public string Get(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return string.Empty;
        }

        return TryResolve(key, out var value) ? value : MissingKey(key);
    }

    /// <inheritdoc />
    public string Format(string key, IReadOnlyDictionary<string, object?> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var template = Get(key);
        return MessageFormatter.Format(template, arguments, CurrentCulture);
    }

    /// <inheritdoc />
    public string Format(string key, params (string Name, object? Value)[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var template = Get(key);
        return MessageFormatter.Format(template, CurrentCulture, arguments);
    }

    /// <inheritdoc />
    public bool TryGet(string key, out string value)
    {
        if (string.IsNullOrEmpty(key))
        {
            value = string.Empty;
            return false;
        }

        return TryResolve(key, out value);
    }

    /// <inheritdoc />
    public string GetCultureDisplayName(string cultureTag)
    {
        var normalized = Locales.Normalize(cultureTag);
        var key = normalized switch
        {
            Locales.Source or "en" => "Locale.English",
            Locales.ChineseSimplified or "zh" => "Locale.Chinese",
            Locales.Pseudo => "Locale.Pseudo",
            _ => null,
        };

        if (key is not null && TryGet(key, out var localized))
        {
            return localized;
        }

        var culture = ResolveCultureInfo(normalized);
        return culture.Name.Length == 0 ? cultureTag : culture.NativeName;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _catalogs.Clear();
        }
    }

    private bool TryResolve(string key, out string value)
    {
        var chain = Locales.BuildFallbackChain(Culture);
        foreach (var locale in chain)
        {
            LocalizationCatalog? catalog;
            lock (_gate)
            {
                _catalogs.TryGetValue(locale, out catalog);
            }

            if (catalog is not null && catalog.Messages.TryGetValue(key, out var message))
            {
                value = message;
                return true;
            }
        }

        // Pseudo-locale: prefer the generated pseudo catalog, else fall through to en-US content.
        lock (_gate)
        {
            if (_catalogs.TryGetValue(Locales.Pseudo, out var pseudo)
                && pseudo.Messages.TryGetValue(key, out var pseudoMessage)
                && string.Equals(_culture, Locales.Pseudo, StringComparison.OrdinalIgnoreCase))
            {
                value = pseudoMessage;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    private string MissingKey(string key)
    {
        if (_showMissingKeys)
        {
            return $"\u27E6{key}\u27E7";
        }

        // Release: fall back to the source value, then the raw key (spec 7.2 fallback chain).
        lock (_gate)
        {
            if (_catalogs.TryGetValue(Locales.Source, out var source)
                && source.Messages.TryGetValue(key, out var sourceValue))
            {
                return sourceValue;
            }
        }

        return key;
    }

    private string ResolveCulture(string? requested)
    {
        var normalized = Locales.Normalize(requested);
        lock (_gate)
        {
            if (_catalogs.ContainsKey(normalized))
            {
                return normalized;
            }

            // Resolve to the closest catalog: zh-Hans-CN → zh-Hans, en-GB → en-US.
            if (normalized.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
                && _catalogs.ContainsKey(Locales.ChineseSimplified))
            {
                return Locales.ChineseSimplified;
            }
        }

        return Locales.Source;
    }

    private IReadOnlyList<string> BuildAvailableCultures()
    {
        var list = new List<string>(_catalogs.Count);
        if (_catalogs.ContainsKey(Locales.Source))
        {
            list.Add(Locales.Source);
        }

        foreach (var locale in _catalogs.Keys)
        {
            if (!string.Equals(locale, Locales.Source, StringComparison.OrdinalIgnoreCase))
            {
                list.Add(locale);
            }
        }

        return list;
    }

    private static CultureInfo ResolveCultureInfo(string tag)
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo(tag);
            return culture;
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.GetCultureInfo(Locales.Source);
        }
    }

    private static void ApplyThreadCultures(string cultureTag)
    {
        try
        {
            var culture = ResolveCultureInfo(cultureTag);
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            CultureInfo.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.CurrentCulture = culture;
        }
        catch (ArgumentException)
        {
            // Invalid culture tags are already normalized before this point; nothing to restore.
        }
    }
}

/// <summary>
/// Builds the <c>qps-ploc</c> pseudo-locale from <c>en-US</c> (spec 7.6): brackets every message,
/// accents vowels, grows word length by ~35 % and keeps ICU placeholders intact.
/// </summary>
public static class PseudoCatalogBuilder
{
    private const string Open = "\u27E6";
    private const string Close = "\u27E7";

    /// <summary>Derives a pseudo catalog from the source catalog.</summary>
    public static LocalizationCatalog Build(LocalizationCatalog source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var builder = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        foreach (var pair in source.Messages)
        {
            builder[pair.Key] = Wrap(pair.Value);
        }

        return new LocalizationCatalog(Locales.Pseudo, source.Version, builder.ToImmutable());
    }

    /// <summary>Pseudo-localizes one message, preserving ICU argument syntax.</summary>
    public static string Wrap(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return message;
        }

        var builder = new StringBuilder(message.Length + 8);
        builder.Append(Open).Append(' ');
        var argumentDepth = 0;
        var index = 0;
        while (index < message.Length)
        {
            var c = message[index];
            if (argumentDepth > 0)
            {
                builder.Append(c);
                if (c == '{')
                {
                    argumentDepth++;
                }
                else if (c == '}')
                {
                    argumentDepth--;
                }

                index++;
                continue;
            }

            if (c == '{')
            {
                argumentDepth++;
                builder.Append(c);
                index++;
                continue;
            }

            if (char.IsLetter(c))
            {
                // Repeat the vowel word once to emulate +35 % text growth (CJK-safe: only Latin).
                var end = index;
                while (end < message.Length && (char.IsLetter(message[end]) || message[end] == '\u2019'))
                {
                    end++;
                }

                var word = message[index..end];
                builder.Append(Accent(word));
                if (word.Length >= 5)
                {
                    builder.Append('\u00B7').Append(Accent(word));
                }

                index = end;
                continue;
            }

            builder.Append(c);
            index++;
        }

        builder.Append(' ').Append(Close);
        return builder.ToString();
    }

    private static string Accent(string word)
    {
        var builder = new StringBuilder(word.Length);
        foreach (var c in word)
        {
            builder.Append(c switch
            {
                'a' => '\u00E5',
                'e' => '\u00EB',
                'i' => '\u00EF',
                'o' => '\u00F6',
                'u' => '\u00FC',
                'y' => '\u00FF',
                'A' => '\u00C5',
                'E' => '\u00CB',
                'I' => '\u00CF',
                'O' => '\u00D6',
                'U' => '\u00DC',
                'Y' => '\u0178',
                _ => c,
            });
        }

        return builder.ToString();
    }
}
