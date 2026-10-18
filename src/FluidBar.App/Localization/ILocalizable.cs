namespace FluidBar.App.Localization;

/// <summary>
/// Implemented by views that render localized text imperatively (custom-drawn controls). The host
/// re-invokes <see cref="ApplyLocalization"/> after <see cref="ILocalizer.Changed"/>, so a language
/// switch updates text in place without rebuilding the visual tree or losing state (spec 7.2).
/// </summary>
public interface ILocalizable
{
    /// <summary>Re-resolves all localized strings for the supplied localizer.</summary>
    void ApplyLocalization(ILocalizer localizer);
}
