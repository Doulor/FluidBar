using FluidBar.PluginHost.Loader;
using FluidBar.Sdk;
using FluidBar.Sdk.Manifest;

namespace FluidBar.PluginHost.Tests;

public sealed class PluginHostUnloadTests
{
    [Fact]
    public async Task Unload_proves_collection_with_weak_references()
    {
        using var workspace = TempPluginRoot.Create();
        FixturePackageFactory.Create(workspace.Root);
        var (host, _, _) = HostFactory.Create(workspace.Root);
        await using var hostScope = host;

        var load = await host.LoadAsync("test-plugin");
        Assert.True(load.Success, load.Message);
        var handle = load.Handle!;
        var instanceReference = handle.PluginInstanceReference!;
        var loadContextReference = handle.LoadContextReference!;

        var unload = await host.UnloadAsync("test-plugin");

        Assert.True(unload.Success);
        Assert.True(unload.Collected, unload.FailureReason);
        Assert.False(unload.RestartRequired);
        Assert.True(unload.CollectionAttempts > 0);
        Assert.False(instanceReference.IsAlive, "The plugin instance must be collected after unload.");
        Assert.False(loadContextReference.IsAlive, "The collectible ALC must be collected after unload.");
        Assert.Equal(PluginLifecycleState.Unloaded, handle.State);
    }

    [Fact]
    public async Task Repeated_load_unload_cycles_collect_every_time()
    {
        using var workspace = TempPluginRoot.Create();
        FixturePackageFactory.Create(workspace.Root);
        var (host, _, _) = HostFactory.Create(workspace.Root);
        await using var hostScope = host;

        for (var cycle = 1; cycle <= 5; cycle++)
        {
            var load = await host.LoadAsync("test-plugin");
            Assert.True(load.Success, $"cycle {cycle}: {load.Message}");
            var contextReference = load.Handle!.LoadContextReference!;
            var unload = await host.UnloadAsync("test-plugin");
            Assert.True(unload.Collected, $"cycle {cycle}: {unload.FailureReason}");
            Assert.False(contextReference.IsAlive, $"cycle {cycle}: ALC leaked");
        }

        Assert.Empty(host.Plugins.Where(plugin => plugin.State == PluginLifecycleState.Active));
    }

    [Fact]
    public async Task External_gc_root_leak_is_detected_and_requires_restart()
    {
        using var workspace = TempPluginRoot.Create();
        FixturePackageFactory.Create(workspace.Root, "test-leaky", "LeakyGcHandlePlugin");
        var (host, logger, _) = HostFactory.Create(workspace.Root);
        await using var hostScope = host;

        var load = await host.LoadAsync("test-leaky");
        Assert.True(load.Success, load.Message);

        var unload = await host.UnloadAsync("test-leaky");

        Assert.False(unload.Collected, "A GCHandle root must keep the ALC alive.");
        Assert.True(unload.RestartRequired);
        Assert.True(logger.Contains("could not prove collection"));

        var reload = await host.ReloadAsync("test-leaky");
        Assert.False(reload.Success);
        Assert.Equal(PluginLoadFailureKind.Unloaded, reload.FailureKind);
    }

    [Fact]
    public async Task Reload_creates_a_new_instance_and_collects_the_old_one()
    {
        using var workspace = TempPluginRoot.Create();
        FixturePackageFactory.Create(workspace.Root);
        var (host, _, publisher) = HostFactory.Create(workspace.Root);
        await using var hostScope = host;

        var first = await host.LoadAsync("test-plugin");
        Assert.True(first.Success, first.Message);
        var firstHandle = first.Handle!;
        var oldInstance = firstHandle.PluginInstanceReference!;
        var oldContext = firstHandle.LoadContextReference!;

        var reload = await host.ReloadAsync("test-plugin");

        Assert.True(reload.Success, reload.Message);
        Assert.Equal(PluginLifecycleState.Active, reload.Handle!.State);
        Assert.False(oldInstance.IsAlive);
        Assert.False(oldContext.IsAlive);
        Assert.Equal(2, publisher.Events.Count(static islandEvent => islandEvent.Title == "happy-started"));
    }

    [Fact]
    public async Task Unload_is_idempotent()
    {
        using var workspace = TempPluginRoot.Create();
        FixturePackageFactory.Create(workspace.Root);
        var (host, _, _) = HostFactory.Create(workspace.Root);
        await using var hostScope = host;
        await host.LoadAsync("test-plugin");

        var first = await host.UnloadAsync("test-plugin");
        var second = await host.UnloadAsync("test-plugin");

        Assert.True(first.Collected);
        Assert.True(second.Success);
        Assert.True(second.Collected);
        Assert.Equal(0, second.CollectionAttempts);
    }

    [Fact]
    public async Task RegisterForDisposal_resources_are_released_by_the_loader()
    {
        using var workspace = TempPluginRoot.Create();
        FixturePackageFactory.Create(workspace.Root, "test-disposal", "DisposalPlugin");
        var (host, logger, _) = HostFactory.Create(workspace.Root);
        await using var hostScope = host;
        await host.LoadAsync("test-disposal");

        var unload = await host.UnloadAsync("test-disposal");

        Assert.True(unload.Collected, unload.FailureReason);
        Assert.True(logger.Contains("disposal plugin resource released"), "Context disposal must release registered resources.");
    }

    [Fact]
    public async Task Lease_timers_stop_on_stop_and_on_unload()
    {
        using var workspace = TempPluginRoot.Create();
        FixturePackageFactory.Create(workspace.Root, "test-timed", "TimedPlugin");
        var (host, _, publisher) = HostFactory.Create(workspace.Root);
        await using var hostScope = host;
        var load = await host.LoadAsync("test-timed");
        Assert.True(load.Success, load.Message);

        await Task.Delay(200);
        var ticksWhileActive = publisher.Events.Count(static islandEvent => islandEvent.Title == "tick");
        Assert.True(ticksWhileActive > 0, "TimedPlugin should have published ticks.");

        Assert.True(await host.StopAsync("test-timed"));
        var ticksAfterStop = publisher.Events.Count(static islandEvent => islandEvent.Title == "tick");
        await Task.Delay(225);
        Assert.Equal(ticksAfterStop, publisher.Events.Count(static islandEvent => islandEvent.Title == "tick"));

        var unload = await host.UnloadAsync("test-timed");
        Assert.True(unload.Collected, unload.FailureReason);
    }

    [Fact]
    public async Task Sdk_is_loaded_once_from_the_default_context()
    {
        using var workspace = TempPluginRoot.Create();
        FixturePackageFactory.Create(workspace.Root, "test-identity", "IdentityPlugin");
        var (host, _, publisher) = HostFactory.Create(workspace.Root);
        await using var hostScope = host;

        var load = await host.LoadAsync("test-identity");
        Assert.True(load.Success, load.Message);
        var identity = publisher.Events.First(static islandEvent => islandEvent.Title.StartsWith("plugin-alc=", StringComparison.Ordinal));
        Assert.Contains("plugin-alc=FluidBar.Plugin:", identity.Title, StringComparison.Ordinal);
        Assert.Contains("sdk-alc=Default", identity.Title, StringComparison.Ordinal);
        Assert.Contains("sdk-location=" + typeof(IFluidBarPlugin).Assembly.Location, identity.Title, StringComparison.Ordinal);

        var unload = await host.UnloadAsync("test-identity");
        Assert.True(unload.Collected, unload.FailureReason);
    }
}

public sealed class PluginHostServiceTests
{
    [Fact]
    public async Task Settings_persist_across_reload()
    {
        using var workspace = TempPluginRoot.Create();
        FixturePackageFactory.Create(workspace.Root, "test-settings", "SettingsPlugin");
        var (host, _, publisher) = HostFactory.Create(workspace.Root);
        await using var hostScope = host;

        var first = await host.LoadAsync("test-settings");
        Assert.True(first.Success, first.Message);
        Assert.True(publisher.HasTitle("counter=1"));
        Assert.True((await host.UnloadAsync("test-settings")).Collected);

        var second = await host.LoadAsync("test-settings");
        Assert.True(second.Success, second.Message);
        Assert.True(publisher.HasTitle("counter=2"), "The counter should survive unload/reload in the host-owned settings file.");

        var settingsPath = Path.Combine(workspace.Root, "test-settings", "settings.json");
        Assert.True(File.Exists(settingsPath));
        Assert.Contains("\"pluginId\"", File.ReadAllText(settingsPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Settings_are_isolated_per_plugin()
    {
        using var workspace = TempPluginRoot.Create();
        FixturePackageFactory.Create(workspace.Root, "plugin-a", "SettingsPlugin");
        FixturePackageFactory.Create(workspace.Root, "plugin-b", "SettingsPlugin");
        var (host, _, publisher) = HostFactory.Create(workspace.Root);
        await using var hostScope = host;

        Assert.True((await host.LoadAsync("plugin-a")).Success);
        Assert.True((await host.LoadAsync("plugin-b")).Success);

        Assert.Equal(2, publisher.Events.Count(static islandEvent => islandEvent.Title == "counter=1"));
    }

    [Fact]
    public async Task Context_services_enforce_bounds_and_report_markers()
    {
        using var workspace = TempPluginRoot.Create();
        FixturePackageFactory.Create(workspace.Root, "test-probe", "ProbePlugin", customizeManifest: value =>
        {
            var commands = (System.Text.Json.Nodes.JsonArray)value["contributes"]!["commands"]!;
            commands.Add(new System.Text.Json.Nodes.JsonObject
            {
                ["id"] = "test-probe.fail",
                ["title"] = new System.Text.Json.Nodes.JsonObject { ["en-US"] = "Fail" },
                ["contexts"] = new System.Text.Json.Nodes.JsonArray("commandPalette"),
            });
        });
        var (host, logger, publisher) = HostFactory.Create(workspace.Root);
        await using var hostScope = host;

        var load = await host.LoadAsync("test-probe");
        Assert.True(load.Success, load.Message);
        Assert.True(publisher.HasTitle("localized=fixture body"));
        Assert.True(publisher.HasTitle("missing=missing.key"));
        Assert.True(publisher.HasTitle("plural=2 items"));
        Assert.True(publisher.HasTitle("storage-rejected"));
        Assert.True(publisher.HasTitle("storage-write=True"));
        Assert.True(publisher.HasTitle("notification-rejected=True"));
        Assert.True(publisher.HasTitle("source-rejected=True"));
        Assert.True(publisher.HasTitle("granted-ui=True"));
        Assert.True(logger.Contains("Missing translation key 'missing.key'"));
        Assert.True(File.Exists(Path.Combine(workspace.Root, "test-probe", "data", "cache", "probe.txt")));

        var ping = await host.ExecuteCommandAsync("test-probe", "test-probe.ping");
        Assert.True(ping.Executed);
        Assert.True(ping.Succeeded);
        Assert.True(publisher.HasTitle("command-executed"));

        var failing = await host.ExecuteCommandAsync("test-probe", "test-probe.fail");
        Assert.True(failing.Executed);
        Assert.False(failing.Succeeded);
        Assert.NotNull(failing.Error);

        var missing = await host.ExecuteCommandAsync("test-probe", "test-probe.unknown");
        Assert.False(missing.Executed);
    }

    [Fact]
    public async Task Localization_honors_the_host_ui_locale_and_parent_fallback()
    {
        using var workspace = TempPluginRoot.Create();
        var manifest = FixturePackageFactory.CreateManifest("test-probe", "ProbePlugin");
        var locales = (System.Text.Json.Nodes.JsonObject)manifest["locales"]!;
        locales["available"] = new System.Text.Json.Nodes.JsonArray("en-US", "zh-Hans");
        var packageDirectory = FixturePackageFactory.Create(workspace.Root, "test-probe", "ProbePlugin", customizeManifest: value =>
        {
            value["locales"] = locales.DeepClone();
        });
        File.WriteAllText(
            Path.Combine(packageDirectory, "locales", "zh-Hans.json"),
            """
            {"$schema":"https://fluidbar.app/schemas/locale.schema.json","locale":"zh-Hans","version":1,
             "messages":{"fixture.body":"中文正文","items.count":"{count, plural, other {# 项}}"}}
            """);
        var (host, _, publisher) = HostFactory.Create(
            workspace.Root,
            options => options with { HostUiLocales = ["zh-Hans"] });
        await using var hostScope = host;

        var load = await host.LoadAsync("test-probe");
        Assert.True(load.Success, load.Message);
        Assert.True(publisher.HasTitle("localized=中文正文"));
        Assert.True(publisher.HasTitle("plural=2 项"));
    }

    [Fact]
    public async Task Versioned_layout_uses_the_active_json_pointer()
    {
        using var workspace = TempPluginRoot.Create();
        var pluginRoot = workspace.Combine("plugins");
        Directory.CreateDirectory(pluginRoot);
        var versionOne = FixturePackageFactory.Create(
            Path.Combine(workspace.Root, "staging-v1"),
            "test-plugin",
            "HappyPlugin",
            version: "1.0.0");
        var versionTwo = FixturePackageFactory.Create(
            Path.Combine(workspace.Root, "staging-v2"),
            "test-plugin",
            "HappyPlugin",
            version: "2.0.0");
        var pluginDirectory = Path.Combine(pluginRoot, "test-plugin");
        Directory.CreateDirectory(pluginDirectory);
        Directory.Move(versionOne, Path.Combine(pluginDirectory, "1.0.0"));
        Directory.Move(versionTwo, Path.Combine(pluginDirectory, "2.0.0"));
        File.WriteAllText(Path.Combine(pluginDirectory, "active.json"), """{"activeVersion":"1.0.0"}""");

        var (host, _, _) = HostFactory.Create(pluginRoot);
        await using var hostScope = host;
        var load = await host.LoadAsync("test-plugin");
        Assert.True(load.Success, load.Message);
        Assert.Equal("1.0.0", load.Handle!.Manifest.Version);
        Assert.True((await host.UnloadAsync("test-plugin")).Collected);

        File.WriteAllText(Path.Combine(pluginDirectory, "active.json"), """{"activeVersion":"2.0.0"}""");
        var upgraded = await host.LoadAsync("test-plugin");
        Assert.True(upgraded.Success, upgraded.Message);
        Assert.Equal("2.0.0", upgraded.Handle!.Manifest.Version);
    }

    [Fact]
    public async Task Discovery_rejects_manifest_id_folder_mismatch_and_ignores_staging()
    {
        using var workspace = TempPluginRoot.Create();
        var packageDirectory = FixturePackageFactory.Create(Path.Combine(workspace.Root, "staging"), "test-plugin");
        var stagingRoot = Path.Combine(workspace.Root, "staging");
        Directory.Move(packageDirectory, Path.Combine(workspace.Root, "wrong-folder"));
        if (Directory.Exists(stagingRoot) && !Directory.EnumerateFileSystemEntries(stagingRoot).Any())
        {
            Directory.Delete(stagingRoot);
        }

        var stagingManifest = FixturePackageFactory.Create(Path.Combine(workspace.Root, ".staging"), "ignored-plugin");
        Assert.True(Directory.Exists(stagingManifest));

        var (host, _, _) = HostFactory.Create(workspace.Root);
        await using var hostScope = host;
        var discoveries = await host.DiscoverAsync();

        var mismatch = Assert.Single(discoveries);
        Assert.Equal("test-plugin", mismatch.PluginId);
        Assert.False(mismatch.IsValid);
        Assert.Contains(mismatch.Errors, error => error.Contains("does not match", StringComparison.OrdinalIgnoreCase));
    }
}
