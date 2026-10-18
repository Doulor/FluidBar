using System.Globalization;

namespace FluidBar.App.Localization;

/// <summary>
/// One parsed locale catalog: a BCP-47 tag plus flat <c>Area.SubArea.Element</c> message keys.
/// Catalog files are JSON (spec 7.1) and are never resx.
/// </summary>
/// <param name="Locale">BCP-47 locale tag, e.g. <c>en-US</c> or <c>zh-Hans</c>.</param>
/// <param name="Version">Catalog schema version (currently 1).</param>
/// <param name="Messages">Read-only key/value messages.</param>
public sealed record LocalizationCatalog(
    string Locale,
    int Version,
    IReadOnlyDictionary<string, string> Messages);

/// <summary>
/// The single localization abstraction the UI codes against (spec 7.1). The backend is a JSON
/// catalog today; Core may take over behind the same shape once its <c>ILocalizer</c> lands.
/// </summary>
public interface ILocalizer
{
    /// <summary>Resolved BCP-47 UI culture currently in effect.</summary>
    string Culture { get; }

    /// <summary>
    /// Monotonic version raised on every culture change or catalog reload. Views that relocalize
    /// manually can compare it to avoid redundant work.
    /// </summary>
    int Version { get; }

    /// <summary>Raised after the culture or catalogs changed; text must be re-resolved.</summary>
    event EventHandler? Changed;

    /// <summary>Cultures with a concrete catalog, in preference order (<c>en-US</c> first).</summary>
    IReadOnlyList<string> AvailableCultures { get; }

    /// <summary>Whether the current culture is a right-to-left locale.</summary>
    bool IsRightToLeft { get; }

    /// <summary>Resolved <see cref="System.Globalization.CultureInfo"/> for the active UI culture.</summary>
    System.Globalization.CultureInfo CurrentCulture { get; }

    /// <summary>Summary label for a culture, localized in the current UI culture when known.</summary>
    string GetCultureDisplayName(string cultureTag);

    /// <summary>Resolves a message key through the fallback chain.</summary>
    string Get(string key);

    /// <summary>Formats a message whose template takes named ICU arguments.</summary>
    string Format(string key, IReadOnlyDictionary<string, object?> arguments);

    /// <summary>Formats a message with named arguments supplied as tuples.</summary>
    string Format(string key, params (string Name, object? Value)[] arguments);

    /// <summary>Non-throwing lookup (diagnostics/plug-in catalogs).</summary>
    bool TryGet(string key, out string value);
}

/// <summary>Locale handling helpers shared by the service, tests and diagnostics.</summary>
public static class Locales
{
    /// <summary>Source locale; every other catalog falls back to it (spec 7.2).</summary>
    public const string Source = "en-US";

    /// <summary>First translated locale shipped in v2.0.</summary>
    public const string ChineseSimplified = "zh-Hans";

    /// <summary>Pseudo-locale used to expose unlocalized layout growth (spec 7.6).</summary>
    public const string Pseudo = "qps-ploc";

    /// <summary>
    /// Builds the fallback chain for a requested tag: exact, parent chain, then <c>en-US</c>.
    /// Unknown tags resolve straight to <c>en-US</c> (never throws for user-supplied settings).
    /// </summary>
    public static IReadOnlyList<string> BuildFallbackChain(string? requested)
    {
        var chain = new List<string>(5);
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var tag = requested.Trim();
            if (string.Equals(tag, "system", StringComparison.OrdinalIgnoreCase))
            {
                tag = CultureInfo.CurrentUICulture.Name;
            }

            if (tag.Length > 0)
            {
                var culture = TryCulture(tag);
                var current = culture?.Name;
                while (!string.IsNullOrEmpty(current))
                {
                    AddUnique(chain, current);
                    var parent = TryCulture(current)?.Parent;
                    if (parent is null || parent.Name.Length == 0 || parent.Name == current)
                    {
                        break;
                    }

                    current = parent.Name;
                }
            }
        }

        AddUnique(chain, Source);
        return chain;
    }

    /// <summary>Returns whether <paramref name="tag"/> is one of the shipped catalog locales.</summary>
    public static bool IsShipped(string tag)
        => tag is Source or ChineseSimplified or Pseudo
           || tag.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
           || tag.StartsWith("en", StringComparison.OrdinalIgnoreCase);

    /// <summary>Normalizes a settings value to a shipped catalog tag (system → current UI culture).</summary>
    public static string Normalize(string? requested)
    {
        if (string.IsNullOrWhiteSpace(requested) || string.Equals(requested, "system", StringComparison.OrdinalIgnoreCase))
        {
            return CultureInfo.CurrentUICulture.Name.Length > 0
                ? CultureInfo.CurrentUICulture.Name
                : Source;
        }

        var tag = requested.Trim();
        if (tag.Equals(Pseudo, StringComparison.OrdinalIgnoreCase))
        {
            return Pseudo;
        }

        if (tag.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return ChineseSimplified;
        }

        if (tag.StartsWith("en", StringComparison.OrdinalIgnoreCase))
        {
            return Source;
        }

        return tag;
    }

    private static void AddUnique(List<string> chain, string tag)
    {
        foreach (var existing in chain)
        {
            if (string.Equals(existing, tag, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        chain.Add(tag);
    }

    private static CultureInfo? TryCulture(string tag)
    {
        try
        {
            return CultureInfo.GetCultureInfo(tag);
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }
}
