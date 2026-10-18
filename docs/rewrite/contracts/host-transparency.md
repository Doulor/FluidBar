# Host transparency — root-cause investigation (M6 hotfix, `task-11`)

Status: **investigation complete; verdict points to an environment/display-mirror artifact, not an
App rendering defect. Human confirmation on the physical monitor is still required before closing.**
Owner: platform-owner. Files touched: none in `src/FluidBar.App/Hosting`; `Program.cs` was used for
diagnostics and has been restored to the UI owner's WIP version (App builds 0 errors). New tools:
`build/verify-transparency.ps1` + `tools/perf/layered-probe/`.

---

## 1. TL;DR

The grey rectangle is **not reproducible as an app-level rendering defect**:

1. A **canonical Win32 `UpdateLayeredWindow`** probe (32bpp premultiplied DIB, red ellipse, transparent
   background) captured through the exact same `CopyFromScreen` path is **transparent in 5/5 runs**:
   desktop pixels are visible through its corners. So the capture path itself preserves per-pixel alpha
   for GDI-layered surfaces.
2. Avalonia 12.1.4 windows — the standalone **spike**, the **same spike code compiled inside the App
   assembly**, the App's `FLUIDBAR_SPIKE_WINDOW=1` probe and the real App host — all flatten to a uniform
   **`#D3D3D3`** rectangle over the full window bounds, **in the same captured frame** where the layered
   probe is transparent. Window state is reported correctly (`ActualTransparencyLevel=Transparent`,
   `TransparencyLevelHint=[Transparent]`, `Background=Transparent`, fallback default `White`,
   `RenderScaling=1.5`).
3. The machine has **two virtual display adapters running** (`GameViewer Virtual Display Adapter`,
   `ToDesk Virtual Display Adapter`) plus AnyDesk/GameViewer processes. Avalonia's
   `WinUIComposition`/`DirectComposition` surfaces are flattened to opaque `#D3D3D3` in this
   display/capture path; GDI layered windows are not. Early captures showed the spike transparent and
   later captures grey with **no code change**, i.e. the condition is environment-state dependent.

**Conclusion:** the most probable root cause is the remote-desktop / virtual-display mirroring stack
flattening DirectComposition alpha into an opaque grey surface — this is a *capture/mirror* artifact
(and possibly what remote viewers see), **not** an Avalonia/app configuration bug. Before changing the
host architecture we need one human observation on the physical monitor (see §7).

## 2. What ui-owner had already ruled out (re-verified here)

Composition modes `default`/`winui`/`direct`/`redirection`; with/without FluentTheme; with/without
`ApplyIslandStyles`/`NOREDIRECTIONBITMAP`; empty content; removing `TargetPlatformMinVersion`;
removing the pre-show `Opacity=0` trick. My A/B adds:

| Variable | Result |
|---|---|
| Composition mode (all four values, current binary) | grey in all |
| `base.OnFrameworkInitializationCompleted()` on/off in probe path | grey both |
| Probe window pre-show `Position`, `ShutdownMode`, probe visual vs spike visual | grey |
| Exact spike `Application`/`Window`/`Control` code compiled **inside** `FluidBar.App` | grey in later runs (transparent in one earlier run — intermittent) |
| Same spike code in its own project (positive control) | transparent in some captures, grey in others (intermittent) |
| `TransparencyLevelHint`, `Background`, `TransparencyBackgroundFallback` | values correct while grey |
| DWM/ex-styles: window has `WS_EX_NOREDIRECTIONBITMAP \| WS_EX_TOPMOST` before/after probe (+`TOOLWINDOW/NOACTIVATE` only when App interop applies them) | identical to transparent spike capture |
| Loaded renderer modules | identical (`av_libGLESv2` ANGLE, `d3d11`, `dcomp`, `dxgi`, `d2d1`, `Avalonia.Skia`, `libSkiaSharp`) |

## 3. Canonical Win32 layered control (the discriminator)

`tools/perf/layered-probe/` is a self-contained net10.0-windows probe that creates a
`WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST` popup and calls the canonical
`UpdateLayeredWindow` with a 32bpp premultiplied DIB (fully transparent background + opaque red ellipse).

Five consecutive runs with the layered probe **and** the Avalonia spike on screen simultaneously
(capture via `System.Drawing.Graphics.CopyFromScreen`, 2560×1600 physical):

| run | layered probe corner (155,155) | layered ellipse (350,300) | Avalonia spike grey samples (row y=100, x=900..1500) |
|---|---|---|---|
| 1–5 | desktop pixels (`transparent`) | red `(255,0,0)` | **146** each run (`#D3D3D3`) |

Screenshots: `build/reports/hotfix-intermittent-1..5.png` (not committed; regenerate with
`build/verify-transparency.ps1`).

`build/verify-transparency.ps1` (the committed evidence tool) reproduces this deterministically:
`layered-transparent=True ellipse-red=True | fluidbar-transparent=False grey-samples=36` (two samples,
report `build/reports/transparency-verify-*-*.json`), exit code 1 because the Avalonia probe captures grey. This proves the capture path handles per-pixel alpha correctly for
layered windows, and that the grey is specific to Avalonia's composited surface in this environment.

Additional comparisons (same session): `build/reports/hotfix-human-check-current.png` — canonical layed
probe transparent at `(155,155)`, real App host grey (`#D3D3D3`) at `(921,10)`/`(1050,20)` with desktop
visible at `(912,2)`.

## 4. Environment facts (at investigation time)

| Fact | Value |
|---|---|
| OS | Windows 10.0.26200 (11 24H2-class) |
| GPU | NVIDIA GeForce RTX 5060 Laptop GPU, driver 32.0.15.9649 |
| Other adapters | AMD Radeon 610M; **GameViewer Virtual Display Adapter** (15.6.5.199); **ToDesk Virtual Display Adapter** (16.44.2.509) — all `Status=OK` |
| Remote tools running | `GameViewer`, `GameViewerServer/Service/Healthd`, `ToDesk`, `AnyDesk` |
| DWM | running; `EnableTransparency=1` |
| Display | 2560×1600 @ 150 % scaling |
| Avalonia | 12.1.4; renderer modules ANGLE/EGL + dcomp/d3d11 (identical between App and spike) |

## 5. Verdict and its confidence

**Verdict:** `ISOLATED` — canonical layered windows are transparent in captures while Avalonia
(`WinUIComposition`/DirectComposition) windows flatten to opaque `#D3D3D3`, intermittently and without
any app-code difference; the flattening correlates with the active virtual-display/remote-mirror stack.

Confidence: **medium-high for "not an app-level configuration bug"**, **medium for "physically
innocuous"** — the remaining uncertainty is whether the physical panel shows the same grey (which would
mean per-pixel alpha for DComp surfaces is genuinely broken for this session/GPU) or whether only the
remote view/capture does. That single fact decides whether any host strategy change is needed.

**Do not rewrite the host on current evidence.** The design/spike S1 corner evidence is invalid: a
`#D3D3D3` "desktop" sample is the flattening/mirror colour, not the desktop; the desktop behind the
window is `#F9FAFB` (page background), as the transparent layered probe shows.

## 6. What would make this a real bug (and then what to do)

If human eyes confirm a grey rectangle on the physical monitor while remote tools are disconnected:

1. Re-run `build/verify-transparency.ps1` on the physical session (it exits 1 when FluidBar captures grey).
2. Probe **Avalonia 11.3.x** with the same minimal window (possible Avalonia 12 Win32 composition
   regression).
3. Force alternate renderers: `Win32PlatformOptions.RenderingMode = [AngleEgl, Software]` with
   `WinUIComposition`; then `RedirectionSurface` + layered fallback.
4. Only then choose a host strategy: Win32 `UpdateLayeredWindow` host (known-good in this environment),
   Avalonia 11.3 host, forced `AngleEgl`, or documented "remote-desktop: disable transparency" fallback.
   All options are documented for a decision; none implemented yet.

## 7. Open action (needs a human, one minute)

Run from a **physical** session (or with ToDesk/GameViewer/AnyDesk disconnected):

```powershell
pwsh build/verify-transparency.ps1
```

while looking at the screen: the canonical probe is a **red ellipse** with a transparent surround at
~ (150,150) physical px; the FluidBar probe is the **dark pill** whose window has transparent padding.
Question to answer: **do you physically see a light-grey rectangle around either window?**

- **No** → capture/mirror artifact confirmed; close this task, keep the host architecture, add the
  manual-QA/capture caveat to the spike report (S1 evidence invalid), and optionally document
  "remote-desktop transparency may flatten in mirrors" for users.
- **Yes** → treat as real; execute §6 and pick a host strategy.

## 8. Files / hand-back

- `src/FluidBar.App/Hosting/**`, `AppBootstrap.cs`: **not modified**.
- `src/FluidBar.App/Program.cs`: used for diagnostic A/B (spike code compiled into App, verbose tracing,
  base-call switch); **restored to the UI owner's WIP version** (`git diff` = only the pre-existing WIP
  change; App builds with 0 errors). Handing ownership back to ui-owner.
- `build/verify-transparency.ps1` + `tools/perf/layered-probe/`: keep as the evidence/regression tool.
- Evidence screenshots: `build/reports/hotfix-*.png` (regenerate with the script; build outputs are not
  committed).

## 9. Related note (not transparency)

While collecting the app perf baseline, the published App exited during a 3-minute idle sample with
`0xE0434352` (unhandled .NET exception) after ~30 s. That is a separate UI-WIP stability item (the
App shell currently renders design data); flagged to Lead/UI owner — do not conflate it with the
transparency question.
