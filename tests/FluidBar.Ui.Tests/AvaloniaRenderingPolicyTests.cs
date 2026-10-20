using Avalonia.Headless;
using Avalonia.VisualTree;
using FluidBar.App;
using FluidBar.App.Localization;
using FluidBar.App.Views.Settings;

namespace FluidBar.Ui.Tests;

/// <summary>
/// Guards for the skia-mode Avalonia rendering policy (task-37): the settings/market window is the
/// only Avalonia surface in skia-host mode, so it renders on the CPU and never loads the GPU stack;
/// the legacy Avalonia island host keeps the platform default.
/// </summary>
public sealed class AvaloniaRenderingPolicyTests
{
    [Theory]
    [InlineData("skia", null, true)]
    [InlineData("SKIA", null, true)]
    [InlineData(null, null, true)]
    [InlineData("avalonia", null, false)]
    [InlineData("skia", "gpu", false)]
    [InlineData("avalonia", "software", true)]
    [InlineData(null, "1", true)]
    [InlineData(null, "0", false)]
    public void Policy_resolves_software_for_skia_and_platform_default_for_avalonia(
        string? host, string? overrideMode, bool expected)
        => Assert.Equal(expected, AvaloniaRenderingPolicy.ShouldUseSoftwareRendering(host, overrideMode));

    [Fact]
    public async Task Settings_window_still_builds_and_renders_with_the_software_policy()
    {
        await AvaloniaTest.Run(() =>
        {
            // The headless backend is Avalonia's CPU rasterizer, i.e. exactly what the policy selects
            // for the real settings window; constructing and showing the window proves it stays usable.
            var (controller, _) = TestFixtures.CreateSettings();
            var localizer = TestFixtures.CreateLocalizer("en-US");
            var window = new SettingsWindow(controller, localizer, SettingsWindowSection.General);
            window.Show();
            try
            {
                var texts = window.GetVisualDescendants().OfType<Avalonia.Controls.TextBlock>()
                    .Select(block => block.Text)
                    .Where(text => !string.IsNullOrEmpty(text))
                    .ToArray();
                Assert.NotEmpty(texts);
                Assert.Contains(texts, text => text!.Contains("FluidBar", StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                window.Close();
            }
        });
    }
}
