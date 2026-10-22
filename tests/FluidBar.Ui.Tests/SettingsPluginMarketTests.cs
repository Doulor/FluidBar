using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using FluidBar.Sdk.Contributions;
using Avalonia;
using FluidBar.App.Localization;
using FluidBar.App.Settings;
using FluidBar.App.Views.Market;
using FluidBar.App.Views.Plugins;
using FluidBar.App.Views.Settings;
using FluidBar.Core.Settings;
using FluidBar.Marketplace.Index;
using FluidBar.Sdk.Manifest;
using AppTheme = FluidBar.App.Settings.AppTheme;
using AppMaterial = FluidBar.App.Settings.IslandMaterial;
using AppDisplayTarget = FluidBar.App.Settings.DisplayTarget;
using AppAdvanced = FluidBar.App.Settings.AdvancedSettings;
using AppStacking = FluidBar.App.Settings.StackingStrategySetting;
using AppReducedMotion = FluidBar.App.Settings.ReducedMotionPreference;
using CoreAppTheme = FluidBar.Core.Settings.AppTheme;
using CoreMonitor = FluidBar.Core.Settings.MonitorPlacement;

namespace FluidBar.Ui.Tests;

/// <summary>Core settings adapter mapping/persistence (spec 8.1/8.2).</summary>
public sealed class CoreSettingsAdapterTests
{
    [Fact]
    public void ApplyToCore_MapsKnownFields_AndPreservesCoreOwnedSections()
    {
        var core = new FluidBarSettings
        {
            Hotkeys = new HotkeySettings { Toggle = "Ctrl+Shift+1", Hide = "Ctrl+Shift+2" },
            Dock = new FluidBar.Core.Settings.DockSettings { Height = 58, Tiles = ["media"] },
            General = new GeneralSettings { Language = "en-US", StartWithWindows = true },
            Island = new IslandSettings { Anchor = FluidBar.Core.Layout.IslandAnchor.TopCenter },
        };
        var app = new AppSettings
        {
            Language = "zh-Hans",
            Theme = AppTheme.Light,
            Material = AppMaterial.Acrylic,
            Palette = "nord",
            FillOpacity = 0.92,
            FontScale = 1.15,
            Mode = "dock",
            Anchor = "bottom-right",
            Stacking = AppStacking.Latest,
            HoverPeek = false,
            AutoCollapseMs = 1500,
            AutoHideMs = 45000,
            DisplayOn = AppDisplayTarget.All,
            AutoUpdate = "off",
            Advanced = new AppAdvanced { SafeMode = true },
        };

        var mapped = CoreSettingsStoreAdapter.ApplyToCore(core, app);
        Assert.Equal("zh-Hans", mapped.General.Language);
        Assert.Equal(CoreAppTheme.Light, mapped.Appearance.Theme);
        Assert.Equal(FluidBar.Core.Settings.IslandMaterial.Acrylic, mapped.Appearance.Material);
        Assert.Equal(FluidBar.Core.Settings.ColorPalette.Nord, mapped.Appearance.Palette);
        Assert.Equal(FluidBar.Core.Layout.IslandMode.Dock, mapped.Island.Mode);
        Assert.Equal(FluidBar.Core.Layout.IslandAnchor.BottomRight, mapped.Island.Anchor);
        Assert.Equal(FluidBar.Core.Settings.StackingPreference.Latest, mapped.Island.Stacking);
        Assert.Equal(FluidBar.Core.Settings.DisplayTarget.All, mapped.Island.DisplayOn);
        Assert.Equal(AutoUpdatePreference.Off, mapped.General.AutoUpdate);
        Assert.True(mapped.General.SafeMode);

        // Core-owned sections survive the read-modify-write.
        // The App UI model owns the dock height (defaults to 52); Core-owned tiles survive.
        Assert.Equal(52, mapped.Dock.Height);
        Assert.Equal(["media"], mapped.Dock.Tiles.ToArray());
    }

    [Fact]
    public void LiquidGlassMaterial_RoundTripsThroughTheCoreAdapter()
    {
        var core = FluidBarSettings.CreateDefault();
        var app = AppSettings.Default with { Material = AppMaterial.LiquidGlass };

        var mapped = CoreSettingsStoreAdapter.ApplyToCore(core, app);
        Assert.Equal(FluidBar.Core.Settings.IslandMaterial.LiquidGlass, mapped.Appearance.Material);

        var back = CoreSettingsStoreAdapter.ToAppSettings(mapped);
        Assert.Equal(AppMaterial.LiquidGlass, back.Material);
    }

    [Fact]
    public void ToAppSettings_RoundTripsTheUiModel()
    {
        var app = new AppSettings
        {
            Language = "zh-Hans",
            Theme = AppTheme.Dark,
            Material = AppMaterial.Obsidian,
            Palette = "slate",
            MotionStrength = 1.2,
            ReducedMotion = AppReducedMotion.On,
            Mode = "flush",
            Anchor = "top-left",
            Stacking = AppStacking.Latest,
            DisplayOn = AppDisplayTarget.FollowMouse,
            AutoCollapseMs = 900,
            AutoHideMs = 0,
        };
        var core = CoreSettingsStoreAdapter.ApplyToCore(new FluidBarSettings(), app);
        var roundTripped = CoreSettingsStoreAdapter.ToAppSettings(core);

        Assert.Equal(app.Language, roundTripped.Language);
        Assert.Equal(app.Theme, roundTripped.Theme);
        Assert.Equal(app.Palette, roundTripped.Palette);
        Assert.Equal(app.Mode, roundTripped.Mode);
        Assert.Equal(app.Anchor, roundTripped.Anchor);
        Assert.Equal(app.Stacking, roundTripped.Stacking);
        Assert.Equal(app.DisplayOn, roundTripped.DisplayOn);
        Assert.Equal(app.AutoCollapseMs, roundTripped.AutoCollapseMs);
        Assert.Equal(app.ReducedMotion, roundTripped.ReducedMotion);
        Assert.Equal(app.MotionStrength, roundTripped.MotionStrength);
    }

    [Fact]
    public async Task Adapter_FlushWaitsForQueuedDebounceWrites()
    {
        var fs = new InMemorySettingsFileSystem();
        var options = new SettingsStoreOptions
        {
            SettingsPath = @"C:\test\settings.json",
            KeepBackup = false,
        };
        var logger = FluidBar.Core.Diagnostics.NullAppLogger.Instance;
        var store = new SettingsStore(fs, options, logger);
        using var adapter = new CoreSettingsStoreAdapter(store, logger, TimeSpan.Zero);
        var initial = await adapter.LoadAsync();

        for (var i = 0; i < 20; i++)
        {
            adapter.QueueSave(initial with { Language = i % 2 == 0 ? "zh-Hans" : "en-US" });
        }

        await adapter.FlushAsync();

        var reloadedStore = new SettingsStore(fs, options, logger);
        using var reloaded = new CoreSettingsStoreAdapter(reloadedStore, logger);
        var loaded = await reloaded.LoadAsync();
        Assert.Equal("en-US", loaded.Language);
        Assert.True(adapter.SaveCount >= 1);
    }

    [Fact]
    public async Task Adapter_PersistsThroughCoreStore_AndLoadsBack()
    {
        var fs = new InMemorySettingsFileSystem();
        var options = new SettingsStoreOptions
        {
            SettingsPath = @"C:\test\settings.json",
            KeepBackup = false,
        };
        var logger = FluidBar.Core.Diagnostics.NullAppLogger.Instance;
        var store = new SettingsStore(fs, options, logger);
        using var adapter = new CoreSettingsStoreAdapter(store, logger, TimeSpan.FromMilliseconds(10));
        var initial = await adapter.LoadAsync();
        Assert.Equal(AppSettings.Default.Normalize().Language, initial.Language);

        adapter.QueueSave(initial with { Language = "zh-Hans", Mode = "dock", AutoCollapseMs = 1234 });
        await adapter.FlushAsync();
        Assert.True(adapter.SaveCount >= 1);

        var reloadedStore = new SettingsStore(fs, options, logger);
        using var reloaded = new CoreSettingsStoreAdapter(reloadedStore, logger);
        var loaded = await reloaded.LoadAsync();
        Assert.Equal("zh-Hans", loaded.Language);
        Assert.Equal("dock", loaded.Mode);
        Assert.Equal(1234, loaded.AutoCollapseMs);
    }
}

/// <summary>Declarative plugin contribution UI + marketplace index adapter.</summary>
public sealed class PluginAndMarketplaceAdapterTests
{
    [Fact]
    public async Task BuiltinPluginService_ListsDescriptorsLocalized_WithContributions()
    {
        var result = await AvaloniaTest.Run(() =>
        {
            var localizer = TestFixtures.CreateLocalizer("en-US");
            var (settings, _) = TestFixtures.CreateSettings();
            var service = new BuiltinPluginContributionUiService(settings, localizer);
            var list = service.ListAsync(CancellationToken.None).GetAwaiter().GetResult();
            return (list, service, localizer);
        });

        Assert.Equal(5, result.list.Count);
        Assert.Contains(result.list, item => item.Id == "fluidbar-media" && item.Name == "Now Playing");
        Assert.Contains(result.list, item => item.Builtin);

        var mediaPages = result.service.SettingsPages("fluidbar-media");
        Assert.Single(mediaPages);
        Assert.NotEmpty(result.service.Commands("fluidbar-media"));
        Assert.NotEmpty(result.service.Capabilities("fluidbar-media"));
    }

    [Fact]
    public async Task PluginSettingsRenderer_BuildsDeclarativePages_AndInvokesCommands()
    {
        var outcome = await AvaloniaTest.Run(() =>
        {
            var localizer = TestFixtures.CreateLocalizer("en-US");
            var (settings, _) = TestFixtures.CreateSettings();
            var service = new BuiltinPluginContributionUiService(settings, localizer);
            var pages = service.SettingsPages("fluidbar-media");
            var controls = pages.SelectMany(page => (page.Controls ?? []).Concat((page.Groups ?? []).SelectMany(group => group.Controls ?? []))).ToList();
            var content = PluginSettingsPageRenderer.BuildPage(pages[0], "fluidbar-media", localizer, service.Values, (_, _) => { }, _ => { });
            Assert.NotNull(content);
            return (controls.Count, content, service);
        });

        Assert.True(outcome.Count > 0, "built-in media settings page should expose controls");
        Assert.NotNull(outcome.content);
        await outcome.service.InvokeCommandAsync("fluidbar-media", "media.toggle-play-pause");
        Assert.Equal("media.toggle-play-pause", outcome.service.LastInvokedCommand);
    }

    [Fact]
    public async Task Schema_SecretControlsRenderRedacted()
    {
        var outcome = await AvaloniaTest.Run(() =>
        {
            var localizer = TestFixtures.CreateLocalizer("en-US");
            var palette = SettingsPalette.For(AppTheme.Dark);
            var values = new InMemoryPluginSettingsValues();
            values.Set("sample", "api.token", System.Text.Json.JsonSerializer.SerializeToElement("s3cr3t"));
            var page = new SettingsPageContribution
            {
                Id = "sample",
                Controls =
                [
                    new SettingControlContribution
                    {
                        Type = SettingControlType.Secret,
                        Key = "api.token",
                        Title = LocalizedString.From("api.token"),
                    },
                ],
            };
            var content = PluginSettingsPageRenderer.BuildPage(
                page, "sample", localizer, values, (_, _) => { }, _ => { }, palette);
            var boxes = content.GetLogicalDescendants().OfType<TextBox>().ToList();
            return (boxes.Count, Masked: boxes.All(box => box.Text == "••••••••" && box.IsReadOnly),
                Leaks: boxes.Any(box => (box.Text ?? string.Empty).Contains("s3cr3t", StringComparison.Ordinal)));
        });

        Assert.Equal(1, outcome.Count);
        Assert.True(outcome.Masked, "the secret control must render a read-only mask");
        Assert.False(outcome.Leaks, "the stored secret must never be bound into the UI");
    }

    [Fact]
    public async Task MarketplaceAdapter_MapsSignedIndexEntries_AndInstallState()
    {
        var index = new MarketplaceIndexDocument
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            Plugins =
            [
                new MarketplacePluginEntry
                {
                    Id = "now-playing",
                    Name = LocalizedString.FromMap(new Dictionary<string, string> { ["en-US"] = "Now Playing+", ["zh-Hans"] = "正在播放+" }),
                    Summary = LocalizedString.From("Lyrics and artwork"),
                    Publisher = new MarketplacePublisherInfo { Id = "doulor", DisplayName = "doulor", Verified = true },
                    Latest = new MarketplaceVersionInfo { Version = "2.1.0", PublishedAt = DateTimeOffset.UtcNow },
                    Categories = ["media"],
                    Stats = new MarketplaceStats { Downloads = 18400 },
                    Featured = true,
                    Status = PluginListingStatus.Active,
                },
                new MarketplacePluginEntry
                {
                    Id = "focus-timer",
                    Name = LocalizedString.From("Focus Timer"),
                    Summary = LocalizedString.From("Pomodoro"),
                    Publisher = new MarketplacePublisherInfo { Id = "doulor", DisplayName = "doulor" },
                    Latest = new MarketplaceVersionInfo { Version = "1.4.2", PublishedAt = DateTimeOffset.UtcNow },
                    Categories = ["productivity"],
                    Status = PluginListingStatus.Active,
                },
            ],
        };

        var result = await AvaloniaTest.RunAsync(async () =>
        {
            var localizer = TestFixtures.CreateLocalizer("en-US");
            var installs = new FakeInstallOperations { InstalledVersion = "2.0.0" };
            var adapter = new MarketplaceServiceAdapter(localizer, "stable", indexProvider: _ => Task.FromResult(index), installs: installs);
            var all = await adapter.BrowseAsync(new MarketplaceQuery(null, "all", "stable"), CancellationToken.None);
            var media = await adapter.BrowseAsync(new MarketplaceQuery("playing", "media", "stable"), CancellationToken.None);
            var installed = await adapter.GetPluginAsync("now-playing", CancellationToken.None);
            var plugin = all.First(p => p.Id == "now-playing");
            return (all, media, plugin, installed);
        });

        Assert.Equal(2, result.all.Count);
        Assert.Single(result.media);
        Assert.Equal("Now Playing+", result.plugin.Name);
        Assert.True(result.plugin.Verified);
        Assert.True(result.plugin.Featured);
        Assert.Equal(18400, result.plugin.Installs);
        Assert.Equal(MarketInstallState.UpdateAvailable, result.installed!.State);
        Assert.Equal("2.0.0", result.installed.InstalledVersion);
    }

    private static string RepoPath(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FluidBar.sln")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory!.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
    }

    private sealed class FakeInstallOperations : IPluginInstallOperations
    {
        public string? InstalledVersion { get; set; }

        public string? GetInstalledVersion(string pluginId) => InstalledVersion;

        public Task InstallAsync(
            MarketplacePluginEntry entry,
            MarketplaceVersionInfo version,
            IProgress<InstallProgress> progress,
            CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task RollbackAsync(string pluginId, string version, IProgress<InstallProgress> progress, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task UninstallAsync(string pluginId, bool keepSettings, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
