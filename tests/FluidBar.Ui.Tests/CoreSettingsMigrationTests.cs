using FluidBar.App.Settings;
using FluidBar.Core.Diagnostics;

namespace FluidBar.Ui.Tests;

/// <summary>The production adapter's one-time legacy import must be observable end to end.</summary>
public sealed class CoreSettingsMigrationTests
{
    [Fact]
    public async Task Legacy_ui_settings_are_imported_into_core_once()
    {
        var root = Path.Combine(Path.GetTempPath(), "FluidBarTests", "migration", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var legacy = Path.Combine(root, LegacySettingsImporter.LegacyFileName);
            await File.WriteAllTextAsync(legacy, (AppSettings.Default with
            {
                Language = "zh-Hans",
                AutoCollapseMs = 1234,
            }).ToJson());

            using (var adapter = CoreSettingsStoreAdapter.CreateDefault(NullAppLogger.Instance, root))
            {
                var loaded = await adapter.LoadAsync();

                Assert.Equal("zh-Hans", loaded.Language);
                Assert.Equal(1234, loaded.AutoCollapseMs);
                Assert.Equal(LegacyImportOutcome.Imported, adapter.LegacyImportResult?.Outcome);
            }

            Assert.True(File.Exists(Path.Combine(root, "settings.json")));
            Assert.True(File.Exists(legacy + LegacySettingsImporter.ImportedSuffix));
            Assert.False(File.Exists(legacy));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
