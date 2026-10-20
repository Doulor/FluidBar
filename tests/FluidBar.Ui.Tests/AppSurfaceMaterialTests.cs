using FluidBar.App.Theme;
using FluidBar.App.Views.Settings;
using FluidBar.Island;
using SkiaSharp;

namespace FluidBar.Ui.Tests;

/// <summary>
/// Settings-side material contract: the window/panel surfaces map onto the shared L0–L4 vocabulary, the
/// settings text passes the WCAG self-check on L0/L1, and palette gaps are reported instead of hidden.
/// </summary>
public sealed class AppSurfaceMaterialTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Settings_text_passes_wcag_contrast_on_the_window_and_panel_layers(bool light)
    {
        var surfaces = light ? AppSurfaceColors.Light : AppSurfaceColors.Dark;
        var report = AppSurfaceMaterial.ContrastReport(surfaces);

        Assert.Equal(6, report.Rows.Count);
        Assert.True(report.AllPass, string.Join("; ", report.Failures));
        Assert.True(report.MinimumBody >= 4.5, $"body minimum {report.MinimumBody:0.00}");
        Assert.True(report.MinimumSecondary >= 3.0, $"secondary minimum {report.MinimumSecondary:0.00}");
        AppSurfaceMaterial.EnsureContrast(surfaces);
    }

    [Fact]
    public void Settings_surfaces_map_onto_the_five_material_layers()
    {
        var surfaces = AppSurfaceColors.Dark;
        var stack = AppSurfaceMaterial.FromSurfaces(surfaces);

        Assert.Equal(5, stack.Layers.Count);
        Assert.Equal([MaterialLayer.L0Container, MaterialLayer.L1Card, MaterialLayer.L2Separator, MaterialLayer.L3TopHighlight, MaterialLayer.L4InnerShadow],
            stack.Layers.Select(static layer => layer.Layer).ToArray());
        Assert.Equal(ToSkia(surfaces.Window), stack.L0.Color);
        Assert.Equal(surfaces.Surface.A, stack.L1.Color.Alpha);
        Assert.Equal(ToSkia(surfaces.Line), stack.L2.Color);
        Assert.Equal(1, stack.L2.StrokeWidthDip);
        Assert.Equal(12, stack.L1.CornerRadiusDip);
        Assert.Equal(stack.L1.Color, stack.EffectiveL1);
        Assert.Equal(MaterialLayer.L3TopHighlight, stack.L3.Layer);
        Assert.True(stack.L3.UsesGradient);
        Assert.Equal(MaterialLayer.L4InnerShadow, stack.L4.Layer);
    }

    [Fact]
    public void Settings_palette_gaps_are_reported_but_never_break_the_text_contrast()
    {
        var issues = AppSurfaceMaterial.Validate(AppSurfaceColors.Dark);

        // The settings hairlines are ~7 % white — below the iOS 8–11 % band — and that must be reported.
        Assert.Contains(issues, static issue => issue.StartsWith("L2:", StringComparison.Ordinal));
        Assert.DoesNotContain(issues, static issue => issue.StartsWith("contrast:", StringComparison.Ordinal));
    }

    [Fact]
    public void Contrast_report_renders_as_a_markdown_table()
    {
        var markdown = AppSurfaceMaterial.ToMarkdown(AppSurfaceMaterial.ContrastReport(AppSurfaceColors.Light), "Porcelain settings");

        Assert.Contains("### Porcelain settings", markdown, StringComparison.Ordinal);
        Assert.Contains("| primary | L0 |", markdown, StringComparison.Ordinal);
        Assert.Contains("PASS", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("FAIL", markdown, StringComparison.Ordinal);
    }

    private static SKColor ToSkia(Avalonia.Media.Color color) => new(color.R, color.G, color.B, color.A);
}
