# Publishing runbook (merge → signed index → live)

Owner: Doulor (sole maintainer). Workflow: [`.github/workflows/marketplace.yml`](../../.github/workflows/marketplace.yml).
Target: **merge → live < 10 minutes**. Everything below can also be run locally without GitHub for rehearsal or
offline/air-gapped deployments.

## Pipeline overview

```
PR (plugins/<id>/ source)
  → validate job: restore .NET 10 → build solution → unit/integration tests
                  → CLI validate/policy per plugin → pack dry-run → determinism hash check
  → Doulor review (checklist) → squash merge
  → publish job (environment: marketplace-publish, required reviewer)
      1. build plugins Release
      2. pack every plugin (deterministic) + verify two packs have identical SHA-256
      3. sign each .fbp with the marketplace key (--key-env), write detached .sig.json
      4. regenerate signed marketplace/index.json + per-plugin shards + keys.json
      5. verify the generated index signature (post-generation gate)
      6. upload packages as workflow artifacts; upload the Pages site artifact
      7. deploy the site to GitHub Pages (or fall back to the site artifact if Pages is unavailable)
      8. post-publish verification + PR/run summary (hashes, key id, URLs)
  → clients see the update on the next catalog refresh (≤ 5 min cache)
```

There are no author-uploaded binaries in the loop: CI builds from the reviewed commit, signs, and the index
pins the resulting hash.

## One-time setup

1. `marketplace-publish` environment: required reviewer = Doulor; deployment branches = `main`.
2. Secret `MARKETPLACE_SIGNING_KEY_PEM` (placeholder name — see
   [signing-and-keys.md](signing-and-keys.md)); production should use an HSM/KMS + OIDC instead.
3. GitHub Pages: Settings → Pages → Source = GitHub Actions. **If Pages is unavailable**, the workflow still
   uploads the `marketplace-site` artifact — download it and host it on the static host of your choice; the
   index is self-contained (`index.json`, `keys.json`, `plugins/*.json`).
4. Optional repository variable `MARKETPLACE_PUBLISH_RELEASES=true` enables the Release-asset upload step
   (requires `contents: write`); otherwise packages stay as workflow artifacts for the demo/CI path.
5. Prefer pinning actions to full commit SHAs in hardened deployments (the workflow pins major versions and
   documents the SHA-pinning step).

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

The publish job re-downloads/regenerates and checks:

- index signature verifies against the trusted marketplace key;
- every artifact `sha256`/`size` in the index matches the generated `.fbp`;
- package signatures verify against the index entry and key id;
- package determinism: re-packing the same source produces the same hash;
- per-plugin shards are consistent with the root index `latest`.

If any check fails, the workflow fails and Pages is not updated (release artifacts remain for inspection).

## Rollback and incident steps

- **Bad plugin version:** mark the version `yanked: true` + `yankReason` in the source metadata and republish;
  clients stop offering it and roll back automatically when the active version crashes repeatedly.
- **Malicious/broken plugin:** `status: blocked` + reason + advisory; clients disable installed instances on
  next refresh. Release assets are never deleted (forensics).
- **Bad index:** redeploy the previous `marketplace-site` artifact (each workflow run keeps one); keys/rotation
  are in [signing-and-keys.md](signing-and-keys.md).
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
| pack + sign + index generation | < 1 min |
| Pages upload/deploy | 1–2 min |
| client cache | ≤ 5 min |

Keep the publish job free of unrelated builds/tests; the validate job already ran them for the PR. If a job
exceeds ~10 minutes, investigate before deploying.

## Definition of done for a publish

- [ ] PR checklist green and merged
- [ ] publish job green; index signature verified
- [ ] packages + `index.json`/`keys.json`/shards deployed (Pages or artifact fallback)
- [ ] run summary/PR comment includes release URL, SHA-256, keyId and index entry
- [ ] installed clients can find the update on refresh; consent sheet unchanged (or capability expansion called out)
