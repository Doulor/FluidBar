# Cross-project integration seam contract (M7 groundwork)

| Item | Value |
|---|---|
| Project | `tests/FluidBar.Integration.Tests` (`net10.0-windows10.0.19041.0`) |
| Command | `dotnet test tests/FluidBar.Integration.Tests` |
| Status | **31 deterministic tests, green in <1 s** (budget <30 s, no hardware, no network) |
| Owner | core-owner (seam tests), Lead integrates |
| References | Core · Sdk · PluginHost · Marketplace · Plugins.Builtin · Platform.Windows · sample plugin (build-time only) |
| Shared fixture | `../FluidBar.Soak/Fixtures/media-session.json` (linked, `fluidbar.event-recording.v1`) |

This suite proves the **cross-project seams** of the v2 architecture without launching UI or touching
real Windows services: every platform dependency is a fake interface implementation, time is a
virtual `TimeProvider`, and the Core pipeline runs with a manual dispatcher. It is the executable
form of the mapping contract the App must implement.

```
platform snapshots      built-in plugins                 SDK contract                 App mapping (tested here)
──────────────────      ─────────────────                 ────────────                 ─────────────────────────
IMediaSessionSource  →  MediaPlugin      ─┐
IClipboardSource     →  ClipboardPlugin   │  SDK IslandEvent     SdkCoreEventAdapter
INotificationSource  →  NotificationsPlugin├─────────────────→  (plugin id/kind/icon/fields
IAgentInboxSource    →  AgentStatusPlugin │  + commands + layouts    → Core kind + source token +
IDeviceEventSource   →  DevicesPlugin    ─┘                          payload/event-kind mapping)
                                                                          ↓
                                       Core EventHub → ActivityScheduler → IslandStateMachine
                                                     → ActivityPresenter (IslandViewModel)
```

---

## 1. Seams covered

| # | Seam | Test file | What is proven |
|---|---|---|---|
| 1 | Platform snapshot → built-in plugin → SDK event | `PlatformToSdkSeamTests` (7 tests) | Fake platform services → real `BuiltinPluginCatalog.CreateAll` → real SDK lifecycle → captured SDK events; media progress/wave, clipboard preview + copy/pin/clear commands, notification verification code + copy command, agent token/waiting flag, device `system:usb`/`system:bluetooth` tokens, clean subscribe/unsubscribe |
| 2 | SDK event → Core pipeline → presentation | `SdkToCorePipelineTests` (12 tests) | Adapter mapping → EventHub coalescing → scheduler priority/replacement/stack/expiry → `IslandViewModel`; notification > media, clipboard preempts transient notification, same-key replacement, incremental coalescing, TTL expiry back to idle, device/generic mapping |
| 3 | Agent hook consent | `AgentConsentTests` (4 tests) | No install on start; non-consented commands ignored + warning notification; only `settings` surface + `consent=true` reaches `IAgentHookInstaller` |
| 4 | v1 settings → migration → projection | `SettingsSeamTests` (3 tests) | v1 JSON → `SettingsStore.Load` → `MigratedFromV1` (mapped/dropped report) → `SettingsProjection` policies → export/import round-trip |
| 5 | Marketplace install → host load → SDK event → unload | `MarketplaceSeamTests` (2 tests) | Built sample plugin → `.fbp` via production packer → offline local install → real `PluginHost` collectible-ALC load → captured SDK event → unload with `Collected == true`; reload after unload |
| 6 | Soak recording replay | `RecordingReplaySeamTests` (3 tests) | `media-session` fixture validates and replays deterministically through the same Core pipeline; app-pipeline snapshots equal `EventRecordingReplay` snapshots |

**Not covered here (by design):** UI rendering/Avalonia, real Win32/WinRT/GSMTC/clipboard/notification
APIs, real marketplace network fetch, multi-monitor/DPI placement, plugin process isolation (v2.1).
Those live in UI/Platform/PluginHost test suites.

---

## 2. Fake strategy

All seams are driven by hand-written fakes stored in `TestSupport/`:

| Fake | Implements | Behaviour |
|---|---|---|
| `FakeMediaSource` | `IMediaSessionSource` | `Emit(MediaSnapshot)` raises `SnapshotChanged`; records transport commands; real `ArtworkCache` instance |
| `FakeClipboardSource` | `IClipboardSource` | bounded `ClipboardHistory`; records `SetTextAsync` writes |
| `FakeNotificationSource` | `INotificationSource` | emits `NotificationsSnapshot` with `NotificationEntry` (verification code already detected by the production detector) |
| `FakeAgentInboxSource` | `IAgentInboxSource` | emits `AgentEventSnapshot` |
| `FakeHookInstaller` | `IAgentHookInstaller` | counts install/uninstall calls; never auto-called |
| `FakeDeviceEventSource` | `IDeviceEventSource` | USB/Bluetooth sources with stable `SourceToken` |
| `TestPluginContext` | `IPluginContext` | test-owned host context: recording publisher/commands/settings/notifications/log, inline dispatcher, no-op leases, virtual `TimeProvider` |
| `RecordingPublisher` | `IIslandPublisher` | captures SDK events + retracts |
| `TestTimeProvider` | `TimeProvider` | virtual clock for TTL/expiry and lease timeouts |
| `ManualTestDispatcher` | `IDispatcher` | deterministic core event-hub pumping |

Rules: no real timers, no `Thread.Sleep` for ordering, no network, no P/Invoke, no `%APPDATA%`
mutation (temp dirs + in-memory file system only).

---

## 3. SDK ↔ Core mapping contract (what the App must implement)

The test adapter `TestSupport/SdkCoreEventAdapter.cs` is the reference implementation. The App must
provide an equivalent bridge (it may live in `FluidBar.App`, reusing the same rules).

### 3.1 Activity kind + stable source token

| SDK event | Core `ActivityKind` | Core source token |
|---|---|---|
| `Source == builtin-media` (`fluidbar-media`) | `Media` | `media:gsmtc` |
| `Source == builtin-clipboard` | `Clipboard` | `clipboard:host` |
| `Source == builtin-notifications` | `Notification` | `notif:toast` |
| `Source == builtin-agent`, field `agent=claude`/`codex` | `Agent` | `agent:claude` / `agent:codex` |
| `Source == builtin-devices`, field `source=system:usb` | `Device` | `system:usb` |
| `Source == builtin-devices`, field `source=system:bluetooth` | `Bluetooth` | `system:bluetooth` |
| any other plugin | by `Kind`/`IconKind` (see 3.2) | `plugin:<pluginId>` (stable, collision-free) |

The SDK event `Source` stays the plugin id (host-enforced). The **Core source token** is the
scheduler identity; never use the raw plugin id for built-in plugins or media TTL/coalescing breaks.

### 3.2 Generic plugin classification

`Kind`: `Media→Media`, `Notification→Notification`, `Agent→Agent`, `Progress→Progress`.
Otherwise icon token: `media/clipboard*/notification/agent/usb/bluetooth/volume/brightness/lockkey/
battery/timer/progress|update`; fallback `Notification` (generic transient text surface).

### 3.3 Event kind (scheduler semantics)

| Situation | Core `IslandEventKind` |
|---|---|
| First event for a core key | `New` |
| Subsequent media event for the same track (title|subtitle unchanged) | `Incremental` (hub coalesces progress/lyrics) |
| Media track change (title/artist different) | `New` (mode-change replacement) |
| Clipboard/notification/agent/device event | `New` (same-key in-place replacement; TTL refresh) |
| Plugin retract (`IIslandPublisher.TryRetract`) | Core `IslandEvent.Retract(key)` |

Sequence numbers are monotonic **per core key** (the adapter owns them); timestamps are producer
clock relative to a fixed origin.

### 3.4 Payload mapping (`ActivityPayload`)

| SDK `IslandEventPayload` | Core `ActivityPayload` |
|---|---|
| `Title` / `Content` | `Title` / `Body` |
| `Subtitle`, `Badge`, `SourceName` | same |
| `Progress` (0..1) | `ProgressPercent` = clamp(round(progress×100), 0, 100) |
| `IsActive` | `IsActive` |
| `AudioWave` present, or media + `IsActive` | `ShowsAudioWave` |
| `AlbumArtPath`, `AppIconPath` | same |
| `LyricLines[0]`, `LyricLines[1]` | `LyricLine`, `SecondaryLyricLine` |
| `DetailLines` | `DetailLines` (bounded by SDK limits) |
| `PlaybackTicks` | `PositionTicks` |
| `VerificationCode` | `VerificationCode` |
| `Fields` | consumed for identity only (`agent`, `source`); layouts/templates are rendered by the UI, not Core |

### 3.5 Known gaps the App/UI must own

1. The SDK payload has no track **duration/end ticks**, so the Core media progress derives only from
   the explicit `Progress` field (the platform adapter gets duration from `MediaSnapshot` — if the App
   wants tick-accurate interpolation it must add `EndTicks` to the SDK payload in a future SDK minor).
2. SDK `LayoutId`/`Fields` map to **declarative UI templates**; Core only stores content slots. The UI
   resolves the layout via the contribution registry.
3. Plugin `PreferredPage` is inferred from kind (media→Media page, notification→Notifications, …); the
   SDK has no explicit page field.
4. Retract: `TryRetract(source)` → Core `Retract(key)` for that plugin's current core key. The App
   keeps the SDK-source→core-key map.

---

## 4. Consent contract (agent hooks)

`AgentStatusPlugin` is deliberately inert: **starting the plugin never installs hooks**, and no timer
exists. `BuiltinIds.AgentInstallHooks` / `RepairHooks` / `UninstallHooks` are accepted only when
`AgentStatusPlugin.IsConsentGranted(context)` is true:

* `context.Source == CommandInvocationSource.Settings`, **and**
* `context.Arguments["consent"] == "true"` (case-insensitive).

Anything else logs a warning and posts an `agent-hooks-consent-required` host notification; the
platform `IAgentHookInstaller` is not touched. Tests cover start, denied command, wrong surface with
consent, and the allowed install/repair/uninstall flow.

---

## 5. Marketplace → host seam

Test flow (offline, no marketplace client/network):

1. `FbpStagingBuilder.FromPluginOutput(<sample plugin build output>)` → `Build(sample-minimal.fbp)`
   (production packer, deterministic `.fbp` layout + checksums).
2. `PluginInstallService(new PluginInstallOptions { PluginRootDirectory = temp, Activator = test
   activator, RequireMarketplaceSignature = false, AllowUnsignedLocalInstall = true,
   AppVersion = 2.0.0 }).InstallFromFileAsync(package, signature: null, allowUnsigned: true)`.
   Production keeps `RequireMarketplaceSignature = true`; unsigned local install is an explicit UI
   choice (unverified warning).
3. `PluginHost(new PluginHostOptions { PluginRootDirectory = temp, AppVersion = 2.0.0,
   IslandPublisherFactory = manifest => appPublisher, ... })` → `LoadAsync("sample-minimal")`.
   The App wires `IslandPublisherFactory` to the SDK→Core bridge (the same adapter as §3).
4. `UnloadAsync` must return `Collected == true` (collectible ALC); a subsequent `LoadAsync` works.

The integration project references the sample plugin with `ReferenceOutputAssembly="false"` so the
DLL is built but always loaded through the real host/ALC path, never as a test reference.

---

## 6. Settings seam

The `SettingsSeamTests` v1 fixture covers the migration table end-to-end:
`Position→anchor+placement`, `Opacity→fillOpacity`, `Theme`, `AccentColor→system accent`,
`AutoHideDelayMs→autoCollapseMs`, `DisplayStrategy→stacking`, `HoldToHideKey`, `HideTrayIcon`,
`AutoUpdateCheck`, `ClipboardEnabled`, `PluginEnabled→plugins.enabled/orphaned`,
`MonitorFeatureSettings→activity settings`; dead fields are reported in
`MigrationReport.Dropped` (`CornerRadius`, `UnknownFutureField`, …) rather than silently ignored.
The migrated tree is then projected through `SettingsProjection` (`ToBehaviorOptions`,
`ToStackPolicy`, `ToMotionProfile`, `ToLayoutRequest`) and exported/imported via
`SettingsStore.Export/Import` (envelope `fluidbar.settings-export.v1`; import returns a preview +
diff, never writes).

App wiring: `SettingsStore` with the real `ISettingsFileSystem` (Platform/App-owned) →
`SettingsProjection` on load/change → pass policies to the Core pipeline/reducer; never read
settings from inside Core.

---

## 7. Recording fixture seam (UI tests can consume the same way)

The soak recording contract is linked from `tests/FluidBar.Soak/Recording/**` and the
`media-session` fixture is embedded:

```xml
<Compile Include="..\FluidBar.Soak\Recording\**\*.cs" Link="Soak\Recording\%(RecursiveDir)%(Filename)%(Extension)" />
<EmbeddedResource Include="..\FluidBar.Soak\Fixtures\media-session.json" Link="Fixtures\media-session.json" />
```

Tests replay the fixture two ways: `EventRecordingReplay.Validate` (fixture self-check) and through
`CorePipelineHarness` (App-shaped pipeline) asserting the resulting `IslandViewModel` equals the
recorded snapshots. UI tests can link the same files or reference `FluidBar.Soak` (see
`docs/rewrite/contracts/soak.md` §4.4).

---

## 8. How to extend

1. **New platform source** → add a fake in `TestSupport/PlatformFakes.cs`; add it to
   `FakeSnapshots.Services`; write a seam-1 test asserting the SDK event and command ids.
2. **New SDK event shape** → extend `SdkCoreEventAdapter` mapping + §3 table + a seam-2 test.
   Keep the adapter pure (no UI/Platform types) so it can move into App/Core later.
3. **New plugin kind** → update the built-in id → Core source-token table and priority expectations.
4. **New fixture** → add JSON under `tests/FluidBar.Soak/Fixtures`, embed/link here, add a
   deterministic replay test.
5. Keep everything offline: no real timers/network/P/Invoke; temp directories only; `TestTimeProvider`
   for time; `ManualTestDispatcher` for pumping.

CI: `dotnet test tests/FluidBar.Integration.Tests` after building the solution; the sample plugin is
built via a build-only `ProjectReference`. Current runtime ~0.6 s (budget <30 s); suite is
deterministic across runs.
