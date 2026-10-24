using Avalonia.Media;
using FluidBar.App.Settings;
using FluidBar.App.Views.Settings;
using FluidBar.Core.Layout;

namespace FluidBar.Ui.Tests;

/// <summary>
/// The settings panel is drawn over the blurred desktop, which lifts its effective background. The dark theme
/// therefore needs brighter secondary/tertiary text than the opaque surface, or the small grey labels wash out
/// (user report 2026-10-24: "灰色的小字有点看不清"). The opaque palette must keep the shared tokens.
/// </summary>
public sealed class SettingsGlassPaletteTests
{
    [Fact]
    public void Glass_dark_theme_lifts_secondary_and_tertiary_text()
    {
        var opaque = SettingsPalette.For(AppTheme.Dark);
        var glass = SettingsPalette.For(AppTheme.Dark, onGlass: true);

        Assert.True(Luminance(glass.Secondary) > Luminance(opaque.Secondary) + 0.10,
            "secondary text must be clearly brighter over the glass surface");
        Assert.True(Luminance(glass.Tertiary) > Luminance(opaque.Tertiary) + 0.10,
            "tertiary text must be clearly brighter over the glass surface");
        Assert.True(Luminance(glass.Foreground) >= Luminance(glass.Secondary),
            "the hierarchy foreground > secondary must survive");
        Assert.True(Luminance(glass.Secondary) >= Luminance(glass.Tertiary),
            "the hierarchy secondary > tertiary must survive");
    }

    [Fact]
    public void Opaque_palette_keeps_the_shared_text_tokens()
    {
        Assert.Equal(Color.Parse(TextTokens.TertiaryDark),
            SettingsPalette.For(AppTheme.Dark).Tertiary);
        Assert.Equal(Color.Parse(TextTokens.TertiaryLight),
            SettingsPalette.For(AppTheme.Light).Tertiary);
    }

    [Fact]
    public void Glass_light_theme_darkens_its_greys()
    {
        // The glass panel is light, so the light theme's greys have to be darker than the opaque tokens; the other
        // way round from the dark theme (user report 2026-10-24: "浅色…灰色字看不清").
        var opaque = SettingsPalette.For(AppTheme.Light);
        var glass = SettingsPalette.For(AppTheme.Light, onGlass: true);

        Assert.True(Luminance(glass.Secondary) < Luminance(opaque.Secondary) - 0.05);
        Assert.True(Luminance(glass.Tertiary) < Luminance(opaque.Tertiary) - 0.05);
        Assert.True(Luminance(glass.Tertiary) < 0.45, "tertiary text must be dark enough for a light panel");
    }

    [Fact]
    public void Light_and_dark_glass_are_separate_materials()
    {
        var light = SettingsWindow.GlassMaterialFor(isLight: true);
        var dark = SettingsWindow.GlassMaterialFor(isLight: false);

        // The light values are the ones that were signed off; they must not drift when dark is tuned.
        Assert.Equal(0.34, light.TopAlpha, 3);
        Assert.Equal(0.48, light.BottomAlpha, 3);
        Assert.Equal(0.70, light.ScrimThreshold, 3);
        Assert.Equal(0.10, light.ScrimMaxAlpha, 3);
        Assert.Equal(0.45, light.LiftMaxAlpha, 3);

        Assert.True(dark.TopAlpha > light.TopAlpha + 0.20, "dark glass must be a materially different tint");
        Assert.True(dark.BottomAlpha > light.BottomAlpha + 0.20);
        Assert.Equal(0.0, dark.LiftMaxAlpha, 3);
        Assert.True(light.LiftMaxAlpha > 0.0, "only the light material lifts a dark backdrop");
        Assert.NotEqual(light, dark);
    }

    private static double Luminance(Color colour)
        => ((0.2126 * colour.R) + (0.7152 * colour.G) + (0.0722 * colour.B)) / 255.0;
}
