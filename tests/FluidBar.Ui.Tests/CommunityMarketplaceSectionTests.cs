using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using FluidBar.App.Localization;
using FluidBar.App.Settings;
using FluidBar.App.Views.Market;
using FluidBar.App.Views.Settings;
using FluidBar.Marketplace.Community;
using FluidBar.Marketplace.Packaging;
using FluidBar.Sdk.Json;

namespace FluidBar.Ui.Tests;

/// <summary>
/// Headless smoke test for the device-local community marketplace block inside the settings Marketplace
/// section: submit entry, pending review actions, star rating widget and the persisted install counter.
/// </summary>
public sealed class CommunityMarketplaceSectionTests
{
    private const string PluginId = "community-ui-sample";

    [Fact]
    public async Task MarketplaceSection_RendersSubmitReviewRatingAndInstallCounters()
    {
        var root = CreateRoot();
        try
        {
            var service = await SeedAsync(root);
            var snapshot = await RenderAsync(service, Locales.Source);

            Assert.Equal(service.Store.RootDirectory, snapshot.RootDirectory);
            Assert.Contains("Local community marketplace", snapshot.Labels);
            Assert.Contains("Pending review (1)", snapshot.Labels);
            Assert.Contains("Community UI Sample", snapshot.Labels);
            Assert.Contains(snapshot.Labels, static text => text.Contains(PluginId, StringComparison.Ordinal));
            Assert.Contains("4.0 / 5 (1)", snapshot.Labels);
            Assert.Contains("1 install", snapshot.Labels);
            Assert.Equal(5, snapshot.Labels.Count(static text => text is "★" or "☆"));
            Assert.Equal(4, snapshot.Labels.Count(static text => text == "★"));
            Assert.Contains("Submit .fbp…", snapshot.Buttons);
            Assert.Contains("Approve", snapshot.Buttons);
            Assert.Contains("Reject", snapshot.Buttons);
            Assert.Contains("Install", snapshot.Buttons);
            Assert.Contains("Delist", snapshot.Buttons);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task MarketplaceSection_IsLocalizedInChinese()
    {
        var root = CreateRoot();
        try
        {
            var service = await SeedAsync(root);
            var snapshot = await RenderAsync(service, Locales.ChineseSimplified);

            Assert.Contains("本地社区市场", snapshot.Labels);
            Assert.Contains("待审核（1）", snapshot.Labels);
            Assert.Contains("未签名", snapshot.Labels);
            Assert.Contains("4.0 / 5（1 条评分）", snapshot.Labels);
            Assert.Contains("1 次安装", snapshot.Labels);
            Assert.Contains("提交 .fbp…", snapshot.Buttons);
            Assert.Contains("通过", snapshot.Buttons);
            Assert.Contains("驳回", snapshot.Buttons);
            Assert.Contains("安装", snapshot.Buttons);
            Assert.Contains("下架", snapshot.Buttons);
            Assert.DoesNotContain("Pending review", snapshot.Labels);
            Assert.DoesNotContain("Approve", snapshot.Buttons);
        }
        finally
        {
            Cleanup(root);
        }
    }

    private static string CreateRoot() =>
        Path.Combine(Path.GetTempPath(), "fluidbar-ui-community-" + Guid.NewGuid().ToString("N"));

    private static async Task<CommunityMarketplaceService> SeedAsync(string root)
    {
        var store = new CommunityMarketplaceStore(Path.Combine(root, "marketplace"));
        var service = new CommunityMarketplaceService(new CommunityMarketplaceOptions { Store = store });

        var submitted = await service.SubmitAsync(new CommunitySubmitRequest { PackagePath = CreatePackage(root, "1.0.0") });
        Assert.True(submitted.Success, string.Join("; ", submitted.Errors));
        Assert.True((await service.ApproveAsync(PluginId, "1.0.0", "approved in a UI smoke test")).Success);
        await service.RecordDownloadAsync(PluginId);
        await service.RateAsync(PluginId, 4);

        var pending = await service.SubmitAsync(new CommunitySubmitRequest { PackagePath = CreatePackage(root, "2.0.0") });
        Assert.True(pending.Success, string.Join("; ", pending.Errors));
        return service;
    }

    private static Task<(List<string> Labels, List<string> Buttons, string RootDirectory)> RenderAsync(
        CommunityMarketplaceService service,
        string culture) =>
        AvaloniaTest.RunAsync(async () =>
        {
            ILocalizer localizer = culture == Locales.Source
                ? TestFixtures.CreateLocalizer(culture)
                : LocalizationService.CreateDefault(culture, showMissingKeys: false, enablePseudoLocale: false);
            var view = new MarketplaceSectionView(
                new DesignMarketplaceUiService(),
                localizer,
                AppSettings.Default.Surfaces,
                static () => null,
                "stable",
                service);
            await view.ReloadAsync();
            var labels = view.GetLogicalDescendants().OfType<TextBlock>()
                .Select(static text => text.Text ?? string.Empty)
                .ToList();
            var buttons = view.GetLogicalDescendants().OfType<Button>()
                .Select(static button => button.Content as string ?? string.Empty)
                .ToList();
            return (labels, buttons, view.Community.Store.RootDirectory);
        });

    private static void Cleanup(string root)
    {
        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort temp cleanup.
        }
    }

    private static string CreatePackage(string directory, string version)
    {
        var stagingDirectory = Path.Combine(directory, "staging-" + version.Replace('.', '_'));
        Directory.CreateDirectory(stagingDirectory);
        using var staging = FbpStagingBuilder.Create(stagingDirectory);
        var manifest = new JsonObject
        {
            ["$schema"] = "https://fluidbar.app/schemas/plugin.schema.json",
            ["schemaVersion"] = 2,
            ["id"] = "community-ui-sample",
            ["version"] = version,
            ["apiVersion"] = "2.0.0",
            ["minAppVersion"] = "2.0.0",
            ["publisher"] = new JsonObject
            {
                ["id"] = "ui-tests",
                ["displayName"] = "UI tests",
                ["url"] = "https://example.com",
                ["keyId"] = "ui-test-publisher",
            },
            ["name"] = new JsonObject { ["en-US"] = "Community UI Sample" },
            ["summary"] = new JsonObject { ["en-US"] = "Fixture plugin for the community marketplace UI test." },
            ["license"] = "MIT",
            ["categories"] = new JsonArray("developer"),
            ["entry"] = new JsonObject
            {
                ["runtime"] = "inproc",
                ["assembly"] = "plugin/Fake.Plugin.dll",
                ["type"] = "Fake.Plugin.Plugin",
                ["targetFramework"] = "net10.0",
            },
            ["platforms"] = new JsonArray("win-x64"),
            ["locales"] = new JsonObject
            {
                ["default"] = "en-US",
                ["available"] = new JsonArray("en-US"),
                ["path"] = "locales",
                ["fallback"] = "en-US",
            },
            ["capabilities"] = new JsonObject
            {
                ["ui"] = new JsonObject { ["island"] = true, ["xaml"] = false },
            },
            ["activation"] = new JsonArray("startup"),
            ["contributes"] = new JsonObject(),
        };
        staging.AddManifestJson(manifest.ToJsonString(PluginJson.CreateStrictOptions()));
        var entryAssembly = Path.Combine(stagingDirectory, "Fake.Plugin.dll");
        File.WriteAllText(entryAssembly, "not a real assembly: the UI test never loads it");
        staging.AddFile("plugin/Fake.Plugin.dll", entryAssembly);
        var locale = Path.Combine(stagingDirectory, "en-US.json");
        File.WriteAllText(locale, new JsonObject
        {
            ["$schema"] = "https://fluidbar.app/schemas/locale.schema.json",
            ["locale"] = "en-US",
            ["version"] = 1,
            ["messages"] = new JsonObject { ["fixture.body"] = "body" },
        }.ToJsonString(PluginJson.CreateStrictOptions()));
        staging.AddFile("locales/en-US.json", locale);
        var license = Path.Combine(stagingDirectory, "LICENSE");
        File.WriteAllText(license, "MIT");
        staging.AddFile("LICENSE", license);
        var readme = Path.Combine(stagingDirectory, "README.md");
        File.WriteAllText(readme, "# Community UI Sample");
        staging.AddFile("README.md", readme);
        var packagePath = Path.Combine(directory, $"community-ui-{version}.fbp");
        staging.Build(packagePath);
        return packagePath;
    }
}
