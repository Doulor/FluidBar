using System.Text;
using FluidBar.PluginHost.Loader;
using FluidBar.Sdk;

namespace FluidBar.PluginHost.Tests;

public sealed class PluginHostLoaderTests
{
    private static string CreateFixture(TempPluginRoot workspace, string pluginId = "test-plugin", string pluginType = "HappyPlugin", System.Text.Json.Nodes.JsonObject? manifest = null)
    {
        if (manifest is null)
        {
            return FixturePackageFactory.Create(workspace.Root, pluginId, pluginType);
        }

        var packageDirectory = FixturePackageFactory.Create(workspace.Root, pluginId, pluginType, customizeManifest: value => CopyManifest(manifest, value));
        return packageDirectory;
    }

    private static void CopyManifest(System.Text.Json.Nodes.JsonObject source, System.Text.Json.Nodes.JsonObject target)
    {
        target.Clear();
        foreach (var pair in source)
        {
            target[pair.Key] = pair.Value?.DeepClone();
        }
    }

    [Fact]
    public async Task Loads_happy_plugin_and_activates()
    {
        using var workspace = TempPluginRoot.Create();
        CreateFixture(workspace);
        var (host, logger, publisher) = HostFactory.Create(workspace.Root);
        await using var hostScope = host;

        var result = await host.LoadAsync("test-plugin");

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.Handle);
        Assert.Equal(PluginLifecycleState.Active, result.Handle!.State);
        Assert.True(publisher.HasTitle("happy-started"), "HappyPlugin should have published its start event.");
        Assert.True(logger.Contains("activated"));
    }

    [Fact]
    public async Task Loading_an_active_plugin_is_idempotent()
    {
        using var workspace = TempPluginRoot.Create();
        CreateFixture(workspace);
        var (host, _, _) = HostFactory.Create(workspace.Root);
        await using var hostScope = host;

        var first = await host.LoadAsync("test-plugin");
        var second = await host.LoadAsync("test-plugin");

        Assert.True(second.Success);
        Assert.Same(first.Handle, second.Handle);
    }

    [Fact]
    public async Task State_transitions_are_deterministic()
    {
        using var workspace = TempPluginRoot.Create();
        CreateFixture(workspace);
        var (host, _, _) = HostFactory.Create(workspace.Root);
        await using var hostScope = host;
        var transitions = new List<(PluginLifecycleState From, PluginLifecycleState To)>();
        host.StateChanged += (_, args) => transitions.Add((args.PreviousState, args.NewState));

        var result = await host.LoadAsync("test-plugin");

        Assert.True(result.Success, result.Message);
        Assert.Equal(PluginLifecycleState.Active, transitions[^1].To);
        Assert.Contains((PluginLifecycleState.Discovered, PluginLifecycleState.Validating), transitions);
        Assert.Contains((PluginLifecycleState.Loading, PluginLifecycleState.Loaded), transitions);
        Assert.Contains((PluginLifecycleState.Starting, PluginLifecycleState.Active), transitions);
        Assert.DoesNotContain(transitions, transition => transition.To == PluginLifecycleState.Failed);
    }

    [Fact]
    public async Task Invalid_manifest_is_isolated()
    {
        using var workspace = TempPluginRoot.Create();
        var packageDirectory = FixturePackageFactory.Create(workspace.Root, "bad-plugin");
        File.WriteAllText(Path.Combine(packageDirectory, "manifest.json"), "{ definitely not json");
        CreateFixture(workspace);

        var (host, _, _) = HostFactory.Create(workspace.Root);
        await using var hostScope = host;

        var bad = await host.LoadAsync("bad-plugin");
        Assert.False(bad.Success);
        Assert.Equal(PluginLoadFailureKind.ManifestInvalid, bad.FailureKind);

        // The host survives and still loads a valid plugin.
        var good = await host.LoadAsync("test-plugin");
        Assert.True(good.Success, good.Message);
    }

    [Fact]
    public async Task Incompatible_api_version_is_reported_before_loading()
    {
        using var workspace = TempPluginRoot.Create();
        var manifest = FixturePackageFactory.CreateManifest("test-plugin", "HappyPlugin");
        manifest["apiVersion"] = "3.0.0";
        CreateFixture(workspace, manifest: manifest);

        var (host, _, _) = HostFactory.Create(workspace.Root);
        await using var hostScope = host;
        var result = await host.LoadAsync("test-plugin");

        Assert.False(result.Success);
        Assert.Equal(PluginLoadFailureKind.Incompatible, result.FailureKind);
        Assert.Equal(PluginLifecycleState.Incompatible, result.Handle!.State);
    }

    [Fact]
    public async Task App_version_gate_is_enforced()
    {
        using var workspace = TempPluginRoot.Create();
        var manifest = FixturePackageFactory.CreateManifest("test-plugin", "HappyPlugin");
        manifest["minAppVersion"] = "9.0.0";
        CreateFixture(workspace, manifest: manifest);

        var (host, _, _) = HostFactory.Create(workspace.Root);
        await using var hostScope = host;
        var result = await host.LoadAsync("test-plugin");

        Assert.False(result.Success);
        Assert.Equal(PluginLoadFailureKind.Incompatible, result.FailureKind);
    }

    [Fact]
    public async Task Missing_entry_assembly_fails_isolated()
    {
        using var workspace = TempPluginRoot.Create();
        var packageDirectory = CreateFixture(workspace);
        File.Delete(Path.Combine(packageDirectory, "plugin", FixturePackageFactory.EntryAssemblyName + ".dll"));

        var (host, logger, _) = HostFactory.Create(workspace.Root);
        await using var hostScope = host;
        var result = await host.LoadAsync("test-plugin");

        Assert.False(result.Success);
        Assert.Equal(PluginLoadFailureKind.EntryMissing, result.FailureKind);
        Assert.True(logger.Contains("Plugin load failed"));
    }

    [Fact]
    public async Task Missing_entry_type_fails_isolated()
    {
        using var workspace = TempPluginRoot.Create();
        var manifest = FixturePackageFactory.CreateManifest("test-plugin", "ThisTypeDoesNotExist");
        CreateFixture(workspace, manifest: manifest);

        var (host, _, _) = HostFactory.Create(workspace.Root);
        await using var hostScope = host;
        var result = await host.LoadAsync("test-plugin");

        Assert.False(result.Success);
        Assert.Equal(PluginLoadFailureKind.EntryTypeMissing, result.FailureKind);
    }

    [Fact]
    public async Task Entry_type_not_implementing_the_contract_fails_isolated()
    {
        using var workspace = TempPluginRoot.Create();
        var manifest = FixturePackageFactory.CreateManifest("test-plugin", "NotAPlugin");
        CreateFixture(workspace, manifest: manifest);

        var (host, _, _) = HostFactory.Create(workspace.Root);
        await using var hostScope = host;
        var result = await host.LoadAsync("test-plugin");

        Assert.False(result.Success);
        Assert.Equal(PluginLoadFailureKind.EntryTypeInvalid, result.FailureKind);
    }

    [Fact]
    public async Task Throwing_constructor_fails_isolated()
    {
        using var workspace = TempPluginRoot.Create();
        CreateFixture(workspace, pluginType: "ThrowingConstructorPlugin");

        var (host, _, _) = HostFactory.Create(workspace.Root);
        await using var hostScope = host;
        var result = await host.LoadAsync("test-plugin");

        Assert.False(result.Success);
        Assert.Equal(PluginLoadFailureKind.ConstructorFailed, result.FailureKind);
    }

    [Fact]
    public async Task Throwing_initialize_fails_isolated()
    {
        using var workspace = TempPluginRoot.Create();
        CreateFixture(workspace, pluginType: "ThrowingInitializePlugin");

        var (host, _, _) = HostFactory.Create(workspace.Root);
        await using var hostScope = host;
        var result = await host.LoadAsync("test-plugin");

        Assert.False(result.Success);
        Assert.Equal(PluginLoadFailureKind.InitializeFailed, result.FailureKind);
    }

    [Fact]
    public async Task Throwing_start_fails_isolated_and_disables_after_repeated_failures()
    {
        using var workspace = TempPluginRoot.Create();
        CreateFixture(workspace, pluginType: "ThrowingStartPlugin");
        var (host, _, _) = HostFactory.Create(workspace.Root, options => options with { MaxConsecutiveFailures = 2 });
        await using var hostScope = host;

        var first = await host.LoadAsync("test-plugin");
        Assert.False(first.Success);
        Assert.Equal(PluginLoadFailureKind.StartFailed, first.FailureKind);

        var second = await host.LoadAsync("test-plugin");
        Assert.False(second.Success);
        Assert.Equal(PluginLifecycleState.Disabled, second.Handle!.State);
    }

    [Fact]
    public async Task Hanging_start_times_out_without_blocking_the_host()
    {
        using var workspace = TempPluginRoot.Create();
        CreateFixture(workspace, pluginType: "HangingStartPlugin");
        var (host, _, _) = HostFactory.Create(
            workspace.Root,
            options => options with { InitializeTimeout = TimeSpan.FromMilliseconds(150), StartTimeout = TimeSpan.FromMilliseconds(150) });
        await using var hostScope = host;

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = await host.LoadAsync("test-plugin");
        stopwatch.Stop();

        Assert.False(result.Success);
        Assert.Equal(PluginLoadFailureKind.Timeout, result.FailureKind);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"Load took {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task Shipped_forbidden_assembly_is_rejected_before_alc_creation()
    {
        using var workspace = TempPluginRoot.Create();
        var packageDirectory = CreateFixture(workspace);
        File.WriteAllText(Path.Combine(packageDirectory, "plugin", "PresentationFramework.dll"), "not a real assembly");

        var (host, logger, _) = HostFactory.Create(workspace.Root);
        await using var hostScope = host;
        var result = await host.LoadAsync("test-plugin");

        Assert.False(result.Success);
        Assert.Equal(PluginLoadFailureKind.ForbiddenReference, result.FailureKind);
        Assert.True(logger.Contains("forbidden"));
    }

    [Fact]
    public async Task Bundled_shared_sdk_is_rejected()
    {
        using var workspace = TempPluginRoot.Create();
        var packageDirectory = CreateFixture(workspace);
        File.WriteAllBytes(Path.Combine(packageDirectory, "plugin", "FluidBar.Sdk.dll"), [0x4d, 0x5a]);

        var (host, _, _) = HostFactory.Create(workspace.Root);
        await using var hostScope = host;
        var result = await host.LoadAsync("test-plugin");

        Assert.False(result.Success);
        Assert.Equal(PluginLoadFailureKind.ForbiddenReference, result.FailureKind);
    }

    [Fact]
    public async Task Forbidden_reference_in_deps_json_is_rejected()
    {
        using var workspace = TempPluginRoot.Create();
        var packageDirectory = CreateFixture(workspace);
        var depsPath = Path.Combine(packageDirectory, "plugin", FixturePackageFactory.EntryAssemblyName + ".deps.json");
        File.WriteAllText(
            depsPath,
            """
            {
              "runtimeTarget": { "name": ".NETCoreApp,Version=v10.0" },
              "targets": {
                ".NETCoreApp,Version=v10.0": {
                  "Avalonia.Base/1.0.0": {}
                }
              },
              "libraries": { "Avalonia.Base/1.0.0": { "type": "package" } }
            }
            """);

        var (host, _, _) = HostFactory.Create(workspace.Root);
        await using var hostScope = host;
        var result = await host.LoadAsync("test-plugin");

        Assert.False(result.Success);
        Assert.Equal(PluginLoadFailureKind.ForbiddenReference, result.FailureKind);
    }

    [Fact]
    public async Task LoadAll_continues_after_individual_failures()
    {
        using var workspace = TempPluginRoot.Create();
        CreateFixture(workspace);
        CreateFixture(workspace, "bad-plugin", "ThrowingStartPlugin");
        var (host, logger, _) = HostFactory.Create(workspace.Root);
        await using var hostScope = host;

        var results = await host.LoadAllAsync();

        Assert.Equal(2, results.Count);
        Assert.Single(results, result => result.Success);
        Assert.Single(results, result => !result.Success);
        Assert.True(logger.Contains("Plugin load failed"));
    }

    [Fact]
    public async Task Stop_then_start_restarts_the_same_instance()
    {
        using var workspace = TempPluginRoot.Create();
        CreateFixture(workspace);
        var (host, _, _) = HostFactory.Create(workspace.Root);
        await using var hostScope = host;
        var load = await host.LoadAsync("test-plugin");
        var handle = load.Handle!;

        Assert.True(await host.StopAsync("test-plugin"));
        Assert.Equal(PluginLifecycleState.Stopped, handle.State);

        var restarted = await host.StartAsync("test-plugin");
        Assert.True(restarted.Success, restarted.Message);
        Assert.Equal(PluginLifecycleState.Active, handle.State);
    }
}
