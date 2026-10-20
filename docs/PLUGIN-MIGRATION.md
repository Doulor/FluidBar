# Plugin Migration Guide

Status: frozen with the v1 plugin platform (SDK apiVersion 2.x).

## 1. Compatibility contract

- The host implements `PluginApiVersion.Current`; the manifest `apiVersion` must match the supported
  major/minor contract.
- `minAppVersion` / `maxAppVersion` gate installation and load against the running FluidBar version.
- `capabilities.ui.pageTemplates` controls which page templates a plugin may register; `custom` is
  available under the v1 page API.
- Manifest parsing is strict: unknown properties are rejected. Add new properties only in a new schema
  version with an explicit migration path.

## 2. Incompatible changes

The following are breaking changes and require a new **major** SDK ABI:

- Removing or renaming a public SDK type, member, enum value or interface method.
- Changing command/hit-area coordinate semantics or limits in a way that changes existing page output.
- Removing a page template, command kind, icon token or color token.
- Changing the compact descriptor contract or making previously valid canvases fail.
- Changing the manifest wire schema in a way that rejects existing installed manifests.
- Changing the trust/channel rules so an already-installed signed package can no longer load without a
  migration path.

Additive changes (new command kind, new optional manifest property with a default, new host icon token) are
minor-compatible; plugins must not assume unknown tokens are available on older hosts.

## 3. Migrating from pre-M3 pages

1. Keep the existing `list` page registration and data payloads working.
2. To move a custom UI region to the command canvas:
   - register with `TemplateId = PageTemplates.Custom`;
   - publish `PageCanvasData` with commands recorded through `IIslandCanvas`;
   - declare `PageHitArea` for every interactive region and give each a stable `CommandId` and
     `AccessibleName`;
   - validate command count/text/hit-area limits before publishing.
3. If the plugin has a compact activity, optionally add `CompactViewDescriptor`; invalid descriptors fall
   back to the generic compact template, so migration is not all-or-nothing.
4. For settings descriptions/previews, use `SettingsPageContribution.PreviewCanvas`; it accepts the same
   command records as a page but is non-interactive.

## 4. Migrating signed installations

- Existing signed market installations need the v1 integrity marker (`package.fbp`, `signature.json`,
  `integrity.json`) to load through the market channel. Reinstall the signed package once with the v1
  installer to create the marker.
- Unsigned local packages remain on the local channel; do not move them to the market root. Local loads
  require the one-time `id@version` risk acknowledgement.
- Keep the publisher key pin from `state.json`; changing keys requires a new review/install cycle.

## 5. Versioning checklist

- Bump `version` for every behavior change; bump `apiVersion` only for ABI changes.
- Keep manifest ids and page ids stable across updates; page ids are persisted in user settings.
- Test against the oldest supported `minAppVersion` and the current host.
- Run `fluidbar-plugin validate` and the package signing flow before publishing.

