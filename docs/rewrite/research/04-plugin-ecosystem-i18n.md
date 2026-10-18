# FluidBar v2 — Plugin Ecosystem, Marketplace & i18n Architecture

**Status:** Research + design proposal (read-only investigation; no files in `D:\build\GitLocal\FluidBar` were modified)
**Scope:** Windows WPF / .NET 10 dynamic-island app, plugin SDK, in-app marketplace, PR→publish pipeline, localization
**Audience:** FluidBar maintainers, platform/extensibility engineers, release engineering
**Date:** 2026-10-18

---

## 0. Executive summary

FluidBar today has a deliberately small, safe extensibility story: **source-only plugins compiled into the app**, a flat `Plugins/catalog.json`, a lifecycle interface (`IIslandPlugin`), and a PR review process. That was the right MVP. v2 needs three things it does not have:

1. **A real plugin boundary** — a versioned SDK, independently versioned plugin packages, runtime loading/unloading, and contribution points that let plugins add island layouts, hover cards, settings pages, commands, context-menu actions and widgets without forking the app.
2. **A marketplace that is a build pipeline, not a website** — a signed index in git, packages built and signed by CI from reviewed source, and a PR-merge-triggered publish path with rollback.
3. **Localization as a first-class product surface** — app strings, plugin strings, marketplace listings, RTL, plural rules, CJK font fallback and a community translation workflow.

The recommendation in one sentence:

> **Adopt a two-lane plugin model: Lane A (MVP, default) is signed, reviewed, in-process .NET 10 DLL plugins built from a monorepo by CI, with a declarative contribution layer and capability disclosure; Lane B (later) is out-of-process, capability-brokered, declaratively-rendered plugins for unreviewed/risky code. Treat in-process plugins as fully trusted code — signing + review + reproducible CI builds are the security boundary, not a sandbox — and say so explicitly in the UI and docs.**

Key decisions:

| Area | Recommendation |
|---|---|
| Plugin runtime (MVP) | Collectible `AssemblyLoadContext` per plugin, built from source by CI, `EnableDynamicLoading`, shared `FluidBar.Sdk` contract in the default ALC |
| Plugin runtime (later) | Out-of-process host over named pipes (JSON-RPC/MessagePack or gRPC), restricted token / AppContainer, declarative UI only |
| Package | `.fbp` = deterministic ZIP: `manifest.json`, `plugin/` (DLL + deps + deps.json), `locales/*.json`, `assets/`, `LICENSE`, `checksums.json`; the signature is a **detached** `signature.json` published next to the ZIP (never inside it) |
| Trust | Marketplace index signed with an HSM-held key under manual approval (ECDSA P-256 default, Ed25519 optional); packages countersigned by the marketplace after CI build; SHA-256 pinned; publisher key TOFU with change warnings |
| Index hosting | `index.json` + per-plugin shards on GitHub Pages (custom domain if possible), raw.githubusercontent fallback, client ETag cache, bundled bootstrap index |
| Publishing | PR to `fluidbar/plugins` monorepo → CI validates/builds/tests → 1 maintainer approval (2 + security review for risky capabilities / first-time publishers) → merge → CI packages, signs, uploads Release, commits index update; target < 10 min merge→live |
| Marketplace MVP | Browse/search, detail page, install/uninstall/enable, update, rollback, offline ZIP install, capability consent, report; **no ratings/reviews in MVP** |
| App i18n | `.resx` + generated strongly-typed accessors + runtime `LocalizationService`; ship `en-US` + `zh-Hans` first; live switch with restart fallback |
| Plugin i18n | JSON locale bundles per plugin (`locales/<bcp47>.json`), ICU-MessageFormat subset for plurals, fallback chain, per-plugin translation ownership |
| Community translation | Weblate (JSON + resx, GitHub PR integration); Crowdin as alternative; GitHub PR for drive-by fixes |
| Compatibility | `apiVersion` (semver, host supports major), `minAppVersion` / `maxAppVersion`, `engines.fluidbar` range, enforced at install and at load |

**Non-negotiable security statement:** an in-process .NET plugin runs with the user's full rights, can read every file the user can read, every browser cookie, every token, and can P/Invoke anything. Manifest "permissions" are **disclosure and consent**, not enforcement. Only the out-of-process lane with OS-level confinement can enforce. Any design that implies otherwise is misleading and will fail a security review.

---

## 1. What FluidBar v1 actually is (code audit)

The audit matters because the plugin/i18n design must fit the real seams, not an imagined architecture.

### 1.1 Runtime and build

- `FluidBar.csproj`: `net10.0-windows10.0.19041.0`, `UseWPF=true`, `UseWindowsForms=true`, `PublishSingleFile=true`, `Version=1.0.1`, package reference only `System.Management 10.0.0`. Plugins are compiled into `FluidBar.dll`/exe.
- Single-file publish is already the shipping mode. That constrains plugin discovery: `Assembly.Location` is empty for bundled assemblies; plugin roots must be derived from `AppContext.BaseDirectory` (or `Environment.ProcessPath`), never from `Assembly.Location`.
- No `.github/workflows` in the repo. Publishing is manual today; the "PR merged → marketplace" promise requires new CI.

### 1.2 Plugin model today

- `PluginManager.cs` defines `IIslandPlugin` (`Id`, `Name`, `Description`, `Icon`, `Enabled`, `Config`, `EventTriggered`, `Initialize/Start/Stop/Dispose`) and `IPluginConfig` (`Title`, `CreateSettingsPanel()` returning `object`, `Save`, `Load`).
- `PluginManager.Register` subscribes every plugin to the `EventBus`; `SetEnabled` persists to `FluidBarSettings.PluginEnabled` and calls `Start`/`Stop`. There is no isolation, no versioning, no dependency resolution, no contribution registry.
- `PluginCatalog.cs` reads `Plugins/catalog.json` (`id`, `name`, `description`, `category`, `entryPoint`, `status`, `builtIn`). It is documentation metadata, not a loader.
- `Plugins/README.md` and `docs/superpowers/specs/2026-06-14-plugin-ecosystem-design.md` explicitly state: **no runtime loading of arbitrary third-party DLLs**; plugins are source-reviewed and shipped with the app.
- `SettingsWindow.ShowPluginDetail` hard-codes per-plugin settings by id (`"clipboard"`, `"media"`, `"agent-status"`), calling `BuildClipboardPluginSettings`, `BuildMediaPluginSettings`, `BuildAgentStatusPluginSettings`. This is the strongest argument for a declarative settings contribution point: every new plugin otherwise edits the settings window.
- Built-in plugins are registered manually in `App.xaml.cs` (`new ClipboardPlugin()`, `new MediaPlugin()`, `new AgentStatusPlugin()`); `AttachWindow` is called directly on the clipboard plugin — an app-specific escape hatch that must not leak into the public SDK.
- `Plugins/Template/README.md` shows the intended contributor shape; `catalog.json` currently lists four built-ins.

### 1.3 UI surfaces a plugin can influence

- `IslandEvent` (`Source`, `Title`, `Content`, `IconKind`, optional `IslandEventPayload`) and `IslandEventKind` (`Auto`, `Text`, `ScrollingText`, `Progress`, `Status`, `Media`, `Notification`, `Agent`). `IslandEventPayload` carries subtitle, badge, source name, progress, active flag, audio wave, album art/app icon paths, lyric lines, detail lines, playback ticks, verification code.
- `IslandPresentationFactory` computes `IslandViewPresentation` (kind, icon, title, content, status text/badge, progress, wave, target/collapsed width+height, subtitle, source, lyrics, detail lines, art paths). `HoverCardPresentation.FromCompact` fixes hover-card sizes per kind (media 206/232 px, notification 272 px, agent 198 px, etc.) and detail-line counts. **The host owns layout and animation; plugins only supply data.** This is a good invariant to keep.
- `MainWindow` owns the pill, hover card, stack policy, springs, rim/glow, media controls, hidden/snapshot windows. `IslandStackPolicy`, `IslandGroupLayout`, `HoverCardPolicy`, `IslandAnimationPerformancePolicy` are static policy classes.
- `SettingsWindow` is a navigation-rail window with 5 `TabItem`s plus a plugin detail panel (`PluginList`, `PluginDetailPanel`, `DetailEnabledToggle`, `PluginSettingsContainer`). Localization is 100% hard-coded Chinese strings in XAML/code-behind.
- Settings persistence is a single `%APPDATA%\FluidBar\settings.json` via `FluidBarSettings` (`System.Text.Json`, silent catch on failure), with `PluginEnabled` and `MonitorFeatureSettings` dictionaries. There is no per-plugin settings file or schema.

### 1.4 Update mechanism today

`App.xaml.cs` calls `https://api.github.com/repos/Doulor/FluidBar/releases/latest` 4 seconds after startup, compares `tag_name` to the assembly version, and offers download of the `.exe` asset to a temp path with a progress island. There is no signature verification of the downloaded installer and no staged rollout. The marketplace should not copy this pattern verbatim; it needs hash/signature verification and a fallback channel.

### 1.5 Implications

1. The v2 plugin system can evolve the `IslandEvent` data contract rather than replacing it: plugins can keep emitting events, but v2 adds a **layout/contribution descriptor** for richer UI and forwards compatibility.
2. A per-plugin ALC is feasible because the app already uses a small, explicit API surface; the hard part is WPF resource/XAML and unload, not the lifecycle.
3. The settings window must become metadata-driven, or every plugin PR will keep touching first-party UI code.
4. The app is currently mono-culture Chinese; adding English first is not just translation — it requires a string extraction pass, layout re-measurement (English is wider), and a locale-aware font stack.
5. Single-file publishing plus plugins is workable but must be tested explicitly; do not assume `Assembly.Location`.

---

## 2. Ecosystem comparison

Seven ecosystems, compared on the dimensions that matter for FluidBar. The point is not to copy any one of them, but to identify which trade-offs are stable under maintenance.

### 2.1 At a glance

| Dimension | VS Code | Obsidian | Raycast | Figma | PowerToys | OBS Studio | JetBrains |
|---|---|---|---|---|---|---|---|
| Manifest | `package.json` (npm-style) | `manifest.json` + `versions.json` | `package.json` (Raycast schema) | `manifest.json` | `plugin.json` (Run v1) / MSIX `AppExtension` + COM (CmdPal) | none standardized (DLL + data) | `plugin.xml` |
| Distribution index | Marketplace service (Azure DevOps-backed), `vsmarketplace` API | `community.obsidian.md` directory (formerly PR to `obsidianmd/obsidian-releases` → `community-plugins.json`) | Raycast Store, source in `raycast/extensions` monorepo | Figma Community (Figma-hosted) | Store / winget (CmdPal); GitHub (Run v1) | OBS forum Resources + GitHub; no official store | JetBrains Marketplace (REST API) |
| Review / trust | Automated scanning + publisher verification + signature verification; no line-by-line review | Automated review + policy checks; human escalation | Manual review by Raycast team | Manual review by Figma + policy enforcement | Microsoft review for first-party modules; third-party CmdPal apps are Store/winget packages | None; forum listing only | Automated checks + Plugin Verifier; author signing + JetBrains countersign |
| Install / update / rollback | VSIX; auto-update; install specific version via UI/CLI; no first-class rollback | GitHub Release assets (`main.js`, `manifest.json`, `styles.css`); Obsidian updates; reinstall older release manually | Store install/update; no user rollback | Community install/update; version history | Store/winget upgrade; MSIX rollback via Store limitations | Manual download/unzip; user manages versions | IDE install/update; version history; downgrade by installing older ZIP |
| Permissions / consent | `capabilities.untrustedWorkspaces`, `extensionKind`, Workspace Trust; no install-time permission list | None; "Restricted Mode" disables all community plugins | API-level OS prompts; no capability manifest | `networkAccess.allowedDomains` shown on listing; `permissions` (`currentuser`, `payments`, …) | Package capabilities (MSIX) for CmdPal; none for Run v1 | None | Plugin dependencies/extensions; no runtime permission prompt |
| Sandbox / crash isolation | Extension host is a separate process; web extensions in worker; a crashing extension host is restarted | None; plugins run in Electron renderer with Node access | Extensions run out-of-process (Node), host survives crashes | Plugin JS sandbox (no DOM/browser APIs), UI in sandboxed iframe; network allowlist enforced | CmdPal extensions out-of-proc COM; Run v1 in-proc .NET | Native DLL in-process; a bad plugin can crash OBS | In-process JVM; a bad plugin can crash/hang the IDE |
| Ratings | 5-star + Q&A | No star ratings in new directory (downloads/trending) | No public ratings | Likes/duplicates/comments | None | Forum star ratings | 5-star + reviews |
| Compatibility | `engines.vscode` semver | `minAppVersion` + `versions.json` map | Raycast API version + manifest `api`? (platform-managed) | `api` version (not auto-upgraded) | PowerToys version / package identity | OBS ABI/version, manual | `idea-version since-build`/`until-build` |

### 2.2 VS Code extensions

**Manifest.** `package.json` at extension root, required `name`, `version` (semver), `publisher`, `engines.vscode` (semver range, `*` forbidden), optional `displayName`, `description`, `categories` (closed list), `keywords` (≤30), `icon` (≥128 px), `main`/`browser`, `contributes`, `activationEvents`, `extensionDependencies`, `extensionPack`, `extensionKind` (`ui`/`workspace`), `capabilities.untrustedWorkspaces` / `virtualWorkspaces`, `pricing`, `license`, `repository`, `bugs`, `homepage`, `qna`, `sponsor`. The manifest is npm-compatible, which is a double-edged sword: familiar, but it conflates package management with platform metadata.

**Marketplace.** A hosted service (not a git JSON file) with its own API; publishing is `vsce publish` with a Personal Access Token tied to a publisher. Extensions are `.vsix` (zip) with `extension.vsixmanifest` + `[Content_Types].xml`.

**Trust.** No human line-by-line review at publish time; Microsoft runs automated malware scanning, publisher verification (domain ownership), and **extension signature verification** on install (unsigned/tampered extensions are blocked or warned). `Workspace Trust` is a runtime consent model: extensions declare whether they can run in untrusted workspaces, and VS Code restricts capabilities until the user trusts the folder. That is the closest thing to a capability system in the group, and it is about *workspace* trust, not install-time permissions.

**Isolation.** Extensions run in a separate **extension host** process (Node.js or web worker), so a crash/restart does not take down the UI; extensions can be "workspace" or "ui" kind for remote scenarios. This is the strongest crash-isolation model of the seven, achieved by giving up direct UI/DOM access and exposing an RPC API.

**Lessons for FluidBar:** a closed, documented contribution-point list scales; `engines`-style ranges are essential; signature verification is table stakes; a separate host process buys reliability at the cost of API surface.

### 2.3 Obsidian community plugins

**Manifest.** `manifest.json` at repo root: `id` (unique, cannot contain "obsidian"), `name`, `version` (`x.y.z`), `minAppVersion`, `description`, `author`, `authorUrl`, `isDesktopOnly`, optional `fundingUrl`. `versions.json` maps plugin version → minimum Obsidian version so the app can pick a compatible release. `main.js` (bundled), optional `styles.css`. The release tag must match `manifest.version`; users download `main.js`, `manifest.json`, `styles.css` from that GitHub Release.

**Submission.** Historically a PR to `obsidianmd/obsidian-releases` editing `community-plugins.json`; now the [Obsidian Community directory](https://community.obsidian.md) handles submission: link GitHub account, add plugin, automated review provides guidance, fix by publishing a new release. Initial submission only; subsequent releases are picked up from GitHub by the app. Requirements: `README.md`, `LICENSE`, `manifest.json`, policy compliance (no obfuscation, no undisclosed telemetry, no self-updating, etc.).

**Trust / isolation.** Obsidian is explicit that community plugins are third-party code with the same access as Obsidian itself; there is no sandbox. The only safety switch is **Restricted Mode** (community plugins off). Review is automated + policy-based, not a security audit. This is the closest analogue to FluidBar's current model, and it has scaled to thousands of plugins — but it has also produced real malware/credential-theft incidents, which is why "review" alone is not a security story.

**Lessons:** GitHub Releases as package storage works and is cheap; `versions.json` is a neat compatibility map; a directory can be automated and still need a human escalation path; "no sandbox" must be stated loudly.

### 2.4 Raycast extensions

**Manifest.** `package.json` with `$schema`, `name`, `title`, `description`, `icon`, `author`, `categories`, `license`, `commands[]` (`name`, `title`, `subtitle`, `description`, `mode` (`view`/`no-view`/`menu-bar`), `preferences`, `arguments`), `preferences`, `dependencies`, `external`? Extensions are React/TypeScript running on Raycast's Node runtime with a rich `@raycast/api`.

**Distribution.** Source lives in the public `raycast/extensions` monorepo; the Store is a curated, **manually reviewed** catalog maintained by Raycast. Publishing is `npm run publish` → PR → review. The monorepo + CI + review model is very close to what FluidBar needs, minus the scale.

**Isolation.** Extensions run out-of-process from the app (Node), so a crash does not kill Raycast; the API is data/UI-descriptor based. No explicit capability manifest; OS-level permissions (Accessibility, Automation, Files) are requested through the API and the OS.

**Lessons:** a monorepo with review is the simplest way to get "PR merged → published"; command/preference schemas are contribution points; out-of-process is compatible with a great UX if the API is high-level.

### 2.5 Figma plugins

**Manifest.** `manifest.json`: `name`, `id` (Figma-assigned), `api` (API version), `main` (JS), `ui` (HTML or map), `editorType[]`, `documentAccess: "dynamic-page"` (required for new plugins), `networkAccess` (`allowedDomains`, `reasoning`, `devAllowedDomains`), `permissions` (`currentuser`, `activeusers`, `fileusers`, `payments`, `teamlibrary`), `capabilities` (`textreview`, `codegen`, `inspect`, `vscode`), `menu`, `parameters`, `relaunchButtons`, `enableProposedApi` (dev only).

**Trust.** Manual review for Community publishing; `networkAccess` is shown on the plugin's Community page, and `["none"]` is a first-class choice. This is the best example of **declared, user-visible network capability** in the group.

**Isolation.** Plugin code runs in a sandbox with no DOM/browser APIs; UI runs in an iframe; network is restricted to the manifest allowlist. Figma can enforce because the runtime is its own JS engine + iframe, not native code.

**Lessons:** network allowlists are cheap to declare and valuable to users; `api` version pinning without auto-upgrade is respectful of plugin authors; the sandbox is only credible because the platform owns the runtime.

### 2.6 PowerToys modules

Two distinct models:

- **First-party modules** live in the `microsoft/PowerToys` monorepo, reviewed by Microsoft, shipped with the installer. No third-party runtime loading.
- **PowerToys Run v1 plugins** are community .NET DLLs dropped into a folder with a `plugin.json` (id, action keyword, name, description, author, version, language, website, icon, execute file). Loaded **in-process** into the PowerToys process; no sandbox, no signing requirement, no marketplace; distributed via GitHub and community lists. A bad plugin can crash or hang PowerToys.
- **Command Palette (CmdPal, "Run v2") extensions** use MSIX package identity + `uap3:AppExtension` (`Name="com.microsoft.commandpalette"`) + COM server (`CoCreateInstance`), so extensions are **out-of-process** and packaged; discovery via `AppExtensionCatalog`, distribution via Store/winget, with a cache of "frozen" commands for fast startup. This is Microsoft's own migration from in-proc DLL plugins to out-of-proc packaged extensions — a strong signal for FluidBar's long-term direction.

**Lessons:** out-of-proc + package identity is where a mature Windows extensibility platform ends up; in-proc DLL plugins are a fine MVP but accumulate reliability/security debt; a command cache lets an out-of-proc model still start fast.

### 2.7 OBS Studio plugins

**No manifest standard, no marketplace.** Plugins are native DLLs (C/C++) placed in `obs-plugins/<arch>/`, often with data under `data/obs-plugins/<name>/`; distribution is GitHub Releases, the OBS forum "OBS Studio Plugins" Resources category (currently ~331 entries, star ratings, download counts), and occasional bundles. There is no signing requirement, no review, no sandbox, no version solver. A plugin compiled against a different OBS/libobs ABI can crash the app; users are told to match OBS versions manually. The forum provides ratings and a social trust signal, but no install integration.

**Lessons:** the absence of a platform is expensive for users (manual DLL placement, ABI roulette) and for the project (no telemetry on plugin health). Ratings without install integration are weak. FluidBar should not drift into "forum + zip" as its long-term model.

### 2.8 JetBrains plugins

**Manifest.** `plugin.xml` (IDEA plugin descriptor) with `<id>`, `<name>`, `<version>`, `<vendor>`, `<description>`, `<depends>`, `<extensions>`/`<extensionPoints>`, `<actions>`, `<applicationService>` etc.; compatibility via `idea-version since-build` / `until-build` (build-number ranges, not semver). Marketplace metadata (category, tags, changelog, screenshots) is uploaded separately.

**Distribution / trust.** JetBrains Marketplace hosts plugins and exposes a REST API; upload via Gradle `publishPlugin` or web UI. Since 2021.2, plugin signing exists: author signs with an RSA key/certificate chain, Marketplace verifies and **countersigns** with the JetBrains CA; IDEs verify on install and warn if unsigned/revoked. The **Plugin Verifier** checks binary compatibility against IDE builds. Review is automated + policy; paid plugins get more scrutiny.

**Isolation.** In-process JVM; a plugin can throw, leak, deadlock or `System.exit` the IDE. JetBrains mitigates with compatibility verification, signing, and a "disable/restart" UX, not a sandbox.

**Lessons:** build-number/version ranges must be machine-checkable; countersigning by the marketplace is a good pattern (author key + marketplace attestation); a verifier that runs the plugin against the host API is worth building early; in-process native platforms accept the risk and invest in compatibility tooling instead.

### 2.9 Cross-ecosystem conclusions

1. **Manifest minimalism wins.** The ecosystems with the most plugins have small required manifests and optional contribution blocks. Required fields are identity, version, compatibility, entry point, license/readme.
2. **Index hosting is not the hard part; artifact trust is.** GitHub Pages/Releases is enough for a catalog; the differentiator is that artifacts are built from reviewed source and verified by hash/signature.
3. **Review models cluster into three tiers:** (a) no review/no sandbox (OBS, Obsidian in practice), (b) automated scanning + signing (VS Code, JetBrains), (c) manual review + runtime sandbox (Figma, Raycast). FluidBar can afford (b) + a light (c) for risky capabilities, not full (c) for everything.
4. **Crash isolation is orthogonal to UI richness.** VS Code/Raycast/Figma all isolate by giving plugins a data/descriptor API instead of direct UI. FluidBar already has that instinct (`IslandPresentation` owns layout); v2 should formalize it.
5. **Ratings are expensive and gameable.** They are a Phase 3 feature, not MVP. Download counts + report + verified publisher + changelog are enough initially.
6. **Rollback is universally weak.** Almost nobody does it well; FluidBar can differentiate with version pinning + yanking + last-known-good.
7. **Localization is usually bolted on late** in all seven; the ones with strong i18n (VS Code, JetBrains) treat it as a manifest/contribution concern (language packs, localized descriptions), not an afterthought. FluidBar should design locale fields into the manifest and index from day one.

### 2.10 Pattern → FluidBar decision map

| Pattern | Best example | FluidBar v2 decision |
|---|---|---|
| Small JSON manifest + closed contribution points | VS Code, Figma | `manifest.json` schema v2 + templates + typed contributions |
| Monorepo + CI builds artifacts from reviewed source | Raycast | `fluidbar/plugins` monorepo; never publish author binaries |
| Author signature + marketplace countersignature | JetBrains | ECDSA author key (optional/pinned) + required marketplace countersignature |
| Signed index with key rotation | JetBrains, VS Code | Signed `index.json`, `keys.json`, RFC 8785 canonical payload |
| Network allowlist shown to users | Figma | `capabilities.network.domains` + consent sheet |
| Separate process for crash isolation | VS Code, PowerToys CmdPal | Phase 2 out-of-proc lane; in-proc watchdog in MVP |
| Honest "no sandbox" disclosure | Obsidian | Security banner + docs; capabilities are disclosure, not enforcement |
| Compatibility ranges checked by tooling | VS Code `engines`, JetBrains build ranges | semver `engines.fluidbar` + `apiVersion` + `minAppVersion`; validator + client filter |
| Forum ratings without install integration | OBS | Reject; defer ratings until install base and moderation capacity exist |
| Language packs / localized listings | VS Code, JetBrains | Localized manifest fields, plugin JSON bundles, localized listings, Weblate |

---

## 3. C#/.NET plugin loading architecture

This section is the technical core. It assumes .NET 10, WPF, `net10.0-windows10.0.19041.0`, single-file publish, and a hard requirement that a bad plugin must not take down the island.

### 3.1 The three viable architectures

| | A. In-process, collectible ALC | B. In-process, default ALC | C. Out-of-process host |
|---|---|---|---|
| Update without restart | Yes (unload/reload), if no WPF/XAML/native deps | No; requires app restart | Yes (kill/restart child) |
| Plugin UI | Code/descriptor only; XAML pack URIs are painful | Full WPF XAML/UserControl | Descriptor or WebView2 only |
| Crash isolation | None (same process) | None | Full |
| Security boundary | None | None | OS-enforceable (restricted token/AppContainer) |
| Dependency conflicts | Isolated per plugin (except shared contract/WPF) | Shared; conflicts break | Isolated per process |
| Native dependency unload | Not unloaded (leak) | Not unloaded | Process exit cleans up |
| Complexity | Medium-high | Low | High (IPC, packaging, UI DSL) |
| Recommended for | Marketplace plugins (MVP) | First-party/built-ins, trusted UI plugins | Unreviewed/sideloaded/high-risk (Phase 2) |

**Decision:** A for marketplace plugins; B for built-ins (they stay compiled-in for now); C as the isolation lane later. This preserves the "host owns UI" invariant that already exists in `IslandPresentation`/`MainWindow`.

### 3.2 AssemblyLoadContext design

Each plugin gets one collectible `AssemblyLoadContext` with an `AssemblyDependencyResolver` pointed at its entry assembly. The resolver uses the plugin's `.deps.json`, which is why plugin projects must set `<EnableDynamicLoading>true</EnableDynamicLoading>` (generates `deps.json` and copies dependencies to output).

```csharp
using System.Reflection;
using System.Runtime.Loader;

public sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    private static readonly HashSet<string> HostAssemblies = BuildHostAssemblySet();

    public PluginLoadContext(string entryAssemblyPath, string pluginId)
        : base($"FluidBar.Plugin.{pluginId}", isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(entryAssemblyPath);
    }

    private static HashSet<string> BuildHostAssemblySet()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "FluidBar.Sdk", "FluidBar.Contracts"
        };

        // Trusted Platform Assemblies = framework + host app assemblies.
        // Sharing these guarantees ONE type identity for WPF, BCL and the SDK.
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string tpa)
        {
            foreach (var path in tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                set.Add(Path.GetFileNameWithoutExtension(path));
        }

        // Anything already loaded by the host is shared too (e.g. System.Management).
        foreach (var asm in Default.Assemblies)
            if (asm.GetName().Name is { } name)
                set.Add(name);

        return set;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var name = assemblyName.Name ?? "";
        if (HostAssemblies.Contains(name))
            return null; // resolve from the default ALC (host's copy)

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
    }
}
```

The TPA-based rule is better than a prefix list (`System.*`) because it shares exactly what the host already has (including `System.Management`, WPF assemblies and the SDK) while still allowing a plugin-local `System.CommandLine` or `Newtonsoft.Json` that the host does not ship. The trade-off: a plugin cannot override a host assembly version; enforce TFM compatibility and validate that the plugin does not bundle any shared assembly.

Notes and sharp edges:

- **Type identity is everything.** If the plugin loads its own `FluidBar.Sdk.dll`, `(IFluidBarPlugin)instance` throws `InvalidCastException` even though the type names match. Build validation must reject a plugin package that contains any shared contract/WPF assembly. `PrivateAssets="all"` + `ExcludeAssets="runtime"` on the SDK PackageReference, or a direct `<ProjectReference>` with `Private="false"`, plus a CI check.
- **Never share a second WPF.** A plugin loading `PresentationFramework.dll` from its own folder creates duplicate `DependencyObject` types; the result is unpredictable XAML/binding failures. WPF assemblies must come from the host (TPA set above).
- **`AssemblyDependencyResolver` requires the assembly path, not the directory**, and the `.deps.json` must sit next to it. Passing a directory silently resolves nothing.
- **Single-file host:** do not call `Assembly.Location`. Plugin root = `Path.Combine(AppContext.BaseDirectory, "plugins")`; entry path from manifest, validated to stay inside the plugin directory after `Path.GetFullPath` (zip-slip defence).
- **Native libraries cannot be unloaded.** `LoadUnmanagedDllFromPath` pins them until process exit. If a plugin ships native code, it is effectively non-unloadable; either require restart or push it to the out-of-process lane.
- **ReadyToRun is ignored inside collectible ALCs** ([.NET unloadability docs](https://learn.microsoft.com/en-us/dotnet/standard/assembly/unloadability)). Don't tell plugin authors to publish R2R expecting a win; publish plain IL.
- **Trimming/AOT:** WPF has no supported trimming/NativeAOT path on .NET 10. Keep `PublishTrimmed=false` and `PublishAot=false` for the host; plugin authors must not trim their assemblies either, or reflection/JSON/serialization breaks. `PublishSingleFile` for the host is fine; plugin assemblies stay loose on disk.
- **Target framework:** plugin TFM must be ≤ host (`net10.0-windows10.0.19041.0`). Enforce, don't just document.
- **`AssemblyLoadContext` is not a security boundary.** It isolates types and lifetimes, not permissions. Any in-proc plugin can P/Invoke, read `%APPDATA%`, or open a socket.

### 3.3 Plugin contract and host context

Keep the public SDK **small, versioned and WPF-free** so the same contract can later be served over IPC. WPF types in the SDK would make the out-of-process lane impossible without a breaking rewrite.

```csharp
namespace FluidBar.Sdk;

public interface IFluidBarPlugin : IAsyncDisposable
{
    ValueTask InitializeAsync(IPluginContext context, CancellationToken ct);
    ValueTask StartAsync(CancellationToken ct);
    ValueTask StopAsync(CancellationToken ct);
}

public interface IPluginContext
{
    string PluginId { get; }
    Version ApiVersion { get; }
    IIslandPublisher Islands { get; }          // Publish(IslandEvent) + layout descriptors
    IPluginLocalization Localization { get; }    // Get(key), Format(key, args), culture
    IPluginStorage Storage { get; }              // per-plugin directory, settings.json
    IPluginSettings Settings { get; }            // typed accessors over host-owned store
    IPluginLog Log { get; }
    IHttpClientFactory Http { get; }             // host-mediated, capability-checked
    ICommandRegistry Commands { get; }           // register handlers for manifest commands
    INotificationSink Notifications { get; }     // toast/island notifications
    IHostDispatcher Dispatcher { get; }          // marshal to UI thread
    IPluginLeases Leases { get; }                // timers/subscriptions owned by the plugin lifetime
    IReadOnlySet<string> GrantedCapabilities { get; }
}
```

Design rules:

1. **No WPF types in `FluidBar.Sdk`.** `IIslandPublisher.Publish` takes the existing `IslandEvent` record (moved to the SDK) plus a `LayoutDescriptor`. This is what makes IPC feasible later.
2. **All host callbacks are async and cancellable.** A plugin that blocks the UI thread must not freeze the island; the host should call `StartAsync` on a background task and apply a timeout (e.g., 5 s for initialize/start, 100 ms for event handlers).
3. **Every subscription is owned by a `PluginLease`.** The host never stores a plugin object in a static field; the lease owns event handlers, timers and resources and is disposed on unload. This is the single most effective unload-leak mitigation.
4. **Exceptions never escape a plugin callback.** Wrap `Initialize/Start/Stop/Dispose` and event handlers in try/catch; log, increment a failure counter, and disable after 3 crashes in 24 h (persisted). A plugin exception must never reach `Application.DispatcherUnhandledException`.
5. **Capabilities are passed to the context**, so a plugin can gracefully degrade when `network` is denied rather than throwing. For in-proc plugins this is advisory; for out-of-proc it is enforced.

### 3.4 Lifecycle, loading, unload and leak pitfalls

**Load sequence**

1. Read and validate `manifest.json` (schema + compatibility + hash).
2. Verify package signature and `checksums.json`.
3. Extract to a staging directory `%LOCALAPPDATA%\FluidBar\plugins\<id>\<version>\` (atomic rename after verify).
4. Create `PluginLoadContext`, load entry assembly, `GetType(manifest.entry.type)`, check `typeof(IFluidBarPlugin).IsAssignableFrom`, instantiate.
5. `InitializeAsync` with a timeout; on failure, mark "failed to load", keep package for diagnostics, never auto-retry in a loop.
6. `StartAsync`; register contributions (layout IDs, commands, settings).
7. Persist `state.json` (`installedAt`, `lastGoodVersion`, `loadFailures`).

**Unload sequence (cooperative!)**

```csharp
static async Task<bool> UnloadAsync(PluginHandle h, CancellationToken ct)
{
    await h.Plugin.StopAsync(ct).WaitAsync(TimeSpan.FromSeconds(5), ct);
    await h.Plugin.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), ct);
    h.Lease.Dispose();                 // unsubscribe events, stop timers, clear caches
    h.Context.Clear();                 // drop references held by the host
    h.Plugin = null; h.Type = null; h.Assembly = null;

    var wr = new WeakReference(h.LoadContext, trackResurrection: true);
    h.LoadContext.Unload();
    for (var i = 0; i < 10 && wr.IsAlive; i++)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        await Task.Delay(20, ct);
    }
    return !wr.IsAlive;
}
```

`AssemblyLoadContext.Unload` only *starts* unloading. The runtime completes it when no thread has plugin code on its stack and no strong reference (including `GCHandle`, static fields, `RegisteredWaitHandle`, and fields on the ALC subclass itself) points at the plugin's types/instances ([.NET docs](https://learn.microsoft.com/en-us/dotnet/standard/assembly/unloadability)). Practically, expect a first-attempt failure rate; treat unload as best-effort and offer "restart FluidBar to finish removing this plugin" (VS Code and JetBrains both do this).

**WPF-specific leak sources** (this is where most implementations fail):

| Leak | Why it pins the ALC | Mitigation |
|---|---|---|
| Merged `ResourceDictionary` in `Application.Current.Resources` | Host resource tree holds a `ResourceDictionary` whose types/BAML come from plugin assembly | Never merge plugin dictionaries into app resources; if unavoidable, remove on unload and clear caches |
| Styles/templates applied to host elements | `Style`/`ControlTemplate` instances reference plugin types | Remove styles, reset `Style = null`, recreate elements |
| `DependencyProperty` metadata / `PropertyMetadata` defaults | WPF caches metadata per type globally | Avoid custom DPs from plugins; if used, accept restart-to-unload |
| Static events / `WeakEventManager` | Static delegate roots keep instances alive | Lease-owned subscriptions; explicit unsubscribe |
| `DispatcherTimer`, `System.Threading.Timer`, `Task.Run` loops | Timer/thread roots keep delegates alive | Cancel + dispose + await completion in `StopAsync` |
| Bindings / `DataContext` | Binding engine keeps source and converter references | `BindingOperations.ClearAllBindings`, `DataContext = null`, remove from visual tree |
| `HwndSource` / extra windows | Native window + WPF objects | Close windows, dispose sources |
| `JsonSerializerOptions` static caches, `Regex` cache, `TypeDescriptor` | Framework caches keyed by type | Avoid static caches; use instance options; don't rely on unload for these |
| `ResourceManager` / satellite assemblies | Resource sets cache types/strings | Recreate per plugin; don't share |
| `Baml2006SchemaContext` / XAML type caches | WPF caches XAML schema per assembly | No XAML in marketplace plugins (MVP rule) |
| Native DLLs | Not unloaded by ALC | Out-of-proc or restart |
| Finalizers / `GCHandle` | Finalizer queue roots | Avoid finalizers; dispose handles |

The blunt rule: **if a plugin touches WPF XAML or native code, plan for restart-to-update.** That is acceptable for v2 and removes the hardest class of bugs.

**Testing unloadability.** Build a harness that loads a plugin, calls `Stop/Dispose`, drops references, and asserts a `WeakReference` to the ALC dies after a GC loop. Run it in CI for every marketplace plugin with a "no-leak" smoke test. Also run a 100× load/unload cycle and watch `PrivateBytes`/`GC.GetTotalMemory` for growth; a leak of one ALC per cycle is easy to detect.

### 3.5 WPF XAML / ResourceDictionary from plugin assemblies

Five options, in order of preference for FluidBar:

1. **No plugin XAML; descriptor-rendered UI (recommended).** The manifest declares layouts and settings; the host renders them with host controls, host theme tokens, host localization and host accessibility. Plugins supply data and metadata. This is what VS Code/Raycast/Figma do at the API level. It also makes out-of-proc and localization tractable.
2. **Code-built UI through SDK builder methods.** Plugin calls `context.UI.CreateSettingsPage(...)` with a fluent descriptor; host builds the visual tree. Same safety/themability as (1) with more flexibility, still no XAML.
3. **Plugin `ResourceDictionary` via pack URI, in-proc only, trusted.** `new ResourceDictionary { Source = new Uri("pack://application:,,,/MyPlugin;component/Themes/Plugin.xaml", UriKind.Absolute) }`. This requires WPF's pack URI resolver to find `MyPlugin` via `Assembly.Load` in the **default** ALC; with a collectible ALC it will usually fail unless you hook `AssemblyLoadContext.Default.Resolving` and load a second copy (type-identity split, leaks). It also merges plugin resources into host scope. If you allow it: prefix every key (`myplugin.brush.accent`), document the theme-token contract, remove dictionaries on unload, and require app restart after update.
4. **Loose XAML via `XamlReader.Load`.** Flexible but dangerous: XAML can instantiate arbitrary types (`ObjectDataProvider`, `Process`, etc.). Only with a restrictive `XamlSchemaContext` that whitelists host SDK types, no `x:Class`, no event handlers, no `ObjectDataProvider`. Not worth it for v2.
5. **WebView2 for custom UI.** Real isolation (web content sandbox), but ~100 MB+ extra memory, a runtime dependency, accessibility/theming mismatch, and a JS bridge that becomes a new security surface. Later, for rich plugin UIs only.

**Recommendation:** MVP allows (1) and (2); (3) only for first-party/verified plugins with an explicit `ui.xaml: true` manifest flag and restart-to-update semantics; (4)/(5) post-MVP.

### 3.6 Out-of-process plugin host (Phase 2)

**When it is worth it:** unreviewed/sideloaded plugins, plugins with native dependencies, plugins needing `shell`/broad file access, and any future "run untrusted code" story. It is the only way to get real crash isolation and OS-enforced capabilities.

**Transport options**

| Transport | Pros | Cons |
|---|---|---|
| Named pipes + length-prefixed JSON (`System.Text.Json`) | No extra deps; easy to debug; good enough for island events (small payloads) | Hand-rolled framing, backpressure, cancellation |
| Named pipes + MessagePack | Compact, fast | Schema evolution discipline; binary debugging |
| `StreamJsonRpc` over named pipes | Mature JSON-RPC 2.0, notifications, cancellation, out-of-proc object proxies; used by Visual Studio | Extra dependency; API surface can grow unintentionally |
| gRPC over named pipes | Typed contracts, streaming, tooling | Pulls ASP.NET Core/Kestrel into a desktop app; heavier install; named-pipe transport is supported ([docs](https://learn.microsoft.com/en-us/aspnet/core/grpc/interprocess-namedpipes)) |

**Recommendation:** `StreamJsonRpc` over a current-user-only named pipe for Phase 2. It gives request/response, notifications and cancellation with far less code than gRPC, and no ASP.NET Core dependency. Define the contract as DTOs in `FluidBar.PluginProtocol` (shared, AOT-friendly, no WPF).

**Host hardening**

- Pipe ACL: current user SID only (`PipeOptions.CurrentUserOnly` or an explicit `PipeSecurity`); random pipe name per session; handshake with a nonce; reject remote clients.
- Child process: `UseShellExecute=false`, `CreateNoWindow=true`, working directory inside plugin folder, environment scrubbed of secrets.
- Restricted token: `CreateRestrictedToken` (disable admin SIDs) + `SetTokenInformation(TokenIntegrityLevel, Low)`; job object with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`, `ProcessMemoryLimit`, `JOB_OBJECT_LIMIT_JOB_TIME`/CPU rate; `SetProcessMitigationPolicy` (ACG/CIG/CFG) where compatible.
- AppContainer: strongest, but it blocks most filesystem/network unless capabilities are granted; many useful plugins (media, clipboard, shell) would break. Offer it as "Untrusted mode" with brokered APIs, not the default.
- Watchdog: heartbeat every 5 s; kill and restart with exponential backoff; disable after N failures.
- UI: descriptor-only. The child sends `LayoutDescriptor` + data; the host renders. No HWND embedding in v2.

**Costs:** IPC schema versioning, serialization overhead (island events are small, so fine), two-process debugging, packaging (child executable + runtime), and a second API surface to document. Do not build this before the in-proc marketplace has real usage.

### 3.7 Security of untrusted code: what is actually enforceable

**Threat model.** A malicious plugin wants: credentials/tokens (browser cookies, SSH keys, cloud CLIs), files, crypto wallets, persistence, network exfiltration, or to pivot into other apps. An in-proc .NET plugin has all of that by default. No manifest field changes that.

| Control | In-proc (Lane A) | Out-of-proc (Lane B) |
|---|---|---|
| Code review | Yes, human + automated | Light review or none |
| CI-built artifact (not author binary) | Yes | Yes |
| SHA-256 + signature | Yes | Yes |
| Publisher identity / key pinning | Yes (TOFU + change warning) | Yes |
| Capability disclosure + consent | Yes (advisory) | Yes (enforced) |
| Network allowlist | Advisory (host HTTP client) | Enforced by broker |
| File scope | Advisory (host storage API) | Enforced (AppContainer/broker) |
| Shell/process execution | Consent + review | Denied or brokered |
| Crash isolation | No (watchdog + disable) | Yes |
| Memory/CPU limits | No | Job object |
| Secret access | Full user rights | Restricted token/AppContainer |

**Signing layers**

1. **Authenticode** on `FluidBar.exe` and first-party DLLs (SmartScreen, Windows trust). Third-party plugin authors are not required to buy a certificate.
2. **Detached package signature** (Ed25519 or ECDSA P-256) over the SHA-256 of the `.fbp` zip, plus a `checksums.json` for every file. The BCL has ECDSA/RSA but no public Ed25519 primitive; use `NSec`/BouncyCastle for Ed25519 or standardize on ECDSA P-256 (built-in, simpler). minisign compatibility is nice for offline verification but not required.
3. **Marketplace countersignature**: after CI builds the package, the marketplace signs it with a key held in an HSM (manual approval for the root index key, automated for the package key). Clients trust "built and reviewed by FluidBar" even if the author key is unknown. This mirrors JetBrains' author-sign + Marketplace-countersign model ([Plugin Signing](https://plugins.jetbrains.com/docs/intellij/plugin-signing.html)).
4. **Index signature**: the whole `index.json` is signed; clients verify before trusting hashes/URLs. Key rotation via `keyId` + a signed key list; revocation list with `revokedAt`.
5. **Pin on first install**: store publisher key fingerprint; a later package signed by a different key requires an explicit "publisher key changed" consent (SSH-style).

**Other mitigations**

- Default-deny for high-risk capabilities: `shell.execute`, `files.write` outside the plugin data dir, `network` wildcard, `process.inject`, `registry.write`, `hotkeys.global`. These require a second reviewer and a written justification in the PR.
- Install-time consent screen listing capabilities in plain language ("Can read files in your Documents folder", "Can access the internet: api.example.com"), with a link to the manifest section.
- Runtime audit log (plugin id, capability, target, timestamp) visible in settings; exportable for incident response.
- Kill switch: index can mark a plugin `blocked` with a reason; clients disable it on next refresh even if installed.
- Reproducible builds: `Deterministic=true`, pinned SDK, locked restore (`packages.lock.json`), build in a container, publish build provenance/attestation; users can rebuild and compare hashes.
- Dependency hygiene: `dotnet list package --vulnerable --include-transitive` in CI, banned-package list, no post-build download scripts, no `Install-Package` at runtime.
- Zip safety: reject absolute paths, `..`, symlinks, duplicate entries, > 50 MB uncompressed, > 5,000 files; verify every extracted file against `checksums.json`.
- DLL search order: call `SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_DEFAULT_DIRS)` at startup and load plugin natives with `LoadLibraryEx(..., LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR)` to avoid planting attacks.
- Never run plugin installers/elevated code; installation is file copy + registry-free; no admin prompt.

**What not to claim.** Do not call manifest capabilities a "sandbox". Do not say "reviewed plugins are safe". Say: "Plugins run with the same access as FluidBar. We review and sign what we publish; install plugins you trust." That honesty is itself a security feature (informed consent), and it matches Obsidian's approach while adding stronger supply-chain controls.

---

## 4. Distribution & marketplace

### 4.1 Topology

```
fluidbar/FluidBar            (app source, SDK, docs; built-ins)
fluidbar/plugins             (monorepo: plugins/<id>/ source + manifest + locales)
  └─ .github/workflows/validate-plugin.yml
  └─ .github/workflows/publish-plugin.yml
fluidbar/marketplace         (generated index + Pages, or a gh-pages branch)
  └─ index.json, index.sig, keys.json, plugins/<id>.json,
     listings/<id>/<bcp47>.json, stats.json, .nojekyll
GitHub Releases (in fluidbar/plugins)
  └─ plugin-<id>-v<version> / fluidbar-plugin-<id>-<version>.fbp
     + signature.json (detached) + SBOM
```

Why a monorepo for marketplace plugins:

- **Build from reviewed source**, not from author-uploaded binaries. This kills the bait-and-switch attack (reviewed source, different binary) and is what Raycast does at scale.
- One CI, one review policy, one index generator.
- Easy dependency pinning against the current SDK.
- Downside: repo growth and a lower bus factor for the monorepo; mitigate with per-plugin folders, CODEOWNERS, and a size budget.

Allow **external repositories** later for large plugins, but still require CI to build from a pinned commit into the marketplace release; never trust a third-party release asset.

### 4.2 Index hosting: raw vs Pages vs Releases

| Option | Pros | Cons | Use for |
|---|---|---|---|
| `raw.githubusercontent.com` | Zero setup, CDN, no build step, versioned by commit | No custom headers (cache-control), rate limits, ugly URL, `main` can change under you | Bootstrap/fallback; mirrors |
| GitHub Pages | Custom domain, proper `Cache-Control` (via `_headers` on some hosts; Pages itself is limited), HTTPS, atomic deploys | Build/deploy step, 1 GB site limit, 100 GB/month soft bandwidth, no dynamic API | Primary index + listing pages |
| GitHub Releases | Assets up to 2 GB, immutable-ish, checksums, download counts, CDN redirects | API quota for listing (60/h unauthenticated), asset names flat per tag | Plugin packages |
| Third-party CDN (Cloudflare/R2) | Full header control, WAF, analytics, global | Cost/ops, another account, trust | Later, if Pages limits bite |

**Recommendation:** index on **GitHub Pages** under `marketplace.fluidbar.app` (or `doulor.github.io/fluidbar-marketplace` initially), with `raw.githubusercontent.com` as a compiled-in fallback and a bundled `index.bootstrap.json` in the installer so first-run works offline. Packages as Release assets in `fluidbar/plugins`, referenced by absolute URL + SHA-256 in the index. Never use the GitHub API on the client hot path; use `raw`/Pages and direct `releases/download` URLs (which 302 to a CDN and do not consume API quota).

**Index shape and size.** Keep `index.json` under ~1 MB (gzip ~150 KB) with latest version per plugin. Put full version history in `plugins/<id>.json` (fetched on demand for the detail page/rollback), and localized long descriptions in `listings/<id>/<locale>.json` (fetched on demand). Shard by first letter if the catalog exceeds ~2,000 plugins (`index.a.json`, …), with a root `index.json` that lists shards. Client caches by ETag/`If-None-Match` and stores `lastIndexHash`.

**Index generation is code, not hand-editing.** A `tools/marketplace-gen` console app reads every `plugins/*/manifest.json`, validates, sorts deterministically, computes hashes from the published release assets, and writes JSON with stable key order. The generated files are committed by a bot with `[skip ci]`; a human never edits `index.json`.

### 4.3 Package format

`.fbp` (FluidBar Plugin) = ZIP, deterministic (fixed timestamps, sorted entries, no extra fields):

```
manifest.json
plugin/
  FluidBar.Plugin.MyThing.dll
  FluidBar.Plugin.MyThing.deps.json
  <third-party deps>.dll
  runtimes/win-x64/native/...        (optional; discouraged)
locales/
  en-US.json
  zh-Hans.json
assets/
  icon.png                          (256x256, PNG)
  screenshots/01.png ...
LICENSE
README.md
checksums.json                      (path -> sha256, excludes itself)
```

`signature.json` is **not** inside the ZIP. It is published as a sibling asset (`fluidbar-plugin-<id>-<version>.fbp.sig.json` or `signature.json` in the same release) because a signature that covers the ZIP hash cannot be embedded in the ZIP it hashes — that would be circular. If a single-file offline hand-off is required, use a sidecar `.sig` file or a small container (`.fbp` + `.sig`) rather than embedding.

Rules:

- `manifest.json` at ZIP root; entry paths relative, forward slashes only.
- No `FluidBar.Sdk.dll`, no WPF assemblies, no `FluidBar.exe`, no `.pdb` in the marketplace package (symbols can be a separate artifact).
- `checksums.json` covers every file except itself; `signature.json` (detached) signs the ZIP SHA-256 (`package.sha256`) and carries key metadata. The index repeats the same ZIP hash, so a client can verify before extraction.
- Maximum 50 MB compressed / 150 MB uncompressed / 5,000 entries; reject encrypted entries.
- License required; README required; icon required for marketplace listing.
- Deterministic build: `-p:Deterministic=true -p:ContinuousIntegrationBuild=true`, fixed `SourceDateEpoch` for ZIP timestamps. Two CI runs must produce identical SHA-256; if not, investigate before publishing.

#### Why ZIP, not `.nupkg` or `.msix`

| Format | Fit for FluidBar | Verdict |
|---|---|---|
| ZIP (`.fbp`) | Simple, inspectable, deterministic, no NuGet client/runtime dependency, works offline, easy to diff/inspect, no package-manager semantics (feeds, versions, dependency graphs) that we don't want | **Chosen** |
| NuGet `.nupkg` | Familiar to .NET authors; built-in versioning and dependency metadata; `dotnet pack` works; but NuGet is designed for compile-time libraries, `PackageReference` resolution and feeds. Installing a plugin via NuGet means a package source, global-packages folder, restore semantics, and a second versioning system alongside our index. `.nupkg` can carry arbitrary content (it is a ZIP), but tooling assumes lib/ref/build assets. Also, NuGet's own signature scheme (author/repository signatures) is different from our package signature and adds complexity. | Use only as an authoring/build intermediate, not the distribution format |
| MSIX / AppX | OS-level package identity, clean install/uninstall, Store distribution, AppContainer option; but requires packaging identity/signing, is heavy for a 2 MB plugin, complicates dev-loop install, and AppContainer breaks most useful plugins. Good for the future isolated lane (CmdPal-style), not for v2 in-proc plugins. | Later, for isolated/Store lane |
| Plain DLL drop-in | What OBS/PowerToys Run v1 do; no manifest, no integrity, no versions, no rollback, AV noise, DLL planting risk | Rejected |

A `.fbp` is deliberately **not** a NuGet feed citizen. If the ecosystem later wants `dotnet add package`-style authoring, `dotnet pack` can produce an intermediate `.nupkg` that the publish tool converts to `.fbp`; the marketplace never serves `.nupkg`.

### 4.4 Signing and key management

**Algorithms.** ECDSA P-256 with SHA-256 is the pragmatic default: built into .NET (`ECDsa`), supported by HSM/KMS, no extra dependency, and sufficient for package signing. Ed25519 is smaller/faster and minisign-compatible, but the BCL has no public Ed25519 API as of .NET 10; it requires NSec/BouncyCastle/libsodium. If interoperability with minisign is a goal, use Ed25519 via NSec and document the dependency; otherwise ECDSA P-256.

**Keys.**

| Key | Location | Signs | Rotation |
|---|---|---|---|
| Marketplace index key | Offline/HSM (Azure Key Vault Premium + OIDC, or offline ceremony) | `index.json`, `keys.json` | Yearly; `keyId` in signature; old key kept for verification |
| Marketplace package key | HSM/KMS, CI access via OIDC + protected environment | `.fbp` countersignature | Yearly |
| Publisher key (author) | Author's machine/CI | `.fbp` author signature | Author-controlled; fingerprint pinned client-side |
| Authenticode cert | CA (OV/EV) | `FluidBar.exe`, first-party DLLs | Per CA rules |

#### Signing options compared

| Option | What it proves | Cost / friction | Revocation | Recommendation |
|---|---|---|---|---|
| **Authenticode** (`signtool` + code-signing cert) | The PE was signed by the cert subject; Windows/SmartScreen trust | OV/EV cert cost (EV ~$300–600/yr); hardware token/HSM; every build signed; SmartScreen reputation for new certs | Cert revocation, timestamping | Sign `FluidBar.exe` and first-party DLLs; do not require for community plugins |
| **Ed25519 detached** (minisign-compatible) | Holder of the private key signed the package hash | Tiny keys/signatures; excellent offline tooling (`minisign -S`); BCL has no Ed25519 API through .NET 10 → NSec/BouncyCastle/libsodium dependency | Publish revocation list; key id in signature | Strong option if minisign interop matters; caveat: extra crypto dependency |
| **ECDSA P-256 detached** | Holder of the private key signed the package hash | Built into .NET (`ECDsa`), supported by Azure Key Vault/HSM, no extra dependency; larger signatures than Ed25519 | Revocation list + key rotation | **Default for FluidBar** (marketplace + author keys) |
| **RSA-4096 detached** | Same as ECDSA | Built in, universally supported, slow/large | Revocation list | Accept for author keys for compatibility (JetBrains uses RSA) |
| **GitHub artifact attestation / Sigstore** | Build provenance (which workflow/commit produced the artifact) | Free for public repos, transparency log; requires OIDC and client verification support | Transparency log | Add in v2.2 as a **second** attestation, not a replacement for package signing |
| **No signature + SHA-256 only** | Integrity against corruption, not tampering (whoever controls the index can change the hash) | Zero | None | Never for marketplace; acceptable only for local dev/sideload with a warning |

**Why both Authenticode and detached signatures?** Authenticode is what Windows/Defender/SmartScreen understand, but it cannot sign a ZIP or a JSON index, and requiring every plugin author to buy a certificate would kill the ecosystem. Detached signatures cover the package and index, work offline, and can be verified by the app; Authenticode covers the app binary and first-party components. JetBrains uses the same two-layer idea: author signature + Marketplace countersignature, with the IDE verifying the chain.

**Signature file**

```json
{
  "schemaVersion": 1,
  "package": {
    "sha256": "9f2c1b7e5d4a3c2b1a0f9e8d7c6b5a4f3e2d1c0b…",
    "size": 1843200
  },
  "signatures": [
    {
      "keyId": "fluidbar-marketplace-2026",
      "role": "marketplace",
      "algorithm": "ECDSA-P256-SHA256",
      "publicKey": "base64-spki",
      "value": "base64-signature",
      "signedAt": "2026-10-18T09:00:00Z"
    },
    {
      "keyId": "acme-2026",
      "role": "author",
      "algorithm": "ECDSA-P256-SHA256",
      "publicKey": "base64-spki",
      "value": "base64-signature",
      "signedAt": "2026-10-18T08:55:00Z"
    }
  ]
}
```

Both signatures cover the same `package.sha256` and live in the **detached** `signature.json`. The index entry repeats `sha256`, `size`, `keyId` and `signatureUrl` so a client can verify even if the sidecar is malformed or missing. Verification order: index signature → entry hash matches downloaded bytes → package signature by the key pinned for that publisher (or the marketplace key) → per-file checksums after extraction. Any mismatch aborts install and quarantines the file.

**Key compromise playbook.** Publish `keys.json` with `revokedAt` + `revokedReason`; clients refuse new installs with revoked keys and warn on already-installed plugins; force-update index; publish a security advisory; rotate; re-sign all marketplace packages with the new key (or re-publish versions under the new key id).

### 4.5 CI: validate on PR, publish on merge

**PR validation** (`.github/workflows/validate-plugin.yml`), triggered on `pull_request` paths `plugins/**`:

```yaml
name: Validate plugin PR
on:
  pull_request:
    paths: ["plugins/**", "schemas/**", "tools/**"]
permissions:
  contents: read
  pull-requests: write
jobs:
  validate:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: "10.0.x" }
      - name: Detect changed plugins
        id: changed
        shell: pwsh
        run: |
          $base = "${{ github.event.pull_request.base.sha }}"
          $files = git diff --name-only $base HEAD -- plugins
          $ids = $files | ForEach-Object { ($_ -split '/')[1] } | Sort-Object -Unique
          "ids=$($ids -join ',')" >> $env:GITHUB_OUTPUT
      - name: Validate manifest schema
        shell: pwsh
        run: dotnet run --project tools/FluidBar.PluginTool -- validate --all --strict
      - name: Build and test each plugin
        shell: pwsh
        run: dotnet run --project tools/FluidBar.PluginTool -- build --all --configuration Release
      - name: Package dry-run (hash + size + contents)
        shell: pwsh
        run: dotnet run --project tools/FluidBar.PluginTool -- pack --all --out artifacts
      - name: Security & policy checks
        shell: pwsh
        run: |
          dotnet run --project tools/FluidBar.PluginTool -- policy --all
          # no committed binaries, no Sdk/WPF assemblies, license/readme/icon present,
          # locale keys valid, banned APIs flagged, secrets scan (gitleaks), SBOM generated
      - uses: actions/upload-artifact@v4
        with: { name: plugin-artifacts, path: artifacts/** }
```

`FluidBar.PluginTool` is the single source of truth for validation, used identically by contributors locally (`dotnet run -- validate --plugin plugins/foo`), by CI, and by the client installer (schema version check). Do not duplicate rules in YAML.

**Gates (all must pass):**

1. JSON Schema validation against `schemas/plugin.schema.json` (strict: unknown properties rejected or warned).
2. Identity: `id` matches `^[a-z0-9]([a-z0-9-]{1,38}[a-z0-9])?$`, not reserved (`fluidbar`, `system`, `settings`, `marketplace`, `core`, `api`, `plugin`), not already owned by another publisher; publisher consistent across versions.
3. Version: semver, greater than the latest published version; no reuse; changelog entry.
4. Compatibility: `apiVersion` major supported by current app; `minAppVersion <= current stable`; `maxAppVersion` sane; `engines.fluidbar` range valid.
5. Build: Release, warnings-as-errors for SDK usage, TFM check, no shared assemblies copied, `deps.json` present, entry type implements `IFluidBarPlugin`.
6. Tests: at least one smoke test that loads/unloads the plugin in a host test harness; unload leak test.
7. Package: deterministic hash, size limits, no forbidden files, checksums valid.
8. Locales: `en-US` complete; all keys referenced exist; placeholders balanced; no missing plural forms for languages that need them; locale codes canonical.
9. Policy: license OSI-approved or explicit; README with description/screenshots; no obfuscation/minified-only source; no telemetry without disclosure; no auto-update code; no bundled secrets; no downloaded-at-runtime binaries.
10. Capabilities: every high-risk capability has a `reason`; `shell`/`files.write`/`network:"*"` trigger the security label and require a second reviewer.
11. Human review: CODEOWNERS approval, UX check (screenshots), i18n check, security review if labelled.

**Publish on merge** (`.github/workflows/publish-plugin.yml`), triggered on `push` to `main` paths `plugins/**`, with a protected `environment: marketplace-publish` (required reviewers for first publish / high-risk changes):

```yaml
name: Publish plugin
on:
  push:
    branches: [main]
    paths: ["plugins/**"]
concurrency: { group: marketplace-publish, cancel-in-progress: false }
permissions:
  contents: write
  id-token: write          # OIDC to Key Vault for signing
jobs:
  publish:
    runs-on: windows-latest
    environment: marketplace-publish
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: "10.0.x" }
      - name: Build, test, pack (Release)
        run: dotnet run --project tools/FluidBar.PluginTool -- publish-prepare --all
      - name: Azure login (OIDC) and sign packages
        uses: azure/login@v2
        with:
          client-id: ${{ secrets.MARKETPLACE_SIGNING_CLIENT_ID }}
          tenant-id: ${{ secrets.MARKETPLACE_SIGNING_TENANT_ID }}
          subscription-id: ${{ secrets.MARKETPLACE_SIGNING_SUBSCRIPTION_ID }}
      - name: Sign + upload release assets
        shell: pwsh
        run: |
          dotnet run --project tools/FluidBar.PluginTool -- sign --all --keyvault $env:KEYVAULT
          dotnet run --project tools/FluidBar.PluginTool -- release --all --repo fluidbar/plugins
      - name: Regenerate and sign marketplace index
        shell: pwsh
        run: |
          dotnet run --project tools/marketplace-gen -- --plugins ./plugins --out ./marketplace
          dotnet run --project tools/marketplace-gen -- sign-index --keyvault $env:KEYVAULT
      - name: Deploy index to Pages
        uses: actions/deploy-pages@v4
        with: { artifact_name: marketplace }
```

Implementation notes:

- **Idempotency:** if `plugin-<id>-v<version>` already exists, fail unless the artifact hash matches; never overwrite a published version.
- **Only changed plugins:** compute the set from the push diff; a full rebuild is slow but a nightly job can reconcile drift.
- **First publish:** require a maintainer to approve the `marketplace-publish` environment; this is the human gate for new publishers.
- **No author-uploaded binaries:** the release asset is the CI-built one. If a contributor needs a native build, use a pinned build container/SBOM and review it explicitly.
- **Post-publish verification:** a separate job downloads the asset from the release URL, re-verifies hash/signature and index entry, and fails the workflow if they disagree. This catches CDN/deploy races.
- **Timing:** target merge→index live < 10 minutes (build ~2–4 min, Pages deploy ~1–2 min, cache max-age 5 min).
- **Notifications:** comment on the merged PR with the release URL, index entry, hash and signature key id.

### 4.6 Client install / update / rollback state machine

```
Available → Downloading → Verifying (hash → signature → checksums)
   → Extracting (zip-safe) → Staged → Activating (ALC load, Init/Start)
   → Active
Failures: DownloadFailed, HashMismatch, SignatureInvalid, Incompatible,
          LoadFailed, CrashLoop → Disabled (with reason + log path)
```

- **Atomic activation:** install into `plugins/<id>/<version>/`, write `state.json` `{activeVersion, previousVersion, pinned}` only after successful `Start`. On failure, keep the old version active.
- **Update policy:** check at startup (delayed 5 s) and every 24 h; channels `stable` (default) and `beta` (opt-in). Index entries carry `channel` and `rollout` (0–100); client uses a stable hash of `pluginId + machineId` to decide staged rollout.
- **Rollback:** detail page offers "Version history" with `rollback` to any retained version; keep the last 2 versions on disk; if the active version crashes 3× in 24 h, auto-rollback to `lastGoodVersion` and notify.
- **Disable vs uninstall:** disable stops the plugin but keeps files; uninstall removes the directory and settings (with a "keep settings" checkbox).
- **Offline install:** "Install from file…" accepts `.fbp`; if unsigned, show a prominent "Unverified — only install if you trust the source" confirmation and record it in the audit log. Air-gapped deployments can point `marketplace.source` at a file share/HTTP mirror.
- **Update requires restart:** if unload fails (`WeakReference.IsAlive`), mark `restartRequired` and show a non-blocking island notification; do not force restart while an island is visible.
- **Concurrency:** one install/update at a time per plugin; a global lock file in `%LOCALAPPDATA%\FluidBar\plugins\.lock`; cancel on app shutdown with a clean staging cleanup.
- **Disk layout:** `%LOCALAPPDATA%\FluidBar\plugins\<id>\<version>\` (code), `%APPDATA%\FluidBar\plugins\<id>\settings.json` (user data), `%LOCALAPPDATA%\FluidBar\logs\plugins\<id>.log` (rotating, 5 MB × 3). Never write plugin code into `Program Files` (no admin, no AV false positives).

#### Marketplace UX flow

1. **Entry points:** Settings → Plugins → "Browse marketplace"; tray menu → "Plugins…"; command palette → "Install plugin"; clicking a `.fbp` file; an island action "Get more plugins" in the empty state.
2. **Browse:** search (localized names + English), category chips, sort by relevance/downloads/updated, filter "Compatible with this version", "Installed", "Updates". Show a compact card: icon, name, publisher + verified badge, summary, rating placeholder (hidden in MVP), install/update button.
3. **Detail:** hero (icon, name, publisher, version, compatibility), screenshots with localized alt text, markdown description in the current UI language with "translated from English" note when applicable, capability chips with plain-language labels, changelog, version history, links (repo/homepage/license), report link.
4. **Install consent sheet:** "This plugin can: access the internet (api.example.com), read the current media session, copy to clipboard, register Ctrl+Alt+M. Plugins run with the same access as FluidBar. Only install plugins you trust." Buttons: Install / Cancel; "Show details" expands the manifest capability block.
5. **Progress:** reuse the island progress kind (the app already has `IslandEventKind.Progress`) with "Downloading… / Verifying… / Installing… / Starting…"; failures show a hover card with a Retry + "View log" action.
6. **Restart prompt:** if unload fails, a non-blocking island "Restart to finish updating <plugin>" with Restart later / Restart now; never force-restart while an island is visible.
7. **Installed list:** enable/disable toggle, settings, version, update available badge, "Check for updates", uninstall (with "Keep settings"), rollback in version history, "Report a problem".
8. **Updates:** badge on the Plugins nav; "Update all" is allowed only for stable-channel plugins with compatible versions; show a summary of capability changes before updating.
9. **Blocked/yanked:** detail page shows a red banner with the reason and a recommended replacement; installed plugins show "Disabled for security" with an uninstall/rollback action.
10. **Empty/error states:** offline ("Showing cached catalog from <date>"), index signature failure ("Catalog verification failed — refresh or check your clock"), no results, incompatible-only results ("Requires FluidBar 2.2").

The settings window already has the navigation rail, detail-panel transition, toggles, sliders and a plugin list; reuse those visual patterns and `DetailEnabledToggle`/`PluginSettingsContainer` primitives rather than introducing a second design language.

### 4.7 Moderation workflow

**Trust levels shown in the UI and stored in the index:**

| Level | How obtained | UI treatment | Capability policy |
|---|---|---|---|
| **Built-in** | Ships with FluidBar, same source tree | No badge needed; listed under "Built-in" | Full access, code-reviewed with the app |
| **Verified publisher** | Domain/org verified, key pinned, ≥ 2 published plugins or manual vetting | Blue check + publisher page | Can request all capabilities; still reviewed |
| **Community** | Normal PR path with a first-time publisher review | No badge or "Community" label | Default-deny high-risk; security review for risky |
| **Sideloaded** | Installed from a `.fbp` file, not in the index | Yellow "Unverified" banner + audit log entry | Explicit consent per capability; no auto-update |
| **Blocked** | Index `status: blocked` | Red banner, disabled | Install disallowed; running instance disabled |
| **Deprecated** | Author or maintainer marks it | Grey label + replacement link | Installable, warned |

| Event | Action | SLA |
|---|---|---|
| New plugin PR | Automated gates + 1 reviewer; security label adds a 2nd | 5 business days |
| Update PR | Automated gates + 1 reviewer (2 if capabilities expand) | 3 business days |
| Capability expansion | Explicit diff review + consent copy update | 5 business days |
| Malware report | Triage, reproduce, `status: blocked` in index, advisory | 24 h |
| Takedown (legal/security) | Block + remove release asset + notify publisher | 24–72 h |
| Publisher key change | Manual verification, client re-consent | 5 business days |
| Abandoned plugin | Mark `deprecated`, suggest replacement, keep installable | 90 days notice |
| Appeal | Maintainer + second reviewer, written decision | 10 business days |

**Index states:** `active`, `deprecated` (warning, still installable), `blocked` (client disables/disallows install, shows reason), `yanked-version` (that version only). Blocking must be possible without deleting the release (forensics + users who already installed). Keep a public `advisories.json` with CVE/GHSA ids when applicable.

**Ratings/reviews (post-MVP):** if added, require a GitHub account with ≥ 6 months age, one review per user per plugin, verified-install flag, rate limits, profanity/spam moderation, and never let ratings affect default sort until volume is meaningful. MVP uses install counts (`download_count` from Release assets, fetched nightly) + "verified publisher" + changelog.

**Telemetry:** plugin crash reports are opt-in, anonymized, batched; no file paths, no plugin payloads; document exactly what is sent. A plugin's own telemetry must be disclosed in its manifest (`capabilities.telemetry`) and privacy policy link.

### 4.8 Compatibility and versioning policy

- `apiVersion` follows semver; the host supports one major at a time (e.g., `2.x`). Breaking SDK changes bump the major and keep the previous major loadable for one app major cycle (dual-contract shim).
- `minAppVersion` is the *first* app version that can load the plugin; `maxAppVersion` optional and rarely used (prefer `maxApiVersion`).
- `engines.fluidbar` is a semver range used at install (`^2.0.0`); `apiVersion` is the ABI the plugin was compiled against; `minAppVersion` covers behavioral dependencies.
- SDK is additive-only within a major: new interfaces with default implementations (C# 8 DIM) or new optional capability objects; never change an existing method signature.
- Obsolete APIs: mark `[Obsolete]` with a diagnostic id, keep for 2 minor releases, log usage, publish a migration guide.
- Plugin index entries record `apiVersion`/`minAppVersion` so the client can grey out incompatible versions instead of failing at load.
- A "compatibility matrix" page is generated from the index; the client sends its app version to filter.

---

## 5. Internationalization and localization for WPF

### 5.1 Requirements

1. Ship `en-US` (source) and `zh-Hans` at v2 launch; add `ja`, `de`, `fr`, `es`, `pt-BR`, `ru`, `ko`, `zh-Hant` as community translations.
2. Switch language at runtime without losing island state; restart is an acceptable fallback for edge cases.
3. Every user-visible string is localizable: island titles/content, hover cards, settings, tray menu, notifications, errors, update dialog, marketplace, plugin names/descriptions, accessibility names/tooltips.
4. Plugins localize independently and can ship locale bundles without rebuilding the app.
5. Marketplace listings localize; users can read plugin descriptions in their language.
6. Layout survives +30–40% string expansion, CJK glyphs, and RTL.
7. Plurals, dates, numbers, durations, file sizes and relative times are culture-correct.
8. Translators can work without touching code; CI catches missing keys, broken placeholders and ICU syntax errors.

### 5.2 `.resx` vs JSON catalogs

| | `.resx` + `ResourceManager` | JSON catalogs |
|---|---|---|
| Tooling | Visual Studio/Rider designers, `resgen`, source generators, ResX Manager | Any editor, JSON Schema, simple diffs |
| Type safety | Strongly typed generated class; compile-time key checks | Needs a key generator or constants class |
| Runtime switching | `ResourceManager` caches per culture; UI must re-read; satellite assemblies loaded on demand | Trivial: reload dictionary, raise change notification |
| Fallback | Built-in culture fallback chain (satellite probing) | Must implement |
| Plural/ICU | Not built in (resx is flat strings) | Natural fit for ICU strings |
| Translator experience | Mature (Weblate/Crowdin/ResX Manager), but XML noise | Excellent for PRs; needs validation |
| Plugin distribution | Plugin must build satellite assemblies; harder for third-party authors | Drop-in files, no build; ideal for plugins |
| Hot reload / live edit | Rebuild or custom | Reload file at runtime |
| Size | XML overhead; satellite per culture | Small |

**Decision:** **.resx for the app** (compile-time safety, mature tooling, satellite fallback, Weblate support) and **JSON for plugins and marketplace listings** (drop-in, no build, per-plugin ownership). Both are exposed through one `ILocalizer` abstraction so UI code never cares which backing store is used.

Do not mix formats inside one component; that is how keys drift.

### 5.3 App localization architecture

**Source layout**

```
Localization/
  Strings.resx                 (en-US, neutral)
  Strings.zh-Hans.resx
  Strings.ja.resx
  Strings.Designer.cs          (generated; do not hand-edit)
  Loc.cs                       (markup extension + service facade)
  LocalizationService.cs
  CultureFallback.cs
  PluralFormatter.cs
  PseudoLocalizer.cs           (build-time)
```

**Culture setup at startup** (WPF has a notorious default):

```csharp
// App.OnStartup, before any window is created
var culture = ResolveStartupCulture(settings.UiLanguage); // explicit setting > OS > en-US
CultureInfo.DefaultThreadCurrentCulture   = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;
CultureInfo.CurrentCulture   = culture;
CultureInfo.CurrentUICulture = culture;

// WPF defaults FrameworkElement.Language to en-US. Override it once,
// so font fallback, line breaking and glyph selection follow the UI culture.
FrameworkElement.LanguageProperty.OverrideMetadata(
    typeof(FrameworkElement),
    new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(culture.IetfLanguageTag)));

LocalizationService.Instance.SetCulture(culture);
```

Without the `LanguageProperty` override, a Chinese UI still measures/lays out text as `en-US`, which affects CJK font selection and line breaking. This is a real, frequently missed WPF bug.

**Runtime language switch**

```csharp
public sealed class LocalizationService : INotifyPropertyChanged
{
    public static LocalizationService Instance { get; } = new();
    private ResourceManager _rm = Strings.ResourceManager;
    private CultureInfo _culture = CultureInfo.GetCultureInfo("en-US");

    public string this[string key] => _rm.GetString(key, _culture) ?? key;

    // Bumped on every culture change; bindings watch this property and
    // re-run their converter, which re-reads the indexer.
    public int Version { get; private set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetCulture(CultureInfo culture)
    {
        _culture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        Version++;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Version)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRtl)));
        CultureChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool IsRtl => _culture.TextInfo.IsRightToLeft;
    public event EventHandler? CultureChanged;
}
```

**XAML usage** — a markup extension that returns a `Binding` through a converter, not a static string. (A direct `Path="[Some.Dotted.Key]"` binding is ambiguous in WPF's property-path parser; the converter approach avoids that and keeps dotted keys.)

```csharp
[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension : MarkupExtension
{
    public LocExtension() { }
    public LocExtension(string key) => Key = key;
    [ConstructorArgument("key")] public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider sp) =>
        new Binding(nameof(LocalizationService.Version))
        {
            Source = LocalizationService.Instance,
            Mode = BindingMode.OneWay,
            Converter = LocalizedStringConverter.Instance,
            ConverterParameter = Key,
            FallbackValue = Key
        }.ProvideValue(sp);
}

public sealed class LocalizedStringConverter : IValueConverter
{
    public static LocalizedStringConverter Instance { get; } = new();
    public object Convert(object? value, Type t, object? p, CultureInfo c) =>
        LocalizationService.Instance[(string)p!];
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) =>
        throw new NotSupportedException();
}
```

```xml
<TextBlock Text="{loc:Loc Key=Settings.Title}" />
<Button Content="{loc:Loc Key=Common.Cancel}" />
```

For formatted strings, call `Loc.Format("Plugin.Count", count)` in code-behind, or use a `MultiBinding` + `IValueConverter` when the arguments are bindable. Avoid `x:Static Strings.Title` for anything that must live-switch: it captures the value at load time and never updates.

**What must be re-applied on culture change**

- `FlowDirection` on each open window (`LocalizationService.IsRtl ? RightToLeft : LeftToRight`).
- Window titles, tray menu items, `NotifyIcon.Text`, tooltips created imperatively.
- `ToolTip`/`AutomationProperties.Name` set in code-behind.
- Strings captured in long-lived view models (re-query, don't cache).
- `XmlLanguage` on the root element if you don't use the metadata override.
- Date/time/number formatting (bindings re-evaluate if they depend on culture; `ToString()` in code does not).
- Any `FormattedText`/`GlyphRun` measurement caches.

**MVP shortcut:** a "Language" setting with `System`, `English`, `简体中文`, `…`; apply immediately, and if any imperative string remains, show "Restart FluidBar to apply everywhere" as a secondary action. v2.1 should be fully restart-free.

**Key naming convention:** `Area.SubArea.Element` (`Settings.Plugins.Title`, `Common.Cancel`, `Error.Network.Timeout`), max 3–4 segments, stable, never reused for different meaning, no English words embedded in keys, no dynamic key construction (breaks extraction and validation). Add comments in resx for translator context (`<comment>`), which Weblate shows.

**Error strings:** never show raw exception messages to users; map to `Error.<Category>.<Code>` with a localized message and a "Details" expander containing the technical text (LTR, monospace). Logs stay English for support.

### 5.4 Plugin localization

**Manifest fields** support two forms:

```json
"name": { "en-US": "Now Playing", "zh-Hans": "正在播放" },
"summary": { "en-US": "Shows the current media session…", "zh-Hans": "显示当前媒体会话…" }
```

or a key reference resolved against the plugin's locale bundle:

```json
"nameKey": "plugin.name",
"summaryKey": "plugin.summary"
```

Use the object form for marketplace/listing metadata (so the index generator can extract it without loading the plugin), and key form for runtime UI strings. If both exist, keys win at runtime and objects win in the index.

**Bundle layout**

```
locales/
  en-US.json          (required, complete)
  zh-Hans.json
  ja.json
  de.json
```

```json
{
  "$schema": "https://fluidbar.app/schemas/plugin-locale.schema.json",
  "locale": "zh-Hans",
  "version": 1,
  "messages": {
    "plugin.name": "正在播放",
    "island.title": "正在播放",
    "island.subtitle": "{artist} · {album}",
    "count.tracks": "{count, plural, =0 {没有曲目} other {# 首曲目}}",
    "error.session_lost": "媒体会话已断开"
  }
}
```

**Resolution order** (host `PluginLocalizer`):

```
plugin locale (exact) → plugin locale (parent: zh-Hans-CN → zh-Hans → zh)
  → app UI culture (exact → parent)
  → plugin en-US → app en-US
  → key (dev) / "" + log (release)
```

- Missing key: log once per plugin/key/culture, show `en-US`, never crash; in Debug show `⟦key⟧` to make gaps visible.
- Placeholder mismatch (`{name}` missing/extra) is a build error, not a runtime surprise.
- Plugin locale files are validated against a JSON Schema and the plugin's message key list; extra keys warn, missing keys fail if `en-US` is missing them.
- Plugins can register additional locales at runtime only from their signed package (no downloading locale packs from arbitrary URLs in MVP; a later "language packs" feature can add signed locale packages).
- Formatting API: `context.Localization.Get("key")`, `Format("key", new { count = 3, artist = "..." })`, `Plural("key", count)`; never expose raw `CultureInfo` mutation to plugins (they share the process).
- Plugin-provided strings in island events are already localized by the plugin; the host must not attempt to translate them. The host does own **chrome** strings (badges like "LIVE", "Now", relative time, "mute"), so those must be host-localized and passed as tokens where possible (e.g., `statusToken: "live"` instead of `statusText: "LIVE"`).

**Per-plugin translation ownership.** The plugin author owns `en-US` and any locales they can maintain. The marketplace runs a Weblate component per plugin; community contributions open PRs against `fluidbar/plugins/plugins/<id>/locales/`. The plugin's CI validates and the publish pipeline includes the new locales in the next version. Do not let translators change source code.

### 5.5 Plurals, numbers and ICU

.NET has no built-in ICU MessageFormat and no public CLDR plural-rule API. Options:

| Approach | Pros | Cons |
|---|---|---|
| Adopt `MessageFormat.NET` / `SmartFormat` | ICU syntax, plural support, maintained | Dependency; behavior may drift from ICU; must wrap |
| `NGettext` (gettext `.po`) | Mature plural rules, great tooling (Poedit/Weblate) | Different syntax; resx/JSON integration work; ASCII-only msgid discipline |
| Own ICU subset + CLDR rules table | No dependency, exact control, small | Must implement/test plural categories; risk of gaps |
| `Humanizer` | Easy English plurals/dates | English-centric; not enough for ru/ar/pl |

**Recommendation:** own a **small ICU subset** in `FluidBar.Localization` (parser + formatter, ~500 lines) with a CLDR plural-category table for the locales we ship, validated by tests. Support:

- `{name}` simple argument
- `{count, plural, =0 {…} one {# …} few {…} many {…} other {…}}`
- `{count, number}` (grouping/decimal via `CultureInfo`)
- `{date, date, short|medium|long}` / `{date, time, short|medium}`
- `#` inside plural branches
- Escaping `'{'` / `'}'`

If this grows beyond a subset, switch to `MessageFormat.NET` behind the same `IIcuFormatter` interface. Do not hand-roll plural rules per call site.

**Plural categories by locale** (CLDR): `en/de/es/it/nl/sv/da/fi/el/hu/tr` → one/other; `fr/pt-BR` → one/many/other (French `many` for large numbers); `ru/uk/pl/cs/sk` → one/few/many/other; `ar` → zero/one/two/few/many/other; `he` → one/two/other; `zh/ja/ko/vi/th/id` → other only. Tests must cover at least `en` (1/2), `ru` (1/2/5/21), `ar` (0/1/2/3/11/100), `zh` (any).

**Number/date/duration:**

- `CurrentCulture` for `ToString("N0")`, `ToString("d")`, `ToString("t")`.
- Durations ("3 min ago") use a template + plural, not `TimeSpan.ToString()`.
- File sizes: localize units and decimal separator (`1.5 MB` vs `1,5 Mo`); use `1.5 Mo` style per locale.
- Percent: `0–100 %` spacing differs (French uses a non-breaking space).
- Battery/volume: avoid hardcoded `%` in XAML; format in code.
- Song titles/artist names are user data: never translate; wrap in Unicode isolates when embedded in RTL text (`\u2068…\u2069`).

### 5.6 RTL

WPF has good bidi support but does not automatically mirror custom layouts.

- Set `FlowDirection="RightToLeft"` on each window root when `IsRtl`; it inherits to children. Re-apply on language switch.
- Use `Grid`/`StackPanel`/`DockPanel` rather than absolute `Canvas.Left` for anything that should mirror. `Canvas` coordinates do not flip.
- Text alignment: set `TextAlignment` explicitly (usually `Right` in RTL); `TextBlock` default is `Left`.
- Mirror directional icons (chevrons, back arrows, progress direction); `FlowDirection` does not flip `Image`/`Path` geometry. Provide `Icon.MirrorInRtl` metadata.
- Keep LTR for: URLs, version numbers, hotkey combos, code, file paths, timestamps in ISO form. Wrap with `FlowDirection="LeftToRight"` or Unicode isolates.
- `ContextMenu`/`Popup` placement may need `PlacementMode` changes near screen edges; test.
- Custom animations: `TranslateTransform.X` signs should flip for slide-in-from-side; check island show/hide and settings panel transitions (`ShowDetailPanel` uses `DetailPanelTranslate.X = 32`).
- Fonts: Arabic/Persian/Hebrew need proper font fallback (`Segoe UI` covers Arabic; `Segoe UI`/`Arial` for Hebrew); test shaping with diacritics.
- Test locales: `ar-SA`, `he-IL`, `fa-IR`; check island compact width (RTL text can be wider), badge placement, truncation, and the hover card's progress bar direction.

### 5.7 Fonts and CJK fallback

- Define theme font tokens with explicit fallback chains:
  - UI: `Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI, Yu Gothic UI, Malgun Gothic, Arial`
  - Display: `Segoe UI Variable Display, Segoe UI Semibold, Microsoft YaHei UI, Yu Gothic UI`
  - Mono: `Cascadia Mono, Consolas, Microsoft YaHei UI`
- WPF resolves a comma-separated `FontFamily` by choosing the first available family; missing glyphs then go through WPF's composite fallback (`GlobalUserInterface.CompositeFont`). Explicitly including the CJK families improves consistency and avoids the wrong Han variant.
- Set `Language`/`xml:lang` on the root (or the metadata override above) so Han unification picks Simplified vs Traditional vs Japanese glyphs correctly. Without it, a `zh-Hant` string may render with Simplified glyph shapes.
- Do not bundle full CJK fonts (10–25 MB each); rely on system fonts (`Microsoft YaHei UI` on Windows 10+, `Microsoft JhengHei UI`, `Yu Gothic UI`, `Malgun Gothic`). Bundle only a Latin display font if branding requires it, with CJK fallback.
- `TextOptions.TextFormattingMode`: `Display` is crisper at 100% but can clip/alias at 125%/150%; test both and expose a setting if needed. Default `Ideal` for scaled displays, `Display` for the 38 px island at 100%.
- `UseLayoutRounding="True"` and `SnapsToDevicePixels="True"` on the pill and hover card; CJK glyphs at 11–13 px need integer positioning.
- Avoid synthetic bold for CJK (WPF may fake it); prefer the family's real weights or `FontWeight="SemiBold"` when available.
- Line height: CJK needs slightly more leading; set `LineHeight`/`LineStackingStrategy` for multi-line detail text rather than relying on defaults.
- Emoji: `Segoe UI Emoji` fallback; color emoji rendering in WPF is limited (monochrome in many cases) — avoid emoji as functional icons; use Segoe Fluent Icons glyphs for UI.

### 5.8 Marketplace listing localization

- `listings/<id>/<locale>.json`:

```json
{
  "locale": "zh-Hans",
  "name": "正在播放",
  "summary": "在灵动岛上显示当前媒体会话。",
  "description": "Markdown, up to 8 KB…",
  "screenshots": [
    { "src": "assets/screenshots/01.png", "alt": "紧凑模式下的媒体岛" }
  ],
  "changelog": "### 2.1.0\n- 新增歌词…"
}
```

- Index carries `defaultLocale`, `availableLocales`, `localeCoverage` (percent of keys translated) and a `listingBaseUrl`; the client fetches the best locale + `en-US` fallback on demand.
- Listing translation is separate from UI translation: a plugin can have a perfect UI translation and an English-only listing. Show `localeCoverage` as a subtle badge, not a blocker.
- Machine translation is allowed only with a `machineTranslated: true` flag and a visible label; never silently MT.
- Screenshots are locale-specific where text appears; require at least one screenshot with no text for fallback.
- Search indexing: index both `en-US` and localized names/summaries so Chinese users can search Chinese names.

### 5.9 Community translation workflow

**Recommended: Weblate** (self-hosted or hosted). Reasons: native resx and JSON support, GitHub PR integration, translation memory, glossary, per-language review, screenshot context, REST API for the marketplace bot, and it is open source. Crowdin is a strong alternative if budget exists; GitHub PR-only is fine for the first 6 months.

Components:

| Component | Format | Owners | Review |
|---|---|---|---|
| `fluidbar-app` | `.resx` | core team | 2 approvals for `en-US` changes; 1 per language |
| `fluidbar-marketplace` | JSON | core team | 1 per language |
| `plugin-<id>` | JSON | plugin author + community | author or language reviewer |

Workflow:

1. Source strings change only in `en-US` (source language), via normal PRs; Weblate pulls on merge.
2. Translators work in Weblate; changes become PRs (batched or per-translation) to `fluidbar/FluidBar` or `fluidbar/plugins`.
3. CI validates placeholders, ICU syntax, key existence, and that `en-US` is complete; translation PRs skip build/tests but run the localization validator.
4. A language maintainer reviews; core team reviews only `en-US` semantics.
5. Weekly release train for translations (or continuous for plugins); the app shows "Translation updated" in About.
6. Glossary: FluidBar, island (灵动岛), hover card (悬停卡片), plugin (插件), marketplace (插件市场), command (命令), widget (小组件). Do-not-translate: `FluidBar`, API names, file extensions.
7. Tone: concise, UI-imperative for actions ("保存"), descriptive for states.
8. Recognition: `translators.json` / About page credits; opt-in.

**String freeze:** 7 days before a release, `en-US` is frozen; translations can continue and ship in a patch. Never block a security fix on translations; ship English fallback with a note.

### 5.10 Pseudo-localization and i18n testing

**Pseudo-locale `qps-ploc`:** generated at build from `en-US`:

- Wrap: `⟦ … ⟧`
- Accent vowels: `a→á, e→é, i→í, o→ó, u→ú, c→ç, n→ñ`
- Expand by ~35% by repeating characters inside words (preserve placeholders and punctuation)
- Keep `{0}`/`{name}` intact; validate formatting after pseudo-localization

Enable via `FLUIDBAR_PSEUDO=1` env var or hidden setting; it must not ship enabled.

**Tests:**

- Resource parity: every key in `Strings.resx` exists in each satellite (or is intentionally untranslated and falls back); no extra keys.
- Placeholder parity: same set/order of `{...}` per key across locales.
- ICU parse tests: one test per supported plural category per locale.
- Layout: for each locale, measure every UI string at 100%/125%/150% scaling and assert it fits its container or is allowed to ellipsize; screenshot the island compact + hover + settings in `en-US`, `de-DE`, `zh-Hans`, `ja`, `ar-SA`, `qps-ploc`.
- RTL: assert `FlowDirection` propagation, mirrored icons, no clipped text.
- Font fallback: render a string containing Latin + CJK + Arabic + emoji; assert no tofu (missing glyph) via `GlyphTypeface` coverage or visual check.
- Runtime switch: automated test calls `SetCulture("zh-Hans")` then `("en-US")` and asserts a bound `TextBlock.Text` changes without recreating the window.
- Marketplace: every plugin's `en-US` listing exists; localized listings validate; `localeCoverage` computed correctly.

**Accessibility:** `AutomationProperties.Name` must be localized too; screen readers use `Language` for pronunciation — set it on controls with mixed-language content.

### 5.11 WPF localization pitfalls checklist

- [ ] `FrameworkElement.LanguageProperty` overridden from `CurrentUICulture` (not left as `en-US`).
- [ ] No `x:Static` for switchable strings; use the `Loc` binding or re-query on culture change.
- [ ] No string concatenation for sentences; use placeholders/ICU (word order differs).
- [ ] No hardcoded `Margin`/`Width` tuned to English; use `MinWidth`, `TextTrimming`, `Wrap`.
- [ ] `TextTrimming="CharacterEllipsis"` on single-line island text; never clip mid-glyph.
- [ ] `FlowDirection` set per window and re-applied on switch.
- [ ] Dates/numbers use `CurrentCulture`, not invariant.
- [ ] `ResourceManager` uses `NeutralResourcesLanguage("en-US")` and satellite assemblies are included in publish.
- [ ] Single-file publish keeps satellite assemblies (`SatelliteResourceLanguages` if trimming; not applicable now).
- [ ] `Strings.Designer.cs` regenerated in CI and checked for drift (or use a source generator).
- [ ] Translator comments present for ambiguous keys.
- [ ] Error dialogs have a localized summary + technical details in English.
- [ ] Plugin locale JSON validated at install and at build; no network-loaded locales.
- [ ] `qps-ploc` build test in CI.
- [ ] CJK/emoji font fallback verified on a clean Windows VM without optional fonts.

---

## 6. Concrete schemas

All schemas are JSON Schema 2020-12, `additionalProperties: false` for objects (strict), and versioned by `schemaVersion`. The same schema is used by the contributor CLI, CI, the marketplace generator, and the client installer. Files:

```
schemas/
  plugin.schema.json
  marketplace-index.schema.json
  marketplace-plugin.schema.json
  plugin-locale.schema.json
  listing.schema.json
  signature.schema.json
  settings.schema.json
```

### 6.1 Plugin manifest — annotated example

This example exercises the media plugin's full surface: localization objects, capabilities with reasons, island + hover-card layouts, settings, commands, context-menu actions, a widget, dependencies and integrity.

> **Elision note:** `…` inside hashes, base64 and long strings marks elided content for readability; real manifests contain full values. The JSON Schema in 6.2 is complete and parses as-is. All examples use `additionalProperties: false`, so field names are normative.

```json
{
  "$schema": "https://fluidbar.app/schemas/plugin.schema.json",
  "schemaVersion": 2,
  "id": "now-playing",
  "version": "2.1.0",
  "apiVersion": "2.0.0",
  "minAppVersion": "2.0.0",
  "maxAppVersion": "2.99.99",
  "publisher": {
    "id": "acme-labs",
    "displayName": "Acme Labs",
    "url": "https://example.com/acme",
    "keyId": "acme-2026"
  },
  "author": {
    "name": "Jane Doe",
    "url": "https://example.com/jane",
    "email": "jane@example.com"
  },
  "name": { "en-US": "Now Playing", "zh-Hans": "正在播放" },
  "summary": {
    "en-US": "Shows the current media session on the island.",
    "zh-Hans": "在灵动岛上显示当前媒体会话。"
  },
  "description": { "en-US": "Longer markdown description…", "zh-Hans": "更长的 Markdown 描述…" },
  "license": "MIT",
  "repository": "https://github.com/acme/fluidbar-now-playing",
  "homepage": "https://example.com/now-playing",
  "changelog": "CHANGELOG.md",
  "categories": ["media", "productivity"],
  "tags": ["music", "lyrics", "gsmts"],
  "icon": {
    "light": "assets/icon-light.png",
    "dark": "assets/icon-dark.png",
    "monochrome": "assets/icon-mono.svg"
  },
  "screenshots": [
    { "src": "assets/screenshots/compact.png", "alt": { "en-US": "Compact island", "zh-Hans": "紧凑灵动岛" } },
    { "src": "assets/screenshots/hover.png", "alt": { "en-US": "Hover card", "zh-Hans": "悬停卡片" } }
  ],
  "entry": {
    "runtime": "inproc",
    "assembly": "plugin/FluidBar.Plugin.NowPlaying.dll",
    "type": "FluidBar.Plugin.NowPlaying.NowPlayingPlugin",
    "targetFramework": "net10.0-windows10.0.19041.0"
  },
  "platforms": ["win-x64", "win-arm64"],
  "locales": {
    "default": "en-US",
    "available": ["en-US", "zh-Hans", "ja"],
    "path": "locales",
    "fallback": "en-US"
  },
  "capabilities": {
    "network": {
      "domains": ["api.example.com", "*.music.example.com"],
      "methods": ["GET"],
      "reason": "Fetch lyrics and album art metadata."
    },
    "media": { "sessions": true, "control": true, "reason": "Read and control the current media session." },
    "notifications": { "post": true, "read": false },
    "clipboard": { "read": false, "write": true, "reason": "Copy the current track title." },
    "files": { "read": ["$PLUGIN_DATA"], "write": ["$PLUGIN_DATA"] },
    "hotkeys": { "global": ["Ctrl+Alt+M"], "reason": "Toggle the now-playing island." },
    "ui": { "island": true, "hoverCard": true, "settings": true, "contextMenu": true, "commands": true, "widgets": true, "xaml": false },
    "background": { "intervalMinMs": 1000, "reason": "Poll media sessions while playing." },
    "telemetry": { "enabled": false, "endpoint": null }
  },
  "activation": ["startup", "onMediaSession", "onCommand:now-playing.toggle"],
  "contributes": {
    "layouts": [
      {
        "id": "now-playing.island",
        "surface": "island.compact",
        "template": "media.v1",
        "slots": {
          "icon": { "field": "albumArt", "kind": "image" },
          "title": { "field": "title", "maxLines": 1, "style": "title" },
          "subtitle": { "field": "artist", "maxLines": 1, "style": "caption" },
          "badge": { "field": "sourceApp" },
          "progress": { "field": "progressPercent" },
          "accessory": { "field": "isPlaying", "kind": "wave" }
        },
        "constraints": { "minWidth": 126, "maxWidth": 430, "minHeight": 38, "maxHeight": 96 }
      },
      {
        "id": "now-playing.hover",
        "surface": "hoverCard",
        "template": "media.hover.v1",
        "slots": {
          "title": { "field": "title", "maxLines": 1 },
          "subtitle": { "field": "artist", "maxLines": 1 },
          "body": { "field": "lyricLine", "maxLines": 2 },
          "progress": { "field": "progressPercent" },
          "actions": { "commands": ["now-playing.toggle", "now-playing.copy-track"] }
        },
        "constraints": { "minWidth": 360, "maxWidth": 760, "minHeight": 176, "maxHeight": 320 }
      }
    ],
    "settingsPages": [
      {
        "id": "general",
        "title": { "en-US": "General", "zh-Hans": "常规" },
        "controls": [
          { "type": "toggle", "key": "showLyrics", "title": { "en-US": "Show lyrics", "zh-Hans": "显示歌词" }, "default": true },
          { "type": "slider", "key": "pollMs", "title": { "en-US": "Poll interval", "zh-Hans": "轮询间隔" }, "min": 500, "max": 5000, "step": 100, "default": 1000, "unit": "ms" },
          { "type": "select", "key": "lyricsSource", "title": { "en-US": "Lyrics source", "zh-Hans": "歌词来源" }, "default": "auto",
            "options": [
              { "value": "auto", "label": { "en-US": "Automatic", "zh-Hans": "自动" } },
              { "value": "local", "label": { "en-US": "Local .lrc", "zh-Hans": "本地 .lrc" } }
            ] },
          { "type": "button", "key": "clearCache", "title": { "en-US": "Clear artwork cache", "zh-Hans": "清除封面缓存" }, "command": "now-playing.clear-cache", "style": "secondary" }
        ]
      }
    ],
    "commands": [
      { "id": "now-playing.toggle", "title": { "en-US": "Toggle Now Playing", "zh-Hans": "切换正在播放" }, "icon": "assets/icons/toggle.svg", "defaultHotkey": "Ctrl+Alt+M", "contexts": ["global", "island"] },
      { "id": "now-playing.copy-track", "title": { "en-US": "Copy track name", "zh-Hans": "复制曲目名称" }, "icon": "assets/icons/copy.svg", "contexts": ["contextMenu", "commandPalette"] },
      { "id": "now-playing.clear-cache", "title": { "en-US": "Clear artwork cache", "zh-Hans": "清除封面缓存" }, "contexts": ["settings"] }
    ],
    "contextMenu": [
      { "id": "now-playing.menu.copy", "command": "now-playing.copy-track", "title": { "en-US": "Copy track name", "zh-Hans": "复制曲目名称" }, "when": "source == 'now-playing' && hasTrack", "group": "clipboard" },
      { "id": "now-playing.menu.toggle", "command": "now-playing.toggle", "title": { "en-US": "Pause island updates", "zh-Hans": "暂停灵动岛更新" }, "when": "source == 'now-playing'", "group": "controls" }
    ],
    "widgets": [
      { "id": "now-playing.wave", "slot": "island.accessory", "template": "wave.v1", "size": "small" }
    ]
  },
  "dependencies": {
    "sdk": "^2.0.0",
    "plugins": [
      { "id": "media-core", "range": "^1.2.0", "optional": false }
    ]
  },
  "settings": {
    "schema": "settings.schema.json",
    "defaults": "settings.defaults.json",
    "storage": "host"
  },
  "integrity": {
    "entrySha256": "9f2c1b7e5d4a…",
    "files": "checksums.json"
  },
  "signature": {
    "scheme": "detached-v1",
    "algorithm": "ECDSA-P256-SHA256",
    "publisherKeyId": "acme-2026",
    "signatureFile": "signature.json"
  }
}
```

Notes:

- `name`/`summary`/`description` are `LocalizedString` (string **or** locale map). `title`/`label` fields everywhere use the same type.
- `entry.assembly` is relative to the package root and must start with `plugin/`; `entry.type` is the fully qualified type name.
- `capabilities` only declares what is used. A capability object without `reason` is rejected if it is in the risky set (`network`, `files`, `shell`, `process`, `registry`, `hotkeys`, `secrets`).
- `contributes` is declarative. The host renders templates; a plugin cannot inject arbitrary XAML when `capabilities.ui.xaml` is `false` (and marketplace policy forbids `true` in MVP).
- `settings.storage: "host"` means the host owns `settings.json` and exposes typed values via `IPluginSettings`; `"plugin"` is allowed only for plugins that need custom migration, and still requires a JSON Schema.
- `integrity.entrySha256` prevents swapping the entry DLL while keeping the manifest; the package-level hash and signature live in the detached `signature.json` and the signed marketplace index (a manifest cannot contain its own package hash without circularity).
- `signature` declares the expected scheme/key and the sidecar name; it is **not** the signature value. The actual signatures are in the detached `signature.json`, covering the package SHA-256; `checksums.json` independently covers the extracted files.

### 6.2 Plugin manifest JSON Schema (normative, compact)

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://fluidbar.app/schemas/plugin.schema.json",
  "title": "FluidBar Plugin Manifest",
  "type": "object",
  "additionalProperties": false,
  "required": ["schemaVersion", "id", "version", "apiVersion", "minAppVersion", "publisher", "name", "summary", "license", "entry", "locales", "capabilities", "contributes"],
  "properties": {
    "$schema": { "type": "string", "format": "uri" },
    "schemaVersion": { "const": 2 },
    "id": { "$ref": "#/$defs/pluginId" },
    "version": { "$ref": "#/$defs/semver" },
    "apiVersion": { "$ref": "#/$defs/semver" },
    "minAppVersion": { "$ref": "#/$defs/semver" },
    "maxAppVersion": { "$ref": "#/$defs/semver" },
    "publisher": { "$ref": "#/$defs/publisher" },
    "author": { "$ref": "#/$defs/author" },
    "name": { "$ref": "#/$defs/localizedString" },
    "summary": { "$ref": "#/$defs/localizedString" },
    "description": { "$ref": "#/$defs/localizedString" },
    "nameKey": { "$ref": "#/$defs/messageKey" },
    "summaryKey": { "$ref": "#/$defs/messageKey" },
    "descriptionKey": { "$ref": "#/$defs/messageKey" },
    "license": { "type": "string", "minLength": 1, "maxLength": 64 },
    "repository": { "type": "string", "format": "uri" },
    "homepage": { "type": "string", "format": "uri" },
    "changelog": { "type": "string", "pattern": "^[A-Za-z0-9._/-]+$" },
    "categories": { "type": "array", "items": { "$ref": "#/$defs/category" }, "uniqueItems": true, "maxItems": 3 },
    "tags": { "type": "array", "items": { "type": "string", "pattern": "^[a-z0-9][a-z0-9-]{0,23}$" }, "uniqueItems": true, "maxItems": 10 },
    "icon": { "$ref": "#/$defs/iconSet" },
    "screenshots": { "type": "array", "maxItems": 8, "items": { "$ref": "#/$defs/screenshot" } },
    "entry": { "$ref": "#/$defs/entry" },
    "platforms": { "type": "array", "items": { "enum": ["win-x64", "win-arm64", "win-x86"] }, "uniqueItems": true, "minItems": 1 },
    "locales": { "$ref": "#/$defs/locales" },
    "capabilities": { "$ref": "#/$defs/capabilities" },
    "activation": { "type": "array", "items": { "$ref": "#/$defs/activation" }, "uniqueItems": true },
    "contributes": { "$ref": "#/$defs/contributes" },
    "dependencies": { "$ref": "#/$defs/dependencies" },
    "settings": { "$ref": "#/$defs/settingsRef" },
    "integrity": { "$ref": "#/$defs/integrity" },
    "signature": { "$ref": "#/$defs/signatureRef" }
  },
  "$defs": {
    "semver": { "type": "string", "pattern": "^(0|[1-9]\\d*)\\.(0|[1-9]\\d*)\\.(0|[1-9]\\d*)(?:-[0-9A-Za-z.-]+)?(?:\\+[0-9A-Za-z.-]+)?$" },
    "versionRange": { "type": "string", "minLength": 1, "maxLength": 64, "pattern": "^[<>=~^ ]*[0-9][0-9A-Za-z.\\-+* ]*$" },
    "pluginId": { "type": "string", "pattern": "^[a-z0-9](?:[a-z0-9-]{0,38}[a-z0-9])?$", "not": { "enum": ["fluidbar", "system", "settings", "marketplace", "core", "api", "plugin", "island", "monitor"] } },
    "locale": { "type": "string", "pattern": "^[a-z]{2,3}(?:-[A-Z][a-z]{3})?(?:-[A-Z]{2}|-[0-9]{3})?$" },
    "messageKey": { "type": "string", "pattern": "^[A-Za-z][A-Za-z0-9._-]{0,127}$" },
    "localizedString": {
      "oneOf": [
        { "type": "string", "minLength": 1, "maxLength": 2000 },
        {
          "type": "object",
          "minProperties": 1,
          "additionalProperties": { "type": "string", "minLength": 1, "maxLength": 2000 },
          "propertyNames": { "$ref": "#/$defs/locale" }
        }
      ]
    },
    "publisher": {
      "type": "object",
      "additionalProperties": false,
      "required": ["id", "displayName"],
      "properties": {
        "id": { "type": "string", "pattern": "^[a-z0-9][a-z0-9-]{1,38}$" },
        "displayName": { "type": "string", "minLength": 1, "maxLength": 80 },
        "url": { "type": "string", "format": "uri" },
        "keyId": { "type": "string", "pattern": "^[A-Za-z0-9._:-]{3,80}$" }
      }
    },
    "author": {
      "type": "object",
      "additionalProperties": false,
      "required": ["name"],
      "properties": {
        "name": { "type": "string", "minLength": 1, "maxLength": 80 },
        "email": { "type": "string", "format": "email" },
        "url": { "type": "string", "format": "uri" }
      }
    },
    "iconSet": {
      "oneOf": [
        { "type": "string", "pattern": "^assets/" },
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "light": { "type": "string", "pattern": "^assets/" },
            "dark": { "type": "string", "pattern": "^assets/" },
            "monochrome": { "type": "string", "pattern": "^assets/" }
          },
          "required": ["light", "dark"]
        }
      ]
    },
    "screenshot": {
      "type": "object",
      "additionalProperties": false,
      "required": ["src"],
      "properties": {
        "src": { "type": "string", "pattern": "^assets/" },
        "alt": { "$ref": "#/$defs/localizedString" }
      }
    },
    "entry": {
      "type": "object",
      "additionalProperties": false,
      "required": ["runtime", "assembly", "type"],
      "properties": {
        "runtime": { "enum": ["inproc", "outofproc"] },
        "assembly": { "type": "string", "pattern": "^plugin/[A-Za-z0-9._/-]+\\.dll$" },
        "type": { "type": "string", "pattern": "^[A-Za-z_][A-Za-z0-9_.`+]*$" },
        "targetFramework": { "type": "string", "pattern": "^net10\\.0-windows" }
      }
    },
    "locales": {
      "type": "object",
      "additionalProperties": false,
      "required": ["default", "available"],
      "properties": {
        "default": { "$ref": "#/$defs/locale" },
        "available": { "type": "array", "items": { "$ref": "#/$defs/locale" }, "uniqueItems": true, "minItems": 1 },
        "path": { "type": "string", "pattern": "^locales/?$" },
        "fallback": { "$ref": "#/$defs/locale" }
      }
    },
    "capabilities": {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "network": { "$ref": "#/$defs/networkCap" },
        "clipboard": { "type": "object", "additionalProperties": false, "properties": { "read": { "type": "boolean" }, "write": { "type": "boolean" }, "reason": { "type": "string" } } },
        "files": { "$ref": "#/$defs/filesCap" },
        "media": { "type": "object", "additionalProperties": false, "properties": { "sessions": { "type": "boolean" }, "control": { "type": "boolean" }, "reason": { "type": "string" } } },
        "notifications": { "type": "object", "additionalProperties": false, "properties": { "read": { "type": "boolean" }, "post": { "type": "boolean" } } },
        "shell": { "type": "object", "additionalProperties": false, "properties": { "open": { "type": "boolean" }, "execute": { "type": "boolean" }, "reason": { "type": "string" } } },
        "hotkeys": { "type": "object", "additionalProperties": false, "properties": { "global": { "type": "array", "items": { "type": "string" }, "uniqueItems": true }, "reason": { "type": "string" } } },
        "ui": { "type": "object", "additionalProperties": false, "properties": { "island": { "type": "boolean" }, "hoverCard": { "type": "boolean" }, "settings": { "type": "boolean" }, "contextMenu": { "type": "boolean" }, "commands": { "type": "boolean" }, "widgets": { "type": "boolean" }, "xaml": { "type": "boolean" } } },
        "process": { "type": "object", "additionalProperties": false, "properties": { "spawn": { "type": "boolean" }, "reason": { "type": "string" } } },
        "registry": { "type": "object", "additionalProperties": false, "properties": { "read": { "type": "array", "items": { "type": "string" } }, "write": { "type": "array", "items": { "type": "string" } }, "reason": { "type": "string" } } },
        "secrets": { "type": "object", "additionalProperties": false, "properties": { "storage": { "type": "boolean" }, "reason": { "type": "string" } } },
        "telemetry": { "type": "object", "additionalProperties": false, "properties": { "enabled": { "type": "boolean" }, "endpoint": { "type": ["string", "null"], "format": "uri" }, "reason": { "type": "string" } } },
        "background": { "type": "object", "additionalProperties": false, "properties": { "intervalMinMs": { "type": "integer", "minimum": 500 }, "reason": { "type": "string" } } }
      }
    },
    "networkCap": {
      "type": "object",
      "additionalProperties": false,
      "required": ["domains", "reason"],
      "properties": {
        "domains": { "type": "array", "minItems": 1, "items": { "type": "string", "minLength": 1 }, "uniqueItems": true },
        "methods": { "type": "array", "items": { "enum": ["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "WEBSOCKET"] }, "uniqueItems": true },
        "reason": { "type": "string", "minLength": 10, "maxLength": 500 }
      }
    },
    "filesCap": {
      "type": "object",
      "additionalProperties": false,
      "required": ["reason"],
      "properties": {
        "read": { "type": "array", "items": { "$ref": "#/$defs/pathScope" } },
        "write": { "type": "array", "items": { "$ref": "#/$defs/pathScope" } },
        "reason": { "type": "string", "minLength": 10, "maxLength": 500 }
      }
    },
    "pathScope": { "enum": ["$PLUGIN_DATA", "$PLUGIN_INSTALL", "$DOWNLOADS", "$DOCUMENTS", "$PICTURES", "$MUSIC", "$VIDEOS", "$DESKTOP", "$TEMP"] },
    "activation": { "type": "string", "pattern": "^(startup|onMediaSession|onNotification|onSettingsOpen|onIslandEvent|onCommand:[a-z0-9.-]+|interval:[0-9]+)$" },
    "contributes": {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "layouts": { "type": "array", "maxItems": 12, "items": { "$ref": "#/$defs/layout" } },
        "settingsPages": { "type": "array", "maxItems": 8, "items": { "$ref": "#/$defs/settingsPage" } },
        "commands": { "type": "array", "maxItems": 32, "items": { "$ref": "#/$defs/command" } },
        "contextMenu": { "type": "array", "maxItems": 16, "items": { "$ref": "#/$defs/contextMenuItem" } },
        "widgets": { "type": "array", "maxItems": 8, "items": { "$ref": "#/$defs/widget" } }
      }
    },
    "layout": {
      "type": "object",
      "additionalProperties": false,
      "required": ["id", "surface", "template"],
      "properties": {
        "id": { "type": "string", "pattern": "^[a-z0-9][a-z0-9.-]{2,63}$" },
        "surface": { "enum": ["island.compact", "island.expanded", "hoverCard"] },
        "template": { "enum": ["text.v1", "status.v1", "progress.v1", "media.v1", "media.hover.v1", "notification.v1", "agent.v1", "clock.v1", "list.v1"] },
        "slots": { "type": "object", "additionalProperties": { "$ref": "#/$defs/slotBinding" } },
        "constraints": { "$ref": "#/$defs/layoutConstraints" }
      }
    },
    "slotBinding": {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "field": { "type": "string", "pattern": "^[A-Za-z][A-Za-z0-9.]{0,63}$" },
        "literal": { "type": "string", "maxLength": 200 },
        "kind": { "enum": ["text", "image", "icon", "badge", "progress", "wave", "code"] },
        "maxLines": { "type": "integer", "minimum": 1, "maximum": 8 },
        "style": { "enum": ["title", "subtitle", "body", "caption", "badge", "mono"] },
        "commands": { "type": "array", "items": { "type": "string" }, "maxItems": 6 }
      },
      "oneOf": [{ "required": ["field"] }, { "required": ["literal"] }, { "required": ["commands"] }]
    },
    "layoutConstraints": {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "minWidth": { "type": "number", "minimum": 80, "maximum": 1000 },
        "maxWidth": { "type": "number", "minimum": 120, "maximum": 1000 },
        "minHeight": { "type": "number", "minimum": 32, "maximum": 600 },
        "maxHeight": { "type": "number", "minimum": 32, "maximum": 600 }
      }
    },
    "settingsPage": {
      "type": "object",
      "additionalProperties": false,
      "required": ["id", "title", "controls"],
      "properties": {
        "id": { "type": "string", "pattern": "^[a-z0-9][a-z0-9-]{1,31}$" },
        "title": { "$ref": "#/$defs/localizedString" },
        "description": { "$ref": "#/$defs/localizedString" },
        "controls": { "type": "array", "minItems": 1, "maxItems": 40, "items": { "$ref": "#/$defs/settingControl" } }
      }
    },
    "settingControl": {
      "type": "object",
      "additionalProperties": false,
      "required": ["type", "key", "title"],
      "properties": {
        "type": { "enum": ["toggle", "select", "slider", "number", "text", "path", "secret", "button", "link", "info"] },
        "key": { "type": "string", "pattern": "^[A-Za-z][A-Za-z0-9.]{0,63}$" },
        "title": { "$ref": "#/$defs/localizedString" },
        "description": { "$ref": "#/$defs/localizedString" },
        "default": {},
        "min": { "type": "number" },
        "max": { "type": "number" },
        "step": { "type": "number", "exclusiveMinimum": 0 },
        "unit": { "type": "string", "maxLength": 16 },
        "options": { "type": "array", "items": { "type": "object", "additionalProperties": false, "required": ["value", "label"], "properties": { "value": { "type": "string" }, "label": { "$ref": "#/$defs/localizedString" } } } },
        "command": { "type": "string", "pattern": "^[a-z0-9][a-z0-9.-]{2,63}$" },
        "style": { "enum": ["primary", "secondary", "danger"] },
        "validation": { "type": "object", "additionalProperties": false, "properties": { "pattern": { "type": "string" }, "minLength": { "type": "integer" }, "maxLength": { "type": "integer" } } }
      }
    },
    "command": {
      "type": "object",
      "additionalProperties": false,
      "required": ["id", "title"],
      "properties": {
        "id": { "type": "string", "pattern": "^[a-z0-9][a-z0-9.-]{2,63}$" },
        "title": { "$ref": "#/$defs/localizedString" },
        "description": { "$ref": "#/$defs/localizedString" },
        "icon": { "type": "string", "pattern": "^assets/" },
        "defaultHotkey": { "type": "string", "maxLength": 64 },
        "contexts": { "type": "array", "items": { "enum": ["global", "island", "contextMenu", "commandPalette", "settings"] }, "uniqueItems": true }
      }
    },
    "contextMenuItem": {
      "type": "object",
      "additionalProperties": false,
      "required": ["id", "command", "title", "when"],
      "properties": {
        "id": { "type": "string", "pattern": "^[a-z0-9][a-z0-9.-]{2,63}$" },
        "command": { "type": "string", "pattern": "^[a-z0-9][a-z0-9.-]{2,63}$" },
        "title": { "$ref": "#/$defs/localizedString" },
        "when": { "type": "string", "minLength": 1, "maxLength": 200 },
        "group": { "enum": ["primary", "controls", "clipboard", "navigation", "danger"] }
      }
    },
    "widget": {
      "type": "object",
      "additionalProperties": false,
      "required": ["id", "slot", "template"],
      "properties": {
        "id": { "type": "string", "pattern": "^[a-z0-9][a-z0-9.-]{2,63}$" },
        "slot": { "enum": ["island.accessory", "island.leading", "hoverCard.footer"] },
        "template": { "enum": ["wave.v1", "badge.v1", "icon.v1", "text.v1"] },
        "size": { "enum": ["small", "medium"] }
      }
    },
    "dependencies": {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "sdk": { "$ref": "#/$defs/versionRange" },
        "plugins": { "type": "array", "maxItems": 16, "items": { "type": "object", "additionalProperties": false, "required": ["id", "range"], "properties": { "id": { "$ref": "#/$defs/pluginId" }, "range": { "$ref": "#/$defs/versionRange" }, "optional": { "type": "boolean" } } } }
      }
    },
    "settingsRef": {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "schema": { "type": "string", "pattern": "^[A-Za-z0-9._/-]+\\.schema\\.json$" },
        "defaults": { "type": "string", "pattern": "^[A-Za-z0-9._/-]+\\.json$" },
        "storage": { "enum": ["host", "plugin"] }
      }
    },
    "integrity": {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "entrySha256": { "type": "string", "pattern": "^[a-f0-9]{64}$" },
        "files": { "const": "checksums.json" }
      }
    },
    "signatureRef": {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "scheme": { "const": "detached-v1" },
        "algorithm": { "enum": ["ECDSA-P256-SHA256", "Ed25519"] },
        "publisherKeyId": { "type": "string", "minLength": 3, "maxLength": 80 },
        "signatureFile": { "const": "signature.json" }
      }
    },
    "category": { "enum": ["productivity", "media", "developer", "system", "communication", "information", "fun", "integration", "accessibility", "theme"] }
  }
}
```

The schema is deliberately closed (`additionalProperties: false`). Adding a field is a schema-version bump, which is a feature: it prevents silent drift between app, tooling and docs.

Validation rules that JSON Schema cannot express (implemented in `FluidBar.PluginTool`):

- `maxAppVersion >= minAppVersion`; `apiVersion <= minAppVersion` major compatibility with the host.
- `locales.default` must be in `available`; `locales.fallback` must be in `available`.
- Every `layout.slots` key must be valid for its `template`; every `commands[]` reference in a slot exists in `contributes.commands`.
- Every `contextMenu.command` exists; every `settings.controls[].command` exists.
- Every localized string has `en-US` (or the default locale) and all locale codes are canonical.
- `capabilities.ui.xaml: true` is rejected for marketplace submissions in MVP (allowed only for built-in/verified).
- Risky capabilities (`network.domains` containing `*`, `shell.execute`, `files.write` outside `$PLUGIN_DATA`, `process.spawn`, `registry.write`) require a non-empty `reason` and set `review: security`.
- `entry.runtime: "outofproc"` is rejected until protocol v1 ships.

### 6.3 Marketplace index — annotated example

The root index is signed and contains only the latest version per plugin plus metadata needed to render the list and filter by compatibility. Full history lives in per-plugin shards.

```json
{
  "$schema": "https://fluidbar.app/schemas/marketplace-index.schema.json",
  "schemaVersion": 1,
  "generatedAt": "2026-10-18T09:12:00Z",
  "sourceCommit": "9f2c1b7e5d4a3c2b1a0f9e8d7c6b5a4f3e2d1c0b",
  "appCompatibility": {
    "minAppVersion": "2.0.0",
    "supportedApiVersions": ["2.0", "2.1"]
  },
  "defaultLocale": "en-US",
  "availableLocales": ["en-US", "zh-Hans", "ja", "de"],
  "categories": [
    { "id": "media", "name": { "en-US": "Media", "zh-Hans": "媒体" } },
    { "id": "productivity", "name": { "en-US": "Productivity", "zh-Hans": "效率" } }
  ],
  "plugins": [
    {
      "id": "now-playing",
      "name": { "en-US": "Now Playing", "zh-Hans": "正在播放" },
      "summary": {
        "en-US": "Shows the current media session on the island.",
        "zh-Hans": "在灵动岛上显示当前媒体会话。"
      },
      "publisher": {
        "id": "acme-labs",
        "displayName": "Acme Labs",
        "verified": true,
        "keyId": "acme-2026",
        "url": "https://example.com/acme"
      },
      "latest": {
        "version": "2.1.0",
        "apiVersion": "2.0.0",
        "minAppVersion": "2.0.0",
        "maxAppVersion": "2.99.99",
        "publishedAt": "2026-10-17T18:30:00Z",
        "channel": "stable",
        "yanked": false,
        "deprecated": false,
        "artifact": {
          "url": "https://github.com/fluidbar/plugins/releases/download/plugin-now-playing-v2.1.0/fluidbar-plugin-now-playing-2.1.0.fbp",
          "sha256": "9f2c1b7e5d4a3c2b1a0f9e8d7c6b5a4f3e2d1c0b…",
          "size": 1843200,
          "signatureUrl": "https://github.com/fluidbar/plugins/releases/download/plugin-now-playing-v2.1.0/signature.json",
          "keyId": "fluidbar-marketplace-2026",
          "algorithm": "ECDSA-P256-SHA256"
        },
        "capabilities": ["network", "media", "notifications", "clipboard", "files", "hotkeys", "ui", "background"],
        "locales": ["en-US", "zh-Hans", "ja"],
        "localeCoverage": { "en-US": 100, "zh-Hans": 98, "ja": 61 },
        "changelog": "### 2.1.0\n- Add lyrics…",
        "license": "MIT",
        "size": 1843200
      },
      "categories": ["media"],
      "tags": ["music", "lyrics", "gsmts"],
      "icon": "https://marketplace.fluidbar.app/assets/now-playing/icon.png",
      "screenshots": [
        { "src": "https://marketplace.fluidbar.app/assets/now-playing/compact.png", "alt": { "en-US": "Compact island", "zh-Hans": "紧凑灵动岛" } }
      ],
      "listing": {
        "defaultLocale": "en-US",
        "availableLocales": ["en-US", "zh-Hans", "ja"],
        "baseUrl": "https://marketplace.fluidbar.app/listings/now-playing/"
      },
      "historyUrl": "https://marketplace.fluidbar.app/plugins/now-playing.json",
      "stats": { "downloads": 12480, "rating": null },
      "status": "active",
      "featured": false,
      "blocked": null
    }
  ],
  "advisories": [],
  "keys": [
    {
      "keyId": "fluidbar-marketplace-2026",
      "role": "marketplace",
      "algorithm": "ECDSA-P256-SHA256",
      "publicKey": "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE…",
      "notBefore": "2026-01-01T00:00:00Z",
      "notAfter": "2027-01-01T00:00:00Z",
      "revokedAt": null
    }
  ],
  "signatures": [
    {
      "keyId": "fluidbar-marketplace-2026",
      "algorithm": "ECDSA-P256-SHA256",
      "value": "MEUCIQD…",
      "signedAt": "2026-10-18T09:12:05Z"
    }
  ]
}
```

**Key semantics.** `publisher.keyId` is the author/publisher key pinned on first install; `latest.artifact.keyId` is the **marketplace countersign key** the client requires for a marketplace install. Both signatures live in the same `signature.json` at `signatureUrl`; the client verifies the marketplace signature first (so a package cannot enter the index unsigned), then the author signature if the publisher is pinned. `artifact.sha256` is the SHA-256 of the exact `.fbp` bytes; `signature.json` signs that hash, not the ZIP's internal checksum file, so a re-zipped package fails verification.

**Canonicalization and signature.** `signatures` is excluded from the signed payload. The payload is serialized with [RFC 8785 JSON Canonicalization Scheme](https://www.rfc-editor.org/rfc/rfc8785) (sorted object keys, no insignificant whitespace, invariant number formatting); the signature is over the UTF-8 canonical bytes. Clients MUST reject an index whose signature does not verify, whose `generatedAt` is in the future by more than 24 h, or whose payload hash is older than the cached one (rollback attack). Signing the whole root index means an attacker cannot swap a plugin URL/hash even if they control the Pages host; per-plugin shards are referenced with their own `sha256` in the root.

**Per-plugin shard** (`plugins/<id>.json`) adds version history:

```json
{
  "schemaVersion": 1,
  "id": "now-playing",
  "versions": [
    { "version": "2.1.0", "publishedAt": "2026-10-17T18:30:00Z", "artifact": { "sha256": "…", "size": 1843200 }, "yanked": false, "changelog": "…" },
    { "version": "2.0.3", "publishedAt": "2026-09-02T11:00:00Z", "artifact": { "sha256": "…", "size": 1792000 }, "yanked": false },
    { "version": "2.0.2", "publishedAt": "2026-08-11T09:12:00Z", "artifact": { "sha256": "…", "size": 1780000 }, "yanked": true, "yankReason": "Crash on session change" }
  ]
}
```

**Marketplace plugin schema (key constraints)**

- `id` matches the manifest id, unique across `plugins`.
- `latest.version` equals the highest non-yanked semver in the shard.
- `artifact.url` must be HTTPS and on an allowlisted host (`github.com`, `objects.githubusercontent.com`, `marketplace.fluidbar.app`); no `file://`, no shorteners.
- `artifact.sha256` is 64 lowercase hex; `size > 0`; `signatureUrl` HTTPS.
- `publisher.keyId` is present in `keys[]` with `revokedAt: null`.
- `latest.minAppVersion <= appCompatibility.minAppVersion`? No — it must be `<= client version` at install time; the index is not filtered by server.
- `status` ∈ `active | deprecated | blocked`; `blocked` requires `blocked.reason` and `blocked.at`.
- `localeCoverage` values 0–100; `availableLocales ⊆ index.availableLocales`.
- `stats.rating` is `null` in MVP (reserved for later; clients must tolerate null).

#### Marketplace index JSON Schema (compact)

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://fluidbar.app/schemas/marketplace-index.schema.json",
  "title": "FluidBar Marketplace Index",
  "type": "object",
  "additionalProperties": false,
  "required": ["schemaVersion", "generatedAt", "sourceCommit", "appCompatibility", "defaultLocale", "plugins", "keys", "signatures"],
  "properties": {
    "schemaVersion": { "const": 1 },
    "generatedAt": { "type": "string", "format": "date-time" },
    "sourceCommit": { "type": "string", "pattern": "^[a-f0-9]{40}$" },
    "appCompatibility": {
      "type": "object",
      "additionalProperties": false,
      "required": ["minAppVersion", "supportedApiVersions"],
      "properties": {
        "minAppVersion": { "type": "string", "pattern": "^\\d+\\.\\d+\\.\\d+$" },
        "supportedApiVersions": { "type": "array", "items": { "type": "string", "pattern": "^\\d+\\.\\d+$" }, "uniqueItems": true, "minItems": 1 }
      }
    },
    "defaultLocale": { "$ref": "#/$defs/locale" },
    "availableLocales": { "type": "array", "items": { "$ref": "#/$defs/locale" }, "uniqueItems": true, "minItems": 1 },
    "categories": { "type": "array", "items": { "$ref": "#/$defs/category" } },
    "plugins": { "type": "array", "items": { "$ref": "#/$defs/plugin" } },
    "advisories": { "type": "array", "items": { "$ref": "#/$defs/advisory" } },
    "keys": { "type": "array", "minItems": 1, "items": { "$ref": "#/$defs/key" } },
    "signatures": { "type": "array", "minItems": 1, "items": { "$ref": "#/$defs/signature" } }
  },
  "$defs": {
    "locale": { "type": "string", "pattern": "^[a-z]{2,3}(?:-[A-Z][a-z]{3})?(?:-[A-Z]{2}|-[0-9]{3})?$" },
    "localizedString": {
      "oneOf": [
        { "type": "string", "minLength": 1, "maxLength": 2000 },
        { "type": "object", "minProperties": 1, "propertyNames": { "$ref": "#/$defs/locale" }, "additionalProperties": { "type": "string", "minLength": 1, "maxLength": 2000 } }
      ]
    },
    "category": {
      "type": "object",
      "additionalProperties": false,
      "required": ["id", "name"],
      "properties": { "id": { "type": "string" }, "name": { "$ref": "#/$defs/localizedString" } }
    },
    "publisher": {
      "type": "object",
      "additionalProperties": false,
      "required": ["id", "displayName", "verified"],
      "properties": {
        "id": { "type": "string", "pattern": "^[a-z0-9][a-z0-9-]{1,38}$" },
        "displayName": { "type": "string", "minLength": 1, "maxLength": 80 },
        "verified": { "type": "boolean" },
        "keyId": { "type": ["string", "null"] },
        "url": { "type": ["string", "null"], "format": "uri" }
      }
    },
    "artifact": {
      "type": "object",
      "additionalProperties": false,
      "required": ["url", "sha256", "size", "signatureUrl", "keyId", "algorithm"],
      "properties": {
        "url": { "type": "string", "format": "uri", "pattern": "^https://" },
        "sha256": { "type": "string", "pattern": "^[a-f0-9]{64}$" },
        "size": { "type": "integer", "minimum": 1, "maximum": 157286400 },
        "signatureUrl": { "type": "string", "format": "uri", "pattern": "^https://" },
        "keyId": { "type": "string", "minLength": 3, "maxLength": 80 },
        "algorithm": { "enum": ["ECDSA-P256-SHA256", "Ed25519"] }
      }
    },
    "version": {
      "type": "object",
      "additionalProperties": false,
      "required": ["version", "apiVersion", "minAppVersion", "publishedAt", "channel", "artifact", "capabilities", "locales"],
      "properties": {
        "version": { "type": "string", "pattern": "^\\d+\\.\\d+\\.\\d+([.-][0-9A-Za-z.-]+)?$" },
        "apiVersion": { "type": "string", "pattern": "^\\d+\\.\\d+\\.\\d+$" },
        "minAppVersion": { "type": "string", "pattern": "^\\d+\\.\\d+\\.\\d+$" },
        "maxAppVersion": { "type": ["string", "null"], "pattern": "^\\d+\\.\\d+\\.\\d+$" },
        "publishedAt": { "type": "string", "format": "date-time" },
        "channel": { "enum": ["stable", "beta"] },
        "yanked": { "type": "boolean", "default": false },
        "yankReason": { "type": ["string", "null"], "maxLength": 200 },
        "artifact": { "$ref": "#/$defs/artifact" },
        "capabilities": { "type": "array", "items": { "type": "string" }, "uniqueItems": true },
        "locales": { "type": "array", "items": { "$ref": "#/$defs/locale" }, "uniqueItems": true },
        "localeCoverage": { "type": "object", "additionalProperties": { "type": "integer", "minimum": 0, "maximum": 100 } },
        "changelog": { "type": "string", "maxLength": 8000 },
        "license": { "type": "string", "maxLength": 64 }
      }
    },
    "plugin": {
      "type": "object",
      "additionalProperties": false,
      "required": ["id", "name", "summary", "publisher", "latest", "categories", "listing", "historyUrl", "stats", "status"],
      "properties": {
        "id": { "type": "string", "pattern": "^[a-z0-9](?:[a-z0-9-]{0,38}[a-z0-9])?$" },
        "name": { "$ref": "#/$defs/localizedString" },
        "summary": { "$ref": "#/$defs/localizedString" },
        "publisher": { "$ref": "#/$defs/publisher" },
        "latest": { "$ref": "#/$defs/version" },
        "categories": { "type": "array", "items": { "type": "string" }, "uniqueItems": true, "minItems": 1, "maxItems": 3 },
        "tags": { "type": "array", "items": { "type": "string" }, "uniqueItems": true, "maxItems": 10 },
        "icon": { "type": "string", "format": "uri" },
        "screenshots": { "type": "array", "maxItems": 8, "items": { "type": "object", "additionalProperties": false, "required": ["src"], "properties": { "src": { "type": "string", "format": "uri" }, "alt": { "$ref": "#/$defs/localizedString" } } } },
        "listing": {
          "type": "object",
          "additionalProperties": false,
          "required": ["defaultLocale", "availableLocales", "baseUrl"],
          "properties": {
            "defaultLocale": { "$ref": "#/$defs/locale" },
            "availableLocales": { "type": "array", "items": { "$ref": "#/$defs/locale" }, "uniqueItems": true },
            "baseUrl": { "type": "string", "format": "uri", "pattern": "^https://" }
          }
        },
        "historyUrl": { "type": "string", "format": "uri", "pattern": "^https://" },
        "stats": {
          "type": "object",
          "additionalProperties": false,
          "required": ["downloads"],
          "properties": { "downloads": { "type": "integer", "minimum": 0 }, "rating": { "type": ["number", "null"], "minimum": 0, "maximum": 5 } }
        },
        "status": { "enum": ["active", "deprecated", "blocked"] },
        "featured": { "type": "boolean", "default": false },
        "blocked": {
          "type": ["object", "null"],
          "additionalProperties": false,
          "required": ["reason", "at"],
          "properties": { "reason": { "type": "string", "minLength": 1, "maxLength": 500 }, "at": { "type": "string", "format": "date-time" }, "replacement": { "type": ["string", "null"] } }
        }
      }
    },
    "advisory": {
      "type": "object",
      "additionalProperties": false,
      "required": ["id", "pluginId", "severity", "summary", "publishedAt"],
      "properties": {
        "id": { "type": "string", "pattern": "^(GHSA|CVE|FB)-[A-Za-z0-9-]+$" },
        "pluginId": { "type": "string" },
        "affectedVersions": { "type": "string", "maxLength": 200 },
        "severity": { "enum": ["low", "moderate", "high", "critical"] },
        "summary": { "type": "string", "maxLength": 1000 },
        "publishedAt": { "type": "string", "format": "date-time" },
        "url": { "type": "string", "format": "uri" }
      }
    },
    "key": {
      "type": "object",
      "additionalProperties": false,
      "required": ["keyId", "role", "algorithm", "publicKey"],
      "properties": {
        "keyId": { "type": "string", "minLength": 3, "maxLength": 80 },
        "role": { "enum": ["marketplace", "publisher"] },
        "algorithm": { "enum": ["ECDSA-P256-SHA256", "Ed25519"] },
        "publicKey": { "type": "string", "minLength": 32 },
        "notBefore": { "type": "string", "format": "date-time" },
        "notAfter": { "type": "string", "format": "date-time" },
        "revokedAt": { "type": ["string", "null"], "format": "date-time" }
      }
    },
    "signature": {
      "type": "object",
      "additionalProperties": false,
      "required": ["keyId", "algorithm", "value", "signedAt"],
      "properties": {
        "keyId": { "type": "string" },
        "algorithm": { "enum": ["ECDSA-P256-SHA256", "Ed25519"] },
        "value": { "type": "string", "minLength": 32 },
        "signedAt": { "type": "string", "format": "date-time" }
      }
    }
  }
}
```

`plugins/<id>.json` shard schema is the same `version` definition plus `{schemaVersion, id, versions[]}`; keep one definition in a shared file to avoid drift (`marketplace-plugin.schema.json`).

### 6.4 Package signature file

`signature.json` (detached; published next to the `.fbp`, never inside it):

```json
{
  "$schema": "https://fluidbar.app/schemas/signature.schema.json",
  "schemaVersion": 1,
  "package": {
    "sha256": "9f2c1b7e5d4a3c2b1a0f9e8d7c6b5a4f3e2d1c0b…",
    "size": 1843200
  },
  "signatures": [
    {
      "keyId": "acme-2026",
      "role": "author",
      "algorithm": "ECDSA-P256-SHA256",
      "publicKey": "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE…",
      "value": "MEUCIQD…",
      "signedAt": "2026-10-17T18:29:00Z"
    },
    {
      "keyId": "fluidbar-marketplace-2026",
      "role": "marketplace",
      "algorithm": "ECDSA-P256-SHA256",
      "publicKey": "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE…",
      "value": "MEUCIQD…",
      "signedAt": "2026-10-17T18:30:00Z"
    }
  ]
}
```

Rules: the author signature is optional for first-party plugins, required for verified publishers who want their key pinned; the marketplace signature is required for marketplace installs; both sign the same `package.sha256`; `publicKey` is SPKI base64 (PKIX) for ECDSA, raw 32-byte key for Ed25519; clients verify `keyId` against pinned keys and `keys.json`, then the signature, then the package hash, then per-file checksums.

### 6.5 Plugin locale bundle schema

```json
{
  "$schema": "https://fluidbar.app/schemas/plugin-locale.schema.json",
  "type": "object",
  "additionalProperties": false,
  "required": ["locale", "version", "messages"],
  "properties": {
    "locale": { "$ref": "plugin.schema.json#/$defs/locale" },
    "version": { "const": 1 },
    "messages": {
      "type": "object",
      "minProperties": 1,
      "propertyNames": { "pattern": "^[a-zA-Z][A-Za-z0-9._-]{0,127}$" },
      "additionalProperties": { "type": "string", "minLength": 1, "maxLength": 4000 }
    }
  }
}
```

Validation beyond schema: placeholder parity with `en-US`, ICU parse, no HTML (except a documented `**bold**`/`[link](url)` subset), no leading/trailing whitespace, max 4,000 chars per message.

### 6.6 Listing schema (localized marketplace page)

```json
{
  "$schema": "https://fluidbar.app/schemas/listing.schema.json",
  "type": "object",
  "additionalProperties": false,
  "required": ["locale", "name", "summary", "description"],
  "properties": {
    "locale": { "type": "string" },
    "name": { "type": "string", "minLength": 1, "maxLength": 80 },
    "summary": { "type": "string", "minLength": 1, "maxLength": 200 },
    "description": { "type": "string", "minLength": 1, "maxLength": 8000 },
    "screenshots": { "type": "array", "maxItems": 8, "items": { "type": "object", "additionalProperties": false, "required": ["src"], "properties": { "src": { "type": "string" }, "alt": { "type": "string", "maxLength": 300 } } } },
    "changelog": { "type": "string", "maxLength": 8000 },
    "machineTranslated": { "type": "boolean", "default": false }
  }
}
```

### 6.7 Settings file schema

Host-owned `%APPDATA%\FluidBar\plugins\<id>\settings.json`:

```json
{
  "schemaVersion": 1,
  "pluginId": "now-playing",
  "values": {
    "showLyrics": true,
    "pollMs": 1000,
    "lyricsSource": "auto"
  },
  "updatedAt": "2026-10-18T09:00:00Z"
}
```

Values are validated against the manifest's control definitions (`type`, `min`, `max`, `options`, `pattern`); unknown keys are preserved (forward compatibility) but not exposed until declared. Secrets (`type: "secret"`) are stored with DPAPI (`ProtectedData.Protect`, `CurrentUser`) and never written to logs; the settings UI shows only "Set/Not set".

### 6.8 Contribution descriptors: design rules

- **Templates, not arbitrary trees, in MVP.** The host knows `media.v1`, `notification.v1`, etc.; plugins map fields into named slots. This guarantees animation, accessibility, theming and i18n behavior.
- **Slot fields are data, not markup.** `field` refers to a well-known payload field or a plugin-provided dictionary value; no expression evaluation beyond a restricted `when` grammar (`source == 'x' && hasTrack`, `&&`, `||`, `!`, parentheses).
- **Commands are IDs, not delegates.** The manifest declares; the plugin registers a handler at runtime (`context.Commands.Register("now-playing.toggle", handler)`). Missing handler → command disabled with a tooltip, not a crash.
- **Settings are host-rendered.** No plugin XAML in MVP. This is what makes live language switching, high-contrast themes and DPI scaling work uniformly.
- **Widgets are accessory templates**, not independent windows, so they inherit the island's animation/stack policy.
- **Layout IDs are namespaced** (`<plugin-id>.<name>`); duplicate IDs fail validation.
- **Every contribution is removable**: disabling a plugin removes its layouts, commands, settings page and widgets; the host must not cache them.

### 6.9 Schema versioning

- `schemaVersion` is an integer per file; a breaking change bumps it. The tool supports the current and previous version for one release cycle (read old, write new).
- `apiVersion` is the SDK ABI; `schemaVersion` is the manifest format; they move independently (a manifest format change does not require a new SDK ABI).
- The client refuses to install a package whose `schemaVersion` is newer than it understands (with a "update FluidBar" message), and warns on older-but-supported.
- JSON Schema files are published at stable URLs (`https://fluidbar.app/schemas/...`) with `$id` matching, so editor autocomplete and third-party tools work.

---

## 7. Contribution and review process: "PR merged → published"

### 7.1 Contributor journey

1. **Scaffold:** `dotnet new fluidbar-plugin -n NowPlaying` (template package) creates `plugins/now-playing/` with `manifest.json`, `Plugin.cs`, `locales/en-US.json`, `README.md`, `LICENSE`, `icon.png`, tests and a `csproj` that references `FluidBar.Sdk` with `<EnableDynamicLoading>true</EnableDynamicLoading>` and `PrivateAssets="all"` on the SDK.
2. **Develop:** `dotnet run --project tools/FluidBar.PluginTool -- dev --plugin plugins/now-playing` launches FluidBar with the plugin loaded from a dev folder (no install, no signature) and restarts the host on rebuild (true hot reload is a v2.2 item).
3. **Validate locally:** `dotnet run --project tools/FluidBar.PluginTool -- validate --plugin plugins/now-playing --strict` — same validator as CI.
4. **Package:** `... pack --plugin plugins/now-playing --out artifacts` produces a `.fbp` and a hash; contributors can install it locally via "Install from file…" to test the real path.
5. **PR:** open against `main` in `fluidbar/plugins`; the PR template auto-fills the checklist; CI runs the gates.
6. **Review:** maintainer + (if labelled) security reviewer; feedback in the PR; contributor pushes fixes; CI re-runs.
7. **Merge:** squash merge; the publish workflow builds from the merge commit; the PR gets a comment with release URL, hash, signature key id and index entry.
8. **Live:** index refreshed and Pages deployed; clients see it on the next refresh (≤ 5 min cache).

**Local-first promise:** a contributor can do everything except publish without a signing key or maintainer involvement. The tool must never require network access to validate/build.

### 7.2 PR template (checklist)

```markdown
## Plugin

- **ID:** `now-playing`
- **Version:** `2.1.0`
- **Publisher:** `acme-labs`
- **One-line summary (en-US):**
- **Repository / homepage:**

## Checklist

### Identity & licensing
- [ ] `id` is unique, lowercase, descriptive, not reserved, and matches the folder name
- [ ] `version` is semver and greater than the last published version
- [ ] `publisher.id` is mine / my organization's and matches previous releases
- [ ] `LICENSE` is present and the `license` field matches it
- [ ] I have the rights to all code, assets, and fonts in this package
- [ ] No trademarks/logos I don't own; plugin name doesn't impersonate another publisher

### Functionality
- [ ] Plugin loads, starts, stops, disables and uninstalls cleanly (tested with the real installer)
- [ ] Works in compact island and hover-card modes; no clipped text at 100%/125%/150% DPI
- [ ] Handles missing media session / API failure / offline gracefully; no unhandled exceptions
- [ ] Does not block the UI thread; startup work is async and cancellable
- [ ] Respects disabled state and user settings; no background work while disabled
- [ ] Unload test passes (`WeakReference` to the ALC is collected) or the plugin documents "restart required"
- [ ] Memory/CPU are bounded; polling interval is configurable and ≥ 500 ms

### Security & privacy
- [ ] I declare every capability the plugin uses, with a reason for risky ones
- [ ] Any `P/Invoke`, `Process.Start`, registry access or native dependency is declared, minimal, and reviewed (no undisclosed native code)
- [ ] No downloaded executables/DLLs at runtime; no self-updater
- [ ] No secrets, tokens, or personal data in the repo, package, logs, or telemetry
- [ ] Telemetry is off by default; if on, it is declared with an endpoint and privacy policy
- [ ] Network calls use HTTPS and only the declared domains; no wildcard unless justified
- [ ] Clipboard access is user-initiated and minimal
- [ ] I understand plugins run with full user rights and have designed accordingly

### UX & accessibility
- [ ] Icon is 256×256 PNG, legible on light and dark backgrounds
- [ ] Strings are localized (en-US complete); no hardcoded user-facing text in code
- [ ] Keyboard accessible; focus states; screen-reader names for custom controls
- [ ] Respects theme tokens, high contrast, and reduced motion (no essential info only in animation)
- [ ] Settings page uses declared controls (no custom XAML) and has sensible defaults

### Packaging & compatibility
- [ ] `minAppVersion`/`apiVersion` are correct; plugin builds against the current SDK
- [ ] No WPF/SDK assemblies in `plugin/`; `deps.json` present; TFM is `net10.0-windows…`
- [ ] `checksums.json` verifies; package is deterministic (two builds → same SHA-256)
- [ ] `locales/*.json` validate; placeholders and plural forms are correct
- [ ] Tested on Windows 10 19041+ and Windows 11, x64 (and arm64 if declared)
- [ ] Changelog updated

### Review
- [ ] I have read `docs/plugin-security.md` and the review policy
- [ ] High-risk capabilities (shell/files-write/network-*/process/registry) are justified below
```

### 7.3 Reviewer checklist

**Tier 1 — every PR (maintainer, ~20–30 min):**

- Manifest correctness: id/version/publisher, compatibility, entry path, capabilities vs code.
- Build/test/pack green; deterministic hash; no forbidden assemblies; package contents sane.
- License and asset provenance; README/screenshots; changelog.
- UX: compact + hover card fit; dark/light; 100%/150% DPI; disabled state; error state.
- i18n: `en-US` complete, no concatenation, no hardcoded strings; locale files valid.
- Code: async correctness, cancellation, disposal, no UI-thread blocking, no unbounded loops/timers.
- Privacy: network endpoints, clipboard, files, telemetry; log content.
- Tests: meaningful smoke + unload; no network reliance in tests.

**Tier 2 — security review (required for `security` label):**

- Capability least-privilege: is each capability necessary? Could the host API replace raw access?
- Data flow: what leaves the machine, to which endpoint, under what consent? Secrets handling?
- Code execution: `Process.Start`, `DllImport`, dynamic `Assembly.Load`, reflection over private host types, `BinaryFormatter`/unsafe deserialization, zip extraction.
- Filesystem: path traversal, symlinks, writes outside `$PLUGIN_DATA`, world-readable secrets.
- Network: TLS validation disabled? certificate pinning? proxy? redirects to undeclared domains?
- Persistence: startup registration, scheduled tasks, services, shell extensions, registry run keys.
- Supply chain: new NuGet dependencies, licenses, vulnerabilities, native binaries.
- Failure modes: what happens on malformed input, huge payloads, rapid events, missing files?
- Does the capability description match behavior? If not, reject.

**Tier 3 — first-time publisher / high-risk (maintainer + security + product):**

- Publisher identity verification (GitHub org, domain, existing track record).
- Manual install on a clean VM; observe network with Fiddler/Procmon for 10 minutes.
- Confirm no bundled malware (Defender scan + VirusTotal for any binary), no obfuscation.
- Approval to create the publisher key and pin it in `keys.json`.
- Written decision recorded in the PR.

**Rejection reasons (non-exhaustive):** obfuscated code, bundled binaries without source, undisclosed telemetry, self-updater, capability mismatch, license violation, impersonation, crypto-mining, adware, bypassing OS security, destructive behavior, excessive permissions without justification.

### 7.4 Automated gates (must be green to merge)

| Gate | Tool | Failure behavior |
|---|---|---|
| JSON Schema | `FluidBar.PluginTool validate` (uses `JsonSchema.Net`) | Block |
| Manifest semantics | same tool (cross-field rules) | Block |
| Build | `dotnet build -c Release` | Block |
| Tests | `dotnet test` + host harness | Block |
| Unload/leak | harness (`WeakReference` + GC loop) | Block (or require `restartRequired: true` + approval) |
| Package determinism | build twice, compare SHA-256 | Block |
| Forbidden contents | tool (`FluidBar.Sdk`, WPF, `.exe`, `.pdb`, obfuscators) | Block |
| Size limits | 50 MB compressed / 150 MB uncompressed / 5k files | Block |
| License/README/icon | tool | Block |
| Locale validation | tool (schema, placeholders, ICU, parity) | Block |
| Secret scan | gitleaks | Block |
| Dependency vulnerabilities | `dotnet list package --vulnerable --include-transitive` | Block for high/critical |
| SBOM | `dotnet CycloneDX` or `sbom-tool` | Warn (block later) |
| CodeQL | GitHub CodeQL (C#) | Block for high severity |
| Capability policy | tool (`security` label, reasons) | Block/Routes to Tier 2 |
| Screenshot presence | tool | Warn |
| Docs link check | lychee | Warn |

Do not let contributors bypass gates with `[skip ci]`; branch protection requires the checks.

### 7.5 Publish pipeline (merge → live)

```
merge to main
  → publish-plugin.yml (paths plugins/**, concurrency 1, environment marketplace-publish)
  → detect changed ids + versions
  → build Release from merge commit (pinned SDK, locked restore)
  → run tests + unload harness
  → pack deterministic .fbp
  → verify no shared assemblies / forbidden files
  → compute SHA-256 + checksums.json
  → sign with marketplace key (HSM/KMS via OIDC) + optional author sig
  → create tag plugin-<id>-v<version>, upload .fbp + signature.json + SBOM
  → regenerate index.json + plugins/<id>.json + listings
  → sign index (RFC 8785 canonical payload)
  → deploy Pages
  → post-publish verification: download from public URL, verify hash/sig/index, install in a clean VM
  → comment on PR with links; update status check
```

**Idempotency and immutability**

- If the tag/asset exists: compare hashes. Same hash → no-op success; different hash → fail loudly (someone rewrote history).
- Never overwrite an asset for a published version; publish a new patch instead.
- Keep releases forever (or at least 2 years); yanking is an index flag, not a deletion.
- Index generation is deterministic: same inputs → byte-identical payload. Set `generatedAt` to the source commit timestamp (not wall-clock time) so re-running the generator for the same commit is reproducible.

**Acceptance criteria for "PR merged → appears in marketplace":**

1. Merge triggers the workflow within 60 s.
2. Build + sign + release + index + deploy completes in < 10 min (p95).
3. The public `index.json` contains the new version with the correct hash/signature before the workflow reports success.
4. A clean-VM job installs the plugin from the public index and reaches `Active` within 60 s.
5. A client with a cached index (TTL 5 min) sees the update within 10 min.
6. No partial state: if any step fails, the index is unchanged and the PR is notified.
7. Rollback: a maintainer can yank the version via a one-line PR/issue command; clients pick it up within the index TTL.

### 7.6 Rollback and incident response

- **Yank a version:** add `"yanked": true` + `"yankReason"` to the shard, regenerate/sign/deploy index. Clients stop offering it; installed users see "This version was withdrawn — update/rollback" with the reason.
- **Block a plugin:** `status: "blocked"` + reason; clients disable it on refresh (even if installed) and block installs; release assets retained for forensics.
- **Bad update auto-rollback:** if the plugin crashes 3× in 24 h after an update, revert to `lastGoodVersion` and notify; if no good version, disable.
- **Key compromise:** revoke in `keys.json`, sign with the new key, publish an advisory, force index refresh; clients warn on installed packages signed with the revoked key.
- **Malware report:** private security issue → triage → block → advisory → notify GitHub (if hosting) → preserve evidence → post-mortem.
- **Communication:** status page (GitHub issue) + in-app banner for blocked plugins; no silent removal without a reason.

### 7.7 Roles and SLAs

| Role | Responsibility | SLA |
|---|---|---|
| Marketplace maintainer | Tier 1 review, merge, yank, publisher onboarding | 5 business days first review; 24 h for blocks |
| Security reviewer | Tier 2/3, incident response, key management | 5 business days; 24 h for reports |
| Release engineer | CI, signing, index, Pages, rollback drills | Publish < 10 min; monthly drill |
| Translation maintainer | Weblate, glossary, locale QA | 5 business days per PR |
| Plugin author | Fix feedback, respond to reports, maintain compatibility | 14 days to respond before deprecation |

### 7.8 Contributor documentation set

- `docs/plugin/quickstart.md` — scaffold → run → package → PR in 15 minutes.
- `docs/plugin/manifest.md` — every field, with examples (generated from schema + hand-written notes).
- `docs/plugin/contributions.md` — layouts, settings, commands, context menu, widgets; screenshots.
- `docs/plugin/security.md` — trust model, capabilities, what the host can/can't enforce, disclosure policy.
- `docs/plugin/localization.md` — JSON bundle format, ICU subset, Weblate, fallback, pseudo-loc.
- `docs/plugin/testing.md` — harness, unload test, DPI/RTL checks, CI commands.
- `docs/plugin/publishing.md` — versioning, signing (author keys), release process, yanking.
- `docs/marketplace/review-policy.md` — acceptance criteria, prohibited behavior, appeals.
- `docs/marketplace/architecture.md` — index, signatures, hosting, cache, incident response.
- `samples/` — minimal text plugin, media plugin, settings-heavy plugin, localized plugin.

### 7.9 Definition of done for a plugin

- Installs, updates, rolls back and uninstalls from the public index on a clean Windows VM.
- Signature and hash verify; publisher key pinned; capability consent shown and recorded.
- `en-US` + one additional locale complete; layout passes pseudo-loc and RTL smoke tests.
- No unhandled exception in a 24-hour soak with the plugin enabled; memory stable.
- Docs and changelog updated; support contact in the manifest/repo.
- If the plugin is abandoned for 12 months and has a security issue, maintainers can block it without breaking the app.

---

## 8. MVP scope vs later

### 8.1 MVP (v2.0) — ship the loop, not the universe

**Goal:** a contributor can merge a plugin PR and a user can discover, install, update, roll back and uninstall it safely; the app is localized in English and Simplified Chinese; the whole loop is reproducible.

| Workstream | MVP deliverables | Explicitly out |
|---|---|---|
| SDK & loader | `FluidBar.Sdk` 2.0 (`IFluidBarPlugin`, `IPluginContext`, `IslandEvent` moved, `IIslandPublisher`, `IPluginSettings`, `IPluginLocalization`, `IPluginLog`, command registry); collectible ALC loader; manifest validation; install/uninstall/enable/disable; crash guard; per-plugin logs; safe mode | Out-of-proc, XAML UI, native deps, plugin-to-plugin APIs |
| Contributions | Templates: compact island (`text/status/progress/media/notification/agent/clock`), hover cards (same set), settings pages (declarative controls), commands, context-menu actions, accessory widgets | Arbitrary layout trees, custom renderers, plugin windows, dock/panel contributions |
| Marketplace | Browse/search/category filter, detail page (localized), install/update/rollback/uninstall, version history, capability consent, report, offline `.fbp` install, update check, yanked/blocked states | Ratings/reviews, collections, recommendations, paid, beta channel UI, dependency auto-install |
| Index & hosting | Signed `index.json` on Pages, per-plugin shards, listing shards, `keys.json`, raw fallback, bundled bootstrap index, ETag cache, install counts nightly | Multi-CDN, mirrors, delta updates, staged rollout |
| Publishing | Monorepo, validator tool, PR gates, publish workflow, signing via KMS/OIDC, Pages deploy, post-publish verification, rollback drill | External repos, author signing UX, reproducible-build attestations (add if cheap) |
| Security | Package hash + marketplace signature, publisher key pinning (TOFU), capability consent + audit log, block/yank, zip safety, no-sandbox disclosure | AppContainer/out-of-proc enforcement, per-plugin network firewall, secret broker |
| i18n | `en-US` + `zh-Hans` app strings, runtime switch (+ restart fallback), localized island/settings/tray/errors, plugin JSON locales with fallback, marketplace listing localization, RTL scaffolding, CJK font tokens, pseudo-loc CI test, Weblate setup | Additional official languages, full RTL polish, machine translation, locale-specific screenshots at scale |
| Docs | Quickstart, manifest/contribution/security/localization/publishing/review docs, 4 sample plugins | Video tutorials, API reference site (generate from XML docs if time) |
| Migration | Keep 4 built-ins compiled-in; add manifests + expose them in marketplace as "built-in"; dogfood contributions for one built-in (e.g., clipboard settings) | Full removal of built-in code into packages |

**Suggested sequencing (2 engineers, ~12 weeks):**

1. Weeks 1–2: SDK surface freeze + manifest/schema + validator CLI + plugin template; spike ALC loading in a WPF harness (including single-file host).
2. Weeks 3–5: loader/lifecycle/install/uninstall/logging/crash guard; contribution registry + templates; migrate one built-in to the contribution model.
3. Weeks 4–6 (parallel): marketplace UI shell (list/detail/install/update) against a local mock index; index generator + signing tool.
4. Weeks 6–8: real index on Pages + publish workflow + first-party sample plugins end-to-end; rollback/yank.
5. Weeks 8–10: i18n extraction (en-US + zh-Hans), `Loc` infrastructure, plugin locales, listing localization, RTL/font tokens.
6. Weeks 10–11: security hardening (zip, signature, key pinning, consent, audit), review policy, docs, contributor dry run with an external author.
7. Week 12: soak, DPI/RTL/locale QA, incident drill, release.

This is aggressive but feasible if the marketplace UI reuses existing settings-window visual language and the index is generated, not hand-built.

**MVP non-goals (say no explicitly):** running unreviewed code, plugin XAML, ratings, plugin-to-plugin dependencies, auto-updating plugins outside the marketplace, paid plugins, telemetry dashboards, mobile/web marketplace.

### 8.2 Later (v2.1 → v2.3)

**v2.1 — isolation and richer UI (8–12 weeks):**
- Out-of-process host (`StreamJsonRpc` over named pipe), restricted token + job object, heartbeat/watchdog, capability broker (network/files/clipboard/notifications), descriptor-only UI.
- "Isolated" install mode for sideloaded/unreviewed plugins; per-plugin process view in settings (memory/CPU/kill).
- Custom layout DSL (restricted primitive tree) + host theme tokens; plugin XAML for verified publishers (restart-to-update).
- Ratings/reviews (GitHub auth, verified installs, moderation), collections/featured.
- Staged rollout (`rollout`), beta channel, dependency auto-install with consent.

**v2.2 — developer experience and scale (6–10 weeks):**
- `dotnet watch`-style plugin dev loop, hot reload for descriptor/settings/locales, debug attach, plugin logs viewer, crash dumps.
- Command palette + user-assigned hotkeys; per-plugin settings search; accessibility pass.
- External repository plugins built from pinned commits; build attestations/SLSA; SBOM in index.
- Mirrors/CDN, delta updates, offline enterprise policy (`allowedPublishers`, `blockedPlugins`, `disableMarketplace`).
- More locales (ja/de/fr/es/pt-BR/ru/ko/zh-Hant), full RTL, locale screenshot automation.

**v2.3 — platform maturity:**
- WebView2 UI contributions for rich plugin panels (sandboxed, bridged).
- Public API for monitors/events; plugin-to-plugin services with explicit contracts.
- Plugin analytics (opt-in), crash telemetry, quality scores (not just downloads).
- Theme/icon packs as a plugin category; accessibility contributions (screen-reader descriptions).
- Marketplace website with search SEO, localization, publisher profiles.

### 8.3 What not to build (ever, or without a strong reason)

- A custom scripting language/runtime for plugins (use .NET).
- An in-app code editor for plugins.
- A plugin permission system that pretends to sandbox in-proc code.
- A proprietary package format that requires a custom installer.
- Ratings before there are enough users to make them meaningful.
- A plugin store that hosts binaries the project cannot rebuild.

---

## 9. Risks and mitigations

| # | Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| 1 | Malicious/compromised plugin steals credentials or crypto | Medium | Critical | Review + CI-built signed artifacts + publisher pinning + capability consent + block/yank + out-of-proc lane; incident response; honest disclosure; no silent auto-update to new publisher key |
| 2 | In-proc plugin crashes/hangs the island | High | High | try/catch every callback, watchdog/timeout, crash counter + auto-disable/rollback, safe mode, out-of-proc for risky plugins |
| 3 | ALC unload leaks / update requires restart | High | Medium | No plugin XAML rule, lease-owned subscriptions, leak tests, restart fallback UX, keep previous version |
| 4 | WPF + single-file + ALC edge cases (pack URIs, resolver, native deps) | Medium | High | Week-1 spike, CI integration tests with published single-file build, no plugin XAML/native in MVP |
| 5 | API churn breaks the ecosystem | Medium | High | Semver `apiVersion`, additive-only within major, DIM, deprecation policy, compatibility tests against sample plugins, dual-contract shim for one cycle |
| 6 | Marketplace moderation burden / low supply | High | Medium | Start first-party + invited publishers, automate gates, no ratings, clear policy, templates, fast review SLA |
| 7 | Signing key compromise | Low | Critical | HSM/KMS, OIDC short-lived creds, offline index key, rotation + revocation list, audit logs, drills |
| 8 | GitHub dependency (outage, rate limits, policy change) | Medium | Medium | Pages/raw fallback, bundled bootstrap index, direct release URLs (no API), plan for R2/Cloudflare mirror |
| 9 | Index rollback / freeze attack | Low | High | Signed index with `generatedAt`, client rejects older-than-cached payload, monotonic version check |
| 10 | Localization drift / broken layouts | High | Medium | CI parity/placeholder/ICU checks, pseudo-loc, screenshot tests, translator glossary, per-locale reviewers, fallback to English |
| 11 | CJK/RTL rendering bugs | Medium | Medium | Font tokens + `Language` override, RTL test locales, mirrored icon metadata, DPI matrix, clean-VM QA |
| 12 | Performance degradation with many plugins | Medium | Medium | Lazy activation, startup budget, event coalescing, per-plugin CPU/memory metrics, limit enabled plugins, out-of-proc for heavy ones |
| 13 | Users misunderstand permissions | High | Medium | Plain-language consent, "runs with your access" banner, audit log, link to security doc, no "verified = safe" language |
| 14 | Namesquatting / impersonation | Medium | Medium | Reserved ids, publisher verification, first-publish manual review, id transfer policy, trademark takedown |
| 15 | License/asset violations | Medium | Medium | License check, provenance attestation in PR, DMCA process, takedown SLA |
| 16 | AV/Defender false positives on plugin DLLs | Medium | Medium | Authenticode for first-party, no obfuscation, reproducible builds, submit to Microsoft, signed marketplace packages |
| 17 | Settings/data loss on update | Low | High | Settings schema version + migration, backup before update, host-owned storage, atomic writes |
| 18 | Accessibility regressions | Medium | Medium | Host-rendered UI, accessibility checklist, UI Automation tests, high-contrast theme, screen-reader review |
| 19 | Scope creep delays v2 | High | High | MVP non-goals enforced, feature flags, dogfood one built-in first, cut ratings/out-of-proc |
| 20 | Legal/privacy (telemetry, GDPR) | Low | High | Off by default, explicit consent, data minimization, privacy policy, deletion on uninstall, DPA for any hosted service |

### 9.1 Open questions / spikes (do these before committing dates)

1. **ALC + WPF spike:** load a plugin that only emits data and a declarative settings page; load/unload 100×; measure leaks; test in a `PublishSingleFile` host. Decide if `restartRequired` is the default.
2. **Pack URI behavior:** confirm whether `ResourceDictionary` pack URIs resolve for a collectible-ALC plugin; if not (expected), lock the "no plugin XAML" rule.
3. **Settings UI without XAML:** prototype the declarative settings renderer for 6 control types; verify theming, DPI, RTL and screen readers.
4. **Signing choice:** ECDSA P-256 via BCL vs Ed25519 via NSec; HSM/KMS availability; signature size and verification cost.
5. **Index hosting:** measure Pages latency/size in target regions (especially CN), decide on a mirror/CDN earlier if needed.
6. **Event throughput:** simulate 100 events/s from 10 plugins; verify stack policy/coalescing and UI frame rate.
7. **Localization extraction:** estimate strings in `MainWindow.xaml`, `SettingsWindow.xaml`, `App.xaml.cs`, monitors and plugins; design key namespaces.
8. **Migration path for built-ins:** can clipboard/media/agent/notifications be expressed with the contribution model without losing behavior? If not, keep them compiled-in and exclude from the API contract.
9. **Marketplace UI reuse:** how much of `SettingsWindow` can be reused for list/detail without a rewrite?
10. **Update semantics:** in-proc update with unload vs restart; measure unload success rate with real plugins.

---

## 10. References

Primary sources consulted (external content treated as data):

- VS Code extension manifest: <https://code.visualstudio.com/api/references/extension-manifest>
- VS Code workspace trust (capability-style consent): <https://code.visualstudio.com/api/extension-guides/workspace-trust>
- Obsidian plugin submission (community directory, automated review): <https://docs.obsidian.md/Plugins/Releasing/Submit+your+plugin>
- Figma plugin manifest (`networkAccess`, `permissions`, `api`): <https://developers.figma.com/docs/plugins/manifest/>
- Figma "How plugins run" (sandbox): <https://www.figma.com/plugin-docs/how-plugins-run/>
- JetBrains plugin signing (author + Marketplace countersign, 2021.2+): <https://plugins.jetbrains.com/docs/intellij/plugin-signing.html>
- JetBrains build number ranges: <https://plugins.jetbrains.com/docs/intellij/build-number-ranges.html>
- Raycast store preparation/review: <https://developers.raycast.com/basics/prepare-an-extension-for-store>, <https://manual.raycast.com/extensions-guidelines>
- PowerToys Command Palette extensions SDK (out-of-proc COM + AppExtension): <https://github.com/microsoft/PowerToys/blob/main/src/modules/cmdpal/doc/initial-sdk-spec/initial-sdk-spec.md>
- OBS forum plugin resources (no official marketplace): <https://obsproject.com/forum/plugins/>
- .NET assembly unloadability (collectible ALC, cooperative unload, R2R ignored): <https://learn.microsoft.com/en-us/dotnet/standard/assembly/unloadability>
- WPF unload limitation discussion: <https://github.com/dotnet/wpf/issues/1764>
- gRPC over named pipes: <https://learn.microsoft.com/en-us/aspnet/core/grpc/interprocess-namedpipes>
- Single-file assembly API behavior (`Assembly.Location` empty): <https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/5.0/assembly-api-behavior-changes-for-single-file-publish>
- RFC 8785 JSON Canonicalization Scheme: <https://www.rfc-editor.org/rfc/rfc8785>
- WPF localization / `Language` property background: <https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/wpf-globalization-and-localization-overview>
- Weblate (resx/JSON, GitHub integration): <https://docs.weblate.org/>
- ICU MessageFormat plural rules: <https://unicode-org.github.io/icu/userguide/format_parse/messages/>

FluidBar files inspected (read-only): `FluidBar.csproj`, `App.xaml.cs`, `PluginManager.cs`, `PluginCatalog.cs`, `EventSystem.cs`, `Settings.cs`, `IslandPresentation.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`, `SettingsWindow.xaml`, `SettingsWindow.xaml.cs`, `Plugins/catalog.json`, `Plugins/README.md`, `Plugins/Template/README.md`, `README.md`, `docs/superpowers/specs/2026-06-14-plugin-ecosystem-design.md`, `docs/superpowers/plans/2026-06-14-plugin-ecosystem.md`.

---

## Appendix A — Loader pseudocode (host)

```csharp
public sealed class PluginHost
{
    private readonly Dictionary<string, PluginHandle> _handles = new(StringComparer.OrdinalIgnoreCase);

    public async Task<PluginLoadResult> LoadAsync(PluginInstall install, CancellationToken ct)
    {
        var manifest = install.Manifest;
        if (!ApiCompatibility.IsSupported(manifest.ApiVersion, HostInfo.AppVersion))
            return PluginLoadResult.Incompatible(manifest);

        if (!PackageVerifier.Verify(install.PackagePath, install.IndexEntry, out var reason))
            return PluginLoadResult.SignatureInvalid(reason);

        var root = Path.GetFullPath(install.Directory)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var entryPath = Path.GetFullPath(Path.Combine(root, manifest.Entry.Assembly));
        if (!entryPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return PluginLoadResult.Invalid("entry outside package");

        var alc = new PluginLoadContext(entryPath, manifest.Id);
        try
        {
            var asm = alc.LoadFromAssemblyPath(entryPath);
            var type = asm.GetType(manifest.Entry.Type, throwOnError: true)!;
            if (!typeof(IFluidBarPlugin).IsAssignableFrom(type))
                return PluginLoadResult.Invalid("entry type does not implement IFluidBarPlugin");

            var plugin = (IFluidBarPlugin)Activator.CreateInstance(type)!;
            var context = new PluginContext(manifest, install.Directory, _services);
            await plugin.InitializeAsync(context, ct).WaitAsync(TimeSpan.FromSeconds(5), ct);
            await plugin.StartAsync(ct).WaitAsync(TimeSpan.FromSeconds(5), ct);

            var handle = new PluginHandle(manifest, plugin, context, alc);
            _handles[manifest.Id] = handle;
            return PluginLoadResult.Active(handle);
        }
        catch (Exception ex)
        {
            _log.Error(manifest.Id, "load failed", ex);
            alc.Unload();
            return PluginLoadResult.Failed(ex);
        }
    }
}
```

## Appendix B — Minimal plugin example

```csharp
using FluidBar.Sdk;

public sealed class HelloPlugin : IFluidBarPlugin
{
    private IPluginContext? _context;
    private IPluginLease? _lease;

    public ValueTask InitializeAsync(IPluginContext context, CancellationToken ct)
    {
        _context = context;
        _lease = context.Leases.Create();
        _lease.Timers.Create(TimeSpan.FromMinutes(30), () =>
            context.Islands.Publish(new IslandEvent(
                Source: context.PluginId,
                Title: context.Localization.Get("island.title"),
                Content: context.Localization.Get("island.content"),
                IconKind: "hello")));
        return ValueTask.CompletedTask;
    }

    public ValueTask StartAsync(CancellationToken ct) => ValueTask.CompletedTask;
    public ValueTask StopAsync(CancellationToken ct) { _lease?.Dispose(); return ValueTask.CompletedTask; }
    public ValueTask DisposeAsync() { _lease?.Dispose(); return ValueTask.CompletedTask; }
}
```

## Appendix C — Glossary

| Term | Meaning |
|---|---|
| Island | The pill-shaped top-of-screen window (`MainWindow`) |
| Hover card | Expanded panel shown on pointer hover |
| Layout template | Host-owned visual arrangement (`media.v1`, …) that a plugin fills with slot data |
| Contribution point | A declared extension surface (layout, settings page, command, menu item, widget) |
| Capability | A declared access need (network, files, clipboard, …); disclosure in-proc, enforcement out-of-proc |
| Lane A / Lane B | In-process trusted plugins / out-of-process isolated plugins |
| `.fbp` | FluidBar Plugin package (deterministic ZIP) |
| Index | Signed JSON catalog of plugins/versions/artifacts |
| Shard | Per-plugin JSON with version history, referenced by hash from the index |
| TOFU | Trust-on-first-use publisher key pinning |
| Pseudo-loc | `qps-ploc` synthetic locale for layout testing |
| ICU | MessageFormat syntax used for plurals/gender in locale bundles |
| ALC | `System.Runtime.Loader.AssemblyLoadContext` |
