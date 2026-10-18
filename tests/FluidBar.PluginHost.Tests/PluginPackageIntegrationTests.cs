using FluidBar.Marketplace.Install;
using FluidBar.Marketplace.Packaging;
using FluidBar.Marketplace.Signing;
using FluidBar.PluginHost.Loader;

namespace FluidBar.PluginHost.Tests;

/// <summary>End-to-end: real sample plugin -> .fbp -> signature -> extract -> host load -> unload collected.</summary>
public sealed class PluginPackageIntegrationTests
{
    [Fact]
    public async Task Sample_minimal_package_verifies_loads_and_unloads()
    {
        using var workspace = TempPluginRoot.Create();
        using var keys = new HostTestKeys();
        var (packagePath, signature) = SamplePackageBuilder.BuildSigned(
            "FluidBar.Plugins.SampleMinimal",
            workspace.Combine("packages"),
            keys);

        var verifier = new PackageSignatureVerifier(keys.Trust);
        var verification = await verifier.VerifyFileAsync(packagePath, signature);
        Assert.True(verification.Verified, string.Join("; ", verification.Errors));

        var extractDirectory = workspace.Combine("extracted", "sample-minimal", "1.0.0");
        var extraction = await FbpPackageReader.ExtractFileAsync(packagePath, extractDirectory);
        Assert.Equal("sample-minimal", extraction.Manifest.Id);

        var (host, _, publisher) = HostFactory.Create(workspace.Combine("host-root"));
        await using var hostScope = host;
        var load = await host.LoadFromDirectoryAsync(extractDirectory);

        Assert.True(load.Success, load.Message);
        Assert.Equal(PluginLifecycleState.Active, load.Handle!.State);
        var islandEvent = Assert.Single(publisher.Events);
        Assert.Equal("Sample Minimal", islandEvent.Title);

        var unload = await host.UnloadAsync("sample-minimal");
        Assert.True(unload.Collected, unload.FailureReason);
    }

    [Fact]
    public async Task Sample_rich_package_ships_private_dependency_and_runs_commands()
    {
        using var workspace = TempPluginRoot.Create();
        using var keys = new HostTestKeys();
        var (packagePath, signature) = SamplePackageBuilder.BuildSigned(
            "FluidBar.Plugins.SampleRich",
            workspace.Combine("packages"),
            keys);

        var inspection = FbpPackageReader.Inspect(File.ReadAllBytes(packagePath));
        Assert.Contains("plugin/FluidBar.Plugins.SampleRich.Core.dll", inspection.EntryPaths);

        var verifier = new PackageSignatureVerifier(keys.Trust);
        Assert.True((await verifier.VerifyFileAsync(packagePath, signature)).Verified);

        var extractDirectory = workspace.Combine("extracted", "sample-rich", "1.0.0");
        await FbpPackageReader.ExtractFileAsync(packagePath, extractDirectory);

        var (host, _, publisher) = HostFactory.Create(workspace.Combine("host-root"));
        await using var hostScope = host;
        var load = await host.LoadFromDirectoryAsync(extractDirectory);
        Assert.True(load.Success, load.Message);

        // The private helper library resolved from the plugin package, inside the plugin ALC.
        // (Do not retain the Assembly object: it would pin the collectible ALC during the unload check.)
        Assert.Equal(1, CountLoadedAssemblies("FluidBar.Plugins.SampleRich.Core"));

        var command = await host.ExecuteCommandAsync("sample-rich", "sample-rich.refresh");
        Assert.True(command.Executed);
        Assert.True(command.Succeeded, command.Error);
        Assert.True(publisher.Events.Count >= 2);

        var unload = await host.UnloadAsync("sample-rich");
        Assert.True(unload.Collected, unload.FailureReason);
        Assert.Equal(0, CountLoadedAssemblies("FluidBar.Plugins.SampleRich.Core"));
    }

    private static int CountLoadedAssemblies(string simpleName) =>
        AppDomain.CurrentDomain.GetAssemblies().Count(assembly => assembly.GetName().Name == simpleName);

    [Fact]
    public async Task Install_service_versioned_layout_is_found_and_loaded_by_the_host()
    {
        using var workspace = TempPluginRoot.Create();
        using var keys = new HostTestKeys();
        var pluginRoot = workspace.Combine("plugins");
        var (packagePath, signature) = SamplePackageBuilder.BuildSigned(
            "FluidBar.Plugins.SampleMinimal",
            workspace.Combine("packages"),
            keys);

        var activator = new RecordingActivator();
        using var installService = new PluginInstallService(new PluginInstallOptions
        {
            PluginRootDirectory = pluginRoot,
            Activator = activator,
            Trust = keys.Trust,
            AppVersion = new FluidBar.Sdk.Versioning.SemanticVersion(2, 0, 0),
            RequireMarketplaceSignature = true,
        });

        var install = await installService.InstallFromFileAsync(packagePath, signature, allowUnsigned: false);
        Assert.True(install.Success, install.Message);
        Assert.Equal("sample-minimal@1.0.0", Assert.Single(activator.Activated));
        Assert.True(File.Exists(Path.Combine(pluginRoot, "sample-minimal", "active.json")));

        var (host, _, _) = HostFactory.Create(pluginRoot);
        await using var hostScope = host;
        var discoveries = await host.DiscoverAsync();
        var discovery = Assert.Single(discoveries);
        Assert.True(discovery.IsValid, string.Join("; ", discovery.Errors));
        Assert.EndsWith(Path.Combine("sample-minimal", "1.0.0"), discovery.PackageDirectory, StringComparison.OrdinalIgnoreCase);

        var load = await host.LoadAsync("sample-minimal");
        Assert.True(load.Success, load.Message);
        Assert.Equal("1.0.0", load.Handle!.Manifest.Version);

        var unload = await host.UnloadAsync("sample-minimal");
        Assert.True(unload.Collected, unload.FailureReason);
    }
}
