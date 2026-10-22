using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using FluidBar.App.Localization;
using FluidBar.App.Settings;
using FluidBar.App.Views.Settings;

namespace FluidBar.Ui.Tests;

/// <summary>The Advanced pane's SafeMode switch must write through the settings controller.</summary>
public sealed class SettingsSafeModeTests
{
    [Fact]
    public async Task SafeModeSwitch_WritesTheSetting()
    {
        await AvaloniaTest.Run(() =>
        {
            var localizer = TestFixtures.CreateLocalizer("en-US");
            var (controller, _) = TestFixtures.CreateSettings(AppSettings.Default with
            {
                Advanced = new AdvancedSettings { SafeMode = false },
            });
            var window = new SettingsWindow(controller, localizer, SettingsWindowSection.Advanced);
            window.Show();

            var label = window.GetVisualDescendants().OfType<TextBlock>()
                .First(text => text.Text == localizer.Get(LocKeys.Settings.Advanced.SafeMode));
            var row = label.GetVisualAncestors().OfType<Control>()
                .First(ancestor => ancestor.GetVisualDescendants().OfType<SettingsSwitch>().Any());
            var toggle = row.GetVisualDescendants().OfType<SettingsSwitch>().First();
            var center = toggle.TranslatePoint(
                new Point(toggle.Bounds.Width / 2, toggle.Bounds.Height / 2), window)!.Value;

            window.MouseDown(center, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(center, MouseButton.Left, RawInputModifiers.None);

            Assert.True(controller.Current.Advanced.SafeMode);
            window.Close();
        });
    }
}
