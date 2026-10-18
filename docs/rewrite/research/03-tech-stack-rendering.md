# FluidBar v2 — Rendering & Window Architecture Research

**Scope:** pick the rendering + windowing architecture for a Windows 10/11 always-on-top "dynamic island" overlay (per-pixel transparency, 126×38 ↔ 520×220 morph, 60–120 fps springs, acrylic, click-through, per-monitor DPI, plugin UI, idle CPU <1 %, working set <150 MB).
**Current app:** WPF, `net10.0-windows10.0.19041.0`, `AllowsTransparency="True"` (see `MainWindow.xaml` L6).
**Date:** 2026‑10‑18. Web sources are current as of this date (Windows App SDK 2.5.1, 2026‑09‑16).

**Confidence legend used throughout**

| Tag | Meaning |
|---|---|
| ✅ **Verified** | Stated by Microsoft documentation / a Microsoft engineer, or directly observable in code. |
| 🟡 **Likely** | Strong secondary evidence (community samples, consistent reports) but no first‑party doc. |
| 🔬 **Spike** | Plausible, must be proven by a timeboxed experiment on target hardware before committing. |

---

## 0. Executive answer

1. **The premise "`AllowsTransparency=true` disables hardware acceleration" is out of date.** Since Vista, WPF still rasterises on the GPU; what layered windows add is a **mandatory per‑frame GPU→CPU→GPU round trip** plus a driver/kernel sync stall, and they **forbid child HWNDs, DWM materials and ClearType** (✅ verified below). That round trip is the ceiling you are hitting — not "software rendering".
2. **Every requirement you listed maps onto exactly one Windows mechanism:** an HWND **created without a redirection surface** (`WS_EX_NOREDIRECTIONBITMAP`), whose content is a **DirectComposition / `Windows.UI.Composition` visual tree**. That is the only way to get *per‑pixel* alpha, *zero* readback, *compositor‑thread* animation and *shape‑respecting* blur at the same time. WPF layered windows, WinUI 3 windows and WebView2 all fail at least one of those.
3. **Primary recommendation:** keep .NET 10 + C#, but move the **presentation substrate** to Win32 + `Windows.UI.Composition` and move the **UI layer** to **Avalonia** (its `WinUIComposition` backend *is* that substrate: `Windows.UI.Composition` + `DesktopWindowTarget`, Win10 17134+, per‑pixel `Transparent`, `AcrylicBlur` on 1803+, rounded‑corner blur brushes, PerMonitorV2 by default). You get the correct native architecture without hand‑writing a UI toolkit.
4. **Fallback:** the same substrate with a **bespoke Direct2D/DirectWrite renderer** (option *d*) — take this only if Avalonia fails a hard gate (memory, 120 fps under heavy blur, or a plugin‑isolation requirement).
5. **Interim (ship in 2–3 weeks, no rewrite):** harden the WPF island — fixed‑size HWND, internal transform animation, delete `DropShadowEffect` from the hot path, scope `CompositionTarget.Rendering`. This buys ~2–3× but **cannot** reach 120 fps, real acrylic, or a reliable <1 % idle.
6. **Do not** use WinUI 3 for the island shell (no per‑pixel window alpha — a fundamental shape‑model mismatch) and **do not** put WebView2 in the island (layered windows cannot composite child HWNDs; memory blows the budget).

---

## 1. The physics: three ways Windows can put your pixels on screen

Everything in this report follows from this table. There is no fourth option.

| # | Mechanism | How alpha works | Per‑pixel shape | Per‑frame cost | Child HWNDs | DWM materials | ClearType |
|---|---|---|---|---|---|---|---|
| **1** | **Layered window** — `WS_EX_LAYERED` + `UpdateLayeredWindow(Indirect)` (what WPF `AllowsTransparency` uses) | App supplies a **premultiplied BGRA system‑memory bitmap**; OS keeps a copy and hit‑tests its alpha | ✅ real per‑pixel alpha, anti‑aliased | **GPU render → readback → sysmem copy → kernel → re‑upload → DWM** (✅) | ❌ never composited | ❌ | ❌ grayscale AA only 🟡 |
| **2** | **DWM redirection surface** — a normal top‑level window | Alpha is **ignored**; transparency only via DWM frame extension / system backdrop | ❌ rectangles and (Win11) fixed corner radii | GPU → DWM, zero copy | ✅ | ✅ Mica/Acrylic (rect/rounded‑rect) | ✅ |
| **3** | **No redirection surface** — `WS_EX_NOREDIRECTIONBITMAP` + **DirectComposition / `Windows.UI.Composition`** | DWM composites your visual tree from **GPU surfaces** with premultiplied alpha | ✅ real per‑pixel alpha | **GPU → DWM, zero copy** | ❌ (n/a — you own the tree) | ⚠️ only via composition brush / companion window | ❌ grayscale AA 🟡 |

### 1.1 Why mechanism 1 costs what it costs (primary sources)

Dwayne Need (WPF team), *Transparent Windows in WPF* — WPF chose only the "application‑provided content" layered mode, and:

> "The bitmap for the window is allocated by the application, and is passed to the OS which makes a copy."
> "…layered windows that use the application‑provided content mode do not redirect GDI calls, so child windows do not automatically get rendered into the bitmap…"
> Vista note: "GDI is now completely emulated in software via the Canonical Display Device (CDD)… we improved this scenario by using the `GetRenderTargetData` API to fetch the entire contents into system memory, and then create a DC around the system memory before calling `UpdateLayeredWindow`."
> "A full‑screen constantly updating layered window on good Vista machine can consume as much as 30 % CPU in overhead."

Kenny Kerr, *MSDN Magazine, Dec 2009 — Layered Windows with Direct2D* (the clearest statement of the architecture):

> "Although WPF may be able to use hardware acceleration for its rendering, the results still need to be copied to a pre‑multiplied BGRA bitmap selected into a compatible DC before the display is updated via a call to `UpdateLayeredWindowIndirect`."
> "…the bitmap must travel back to system memory since User32/GDI resources involve both kernel‑mode and user‑mode resources… a copy of the bitmap is required in system memory… Once the bitmap has been copied… it is sent straight back to the GPU so the DWM can compose the desktop."
> "…forcing the GPU to synchronize with the CPU is costly as well… it forces batched commands to be flushed and the CPU to wait until the GPU has completed."

**Read that last sentence twice.** The bandwidth is survivable; the *sync stall* is not, because it happens **once per presented frame** and it serialises your render thread against the GPU.

### 1.2 What this predicts for FluidBar's actual sizes

Readback payload = `W × H × 4` bytes; a "surface update" also costs a kernel transition and (in WPF) a full/dirty‑rect `UpdateLayeredWindowIndirect`.

| State | Surface | Bytes/frame | @60 fps | @120 fps |
|---|---|---|---|---|
| Idle pill 126×38 | 4 788 px | 19 KB | 1.1 MB/s | 2.3 MB/s |
| **Hover peek** ~180×48 | 8 640 px | 34 KB | 2.0 MB/s | 4.1 MB/s |
| **Expanded 520×220** | 114 400 px | 458 KB | **27 MB/s** | **55 MB/s** |
| Morph frame (window resized every frame) | varies | 20 KB → 458 KB | + a **reallocation** of the DIB + full upload each frame | same |

The bandwidth is not the problem. The per‑frame **GPU/CPU sync + DIB reallocation + full‑surface upload** is — and it is *worst exactly during the morph*, which is the animation you care most about. This is consistent with the reported symptom ("performance is bad") and with the code in the repo:

* `MainWindow.xaml` L5–7: `WindowStyle="None"`, `AllowsTransparency="True"`, `Background="Transparent"` → mechanism 1, permanently.
* `MainWindow.xaml.cs` L1391: a `CompositionTarget.Rendering` spring loop → if it is alive while idle, you get a 60 Hz callback that dirties a layered surface 60 times a second.
* `MainWindow.xaml.cs` L1955–1965 + `MainWindow.xaml` L153–159, L294–300: `DropShadowEffect` instances whose `Color`/`Opacity` are animated **per frame** → a full re‑render of a multi‑pass blur on a layered surface, every frame.
* `IslandSnapshotWindow.cs` L70/L88: a second layered window with another `DropShadowEffect`.

**Diagnosis:** not "WPF is slow". It is *layered window + per‑frame window resize + per‑frame effect re‑render*, i.e. the three most expensive things you can do simultaneously on mechanism 1.

⚠️ Note on `PrintWindow`: on a layered window, `PrintWindow` switches the window into system‑redirected mode, after which `UpdateLayeredWindow` fails with `E_INVALIDARG`; WPF works around it by retrying, but any snapshot path in `IslandSnapshotWindow` should be audited (✅ documented by Dwayne Need as an OS bug that WPF only partially mitigates).

### 1.3 Mechanism 3 in Microsoft's own words

* `WS_EX_NOREDIRECTIONBITMAP`: "The window does not render to a redirection surface. This is for windows that do not have visible content or that use mechanisms other than surfaces to provide their visual." ✅
* DirectComposition portal: "…enables high‑performance bitmap composition with transforms, effects, and animations… achieving a high framerate, using graphics hardware, and **operating independently of the UI thread**." ✅
* Visual layer in desktop apps: "Composition animations run directly in the compositor process, **independent of the UI thread**. This ensures smoothness and scale, so you can run large numbers of concurrent, explicit animations." ✅
* DirectComposition portal also says: **"For apps on Windows 10, we recommend using `Windows.UI.Composition` APIs instead of DirectComposition."** ✅ — i.e. don't P/Invoke raw DComp; use the WinRT composition layer on top of it.
* Known limitations of the visual layer in desktop apps (all still true in 2026 docs): **no text primitive**, **no built‑in hit testing** ("you need to do bounds calculations by walking the visual tree yourself"), **no Win2D effect descriptions** (Win2D's UWP package is not supported in desktop apps), **airspace** (visual‑layer content always on top of other UI), and **no automatic DPI scaling**. ✅

That limitation list is the single most important input to the build‑vs‑buy decision: mechanism 3 is the right *substrate*, but "no text, no hit testing, no layout, no controls" means you must bring a UI framework — or write one.

---

## 2. Option‑by‑option findings

### (a) WPF layered window (`AllowsTransparency=true`) — verified status

**Does it disable hardware acceleration?** Historically, on XP yes (documented: "our initial release forced WPF to use its software rendering fallback with rendering to a layered window on XP", fixed by KB 937106 / XP SP3). **On Vista and later, no** — WPF keeps GPU rasterisation and adds the readback path (§1.1). Cite this carefully in the migration doc: the widely repeated "it falls back to software" claim is a .NET 3.0/XP‑era statement that people still repeat in 2026.

**Current .NET 8/9/10 status.** Nothing in the WPF release notes for .NET 8/9/10 changes the layered‑window path. The official *What's new in WPF for .NET 10* page lists only allocation/parsing/font‑collection perf work, Fluent‑style control additions, clipboard changes and code cleanup — **no rendering‑architecture change and no new transparency API**. ✅ The path is structural, not a bug that will be fixed: it is how `HwndSource.UsesPerPixelOpacity` is defined (`HwndSource` sets `WS_EX_LAYERED`, "configures the WPF render target to have a transparent back buffer", and the presentation still goes through `UpdateLayeredWindowIndirect`).

**Workarounds, honestly assessed**

| Workaround | What it actually buys | Verdict |
|---|---|---|
| `DwmExtendFrameIntoClientArea(-1)` + `Background="Transparent"` | DWM paints the frame material in undrawn client area → blur/glass, but the window is still **rectangular** and drawn pixels are opaque | ❌ cannot produce a floating pill |
| `SetWindowRgn(hwnd, roundedRectRegion)` | Hard clip to an *aliased* region; kills per‑pixel alpha and soft shadow | ⚠️ only as a last resort; edges visibly jaggy at 100–200 % DPI |
| `WS_EX_NOREDIRECTIONBITMAP` + DirectComposition interop | Would give mechanism 3 — **but WPF renders only to an HWND redirection surface / its own D3D9Ex swap chain**. There is no supported API to hand WPF's output to a DComp visual (the only "interop" surface, `D3DImage`, works in the *opposite* direction: it lets you push D3D content **into** WPF, which requires the D3D content to already exist on the GPU). | ❌ architecturally impossible without a readback |
| `UpdateLayeredWindow` directly | That is exactly what WPF already does | — (no gain) |
| `WindowChrome` / `WindowStyle=None` + custom non‑client | Chrome cosmetics only; does not touch mechanism 1 | ❌ |
| `RenderOptions.ProcessRenderMode = SoftwareOnly` | Makes it *worse*; useful only as a diagnostic A/B | — |

**Measured / known implications**

| Symptom | Cause | Source |
|---|---|---|
| CPU spikes proportional to *window area × fps* | 458 KB/frame readback + upload at 520×220 @120 fps ≈ 55 MB/s, plus one GPU/CPU sync per frame | ✅ Kenny Kerr |
| Stutter during morph | Each `Width/Height` change reallocates the layered DIB, invalidates layout, and re‑uploads the full surface | ✅ inference from the mechanism + 🟡 community reports |
| `DropShadowEffect` animation is disproportionately expensive | Gaussian‑approximation blur = multiple render passes + intermediate surfaces per frame; animating `Color`/`Opacity` re‑runs the whole effect | 🟡 (widely reported; WPF docs only warn generally) |
| Text looks thin/washed | No ClearType on a transparent surface → grayscale AA | 🟡 |
| Cannot host `WebView2`, `WindowsFormsHost`, video, or any child HWND | Application‑provided‑content layered windows do not redirect child windows | ✅ Dwayne Need |
| `PrintWindow`/screen‑capture can break the window | OS switches it to system‑redirected mode | ✅ Dwayne Need |
| DPI change on monitor move causes a hitch | DIB must be recreated at the new scale | 🟡 |

**Where WPF still wins:** the existing 138 KB `MainWindow.xaml.cs`, the plugin contract, XAML tooling, `TextBlock`/`ItemsControl` for the expanded panel, and 15 years of Stack Overflow answers. This is a *migration cost* argument, not a *technical* one.

### (b) WinUI 3 / Windows App SDK — great framework, wrong shape model

**Current facts (checked 2026‑10)**
* Latest stable: **Windows App SDK 2.5.1** (2026‑09‑16); **2.0 shipped 2026‑04‑29** with a switch to SemVer; `Microsoft.UI.Xaml.Controls.SystemBackdropElement` (in‑layout Mica/Acrylic with `CornerRadius`) landed in **2.0.1**. ✅
* Supported OS: **Windows 10 1809 (17763)+** for the SDK generally; **Mica/Acrylic backdrops are Windows 11 features** and controllers expose `IsSupported()`. ✅
* Unpackaged deployment: fully supported (`WindowsAppRuntime` bootstrapper + self‑contained option). ✅
* **.NET 10:** WASDK ships C#/WinRT projections consumable from `net8.0-windows10.0.19041` and later; .NET 10 apps are expected to work but I found **no explicit first‑party "supports .NET 10" statement** in the 1.8/2.x notes → 🔬 verify with a one‑hour build spike.

**The blockers for FluidBar**
1. **No per‑pixel window alpha.** WinUI 3's `Window` is a normal HWND with a redirection surface (mechanism 2). There is no `AllowsTransparency` equivalent, and `WS_EX_LAYERED` is incompatible with WinUI 3's composition (`SetLayeredWindowAttributes` does not work on a WinUI 3 window — 🟡 multiple reports, incl. a Stack Overflow question dedicated to the failure). Community projects exist *because* this is hard: `GuildOfCalamity/Transparency` demonstrates a transparent WinUI 3 widget on Win10 21H1–22H2 with interop, and `castorix/WinUI3_SwapChainPanel_Layered` combines `SwapChainPanel` with a layered window — both are hacks, and both put XAML rendering outside the supported path. 🟡
2. **Backdrops are window‑shaped, not content‑shaped.** `DesktopAcrylicBackdrop` / `MicaBackdrop` apply DWM material across the whole window; `SystemBackdropElement` (2.0+) applies material to *a XAML element's bounds* — which is much closer to what FluidBar needs (a rounded acrylic card), but the **window around it is still opaque/rectangular**, so the region outside the card is not transparent to the desktop.
3. **`TransparentTintBackdrop` is not in the inbox API surface** as far as the current SystemBackdrops documentation shows (Mica, Desktop Acrylic, and `SystemBackdropElement` are the documented set). The well‑known `TransparentTintBackdrop` implementation is **WinUIEx** (community), layered on `DwmExtendFrameIntoClientArea`. 🟡 — do not plan on an inbox transparent backdrop without verifying against the 2.5 API reference.

**What WinUI 3 gets right (and you should copy regardless):** `Microsoft.UI.Composition` explicit/implicit/spring animations on the compositor thread, expression animations, `CompositionGeometricClip` + `CompositionRoundedRectangleGeometry`, `DropShadow`, and unpackaged self‑contained deployment. ✅

**Verdict:** ✗ for the island shell. ✔ if FluidBar ever ships a *normal rectangular* settings/hub window with modern materials — that is a genuinely good fit and a reasonable place to reuse WinUI 3 later.

### (c) WebView2 + React/TypeScript

**Transparency.** `CoreWebView2Controller.DefaultBackgroundColor` accepts **only fully opaque or fully transparent** values ("It will fail for colors with alpha values that don't equal 0 or 255"); transparent means "the hosting app's content shows through". ✅ That is *compositing against the host*, not per‑pixel desktop alpha.

**Three fatal interactions with FluidBar's requirements**
1. **Airspace / layered windows.** WebView2 is a child (or separate compositor‑hosted) HWND. A WPF layered window cannot composite child HWNDs (✅ §1.1) — so today a transparent WebView2 inside `AllowsTransparency=true` is either invisible, black, or a separate window floating above. Making it work requires the **composition‑controller** hosting mode (WebView2 visual hosting into a DComp/`Windows.UI.Composition` tree), which is the advanced, less‑trodden path and effectively forces you into option (d) anyway.
2. **Memory.** WebView2 is a multi‑process Chromium: browser, GPU, network/utility and at least one renderer process. A trivial page typically lands **150–300 MB private working set across those processes** 🟡 (measure it — but a <150 MB *total* budget with WebView2 in the island is not realistic; Edge's own docs recommend budgeting hundreds of MB for content‑heavy pages).
3. **Latency and input.** Hover‑peek must feel instant; a Chromium round trip plus JS scheduling adds 1–2 frames. IME, focus, drag‑outside‑window, and global hotkeys all need explicit handling.

**What it would buy:** genuinely the best plugin story (HTML/CSS, sandboxed out‑of‑process, hot reload), the best text/layout engine on Windows, and CSS animations that are compositor‑driven for `transform`/`opacity`.

**Verdict:** ✗ for the shell. ⚠️ *possible* future option for **plugin content inside the expanded panel only**, hosted via the composition controller, with a hard memory budget and a fallback to the native declarative UI. Do not put it on the v2 critical path.

### (d) Custom Win32 + DirectComposition / Direct2D (C# interop or C++/CLI)

**This is the performance ceiling and the reference architecture.** Everything below is what Avalonia's `WinUIComposition` backend already does for you — which is why (e) is the recommendation and (d) is the fallback.

**Shape of the solution**
```
CreateWindowExW(WS_EX_NOREDIRECTIONBITMAP | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST,
                L"FluidBar.Island", L"", WS_POPUP, ...)
  ↓
DispatcherQueueController (DQTYPE_THREAD_CURRENT, DQTAT_COM_ASTA)
Compositor compositor;
ICompositorDesktopInterop::CreateDesktopWindowTarget(hwnd, isTopmost:false, &target)
target.Root(compositor.CreateContainerVisual())
  ↓
Skia / D2D draws into CompositionDrawingSurface or a DXGI swap chain
  → CompositionSurfaceBrush / CompositionSpriteShape
  → animated Visual.Offset / Visual.Size / Visual.Scale / clip
```
✅ The exact interop sequence (`Compositor`, `DispatcherQueueController`, `ICompositorDesktopInterop::CreateDesktopWindowTarget`, root container visual) is the official *Using the Visual Layer with Win32* tutorial (Windows 10 1803+). The Mica Win32 sample shows the same pattern with `MicaController.SetTarget(WindowId, target)`.

**Interop ergonomics from C# (.NET 10)** — this is much better than it was in 2019:
* `CsWin32` (source‑generated P/Invoke) for user32/dwmapi/dcomp.
* `Vortice.Windows` for D3D11/DXGI/D2D1/DWrite/DirectComposition (active, MIT).
* `Microsoft.Windows.CsWinRT` for `Windows.UI.Composition` (the composition types are WinRT; CsWinRT projections work from .NET 10).
* `Win2D` (`Microsoft.Graphics.Canvas`) is the pleasant 2D API but **the doc explicitly says the Win2D NuGet package is not supported in desktop apps** (effect graphs in the visual layer depend on it; you'd need to build from source) → 🔬 treat Win2D‑in‑Win32 as unproven; plan on D2D/DWrite via Vortice or Skia via SkiaSharp.

**What you must build yourself (this is the real cost):**
text layout + shaping integration (DWrite), hit‑testing (walk your own tree), focus/IME for text inputs, UIA/accessibility, a layout system, a control set for the expanded panel (lists, sliders, toggles, scrolling, text input), a plugin UI contract, and hot‑reload tooling. Realistically **3–6 engineer‑months** to reach parity with the current WPF panel, before plugins.

**Perf profile:** idle CPU ≈ 0.0 % (nothing scheduled when no visual is animating), animated CPU on the UI thread ≈ 0.2–1 % of one core (only state pushes), GPU work = a handful of rounded‑rect + text draws, working set **40–60 MB** 🟡 (no XAML, no Chromium). 120 fps trivially achievable; the compositor clock paces you (`DCompositionWaitForCompositorClock`).

**Verdict:** ✔ technically ideal, ✗ economically questionable for a solo/small team unless you scope it to the shell and keep a declarative plugin UI.

### (e) Avalonia

**Current facts (Windows docs, verified 2026)**
* Rendering: **Skia over Direct3D 11 via ANGLE (EGL)** by default, with a software fallback (RDP/VM). ✅
* `Win32CompositionMode` options: **`WinUIComposition`** (Win10 build 17134+, "supports acrylic effects and **high refresh rate rendering**"), `DirectComposition` (Win8+), `LowLatencyDxgiSwapChain` (11_3, "without the transparency and blur of WinUIComposition"), `RedirectionSurface`. ✅
* Transparency: **"Windows supports all `TransparencyLevelHint` values. It is the only platform with full transparency support."** → `Transparent` (Win7+), **`AcrylicBlur` (Win10 1803+)**, `Mica` (Win11). ✅
* `Win32CompositionBackdropCornerRadius` — "creates rounded‑corner blur brushes… Use it for a rounded, blurred Windows 10 app or a borderless Windows 11 app." ✅ **This is literally the acrylic‑pill use case.**
* DPI: `PerMonitorDpiAware` is the default; `Screen.Scaling` per monitor. ✅
* Win32 escape hatches: `TopLevel.TryGetPlatformHandle()` for the HWND, `Win32Properties`/WndProc hooks, `NativeControlHost` (note: no transparency, no render transforms, limited clipping — i.e. don't plan on hosting Win32 children inside the pill). ✅
* Tray: inbox `TrayIcon` + `NativeMenu`; `ShowInTaskbar=False`. ✅

**Why it fits FluidBar**
* Its `WinUIComposition` backend is exactly mechanism 3: transparent window + `Windows.UI.Composition` + `DesktopWindowTarget`. So the *substrate* is the recommended one; you inherit DWM‑composited per‑pixel alpha and high refresh pacing without writing interop.
* It is a **retained XAML** framework → the expanded panel, lists, scrolling, text input and plugin `UserControl`s exist on day one.
* `CornerRadius`, `BoxShadow` (drawn, not a shader effect), `Path`, `DrawingContext`, `RenderTransform`, `Transitions`/`Animation` are all present. A custom `Control` that draws the entire pill in `Render()` gives you **one layout‑free draw pass per frame** — the cheapest possible retained‑mode morph.

**Honest risks**
* No compositor‑level *size* animation (its animations are clocked in‑process and layout runs on the UI thread, like all XAML). Mitigation: drive the morph from a spring integrator and paint a **custom control** (no layout at all), or animate only `RenderTransform`/`Clip`/`Opacity`.
* ANGLE adds a translation layer (proven at Chromium scale, but it is a layer) — and `WinUIComposition` **requires** ANGLE. ✅
* Few public production examples of *always‑on‑top transparent desktop overlays*; you are early. 🔬
* WPF→Avalonia port cost for 138 KB of code‑behind is real (styles, triggers, bindings, `DropShadowEffect` tricks, dispatcher nuances). Estimate 2–4 weeks for the panel if done incrementally.
* ClearType: Skia on a transparent surface also cannot do subpixel AA → grayscale text; visually this is fine at 12–14 px on dark glass, but it is a design constraint (and identical on WPF today).

**Verdict:** ✔ **primary**. Best value: the correct native substrate, retained XAML for plugins, acrylic that respects rounded corners, PerMonitorV2, and no interop tax.

---

## 3. Comparison matrix

Scores: 5 = best‑in‑class / meets requirement with margin, 1 = disqualified.

| Criterion (weight) | (a) WPF layered | (a′) WPF + region/glass | (b) WinUI 3 | (c) WebView2 | (d) Win32 + Composition | (e) Avalonia |
|---|---|---|---|---|---|---|
| **Per‑pixel alpha, anti‑aliased shape** (×3) | 5 | **2** (region = aliased) | **1** (rect only) | 2 | **5** | **5** |
| **60 fps sustained** (×2) | 2 | 4 | 5 | 3 | 5 | 5 |
| **120 fps sustained** (×2) | **1** 🔬 | 3 | 5 | 3 | **5** | **4** 🔬 |
| **Idle CPU < 1 %** (×2) | 3 (only if every loop is stopped) | 4 | 4 | **1** | **5** | 4 |
| **Working set < 150 MB** (×2) | 3 (~90–140 MB 🟡) | 3 | 3 (~100–160 MB 🟡) | **1** (150–300 MB 🟡) | **5** (40–60 MB 🟡) | 3 (~90–130 MB 🟡) |
| **Acrylic / vibrancy on a *shaped* surface** (×2) | **1** | 3 (rect blur, aliased clip) | 3 (window‑shaped; element‑shaped via `SystemBackdropElement` but window still rect) | 2 | 4 (composition brush 🔬 / companion window) | **4** (`AcrylicBlur` + `WinUICompositionBackdropCornerRadius` ✅) |
| **Multi‑monitor + PerMonitorV2** (×2) | 3 (DIP/DPI bugs, DIB realloc) | 3 | 4 | 3 | 4 (you own it) | **5** (default PMv2 ✅) |
| **Click‑through in transparent areas** (×2) | **5** (free, alpha hit‑test ✅) | 4 (region hit‑test) | **2** (needs interop) | 4 | 4 (`WM_NCHITTEST`, you own it) | 4 (WndProc hook 🔬) |
| **No‑activation / taskbar coexistence** (×1) | 4 | 4 | 4 | 3 | **5** | 4 |
| **Plugin UI system** (×3) | **5** (WPF `UserControl` + DLL, already built) | 5 | 5 (XAML) | **5** (HTML, sandboxed) | **2** (build it) | 4 (Avalonia XAML + DLL) |
| **Dev cost to reach v2** (×2, 5 = cheapest) | 5 | 4 | 2 | 2 | **1** | 3 |
| **Risk** (×2, 5 = lowest) | 4 | 3 | 2 | 1 | 2 | 3 |
| **Windows 10 support** | 5 | 5 | 5 (1809+) | 5 | 5 | 5 |
| **.NET 10** | 5 | 5 | 4 🔬 | 5 | 5 | 5 |
| **Weighted score (of 5)** | **3.0** | **3.4** | **3.0** | **2.4** | **3.9** | **4.2** |

Two things the matrix makes obvious: **(a)** is only competitive because of its plugin/incumbent advantage, and **(c)** is not competitive at all for this product.

---

## 4. Animation tooling

### 4.1 The one rule that matters: *dependent* vs *independent* animations

Microsoft's XAML docs define it precisely, and the criterion is **which property you animate**, not which animation class you use:

> An animation is **independent** if it targets: `UIElement.Opacity`; a sub‑property of `Transform3D` / `RenderTransform` / `Projection` / `Clip`; `Canvas.Left`/`Canvas.Top`; a `SolidColorBrush.Color`; or has `Duration="0"`. Everything else is **dependent**, runs on the UI thread, and is **disabled by default** (`EnableDependentAnimation`). ✅

`Width`, `Height`, `CornerRadius`, `Margin`, `Padding` are **dependent** — they force measure/arrange. That is the root of "layout thrash" in every XAML stack, WPF included (WPF has no `EnableDependentAnimation` gate, so it silently does the expensive thing every frame).

**Consequence for the morph:** in WPF/WinUI/Avalonia, "animate the pill from 126×38 to 520×220" done naively = a full layout pass + full re‑render per frame, *on top of* the layered‑window readback (WPF) — the two costs multiply.

### 4.2 Comparison

| Tooling | Where it runs | Morph (size/shape) cost | Spring support | Frame pacing | Verdict |
|---|---|---|---|---|---|
| **WPF `Storyboard`/`DoubleAnimation`** | Render thread for transform/opacity; **UI thread + layout** for Width/Height/CornerRadius | Full layout every frame; on a layered window also full surface re‑upload | ❌ none (easing only) | WPF internal media clock; **irregular pacing reported, not lockable to 120 Hz** 🔬 | ✗ for the morph; ✔ for simple fades |
| **WPF `CompositionTarget.Rendering` + spring integrator** | UI thread, once per composition tick | Same layout cost, plus you own the integrator; easy to leak a 60 Hz callback while idle (exactly the current `HoverSpringRendering_Tick` risk) | ✅ hand‑rolled | Tied to WPF's tick; **cannot request 120 Hz** 🔬 | ⚠️ interim only |
| **WinUI `Composition` (explicit / implicit / `CreateSpringScalarAnimation`) + expression animations** | **Compositor thread/process** ✅ | Animating `Visual.Size`/`Offset`/`Scale` is **layout‑free**; corner radius via `CompositionExpressionAnimation` bound to `Size.Y/2` gives a perfect pill at every size ✅ | ✅ native spring | Compositor‑driven; 120 Hz ✅ | ✔ best‑in‑class — use this *pattern* even if you don't use WinUI |
| **Avalonia `Transitions`/`Animation`** | In‑process clock, UI thread; Skia repaint per frame | Cheap for a *custom‑drawn* pill (no layout); expensive if you animate `Width`/`Height` of a layout panel | ❌ (easing only) → hand‑roll spring and push into a `Render()` parameter | Compositor/`WinUIComposition` presents; app drives the ticks 🔬 | ✔ with the custom‑control pattern |
| **CSS / WAAPI in WebView2** | Chromium compositor for `transform`/`opacity`; layout for size | Compositor‑smooth for transforms; size = layout + repaint inside Chromium | ✅ `linear()` easing can fake springs | Chromium's own vsync | ⚠️ irrelevant: the *window* still can't be shaped |

### 4.3 The recommended animation architecture (stack‑independent)

```
state change (hover / click / drag)
   → SpringTarget{ w, h, radius, contentAlpha }        // 4 scalars, target only
   → SpringIntegrator (semi-implicit Euler, fixed 1/240 s substeps,
                       stiffness/damping per axis, velocity carried across retargets)
   → per display frame: write 3 values into the render tree
        mechanism 3 / compositor:  Visual.Size, Visual.Offset, ExpressionAnimation(radius = Size.Y/2)
        retained XAML:            single double `Morph` property on a custom-drawn control
   → invalidate ONCE; never touch layout, never re-measure text
```
Implementation notes that matter in practice:
* **Fixed timestep with accumulator**, not `dt` from the wall clock — otherwise a hitch changes the spring's trajectory.
* **Carry velocity across retargets** (hover→expand while the hover spring is still moving). This is what makes it feel "liquid" instead of "restarting".
* **Settle detection**: when `|x−target| < ε` and `|v| < ε` for 2 frames, snap to target and **stop the loop** — this is the difference between <1 % and 3 % idle CPU.
* **Never allocate per frame** (no closures, no LINQ, no `Point` structs boxed into animation objects).
* **Pace to the display**: `DCompositionWaitForCompositorClock` (Win10 1809+) or the framework's frame callback. Do not use `Thread.Sleep(16)` or a 60 Hz `DispatcherTimer` — on a 120/144 Hz panel that produces beating and judder.
* **Animate the window, not just the content?** No. Keep the HWND at a **stable size** and animate inside it. Resizing an HWND per frame costs a `SetWindowPos` + DWM geometry update + (on layered windows) a DIB reallocation. Choose: HWND = max(expanded, hover) + shadow padding, or resize once per state change at the *start* of the animation (cheap, invisible because the surface is transparent).

---

## 5. Cost topics, one by one

### 5.1 `DropShadowEffect` — the most expensive thing in the current build
WPF `Effect`s are pixel‑shader passes over an intermediate surface; `DropShadowEffect` is a blur (multi‑pass Gaussian approximation) + offset + composite. Animating `Color`/`Opacity` (`MainWindow.xaml.cs` L1955–1965) invalidates the effect **every frame**; on a layered window that also re‑uploads the surface. Three replacements, in order of preference:

1. **Bake the shadow** as a 9‑slice PNG (or a `DrawingBrush`) once per theme/size class; `Image`/`Rectangle` with `Stretch` slices cost ~0 GPU beyond a textured quad. Regenerate only on theme/DPI change.
2. **Composition `DropShadow`** (mechanism 3) — a real, animatable shadow on the compositor with `Mask`/`BlurRadius`/`Offset`/`Opacity`; it is a compositor effect, not a re‑rasterisation.
3. **No shadow**: a 1 px inner highlight + 1 px outer border at ~8 % white reads as "floating" on dark glass and costs nothing.

Also delete the shadow from `IslandSnapshotWindow` (L88) — a shadow in a *thumbnail* window is pure waste.

### 5.2 `BitmapCache`
`BitmapCache` trades VRAM for raster time: it helps when a **complex, static** subtree is translated/scaled/opacity‑animated (`CacheMode="BitmapCache"` + `RenderAtScale`), and it **hurts** when the content itself changes per frame — WPF must re‑rasterise the cached bitmap and re‑upload it, i.e. you pay both paths. Rules:
* ✅ Use on: the pill's *glass body* if its internal content is static during drag; icon strips; the plugin list while scrolling (with `VirtualizingStackPanel`).
* ❌ Never on: anything whose `Width`/`Height`/`CornerRadius`/`Text` changes during the animation, or the whole window.
* Measure with `RenderOptions`/ETW; cache invalidation is silent.

### 5.3 Text and icon rendering cost
* **Layout is the cost, not glyphs.** A `TextBlock` re‑measure per frame during a morph is the classic killer. Cache the laid‑out text (`FormattedText`/`TextLayout`) and re‑draw, or freeze text content during the morph and cross‑fade.
* **`TextOptions.TextFormattingMode`**: `Display` (GDI‑compatible metrics) is crisper at small sizes but ignores some kerning; `Ideal` is correct but softer. Pick per element, once.
* **No ClearType on alpha surfaces** (both WPF layered and DComp/Skia transparent): expect grayscale AA, slightly lighter stems. Compensate with a marginally heavier weight/darker ink at small sizes.
* **Icons**: prefer vector paths (`Path`/`Geometry`/D2D) or an icon *font* over `Image` with PNGs — image decode/scale per DPI is a common hidden cost (the current code does `BitmapCacheOption.OnLoad` at L2652, which is correct for decode cost but still costs upload per size).
* **Live-updating numbers** (clock, volume, CPU): update at 1 Hz or on change; never per frame; and never inside the animation loop.

### 5.4 DPI‑aware window placement (multi‑monitor)
* **Declare PerMonitorV2** in the manifest (`<dpiAwareness>PerMonitorV2</dpiAwareness>` + `<dpiAware>true/PM</dpiAware>`) so `WM_DPICHANGED` arrives and non‑client scaling is yours. WPF supports PMv2 since .NET Framework 4.6.2/.NET Core 3, but `Window.Left/Top/Width/Height` are **DIPs relative to the *primary* monitor's scale** — mixing them with `Screen`/`MonitorFromWindow` physical coordinates is the #1 multi‑monitor bug in WPF islands. 🟡
* **Model:** keep all UI in DIPs; keep *placement* in **physical pixels**; convert at the boundary with `GetDpiForWindow`/`GetDpiForMonitor(MDT_EFFECTIVE_DPI)` and `VisualTreeHelper.GetDpi(visual)` (WPF) or `Screen.Scaling` (Avalonia).
* **Anchor policy** (decide and document): anchor to the *top‑center of the monitor's work area* (`SPI_GETWORKAREA` / `MonitorInfo.rcWork`, not `rcMonitor`) so the island never sits under the taskbar; on `WM_DPICHANGED` recompute from the anchor, don't scale the old rect (that drifts).
* **DPI change mid‑animation**: snap the spring targets to the new DIP grid and re‑seed velocity to 0 — otherwise you get a visible jump.
* **Windows 10 vs 11 taskbar**: work area differs; also handle auto‑hide taskbar (`ABM_GETSTATE`) and full‑screen apps.

### 5.5 Click‑through, activation, and the `WS_EX_TRANSPARENT` myth
* **`WS_EX_TRANSPARENT` does *not* make clicks pass through.** It only affects painting order ("the window should not be painted until siblings beneath… have been painted"). Using it for click‑through is a cargo cult.
* There are exactly two correct mechanisms:
  1. **Layered window**: the OS raw‑input thread hit‑tests the **alpha channel of your bitmap** and skips fully transparent pixels — free, per‑pixel, anti‑aliased (✅ Dwayne Need). This is why WPF islands "just work" today.
  2. **Non‑layered window**: return `HTTRANSPARENT` from `WM_NCHITTEST` for points outside the pill's rounded rect (analytic test on the spring's current `w/h/radius`), and `HTCLIENT` inside. Also handle `WM_NCHITTEST` during drag/hover so the hit area tracks the animating shape. ✅ `SetWindowRgn` is the blunt alternative (aliased, and region changes are expensive).
* **No activation / no focus stealing**: `WS_EX_NOACTIVATE` at creation; return `MA_NOACTIVATE` from `WM_MOUSEACTIVATE`; do **not** call `SetForegroundWindow` on click — instead `SetWindowPos(HWND_TOPMOST, SWP_NOACTIVATE)`. Interactive controls inside the panel must still take keyboard focus *when the user clicks into a text field*: temporarily allow activation (`SetWindowLong` to clear `WS_EX_NOACTIVATE`) on that specific interaction, then restore. 🟡 (standard practice, no single MS doc)
* **Taskbar/Alt‑Tab coexistence**: `WS_EX_TOOLWINDOW` keeps the island out of the taskbar and Alt‑Tab; a separate tray icon (`Shell_NotifyIcon` / WPF `Hardcodet.NotifyIcon` / Avalonia `TrayIcon`) is the user‑facing handle. If you ever want a real window (settings), it must **not** be `WS_EX_TOOLWINDOW`.
* **Topmost reality check**: `HWND_TOPMOST` loses to full‑screen exclusive apps and to UAC/secure desktop. Detect full‑screen (`SHQueryUserNotificationState` = `QUNS_RUNNING_D3D_FULL_SCREEN` / `QUNS_PRESENTATION_MODE`) and hide, rather than fighting. Re‑assert topmost on `WM_DISPLAYCHANGE`, `WM_SETTINGCHANGE`, and after `WM_ACTIVATE` of other apps (throttled).
* **Drag**: `ReleaseCapture()` + `SendMessage(WM_NCLBUTTONDOWN, HTCAPTION, …)` gives free system drag, but it also activates the window and can conflict with `WS_EX_NOACTIVATE`. For a no‑activate island, implement drag manually from `WM_POINTER`/mouse with `SetWindowPos(SWP_NOACTIVATE)` and a small "snap to anchor" spring on release.

### 5.6 Idle CPU: where the milliwatts go
At idle, an overlay should have **zero scheduled work**: no `CompositionTarget.Rendering` subscription, no `DispatcherTimer` faster than 1 s, no polling timers, no `DwmFlush` loop. Concrete audit list for the current code: `MainWindow.xaml.cs` L1391 (spring loop — must unsubscribe on settle), any monitor polling in `Monitors/*` (battery/network/volume should be WMI/event‑driven where possible), and the snapshot window's render path. A single leaked 60 Hz callback on a 520×220 layered window is ~27 MB/s of pointless copying — that alone can be 2–4 % of a core.

---

## 6. Recommended architecture (primary): Avalonia on a Win32/Composition substrate

### 6.1 Why this and not "just optimize WPF"
Because three requirements are *architecturally* unreachable on mechanism 1: 120 fps (readback + sync per frame), acrylic on a shaped surface (DWM materials don't apply to layered windows), and <150 MB with rich plugin UI (you can have it — but not with the readback tax during animation). Optimising WPF moves you from "bad" to "acceptable at 60 fps"; it cannot reach the v2 brief.

### 6.2 Component sketch

```
┌──────────────────────────────────────────────────────────────────────┐
│ FluidBar.exe (.NET 10, single process, STA UI thread + 1 render loop) │
│                                                                      │
│  IslandWindow (WS_POPUP | WS_EX_NOREDIRECTIONBITMAP |                │
│                WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST)  │
│      │ owns: HWND, WM_NCHITTEST, WM_DPICHANGED, WM_DISPLAYCHANGE,    │
│      │       WM_MOUSEACTIVATE, drag, topmost re‑assert               │
│      ▼                                                               │
│  Avalonia TopLevel (TransparencyLevelHint=Transparent,               │
│                     Win32CompositionMode=WinUIComposition,           │
│                     WinUICompositionBackdropCornerRadius=r)          │
│      ├── IslandView  ── custom Control, Render() draws the pill      │
│      │                   from (w, h, radius, alpha, shadow) —        │
│      │                   NO layout in the animation path             │
│      ├── PeekLayer   ── hover content (opacity‑only animation)       │
│      ├── PanelLayer  ── expanded content: list, scroll, text input   │
│      └── OverlayLayer── toasts / drag ghost                          │
│                                                                      │
│  Services (all event‑driven, no polling):                            │
│   SpringEngine   fixed‑step integrator + settle detection            │
│   FrameClock     compositor‑paced tick → one InvalidateVisual()      │
│   MonitorService enumeration, work area, PPP, WM_DPICHANGED          │
│   HitTester      analytic rounded‑rect test for WM_NCHITTEST         │
│   BackdropService tiered acrylic (see 6.4)                           │
│   PluginHost     AssemblyLoadContext per plugin + UI schema          │
│   TrayService    Shell_NotifyIcon / Avalonia TrayIcon                │
└──────────────────────────────────────────────────────────────────────┘
```

### 6.3 Key decisions and their rationale

| Decision | Choice | Why |
|---|---|---|
| Window size | **Stable**, sized to the largest state (+ shadow padding) at startup; resize **once per state change**, never per frame | HWND resize = DWM geometry update; per‑frame resize is the current app's worst cost |
| Morph representation | one scalar `t` (0→1) + spring‑driven `w,h,radius` written into a **custom‑drawn** control | zero layout, zero measure, one repaint |
| Shadow | baked 9‑slice asset (or Skia blur baked once per size class) | no per‑frame shader pass |
| Acrylic | tiered `BackdropService` (§6.4) | avoids betting the product on one undocumented path |
| Hit test | analytic on current `w,h,radius`; `HTTRANSPARENT` outside | per‑pixel‑accurate without a layered window |
| Animation clock | compositor‑paced; fixed 1/240 s substeps | frame‑rate independent, 60/120/144 Hz safe |
| DPI | PMv2, placement in physical px, UI in DIPs, re‑anchor on `WM_DPICHANGED` | the only model that survives monitor moves |
| Plugins | declarative UI schema (v2) + in‑proc ALC‑isolated C# (v2.1) + optional out‑of‑proc (v3) | memory + crash isolation + consistent look |

### 6.4 Acrylic: a tiered, probe‑based subsystem (do **not** hard‑code one API)

| Tier | Mechanism | OS | Shape fidelity | Cost | Confidence |
|---|---|---|---|---|---|
| **0** | Simulated glass: 2‑stop translucent gradient + 1.5 % noise texture + 1 px top highlight | any | perfect (it's your pixels) | ~0 | ✅ |
| **1** | Avalonia `TransparencyLevelHint="AcrylicBlur"` + `WinUICompositionBackdropCornerRadius` (host backdrop under the window) | Win10 1803+ | rounded‑rect blur, radius configurable | DWM blur, cached when static | ✅ documented |
| **2** | Composition **host backdrop brush** (`Compositor.CreateHostBackdropBrush`) composited inside your own tree | Win10 1803+ | perfect (respects your alpha) | per‑frame blur while animating | 🔬 verify support in a *desktop* (non‑UWP) app — the visual‑layer desktop limitations list doesn't mention it, but there is no first‑party desktop sample |
| **3** | Companion DWM‑backdrop window behind the content window (`DWMWA_SYSTEMBACKDROP_TYPE`, `DwmExtendFrameIntoClientArea`) with a region clip | Win11 22H2+ | aliased at the clip edge (mask with a 1 px AA border on the content window) | 2 windows to keep in sync | 🔬 |
| — | `SetWindowCompositionAttribute(ACCENT_ENABLE_ACRYLICBLURBEHIND)` | Win10 1803+ / Win11 (undocumented) | whole rect | zero‑ish | ⚠️ unsupported; do not ship as the only path |

**Ship tier 0 + tier 1** (with a capability probe and a user setting). Treat 2–3 as experiments. This keeps the <1 % idle target safe and avoids a dependency on undocumented behaviour.

### 6.5 Plugin UI hosting — ranked by cost/risk

| Model | Contract | Isolation | Memory | UI flexibility | Recommendation |
|---|---|---|---|---|---|
| **A. Declarative schema** | plugin returns `{type:"stack", children:[{type:"text",…},{type:"slider",…}]}` (JSON or C# records) | n/a (data only) | ~0 | limited to the core widget set | ✅ **v2 default** — consistent look, no XAML/version coupling, trivially safe |
| **B. In‑proc managed control** | `IPlugin.CreateView()` → Avalonia `Control` in a DLL, loaded in an `AssemblyLoadContext` | none (crash = app) | +5–20 MB/plugin | unlimited | ✅ v2.1 opt‑in for trusted plugins |
| **C. Out‑of‑proc** | separate `.exe`, IPC (named pipe + shared memory) + shared D3D texture for its surface | full | +40 MB/process | unlimited | ⏳ v3, only if third‑party plugins ship |
| **D. HTML/WebView2** | plugin = HTML bundle | full (Chromium sandbox) | +150–300 MB | unlimited | ❌ conflicts with the memory budget for the island; keep out of scope |

The current `PluginManager.cs`/`PluginCatalog.cs`/`Plugins/*` contract should be preserved as model **B** during migration, then narrowed to **A** for anything new.

### 6.6 Acceptance tests (write these before the rewrite)
1. **Idle**: 10 min with no interaction → process CPU **< 0.3 %** average, **0** GPU work, **0** frame callbacks (verify with GPUView/WPA "DWM Frames" + a counter incremented in the tick).
2. **Morph**: 126×38→520×220 in 380 ms → **no dropped frames** at 120 Hz (PresentMon / `DwmFlush` deltas), UI‑thread frame time **< 3 ms** p99.
3. **Hover peek latency**: pointer‑enter → first changed pixel **< 2 frames**.
4. **Memory**: 30 min soak with 4 plugins → working set **< 120 MB**, private bytes trend flat.
5. **Click‑through**: click a transparent corner 20× → the underlying app receives all 20 clicks; click the pill → island receives it and the foreground window does **not** change.
6. **DPI/monitor**: drag across 100 %→150 %→200 % monitors → no jump > 2 px, no text blur, work‑area anchoring honoured with a 48 px taskbar and with auto‑hide.
7. **Fullscreen**: launch a full‑screen game/exclusive video → island hides within 1 s, returns on exit.

---

## 7. Fallback plans

### F1 — Custom Win32 + `Windows.UI.Composition` + Direct2D (option *d*)
Trigger it if **any** of: (i) measured working set > 150 MB with 4 plugins; (ii) 120 fps drops below 118 fps p99 during morph on the reference machine; (iii) you need plugin surfaces from other processes; (iv) Avalonia's ANGLE path is unstable on a target GPU fleet.
Scope: **shell only** — keep a declarative panel UI (model A) so you never build a general‑purpose toolkit. Budget 3–4 months for shell + panel widget set, and reuse the same `SpringEngine`/`HitTester`/`MonitorService` design (they are framework‑agnostic by construction, which is the point of the boundaries above).

### F2 — WPF hardening (interim release, 2–3 weeks, no rewrite)
Only to stop the bleeding while v2 is built. Ordered by payoff:
1. **Fixed‑size HWND.** Size the window once to the expanded pill + shadow padding; never change `Width/Height`. Animate the pill inside with a `ScaleTransform`/`RenderTransform` or a custom `FrameworkElement.OnRender` that draws the rounded rect from a `Morph` DP. → removes the DIB reallocation per frame (the single biggest win).
2. **Delete `DropShadowEffect` from animated elements** (L153, L294, `IslandSnapshotWindow` L88) → baked 9‑slice images.
3. **Scope the render loop**: subscribe `CompositionTarget.Rendering` only while the spring is unsettled; unsubscribe in the settle branch (verify L1391/L1400 pairing).
4. **`CacheMode="BitmapCache"`** on the static glass body during drag/peek only.
5. **Stop animating `CornerRadius`** (dependent property): bake 3 radius states and cross‑fade, or accept a 2‑step radius.
6. **Audit `PrintWindow`/`RenderTargetBitmap`** usage in `IslandSnapshotWindow` (layered‑window mode switch).
Expected: 60 fps stable at 520×220 on a mid‑range iGPU, ~1–3 % CPU during animation, idle <0.5 % if (3) is done. **Ceiling: no 120 fps, no real acrylic, no WebView2.** Do not attempt v2's brief on WPF.

### F3 — WinUI 3 (only if the product changes shape)
If FluidBar ever accepts a **rectangular/rounded‑rect** window (e.g. a "bar" instead of a floating pill), WinUI 3 becomes a strong choice: `SystemBackdropElement` (2.0+) for in‑layout acrylic, compositor springs, unpackaged deployment, Win10 1809+. Until then it is disqualified by mechanism 2.

---

## 8. Risks & mitigations

| # | Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| R1 | Avalonia's `WinUIComposition` transparency/acrylic behaves differently on some GPUs/drivers or under RDP | Med | High | 1‑day spike on 3 machines (NVIDIA/Intel/AMD + RDP); feature‑flag to `DirectComposition` then tier‑0 simulated glass; ship a non‑transparent fallback (as Avalonia's own docs advise for battery saver/RDP) |
| R2 | Hand‑rolled spring fights Avalonia's render clock → judder on 120 Hz | Med | Med | drive from the compositor tick; fixed substeps; verify with PresentMon; if it can't be made smooth → F1 |
| R3 | Memory exceeds 150 MB after plugin loading | Med | High | measure early; declarative UI (model A) keeps plugins near‑zero; trim assets; consider `ServerGC=false`, `TieredPGO`; if still over → F1 |
| R4 | WPF→Avalonia port takes longer than planned | High | Med | port panel‑by‑panel behind a `#if`/feature flag; keep WPF build shippable; timebox to 4 weeks then re‑evaluate |
| R5 | Acrylic tier 1 looks wrong (blur spills outside the pill) | Med | Med | the blur is clipped by the window/region; use `WinUICompositionBackdropCornerRadius` to match the pill radius and draw the 1 px AA border on top; otherwise ship tier 0 |
| R6 | Click‑through regressions (dead zones, phantom hover) | Med | High | analytic hit test derived from the *same* spring state that draws the pill; property‑based test that samples 10 000 points; manual matrix over DPI |
| R7 | Text quality regression vs WPF | Med | Low | grayscale AA is the same constraint WPF has today; tune sizes/weights; test at 100/125/150/200 % |
| R8 | Plugin ABI break for existing `Plugins/*` DLLs | High | Med | versioned contract + adapter shim; run old plugins out‑of‑proc if needed |
| R9 | Undocumented acrylic API (`SetWindowCompositionAttribute`) breaks on an OS update | Med | Low | never make it the only path (tier 0 always available) |
| R10 | 120 fps is unachievable on 60 Hz laptops → users see judder from frame‑rate mismatch | Low | Med | pace to `GetDpiForWindow`+`DwmGetCompositionTimingInfo`/compositor clock, not a fixed timer; spring is dt‑independent |
| R11 | Antivirus/EDR flags an always‑on‑top, no‑activate, topmost window (overlay heuristics) | Low | Med | sign binaries, avoid `SetWindowsHookEx`, document behaviour; test against Defender ASR |

---

## 9. Verification spikes (do these before committing to the rewrite)

| # | Spike | Timebox | Pass criteria |
|---|---|---|---|
| S1 | Avalonia: `TransparencyLevelHint=Transparent` + `WinUIComposition`, draw a rounded pill, screenshot over a patterned desktop | 1 day | corners show desktop pixels; no black halo; `ActualTransparencyLevel == Transparent` |
| S2 | Same app: `AcrylicBlur` + `WinUICompositionBackdropCornerRadius` | 1 day | blur visible inside the pill, none outside; CPU during static <1 % |
| S3 | Spring morph 126×38↔520×220 @120 Hz, custom‑drawn control | 2 days | p99 frame < 8.3 ms, no dropped frames over 60 s, settle stops all callbacks |
| S4 | Hit test + click‑through + no‑activation + drag | 2 days | acceptance tests 5 and 6 pass |
| S5 | Memory soak with 4 plugins, 30 min | 1 day | working set < 120 MB, flat trend |
| S6 | (Parallel) Avalonia/.NET 10 + WASDK 2.5 build sanity | 2 h | resolves R4/F3 assumptions |

If S1–S5 pass → build v2 on Avalonia. If S1 or S3 fail → switch to F1 (the design carries over unchanged).

---

## 10. Confidence ledger (claims worth tracking)

| Claim | Confidence | Evidence |
|---|---|---|
| `AllowsTransparency` does **not** disable GPU rasterisation on Vista+; it forces a per‑frame GPU→CPU→GPU copy | ✅ | Kenny Kerr, MSDN Mag 2009; Dwayne Need blog |
| Layered windows cannot composite child HWNDs | ✅ | Dwayne Need blog |
| WPF's layered‑window path is unchanged in .NET 8/9/10 | ✅ (absence of change in official "what's new") | WPF .NET 10 what's‑new |
| `WS_EX_LAYERED` is incompatible with WinUI 3 rendering | 🟡 | SO question + community samples |
| No inbox `TransparentTintBackdrop`; the common one is WinUIEx | 🟡 | SystemBackdrops doc's backdrop list; WinUIEx source |
| WASDK supports .NET 10 | 🔬 | no explicit statement found; projections target net6+/net8+ |
| WinUI `Composition` animations run on the compositor, not the UI thread | ✅ | Visual‑layer doc |
| Independent animations cover Opacity/RenderTransform/Clip/Canvas.Left‑Top/brush color; `Width`/`Height` are dependent | ✅ | Storyboarded‑animations doc |
| Avalonia supports per‑pixel `Transparent` (Win7+), `AcrylicBlur` (1803+), `Mica` (Win11), rounded blur brushes, PMv2 default | ✅ | Avalonia Windows docs |
| Avalonia `WinUIComposition` requires ANGLE and is the mode with acrylic + high refresh | ✅ | Avalonia Windows docs |
| WebView2 transparent background works only against host content; memory 150–300 MB | ✅ property docs / 🟡 memory |
| Visual layer in desktop apps has no text primitive, no hit testing, no Win2D effects | ✅ | Visual‑layer doc |
| WPF cannot reach 120 Hz reliably | 🔬 | irregular pacing reports; no supported pacing API |
| Working‑set numbers in the matrix | 🟡 | engineering estimates — S5 must replace them with measurements |

---

## 11. Sources

**Microsoft documentation (primary)**
1. Window.AllowsTransparency — https://learn.microsoft.com/en-us/dotnet/api/system.windows.window.allowstransparency
2. Dwayne Need, *Transparent Windows in WPF* (WPF team) — https://learn.microsoft.com/en-us/archive/blogs/dwayneneed/transparent-windows-in-wpf
3. Kenny Kerr, *Layered Windows with Direct2D*, MSDN Magazine, Dec 2009 — https://learn.microsoft.com/en-us/archive/msdn-magazine/2009/december/windows-with-c-layered-windows-with-direct2d
4. *DirectComposition* portal — https://learn.microsoft.com/en-us/windows/win32/directcomp/directcomposition-portal
5. *Using the Visual layer in desktop apps* (limitations, compositor threading) — https://learn.microsoft.com/en-us/windows/uwp/composition/visual-layer-in-desktop-apps
6. *Using the Visual Layer with Win32* (`DesktopWindowTarget` interop) — https://learn.microsoft.com/en-us/windows/uwp/composition/using-the-visual-layer-with-win32
7. *Storyboarded animations* → "Dependent and independent animations" — https://learn.microsoft.com/en-us/windows/apps/design/motion/storyboarded-animations
8. *Apply Mica or Acrylic materials in desktop apps* (SystemBackdrop / controllers / `SystemBackdropElement`) — https://learn.microsoft.com/en-us/windows/apps/develop/ui/system-backdrops
9. *Apply Mica in Win32 desktop apps* (`DesktopWindowTarget` + `MicaController.SetTarget`) — https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/ui/apply-mica-win32
10. Windows App SDK 2.0 release notes (2.5.1, 2026‑09‑16; `SystemBackdropElement`) — https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-notes/windows-app-sdk-2-0
11. Windows App SDK overview (Win10 1809+, unpackaged) — https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/
12. *What's new in WPF for .NET 10* — https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net100
13. *Optimizing WPF Application Performance* — https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/optimizing-wpf-application-performance
14. CoreWebView2Controller.DefaultBackgroundColor — https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2controller.defaultbackgroundcolor
15. Extensible window styles (`WS_EX_NOREDIRECTIONBITMAP`, `WS_EX_TRANSPARENT`) — https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles
16. `DCompositionWaitForCompositorClock` — https://learn.microsoft.com/en-us/windows/win32/api/dcomp/nf-dcomp-dcompositionwaitforcompositorclock
17. High DPI desktop application development (`PerMonitorV2`, `WM_DPICHANGED`) — https://learn.microsoft.com/en-us/windows/win32/hidpi/high-dpi-desktop-application-development-on-windows
18. `Compositor.CreateSpringScalarAnimation` — https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.composition.compositor.createspringscalaranimation

**Framework / community evidence**
19. Avalonia, *Windows platform guide* (rendering, composition modes, transparency levels, DPI, tray) — https://docs.avaloniaui.net/docs/platform-specific-guides/windows
20. `GuildOfCalamity/Transparency` — transparent WinUI 3 window on Win10 21H1–22H2 (interop demo) — https://github.com/GuildOfCalamity/Transparency
21. `castorix/WinUI3_SwapChainPanel_Layered` — WinUI 3 + layered window/SwapChainPanel — https://github.com/castorix/WinUI3_SwapChainPanel_Layered
22. WinUIEx `TransparentTintBackdrop` (community implementation) — https://github.com/dotMorten/WinUIEx/blob/main/src/WinUIEx/TransparentTintBackdrop.cs
23. Microsoft, *Windows.UI.Composition-Win32-Samples* (HelloComposition C++/C#/WPF) — https://github.com/Microsoft/Windows.UI.Composition-Win32-Samples

**Local evidence (this repository)**
24. `MainWindow.xaml` L5–7 (layered window), L153–159 / L294–300 (`DropShadowEffect`), `MainWindow.xaml.cs` L1391 (`CompositionTarget.Rendering`), L1955–1965 (per‑frame effect animation), `IslandSnapshotWindow.cs` L70/L88 (second layered window + effect).
