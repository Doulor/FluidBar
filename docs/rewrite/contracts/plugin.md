# FluidBar v2 — Plugin platform contract

**Audience:** App/UI integrators, built-in plugin authors, marketplace/CI contributors.
**Status:** M2 implementation contract (frozen SDK surface = `FluidBar.Sdk` apiVersion 2.0).
**Code:** `src/FluidBar.Sdk`, `src/FluidBar.PluginHost`, `src/FluidBar.Marketplace`, `src/FluidBar.Cli`,
`plugins/samples/*`, `marketplace/`, root `schemas/`.
**Tests:** `tests/FluidBar.Sdk.Tests` (manifest/packaging/signing/index/install/i18n),
`tests/FluidBar.PluginHost.Tests` (loader, unload evidence, services, end-to-end package → load).

> **Security statement (must appear verbatim in marketplace UI copy):**
> *An in-process plugin runs with the same access as FluidBar. Manifest capabilities are disclosed for your
> consent; they are not a sandbox. We review and sign the plugins we publish — install plugins you trust.*
> The ALC is a version/lifetime boundary, **not** a permission boundary.

---

## 1. Assemblies and dependency rules

| Project | TFM | References | Must not |
|---|---|---|---|
| `FluidBar.Sdk` | `net10.0` | BCL only | Core/App/host/WPF/Avalonia/Win32, third-party packages |
| `FluidBar.PluginHost` | `net10.0` | Sdk, Core | App, UI frameworks |
| `FluidBar.Marketplace` | `net10.0` | Sdk, Core | App, UI frameworks; network only through injected `HttpMessageHandler`/`HttpClient` |
| `FluidBar.Cli` (`fluidbar-plugin`) | `net10.0` | Sdk, Marketplace, PluginHost | network for `validate`/`pack`/`policy` |
| `FluidBar.Plugins.Builtin` | compile-time built-ins | Sdk | — |
| `plugins/samples/*` | `net10.0` | **Sdk only**, `Private=false`, `EnableDynamicLoading=true` | Core/App/UI frameworks |

The SDK is deliberately UI-free so the same contract can later be served over IPC (v2.1 out-of-process lane).
Built-in plugins are registered in the App and share the same contribution registry; community plugins load
through a collectible ALC.

---

## 2. SDK surface (frozen v2.0)

Namespace `FluidBar.Sdk`:

```csharp
public interface IFluidBarPlugin : IAsyncDisposable
{
    ValueTask InitializeAsync(IPluginContext context, CancellationToken cancellationToken); // 5 s timeout
    ValueTask StartAsync(CancellationToken cancellationToken);                            // 5 s timeout
    ValueTask StopAsync(CancellationToken cancellationToken);                             // 5 s timeout
}
```

Lifecycle rules enforced by the host:

1. `InitializeAsync` is called once; `StartAsync` once per activation; `StopAsync` before deactivation;
   `DisposeAsync` on unload. The host **never** calls `StopAsync` if `StartAsync` did not complete.
2. Every call is async, cancellable and bounded by a host timeout (default 5 s). On timeout the host cancels
   the passed token and isolates the plugin.
3. Exceptions never escape into the App: they are logged, counted and the plugin is marked `Failed` / after
   `MaxConsecutiveFailures` (default 3) `Disabled`. The host itself always survives.
4. `StopAsync`/`DisposeAsync` must be idempotent, fast and release timers/threads/subscriptions. The loader
   has a disposal safety net (context leaks + `RegisterForDisposal`) but **cannot fix an undisposed timer or
   thread**; such a plugin is detected at unload (`RestartRequired`) and stays loaded until restart.
5. Plugins must not block, must not call `Environment.Exit`/`Process.GetCurrentProcess().Kill`, and should
   observe `IPluginContext.HostStopping`.

### IPluginContext services

| Member | Contract |
|---|---|
| `PluginId`, `ApiVersion`, `PackageDirectory`, `DataDirectory` | identity/paths; `PackageDirectory` read-only, `DataDirectory` = `$PLUGIN_DATA` |
| `Islands : IIslandPublisher` | `bool TryPublish(IslandEvent)`, `bool TryRetract(string source)`; non-blocking, may drop; event source must equal plugin id; bounded by `IslandEventLimits` |
| `Localization : IPluginLocalization` | `Get`, `TryGet`, `Format(key, args)`, `Plural(key, count)`; fallback chain in §7; host owns culture |
| `Settings : IPluginSettings` | host-owned `%APPDATA%\FluidBar\plugins\<id>\settings.json`, typed getters/setters, `Subscribe(Action<string>)` (dispose to unsubscribe), `SaveAsync`; atomic writes; schema-validated by manifest controls |
| `Storage : IPluginStorage` | per-plugin data dir; relative paths only, `..`/absolute rejected; atomic text/bytes writes; bounded reads |
| `Log : IPluginLog` | structured levels; per-plugin log file sink; never throws |
| `Dispatcher : IHostDispatcher` | `CheckAccess`, `TryPost`, `InvokeAsync`; the only way to reach the UI thread |
| `Leases : IPluginLeases` | `CreateLease(name)` → `IPluginLease` owning `ScheduleOnce`/`SchedulePeriodic`; non-overlapping timers; disposed on stop/unload; lease limits (32 leases, 64 schedules) |
| `Commands : ICommandRegistry` | `Register(commandId, handler)` only for manifest-declared commands; duplicate/undeclared → `InvalidOperationException`; dispose to unregister |
| `Notifications : INotificationSink` | host-rendered notifications; bounded/validated; no plugin windows |
| `GrantedCapabilities` | declared/consented capability set (`PluginCapability` enum) — advisory in-process |
| `Time` | `TimeProvider` (tests virtualize time) |
| `HostStopping` | cancelled during unload/App shutdown |
| `RegisterForDisposal(Action)` | LIFO disposal by the loader after Stop/Dispose; max 64 resources |

**No static events in the SDK.** Cross-plugin/host notifications are context-scoped: settings change via
`Settings.Subscribe`, host nullability not exposed. This removes the classic "static event leaks the ALC" bug
proven in the ALC spike (`docs/rewrite/research/06-plugin-alc-spike.md`).

### Island event abstraction

`IslandEvent` (SDK) is the plugin-facing data contract; Core keeps its own internal event/activity model and
the **App adapts between them** (SDK `IslandEvent` → Core `IslandEvent`/`IslandEventHub`). Fields:
`Source`, `Title`, `Content`, `IconKind`, `Kind` (`Auto/Text/ScrollingText/Progress/Status/Media/Notification/Agent`),
`LayoutId`, `Payload` (`Subtitle`, `Badge`, `SourceName`, `Progress` 0..1, `IsActive`, `AudioWave`, `AlbumArtPath`,
`AppIconPath`, `LyricLines`, `DetailLines`, `PlaybackTicks`, `VerificationCode`, `Fields` map for declarative slots).
Host drops invalid events (limits: source ≤64, title ≤256, content ≤2048, 32 lines, 128 wave samples, 64 fields)
and logs once per source (bounded).

---

## 3. Manifest (`manifest.json`, schemaVersion 2)

Normative JSON Schema: [`schemas/plugin.schema.json`](../../../schemas/plugin.schema.json); strict parser rejects
unknown properties. Required: `schemaVersion`, `id`, `version`, `apiVersion`, `minAppVersion`, `publisher`, `name`,
`summary`, `license`, `entry`, `locales`, `capabilities`, `contributes`.

Key blocks:

```jsonc
{
  "id": "sample-rich",                     // ^[a-z0-9]([a-z0-9-]{0,38}[a-z0-9])?$ ; reserved ids rejected
  "version": "1.0.0", "apiVersion": "2.0.0", "minAppVersion": "2.0.0", "maxAppVersion": null,
  "publisher": { "id": "fluidbar-samples", "displayName": "...", "url": "https://...", "keyId": "..." },
  "name": { "en-US": "...", "zh-Hans": "..." },   // LocalizedString: string or locale map
  "summary": { ... }, "description": { ... }, "license": "MIT", "repository": "https://...",
  "categories": ["media"], "tags": ["sample"], "icon": "assets/icon.png" /* or {light,dark,monochrome} */,
  "entry": { "runtime": "inproc", "assembly": "plugin/FluidBar.Plugins.SampleRich.dll",
             "type": "FluidBar.Plugins.SampleRich.RichPlugin", "targetFramework": "net10.0" },
  "platforms": ["win-x64"],
  "locales": { "default": "en-US", "available": ["en-US","zh-Hans"], "path": "locales", "fallback": "en-US" },
  "capabilities": { "network": { "domains": ["api.example.com"], "methods": ["GET"], "reason": "..." },
                    "clipboard": { "read": false, "write": true }, "files": { "read": ["$PLUGIN_DATA"], "write": ["$PLUGIN_DATA"], "reason": "..." },
                    "media": { "sessions": true, "control": true, "reason": "..." },
                    "notifications": { "read": false, "post": true },
                    "shell": { "open": false, "execute": false, "reason": "..." },
                    "hotkeys": { "global": ["Ctrl+Alt+M"], "reason": "..." },
                    "process": { "spawn": false, "reason": "..." }, "registry": { "read": [], "write": [], "reason": "..." },
                    "telemetry": { "enabled": false, "endpoint": null, "reason": "..." },
                    "background": { "intervalMinMs": 60000, "reason": "..." },
                    "ui": { "island": true, "hoverCard": true, "settings": true, "contextMenu": true,
                            "commands": true, "widgets": true, "xaml": false } },
  "activation": ["startup", "onCommand:sample-rich.refresh"],
  "contributes": { "layouts": [...], "settingsPages": [...], "commands": [...], "contextMenu": [...], "widgets": [...] },
  "dependencies": { "sdk": "^2.0.0", "plugins": [{ "id": "media-core", "range": "^1.2.0", "optional": false }] },
  "settings": { "schema": "settings.schema.json", "defaults": "settings.defaults.json", "storage": "host" },
  "integrity": { "entrySha256": "<64 lowercase hex>", "files": "checksums.json" },
  "signature": { "scheme": "detached-v1", "algorithm": "ECDSA-P256-SHA256",
                 "publisherKeyId": "...", "signatureFile": "signature.json" }
}
```

Validator rules beyond JSON Schema (`PluginManifestValidator`): semver validity, apiVersion major supported by
host, `maxAppVersion >= minAppVersion`, id not reserved/mismatched with folder, publisher id/displayName/keyId,
default-locale translation present in every localized string, `locales.default`/`fallback` ∈ `available`,
entry path safety (`plugin/…`, no `..`, `.dll`), TFM net10.0+, forbidden runtime `outofproc` (until v2.1),
risky capabilities require a `reason` (`network`, `files`, `shell`, `process`, `registry`, `hotkeys`; wildcard
network / process.spawn / registry.write / shell.execute produce security warnings), `ui.xaml: true` rejected
for marketplace plugins, contribution ids namespaced under the plugin id and unique, slot names valid for their
template (`LayoutTemplateCatalog`), slot/command/context-menu/button command references must exist, layout
constraint ranges, settings control type-specific requirements (slider min/max/step, select options/default,
button command, validation regex/timeouts bounded), `dependencies.sdk` range satisfiable, integrity/signature
fields valid.

### Capabilities = consent, not enforcement

`PluginCapability` enum: `Network, Clipboard, Files, Media, Notifications, Shell, Hotkeys, Process, Registry,
Telemetry, Background, Ui`. `CapabilityConsentBuilder` (Marketplace) turns a manifest into plain-language
consent rows (`TitleKey`, `DescriptionKey`, high-risk flag, `reason`, concrete details like domains/hotkeys/
scopes). The UI must show the security statement above and must not use "sandbox", "limited permissions" or
"reviewed = safe" language.

---

## 4. Declarative contributions (data only — never plugin XAML)

All UI is host-rendered with host theme/localization/accessibility. Plugins supply descriptors that reference
host templates and named slots (validated by `LayoutTemplateCatalog`).

- **Layouts:** `id`, `surface` (`island.compact` | `island.expanded` | `hoverCard`), `template`
  (`text.v1`, `status.v1`, `progress.v1`, `media.v1`, `media.hover.v1`, `notification.v1`, `agent.v1`,
  `clock.v1`, `list.v1`), `slots` (`field` | `literal` | `commands`; kind/maxLines/style), `constraints`
  (DIP bounds). Limit 12.
- **Settings pages:** host-rendered, declared as `settingsPages[]` with an optional flat `controls[]` (legacy,
  rendered first as an implicit "General" group) and/or titled `groups[]` (`{ id, title, description?,
  controls[] }`). Control types: `toggle`, `select`, `slider`, `number`, `text`, `path`, `secret`, `button`,
  `link`, `info`, and read-only `status` rows (`{ key, format: text|number|boolean|timestamp, unit?, command? }`;
  the plugin writes the value into the host settings store and the host re-renders it on change). Titles /
  descriptions are `LocalizedString` (locale map or message-key form). Limits: 8 pages/plugin, 8 groups/page,
  20 controls/group, 40 controls/page; control keys unique per plugin (`^[A-Za-z][A-Za-z0-9.]{0,63}$`).
  Defaults/limits/validation are host-enforced; secrets are stored with DPAPI(CurrentUser) and never re-shown.
  The final renderer contract is documented in [`docs/plugin/declarative-ui.md`](../../plugin/declarative-ui.md).
- **Commands:** IDs only; handlers are bound at runtime via `context.Commands.Register`; missing handler =
  disabled command, never a crash. Contexts: `global`, `island`, `contextMenu`, `commandPalette`, `settings`.
- **Context menu:** `command` reference + restricted `when` predicate grammar (`source == 'x' && hasTrack`,
  `&&`, `||`, `!`, parentheses; no code evaluation). Limit 16.
- **Widgets:** accessory templates (`wave.v1`, `badge.v1`, `icon.v1`, `text.v1`) in host slots; never
  independent windows. Limit 8.
- Disabling a plugin removes **all** its contributions; the host must not cache them.
- Slots get data from `IslandEvent.Payload` well-known fields or the `Fields` dictionary.

---

## 5. Loader contract (`FluidBar.PluginHost`)

```csharp
var host = new PluginHost(new PluginHostOptions {
    PluginRootDirectory = Path.Combine(appData, "FluidBar", "plugins"),
    AppVersion = new SemanticVersion(2, 0, 0),
    Logger = myHostLogger,                                // IPluginHostLogger
    Dispatcher = myUiDispatcher,                          // IHostDispatcher (App injects)
    IslandPublisherFactory = manifest => coreAdapter,     // App adapts SDK events to Core
    NotificationSinkFactory = manifest => notificationService,
});
var results = await host.LoadAllAsync();                   // one failure never stops the rest
var handle  = host.Find("sample-rich");
var command = await host.ExecuteCommandAsync("sample-rich", "sample-rich.refresh");
var unload  = await host.UnloadAsync("sample-rich");       // unload.Collected / RestartRequired
```

Uses: `Plugins`, `Find`, `DiscoverAsync`, `LoadAllAsync`, `LoadAsync`, `LoadFromDirectoryAsync` (dev/offline),
`StopAsync` (disable), `StartAsync` (re-enable), `UnloadAsync`, `ReloadAsync`, `ExecuteCommandAsync`,
`StateChanged` event, `DisposeAsync`.

### Directory layout & discovery

```
%APPDATA%\FluidBar\plugins\<id>\manifest.json                 flat/dev install
%APPDATA%\FluidBar\plugins\<id>\<version>\manifest.json       marketplace install (Windows locks loaded DLLs)
%APPDATA%\FluidBar\plugins\<id>\active.json                   {"activeVersion":"1.2.3"} atomic pointer
%APPDATA%\FluidBar\plugins\<id>\state.json                    install state (last good, publisher pin, hashes)
%APPDATA%\FluidBar\plugins\<id>\settings.json                 host-owned settings
%APPDATA%\FluidBar\plugins\<id>\data\                         $PLUGIN_DATA (version independent)
```

- Flat layout wins when `<id>/manifest.json` exists; otherwise version directories are scanned, selected by
  `active.json` then highest version (dangling pointer → warning + highest).
- Hidden dirs (`.staging`) and symlinks/reparse points are rejected; manifest id must equal the `<id>` folder;
  version dir name must equal `manifest.version`; entry path is resolved and must stay inside the package
  (`PluginPackageInspector.TryResolvePackagePath`, zip-slip defence).
- One **active version per plugin id** at the application level; update = new version dir + pointer switch,
  rollback = pointer switch. Never overwrite a loaded DLL.
- Single-file host: plugin paths come from `IPluginHostOptions.PluginRootDirectory` (`AppContext.BaseDirectory`
  for app paths); **never** `Assembly.Location`.

### Load pipeline (deterministic)

`Discovered → Validating → Validated → Loading → Loaded → Initializing → Initialized → Starting → Active`
(terminal: `Failed`, `Disabled`, `Incompatible`, `Unloaded`; transitions validated by
`PluginLifecycleTransitions`, invalid transitions are refused+logged, never corrupt state).

Before creating the ALC: strict manifest parse → API/app compatibility gates → package inspection
(forbidden shipped DLLs / assembly definition / `AssemblyReference` rows / deps.json, bundled
`FluidBar.Sdk.dll` rejected, entry containment managed entry) → then ALC creation:
`PluginLoadContext` (collectible, `AssemblyDependencyResolver`, unique `FluidBar.Plugin:<assembly>:<guid>` name).
The host/TPA assembly set plus `FluidBar.Sdk` are shared from the default ALC **before** the resolver runs, so
`IFluidBarPlugin` has one type identity even if a package illegally ships an SDK copy (which is rejected anyway).
Then type resolution, activation, `InitializeAsync`/`StartAsync`, state `Active`, failure counters reset.

Failure isolation: every failure point returns `PluginLoadResult.FailureKind`
(`ManifestMissing/ManifestInvalid/Incompatible/ForbiddenReference/EntryMissing/EntryTypeMissing/
EntryTypeInvalid/ConstructorFailed/InitializeFailed/StartFailed/Timeout/Unloaded/Disabled/Unexpected`), logs a
deterministic message, unloads a partial ALC and keeps the host alive. Repeated failures → `Disabled`.

### Unload contract (cooperative, verified)

Order: `StopAsync` (if started) → `DisposeAsync` → runtime services teardown (leases cancelled+awaited,
settings flushed, command handlers cleared, context `HostStopping` cancelled, `RegisterForDisposal` LIFO) →
drop all strong host references in a `[NoInlining]` frame → `ALC.Unload()` → forced GC loop in a separate frame →
verify: `WeakReference` to the plugin instance **and** to the ALC are dead **and** no assembly of that ALC name is
still loaded. Result `PluginUnloadResult { Collected, CollectionAttempts, Duration, RestartRequired }`.

- `Collected == false` ⇒ `RestartRequired = true`; the App shows a non-blocking "restart to finish updating"
  island; the version directory is kept (never delete mapped files).
- `AssemblyLoadContext.All` is **not** evidence (it drops ALCs at unload request); WeakReference + loaded
  assembly count are.
- Plugin-internal static caches are collectible; host-owned static roots are what leak. Undisposed plugin
  timers/threads cannot be fixed by the loader.

### Threading

`Start/Stop/Dispose` are invoked on the host lifecycle thread (App worker; must not be UI-affine for plugin
code). Plugin UI work must go through `IHostDispatcher`. The loader cannot abort a hung in-process call; a hung
`StopAsync` delays unload by the timeout and is logged.

### Forbidden assembly policy

Exact: WPF assemblies (`PresentationFramework`, `PresentationCore`, `WindowsBase`, `System.Xaml`,
`System.Windows.Forms`, …), `FluidBar.App`, `FluidBar.Host`, `FluidBar.PluginHost`, `FluidBar.Marketplace`,
`FluidBar.Platform.Windows`, `FluidBar.Core`. Prefix: `Avalonia`, `Microsoft.Web.WebView2`. Applied to every
shipped DLL file name, managed assembly definition name, every `AssemblyReference` row and deps.json libraries.
Combining file-name and metadata checks is mandatory (`const string` inlining can remove AssemblyRefs). The scan
fails closed if a managed DLL cannot be parsed. It stops accidental UI/app coupling; it is not malware defence.

---

## 6. Marketplace & installer contract (`FluidBar.Marketplace`)

### Package `.fbp` (deterministic ZIP)

```
manifest.json
plugin/<entry>.dll + <entry>.deps.json + private dependencies
locales/<bcp47>.json
assets/...            LICENSE          README.md          checksums.json
```

- Excludes `signature.json` (detached), `FluidBar.Sdk.dll`, WPF/UI assemblies, `.pdb`.
- Limits: 50 MB compressed / 150 MB uncompressed / 5 000 entries; reject zip-slip, absolute paths, `..`,
  duplicates, symlinks/reparse points, encrypted/corrupt entries; verify every extracted file against
  `checksums.json` after extraction.
- Deterministic: sorted entries, fixed timestamps, generated checksums → two builds have identical SHA-256.
- Extraction is atomic (temp dir + directory move); never overwrite an existing installed version.

### Signing

- `signature.json` is detached and covers `package.sha256` (not per-file checksums): ECDSA P-256/SHA-256,
  IEEE P1363 signatures, base64 SPKI keys. Roles: `marketplace` (required for marketplace installs) and
  `author` (optional, pinned TOFU).
- Verification order: index signature → index-entry hash/size → package hash → detached signature(s) → per-file
  checksums after extraction. Any mismatch aborts the install (quarantine/log).
- Production trust: marketplace public key is **injected** into `MarketplaceTrustOptions` (compiled-in/HSM
  origin). Keys carry validity windows and `revokedAt`. Publisher key change requires explicit user consent.

### Index

- Root `index.json` (signed, `schemas/marketplace-index.schema.json`): latest version per plugin + listing
  metadata; per-plugin shards `plugins/<id>.json` hold version history; `keys.json` informational; advisories.
- Canonical signed payload: `CanonicalJson.Canonicalize(json, "signatures")` (sorted keys, no insignificant
  whitespace) → SHA-256 → ECDSA. Clients reject an unsigned/invalid index, `generatedAt` > now+24 h, or an index
  older than the cached one (rollback attack).
- `MarketplaceCatalogClient`: async-only, injectable `HttpMessageHandler`/`HttpClient`, per-request timeout,
  size-bounded, ETag single-entry cache, fallback URL, signature verification before use.

### Index generation (CI, not hand-edited)

`MarketplaceIndexGenerator.Build(artifacts, options)` turns packed + signed artifacts into the signed root
`index.json`, per-plugin history shards (`plugins/<id>.json`) and `keys.json`, deterministically (sorted plugin
order, explicit `generatedAt`, stable JSON writer) and validates the result before writing. It is exposed through
`fluidbar-plugin publish-prepare` and used by [`.github/workflows/marketplace.yml`](../../../.github/workflows/marketplace.yml):
PRs run validate/policy/pack-determinism; merges pack + sign with the protected marketplace key
(`MARKETPLACE_SIGNING_KEY_PEM`, never in the repo), regenerate + verify the index, publish packages/site
artifacts, then a separate protected `github-pages` deploy job runs `configure-pages` → `upload-pages-artifact`
→ `deploy-pages` (explicit artifact-only mode via `MARKETPLACE_DEPLOY_PAGES=false`; no silent fallback). Offline
end-to-end coverage: `tests/FluidBar.PluginHost.Tests/MarketplaceEndToEndTests` (catalog fetch → install → host
load/unload → update → rollback → load/unload, no network). Maintainer flow:
[`docs/plugin/publish-runbook.md`](../../plugin/publish-runbook.md), signing/key rotation:
[`docs/plugin/signing-and-keys.md`](../../plugin/signing-and-keys.md).

### Install / update / rollback / uninstall

`PluginInstallService` owns the state machine:
`Available → Downloading → Verifying(hash→signature) → Extracting(zip-safe) → Staged → Activating → Active`;
failures map to `InstallFailureKind` (`DownloadFailed, HashMismatch, SignatureInvalid, Incompatible, Blocked,
Yanked, InvalidPackage, ExtractionFailed, ActivationFailed, AlreadyInstalled, VersionAlreadyExists,
RollbackFailed, NotInstalled, Cancelled, IoError`).

- Versioned install dir `plugins/<id>/<version>/`; on success write state + atomically switch `active.json`, then
  call `IPluginActivator.ActivateAsync(manifest, versionDir)`; on activation failure revert the pointer to the
  previous version, keep/discard the new dir safely and return `ActivationFailed` (previous stays active).
- Update requires a strictly newer version than the active one; `RetainedVersions` (default 2: active + previous)
  are pruned; installed version directories are never overwritten (`VersionAlreadyExists` if hash differs).
- Rollback = deactivate → switch pointer → activate target; failure restores the old pointer. Settings/data are
  version-independent and survive updates.
- Uninstall = deactivate → delete version dirs (locked files ⇒ `RestartRequired`) → delete state/pointer and,
  unless "keep settings", `settings.json` + data dir.
- Policy: yanked version → not installable; `status: blocked` → install refused and installed instance disabled
  (`PluginPolicyDecision`); deprecated → warning.
- Offline install from a local `.fbp` is supported; unsigned local install requires explicit `AllowUnsigned`
  consent and produces an "Unverified" warning + audit entry. Temp/staging dirs are always cleaned up; stale
  staging entries are swept on service start.
- Capability consent data: `CapabilityConsentBuilder.Build(manifest)` → `CapabilityConsentData` (localized keys,
  details, high-risk flags, mandatory same-access warning).

---

## 7. Plugin i18n (JSON locale bundles)

```
locales/en-US.json   (required, complete)   locales/zh-Hans.json   ...
```

```json
{
  "$schema": "https://fluidbar.app/schemas/locale.schema.json",
  "locale": "zh-Hans",
  "version": 1,
  "messages": {
    "plugin.name": "正在播放",
    "island.body": "以{style}风格显示 {count, plural, =0 {没有详情} one {# 条详情} other {# 条详情}}。"
  }
}
```

- ICU subset: `{name}`, `{count, plural, =0/zero/one/two/few/many/other {...}}`, `#`, `{value, number}`,
  `'{…}'` quoted literals, `''` escape. CLDR cardinal categories implemented for en/zh/ja/ko/ru/ar/fr/pt.
- Fallback chain: plugin exact → plugin parent(s) → App UI culture exact/parent(s) → plugin default →
  App default → key. Missing keys logged once (bounded) and fall back; never crash; **Debug** may render
  `⟦key⟧` (App concern), Release shows the resolved default/key.
- Locale files are validated at packaging and at load (key pattern, ICU parse, placeholder parity with the
  default locale, limits 512 KB / 10 000 messages / 4 000 chars). Invalid non-default bundles are ignored with
  a warning; the default bundle missing/invalid fails the plugin load.
- Manifest `name/summary/description` may be localized objects (marketplace index uses them) and/or message keys
  (`nameKey`, …); runtime UI uses keys first.
- The host localizes its own chrome only; plugin-provided strings are shown as-is.

---

## 8. App / UI integration points

The App supplies these implementations (all interfaces in the SDK/host, no UI types leak into them):

| Injected | App responsibility |
|---|---|
| `IIslandPublisher` (factory per manifest) | adapt SDK `IslandEvent` → Core event hub; enforce core policy elsewhere |
| `IHostDispatcher` | marshal to App UI thread |
| `IPluginHostLogger` | JSON-lines per-plugin log sink (never retain plugin `Exception` objects; format immediately) |
| `INotificationSink` | host-rendered notifications |
| `IPluginActivator` | call `PluginHost.LoadFromDirectoryAsync` / activate version dir + on failure surface reason |
| `IPluginSettings` defaults | persist granted capabilities per plugin; pass them via `PluginHostOptions.GrantedCapabilities` |
| Marketplace UI | `MarketplaceCatalogClient` for signed index, `PluginInstallService` for install/update/rollback/uninstall, `CapabilityConsentBuilder` for consent sheet, `PluginPolicyDecision` for blocked/yanked banners |

Persistence suggestions: `%APPDATA%\FluidBar\plugins\<id>\settings.json` for settings, install state in
`state.json`, publisher fingerprint in state (TOFU), audit log for capability consent and installs. The App
never stores plugin `Type`/`Assembly`/delegate/`Exception` objects.

---

## 9. Versioning & compatibility policy

- `apiVersion` = SDK ABI (semver). Host supports one major; breaking SDK changes bump the major and keep the
  previous major loadable for one app major cycle. SDK is additive-only within a major (default interface
  methods for new members; `[Obsolete]` for two minors).
- `minAppVersion` = first app version that can load; `maxAppVersion` optional (rarely used).
- `dependencies.sdk` range (caret/tilde/comparator/`||` subset) enforced at install and load.
- `schemaVersion` (manifest format) moves independently of `apiVersion`; older-but-supported manifests are read,
  newer ones are refused with an "update FluidBar" message.
- Manifest is closed (`additionalProperties:false`); adding a field is a schema-version bump.

## 10. Non-goals for v2.0

Out-of-process/untrusted execution (v2.1), plugin XAML/`UserControl`/custom renderers, native dependencies,
plugin windows, ratings/reviews, paid plugins, plugin-to-plugin dependency auto-install, in-app code editors, and
any claim that manifest capabilities sandbox in-process code.

## 11. Tooling & tests

```powershell
dotnet build FluidBar.sln
dotnet test tests/FluidBar.Sdk.Tests
dotnet test tests/FluidBar.PluginHost.Tests

# contributor CLI (same code as CI)
dotnet run --project src/FluidBar.Cli -- validate --plugin plugins/samples/FluidBar.Plugins.SampleRich/bin/Debug/net10.0
dotnet run --project src/FluidBar.Cli -- policy   --plugin plugins/samples/FluidBar.Plugins.SampleRich/bin/Release/net10.0
dotnet run --project src/FluidBar.Cli -- pack     --plugin plugins/samples/FluidBar.Plugins.SampleRich/bin/Release/net10.0 --out artifacts/sample-rich.fbp
dotnet run --project src/FluidBar.Cli -- sign     --package artifacts/sample-rich.fbp --key <p256.pem> --key-id <id> --role marketplace

# marketplace publish pipeline (CI-equivalent, offline)
dotnet run --project src/FluidBar.Cli -- publish-prepare --plugins-root plugins/samples --artifacts artifacts/plugins --marketplace marketplace-site --key-id <id> --key <p256.pem> --configuration Release
dotnet run --project src/FluidBar.Cli -- verify-index --marketplace marketplace-site
```

Sample plugin projects (in `FluidBar.sln` under the `samples` solution folder):
`plugins/samples/FluidBar.Plugins.SampleMinimal`, `plugins/samples/FluidBar.Plugins.SampleRich`,
`plugins/samples/FluidBar.Plugins.SampleRich.Core` (private dependency). Contributor and maintainer guides:
[`docs/plugin/`](../../plugin/README.md) (quickstart, manifest reference, declarative UI, signing/keys, review
checklist, publish runbook, listing/i18n, troubleshooting). CI: [`.github/workflows/marketplace.yml`](../../../.github/workflows/marketplace.yml).
