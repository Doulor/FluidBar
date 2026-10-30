using System.Globalization;
using System.Reflection;
using Avalonia;
using FluidBar.App.Island;
using FluidBar.App.Localization;
using FluidBar.App.Rendering;
using FluidBar.App.Settings;
using FluidBar.App.Theme;
using FluidBar.App.Views.Market;
using FluidBar.App.Views.Settings;
using FluidBar.Core.Layout;

namespace FluidBar.Ui.Tests;

/// <summary>Catalog, formatting, key-parity and runtime-switch behavior (spec 7).</summary>
public sealed class LocalizationTests
{
    private static readonly System.Text.RegularExpressions.Regex PlaceholderPattern =
        new(@"\{([A-Za-z0-9_]+)", System.Text.RegularExpressions.RegexOptions.Compiled);

    [Fact]
    public void EnAndZhCatalogs_HaveIdenticalKeySets_AndNonEmptyText()
    {
        var en = JsonCatalogParser.Parse(File.ReadAllText(RepoPath("src/FluidBar.App/Localization/en-US.json")), "en-US");
        var zh = JsonCatalogParser.Parse(File.ReadAllText(RepoPath("src/FluidBar.App/Localization/zh-Hans.json")), "zh-Hans");

        Assert.Equal(en.Messages.Keys.OrderBy(static k => k, StringComparer.Ordinal),
            zh.Messages.Keys.OrderBy(static k => k, StringComparer.Ordinal));
        Assert.All(en.Messages, pair => Assert.False(string.IsNullOrWhiteSpace(pair.Value), pair.Key));
        Assert.All(zh.Messages, pair => Assert.False(string.IsNullOrWhiteSpace(pair.Value), pair.Key));
        Assert.True(en.Messages.Count > 300, $"catalog should be comprehensive, got {en.Messages.Count}");
    }

    [Fact]
    public void EnAndZhCatalogs_KeepTheSamePlaceholderNames()
    {
        var en = JsonCatalogParser.Parse(File.ReadAllText(RepoPath("src/FluidBar.App/Localization/en-US.json")), "en-US");
        var zh = JsonCatalogParser.Parse(File.ReadAllText(RepoPath("src/FluidBar.App/Localization/zh-Hans.json")), "zh-Hans");

        foreach (var pair in en.Messages)
        {
            var zhText = zh.Messages[pair.Key];
            var enPlaceholders = PlaceholderPattern.Matches(pair.Value).Select(static m => m.Groups[1].Value).Distinct().Order().ToArray();
            var zhPlaceholders = PlaceholderPattern.Matches(zhText).Select(static m => m.Groups[1].Value).Distinct().Order().ToArray();
            Assert.Equal(enPlaceholders, zhPlaceholders);
        }
    }

    [Fact]
    public void LocKeys_ConstantsMatchCatalogAndEveryCatalogKeyHasAConstant()
    {
        var en = JsonCatalogParser.Parse(File.ReadAllText(RepoPath("src/FluidBar.App/Localization/en-US.json")), "en-US");
        var constants = new List<string>();
        CollectConstants(typeof(LocKeys), constants);

        Assert.All(constants, key => Assert.True(en.Messages.ContainsKey(key), $"missing catalog key {key}"));
        var constantSet = constants.ToHashSet(StringComparer.Ordinal);
        foreach (var key in en.Messages.Keys)
        {
            Assert.Contains(key, constantSet);
        }
    }

    [Fact]
    public void PseudoCatalog_WrapsAndPreservesPlaceholders()
    {
        var en = JsonCatalogParser.Parse(File.ReadAllText(RepoPath("src/FluidBar.App/Localization/en-US.json")), "en-US");
        var pseudo = PseudoCatalogBuilder.Build(en);

        Assert.Equal(Locales.Pseudo, pseudo.Locale);
        Assert.Equal(en.Messages.Count, pseudo.Messages.Count);
        Assert.All(pseudo.Messages.Values, value =>
        {
            Assert.StartsWith("\u27E6", value);
            Assert.EndsWith("\u27E7", value);
        });

        foreach (var pair in en.Messages)
        {
            var enPlaceholders = PlaceholderPattern.Matches(pair.Value).Select(static m => m.Groups[1].Value).ToArray();
            var pseudoPlaceholders = PlaceholderPattern.Matches(pseudo.Messages[pair.Key]).Select(static m => m.Groups[1].Value).ToArray();
            Assert.Equal(enPlaceholders, pseudoPlaceholders);
        }
    }

    [Fact]
    public void BuildFallbackChain_HandlesParentsAndUnknownTags()
    {
        Assert.Equal(["zh-Hans", "zh", "en-US"], Locales.BuildFallbackChain("zh-Hans").Take(3).ToArray());
        Assert.Contains("zh-Hans", Locales.BuildFallbackChain("zh-Hans-CN"));
        Assert.Equal("en-US", Locales.BuildFallbackChain("fr-FR").Last());
        Assert.Equal("en-US", Locales.BuildFallbackChain(null).Single());
    }

    [Fact]
    public async Task RuntimeSwitch_ChangesTextAndRaisesVersion()
    {
        var result = await AvaloniaTest.Run(() =>
        {
            var localizer = TestFixtures.CreateLocalizer("en-US");
            var changed = 0;
            localizer.Changed += (_, _) => changed++;
            var english = localizer.Get(LocKeys.Rail.Home);
            var version = localizer.Version;
            var switched = localizer.SetCulture("zh-Hans");
            var chinese = localizer.Get(LocKeys.Rail.Home);
            return (english, chinese, version, localizer.Version, changed, switched);
        });

        Assert.Equal("Home", result.english);
        Assert.Equal("主页", result.chinese);
        Assert.True(result.switched);
        Assert.True(result.Version > result.version);
        Assert.Equal(1, result.changed);
        Assert.Equal("zh-Hans", await AvaloniaTest.Run(() => TestFixtures.Localizer.Culture));
        await AvaloniaTest.Run(() => TestFixtures.Localizer.SetCulture("en-US"));
    }

    [Fact]
    public async Task MissingKey_FallsBackToKeyOrBracketsPerConfiguration()
    {
        var result = await AvaloniaTest.Run(() =>
        {
            var strict = LocalizationService.CreateDefault("en-US", showMissingKeys: true, enablePseudoLocale: false);
            var release = LocalizationService.CreateDefault("en-US", showMissingKeys: false, enablePseudoLocale: false);
            var strictValue = strict.Get("No.Such.Key");
            var releaseValue = release.Get("No.Such.Key");
            return (strictValue, releaseValue);
        });

        Assert.Equal("\u27E6No.Such.Key\u27E7", result.strictValue);
        Assert.Equal("No.Such.Key", result.releaseValue);
    }

    [Theory]
    [InlineData("en-US", 1d, "one")]
    [InlineData("en-US", 2d, "other")]
    [InlineData("zh-Hans", 1d, "other")]
    [InlineData("zh-Hans", 5d, "other")]
    [InlineData("ru-RU", 1d, "one")]
    [InlineData("ru-RU", 3d, "few")]
    [InlineData("ru-RU", 11d, "many")]
    [InlineData("ru-RU", 1.5d, "other")]
    [InlineData("ar-SA", 0d, "zero")]
    [InlineData("ar-SA", 2d, "two")]
    [InlineData("ar-SA", 5d, "few")]
    [InlineData("ar-SA", 20d, "many")]
    public void PluralRules_MatchCldrCategories(string culture, double value, string expected)
        => Assert.Equal(expected, PluralRules.Category(CultureInfo.GetCultureInfo(culture), value));

    [Fact]
    public void MessageFormatter_HandlesNamedArgsPluralsNumbersAndEscapes()
    {
        var en = CultureInfo.GetCultureInfo("en-US");
        var args = new Dictionary<string, object?> { ["count"] = 3, ["name"] = "FluidBar", ["value"] = 62 };
        Assert.Equal("3 tracks", MessageFormatter.Format("{count, plural, one {# track} other {# tracks}}", args, en));
        Assert.Equal("Hello FluidBar", MessageFormatter.Format("Hello {name}", args, en));
        Assert.Equal("62%", MessageFormatter.Format("{value, number}%", args, en));
        Assert.Equal("1 track", MessageFormatter.Format("{count, plural, one {# track} other {# tracks}}",
            new Dictionary<string, object?> { ["count"] = 1 }, en));
        Assert.Equal("zero", MessageFormatter.Format("{count, plural, =0 {zero} other {other}}",
            new Dictionary<string, object?> { ["count"] = 0 }, en));
        Assert.Equal("{literal}", MessageFormatter.Format("'{literal}'", args, en));
        Assert.Equal("It's fine", MessageFormatter.Format("It''s fine", args, en));

        var zh = CultureInfo.GetCultureInfo("zh-Hans");
        Assert.Equal("3 首曲目", MessageFormatter.Format("{count, plural, other {# 首曲目}}",
            new Dictionary<string, object?> { ["count"] = 3 }, zh));
    }

    [Fact]
    public void CatalogParser_RejectsInvalidDocuments()
    {
        Assert.Throws<FormatException>(() => JsonCatalogParser.Parse("{}"));
        Assert.Throws<FormatException>(() => JsonCatalogParser.Parse("{\"locale\":\"en-US\",\"messages\":{\"Bad Key\":\"x\"}}"));
        Assert.Throws<FormatException>(() => JsonCatalogParser.Parse("{\"locale\":\"en-US\",\"messages\":{\"A.B\":1}}"));
        Assert.Throws<FormatException>(() => JsonCatalogParser.Parse("{ broken", "en-US"));
    }

    private static void CollectConstants(Type type, List<string> output)
    {
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy))
        {
            if (field.IsLiteral && field.FieldType == typeof(string) && field.GetRawConstantValue() is string value)
            {
                output.Add(value);
            }
        }

        foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.Static))
        {
            CollectConstants(nested, output);
        }
    }

    private static string RepoPath(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FluidBar.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException("Could not locate repository root.");
        }

        return Path.Combine(directory.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
    }
}

/// <summary>Visual token / geometry contract (spec §3.1, §3.2, §4).</summary>
public sealed class TokenAndGeometryTests
{
    [Fact]
    public void CompactSizes_MatchTheSpecTokensExactly()
    {
        Assert.Equal(new VisualSize(132, 36), LayoutTokens.CompactSize(null));
        Assert.Equal(new VisualSize(226, 40), LayoutTokens.CompactSize(FluidBar.Core.Activities.ActivityKind.Media));
        Assert.Equal(new VisualSize(208, 40), LayoutTokens.CompactSize(FluidBar.Core.Activities.ActivityKind.Volume));
        Assert.Equal(new VisualSize(208, 40), LayoutTokens.CompactSize(FluidBar.Core.Activities.ActivityKind.Brightness));
        Assert.Equal(new VisualSize(360, 64), LayoutTokens.CompactSize(FluidBar.Core.Activities.ActivityKind.Notification));
        Assert.Equal(new VisualSize(264, 36), LayoutTokens.CompactSize(FluidBar.Core.Activities.ActivityKind.Clipboard));
        Assert.Equal(new VisualSize(236, 36), LayoutTokens.CompactSize(FluidBar.Core.Activities.ActivityKind.Agent));
        Assert.Equal(new VisualSize(190, 36), LayoutTokens.CompactSize(FluidBar.Core.Activities.ActivityKind.Timer));
        Assert.Equal(new VisualSize(208, 36), LayoutTokens.CompactSize(FluidBar.Core.Activities.ActivityKind.BatteryLow));
        Assert.Equal(new VisualSize(236, 36), LayoutTokens.CompactSize(FluidBar.Core.Activities.ActivityKind.Progress));
        Assert.Equal(new VisualSize(180, 36), LayoutTokens.CompactSize(FluidBar.Core.Activities.ActivityKind.LockKeys));
        Assert.Equal(new VisualSize(240, 44), LayoutTokens.CompactSize(FluidBar.Core.Activities.ActivityKind.Device));
        Assert.Equal(new VisualSize(280, 44), LayoutTokens.CompactSize(FluidBar.Core.Activities.ActivityKind.Bluetooth));
        Assert.Equal(new VisualSize(220, 36), LayoutTokens.CompactSize(FluidBar.Core.Activities.ActivityKind.Focus));
        Assert.Equal(new VisualSize(480, 44), LayoutTokens.CompactSize(FluidBar.Core.Activities.ActivityKind.Lookup));
    }

    [Fact]
    public void ExpandedShellIsUniform472x284_AndWindowIs520x332()
    {
        Assert.Equal(472, LayoutTokens.ExpandedWidth);
        Assert.Equal(284, LayoutTokens.ExpandedHeight);
        Assert.Equal(new VisualSize(472, 284), LayoutTokens.ExpandedSize());
        Assert.Equal(520, LayoutTokens.WindowWidth);
        Assert.Equal(332, LayoutTokens.WindowHeight);
        // New frozen navigation design: expanded shell 440×284 with a 20 DIP inset and a 400×244
        // pager viewport (the rail-era 48+392 split is superseded).
        Assert.Equal(400, FluidBar.App.Island.Expanded.ExpandedPanel.ViewportWidth);
        Assert.Equal(252, FluidBar.App.Island.Expanded.ExpandedPanel.ViewportHeight);
        Assert.Equal(60, FluidBar.App.Island.Expanded.ExpandedPanel.ViewportInset);
        Assert.Equal(16, FluidBar.App.Island.Expanded.ExpandedPanel.ViewportInsetY);
    }

    [Fact]
    public void UiMetrics_MatchTheSpecAndCoreTokens()
    {
        Assert.Equal(LayoutTokens.WindowWidth, UiMetrics.WindowWidth);
        Assert.Equal(LayoutTokens.HostSurfaceFloatHeight, UiMetrics.WindowHeight);
        Assert.Equal(24, UiMetrics.WindowPadding);
        Assert.Equal(48, UiMetrics.RailWidth);
        Assert.Equal(34, UiMetrics.RailItemSize);
        Assert.Equal(36, UiMetrics.RailItemHitSize);
        Assert.Equal(8, UiMetrics.RailItemGap);
        Assert.Equal(6, UiMetrics.RailItemCount);
        Assert.Equal(18, UiMetrics.RailIconSize);
        Assert.Equal(28, LayoutTokens.ExpandedRadius);
        Assert.Equal(52, UiMetrics.DockStripHeight);
        Assert.Equal(40, UiMetrics.HudHeight);
    }

    [Fact]
    public void SquircleExponent_MatchesTheSpecAtTheExpandedShell()
    {
        var (_, _, _, _, power) = SquircleGeometryFactory.Resolve(440, 284, CornerRadii.Uniform(28));
        var exponent = 2.0 / power;
        // The renderer must use exactly the same superellipse exponent as Core hit-testing (spec 3.2),
        // and the shell must read as a squircle, not a stadium (k < 2.5) or a rounded box (k > 3.3).
        var core = ShellGeometry.Create(new VisualRect(0, 0, 440, 284), CornerRadii.Uniform(28));
        Assert.Equal(core.ExponentK, exponent, 3);
        Assert.InRange(exponent, 2.7, 3.3);
    }

    [Fact]
    public async Task SquircleGeometry_CacheReturnsSameInstanceAndIsBounded()
    {
        await AvaloniaTest.Run(() =>
        {
            SquircleGeometryFactory.ClearCache();
            var first = SquircleGeometryFactory.GetOrCreate(440, 284, CornerRadii.Uniform(28));
            var second = SquircleGeometryFactory.GetOrCreate(440, 284, CornerRadii.Uniform(28));
            Assert.Same(first, second);
            for (var i = 0; i < SquircleGeometryFactory.MaxCacheEntries + 10; i++)
            {
                SquircleGeometryFactory.GetOrCreate(200 + i, 80 + i, CornerRadii.Uniform(12));
            }

            Assert.True(SquircleGeometryFactory.CacheCount <= SquircleGeometryFactory.MaxCacheEntries);
        });
    }

    [Fact]
    public void ContentOpacityPolicy_KeepsTextOutOfTinyShells()
    {
        Assert.Equal(1, FluidBar.Core.Motion.ContentOpacityPolicy.ForCompactContent(36, 284));
        Assert.Equal(0, FluidBar.Core.Motion.ContentOpacityPolicy.ForCompactContent(284, 284));
        Assert.Equal(0, FluidBar.Core.Motion.ContentOpacityPolicy.ForExpandedContent(36, 284));
        Assert.Equal(1, FluidBar.Core.Motion.ContentOpacityPolicy.ForExpandedContent(284, 284));
        Assert.True(FluidBar.Core.Motion.ContentOpacityPolicy.ForExpandedContent(240, 284) is > 0 and < 1);
    }
}

/// <summary>Settings model, persistence adapter and marketplace service behavior.</summary>
public sealed class SettingsAndMarketTests
{
    [Fact]
    public void Normalize_ClampsOutOfRangeValues()
    {
        var normalized = (AppSettings.Default with
        {
            FillOpacity = 0.1,
            FontScale = 99,
            MotionStrength = 0,
            AutoCollapseMs = -1,
            AutoHideMs = -50,
            Palette = "bogus",
            Mode = "nope",
            Anchor = "middle",
            Plugins = new PluginSettings { CheckIntervalHours = 0 },
        }).Normalize();

        Assert.Equal(0.85, normalized.FillOpacity);
        Assert.Equal(1.6, normalized.FontScale);
        Assert.Equal(0.5, normalized.MotionStrength);
        Assert.Equal(0, normalized.AutoCollapseMs);
        Assert.Equal(0, normalized.AutoHideMs);
        Assert.Equal("obsidian", normalized.Palette);
        Assert.Equal("float", normalized.Mode);
        Assert.Equal("top-center", normalized.Anchor);
        Assert.Equal(1, normalized.Plugins.CheckIntervalHours);
    }

    [Fact]
    public void JsonRoundTrip_PreservesSettings()
    {
        var settings = AppSettings.Default with
        {
            Language = "zh-Hans",
            Theme = AppTheme.Light,
            Material = IslandMaterial.Acrylic,
            Palette = "nord",
            FillOpacity = 0.92,
            FontScale = 1.15,
            MotionStrength = 1.2,
            Mode = "flush",
            Anchor = "bottom-right",
            Stacking = StackingStrategySetting.Latest,
            HoverPeek = false,
            AutoCollapseMs = 1500,
            AutoHideMs = 45000,
            DisplayOn = DisplayTarget.All,
            StartWithWindows = false,
            TrayIcon = false,
            AutoUpdate = "auto",
        };

        var json = settings.ToJson();
        var parsed = AppSettings.TryParse(json, out var error);

        Assert.Null(error);
        Assert.NotNull(parsed);
        Assert.Equal(settings.Normalize().ToJson(), parsed!.ToJson());
        Assert.Contains("\"schemaVersion\":2", json);
        Assert.Contains("\"fillOpacity\"", json);
    }

    [Fact]
    public void InvalidJson_FallsBackToListedDefaults()
    {
        Assert.Null(AppSettings.TryParse("not json", out var error));
        Assert.NotNull(error);
        Assert.Equal(AppSettings.Default.Normalize(), AppSettings.TryParse("{}", out var emptyError));
        Assert.Null(emptyError);
    }

    [Fact]
    public void SettingsController_RaisesChangedAndPersists()
    {
        var (controller, store) = TestFixtures.CreateSettings();
        var changes = 0;
        controller.Changed += (_, _) => changes++;

        var updated = controller.Update(s => s with { Language = "zh-Hans" });

        Assert.Equal("zh-Hans", updated.Language);
        Assert.Equal(1, changes);
        Assert.Equal(1, store.SaveCount);
        Assert.Equal("zh-Hans", store.LastSaved.Language);

        // No-op updates do not raise or persist again.
        controller.Update(s => s with { Language = "zh-Hans" });
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task DesignMarketplace_BrowseSearchInstallRollbackUninstall()
    {
        var marketplace = new DesignMarketplaceUiService();
        var all = await marketplace.BrowseAsync(new MarketplaceQuery(null, "all", "stable"), CancellationToken.None);
        Assert.Equal(5, all.Count);
        Assert.Contains(all, p => p.Featured);

        var filtered = await marketplace.BrowseAsync(new MarketplaceQuery(null, "media", "stable"), CancellationToken.None);
        Assert.Single(filtered);

        var searched = await marketplace.BrowseAsync(new MarketplaceQuery("lyrics", "all", "stable"), CancellationToken.None);
        Assert.Single(searched);

        var progress = new List<InstallProgress>();
        await marketplace.InstallAsync("focus-timer", new Progress<InstallProgress>(progress.Add), CancellationToken.None);
        Assert.Equal(["download", "verify", "extract", "activate"], progress.Select(static p => p.StageToken).ToArray());
        Assert.Equal(100, progress[^1].Percent);

        var installed = await marketplace.GetPluginAsync("focus-timer", CancellationToken.None);
        Assert.Equal(MarketInstallState.Installed, installed!.State);
        Assert.Equal("1.4.2", installed.InstalledVersion);

        var rollback = new List<InstallProgress>();
        await marketplace.RollbackAsync("focus-timer", "1.3.0", new Progress<InstallProgress>(rollback.Add), CancellationToken.None);
        Assert.Equal("1.3.0", (await marketplace.GetPluginAsync("focus-timer", CancellationToken.None))!.InstalledVersion);

        await marketplace.UninstallAsync("focus-timer", keepSettings: true, CancellationToken.None);
        var uninstalled = await marketplace.GetPluginAsync("focus-timer", CancellationToken.None);
        Assert.Equal(MarketInstallState.NotInstalled, uninstalled!.State);
        Assert.Null(uninstalled.InstalledVersion);
    }

    [Fact]
    public async Task DesignPlugins_ListAndToggleThroughSettings()
    {
        var (controller, _) = TestFixtures.CreateSettings();
        var plugins = new DesignPluginUiService(controller);
        var list = await plugins.ListAsync(CancellationToken.None);
        Assert.Contains(list, p => p.Builtin);
        Assert.Contains(list, p => p.Id == "code-pulse");

        await plugins.SetEnabledAsync("code-pulse", false, CancellationToken.None);
        Assert.DoesNotContain("code-pulse", controller.Current.Plugins.Enabled);
        await plugins.SetEnabledAsync("code-pulse", true, CancellationToken.None);
        Assert.Contains("code-pulse", controller.Current.Plugins.Enabled);
    }
}
