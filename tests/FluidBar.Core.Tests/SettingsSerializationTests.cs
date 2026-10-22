using System.Collections.Immutable;
using System.Text.Json;
using FluidBar.Core.Layout;
using FluidBar.Core.Settings;

namespace FluidBar.Core.Tests;

public class SettingsSerializationTests
{
    [Fact]
    public void Defaults_AreValid()
    {
        var settings = FluidBarSettings.CreateDefault();
        var report = SettingsValidator.Validate(settings);
        Assert.False(report.HasErrors);
        Assert.False(report.HasWarnings);
        Assert.Equal(FluidBarSettings.CurrentSchemaVersion, settings.SchemaVersion);
        Assert.Equal(IslandMode.Float, settings.Island.Mode);
        Assert.Equal(IslandAnchor.TopCenter, settings.Island.Anchor);
        Assert.Equal(StackingPreference.Split, settings.Island.Stacking);
        Assert.Equal(0.96, settings.Appearance.FillOpacity, 1e-9);
    }

    [Fact]
    public void Json_UsesCamelCaseAndSpecEnumTokens()
    {
        var json = SettingsJson.Serialize(FluidBarSettings.CreateDefault());

        Assert.Contains("\"schemaVersion\":2", json);
        Assert.Contains("\"theme\":\"dark\"", json);
        Assert.Contains("\"mode\":\"float\"", json);
        Assert.Contains("\"anchor\":\"top-center\"", json);
        Assert.Contains("\"stacking\":\"split\"", json);
        Assert.Contains("\"displayOn\":\"primary\"", json);
        Assert.Contains("\"autoUpdate\":\"notify\"", json);
        Assert.Contains("\"reducedMotion\":\"system\"", json);
        Assert.Contains("\"material\":\"obsidian\"", json);
    }

    [Fact]
    public void Json_RoundTripsValues()
    {
        var settings = FluidBarSettings.CreateDefault() with
        {
            Appearance = new AppearanceSettings
            {
                Theme = AppTheme.Light,
                Material = IslandMaterial.Acrylic,
                FillOpacity = 0.9,
                FontScale = 1.25,
                Accent = new AccentSettings { Mode = AccentMode.Custom, CustomHex = "#FF8800" },
            },
            Island = new IslandSettings
            {
                Mode = IslandMode.Dock,
                Anchor = IslandAnchor.BottomRight,
                Stacking = StackingPreference.Latest,
                AutoCollapseMs = 1200,
            },
            General = new GeneralSettings { Language = "zh-Hans", TrayIcon = false },
            Plugins = new PluginsSettings { Enabled = ["builtin.media"], Channel = "beta" },
        };

        var json = SettingsJson.Serialize(settings);
        Assert.True(SettingsJson.TryDeserialize(json, out var parsed, out var error), error);
        Assert.NotNull(parsed);
        Assert.Equal(AppTheme.Light, parsed!.Appearance.Theme);
        Assert.Equal(IslandMaterial.Acrylic, parsed.Appearance.Material);
        Assert.Equal(0.9, parsed.Appearance.FillOpacity, 1e-9);
        Assert.Equal(1.25, parsed.Appearance.FontScale, 1e-9);
        Assert.Equal(AccentMode.Custom, parsed.Appearance.Accent.Mode);
        Assert.Equal("#FF8800", parsed.Appearance.Accent.CustomHex);
        Assert.Equal(IslandMode.Dock, parsed.Island.Mode);
        Assert.Equal(IslandAnchor.BottomRight, parsed.Island.Anchor);
        Assert.Equal(StackingPreference.Latest, parsed.Island.Stacking);
        Assert.Equal(1200, parsed.Island.AutoCollapseMs);
        Assert.Equal("zh-Hans", parsed.General.Language);
        Assert.False(parsed.General.TrayIcon);
        Assert.Equal("builtin.media", string.Join(',', parsed.Plugins.Enabled));
        Assert.Equal("beta", parsed.Plugins.Channel);
    }

    [Fact]
    public void Json_MissingFieldsUseDefaults()
    {
        Assert.True(SettingsJson.TryDeserialize("{\"schemaVersion\":2}", out var parsed, out var error), error);
        Assert.NotNull(parsed);
        Assert.Equal(IslandMode.Float, parsed!.Island.Mode);
        Assert.Equal(0.96, parsed.Appearance.FillOpacity, 1e-9);
        Assert.Contains(BuiltinPlugins.Notifications, parsed.Plugins.Enabled);
    }

    [Fact]
    public void Json_InvalidEnumValueFails()
    {
        Assert.False(SettingsJson.TryDeserialize("{\"schemaVersion\":2,\"appearance\":{\"theme\":\"neon\"}}", out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Placement_ResolvesMonitorThenPrimaryFallback()
    {
        var placement = new PlacementSettings();
        placement.Monitors["\\\\.\\DISPLAY2"] = new MonitorPlacement { Anchor = IslandAnchor.TopRight, OffsetX = 5 };

        var exact = placement.Resolve("\\\\.\\DISPLAY2", isPrimary: false);
        Assert.Equal(IslandAnchor.TopRight, exact.Anchor);
        Assert.Equal(5, exact.OffsetX, 1e-9);

        var fallback = placement.Resolve("\\\\.\\DISPLAY9", isPrimary: false);
        Assert.Equal(IslandAnchor.TopCenter, fallback.Anchor);
    }

    [Fact]
    public void StoreOptions_ForAppDataBuildsPaths()
    {
        var options = SettingsStoreOptions.ForAppData("C:\\Users\\test\\AppData\\Roaming\\FluidBar");
        Assert.EndsWith("settings.json", options.SettingsPath);
        Assert.EndsWith("settings.json.bak", options.EffectiveBackupPath);
        Assert.EndsWith("settings.json.tmp", options.EffectiveTempPath);
        Assert.EndsWith("settings.v1.backup.json", options.EffectiveV1BackupPath);
    }
}

public class HotkeyParserTests
{
    [Fact]
    public void Parse_ValidCombination()
    {
        Assert.True(HotkeyParser.TryParse("Ctrl+Alt+D", out var binding, out var error), error);
        Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Alt, binding.Modifiers);
        Assert.Equal("D", binding.Key);
        Assert.Equal("Ctrl+Alt+D", HotkeyParser.Format(binding));
    }

    [Fact]
    public void Parse_LowercaseIsCanonicalized()
    {
        Assert.True(HotkeyParser.TryParse("ctrl+alt+d", out var binding, out _));
        Assert.Equal("Ctrl+Alt+D", HotkeyParser.Format(binding));
    }

    [Theory]
    [InlineData("LeftAlt", "LeftAlt", HotkeyModifiers.None)]
    [InlineData("Ctrl+F5", "F5", HotkeyModifiers.Control)]
    [InlineData("Win+Shift+S", "S", HotkeyModifiers.Win | HotkeyModifiers.Shift)]
    [InlineData("Ctrl+Alt+Space", "Space", HotkeyModifiers.Control | HotkeyModifiers.Alt)]
    public void Parse_NamedKeys(string text, string key, HotkeyModifiers modifiers)
    {
        Assert.True(HotkeyParser.TryParse(text, out var binding, out var error), error);
        Assert.Equal(key, binding.Key);
        Assert.Equal(modifiers, binding.Modifiers);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+Alt+D+E")]
    [InlineData("Ctrl+Blah")]
    [InlineData("F25")]
    public void Parse_InvalidInputFails(string text)
    {
        Assert.False(HotkeyParser.TryParse(text, out _, out var error));
        Assert.NotNull(error);
    }
}

public class SettingsValidationTests
{
    [Fact]
    public void Normalize_ClampsRanges()
    {
        var input = FluidBarSettings.CreateDefault() with
        {
            Appearance = new AppearanceSettings
            {
                FillOpacity = 0.5,
                FontScale = 3,
                MotionStrength = 0.1,
                BloomStrength = 5,
            },
            Island = new IslandSettings { AutoCollapseMs = -100, AutoHideMs = int.MaxValue },
            Dock = new DockSettings { Height = 10 },
        };

        var normalized = SettingsValidator.Normalize(input, out var report);

        Assert.Equal(SettingsValidator.MinFillOpacity, normalized.Appearance.FillOpacity, 1e-9);
        Assert.Equal(SettingsValidator.MaxFontScale, normalized.Appearance.FontScale, 1e-9);
        Assert.Equal(SettingsValidator.MinMotionStrength, normalized.Appearance.MotionStrength, 1e-9);
        Assert.Equal(2, normalized.Appearance.BloomStrength, 1e-9);
        Assert.Equal(SettingsValidator.MinAutoCollapseMs, normalized.Island.AutoCollapseMs);
        Assert.Equal(SettingsValidator.MaxAutoHideMs, normalized.Island.AutoHideMs);
        Assert.Equal(SettingsValidator.MinDockHeight, normalized.Dock.Height);
        Assert.True(report.HasWarnings);
        Assert.True(report.Issues.Any(i => i.Code == "range.clamped"));
    }

    [Fact]
    public void Normalize_DefaultsInvalidEnumsAndStrings()
    {
        var input = FluidBarSettings.CreateDefault() with
        {
            Advanced = new AdvancedSettings { LogLevel = "verbose" },
            Plugins = new PluginsSettings { Channel = "nightly" },
            I18n = new I18nSettings { Language = "not a language!" },
            Appearance = new AppearanceSettings
            {
                Accent = new AccentSettings { FallbackHex = "blue" },
            },
        };

        var normalized = SettingsValidator.Normalize(input, out var report);

        Assert.Equal("warn", normalized.Advanced.LogLevel);
        Assert.Equal("stable", normalized.Plugins.Channel);
        Assert.Equal("system", normalized.I18n.Language);
        Assert.Equal("#4CC9F0", normalized.Appearance.Accent.FallbackHex);
        Assert.True(report.HasWarnings);
    }

    [Fact]
    public void Normalize_InvalidHotkeysAreClearedAndConflictsReported()
    {
        var input = FluidBarSettings.CreateDefault() with
        {
            Hotkeys = new HotkeySettings
            {
                Toggle = "Ctrl+Alt+D",
                Hide = "Ctrl+Alt+D",
                Settings = "shout",
            },
        };

        var normalized = SettingsValidator.Normalize(input, out var report);

        Assert.Equal("Ctrl+Alt+D", normalized.Hotkeys.Toggle);
        Assert.Equal(string.Empty, normalized.Hotkeys.Settings);
        Assert.Contains(report.Issues, i => i.Code == "hotkey.invalid");
        Assert.Contains(report.Issues, i => i.Code == "hotkey.conflict");
    }

    [Fact]
    public void Normalize_InvalidMarketUrlIsErrorAndFallsBack()
    {
        var input = FluidBarSettings.CreateDefault() with
        {
            Market = new MarketSettings { SourceUrl = "not-a-url" },
        };

        var normalized = SettingsValidator.Normalize(input, out var report);

        Assert.Equal(SettingsJsonNames.DefaultMarketSourceUrl, normalized.Market.SourceUrl);
        Assert.True(report.HasErrors);
    }

    [Fact]
    public void Normalize_EmptyMarketUrlWarns()
    {
        var input = FluidBarSettings.CreateDefault() with { Market = new MarketSettings { SourceUrl = " " } };
        _ = SettingsValidator.Normalize(input, out var report);
        Assert.Contains(report.Issues, i => i.Code == "url.empty");
    }

    [Fact]
    public void Normalize_PlacementDropsEmptyMonitorIdsAndKeepsPrimary()
    {
        var input = FluidBarSettings.CreateDefault() with
        {
            Placement = new PlacementSettings
            {
                Monitors = new Dictionary<string, MonitorPlacement>(StringComparer.Ordinal)
                {
                    [""] = new MonitorPlacement(),
                    ["\\\\.\\DISPLAY1"] = new MonitorPlacement { OffsetX = 4 },
                },
            },
        };

        var normalized = SettingsValidator.Normalize(input, out var report);

        Assert.False(normalized.Placement.Monitors.ContainsKey(string.Empty));
        Assert.True(normalized.Placement.Monitors.ContainsKey("\\\\.\\DISPLAY1"));
        Assert.True(normalized.Placement.Monitors.ContainsKey(SettingsJsonNames.PrimaryPlacementKey));
        Assert.Contains(report.Issues, i => i.Code == "monitor.emptyId");
    }

    [Fact]
    public void Normalize_ActivityDurationsAndBlocklist()
    {
        var input = FluidBarSettings.CreateDefault() with
        {
            Activities = new ActivitySettings
            {
                Media = new ActivityFeatureSettings
                {
                    DurationMs = -5,
                    Blocklist = [" Chrome ", "chrome", "", "YouTube"],
                },
                Notification = new ActivityFeatureSettings { DurationMs = 50 },
            },
        };

        var normalized = SettingsValidator.Normalize(input, out var report);

        Assert.Null(normalized.Activities.Media.DurationMs);
        Assert.Equal("chrome,youtube", string.Join(',', normalized.Activities.Media.Blocklist));
        Assert.Equal(100, normalized.Activities.Notification.DurationMs);
        Assert.True(report.HasWarnings);
    }

    [Fact]
    public void ActivitySettings_For_ReturnsConfiguredKind()
    {
        var settings = FluidBarSettings.CreateDefault();
        Assert.True(settings.Activities.For(Activities.ActivityKind.Media).ShowLyrics);
        Assert.True(settings.Activities.For(Activities.ActivityKind.Notification).DetectVerificationCode);
        Assert.Equal(1600, settings.Activities.For(Activities.ActivityKind.Volume).DurationMs);
    }

    [Fact]
    public void HexColors_AreNormalizedToUppercase()
    {
        var input = FluidBarSettings.CreateDefault() with
        {
            Appearance = new AppearanceSettings
            {
                Accent = new AccentSettings { FallbackHex = "#4cc9f0" },
            },
        };
        var normalized = SettingsValidator.Normalize(input, out _);
        Assert.Equal("#4CC9F0", normalized.Appearance.Accent.FallbackHex);
    }
}

public class SettingsMigrationTests
{
    public const string V1Sample = """
    {
      "Position": "TopLeft",
      "OffsetX": 10,
      "OffsetY": 20,
      "CollapsedWidth": 126,
      "CollapsedHeight": 38,
      "ExpandedMaxWidth": 430,
      "ExpandedHeight": 76,
      "CornerRadius": 24,
      "Opacity": 96,
      "BackgroundOpacity": 0.75,
      "BackgroundColor": "#F4000000",
      "AccentColor": "#0A84FF",
      "AlwaysOnTop": true,
      "AlwaysVisible": false,
      "AutoHideDelayMs": 4000,
      "DisplayStrategy": "Multiple",
      "MaxVisibleIslands": 4,
      "MultiIslandGap": 10,
      "RimMode": "Event",
      "ClipboardEnabled": false,
      "PluginEnabled": { "clipboard": true, "media": false, "communityx": true },
      "MonitorFeatureSettings": {
        "volume": { "HoverCardEnabled": true, "DisplayDurationMs": 2000 },
        "notification": { "VerificationCodeDetection": false },
        "unknownFeature": { "Enabled": true }
      },
      "HideTrayIcon": true,
      "HoldToHideKey": "Disabled",
      "SettingsTheme": "Light",
      "AutoUpdateCheck": false,
      "AgentHooksGuardEnabled": true,
      "AgentHooksGuardIntervalMs": 30000,
      "AgentKeepIslandVisible": true,
      "HooksGuardEnabled": true,
      "HooksGuardIntervalMs": 30000,
      "UnknownFutureField": 42
    }
    """;

    [Fact]
    public void DetectVersion_RecognizesLegacyAndV2()
    {
        Assert.Null(SettingsMigrator.DetectVersion(V1Sample, out var error));
        Assert.Null(error);

        var v2 = SettingsJson.Serialize(FluidBarSettings.CreateDefault());
        Assert.Equal(2, SettingsMigrator.DetectVersion(v2, out _));

        _ = SettingsMigrator.DetectVersion("{oops", out var jsonError);
        Assert.NotNull(jsonError);
    }

    [Fact]
    public void MigrateV1_MapsKnownFields()
    {
        var result = SettingsMigrator.MigrateV1(V1Sample);

        Assert.True(result.Report.Success);
        Assert.Equal(1, result.Report.FromVersion);
        Assert.Equal(2, result.Report.ToVersion);
        Assert.True(result.Report.MappedCount >= 8);

        var settings = result.Settings;
        Assert.Equal(IslandAnchor.TopLeft, settings.Island.Anchor);
        Assert.Equal(10, settings.Placement.Monitors[SettingsJsonNames.PrimaryPlacementKey].OffsetX, 1e-9);
        Assert.Equal(20, settings.Placement.Monitors[SettingsJsonNames.PrimaryPlacementKey].OffsetY!.Value, 1e-9);
        Assert.Equal(0.96, settings.Appearance.FillOpacity, 1e-9);
        Assert.Equal(AppTheme.Light, settings.Appearance.Theme);
        Assert.Equal(AccentMode.System, settings.Appearance.Accent.Mode);
        Assert.Equal(4000, settings.Island.AutoCollapseMs);
        Assert.Equal(StackingPreference.Stack, settings.Island.Stacking);
        Assert.Null(settings.Hotkeys.HoldToHide);
        Assert.False(settings.General.TrayIcon);
        Assert.Equal(AutoUpdatePreference.Off, settings.General.AutoUpdate);
        Assert.False(settings.Activities.Clipboard.Enabled);
        Assert.Equal(2000, settings.Activities.Volume.DurationMs);
        Assert.True(settings.Activities.Volume.Enabled);
        Assert.False(settings.Activities.Notification.DetectVerificationCode);
        Assert.Contains(BuiltinPlugins.Clipboard, settings.Plugins.Enabled);
        Assert.Contains("communityx", settings.Plugins.Enabled);
        Assert.Contains("communityx", settings.Plugins.Orphaned);
    }

    [Fact]
    public void MigrateV1_ReportsDroppedFields()
    {
        var report = SettingsMigrator.MigrateV1(V1Sample).Report;
        var dropped = report.Dropped.Select(d => d.SourcePath).ToArray();

        Assert.Contains("CornerRadius", dropped);
        Assert.Contains("AlwaysOnTop", dropped);
        Assert.Contains("UnknownFutureField", dropped);
        Assert.Contains("AgentHooksGuardEnabled", dropped);
        Assert.Contains("HooksGuardEnabled", dropped);
    }

    [Fact]
    public void MigrateV1_MapsUnknownPositionWithReport()
    {
        var json = """{ "Position": "MiddleOfNowhere", "Opacity": 90 }""";
        var result = SettingsMigrator.MigrateV1(json);

        Assert.True(result.Report.Success);
        Assert.Equal(IslandAnchor.TopCenter, result.Settings.Island.Anchor);
        Assert.Contains(result.Report.Dropped, d => d.SourcePath == "Position");
        Assert.Equal(0.9, result.Settings.Appearance.FillOpacity, 1e-9);
    }

    [Fact]
    public void Migrate_AcceptsV2Document()
    {
        var json = SettingsJson.Serialize(FluidBarSettings.CreateDefault() with
        {
            Appearance = new AppearanceSettings { Theme = AppTheme.Light },
        });

        var result = SettingsMigrator.Migrate(json);

        Assert.True(result.Report.Success);
        Assert.Equal(2, result.Report.FromVersion);
        Assert.Equal(AppTheme.Light, result.Settings.Appearance.Theme);
    }

    [Fact]
    public void Migrate_RejectsFutureVersion()
    {
        var result = SettingsMigrator.Migrate("""{ "schemaVersion": 99 }""");
        Assert.False(result.Report.Success);
        Assert.NotNull(result.Report.Error);
    }

    [Fact]
    public void Migrate_InvalidJsonFails()
    {
        var result = SettingsMigrator.Migrate("{not json");
        Assert.False(result.Report.Success);
        Assert.NotNull(result.Report.Error);
    }

    [Fact]
    public void MigrateV1_CustomAccentIsPreserved()
    {
        var json = """{ "AccentColor": "#FF8800" }""";
        var result = SettingsMigrator.MigrateV1(json);

        Assert.Equal(AccentMode.Custom, result.Settings.Appearance.Accent.Mode);
        Assert.Equal("#FF8800", result.Settings.Appearance.Accent.CustomHex);
    }

    [Fact]
    public void MigrateV1_EmptyPluginSetKeepsDefaults()
    {
        var result = SettingsMigrator.MigrateV1("""{ "Position": "Top" }""");
        Assert.True(result.Report.Success);
        Assert.Empty(result.Settings.Plugins.Orphaned);
    }
}
