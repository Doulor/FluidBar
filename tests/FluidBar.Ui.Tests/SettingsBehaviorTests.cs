using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using FluidBar.App.Localization;
using FluidBar.App.Settings;
using FluidBar.App.Views.Settings;
using FluidBar.Core.Layout;
using FluidBar.Core.Settings;
using AppSettings = FluidBar.App.Settings.AppSettings;
using AppTheme = FluidBar.App.Settings.AppTheme;
using IslandMaterial = FluidBar.App.Settings.IslandMaterial;
using SettingsJson = FluidBar.Core.Settings.SettingsJson;
using FluidBar.Core.Configuration;

namespace FluidBar.Ui.Tests;

public sealed class SettingsBehaviorTests
{
    [Fact]
    public async Task NewInstallEnablesAgentThroughProductionSettingsStore()
    {
        var directory = Path.Combine(Path.GetTempPath(), "fluidbar-default-agent-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var store = CoreSettingsStoreAdapter.CreateDefault(appDataDirectory: directory);
            var settings = await store.LoadAsync();
            Assert.Contains("fluidbar-agent", settings.Plugins.Enabled);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void ExplicitlyDisabledAgentStaysDisabledAfterSettingsRoundTrip()
    {
        var settings = AppSettings.Default with
        {
            Plugins = AppSettings.Default.Plugins with { Enabled = ["fluidbar-media"] },
        };
        Assert.DoesNotContain("fluidbar-agent", RoundTrip(settings).Plugins.Enabled);
    }

    [Theory]
    [InlineData(nameof(AppSettings.PanelRadius))]
    [InlineData(nameof(AppSettings.PowerMode))]
    [InlineData(nameof(AppSettings.LyricOffsetMs))]
    [InlineData(nameof(AppSettings.TabBarStyle))]
    [InlineData(nameof(AppSettings.RailSide))]
    [InlineData(nameof(AppSettings.WeatherCity))]
    [InlineData(nameof(AppSettings.HotkeyToggle))]
    [InlineData(nameof(AppSettings.HotkeyHide))]
    [InlineData(nameof(AppSettings.HotkeySettings))]
    public void EditableSettingsSurviveCoreSerialization(string propertyName)
    {
        var initial = AppSettings.Default with
        {
            PanelRadius = 35, PowerMode = IslandPowerMode.Saver, LyricOffsetMs = 350,
            TabBarStyle = TabBarStyle.BottomPreview, RailSide = PageRailSide.Right,
            WeatherCity = "Shanghai", HotkeyToggle = "Ctrl+Shift+F10",
            HotkeyHide = "Ctrl+Shift+F11", HotkeySettings = "Ctrl+Shift+F12",
        };
        var reloaded = RoundTrip(initial);
        var property = typeof(AppSettings).GetProperty(propertyName)!;
        Assert.Equal(property.GetValue(initial), property.GetValue(reloaded));
    }

    [Fact]
    public void PageOrderAndMonitorPlacementSurviveCoreSerialization()
    {
        var initial = AppSettings.Default.WithMonitorPlacement("display-1", IslandAnchor.BottomRight, 70, 90) with
        {
            Plugins = AppSettings.Default.Plugins with { PageOrder = ["builtin.media", "builtin.home"] },
        };
        var reloaded = RoundTrip(initial);
        Assert.Equal(initial.Plugins.PageOrder, reloaded.Plugins.PageOrder);
        Assert.Equal(initial.ResolveMonitorPlacement("display-1"), reloaded.ResolveMonitorPlacement("display-1"));
    }

    [Fact]
    public void UnrelatedSettingsDoNotRequestPlacementAfterDragging()
    {
        using var fixture = ControllerFixture.Create(AppSettings.Default.WithMonitorPlacement("display-1", IslandAnchor.TopRight, 70, 90));
        var requests = 0;
        fixture.Controller.PlacementRequested += () => requests++;
        fixture.Settings.Update(s => s with { PanelRadius = 35 });
        Assert.Equal(0, requests);
    }

    private static AppSettings RoundTrip(AppSettings initial)
    {
        var core = CoreSettingsStoreAdapter.ApplyToCore(new FluidBarSettings(), initial);
        var json = SettingsJson.Serialize(core);
        Assert.True(SettingsJson.TryDeserialize(json, out var reloaded, out var error), error);
        return CoreSettingsStoreAdapter.ToAppSettings(SettingsValidator.Normalize(reloaded!, out _));
    }

    [Fact]
    public void OldNeutralPrimaryDoesNotOverrideGlobalPosition()
    {
        var core = new FluidBarSettings { Island = new IslandSettings { Anchor = IslandAnchor.BottomRight } };
        var settings = CoreSettingsStoreAdapter.ToAppSettings(core);
        Assert.Equal(IslandAnchor.BottomRight, settings.ResolveMonitorPlacement("display-1").Anchor);
    }

    [Fact]
    public void MigratedPrimaryOffsetsRemainAvailableAfterRestartAndCanBeCleared()
    {
        var core = new FluidBarSettings
        {
            Placement = new PlacementSettings { Monitors = new()
            {
                [SettingsJsonNames.PrimaryPlacementKey] = new MonitorPlacement { Anchor = IslandAnchor.TopCenter, OffsetX = 42, OffsetY = 18 },
            } },
        };
        var settings = CoreSettingsStoreAdapter.ToAppSettings(core);
        Assert.Equal(42, settings.ResolveMonitorPlacement("display-1").OffsetX);
        Assert.Equal(settings.ResolveMonitorPlacement("display-1"), RoundTrip(settings).ResolveMonitorPlacement("display-1"));
        var reset = settings with { Placement = settings.Placement.Clear(), Anchor = "bottom-right" };
        var reloaded = RoundTrip(reset);
        Assert.Equal(new ResolvedMonitorPlacement(IslandAnchor.BottomRight, 0, null), reloaded.ResolveMonitorPlacement("display-1"));
    }

    [Fact]
    public async Task ProductionAdapterSavesAndReloadsEditableFields()
    {
        var directory = Path.Combine(Path.GetTempPath(), "fluidbar-settings-restart-" + Guid.NewGuid().ToString("N"));
        try
        {
            var initial = AppSettings.Default.WithMonitorPlacement("display-1", IslandAnchor.BottomLeft, 21, 36) with
            {
                PanelRadius = 34, PowerMode = IslandPowerMode.Saver,
                TabBarStyle = TabBarStyle.BottomPreview, RailSide = PageRailSide.Right,
                WeatherCity = "Shanghai", HotkeySettings = "Ctrl+Shift+F12",
                Plugins = AppSettings.Default.Plugins with { PageOrder = ["builtin.media", "builtin.home"] },
            };
            using (var writer = CoreSettingsStoreAdapter.CreateDefault(appDataDirectory: directory))
            {
                await writer.LoadAsync();
                writer.QueueSave(initial);
                await writer.FlushAsync();
            }
            using var reader = CoreSettingsStoreAdapter.CreateDefault(appDataDirectory: directory);
            var reloaded = await reader.LoadAsync();
            Assert.Equal(initial.PanelRadius, reloaded.PanelRadius);
            Assert.Equal(initial.PowerMode, reloaded.PowerMode);
            Assert.Equal(initial.TabBarStyle, reloaded.TabBarStyle);
            Assert.Equal(initial.RailSide, reloaded.RailSide);
            Assert.Equal(initial.WeatherCity, reloaded.WeatherCity);
            Assert.Equal(initial.HotkeySettings, reloaded.HotkeySettings);
            Assert.Equal(initial.Plugins.PageOrder, reloaded.Plugins.PageOrder);
            Assert.Equal(initial.ResolveMonitorPlacement("display-1"), reloaded.ResolveMonitorPlacement("display-1"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task MaterialChanges_RefreshPreviewAndOpacityAvailability()
    {
        await AvaloniaTest.Run(() =>
        {
            var (settings, _) = TestFixtures.CreateSettings(AppSettings.Default with { Theme = AppTheme.Dark });
            var window = new SettingsWindow(settings, TestFixtures.CreateLocalizer("en-US"), SettingsWindowSection.Appearance);
            window.Show();
            try
            {
                settings.Update(s => s with { Material = IslandMaterial.Acrylic });
                Assert.Equal("acrylic", window.GetVisualDescendants().OfType<IslandPreview>().Single().Model.Material);
                Assert.False(OpacitySlider(window).IsEnabled);

                settings.Update(s => s with { Material = IslandMaterial.Obsidian });
                Assert.Equal("obsidian", window.GetVisualDescendants().OfType<IslandPreview>().Single().Model.Material);
                Assert.True(OpacitySlider(window).IsEnabled);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task ReplacingSettings_RefreshesControlsWithoutChangingTheme()
    {
        await AvaloniaTest.Run(() =>
        {
            var (settings, _) = TestFixtures.CreateSettings(AppSettings.Default with { Theme = AppTheme.Dark });
            var window = new SettingsWindow(settings, TestFixtures.CreateLocalizer("en-US"), SettingsWindowSection.Appearance);
            window.Show();
            try
            {
                settings.Replace(settings.Current with { PanelRadius = 35, FontScale = 1.4, Palette = "nord" });
                Assert.Equal(35, window.GetVisualDescendants().OfType<SettingsSlider>().Single(s => s.Minimum == 16).Value);
                Assert.Equal("nord", window.GetVisualDescendants().OfType<SegmentedControl>()
                    .Single(s => s.Items.Any(i => i.Value == "nord")).SelectedValue);
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(1, "top-center", IslandAnchor.TopCenter)]
    [InlineData(2, "top-right", IslandAnchor.TopRight)]
    public async Task SelectingPosition_ClearsSavedDragOffsets(int index, string token, IslandAnchor anchor)
    {
        await AvaloniaTest.Run(() =>
        {
            var initial = AppSettings.Default.WithMonitorPlacement("display-1", IslandAnchor.BottomRight, 70, 90);
            var (settings, _) = TestFixtures.CreateSettings(initial);
            var window = new SettingsWindow(settings, TestFixtures.CreateLocalizer("en-US"), SettingsWindowSection.Island);
            window.Show();
            try
            {
                var positions = window.GetVisualDescendants().OfType<SegmentedControl>()
                    .Single(s => s.Items.Any(i => i.Value == "bottom-right"));
                var point = positions.TranslatePoint(new Point(positions.Bounds.Width * (index + 0.5) / 6, positions.Bounds.Height / 2), window)!.Value;
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                Assert.Equal(token, settings.Current.Anchor);
                var resolved = settings.Current.ResolveMonitorPlacement("display-1");
                Assert.Equal(anchor, resolved.Anchor);
                Assert.Equal(0, resolved.OffsetX);
                Assert.Null(resolved.OffsetY);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void ClearingDragOffsets_RequestsRuntimePlacement()
    {
        using var fixture = ControllerFixture.Create(AppSettings.Default.WithMonitorPlacement("display-1", IslandAnchor.TopRight, 70, 90));
        var requests = 0;
        fixture.Controller.PlacementRequested += () => requests++;
        fixture.Settings.Update(s => s with { Placement = s.Placement.Clear() });
        Assert.True(requests > 0);
    }

    [Fact]
    public async Task Reset_RestoresNavigationAndPrivacyAndQuickActions()
    {
        await AvaloniaTest.Run(() =>
        {
            var (settings, _) = TestFixtures.CreateSettings(AppSettings.Default with
            {
                Telemetry = false,
                ClipboardPaused = true,
                ClipboardHidePreviews = true,
                TabBarStyle = TabBarStyle.BottomPreview,
                RailSide = PageRailSide.Right,
                QuickActions = [new QuickActionSetting { Id = "sample", Name = "Sample", Kind = "settings" }],
            });
            var localizer = TestFixtures.CreateLocalizer("en-US");
            var window = new SettingsWindow(settings, localizer, SettingsWindowSection.Advanced);
            window.Show();
            try
            {
                window.GetVisualDescendants().OfType<TextBox>().Single(b => b.Watermark?.Contains("RESET") == true).Text = "RESET";
                var label = window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == localizer.Get(LocKeys.Settings.Advanced.Reset));
                label.GetVisualAncestors().OfType<Button>().First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(AppSettings.Default.TabBarStyle, settings.Current.TabBarStyle);
                Assert.Equal(AppSettings.Default.RailSide, settings.Current.RailSide);
                Assert.Equal(AppSettings.Default.Telemetry, settings.Current.Telemetry);
                Assert.False(settings.Current.ClipboardPaused);
                Assert.False(settings.Current.ClipboardHidePreviews);
                Assert.Empty(settings.Current.QuickActions);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void AutomaticDownload_SurvivesCorePersistence()
    {
        var core = CoreSettingsStoreAdapter.ApplyToCore(new FluidBarSettings(), AppSettings.Default with { AutoUpdate = "auto" });
        var json = SettingsJson.Serialize(core);
        Assert.True(SettingsJson.TryDeserialize(json, out var reloaded, out var error), error);
        Assert.Equal("auto", CoreSettingsStoreAdapter.ToAppSettings(reloaded!).AutoUpdate);
    }

    [Fact]
    public async Task Advanced_UsesConfiguredDataDirectory()
    {
        var previous = Environment.GetEnvironmentVariable("FLUIDBAR_SETTINGS_DIR");
        var directory = Path.Combine(Path.GetTempPath(), "FluidBar-settings-panel");
        Environment.SetEnvironmentVariable("FLUIDBAR_SETTINGS_DIR", directory);
        try
        {
            await AvaloniaTest.Run(() =>
            {
                var localizer = TestFixtures.CreateLocalizer("en-US");
                var (settings, _) = TestFixtures.CreateSettings(AppSettings.Default);
                string? opened = null;
                var window = new SettingsWindow(settings, localizer, SettingsWindowSection.Advanced, openPath: path => opened = path);
                window.Show();
                try
                {
                    Assert.Contains(window.GetVisualDescendants().OfType<TextBox>(), b => b.IsReadOnly && b.Text == directory);
                    var label = window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == localizer.Get(LocKeys.Settings.Advanced.OpenFolder));
                    label.GetVisualAncestors().OfType<Button>().First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.Equal(directory, opened);
                }
                finally { window.Close(); }
            });
        }
        finally { Environment.SetEnvironmentVariable("FLUIDBAR_SETTINGS_DIR", previous); }
    }

    private static SettingsSlider OpacitySlider(Window window)
        => window.GetVisualDescendants().OfType<SettingsSlider>().Single(s => s.Minimum == LayoutTokens.MinFillOpacity && s.Maximum == 1);
}
