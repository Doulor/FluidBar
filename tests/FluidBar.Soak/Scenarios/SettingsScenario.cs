using System.Diagnostics;
using FluidBar.Core.Diagnostics;
using FluidBar.Core.Settings;

namespace FluidBar.Soak.Scenarios;

/// <summary>
/// Scenario E — settings store: 10,000 atomic saves through <c>SettingsStore</c> +
/// <c>InMemorySettingsFileSystem</c>. Asserts bounded memory (the in-memory file system's
/// operation log is the only unbounded structure, so it is trimmed like a real rotation point),
/// correct final state and no silent failures.
/// </summary>
public static class SettingsScenario
{
    private const int WarmupSaves = 500;

    public static SoakScenarioResult Run(int seed)
    {
        var builder = new ScenarioResultBuilder("E", "settings atomic saves (10k)");
        var stopwatch = Stopwatch.StartNew();
        var fileSystem = new InMemorySettingsFileSystem();
        var counters = new DiagnosticCounters();
        var options = SettingsStoreOptions.ForAppData("C:\\soak\\FluidBar");
        var store = new SettingsStore(
            fileSystem,
            options,
            NullAppLogger.Instance,
            counters);
        var random = new Random(seed);
        var settings = FluidBarSettings.CreateDefault();
        var failedSaves = 0;
        var liveHeapAfterWarmup = 0L;
        var gcBefore = GcSnapshot.Capture();
        var measuredSaves = 0;

        for (var i = 0; i < SoakBudgets.SettingsSaveCount; i++)
        {
            settings = settings with
            {
                Appearance = settings.Appearance with
                {
                    Theme = i % 2 == 0 ? AppTheme.Dark : AppTheme.Light,
                    FontScale = 0.8 + (random.Next(80) * 0.01),
                },
                General = settings.General with { Telemetry = i % 3 == 0 },
            };

            if (!store.Save(settings).Success)
            {
                failedSaves++;
            }

            // The test double records every file operation; trim it like a real log rotation.
            if (i % 1_000 == 0)
            {
                fileSystem.ClearOperations();
            }

            if (i == WarmupSaves)
            {
                fileSystem.ClearOperations();
                liveHeapAfterWarmup = SoakMeasure.LiveHeapBytes();
                gcBefore = GcSnapshot.Capture();
            }

            if (i >= WarmupSaves)
            {
                measuredSaves++;
            }
        }

        var gcAfter = GcSnapshot.Capture();
        var heapAfter = SoakMeasure.LiveHeapBytes();
        var heapGrowth = Math.Max(0, heapAfter - liveHeapAfterWarmup);
        var loaded = store.Load();
        stopwatch.Stop();

        var finalMatches = loaded.Status == SettingsLoadStatus.Loaded
                           && loaded.Settings.Appearance.FontScale.Equals(settings.Appearance.FontScale)
                           && loaded.Settings.Appearance.Theme == settings.Appearance.Theme;
        var tempGone = !fileSystem.FileExists(options.EffectiveTempPath);
        var backupExists = fileSystem.FileExists(options.EffectiveBackupPath);

        builder.Metric("saves", SoakBudgets.SettingsSaveCount);
        builder.Metric("failedSaves", failedSaves);
        builder.Metric("heapGrowthBytes", heapGrowth);
        builder.Metric("loadStatus", (double)(int)loaded.Status);
        builder.Metric("settingsSavesCounter", counters.Get(CounterKind.SettingsSaves));
        builder.Metric("saveFailuresCounter", counters.Get(CounterKind.SettingsSaveFailures));
        builder.Metric("tempFilePresent", tempGone ? 0 : 1);
        builder.Metric("backupFilePresent", backupExists ? 1 : 0);
        builder.Metric("finalMatches", finalMatches ? 1 : 0);
        builder.Metric("elapsedMs", stopwatch.Elapsed.TotalMilliseconds);
        builder.Metric("allocatedBytesPerSave",
            (double)gcAfter.AllocatedSince(gcBefore) / Math.Max(1, measuredSaves));

        builder.Check("failedSaves", failedSaves, 0, "==");
        builder.Check("heapGrowthBytes", heapGrowth, SoakBudgets.SettingsMaxHeapGrowthBytes, "<=");
        builder.Check("settingsSavesCounter", counters.Get(CounterKind.SettingsSaves),
            SoakBudgets.SettingsSaveCount, "==");
        builder.Check("saveFailuresCounter", counters.Get(CounterKind.SettingsSaveFailures), 0, "==");
        builder.Check("tempFilePresent", tempGone ? 0 : 1, 0, "==");
        builder.Check("backupFilePresent", backupExists ? 1 : 0, 1, "==");
        builder.Check("finalMatches", finalMatches ? 1 : 0, 1, "==");
        builder.Check("loadStatus", (double)(int)loaded.Status, (double)(int)SettingsLoadStatus.Loaded, "==");

        return builder.Build(stopwatch.Elapsed.TotalMilliseconds);
    }
}
