using Avalonia.Media;
using FluidBar.App.Hosting.Skia;
using FluidBar.App.Settings;
using FluidBar.App.Theme;
using FluidBar.Core.Layout;
using FluidBar.Island;
using IslandMaterialTier = FluidBar.Island.IslandMaterial;
using AppMaterial = FluidBar.App.Settings.IslandMaterial;

namespace FluidBar.App.Views.Settings;

/// <summary>
/// Everything the appearance preview draws, resolved through the same mapper the Skia island uses
/// (<see cref="SkiaIslandThemeMapper"/>) — one token source, no second hard-coded palette.
/// <para>
/// The model is a plain value so tests can assert "settings → preview" without rendering, and so the
/// preview control stays a thin painter. It is rebuilt on every settings change, which is what makes
/// the preview live.
/// </para>
/// </summary>
public sealed record IslandPreviewModel
{
    /// <summary>Shell body colour (island <c>Background</c> token).</summary>
    public required Color Background { get; init; }

    /// <summary>Raised surface colour (island <c>BackgroundRaised</c> token).</summary>
    public required Color BackgroundRaised { get; init; }

    /// <summary>Hairline colour (island <c>Line</c> token).</summary>
    public required Color Line { get; init; }

    /// <summary>Primary text colour (island <c>Foreground</c> token).</summary>
    public required Color Foreground { get; init; }

    /// <summary>Secondary text colour (island <c>ForegroundSecondary</c> token).</summary>
    public required Color ForegroundSecondary { get; init; }

    /// <summary>Artwork gradient end colour (island <c>ArtEnd</c> token).</summary>
    public required Color ArtEnd { get; init; }

    /// <summary>Resolved accent (custom, system or artwork fallback).</summary>
    public required Color Accent { get; init; }

    /// <summary>User fill opacity (island <c>FillOpacity</c> token).</summary>
    public required double FillOpacity { get; init; }

    /// <summary>True when the shell renders the simulated-glass tier (island material).</summary>
    public required bool Glass { get; init; }

    /// <summary>Panel corner radius in DIP (<c>--island-radius</c>).</summary>
    public required double PanelRadius { get; init; }

    /// <summary>Island text scale.</summary>
    public required double FontScale { get; init; }

    /// <summary>Palette id (<c>obsidian</c>…<c>nord</c>).</summary>
    public required string Palette { get; init; }

    /// <summary>Material id (<c>obsidian</c>/<c>acrylic</c>).</summary>
    public required string Material { get; init; }

    /// <summary>Resolved accent as <c>#RRGGBB</c> (for the meta line).</summary>
    public required string AccentHex { get; init; }

    /// <summary>Glass tier paints a fixed translucent fill (renderer parity: 0.72).</summary>
    public const double GlassFillOpacity = 0.72;

    /// <summary>Fill opacity actually painted (glass overrides the slider, like the renderer).</summary>
    public double EffectiveFillOpacity => Glass ? GlassFillOpacity : Math.Clamp(FillOpacity, LayoutTokens.MinFillOpacity, 1.0);

    /// <summary>Corner radius clamped to the preview shell height.</summary>
    /// <param name="shellHeight">Rendered shell height.</param>
    public double RadiusFor(double shellHeight) => Math.Clamp(PanelRadius, 0, shellHeight / 2);

    /// <summary>Builds the preview model from live settings (Windows appearance resolved from the probe cache).</summary>
    /// <param name="settings">Live App settings.</param>
    public static IslandPreviewModel From(AppSettings settings)
        => From(settings, SystemAppearanceSnapshot.FromWindows());

    /// <summary>Builds the preview model from live settings and an explicit Windows appearance.</summary>
    /// <param name="settings">Live App settings.</param>
    /// <param name="appearance">Resolved Windows appearance preferences.</param>
    public static IslandPreviewModel From(AppSettings settings, in SystemAppearanceSnapshot appearance)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var theme = SkiaIslandThemeMapper.Map(settings, appearance);
        return new IslandPreviewModel
        {
            Background = ToColor(theme.Background),
            BackgroundRaised = ToColor(theme.BackgroundRaised),
            Line = ToColor(theme.Line),
            Foreground = ToColor(theme.Foreground),
            ForegroundSecondary = ToColor(theme.ForegroundSecondary),
            ArtEnd = ToColor(theme.ArtEnd),
            Accent = ToColor(theme.Accent),
            FillOpacity = theme.FillOpacity,
            Glass = theme.Material == IslandMaterialTier.SimulatedGlass,
            PanelRadius = settings.PanelRadius,
            FontScale = Math.Clamp(settings.FontScale, 0.8, 1.6),
            Palette = string.IsNullOrWhiteSpace(settings.Palette) ? "obsidian" : settings.Palette,
            Material = settings.Material == AppMaterial.Acrylic ? "acrylic" : "obsidian",
            AccentHex = "#" + theme.Accent.Red.ToString("X2") + theme.Accent.Green.ToString("X2") + theme.Accent.Blue.ToString("X2"),
        };
    }

    private static Color ToColor(SkiaSharp.SKColor color) => Color.FromArgb(color.Alpha, color.Red, color.Green, color.Blue);
}
