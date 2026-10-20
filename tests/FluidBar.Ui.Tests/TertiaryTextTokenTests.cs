using FluidBar.App.Settings;
using FluidBar.App.Theme;
using FluidBar.App.Views.Settings;
using FluidBar.Core.Layout;
using FluidBar.Island;
using Xunit;

namespace FluidBar.Ui.Tests;

/// <summary>
/// Task-36: the island and the settings window must never drift back to two different tertiary greys.
/// Every surface that paints "hint" text reads the single <see cref="TextTokens"/> constant.
/// </summary>
public sealed class TertiaryTextTokenTests
{
    [Fact]
    public void Island_and_settings_use_the_same_tertiary_text_token()
    {
        var dark = Avalonia.Media.Color.Parse(TextTokens.TertiaryDark);
        var light = Avalonia.Media.Color.Parse(TextTokens.TertiaryLight);

        Assert.Equal(dark, SettingsPalette.For(AppTheme.Dark).Tertiary);
        Assert.Equal(light, SettingsPalette.For(AppTheme.Light).Tertiary);
        Assert.Equal(dark, AppSurfaceColors.Dark.ForegroundTertiary);
        Assert.Equal(light, AppSurfaceColors.Light.ForegroundTertiary);
        Assert.Equal(dark, IslandColors.Obsidian.ForegroundTertiary);
        Assert.Equal(dark, IslandColors.ForPalette("graphite").ForegroundTertiary);
        Assert.Equal(dark, IslandColors.ForPalette("slate").ForegroundTertiary);
        Assert.Equal(dark, IslandColors.ForPalette("nord").ForegroundTertiary);
        Assert.Equal(dark, ((Avalonia.Media.ISolidColorBrush)IslandBrushes.Tertiary).Color);

        Assert.Equal(IslandTheme.ParseColor(TextTokens.TertiaryDark, default), IslandTheme.Dark.ForegroundTertiary);
        Assert.Equal(IslandTheme.ParseColor(TextTokens.TertiaryLight, default), IslandTheme.Light.ForegroundTertiary);
    }

}
