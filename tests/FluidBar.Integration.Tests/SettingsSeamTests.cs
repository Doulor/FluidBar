using FluidBar.Core.Settings;
using FluidBar.Core.Layout;
using FluidBar.Integration.Tests.TestSupport;

namespace FluidBar.Integration.Tests;

/// <summary>
/// Seam 4: v1 settings JSON → Core SettingsStore migration → SettingsProjection → export/import
/// round-trip. Proves the persisted-settings seam end-to-end without touching the real %APPDATA% path.
/// </summary>
public class SettingsSeamTests
{
    private const string V1SettingsJson = """
    {
      "Position": "TopLeft",
      "OffsetX": 10,
      "OffsetY": 20,
      "CollapsedWidth": 126,
      "CollapsedHeight": 38,
      "CornerRadius": 24,
      "Opacity": 96,
      "BackgroundOpacity": 0.75,
      "AccentColor": "#0A84FF",
      "AlwaysOnTop": true,
      "AlwaysVisible": false,
      "AutoHideDelayMs": 4000,
      "DisplayStrategy": "Multiple",
      "MaxVisibleIslands": 4,
      "RimMode": "Event",
      "ClipboardEnabled": false,
      "PluginEnabled": { "clipboard": true, "communityx": true },
      "MonitorFeatureSettings": {
        "volume": { "DisplayDurationMs": 2000 },
        "notification": { "VerificationCodeDetection": false }
      },
      "HideTrayIcon": true,
      "HoldToHideKey": "Disabled",
      "SettingsTheme": "Light",
      "AutoUpdateCheck": false,
      "UnknownFutureField": 42
    }
    """;

    private static (SettingsStore Store, InMemorySettingsFileSystem FileSystem, SettingsStoreOptions Options) CreateStore()
    {
        var fileSystem = new InMemorySettingsFileSystem();
        var options = SettingsStoreOptions.ForAppData("C:\\appdata\\FluidBar");
        return (new SettingsStore(fileSystem, options), fileSystem, options);
    }

    [Fact]
    public void V1_settings_migrate_through_the_store_and_project_into_core_policies()
    {
        var (store, fileSystem, _) = CreateStore();
        fileSystem.SeedFile(SettingsStoreOptions.ForAppData("C:\\appdata\\FluidBar").SettingsPath, V1SettingsJson);

        var load = store.Load();

        Assert.Equal(SettingsLoadStatus.MigratedFromV1, load.Status);
        Assert.NotNull(load.Migration);
        Assert.True(load.Migration!.Success);
        Assert.True(load.Migration.MappedCount >= 8);
        Assert.Contains(load.Migration.Dropped, entry => entry.SourcePath == "CornerRadius");
        Assert.Contains(load.Migration.Dropped, entry => entry.SourcePath == "UnknownFutureField");

        var settings = load.Settings;
        Assert.Equal(0.96, settings.Appearance.FillOpacity, 1e-9);
        Assert.Equal(AppTheme.Light, settings.Appearance.Theme);
        Assert.Equal(IslandAnchor.TopLeft, settings.Island.Anchor);
        Assert.Equal(StackingPreference.Stack, settings.Island.Stacking);
        Assert.Equal(4000, settings.Island.AutoCollapseMs);
        Assert.Null(settings.Hotkeys.HoldToHide);
        Assert.False(settings.General.TrayIcon);
        Assert.False(settings.Activities.Clipboard.Enabled);
        Assert.Equal(2000, settings.Activities.Volume.DurationMs);
        Assert.Contains("communityx", settings.Plugins.Orphaned);

        // Projection: settings → pure policies consumed by App/core.
        var behavior = SettingsProjection.ToBehaviorOptions(settings, systemReducedMotion: false);
        Assert.True(behavior.Enabled);
        Assert.Equal(4000, behavior.AutoCollapseMs);
        var stack = SettingsProjection.ToStackPolicy(settings);
        Assert.Equal(TimeSpan.FromMilliseconds(2000), stack.TtlOverrides[FluidBar.Core.Activities.ActivityKind.Volume]);
        var motion = SettingsProjection.ToMotionProfile(settings, systemReducedMotion: true);
        Assert.Equal(1.0, motion.Strength, 1e-9);
        var monitor = FluidBar.Core.Layout.MonitorInfo.Create("m", new FluidBar.Core.Layout.PixelRect(0, 0, 1920, 1040), 96, isPrimary: true);
        var request = SettingsProjection.ToLayoutRequest(settings, FluidBar.Core.Activities.IslandState.Hidden with
        {
            Kind = FluidBar.Core.Activities.IslandStateKind.Compact,
            ActivityCount = 1,
            ContentKind = FluidBar.Core.Activities.ActivityKind.Notification,
        }, monitor);
        Assert.Equal(IslandAnchor.TopLeft, request.Anchor);
    }

    [Fact]
    public void Migrated_settings_export_and_import_round_trip()
    {
        var (store, fileSystem, options) = CreateStore();
        fileSystem.SeedFile(options.SettingsPath, V1SettingsJson);
        var migrated = store.Load().Settings;
        var exportPath = "C:\\appdata\\FluidBar\\exports\\fluidbar-settings-20261018-1200.json";

        var export = store.Export(exportPath, migrated);
        Assert.True(export.Success, export.Error);

        var preview = store.Import(exportPath, FluidBarSettings.CreateDefault());
        Assert.True(preview.Success, preview.Error);
        Assert.NotNull(preview.Settings);
        Assert.NotNull(preview.Diff);
        Assert.Equal(AppTheme.Light, preview.Settings!.Appearance.Theme);
        Assert.Equal(IslandAnchor.TopLeft, preview.Settings.Island.Anchor);
        Assert.Equal(0.96, preview.Settings.Appearance.FillOpacity, 1e-9);

        Assert.True(store.Save(preview.Settings).Success);
        var reloaded = store.Load();
        Assert.Equal(SettingsLoadStatus.Loaded, reloaded.Status);
        Assert.Equal(IslandAnchor.TopLeft, reloaded.Settings.Island.Anchor);
    }

    [Fact]
    public void Imported_v1_settings_show_expected_diff_against_defaults()
    {
        var (store, fileSystem, options) = CreateStore();
        var legacyImportPath = "C:\\appdata\\FluidBar\\imports\\legacy-v1.json";
        fileSystem.SeedFile(legacyImportPath, V1SettingsJson);

        var preview = store.Import(legacyImportPath, FluidBarSettings.CreateDefault());

        Assert.True(preview.Success, preview.Error);
        Assert.NotNull(preview.Diff);
        Assert.True(preview.Diff!.TotalChanges > 0);
        Assert.NotNull(preview.Migration);
        Assert.True(preview.Migration!.MappedCount > 0);
    }
}
