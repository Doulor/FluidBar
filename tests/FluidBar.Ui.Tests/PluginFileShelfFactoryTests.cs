using FluidBar.App.Localization;
using FluidBar.App.Plugins;
using FluidBar.Plugins.Builtin;
using FluidBar.Sdk.Manifest;
using FluidBar.Sdk;
using FluidBar.PluginHost.Loader;
using FluidBar.Sdk.Versioning;
using FluidBar.Platform.Windows.Agent;
using FluidBar.Platform.Windows.Clipboard;
using FluidBar.Platform.Windows.Devices;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Media;
using FluidBar.Platform.Windows.Notifications;
using FluidBar.Platform.Windows.Threading;

namespace FluidBar.Ui.Tests;

public sealed class PluginFileShelfFactoryTests
{
    [Fact]
    public async Task Production_runtime_starts_all_five_real_builtins_with_the_file_shelf_factory()
    {
        var root = Path.Combine(Path.GetTempPath(), "FluidBarTests", "builtin-runtime", Guid.NewGuid().ToString("N"));
        try
        {
            var logger = NullPlatformLogger.Instance;
            var counters = new PlatformCounters();
            // Construct actual platform sources without starting watchers, accessing the clipboard,
            // registering startup, or changing the running application's plugin state.
            await using var messages = new PlatformMessageHost(logger, counters);
            await using var media = new GsmtcMediaSessionService(logger, counters);
            await using var clipboard = new ClipboardService(messages, logger, counters);
            await using var notifications = new NotificationService(logger, counters);
            await using var inbox = new AgentInboxWatcher(new AgentInboxOptions { InboxDirectory = Path.Combine(root, "inbox") }, logger, counters);
            await using var devices = new UsbDeviceMonitor(messages, logger, counters);
            var localizer = new FakeLocalizer();
            await using var runtime = await PluginRuntime.CreateAsync(new PluginRuntimeOptions
            {
                PluginRootDirectory = Path.Combine(root, "plugins"), AppVersion = new SemanticVersion(2, 0, 0),
                Localizer = localizer, LoadCommunity = false,
                FileShelfFactory = manifest => PluginFileShelfFactory.Create(manifest, localizer, null, null),
                BuiltinServices = new BuiltinPlatformServices
                {
                    Media = media, Clipboard = clipboard, Notifications = notifications, AgentInbox = inbox,
                    UsbDevices = devices, AgentHookInstaller = new AgentHookInstaller(new HookInstallOptions
                    {
                        SettingsFilePath = Path.Combine(root, "agent.json"), HookCommand = "unused-test-command",
                        BackupDirectory = Path.Combine(root, "backups"),
                    }, logger),
                },
            });
            Assert.Equal(5, runtime.Builtins.Count);
            Assert.All(runtime.Builtins, builtin =>
            {
                Assert.Null(builtin.Error);
                Assert.NotNull(builtin.Session);
                Assert.Equal(PluginLifecycleState.Active, builtin.Session.State);
            });
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Builtin_sessions_start_with_the_production_file_shelf_factory()
    {
        var root = Path.Combine(Path.GetTempPath(), "FluidBarTests", "builtin-file-shelf", Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var descriptor in BuiltinPluginCatalog.Descriptors)
            {
                var manifest = BuiltinPluginManifests.Create(descriptor);
                await using var session = new InProcessPluginSession(new PluginHostOptions
                {
                    PluginRootDirectory = root, AppVersion = new SemanticVersion(2, 0, 0),
                    FileShelfFactory = value => PluginFileShelfFactory.Create(value, new FakeLocalizer(), null, null),
                }, manifest, root, new ContextProbe());
                var result = await session.StartAsync();
                Assert.True(result.Success, descriptor.Id + ": " + result.Message);
                Assert.Equal(PluginLifecycleState.Active, session.State);
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private sealed class ContextProbe : IFluidBarPlugin
    {
        public ValueTask InitializeAsync(IPluginContext context, CancellationToken token)
        { Assert.Null(context.FileShelf); return ValueTask.CompletedTask; }
        public ValueTask StartAsync(CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask StopAsync(CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public void Production_factory_accepts_all_builtin_manifests_without_file_shelf_capabilities()
    {
        foreach (var descriptor in BuiltinPluginCatalog.Descriptors)
        {
            var manifest = BuiltinPluginManifests.Create(descriptor);
            Assert.Null(PluginFileShelfFactory.Create(manifest, new FakeLocalizer(), null, null));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Clipboard_copy_requires_its_own_write_permission(bool write)
    {
        var manifest = new PluginManifest { Id = "fluidbar-file-shelf", Capabilities = new PluginCapabilities
        { Files = new FilesCapability(), Ui = new UiCapability { Pages = true },
            Clipboard = new ClipboardCapability { Write = write } } };
        var copies = 0;
        var shelf = PluginFileShelfFactory.Create(manifest, new FakeLocalizer(), null,
            (_, _) => { copies++; return Task.FromResult(true); });
        Assert.NotNull(shelf);
        Assert.Equal(write, await shelf.CopyFilesAsync([Path.Combine(Path.GetTempPath(), "reference.txt")], default));
        Assert.Equal(write ? 1 : 0, copies);
    }
}
