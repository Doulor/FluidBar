using FluidBar.PluginHost.Loader;
using FluidBar.Sdk;
using FluidBar.Sdk.Contributions;
using FluidBar.Sdk.Manifest;
using FluidBar.Sdk.Versioning;

namespace FluidBar.PluginHost.Tests;

/// <summary>
/// Built-in lane: a compiled-in plugin runs through the same SDK lifecycle and host-owned services
/// (settings, storage, localization, commands, leases) without an AssemblyLoadContext.
/// </summary>
public sealed class InProcessPluginSessionTests
{
    [Fact]
    public async Task Session_runs_lifecycle_commands_and_settings()
    {
        using var workspace = TempPluginRoot.Create();
        var plugin = new TestBuiltinPlugin();
        await using var session = CreateSession(workspace.Root, plugin);

        var start = await session.StartAsync();
        Assert.True(start.Success, start.Message);
        Assert.Equal(PluginLifecycleState.Active, session.State);
        Assert.True(plugin.Initialized);
        Assert.Equal(1, plugin.StartCount);

        var command = await session.ExecuteCommandAsync("builtin-test.ping");
        Assert.True(command.Executed);
        Assert.True(command.Succeeded);
        Assert.Equal(1, plugin.PingCount);

        Assert.Null(session.GetSettingValue("builtin-test.enabled"));
        Assert.True(session.SetSettingValue("builtin-test.enabled", System.Text.Json.JsonSerializer.SerializeToElement(true)));
        Assert.True(session.GetSettingValue("builtin-test.enabled")!.Value.GetBoolean());
        await session.SaveSettingsAsync();
        var settingsPath = Path.Combine(workspace.Root, "builtin-test", "settings.json");
        Assert.True(File.Exists(settingsPath));
        Assert.Contains("builtin-test.enabled", File.ReadAllText(settingsPath), StringComparison.Ordinal);

        Assert.True(await session.StopAsync());
        Assert.Equal(PluginLifecycleState.Stopped, session.State);
        Assert.Equal(1, plugin.StopCount);

        var restart = await session.StartAsync();
        Assert.True(restart.Success, restart.Message);
        Assert.Equal(PluginLifecycleState.Active, session.State);
        Assert.Equal(2, plugin.StartCount);

        await session.DisposeAsync();
        Assert.Equal(PluginLifecycleState.Unloaded, session.State);
        Assert.True(plugin.Disposed);
    }

    [Fact]
    public async Task Session_isolates_a_throwing_start_and_keeps_the_host_alive()
    {
        using var workspace = TempPluginRoot.Create();
        var plugin = new TestBuiltinPlugin { ThrowOnStart = true };
        await using var session = CreateSession(workspace.Root, plugin);

        var start = await session.StartAsync();
        Assert.False(start.Success);
        Assert.Equal(PluginLoadFailureKind.StartFailed, start.FailureKind);
        Assert.Equal(PluginLifecycleState.Failed, session.State);
        Assert.NotNull(session.LastError);

        var command = await session.ExecuteCommandAsync("builtin-test.ping");
        Assert.False(command.Executed);
    }

    [Fact]
    public async Task Session_refuses_commands_that_are_not_declared()
    {
        using var workspace = TempPluginRoot.Create();
        var plugin = new TestBuiltinPlugin();
        await using var session = CreateSession(workspace.Root, plugin);
        Assert.True((await session.StartAsync()).Success);

        var command = await session.ExecuteCommandAsync("builtin-test.undeclared");
        Assert.False(command.Executed);
        Assert.Contains("no handler", command.Error, StringComparison.OrdinalIgnoreCase);
    }

    private static InProcessPluginSession CreateSession(string root, IFluidBarPlugin plugin)
    {
        var options = new PluginHostOptions
        {
            PluginRootDirectory = root,
            AppVersion = new SemanticVersion(2, 0, 0),
            InitializeTimeout = TimeSpan.FromSeconds(2),
            StartTimeout = TimeSpan.FromSeconds(2),
            StopTimeout = TimeSpan.FromSeconds(2),
            DisposeTimeout = TimeSpan.FromSeconds(2),
        };
        var manifest = new PluginManifest
        {
            SchemaVersion = 2,
            Id = "builtin-test",
            Version = "2.0.0",
            ApiVersion = "2.0.0",
            MinAppVersion = "2.0.0",
            Publisher = new PublisherInfo { Id = "fluidbar", DisplayName = "FluidBar" },
            Name = LocalizedString.From("Built-in Test"),
            Summary = LocalizedString.From("Test plugin."),
            License = "MIT",
            Contributes = new PluginContributions
            {
                Commands =
                [
                    new CommandContribution { Id = "builtin-test.ping", Title = LocalizedString.From("Ping") },
                ],
                SettingsPages =
                [
                    new SettingsPageContribution
                    {
                        Id = "builtin-test.settings",
                        Title = LocalizedString.From("Test"),
                        Controls =
                        [
                            new SettingControlContribution
                            {
                                Type = SettingControlType.Toggle,
                                Key = "builtin-test.enabled",
                                Title = LocalizedString.From("Enabled"),
                            },
                        ],
                    },
                ],
            },
        };

        return new InProcessPluginSession(options, manifest, root, plugin);
    }

    private sealed class TestBuiltinPlugin : IFluidBarPlugin
    {
        private IDisposable? _registration;
        private IPluginContext? _context;

        public bool Initialized { get; private set; }

        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public int PingCount { get; private set; }

        public bool Disposed { get; private set; }

        public bool ThrowOnStart { get; init; }

        public ValueTask InitializeAsync(IPluginContext context, CancellationToken cancellationToken)
        {
            _context = context;
            Initialized = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask StartAsync(CancellationToken cancellationToken)
        {
            if (ThrowOnStart)
            {
                throw new InvalidOperationException("boom");
            }

            StartCount++;
            _registration = _context!.Commands.Register("builtin-test.ping", (_, _) =>
            {
                PingCount++;
                return ValueTask.CompletedTask;
            });
            return ValueTask.CompletedTask;
        }

        public ValueTask StopAsync(CancellationToken cancellationToken)
        {
            StopCount++;
            _registration?.Dispose();
            _registration = null;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
