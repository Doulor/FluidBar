# Publishing runbook (merge → signed index → live)

Owner: Doulor (sole maintainer). Workflow: [`.github/workflows/marketplace.yml`](../../.github/workflows/marketplace.yml).
Target: **merge → live < 10 minutes**. Everything below can also be run locally without GitHub for rehearsal or
offline/air-gapped deployments.

## Pipeline overview

```
PR (plugins/<id>/ source)
  → validate job: restore .NET 10 → build CLI + plugins (Release; build output derived via MSBuild TargetPath)
                  → SDK + PluginHost tests (includes offline publish/install E2E)
                  → CLI validate/policy per plugin → pack twice → determinism hash check
                  → strict-manifest negative check
  → Doulor review (checklist) → squash merge
  → publish job (environment: marketplace-publish, required reviewer)
      1. fail fast if required variables/secret are missing
      2. build plugins Release; pack every plugin (deterministic; two packs must match) + sign each .fbp
      3. regenerate signed marketplace/index.json + per-plugin shards + keys.json
      4. verify the generated index signature (post-generation gate)
      5. upload packages + self-contained site workflow artifacts (always available)
  → deploy job (environment: github-pages; skipped only if MARKETPLACE_DEPLOY_PAGES=false)
      download marketplace-site artifact → actions/configure-pages → upload-pages-artifact → deploy-pages
  → clients see the update on the next catalog refresh (≤ 5 min cache)
```

There are no author-uploaded binaries in the loop: CI builds from the reviewed commit, signs, and the index
pins the resulting hash. Pages deployment is a **separate protected `github-pages` environment job** and is
expected to succeed; there is no silent fallback (see "Pages unavailable / artifact-only" below).

## Required configuration (checked by the publish job)

| Kind | Name | Value |
|---|---|---|
| Environment | `marketplace-publish` | required reviewer Doulor; deployment branches `main` |
| Environment secret | `MARKETPLACE_SIGNING_KEY_PEM` | PKCS#8 PEM of the ECDSA P-256 marketplace key (placeholder name; never committed) |
| Repository/environment variable | `MARKETPLACE_ARTIFACT_BASE_URL` | absolute `https://` base for release packages, e.g. `https://github.com/<org>/<repo>/releases/download` |
| Repository/environment variable | `MARKETPLACE_BASE_URL` | absolute `https://` marketplace site base, e.g. `https://marketplace.fluidbar.app` |
| Repository/environment variable | `MARKETPLACE_SIGNING_KEY_ID` | e.g. `fluidbar-marketplace-2026` (must match the signing key + trusted key list) |
| Repository variable (optional) | `MARKETPLACE_PLUGINS_ROOT` | plugin source root; default `plugins/samples` in this repo |
| Repository variable (optional) | `MARKETPLACE_DEPLOY_PAGES` | `false` = explicit artifact-only publishing (deploy job skipped) |
| Environment | `github-pages` | created by the deploy job; required reviewer optional |

The workflow **fails fast** if the two base URLs or the signing key id/secret are missing; there are no
placeholder defaults in publish mode. Validate the URLs are HTTPS before the key is touched.

## Publishing scope (samples vs real plugins)

- This repository is the **App/SDK/tooling** repository: `plugins/samples/` contains two demo/reference plugins
  used by CI, tests and docs. The workflow defaults to `MARKETPLACE_PLUGINS_ROOT=plugins/samples` and publishes
  them as a pipeline demonstration/consistency check, not as the production catalog.
- The **real marketplace catalogue** lives in the `fluidbar/plugins` monorepo (`plugins/<id>/`), built and
  signed by the same `fluidbar-plugin` CLI. To publish it, set `MARKETPLACE_PLUGINS_ROOT=plugins` (or point the
  workflow at a checkout of that monorepo) and configure the real artifact/marketplace base URLs.
- Demo/sample plugins are always excluded from the production index by keeping them under `plugins/samples/` and
  publishing from `plugins/<id>/` only; never mix demo ids into the production catalog.
- Packages uploaded by CI are GitHub Release assets in the real flow (`MARKETPLACE_ARTIFACT_BASE_URL` points at
  them). In this repository they are also uploaded as `marketplace-packages` workflow artifacts for offline
  verification.

## Pages setup / artifact-only mode

1. Settings → Pages → Source = GitHub Actions.
2. The deploy job uses the `github-pages` environment (with `pages: write` + `id-token: write`) and
   `actions/configure-pages` before `actions/upload-pages-artifact` + `actions/deploy-pages`.
3. If Pages is deliberately unavailable (forks, air-gapped mirrors, incidents), set the repository variable
   `MARKETPLACE_DEPLOY_PAGES=false`. The deploy job is skipped explicitly and the `marketplace-site` workflow
   artifact is the publish output — host it on any static host; the index is self-contained
   (`index.json`, `keys.json`, `plugins/*.json`).
4. If Pages deployment fails while enabled, the run fails visibly (no silent fallback). The publish artifacts
   are still uploaded for manual recovery.

## Supply-chain pinning

- Every action is pinned to a **full 40-character commit SHA** with the release tag in a trailing comment
  (checkout v4.2.2, setup-dotnet v4.3.0, upload-artifact v4.6.2, download-artifact v4.3.0,
  configure-pages v5.0.0, upload-pages-artifact v3.0.1, deploy-pages v4.0.5).
- Update procedure: bump the SHA + comment in one PR, review the upstream release notes and diff of the tag,
  and re-run the `validate` job; never use floating `@vN` tags on the publish path.
- The CLI itself is built from the reviewed source in this repository, not installed from the network.

## Local rehearsal (no signing secret, no GitHub)

```powershell
# build samples Release
dotnet build plugins/samples/FluidBar.Plugins.SampleMinimal -c Release
dotnet build plugins/samples/FluidBar.Plugins.SampleRich -c Release

# one-time ephemeral key for the rehearsal
dotnet run --project src/FluidBar.Cli -- keygen --out artifacts/rehearsal-key.pem

# pack + deterministic check + sign + regenerate index/shards/keys
dotnet run --project src/FluidBar.Cli -- publish-prepare `
  --plugins-root plugins/samples --artifacts artifacts/plugins --marketplace marketplace-site `
  --key artifacts/rehearsal-key.pem --key-id rehearsal-2026 `
  --artifact-base-url https://github.com/fluidbar/plugins/releases/download `
  --marketplace-base-url https://marketplace.fluidbar.app `
  --configuration Release

# verify the signed index (same command CI runs after generation)
dotnet run --project src/FluidBar.Cli -- verify-index --marketplace marketplace-site
```

The offline E2E test (`MarketplaceEndToEndTests`) exercises the next stage with the generated index: catalog
fetch → install v1 → host load/unload → update v2 → load/unload → rollback v1 → load/unload, all without
network access.

## Post-publish verification (automatic)

The publish job verifies before the deploy job runs:

- index signature verifies against the trusted marketplace key (`fluidbar-plugin verify-index`);
- every artifact `sha256`/`size` in the index matches the generated `.fbp`;
- package signatures verify against the index entry and key id;
- package determinism: re-packing the same source produces the same hash (publish-prepare gate);
- per-plugin shards are consistent with the root index `latest` (generator validation).

If any check fails, the publish job fails and the deploy job never runs (previous Pages deployment stays live;
workflow artifacts remain for inspection). If the deploy job itself fails, the run fails visibly — set
`MARKETPLACE_DEPLOY_PAGES=false` for intentional artifact-only publishing rather than relying on a fallback.

## Rollback and incident steps

- **Bad plugin version:** mark the version `yanked: true` + `yankReason` in the source metadata and republish;
  clients stop offering it and roll back automatically when the active version crashes repeatedly.
- **Malicious/broken plugin:** `status: blocked` + reason + advisory; clients disable installed instances on
  next refresh. Release assets are never deleted (forensics).
- **Bad index:** re-run the deploy job from the last good publish run's `marketplace-site` artifact (workflow
  artifacts are retained 30 days); keys/rotation are in [signing-and-keys.md](signing-and-keys.md).
- **Cache:** Pages cache is ~5 min; a stale client index is rejected if `generatedAt` is older than its cached
  copy (rollback protection), so a rollback deployment always wins.
- **Signing key rotation/compromise:** follow the rotation/compromise playbook in
  [signing-and-keys.md](signing-and-keys.md); after rotation, regenerate and re-verify the index before
  deploying.

## Merge→live time budget

| Step | Budget |
|---|---|
| checkout + restore + build Release | 1–2 min |
| tests (Sdk + PluginHost, includes E2E) | 1–2 min |
| pack + sign + index generation + verify | < 1 min |
| deploy job (artifact download + configure + Pages upload/deploy) | 1–2 min |
| client cache | ≤ 5 min |

Keep the publish job free of unrelated builds/tests; the validate job already ran them for the PR. If a job
exceeds ~10 minutes, investigate before deploying.

## Definition of done for a publish

- [ ] PR checklist green and merged
- [ ] publish job green; index signature verified; signing key id in the run summary
- [ ] deploy job green (Pages URL in the `github-pages` environment) or `MARKETPLACE_DEPLOY_PAGES=false` explicitly set
- [ ] packages + `index.json`/`keys.json`/shards available (Pages / `marketplace-site` artifact / release assets)
- [ ] run summary/PR comment includes release URL, SHA-256, keyId and index entry
- [ ] installed clients can find the update on refresh; consent sheet unchanged (or capability expansion called out)
