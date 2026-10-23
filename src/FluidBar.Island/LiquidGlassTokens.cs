namespace FluidBar.Island;

/// <summary>
/// Tier 2 "Liquid Glass" finish tokens (spec 3.4). The layered host cannot ask DWM for a backdrop blur
/// (layered windows bypass the compositor path), so the material is a *drawn* optical simulation: a
/// lensing rim band, a specular top edge, a caustic bottom bounce, chromatic dispersion, a static sheen
/// and a pointer-following illumination. Values are DIP / straight-alpha bytes, tuned against Apple's
/// iOS 26 material description (lensing, specular highlights, adaptive shadow) and the reference
/// implementations collected in <c>docs/rewrite/research/liquid-glass.md</c>.
/// </summary>
public static class LiquidGlassTokens
{
    /// <summary>
    /// Shell fill opacity while the liquid-glass tier is active (the opacity slider is bypassed). Slightly
    /// clearer than the acrylic tier so the desktop keeps showing through under the baked shadow.
    /// </summary>
    public const double FillOpacity = 0.68;

    /// <summary>Tint alpha painted over the blurred backdrop (backdrop mode).</summary>
    public const double BackdropTintAlpha = 0.30;

    /// <summary>Adaptive tint range: the selected 30 % tint scales between these bounds with backdrop luminance.</summary>
    public const double BackdropTintMinFactor = 0.90;

    /// <summary>Adaptive tint upper bound.</summary>
    public const double BackdropTintMaxFactor = 1.40;

    /// <summary>
    /// Lens magnification of the backdrop inside the rim band (1.0 = no refraction). The band re-draws
    /// the blurred desktop scaled about the shell centre, so the rim pulls in content from just outside
    /// the silhouette — the cheap Skia equivalent of the prototype's SDF displacement.
    /// </summary>
    public const double RefractionScale = 1.05;

    /// <summary>Width of the refraction band in DIP.</summary>
    public const double RefractionBandDip = 11.0;

    /// <summary>Opacity of the refracted band.</summary>
    public const double RefractionOpacity = 0.92;

    /// <summary>Narrowest lensing rim band in DIP (compact pill).</summary>
    public const double RimBandMinDip = 4.5;

    /// <summary>Widest lensing rim band in DIP (expanded panel).</summary>
    public const double RimBandMaxDip = 15.0;

    /// <summary>Rim band height factor (fraction of the shell height).</summary>
    public const double RimBandHeightFactor = 0.18;

    /// <summary>Specular top-edge stroke width in DIP.</summary>
    public const double SpecularWidthDip = 1.15;

    /// <summary>Caustic bottom-edge stroke width in DIP.</summary>
    public const double CausticWidthDip = 1.0;

    /// <summary>Chromatic dispersion stroke width in DIP.</summary>
    public const double DispersionWidthDip = 0.85;

    /// <summary>Pointer illumination radius in DIP.</summary>
    public const double PointerGlowRadiusDip = 132.0;

    /// <summary>Pointer illumination peak alpha (12 %).</summary>
    public const byte PointerGlowAlpha = 0x1F;

    /// <summary>Rim light alpha at the top edge (29 %).</summary>
    public const byte RimLightTopAlpha = 0x4A;

    /// <summary>Rim shade alpha at the bottom edge (22 %).</summary>
    public const byte RimShadeBottomAlpha = 0x38;

    /// <summary>Specular top-edge peak alpha (62 %).</summary>
    public const byte SpecularTopAlpha = 0x9E;

    /// <summary>Caustic bottom-edge alpha (19 %).</summary>
    public const byte CausticBottomAlpha = 0x30;

    /// <summary>Cool dispersion fringe alpha (12 %).</summary>
    public const byte DispersionCoolAlpha = 0x1E;

    /// <summary>Warm dispersion fringe alpha (9 %).</summary>
    public const byte DispersionWarmAlpha = 0x16;

    /// <summary>Sheen band peak alpha (8 %).</summary>
    public const byte SheenAlpha = 0x14;

    /// <summary>Body veil alpha at the top edge (11 %).</summary>
    public const byte VeilTopAlpha = 0x1C;

    /// <summary>Body shade alpha at the bottom edge (10 %).</summary>
    public const byte BodyShadeAlpha = 0x1A;

    /// <summary>Resolves the rim band width for a shell height.</summary>
    /// <param name="shellHeight">Shell height in DIP.</param>
    public static float RimBandFor(double shellHeight)
    {
        var height = double.IsFinite(shellHeight) ? Math.Max(0, shellHeight) : 0;
        var band = Math.Clamp(height * RimBandHeightFactor, RimBandMinDip, RimBandMaxDip);
        // A band thicker than a third of the shell would swallow the content area.
        return (float)Math.Min(band, height / 3.0);
    }
}
