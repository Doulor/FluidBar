# Declarative UI contributions (contract for plugins and the App/UI renderer)

**Invariant:** plugins never ship UI code, XAML, `UserControl` or windows. They contribute *data descriptors*
that the host renders with host controls, host theme tokens, host localization and host accessibility. This is
what makes live language switching, high-contrast themes, DPI scaling and the future out-of-process lane
possible. The App/UI owns rendering; the manifest + runtime APIs are the contract.

This document is the reference for ui-owner (renderer) and plugin authors (producers). It matches
`FluidBar.Sdk.Contributions` and `schemas/plugin.schema.json`.

## Layouts (island + hover card)

`contributes.layouts[]`:

```json
{
  "id": "sample-rich.compact",                   // namespaced <plugin-id>.<name>, unique
  "surface": "island.compact",                    // island.compact | island.expanded | hoverCard
  "template": "media.v1",                         // host-owned template (below)
  "slots": {                                      // host renders slots; plugin supplies data
    "title":    { "field": "title", "maxLines": 1, "style": "title" },
    "subtitle": { "field": "subtitle", "style": "caption" },
    "body":     { "field": "tracksLabel", "maxLines": 2, "style": "body" },
    "progress": { "field": "progress" },
    "actions":  { "commands": ["sample-rich.refresh"] }
  },
  "constraints": { "minWidth": 132, "maxWidth": 430, "minHeight": 36, "maxHeight": 44 }
}
```

- Template ↔ allowed slot names are enforced by `LayoutTemplateCatalog` (validator error
  `manifest.contributes.layout.slot.unknown`). Slots are `field` (from `IslandEvent.Payload` well-known fields
  or `Payload.Fields`), `literal`, or `commands` (buttons).
- `kind` (`text|image|icon|badge|progress|wave|code`), `maxLines` (1–8) and `style`
  (`title|subtitle|body|caption|badge|mono`) are hints for the host; the host owns actual typography.
- Constraints are DIP bounds within host geometry tokens; the host clamps to its own tokens and animation
  policy. Layout ids are removed when the plugin is disabled/uninstalled.
- The plugin runtime event references the layout by id (`IslandEvent.LayoutId`) and supplies slot data in
  `IslandEvent.Payload.Fields` (bounded: 64 entries, ≤1024 chars/value).

## Settings pages (final descriptor — what the App renders)

```json
"contributes": {
  "settingsPages": [
    {
      "id": "general",
      "title": { "en-US": "General", "zh-Hans": "常规" },
      "description": { "en-US": "Host-rendered settings." },
      "controls": [ /* optional legacy loose rows, rendered first as an implicit "General" group */ ],
      "groups": [
        {
          "id": "playback",
          "title": { "en-US": "Playback", "zh-Hans": "播放" },
          "description": { "en-US": "How the plugin behaves." },
          "controls": [
            { "type": "toggle", "key": "showDetails", "title": { "en-US": "Show details" }, "default": true },
            { "type": "slider", "key": "refreshMinutes", "title": { "en-US": "Refresh interval" },
              "min": 1, "max": 60, "step": 1, "default": 5, "unit": "min" }
          ]
        },
        {
          "id": "diagnostics",
          "title": { "en-US": "Diagnostics" },
          "controls": [
            { "type": "status", "key": "status.lastRefresh", "format": "timestamp",
              "title": { "en-US": "Last refresh" } },
            { "type": "button", "key": "clearCache", "command": "sample-rich.clear-cache",
              "title": { "en-US": "Clear cache" }, "style": "secondary" }
          ]
        }
      ]
    }
  ]
}
```

### Rendering contract (host/UI)

1. Render loose `controls` first (implicit "General" group), then `groups` in declaration order; group `title`
   and optional `description` are section headers.
2. Every control has `title` and optional `description` (`LocalizedString`, resolved with host locale +
   plugin fallback). Controls are host-styled; no plugin-supplied colors/fonts/sizes except the declared
   `unit` label and validation limits.
3. Control semantics:

| `type` | Rendering | Key data | Validation |
|---|---|---|---|
| `toggle` | switch | `default: bool` | default must be boolean |
| `slider` | slider + value + `unit` | `min`, `max` (+ `step`), `default` number | `min < max`, `step > 0`, default in range |
| `number` | numeric field | optional `min`/`max`, `default` | `min <= max` |
| `select` | dropdown | `options[{value,label}], `default` | options non-empty/unique, default in options |
| `text` | text box | optional `validation.pattern/minLength/maxLength` | regex valid + bounded |
| `path` | folder/file picker | optional `validation` | one of the declared file scopes at runtime |
| `secret` | masked input, "Set/Not set" state | none | stored by host with DPAPI(CurrentUser) |
| `button` | host button + `style` | declared `command` id | command must exist in `contributes.commands`; missing handler = disabled |
| `link` | link button | `default` absolute http(s) URL (or `command`) | URL validated |
| `info` | static info row | `description` | — |
| `status` | **read-only** row: label + formatted value + `unit` | `key`, `format` (`text\|number\|boolean\|timestamp`), optional `command` (refresh action) | no `min/max/step/options` |

4. **Status rows protocol:** the host reads the value from the host-owned settings store for `key` and
   re-renders when settings change. The plugin updates it via `IPluginContext.Settings.SetString/SetInt32/...`
   (e.g. `Settings.SetString("status.lastRefresh", DateTimeOffset.UtcNow.ToString("O"))`). `timestamp` format
   is local-time relative ("2 min ago") or absolute per host locale; `boolean` renders a localized Yes/No;
   missing value renders "—". Optional `command` gives the row a refresh button.
5. Settings values are persisted by the host (`%APPDATA%\FluidBar\plugins\<id>\settings.json`, schema-versioned,
   atomic write). Unknown keys are preserved but not rendered. Secrets are never shown or logged.
6. Validation failures show inline errors and are not persisted; defaults are applied for missing values.
7. Disabling/uninstalling a plugin removes its pages; the host must not cache descriptor objects.

### Limits (validator-enforced)

- 8 settings pages per plugin; 8 groups per page; 20 controls per group; 40 controls per page.
- Page `id` and group `id`: `^[a-z0-9][a-z0-9-]{1,31}$`; page/group ids unique per page.
- Control `key`: `^[A-Za-z][A-Za-z0-9.]{0,63}$`, **unique across the whole plugin** (all pages/groups); keys are
  the settings storage keys.
- A page needs at least one loose control or one group; a group needs at least one control.

## Commands

`contributes.commands[]`: `{ id (namespaced), title, description?, icon?, defaultHotkey?, contexts? }`.
Contexts: `global`, `island`, `contextMenu`, `commandPalette`, `settings`. The plugin binds handlers at runtime
with `IPluginContext.Commands.Register(id, handler)` (undeclared/duplicate → `InvalidOperationException`).
Missing handler means the command renders disabled, never a crash. Commands are invoked by the host with a
timeout and exception isolation (`PluginHost.ExecuteCommandAsync`). `defaultHotkey` is only a suggestion — the
user's remap wins.

## Context menu

`contributes.contextMenu[]`: `{ id, command, title, when, group? }`. `when` is a **restricted predicate**,
never code: comparisons (`==`, `!=`), boolean ops (`&&`, `||`, `!`), parentheses, `source == 'plugin-id'`,
payload presence checks. Groups: `primary`, `controls`, `clipboard`, `navigation`, `danger`. Items whose
predicate is false are hidden; command handlers are shared with the command registry.

## Widgets

`contributes.widgets[]`: `{ id, slot, template, size? }` — accessory content inside the host island/hover card.
Slots: `island.accessory`, `island.leading`, `hoverCard.footer`. Templates: `wave.v1`, `badge.v1`, `icon.v1`,
`text.v1`. Widgets are not independent windows and inherit the island's animation/stack/theme policy.

## Internationalization

- `LocalizedString` (manifest + descriptors) is a plain string *or* a locale map:
  `{ "en-US": "…", "zh-Hans": "…" }`. Maps must include the default locale (`en-US`). For runtime UI strings,
  prefer message keys resolved from `locales/<bcp47>.json`; `nameKey`/`summaryKey`/`descriptionKey` on the
  manifest support that.
- Locale bundles: `{ "$schema", "locale": "zh-Hans", "version": 1, "messages": { "key": "value" } }`.
  ICU subset: `{name}`, `{count, plural, =0/one/few/many/other {...}}`, `{value, number}`, `#`, `'{…}'` quoting.
- Resolution chain (host): requested locale exact → parents → app UI culture exact/parents → plugin default →
  app default → key. Missing keys are logged once and fall back; they never crash the host.
- Placeholder parity across locales is enforced at package build (packaging error). Extra keys warn; missing
  keys in the default bundle fail. `en-US` must be complete.
- Relative time/number/date formatting is host-owned; plugins pass raw values + explicit format tokens rather
  than pre-formatted localized strings where the host chrome is involved (`statusToken`-style data).
- The App's own chrome (badges like "LIVE", "muted", relative time) is localized by the App, never by plugins;
  plugin-provided strings are rendered as-is.

## Theming, accessibility, motion

Plugins cannot set colors, fonts, radii, shadows or animation. The host applies theme tokens, high-contrast
support, focus order, screen-reader names from titles/descriptions, DPI scaling and reduced-motion policy.
Any new visual capability is a host template change, not a plugin UI escape hatch.
