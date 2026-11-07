# FluidBar v2 — Release, signing, update-feed and performance baseline (M6)

2026-11-07：**4.2.0 发行流程**统一从 `Directory.Build.props` 读取基础版本，显式版本或标签必须匹配该基础版本。
默认不再读取最近的旧标签。`build/publish.ps1 -Version 4.2.0 -SelfContained` 和 GitHub `v4.2.0` 标签构建均执行完整解决方案回归；普通 CI 通过同一脚本的 `-VerifyOnly` 模式执行相同检查。既有平台 / 内置插件编译警告门禁保留，全仓库分析器建议仍单独跟踪，不宣称全仓库零警告。

版本规则：不兼容变更提升主版本，兼容新增功能提升次版本，修复提升修订号；预发布使用 `-preview.N` 或 `-rc.N`。旧版标签与已发布资产保持不变，不能以预览轮次代替主版本。

2026-11-02：**4.1.0 发行流程**，用户已授权提交与发布。正式发行使用 `main` 上的 `v4.1.0` 标签、
`.github/workflows/release.yml` 和 `build/publish.ps1 -Version 4.1.0 -SelfContained`，提供自包含单文件
EXE、ZIP 和 SHA-256 清单；版本来自 Directory.Build.props 的 4.1.0 构建戳。文件暂存 1.0.3 由现有
签名市场流水线独立发布，最低宿主 4.1.0。4.1 延续 4.0 未签名宿主和强制资产 SHA-256 校验策略。
后文的 M6 性能占位表及历史交付状态不代表本次发行实测；本次验证以 RELEASE-4.1.0.md、STATUS.md
和对应 GitHub Actions 记录为准。

---

## 1. Artifacts and versioning

| Item | Value |
|---|---|
| App project | `src/FluidBar.App/FluidBar.App.csproj` (assembly name `FluidBar`, Avalonia shell) |
| Runtime | `win-x64`, single-file bundle (`PublishSingleFile=true`), framework-dependent by default (`--self-contained false`) |
| Version source | `VersionPrefix` in `Directory.Build.props` (currently `4.2.0`); explicit `-Version` / tag must use the same base version |
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
* **Release notes:** for version `X.Y.Z`, the workflow reads `docs/rewrite/RELEASE-X.Y.Z.md` and passes it with
  `gh release create --notes-file`. Never pass Chinese Markdown through an inline `--notes` argument or a
  PowerShell pipe: Windows PowerShell can encode that path as ASCII and replace every non-ASCII character
  with `?` (the v4.0.0 incident). After creation the workflow compares the GitHub-stored body with the file
  and fails if they differ.

2026-11-02 修正：正文验证使用 `build/verify-release-notes.ps1` 从 `--json body` 提取单个字符串，统一
CRLF/LF 后逐字符比较，避免 PowerShell 把多行原生命令输出当成数组及 Windows checkout 换行误报。
手动运行 release 工作流时可传 `verify_published=true`，同时验证指定版本已经发布的正文。


## 4. Signing (deferred for the unsigned 4.0.0 first release)

Current state: `signature: unsigned` in the publish manifest. Planned:

1. Authenticode sign `FluidBar.exe` with an EV/OV code-signing certificate (hardware token or Azure Trusted
   Signing) **after** publish and **before** zipping; timestamp the signature (RFC 3161).
2. Write the certificate thumbprint + expected signer subject into the publish manifest.
3. The in-app update checker already has the verification hooks:
   `UpdateCheckClient.DownloadInstallerAsync` → `CompositeUpdateVerifier` = SHA-256 of the downloaded file
   **and** `WinVerifyTrustSignatureVerifier` (Authenticode). A file that is not signed/trusted is deleted
   and the install action is never exposed (`UpdateDownloadResult.CanInstall == false`).
4. Document the offline-key/HSM ceremony and annual rotation in the signing runbook (Lead-owned).

For the 4.0.0 first release the updater uses the GitHub asset SHA-256 digest as the integrity gate
and accepts unsigned artifacts; invalid or untrusted signatures are still rejected. Signing remains
the next release gate.

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
