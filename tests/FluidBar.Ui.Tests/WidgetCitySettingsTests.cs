using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using FluidBar.App.Hosting.Skia;
using FluidBar.App.Localization;
using FluidBar.App.Settings;
using FluidBar.App.Views.Settings;
using FluidBar.Platform.Windows.Weather;

namespace FluidBar.Ui.Tests;

/// <summary>Weather city setting: persistence, the settings row, and the live hand-off to WeatherService.</summary>
public sealed class WidgetCitySettingsTests
{
    [Fact]
    public void WeatherCity_RoundTripsThroughTheSettingsJson_AndIsTrimmed()
    {
        var settings = (AppSettings.Default with { WeatherCity = "  Beijing  " }).Normalize();

        Assert.Equal("Beijing", settings.WeatherCity);
        var json = settings.ToJson();
        Assert.Contains("\"weatherCity\":\"Beijing\"", json, StringComparison.Ordinal);

        var parsed = AppSettings.TryParse(json, out var error);
        Assert.NotNull(parsed);
        Assert.Null(error);
        Assert.Equal("Beijing", parsed!.WeatherCity);

        var blank = (AppSettings.Default with { WeatherCity = "   " }).Normalize();
        Assert.Equal(string.Empty, blank.WeatherCity);
    }

    [Fact]
    public void WeatherCity_LongerThanTheCap_IsTruncated()
    {
        var settings = (AppSettings.Default with { WeatherCity = new string('x', 200) }).Normalize();

        Assert.Equal(AppSettings.MaxWeatherCityLength, settings.WeatherCity.Length);
    }

    [Fact]
    public async Task IslandPane_ShowsTheWeatherCityRow_AndPersistsTheTypedValue()
    {
        await AvaloniaTest.Run(() =>
        {
            var localizer = TestFixtures.CreateLocalizer("en-US");
            var (controller, _) = TestFixtures.CreateSettings(AppSettings.Default);
            var window = new SettingsWindow(controller, localizer, SettingsWindowSection.Island);
            window.Show();

            var placeholder = localizer.Get(LocKeys.Settings.Island.WeatherCityPlaceholder);
            var label = localizer.Get(LocKeys.Settings.Island.WeatherCity);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == label);

            var box = window.GetVisualDescendants().OfType<TextBox>()
                .First(candidate => candidate.Watermark == placeholder);
            box.Focus();
            box.Text = "Shanghai";
            // Enter commits; the same handler also commits when the field loses focus.
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);

            Assert.Equal("Shanghai", controller.Current.WeatherCity);
            window.Close();
        });
    }

    [Fact]
    public async Task IslandPane_ShowsTheWeatherCityRowInBothCultures()
    {
        await AvaloniaTest.Run(() =>
        {
            foreach (var culture in new[] { "zh-Hans", "en-US" })
            {
                var localizer = TestFixtures.CreateLocalizer(culture);
                var (controller, _) = TestFixtures.CreateSettings(AppSettings.Default);
                var window = new SettingsWindow(controller, localizer, SettingsWindowSection.Island);
                window.Show();
                var label = localizer.Get(LocKeys.Settings.Island.WeatherCity);
                Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == label);
                window.Close();
            }
        });
        await AvaloniaTest.Run(() => TestFixtures.Localizer.SetCulture("en-US"));
    }

    [Fact]
    public async Task Actions_TakeTheSettingsCity_AndFollowLaterChanges()
    {
        var settings = CreateSettings("Shanghai");
        await using var actions = new SkiaPlatformActions(settings, weatherCity: settings.Current.WeatherCity);

        Assert.Equal("Shanghai", actions.WeatherSource.CityOverride);

        settings.Update(s => s with { WeatherCity = "Osaka" });
        Assert.Equal("Osaka", actions.WeatherSource.CityOverride);

        settings.Update(s => s with { WeatherCity = string.Empty });
        Assert.Null(actions.WeatherSource.CityOverride);
        settings.Dispose();
    }

    private static SettingsController CreateSettings(string city)
    {
        var store = new InMemoryUiSettingsStore(AppSettings.Default with { WeatherCity = city });
        return new SettingsController(store.LastSaved, store, FluidBar.Core.Diagnostics.NullAppLogger.Instance);
    }
}
