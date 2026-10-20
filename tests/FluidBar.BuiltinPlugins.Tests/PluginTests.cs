using System.Collections.Immutable;
using FluidBar.Platform.Windows.Agent;
using FluidBar.Platform.Windows.Clipboard;
using FluidBar.Platform.Windows.Devices;
using FluidBar.Platform.Windows.Media;
using FluidBar.Platform.Windows.Media.Lyrics;
using FluidBar.Platform.Windows.Notifications;
using FluidBar.Plugins.Builtin;
using FluidBar.Sdk;
using FluidBar.Sdk.Contributions;

namespace FluidBar.BuiltinPlugins.Tests;

internal static class PluginTestHarness
{
    public static async Task<(FakePluginContext Context, T Plugin)> StartAsync<T>(T plugin, string pluginId,
        TimeProvider? timeProvider = null)
        where T : IFluidBarPlugin
    {
        var context = new FakePluginContext(pluginId) { Time = timeProvider ?? TimeProvider.System };
        await plugin.InitializeAsync(context, CancellationToken.None);
        await plugin.StartAsync(CancellationToken.None);
        return (context, plugin);
    }

    public static PluginCommandContext ConsentCommand(string pluginId, string commandId,
        bool consent = true, CommandInvocationSource source = CommandInvocationSource.Settings)
        => new()
        {
            PluginId = pluginId,
            CommandId = commandId,
            Source = source,
            Arguments = consent
                ? new Dictionary<string, string?>(StringComparer.Ordinal) { ["consent"] = "true" }
                : null,
        };

    public static MediaSnapshot MediaSnapshot(string trackKey = "track-1", bool playing = true)
        => new("Spotify.exe", "Spotify", "Song", "Artist", "Album", MediaPlaybackState.Playing,
            playing, TimeSpan.FromSeconds(30), DateTimeOffset.UnixEpoch, TimeSpan.FromSeconds(200),
            true, true, true, trackKey);

    public static ClipboardEntry ClipboardText(string text, uint sequence = 1)
        => new(sequence, DateTimeOffset.UnixEpoch, ClipboardContentKind.Text, text, text.Length,
            ImmutableArray<string>.Empty);

    public static NotificationEntry Notification(string? code = "123456", uint id = 7)
        => new(id, "App", "Title", "Body", DateTimeOffset.UnixEpoch, code);

    public static AgentEventEntry AgentEvent(string id = "agent-1", string title = "Done",
        string? sessionTitle = null, string? action = null)
        => new(id, AgentKind.Claude, AgentEventKind.Completed, title, "message", DateTimeOffset.UnixEpoch,
            "a.json", SessionTitle: sessionTitle, Action: action);

    public static DeviceEventEntry Device(string id = "usb-1", DeviceKind kind = DeviceKind.Usb)
        => new(id, kind, DeviceChangeKind.Connected, "USB 1234:5678", DateTimeOffset.UnixEpoch);
}

public sealed class MediaPluginTests
{
    [Fact]
    public async Task PublishesMediaEventAndDedupesIdenticalSnapshots()
    {
        var media = new FakeMediaSource();
        var (context, _) = await PluginTestHarness.StartAsync(new MediaPlugin(media), BuiltinIds.MediaPlugin);

        media.Emit(PluginTestHarness.MediaSnapshot());
        Assert.Equal(1, context.Publisher.Count);
        var published = context.Publisher.LastPublished;
        Assert.Equal(BuiltinIds.MediaPlugin, published.Source);
        Assert.Equal(IslandEventKind.Media, published.Kind);
        Assert.Equal(BuiltinIds.MediaCompactLayout, published.LayoutId);
        Assert.Equal("Song", published.Title);
        Assert.Equal("Artist", published.Payload!.Subtitle);
        Assert.True(published.Payload.IsActive);
        Assert.Equal("playing", published.Payload.Fields!["badge"]);

        media.Emit(PluginTestHarness.MediaSnapshot());
        Assert.Equal(1, context.Publisher.Count); // change-only coalescing in the adapter
    }

    [Fact]
    public async Task ArtworkIsMaterializedToAPath()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FluidBarBuiltinTests", Guid.NewGuid().ToString("N"));
        using var store = new ArtworkTempStore(new ArtworkTempStoreOptions { DirectoryPath = directory });
        var media = new FakeMediaSource();
        var (context, _) = await PluginTestHarness.StartAsync(
            new MediaPlugin(media, store), BuiltinIds.MediaPlugin);

        var artwork = new ArtworkEntry(TestWait.PngHeader(64, 64), "image/png", new ImageDimensions(64, 64));
        media.EmitArtwork("track-1", artwork);
        media.Emit(PluginTestHarness.MediaSnapshot());

        await TestWait.UntilAsync(() => context.Publisher.Published.Any(
            islandEvent => islandEvent.Payload?.AlbumArtPath is not null));
        var withArt = context.Publisher.Published.Last(islandEvent => islandEvent.Payload?.AlbumArtPath is not null);
        Assert.True(File.Exists(withArt.Payload!.AlbumArtPath));
        Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public async Task LyricsAreAttachedFromThePlatformLookup()
    {
        var lookup = new FakeLyricsLookup(new LyricsDocument("Song", "Artist", "test",
            ImmutableArray.Create(new LyricsLine(TimeSpan.Zero, "first line"),
                new LyricsLine(TimeSpan.FromSeconds(10), "second line"))));
        var media = new FakeMediaSource();
        var (context, _) = await PluginTestHarness.StartAsync(
            new MediaPlugin(media, artworkStore: null, lyrics: lookup), BuiltinIds.MediaPlugin);

        media.Emit(PluginTestHarness.MediaSnapshot());
        await TestWait.UntilAsync(() => context.Publisher.Published.Any(
            islandEvent => islandEvent.Payload?.LyricLines is { Count: > 0 }));
        var published = context.Publisher.Published.Last(islandEvent => islandEvent.Payload?.LyricLines is { Count: > 0 });
        Assert.Contains("second line", published.Payload!.LyricLines!);
        Assert.Equal(1, lookup.Calls);
    }

    [Fact]
    public async Task TransportCommandsRouteToThePlatformService()
    {
        var media = new FakeMediaSource();
        var (context, _) = await PluginTestHarness.StartAsync(new MediaPlugin(media), BuiltinIds.MediaPlugin);

        await context.CommandRegistry.InvokeAsync(BuiltinIds.MediaTogglePlayPause,
            PluginTestHarness.ConsentCommand(BuiltinIds.MediaPlugin, BuiltinIds.MediaTogglePlayPause));
        await context.CommandRegistry.InvokeAsync(BuiltinIds.MediaNext,
            PluginTestHarness.ConsentCommand(BuiltinIds.MediaPlugin, BuiltinIds.MediaNext));
        await context.CommandRegistry.InvokeAsync(BuiltinIds.MediaPrevious,
            PluginTestHarness.ConsentCommand(BuiltinIds.MediaPlugin, BuiltinIds.MediaPrevious));

        Assert.Equal(["toggle", "next", "previous"], media.TransportCalls);
    }

    [Fact]
    public async Task StopReleasesPlatformSubscriptions()
    {
        var media = new FakeMediaSource();
        var plugin = new MediaPlugin(media);
        await PluginTestHarness.StartAsync(plugin, BuiltinIds.MediaPlugin);
        Assert.Equal(1, media.CountSubscribers());

        await plugin.StopAsync(CancellationToken.None);
        Assert.Equal(0, media.CountSubscribers());
        await plugin.StopAsync(CancellationToken.None); // idempotent
        await plugin.DisposeAsync();
    }

    private sealed class FakeLyricsLookup : ILyricsLookup
    {
        private readonly LyricsDocument _document;

        public FakeLyricsLookup(LyricsDocument document) => _document = document;

        public int Calls { get; private set; }

        public Task<LyricsDocument?> GetLyricsAsync(LyricsQuery query, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult<LyricsDocument?>(_document);
        }
    }
}

public sealed class ClipboardPluginTests
{
    [Fact]
    public async Task PublishesTextAndSuppressesDuplicateSequence()
    {
        var clipboard = new FakeClipboardSource();
        var (context, _) = await PluginTestHarness.StartAsync(new ClipboardPlugin(clipboard),
            BuiltinIds.ClipboardPlugin);

        clipboard.Emit(PluginTestHarness.ClipboardText("hello world"));
        Assert.Equal(1, context.Publisher.Count);
        var published = context.Publisher.LastPublished;
        Assert.Equal(BuiltinIds.ClipboardPlugin, published.Source);
        Assert.Equal("hello world", published.Content);
        Assert.Equal(BuiltinIds.ClipboardCompactLayout, published.LayoutId);

        clipboard.Emit(PluginTestHarness.ClipboardText("hello world")); // same sequence
        Assert.Equal(1, context.Publisher.Count);
    }

    [Fact]
    public async Task CommandsCopyPinAndClear()
    {
        var clipboard = new FakeClipboardSource();
        var (context, _) = await PluginTestHarness.StartAsync(new ClipboardPlugin(clipboard),
            BuiltinIds.ClipboardPlugin);
        clipboard.Emit(PluginTestHarness.ClipboardText("pin me"));

        await context.CommandRegistry.InvokeAsync(BuiltinIds.ClipboardCopyLatest,
            PluginTestHarness.ConsentCommand(BuiltinIds.ClipboardPlugin, BuiltinIds.ClipboardCopyLatest));
        Assert.Equal(["pin me"], clipboard.WrittenTexts);

        await context.CommandRegistry.InvokeAsync(BuiltinIds.ClipboardPinLatest,
            PluginTestHarness.ConsentCommand(BuiltinIds.ClipboardPlugin, BuiltinIds.ClipboardPinLatest));
        Assert.Equal("pin me", context.SettingsStore.GetString("clipboard.pinnedText", ""));
        Assert.Equal("pin me", context.SettingsStore.Saved["clipboard.pinnedText"]);

        await context.CommandRegistry.InvokeAsync(BuiltinIds.ClipboardClearHistory,
            PluginTestHarness.ConsentCommand(BuiltinIds.ClipboardPlugin, BuiltinIds.ClipboardClearHistory));
        Assert.Empty(clipboard.GetHistory());
        Assert.Contains(BuiltinIds.ClipboardPlugin, context.Publisher.Retracted);
    }
}

public sealed class NotificationsPluginTests
{
    [Fact]
    public async Task PublishesVerificationCodeFlag()
    {
        var notifications = new FakeNotificationSource();
        var (context, _) = await PluginTestHarness.StartAsync(new NotificationsPlugin(notifications),
            BuiltinIds.NotificationsPlugin);

        notifications.Emit(PluginTestHarness.Notification("654321"));
        var published = context.Publisher.LastPublished;
        Assert.Equal(IslandEventKind.Notification, published.Kind);
        Assert.Equal("654321", published.Payload!.VerificationCode);
        Assert.Equal("App", published.Payload.Subtitle);

        notifications.Emit(PluginTestHarness.Notification("654321")); // same id → no republish
        Assert.Equal(1, context.Publisher.Count);
    }

    [Fact]
    public async Task CopyCodeAndRequestAccessCommandsRoute()
    {
        var notifications = new FakeNotificationSource();
        var copied = new List<string>();
        var (context, _) = await PluginTestHarness.StartAsync(
            new NotificationsPlugin(notifications, code =>
            {
                copied.Add(code);
                return Task.FromResult(true);
            }), BuiltinIds.NotificationsPlugin);
        notifications.Emit(PluginTestHarness.Notification("111222"));

        await context.CommandRegistry.InvokeAsync(BuiltinIds.NotificationsCopyCode,
            PluginTestHarness.ConsentCommand(BuiltinIds.NotificationsPlugin, BuiltinIds.NotificationsCopyCode));
        Assert.Equal(["111222"], copied);

        await context.CommandRegistry.InvokeAsync(BuiltinIds.NotificationsRequestAccess,
            PluginTestHarness.ConsentCommand(BuiltinIds.NotificationsPlugin,
                BuiltinIds.NotificationsRequestAccess));
        Assert.Equal(1, notifications.RequestAccessCalls);
    }
}

public sealed class AgentStatusPluginTests
{
    [Fact]
    public async Task PublishesAgentEvent()
    {
        var inbox = new FakeAgentInboxSource();
        var installer = new FakeHookInstaller();
        var (context, _) = await PluginTestHarness.StartAsync(new AgentStatusPlugin(inbox, installer),
            BuiltinIds.AgentPlugin);

        inbox.Emit(PluginTestHarness.AgentEvent());
        var published = context.Publisher.LastPublished;
        Assert.Equal(IslandEventKind.Agent, published.Kind);
        Assert.Equal("Done", published.Title);
        Assert.Equal("claude", published.Payload!.Subtitle);
        Assert.Contains("claude:completed", published.Payload.DetailLines!);
    }

    [Fact]
    public async Task CapsulePrefersTheActionThenTheSessionAndNeverTheBrand()
    {
        var inbox = new FakeAgentInboxSource();
        var installer = new FakeHookInstaller();
        var (context, _) = await PluginTestHarness.StartAsync(new AgentStatusPlugin(inbox, installer),
            BuiltinIds.AgentPlugin);

        // 1) concrete tool action wins, 2) session/task name next.
        inbox.Emit(PluginTestHarness.AgentEvent("agent-action", title: "DeepSeek Harness · FluidBar",
            sessionTitle: "接手灵动岛项目重构与预览", action: "读取 src/App.tsx"));
        Assert.Equal("读取 src/App.tsx", context.Publisher.LastPublished.Title);

        inbox.Emit(PluginTestHarness.AgentEvent("agent-session", title: "DeepSeek Harness · FluidBar",
            sessionTitle: "接手灵动岛项目重构与预览"));
        Assert.Equal("接手灵动岛项目重构与预览", context.Publisher.LastPublished.Title);

        // 3) a brand-only title is dropped; the presenter shows the localized status instead.
        inbox.Emit(PluginTestHarness.AgentEvent("agent-brand", title: "DeepSeek Harness · FluidBar"));
        Assert.Equal(string.Empty, context.Publisher.LastPublished.Title);
    }

    [Fact]
    public async Task HookInstallNeverRunsAutomatically()
    {
        var inbox = new FakeAgentInboxSource();
        var installer = new FakeHookInstaller();
        var plugin = new AgentStatusPlugin(inbox, installer);
        var (context, _) = await PluginTestHarness.StartAsync(plugin, BuiltinIds.AgentPlugin);

        inbox.Emit(PluginTestHarness.AgentEvent());
        Assert.True(context.CommandRegistry.Handlers.ContainsKey(BuiltinIds.AgentInstallHooks));

        await plugin.StopAsync(CancellationToken.None);
        Assert.Equal(0, installer.InstallCalls);
        Assert.Equal(0, installer.UninstallCalls);
    }

    [Fact]
    public async Task HookInstallRequiresSettingsSurfaceAndExplicitConsent()
    {
        var inbox = new FakeAgentInboxSource();
        var installer = new FakeHookInstaller();
        var (context, _) = await PluginTestHarness.StartAsync(new AgentStatusPlugin(inbox, installer),
            BuiltinIds.AgentPlugin);

        await context.CommandRegistry.InvokeAsync(BuiltinIds.AgentInstallHooks,
            PluginTestHarness.ConsentCommand(BuiltinIds.AgentPlugin, BuiltinIds.AgentInstallHooks,
                source: CommandInvocationSource.Programmatic));
        Assert.Equal(0, installer.InstallCalls);

        await context.CommandRegistry.InvokeAsync(BuiltinIds.AgentInstallHooks,
            PluginTestHarness.ConsentCommand(BuiltinIds.AgentPlugin, BuiltinIds.AgentInstallHooks, consent: false));
        Assert.Equal(0, installer.InstallCalls);
        Assert.NotEmpty(context.NotificationSink.Posted);

        await context.CommandRegistry.InvokeAsync(BuiltinIds.AgentInstallHooks,
            PluginTestHarness.ConsentCommand(BuiltinIds.AgentPlugin, BuiltinIds.AgentInstallHooks));
        Assert.Equal(1, installer.InstallCalls);

        await context.CommandRegistry.InvokeAsync(BuiltinIds.AgentUninstallHooks,
            PluginTestHarness.ConsentCommand(BuiltinIds.AgentPlugin, BuiltinIds.AgentUninstallHooks));
        Assert.Equal(1, installer.UninstallCalls);
    }

    [Fact]
    public async Task StopReleasesInboxSubscription()
    {
        var inbox = new FakeAgentInboxSource();
        var plugin = new AgentStatusPlugin(inbox, new FakeHookInstaller());
        await PluginTestHarness.StartAsync(plugin, BuiltinIds.AgentPlugin);
        Assert.Equal(1, inbox.CountSubscribers());
        await plugin.StopAsync(CancellationToken.None);
        Assert.Equal(0, inbox.CountSubscribers());
    }
}

public sealed class DevicesPluginTests
{
    [Fact]
    public async Task ColdStartDoesNotReplayHistoryButLiveChangesPublish()
    {
        var usb = new FakeDeviceEventSource("system:usb");
        usb.Emit(PluginTestHarness.Device("usb-old"), primeOnly: true);
        var bluetooth = new FakeDeviceEventSource("system:bluetooth");
        var (context, _) = await PluginTestHarness.StartAsync(new DevicesPlugin(usb, bluetooth),
            BuiltinIds.DevicesPlugin);
        Assert.Equal(0, context.Publisher.Count);

        usb.Emit(PluginTestHarness.Device("usb-new"));
        Assert.Equal(1, context.Publisher.Count);
        var published = context.Publisher.LastPublished;
        Assert.Equal("system:usb", published.Payload!.Fields!["source"]);
        Assert.Equal("system:usb:connected", published.Content);

        usb.Emit(PluginTestHarness.Device("usb-new")); // duplicate signature
        Assert.Equal(1, context.Publisher.Count);
    }

    [Fact]
    public async Task BluetoothEventsCarrySourceTokenAndStopUnsubscribes()
    {
        var usb = new FakeDeviceEventSource("system:usb");
        var bluetooth = new FakeDeviceEventSource("system:bluetooth");
        var plugin = new DevicesPlugin(usb, bluetooth);
        var (context, _) = await PluginTestHarness.StartAsync(plugin, BuiltinIds.DevicesPlugin);

        bluetooth.Emit(PluginTestHarness.Device("bt-1", DeviceKind.Bluetooth));
        Assert.Equal("system:bluetooth", context.Publisher.LastPublished.Payload!.Fields!["source"]);
        Assert.Equal(1, bluetooth.CountSubscribers());

        await plugin.StopAsync(CancellationToken.None);
        Assert.Equal(0, bluetooth.CountSubscribers());
        Assert.Equal(0, usb.CountSubscribers());
    }
}

public sealed class BuiltinCatalogTests
{
    [Fact]
    public void CatalogHasTheFiveFirstPartyPlugins()
    {
        var ids = BuiltinPluginCatalog.Descriptors.Select(descriptor => descriptor.Id).ToArray();
        Assert.Equal(5, ids.Length);
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(BuiltinIds.MediaPlugin, ids);
        Assert.Contains(BuiltinIds.ClipboardPlugin, ids);
        Assert.Contains(BuiltinIds.NotificationsPlugin, ids);
        Assert.Contains(BuiltinIds.AgentPlugin, ids);
        Assert.Contains(BuiltinIds.DevicesPlugin, ids);
    }

    [Fact]
    public void ContributionsReferenceValidHostTemplatesAndSlots()
    {
        foreach (var descriptor in BuiltinPluginCatalog.Descriptors)
        {
            Assert.NotNull(descriptor.Contributions);
            foreach (var layout in descriptor.Contributions.Layouts ?? [])
            {
                Assert.Contains(layout.Template, LayoutTemplateCatalog.GetAllowedTemplates(layout.Surface));
                foreach (var (slot, binding) in layout.Slots ?? new Dictionary<string, LayoutSlotBinding>())
                {
                    Assert.True(LayoutTemplateCatalog.IsSlotAllowed(layout.Template, slot),
                        $"{descriptor.Id}/{layout.Id}: slot '{slot}' is not valid for {layout.Template}");
                    Assert.NotNull(binding.Field ?? binding.Literal);
                }
            }

            foreach (var command in descriptor.Contributions.Commands ?? [])
            {
                Assert.False(string.IsNullOrWhiteSpace(command.Id));
                Assert.NotNull(command.Title);
            }

            foreach (var page in descriptor.Contributions.SettingsPages ?? [])
            {
                Assert.False(string.IsNullOrWhiteSpace(page.Id));
                Assert.NotNull(page.Title);
            }

            foreach (var menuItem in descriptor.Contributions.ContextMenu ?? [])
            {
                Assert.Contains(descriptor.Contributions.Commands ?? [],
                    command => string.Equals(command.Id, menuItem.Command, StringComparison.Ordinal));
            }
        }
    }

    [Fact]
    public void FactorySkipsPluginsWhosePlatformDependenciesAreMissing()
    {
        Assert.Empty(BuiltinPluginCatalog.CreateAll(new BuiltinPlatformServices()));

        var instances = BuiltinPluginCatalog.CreateAll(new BuiltinPlatformServices
        {
            Clipboard = new FakeClipboardSource(),
        });
        Assert.Single(instances);
        Assert.Equal(BuiltinIds.ClipboardPlugin, instances[0].Descriptor.Id);
    }

    [Fact]
    public void FactoryCreatesAllPluginsWithFullPlatformServices()
    {
        var services = new BuiltinPlatformServices
        {
            Media = new FakeMediaSource(),
            Clipboard = new FakeClipboardSource(),
            Notifications = new FakeNotificationSource(),
            AgentInbox = new FakeAgentInboxSource(),
            AgentHookInstaller = new FakeHookInstaller(),
            UsbDevices = new FakeDeviceEventSource("system:usb"),
            BluetoothDevices = new FakeDeviceEventSource("system:bluetooth"),
        };
        Assert.Equal(5, BuiltinPluginCatalog.CreateAll(services).Count);
        Assert.NotNull(BuiltinPluginCatalog.Create(services, BuiltinIds.MediaPlugin));
        Assert.Null(BuiltinPluginCatalog.Create(services, "unknown-plugin"));
    }
}

public sealed class BuiltinLifecycleTests
{
    private sealed class ThrowingPlugin : BuiltinPluginBase
    {
        public override string PluginId => "throwing-plugin";

        protected override void OnStarted() => throw new InvalidOperationException("boom");
    }

    private sealed class RecordingPlugin : BuiltinPluginBase
    {
        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public override string PluginId => "recording-plugin";

        protected override void OnStarted() => StartCount++;

        protected override void OnStopped() => StopCount++;
    }

    [Fact]
    public async Task StartBeforeInitializeThrowsAndFailureIsIsolated()
    {
        var plugin = new RecordingPlugin();
        Assert.Throws<InvalidOperationException>(() => plugin.StartAsync(CancellationToken.None));
    }

    [Fact]
    public async Task FailedStartDoesNotPropagateAndIsLogged()
    {
        var throwing = new ThrowingPlugin();
        var context = new FakePluginContext("throwing-plugin");
        await throwing.InitializeAsync(context, CancellationToken.None);
        await throwing.StartAsync(CancellationToken.None); // must not throw
        Assert.NotEmpty(context.LogSink.Entries);
        await throwing.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task InitializeIsOnceAndStartStopAreIdempotent()
    {
        var plugin = new RecordingPlugin();
        var context = new FakePluginContext("recording-plugin");
        await plugin.InitializeAsync(context, CancellationToken.None);
        await plugin.InitializeAsync(new FakePluginContext("other"), CancellationToken.None);

        await plugin.StartAsync(CancellationToken.None);
        await plugin.StartAsync(CancellationToken.None);
        Assert.Equal(1, plugin.StartCount);

        await plugin.StopAsync(CancellationToken.None);
        await plugin.StopAsync(CancellationToken.None);
        Assert.Equal(1, plugin.StopCount);

        await plugin.DisposeAsync();
        await plugin.DisposeAsync();
        Assert.Equal(1, plugin.StopCount);
    }

    [Fact]
    public async Task DanglingCommandRegistrationFailureIsIsolated()
    {
        var clipboard = new FakeClipboardSource();
        var plugin = new ClipboardPlugin(clipboard);
        var context = new FakePluginContext(BuiltinIds.ClipboardPlugin)
        {
            CommandRegistry = { ThrowOnRegister = true },
        };
        await plugin.InitializeAsync(context, CancellationToken.None);
        await plugin.StartAsync(CancellationToken.None); // must not throw
        Assert.NotEmpty(context.LogSink.Entries);

        // Subscriptions still work even when command registration failed.
        clipboard.Emit(PluginTestHarness.ClipboardText("still works"));
        Assert.Equal(1, context.Publisher.Count);
    }
}
