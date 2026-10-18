# FluidBar v2 — Release, signing, update-feed and performance baseline (M6)

Status: **draft delivered** (branch `v2/rebuild`). Owner: platform-owner; release/signing follows the
Lead's review (`Doulor` maintains signing keys). This document is the operational source of truth for
building, versioning, publishing, signing, rollback and for the measured performance numbers.

---

## 1. Artifacts and versioning

| Item | Value |
|---|---|
| App project | `src/FluidBar.App/FluidBar.App.csproj` (assembly name `FluidBar`, Avalonia shell) |
| Runtime | `win-x64`, single-file bundle (`PublishSingleFile=true`), framework-dependent by default (`--self-contained false`) |
| Version source | explicit `-Version`, else latest `v*` git tag, else `VersionPrefix` in `Directory.Build.props` (currently `2.0.0`) |
| Version stamping | `-p:Version=… -p:FileVersion=… -p:InformationalVersion=<version>+<gitsha>` |
| Release artifact | `build/artifacts/FluidBar-<version>-win-x64.zip` |
| Publish manifest | `build/reports/publish-<version>.json` (version, git SHA, exe/zip SHA-256, size, `signature: unsigned`) |
| Log/diagnostics | `%APPDATA%\FluidBar\logs\fluidbar-*.jsonl` (rolling, 4 MB × 5, rate-limited) |
| User data | `%APPDATA%\FluidBar\` (settings, plugins, agent-events, backups), `%TEMP%\FluidBar\art` (swept) |

The publish manifest's `exeSha256` is the hash the in-app update flow needs; the release workflow uploads
the manifest alongside the zip so the feed can be validated without re-hashing.

## 2. Build & publish locally

```powershell
# Full release build: warning gate + tests + single-file publish + zip + manifest.
pwsh build/publish.ps1

# Explicit version, self-contained bundle, skip tests (local iteration only).
pwsh build/publish.ps1 -Version 2.0.0 -SelfContained -SkipTests

# Outputs:
#   build/publish/win-x64/FluidBar.exe        (single-file bundle)
#   build/artifacts/FluidBar-<version>-win-x64.zip
#   build/reports/publish-<version>.json
```

`build/publish.ps1` fails the build when `src/FluidBar.Platform.Windows` or
`src/FluidBar.Plugins.Builtin` compile with warnings (`-warnaserror` gate for the repo-owned projects).
It then runs `tests/FluidBar.Platform.Tests` and `tests/FluidBar.BuiltinPlugins.Tests` (unless
`-SkipTests`). The App project itself is published; the full solution WIP state is not a publishing gate
because release builds use `dotnet publish` on the app project graph.

## 3. CI release workflow (`.github/workflows/release.yml`)

* **Triggers:** `workflow_dispatch` (manual, optional `version` input) and `push` of tags `v*`.
* **Jobs:** checkout (full history/tags) → setup .NET 10 → `dotnet restore` → `build/publish.ps1` (warning
  gate + tests + publish) → upload `*.zip` + publish manifest as a workflow artifact → for tags, create a
  GitHub Release with `gh release create` (attaches zip + manifest).
* **Permissions:** `contents: read` at workflow level; the release job gets `contents: write` only via
  `GH_TOKEN`/`GITHUB_TOKEN` for the release publication step (no repository write, no packages).
* **Pinned actions:** checkout/setup-dotnet/upload-artifact/… are pinned to major versions today; before
  the first public release they must be pinned to full commit SHAs (Lead review item).
* **Signing:** intentionally absent in the workflow — see §4. `release.yml` currently publishes
  `UNSIGNED` binaries; the release notes state that explicitly.

## 4. Signing (future step, required before public release)

Current state: `signature: unsigned` in the publish manifest. Planned:

1. Authenticode sign `FluidBar.exe` with an EV/OV code-signing certificate (hardware token or Azure Trusted
   Signing) **after** publish and **before** zipping; timestamp the signature (RFC 3161).
2. Write the certificate thumbprint + expected signer subject into the publish manifest.
3. The in-app update checker already has the verification hooks:
   `UpdateCheckClient.DownloadInstallerAsync` → `CompositeUpdateVerifier` = SHA-256 of the downloaded file
   **and** `WinVerifyTrustSignatureVerifier` (Authenticode). A file that is not signed/trusted is deleted
   and the install action is never exposed (`UpdateDownloadResult.CanInstall == false`).
4. Document the offline-key/HSM ceremony and annual rotation in the signing runbook (Lead-owned).

Until signing exists, release notes must say the build is unsigned and the update checker will refuse the
install action on unsigned artifacts (by design: never execute unverified downloads).

## 5. Update feed relationship (GitHub Releases API)

The installed app checks `UpdateClientOptions.ReleaseFeedUrl` (GitHub Releases API JSON) through
`UpdateCheckClient`:

* conditional `GET` with `If-None-Match` (ETag), 8 s timeout, `User-Agent` required by GitHub;
* `ReleaseManifestParser` selects the newest stable/prerelease release, picks the installer asset by
  pattern (`FluidBar.*\.exe$` by default) and reads the SHA-256 digest from `digest` (GitHub's
  `sha256:…` form), `sha256`, `sha256sum` or `integrity` fields;
* download happens **only** from an explicit user action, to `<DataDir>\updates`, `.part` file → verify →
  atomic rename;
* verification = SHA-256 + Authenticode; only a verified file enables the install action; the client never
  executes the installer itself;
* state is exposed via `UpdateStatusSnapshot` (About page) and `CoreSnapshotMapper.ToPayload` for the
  update activity.

If a feed does not expose a digest for the installer asset, the parser leaves `InstallerSha256Hex` empty;
`CanOfferInstaller` is then false and the App should link to the release page instead of offering an
in-place install. Publishing `publish-<version>.json` as a release asset gives the release manager the
authoritative hashes for the release notes/feed metadata.

## 6. Rollback

* Binaries: keep the last N release zips in GitHub Releases; a bad release is deleted/yanked and the
  previous zip is re-downloaded manually. The update client does not auto-downgrade (by design).
* Settings: v2 settings carry a schema version; migrations are forward-only. Downgrading the app after a
  migration requires restoring `%APPDATA%\FluidBar\backups\v1-<ts>` (v1 migration backup) or the v2 settings
  backup written before migration.
* Crash loop: a `crash.marker` written before risky startup work is read on next start (App scope); safe
  mode is documented in the design spec §9.3.
* Feed: yanked releases are filtered by the marketplace/index policy; the update client only reads GitHub
  Releases, so yanking = edit/delete the release and (for strict cases) roll the tag.

## 7. Performance baseline (measured)

Tooling: `build/perf-baseline.ps1` — mode A launches the published App and samples startup, 3-min idle
CPU, working set, handles and threads; mode B (headless, used until the App shell fully consumes the
platform graph) runs the guarded platform composition-root soak (`Category=Soak`, `FLUIDBAR_SOAK=1`) and
renders the same table from its JSON evidence.

### 7.1 Measured numbers — HEADLESS platform composition root (`PlatformServiceGraph`)

| Metric | Value | Budget / target |
|---|---|---|
| Startup (graph start, all monitors + shell services) | `TBD` ms | ≤ 900 ms cold app (App budget) |
| Idle CPU (vCPU-normalized average) | `TBD` % | < 0.3 % target, < 1.0 % max |
| Working set start → end (10 min idle) | `TBD` MB → `TBD` MB | < 120 MB target, < 150 MB max |
| Handle count start → end | `TBD` → `TBD` | < 600 steady, 1 h growth < 60 |
| Thread count start → end | `TBD` → `TBD` | — |
| Gen2 GCs during 10 min idle | `TBD` | 0 |

Machine: `TBD` (CPU/RAM/OS/dotnet).

### 7.2 Measured numbers — published App (if runnable at build time)

| Metric | Value |
|---|---|
| Startup to first window | `TBD` ms |
| Idle CPU avg (3 min) | `TBD` % |
| Working set start → end | `TBD` MB → `TBD` MB |
| Handles / threads | `TBD` / `TBD` |

Known gap: the App shell baseline is only meaningful once `AppBootstrap` consumes `PlatformServiceGraph`
and the real activity pipeline (currently the shell renders design data — UI owner integration in M6).

### 7.3 Soak evidence (platform services)

Reproduce (long runs are excluded from default CI; the soak tests return immediately unless
`FLUIDBAR_SOAK=1`):

```powershell
$env:FLUIDBAR_SOAK='1'
$env:FLUIDBAR_SOAK_MINUTES='10'          # idle scenario
$env:FLUIDBAR_MEDIA_SOAK_MINUTES='30'    # media service scenario
$env:FLUIDBAR_SOAK_REPORT_DIR='build/reports'
dotnet test tests/FluidBar.Platform.Tests --filter 'Category=Soak'
```

| Scenario | Duration | Handles start→end | WS start→end | Idle CPU avg | Result |
|---|---|---|---|---|---|
| Idle platform graph (all monitors, clipboard, notifications, agent inbox, USB/BT, display) | 10 min | `TBD` | `TBD` | `TBD` % | `TBD` |
| GSMTC media service idle (no guaranteed playback on the soak machine) | 30 min | `TBD` | `TBD` | `TBD` % | `TBD` |
| Clipboard 1000 events (real listener) | ~`TBD` s | `TBD` | `TBD` | — | `TBD` |
| Notification storm (5 000 candidates, bounded history) | ~`TBD` s | `TBD` | `TBD` | — | `TBD` |
| USB/BT no-op (part of idle graph) | 10 min | incl. above | incl. above | incl. above | no growth |

Interpretation rules used: handle growth < 100 over the run (guardrail test assertion), working-set growth
< 96 MB, idle CPU < 10 % on a shared CI/dev machine (production target is much tighter; see §7.1).

## 8. Known gaps / follow-ups

1. **Signing not implemented** — release builds are unsigned; `release.yml` publishes with a warning. The
   update installer refuses unsigned/untrusted downloads until §4 is implemented.
2. **App consumes `PlatformServiceGraph`** — the composition root is delivered with a smoke test; the App
   shell still renders design data and is wired by the UI owner. App perf numbers in §7.2 are therefore
   pending.
3. **Long soak in CI** — intentionally excluded (default CI runs only the guarded no-op path); M6 soak
   execution is a manual/release-candidate step.
4. **GitHub asset digest** — GitHub Releases API may not expose a SHA-256 digest for assets in all
   account types; see §5 for the fallback policy.
5. **AV/EDR** — the always-on-top no-activate overlay can be flagged by some EDR products; release notes
   must document behavior; signing (2) is the mitigation.
