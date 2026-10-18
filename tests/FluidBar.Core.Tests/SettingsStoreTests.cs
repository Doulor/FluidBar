using FluidBar.Core.Activities;
using FluidBar.Core.Diagnostics;
using FluidBar.Core.Layout;
using FluidBar.Core.Settings;
using FluidBar.Core.Tests.TestSupport;

namespace FluidBar.Core.Tests;

public class SettingsStoreTests
{
    private const string Root = "C:\\appdata\\FluidBar";
    private const string SettingsPath = Root + "\\settings.json";
    private const string BackupPath = Root + "\\settings.json.bak";
    private const string V1BackupPath = Root + "\\settings.v1.backup.json";

    private static SettingsStoreOptions Options => SettingsStoreOptions.ForAppData(Root);

    private static (SettingsStore Store, InMemorySettingsFileSystem Fs, RecordingAppLogger Logger, DiagnosticCounters Counters) Create()
    {
        var fs = new InMemorySettingsFileSystem();
        var logger = new RecordingAppLogger();
        var counters = new DiagnosticCounters();
        var time = new FakeTimeProvider(TestData.Now);
        var store = new SettingsStore(fs, Options, logger, counters, time);
        return (store, fs, logger, counters);
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var (store, _, _, _) = Create();
        var result = store.Load();

        Assert.Equal(SettingsLoadStatus.DefaultsCreated, result.Status);
        Assert.True(result.Success);
        Assert.False(result.Validation.HasErrors);
        Assert.Equal(FluidBarSettings.CurrentSchemaVersion, result.Settings.SchemaVersion);
    }

    [Fact]
    public void Save_ThenLoad_RoundTrips()
    {
        var (store, fs, _, counters) = Create();
        var settings = FluidBarSettings.CreateDefault() with
        {
            Appearance = new AppearanceSettings { Theme = AppTheme.Light },
            Island = new IslandSettings { Mode = IslandMode.Dock },
        };

        var save = store.Save(settings);
        Assert.True(save.Success);
        Assert.Equal(1L, counters.Get(CounterKind.SettingsSaves));

        var loaded = new SettingsStore(fs, Options).Load();
        Assert.Equal(SettingsLoadStatus.Loaded, loaded.Status);
        Assert.Equal(AppTheme.Light, loaded.Settings.Appearance.Theme);
        Assert.Equal(IslandMode.Dock, loaded.Settings.Island.Mode);
    }

    [Fact]
    public void Save_FirstWrite_UsesTempThenMove_SecondUsesReplaceWithBackup()
    {
        var (store, fs, _, _) = Create();
        store.Save(FluidBarSettings.CreateDefault());
        fs.ClearOperations();
        store.Save(FluidBarSettings.CreateDefault() with
        {
            Appearance = new AppearanceSettings { Theme = AppTheme.Light },
        });

        var ops = fs.Operations;
        Assert.Contains(ops, o => o.Kind == SettingsFileOperationKind.CreateDirectory);
        Assert.Contains(ops, o => o.Kind == SettingsFileOperationKind.Write && o.Path.EndsWith(".tmp", StringComparison.Ordinal));
        Assert.Contains(ops, o => o.Kind == SettingsFileOperationKind.Replace && o.Destination == SettingsPath);
        Assert.NotNull(fs.PeekFile(BackupPath));
        Assert.False(fs.FileExists(Options.EffectiveTempPath));
    }

    [Fact]
    public void Save_WriteFailure_ReturnsErrorAndLogs()
    {
        var (store, fs, logger, counters) = Create();
        fs.FailOnWrite = new IOException("disk full");

        var result = store.Save(FluidBarSettings.CreateDefault());

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.True(logger.HasEvent("settings.save.failed"));
        Assert.Equal(1L, counters.Get(CounterKind.SettingsSaveFailures));
    }

    [Fact]
    public void Save_ReplaceFailure_FallsBackToMove()
    {
        var (store, fs, logger, _) = Create();
        store.Save(FluidBarSettings.CreateDefault());
        fs.FailOnReplace = new IOException("replace unsupported");

        var result = store.Save(FluidBarSettings.CreateDefault() with
        {
            Appearance = new AppearanceSettings { Theme = AppTheme.Light },
        });

        Assert.True(result.Success);
        Assert.True(logger.HasEvent("settings.save.replaceFallback"));
        Assert.Contains("\"theme\":\"light\"", fs.PeekFile(SettingsPath));
    }

    [Fact]
    public void Load_CorruptFileWithoutBackup_FailsToDefaults()
    {
        var (store, fs, logger, _) = Create();
        fs.SeedFile(SettingsPath, "{ not valid json");

        var result = store.Load();

        Assert.Equal(SettingsLoadStatus.FailedDefaults, result.Status);
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.True(logger.HasEvent("settings.load.invalidJson") || logger.HasEvent("settings.load.invalid"));
        Assert.Equal(FluidBarSettings.CurrentSchemaVersion, result.Settings.SchemaVersion);
    }

    [Fact]
    public void Load_CorruptFileWithValidBackup_Recovers()
    {
        var (store, fs, _, _) = Create();
        fs.SeedFile(SettingsPath, "{ broken");
        fs.SeedFile(BackupPath, SettingsJson.Serialize(FluidBarSettings.CreateDefault() with
        {
            Appearance = new AppearanceSettings { Theme = AppTheme.Light },
        }));

        var result = store.Load();

        Assert.Equal(SettingsLoadStatus.RecoveredFromBackup, result.Status);
        Assert.Equal(AppTheme.Light, result.Settings.Appearance.Theme);
    }

    [Fact]
    public void Load_FutureSchema_FailsWithoutOverwriting()
    {
        var (store, fs, logger, _) = Create();
        var future = """{ "schemaVersion": 99 }""";
        fs.SeedFile(SettingsPath, future);

        var result = store.Load();

        Assert.Equal(SettingsLoadStatus.FailedDefaults, result.Status);
        Assert.Equal(future, fs.PeekFile(SettingsPath));
        Assert.True(logger.HasEvent("settings.load.futureSchema"));
    }

    [Fact]
    public void Load_LegacyV1File_MigratesBacksUpAndWritesV2()
    {
        var (store, fs, logger, counters) = Create();
        fs.SeedFile(SettingsPath, SettingsMigrationTests.V1Sample);

        var result = store.Load();

        Assert.Equal(SettingsLoadStatus.MigratedFromV1, result.Status);
        Assert.NotNull(result.Migration);
        Assert.Equal(IslandAnchor.TopLeft, result.Settings.Island.Anchor);
        Assert.NotNull(fs.PeekFile(V1BackupPath));
        var written = fs.PeekFile(SettingsPath);
        Assert.Contains("\"schemaVersion\":2", written);
        Assert.True(logger.HasEvent("settings.migrate.succeeded"));
        Assert.Equal(1L, counters.Get(CounterKind.MigrationRuns));

        var reloaded = new SettingsStore(fs, Options).Load();
        Assert.Equal(SettingsLoadStatus.Loaded, reloaded.Status);
        Assert.Equal(IslandAnchor.TopLeft, reloaded.Settings.Island.Anchor);
    }

    [Fact]
    public void Export_And_Import_RoundTripsWithDiff()
    {
        var (store, fs, _, _) = Create();
        var exportPath = Root + "\\exports\\fluidbar-settings-20261018-1200.json";
        var settings = FluidBarSettings.CreateDefault() with
        {
            Appearance = new AppearanceSettings { Theme = AppTheme.Light },
            Plugins = new PluginsSettings { Enabled = ["builtin.media", "builtin.agent"] },
        };

        var export = store.Export(exportPath, settings);
        Assert.True(export.Success);
        var json = fs.PeekFile(exportPath);
        Assert.NotNull(json);
        Assert.Contains("\"exportedAt\"", json);
        Assert.Contains("\"settings\"", json);
        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);

        var preview = store.Import(exportPath, FluidBarSettings.CreateDefault());
        Assert.True(preview.Success);
        Assert.NotNull(preview.Settings);
        Assert.NotNull(preview.Diff);
        Assert.True(preview.Diff!.TotalChanges > 0);
        Assert.Equal(AppTheme.Light, preview.Settings!.Appearance.Theme);

        Assert.True(store.Save(preview.Settings).Success);
        var loaded = new SettingsStore(fs, Options).Load();
        Assert.Equal(AppTheme.Light, loaded.Settings.Appearance.Theme);
    }

    [Fact]
    public void Import_RawV2Json_Works()
    {
        var (store, fs, _, _) = Create();
        var path = Root + "\\import.json";
        fs.SeedFile(path, SettingsJson.Serialize(FluidBarSettings.CreateDefault()));

        var preview = store.Import(path, FluidBarSettings.CreateDefault());

        Assert.True(preview.Success);
        Assert.NotNull(preview.Settings);
        Assert.Equal(0, preview.Diff!.TotalChanges);
    }

    [Fact]
    public void Import_LegacyV1Json_MigratesInPreview()
    {
        var (store, fs, _, _) = Create();
        var path = Root + "\\legacy.json";
        fs.SeedFile(path, SettingsMigrationTests.V1Sample);

        var preview = store.Import(path, FluidBarSettings.CreateDefault());

        Assert.True(preview.Success);
        Assert.NotNull(preview.Migration);
        Assert.Equal(IslandAnchor.TopLeft, preview.Settings!.Island.Anchor);
    }

    [Fact]
    public void Import_MissingOrInvalidFile_Fails()
    {
        var (store, fs, _, _) = Create();
        Assert.False(store.Import(Root + "\\nope.json", FluidBarSettings.CreateDefault()).Success);

        var path = Root + "\\broken.json";
        fs.SeedFile(path, "nope");
        var preview = store.Import(path, FluidBarSettings.CreateDefault());
        Assert.False(preview.Success);
        Assert.NotNull(preview.Error);
    }

    [Fact]
    public void SaveDebounce_RequestsFlushAfter500ms()
    {
        var debounce = new SettingsSaveDebounce();
        Assert.Equal(TimeSpan.FromMilliseconds(500), debounce.Interval);
        var now = TestData.Now;

        debounce.Request(now);
        Assert.True(debounce.IsPending);
        Assert.False(debounce.ShouldFlush(now.AddMilliseconds(499)));
        Assert.True(debounce.ShouldFlush(now.AddMilliseconds(500)));

        debounce.MarkFlushed();
        Assert.False(debounce.IsPending);
    }

    [Fact]
    public void DiffCalculator_ReportsAddedModifiedAndPaths()
    {
        var current = FluidBarSettings.CreateDefault();
        var imported = current with
        {
            Appearance = new AppearanceSettings { Theme = AppTheme.Light, FillOpacity = 0.9 },
            Plugins = new PluginsSettings { Enabled = ["builtin.media"] },
        };

        var diff = SettingsDiffCalculator.Compare(current, imported);

        Assert.True(diff.Modified >= 1);
        Assert.True(diff.TotalChanges > 0);
        Assert.Contains("~appearance.theme", diff.ChangedPaths);
        Assert.True(diff.ChangedPaths.Length <= SettingsDiffCalculator.MaxPaths);
    }
}

public class SettingsProjectionTests
{
    [Fact]
    public void ReducedMotion_TriState()
    {
        Assert.False(SettingsProjection.ResolveReducedMotion(MotionPreference.Enabled, systemReducedMotion: true));
        Assert.True(SettingsProjection.ResolveReducedMotion(MotionPreference.Off, systemReducedMotion: false));
        Assert.True(SettingsProjection.ResolveReducedMotion(MotionPreference.System, systemReducedMotion: true));
        Assert.False(SettingsProjection.ResolveReducedMotion(MotionPreference.System, systemReducedMotion: false));
    }

    [Fact]
    public void ToBehaviorOptions_MapsIslandSettings()
    {
        var settings = FluidBarSettings.CreateDefault() with
        {
            Island = new IslandSettings
            {
                Enabled = false,
                HoverPeek = false,
                AutoCollapseMs = 999,
                AutoHideMs = 5000,
            },
        };

        var options = SettingsProjection.ToBehaviorOptions(settings, systemReducedMotion: false);
        Assert.False(options.Enabled);
        Assert.False(options.HoverPeek);
        Assert.Equal(999, options.AutoCollapseMs);
        Assert.Equal(5000, options.AutoHideMs);
    }

    [Fact]
    public void ToStackPolicy_MapsStackingAndTtls()
    {
        var settings = FluidBarSettings.CreateDefault() with
        {
            Island = new IslandSettings { Stacking = StackingPreference.Latest },
            Activities = new ActivitySettings
            {
                Volume = new ActivityFeatureSettings { DurationMs = 1200 },
            },
        };

        var policy = SettingsProjection.ToStackPolicy(settings);
        Assert.Equal(StackingStrategy.LatestOnly, policy.Stacking);
        Assert.Equal(1, policy.VisibleCapacity);
        Assert.Equal(TimeSpan.FromMilliseconds(1200), policy.TtlOverrides[ActivityKind.Volume]);
        Assert.False(policy.TtlOverrides.ContainsKey(ActivityKind.Media));
    }

    [Fact]
    public void ToMotionProfile_MapsStrengthAndReducedMotion()
    {
        var settings = FluidBarSettings.CreateDefault() with
        {
            Appearance = new AppearanceSettings { ReducedMotion = MotionPreference.Off, MotionStrength = 1.2 },
        };
        var profile = SettingsProjection.ToMotionProfile(settings, systemReducedMotion: false);
        Assert.True(profile.ReducedMotion);
        Assert.Equal(1.2, profile.Strength, 1e-9);
    }

    [Fact]
    public void ToLayoutRequest_MapsModeAnchorOffsetsAndState()
    {
        var settings = FluidBarSettings.CreateDefault() with
        {
            Island = new IslandSettings { Mode = IslandMode.Dock, Stacking = StackingPreference.Latest },
            Placement = new PlacementSettings
            {
                Monitors = new Dictionary<string, MonitorPlacement>(StringComparer.Ordinal)
                {
                    ["monitor-1"] = new MonitorPlacement { Anchor = IslandAnchor.BottomLeft, OffsetX = 7 },
                },
            },
        };
        var monitor = MonitorInfo.Create("monitor-1", new PixelRect(0, 0, 1920, 1040), 96, isPrimary: true);
        var state = IslandState.Hidden with
        {
            Kind = IslandStateKind.Compact,
            ContentKind = ActivityKind.Notification,
            ActivityCount = 1,
        };

        var request = SettingsProjection.ToLayoutRequest(settings, state, monitor);

        Assert.Equal(ShellForm.Compact, request.Form);
        Assert.Equal(IslandMode.Dock, request.Mode);
        Assert.Equal(IslandAnchor.BottomLeft, request.Anchor);
        Assert.Equal(7, request.OffsetX, 1e-9);
        Assert.Equal(StackingStrategy.LatestOnly, request.Stacking);
        Assert.Equal(ActivityKind.Notification, request.CompactKind);
    }

    [Fact]
    public void ActivityEnableAndTtlHelpers()
    {
        var settings = FluidBarSettings.CreateDefault() with
        {
            Activities = new ActivitySettings
            {
                Notification = new ActivityFeatureSettings { Enabled = false, DurationMs = 8000 },
            },
        };

        Assert.False(SettingsProjection.IsActivityEnabled(settings, ActivityKind.Notification));
        Assert.True(SettingsProjection.IsActivityEnabled(settings, ActivityKind.Volume));
        Assert.Equal(TimeSpan.FromMilliseconds(8000), SettingsProjection.TtlFor(settings, ActivityKind.Notification));
        Assert.Null(SettingsProjection.TtlFor(settings, ActivityKind.Media));
    }
}

public class DiagnosticsTests
{
    [Fact]
    public void Counters_AddGetSnapshotReset()
    {
        var counters = new DiagnosticCounters();
        counters.Increment(CounterKind.EventsPublished);
        counters.Add(CounterKind.EventsDropped, 5);
        counters.Set(CounterKind.LayoutComputed, 42);

        Assert.Equal(1, counters.Get(CounterKind.EventsPublished));
        Assert.Equal(5, counters.Get(CounterKind.EventsDropped));
        Assert.Equal(42, counters.Get(CounterKind.LayoutComputed));

        var snapshot = counters.Snapshot();
        Assert.Equal(1, snapshot[CounterKind.EventsPublished]);
        Assert.Equal(42, snapshot[CounterKind.LayoutComputed]);
        Assert.Equal(0, snapshot[CounterKind.SettingsSaves]);

        counters.Reset();
        Assert.Equal(0, counters.Get(CounterKind.EventsPublished));
    }

    [Fact]
    public void RecordingLogger_CapturesEntriesAndDropsOldest()
    {
        var time = new FakeTimeProvider(TestData.Now);
        var logger = new RecordingAppLogger(capacity: 2, timeProvider: time);
        logger.Information(LogCategories.Settings, "a", "first");
        logger.Warning(LogCategories.Settings, "b", "second");
        logger.Error(LogCategories.Settings, "c", "third", new InvalidOperationException("x"));

        Assert.Equal(2, logger.Count);
        Assert.Equal(1, logger.DroppedCount);
        Assert.False(logger.HasEvent("a"));
        Assert.True(logger.HasEvent("b"));
        Assert.True(logger.HasEvent("c"));
        var snapshot = logger.Snapshot();
        Assert.Equal(TestData.Now, snapshot[0].Timestamp);
        Assert.Equal(LogLevel.Error, snapshot[1].Level);
        Assert.NotNull(snapshot[1].Exception);
    }

    [Fact]
    public void RecordingLogger_HonorsMinimumLevel()
    {
        var logger = new RecordingAppLogger(minimumLevel: LogLevel.Warning);
        logger.Information(LogCategories.Settings, "i", "info");
        logger.Warning(LogCategories.Settings, "w", "warn");

        Assert.False(logger.IsEnabled(LogLevel.Information));
        Assert.True(logger.IsEnabled(LogLevel.Warning));
        Assert.Equal(1, logger.Count);
        Assert.True(logger.HasEvent("w"));
    }

    [Fact]
    public void NullLogger_DiscardsEverything()
    {
        Assert.False(NullAppLogger.Instance.IsEnabled(LogLevel.Critical));
        NullAppLogger.Instance.Log(LogLevel.Critical, LogCategories.Lifecycle, "x", "y");
    }

    [Fact]
    public void LogCategories_AreUniqueAndComplete()
    {
        Assert.Equal(8, LogCategories.All.Count);
        Assert.Equal(LogCategories.All.Count, LogCategories.All.Distinct(StringComparer.Ordinal).Count());
        Assert.All(LogCategories.All, category => Assert.False(string.IsNullOrWhiteSpace(category)));
    }

    [Fact]
    public void InlineDispatcher_RunsSynchronously()
    {
        var ran = false;
        Assert.True(InlineDispatcher.Instance.IsOnDispatcherThread);
        InlineDispatcher.Instance.Post(() => ran = true);
        Assert.True(ran);
    }
}
