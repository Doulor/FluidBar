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

    [Fact]
    public async Task Existing_core_settings_win_over_a_late_legacy_file()
    {
        var root = Path.Combine(Path.GetTempPath(), "FluidBarTests", "migration", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using (var first = CoreSettingsStoreAdapter.CreateDefault(NullAppLogger.Instance, root))
            {
                var initial = await first.LoadAsync();
                first.QueueSave(initial with { Language = "en-US" });
                await first.FlushAsync();
            }

            var legacy = Path.Combine(root, LegacySettingsImporter.LegacyFileName);
            await File.WriteAllTextAsync(legacy, (AppSettings.Default with { Language = "zh-Hans" }).ToJson());

            using (var second = CoreSettingsStoreAdapter.CreateDefault(NullAppLogger.Instance, root))
            {
                var loaded = await second.LoadAsync();
                Assert.Equal("en-US", loaded.Language);
                Assert.Equal(LegacyImportOutcome.Skipped, second.LegacyImportResult?.Outcome);
            }

            Assert.True(File.Exists(legacy));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
