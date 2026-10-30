# Publishing runbook (merge → signed index → live)

Owner: Doulor. Workflow: [`.github/workflows/marketplace.yml`](../../.github/workflows/marketplace.yml).
Target: **merge → live < 10 minutes**. The same CLI steps work locally for rehearsal or air-gapped mirrors.

## Pipeline overview

```
PR / push under plugins/, schemas/, marketplace/ or the marketplace tooling
  → validate: build CLI + plugins (Release) → SDK + PluginHost tests → schema parse
              → CLI validate/policy per plugin → pack twice + deterministic SHA-256 check
              → strict-manifest negative check
  → publish (environment: marketplace-publish)
      1. fail fast if the signing key, key id or base URLs are missing
      2. build every plugin, pack + sign each .fbp, verify deterministic output
      3. regenerate signed marketplace-site/index.json + shards + keys.json
      4. verify the generated index signature
      5. upload each .fbp + .fbp.sig.json to its versioned GitHub Release
      6. upload workflow artifacts for inspection/offline mirrors
  → deploy (environment: marketplace-deploy)
      build the official website → merge the signed index into dist/market
      → deploy dist to Cloudflare Pages project `fluidbar`
  → clients see the change on their next catalog refresh
```

No author-uploaded binary enters the catalog: CI builds from the reviewed commit, signs the bytes it built,
publishes them under a versioned release, and pins the SHA-256 in the signed index.

## Required configuration

| Kind | Name | Value |
|---|---|---|
| Environment | `marketplace-publish` | deployment branch policy: `main` only |
| Environment secret | `MARKETPLACE_SIGNING_KEY_PEM` | PKCS#8 PEM of the ECDSA P-256 marketplace key (never committed) |
| Environment variable | `MARKETPLACE_ARTIFACT_BASE_URL` | `https://github.com/Doulor/FluidBar/releases/download` |
| Environment variable | `MARKETPLACE_BASE_URL` | `https://fluidbar.doulor.cn/market` |
| Environment variable | `MARKETPLACE_SIGNING_KEY_ID` | `fluidbar-marketplace-2026` |
| Environment variable | `MARKETPLACE_PLUGINS_ROOT` | `plugins/samples` in this repository |
| Repository variable | `MARKETPLACE_DEPLOY_PAGES` | leave unset/true for automatic deploy; `false` = artifact-only |
| Environment | `marketplace-deploy` | deployment branch policy: `main` only |
| Environment secret | `CLOUDFLARE_API_TOKEN` | scoped API token with **Cloudflare Pages: Edit** on the FluidBar account |
| Repository variable | `CLOUDFLARE_ACCOUNT_ID` | `83fdea7d910cbc683e6d53fa6baf45ab` |

The publish job fails before touching the signing key if a base URL or key id is missing. The deploy job fails
if the Cloudflare token is missing; it never silently skips a requested deploy.

## How versioned release URLs map to the index

The generated index uses:

```
{artifactBaseUrl}/plugin-{id}-v{version}/{packageFile}
```

With `MARKETPLACE_ARTIFACT_BASE_URL=https://github.com/Doulor/FluidBar/releases/download`, CI creates one
GitHub Release per plugin version with tag `plugin-<id>-v<version>` and attaches:

```
<id>-<version>.fbp
<id>-<version>.fbp.sig.json
```

Re-running the same version uses `gh release upload --clobber`; old versions remain downloadable for
rollback and forensics. Never delete release assets.

## Samples vs production catalogs

- This App/SDK/tooling repository publishes `plugins/samples` as a pipeline self-test.
- A separate plugin monorepo can reuse the same workflow by setting `MARKETPLACE_PLUGINS_ROOT=plugins`.
- Keep demo ids out of the production index; point one deployment at one plugin root.

## Cloudflare Pages setup

1. Project `fluidbar` is a Direct Upload Pages project; the workflow builds `website/dist`, copies the freshly
   signed marketplace files into `dist/market`, and runs `wrangler pages deploy`.
2. Create a scoped Cloudflare API token with **Account → Cloudflare Pages → Edit** and store it as the
   `marketplace-deploy` environment secret `CLOUDFLARE_API_TOKEN`. Do not use an OAuth session token: it
   expires after one hour.
3. Store the account id as the repository variable `CLOUDFLARE_ACCOUNT_ID`.
4. `MARKETPLACE_DEPLOY_PAGES=false` is the explicit artifact-only escape hatch. It skips only the deploy job;
   signed packages and the `marketplace-site` artifact are still produced.

## Supply-chain pinning

- Actions are pinned to full commit SHAs with the release tag in a trailing comment.
- Update a pin only with a reviewed PR that checks the upstream release notes and diff.
- The CLI is built from this repository's reviewed source, not installed from a marketplace.

## Local rehearsal

```powershell
dotnet build src/FluidBar.Cli -c Release
dotnet build plugins/samples/FluidBar.Plugins.SampleMinimal -c Release
dotnet build plugins/samples/FluidBar.Plugins.SampleRich -c Release

dotnet run --project src/FluidBar.Cli -c Release --no-build -- publish-prepare `
  --plugins-root plugins/samples --artifacts artifacts/plugins --marketplace marketplace-site `
  --key .local/keys/marketplace-2026.pem --key-id fluidbar-marketplace-2026 `
  --artifact-base-url https://github.com/Doulor/FluidBar/releases/download `
  --marketplace-base-url https://fluidbar.doulor.cn/market `
  --asset-base-url https://fluidbar.doulor.cn/market/assets `
  --configuration Release --channel stable

dotnet run --project src/FluidBar.Cli -c Release --no-build -- verify-index --marketplace marketplace-site
```

## Post-publish verification

The publish job verifies before deployment:

- index signature against the trusted marketplace key;
- every artifact SHA-256/size in the index matches the generated package;
- package signatures match the index key id;
- re-packing produces the same SHA-256;
- shards agree with the root index's `latest`.

The deploy job then rebuilds the site and copies the signed files into `dist/market`. If it fails, the run is
red; packages and artifacts are already published. Re-run the deploy job once Cloudflare is reachable.

## Rollback and incidents

- **Bad version:** mark it yanked in source metadata and republish; clients stop offering it.
- **Malicious plugin:** mark it blocked with an advisory; clients disable it on refresh.
- **Bad index:** re-deploy the last known-good `marketplace-site` artifact (retained 30 days) or revert the
  source commit and rerun the workflow. Never hand-edit a signed `index.json`.
- **Key rotation/compromise:** follow [signing-and-keys.md](signing-and-keys.md).

## Definition of done

- [ ] validate + publish jobs green; `verify-index` output in the log
- [ ] each `plugin-<id>-v<version>` Release has the `.fbp` and `.sig.json`
- [ ] deploy job green; `https://fluidbar.doulor.cn/market/index.json` serves the new `generatedAt`
- [ ] downloaded package SHA-256 matches the signed index
- [ ] installed clients discover the update on refresh
