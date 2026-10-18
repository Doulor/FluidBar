# FluidBar 2.0 — Reference & Visual Design Research

**Document:** `01-references-design.md`
**Prepared for:** FluidBar rewrite lead (WPF/.NET 10 → target stack TBD)
**Date:** 2026-10-18
**Scope:** (1) deep-dive of two named reference repos, (2) Apple's authoritative public design facts for Live Activities / Dynamic Island, (3) analysis of 6 additional open-source island implementations, (4) design reference report: feature matrix, visual spec, motion spec, interaction patterns, plugin/marketplace patterns, license warnings, recommended tokens.

**Constraint compliance:** no file inside `D:\build\GitLocal\FluidBar` was created, modified, deleted or moved (verified `git status` before and after; only the two pre-existing untracked entries `.superpowers/` and `docs/tmpFD28.png` are present). The GUI app was never launched. All external repos were cloned with `--depth 1` under `%TEMP%\fluidbar-ref\`. FluidBar source was read read-only, purely to calibrate recommendations against current values.

**Reading conventions**
- `[VERIFIED]` = read directly in source or in Apple's own published data during this research.
- `[INFERRED]` = deduced from evidence but not stated by the source (reasoning given).
- `[UNCERTAIN]` = cannot be confirmed from authoritative sources; do not ship as fact.
- All pixel values from Windows projects are **DIPs at 96 DPI** unless the source says otherwise. Apple's values are **points (pt)**, which are 1:1 with DIP at 100% scaling.

---

## 0. Executive summary — what to take from where

| Need | Best donor | Why |
|---|---|---|
| Sizing/state model for a Windows island | **devcode90/Dynamic-Island-for-Windows** (MIT) | 13 activity kinds each with a concrete width×height, plus a measured (not guessed) idle-strip sizer. |
| Windows platform plumbing (click-through, z-order, per-monitor, DPI, acrylic) | **devcode90/…** (MIT) + **WinIsland** (GPL, read-only) | Both hit the same real problems; the Windhawk mod documents its reasoning inline (23.6k lines, very heavily commented). |
| Motion system | **dynamic-island-mchack** (MIT) | A closed-form spring with a full `(period, damping-ratio)` preset table per semantic slot, plus velocity→deformation geometry. Copyable. |
| Motion numbers to sanity-check against | **Atoll** and **boring.notch** (GPL, read-only) | Shipped SwiftUI springs with explicit `response`/`dampingFraction`. |
| Plugin + marketplace architecture | **WinIsland** (GPL, read-only) | ABI v2 DLL plugins, capability-gated service tables, validated draw lists, signed ZIP packages, marketplace catalog. |
| Content rules / restraint / "what belongs in a pill" | **Apple HIG** | Only authoritative source; now publishes exact sizes. |
| Typography & iconography | **Apple HIG** + Windhawk mod's DWrite table | Weights, sizes, icon-font fallback ladder. |

**Three headline conclusions**

1. **The "most important" reference is a spec, not a codebase.** `devcode90/Dynamic-Island-for-Windows` is a Windhawk mod injected into `windhawk.exe` — 23,669 lines of C++23 in one file. Nothing in it can be linked into a WPF app. What transfers is its *design constants* and its *hard-won Windows behaviours*, which are documented well enough to be re-implemented.
2. **FluidBar is currently oversized relative to every reference.** FluidBar: `MinWidth 126`, `MinHeight 38`, corner radii 24/28, expanded `MaxWidth 640`. Apple's expanded envelope tops out at 408×160 pt; WinIsland expands to 360×200; the Windhawk mod expands to 380×184. A 640-wide island is 1.6–1.8× the width of every shipping reference.
3. **The best architectural reference is GPL-3.0 — and FluidBar is MIT.** WinIsland, boring.notch and Atoll are all GPL-3.0, which is one-way compatible with MIT: their *patterns* are fair game (ideas are not copyrightable), their *code, assets and string tables* are not, short of relicensing all of FluidBar to GPL. So the two MIT repos are the only copy-safe donors — the Windhawk mod for Windows behaviour and constants, mchack for motion maths.

---

## 1. Method & source ledger

**Repos cloned** (`git clone --depth 1`, under `%TEMP%\fluidbar-ref\`):

| Local dir | Upstream | Commit inspected |
|---|---|---|
| `Dynamic-Island-for-Windows` | https://github.com/devcode90/Dynamic-Island-for-Windows | `e6cfedb` |
| `dynamic-island-mchack` | https://github.com/ng1145/dynamic-island-mchack | `7fd875a` |
| `Atoll` | https://github.com/Ebullioscopic/Atoll | `9d24aa2` |
| `boring.notch` | https://github.com/TheBoredTeam/boring.notch | `64b5459` |
| `WinIsland` | https://github.com/WinIslandProject/WinIsland | `ebf6760` |

**Read but not cloned:** `Lakr233/NotchDrop` (MIT), `appsfolder/livebridge` (GPL-3.0), `JNTMTMTM/eIsland` (Electron), plus the commercial apps NotchNook and Dynamic Lake (documentation only). Repo metrics obtained from the GitHub REST API on 2026-10-18.

**Apple sources** (all fetched 2026-10-18):

- HIG → Live Activities: https://developer.apple.com/design/human-interface-guidelines/live-activities (machine-readable: `https://developer.apple.com/tutorials/data/design/human-interface-guidelines/live-activities.json`). Page's own change log: **October 13, 2026 — "Added guidance for limited width layouts, and updated supported platforms and specifications."** The size table below is from that revision.
- HIG → SF Symbols: https://developer.apple.com/design/human-interface-guidelines/sf-symbols
- Apple Design Resources: https://developer.apple.com/design/resources/
- SF Symbols download/licence entry point: https://developer.apple.com/sf-symbols/
- WWDC sessions linked *by the HIG page itself*: `WWDC26-223` "Live Activities essentials", `WWDC25-278` "What's new in widgets", `WWDC25-216` "Turbocharge your app for CarPlay".
- WidgetKit / SwiftUI API surface: `DynamicIslandMode`, `ContainerRelativeShape`, `EnvironmentValues.isDynamicIslandLimitedInWidth`, `WidgetKit/Animating-data-updates-in-widgets-and-live-activities`.

> Note on WWDC numbering: I deliberately **do not** cite a WWDC22 "Dynamic Island design" session number. A search surfaced plausible but unverified numbers; Apple never published a session dedicated solely to Dynamic Island visual design, and the numbers I could not confirm are omitted rather than guessed.

---

## 2. Deep-dive A — `devcode90/Dynamic-Island-for-Windows` (the highlighted reference)

### 2.1 Identity and repo facts `[VERIFIED, GitHub API 2026-10-18]`

| Field | Value |
|---|---|
| Full name | `devcode90/Dynamic-Island-for-Windows` |
| Description | "A fluid, highly responsive desktop pill overlay inspired by iPhone's Dynamic Island…" (submitted as a Windhawk **mod**) |
| Language | C++ (C++23) |
| Stars / forks / watchers | **40 / 21 / 40** |
| Open issues | 22 |
| Size (API) | 8,526 KB (~8.3 MB) |
| Created | 2026-05-20 |
| Last push | 2026-10-13 |
| Default branch | `main` |
| Licence | MIT, "Copyright (c) 2026 devcode90" |
| Mod version | 1.3.1 (`// @version` in the header) |
| Files in tree | 16 (1 source file + README + CHANGELOG 37.8 KB + 13 preview PNGs) |

**The single most important structural fact:** the entire product is one file,
`dynamic-island-for-windows.wh.cpp` — **1,100,199 bytes, 23,669 lines**. Its Windhawk header declares:

```
// @include         windhawk.exe
// @compilerOptions -lole32 -loleaut32 -lshcore -ld2d1 -ldwrite -ldwmapi -lgdi32
//                  -luser32 -lshell32 -lruntimeobject -lwindowscodecs -lavrt
//                  -lsetupapi -lwinhttp -lpdh -lwinmm
// @license         MIT
```

`@include windhawk.exe` means Windhawk loads this DLL **inside the Windhawk host process**, not into Explorer (the README's older text about adding `explorer.exe` to the process inclusion list is stale relative to the mod's own README block, which says "the island runs in its own process"). This is a normal Win32 process with its own window — which is why the windowing techniques below are portable in *concept* to any Win32/WPF host, even though the code is not portable at all.

### 2.2 Tech stack `[VERIFIED]`

- **Language:** C++23, single translation unit, no build system in the repo (Windhawk compiles it).
- **Rendering:** Direct2D 1.x + DirectWrite for all text; ID2D1HwndRenderTarget-style offscreen target composited through `UpdateLayeredWindowIndirect` (per-pixel alpha). Software fallback paths exist for Gaussian blur.
- **Presentation:** `WS_EX_LAYERED` window + 32-bpp DIB + `UpdateLayeredWindowIndirect` each frame — i.e. classic layered-window blitting, not DirectComposition.
- **Backdrop:** `DwmEnableBlurBehindWindow`-class blur / acrylic behind the window (`BackdropMaterial: none|blur|acrylic`, "Requires Windows 10 1803 or newer"), composed *under* the layered pixels.
- **Media:** WinRT `GlobalSystemMediaTransportControlsSessionManager`; **audio spectrum** via WASAPI loopback + FFT (4 visualiser styles).
- **Notifications:** WinRT `UserNotificationListener` (needs OS permission).
- **Bluetooth:** WinRT `Windows.Devices.Enumeration` + `Bluetooth.GenericAttributeProfile` (gated by `__has_include`, lines 631–640).
- **System data:** PDH (`-lpdh`) for CPU/GPU/RAM/disk, `-lsetupapi` for device events, `-lwinhttp` for weather (wttr.in) and lyrics (LRCLIB), `-lwinmm` for `timeBeginPeriod`.
- **Icons:** Segoe Fluent Icons with fallback to Segoe MDL2 Assets (probed at runtime via `FindFamilyName`).

### 2.3 Architecture `[VERIFIED]`

**Threads**

1. **Render/UI thread** — owns the window, the message loop, all springs, all drawing. A `while (WaitForSingleObject(g_stopEvent, 0) == WAIT_TIMEOUT)` loop with `PeekMessageW` + a hand-rolled frame scheduler (`nextFrameTarget`, `WaitUntil`-style wait), so the island is not driven by `WM_PAINT`.
2. **Settings thread** (Windhawk-owned) — `LoadSettings()` builds a whole new `Settings` struct and swaps it under `g_settingsMutex`.
3. **Weather thread**, **media thread**, **audio-capture thread** — publish into `SharedState` under `g_stateMutex`.

**State model**

- `Settings` — every user option, value type, replaced wholesale on change, guarded by a **strict leaf mutex** (`g_settingsMutex`; the comment at line 1640 states it must never acquire another lock). Readers take `GetSettingsCopy()` once per frame so a mid-frame settings change cannot tear one frame across two configurations. This is a good pattern and directly portable.
- `SharedState` — the "what is happening on the system now" snapshot (media, clipboard, volume, battery, privacy dots, notifications, timer, file tray, lookup…), published by worker threads.
- `Activity` — the *derived* presentation request: `{ IslandKind kind; float width, height; }`, produced every frame by `ActivityForKind()` from state+settings. **This is the key architectural idea**: the view is a pure function of (state, settings) → `Activity`, and a pair of springs then chases that width/height.

**Frame pipeline** (render loop, simplified from lines 22390–23020)

```
1. poll message queue (incl. WM_APP_NEW_EVENT → nudgeSpring kick)
2. choose primary + secondary activity   (ChooseActivities → priority list)
3. targetWidth/Height = activity size(s) (+12 DIP gap when two coexist)
4. widthSpring.target = targetWidth; heightSpring.target = targetHeight
5. dt = clamp(now - previousFrame, 1ms, 50ms)
6. pick stiffness/damping from animation style + direction (expand vs collapse)
7. widthSpring.Step(dt*speed,…); heightSpring.Step(…); nudgeSpring.Step(…)
8. SetWindowRgn(rounded region)  +  SetWindowPos(TOPMOST/NOTOPMOST, x, y, w, h)
9. render offscreen D2D → UpdateLayeredWindowIndirect
10. schedule next frame; release 1 ms timer resolution 0.5 s after motion stops
```

**Priority model for competing activities** (`ChooseActivities`, line 20577): Lookup → Clipboard → CapsLock → Device → Bluetooth → DND → Volume → Brightness → Notification → BatteryLow → Progress → Timer → Media. Two can be shown at once (`primary` + `secondary` side by side, 12 DIP gap). Idle is the fallback. This ordering is a *product decision* worth copying nearly verbatim; it encodes "short-lived, user-initiated" above "ambient", and "media" last.

### 2.4 Feature list `[VERIFIED, README + settings schema]`

**Modules:** Media player (art, title/artist/album, scrubbable progress, transport, source dock for multi-source switching, 4 audio-spectrum styles, 4 progress-bar styles, optional cover-spin on track change); Calendar (month grid, locale-aware first weekday); Weather (wttr.in, auto-IP or fixed city, °F/°C); Hardware monitor (CPU/GPU/RAM/disk/network); Game overlay (FPS/CPU/GPU/RAM/disk, compact taskbar strip); Idle (clock + battery + page dots); Lyrics (LRCLIB, karaoke wipe + letter glow, Bluetooth latency offset, side panel); File Tray (drag files onto island, drag back out, persisted paths only); Clipboard (text + 160 px image thumbs); Notifications; Volume & Brightness flyouts; Bluetooth connect/disconnect with BLE battery; DND indicator; Caps/Num Lock; Battery low; USB/device events; Privacy dots (mic orange `#FF9500`, camera green `#34C759`); Focus Timer (Pomodoro, right-click); Quick Lookup (hotkey → Wiktionary/Wikipedia/dictionaryapi.dev); Hover clock.

**Customisation:** 6 positions + per-state Y offsets + border-merged (flush) mode; 3 shape styles (Apple Pill / Windows 11 rounded box / macOS Notch flush); size scale 0.8–2.5×; auto-DPI scaling; text scale 70–160 % independent of island scale; 8 themes + custom hex incl. `#RRGGBBAA`; tint intensity; depth shading; drop shadow; accent bloom 0–200 %; accent from album art / system / custom; backdrop blur or acrylic + fill opacity + tint; contour border default/auto/borderless; font family override; 12 languages; 12/24 h; seconds; custom date pattern (`yyyy年MM月dd日` supported); refresh rate Auto/60/90/120/144/165/240/360/500; 4 physics presets; 6 speed multipliers; per-module toggles; 2 customisable hotkeys.

### 2.5 Window / transparency / always-on-top / click-through / multi-monitor / DPI `[VERIFIED]`

**Window creation** (line 22324):

```cpp
WS_EX_LAYERED | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT,
kWindowClass, L"Dynamic Island for Windows", WS_POPUP, 0, 0, 520, 140
```

- `WS_EX_LAYERED` — per-pixel alpha via `UpdateLayeredWindowIndirect` (line 11635–11645). Note the comment at 3744: alpha and pixels must be set in the **same** `UpdateLayeredWindow` call to avoid a flash.
- `WS_EX_TOOLWINDOW` — keeps it out of Alt-Tab and the taskbar.
- `WS_EX_NOACTIVATE` — never steals focus. It is **removed temporarily** while the Quick Lookup search box is open (line 10086–10109: `ex & ~WS_EX_NOACTIVATE` / restore), because a text field needs keyboard focus. This toggle-on-demand trick is the clean solution to the "island must be clickable but must not steal focus" problem.
- `WS_EX_TRANSPARENT` (click-through) is **dynamic**, not static (line 10499 `SetClickThrough`): applied with `SetWindowPos(..., SWP_FRAMECHANGED)` only when the hit region is empty. Policy at line 23001:

  ```cpp
  SetClickThrough(hwnd, (primary.kind == IslandKind::Idle && !draggingOrHover && !pinned) || ctrlHoverCT);
  ```

  i.e. an idle pill is click-through unless hovered, dragged or pinned — so a 96 px pill parked at the top of the screen never eats a click meant for the app underneath, but still expands on hover.

- **Shaped hit region, not a rectangle:** `SetWindowRgn(CreateRoundRectRgn(..., radius*2, radius*2), TRUE)` (line 3812) shapes both the visible window and the hit area to the pill's rounded outline, so clicks in the transparent margins are not captured. A `kNudgeSlack = 10` px pad is added because the nudge spring moves the pill vertically outside the region computation.
- **Always-on-top is re-asserted, not set once:** a periodic check re-applies `HWND_TOPMOST` if something demoted it (line 3870, 3915) — but it explicitly *refuses* to fight shell surfaces: `IsShellOwnedWindow()` whitelists `Windows.UI.Core.CoreWindow`, `Xaml_WindowedPopupClass`, `Shell_TrayWnd`, `Shell_SecondaryTrayWnd`, `TopLevelWindowForOverflowXamlIsland`, `MultitaskingViewFrame`, … "Raising ourselves over the Start menu, the notification centre, the volume OSD, Task View, a popup menu or a tooltip would be a bug, not a fix" (line 3822). **This is the single best paragraph in the repo** and the exact trap most Windows island apps fall into.
- **Multi-monitor:** `Appearance.TargetMonitor` = `primary | 1 | 2 | 3 | 4 | 5 | follow`, with `follow` = the monitor containing the cursor; graceful fallback to primary when the chosen display is absent. Plus `OffsetX/OffsetY` and a *separate* expanded-state Y offset that is eased, not snapped (`The island eases between this and Offset Y as it expands and collapses, so the two never snap`).
- **DPI:** handled by **uniform layout scaling**, not per-monitor DPI awareness: `GetDpiForMonitor(MDT_EFFECTIVE_DPI)` from `shcore.dll` on the primary monitor, then `sizeScale *= dpi/96.0` (line 2680–2772), applied in `ActivityForKind` (`activity.width *= settings.sizeScale`). `[INFERRED]` Because the host is `windhawk.exe` and the window is created with no explicit DPI-awareness context, the mod cannot truly render at 200 % on one monitor and 100 % on another; the `follow`-monitor mode plus a global scale factor is an approximation, and the code does not re-query DPI when the island moves displays. **Do not copy this**; FluidBar (a first-class WPF app) should declare Per-Monitor V2 and recompute layout on `WM_DPICHANGED`.
- **Idle cost:** the render thread fully parks when hidden (`std::atomic` flags + stop event) — "CPU usage drops to ~0 % while hidden". 1 ms timer resolution is requested only while animating and released 0.5 s after motion stops (`kHighResTimerHoldSec = 0.5`), with balanced `timeBeginPeriod`/`timeEndPeriod` reference counting. **Copy this pattern.**

### 2.6 Animation approach — with exact constants `[VERIFIED]`

**Spring integrator** (line 1603):

```cpp
struct SpringValue { float value, velocity, target;
  void Step(float totalDt, float stiffness, float damping) {
    const float kFixedDt = 0.0005f;            // 0.5 ms fixed substep
    while (totalDt > 0) { dt = min(totalDt, kFixedDt);
      displacement = value - target;
      acceleration = -stiffness*displacement - damping*velocity;
      velocity += acceleration*dt; value += velocity*dt; totalDt -= dt; }
    if (|value-target| < 0.01 && |velocity| < 0.01) { value = target; velocity = 0; }
  } };
```

Note the **fixed 0.5 ms substep**: at 360 Hz (2.78 ms/frame) that is ~6 substeps per frame. This is the cheapest way to make a semi-implicit Euler spring frame-rate independent without a closed-form solution.

**Spring parameters** (line 22930–22962) — **asymmetric by direction**, which is the core "feel" trick:

| Parameter | Expand | Collapse | Neutral |
|---|---|---|---|
| stiffness | **380** | **200** | 280 |
| damping | **26** | **28** | 24 |

Style multipliers:

| Style | stiffness × | damping × | Intent (source comment) |
|---|---|---|---|
| Smooth | 1.00 | **1.35** | critically damped, no bounce |
| Default | 1.00 | 1.00 | "balanced Apple-like spring" |
| Bouncy | 1.10 | **0.70** | underdamped, elastic |
| Snappy | **1.50** | 1.25 | high stiffness, quick settle |

Speed multipliers: 0.5 / 0.75 / 1.0 / 1.35 / 1.65 / 2.0 applied as `dt * speed` (so it is a time-scale, not a retune).

Other springs in the file:

| Purpose | stiffness / damping | Line |
|---|---|---|
| Tab carousel | 250 / 31 ("settles in ~0.29 s", "~0.92 damping ratio") | 15199 |
| Lyrics scroll | 190 / 26 ("~critically damped: no wobble on line change") | 770 |
| Nudge (event kick) | 280 / 24, kicked with `velocity = -6.0` | 22458 |

**Secondary motion:** a `nudgeSpring` gives a −6 px vertical kick and springs back on every new event — a 4–8 px "bounce" is what makes an arriving toast feel physical rather than slid-in. Time-based (non-spring) durations found in the file: source-dock slide 0.30 s, source switch out/in 0.17/0.24 s, immersive cross-fade 0.32 s.

**Perceptual widening:** `widthSpring` and `heightSpring` are independent, so a 150×44 → 380×184 expansion is not a single diagonal tween; width and height arrive on different curves. On top of that, content is cross-faded by a *derived* alpha rather than its own animation: `expandedAlpha = clamp((contentHeight - 60) / 60)` (line 707) — the expanded dashboard fades in only once the pill is taller than 60 DIP and is fully opaque at 120 DIP. **This coupling of content opacity to the container's spring position is the highest-value motion trick in the repo** — it costs zero extra springs and guarantees content never appears inside a too-small container.

### 2.7 Settings surface `[VERIFIED]`

Windhawk's settings DSL, ~450 lines, 10 groups: **Appearance & Position** (position, target monitor, offsets, separate expanded offset, border-merged, shape style, progress style, spectrum style, size scale, auto-DPI) · **Behavior & Visibility** (always-on-top, expand-on-hover, auto-hide idle seconds, hide on fullscreen, unhide on hover) · **Animations & Performance** (target FPS, animation style, speed, expanded-player transitions, pill cover spin) · **Colors & Theming** (8 presets, opacity, tint, backdrop material, fill opacity, depth, shadow, bloom, text scale, accent mode, accent hex, calendar accent, clock glow, font, border) · **Privacy Indicators** · **Modules & Features** (~35 toggles + numbers) · **Shortcuts & Hotkeys**.

Two settings-design lessons:
- **Defaults are opinions, well chosen**: `AlwaysOnTop: true`, `ExpandOnHover: true`, `AutoHideFullscreen: true`, `PillOpacity: 96`, `TintIntensity: 72`, `AccentBloom: 100`, `TextScale: 100`, `MediaAutoExpand: false` (do **not** auto-expand on every track change — that is the #1 annoyance in these apps), `ExpandOnHover` + `UnhideOnHover`.
- **A documented anti-pattern**: the right-click menu holds Theme / Shape / Pin, but those are re-applied from Windhawk settings on restart, so they silently reset. The README has to shout about it in a `> [!IMPORTANT]` block. **Lesson: exactly one source of truth for persisted state; transient menus may only toggle session state, and the UI must show which is which.**

### 2.8 Plugin / extensibility `[VERIFIED: none]`

There is **no plugin system**. Extensibility is compile-time (edit the file, rebuild) plus runtime configuration. Data-level extension points: `MediaExpandBlocklist` (comma-separated app/site names that never auto-expand), custom date patterns, custom font family, custom hex palettes, `GameOverlayShow*` metric toggles, File Tray capacity.

Given FluidBar already has `Plugins/` with `catalog.json` and per-plugin settings, **this repo offers nothing to copy here** — WinIsland is the reference (§5.2).

### 2.9 i18n `[VERIFIED]`

12 languages: en, fr, es, de, pt, it, ru, tr, hi, zh-Hans, ja, ko; `auto` follows the Windows display language. Implemented as an in-file string table keyed by label id (`Wh_GetStringSetting`-adjacent lookups), not resource files. Notably localisation-aware beyond strings: 12/24 h and date format follow the Windows locale, calendar first-day-of-week follows regional format with a manual override, and CJK date patterns work because the formatter is hand-written.

### 2.10 Licence / size / activity

MIT (`Copyright (c) 2026 devcode90`) — **copying code and constants into FluidBar is permitted**, provided the MIT notice is retained for substantial portions. Practical caveat: the file is a Windhawk mod that calls `Wh_Log`, `Wh_GetIntSetting`, `Wh_SetFunctionHook` etc. Any extracted code must have those replaced, and the file has essentially no seam between "app logic" and "Windhawk runtime".

Activity: created 2026-05-20, last push 2026-10-13, 40 stars, 21 forks, 22 open issues, single squashed commit in a shallow clone (PR #103 is the tip: "Live Lyrics, Quick Lookup, and Cross-Source Media Switching"). CHANGELOG.md is 37.8 KB — unusually thorough for a mod, and a good read for edge cases (sleep/resume, DPI, fullscreen detection).

### 2.11 Worth learning (ranked)

1. **`Activity = f(state, settings)`** with a deterministic priority list, then springs chase the result. One code path for every module; no per-module show/hide animation code.
2. **Asymmetric springs** (expand stiff + slightly underdamped, collapse softer + critically damped). This asymmetry is what makes an island feel like it *snaps open* and *relaxes shut*.
3. **Content opacity derived from container height** (`(h-60)/60`) instead of its own animation.
4. **Click-through as a policy, not a flag**: idle + not hovered + not pinned ⇒ `WS_EX_TRANSPARENT`; everything else interactive.
5. **`SetWindowRgn` rounded hit region** so transparent margins don't steal clicks; slack for spring overshoot.
6. **`WS_EX_NOACTIVATE` temporarily dropped** while a text input is focused.
7. **Never out-rank shell surfaces**; whitelist shell window classes before re-asserting topmost.
8. **1 ms timer resolution scoped to animation**, released after 0.5 s idle; render thread fully parked when hidden.
9. **Measured, not hard-coded, idle width** — with a long comment explaining the bug it fixed (`96px` for "9:41" = 24 % occupancy; `10:41:32 PM` clipped at textScale 1.4). Round measured widths up to a 2 px quantum so sub-pixel text metrics can't resize the layered window every frame.
10. **Fixed 0.5 ms substeps** in the spring stepper.
11. **Document the invariant in the code**: "the sizer and the painter read the same constants so they cannot drift apart" — repeated in five layout namespaces. Layout constants shared by hit-test, sizer and painter is the discipline that stops "the click target is 12 px off" bugs.
12. **Adaptive type ladder**: weather description font size steps 13.5 → 12.5 → 11.5 → 10.5 → 9.8 px by string length (line 13461) — cheaper and better-looking than ellipsis.

### 2.12 Do NOT copy

1. **Nothing that touches Windhawk.** `Wh_*` APIs, `@include windhawk.exe`, hooking, and the single-file 23.6k-line structure. It is unmaintainable by construction, untestable (zero tests), and every global is a file-scope mutable.
2. **The DPI model** (global scale factor from the primary monitor, no `WM_DPICHANGED` handling). Wrong for a real app in 2026.
3. **Per-frame `SetWindowPos` + `SetWindowRgn` + full `UpdateLayeredWindowIndirect`.** It works in C++/D2D at 360 Hz; in WPF this would be a layout+present storm. See §12.4 for the WPF-shaped alternative.
4. **The 8 theme palettes' exact colours** as a *design system* — they are a nice curated set (§7.4) but they are tuned to a 96 %-opaque pill on black; re-derive for FluidBar's material.
5. **The settings/right-click split-brain** (§2.7).
6. **A 500 FPS option.** It is a marketing checkbox; it multiplies power draw for no perceptual gain beyond ~120 Hz (and the mod's own motion blur is absent, so high FPS is doing the work that velocity blur should do).
7. **The 23k-line-in-one-file contribution model** — every one of the 6 credited contributors edited the same file; the README literally instructs contributors to add their name to a comment block. Merge pain is the reason the CHANGELOG is longer than the README.

---

## 3. Deep-dive B — `ng1145/dynamic-island-mchack`

### 3.1 Identity and repo facts `[VERIFIED]`

| Field | Value |
|---|---|
| Full name | `ng1145/dynamic-island-mchack` |
| What it is | "Dynamic Island · MC-Hack v3.7（Android 原生复刻）" — a **Kotlin/Jetpack Compose Android app** that reproduces the iOS Dynamic Island, demoed as a fictional Minecraft cheat-menu control panel (15 modules, all telemetry is local random-walk) |
| Language / stack | Kotlin 2.x, Jetpack Compose (Material 3), Gradle 8.5+/9.1, `compileSdk 35`, **`minSdk 31`** (Android 12) — because goo-blur and content blur use `RenderEffect` |
| Stars / forks | not captured via API; README badges indicate a small demo project (v3.7, Aug 2026) |
| Size | 96 files, ~720 KB; app module ~430 KB of Kotlin |
| Licence | **MIT** ("Copyright (c) 2026 Dynamic Island MC-Hack contributors") |
| Last commit inspected | `7fd875a`, 2026-08-30 |

**It is not a Windows project and not a Windows reference.** It is included because the user named it, and because its *motion engineering* is the most reusable artefact in either repo: a complete `(period, damping-ratio)` preset table, squircle corner maths, and velocity-driven deformation — all MIT.

### 3.2 Architecture `[VERIFIED]`

```
app/src/main/java/com/mchack/island/
├── MainActivity.kt
├── core/      IslandState.kt (28.7 KB, the single state machine)  LayerAnim.kt  Scheduler.kt
│              Showtime.kt (14.6 KB, "what is on screen now" sequencing)  Springs.kt (the 20-spring set)
├── motion/    Spring.kt (closed-form)  Motion.kt (knobs + preset table)  Anims.kt  Keyframes.kt  Squircle.kt
├── island/    IslandStage.kt  IslandView.kt  Compact.kt  Expanded.kt  Geometry.kt  Gestures.kt
│              Goo.kt  Primitives.kt  Avatar.kt  cards/{CardsCombat,CardsMove,CardsWorld}.kt
├── icons/     Glyphs.kt GlyphRender.kt GlyphBitmaps.kt Icons.kt
├── model/     Modules.kt Telemetry.kt
├── panel/     Panel.kt (36.5 KB, the settings/控制面板 UI)
└── ui/theme/  Theme.kt Type.kt
```

Three-stage separation, stated as a design rule in the README: **composition / layout / draw are separate passes**, and per-frame geometry is computed once in `Geometry.solve()` per frame — not read inside composition (reading a per-frame value during composition would re-compose the whole tree every frame, which the README's pitfall table lists as the #1 performance bug).

`Springs.kt` holds **20 independent springs** — `w, h, r, pr, band, gap, dx, dy, fx, fy, bx, by, cs, cly, pz, bs, bo, gl, bg, tn` — stepped in one loop, with `allRest()` short-circuiting frames when everything is at rest. The README explains why: "every spring is an independent physical channel; they never block each other — that is the root reason the animation can be interrupted or superposed at any moment without a jump."

### 3.3 Feature list (as a UI demo) `[VERIFIED]`

15 modules (KillAura / Aimbot / Criticals / Velocity / AutoClicker / Fly / Speed / Jesus / Scaffold / Nuker / ESP / X-Ray / Tracers / Freecam / AutoTotem), each with an expanded data card and 390 ms-heartbeat fake telemetry. Island capabilities: collapse/expand, long-press preview, double-tap, swipe-up dismiss, horizontal swipe to change module, a detached **ball** that can be dragged/thrown and merges with the body via a goo blur+threshold, badge dot, press-zoom, glow, tint, pagination. Settings panel with three global knobs (duration / speed / amplitude), a "minimal" style switch, and a reduced-motion (`CALM`) flag.

### 3.4 Motion system — the reusable part `[VERIFIED]`

**Closed-form spring** (`Spring.kt`): each step advances a damped harmonic oscillator analytically, so the result is frame-rate independent and identical to the original web implementation:

```
if ζ < 0.9999:  ωd = ω√(1-ζ²);  e = e^(-ζωt)
                x(t) = e·(x₀cos ωd t + ((v₀+ζωx₀)/ωd) sin ωd t)
else:           x(t) = (x₀ + (v₀+ωx₀)t)·e^(-ωt)
rest threshold: |x| < 4e-4 && |v| < 4e-3
```
with `ω = 2π / r`, `r` = period in seconds, `ζ` = damping ratio. This is the same parameterisation SwiftUI uses (`response` / `dampingFraction`), which makes cross-platform token transfer trivial (§8.1).

**Preset table** (`Motion.kt`, `BASE`), with the exact source numbers — the most valuable single artefact in the repo:

| Slot | r (s) | ζ | Meaning |
|---|---|---|---|
| `popW` / `popH` | 0.50 / 0.56 | 0.62 / 0.58 | pop/peek width & height — bouncy |
| `morW` / `morH` | 0.42 / 0.46 | 0.76 / 0.72 | morph (expand) width & height |
| `exW` / `exH` | 0.48 / 0.58 | 0.66 / 0.60 | expand width & height |
| `coW` / `coH` | 0.42 / 0.46 | 0.80 / 0.76 | collapse width & height |
| `hide` | 0.34 | 0.90 | hide/dismiss |
| `band` | 0.44 | 0.84 | band height |
| `gapO` / `gapI` | 0.56 / 0.36 | 0.46 / 0.74 | outer/inner gap (ball detachment) |
| `bIn` / `bOut` | 0.44 / 0.30 | 0.48 / 0.88 | ball in / ball out |
| `lag` / `snap` | 0.32 / 0.34 | 0.70 / 0.58 | content lag / snap |
| `press` | 0.24 | 0.68 | press-zoom |
| `bdg` | 0.42 | 0.46 | badge |
| `glow` | 0.68 | 1.00 | glow (never overshoots) |
| `clag` | 0.50 | 0.64 | content lag |
| `prad` | 0.30 | 0.80 | press radius |
| `sfW`/`sfH` | 0.38/0.40 | 0.78/0.76 | soft (idle) width/height |
| `itW`/`itH` | 0.32/0.34 | 0.92/0.90 | interrupt width/height (near-critical) |

Two derived knobs: `SPD = 0.55 + speedKnob·1.45` divides every period; `AMP = ampV·1.9` lowers every damping ratio via `ζ = clamp(1 - (1-ζ₀)·AMP, 0.14, 1)`. So one "bounciness" slider scales overshoot across the whole system while keeping relative relationships — a very good settings design (and `CALM` forces `AMP = 0.10`, i.e. accessibility reduced-motion).

**Time-based durations** (non-spring), all divided by `SPD`: content fade in **340 ms**, transform/morph **580 ms**, content out **150 ms**, interrupt out **100 ms**, hit reaction **950 ms**, child stagger **36 ms**; auto-expand dwell `beat()` = min(2200 ms, 55 % of the notification duration); notification duration 4 ms … 10 s or "always on".

**Easings** (for the CSS-ish parts):
- iOS standard move: `cubic-bezier(.32,.72,0,1)`
- exit (accelerating): `cubic-bezier(.4,0,1,1)`
- overshoot: `cubic-bezier(.1, 1+0.42·A, .33, 1)` and `cubic-bezier(.16, 1+0.62·A, .3, 1)`
- row-in: `cubic-bezier(.24,1.2,.5,1)`; switch knob `(.26,1.42,.48,1)`; button press `(.24,1.56,.48,1)`; queue dot `(.22,1.5,.44,1)`; slider knob `(.24,1.5,.46,1)`

Note the **pattern**: control-point `y > 1` = overshoot, and the amount of overshoot is tied to the same amplitude knob as the springs. Consistent physics and consistent easing damping = one coherent feel.

### 3.5 Geometry / squircle `[VERIFIED]`

- `IDLE_W = 112`, `IDLE_H = 33` (design units = dp); stage `TOP = 22`, `STAGE_H = 242`.
- **Squircle corners**, not `RoundedRectangle` (`Squircle.kt`): superellipse sampled at N=15 points per quadrant, exponent `e = 2/k`, with

  ```
  m = min(w,h)/2 ;  r = clamp(radius, 0, m) ;  k = 2 + (1 - r/m) * 1.22
  ```

  So a full-height radius (a pill) gives `k = 2` (a true ellipse corner) and a small radius gives `k ≈ 3.2` (squarer) — the iOS continuous-curvature family, in one line. Sample points cached by `(k*32)|0`.
- **Velocity → deformation** (`Geometry.kt`) — the "rubber" feel:

  ```
  vw  = clamp( w.vel  * 0.000125 * A, -.05, .062)
  vg  = clamp( gap.vel* 0.00025  * A, -.062, .12)
  vd  = clamp( dy.vel * 0.000048 * A, -.05, .05)
  vfx = clamp(|dx.vel|* 0.000042 * A, 0, .05)
  W = W0*(1 + vw + vg*.26 + vfx)
  H = H0*(1 - vw*.64 - vg*.14 + vd - vfx*.5)
  ```
  i.e. while the width spring is moving fast, the island is drawn **wider and shorter**, and the deformation reverses as the spring overshoots back. No extra animation state; it is derived from spring velocity. This is the cheapest possible "squash & stretch" and it is what separates a springy island from a resizing rectangle.
- Ball squash: `Dw = D*(1+sqx-sqy*.5)`, `Dh = D*(1-sqx*.55+sqy)`; icon counter-rotates `clamp(gap.vel*.006*A, ±16°) + clamp(bx.vel*.004*A, ±11°)` to fake inertia.
- Goo merge: Gaussian blur (`stdDeviation ≈ 8` design px, scaled by density) + alpha threshold — equivalent to SVG `feGaussianBlur` + `feColorMatrix`, on Android via `RenderEffect` (hence minSdk 31).
- **Pixel snapping**: `DPR = min(density, 3)`, `snapPx(v) = round(v·DPR)/DPR` to kill sub-pixel shimmer.
- **Rubber-banding** for drag: `(1 - 1/(|d|·0.55/dim + 1))·dim·sign(d)`.

### 3.6 Windows relevance

Zero, directly. But three transferable rules:
1. Express motion as `(period, damping ratio)` per *semantic slot*, not as "duration + curve" per widget.
2. Derive deformation from spring velocity instead of animating it.
3. Keep one global amplitude knob that scales overshoot everywhere, and an accessibility flag that clamps it.

### 3.7 Do NOT copy

- `RenderEffect`-based goo/blur (API 31+, Android-only). On Windows: D2D Gaussian blur or a shader; in WPF: `BlurEffect` on a `VisualBrush` is slow — prefer pre-blurred backdrop composition or skip the goo entirely (Apple doesn't use a goo merge on the island; that is an Android launcher idiom).
- The 20-spring set as-is: several of them (`fx`, `fy`, `clag`, `band`, `pr`) exist to serve the detached-ball design, which FluidBar does not have. Take 6–8.
- The 390 ms fake-telemetry heartbeat (a demo scaffold).
- The Material-3 theming (`Theme.kt`, `Type.kt`) — Android typography, irrelevant to Windows.
- Its README/build tooling assumes a `proot`/ARM64 environment (it forces `aapt2:linux-aarch64`), i.e. it was built on a phone/termux. Not a CI model to imitate.

---

## 4. Apple official reference — the authoritative public facts

### 4.1 What Apple does and does not publish

- Apple **does not** open-source the Dynamic Island UI, and there is no public API to *draw* an arbitrary island. Third-party apps reach it only through **ActivityKit / WidgetKit Live Activities**, which have three island presentations (compact, minimal, expanded) and a Lock Screen presentation.
- Apple **does** publish: the HIG page for Live Activities (with a real dimensions table, updated 2026-10-13), the SwiftUI/WidgetKit API surface, Apple Design Resources templates ("Templates with default system margins and recommended text sizes are available in [Apple Design Resources]"), and SF Symbols under a separate licence.
- Apple **does not** publish: the island's exact corner radius, its spring constants, its blur parameters, or the exact expanded radius. Anything numeric beyond the HIG table is `[UNCERTAIN]` unless measured from a device screenshot.

### 4.2 Anatomy and presentation model `[VERIFIED, HIG]`

> "In the Dynamic Island, the system uses the compact presentation when only one Live Activity is active. The presentation consists of two separate elements: one on the leading side of the TrueDepth camera and one on the trailing side."

> "When multiple Live Activities are active, the system uses the minimal presentation to display up to three of them in the Dynamic Island. One appears attached to the Dynamic Island, while a second (and third, when supported) appear detached on one or both sides. Depending on its content size, a detached minimal presentation appears **circular or oval**."

> "When people touch and hold a Live Activity in compact or minimal presentation, the system displays the **expanded** presentation."

Also: compact appears on iPhone, and the *same* compact content is recombined into a single view on Apple Watch and CarPlay; macOS mirrors iPhone Live Activities into the **menu bar** using all three presentations; StandBy shows the minimal presentation scaled **2×**.

### 4.3 Official dimensions `[VERIFIED, HIG "Specifications", 2026-10-13 revision]`

**iOS** (all pt):

| Screen (portrait) | Compact (leading and trailing, each) | Minimal (width range) | Expanded (W × H range) | Lock Screen | Limited width |
|---|---|---|---|---|---|
| 430 × 932 | **62.33 × 36.67** | 36.67–45 × 36.67 | **408 × 84–160** | 408 × 84–160 | **28 × 28** |
| 393 × 852 | **52.33 × 36.67** | 36.67–45 × 36.67 | **371 × 84–160** | 371 × 84–160 | **28 × 28** |

**Other platforms:** iPadOS Lock Screen 425–500 × 84–160. macOS: "use the provided iOS dimensions". CarPlay: 240×78, 240×100, 170×78. watchOS Smart Stack: 152×69.5 (40 mm) … 198×81.5 (49 mm Ultra 3/4).

**Reading the numbers**

- The compact *element* is a fixed **36.67 pt tall**, and its width is whatever the screen leaves beside the camera (62.33 on a 430 pt screen, 52.33 on 393). Two of them plus the camera cutout make up the pill.
- **36.67 pt** is the number to design against for a collapsed pill height. Not 44, not 27.
- Expanded width is **the full island width** (371/408) and expanded height is **variable, 84–160 pt**. Apple explicitly wants the height to change with content: "Dynamically change the height of your Live Activity… When there's less information to show, reduce the height."
- The **minimal** detached element is 36.67–45 wide (circle → oval), and in **limited-width** contexts (landscape) content must fit a **28 pt-diameter circle**.
- **Lock Screen margin = 14 pt** (explicit: "The standard layout margin for Live Activities on the Lock Screen is 14 points").

### 4.4 Content and layout rules `[VERIFIED, HIG, condensed quotes]`

Compact:
- "Focus on the most important information… show dynamic, up-to-date information that's essential."
- "Ensure unified information and design of the compact presentations… design them to read as a single piece of information."
- "**Keep content as narrow as possible and ensure it's snug against the TrueDepth camera.** … don't add padding between content and the TrueDepth camera. Maintain a balanced layout with similarly sized views for both leading and trailing elements."
- "Ensure both leading and trailing elements link to the same screen."

Minimal: "display updated information rather than just a logo."

Expanded:
- "The expanded presentation is an enlarged version of the compact or minimal presentation. Ensure information and layouts **expand predictably**."
- "Wrap content tightly around the TrueDepth camera… avoid leaving too much room around it."

Global layout rules:
- "Use consistent margins and **concentric** placement… When you place a rounded element near the corner of a Live Activity, use `ContainerRelativeShape` to match the corner radius… instead of hard-coding a value."
- "Keep content compact and snug within a margin that's concentric to the outer edge."
- "When separating a block of content, place it in an inset container shape or use a thick line. **Don't draw content all the way to the edge of the Dynamic Island.**"
- "Don't add elements to your app that draw attention to the Dynamic Island."
- "If you include a logo mark, display it without a container. Don't use the entire app icon."
- "Avoid displaying sensitive information."
- "Adapt to different screen sizes and presentations."

### 4.5 Colour `[VERIFIED, HIG]`

- "**Live Activities in the Dynamic Island use a black opaque background.**" (Compact/minimal/expanded backgrounds are **not customisable** — only the Lock Screen presentation can have a custom background.)
- "Use color to express the character and identity of your app… Consider using **bold colors** for text and objects."
- "When the background is dark — for example, in Dark Mode — a **key line** appears around the Dynamic Island to distinguish it from other content. Choose a key line color that's consistent with the color of other elements."

Design consequence for FluidBar: Apple's island is **opaque black with a subtle key line**, not a translucent glass pill. FluidBar's current 96 %-opaque `#08080A` pill + `#1E1E22` contour is closer to Apple than any acrylic variant — see §7.4 for a reconciled palette.

### 4.6 Typography `[VERIFIED, HIG]`

- "Ensure text is easy to read. Use large, heavier-weight text — **a medium weight or higher**. Use small text sparingly and make sure key information is legible at a glance."
- Two paired correct/incorrect examples exist for text size and for content position; the incorrect text example is explicitly "small and difficult to read".
- SF Symbols weights map 1:1 to San Francisco weights (nine weights), with three scales (small/medium/large) defined **relative to the San Francisco cap height**.
- No point sizes are published per element. `[UNCERTAIN]` Anything claiming "Apple uses 17 pt semibold in the island" is reverse-engineered, not published.

### 4.7 Motion and transitions `[VERIFIED, HIG]`

- "In addition to extending and contracting transitions, Live Activities use system and custom animations with a **maximum duration of two seconds**."
- "**The system doesn't perform animations on Always-On displays with reduced luminance.**"
- "**Animate layout changes.** … preserve as much of the existing layout as possible by **animating existing elements to their new positions** rather than removing and animating them back in."
- "**Try to avoid overlapping elements.** … only animate the element that moves to a new position and use fade-in-and-out transitions for the other list items."
- "Animate elements in and out with the **default content-replace transition**, or create custom transitions using **scale, opacity, and movement**."
- The "extend and contract" transitions are *system-provided*; their curves are not documented. `[UNCERTAIN]` Whether the island's morph is a spring or a bezier is not published; the observable behaviour (fast expand, slight overshoot, slower settle, content cross-fade) is consistent with a spring in the 0.35–0.45 s period range and ζ ≈ 0.75–0.9 — which is exactly where every shipping clone sits (§8.1), and is the empirical justification for the recommended tokens.

### 4.8 Apple Design Resources & templates

Apple ships iOS/iPadOS design templates (Sketch/Figma/Photoshop) containing the Dynamic Island with correct dimensions and **live-activity layout guides**, plus the SF Symbols app. The HIG explicitly points to them: "Templates with default system margins and recommended text sizes are available in [Apple Design Resources]." `[VERIFIED as pointer]` — the files themselves require accepting Apple's licence and are not redistributable; they are for reference only and cannot be shipped as FluidBar assets.

### 4.9 SF Symbols licensing — the hard boundary `[VERIFIED, HIG]`

> "Be sure to understand the terms and conditions for using SF Symbols, including the **prohibition against using symbols — or images that are confusingly similar — in app icons, logos, or any other trademarked use**."

Practical rules for FluidBar:
1. SF Symbols may not be used as FluidBar's **logo/app icon**, nor in anything "confusingly similar".
2. The SF Symbols licence is scoped to Apple-platform UI; shipping the font inside a **Windows** app is outside that scope. `[INFERRED from licence scope — legal review advised]`
3. Do **not** extract glyph outlines from SF Symbols into a Windows icon font. That is redistribution.
4. The safe substitute on Windows is the system icon fonts already used by the Windhawk mod and by WinIsland: **Segoe Fluent Icons**, falling back to **Segoe MDL2 Assets**, with handwritten vector paths (WinIsland draws its own icon paths) as the third tier.
5. Never imitate Apple's *trade dress*: no SF-symbol-shaped logo, no "Dynamic Island" name/wordmark, no Apple-style island silhouette in FluidBar's branding. Nominative references in documentation are fine; product identity must be distinct.

### 4.10 WWDC and documentation entry points `[VERIFIED links]`

- WWDC26-223 "Live Activities essentials" — https://developer.apple.com/videos/play/wwdc2026/223 (the HIG page's current primary video; covers the new landscape style in the Dynamic Island, i.e. the limited-width guidance added 2026-10-13).
- WWDC25-278 "What's new in widgets" — https://developer.apple.com/videos/play/wwdc2025/278
- WWDC25-216 "Turbocharge your app for CarPlay" — https://developer.apple.com/videos/play/wwdc2025/216
- WidgetKit "Animating data updates in widgets and Live Activities" — https://developer.apple.com/documentation/widgetkit/animating-data-updates-in-widgets-and-live-activities
- `EnvironmentValues.isDynamicIslandLimitedInWidth` — the API behind the 28 pt circle rule.
- `DynamicIslandMode` — https://developer.apple.com/documentation/widgetkit/dynamicislandmode

### 4.11 What we still do not know (do not invent)

| Unknown | Status | How to resolve |
|---|---|---|
| Island corner radius (compact/expanded) | Not published | Measure a screenshot: at 393 pt width the pill's end caps are elliptical, consistent with radius = h/2; expanded corners read ~40–44 pt at 371 pt width `[UNCERTAIN]` |
| Spring constants for extend/contract | Not published | Empirical fit only (§8.1) |
| Blur/material parameters | Not published | None — the background is opaque black by design |
| Inner content margins for expanded | Not published numerically; 14 pt is Lock Screen only | Adopt 16–20 pt and validate visually |
| Whether minimal detached elements animate position | Not published | Not needed for FluidBar |

---

## 5. Additional open-source implementations analysed

Selection criteria: (a) actually ships a notch/island overlay, (b) source available, (c) covers a technique FluidBar needs (plugin system, transparency pipeline, motion), (d) recency.

### 5.1 `Ebullioscopic/Atoll` — macOS "Dynamic Island", Swift/SwiftUI

`[VERIFIED]` 4,985 stars · 339 forks · 219 open issues · Swift · **GPL-3.0** · created 2024-11-01 · pushed 2026-10-18 (today) · 60 MB tree, 587 files · Apache-2.0? no — GPL-3.0.

What it is: "Atoll turns the MacBook notch into a focused command surface for media, system insight, and quick utilities." Highlights: media controls (Apple Music, Spotify, Cider), Live Activities for playback/Focus/screen-recording/privacy-indicators/downloads/battery, Lock Screen widgets, CPU/GPU/memory/network/disk insight, timers, clipboard history, colour picker, calendar preview, layout/animation/hover/shortcut customisation.

`[INFERRED]` **Atoll is a fork/derivative of boring.notch**: identical module folder names (`animations`, `components`, `metal`, `models`, `observers`, `MediaControllers`, `menu`, `private`), an identical `mediaremote-adapter/` submodule, and its git history contains merge commits authored by `TheBoredTeam`. Both are GPL-3.0, so this is legally coherent.

Techniques worth studying:
- **`NSPanel`-based non-activating overlay** at the notch, with the window sized to the notch and expanded content drawn *outside* the physical notch area.
- `Metal` folder → a real GPU path for the visualiser/blur, not just SwiftUI effects.
- **Spring constants** (`ContentView.swift`): notch open `Animation.spring(response: 0.42, dampingFraction: 1.0)`; close `response: 0.45, dampingFraction: 1.0`; hover `response: 0.34, dampingFraction: 0.88`.
- **`.blurReplace` transition** for content swaps — `[VERIFIED in source]` a *blur-morph* between two content states, which is a much better answer than a cross-fade for "one card becomes another". `transition(.blurReplace.animation(.interactiveSpring(dampingFraction: 1.2)))`.
- Hover-intent handling, per-layout customisation, shortcut remapping.

Not to copy: GPL code; macOS-only APIs; the entirely notch-shaped (top-flush, fixed-width-at-notch) layout assumption — Windows has no notch, so a *floating pill* is the correct target shape (which is why the Windhawk mod's "notch" style is opt-in, not default).

### 5.2 `TheBoredTeam/boring.notch` — the upstream macOS notch app

`[VERIFIED]` **11,065 stars** · 1,115 forks · 399 open issues · Swift · **GPL-3.0** · created 2024-08-02 · pushed 2026-10-18 · 7.2 MB, 223 files · Crowdin-based i18n (`crowdin.yml`) · `BoringNotchXPCHelper` (a privileged helper for HUD replacement) · `mediaremote-adapter` (private-framework media control) · `metal/` · `updater/` (Sparkle-style).

Feature set: music control + visualiser, calendar, file shelf with AirDrop, full macOS HUD replacement (volume/brightness), Live Activities, multiple gestures, extensive settings.

Springs (`ContentView.swift`): `interactiveSpring(response: 0.38, dampingFraction: 0.8)` for gestures; open `response: 0.42, dampingFraction: 0.8`; close `response: 0.45, dampingFraction: 1.0`.

Architecture lessons for FluidBar:
- **XPC helper for privileged/HUD features** — the Windows analogue is a separate optional helper process or a tray-only service, rather than trying to do everything in the UI process. FluidBar's plugin hosts (e.g. AgentStatus via PowerShell hooks) already lean this way.
- **Crowdin i18n** — string externalisation from day one, community translation. The Windhawk mod's in-file table (12 languages) is the cautionary opposite.
- **A real updater** (`updater/`, appcast) and signed releases.
- 399 open issues at 11k stars is a reminder that the "everything in one notch" product surface generates enormous support load. Prioritise.

### 5.3 `WinIslandProject/WinIsland` — Windows, Rust + Skia + D3D12 — **the most relevant architecture**

`[VERIFIED]` 356 stars · 30 forks · 6 open issues · Rust · **GPL-3.0** · created 2026-02-28 · pushed 2026-10-18 · 16.9 MB, 500 files · v1.5.1 · website https://winisland.cn (also tanikaze.icu) · QQ community.

**Stack:** Rust 2024 edition; `winit` + **DirectComposition**; **Skia Ganesh on D3D12** with premultiplied-alpha DXGI composition swap chains; Windows SMTC via COM; cpal/WASAPI loopback + realfft (6-band spectrum); WinRT toasts; tray-icon; `windows` crate 0.62.

**Workspace layout** (11 crates — this is the lesson):

```
crates/winisland-core/           domain layer: NO Windows API, NO Skia, NO winit, NO UI
                                 config/ (schema + migration + atomic write), context/, i18n/,
                                 lyrics/ (+providers), anim.rs (keyed animation pool),
                                 physics.rs (spring), plugin_settings.rs, widgets.rs
crates/winisland-plugin-api/     ABI v2 types, draw protocol, SDK, packager
crates/winisland-plugin-host/    per-instance service tables, loader, lifecycle, draw validation/replay
crates/winisland-plugin-package/ manifest, ZIP activation, signing, marketplace catalog
crates/winisland-render/         Painter, images, text, D3D12 targets, frame lifecycle
crates/winisland-platform/       OS-neutral traits + value types; zero deps, zero unsafe
crates/winisland-platform-windows/ window/, backdrop, shell, metrics, display, audio, media, notify, input
crates/settings-schema(+derive)/ a #[derive(Settings)] macro → settings UI generated from the struct
crates/pollkit/                  polling helpers
src/                             app crate; depends on core, never the reverse
```

**Rendering pipeline** (`ARCHITECTURE.md`): `Resumed → create backdrop before the owned foreground window (transparent, topmost, skip-taskbar) → D3D12 device + shared Skia DirectContext → independent composition swap chains for island and settings windows`. Per-frame in `about_to_wait()`: enforce topmost → tray events → config diff → plugin installs → cursor hit-test/auto-hide → seeking/borders/lyrics transitions → **compute spring targets, step all springs** → request redraw if animating → return earliest deadline (`WaitUntil`). Draw: dt + **motion-blur sigmas** → SMTC state → spectrum → background (default/glass/dynamic) → album art → lyrics → spectrum → progress → controls → present.

**Three background styles:** `glass` = a *companion Windows Composition window* with a clipped host-backdrop brush; `dynamic` = cached blurred album art rendered by Skia; `default` = solid black. "Liquid glass" settings: blur 6, refraction 200, dispersion 40, tint 100, Siri mode, capture-compat toggle.

**Defaults that calibrate our spec** (`config/mod.rs`): `base_width 120`, `base_height 27`, `expanded_width 360`, `expanded_height 200`, `compact_scale`/`expanded_scale` 0.5–5.0, `hidden_width 5`, `motion_blur true`, `lyrics_scroll_max_width 300`, `auto_hide_delay 5 s`, `monitor_index`, `position_x/y_offset`, font family/weight/`font_size`, `music_text_line_spacing 22`.

**Springs** (`physics.rs` + `frame.rs`): a *stiffness-as-fraction / damping-as-retention* formulation rather than the classical `k`/`c`:

```rust
force = (target - value) * stiffness * dt;
velocity = (velocity + force) * damping.powf(dt);
value += velocity * dt;
```
with `(0.10, 0.68)` for w/h/r, `(0.12, 0.68)` for view, `(0.15, 0.71)` for multitask, hide `(0.12, 0.70)` when hiding / `(0.08, 0.78)` when showing, plus `settle(eps)` and `redirect_velocity_towards()` (momentum retention 0.35, max distance ratio 0.2) for **interrupt handling**. `[VERIFIED]`.

`[INFERRED]` These constants are not directly convertible to `k`/`c` (the damping term is a per-second multiplier, not a force), so do not transliterate them; use the *regime* they imply (near-critical, ~0.3–0.4 s settle) and the *techniques*.

**Motion blur** (`utils/blur.rs`) — velocity-driven, GPU-profile aware:

```
integrated GPU caps:  (4.0, 3.5) px σ ;  discrete: (12.0, 10.0)
horizontal σ = min(|w.vel|*0.3 + |view.vel|*current_w*0.4, cap_x)
vertical   σ = min(|h.vel|*0.3, cap_y)
```
This is the honest answer to "how do I look smooth at 60 Hz": smear the fast frames instead of adding frames.

**Plugin system** (the crown jewel, §10).

**Also notable:** `settings-schema` derive macro generates the settings *UI* from the config struct (typed `number(min,max,step)`, `toggle`, `choice`, labels resolved through i18n) — a huge saving and a strong pattern for FluidBar's `Settings.cs`; `pollkit`; atomic config writes; version migration; `~/.winisland/config.toml`; i18n via custom `.lang` files with **plugin translation bundles**; **no telemetry** (explicit privacy policy); signed plugin packages with `ed25519-dalek`; SHA-2; ZIP activation with backup/rollback.

**Licence warning:** GPL-3.0. Copy patterns, not code.

### 5.4 `Lakr233/NotchDrop` — the minimal, permissively-licensed notch app

`[VERIFIED via API]` 2,127 stars · 160 forks · 5 open issues · Swift · **MIT** · 1,060 KB · created 2024-07-07 · pushed 2026-05-18. "Use your MacBook's notch like Dynamic Island for temporary storing files and AirDrop."

Why it matters: it is the **MIT-licensed proof that a small, single-purpose island utility can reach 2k stars** — and it is small enough (1 MB) to read end-to-end. It is the right scope model for a *plugin*, not for FluidBar itself.

### 5.5 `appsfolder/livebridge` — Android, Flutter

`[VERIFIED via API]` 1,133 stars · 52 forks · 28 open issues · Dart/Flutter · **GPL-3.0** · 60 MB · created 2026-02-22 · "Level up your dynamic pill/island on Android". Useful only as evidence that the **"island as a platform for other apps' live updates"** framing has real demand (it is an *API/bridge* for other apps, not just a widget). Skip for implementation.

### 5.6 Commercial references — NotchNook and Dynamic Lake

Both are closed-source macOS apps. `[UNCERTAIN]` details are from their public marketing pages only; nothing was downloaded or reverse-engineered. What is observable and worth noting as *product* patterns:
- **NotchNook** sells "the notch as a shelf + media player + HUD" with a paid licence and a trial — i.e. the File Tray + HUD + media bundle is a proven paid product, which validates FluidBar's File Tray and volume/brightness flyouts.
- **Dynamic Lake** positions itself around *live* activity cards (timers, downloads, now-playing) rather than static widgets.
- Neither has a plugin marketplace. That remains an open niche.

### 5.7 `JNTMTMTM/eIsland` — Electron cautionary tale

Windows, Electron. `[VERIFIED description]` "A sleek, Apple Dynamic Island inspired floating widget for Windows, built with Electron." `[INFERRED]` A Chromium renderer for a 120×36 pill costs 80–200 MB RSS and cannot do per-pixel-alpha window shaping cleanly (frameless + transparent BrowserWindow gives you a full rectangular hit area unless you also do `setIgnoreMouseEvents` with forwarding). FluidBar should treat this as the counter-example: the whole value of an island is that it is *cheap and always there*.

---

## 6. Feature matrix

Legend: ● full · ◐ partial/one-off · ○ none · `–` not applicable. FluidBar column = current state read from source (`MainWindow.xaml`, `Settings.cs`, `Plugins/`), not aspirational.

| Capability | Windhawk mod (MIT) | mchack (MIT) | WinIsland (GPL) | boring.notch (GPL) | Atoll (GPL) | NotchDrop (MIT) | FluidBar today (WPF) |
|---|---|---|---|---|---|---|---|
| Host model | injected into `windhawk.exe`, own HWND | Android app | standalone Rust app | standalone macOS app | standalone macOS app | standalone macOS app | standalone WPF app |
| Media now-playing | ● SMTC + 4 spectrum + 4 progress styles | ○ (fake) | ● SMTC + 6-band spectrum | ● MediaRemote + visualiser | ● Music/Spotify/Cider | ○ | ● MediaPlugin (37.6 KB) + lyrics |
| Lyrics | ● LRCLIB, karaoke, BT offset | ○ | ● 5 providers, scroll, transitions | ◐ | ◐ | ○ | ● Kugou provider (32 KB) |
| Calendar | ● | ○ | ◐ plugin | ● | ● | ○ | ○ |
| Weather | ● wttr.in | ○ | ○ | ○ | ◐ | ○ | ○ |
| System monitor (CPU/GPU/RAM/net) | ● PDH | ○ | ○ | ○ | ● | ○ | ○ |
| Notifications | ● WinRT listener | ○ | ● WNF/toast/WinRT | ○ | ○ | ○ | ◐ (verification-code detection) |
| Clipboard | ● text + image thumb | ○ | ○ | ○ | ● history | ○ | ● ClipboardPlugin |
| File shelf / tray | ● | ○ | ○ | ● + AirDrop | ○ | ● + AirDrop | ○ |
| Volume / brightness HUD | ● | ○ | ○ | ● (replaces macOS HUD) | ○ | ○ | ○ |
| Privacy indicators | ● mic+cam | ○ | ○ | ◐ | ● | ○ | ○ |
| Battery / BT / USB events | ● | ○ | ◐ | ◐ | ● | ○ | ○ |
| Timer / Pomodoro | ● | ○ | ○ | ● | ● | ○ | ○ |
| Quick lookup / AI | ● Wiktionary/Wikipedia | ○ | ○ (AI agents in `ping-island`) | ○ | ○ | ○ | ◐ AgentStatus plugin |
| **Plugin system** | ○ | ○ | ● **ABI v2 DLL + signed packages + marketplace** | ○ | ○ | ○ | ◐ in-proc C# `Plugins/` + `catalog.json` |
| Plugin marketplace | ○ | ○ | ● catalog + signing | ○ | ○ | ○ | ◐ `catalog.json` (local) |
| Themes | ● 8 presets + custom hex | ● M3 | ● default/glass/dynamic + liquid-glass params | ● | ● | ○ | ◐ theme transition anim |
| i18n | ● 12 langs, in-file | ◐ zh only | ● en/zh, `.lang` + plugin bundles | ● Crowdin | ◐ | ○ | ◐ (needs audit) |
| Multi-monitor | ● 6 positions + follow-mouse | – | ● `monitor_index` + offsets | – | – | – | ◐ `Monitors/` |
| Click-through | ● dynamic policy | – | ● hit-test | – | – | – | ◐ |
| DPI | ◐ global scale | ● density | ● per-monitor (winit) | – | – | – | ◐ |
| Motion blur | ○ | ○ | ● velocity σ | ○ | ○ | ○ | ○ |
| Squircle corners | ○ (round-rect) | ● superellipse | ● `continuous_rounded_rect` | ◐ | ◐ | ○ | ○ (Border radius) |
| Velocity deformation | ○ | ● | ◐ | ○ | ○ | ○ | ○ |
| Tests | ○ | ◐ instrumentation | ○ (explicitly refuses tests) | ● CI + tests | ● tests | ○ | ● FluidBar.Tests |
| Licence | **MIT** | **MIT** | **GPL-3.0** | **GPL-3.0** | **GPL-3.0** | **MIT** | **MIT** |

**Read-out:** FluidBar is already competitive on *integration breadth* (media, lyrics, clipboard, notifications, agents) and behind on *platform polish* (click-through policy, multi-monitor placement, motion blur, squircle geometry, DPI) and *extensibility* (a real plugin ABI + marketplace). That is exactly the gap a rewrite should close.

---

## 7. Visual specification

### 7.1 Size comparison — every reference on one axis (DIP / pt)

| Reference | Collapsed / small | Expanded / large | Notes |
|---|---|---|---|
| **Apple (official)** | h **36.67**; compact element w 52.33–62.33; minimal w 36.67–45; limited 28×28 | **371–408 × 84–160** | Lock Screen margin 14 |
| **Windhawk mod** | idle 96 (170 w/ weather) × 36; media 150×44; caps 180×42; DND 220×42; device 240×50; progress 230×48; timer 260×54; BT 280×54; battery 290×52; volume/brightness 300×54; clipboard 340×56; notification 360×58 | media **380×184** (+220 w for side lyrics) | window = content + 28 pad X / 22 pad Y; initial HWND 520×140 |
| **WinIsland** | **120×27** (default, user-editable 40–400 × 15–200) | **360×200** (editable 200–2000 × 100–1000) | scales 0.5–5.0; hidden width 5 |
| **mchack (design units)** | **112×33** | (per-card, stage 242 tall) | TOP offset 22 |
| **Atoll / boring.notch** | notch-width, ~32–38 tall | ~600×~250 (screen-width-ish) | top-flush, no floating pill |
| **NotchDrop** | notch-width | ~notch + drop zone | top-flush |
| **FluidBar today** | `MinWidth 126` × `MinHeight 38` | `MaxWidth 640`, expanded card `MaxWidth 430` | radius 24 / 28; 14×14 inner |

**Convergent centre of gravity for a *floating Windows pill*:** height **36–40**, idle width **96–120**, media width **150–180**, expanded **360–390 × ~150–200**, expanded radius **28–32**, stadium small radius.

### 7.2 Corner radius & curvature

| Surface | Apple | Windhawk mod | WinIsland | Recommendation for FluidBar |
|---|---|---|---|---|
| Small pill | stadium (h/2) | `min(h/2, 44)`; notch style 16; W11 style 8 | `r = h/2` as a spring | **stadium**, `r = h/2`, clamped to 44 max |
| Large island | ~40–44 `[UNCERTAIN]` | `min(h/2, 44)` — for h=184 that is 44 | `r` is its own spring, target h/2→ | **28** (at h=152), continuous |
| Inner cards / inset groups | concentric via `ContainerRelativeShape` | — | — | **16** |
| Buttons / chips | — | nav 16, play 22, lyrics 14 | — | **10–12** (32–40 hit) |
| Artwork | — | collapsed art radius → expanded 12 (radius morphs) | rounded cover | **8** small, **12** large |
| Notch style | — | 16 | — | 16 (only for the opt-in notch mode) |

**Continuous curvature (squircle).** WPF `Border.CornerRadius` and `RectangleGeometry` produce *circular* arcs, which read as "web card" rather than "iOS island". The superellipse formula from mchack is one line and is the cheapest way to fix this:

```
m = min(w, h) / 2
rr = clamp(radius, 0, m)
k  = 2 + (1 - rr/m) * 1.22        // k = 2 at a full-height pill; ≈3.2 at a small radius
x(θ) = cx + rr * sign * |sin θ|^(2/k)
y(θ) = cy + rr * sign * |cos θ|^(2/k)      // 15 samples per quadrant is enough at ≤44 px radius
```
WinIsland's renderer has the same concept as `Path::continuous_rounded_rect`. `[VERIFIED name only; implementation not read]`

### 7.3 Spacing & type scale

**Spacing (4-based, with a 2-px optical sub-grid)**

`2 · 4 · 6 · 8 · 12 · 14 · 16 · 20 · 24 · 28 · 32`

| Token | Value | Use |
|---|---|---|
| `space.inset.small` | 12 | inside a 36–40 tall pill (left/right) |
| `space.inset.large` | 20 | inside the expanded island |
| `space.gap` | 4 | between stacked text lines |
| `space.gap.md` | 10–12 | art ↔ text |
| `space.gap.lg` | 16 | between card clusters |
| `space.section` | 24 | between dashboard sections |
| Lock-Screen-style margin | **14** | Apple's published value, use for list rows |
| Idle strip inner padding | **14** | Windhawk `kPadX`, measured |
| Slot gap (clock ↔ divider ↔ weather) | **9** | Windhawk `kSlotGap` |
| Divider | 1 × (h − 18) | Windhawk `kDividerWidth/InsetY` |
| Privacy-dot reserve | **18** | Windhawk `kPrivacyReserve` (dot r=4 at right−20) |

**Typography** — the Windhawk mod's DWrite table is the only fully-specified Windows island type ramp; reuse its shapes, restated in DIP:

| Role | Size | Weight | Family |
|---|---|---|---|
| Body / track title | **13.5** | SemiBold | Segoe UI Variable Display |
| Secondary (artist, labels) | **11** | Normal | Segoe UI Variable Small |
| Clock (expanded) | **18** | Bold | Display |
| Emphasised small (badges) | **12** | Bold | Display |
| Idle clock | **13** | SemiBold | Display |
| Huge numeral (timer/score) | **42** | Bold | Display |
| Calendar day numeral | **63** | Bold | Display |
| Dashboard time | **56** | Bold | Display |
| Dashboard date | **16** | SemiBold | Display |
| Icon (inline) | **16** | Normal | Segoe Fluent Icons → Segoe MDL2 Assets |
| Icon (transport) | **18** | Normal | same |
| Icon (nav) | **14** | Normal | same |

Apple's constraint "medium weight or higher" is satisfied everywhere above (Normal appears only at 11 for secondary labels — which is the one place Apple would say "use sparingly", and we do). Add the Windhawk **length-adaptive ladder** for user strings: 13.5 → 12.5 → 11.5 → 10.5 → 9.8 px at 22 / 32 / 44 / 58 characters.

### 7.4 Colour

**Dark (default)** — reconciled from Apple's "opaque black" rule and the mod's Obsidian palette, with WCAG checks:

| Token | Hex | Contrast on `--bg` | Use |
|---|---|---|---|
| `--bg` (island) | `#08080A` | — | base fill (Apple: opaque black; this is 98 % black) |
| `--bg.raised` | `#141418` | 1.09:1 | inset cards, track |
| `--stroke` | `#1E1E22` | 1.28:1 | 1 px contour / key line |
| `--fg` | `#FFFFFF` | **19.4:1** | primary text, icons |
| `--fg.secondary` | `#9B9BA5` | **7.2:1** | artist, labels (passes AA + AAA-large) |
| `--fg.tertiary` | `#6E6E78` | 3.6:1 | disabled / ghost neighbour lyric lines only |
| `--accent` | from artwork; fallback `#4CC9F0` | ≥ 8:1 on `--bg` for the fallback | bloom, progress, active |
| `--success` | `#34C759` | 9.1:1 | camera dot (Apple's own green) |
| `--warning` | `#FF9500` | 8.0:1 | mic dot (Apple's own orange) |
| `--danger` | `#FF453A` | 5.6:1 | battery low, destructive |
| `--info` | `#0A84FF` | 5.0:1 | links, focus |

**Light ("Porcelain")**: `--bg #F5F6F8`, `--fg #14161A`, `--fg.secondary #5B6270` (5.9:1 on `--bg`), `--stroke #D8DBE1`, `--bg.raised #FFFFFF`.

**Curated alternates** (from the mod, all verified pairs; ship 4, not 8 — see §2.12):
| Name | bg | fg | secondary | stroke |
|---|---|---|---|---|
| Obsidian | `#08080A` | `#FFFFFF` | `#9B9BA5` | `#1E1E22` |
| Graphite | `#1C1C1E` | `#FFFFFF` | `#A8A8AE` | `#323236` |
| Slate | `#111721` | `#E9EEF6` | `#92A2B8` | `#232C3A` |
| Nord | `#2E3440` | `#ECEFF4` | `#A7B0C0` | `#3B4252` |
| Porcelain (light) | `#F5F6F8` | `#14161A` | `#5B6270` | `#D8DBE1` |

Rules to encode: **background opacity default 96 %**, never below 85 % for text-bearing states; **one** accent per state (album-art extraction or system accent); accent bloom clamped to 0–200 % with 100 % default; **no rim light / inner highlight** — the mod explicitly removed its inner highlight ring ("a second bright hairline just inside the contour read as a light rim tracing the whole island, which is exactly the edge glow this design drops"). That is a good call and matches Apple's opaque-black flatness.

### 7.5 Elevation & material

- Shadow: one soft drop shadow, large blur, low alpha, **offset downward only** (the mod: "soft downward depth shading"). No inner shadow, no specular edge.
- Backdrop: `none | blur | acrylic` (Win10 1803+), composed under the layered pixels; when enabled, the island's own fill opacity does the tinting. WinIsland's alternative is stronger: a *separate* composition window that carries the blur, so the island's pixels stay crisp. `[VERIFIED description]`
- Optional liquid-glass refraction (WinIsland: blur 6, refraction 200, dispersion 40, tint 100) — **do not ship as default**; it is expensive and it fights text legibility at 36 px height.

---

## 8. Motion specification

### 8.1 One parameterisation to rule them all

Three schemes appear across the references. They are the same physical system; convert once and pick one for FluidBar.

```
classical:    x'' = -k·x - c·x'          → ω = √k,  ζ = c / (2√k),  T = 2π/ω
period/ratio: ω = 2π / r,  ζ given       (SwiftUI `response`/`dampingFraction`, mchack)
fraction:     v ← (v + (target-x)·s·dt) · d^dt     (WinIsland; not directly convertible)
```

`r`/`T` is the best *authoring* unit: it is what designers and SwiftUI use, and it is frame-rate independent. `k`/`c` is the best *implementation* unit for a fixed-substep integrator. Ship `(T, ζ)` in the design tokens and derive `k = (2π/T)²`, `c = 4πζ/T`.

### 8.2 Measured / declared springs across references

| Source | Declared | → T (s) | → ζ | Feel |
|---|---|---|---|---|
| Windhawk — expand | k 380, c 26 | **0.322** | **0.667** | fast, a visible overshoot |
| Windhawk — collapse | k 200, c 28 | 0.444 | 0.990 | soft, no bounce |
| Windhawk — neutral | k 280, c 24 | 0.376 | 0.717 | default |
| Windhawk — tab carousel | k 250, c 31 | 0.397 | 0.980 | glides, no overshoot |
| Windhawk — lyric scroll | k 190, c 26 | 0.456 | 0.943 | smooth follow |
| Windhawk — "Smooth" × | c × 1.35 | 0.322 | 0.900 | damped |
| Windhawk — "Bouncy" × | k × 1.10, c × 0.70 | 0.307 | 0.445 | very elastic |
| Windhawk — "Snappy" × | k × 1.50, c × 1.25 | 0.263 | 0.681 | quick, tight (note: raising k faster than c *lowers* ζ) |
| Atoll — open | response 0.42, ζ 1.0 | 0.420 | 1.000 | no overshoot |
| Atoll — close | response 0.45, ζ 1.0 | 0.450 | 1.000 | no overshoot |
| Atoll — hover | response 0.34, ζ 0.88 | 0.340 | 0.880 | tiny settle |
| boring.notch — open | response 0.42, ζ 0.8 | 0.420 | 0.800 | gentle overshoot |
| boring.notch — close | response 0.45, ζ 1.0 | 0.450 | 1.000 | no overshoot |
| boring.notch — interactive | response 0.38, ζ 0.8 | 0.380 | 0.800 | gesture-follow |
| mchack — expand (`exW/H`) | 0.48 / 0.58, 0.66 / 0.60 | 0.48–0.58 | 0.60–0.66 | lively |
| mchack — morph (`morW/H`) | 0.42 / 0.46, 0.76 / 0.72 | 0.42–0.46 | 0.72–0.76 | controlled |
| mchack — collapse (`coW/H`) | 0.42 / 0.46, 0.80 / 0.76 | 0.42–0.46 | 0.76–0.80 | softer than expand |
| mchack — press | 0.24, 0.68 | 0.240 | 0.680 | snappy |
| mchack — hide | 0.34, 0.90 | 0.340 | 0.900 | clean exit |

**Consensus zone:** expand **T 0.30–0.42 s, ζ 0.65–0.85**; collapse **T 0.35–0.45 s, ζ 0.90–1.00**; hover **T 0.24–0.38 s, ζ 0.70–0.90**; press **T 0.18–0.26 s, ζ 0.60–0.75**. Everyone independently lands in the same box — that is the strongest available evidence for the tokens in §12.

### 8.3 Settling time (for acceptance tests)

For an underdamped spring, the 2 % envelope is `t₂% ≈ 3.91 / (ζω) = 0.622 · T / ζ`. For ζ ≥ 0.99 use the critical form, `ωt ≈ 5.83` → `t₂% ≈ 0.93 · T`.

| Token | T | ζ | t₂% (predicted) |
|---|---|---|---|
| expand 0.36 / 0.82 | 0.36 | 0.82 | **0.27 s** |
| collapse 0.30 / 1.00 | 0.30 | 1.00 | **0.28 s** |
| hover pop 0.24 / 0.70 | 0.24 | 0.70 | 0.21 s |
| tab 0.34 / 0.92 | 0.34 | 0.92 | 0.23 s |
| press 0.18 / 0.65 | 0.18 | 0.65 | 0.17 s |
| drop-in toast 0.32 / 0.58 | 0.32 | 0.58 | 0.34 s (with ~1 overshoot) |

Wrap these in tests: step the spring until `|x−target| < 0.5 DIP` and assert the frame count lands within ±20 % of the prediction. That prevents a future "make it snappier" tweak from silently doubling the duration.

### 8.4 Recommended motion tokens (authored as `(T, ζ)`)

| Token | T (s) | ζ | k = (2π/T)² | c = 4πζ/T | Use |
|---|---|---|---|---|---|
| `motion.expand` | **0.36** | **0.82** | 304.6 | 28.6 | small → large width **and** height (height lags: T × 1.08) |
| `motion.collapse` | **0.30** | **1.00** | 438.6 | 41.9 | large → small |
| `motion.morph` | 0.28 | 0.85 | 503.6 | 38.2 | content swap inside a settled island |
| `motion.hoverPop` | 0.24 | 0.70 | 685.4 | 36.7 | 1.0 → 1.02 scale on hover |
| `motion.press` | 0.18 | 0.65 | 1217.8 | 45.4 | press-in scale 0.97 |
| `motion.tab` | 0.34 | 0.92 | 341.5 | 34.0 | horizontal page spring |
| `motion.nudge` | 0.26 | 0.55 | 584.2 | 26.6 | −4 DIP vertical kick on new event |
| `motion.hide` | 0.30 | 0.95 | 438.6 | 39.8 | slide/fade out |

Derived rules:
- **Height trails width by ~8 %.** Never drive both from one progress value; the Windhawk mod has independent `widthSpring`/`heightSpring` for exactly this reason.
- **Expand is bouncier than collapse** (ζ 0.82 vs 1.00). Reversing that reads as "sticky".
- **Content opacity is derived from container size**, not animated: `alpha = clamp((h - 0.62·h_large) / (0.35·h_large), 0, 1)` (generalised from the mod's `(h−60)/60`). Guarantees no text inside a too-small pill.
- **Interrupt policy:** on retarget mid-flight, keep 100 % of velocity if the direction is unchanged, else `v ← v · 0.35` and clamp `|v| ≤ 0.2 · |Δ| / dt` (WinIsland's `redirect_velocity_towards`: momentum retention 0.35, max distance ratio 0.2). `[VERIFIED constants]`

### 8.5 Time-based durations (non-spring)

| Token | ms | Curve | Use |
|---|---|---|---|
| `dur.fadeIn` | 180 | `cubic-bezier(.2,0,0,1)` | content appearing |
| `dur.fadeOut` | 120 | `cubic-bezier(.4,0,1,1)` | content leaving |
| `dur.crossFade` | 220 | linear | one card → another |
| `dur.stagger` | 32 | — | per-child delay (mod: 36; mchack: 36) |
| `dur.hoverIntent` | 90 | — | delay before hover-expand fires |
| `dur.pressFeedback` | 90 | — | down state |
| `dur.peekDwell` | 2400 | — | expanded then auto-collapse (mchack `beat` ≤ 2200) |
| `dur.toastHold` | 3200 | — | transient card visible (dock 4000) |
| `dur.spin` | 600 | `cubic-bezier(.2,0,0,1)` | album-cover flip/spin on track change |
| **`dur.max`** | **2000** | — | **Apple's hard ceiling for Live Activity animation** |

### 8.6 Frame pacing to 60–120 fps

| Target | Frame budget | Notes |
|---|---|---|
| 60 Hz | 16.67 ms | floor; anything below this feels broken on a desktop |
| 90 Hz | 11.11 ms | minimum "premium" target for FluidBar 2.0 |
| 120 Hz | **8.33 ms** | recommended cap; keep managed work < 4 ms |
| 144/165/240 | 6.9/6.1/4.2 ms | only if the renderer is composition-based; otherwise stop at 120 and use blur |

Implementation rules (platform-neutral, then WPF-specific in §12.4):

1. **Integrate with a fixed substep** (`0.5 ms`, as the mod does) or with the closed form (mchack). Never raw-Euler a spring with a 33 ms dt — it explodes.
2. **Clamp dt** to `[1 ms, 50 ms]` and after a resume-from-sleep snap springs to target (`Reset`), never animate from a stale value.
3. **Suspend when at rest.** `allRest()` / `settle(eps)` → stop the frame loop entirely. The mod parks its render thread when hidden; WinIsland returns `Wait` instead of `WaitUntil` when idle.
4. **Request 1 ms timer resolution only while animating**, release ~0.5 s after motion stops (mod: `kHighResTimerHoldSec = 0.5`, balanced `timeBeginPeriod`/`timeEndPeriod`).
5. **Velocity-driven motion blur beats extra frames** below 120 Hz: `σ_h = min(|v_w|·0.3 + |v_view|·w·0.4, cap)`, `cap = 12 px` on discrete GPUs, `4 px` on integrated (WinIsland). Ship it behind a setting, default on for integrated GPUs off.
6. **Never animate layout properties at frame rate.** Animate transform/opacity/geometry only (see §12.4).
7. **Measure**: log p99 frame time and the count of frames where dt > 1.5 × target; a rewrite that hits 120 fps in the lab and 45 fps on a 4K laptop is a regression.

---

## 9. Interaction patterns observed

| Pattern | Who does it | Recommendation |
|---|---|---|
| **Hover expands** | Windhawk (`ExpandOnHover: true` default), WinIsland, Atoll (`hover behaviour` settings), boring.notch | ✅ default on, with a **90 ms hover-intent delay** and a **collapse delay ~400 ms** so a pointer crossing the pill doesn't flap it. Provide a "click to expand" mode for people who hate hover-expansion. |
| **Click expands / click-through when idle** | Windhawk (policy at line 23001) | ✅ exactly this policy: idle + not hovered + not pinned ⇒ `WS_EX_TRANSPARENT`. |
| **Scroll to change page** | Windhawk (media/calendar/weather/hardware/tray/lyrics in a scroll loop), WinIsland (pager) | ✅ but **accumulate wheel deltas** into a spring target (the mod's `tabPos_` is a continuous spring value) instead of paging on each notch — that is what makes it feel like a carousel. |
| **Drag to reposition** | Windhawk (offset settings + drag), WinIsland (`position_x/y_offset`), macOS apps | ✅ with rubber-banding (`1 − 1/(|d|·0.55/dim + 1)`) and snapping to the 6 anchor positions; persist per-monitor. |
| **Long-press / touch-and-hold → expanded** | Apple (system behaviour), mchack (long-press preview) | ⚠️ desktop: long-press is a touch idiom; support it only when a touch digitiser is present. |
| **Right-click context menu** | Windhawk (theme, transparency, pin, timer, lyrics) | ✅ but the menu must only hold **session** toggles; persisted settings live in Settings (§2.7 anti-pattern). |
| **Double-click to open app** | common | ✅ "tap opens the related screen" is Apple's rule; map double-click → open the source app. |
| **Esc / click-outside to dismiss** | WinIsland, mchack | ✅ click-outside dismiss must be implemented as *focus loss* detection, not a full-screen transparent window (which steals clicks). |
| **Pin open** | Windhawk (`pinned`) | ✅ useful for dashboards; must suppress auto-collapse and click-through. |
| **Auto-hide on idle / fullscreen** | Windhawk (both), WinIsland (`auto_hide`, `fullscreen_auto_hide`, 5 s), Atoll | ✅ fullscreen suppression is mandatory — a pill over a game or a video is a bug report. Detect via `SHQueryUserNotificationState` / fullscreen-window heuristics, and add a per-app exemption list. |
| **Don't steal focus** | Windhawk (`WS_EX_NOACTIVATE`), all macOS panels | ✅ and drop `NOACTIVATE` only while a text field is focused. |
| **Never out-rank shell surfaces** | Windhawk's `IsShellOwnedWindow` | ✅ hard requirement: Start, Action Center, taskbar, Task View, popups and tooltips must win. |
| **Hover reveals hidden island** | Windhawk (`UnhideOnHover`) | ✅ but only if the hidden pill has a 8–12 px hover strip; otherwise the island becomes un-findable. |
| **Keyboard shortcut to toggle** | Windhawk (Ctrl+Alt+D default; Ctrl+Alt+Space lookup) | ✅; make the hide shortcut the *only* guaranteed way back and document that. |
| **Peek on track change** | Windhawk (`MediaAutoExpand: false` default), WinIsland | ✅ default **off**, with a per-app blocklist (`chrome, tiktok, youtube`) — short-form video feeds otherwise pop the island constantly. |
| **Pointer affordances** | — | hand cursor only on genuinely clickable rows; the pill body should stay arrow so drag feels natural. |
| **Reduced motion** | mchack `CALM` (amp → 0.10), macOS "Reduce motion" | ✅ map to `ζ → 1.0`, amplitude → 0, durations ×0.5, disable bloom/nudge/spin. Also honour Windows "Show animations in Windows" (`SPI_GETCLIENTAREAANIMATION`). |

---

## 10. Plugin / marketplace patterns

### 10.1 WinIsland's model (the reference implementation) `[VERIFIED, ARCHITECTURE.md]`

- **Trusted in-process DLLs**, loaded via `libloading`. Not sandboxed — the docs say "trusted".
- Each plugin exports `winisland_plugin_entry_v2()` returning a `PluginDescriptorV2` `{ metadata, capabilities, create, shutdown, destroy, on_tick? }`.
- The host issues a **token** and an instance-owned `PluginHostV2` table; `query_interface` exposes **11 capability-gated tables**: Context, Media, I18n, HostState, Widget, LyricsTransform, Settings, Text, Image, Store, Log.
- **Ownership is per-token**: host services validate capability, ownership, generation and quotas; `shutdown` revokes the token's resources.
- **No plugin callback runs on the render thread.** Widget plugins run on worker threads and submit a **complete draw list**; the host validates the *entire* list, resolves owned images, and prepares immutable commands before rendering. Invalid lists are rejected; repeatedly malformed frames can disable the widget.
- **Lifecycle**: unload joins the host worker → `shutdown` → `destroy` → unload DLL. A failed shutdown (including partial `create` cleanup) **keeps the DLL and tables allocated until process exit** so plugin threads can finish — a deliberately leaky-but-safe choice.
- **Crash containment is weak by design**: release builds use `panic = "abort"`, so a panic in an `extern "C"` callback kills the process. Mitigation: the host writes an **active-plugin marker** before callbacks; on next start it disables the plugins named by leftover markers and reports them.
- **Packaging**: ZIP + manifest, `abi-version: 2` required (no v1 runtime compatibility path), DLL descriptor metadata validated, **ed25519 signature**, SHA-2 hashing, staging with **backup/rollback** activation. Marketplace catalogue lives in `winisland-plugin-package` behind a `marketplace` feature flag.
- **Settings**: a `#[derive(Settings)]` macro with `number(min,max,step,precision)`, `toggle`, `choice(value => i18n_key)`, `label` — **the settings UI is generated from the config struct**, and plugin settings pages reuse it (`plugin_settings.rs`).
- **i18n**: `winisland-core/i18n` catalogue + **plugin translation bundles**, so a plugin ships its own strings.

### 10.2 What FluidBar should do (recommendation)

FluidBar already has `Plugins/` with `catalog.json`, per-plugin settings classes, and real plugins (Media, Clipboard, Lyrics providers, AgentStatus). The rewrite should formalise, not reinvent:

1. **Stay managed/in-proc for v1.** `AssemblyLoadContext` per plugin (collectible), `[PluginManifest]` attribute or `plugin.json` with `id`, `name`, `version`, `apiVersion`, `capabilities[]`, `minHostVersion`. Native ABI (WinIsland-style) buys crash isolation at the cost of a C ABI, a draw protocol, and cross-language SDKs — not worth it before v3.
2. **Versioned contract + hard gate.** `apiVersion` major must match; refuse to load otherwise and surface a reason in the UI (WinIsland's "no v1 compatibility path" is the right call — silent best-effort loading is how plugin ecosystems rot).
3. **Capability-gated services**, not a god-object. At minimum: `IIslandHost` (state snapshot, show/expand/hide), `IMediaService`, `ISettingsService`, `II18n`, `IIconService`, `ILog`, `INotificationSink`. Each capability declared in the manifest and checked at resolve time.
4. **Never let a plugin touch the UI thread's visual tree.** Plugins return a **view model / declarative card description** (or a `DataTemplate` key built from a registered template), and the host renders it. This mirrors WinIsland's validated draw lists and prevents a plugin from blocking the render loop.
5. **Timeout every plugin callback** (50 ms for tick, 200 ms for data fetch) and quarantine after N timeouts. Log + disable, never crash the island.
6. **Signed catalogue, hash-pinned.** `catalog.json` entries carry `id`, `version`, `sha256`, `minHostVersion`, `downloadUrl`, `signature`. Verify on install; keep a rollback copy of the previous version.
7. **Settings UI generated from the settings model** (WinIsland's `settings-schema`). FluidBar's `Settings.cs` is already a single model — annotate it (`[Range]`, `[Toggle]`, `[Choice]`) and generate the page. This is the single biggest UI-code saving available in the rewrite.
8. **Plugin i18n bundles** (`.resx` or JSON per plugin) merged into the host catalogue, with the host language as fallback.
9. **Marketplace = catalogue + signing + version gating + one-click install/rollback.** Do not build payments; build trust primitives.

---

## 11. Licence and incompatibility warnings

### 11.0 FluidBar's own licence — read this first

FluidBar is **MIT** (`D:\build\GitLocal\FluidBar\LICENSE`, "Copyright (c) 2026 Doulor"). MIT and GPL-3.0 are **one-way compatible**: MIT code may be taken *into* a GPL project, but GPL code may not be taken into an MIT project unless the whole work is relicensed to GPL-3.0 and every distributor accepts those terms. Since FluidBar is MIT and presumably intends to stay permissively licensed, **every GPL-3.0 repo in this document (WinIsland, boring.notch, Atoll, livebridge) is read-only reference material: no code, no assets, no string tables, no copy-paste-then-rename.** Reimplementing a *documented technique* from a spec is fine; translating their source is a derivative work.

### 11.1 Licence matrix

| Project | Licence | Copy code? | Copy assets? | Notes |
|---|---|---|---|---|
| `devcode90/Dynamic-Island-for-Windows` | **MIT** | ✅ with notice | ⚠️ preview PNGs contain third-party album art / app icons — do **not** ship | Windhawk runtime calls must be stripped; single 23.6k-line file |
| `ng1145/dynamic-island-mchack` | **MIT** | ✅ with notice | ❌ icons/avatars "provided by the user; confirm your own rights" (README) | Android-only APIs |
| `WinIslandProject/WinIsland` | **GPL-3.0** | ❌ | ❌ | Ideas/architecture only; GPL is viral for derived code |
| `TheBoredTeam/boring.notch` | **GPL-3.0** | ❌ | ❌ | also bundles `mediaremote-adapter` (separate licence) and `THIRD_PARTY_LICENSES` |
| `Ebullioscopic/Atoll` | **GPL-3.0** | ❌ | ❌ | `[INFERRED]` derivative of boring.notch → **double** GPL obligation |
| `Lakr233/NotchDrop` | **MIT** | ✅ | ⚠️ check per-asset | small enough to read fully |
| `appsfolder/livebridge` | **GPL-3.0** | ❌ | ❌ | — |
| Apple SF Symbols | Apple SF Symbols licence | ❌ redistribution | ❌ **and** no use in icons/logos/"confusingly similar" | see §4.9 |
| Apple Design Resources templates | Apple licence | ❌ | ❌ | reference-only |
| Apple HIG text/numbers | © Apple | — | — | facts are not copyrightable; **quoted prose is** — paraphrase, cite |

### 11.2 Hard incompatibilities (technical, not legal)

| Technique | Why it does not port to a WPF/.NET island |
|---|---|
| Windhawk mod as a whole | `@include windhawk.exe` + `Wh_*` runtime; C++23; Direct2D; no XAML, no composition tree, no managed threading |
| `UpdateLayeredWindowIndirect` per frame | available via P/Invoke but incompatible with WPF's own `HwndSource` composition; doing both produces flicker. Choose one presentation path. |
| `timeBeginPeriod(1)` | works, but it is a **process-wide** power cost; only justified if the renderer is genuinely frame-paced |
| WinIsland's D3D12 + Skia + DirectComposition pipeline | a full native renderer; a rewrite in WPF cannot host it without a `SwapChainPanel`/`D3DImage` interop layer (a project in itself) |
| mchack's `RenderEffect` goo/blur | Android 12+ only |
| macOS `NSPanel` + notch-flush layout | no notch; Windows needs a floating, repositionable pill |
| `ContainerRelativeShape` (concentric radii) | no WPF equivalent — compute `innerRadius = outerRadius − inset` manually |

### 11.3 Trademark / trade-dress warnings

- **"Dynamic Island" is Apple's term.** Nominative use in documentation ("inspired by…") is generally defensible; using it as FluidBar's product name, in the logo, or in the installer's branding is not. `[UNCERTAIN — get counsel before shipping a name]`
- Do not reproduce the island silhouette *as a logo*, and do not ship SF Symbols or "confusingly similar" glyphs in the icon (§4.9).
- Do not copy preview screenshots from any repo into FluidBar's store listing — they contain third-party album art and app icons.

---

## 12. Recommended FluidBar design tokens

Authoring format: **DIP @ 96 DPI**, dark theme default. Every value below is either Apple-published, measured from a reference, or derived from §8's consensus; the derivation is stated.

### 12.1 Geometry

| Token | Value | Derivation |
|---|---|---|
| `island.height.small` | **36** | Apple compact 36.67 → 36 for pixel-grid alignment |
| `island.height.media` | **40** | needs a 28 px artwork + 6 px inset top/bottom |
| `island.height.large` | **152** | Apple 84–160; Windhawk 184; WinIsland 200 → 152 keeps 3 rows + header |
| `island.height.large.min` | 96 | one-row dashboard |
| `island.height.large.max` | 200 | 4-row dashboard; hard cap |
| `island.width.idle` | **120** | measured content, mod 96/170, WinIsland 120 |
| `island.width.idle.min/max` | 96 / 280 | mod's `kMinWidth`/`kMaxWidth` |
| `island.width.media` | **168** | 40 art + 10 gap + title/artist + 28 controls |
| `island.width.small.max` | 300 | transient card cap |
| `island.width.large` | **380** | mod 380, WinIsland 360, Apple 371/408 |
| `island.width.large.compact` | 340 | with the lyrics side-panel off |
| `island.width.large.max` | 420 | with side panel on |
| `island.width.quantum` | **2** | round measured widths up; prevents per-frame HWND resizes |
| `radius.small` | `h/2` (18 at h=36; 20 at h=40), clamp ≤ 44 | stadium |
| `radius.large` | **28** | squircle at 380×152 → k = 2.77 |
| `radius.card` | **16** | concentric rule: `inner = outer − inset`; outer 28, card inset 12 → 16 |
| `radius.button` | **10** (icon) / **12** (pill button) | hit 32×32 |
| `radius.art.small` | **8** | 28 px art |
| `radius.art.large` | **12** | 64 px art |
| `radius.notchStyle` | 16 | opt-in flush mode |
| `radius.win11Style` | 8 | opt-in fluent box |
| `squircle.k` | `2 + (1 − r/m)·1.22` | mchack; k=2 at a stadium |
| `space.inset.small` | **12** | |
| `space.inset.large` | **20** | |
| `space.gap` | 4 | stacked text |
| `space.gap.md` | 10 | art ↔ text |
| `space.gap.lg` | 16 | clusters |
| `space.section` | 24 | dashboard sections |
| `space.row` | 14 | Apple's published Lock-Screen margin |
| `hit.min` | **32** | desktop pointer (transport buttons 36) |
| `shadow` | offset `0 8`, blur 24, `#000` @ 35 % | soft downward only |
| `stroke` | 1 DIP `#1E1E22` | key line |
| `backdropPadding` | 0 (small) / 0 (large) | do **not** pad the pill inside its window; pad the window instead |

### 12.2 Motion

| Token | T (s) | ζ | k | c |
|---|---|---|---|---|
| `motion.expand` | **0.36** | **0.82** | 305 | 28.6 |
| `motion.expand.height` | 0.39 | 0.86 | 260 | 27.7 |
| `motion.collapse` | **0.30** | **1.00** | 439 | 41.9 |
| `motion.morph` | 0.28 | 0.85 | 504 | 38.2 |
| `motion.hoverPop` | 0.24 | 0.70 | 685 | 36.7 |
| `motion.press` | 0.18 | 0.65 | 1218 | 45.4 |
| `motion.tab` | 0.34 | 0.92 | 342 | 34.0 |
| `motion.nudge` | 0.26 | 0.55 | 584 | 26.6 |
| `motion.hide` | 0.30 | 0.95 | 439 | 39.8 |

Durations: `fadeIn 180`, `fadeOut 120`, `crossFade 220`, `stagger 32`, `hoverIntent 90`, `pressFeedback 90`, `peekDwell 2400`, `toastHold 3200`, `coverSpin 600`, `maxAnimation 2000` (Apple).
Easings: enter `cubic-bezier(.2,0,0,1)`, exit `cubic-bezier(.4,0,1,1)`, move `cubic-bezier(.32,.72,0,1)`, overshoot `cubic-bezier(.16,1.4,.3,1)`.
Reduced motion: ζ → 1, nudge amplitude → 0, bloom → 0, durations × 0.5, spin off.
Scale-on-press: 0.97; hover: 1.02 (nothing larger — a 36 px pill at 1.06 looks like it's breathing at you).

### 12.3 Colour / type tokens

Dark: `bg #08080A`, `bg.raised #141418`, `stroke #1E1E22`, `fg #FFFFFF`, `fg.secondary #9B9BA5` (7.2:1), `fg.tertiary #6E6E78`, `accent` = artwork or `#4CC9F0`, `success #34C759`, `warning #FF9500`, `danger #FF453A`, `info #0A84FF`.
Light: `bg #F5F6F8`, `bg.raised #FFFFFF`, `stroke #D8DBE1`, `fg #14161A`, `fg.secondary #5B6270` (5.9:1).
Fill opacity default **96 %**, min 85 % for text-bearing states. No rim light.

Type: `body 13.5 SemiBold / 11 Normal`, `clock 18 Bold`, `idleClock 13 SemiBold`, `hero 42 Bold`, `dashboardTime 56 Bold`, `dashboardDate 16 SemiBold`, `icon 16/18/14` — family `Segoe UI Variable Display` / `Small`, fallback `Segoe UI`, icons `Segoe Fluent Icons` → `Segoe MDL2 Assets`.

### 12.4 Implementation notes specific to WPF/.NET 10

These are engineering opinions, marked as such, aimed at the single biggest risk in the rewrite: **WPF's layout system is not a 120 Hz renderer.**

1. **Do not animate `Width`/`Height`/`Margin`.** Each change invalidates measure → arrange → render for the whole island. At 120 Hz that is 3 full layout passes per 25 ms. Instead:
   - Give the window a **fixed maximum size** (e.g. 440 × 216 at 100 % scale, plus a 24 DIP shadow margin) and never resize the HWND during ordinary interaction.
   - Draw the island body as a **custom `FrameworkElement`** (`OnRender` with a `StreamGeometry` squircle) whose geometry is driven by two spring values. `OnRender` is cheap; layout is not.
   - Update the **window region** (`SetWindowRgn`) only when the pill settles, not per frame — the region is for click-through correctness, and the transparent margins are already alpha-0.
   - If the HWND must resize per frame (e.g. for a top-flush style), do it on a **4-frame cadence** and let the geometry catch up.
2. **Hit-testing must match the drawn shape.** WPF hit-tests on rendered geometry, so a custom element with a `StreamGeometry` fill already gives you a shaped hit area — but only if the surrounding `Grid`/`Border` does not have a transparent `Background` (a `Transparent` brush is hit-testable; `null` is not). Audit every container.
3. **Click-through**: WPF has no first-class per-region transparency. Use `HwndSourceHook` on `WM_NCHITTEST` → return `HTTRANSPARENT` when the point is outside the island geometry and the island is idle-unhovered. That is the WPF-idiomatic equivalent of the mod's `WS_EX_TRANSPARENT` policy, and it is per-message rather than per-style, so it cannot get stuck in the wrong state.
4. **`WS_EX_NOACTIVATE` + `WS_EX_TOOLWINDOW`** on the `HwndSource` parameters (`WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`), and a `ShowActivated="False"` window. Drop `NOACTIVATE` while a `TextBox` needs focus, exactly as the mod does.
5. **Topmost**: set `Topmost="True"` but also re-assert with `SetWindowPos(HWND_TOPMOST, SWP_NOACTIVATE)` on a low-frequency timer, *after* checking the foreground window's class against the shell whitelist (§2.5). Never fight the Start menu.
6. **DPI**: `PerMonitorV2` in the manifest + `WM_DPICHANGED` handler; recompute the layout scale and re-measure, do not keep a global scale factor.
7. **Timing**: drive springs from `CompositionTarget.Rendering` (it is vsync-aligned) with a `Stopwatch` dt, not a `DispatcherTimer`. Stop the subscription when `allRest()` — WPF will otherwise keep the render thread at 60 Hz forever, which is exactly the battery complaint every WPF island gets.
8. **Text**: `TextOptions.TextFormattingMode="Ideal"` + `TextRenderingMode="ClearType"` on opaque backgrounds, `"Grayscale"` over acrylic/blur. Cache `FormattedText`/`GlyphRun` per string — re-laying out text every frame is the second-most-common WPF island performance bug.
9. **Plugins**: keep the current in-process model, but add an `AssemblyLoadContext` per plugin so unload is possible, and hard-gate on `apiVersion` (§10.2).
10. **Blur**: WPF's `BlurEffect` on a live `VisualBrush` is a GPU readback path and will cost more than the whole animation budget. If blur is needed, capture once on state change into a `RenderTargetBitmap`/`WriteableBitmap`, blur that, and keep it until the state changes — or use a native acrylic/DWM backdrop behind the window (the mod's `BackdropMaterial` route).

---

## 13. Open questions and next measurements

| # | Question | Why it matters | Suggested action |
|---|---|---|---|
| 1 | Is FluidBar's target truly "floating pill" or "top-flush bar"? | Determines radius, shadow, hit-region strategy and whether a notch style is even needed | Product decision; recommend floating pill + optional top-flush mode |
| 2 | What is the real per-frame cost of a custom `OnRender` island at 120 Hz on an integrated GPU? | Decides whether the rewrite needs a composition/D3D interop path | Prototype: 380×152 squircle + 6 text runs + 1 bitmap, measure p99 frame time on the dev machine and on a low-end laptop |
| 3 | Does FluidBar relicensing allow GPL inspiration to be *reimplemented*? | Ideas are not copyrightable; expressions are. Reimplementation from a spec is fine, translation of code is not | Keep a written clean-room note per borrowed technique |
| 4 | Are the 4 shipped theme palettes enough? | The mod ships 8, 4 of which are < 40 % distinct in practice | Ship 4 + custom hex |
| 5 | Which Windows fullscreen detection actually works for borderless games? | Mis-detection is the #1 user-visible bug class | Test `SHQueryUserNotificationState` + `GetForegroundWindow` + `DwmGetWindowAttribute(DWMWA_CLOAKED)` on: DX11 fullscreen, borderless, DWM MPO video, PowerPoint slideshow |
| 6 | Plugin crash isolation: in-proc with timeouts, or out-of-proc host? | Out-of-proc doubles IPC cost for per-frame widget data | Defer; measure a 60 Hz widget provider through named pipes first |
| 7 | Apple's expanded corner radius | Visual fidelity | Measure a 3× screenshot of an expanded Live Activity if a device is available; otherwise ship 28 and validate by eye |
| 8 | Does the limited-width (28 pt circle) rule apply to a desktop app? | Only relevant for a landscape/compact mode | Adopt as a "micro pill" state at ≤ 28 DIP height |

---

## Appendix A — Source ledger

**Repositories**

- https://github.com/devcode90/Dynamic-Island-for-Windows — MIT; mod file `dynamic-island-for-windows.wh.cpp` (23,669 lines); README, LICENSE, CHANGELOG
- https://github.com/ng1145/dynamic-island-mchack — MIT; `motion/`, `island/Geometry.kt`, `island/Squircle.kt`, `core/Springs.kt`, `README.md`
- https://github.com/WinIslandProject/WinIsland — GPL-3.0; `.agents/ARCHITECTURE.md`, `crates/winisland-core/src/{physics.rs,config/mod.rs}`, `src/window/app/frame.rs`, `src/utils/blur.rs`, `Cargo.toml`
- https://github.com/TheBoredTeam/boring.notch — GPL-3.0; `boringNotch/ContentView.swift`
- https://github.com/Ebullioscopic/Atoll — GPL-3.0; `ReadMe.md`, `DynamicIsland/ContentView.swift`
- https://github.com/Lakr233/NotchDrop — MIT (metrics via API)
- https://github.com/appsfolder/livebridge — GPL-3.0 (metrics via API)

**Apple**

- HIG Live Activities — https://developer.apple.com/design/human-interface-guidelines/live-activities
- HIG Live Activities, machine-readable (dimensions + change log) — https://developer.apple.com/tutorials/data/design/human-interface-guidelines/live-activities.json
- HIG SF Symbols — https://developer.apple.com/design/human-interface-guidelines/sf-symbols
- Apple Design Resources — https://developer.apple.com/design/resources/
- SF Symbols — https://developer.apple.com/sf-symbols/
- `DynamicIslandMode` — https://developer.apple.com/documentation/widgetkit/dynamicislandmode
- `isDynamicIslandLimitedInWidth` — https://developer.apple.com/documentation/swiftui/environmentvalues/isdynamicislandlimitedinwidth
- WidgetKit animation guidance — https://developer.apple.com/documentation/widgetkit/animating-data-updates-in-widgets-and-live-activities
- WWDC26-223 — https://developer.apple.com/videos/play/wwdc2026/223
- WWDC25-278 — https://developer.apple.com/videos/play/wwdc2025/278
- WWDC25-216 — https://developer.apple.com/videos/play/wwdc2025/216

**Commands used (reproducible)**

```powershell
New-Item -ItemType Directory -Force "$env:TEMP\fluidbar-ref"
git clone --depth 1 https://github.com/ng1145/dynamic-island-mchack        "$env:TEMP\fluidbar-ref\dynamic-island-mchack"
git clone --depth 1 https://github.com/devcode90/Dynamic-Island-for-Windows "$env:TEMP\fluidbar-ref\Dynamic-Island-for-Windows"
git clone --depth 1 https://github.com/Ebullioscopic/Atoll                  "$env:TEMP\fluidbar-ref\Atoll"
git clone --depth 1 https://github.com/WinIslandProject/WinIsland           "$env:TEMP\fluidbar-ref\WinIsland"
git clone --depth 1 https://github.com/TheBoredTeam/boring.notch            "$env:TEMP\fluidbar-ref\boring.notch"
```

**Key file → claim map (for verification)**

| Claim | File:line |
|---|---|
| 23,669 lines / 1,100,199 bytes | `dynamic-island-for-windows.wh.cpp` (`Get-Content \| Measure-Object -Line`) |
| Window ex-styles | `…wh.cpp:22324` |
| Dynamic click-through policy | `…wh.cpp:23001`, `SetClickThrough` at `10499` |
| Shell-window whitelist + rationale | `…wh.cpp:3822–3839` |
| Rounded window region + nudge slack | `…wh.cpp:3781–3819` |
| DPI scale from primary monitor | `…wh.cpp:2680–2691`, `2769–2772` |
| Spring integrator (0.5 ms substep) | `…wh.cpp:1603–1629` |
| Expand/collapse stiffness+damping | `…wh.cpp:22930–22962` |
| Tab / lyrics springs | `…wh.cpp:15199`, `770–771` |
| Content alpha derived from height | `…wh.cpp:707` |
| Activity size table | `…wh.cpp:20498–20575` |
| Activity priority order | `…wh.cpp:20577–20627` |
| Media expanded 380×184 | `…wh.cpp:668–669` |
| Idle strip measured width + quantum | `…wh.cpp:822–845` |
| 1 ms timer scoped to animation | `…wh.cpp:22390–22431` |
| Theme palettes | `…wh.cpp:1056–1065` |
| DWrite type ramp | `…wh.cpp:11976–12050` |
| Adaptive font ladder | `…wh.cpp:13461–13466` |
| Spring presets (r, ζ) | `mchack/…/motion/Motion.kt:123–155` |
| Closed-form spring | `mchack/…/motion/Spring.kt:103–127` |
| Squircle k formula | `mchack/…/motion/Squircle.kt:54–97` |
| Velocity deformation | `mchack/…/island/Geometry.kt:144–190` |
| 20-spring set, idle 112×33 | `mchack/…/core/Springs.kt:23–108` |
| WinIsland defaults 120×27 / 360×200 | `WinIsland/crates/winisland-core/src/config/mod.rs:39–60` |
| WinIsland spring + redirect policy | `WinIsland/crates/winisland-core/src/physics.rs:13–54` |
| WinIsland spring constants | `WinIsland/src/window/app/frame.rs:1110–1129`, `918–925` |
| WinIsland motion-blur sigmas | `WinIsland/src/utils/blur.rs:1–30` |
| WinIsland plugin ABI / draw-list validation | `WinIsland/.agents/ARCHITECTURE.md` ("Plugin system") |
| Atoll springs | `Atoll/DynamicIsland/ContentView.swift:386,695,704–705` |
| boring.notch springs | `boring.notch/boringNotch/ContentView.swift:41,123–124` |
| FluidBar current sizes | `D:\build\GitLocal\FluidBar\MainWindow.xaml:41–44`, `224` |

---

## Appendix B — Uncertain / do-not-cite-as-fact list

1. Apple's island **corner radius** and **spring constants** — not published; the values above are inferred (§4.11).
2. Apple's **expanded content margins** — only the 14 pt **Lock Screen** margin is published.
3. Atoll being a **fork of boring.notch** — inferred from shared structure + merge-commit authorship, not stated in either README.
4. **NotchNook / Dynamic Lake** feature claims — from marketing copy only; nothing was inspected.
5. **SF Symbols for Windows** — the licence is scoped to Apple platforms; shipping the font in a Windows app is outside that scope. Legal review advised before relying on this reading.
6. WinIsland's `Path::continuous_rounded_rect` **implementation** — the symbol name was verified, the maths was not read.
7. Any claim that a specific **FPS above 120** improves perceived smoothness — not supported; the mod's 360/500 FPS options are unvalidated marketing.

