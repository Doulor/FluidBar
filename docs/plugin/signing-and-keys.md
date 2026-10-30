# Signing and key management (maintainer: Doulor)

FluidBar uses **detached ECDSA P-256 / SHA-256** signatures. No private key ever enters the repository, a
release artifact, the `/marketplace` directory or a plugin package. The marketplace private key is held by the
maintainer and exposed to CI only as a protected secret (in production: an HSM/KMS with short-lived OIDC
credentials).

## What is signed

| Artifact | Signed by | Covered bytes | Stored where |
|---|---|---|---|
| `.fbp` package | marketplace key **required**, publisher/author key optional | SHA-256 of the ZIP bytes | detached `<file>.fbp.sig.json` next to the package |
| `marketplace/index.json` | marketplace key | canonical JSON payload **excluding** `signatures` (sorted keys, no insignificant whitespace) | `marketplace/index.json` itself |
| `FluidBar.exe` + first-party DLLs | Authenticode (code-signing cert) | PE binary | OS trust; not part of the plugin contract |

The package signature is **detached** because a signature cannot cover the ZIP that contains it. Both package
and index signatures use base64 **IEEE P1363** (r||s, 64-byte) ECDSA signatures and base64 **SPKI** public keys.

## Detached package signature format

```json
{
  "$schema": "https://fluidbar.doulor.cn/schemas/signature.schema.json",
  "schemaVersion": 1,
  "package": { "sha256": "9f2c…", "size": 184320 },
  "signatures": [
    {
      "keyId": "fluidbar-marketplace-2026",
      "role": "marketplace",
      "algorithm": "ECDSA-P256-SHA256",
      "publicKey": "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE…",
      "value": "MEUCIQD…",
      "signedAt": "2026-10-18T09:00:00Z"
    }
  ]
}
```

Roles: `marketplace` (required for marketplace installs) and `author` (optional, publisher key pinned on first
install — TOFU; a changed publisher key requires explicit user consent). Clients verify: index signature →
index-advertised hash/size → downloaded package SHA-256 → detached signature(s) → per-file `checksums.json`
after extraction. Any mismatch fails the install and keeps the old version active.

## Index signature

- Payload = `CanonicalJson.Canonicalize(index.json, "signatures")` (object keys sorted ordinal, no whitespace;
  RFC 8785-inspired). Signature value is over the SHA-256 of those bytes.
- `keys.json` lists key metadata (`keyId`, role, algorithm, SPKI public key, `notBefore`, `notAfter`,
  `revokedAt`); clients trust **injected/compiled-in** marketplace keys, not keys taken from an unsigned file.
- Rotation: add the new public key to the trusted set/`keys.json`, deploy clients, start signing with the new
  `keyId`, keep the old key verifiable until old signatures expire, then remove/revoke.

## CI secret (placeholder, never committed)

The workflow expects this repository/Environment secret:

```
MARKETPLACE_SIGNING_KEY_PEM   # placeholder name; value = PKCS#8 PEM of the ECDSA P-256 signing key
```

The workflow writes it to a temporary file with restrictive permissions and passes it to the CLI via
`--key-env`. The file is shredded in an `always()` step. **Never** commit the PEM, a base64 copy of it, or a
`.pfx`. Production replacement (target, after MVP): load the key from an HSM/KMS with OIDC
(`id-token: write`) instead of a repository secret.

Maintainer setup steps (one-time):

1. Generate/import a P-256 key in the maintainer's protected store (password manager + offline backup, or
   Azure Key Vault Premium).
2. `fluidbar-plugin keygen --out marketplace-2026.pem` (offline demo path), or export the public SPKI from the
   HSM. Store the private key offline; put the **public** key in `marketplace/keys.json` via a normal PR.
3. GitHub → Settings → Environments → `marketplace-publish`: add `MARKETPLACE_SIGNING_KEY_PEM` and restrict
   deployments to `main`.
4. Create the `marketplace-deploy` environment with a **Pages: Edit** Cloudflare API token
   (`CLOUDFLARE_API_TOKEN`) and set the repository variable `CLOUDFLARE_ACCOUNT_ID`; see
   [publish-runbook.md](publish-runbook.md).

## Rotation runbook (yearly, or on suspicion)

1. Create the new key pair offline/HSM (`<publisher-or-marketplace>-<year>`), record its `keyId`.
2. Open a PR adding the new **public** key to `keys.json`/trusted key list; keep the old entry with `notAfter`.
3. Merge and deploy the client/trust change (client release if compiled-in).
4. Update the `MARKETPLACE_SIGNING_KEY_PEM` secret and `--key-id` input to the new key; run the publish
   workflow to re-sign the index (and republish packages whose signatures must chain to the new key).
5. After the overlap window, mark the old key `revokedAt` in `keys.json`, publish an advisory, remove the old
   secret.

## Compromise / revocation playbook

1. **Block:** mark the affected plugin/version `status: blocked` / `yanked` in the index and republish
   (`blocked` requires a reason; yanked requires `yankReason`). Clients disable installed instances on refresh.
2. **Revoke the key:** set `revokedAt` (+ reason) on the compromised key in `keys.json`, rotate immediately,
   and re-sign the index with the new key (signatures from the revoked key are refused).
3. **Notify:** security advisory (GHSA/CVE id where applicable) + release notes; 24 h triage SLA.
4. **Investigate:** audit the CI run logs/artifacts, compare package hashes with the release assets, keep the
   malicious artifacts for forensics (release assets are never deleted).
5. **Recover:** rebuild suspect packages from reviewed source with the new key, regenerate the index, publish.

## Author/publisher signatures (optional in MVP)

A verified publisher may additionally sign packages with their own P-256 key. The client pins the publisher
`keyId` on first install and warns if it changes. The marketplace countersignature is always required; the
author signature is defense-in-depth, not a replacement. Publisher key changes need a manual verification and a
user re-consent (`docs/rewrite/contracts/plugin.md` §6).

## Rules of the road

- Never commit key material, environment dumps, or signed artifacts containing secrets.
- `validate`/`pack`/`policy` are network-free; only `sign`/`sign-index`/`publish-prepare` touch the key.
- Never sign bytes you did not build from reviewed source. The CI artifact is the only one that goes live.
- Treat the index signing key as the highest-value secret: it authenticates every package hash/URL.
