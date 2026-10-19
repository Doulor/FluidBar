using FluidBar.Platform.Windows.Shell;

namespace FluidBar.App.Settings;

/// <summary>
/// Snapshot of the Windows appearance preferences the App honours for the "follow the system"
/// settings. Kept as a value so the mapping code is deterministic and unit-testable; production
/// reads it through <see cref="FromWindows"/> (a cached, off-UI-thread probe).
/// </summary>
/// <param name="LightTheme">Windows app theme is the light variant.</param>
/// <param name="ReducedMotion">Windows reports that client-area animations are disabled.</param>
/// <param name="AccentHex">Windows accent colour (<c>#RRGGBB</c>) when it could be read.</param>
public readonly record struct SystemAppearanceSnapshot(bool LightTheme, bool ReducedMotion, string? AccentHex)
{
    /// <summary>Neutral fallback used before the probe ran (dark chrome, animations enabled, default accent).</summary>
    public static SystemAppearanceSnapshot Neutral { get; } = new(false, false, null);

    /// <summary>The current cached Windows preferences.</summary>
    public static SystemAppearanceSnapshot FromWindows()
        => new(SystemAppearanceProbe.IsLightTheme, SystemAppearanceProbe.PrefersReducedMotion,
            SystemAppearanceProbe.SystemAccentHex);
}

/// <summary>Resolves the "system"/"artwork" appearance settings against a platform snapshot.</summary>
public static class SystemSettingResolution
{
    /// <summary>
    /// Resolves the configured theme: <see cref="AppTheme.System"/> follows the Windows app theme,
    /// every other value is used as-is.
    /// </summary>
    /// <param name="theme">Configured theme.</param>
    /// <param name="systemPrefersLight">Whether Windows currently uses the light app theme.</param>
    public static AppTheme ResolveTheme(AppTheme theme, bool systemPrefersLight) => theme switch
    {
        AppTheme.System => systemPrefersLight ? AppTheme.Light : AppTheme.Dark,
        _ => theme,
    };

    /// <summary>
    /// Resolves the accent colour source: <c>system</c> uses the Windows accent (falling back to the
    /// configured colour), <c>custom</c> and <c>artwork</c> use the configured colour (the artwork
    /// accent overrides it per activity at draw time).
    /// </summary>
    /// <param name="accentMode">Accent mode token.</param>
    /// <param name="accentFallback">Configured fallback colour.</param>
    /// <param name="systemAccentHex">Windows accent colour when available.</param>
    public static string ResolveAccent(string? accentMode, string accentFallback, string? systemAccentHex)
        => string.Equals(accentMode, "system", StringComparison.OrdinalIgnoreCase)
           && !string.IsNullOrWhiteSpace(systemAccentHex)
            ? systemAccentHex
            : accentFallback;

    /// <summary>
    /// Resolves the reduced-motion tri-state: <c>on</c>/<c>off</c> are explicit, <c>system</c>
    /// follows the Windows animation preference.
    /// </summary>
    /// <param name="preference">Configured preference.</param>
    /// <param name="systemPrefersReducedMotion">Whether Windows reports reduced motion.</param>
    public static bool ResolveReducedMotion(ReducedMotionPreference preference, bool systemPrefersReducedMotion)
        => preference switch
        {
            ReducedMotionPreference.On => true,
            ReducedMotionPreference.Off => false,
            _ => systemPrefersReducedMotion,
        };
}
