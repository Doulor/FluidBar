using FluidBar.App.Settings;
using FluidBar.Core.Layout;
using FluidBar.Island;
using SkiaSharp;
using AppTheme = FluidBar.App.Settings.AppTheme;
using AppMaterial = FluidBar.App.Settings.IslandMaterial;
using IslandThemeMaterial = FluidBar.Island.IslandMaterial;

namespace FluidBar.App.Hosting.Skia;

/// <summary>
/// Maps the App settings snapshot onto the renderer's immutable <see cref="IslandTheme"/>.
/// <para>
/// Material: the approved design keeps the island "Obsidian" in every desktop theme. The App's
/// <c>Acrylic</c> tier has no real blur behind a layered window, so it degrades to the renderer's
/// simulated-glass tint (Tier 1) — documented fallback, not a silent no-op.
/// </para>
/// <para>Accent: <c>custom</c>/<c>system</c> use the configured fallback colour; <c>artwork</c> keeps
/// the per-activity artwork accent resolved by the presenter.</para>
/// </summary>
public static class SkiaIslandThemeMapper
{
    /// <summary>Builds the renderer theme for a settings snapshot.</summary>
    /// <param name="settings">Live App settings.</param>
    public static IslandTheme Map(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var theme = settings.Theme == AppTheme.Light ? IslandTheme.Light : IslandTheme.Dark;

        if (settings.Material == AppMaterial.Acrylic)
        {
            theme = theme.WithMaterial(IslandThemeMaterial.SimulatedGlass);
        }

        if (!string.Equals(settings.AccentMode, "artwork", StringComparison.OrdinalIgnoreCase))
        {
            var accent = IslandTheme.ParseColor(settings.AccentFallback, theme.Accent);
            theme = theme.WithAccent(accent);
        }

        var fill = Math.Clamp(settings.FillOpacity, LayoutTokens.MinFillOpacity, 1.0);
        return theme with { FillOpacity = fill };
    }

    /// <summary>Resolves the reduced-motion flag the renderer consumes (settings tri-state).</summary>
    /// <param name="settings">Live App settings.</param>
    public static bool ReducedMotion(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // The Windows client-area animation probe (SPI_GETCLIENTAREAANIMATION) lives in the platform
        // layer; App cannot P/Invoke (spec 5.2). Until that adapter is wired, "system" means enabled
        // (same rule as the existing Avalonia host).
        return settings.ResolveReducedMotion(systemPrefersReducedMotion: false);
    }
}
