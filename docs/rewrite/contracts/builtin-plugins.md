# FluidBar.Plugins.Builtin — first-party plugin contract (M5)

Status: **delivered** (branch `v2/rebuild`). Owner: platform-owner. Consumers: `FluidBar.App` (host
registration + rendering), tests: `FluidBar.BuiltinPlugins.Tests`.

`FluidBar.Plugins.Builtin` moves the v1 built-in feature set onto the frozen SDK so the plugin/marketplace
architecture is exercised by real first-party plugins. It is the **only** project allowed to reference
both `FluidBar.Sdk` and `FluidBar.Platform.Windows` (first-party special case; community `plugins/` remains
SDK-only). It references **no** App/Avalonia/WPF/Core types and contains **no UI code or XAML** — only SDK
data contracts + platform service adapters.

---

## 1. Plugins and platform dependencies

| Plugin | id | Category | Consumes (Platform interfaces) | SDK event |
|---|---|---|---|---|
| MediaPlugin | `fluidbar-media` | Media | `IMediaSessionSource`, `ArtworkTempStore`, optional `ILyricsLookup` | `IslandEventKind.Media` |
| ClipboardPlugin | `fluidbar-clipboard` | Productivity | `IClipboardSource` | `IslandEventKind.Auto` |
| NotificationsPlugin | `fluidbar-notifications` | System | `INotificationSource`, optional clipboard write (`Func<string,Task<bool>>`) | `IslandEventKind.Notification` |
| AgentStatusPlugin | `fluidbar-agent` | Developer | `IAgentInboxSource`, `IAgentHookInstaller` | `IslandEventKind.Agent` |
| DevicesPlugin | `fluidbar-devices` | System | `IDeviceEventSource` (USB and/or Bluetooth) | `IslandEventKind.Status` |

A plugin is only constructed when its required platform services exist; the rest are skipped (never
half-initialized). Missing permissions/hardware degrade silently.

## 2. Registration recipe (App)

```csharp
// 1. Build the platform sources (created/started by the App per docs/rewrite/contracts/platform.md).
var services = new BuiltinPlatformServices
{
    Media = mediaSessionService,             // GsmtcMediaSessionService
    Lyrics = lyricsService,                  // optional LyricsService
    ArtworkTempStore = new ArtworkTempStore(new ArtworkTempStoreOptions
    {
        DirectoryPath = Path.Combine(Path.GetTempPath(), "FluidBar", "art"),
    }),
    Clipboard = clipboardService,
    Notifications = notificationService,
    AgentInbox = agentInboxWatcher,
    AgentHookInstaller = agentHookInstaller,
    UsbDevices = usbMonitor,
    BluetoothDevices = bluetoothMonitor,
};

// 2. Read descriptors before constructing anything (UI needs contributions for settings/rail/menus).
foreach (var descriptor in BuiltinPluginCatalog.Descriptors)
{
    // descriptor.Id / DisplayName / Category / Capabilities / Contributions
}

// 3. Construct + run each plugin through the SAME SDK lifecycle the community lane uses.
var instances = BuiltinPluginCatalog.CreateAll(services);
foreach (var instance in instances)
{
    IPluginContext context = host.CreatePluginContext(instance.Descriptor); // host-owned (App)
    await instance.Plugin.InitializeAsync(context, cancellationToken);
    await instance.Plugin.StartAsync(cancellationToken);
}

// 4. Shutdown: StopAsync then DisposeAsync (both idempotent); stop platform sources afterwards.
```

The App maps the plugin events to its internal Core activity model; the first-party mapping is:
`fluidbar-media → media`, `fluidbar-clipboard → clipboard`, `fluidbar-notifications → notification`,
`fluidbar-agent → agent`, `fluidbar-devices → system:usb` / `system:bluetooth` (see §4 tokens).

## 3. Declarative contributions

All contributions are `FluidBar.Sdk.Contributions` descriptors backed by host templates; the plugin never
supplies a visual tree. Template/slot validity is enforced by
`LayoutTemplateCatalog.IsSlotAllowed` and covered by tests.

| Plugin | Layouts (`LayoutId` → surface/template) | Settings page | Commands |
|---|---|---|---|
| Media | `media.compact` → island.compact/`media.v1`; `media.hover` → hoverCard/`media.hover.v1` | `media.settings` (`media.showWave`, `media.showLyrics`) | `media.toggle-play-pause`, `media.next`, `media.previous` |
| Clipboard | `clipboard.compact` → island.compact/`text.v1` | `clipboard.settings` | `clipboard.copy-latest`, `clipboard.pin-latest`, `clipboard.clear-history` |
| Notifications | `notifications.compact` → island.compact/`notification.v1` | `notifications.settings` (permission info, verification-code toggle) | `notifications.copy-code`, `notifications.request-access`, `notifications.retract` |
| Agent | `agent.compact` → island.compact/`agent.v1` | `agent.settings` (install/repair/uninstall hook buttons) | `agent.install-hooks`, `agent.repair-hooks`, `agent.uninstall-hooks` |
| Devices | `devices.transient` → island.compact/`status.v1` | `devices.settings` (USB/BT toggles) | `devices.retract` |

Plus context-menu items (`BuiltinContributions.*.ContextMenu`) for the relevant commands and a media
`wave.v1` accessory widget. Stable ids live in `BuiltinIds` (UI/tray/menu code must use those constants).

## 4. Event payload mapping (stable field keys)

Events set `Source = plugin id` (SDK rule) and a `Fields` dictionary the host layout templates bind to.
Stable field keys used by the layouts:

| Plugin | Fields | Notes |
|---|---|---|
| Media | `icon`, `title`, `subtitle`, `badge` (`playing`/`paused`), `progress` (0..1 invariant string), `wave` (`on`/`off`), `artwork` (path), `body` (current lyric), `actions` | `Payload` also carries `Subtitle/SourceName/Progress/IsActive/AlbumArtPath/LyricLines/PlaybackTicks`. Artwork path points into the bounded `ArtworkTempStore` (SHA-256-named, byte/retention capped). |
| Clipboard | `icon` (kind token), `title` (preview), `preview`, `badge` (history count), `kind`, `actions` | file drops expose `DetailLines` |
| Notifications | `icon`, `title`, `body`, `code`, `actions` | `Payload.VerificationCode`/`Badge` carry the 6-digit code; permission-denied state publishes a `Status` event with `Title=notification-access`, `Subtitle=<state>` |
| Agent | `Status` event: `icon=agent`, `title`, `subtitle` (localized status), `badge` (`running`/`completed`/`failed`/`waiting`), `body`; page is a plugin-owned `builtin.agent` list page | `PageListRow` carries icon/title/project/status/duration; no host agent model |
| Devices | `icon`, `title`, `source` (`system:usb`/`system:bluetooth`), `badge` (kind), `device` | `Content` = `<sourceToken>:<connected|disconnected>`; cold start primes history without replaying it |

Transport buttons: media `actions` field holds the command id list the host renders as buttons
(`media.previous,media.toggle-play-pause,media.next`).

## 5. Lifecycle, isolation and consent rules

- **SDK lifecycle only**: `InitializeAsync` once, `StartAsync`/`StopAsync` per activation,
  `DisposeAsync` on unload. All are idempotent; `StartAsync` before `InitializeAsync` throws (host bug).
- **No host crash**: a failed start or a throwing command/subscription is caught, logged via
  `IPluginContext.Log`, partial subscriptions are disposed and the plugin stays stopped. Tests cover this.
- **No leaks**: every platform event subscription and command registration is tracked and disposed on
  stop (tests assert subscriber counts return to 0).
- **No UI-thread blocking**: plugins only subscribe to platform events (already off-UI), publish through
  the non-blocking `IIslandPublisher`, and run async work via `Task.Run` bounded by
  `IPluginContext.HostStopping`.
- **Consent-gated agent hooks**: `agent.*-hooks` handlers call `IAgentHookInstaller` only when invoked with
  `PluginCommandContext.Source == CommandInvocationSource.Settings` **and** argument
  `consent == "true"`. Any other invocation logs a warning and posts `agent-hooks-consent-required`; there
  is no timer and no startup/repair path. Tests assert the installer is never invoked automatically.

## 6. App-side platform interface additions (additive, non-breaking)

`FluidBar.Platform.Windows` now also exposes small plugin-facing interfaces (implemented by the existing
services): `IMediaSessionSource`, `ILyricsLookup`, `IClipboardSource`, `INotificationSource`,
`IAgentInboxSource`, `IAgentHookInstaller`, `IDeviceEventSource`, plus `ArtworkTempStore` (bounded temp
materialization for `AlbumArtPath`). No service behavior changed; the dedicated services remain the
implementation and keep their own lifecycle.

## 7. Verification

```powershell
dotnet build src/FluidBar.Plugins.Builtin/FluidBar.Plugins.Builtin.csproj   # 0 errors, 0 warnings
dotnet test  tests/FluidBar.BuiltinPlugins.Tests/FluidBar.BuiltinPlugins.Tests.csproj
#   → 23 passed, 0 failed, 0 skipped
dotnet test  tests/FluidBar.Platform.Tests/FluidBar.Platform.Tests.csproj    # 341 passed (no regression)
```

Coverage: adapter mappings with fake platform sources (media/clipboard/notifications/agent/devices),
change-only coalescing/dedupe, artwork materialization path, lyrics attachment, transport/command routing,
lifecycle idempotency + subscription cleanup, failed-start isolation, catalog skip/complete construction,
contribution template/slot validity, and the consent gate (installer never auto-runs).
