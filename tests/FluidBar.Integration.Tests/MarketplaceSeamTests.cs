using FluidBar.Integration.Tests.TestSupport;
using FluidBar.Marketplace.Install;
using FluidBar.Marketplace.Packaging;
using FluidBar.Marketplace.Signing;
using FluidBar.PluginHost.Loader;
using PluginHostType = FluidBar.PluginHost.Loader.PluginHost;
using FluidBar.Sdk.Manifest;
using FluidBar.Sdk.Versioning;

namespace FluidBar.Integration.Tests;

/// <summary>
/// Seam 5: offline marketplace package → PluginHost install → real collectible ALC load → published SDK
/// event → unload + collection. Fully local: packages a built sample plugin with the production packer
/// and installs from the local file (no network, no hosted marketplace).
/// </summary>
public class MarketplaceSeamTests
{
    private const string SamplePluginId = "sample-minimal";

    private sealed class NoopActivator : IPluginActivator
    {
        public Task<ActivationResult> ActivateAsync(PluginManifest manifest, string versionDirectory, CancellationToken cancellationToken)
            => Task.FromResult(ActivationResult.Ok());

        public Task DeactivateAsync(string pluginId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Fact]
    public async Task Offline_package_installs_loads_publishes_event_and_unloads_collected()
    {
        using var workspace = TempDirectory.Create();
        var pluginRoot = workspace.Combine("plugins");
        var artifacts = workspace.Combine("artifacts");
        Directory.CreateDirectory(artifacts);

        // 1) Pack the built sample plugin exactly like the contributor CLI/CI would.
        var sampleOutput = IntegrationPaths.SamplePluginOutput("FluidBar.Plugins.SampleMinimal");
        var packagePath = Path.Combine(artifacts, SamplePluginId + ".fbp");
        using (var staging = FbpStagingBuilder.FromPluginOutput(sampleOutput))
        {
            var package = staging.Build(packagePath);
            Assert.True(File.Exists(package.PackagePath));
        }

        // 2) Offline local install into a temp plugin root (unsigned local install is an explicit policy).
        using var installService = new PluginInstallService(new PluginInstallOptions
        {
            PluginRootDirectory = pluginRoot,
            Activator = new NoopActivator(),
            Trust = MarketplaceTrustOptions.Empty,
            RequireMarketplaceSignature = false,
            AllowUnsignedLocalInstall = true,
            AppVersion = new SemanticVersion(2, 0, 0),
        });
        var install = await installService.InstallFromFileAsync(packagePath, signature: null, allowUnsigned: true);
        Assert.True(install.Success, install.Message);
        Assert.Equal(SamplePluginId, install.PluginId);
        Assert.Equal("1.0.0", install.Version);
        Assert.NotNull(installService.GetState(SamplePluginId));
        Assert.Contains("1.0.0", installService.GetInstalledVersions(SamplePluginId));

        // 3) Load through the real host; capture what the plugin publishes to the island.
        var publisher = new RecordingPublisher();
        await using var host = new PluginHostType(new PluginHostOptions
        {
            PluginRootDirectory = pluginRoot,
            AppVersion = new SemanticVersion(2, 0, 0),
            IslandPublisherFactory = _ => publisher,
            InitializeTimeout = TimeSpan.FromSeconds(5),
            StartTimeout = TimeSpan.FromSeconds(5),
            StopTimeout = TimeSpan.FromSeconds(5),
            DisposeTimeout = TimeSpan.FromSeconds(5),
            UnloadCollectionTimeout = TimeSpan.FromSeconds(5),
        });

        var load = await host.LoadAsync(SamplePluginId);
        Assert.True(load.Success, load.Message);
        Assert.Equal("1.0.0", load.Handle!.Manifest.Version);
        var published = publisher.Events;
        Assert.NotEmpty(published);
        Assert.Contains(published, islandEvent => string.Equals(islandEvent.Source, SamplePluginId, StringComparison.Ordinal));
        Assert.Contains(published, islandEvent => !string.IsNullOrWhiteSpace(islandEvent.Title));

        // 4) Unload must release the collectible ALC (WeakReference/GC evidence in PluginHost).
        var unload = await host.UnloadAsync(SamplePluginId);
        Assert.True(unload.Success, unload.FailureReason);
        Assert.True(unload.Collected, unload.FailureReason);
        Assert.Equal(PluginLifecycleState.Unloaded, host.Find(SamplePluginId)?.State);
    }

    [Fact]
    public async Task Installed_sample_plugin_can_be_loaded_twice_after_unload()
    {
        using var workspace = TempDirectory.Create();
        var pluginRoot = workspace.Combine("plugins");
        var artifacts = workspace.Combine("artifacts");
        Directory.CreateDirectory(artifacts);
        var packagePath = Path.Combine(artifacts, SamplePluginId + ".fbp");
        using (var staging = FbpStagingBuilder.FromPluginOutput(IntegrationPaths.SamplePluginOutput("FluidBar.Plugins.SampleMinimal")))
        {
            staging.Build(packagePath);
        }

        using var installService = new PluginInstallService(new PluginInstallOptions
        {
            PluginRootDirectory = pluginRoot,
            Activator = new NoopActivator(),
            RequireMarketplaceSignature = false,
            AllowUnsignedLocalInstall = true,
            AppVersion = new SemanticVersion(2, 0, 0),
        });
        Assert.True((await installService.InstallFromFileAsync(packagePath, allowUnsigned: true)).Success);

        var firstPublisher = new RecordingPublisher();
        await using (var firstHost = CreateHost(pluginRoot, firstPublisher))
        {
            Assert.True((await firstHost.LoadAsync(SamplePluginId)).Success);
            Assert.True((await firstHost.UnloadAsync(SamplePluginId)).Collected);
        }

        var secondPublisher = new RecordingPublisher();
        await using var secondHost = CreateHost(pluginRoot, secondPublisher);
        var secondLoad = await secondHost.LoadAsync(SamplePluginId);
        Assert.True(secondLoad.Success, secondLoad.Message);
        Assert.NotEmpty(secondPublisher.Events);
        Assert.True((await secondHost.UnloadAsync(SamplePluginId)).Collected);
    }

    private static PluginHostType CreateHost(string pluginRoot, RecordingPublisher publisher)
        => new(new PluginHostOptions
        {
            PluginRootDirectory = pluginRoot,
            AppVersion = new SemanticVersion(2, 0, 0),
            IslandPublisherFactory = _ => publisher,
            InitializeTimeout = TimeSpan.FromSeconds(5),
            StartTimeout = TimeSpan.FromSeconds(5),
            StopTimeout = TimeSpan.FromSeconds(5),
            DisposeTimeout = TimeSpan.FromSeconds(5),
            UnloadCollectionTimeout = TimeSpan.FromSeconds(5),
        });
}
