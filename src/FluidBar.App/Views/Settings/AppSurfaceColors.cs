using FluidBar.App.Theme;
using FluidBar.Island;
using SkiaSharp;

namespace FluidBar.App.Views.Settings;

/// <summary>
/// Settings-side material layer contract. The window/panel/row palette lives in
/// <c>FluidBar.App.Theme.AppSurfaceColors</c> (owned by the theme file); this type maps those surfaces onto the
/// shared L0–L4 material vocabulary and self-checks the WCAG contrast of the settings text on them.
/// <para>
/// The five layers are identical in spirit to the island stack (<see cref="MaterialTokens"/>): L0 container,
/// L1 card/section, L2 separator, L3 top micro-highlight, L4 pressed inset. The settings palette is a different
/// visual language (Graphite/Porcelain overlays), so <see cref="Validate"/> reports where it deviates from the
/// iOS numeric bounds instead of silently rewriting another owner's palette.
/// </para>
/// </summary>
public static class AppSurfaceMaterial
{
    /// <summary>Maps an <see cref="AppSurfaceColors"/> snapshot onto the five material layers.</summary>
    public static MaterialSurfaceStack FromSurfaces(AppSurfaceColors surfaces)
    {
        var l0 = ToSkia(surfaces.Window);
        var surface = ToSkia(surfaces.Surface);
        var l1 = MaterialContrast.Composite(surface, l0);
        var line = ToSkia(surfaces.Line);

        return new MaterialSurfaceStack(
            Surfaces: surfaces,
            L0: new MaterialLayerToken { Layer = MaterialLayer.L0Container, Color = l0 },
            L1: new MaterialLayerToken
            {
                Layer = MaterialLayer.L1Card,
                Color = surface,
                CornerRadiusDip = AppSettingsMaterial.CardCornerRadiusDip,
            },
            L2: new MaterialLayerToken
            {
                Layer = MaterialLayer.L2Separator,
                Color = line,
                StrokeWidthDip = AppSettingsMaterial.SeparatorWidthDip,
            },
            L3: MaterialTokens.Dark.L3 with { Layer = MaterialLayer.L3TopHighlight },
            L4: MaterialTokens.Dark.L4 with { Layer = MaterialLayer.L4InnerShadow },
            EffectiveL1: l1);
    }

    /// <summary>
    /// WCAG self-check of the settings text: primary (body ≥ 4.5) and secondary/tertiary (≥ 3.0) on L0 and the
    /// composited L1, using the same engine as the island material stack.
    /// </summary>
    public static MaterialContrastReport ContrastReport(AppSurfaceColors surfaces)
    {
        var stack = FromSurfaces(surfaces);
        var text = new MaterialTextSet(
            ToSkia(surfaces.Foreground),
            ToSkia(surfaces.ForegroundSecondary),
            ToSkia(surfaces.ForegroundTertiary));

        return new MaterialContrastReport(
        [
            MaterialContrast.Measure("primary", "L0", text.Primary, stack.L0.Color, text.BodyMinimum),
            MaterialContrast.Measure("primary", "L1", text.Primary, stack.EffectiveL1, text.BodyMinimum),
            MaterialContrast.Measure("secondary", "L0", text.Secondary, stack.L0.Color, text.SecondaryMinimum),
            MaterialContrast.Measure("secondary", "L1", text.Secondary, stack.EffectiveL1, text.SecondaryMinimum),
            MaterialContrast.Measure("tertiary", "L0", text.Tertiary, stack.L0.Color, text.TertiaryMinimum),
            MaterialContrast.Measure("tertiary", "L1", text.Tertiary, stack.EffectiveL1, text.TertiaryMinimum),
        ]);
    }

    /// <summary>Throws when the settings text fails the WCAG self-check.</summary>
    public static MaterialContrastReport EnsureContrast(AppSurfaceColors surfaces) =>
        ContrastReport(surfaces).EnsureAllPass();

    /// <summary>
    /// Reports where the settings surfaces deviate from the iOS material bounds. Contrast failures are included;
    /// geometry/colour deviations are informational because the settings palette is a different surface language.
    /// </summary>
    public static IReadOnlyList<string> Validate(AppSurfaceColors surfaces)
    {
        var stack = FromSurfaces(surfaces);
        var issues = new List<string>();

        if (stack.L1.Color.Alpha < 255)
        {
            var spec = MaterialSpecs.For(MaterialLayer.L1Card);
            if (stack.L1.Opacity < spec.MinOpacity || stack.L1.Opacity > spec.MaxOpacity)
            {
                issues.Add($"L1: settings card opacity {stack.L1.Opacity * 100:0.#} % is outside the iOS {spec.MinOpacity * 100:0}–{spec.MaxOpacity * 100:0} % band.");
            }
        }

        var l2Spec = MaterialSpecs.For(MaterialLayer.L2Separator);
        if (stack.L2.Opacity < l2Spec.MinOpacity || stack.L2.Opacity > l2Spec.MaxOpacity)
        {
            issues.Add($"L2: settings separator opacity {stack.L2.Opacity * 100:0.#} % is outside the iOS {l2Spec.MinOpacity * 100:0}–{l2Spec.MaxOpacity * 100:0} % band.");
        }

        foreach (var failure in ContrastReport(surfaces).Failures)
        {
            issues.Add("contrast: " + failure);
        }

        return issues;
    }

    /// <summary>Formats a WCAG row set as a compact markdown table (reports/evidence).</summary>
    public static string ToMarkdown(MaterialContrastReport report, string title)
    {
        ArgumentNullException.ThrowIfNull(report);
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("### " + title).AppendLine();
        builder.AppendLine("| role | layer | text | background | ratio | required | result |");
        builder.AppendLine("| --- | --- | --- | --- | ---: | ---: | --- |");
        foreach (var row in report.Rows)
        {
            builder.AppendLine($"| {row.Role} | {row.Layer} | {MaterialTokens.Hex(row.Text)} | {MaterialTokens.Hex(row.Background)} | {row.Rounded:0.00}:1 | {row.Required:0.0}:1 | {(row.Passes ? "PASS" : "FAIL")} |");
        }

        return builder.ToString();
    }

    private static SKColor ToSkia(Avalonia.Media.Color color) => new(color.R, color.G, color.B, color.A);

    /// <summary>Geometry the settings rows already use; kept here so the audit and the views agree.</summary>
    private static class AppSettingsMaterial
    {
        internal const double CardCornerRadiusDip = 12;
        internal const double SeparatorWidthDip = 1;
    }
}

/// <summary>One settings surface resolved into the five material layers.</summary>
/// <param name="Surfaces">The source palette snapshot.</param>
/// <param name="L0">Container layer (window background).</param>
/// <param name="L1">Card/section layer (panel surface).</param>
/// <param name="L2">Separator layer (hairline).</param>
/// <param name="L3">Top highlight layer (shared island values until the settings shell adopts it).</param>
/// <param name="L4">Pressed inset layer (shared island values until the settings shell adopts it).</param>
/// <param name="EffectiveL1">L1 composited on L0 (what text contrast is measured against).</param>
public sealed record MaterialSurfaceStack(
    AppSurfaceColors Surfaces,
    MaterialLayerToken L0,
    MaterialLayerToken L1,
    MaterialLayerToken L2,
    MaterialLayerToken L3,
    MaterialLayerToken L4,
    SKColor EffectiveL1)
{
    /// <summary>All five layers in stack order.</summary>
    public IReadOnlyList<MaterialLayerToken> Layers => [L0, L1, L2, L3, L4];
}
