# Plugin review checklist (single maintainer: Doulor)

Use this for every plugin PR. The automated gates run first; the human review is intentionally short because
the single maintainer cannot line-by-line audit third-party code. **The trust story is: reviewed source + CI
build + signature + hash pinning**, not a sandbox.

## Automated gates (must be green before human review)

- [ ] `dotnet build FluidBar.sln` green; plugin builds on `net10.0` with `EnableDynamicLoading=true`
- [ ] `fluidbar-plugin validate --plugin <build output>` exit 0 (strict manifest + semantic rules)
- [ ] `fluidbar-plugin policy --plugin <build output>` exit 0:
  - no forbidden assemblies/references (WPF/Avalonia/WebView2/`FluidBar.App`/host); no bundled
    `FluidBar.Sdk.dll`; scan covers file names, assembly definitions, `AssemblyReference` rows and deps.json
  - package layout valid (manifest/plugin/locales/LICENSE/README/checksums)
  - deterministic packaging: two packs produce the same SHA-256
- [ ] `dotnet test tests/FluidBar.Sdk.Tests` + `tests/FluidBar.PluginHost.Tests` green (includes the offline
  publish→install→update→rollback→load/unload E2E)
- [ ] Loader smoke: plugin loads → starts → stops → unloads with the ALC collected (no `restartRequired`)
- [ ] Locales: `en-US` complete; declared locales parse; placeholder parity; canonical tags
- [ ] Manifest schema-valid (`schemas/plugin.schema.json`); no unknown properties

## Human review (Doulor)

### Identity & licensing
- [ ] `id` matches the folder, is not a reserved name, not impersonating another publisher
- [ ] publisher block consistent across versions; repository URL points at the real source
- [ ] LICENSE is OSI-approved/explicit; README explains what the plugin does
- [ ] Version strictly greater than the published version; changelog entry exists

### Functionality & UX
- [ ] The description matches the behavior; capabilities are minimal for what it does
- [ ] Contribution descriptors render correctly for every declared layout/settings page/command/widget
- [ ] Settings keys/types/defaults match the plugin logic; no dead controls; localization keys resolve
- [ ] Screenshots/listing metadata present (if the plugin is not a pure utility)
- [ ] No first-run surprise (network calls, hotkeys, toasts) that is not disclosed in the manifest

### Security & privacy
- [ ] Every high-risk capability has a plain-language `reason`; wildcard network, shell execute, process spawn,
  registry write, file writes outside `$PLUGIN_DATA` are treated as **security label** → written acceptance
  note in the PR *and* release notes
- [ ] No obfuscation/minified-only source; no post-build downloads or runtime installers; no secrets committed
- [ ] Telemetry (if any) is disclosed and disabled by default; no silent data collection
- [ ] Code does not read browser profiles/cookies/tokens/env secrets unless the capability/description says so
- [ ] No elevation prompts; no writes to `Program Files`; no background timers below declared minimums

### Packaging & compatibility
- [ ] `apiVersion` on 2.x; `minAppVersion` sane; SDK dependency range satisfied
- [ ] Private dependencies shipped (deps.json + DLLs under `plugin/`); no native deps unless explicitly approved
- [ ] No `FluidBar.Sdk.dll`/WPF/Avalonia files; no `.pdb`; no `signature.json` inside the package
- [ ] The package is built from the reviewed commit by CI (never an author-uploaded binary)

### i18n / accessibility
- [ ] Default locale complete; localized manifest fields include `en-US`
- [ ] Placeholders/plurals balanced; no hard-coded UI strings in rendered data where the host owns chrome
- [ ] Descriptions/titles meaningful for screen readers (no icon-only labels without a title)

## Decision

- **Approve:** all gates green + checklist satisfied → squash merge → publish workflow runs.
- **Request changes:** precise list referencing checklist items; CI re-runs after push.
- **Reject:** malware, undisclosed telemetry/self-update, obfuscation, license violation, impersonation,
  capability not justified. Record the reason in the PR; block/yank the version if already published.

## Risk acceptance record (single reviewer)

For high-risk capabilities, add a PR comment containing: capability, why it is necessary, what the code does
with it, residual risk, and whether the description/consent copy covers it. This replaces the second reviewer
until the community-governance triggers in `marketplace/README.md` are met.

## SLAs

| Event | Action |
|---|---|
| New plugin PR | first review ≤ 5 business days |
| Update PR | ≤ 3 business days; 2 reviewers later if capabilities expand |
| Capability expansion | explicit diff review + consent copy update |
| Malware report | triage + `status: blocked` ≤ 24 h; advisory |
| Takedown (legal/security) | block + remove asset + notify ≤ 72 h |
