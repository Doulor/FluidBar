# Plugin manifest reference (schemaVersion 2)

Normative JSON Schema: [`schemas/plugin.schema.json`](../../schemas/plugin.schema.json). Parsing is **strict**
(unknown properties are rejected); semantic rules are enforced by `PluginManifestValidator` (same code in the
CLI, CI, host loader and installer). Example manifests: `plugins/samples/*/manifest.json`.

## Top level

| Field | Required | Rules |
|---|---|---|
| `$schema` | no | schema URL for editors |
| `schemaVersion` | yes | must be `2` |
| `id` | yes | `^[a-z0-9](?:[a-z0-9-]{0,38}[a-z0-9])?$`; equals the `<id>` folder; reserved ids rejected (`fluidbar, system, settings, marketplace, core, api, plugin, island, monitor`) |
| `version` | yes | semver `major.minor.patch[-pre][+build]`; strictly greater than the published version |
| `apiVersion` | yes | semver; SDK ABI the plugin was compiled against; host supports one major (`2.x`) |
| `minAppVersion` | yes | semver; first FluidBar version that can load the plugin |
| `maxAppVersion` | no | semver; must be ≥ `minAppVersion` (rarely used) |
| `publisher` | yes | `{ id (^[a-z0-9][a-z0-9-]{1,38}$), displayName (1–80), url?, keyId? }`; `keyId` required for marketplace submissions |
| `author` | no | `{ name (1–80), email?, url? }` |
| `name`, `summary` | yes | `LocalizedString` (plain string or locale map); must include `en-US` |
| `description` | no | `LocalizedString` (≤ 8000 chars per locale) |
| `nameKey`, `summaryKey`, `descriptionKey` | no | message keys resolved from the plugin locale bundles (runtime keys win over localized objects) |
| `license` | yes | 1–64 chars, OSI-approved or explicit |
| `repository`, `homepage` | no | absolute http(s) URLs |
| `changelog` | no | package-relative file name |
| `categories` | no | ≤ 3 of `productivity, media, developer, system, communication, information, fun, integration, accessibility, theme` |
| `tags` | no | ≤ 10 of `^[a-z0-9][a-z0-9-]{0,23}$` |
| `icon` | no | `assets/...` path or `{ light, dark, monochrome? }` (light+dark required in object form) |
| `screenshots` | no | ≤ 8 `{ src: assets/..., alt? }` |
| `entry` | yes | see below |
| `platforms` | no | subset of `win-x64, win-arm64, win-x86` |
| `locales` | yes | see below |
| `capabilities` | yes | object (use `{}` when none); see below |
| `activation` | no | `startup`, `onMediaSession`, `onNotification`, `onSettingsOpen`, `onIslandEvent`, `onCommand:<command-id>`, `interval:<ms>` |
| `contributes` | yes | object (`{}` for data-only plugins) — layouts/settingsPages/commands/contextMenu/widgets |
| `dependencies` | no | `{ sdk: "<range>", plugins: [{ id, range, optional? }] }` (≤ 16 plugin deps, no self-dependency, no auto-install in v2.0) |
| `settings` | no | `{ schema?, defaults?, storage: "host" \| "plugin" }` — host-owned settings are the default |
| `integrity` | no | `{ entrySha256: 64 lowercase hex, files: "checksums.json" }` |
| `signature` | no | `{ scheme: "detached-v1", algorithm: "ECDSA-P256-SHA256", publisherKeyId?, signatureFile: "signature.json" }` — the actual signature is detached |

## `entry`

```json
{ "runtime": "inproc", "assembly": "plugin/FluidBar.Plugins.NowPlaying.dll",
  "type": "FluidBar.Plugins.NowPlaying.NowPlayingPlugin", "targetFramework": "net10.0" }
```

- `runtime`: only `inproc` in v2.0 (`outofproc` rejected until the v2.1 protocol ships).
- `assembly`: package-relative, forward slashes, must start with `plugin/`, end `.dll`, no `..`.
- `type`: fully qualified type name implementing `IFluidBarPlugin` from the **host** SDK identity.
- `targetFramework`: `net10.0` or `net10.0-windows…`.

## `locales`

```json
{ "default": "en-US", "available": ["en-US", "zh-Hans"], "path": "locales", "fallback": "en-US" }
```

`default` must be in `available`; `fallback` likewise; locale tags are canonical BCP-47; ≤ 64 locales. `en-US`
must be complete for every declared locale (placeholder parity with the default bundle is a packaging error).
Bundle format + fallback chain: [listing-i18n.md](listing-i18n.md) and
[declarative-ui.md](declarative-ui.md#internationalization).

## `capabilities` — disclosure and consent, never a sandbox

An in-process plugin runs with the user's full rights. Capabilities are the **install consent + review
metadata**; they are not enforced at runtime. Each risky capability needs a plain-language `reason`.

| Capability | Shape | Risk |
|---|---|---|
| `network` | `{ domains: [host…] (required), methods?, reason (≥10) }` | high; wildcard `*`/`*.domain` needs security review |
| `clipboard` | `{ read?, write?, reason? }` | medium |
| `files` | `{ read?: [scope…], write?: [scope…], reason (≥10) }` | high; writing outside `$PLUGIN_DATA/$PLUGIN_INSTALL/$TEMP` needs review |
| `media` | `{ sessions?, control?, reason? }` | medium |
| `notifications` | `{ read?, post? }` | low |
| `shell` | `{ open?, execute?, reason? }` | high (`execute` needs review) |
| `hotkeys` | `{ global: ["Ctrl+Alt+M"], reason (≥10) }` | high |
| `process` | `{ spawn?, reason? }` | high |
| `registry` | `{ read?: [path…], write?: [path…], reason? }` | high (`write` needs review) |
| `telemetry` | `{ enabled, endpoint?, reason? }` | disclosure only; off by default; endpoint required when enabled |
| `background` | `{ intervalMinMs (≥500), reason }` | host enforces the minimum interval |
| `ui` | `{ island, hoverCard, settings, contextMenu, commands, widgets, xaml }` | `xaml:true` rejected for marketplace plugins |

Scopes for `files`: `$PLUGIN_DATA`, `$PLUGIN_INSTALL`, `$DOWNLOADS`, `$DOCUMENTS`, `$PICTURES`, `$MUSIC`,
`$VIDEOS`, `$DESKTOP`, `$TEMP`. **The App must show the same-access warning** from the consent model; see
[`contracts/plugin.md`](../rewrite/contracts/plugin.md) §3.

## `contributes` (summary)

- `layouts[]` — host templates + named slots (no XAML). Surface `island.compact|island.expanded|hoverCard`;
  templates `text.v1, status.v1, progress.v1, media.v1, media.hover.v1, notification.v1, agent.v1, clock.v1,
  list.v1`. Slot names are validated per template; slot bindings use `field`/`literal`/`commands`.
- `settingsPages[]` — host-rendered groups/controls incl. read-only `status` rows; see
  [declarative-ui.md](declarative-ui.md#settings-pages).
- `commands[]` — ids only; register handlers at runtime via `IPluginContext.Commands`; optional
  `defaultHotkey` (user-remappable) and `contexts`.
- `contextMenu[]` — `{ id, command, title, when, group? }`; `when` uses the restricted predicate grammar.
- `widgets[]` — accessory templates in host slots (`island.accessory`, `island.leading`, `hoverCard.footer`).

Limits: 12 layouts, 8 settings pages (8 groups/page, 40 controls/page), 32 commands, 16 context-menu items,
8 widgets. Contribution ids are namespaced `<plugin-id>.<name>` and unique.

## Validation + packaging rules (CLI/CI)

`fluidbar-plugin validate` → JSON shape (strict parser) + semantic rules; exit 2 on error.
`fluidbar-plugin policy` → validate + package inspection: forbidden assemblies/references (WPF/Avalonia/
WebView2/FluidBar.App/host), no bundled `FluidBar.Sdk.dll`, locale completeness/placeholder parity, `.fbp`
layout (LICENSE/README/checksums), deterministic packaging.
Errors use stable codes such as `manifest.id.invalid`, `manifest.locales.default.notAvailable`,
`manifest.capability.reason.missing`, `manifest.contributes.layout.slot.unknown`,
`manifest.contributes.setting.key.duplicate`, `manifest.dependencies.sdk.unsatisfied`, `package.forbiddenAssembly`.
