using FluidBar.App.Plugins;
using FluidBar.Sdk.Versioning;

namespace FluidBar.Ui.Tests;

/// <summary>Safe mode composes the runtime with LoadCommunity=false; refuse to discover or load packages.</summary>
public sealed class SafeModePluginRuntimeTests
{
    [Fact]
    public async Task SafeMode_SkipsCommunityDiscoveryAndLoading()
    {
        var root = Path.Combine(Path.GetTempPath(), "FluidBarTests", "safe-mode", Guid.NewGuid().ToString("N"));
        var local = Path.Combine(root, "local", "demo");
        Directory.CreateDirectory(local);
        await File.WriteAllTextAsync(Path.Combine(local, "manifest.json"), "{}");
        try
        {
            await using var runtime = await PluginRuntime.CreateAsync(new PluginRuntimeOptions
            {
                PluginRootDirectory = root,
                LocalPluginRootDirectory = Path.Combine(root, "local"),
                AppVersion = new SemanticVersion(2, 0, 0),
                Localizer = new FakeLocalizer(),
                LoadBuiltins = false,
                LoadCommunity = false,
            });

            Assert.Empty(runtime.Report.DiscoveredPackages);
            Assert.Empty(runtime.Report.CommunityResults);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
