using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using FluidBar.App.Localization;
using FluidBar.App.Settings;
using FluidBar.App.Views.Settings;
using FluidBar.Core.Configuration;

namespace FluidBar.Ui.Tests;

/// <summary>
/// Regression coverage for the advanced-pane reset: typing RESET arms the chip, clicking it applies
/// the defaults, and any other text keeps the chip disabled. Both paths used to be broken (the chip
/// listened to TextChanged, which Avalonia does not raise for every text mutation, and the click
/// handler was appended after SmallAction had already marked the press handled).
/// </summary>
public sealed class SettingsResetArmingTests
{
    [Fact]
    public async Task ResetChip_ArmsAndResetsOnlyAfterResetIsTyped()
    {
        await AvaloniaTest.Run(() =>
        {
            var localizer = TestFixtures.CreateLocalizer("en-US");
            var (controller, _) = TestFixtures.CreateSettings(AppSettings.Default with
            {
                Palette = "nord",
                PanelRadius = 34,
                Mode = "flush",
                PowerMode = IslandPowerMode.Saver,
                LyricOffsetMs = 300,
                Advanced = new AdvancedSettings { FrameStats = true, SafeMode = true },
            });
            var window = new SettingsWindow(controller, localizer, SettingsWindowSection.Advanced);
            window.Show();

            var textBox = window.GetVisualDescendants().OfType<TextBox>()
                .First(box => box.Watermark?.Contains("RESET", StringComparison.OrdinalIgnoreCase) == true);
            var resetLabel = localizer.Get(LocKeys.Settings.Advanced.Reset);
            var label = window.GetVisualDescendants().OfType<TextBlock>().First(text => text.Text == resetLabel);
            var chip = label.GetVisualAncestors().OfType<Button>().First();

            Assert.False(chip.IsEnabled);

            textBox.Focus();
            window.KeyTextInput("RESET");
            Assert.Equal("RESET", textBox.Text);
            Assert.True(chip.IsEnabled);
            Assert.Equal(1.0, chip.Opacity);

            var center = chip.TranslatePoint(new Point(chip.Bounds.Width / 2, chip.Bounds.Height / 2), window)!.Value;
            window.MouseMove(center, RawInputModifiers.None);
            window.MouseDown(center, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(center, MouseButton.Left, RawInputModifiers.None);

            Assert.Equal("obsidian", controller.Current.Palette);
            Assert.Equal(30, Math.Round(controller.Current.PanelRadius));
            Assert.Equal("float", controller.Current.Mode);
            Assert.Equal(IslandPowerMode.Performance, controller.Current.PowerMode);
            Assert.Equal(0, controller.Current.LyricOffsetMs);
            Assert.False(controller.Current.Advanced.FrameStats);
            Assert.False(controller.Current.Advanced.SafeMode);

            // Wrong confirmation text keeps the chip disabled.
            textBox.Text = "reset";
            Assert.False(chip.IsEnabled);

            window.Close();
        });
    }
}
