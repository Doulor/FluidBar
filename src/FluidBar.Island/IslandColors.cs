using SkiaSharp;

namespace FluidBar.Island;

/// <summary>
/// Named colour accessors for the island material stack (L0–L4) and its three text roles. The values come
/// from <see cref="MaterialTokens.Dark"/> so the layers, the renderer and the audit share one source of truth.
/// <para>
/// The class is deliberately named <c>IslandMaterialColors</c> (not <c>IslandColors</c>) because the App project
/// already owns <c>FluidBar.App.Theme.IslandColors</c> (the palette record used by the Avalonia-drawn surfaces);
/// keeping distinct names avoids ambiguous references in files that import both namespaces.
/// </para>
/// </summary>
public static class IslandMaterialColors
{
    /// <summary>L0 container hex from the study (#08080A).</summary>
    public const string ContainerHex = "#08080A";

    /// <summary>L1 card hex from the study (#141418).</summary>
    public const string CardHex = "#141418";

    /// <summary>L0 container fill (opaque near-black).</summary>
    public static SKColor Container => MaterialTokens.Dark.ContainerColor;

    /// <summary>L1 card fill (#141418 at 60 % straight alpha).</summary>
    public static SKColor Card => MaterialTokens.Dark.L1.Color;

    /// <summary>L1 card colour as it appears composited on L0 (the value contrast is measured against).</summary>
    public static SKColor CardEffective => MaterialTokens.Dark.EffectiveColor(MaterialLayer.L1Card);

    /// <summary>L2 separator (white 9 %).</summary>
    public static SKColor Separator => MaterialTokens.Dark.L2.Color;

    /// <summary>L3 top highlight start colour (white 5 %, fades to transparent).</summary>
    public static SKColor TopHighlight => MaterialTokens.Dark.L3.Color;

    /// <summary>L4 pressed inner shadow (black 18 %).</summary>
    public static SKColor InnerShadow => MaterialTokens.Dark.L4.Color;

    /// <summary>Body text (#EDEDEE).</summary>
    public static SKColor TextPrimary => MaterialTokens.Dark.Text.Primary;

    /// <summary>Secondary text (#9B9BA5).</summary>
    public static SKColor TextSecondary => MaterialTokens.Dark.Text.Secondary;

    /// <summary>Tertiary text (shared <c>TextTokens.TertiaryDark</c>, #86868F).</summary>
    public static SKColor TextTertiary => MaterialTokens.Dark.Text.Tertiary;

    /// <summary>WCAG self-check of body/secondary/tertiary text on L0 and L1.</summary>
    public static MaterialContrastReport ContrastReport => MaterialTokens.Dark.CheckContrast();

    /// <summary>Contrast ratio of one text colour on one layer of the Obsidian stack.</summary>
    public static double Contrast(SKColor text, MaterialLayer layer) => MaterialTokens.Dark.Contrast(text, layer);

    /// <summary>Spec validation of the Obsidian stack (empty when conformant).</summary>
    public static IReadOnlyList<string> Validate() => MaterialTokens.Dark.Validate();

    /// <summary>One-line description of a layer for evidence tables and logs.</summary>
    public static string Describe(MaterialLayer layer)
    {
        var token = MaterialTokens.Dark[layer];
        return layer switch
        {
            MaterialLayer.L0Container => $"L0 container {MaterialTokens.Hex(token.Color)} opaque, no gradient, no accent",
            MaterialLayer.L1Card => $"L1 card {MaterialTokens.Hex(token.Color)} ({token.Opacity * 100:0} %), radius {token.CornerRadiusDip:0}",
            MaterialLayer.L2Separator => $"L2 separator {MaterialTokens.Hex(token.Color)} ({token.Opacity * 100:0} %), {token.StrokeWidthDip:0} DIP",
            MaterialLayer.L3TopHighlight => $"L3 highlight {MaterialTokens.Hex(token.Color)} ({token.Opacity * 100:0} %) → transparent, {token.HeightDip:0.#} DIP",
            MaterialLayer.L4InnerShadow => $"L4 inner shadow {MaterialTokens.Hex(token.Color)} ({token.Opacity * 100:0} %), top inset {token.HeightDip:0} DIP",
            _ => layer.ToString(),
        };
    }
}
