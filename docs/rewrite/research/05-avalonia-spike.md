# FluidBar v2 — Avalonia 12.1.4 / .NET 10 Windows Spike

**Date:** 2026-10-18  
**Host:** Windows 11 26200 (25H2), .NET SDK 10.0.301, runtime 10.0.9, NVIDIA RTX 5060 Laptop, 2560×1600 @ 165 Hz, system scale 150 % (144 DPI). One active display (`Screens.ScreenCount=1`); the ToDesk and GameViewer virtual adapters are present but have no active monitor.  
**Workspace:** `C:\Users\Doulor\AppData\Local\Temp\fluidbar-spikes\avalonia-window`  
**Repo:** `D:\build\GitLocal\FluidBar` was not modified.  
**Overall verdict:** **GO, with two conditions** — accept a simulated/tinted-glass pill (or window-sized acrylic), and complete secondary-monitor validation. Avalonia 12.1.4 is a viable host for the fixed-size island and its 165 Hz morph. Native *pill-shaped* acrylic is **NO-GO** with Avalonia's high-level transparency API. If pill-clipped acrylic or a hard no-tail 120 fps guarantee is a must-have, use the Win32 + Windows.UI.Composition fallback.

---

## Verdict by requirement

| # | Requirement | Verdict | Evidence |
|---|---|---|---|
| 1 | Builds/runs on `net10.0-windows` | **GO** | Clean build, 0 warnings/errors; Avalonia 12.1.4 / Desktop 12.1.4 / Fluent 12.1.4; runtime 10.0.9 |
| 2 | Borderless, topmost, no taskbar, non-activating, per-pixel transparent pill | **GO** | HWND ex-style `0x08200088`; wallpaper visible through transparent pixels; internal RGBA alpha 0 at corners, 255 in pill; anti-aliased edges; no black box with WinUI composition |
| 3 | 132×36 → 440×284 spring morph in fixed HWND, 120 fps on 165 Hz | **GO, caveat** | 0.362 s median morph; fixed rect in every sampled frame; 360 warm frames: p50 **6.05 ms**, p99 **7.76 ms**, mean **165.9 fps**; 4/360 frames > 8.33 ms; max 18.59 ms. UI-thread-driven RAF loop, not composition-driven layout |
| 4 | Click-through outside pill, clicks on pill, no focus steal, drag-by-pill | **GO** | `WM_NCHITTEST` returns `HTTRANSPARENT` outside and at rounded-corner cutouts, `HTCLIENT` on pill; `MA_NOACTIVATE`; foreground unchanged on show/click/drag; drag moved exactly (−120,−80) px |
| 5 | Transparent / AcrylicBlur / Mica, rounded-corner acrylic, Win32 composition modes | **NO-GO for native pill acrylic**; fallback GO | Transparent works. AcrylicBlur and Mica are achieved, but the backdrop covers the **whole fixed 720×480 window** as a rounded window (not the pill); DirectComposition has no acrylic; LowLatency is a black box; Redirection ~77 fps and no acrylic |
| 6 | Idle ~0 % CPU, no render loop, working set after 60 s, no per-frame allocations | **GO** | 62.02 s idle: 0 renders, **0.151 % of one core** (0.0047 % of 32 logical CPUs), +28.7 KB allocations, GC counts unchanged, WS **170.5 MiB** |
| 7 | Secondary monitor, per-monitor DPI, pixel-perfect placement | **NO-GO / unverified** | Only one active display. PMv2 verified on primary: 144 DPI, scaling 1.5, window at exact `1760,1060`, 720×480 physical, ext-frame equals client. Secondary-monitor test was impossible in this environment |
| 8 | Always-on-top policy vs. shell surfaces | **GO** | `WS_EX_TOPMOST`; foreground class check: Chrome → raise allowed, `Windows.UI.Core.CoreWindow` (Start) → raise suppressed; Start menu overlays the island in screenshot |

\* Requirement 3's numbers are render/RAF submission cadence, not an OS hardware-flip timestamp. See [Presented-frame caveat](#presented-frame-caveat).

---

## 1. Build and runtime

No Avalonia templates were installed. The project is a plain `dotnet new console` converted to:

- `TargetFramework=net10.0-windows`
- `OutputType=WinExe`
- `BuiltInComInteropSupport=true`
- `ApplicationManifest=app.manifest` with **PerMonitorV2** DPI awareness
- No XAML, no Avalonia templates; all UI is code.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <BuiltInComInteropSupport>true</BuiltInComInteropSupport>
    <ApplicationManifest>app.manifest</ApplicationManifest>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Avalonia" Version="12.1.4" />
    <PackageReference Include="Avalonia.Desktop" Version="12.1.4" />
    <PackageReference Include="Avalonia.Themes.Fluent" Version="12.1.4" />
  </ItemGroup>
</Project>
```

Verified resolved versions (see `evidence/packages.txt`):

| Package | Version |
|---|---|
| Avalonia / Avalonia.Desktop / Avalonia.Themes.Fluent | 12.1.4 |
| Avalonia.Win32 / Avalonia.Skia | 12.1.4 |
| SkiaSharp / SkiaSharp.NativeAssets.Win32 | 3.119.4 |
| Avalonia.Angle.Windows.Natives | 2.1.27548.20260419 |
| HarfBuzzSharp | 8.3.1.3 |

Build result:

```
已成功生成。
    0 个警告
    0 个错误
```

Runtime log: `runtime=10.0.9`, `avalonia=12.1.4.0`, output TFM `net10.0-windows`. The app ran from `bin\Debug\net10.0-windows\FluidBarSpike.exe`.

Relevant source: `Program.cs`, `App.cs`, `IslandWindow.cs`, `IslandView.cs`, `Native.cs`.

---

## 2. Window surface: borderless, topmost, no taskbar, transparent pill

### Observable HWND state

From the warm run (`evidence/run9.log`):

```
style=0x16000000 ex=0x08200088
topmost=True toolwindow=True noactivate=True
noredirectionbitmap=True layered=False
class=Avalonia-cc7d3039-...
```

Decoded:

- `WS_VISIBLE | WS_CLIPSIBLINGS | WS_CLIPCHILDREN`; **no caption/thick frame**.
- `WS_EX_TOPMOST` (always-on-top).
- `WS_EX_TOOLWINDOW` + `ShowInTaskbar=false` → no taskbar button / not in normal Alt-Tab.
- `WS_EX_NOACTIVATE`.
- `WS_EX_NOREDIRECTIONBITMAP` was added automatically by Avalonia's WinUI composition path. The HWND is **not layered**.

`Win32Properties.AddWindowStylesCallback(this, (style, ex) => (style, ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW))` is the exact Avalonia 12 hook used. `TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent }`, `Background=Brushes.Transparent`, `TransparencyBackgroundFallback=Brushes.Transparent`.

DWM checks:

```
DwmIsCompositionEnabled = 1
DWMWA_CLOAKED = 0
DWMWA_EXTENDED_FRAME_BOUNDS = (1760,1060)-(2480,1540) [720x480]
GetWindowRect                  = (1760,1060)-(2480,1540) [720x480]
```

### Visual proof

The transparent areas show the actual desktop/browser underneath; there is **no black box**. The pill itself is an opaque dark squircle in the normal state.

![Collapsed island over desktop](assets/spike-collapsed-screen.png)

![Expanded island over browser content](assets/spike-transparency-over-browser.png)

The in-app `RenderTargetBitmap` readback is even stronger than a screenshot:

- PNG is RGBA.
- Window corner pixel: `(0,0,0,0)` — alpha **0**.
- Pill interior: alpha **255**.
- Collapsed: 1,441 non-opaque pixels; expanded: 6,384 (edge AA plus the 3 px pseudo-shadow stroke); text antialiasing also contributes.
- Left edge alpha profile at the pill mid-line (expanded): `0, 0, 0, 4, 18, 54, 255, 255 ...` → three partial-alpha pixels before full opacity, then a hard 255 interior.
- Corners are smooth cubic curves, not a hard clipped rectangle.

![4× nearest-neighbour zoom of the top-left squircle corner](assets/corner-zoom-expanded.png)

The custom control overrides `public override void Render(DrawingContext context)` and draws a `StreamGeometry` squircle. This is Avalonia 12's render override; `OnRender(DrawingContext)` no longer exists with that signature. The stream is built with four cubic corners and `CubicBezierTo(..., isStroked: true)`.

Text uses subpixel antialiasing; the zoom shows colour fringing on glyphs. Geometry edges are clean. If that fringing is undesirable on a transparent surface, request grayscale AA for text.

---

## 3. Morph / frame loop / 120 fps

### What was animated

- Window is fixed at **480×320 DIP = 720×480 physical** at 150 %.
- Pill: **132×36 DIP → 440×284 DIP**.
- Center-anchored: collapsed `(174,18)`, expanded `(20,18)`.
- Spring: stiffness **550**, damping **38**, mass **1.0**, fixed 240 Hz sub-stepping, settle threshold 0.0015 / 0.03.
- Measured settle time (6 warm runs): **359.1–365.5 ms**, median **362.0 ms**. Target “~0.35 s” is met within ~3.4 %.

### Window bounds never change

Five morphs were sampled from the probe every ~4 ms. Every sample returned exactly `(1760,1060,720,480)`. The app's own `MORPH_START`/`MORPH_END` also show the same outer rect and only the `pillRectDip` changes.

### Cadence

Six warm morphs, no probe polling during the animation (to avoid perturbing the measurement), 360 intervals:

| Metric | Result |
|---|---|
| p50 interval | **6.054 ms** |
| p95 | 6.781 ms |
| p99 | **7.761 ms** |
| mean | 6.029 ms → **165.9 fps** |
| intervals > 8.33 ms | 4 / 360 = **1.11 %** |
| worst interval | 18.588 ms |
| per-run effective range | 161.5–168.3 fps |

A representative run is shown below; note the one 8.8 ms spike. The aggregate p99 is below the 120 fps budget, the tail occasionally misses a 165 Hz vsync.

![Frame intervals, 165 Hz vsync and 120 fps budget](assets/frame-cadence.png)

Earlier runs with a probe polling `GetWindowRect` at 4 ms and with cold JIT produced p99s of 12–25 ms. Those are not the steady-state numbers. A cold first animation had a first-frame delay of ~116–120 ms; pre-warm or ReadyToRun in production.

### Correct frame loop in Avalonia 12

**`TopLevel.RequestAnimationFrame(Action<TimeSpan>)` is the correct app-level per-frame hook.** Verified API:

```csharp
RequestAnimationFrame(OnAnimationFrame);

private void OnAnimationFrame(TimeSpan _)
{
    var now = Stopwatch.GetTimestamp();
    var dt = Math.Min(0.1, (now - _last) / (double)Stopwatch.Frequency);
    _last = now;

    if (!_island.Step(dt))
        return; // settled: do not request another frame

    RequestAnimationFrame(OnAnimationFrame);
}
```

Facts established by reflection/compile checks on 12.1.4:

- `Avalonia.Rendering.Composition.CompositionTarget` has **no public `Rendering` event** (unlike WPF).
- There is a public composition API: `ElementComposition.GetElementVisual(control)`, `visual.Compositor.CreateVector2KeyFrameAnimation()`, `StartAnimation("Size", anim)`. This **compiled** against 12.1.4. However, it animates a composition-visual property; it does not run layout or re-execute `Control.Render(DrawingContext)`. A `CompositionCustomVisual`/`OnAnimationFrameUpdate` path exists but is a low-level render-thread renderer, not a drop-in for this control. It was not productionized in this spike.
- Therefore the implemented morph is **UI-thread-driven**: each RAF callback advances the spring, invalidates the custom control, and Skia redraws. DWM/WinUI composition presents the result. It is *not* a GPU-composited layout animation.
- A `DispatcherTimer` at `DispatcherPriority.Render` is not display-synchronised and is not recommended over RAF.

**GPU-composited size animation without layout passes?** For a plain Avalonia `Control`, **no** — changing `Width`/`Height` or re-rendering the squircle requires layout/drawing work. A low-level composition visual can animate `Size`/`Offset`/`Scale` without a layout pass (API compile-verified), but its content is a pre-rendered visual; that is not equivalent to reflowing the pill text/waveform. If GPU-driven size and content are mandatory, that is a Win32 + Windows.UI.Composition custom-renderer decision, not a high-level Avalonia Control.

### Presented-frame caveat

The Stopwatch timestamps are taken inside `RequestAnimationFrame`, immediately before invalidating and submitting the frame. DWM composition is enabled, but Avalonia 12 exposes no public “frame presented” event or swap-chain feedback. The numbers above are **animation/render-submission cadence**, corroborated by a 165 Hz display configuration, not hardware flip timestamps. If the acceptance test requires a DWM/ETW present timestamp, that instrumentation remains to be added.

---

## 4. Click-through, focus, drag

### Mechanism

`Win32Properties.AddWndProcHookCallback(this, WndProcHook)` is the exact Avalonia 12 HWND subclassing mechanism. The callback signature is:

```csharp
IntPtr Hook(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
```

For `WM_NCHITTEST`:

1. Unpack screen coordinates from `lParam`.
2. `ScreenToClient(hwnd, ref pt)`.
3. Divide by `RenderScaling` to get DIPs.
4. Use `StreamGeometry.FillContains(dipPoint)` — i.e. the **actual rounded shape**, not the bounding box.
5. Return `HTCLIENT (1)` inside; `HTTRANSPARENT (-1)` outside.

For `WM_MOUSEACTIVATE`, return `MA_NOACTIVATE (3)`. `ShowActivated=false` and `WS_EX_NOACTIVATE` complete the non-activating policy.

Measured hit-test results (DPI-aware probe; see caveat below):

| State / point | Screen (physical) | Result |
|---|---|---|
| Collapsed center | 2120,1114 | `HTCLIENT` (1) |
| Collapsed outside pill | 1790,1090 | `HTTRANSPARENT` (−1) |
| Collapsed pill bbox corner (outside capsule) | 2024,1090 | `HTTRANSPARENT` (−1) |
| Expanded center | 2120,1300 | `HTCLIENT` (1) |
| Expanded pill bbox top-left (outside squircle) | 1796,1093 | `HTTRANSPARENT` (−1) |
| Expanded bottom center | 2120,1513 | `HTCLIENT` (1) |

The bbox-corner results prove that transparent corner cut-outs pass through too — this is genuine per-shape hit testing, not “the whole 720×480 HWND is a hit target”.

Real-click test: click on the pill toggled the morph; click outside the pill did not change the app state. Because no third-party witness window could be composited behind the topmost island in this session, end-to-end delivery to another app was not instrumented; `HTTRANSPARENT` is the Windows routing contract. `WindowFromPoint` is **not** a valid test here (it returns the Avalonia HWND even at `HTTRANSPARENT` points).

### Focus and drag

`GetForegroundWindow` before and after:

- initial `Show()`: Chrome, unchanged.
- click on pill: Chrome, unchanged; app `IsActive=false`.
- drag: Chrome, unchanged; app `IsActive=false`.

`IsActive` is not a safe proof by itself (it can be stale while another process holds the foreground). The foreground HWND/PID check is the authoritative result; `IsActive=false` is corroboration here.

Drag is implemented natively to avoid activation:

- `WM_LBUTTONDOWN` inside pill → `SetCapture(hwnd)`, record cursor + window rect.
- `WM_MOUSEMOVE` → `SetWindowPos(hwnd, HWND_TOPMOST, newX, newY, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE)`.
- `WM_LBUTTONUP` → `ReleaseCapture`; if movement < 3 px, treat as a click and toggle morph.

Measured drag: start `(1760,1060)`, delta `(−120,−80)`, end `(1640,980)` — exact pixel delta and size unchanged. The app remained inactive. `Window.BeginMoveDrag` was not used; it is a reasonable API but its modal/non-client move loop was not needed and can interact with activation.

### DPI-awareness pitfall

The probe **must be PerMonitorV2-aware**. A DPI-unaware PowerShell/Python probe sees virtualised window rectangles (480×320 at 280,80 instead of 720×480 at 420,120) and Windows may virtualise message coordinates, producing inverted hit-test results. Our first probe pass was wrong for exactly this reason. Always call `SetProcessDpiAwarenessContext(-4)` (or equivalent) before P/Invoke tests.

---

## 5. Acrylic / blur / composition modes

Tested with `TransparencyLevelHint` cycled at runtime; glass-probe drawing (semi-transparent pill) was used so blur inside the pill was visible.

| Mode | Transparency actual | Acrylic/Mica | Evidence |
|---|---|---|---|
| **WinUIComposition** (strict) | `Transparent` works; backdrop modes work | **AcrylicBlur** and **Mica** actual | Recommended; 165 fps; per-pixel alpha; no black box |
| DirectComposition (strict) | `Transparent` works | Requested AcrylicBlur → actual **Transparent**; no Mica/Acrylic | 148 fps cold; not sufficient for blur |
| LowLatencyDxgiSwapChain (strict) | `None` for acrylic; transparent request gives a **black rectangle** around the pill | none | Not viable |
| RedirectionSurface | `Transparent` works | Actual **None**; no acrylic/mica | ~77 fps; not viable for 120 fps |

### The acrylic result

`AcrylicBlur` and `Mica` **do** work on Windows 11 with WinUIComposition. But they apply to the **whole fixed 720×480 HWND**, not the pill. The screenshot below (glass pill probe) shows a blurred/tinted rectangle around the pill, with window-level rounded corners. `Win32PlatformOptions.WinUICompositionBackdropCornerRadius=24f` rounded the backdrop's window corners; it did **not** clip the blur to the pill squircle. The top-left window pixel remains the desktop because of the window-level 24 DIP corner radius.

![AcrylicBlur applied to the whole fixed window](assets/spike-acrylic-whole-window.png)

![Mica applied to the whole fixed window](assets/spike-mica-whole-window.png)

Outside-pill samples:

- Transparent: `(84,84,84)` wallpaper.
- AcrylicBlur: `(112,116,120)` (blurred/tinted whole window).
- Mica: `(31,34,24)` (window-wide tint).

**Conclusion — requirement 5 (rounded-corner acrylic) is NO-GO with the high-level Avalonia API.** You cannot keep a 720×480 fixed window and have native acrylic only inside the 660×426 pill. The options are:

1. **Recommended production fallback:** opaque/tinted simulated glass. Use the dark squircle with a subtle vertical tint, 1 px inner highlight, and a low-alpha edge; do not request AcrylicBlur/Mica. This preserves per-pixel transparency, 165 Hz morph, hit testing and drag. Visual fidelity is lower but deterministic and cheap.
2. If a real blurred material is mandatory: use a **native composition renderer** (Win32 + Windows.UI.Composition `DesktopAcrylicBackdrop` + rounded-rectangle clip, or equivalent) where the acrylic geometry can be clipped to the pill. Resizing the HWND to the pill on every frame would make acrylic “look right” but violates the fixed-window requirement and reintroduces a layout/window-resize loop.

LowLatency black box evidence:

![LowLatencyDxgiSwapChain has no per-pixel transparency](assets/spike-lowlatency-blackbox.png)

---

## 6. Idle cost

Measured over **62.02 s** on the expanded 440×284 pill, WinUIComposition, transparent, visible/topmost, input locked, no probe commands except start/end dumps:

| Metric | Result |
|---|---|
| Render count delta | **0** (start 789, end 789) |
| Process CPU | 0.09375 s over 62.02 s |
| CPU, one core | **0.151 %** |
| CPU, all 32 logical CPUs | **0.0047 %** |
| Allocated bytes delta | +28,696 B (~463 B/s, no frames) |
| GC0/1/2 delta | 0 / 0 / 0 |
| Working set at 60 s | **178,745,344 B = 170.5 MiB** |
| External working-set sample | 177,418,240 B = 169.2 MiB |
| Phase / active | `idle-expanded`, `IsActive=false` |

The app **stops its RAF loop at spring settle**; no timer or render loop remains. “CPU ~0%” passes. The 170 MiB working set is Debug-build Avalonia + Skia + JIT; it is stable over the sample but high for an always-on-top utility. A Release/ReadyToRun build and publish trimming should be measured before shipping.

---

## 7. Multi-monitor / DPI

PMv2 is working on the single active display:

```
Screens.ScreenCount = 1
screen bounds  = 0,0,2560,1600 (physical)
working area   = 0,0,2560,1600
scaling        = 1.5
dpi            = 144
process/thread awareness = 2 (PerMonitorV2)
```

- Window placed at exactly `1760,1060` physical, `720×480` physical.
- `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)` equals `GetWindowRect`.
- `RenderTargetBitmap` is 720×480 at 144 DPI; the screen crop is 720×480 pixels — no screenshot resampling.
- Custom-control geometry is rendered at physical resolution; the corner alpha profile is smooth and crisp.

**Secondary-monitor test: NO-GO / unverified in this environment.** There is only one active display. The two virtual display adapters have no active monitor. I did not change the display topology. Therefore I cannot honestly claim secondary-monitor placement or a 150 %→100 % DPI transition works. The code uses physical `SetWindowPos` and `RenderScaling`, and handles `WM_DPICHANGED` by logging/forcing a redraw, but this must be validated on a real dual-monitor machine.

Required follow-up before shipping:

1. Launch on monitor 2 with a different scale; assert `Screens.ScreenFromWindow` reports it and `GetDpiForWindow`/`RenderScaling` match.
2. Move across the boundary; verify `WM_DPICHANGED` keeps the 480×320 DIP size and the pill remains pixel-crisp.
3. Verify physical placement and that topmost/click-through survive the move.

---

## 8. Always-on-top policy vs shell surfaces

The window is genuinely topmost (`WS_EX_TOPMOST`). Shell surfaces still overlay it: the screenshot below was taken with the Start menu open, after the app suppressed its own raise. This is the desired behavior.

![Start menu overlay over the island](assets/spike-startmenu-open.png)

Policy implementation:

```csharp
var fg = GetForegroundWindow();
var cls = ClassNameOf(fg); // GetClassName
bool shell = cls is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd"
    or "Windows.UI.Core.CoreWindow"          // Start / shell UI
    or "XamlExplorerHostIslandWindow"
    or "StartMenuExperienceHost";
if (!shell)
    SetWindowPos(hwnd, HWND_TOPMOST, 0,0,0,0,
        SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
```

Observed log:

```
RAISE_POLICY ... fg=0x130FE0 class=Chrome_WidgetWin_1 raise=SetWindowPos(True)
RAISE_POLICY ... fg=0xA0588 class=Windows.UI.Core.CoreWindow raise=SKIPPED
  reason=shell/surface foreground; raise suppressed
```

`Shell_TrayWnd` was also observed as the foreground class after closing Start; it is in the same suppression list. **Pitfall:** these window classes are undocumented implementation details and can change between builds; in production also check the process name (`explorer.exe`, `StartMenuExperienceHost.exe`, `ShellExperienceHost.exe`) and/or the window's `GetWindowThreadProcessId` rather than relying only on class strings.

This policy applies to *explicit raises* (show, state change, drag). A topmost window is still above ordinary windows all the time. If the product must never obscure shell flyouts even transiently, lower to `HWND_NOTOPMOST` while a shell surface is foreground and restore on deactivation.

---

## Recommended architecture (GO path)

1. **Runtime:** Avalonia 12.1.4 on `net10.0-windows`, .NET 10, WinUIComposition + ANGLE, software fallback only as a last resort. Force WinUIComposition; do **not** ship LowLatency or Redirection for the island.
2. **Window:** one fixed-size non-resizable borderless HWND (480×320 DIPs for this design), `WindowDecorations=None`, `ShowActivated=false`, `ShowInTaskbar=false`, `Topmost=true`, transparent background, no titlebar.
3. **Styles:** add `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW` through `Win32Properties.AddWindowStylesCallback`; DWM `NCRENDERING` disabled/corner preference do-not-round; never layer the window manually.
4. **Input:** `Win32Properties.AddWndProcHookCallback`; `WM_MOUSEACTIVATE → MA_NOACTIVATE`; `WM_NCHITTEST` against the live squircle geometry; native drag with `SetCapture`/`SetWindowPos(... SWP_NOACTIVATE)`.
5. **Rendering:** custom `Control` overriding `Render(DrawingContext)`, drawing the squircle through a `StreamGeometry` at physical resolution. Keep the window fixed; animate only the pill's drawing geometry.
6. **Animation:** spring on the UI thread with a single `TopLevel.RequestAnimationFrame` chain; stop requesting frames at rest. Do not leave a timer or render loop at idle. Pre-warm the first frame / use ReadyToRun for production to avoid the 100 ms cold first frame.
7. **Glass:** ship simulated glass (opaque dark tint + highlight) or accept window-level acrylic. Do not promise pill-clipped acrylic with this stack.
8. **Z-order:** initial topmost is fine; gate every explicit raise on foreground class/process. Prefer to lower while shell UI is foreground if strict non-overlap is required.
9. **DPI:** physical-pixel placement via `SetWindowPos`, keep sizes in DIPs × `RenderScaling`, handle `WM_DPICHANGED`; test on the actual secondary monitor.

### Code path

| File | Role |
|---|---|
| `%TEMP%\fluidbar-spikes\avalonia-window\Program.cs` | TFM/bootstrap, composition options, tag/position/input-lock CLI |
| `App.cs` | FluentTheme, window creation, optional test backdrop |
| `IslandWindow.cs` | HWND styles, WndProc, DPI/DWM facts, spring RAF loop, hit test, drag, transparency/mode tests, diagnostics |
| `IslandView.cs` | Custom `Render(DrawingContext)` squircle, content, hit-test geometry |
| `Native.cs` | P/Invoke, message/style constants, DWM helpers |
| `BackdropWindow.cs` | Test-only checkerboard/focus witness; not for production |
| `artifacts/` | Logs, status JSON, screenshots, frame-interval JSON |

---

## Minimal reusable skeleton

The full working code is in the files above. This is the minimal shape, extracted from the APIs that compiled and ran in this spike. P/Invoke declarations are abbreviated; `Native` in the spike contains the full set.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <BuiltInComInteropSupport>true</BuiltInComInteropSupport>
    <ApplicationManifest>app.manifest</ApplicationManifest>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Avalonia" Version="12.1.4" />
    <PackageReference Include="Avalonia.Desktop" Version="12.1.4" />
    <PackageReference Include="Avalonia.Themes.Fluent" Version="12.1.4" />
  </ItemGroup>
</Project>
```

```csharp
// Program.cs
[STAThread]
static void Main(string[] args) =>
    AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .With(new Win32PlatformOptions
        {
            CompositionMode = new[] { Win32CompositionMode.WinUIComposition },
            RenderingMode = new[] { Win32RenderingMode.AngleEgl, Win32RenderingMode.Software },
            DpiAwareness = Win32DpiAwareness.PerMonitorDpiAware
            // This rounds the *window* backdrop, not the pill:
            // WinUICompositionBackdropCornerRadius = 24f
        })
        .LogToTrace()
        .StartWithClassicDesktopLifetime(args);

public sealed class App : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new IslandWindow();
            desktop.MainWindow.Show();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
```

```csharp
public sealed class IslandWindow : Window
{
    private readonly IslandView _island = new();
    private IntPtr _hwnd;
    private bool _dragging;
    private bool _moved;
    private POINT _downCursor;
    private RECT _downRect;

    public IslandWindow()
    {
        Width = 480; Height = 320;
        CanResize = false;
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        ShowActivated = false;
        ShowInTaskbar = false;
        Topmost = true;
        Background = Brushes.Transparent;
        TransparencyBackgroundFallback = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Content = _island;

        Win32Properties.AddWindowStylesCallback(this,
            (style, ex) => (style, ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));
        Win32Properties.AddWndProcHookCallback(this, WndProc);
        Opened += OnOpened;
    }

    private void OnOpened(object? _, EventArgs __)
    {
        _hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        var w = (int)Math.Round(Width * RenderScaling);
        var h = (int)Math.Round(Height * RenderScaling);
        SetWindowPos(_hwnd, HWND_TOPMOST, 1760, 1060, w, h,
            SWP_NOACTIVATE | SWP_SHOWWINDOW);
        // Optional: disable DWM non-client rendering/transitions, set dark mode,
        // disable DWM window rounding. See IslandWindow.ConfigureDwm().
    }

    private Point ScreenToDip(IntPtr lParam)
    {
        var v = lParam.ToInt64();
        var pt = new POINT { X = (short)(v & 0xFFFF), Y = (short)((v >> 16) & 0xFFFF) };
        ScreenToClient(_hwnd, ref pt);
        return new Point(pt.X / RenderScaling, pt.Y / RenderScaling);
    }

    private Point ClientToDip(IntPtr lParam)
    {
        var v = lParam.ToInt64();
        return new Point((short)(v & 0xFFFF) / RenderScaling,
                         (short)((v >> 16) & 0xFFFF) / RenderScaling);
    }

    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_NCHITTEST:
            {
                var p = ScreenToDip(lParam);
                handled = true;
                return (IntPtr)(_island.HitTestPill(p) ? HTCLIENT : HTTRANSPARENT);
            }
            case WM_MOUSEACTIVATE:
                handled = true;
                return (IntPtr)MA_NOACTIVATE;

            // The pill drag must not activate. Do not call BeginMoveDrag here.
            case WM_LBUTTONDOWN:
                if (_island.HitTestPill(ClientToDip(lParam)))
                {
                    GetCursorPos(out _downCursor);
                    GetWindowRect(hwnd, out _downRect);
                    _dragging = true; _moved = false;
                    SetCapture(hwnd);
                    handled = true;
                }
                break;
            case WM_MOUSEMOVE when _dragging:
                GetCursorPos(out var cur);
                var dx = cur.X - _downCursor.X;
                var dy = cur.Y - _downCursor.Y;
                if (!_moved && (Math.Abs(dx) > 3 || Math.Abs(dy) > 3)) _moved = true;
                if (_moved)
                    SetWindowPos(hwnd, HWND_TOPMOST, _downRect.Left + dx, _downRect.Top + dy,
                        0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
                handled = true;
                break;
            case WM_LBUTTONUP when _dragging:
                ReleaseCapture();
                _dragging = false;
                if (!_moved) _island.Toggle();
                handled = true;
                break;
        }
        return IntPtr.Zero;
    }
}
```

```csharp
public sealed class IslandView : Control
{
    private double _progress;      // 0 = 132x36, 1 = 440x284
    private double _velocity;
    private bool _running;
    private double _target;
    private Geometry? _hitGeometry;
    private long _lastTick;

    public void Toggle() => StartMorph(_target < 0.5);

    public void StartMorph(bool expand)
    {
        _target = expand ? 1.0 : 0.0;
        if (_running) return;
        _running = true;
        _lastTick = Stopwatch.GetTimestamp();
        TopLevel.GetTopLevel(this)!.RequestAnimationFrame(OnFrame);
    }

    private void OnFrame(TimeSpan _)
    {
        var now = Stopwatch.GetTimestamp();
        var dt = Math.Min(0.1, (now - _lastTick) / (double)Stopwatch.Frequency);
        _lastTick = now;

        const int steps = 4;
        for (var i = 0; i < steps; i++)
        {
            var h = dt / steps;
            var a = (-550 * (_progress - _target) - 38 * _velocity);
            _velocity += a * h;
            _progress += _velocity * h;
        }

        if (Math.Abs(_progress - _target) < 0.0015 && Math.Abs(_velocity) < 0.03)
        {
            _progress = _target; _velocity = 0; _running = false;
            InvalidateVisual();
            return; // settle: no further RAF, no idle render loop
        }

        InvalidateVisual();
        TopLevel.GetTopLevel(this)!.RequestAnimationFrame(OnFrame);
    }

    public override void Render(DrawingContext context)
    {
        var r = PillRect(); // center-anchor; width 132..440, height 36..284
        var radius = 18 + 24 * Math.Clamp(_progress, 0, 1);
        var geometry = BuildSquircle(r, radius);
        _hitGeometry = geometry;

        var fill = new SolidColorBrush(Color.Parse("#0C0E12"));       // fallback glass
        var border = new Pen(new SolidColorBrush(Color.Parse("#33FFFFFF")), 1);
        context.DrawGeometry(fill, border, geometry);
        DrawContent(context, r); // title/waveform/text, clipped to geometry
    }

    public bool HitTestPill(Point dip)
        => _hitGeometry?.FillContains(dip) ?? false;

    private static StreamGeometry BuildSquircle(Rect r, double radius)
    {
        radius = Math.Min(radius, Math.Min(r.Width, r.Height) / 2);
        var k = radius * 0.60;
        var g = new StreamGeometry();
        using var c = g.Open();
        c.SetFillRule(FillRule.NonZero);
        c.BeginFigure(new Point(r.X + radius, r.Y), true);
        c.LineTo(new Point(r.Right - radius, r.Y), true);
        c.CubicBezierTo(new Point(r.Right - radius + k, r.Y),
                        new Point(r.Right, r.Y + radius - k),
                        new Point(r.Right, r.Y + radius), true);
        c.LineTo(new Point(r.Right, r.Bottom - radius), true);
        c.CubicBezierTo(new Point(r.Right, r.Bottom - radius + k),
                        new Point(r.Right - radius + k, r.Bottom),
                        new Point(r.Right - radius, r.Bottom), true);
        c.LineTo(new Point(r.X + radius, r.Bottom), true);
        c.CubicBezierTo(new Point(r.X + radius - k, r.Bottom),
                        new Point(r.X, r.Bottom - radius + k),
                        new Point(r.X, r.Bottom - radius), true);
        c.LineTo(new Point(r.X, r.Y + radius), true);
        c.CubicBezierTo(new Point(r.X, r.Y + radius - k),
                        new Point(r.X + radius - k, r.Y),
                        new Point(r.X + radius, r.Y), true);
        c.EndFigure(true);
        return g;
    }
}
```

Production hit testing should rebuild geometry on demand from `_progress` rather than using the last presented `_hitGeometry`, or intentionally accept one frame of latency. The spike uses the last-rendered geometry and was accurate in tests.

---

## Known pitfalls / non-obvious findings

1. **Acrylic is window-sized, not pill-sized.** `TransparencyLevelHint=AcrylicBlur/Mica` with a fixed morph window gives a frosted rectangle. `WinUICompositionBackdropCornerRadius` rounds the window/border, not the pill.
2. **LowLatencyDxgiSwapChain is not usable for this app.** Its HWND had no `WS_EX_NOREDIRECTIONBITMAP`, per-pixel transparency became a black box, and acrylic `ActualTransparencyLevel` was `None`. It still ran ~158 fps, which can mislead a perf-only test.
3. **RedirectionSurface cannot hit 120 fps here.** p50 15.3 ms (≈77 fps mean) and no acrylic/mica. It is a compatibility fallback, not the production path.
4. **DirectComposition has transparency but no Acrylic/Mica** on this build. Requesting AcrylicBlur resolved to `Transparent`.
5. **Transparency + hit testing are independent.** A transparent pixel still belongs to the HWND unless `WM_NCHITTEST` says `HTTRANSPARENT`. The default Avalonia HWND hit-tests the whole client. Point testing must use the actual squircle geometry and `RenderScaling`.
6. **DPI-unaware test tools lie.** They see virtualized client rects and message coordinates; our first hit-test attempts were inverted. Set PMv2 in the probe.
7. **Do not layer the window manually.** The working WinUI path had `WS_EX_NOREDIRECTIONBITMAP` and no `WS_EX_LAYERED`. Layered-window tricks fight Avalonia's composition and acrylic. Let Avalonia own those bits.
8. **`Window.SystemDecorations` is obsolete in 12.** Use `WindowDecorations = WindowDecorations.None`.
9. **`OnRender` is not the override in Avalonia 12's `Control`.** Use `public override void Render(DrawingContext)`.
10. **Focus:** `ShowActivated=false` + `WS_EX_NOACTIVATE` + `MA_NOACTIVATE` kept foreground unchanged on show/click/drag. `IsActive` alone is not proof; use `GetForegroundWindow`.
11. **120 Hz is not a native mode on a 165 Hz panel.** RAF ticks at ~165 Hz; a strict 120 fps budget is met in the p99 aggregate but occasional missed vsyncs occur (max 18.6 ms in 360 frames). Do not set `DispatcherTimer` to 8.3 ms and call it “120 fps” — it will beat against vsync.
12. **Cold start matters.** The first animation after process launch had first-frame delays of 100–120 ms. Pre-warm/ReadyToRun/startup prewarm is required for a polished first appearance.
13. **Per-morph managed allocation is 1.4–3.1 MB.** It did not trigger a gen0 GC in the measured morphs, but it increases tail risk. Cache brushes/pens and avoid per-frame `FormattedText` allocation before shipping. Do not remove the custom `StreamGeometry` merely to reduce allocation if shape fidelity matters.
14. **Taskbar exclusion is ex-style based.** `WS_EX_TOOLWINDOW` + `ShowInTaskbar=false`; no separate `ITaskbarList` call was needed. Confirm on the release target's shell build.
15. **Shell classes are undocumented.** The Start surface was `Windows.UI.Core.CoreWindow` in this build; guard with process-name checks too.
16. **A second monitor changes more than X/Y.** Physical pixel size changes with DPI; a fixed DIP window may be physically resized by Avalonia on `WM_DPICHANGED`. This was not testable here.
17. **DWM corner rounding.** We set `DWMWA_WINDOW_CORNER_PREFERENCE=DWMWCP_DONOTROUND`; the shape itself is drawn in the client area. If left to DWM, system rounding can clip the transparent window/acrylic in ways unrelated to the pill.
18. **Manual `SetWindowLongPtr(GWLP_WNDPROC)` is possible but unnecessary.** Avalonia 12's `Win32Properties` hook keeps the callback rooted and composes with Av’s own WndProc. It is less fragile than a raw subclass.

---

## Fallback stack triggers

Use the **Win32 + Windows.UI.Composition + custom renderer** fallback if any of these is a hard requirement:

- **Native, pill-clipped acrylic/Mica** (not a whole-window rectangle).
- A strict **per-frame <8.33 ms** guarantee (no occasional missed vsync), or composition-thread-driven animation.
- Secondary-monitor/DPI validation fails or is not achievable with Avalonia's HWND/window model.
- The team cannot accept the ~170 MiB Debug working set and cannot get Release/trimming down to target.
- The shell z-order policy must guarantee non-overlap with every shell surface, including while dragging.

Why not **WPF + fixed-size layered window** as the first fallback: it can do per-pixel transparency and custom drawing, but a layered WPF window is composition-hosted by WPF with no Windows.UI.Composition acrylic, generally worse high-refresh behavior, and a larger rewrite. It is only a fallback if real acrylic and strict 120 fps are dropped. The technically strongest fallback for FluidBar is a native `HWND` with Windows.UI.Composition (CompositionRoundedRectangleGeometry for the pill clip, DesktopAcrylicBackdrop for material, or Direct2D/Skia content with a compositor-driven size animation), accepting the larger engineering cost.

---

## Reproduction / evidence index

Run:

```powershell
cd "$env:TEMP\fluidbar-spikes\avalonia-window"
dotnet build
.\bin\Debug\net10.0-windows\FluidBarSpike.exe --tag=demo --comp=winui --x=1760 --y=1060
```

`--input-locked` disables mouse morph/drag for unattended capture. `--backdrop` adds the test-only checkerboard/focus window. Probe scripts must set PerMonitorV2 DPI awareness before P/Invoke.

Evidence in `C:\Users\Doulor\AppData\Local\Temp\fluidbar-research`:

- `evidence/run9.log` — full warm-run trace (HWND facts, DPI, DWM, hit tests, drag, acrylic, policy).
- `evidence/run9-final-status.json` — final JSON snapshot.
- `evidence/idle.json` — 62 s idle measurement.
- `evidence/frame-analysis.json` — 6-run, 360-frame aggregate.
- `evidence/frame-intervals-representative.json` — one warm morph.
- `evidence/packages.txt` — resolved package graph.
- `evidence/composition-modes.json` — WinUI/Direct/LowLatency/Redirection comparison.
- `evidence/comp-*.log` — per-mode traces.
- `assets/` — screenshots and chart used above.

Spike code was written only under `%TEMP%\fluidbar-spikes\avalonia-window`; `D:\build\GitLocal\FluidBar` was not touched. All spike windows were closed; no `FluidBarSpike` process remains.

---

## Bottom line

- **Avalonia 12.1.4 on .NET 10 is a credible host** for FluidBar's fixed-size, per-pixel-transparent, non-activating, click-through island, including a 165 Hz, ~0.36 s spring morph and <0.2 % idle CPU.
- **It is not a credible host for pill-shaped native acrylic** while keeping the window fixed. Use simulated glass, or move the island content into a native compositor.
- **One validation gap remains:** real secondary-monitor / mixed-DPI testing. That is an environment limitation, not a demonstrated Avalonia failure, but it must be closed before a production GO.
