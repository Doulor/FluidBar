using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;
using FluidBar.App.Settings;
using FluidBar.App.Views.Settings;

namespace FluidBar.Ui.Tests;

/// <summary>
/// Material interaction states. Avalonia 12 does not render BoxShadow.IsInset, so L4 is a real overlay
/// band inside the card (black 18 % at the top, 8 DIP, fading down). These tests pin the overlay
/// geometry/colour and that the forced states switch it on and off.
/// </summary>
public sealed class SettingsMaterialStateTests
{
    [Fact]
    public async Task Pressed_state_shows_the_L4_overlay_band()
    {
        var result = await AvaloniaTest.Run(() =>
        {
            var localizer = TestFixtures.CreateLocalizer("en-US");
            var (controller, _) = TestFixtures.CreateSettings(AppSettings.Default);
            var window = new SettingsWindow(controller, localizer, SettingsWindowSection.Plugins);
            window.Show();
            try
            {
                var cards = window.ForceMaterialState(SettingsWindow.SettingsMaterialVisualState.Pressed);
                var overlays = window.GetVisualDescendants().OfType<Border>()
                    .Where(border => border.Name == "MaterialPressedInset").ToArray();
                Assert.True(cards > 0);
                Assert.NotEmpty(overlays);
                foreach (var overlay in overlays)
                {
                    Assert.Equal(1, overlay.Opacity);
                    Assert.Equal(SettingsPalette.PressedInsetTopDip, overlay.Height);
                }

                var gradient = Assert.IsType<LinearGradientBrush>(overlays[0].Background);
                Assert.Equal(Color.FromArgb(46, 0, 0, 0), gradient.GradientStops[0].Color);
                Assert.Equal(0, gradient.GradientStops[1].Color.A);

                window.ForceMaterialState(SettingsWindow.SettingsMaterialVisualState.Idle);
                Assert.All(overlays, overlay => Assert.Equal(0, overlay.Opacity));
                return true;
            }
            finally
            {
                window.Close();
            }
        });

        Assert.True(result);
    }

    [Fact]
    public async Task Hover_state_uses_the_soft_outer_glow()
    {
        var glow = await AvaloniaTest.Run(() =>
        {
            var localizer = TestFixtures.CreateLocalizer("en-US");
            var (controller, _) = TestFixtures.CreateSettings(AppSettings.Default);
            var window = new SettingsWindow(controller, localizer, SettingsWindowSection.Plugins);
            window.Show();
            try
            {
                _ = window.ForceMaterialState(SettingsWindow.SettingsMaterialVisualState.Hover);
                var card = window.GetVisualDescendants().OfType<Border>()
                    .First(border => border.Child is Grid && border.BorderThickness.Left == 1
                        && border.CornerRadius.TopLeft > 0 && border.BoxShadow.Count > 0);
                return card.BoxShadow[0];
            }
            finally
            {
                window.Close();
            }
        });

        Assert.False(glow.IsInset);
        Assert.Equal(10, glow.Blur);
        Assert.Equal(Color.FromArgb(20, 255, 255, 255), glow.Color);
    }

    [Fact]
    public async Task Row_press_scales_without_reflowing_the_card()
    {
        var result = await AvaloniaTest.Run(() =>
        {
            var localizer = TestFixtures.CreateLocalizer("en-US");
            var (controller, _) = TestFixtures.CreateSettings(AppSettings.Default);
            var window = new SettingsWindow(controller, localizer, SettingsWindowSection.Plugins);
            window.Show();
            try
            {
                var row = window.GetVisualDescendants().OfType<Border>()
                    .First(border => border.Child is Grid stack
                        && stack.Children.OfType<Border>().Any(child => child.Name == "MaterialPressHighlight"));
                var idleHeight = row.Bounds.Height;

                _ = window.ForceMaterialState(SettingsWindow.SettingsMaterialVisualState.Pressed);
                var highlight = ((Grid)row.Child).Children.OfType<Border>()
                    .Single(child => child.Name == "MaterialPressHighlight");
                var scale = Assert.IsType<ScaleTransform>(row.RenderTransform);
                Assert.Equal(0.99, scale.ScaleX, 3);
                Assert.Equal(0.99, scale.ScaleY, 3);
                Assert.Equal(1, highlight.Opacity);
                Assert.Equal(1.5, highlight.Height);
                // Draw-only scale: the layout height must not change (no reflow, no scroll jump).
                Assert.Equal(idleHeight, row.Bounds.Height, 3);

                _ = window.ForceMaterialState(SettingsWindow.SettingsMaterialVisualState.Idle);
                Assert.Null(row.RenderTransform);
                Assert.Equal(0, highlight.Opacity);
                return true;
            }
            finally
            {
                window.Close();
            }
        });

        Assert.True(result);
    }

    [Fact]
    public async Task Tiles_do_not_get_the_press_scale()
    {
        var result = await AvaloniaTest.Run(() =>
        {
            var localizer = TestFixtures.CreateLocalizer("en-US");
            var (controller, _) = TestFixtures.CreateSettings(AppSettings.Default);
            var window = new SettingsWindow(controller, localizer, SettingsWindowSection.Marketplace);
            window.Show();
            try
            {
                _ = window.ForceMaterialState(SettingsWindow.SettingsMaterialVisualState.Pressed);
                var scaled = window.GetVisualDescendants().OfType<Border>()
                    .Count(border => border.RenderTransform is ScaleTransform);
                return scaled == 0;
            }
            finally
            {
                window.Close();
            }
        });

        Assert.True(result, "only Row cards may scale on press");
    }
}
