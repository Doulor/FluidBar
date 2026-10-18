using FluidBar.Integration.Tests.TestSupport;
using FluidBar.Plugins.Builtin;
using FluidBar.Sdk;

namespace FluidBar.Integration.Tests;

/// <summary>
/// Seam 1: platform snapshots → built-in plugins (real catalog + real SDK lifecycle) → captured SDK
/// events + commands. Everything is driven through fake platform service interfaces; no hardware, no UI.
/// </summary>
public class PlatformToSdkSeamTests
{
    private sealed class FakePlatformGraph : IAsyncDisposable
    {
        public FakeMediaSource Media { get; } = new();

        public FakeClipboardSource Clipboard { get; } = new();

        public FakeNotificationSource Notifications { get; } = new();

        public FakeAgentInboxSource AgentInbox { get; } = new();

        public FakeHookInstaller HookInstaller { get; } = new();

        public FakeDeviceEventSource Usb { get; } = new("system:usb");

        public FakeDeviceEventSource Bluetooth { get; } = new("system:bluetooth");

        public TestTimeProvider Time { get; } = TestTimeProvider.StartingAtEpoch();

        public BuiltinPlatformServices Services => FakeSnapshots.Services(
            Media, Clipboard, Notifications, AgentInbox, HookInstaller, Usb, Bluetooth);

        public Task<BuiltinPluginHost> StartAsync() => BuiltinPluginHost.StartAsync(Services, Time);

        public async ValueTask DisposeAsync()
        {
            Media.ArtworkCache.Dispose();
            await Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Catalog_constructs_all_five_builtin_plugins_from_fake_services()
    {
        var graph = new FakePlatformGraph();
        await using var host = await graph.StartAsync();

        Assert.Equal(5, host.Plugins.Count);
        Assert.Equal(
            [
                BuiltinIds.MediaPlugin, BuiltinIds.ClipboardPlugin, BuiltinIds.NotificationsPlugin,
                BuiltinIds.AgentPlugin, BuiltinIds.DevicesPlugin,
            ],
            host.Plugins.Select(run => run.Descriptor.Id));
        Assert.All(host.Plugins, run => Assert.Equal(run.Descriptor.Id, Assert.IsAssignableFrom<BuiltinPluginBase>(run.Plugin).PluginId));
    }

    [Fact]
    public async Task Media_snapshot_becomes_media_sdk_event_with_progress_and_transport_commands()
    {
        var graph = new FakePlatformGraph();
        await using var host = await graph.StartAsync();

        graph.Media.Emit(FakeSnapshots.MediaPlaying());

        var mediaEvents = host.EventsOf(BuiltinIds.MediaPlugin);
        var latest = mediaEvents[^1];
        Assert.Equal(BuiltinIds.MediaPlugin, latest.Source);
        Assert.Equal(IslandEventKind.Media, latest.Kind);
        Assert.Equal("Midnight City", latest.Title);
        Assert.Equal("M83", latest.Payload?.Subtitle);
        Assert.True(latest.Payload?.IsActive);
        Assert.Equal(BuiltinIds.MediaCompactLayout, latest.LayoutId);
        Assert.Equal(0.125, latest.Payload!.Progress!.Value, 3);

        var context = host.Get(BuiltinIds.MediaPlugin).Context;
        var commandContext = new PluginCommandContext
        {
            PluginId = BuiltinIds.MediaPlugin,
            CommandId = BuiltinIds.MediaTogglePlayPause,
        };
        await context.CommandRegistry.InvokeAsync(BuiltinIds.MediaTogglePlayPause, commandContext);
        await context.CommandRegistry.InvokeAsync(BuiltinIds.MediaNext, commandContext);
        await context.CommandRegistry.InvokeAsync(BuiltinIds.MediaPrevious, commandContext);

        Assert.Equal(["toggle", "next", "previous"], graph.Media.TransportCalls);
        Assert.True(context.CommandRegistry.IsRegistered(BuiltinIds.MediaTogglePlayPause));
    }

    [Fact]
    public async Task Clipboard_snapshot_becomes_sdk_event_and_commands_work()
    {
        var graph = new FakePlatformGraph();
        await using var host = await graph.StartAsync();

        graph.Clipboard.Emit(FakeSnapshots.ClipboardText("npm run build"));

        var latest = host.EventsOf(BuiltinIds.ClipboardPlugin)[^1];
        Assert.Equal(IslandEventKind.Auto, latest.Kind);
        Assert.Equal("npm run build", latest.Title);
        Assert.Equal("npm run build", latest.Content);
        Assert.Equal("1", latest.Payload?.Badge);

        var context = host.Get(BuiltinIds.ClipboardPlugin).Context;
        var commandContext = new PluginCommandContext { PluginId = BuiltinIds.ClipboardPlugin, CommandId = BuiltinIds.ClipboardCopyLatest };
        await context.CommandRegistry.InvokeAsync(BuiltinIds.ClipboardCopyLatest, commandContext);
        Assert.Contains("npm run build", graph.Clipboard.WrittenTexts);

        await context.CommandRegistry.InvokeAsync(BuiltinIds.ClipboardPinLatest, commandContext);
        Assert.True(context.SettingsStore.TryGetString("clipboard.pinnedText", out var pinned));
        Assert.Equal("npm run build", pinned);
        Assert.Equal(1, context.SettingsStore.SaveCount);

        await context.CommandRegistry.InvokeAsync(BuiltinIds.ClipboardClearHistory, commandContext);
        Assert.Contains(BuiltinIds.ClipboardPlugin, context.Publisher.Retracted);
    }

    [Fact]
    public async Task Notification_with_verification_code_exposes_flag_and_copy_command()
    {
        var graph = new FakePlatformGraph();
        await using var host = await graph.StartAsync();

        graph.Notifications.Emit(FakeSnapshots.NotificationWithCode());

        var latest = host.EventsOf(BuiltinIds.NotificationsPlugin)[^1];
        Assert.Equal(IslandEventKind.Notification, latest.Kind);
        Assert.Equal("Sign-in code", latest.Title);
        Assert.Equal("482913", latest.Payload?.VerificationCode);
        Assert.Equal("482913", latest.Payload?.Badge);

        var context = host.Get(BuiltinIds.NotificationsPlugin).Context;
        await context.CommandRegistry.InvokeAsync(
            BuiltinIds.NotificationsCopyCode,
            new PluginCommandContext { PluginId = BuiltinIds.NotificationsPlugin, CommandId = BuiltinIds.NotificationsCopyCode });
        Assert.Contains("482913", graph.Clipboard.WrittenTexts);
    }

    [Fact]
    public async Task Agent_snapshot_becomes_agent_sdk_event_with_agent_token()
    {
        var graph = new FakePlatformGraph();
        await using var host = await graph.StartAsync();

        graph.AgentInbox.Emit(FakeSnapshots.AgentCompleted("claude"));

        var latest = host.EventsOf(BuiltinIds.AgentPlugin)[^1];
        Assert.Equal(IslandEventKind.Agent, latest.Kind);
        Assert.Equal("Agent finished", latest.Title);
        Assert.Equal("claude", latest.Payload?.Subtitle);
        Assert.Equal("completed", latest.Payload?.Badge);
    }

    [Fact]
    public async Task Device_snapshots_publish_with_stable_core_source_tokens()
    {
        var graph = new FakePlatformGraph();
        await using var host = await graph.StartAsync();

        graph.Usb.Emit(FakeSnapshots.UsbConnected());
        graph.Bluetooth.Emit(FakeSnapshots.BluetoothConnected());

        var deviceEvents = host.EventsOf(BuiltinIds.DevicesPlugin);
        var usb = deviceEvents.Single(islandEvent => islandEvent.Content?.Contains("system:usb", StringComparison.Ordinal) == true);
        Assert.Equal("system:usb", usb.Payload?.Fields!["source"]);
        Assert.Equal("USB 046D:C52B", usb.Title);

        var bluetooth = deviceEvents.Single(islandEvent => islandEvent.Content?.Contains("system:bluetooth", StringComparison.Ordinal) == true);
        Assert.Equal("system:bluetooth", bluetooth.Payload?.Fields!["source"]);
    }

    [Fact]
    public async Task Plugin_lifecycle_subscribes_and_unsubscribes_cleanly()
    {
        var graph = new FakePlatformGraph();
        var host = await graph.StartAsync();

        Assert.Equal(1, graph.Media.SnapshotSubscriberCount);
        Assert.Equal(1, graph.Clipboard.SnapshotSubscriberCount);
        Assert.Equal(1, graph.Notifications.SnapshotSubscriberCount);
        Assert.Equal(1, graph.AgentInbox.SnapshotSubscriberCount);
        Assert.Equal(1, graph.Usb.SnapshotSubscriberCount);
        Assert.Equal(1, graph.Bluetooth.SnapshotSubscriberCount);

        await host.DisposeAsync();

        Assert.Equal(0, graph.Media.SnapshotSubscriberCount);
        Assert.Equal(0, graph.Clipboard.SnapshotSubscriberCount);
        Assert.Equal(0, graph.Notifications.SnapshotSubscriberCount);
        Assert.Equal(0, graph.AgentInbox.SnapshotSubscriberCount);
        Assert.Equal(0, graph.Usb.SnapshotSubscriberCount);
        Assert.Equal(0, graph.Bluetooth.SnapshotSubscriberCount);

        await graph.DisposeAsync();
    }
}
