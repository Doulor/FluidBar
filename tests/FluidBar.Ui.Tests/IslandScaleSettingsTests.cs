using Avalonia.Controls;
using Avalonia.VisualTree;
using FluidBar.App.Controls;
using FluidBar.App.Settings;
using FluidBar.App.Views.Settings;
using FluidBar.Core.Settings;
using SettingsJson = FluidBar.Core.Settings.SettingsJson;

namespace FluidBar.Ui.Tests;

public sealed class IslandScaleSettingsTests
{
    [Fact]
    public void IslandScaleSurvivesExportAndCorePersistenceIndependentlyOfFontScale()
    {
        var settings = AppSettings.TryParse("""
            {"schemaVersion":2,"appearance":{"islandScale":1.25,"fontScale":1.1}}
            """, out var error);
        Assert.NotNull(settings);
        Assert.Null(error);
        using var exported = System.Text.Json.JsonDocument.Parse(settings.ToJson());
        Assert.Equal(1.25, exported.RootElement.GetProperty("appearance").GetProperty("islandScale").GetDouble());
        var core = CoreSettingsStoreAdapter.ApplyToCore(new FluidBarSettings(), settings);
        Assert.True(SettingsJson.TryDeserialize(SettingsJson.Serialize(core), out var loaded, out _));
        var restored = CoreSettingsStoreAdapter.ToAppSettings(loaded!);
        Assert.Equal(1.25, restored.IslandScale);
        Assert.Equal(1.1, restored.FontScale);
    }

    [Theory]
    [InlineData("zh-Hans", "灵动岛大小")]
    [InlineData("en-US", "Island size")]
    public async Task AppearanceOffersAnIndependentLiveSizeSlider(string culture, string label)
    {
        await AvaloniaTest.Run(() =>
        {
            var (settings, store) = TestFixtures.CreateSettings(AppSettings.Default);
            var window = new SettingsWindow(settings, TestFixtures.CreateLocalizer(culture), SettingsWindowSection.Appearance);
            window.Show();
            try
            {
                var text = Assert.Single(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == label);
                var row = text.GetVisualAncestors().First(v => v.GetVisualDescendants().OfType<SettingsSlider>().Any());
                var slider = Assert.Single(row.GetVisualDescendants().OfType<SettingsSlider>());
                Assert.Equal(0.8, slider.Minimum);
                Assert.Equal(1.4, slider.Maximum);
                Assert.Equal(1, slider.Value);
                var x = (SettingsMetrics.ThumbSize / 2) +
                    ((1.25 - slider.Minimum) / (slider.Maximum - slider.Minimum) * (slider.Bounds.Width - SettingsMetrics.ThumbSize));
                typeof(SettingsSlider).GetMethod("UpdateFromPosition", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(slider, [x]);
                Assert.Equal(1, settings.Current.FontScale);
                Assert.Equal(1.25, store.LastSaved.IslandScale, 6);
                var preview = Assert.Single(window.GetVisualDescendants().OfType<IslandPreview>());
                Assert.Equal(1.25, preview.Model.IslandScale, 6);
                Assert.True(preview.Height >= SettingsMetrics.ApShellHeight * preview.Model.IslandScale);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void ExistingSettingsKeepTheDefaultIslandSize()
    {
        var parsed = AppSettings.TryParse("""{"schemaVersion":2,"appearance":{"fontScale":1.4}}""", out _)!;
        Assert.Equal(1, parsed.IslandScale);
        Assert.Equal(1.4, parsed.FontScale);
    }

    [Theory]
    [InlineData(-1, 0.8)]
    [InlineData(4, 1.4)]
    [InlineData(double.NaN, 1)]
    [InlineData(double.PositiveInfinity, 1)]
    public void InvalidIslandScaleIsNormalized(double value, double expected)
    {
        Assert.Equal(expected, (AppSettings.Default with { IslandScale = value }).Normalize().IslandScale);
    }
}
