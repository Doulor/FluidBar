using Avalonia.Markup.Xaml;

namespace FluidBar.App.Localization;

/// <summary>
/// Process-wide localization entry point used by the <see cref="LocExtension"/> markup extension and
/// by diagnostics. Composition sets <see cref="Current"/> once at startup; unlike a service locator
/// used for business logic, this only bridges XAML markup to the one localization service.
/// </summary>
public static class LocalizationRuntime
{
    private static ILocalizer? _current;

    /// <summary>The active localizer; a minimal <c>en-US</c> fallback before composition completes.</summary>
    public static ILocalizer Current
    {
        get => _current ?? FallbackLocalizer.Instance;
        set => _current = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Whether composition has installed the real localizer.</summary>
    public static bool IsConfigured => _current is not null;

    private sealed class FallbackLocalizer : ILocalizer
    {
        public static FallbackLocalizer Instance { get; } = new();

        public string Culture => Locales.Source;

        public int Version => 0;

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }

        public IReadOnlyList<string> AvailableCultures => [Locales.Source];

        public bool IsRightToLeft => false;

        public System.Globalization.CultureInfo CurrentCulture => System.Globalization.CultureInfo.GetCultureInfo(Locales.Source);

        public string GetCultureDisplayName(string cultureTag) => cultureTag;

        public string Get(string key) => key;

        public string Format(string key, IReadOnlyDictionary<string, object?> arguments) => key;

        public string Format(string key, params (string Name, object? Value)[] arguments) => key;

        public bool TryGet(string key, out string value)
        {
            value = key;
            return false;
        }
    }
}

/// <summary>
/// XAML markup extension for localized text: <c>{loc Rail.Home}</c>. The value resolves once at load;
/// language switches re-resolve text through the host's relocalization pass (no restart, no page
/// rebuild — see <c>docs/rewrite/contracts/ui.md</c>).
/// </summary>
public sealed class LocExtension : MarkupExtension
{
    /// <summary>Creates an unset localization extension.</summary>
    public LocExtension()
    {
    }

    /// <summary>Creates a localization extension for <paramref name="key"/>.</summary>
    public LocExtension(string key) => Key = key;

    /// <summary>Message key in <c>Area.SubArea.Element</c> form.</summary>
    public string Key { get; set; } = string.Empty;

    /// <inheritdoc />
    public override object ProvideValue(IServiceProvider serviceProvider)
        => LocalizationRuntime.Current.Get(Key);
}
