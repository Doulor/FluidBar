using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using FluidBar.App.Settings;
using FluidBar.App.Views.Settings;

namespace FluidBar.Ui.Tests;

/// <summary>
/// The accent swatches have to show, and expose, which accent is active: a visible ring/check, a
/// TogglePattern state for UI Automation, and no misleading selection while "artwork" sampling is on.
/// </summary>
public sealed class AccentSwatchStateTests
{
    [Fact]
    public async Task Swatches_MarkExactlyOneSelection_AndExposeItToAutomation()
    {
        var result = await AvaloniaTest.Run(() =>
        {
            var localizer = TestFixtures.CreateLocalizer("en-US");
            var (controller, store) = TestFixtures.CreateSettings(
                AppSettings.Default with { AccentFallback = "#4CC9F0", AccentMode = "custom" });
            var window = new SettingsWindow(controller, localizer, SettingsWindowSection.Appearance);
            window.Show();
            try
            {
                var swatches = window.GetVisualDescendants().OfType<AccentSwatch>().ToArray();
                Assert.Equal(5, swatches.Length);
                Assert.Equal(1, swatches.Count(swatch => swatch.IsActive));
                Assert.True(swatches[0].IsActive, "the first swatch is the configured accent");

                var selected = swatches[0];
                var name = AutomationProperties.GetName(selected);
                Assert.NotNull(name);
                Assert.Contains("Selected", name);
                Assert.True(selected.IsChecked is true, "the selected swatch is checked (TogglePattern state)");

                foreach (var other in swatches.Skip(1))
                {
                    Assert.False(other.IsActive);
                    Assert.DoesNotContain("Selected", AutomationProperties.GetName(other) ?? string.Empty);
                }

                return swatches.Select(swatch => swatch.IsActive).ToArray();
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Equal([true, false, false, false, false], result);
    }

    [Fact]
    public async Task ClickingAnotherSwatch_MovesTheSelection_AndSwitchesModeToCustom()
    {
        var result = await AvaloniaTest.Run(() =>
        {
            var localizer = TestFixtures.CreateLocalizer("en-US");
            var (controller, _) = TestFixtures.CreateSettings(
                AppSettings.Default with { AccentFallback = "#4CC9F0", AccentMode = "artwork" });
            var window = new SettingsWindow(controller, localizer, SettingsWindowSection.Appearance);
            window.Show();
            try
            {
                var swatches = window.GetVisualDescendants().OfType<AccentSwatch>().ToArray();
                // Artwork sampling means the accent is not any swatch: nothing may look selected.
                Assert.DoesNotContain(true, swatches.Select(swatch => swatch.IsActive));

                swatches[2].Activate();
                var current = controller.Current;
                return (current.AccentFallback, current.AccentMode,
                    swatches.Select(swatch => swatch.IsActive).ToArray());
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Equal(Avalonia.Media.Color.Parse("#FF9500"), Avalonia.Media.Color.Parse(result.AccentFallback));
        Assert.Equal("custom", result.AccentMode);
        Assert.Equal([false, false, true, false, false], result.Item3);
    }
}
