# Plugin Trust and Local Plugins

Status: frozen with the v1 plugin platform.
Code: `src/FluidBar.PluginHost`, `src/FluidBar.Marketplace`, `src/FluidBar.App/Plugins`.

## 1. Two channels

### Market root (signed, reviewed)

The market root is `%APPDATA%\FluidBar\plugins` (or `FLUIDBAR_PLUGINS_DIR`) and uses the **versioned layout**:

```text
plugins/<plugin-id>/active.json
plugins/<plugin-id>/state.json
plugins/<plugin-id>/<version>/manifest.json
plugins/<plugin-id>/<version>/package.fbp
plugins/<plugin-id>/<version>/signature.json
plugins/<plugin-id>/<version>/integrity.json
```

- Installation verifies the package SHA-256 and detached signature before extraction.
- A package is admitted to the market root only after a valid **marketplace countersignature**.
- At every process start and every activation, `MarketPluginGate` re-verifies the retained package,
  signature, publisher pin, extracted-tree digest and `MarketplaceReviewed` marker. An unsigned, tampered,
  unreviewed or flat (non-versioned) market package is rejected with `PluginLoadFailureKind.Untrusted`.
- `integrity.json` binds the package hash/size and extracted tree hash; `package.fbp` is the signed artifact.
- The signed marketplace index remains the review and compatibility source; signature verification is not
  a substitute for reviewing the plugin's capabilities.

### Local root (unsigned, user-owned)

The local root is `%APPDATA%\FluidBar\plugins\local` (or `FLUIDBAR_LOCAL_PLUGINS_DIR`) and is deliberately
separate from the market root. Local packages may be unsigned; they are subject to the explicit local
risk acknowledgement and the UI's permission list. They must not be copied into the market root to bypass
signature checks.

## 2. What signatures mean

- **Marketplace countersignature**: the marketplace reviewed and published this artifact. Load-time
  verification requires this role and a trusted marketplace key.
- **Publisher/author signature**: optional pinning of the publisher key recorded at first install. It does
  not replace the marketplace review.
- A hash/signature failure is fail-closed. There is no "continue anyway" path for the market channel.

## 3. Permissions

Manifest capability declarations are **disclosure**, not enforcement. The host shows them in the consent
sheet (market) and the local plugin permission list (local). Examples:

| Capability | Disclosure |
|---|---|
| `ui.pages` | Registers island pages and command canvases. |
| `ui.settings` | Contributes settings pages. |
| `commands` | Registers manifest-declared commands. |
| `network` | May use the plugin's declared network access. |
| `clipboard` | May read/write clipboard through the host seam. |
| `notifications` | May publish host-rendered notifications. |

The exact permission set is the manifest's `capabilities` block; no capability silently grants more than
the host API exposes.

## 4. In-process reality (no sandbox)

An in-process .NET plugin runs in the same process, under the same Windows user, with the same file/network/UI
rights as FluidBar. The collectible AssemblyLoadContext isolates types and lifetimes; it is **not** a
permission boundary. There is no sandbox around plugin code. Signing and review are the trust boundary.

## 5. Local plugin UX

The Settings → Plugins → Local plugins section provides:

- **Load**, **Unload**, **Reload** and **Open folder**.
- **View logs** (opens the host log directory).
- The manifest permission list for each package.
- A one-time risk notice on the first load of each exact `id@version`. Acceptance is persisted in
  `local/risk-acknowledgements.json`; a new version asks again. The notice explains that the package is
  unsigned/unreviewed, runs in-process with the user's rights, and should only be loaded from a trusted
  source. The UI copy deliberately avoids the word "sandbox".

Local packages that are unacknowledged are not auto-loaded on startup. Load is explicit and can be repeated
after fixing a package. Unload removes the page/commands through the host's normal stop path.

## 6. Operational notes

- Local package paths and manifest ids are validated by the same manifest/package inspector as market
  packages (traversal, symlinks and size limits included).
- Local packages are not silently promoted to the market root; keeping the roots separate is part of the
  trust model.
- Failures are reported as plugin load/discovery results and logged; a failed local package never prevents
  other plugins or the host from starting.

