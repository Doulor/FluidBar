using SkiaSharp;

namespace FluidBar.Island;

/// <summary>
/// The five material layers from <c>docs/ios-design-study.md</c> §2. Container → card → separator →
/// top highlight → pressed inset. Content colours (icons, progress, artwork) are deliberately not layers:
/// the container may never carry an accent.
/// </summary>
public enum MaterialLayer
{
    /// <summary>L0 outer container: near-black, opaque, no gradient, no accent border.</summary>
    L0Container = 0,

    /// <summary>L1 card/section fill: #141418 at 55–65 % with radius 12.</summary>
    L1Card = 1,

    /// <summary>L2 separator: white 8–11 %, 1 DIP.</summary>
    L2Separator = 2,

    /// <summary>L3 top micro-highlight: white 4–6 %, 1–2 DIP fading to transparent.</summary>
    L3TopHighlight = 3,

    /// <summary>L4 pressed inset shadow: black 15–20 %, top inset 6–10 DIP.</summary>
    L4InnerShadow = 4,
}

/// <summary>
/// Machine-readable spec bounds for one layer. The audit, the unit tests and <see cref="MaterialTokens.Validate"/>
/// all read these numbers so the study and the code can never drift silently.
/// </summary>
/// <param name="Layer">Layer these bounds apply to.</param>
/// <param name="MinOpacity">Minimum straight-alpha opacity (0..1).</param>
/// <param name="MaxOpacity">Maximum straight-alpha opacity (0..1).</param>
/// <param name="MinCornerRadiusDip">Minimum corner radius in DIP (0 when the layer has no radius).</param>
/// <param name="MaxCornerRadiusDip">Maximum corner radius in DIP (0 when the layer has no radius).</param>
/// <param name="MinStrokeWidthDip">Minimum stroke width in DIP (0 when the layer does not stroke).</param>
/// <param name="MaxStrokeWidthDip">Maximum stroke width in DIP.</param>
/// <param name="MinHeightDip">Minimum highlight height / inset in DIP.</param>
/// <param name="MaxHeightDip">Maximum highlight height / inset in DIP.</param>
/// <param name="Reference">Short quote of the study row this layer implements.</param>
public sealed record MaterialLayerSpec(
    MaterialLayer Layer,
    double MinOpacity,
    double MaxOpacity,
    double MinCornerRadiusDip,
    double MaxCornerRadiusDip,
    double MinStrokeWidthDip,
    double MaxStrokeWidthDip,
    double MinHeightDip,
    double MaxHeightDip,
    string Reference);

/// <summary>Spec bounds of every layer (docs/ios-design-study.md §2 table).</summary>
public static class MaterialSpecs
{
    /// <summary>Spec of the layer.</summary>
    public static MaterialLayerSpec For(MaterialLayer layer) => layer switch
    {
        MaterialLayer.L0Container => new(layer, 0.96, 1.00, 0, 0, 0, 0, 0, 0,
            "L0: #000000 or #08080A; 0.96–1.00; no gradient, no accent border"),
        MaterialLayer.L1Card => new(layer, 0.55, 0.65, 12, 12, 0, 0, 0, 0,
            "L1: #141418, 55–65 %, radius 12"),
        MaterialLayer.L2Separator => new(layer, 0.08, 0.11, 0, 0, 1, 1, 0, 0,
            "L2: white 8–11 %, 1 DIP"),
        MaterialLayer.L3TopHighlight => new(layer, 0.04, 0.06, 0, 0, 0, 0, 1, 2,
            "L3: white 4–6 %, 1–2 DIP fading to transparent"),
        MaterialLayer.L4InnerShadow => new(layer, 0.15, 0.20, 0, 0, 0, 0, 6, 10,
            "L4: black 15–20 %, top inset 6–10 DIP"),
        _ => throw new ArgumentOutOfRangeException(nameof(layer), layer, "Unknown material layer."),
    };

    /// <summary>Every layer in stack order (L0 first).</summary>
    public static IReadOnlyList<MaterialLayerSpec> All { get; } =
        [For(MaterialLayer.L0Container), For(MaterialLayer.L1Card), For(MaterialLayer.L2Separator), For(MaterialLayer.L3TopHighlight), For(MaterialLayer.L4InnerShadow)];
}

/// <summary>One resolved material layer: a base colour (alpha = opacity) plus its geometry.</summary>
public sealed record MaterialLayerToken
{
    /// <summary>Which layer this token represents.</summary>
    public required MaterialLayer Layer { get; init; }

    /// <summary>Base colour with straight alpha; <c>Alpha / 255</c> is the layer opacity.</summary>
    public required SKColor Color { get; init; }

    /// <summary>Corner radius in DIP (L1 = 12; other layers 0).</summary>
    public double CornerRadiusDip { get; init; }

    /// <summary>Stroke width in DIP (L2 = 1; other layers 0).</summary>
    public double StrokeWidthDip { get; init; }

    /// <summary>Highlight height / inset depth in DIP (L3 1–2, L4 6–10).</summary>
    public double HeightDip { get; init; }

    /// <summary>Blur sigma in DIP used for the pressed inset (L4 only).</summary>
    public double BlurSigmaDip { get; init; }

    /// <summary>Whether the layer is painted as a gradient (L3 only, fading to <see cref="GradientEndColor"/>).</summary>
    public bool UsesGradient { get; init; }

    /// <summary>Gradient end colour; transparent for L3.</summary>
    public SKColor GradientEndColor { get; init; } = SKColors.Transparent;

    /// <summary>Whether the layer may carry the user accent. Must stay false for every material layer.</summary>
    public bool UsesAccent { get; init; }

    /// <summary>Opacity in 0..1 derived from the colour alpha.</summary>
    public double Opacity => Color.Alpha / 255d;

    /// <summary>Hex string with alpha (<c>#AARRGGBB</c>) for reports and evidence.</summary>
    public string Hex => $"#{Color.Alpha:X2}{Color.Red:X2}{Color.Green:X2}{Color.Blue:X2}";
}

/// <summary>Text roles whose contrast the material stack must keep (WCAG 2.2 AA thresholds).</summary>
/// <param name="Primary">Body text colour.</param>
/// <param name="Secondary">Secondary text colour.</param>
/// <param name="Tertiary">Tertiary/meta text colour.</param>
/// <param name="BodyMinimum">Minimum contrast for <paramref name="Primary"/> (4.5).</param>
/// <param name="SecondaryMinimum">Minimum contrast for <paramref name="Secondary"/> (3.0).</param>
/// <param name="TertiaryMinimum">Minimum contrast for <paramref name="Tertiary"/> (3.0).</param>
public sealed record MaterialTextSet(
    SKColor Primary,
    SKColor Secondary,
    SKColor Tertiary,
    double BodyMinimum = 4.5,
    double SecondaryMinimum = 3.0,
    double TertiaryMinimum = 3.0)
{
    /// <summary>Obsidian text roles used by the island shell (primary #EDEDEE, secondary #9B9BA5, tertiary #86868F).</summary>
    public static MaterialTextSet Dark { get; } = new(
        IslandTheme.ParseColor("#EDEDEE", SKColors.White),
        IslandTheme.ParseColor("#9B9BA5", SKColors.White),
        IslandTheme.ParseColor(FluidBar.Core.Layout.TextTokens.TertiaryDark, SKColors.White));

    /// <summary>Porcelain text roles (primary #14161A, secondary #5B6270, tertiary #666D78).</summary>
    public static MaterialTextSet Light { get; } = new(
        IslandTheme.ParseColor("#14161A", SKColors.Black),
        IslandTheme.ParseColor("#5B6270", SKColors.Black),
        IslandTheme.ParseColor(FluidBar.Core.Layout.TextTokens.TertiaryLight, SKColors.Black));
}

/// <summary>One measured contrast row (WCAG 2.2 relative-luminance ratio).</summary>
/// <param name="Role">Text role name (<c>primary</c>/<c>secondary</c>/<c>tertiary</c>).</param>
/// <param name="Layer">Layer the text sits on (<c>L0</c>/<c>L1</c>).</param>
/// <param name="Text">Text colour.</param>
/// <param name="Background">Effective (composited) background colour.</param>
/// <param name="Ratio">Measured contrast ratio (unrounded).</param>
/// <param name="Required">Required minimum for this role.</param>
public sealed record MaterialContrastRow(string Role, string Layer, SKColor Text, SKColor Background, double Ratio, double Required)
{
    /// <summary>Whether the row meets its threshold.</summary>
    public bool Passes => Ratio >= Required;

    /// <summary>Ratio rounded to two decimals for reports/UI.</summary>
    public double Rounded => Math.Round(Ratio, 2);
}

/// <summary>Result of a material contrast self-check.</summary>
/// <param name="Rows">Every measured row.</param>
public sealed record MaterialContrastReport(IReadOnlyList<MaterialContrastRow> Rows)
{
    /// <summary>Whether every row meets its threshold.</summary>
    public bool AllPass => Rows.All(static row => row.Passes);

    /// <summary>Rows that failed, for error messages.</summary>
    public IReadOnlyList<string> Failures =>
        [.. Rows.Where(static row => !row.Passes).Select(static row => $"{row.Role} on {row.Layer}: {row.Ratio:0.00}:1 < {row.Required:0.0}:1")];

    /// <summary>Lowest measured body-text ratio (primary role).</summary>
    public double MinimumBody => Rows.Where(static row => row.Role == "primary").Min(static row => row.Ratio);

    /// <summary>Lowest measured secondary-text ratio.</summary>
    public double MinimumSecondary => Rows.Where(static row => row.Role == "secondary").Min(static row => row.Ratio);

    /// <summary>Throws when any row fails; used by startup self-checks and tests.</summary>
    public MaterialContrastReport EnsureAllPass()
    {
        if (!AllPass)
        {
            throw new MaterialContrastException("Material contrast self-check failed: " + string.Join("; ", Failures));
        }

        return this;
    }
}

/// <summary>Raised when a material token set fails its WCAG self-check.</summary>
public sealed class MaterialContrastException : Exception
{
    /// <summary>Creates the exception.</summary>
    public MaterialContrastException(string message)
        : base(message)
    {
    }
}

/// <summary>WCAG 2.2 contrast maths for opaque and translucent material layers.</summary>
public static class MaterialContrast
{
    /// <summary>WCAG relative luminance of an opaque colour (alpha is ignored; composite first).</summary>
    public static double RelativeLuminance(SKColor color)
    {
        static double Channel(byte value)
        {
            var channel = value / 255d;
            return channel <= 0.03928 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(color.Red)) + (0.7152 * Channel(color.Green)) + (0.0722 * Channel(color.Blue));
    }

    /// <summary>Contrast ratio between two opaque colours, in the 1..21 range (no rounding).</summary>
    public static double Ratio(SKColor foreground, SKColor background)
    {
        var a = RelativeLuminance(foreground);
        var b = RelativeLuminance(background);
        var lighter = Math.Max(a, b);
        var darker = Math.Min(a, b);
        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>Source-over composite of a (possibly translucent) layer on an opaque backdrop, in sRGB.</summary>
    public static SKColor Composite(SKColor source, SKColor backdrop)
    {
        var sourceAlpha = source.Alpha / 255d;
        var backdropAlpha = backdrop.Alpha / 255d;
        if (sourceAlpha >= 1)
        {
            return source;
        }

        var outAlpha = sourceAlpha + (backdropAlpha * (1 - sourceAlpha));
        if (outAlpha <= 0)
        {
            return SKColors.Transparent;
        }

        static byte Mix(byte over, byte under, double overAlpha, double underAlpha, double outAlpha)
            => (byte)Math.Clamp(Math.Round(((over * overAlpha) + (under * underAlpha * (1 - overAlpha))) / outAlpha), 0, 255);

        return new SKColor(
            Mix(source.Red, backdrop.Red, sourceAlpha, backdropAlpha, outAlpha),
            Mix(source.Green, backdrop.Green, sourceAlpha, backdropAlpha, outAlpha),
            Mix(source.Blue, backdrop.Blue, sourceAlpha, backdropAlpha, outAlpha),
            (byte)Math.Clamp(Math.Round(outAlpha * 255), 0, 255));
    }

    /// <summary>Measures <paramref name="text"/> against an effective background colour.</summary>
    public static MaterialContrastRow Measure(string role, string layer, SKColor text, SKColor background, double required) =>
        new(role, layer, text, background, Ratio(text, background), required);

    /// <summary>Checks every text role against L0 and the composited L1 of a token set.</summary>
    public static MaterialContrastReport Check(MaterialTextSet text, MaterialTokens tokens)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(tokens);
        var l0 = tokens.EffectiveColor(MaterialLayer.L0Container);
        var l1 = tokens.EffectiveColor(MaterialLayer.L1Card);
        return new MaterialContrastReport(
        [
            Measure("primary", "L0", text.Primary, l0, text.BodyMinimum),
            Measure("primary", "L1", text.Primary, l1, text.BodyMinimum),
            Measure("secondary", "L0", text.Secondary, l0, text.SecondaryMinimum),
            Measure("secondary", "L1", text.Secondary, l1, text.SecondaryMinimum),
            Measure("tertiary", "L0", text.Tertiary, l0, text.TertiaryMinimum),
            Measure("tertiary", "L1", text.Tertiary, l1, text.TertiaryMinimum),
        ]);
    }
}

/// <summary>
/// The complete L0–L4 material stack of one theme, with spec validation and the WCAG contrast self-check.
/// Tokens are pure data: paint construction lives in <see cref="IslandBrushes"/>, adoption in the renderer.
/// </summary>
public sealed record MaterialTokens
{
    /// <summary>Human readable name used by evidence and reports.</summary>
    public required string Name { get; init; }

    /// <summary>Theme mode the stack belongs to.</summary>
    public required IslandThemeMode Mode { get; init; }

    /// <summary>L0 container/base colour (also the backdrop every other layer composites onto).</summary>
    public required SKColor ContainerColor { get; init; }

    /// <summary>L1 card token.</summary>
    public required MaterialLayerToken L1 { get; init; }

    /// <summary>L2 separator token.</summary>
    public required MaterialLayerToken L2 { get; init; }

    /// <summary>L3 top highlight token.</summary>
    public required MaterialLayerToken L3 { get; init; }

    /// <summary>L4 pressed inner shadow token.</summary>
    public required MaterialLayerToken L4 { get; init; }

    /// <summary>Text roles checked against the stack.</summary>
    public required MaterialTextSet Text { get; init; }

    /// <summary>L0 token (the container has no geometry beyond its fill).</summary>
    public MaterialLayerToken L0 => new() { Layer = MaterialLayer.L0Container, Color = ContainerColor };

    /// <summary>All five layers in stack order.</summary>
    public IReadOnlyList<MaterialLayerToken> Layers => [L0, L1, L2, L3, L4];

    /// <summary>Layer lookup by id.</summary>
    public MaterialLayerToken this[MaterialLayer layer] => layer switch
    {
        MaterialLayer.L0Container => L0,
        MaterialLayer.L1Card => L1,
        MaterialLayer.L2Separator => L2,
        MaterialLayer.L3TopHighlight => L3,
        MaterialLayer.L4InnerShadow => L4,
        _ => throw new ArgumentOutOfRangeException(nameof(layer), layer, "Unknown material layer."),
    };

    /// <summary>Approved Obsidian material (the island default).</summary>
    public static MaterialTokens Dark { get; } = CreateDark();

    /// <summary>Porcelain material (light variant / high-contrast previews).</summary>
    public static MaterialTokens Light { get; } = CreateLight();

    /// <summary>Resolves the stack for a theme mode.</summary>
    public static MaterialTokens ForMode(IslandThemeMode mode) => mode == IslandThemeMode.Light ? Light : Dark;

    /// <summary>Builds a stack from a theme, so the renderer can keep one source of truth.</summary>
    public static MaterialTokens FromTheme(IslandTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var tokens = ForMode(theme.Mode);
        return tokens with { ContainerColor = theme.Background, L1 = tokens.L1 with { Color = WithAlpha(theme.BackgroundRaised, tokens.L1.Opacity) } };
    }

    /// <summary>Composites a layer over L0 and returns the effective opaque colour.</summary>
    public SKColor EffectiveColor(MaterialLayer layer)
    {
        var token = this[layer];
        return layer == MaterialLayer.L0Container
            ? ContainerColor
            : MaterialContrast.Composite(token.Color, ContainerColor);
    }

    /// <summary>Runs the WCAG self-check for body (≥4.5) and secondary (≥3.0) text on L0 and L1.</summary>
    public MaterialContrastReport CheckContrast() => MaterialContrast.Check(Text, this);

    /// <summary>Throws <see cref="MaterialContrastException"/> when the self-check fails.</summary>
    public MaterialContrastReport EnsureContrast() => CheckContrast().EnsureAllPass();

    /// <summary>Contrast ratio of one text colour on one layer (composited as needed).</summary>
    public double Contrast(SKColor text, MaterialLayer layer) => MaterialContrast.Ratio(text, EffectiveColor(layer));

    /// <summary>
    /// Validates every token against its <see cref="MaterialLayerSpec"/> plus the theme-specific colour rules
    /// (L0 near-black, L1 #141418, L2 white on Obsidian / near-black on Porcelain, no accents, L3 gradient).
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var issues = new List<string>();
        foreach (var token in Layers)
        {
            var spec = MaterialSpecs.For(token.Layer);
            var name = token.Layer.ToString();
            if (token.UsesAccent)
            {
                issues.Add($"{name}: material layers must not use the accent colour.");
            }

            if (token.Opacity < spec.MinOpacity - 0.0005 || token.Opacity > spec.MaxOpacity + 0.0005)
            {
                issues.Add($"{name}: opacity {token.Opacity:0.000} outside {spec.MinOpacity:0.00}–{spec.MaxOpacity:0.00}.");
            }

            if (token.CornerRadiusDip < spec.MinCornerRadiusDip - 0.001 || token.CornerRadiusDip > spec.MaxCornerRadiusDip + 0.001)
            {
                issues.Add($"{name}: corner radius {token.CornerRadiusDip:0.##} DIP outside {spec.MinCornerRadiusDip:0.##}–{spec.MaxCornerRadiusDip:0.##}.");
            }

            if (token.StrokeWidthDip < spec.MinStrokeWidthDip - 0.001 || token.StrokeWidthDip > spec.MaxStrokeWidthDip + 0.001)
            {
                issues.Add($"{name}: stroke width {token.StrokeWidthDip:0.##} DIP outside {spec.MinStrokeWidthDip:0.##}–{spec.MaxStrokeWidthDip:0.##}.");
            }

            if (token.HeightDip < spec.MinHeightDip - 0.001 || token.HeightDip > spec.MaxHeightDip + 0.001)
            {
                issues.Add($"{name}: height/inset {token.HeightDip:0.##} DIP outside {spec.MinHeightDip:0.##}–{spec.MaxHeightDip:0.##}.");
            }
        }

        if (Mode == IslandThemeMode.Dark)
        {
            if (!IsNearBlack(ContainerColor, 0x0A))
            {
                issues.Add($"L0: container must be near-black (#08080A); got {Hex(ContainerColor)}.");
            }

            if (!IsNear(L1.Color, 0x14, 0x14, 0x18, 3))
            {
                issues.Add($"L1: card must be #141418; got {Hex(L1.Color)}.");
            }

            if (!IsNear(L2.Color, 0xFF, 0xFF, 0xFF, 2))
            {
                issues.Add($"L2: separator must be white; got {Hex(L2.Color)}.");
            }
        }
        else
        {
            var luminance = MaterialContrast.RelativeLuminance(ContainerColor);
            if (luminance < 0.85)
            {
                issues.Add($"L0: light container must be near-white; got {Hex(ContainerColor)} (luminance {luminance:0.00}).");
            }

            if (MaterialContrast.RelativeLuminance(L2.Color) > 0.2)
            {
                issues.Add($"L2: light separator must be near-black; got {Hex(L2.Color)}.");
            }
        }

        if (MaterialContrast.RelativeLuminance(L3.Color) < 0.8)
        {
            issues.Add($"L3: top highlight must be white; got {Hex(L3.Color)}.");
        }

        if (!L3.UsesGradient || L3.GradientEndColor.Alpha != 0)
        {
            issues.Add("L3: the top highlight must be a gradient fading to transparent.");
        }

        if (MaterialContrast.RelativeLuminance(L4.Color) > 0.02)
        {
            issues.Add($"L4: inner shadow must be black; got {Hex(L4.Color)}.");
        }

        var contrast = CheckContrast();
        if (!contrast.AllPass)
        {
            issues.AddRange(contrast.Failures.Select(static failure => "contrast: " + failure));
        }

        return issues;
    }

    /// <summary>Formats a colour as <c>#AARRGGBB</c>.</summary>
    public static string Hex(SKColor color) => $"#{color.Alpha:X2}{color.Red:X2}{color.Green:X2}{color.Blue:X2}";

    /// <summary>Replaces a colour's alpha (opacity 0..1).</summary>
    public static SKColor WithAlpha(SKColor color, double opacity) =>
        new(color.Red, color.Green, color.Blue, (byte)Math.Clamp(Math.Round(opacity * 255), 0, 255));

    private static MaterialTokens CreateDark()
    {
        var container = IslandTheme.ParseColor("#08080A", SKColors.Black);
        return new MaterialTokens
        {
            Name = "Obsidian",
            Mode = IslandThemeMode.Dark,
            ContainerColor = container,
            L1 = new MaterialLayerToken
            {
                Layer = MaterialLayer.L1Card,
                Color = WithAlpha(IslandTheme.ParseColor("#141418", SKColors.Black), 0.60),
                CornerRadiusDip = 12,
            },
            L2 = new MaterialLayerToken
            {
                Layer = MaterialLayer.L2Separator,
                Color = WithAlpha(SKColors.White, 0.09),
                StrokeWidthDip = 1,
            },
            L3 = new MaterialLayerToken
            {
                Layer = MaterialLayer.L3TopHighlight,
                Color = WithAlpha(SKColors.White, 0.05),
                HeightDip = 1.5,
                UsesGradient = true,
                GradientEndColor = SKColors.Transparent,
            },
            L4 = new MaterialLayerToken
            {
                Layer = MaterialLayer.L4InnerShadow,
                Color = WithAlpha(SKColors.Black, 0.18),
                HeightDip = 8,
                BlurSigmaDip = 4,
            },
            Text = MaterialTextSet.Dark,
        };
    }

    private static MaterialTokens CreateLight()
    {
        var container = IslandTheme.ParseColor("#F5F6F8", SKColors.White);
        return new MaterialTokens
        {
            Name = "Porcelain",
            Mode = IslandThemeMode.Light,
            ContainerColor = container,
            L1 = new MaterialLayerToken
            {
                Layer = MaterialLayer.L1Card,
                Color = WithAlpha(SKColors.White, 0.60),
                CornerRadiusDip = 12,
            },
            L2 = new MaterialLayerToken
            {
                Layer = MaterialLayer.L2Separator,
                Color = WithAlpha(IslandTheme.ParseColor("#111827", SKColors.Black), 0.10),
                StrokeWidthDip = 1,
            },
            L3 = new MaterialLayerToken
            {
                Layer = MaterialLayer.L3TopHighlight,
                Color = WithAlpha(SKColors.White, 0.05),
                HeightDip = 1.5,
                UsesGradient = true,
                GradientEndColor = SKColors.Transparent,
            },
            L4 = new MaterialLayerToken
            {
                Layer = MaterialLayer.L4InnerShadow,
                Color = WithAlpha(SKColors.Black, 0.18),
                HeightDip = 8,
                BlurSigmaDip = 4,
            },
            Text = MaterialTextSet.Light,
        };
    }

    private static bool IsNearBlack(SKColor color, byte maximum) =>
        color.Red <= maximum && color.Green <= maximum && color.Blue <= maximum;

    private static bool IsNear(SKColor color, byte red, byte green, byte blue, int tolerance) =>
        Math.Abs(color.Red - red) <= tolerance && Math.Abs(color.Green - green) <= tolerance && Math.Abs(color.Blue - blue) <= tolerance;
}
