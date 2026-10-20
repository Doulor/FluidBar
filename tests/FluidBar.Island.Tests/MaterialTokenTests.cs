using FluidBar.Island;
using SkiaSharp;

namespace FluidBar.Island.Tests;

/// <summary>
/// Contract tests for the five material layers of docs/ios-design-study.md §2: L0 container, L1 card,
/// L2 separator, L3 top highlight, L4 pressed inset — plus the WCAG contrast self-check and the Skia paint
/// behaviour each layer promises.
/// </summary>
public sealed class MaterialTokenTests
{
    [Fact]
    public void L0_container_is_near_black_opaque_and_never_carries_a_gradient_or_accent()
    {
        var tokens = MaterialTokens.Dark;
        var l0 = tokens.L0;

        Assert.Equal(MaterialLayer.L0Container, l0.Layer);
        Assert.Equal(new SKColor(0x08, 0x08, 0x0A), l0.Color);
        Assert.Equal(255, l0.Color.Alpha);
        Assert.Equal(1.0, l0.Opacity, 3);
        Assert.False(l0.UsesGradient);
        Assert.False(l0.UsesAccent);
        Assert.Empty(tokens.Validate());

        // The paint factory must not attach a shader to the container fill.
        using var paint = IslandMaterialBrushes.CreateContainerFill(tokens);
        Assert.Null(paint.Shader);
        Assert.Equal(SKPaintStyle.Fill, paint.Style);
        Assert.Equal(tokens.ContainerColor, paint.Color);
    }

    [Fact]
    public void L1_card_is_141418_at_55_to_65_percent_with_radius_12()
    {
        var tokens = MaterialTokens.Dark;
        var l1 = tokens.L1;
        var spec = MaterialSpecs.For(MaterialLayer.L1Card);

        Assert.Equal(MaterialLayer.L1Card, l1.Layer);
        Assert.Equal(0x14, l1.Color.Red);
        Assert.Equal(0x14, l1.Color.Green);
        Assert.Equal(0x18, l1.Color.Blue);
        Assert.InRange(l1.Opacity, spec.MinOpacity, spec.MaxOpacity);
        Assert.InRange(l1.Opacity, 0.55, 0.65);
        Assert.Equal(12, l1.CornerRadiusDip);

        // Composited on L0 the card is a visibly lighter surface than the container.
        var effective = tokens.EffectiveColor(MaterialLayer.L1Card);
        Assert.True(MaterialContrast.RelativeLuminance(effective) > MaterialContrast.RelativeLuminance(tokens.ContainerColor));
        Assert.InRange(effective.Red, 0x0E, 0x11);
        Assert.InRange(effective.Green, 0x0E, 0x11);
        Assert.InRange(effective.Blue, 0x11, 0x14);
    }

    [Fact]
    public void L2_separator_is_white_8_to_11_percent_one_dip()
    {
        var tokens = MaterialTokens.Dark;
        var l2 = tokens.L2;
        var spec = MaterialSpecs.For(MaterialLayer.L2Separator);

        Assert.Equal(MaterialLayer.L2Separator, l2.Layer);
        Assert.Equal(0xFF, l2.Color.Red);
        Assert.Equal(0xFF, l2.Color.Green);
        Assert.Equal(0xFF, l2.Color.Blue);
        Assert.InRange(l2.Opacity, spec.MinOpacity, spec.MaxOpacity);
        Assert.Equal(1, l2.StrokeWidthDip);

        using var paint = IslandMaterialBrushes.CreateSeparatorStroke(tokens);
        Assert.Equal(SKPaintStyle.Stroke, paint.Style);
        Assert.Equal(1f, paint.StrokeWidth);
    }

    [Fact]
    public void L3_top_highlight_is_white_4_to_6_percent_one_to_two_dip_gradient_to_transparent()
    {
        var tokens = MaterialTokens.Dark;
        var l3 = tokens.L3;
        var spec = MaterialSpecs.For(MaterialLayer.L3TopHighlight);

        Assert.Equal(MaterialLayer.L3TopHighlight, l3.Layer);
        Assert.Equal(0xFF, l3.Color.Red);
        Assert.InRange(l3.Opacity, spec.MinOpacity, spec.MaxOpacity);
        Assert.InRange(l3.HeightDip, 1.0, 2.0);
        Assert.True(l3.UsesGradient);
        Assert.Equal(0, l3.GradientEndColor.Alpha);

        using var paint = IslandMaterialBrushes.CreateTopHighlight(tokens, SKRect.Create(0, 20, 200, 40));
        Assert.NotNull(paint.Shader);
    }

    [Fact]
    public void L4_inner_shadow_is_black_15_to_20_percent_with_top_inset_6_to_10_dip()
    {
        var tokens = MaterialTokens.Dark;
        var l4 = tokens.L4;
        var spec = MaterialSpecs.For(MaterialLayer.L4InnerShadow);

        Assert.Equal(MaterialLayer.L4InnerShadow, l4.Layer);
        Assert.Equal(SKColors.Black.Red, l4.Color.Red);
        Assert.Equal(SKColors.Black.Green, l4.Color.Green);
        Assert.Equal(SKColors.Black.Blue, l4.Color.Blue);
        Assert.InRange(l4.Opacity, spec.MinOpacity, spec.MaxOpacity);
        Assert.InRange(l4.HeightDip, 6.0, 10.0);
        Assert.True(l4.BlurSigmaDip > 0);

        using var paint = IslandMaterialBrushes.CreatePressedInset(tokens);
        Assert.NotNull(paint.MaskFilter);
    }

    [Fact]
    public void Contrast_self_check_keeps_body_at_4_5_and_secondary_at_3_on_L0_and_L1()
    {
        var tokens = MaterialTokens.Dark;
        var report = tokens.CheckContrast();

        Assert.Equal(6, report.Rows.Count);
        Assert.True(report.AllPass, string.Join("; ", report.Failures));
        Assert.True(report.MinimumBody >= 4.5, $"body minimum {report.MinimumBody:0.00}");
        Assert.True(report.MinimumSecondary >= 3.0, $"secondary minimum {report.MinimumSecondary:0.00}");

        // Reference values from the study: primary ≈17:1 and secondary ≈8:1 on #08080A.
        Assert.InRange(tokens.Contrast(tokens.Text.Primary, MaterialLayer.L0Container), 15.0, 19.0);
        Assert.InRange(tokens.Contrast(tokens.Text.Secondary, MaterialLayer.L0Container), 7.0, 9.0);
        Assert.True(tokens.Contrast(tokens.Text.Tertiary, MaterialLayer.L0Container) >= 4.5);

        // The same text on the composited card stays comfortably above the thresholds.
        Assert.True(tokens.Contrast(tokens.Text.Primary, MaterialLayer.L1Card) >= 4.5);
        Assert.True(tokens.Contrast(tokens.Text.Secondary, MaterialLayer.L1Card) >= 3.0);
    }

    [Fact]
    public void Contrast_check_reports_every_row_and_fails_closed()
    {
        var tokens = MaterialTokens.Dark;
        var lowContrast = tokens with { Text = new MaterialTextSet(new SKColor(0x12, 0x12, 0x14), SKColors.DimGray, SKColors.DimGray) };
        var report = lowContrast.CheckContrast();

        Assert.False(report.AllPass);
        Assert.NotEmpty(report.Failures);
        Assert.Throws<MaterialContrastException>(() => lowContrast.EnsureContrast());
        Assert.Equal(6, report.Rows.Count);
        Assert.All(report.Rows, row => Assert.True(row.Ratio >= 1.0));
    }

    [Fact]
    public void Both_themes_validate_against_the_spec_and_contrast_bounds()
    {
        Assert.Empty(MaterialTokens.Dark.Validate());
        Assert.Empty(MaterialTokens.Light.Validate());
        Assert.True(MaterialTokens.Light.CheckContrast().AllPass);
        Assert.NotEqual(MaterialTokens.Dark.ContainerColor, MaterialTokens.Light.ContainerColor);
        Assert.Equal(IslandThemeMode.Light, MaterialTokens.Light.Mode);
        Assert.Equal(MaterialTokens.Dark.L1.Opacity, MaterialTokens.Light.L1.Opacity, 6);
    }

    [Fact]
    public void Validation_flags_a_token_outside_its_spec_band()
    {
        var broken = MaterialTokens.Dark with
        {
            L1 = MaterialTokens.Dark.L1 with { Color = MaterialTokens.WithAlpha(new SKColor(0x14, 0x14, 0x18), 0.90), CornerRadiusDip = 4 },
        };

        var issues = broken.Validate();
        Assert.Contains(issues, issue => issue.Contains("L1", StringComparison.Ordinal) && issue.Contains("opacity", StringComparison.Ordinal));
        Assert.Contains(issues, issue => issue.Contains("L1", StringComparison.Ordinal) && issue.Contains("corner radius", StringComparison.Ordinal));
    }

    [Fact]
    public void Paint_set_draws_the_layers_with_the_expected_pixels()
    {
        var tokens = MaterialTokens.Dark;
        using var paintSet = new IslandMaterialPaintSet(tokens);
        var card = SKRect.Create(10, 10, 140, 60);

        // L0 + L1: the card composite on top of the container, and the container itself.
        using var baseline = Render(tokens, canvas =>
        {
            paintSet.DrawCard(canvas, card);
        });
        Assert.Equal(tokens.ContainerColor, baseline.GetPixel(2, 2));
        var centre = baseline.GetPixel(80, 55);
        var effective = tokens.EffectiveColor(MaterialLayer.L1Card);

        // Skia composites in premultiplied 8-bit space, so allow a 1–2 step rounding delta per channel.
        Assert.InRange(centre.Red, effective.Red - 2, effective.Red + 2);
        Assert.InRange(centre.Green, effective.Green - 2, effective.Green + 2);
        Assert.InRange(centre.Blue, effective.Blue - 2, effective.Blue + 2);
        Assert.InRange(MaterialContrast.RelativeLuminance(centre), 0.002, 0.01);

        // L2: a 1 DIP separator is lighter than the card surface.
        using var withSeparator = Render(tokens, canvas =>
        {
            paintSet.DrawCard(canvas, card);
            paintSet.DrawSeparator(canvas, card.Left, card.Right, 40);
        });
        Assert.True(
            MaterialContrast.RelativeLuminance(withSeparator.GetPixel(80, 40)) > MaterialContrast.RelativeLuminance(centre),
            "L2 separator must be lighter than the card");

        // L3: the top edge is lighter than the card body (highlight only, no pressed inset).
        using var withHighlight = Render(tokens, canvas =>
        {
            paintSet.DrawCard(canvas, card);
            paintSet.DrawTopHighlight(canvas, card);
        });
        Assert.True(
            MaterialContrast.RelativeLuminance(withHighlight.GetPixel(80, 10)) > MaterialContrast.RelativeLuminance(centre),
            "L3 must lighten the top edge");
        Assert.Equal(centre, withHighlight.GetPixel(80, 30));

        // L4: the pressed inset darkens the top band and leaves the body untouched.
        using var pressed = Render(tokens, canvas =>
        {
            paintSet.DrawCard(canvas, card);
            paintSet.DrawPressedInset(canvas, card);
        });
        Assert.True(
            MaterialContrast.RelativeLuminance(pressed.GetPixel(80, 12)) < MaterialContrast.RelativeLuminance(centre),
            "L4 must darken the top inset");
        Assert.Equal(centre, pressed.GetPixel(80, 55));
    }

    private static SKBitmap Render(MaterialTokens tokens, Action<SKCanvas> draw)
    {
        var bitmap = new SKBitmap(new SKImageInfo(160, 80, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(tokens.ContainerColor);
        draw(canvas);
        canvas.Flush();
        return bitmap;
    }

    [Fact]
    public void Island_colour_accessors_expose_the_stack_and_descriptions()
    {
        Assert.Equal(MaterialTokens.Dark.ContainerColor, IslandMaterialColors.Container);
        Assert.Equal(MaterialTokens.Dark.L1.Color, IslandMaterialColors.Card);
        Assert.Equal(MaterialTokens.Dark.L2.Color, IslandMaterialColors.Separator);
        Assert.Equal(MaterialTokens.Dark.L3.Color, IslandMaterialColors.TopHighlight);
        Assert.Equal(MaterialTokens.Dark.L4.Color, IslandMaterialColors.InnerShadow);
        Assert.Equal(MaterialTokens.Dark.Text.Primary, IslandMaterialColors.TextPrimary);
        Assert.True(IslandMaterialColors.ContrastReport.AllPass);
        Assert.Empty(IslandMaterialColors.Validate());
        Assert.Contains("L1 card", IslandMaterialColors.Describe(MaterialLayer.L1Card), StringComparison.Ordinal);
        Assert.Contains("radius 12", IslandMaterialColors.Describe(MaterialLayer.L1Card), StringComparison.Ordinal);
    }

    [Fact]
    public void Material_tokens_can_be_derived_from_an_island_theme()
    {
        var fromDark = MaterialTokens.FromTheme(IslandTheme.Dark);
        Assert.Equal(IslandTheme.Dark.Background, fromDark.ContainerColor);
        Assert.Equal(0x14, fromDark.L1.Color.Red);
        Assert.Equal(MaterialTokens.Dark.L1.Opacity, fromDark.L1.Opacity, 6);

        var fromLight = MaterialTokens.FromTheme(IslandTheme.Light);
        Assert.Equal(IslandTheme.Light.Background, fromLight.ContainerColor);
    }
}
