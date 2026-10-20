using Avalonia.Headless;
using Avalonia.VisualTree;
using FluidBar.App;
using FluidBar.App.Localization;
using FluidBar.App.Views.Settings;

namespace FluidBar.Ui.Tests;

/// <summary>
/// Guards the Avalonia rendering policy. GPU is the default everywhere (task-48): only a per-pixel-alpha
/// surface gives the settings window its true rounded shape, so the CPU/software path is now opt-in
/// through the explicit override for memory-constrained runs.
/// </summary>
public sealed class AvaloniaRenderingPolicyTests
{
    [Theory]
    [InlineData("skia", null, false)]
    [InlineData("SKIA", null, false)]
    [InlineData(null, null, false)]
    [InlineData("avalonia", null, false)]
    [InlineData("skia", "gpu", false)]
    [InlineData("avalonia", "software", true)]
    [InlineData(null, "1", true)]
    [InlineData(null, "0", false)]
    public void Policy_uses_gpu_by_default_and_software_only_on_override(
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
