using FluidBar.App.Settings;
using FluidBar.Core.Diagnostics;
using FluidBar.Core.Settings;

namespace FluidBar.Ui.Tests;

/// <summary>One-time migration from the App-only settings.ui.json adapter to Core SettingsStore.</summary>
public sealed class LegacySettingsMigrationTests
{
    private static readonly string CorePath = @"C:\test\settings.json";
    private static readonly string LegacyPath = @"C:\test\settings.ui.json";

    [Fact]
    public void ImportIfNeeded_ImportsLegacyValues_WhenCoreFileMissing()
    {
        var fs = new InMemorySettingsFileSystem();
        var legacy = (AppSettings.Default with
        {
            Language = "zh-Hans",
            Mode = "dock",
            Anchor = "bottom-center",
            AutoCollapseMs = 1234,
            FillOpacity = 0.91,
        }).ToJson();
        fs.SeedFile(LegacyPath, legacy);
        var options = new SettingsStoreOptions { SettingsPath = CorePath, KeepBackup = false };
        var store = new SettingsStore(fs, options, NullAppLogger.Instance);

        var result = LegacySettingsImporter.ImportIfNeeded(fs, store, LegacyPath, CorePath, NullAppLogger.Instance);

        Assert.Equal(LegacyImportOutcome.Imported, result.Outcome);
        var core = store.Load().Settings;
        Assert.Equal("zh-Hans", core.General.Language);
        Assert.Equal(FluidBar.Core.Layout.IslandMode.Dock, core.Island.Mode);
        Assert.Equal(FluidBar.Core.Layout.IslandAnchor.BottomCenter, core.Island.Anchor);
        Assert.Equal(1234, core.Island.AutoCollapseMs);
        Assert.False(fs.FileExists(LegacyPath));
        Assert.True(fs.FileExists(LegacyPath + LegacySettingsImporter.ImportedSuffix));

        // Idempotent: a second run finds nothing to import.
        var second = LegacySettingsImporter.ImportIfNeeded(fs, store, LegacyPath, CorePath, NullAppLogger.Instance);
        Assert.Equal(LegacyImportOutcome.None, second.Outcome);
    }

    [Fact]
    public void ImportIfNeeded_SkipsWhenCoreSettingsAlreadyExist()
    {
        var fs = new InMemorySettingsFileSystem();
        fs.SeedFile(LegacyPath, (AppSettings.Default with { Language = "zh-Hans" }).ToJson());
        var options = new SettingsStoreOptions { SettingsPath = CorePath, KeepBackup = false };
        var store = new SettingsStore(fs, options, NullAppLogger.Instance);
        store.Save(new FluidBarSettings { General = new GeneralSettings { Language = "en-US" } });

        var result = LegacySettingsImporter.ImportIfNeeded(fs, store, LegacyPath, CorePath, NullAppLogger.Instance);

        Assert.Equal(LegacyImportOutcome.Skipped, result.Outcome);
        Assert.Equal("en-US", store.Load().Settings.General.Language);
        Assert.True(fs.FileExists(LegacyPath));
    }

    [Fact]
    public void ImportIfNeeded_PreservesLegacyFile_WhenJsonIsInvalid()
    {
        var fs = new InMemorySettingsFileSystem();
        fs.SeedFile(LegacyPath, "{ not json");
        var options = new SettingsStoreOptions { SettingsPath = CorePath, KeepBackup = false };
        var store = new SettingsStore(fs, options, NullAppLogger.Instance);

        var result = LegacySettingsImporter.ImportIfNeeded(fs, store, LegacyPath, CorePath, NullAppLogger.Instance);

        Assert.Equal(LegacyImportOutcome.Failed, result.Outcome);
        Assert.True(fs.FileExists(LegacyPath));
    }

    [Fact]
    public async Task CreateDefault_ImportsLegacyFileOnFirstBackgroundLoad()
    {
        var root = Path.Combine(Path.GetTempPath(), "fluidbar-ui-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var fileSystem = new SystemSettingsFileSystem();
            fileSystem.CreateDirectory(root);
            fileSystem.WriteAllText(
                Path.Combine(root, LegacySettingsImporter.LegacyFileName),
                (AppSettings.Default with { Language = "zh-Hans", Mode = "flush" }).ToJson());

            using var adapter = CoreSettingsStoreAdapter.CreateDefault(NullAppLogger.Instance, root);
            var loaded = await adapter.LoadAsync();

            Assert.Equal("zh-Hans", loaded.Language);
            Assert.Equal("flush", loaded.Mode);
            Assert.Equal(LegacyImportOutcome.Imported, adapter.LegacyImportResult?.Outcome);
            Assert.True(fileSystem.FileExists(Path.Combine(root, LegacySettingsImporter.LegacyFileName + LegacySettingsImporter.ImportedSuffix)));
            Assert.True(fileSystem.FileExists(Path.Combine(root, "settings.json")));
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                System.Diagnostics.Debug.WriteLine("legacy migration test cleanup failed: " + ex.Message);
            }
        }
    }
}
