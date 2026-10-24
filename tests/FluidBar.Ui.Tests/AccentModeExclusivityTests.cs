using Avalonia.Controls;
using Avalonia.VisualTree;
using FluidBar.App.Localization;
using FluidBar.App.Settings;
using FluidBar.App.Views.Settings;
using Xunit;

namespace FluidBar.Ui.Tests;

/// <summary>
/// The custom swatches and the "accent from artwork" switch are mutually exclusive: only one of them can be the
/// accent, so choosing artwork must clear the swatch's selected ring immediately (user report 2026-10-24: the ring
/// stayed until the page was rebuilt by navigating away and back).
/// </summary>
public sealed class AccentModeExclusivityTests
{
    [Fact]
    public async Task Choosing_the_artwork_accent_clears_the_selected_swatch()
    {
        var state = await AvaloniaTest.Run(() =>
        {
            var localizer = TestFixtures.CreateLocalizer("en-US");
            var (controller, _) = TestFixtures.CreateSettings(
                AppSettings.Default with { AccentMode = "custom", AccentFallback = "#4CC9F0" });
            var context = new SettingsWindowContext
            {
                Settings = controller,
                Localizer = localizer,
                Marketplace = null!,
                Plugins = null!,
                Palette = SettingsPalette.For(AppTheme.Dark),
                Owner = static () => null,
                OpenPath = static _ => { },
                ShowStatus = static _ => { },
            };

            var root = SettingsSectionBuilder.Build(SettingsWindowSection.Appearance, context);
            var swatches = root.GetVisualDescendants().OfType<AccentSwatch>().ToList();
            var customActive = swatches.Count(swatch => swatch.IsActive);

            // Flip to the artwork accent the way the toggle row does.
            controller.Update(settings => settings with { AccentMode = "artwork" });
            var activeAfterArtwork = swatches.Count(swatch => swatch.IsActive);

            // And back: the same swatch must light up again.
            controller.Update(settings => settings with { AccentMode = "custom" });
            var activeAfterCustom = swatches.Count(swatch => swatch.IsActive);

            return (swatches.Count, customActive, activeAfterArtwork, activeAfterCustom);
        });

        Assert.True(state.Count >= 5, "the appearance section must build the accent swatches");
        Assert.Equal(1, state.customActive);
        Assert.Equal(0, state.activeAfterArtwork);
        Assert.Equal(1, state.activeAfterCustom);
    }
}
