using System.Collections.Concurrent;
using FluidBar.App.Hosting.Skia;
using FluidBar.App.Settings;
using FluidBar.App.State;
using FluidBar.Core.Activities;
using FluidBar.Core.Diagnostics;
using FluidBar.Core.Layout;
using FluidBar.Island;
using AppMaterial = FluidBar.App.Settings.IslandMaterial;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Shell;
using FluidBar.Platform.Windows.Update;

namespace FluidBar.Ui.Tests;

/// <summary>
/// Settings-effect tests: every custom setting covered here is asserted against the consumer that
/// makes it real (theme mapper, controller behaviour, registry, files, log gate, update service), not
/// against the settings JSON alone.
/// </summary>
public sealed class SettingsEffectsTests
{
    private sealed class DictRegistryStore : IRegistryValueStore
    {
        public Dictionary<(string Key, string Value), string> Values { get; } = new();

        public string? GetString(string subKeyPath, string valueName)
            => Values.TryGetValue((subKeyPath, valueName), out var value) ? value : null;

        public void SetString(string subKeyPath, string valueName, string value)
            => Values[(subKeyPath, valueName)] = value;

        public bool DeleteValue(string subKeyPath, string valueName)
            => Values.Remove((subKeyPath, valueName));
    }

    private sealed class ThrowingRegistryStore : IRegistryValueStore
    {
        public string? GetString(string subKeyPath, string valueName) => null;

        public void SetString(string subKeyPath, string valueName, string value)
            => throw new UnauthorizedAccessException("denied");

        public bool DeleteValue(string subKeyPath, string valueName) => false;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string because, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.True(condition(), because);
    }

    private static string NewTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "FluidBarTests", "settings", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    // ---- appearance: palette / accent / theme / material / fill --------------------------------

    [Fact]
    public void ThemeMapper_AppliesCuratedPaletteToTheIsland()
    {
        var obsidian = SkiaIslandThemeMapper.Map(AppSettings.Default with { Palette = "obsidian" });
        Assert.Equal(new SkiaSharp.SKColor(0x08, 0x08, 0x0A), obsidian.Background);

        var graphite = SkiaIslandThemeMapper.Map(AppSettings.Default with { Palette = "graphite" });
        Assert.Equal(new SkiaSharp.SKColor(0x1C, 0x1C, 0x1E), graphite.Background);
        Assert.Equal(new SkiaSharp.SKColor(0x25, 0x25, 0x29), graphite.BackgroundRaised);
        Assert.Equal(new SkiaSharp.SKColor(0x17, 0x17, 0x1A), graphite.BackgroundInset);

        var slate = SkiaIslandThemeMapper.Map(AppSettings.Default with { Palette = "slate" });
        Assert.Equal(new SkiaSharp.SKColor(0x11, 0x17, 0x21), slate.Background);

        var nord = SkiaIslandThemeMapper.Map(AppSettings.Default with { Palette = "nord" });
        Assert.Equal(new SkiaSharp.SKColor(0x2E, 0x34, 0x40), nord.Background);
    }

    [Fact]
    public void ThemeMapper_AccentFallbackAppliesInArtworkAndCustomModes()
    {
        var artwork = SkiaIslandThemeMapper.Map(AppSettings.Default with
        {
            AccentMode = "artwork",
            AccentFallback = "#112233",
        });
        Assert.Equal(new SkiaSharp.SKColor(0x11, 0x22, 0x33), artwork.Accent);

        var custom = SkiaIslandThemeMapper.Map(AppSettings.Default with
        {
            AccentMode = "custom",
            AccentFallback = "#34C759",
        });
        Assert.Equal(new SkiaSharp.SKColor(0x34, 0xC7, 0x59), custom.Accent);
    }

    [Fact]
    public void ThemeMapper_SystemAccentUsesTheWindowsColour()
    {
        var snapshot = new SystemAppearanceSnapshot(false, false, "#A1B2C3");
        var theme = SkiaIslandThemeMapper.Map(AppSettings.Default with { AccentMode = "system" }, snapshot);
        Assert.Equal(new SkiaSharp.SKColor(0xA1, 0xB2, 0xC3), theme.Accent);
    }

    [Fact]
    public void ThemeMapper_SystemThemeAndReducedMotionFollowTheProbe()
    {
        var light = SkiaIslandThemeMapper.Map(AppSettings.Default with { Theme = AppTheme.System },
            new SystemAppearanceSnapshot(true, false, null));
        Assert.Equal(IslandThemeMode.Light, light.Mode);

        var dark = SkiaIslandThemeMapper.Map(AppSettings.Default with { Theme = AppTheme.System },
            new SystemAppearanceSnapshot(false, false, null));
        Assert.Equal(IslandThemeMode.Dark, dark.Mode);

        Assert.True(SkiaIslandThemeMapper.ReducedMotion(AppSettings.Default,
            new SystemAppearanceSnapshot(false, true, null)));
        Assert.False(SkiaIslandThemeMapper.ReducedMotion(
            AppSettings.Default with { ReducedMotion = ReducedMotionPreference.Off },
            new SystemAppearanceSnapshot(false, true, null)));
    }

    [Fact]
    public void ThemeMapper_AcrylicAndSafeModeControlTheMaterialTier()
    {
        var acrylic = SkiaIslandThemeMapper.Map(AppSettings.Default with { Material = AppMaterial.Acrylic });
        Assert.Equal(FluidBar.Island.IslandMaterial.SimulatedGlass, acrylic.Material);

        var safe = SkiaIslandThemeMapper.Map(AppSettings.Default with
        {
            Material = AppMaterial.Acrylic,
            Advanced = new AdvancedSettings { SafeMode = true },
        });
        Assert.Equal(FluidBar.Island.IslandMaterial.Obsidian, safe.Material);
    }

    [Fact]
    public void ThemeMapper_FillOpacityIsClampedToTheReadableRange()
    {
        Assert.Equal(LayoutTokens.MinFillOpacity,
            SkiaIslandThemeMapper.Map(AppSettings.Default with { FillOpacity = 0.1 }).FillOpacity, 3);
        Assert.Equal(0.92, SkiaIslandThemeMapper.Map(AppSettings.Default with { FillOpacity = 0.92 }).FillOpacity, 3);
    }

    // ---- island behaviour: peek on track change ----------------------------------------------

    [Fact]
    public void PeekOnTrackChange_EntersPeekOnANewTrack_ThenRetracts()
    {
        // The design data source already carries a sample track, so establish the baseline with the
        // setting off (the first observed track never peeks), then turn the setting on.
        using var fixture = ControllerFixture.Create(AppSettings.Default with
        {
            AutoCollapseMs = 0,
            AutoHideMs = 0,
            PeekOnTrackChange = false,
        });
        Assert.Equal(IslandStateKind.Compact, fixture.Controller.State.Kind);
        fixture.Data.Update(content => content with
        {
            Media = new MediaSessionData { Title = "Track A", Artist = "A", Album = "A", IsPlaying = true },
        });
        Assert.False(fixture.Controller.IsSyntheticPeekActive);
        fixture.Settings.Update(settings => settings with { PeekOnTrackChange = true });

        // Track change arms the synthetic peek.
        fixture.Data.Update(content => content with
        {
            Media = new MediaSessionData { Title = "Track B", Artist = "B", Album = "B", IsPlaying = true },
        });
        Assert.True(fixture.Controller.IsSyntheticPeekActive);

        fixture.Time.Advance(TimeSpan.FromMilliseconds(120));
        fixture.Controller.PumpTimers();
        Assert.Equal(IslandStateKind.Peek, fixture.Controller.State.Kind);

        fixture.Time.Advance(TimeSpan.FromMilliseconds(1700));
        fixture.Controller.PumpTimers();
        fixture.Time.Advance(TimeSpan.FromMilliseconds(200));
        fixture.Controller.PumpTimers();
        Assert.Equal(IslandStateKind.Compact, fixture.Controller.State.Kind);
    }

    [Fact]
    public void PeekOnTrackChange_DisabledNeverPeeks()
    {
        using var fixture = ControllerFixture.Create(AppSettings.Default with
        {
            AutoCollapseMs = 0,
            AutoHideMs = 0,
            PeekOnTrackChange = false,
        });
        fixture.Data.Update(content => content with
        {
            Media = new MediaSessionData { Title = "Track A", IsPlaying = true },
        });
        fixture.Data.Update(content => content with
        {
            Media = new MediaSessionData { Title = "Track B", IsPlaying = true },
        });

        Assert.False(fixture.Controller.IsSyntheticPeekActive);
        fixture.Time.Advance(TimeSpan.FromMilliseconds(120));
        fixture.Controller.PumpTimers();
        Assert.Equal(IslandStateKind.Compact, fixture.Controller.State.Kind);
    }

    // ---- advanced: frame statistics -----------------------------------------------------------

    [Fact]
    public void FrameStatsRecorder_RecordsOnlyWhenEnabled_AndResetsOnDisable()
    {
        var recorder = new SkiaFrameStatsRecorder();
        recorder.Record(5.0);
        Assert.Equal(0, recorder.Snapshot().Frames);

        recorder.SetEnabled(true);
        recorder.Record(1.0);
        recorder.Record(2.0);
        recorder.Record(3.0);
        recorder.Record(4.0);
        var snapshot = recorder.Snapshot();
        Assert.True(snapshot.Enabled);
        Assert.Equal(4, snapshot.Frames);
        Assert.Equal(2.0, snapshot.P50Ms, 3);
        Assert.Equal(4.0, snapshot.P99Ms, 3);
        Assert.Equal(2.5, snapshot.MeanMs, 3);

        recorder.SetEnabled(false);
        var disabled = recorder.Snapshot();
        Assert.False(disabled.Enabled);
        Assert.Equal(0.0, disabled.P50Ms);
        Assert.Equal(0.0, disabled.P99Ms);
    }

    // ---- advanced: log level ------------------------------------------------------------------

    [Theory]
    [InlineData("trace", LogLevel.Trace, true)]
    [InlineData("debug", LogLevel.Debug, true)]
    [InlineData("info", LogLevel.Information, true)]
    [InlineData("warn", LogLevel.Information, false)]
    [InlineData("error", LogLevel.Warning, false)]
    [InlineData("bogus", LogLevel.Information, false)]
    public void LogLevelGate_FiltersByTheConfiguredToken(string token, LogLevel probe, bool expected)
    {
        var logger = new SettingsAwareLogger(() => token);
        Assert.Equal(expected, logger.IsEnabled(probe));
    }

    [Fact]
    public void LogLevelGate_ReadsTheLiveSetting()
    {
        var settings = AppSettings.Default with { Advanced = new AdvancedSettings { LogLevel = "debug" } };
        var logger = new SettingsAwareLogger(() => settings.Advanced.LogLevel);
        Assert.True(logger.IsEnabled(LogLevel.Debug));

        settings = settings with { Advanced = settings.Advanced with { LogLevel = "error" } };
        Assert.False(logger.IsEnabled(LogLevel.Debug));
        Assert.True(logger.IsEnabled(LogLevel.Error));
    }

    // ---- general: start with Windows ----------------------------------------------------------

    [Fact]
    public async Task StartupRegistration_WritesHKCURunWithBackgroundArgument_AndRemovesIt()
    {
        var (controller, _) = TestFixtures.CreateSettings(AppSettings.Default with { StartWithWindows = true });
        var store = new DictRegistryStore();
        var service = new StartupRegistrationService(store, NullPlatformLogger.Instance);
        var exe = "C:\\Apps\\FluidBar\\FluidBar.exe";
        using var sync = new StartupRegistrationSync(controller, NullAppLogger.Instance, service, exe);
        sync.Attach();

        await sync.SyncAsync(selfHeal: true);
        var command = store.GetString(StartupRegistrationService.RunKeyPath, StartupRegistrationService.DefaultValueName);
        Assert.NotNull(command);
        Assert.Contains(exe, command);
        Assert.Contains(StartupRegistrationSync.StartupArgument, command);

        controller.Update(current => current with { StartWithWindows = false });
        await WaitUntilAsync(
            () => store.GetString(StartupRegistrationService.RunKeyPath, StartupRegistrationService.DefaultValueName) is null,
            "disabling the setting must delete the Run value");

        controller.Update(current => current with { StartWithWindows = true });
        await WaitUntilAsync(
            () => store.GetString(StartupRegistrationService.RunKeyPath, StartupRegistrationService.DefaultValueName) is { } value
                  && value.Contains(exe, StringComparison.Ordinal),
            "enabling the setting must write the Run value again");
    }

    [Fact]
    public async Task StartupRegistration_SelfHealsAStaleValue()
    {
        var (controller, _) = TestFixtures.CreateSettings(AppSettings.Default with { StartWithWindows = true });
        var store = new DictRegistryStore();
        store.SetString(StartupRegistrationService.RunKeyPath, StartupRegistrationService.DefaultValueName,
            "\"C:\\Old\\FluidBar.exe\"");
        var exe = "C:\\Apps\\FluidBar\\FluidBar.exe";
        using var sync = new StartupRegistrationSync(controller, NullAppLogger.Instance,
            new StartupRegistrationService(store, NullPlatformLogger.Instance), exe);

        await sync.SyncAsync(selfHeal: true);

        var command = store.GetString(StartupRegistrationService.RunKeyPath, StartupRegistrationService.DefaultValueName);
        Assert.NotNull(command);
        Assert.Contains(exe, command);
        Assert.DoesNotContain("Old", command);
    }

    [Fact]
    public async Task StartupRegistration_RegistryFailureRevertsTheSetting()
    {
        var (controller, _) = TestFixtures.CreateSettings(AppSettings.Default with { StartWithWindows = true });
        var service = new StartupRegistrationService(new ThrowingRegistryStore(), NullPlatformLogger.Instance);
        using var sync = new StartupRegistrationSync(controller, NullAppLogger.Instance,
            service, "C:\\Apps\\FluidBar\\FluidBar.exe");

        await sync.SyncAsync(selfHeal: true);

        Assert.False(controller.Current.StartWithWindows);
    }

    // ---- advanced: crash guard ----------------------------------------------------------------

    [Fact]
    public async Task CrashGuard_RecordsCrashes_AndEntersSafeModeAfterTheThreshold()
    {
        var path = Path.Combine(NewTempDir(), "crash-guard.json");
        var time = new FixedTimeProvider(new DateTimeOffset(2026, 10, 20, 3, 0, 0, TimeSpan.Zero));
        var guard = new CrashGuard(path, NullAppLogger.Instance, time);

        Assert.Equal(0, guard.Read().ConsecutiveCrashes);
        guard.RecordUnhandledException(new InvalidOperationException("boom"), "appdomain");
        Assert.Equal(1, guard.Read().ConsecutiveCrashes);
        Assert.False(guard.TryEnterSafeMode());

        guard.RecordUnhandledException(new InvalidOperationException("boom2"), "task");
        var state = guard.Read();
        Assert.Equal(2, state.ConsecutiveCrashes);
        Assert.True(File.Exists(path));
        Assert.Contains("\"consecutiveCrashes\":2", await File.ReadAllTextAsync(path));

        Assert.True(guard.TryEnterSafeMode());
        var after = guard.Read();
        Assert.Equal(0, after.ConsecutiveCrashes);
        Assert.NotNull(after.SafeModeEnteredAtUtc);
        Assert.False(guard.TryEnterSafeMode());
    }

    [Fact]
    public void CrashGuard_CleanExitClearsTheStreak_AndCorruptFileIsIgnored()
    {
        var path = Path.Combine(NewTempDir(), "crash-guard.json");
        var guard = new CrashGuard(path, NullAppLogger.Instance);
        guard.RecordUnhandledException(new InvalidOperationException("boom"), "appdomain");
        guard.MarkCleanExit();
        Assert.Equal(0, guard.Read().ConsecutiveCrashes);

        File.WriteAllText(path, "not json");
        Assert.Equal(0, guard.Read().ConsecutiveCrashes);
    }

    // ---- advanced: export / import ------------------------------------------------------------

    [Fact]
    public void SettingsExport_RoundTripsTheEnvelopeAndAcceptsRawSettings()
    {
        var settings = AppSettings.Default with
        {
            Palette = "nord",
            AccentFallback = "#34C759",
            PanelRadius = 31,
            AutoCollapseMs = 3600,
            Mode = "dock",
        };
        var text = SettingsExport.Create(settings, new DateTimeOffset(2026, 10, 20, 3, 0, 0, TimeSpan.Zero));
        Assert.Contains("\"exportedAt\"", text);
        Assert.Contains("\"settings\"", text);

        Assert.True(SettingsExport.TryParse(text, out var parsed, out var error), error);
        Assert.NotNull(parsed);
        Assert.Equal("nord", parsed!.Palette);
        Assert.Equal(31, parsed.PanelRadius);
        Assert.Equal(3600, parsed.AutoCollapseMs);
        Assert.Equal("dock", parsed.Mode);

        Assert.True(SettingsExport.TryParse(settings.ToJson(), out var raw, out _));
        Assert.Equal(settings.Palette, raw!.Palette);

        Assert.False(SettingsExport.TryParse("{ nope", out _, out var syntaxError));
        Assert.NotNull(syntaxError);
        Assert.False(SettingsExport.TryParse(string.Empty, out _, out _));
    }

    // ---- persistence --------------------------------------------------------------------------

    [Fact]
    public async Task JsonSettingsStore_PersistsAcrossInstances_AndFallsBackOnCorruption()
    {
        var path = Path.Combine(NewTempDir(), "settings.ui.json");
        using (var store = new JsonUiSettingsStore(path, NullAppLogger.Instance, TimeSpan.Zero))
        {
            store.QueueSave(AppSettings.Default with
            {
                Language = "zh-Hans",
                Palette = "slate",
                Mode = "flush",
                AutoCollapseMs = 1200,
                HotkeyToggle = "Ctrl+Shift+D",
            });
            await store.FlushAsync();
        }

        Assert.True(File.Exists(path));
        using (var reader = new JsonUiSettingsStore(path))
        {
            var loaded = await reader.LoadAsync();
            Assert.Equal("zh-Hans", loaded.Language);
            Assert.Equal("slate", loaded.Palette);
            Assert.Equal("flush", loaded.Mode);
            Assert.Equal(1200, loaded.AutoCollapseMs);
            Assert.Equal("Ctrl+Shift+D", loaded.HotkeyToggle);
        }

        await File.WriteAllTextAsync(path, "{ this is not json");
        using (var reader = new JsonUiSettingsStore(path, NullAppLogger.Instance))
        {
            Assert.Equal(AppSettings.Default, await reader.LoadAsync());
        }
    }

    [Fact]
    public void SettingsNormalize_ClampsBoundariesAndRejectsInvalidTokens()
    {
        var normalized = (AppSettings.Default with
        {
            PanelRadius = 100,
            FillOpacity = 0.1,
            FontScale = 9,
            MotionStrength = 9,
            AutoCollapseMs = -5,
            AutoHideMs = -1,
            Palette = "vaporwave",
            Mode = "floating",
            Anchor = "middle",
            AccentFallback = "#zzzzzz",
            Theme = (AppTheme)99,
            ReducedMotion = (ReducedMotionPreference)99,
        }).Normalize();

        Assert.Equal(36, normalized.PanelRadius);
        Assert.Equal(LayoutTokens.MinFillOpacity, normalized.FillOpacity, 3);
        Assert.Equal(1.6, normalized.FontScale, 3);
        Assert.Equal(1.5, normalized.MotionStrength, 3);
        Assert.Equal(0, normalized.AutoCollapseMs);
        Assert.Equal(0, normalized.AutoHideMs);
        Assert.Equal("obsidian", normalized.Palette);
        Assert.Equal("float", normalized.Mode);
        Assert.Equal("top-center", normalized.Anchor);
        Assert.Contains("4CC9F0", normalized.AccentFallback, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(AppTheme.System, normalized.Theme);
        Assert.Equal(ReducedMotionPreference.System, normalized.ReducedMotion);
    }

    // ---- update chain composed at the App layer -----------------------------------------------

    [Fact]
    public async Task AppUpdateCoordinator_RunsTheWholeChainOverALocalFeed()
    {
        var root = NewTempDir();
        var payload = new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03 };
        var artifact = Path.Combine(root, "FluidBar-2.1.0-setup.exe");
        await File.WriteAllBytesAsync(artifact, payload);
        var sha = await Sha256UpdateVerifier.ComputeFileSha256Async(artifact, CancellationToken.None);
        var index = Path.Combine(root, "index.json");
        await File.WriteAllTextAsync(index, $$"""
        [
          {
            "tag_name": "v2.1.0",
            "name": "2.1.0",
            "prerelease": false,
            "assets": [
              {
                "name": "FluidBar-2.1.0-setup.exe",
                "browser_download_url": "{{new Uri(artifact).AbsoluteUri}}",
                "size": {{payload.Length}},
                "digest": "sha256:{{sha}}"
              }
            ]
          }
        ]
        """);

        var previousFeed = Environment.GetEnvironmentVariable(AppUpdateCoordinator.FeedEnvironmentVariable);
        Environment.SetEnvironmentVariable(AppUpdateCoordinator.FeedEnvironmentVariable,
            new Uri(index).AbsoluteUri);
        try
        {
            var (controller, _) = TestFixtures.CreateSettings(AppSettings.Default);
            var launcher = new FakeInstallerLauncher();
            await using var coordinator = new AppUpdateCoordinator(controller, NullAppLogger.Instance,
                verifier: new Sha256UpdateVerifier(), launcher: launcher);

            Assert.True(coordinator.IsConfigured);
            Assert.Contains("index.json", coordinator.FeedDescription);
            Assert.Equal(UpdateState.Idle, coordinator.Current.State);

            await coordinator.CheckNowAsync();
            await WaitUntilAsync(() => coordinator.Current.State == UpdateState.Available,
                "the coordinator must publish the available state");
            Assert.Equal("2.1.0", coordinator.Current.AvailableRelease!.Version.ToString());

            await coordinator.DownloadCurrentAsync();
            await WaitUntilAsync(() => coordinator.Current.State == UpdateState.ReadyToInstall,
                "the coordinator must publish the verified download");
            Assert.True(File.Exists(coordinator.Current.DownloadedFilePath));

            var install = coordinator.Install();
            Assert.True(install.Launched, install.FailureReason);
            await WaitUntilAsync(() => coordinator.Current.State == UpdateState.RestartRequired,
                "the coordinator must publish the restart prompt");
            Assert.Equal(coordinator.Current.DownloadedFilePath, launcher.Path);

            coordinator.Discard();
            await WaitUntilAsync(() => coordinator.Current.State == UpdateState.Idle,
                "discarding the download resets the state");
            Assert.False(File.Exists(install.InstallerPath));
        }
        finally
        {
            Environment.SetEnvironmentVariable(AppUpdateCoordinator.FeedEnvironmentVariable, previousFeed);
        }
    }

    private sealed class FakeInstallerLauncher : IUpdateInstallerLauncher
    {
        public string? Path { get; private set; }

        public bool Launch(string installerPath)
        {
            Path = installerPath;
            return true;
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
