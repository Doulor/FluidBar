using Avalonia.Controls;
using Avalonia.VisualTree;
using FluidBar.App.Localization;
using FluidBar.App.Settings;
using FluidBar.App.Views.Settings;

namespace FluidBar.Ui.Tests;

public sealed class SettingsAppearanceOptionsTests
{
    [Theory]
    [InlineData("en-US")]
    [InlineData("zh-Hans")]
    public async Task AppearanceHasNoAccentSelectionOrArtworkSwitch(string culture)
    {
        await AvaloniaTest.Run(() =>
        {
            var localizer = TestFixtures.CreateLocalizer(culture);
            var (settings, _) = TestFixtures.CreateSettings(AppSettings.Default);
            var window = new SettingsWindow(settings, localizer, SettingsWindowSection.Appearance);
            window.Show();
            try
            {
                Assert.Empty(window.GetVisualDescendants().OfType<AccentSwatch>());
                var labels = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
                Assert.DoesNotContain(localizer.Get(LocKeys.Settings.Appearance.Accent.Title), labels);
                Assert.DoesNotContain(localizer.Get(LocKeys.Settings.Appearance.Accent.Artwork), labels);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void ExistingSettingsRemainReadableAfterRemovingAccentEditor()
    {
        var json = (AppSettings.Default with { AccentMode = "custom", AccentFallback = "#FF9500", Palette = "nord" }).ToJson();
        Assert.True(SettingsExport.TryParse(json, out var settings, out var error), error);
        Assert.Equal("nord", settings!.Palette);
    }
}
