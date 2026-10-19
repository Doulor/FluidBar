using FluidBar.App.Settings;
using FluidBar.App.Theme;
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
/// <para>
/// Palette: the four curated palettes port the same background tokens the Avalonia host already used
/// (<see cref="IslandColors"/>), so switching "obsidian/graphite/slate/nord" visibly repaints the shell.
/// The light island variant keeps its own porcelain tokens; palettes are dark-surfaces only.
/// </para>
/// <para>
/// Theme/accent/motion: <c>system</c> values resolve against a <see cref="SystemAppearanceSnapshot"/> so
/// "follow Windows" is a real mapping instead of assuming dark chrome and enabled animations.
/// </para>
/// </summary>
public static class SkiaIslandThemeMapper
{
    /// <summary>Builds the renderer theme for a settings snapshot (system values resolved from the probe cache).</summary>
    /// <param name="settings">Live App settings.</param>
    public static IslandTheme Map(AppSettings settings) => Map(settings, SystemAppearanceSnapshot.FromWindows());

    /// <summary>Builds the renderer theme for a settings snapshot and an explicit Windows appearance.</summary>
    /// <param name="settings">Live App settings.</param>
    /// <param name="appearance">Resolved Windows appearance preferences.</param>
    public static IslandTheme Map(AppSettings settings, in SystemAppearanceSnapshot appearance)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var resolvedTheme = SystemSettingResolution.ResolveTheme(settings.Theme, appearance.LightTheme);
        var theme = resolvedTheme == AppTheme.Light ? IslandTheme.Light : IslandTheme.Dark;

        // Palettes are dark-surface tokens; the porcelain island keeps its own tokens.
        if (theme.Mode == IslandThemeMode.Dark)
        {
            theme = ApplyPalette(theme, settings.Palette);
        }

        // Safe mode forces the Tier 0 opaque material (spec §8.4).
        if (settings.Material == AppMaterial.Acrylic && !settings.Advanced.SafeMode)
        {
            theme = theme.WithMaterial(IslandThemeMaterial.SimulatedGlass);
        }

        var accentHex = SystemSettingResolution.ResolveAccent(
            settings.AccentMode, settings.AccentFallback, appearance.AccentHex);
        var accent = IslandTheme.ParseColor(accentHex, theme.Accent);
        theme = theme.WithAccent(accent);

        var fill = Math.Clamp(settings.FillOpacity, LayoutTokens.MinFillOpacity, 1.0);
        return theme with { FillOpacity = fill };
    }

    /// <summary>Resolves the reduced-motion flag the renderer consumes (settings tri-state + Windows).</summary>
    /// <param name="settings">Live App settings.</param>
    public static bool ReducedMotion(AppSettings settings) => ReducedMotion(settings, SystemAppearanceSnapshot.FromWindows());

    /// <summary>Resolves the reduced-motion flag against an explicit Windows appearance.</summary>
    /// <param name="settings">Live App settings.</param>
    /// <param name="appearance">Resolved Windows appearance preferences.</param>
    public static bool ReducedMotion(AppSettings settings, in SystemAppearanceSnapshot appearance)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return SystemSettingResolution.ResolveReducedMotion(settings.ReducedMotion, appearance.ReducedMotion);
    }

    /// <summary>Applies one curated palette to the dark island theme (Obsidian is the identity mapping).</summary>
    private static IslandTheme ApplyPalette(IslandTheme theme, string? palette)
    {
        var colors = IslandColors.ForPalette(palette);
        return theme with
        {
            Background = ToSkColor(colors.Background),
            BackgroundRaised = ToSkColor(colors.BackgroundRaised),
            BackgroundInset = ToSkColor(colors.BackgroundInset),
            Line = ToSkColor(colors.Line),
            LineStrong = ToSkColor(colors.LineStrong),
            Foreground = ToSkColor(colors.Foreground),
            ForegroundSecondary = ToSkColor(colors.ForegroundSecondary),
            ForegroundTertiary = ToSkColor(colors.ForegroundTertiary),
            ShadowColor = ToSkColor(colors.Background),
        };
    }

    private static SKColor ToSkColor(Avalonia.Media.Color color) => new(color.R, color.G, color.B, color.A);
}
