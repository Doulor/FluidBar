using FluidBar.App.Settings;
using FluidBar.App.Theme;
using FluidBar.App.Views.Settings;

namespace FluidBar.Ui.Tests;

/// <summary>
/// Material layer contract on the settings surfaces: L0 (container), L1 (card, 60 %), L2 (separator, 9 %)
/// must sit inside the iOS bands, so <c>AppSurfaceMaterial.Validate</c> reports zero deviations, and the
/// L3/L4 geometry is the documented 1.5 DIP / 8 DIP inset 4 sigma.
/// </summary>
public sealed class SettingsMaterialLayerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Settings_surfaces_have_no_material_deviations(bool light)
    {
        var palette = SettingsPalette.For(light ? AppTheme.Light : AppTheme.Dark);
        var issues = AppSurfaceMaterial.Validate(palette.ToSurfaces());
        Assert.Empty(issues);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Card_and_separator_match_the_material_bands(bool light)
    {
        var palette = SettingsPalette.For(light ? AppTheme.Light : AppTheme.Dark);
        var stack = AppSurfaceMaterial.FromSurfaces(palette.ToSurfaces());

        Assert.InRange(stack.L1.Opacity, 0.55, 0.65);
        Assert.Equal(12, stack.L1.CornerRadiusDip);
        Assert.InRange(stack.L2.Opacity, 0.08, 0.11);
        Assert.Equal(1, stack.L2.StrokeWidthDip);
        Assert.Equal(1.5, SettingsPalette.TopHighlightHeightDip);
        Assert.Equal(8, SettingsPalette.PressedInsetTopDip);
        Assert.Equal(4, SettingsPalette.PressedInsetSigmaDip);
    }
}
