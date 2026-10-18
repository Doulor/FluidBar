# Marketplace listing, localization and consent presentation

This covers what the marketplace shows, how plugin strings are localized, and the rules the index generator
applies. Schema: [`schemas/marketplace-index.schema.json`](../../schemas/marketplace-index.schema.json),
[`schemas/plugin-locale` → `locale.schema.json`](../../schemas/locale.schema.json).

## Listing metadata sources

| Listing element | Source |
|---|---|
| name / summary | manifest `name` / `summary` (localized strings; index generator copies them) |
| long description | `marketplace/listings/<id>/<locale>.json` (localized listing file; see below) |
| icon | manifest `icon` (`assets/...`) uploaded by CI to `marketplace.fluidbar.app/assets/<id>/icon.png` |
| screenshots + alt text | manifest `screenshots[]` (`alt` is a `LocalizedString`, required for accessibility on the listing) |
| publisher + verified badge | manifest `publisher` + index `publisher.verified` |
| version/compat/size/hash | signed index `latest` entry (generated from the built artifact) |
| capabilities | manifest `capabilities` (declared capability names shown as chips; plain-language reason in the detail view) |
| license/repository/homepage/changelog | manifest fields / release changelog |
| install counts | `latest.stats.downloads` (nightly from Release asset downloads) |
| ratings | **none in v2.0** (`stats.rating` is `null`; clients must tolerate null) |

## Localized listing files

```
marketplace/listings/<id>/<bcp47>.json
```

```json
{
  "$schema": "https://fluidbar.app/schemas/listing.schema.json",
  "locale": "zh-Hans",
  "name": "富示例",
  "summary": "包含设置、命令、小组件和悬停卡片的参考插件。",
  "description": "更长的 Markdown 描述……",
  "screenshots": [
    { "src": "https://marketplace.fluidbar.app/assets/sample-rich/compact.png", "alt": "紧凑灵动岛" }
  ],
  "changelog": "### 1.0.0\n- 首个版本。",
  "machineTranslated": false
}
```

- `en-US` is required for every plugin; other locales are optional and tracked as `localeCoverage`
  (0–100, a weak hint — never blocks install).
- Machine-translated listings **must** set `machineTranslated: true`; the UI shows "translated from English".
- A locale without a listing file falls back: requested locale → parent locale → English → manifest summary.
- Search indexes both the English and localized name/summary so a Chinese user can search Chinese names.
- Translator ownership: the plugin author owns `en-US` + the locales they can maintain; community translations
  arrive as PRs against `plugins/<id>/locales/` and listing files. Translators never change plugin source.

## Runtime plugin i18n (what the host displays)

- Bundles: `locales/<bcp47>.json` with `{ "$schema", "locale", "version": 1, "messages": { key: value } }`.
- ICU subset supported by the shared SDK formatter: `{name}`, plural (`=0`, `zero/one/two/few/many/other`, `#`),
  `{value, number}`, quoted literals (`'{…}'`, `''`), CLDR plural categories for en/zh/ja/ko/ru/ar/fr/pt.
- Fallback chain: plugin locale exact → parents → host UI culture exact/parents → plugin default → app default →
  key. Missing key is logged once per key/locale and shown as fallback (debug may render `⟦key⟧`).
- Validation (build + load): canonical BCP-47 tags, key pattern `^[A-Za-z][A-Za-z0-9._-]{0,127}$`, message
  length ≤ 4000, ICU parseable, placeholder parity with `en-US`, default bundle complete. A broken non-default
  bundle is ignored with a warning; a broken/missing default bundle fails the load.
- Plugin strings in island events are already localized by the plugin; the host never re-translates them. Host
  chrome strings are host-localized (pass tokens, not pre-formatted words).
- Locale switching at runtime re-reads plugin catalogs and re-renders contribution descriptors; no plugin
  restart is required (restart is only a fallback for exotic cases).

## Consent presentation (install sheet)

The marketplace install sheet uses `CapabilityConsentBuilder` output (see
[`contracts/plugin.md`](../rewrite/contracts/plugin.md) §6):

- One plain-language row per declared capability with the manifest `reason` and concrete details
  (domains, hotkeys, file scopes), high-risk rows emphasized.
- The **mandatory** warning, verbatim: *"Plugins run with the same access as FluidBar. Only install plugins you
  trust. Capabilities are disclosed for your consent; they are not a sandbox."*
- Buttons: Install / Cancel; "Show details" expands the raw capability block. A capability expansion between
  versions must be shown before updating.
- Sideloaded (offline `.fbp`) installs show the "Unverified" banner and require explicit consent; they are
  recorded in the audit log and never auto-update.

## Index/locale consistency rules (generator)

- `availableLocales`/`locales` are the union of the declared plugin bundles; `localeCoverage` maps each locale
  to 100 for the default/bundled locale and 0 when only fallback content exists (improves as listing files are
  added).
- Listing `baseUrl` and `historyUrl` are derived from the marketplace base URL; artifact/signature URLs are
  HTTPS only and on an allowlisted host (`github.com`, `objects.githubusercontent.com`,
  `marketplace.fluidbar.app`, `raw.githubusercontent.com`).
- Names/summaries must be non-empty for the default locale; a localized map without `en-US` fails validation.
- The index is signed after generation; clients verify the signature before showing anything from it, so a
  tampered listing URL/hash cannot be injected.
