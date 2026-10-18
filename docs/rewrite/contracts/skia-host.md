# Skia island host — Win32 layered host integration (`SkiaIslandHost`)

Status: **delivered (branch `v2/rebuild`, not committed)**. Owner: App/graphics integration.
Consumers: `FluidBar.App`. Tests: `tests/FluidBar.Ui.Tests/SkiaIslandHostMappingTests.cs` (headless)
and `SkiaLayeredWindowIntegrationTests.cs` (guarded real HWND).

This contract covers the App-side integration of the production Skia renderer
([`skia-island.md`](skia-island.md), `FluidBar.Island`) with the Win32 per-pixel-alpha host
([`layered-host.md`](layered-host.md), `LayeredWindowHost`). It replaces the Avalonia island window on
the default path; the Avalonia `IslandHost` stays compiled and selectable as the fallback.

---

## 1. Feature flag and startup

| `FLUIDBAR_HOST` | Behaviour |
|---|---|
| unset / anything else | Try the Skia layered host first; on any failure log `app.skia-host-failed` and fall back to the Avalonia `IslandHost`. |
| `skia` | Force the Skia layered host; a failure is fatal (no silent fallback). |
| `avalonia` | Force the legacy Avalonia island window host. |

`AppBootstrap.StartIslandHostAsync` composes `SkiaIslandHost` with the same services as the Avalonia
host (`ILocalizer`, `SettingsController`, `IIslandDataSource`, `IAppLogger`) and wires
`SettingsRequested`/`MarketRequested` to the existing Avalonia settings/market windows (unchanged).
`MediaCommandRequested` is exposed but not subscribed by the bootstrap yet (no platform media
command seam in this milestone).

## 2. Composition (`src/FluidBar.App/Hosting/Skia/`)

| File | Contents |
|---|---|
| `SkiaIslandHost.cs` | One `LayeredWindowHost` per target monitor, fixed surfaces, placement, frame loop, DIB→Skia surface, input subscription, hit-region sync, stats, `StartAsync`/`Dispose`. |
| `SkiaIslandController.cs` | Window-free pipeline: `EventHub` → `ActivityScheduler` → `IslandStateController`, deadlines, effects, content mapping, pager/archipelago, input routing, invalidation. |
| `SkiaIslandMotion.cs` | Avalonia-free shell springs (width/height/hover/press/nudge/hide), radii blend, origin, content opacity (mirrors the old `ShellMotionDriver`). |
| `SkiaPager.cs` | `PagePagerPolicy` drag/release + `PagerSnapMotion`; wheel via `PagerWheelPolicy` (notch/cooldown) and `PagerScrollChainPolicy` (ownership/lock/dwell). |
| `SkiaArchipelago.cs` | Transient strip reveal/hide (hot zone 300 ms, auto-hide 1000 ms) with Core springs and pager dot counts. |
| `SkiaIslandStateBuilder.cs` | Core state/presenter/content → `CompactActivity`, `ExpandedPageContent`, theme, structural/dynamic signatures. |
| `SkiaIslandThemeMapper.cs` | `AppSettings` → `IslandTheme` (dark/light, obsidian; acrylic → simulated glass, accent, fill clamp, reduced motion). |
| `SkiaIslandHitTest.cs` | Renderer-equivalent hit testing + strip geometry (parity-tested against `IslandRenderer.MeasureStrip`). |
| `UiThreadTicker.cs` | Non-blocking UI-thread ticker + `SkiaFrameClock` (no `DwmFlush`/blocking wait on the UI thread). |
| `SkiaHostDispatcher.cs` | Avalonia-free `IDispatcher` (captured synchronization context). |

## 3. Frame path

```
UI-thread frame clock (System.Threading.Timer → SynchronizationContext.Post, ~8.3 ms at 120 fps)
  → SkiaIslandController.PumpTimers()/Advance(dt)        (springs, pager, strip, invalidation)
  → LayeredWindowHost.BeginFrame()                        (persistent DIB, thread-affine)
  → SKSurface.Create(Bgra8888, Premul, frame.Bits, frame.Stride)
  → IslandRenderer.Render(canvas, state, dpiScale)        (full) or (…, damageDip) (dirty)
  → LayeredWindowHost.CommitFrame() / CommitFrame(LayeredRect)
```

- Full frames: any state/content/theme/page change, shell morph (damage = union of old/new shell),
  DPI/mode/anchor change, first frame.
- Dirty frames: expanded media progress/lyrics (`PageLayout.MediaLyricCard` + `MediaSeekRow`), the
  compact media waveform, and the detached strip band. Measured 0.7–1.6 ms warm.
- `ConfigurePacing(LayeredPacingMode.TargetFrameRate, 60/120)` is applied per window for contract
  parity, but `WaitForNextFrame()` is deliberately **not** called on the UI thread; the UI-thread
  ticker owns the cadence (wave-only content drops to ~30 fps).
- Idle: the clock stops when the springs, pager, strip and dynamic content are at rest (smoke:
  `idle=True`, `clockTicks` frozen after collapse).

## 4. Input

`LayeredWindowHost` pointer/wheel/drag events are mapped to the Core controller:

| Host event | Controller | Effect |
|---|---|---|
| `PointerMoved` / `PointerLeft` | `HandlePointerMoved/Left` | hover enter/leave (Core `IslandInput`), hot-zone dwell, strip hover keep-alive, cursor |
| `PointerPressed` / `Released` | `HandlePointerPressed/Released` | compact → expand (play/pause control handled separately), collapse chevron → Escape, page controls → media commands, mini-islands/dots → page request |
| `PointerWheel` | `HandleWheel` | one notch = 140 DIP; `PagerWheelPolicy` gate + `PagerScrollChainPolicy` decision; wheel-down advances |
| `Dragged` (Started/Moved/Ended/Cancelled) | `HandleDragStarted/Moved/Ended` | pager drag (`PagePagerPolicy.Drag`, `-pointerDeltaX`), release → `PagePagerPolicy.Release` + snap |
| `LayeredDpiChanged` | `NotifyDisplayChanged` | snap, full frame, re-place |
| `PointerCaptureLost` | `HandleDragEnded` | cancel the pager drag |

Esc/hotkeys are exposed as `HandleEscape`/`HandleToggleHotkey`/`HandleShowHotkey`/`HandleHideHotkey`
(the platform hotkey service is not wired in this milestone). Click-through outside the shell and the
strip pills comes from `LayeredHitRegion` (shell squircle + mini-island/dot rectangles); the strip
background and the lane stay `HTTRANSPARENT`.

## 5. Placement / DPI / modes

- `IslandLayoutRequest.FromState` → `IslandLayout.Calculate` → `WindowPlacementConverter.PlaceWindow`
  is the single placement source; the window is created at the mode's fixed surface (488×384
  float/flush, 488×444 dock) and never resized or moved during animation.
- Float/flush use `LayeredWindowHost.PlaceForWindowPlacement` (Core `WindowPlacement`).
- **Dock** uses `PlacePhysical` with Core's `WindowPixel`: Core's dock composition (panel 24..308,
  strip 316..368, archipelago band below) differs from `LayeredSurfaceSize.ShellLocalOffset` (a
  52 DIP lane above the panel), so the host's offset correction would shift the window by 52 DIP.
  The fixed surface *size* still matches.
- Mode switch: `SetMode` + re-place + full frame. Per-monitor DPI: `SetMonitor` + `WM_DPICHANGED`
  handling in the host; the renderer receives `dpiScale` per window.
- `DisplayOn.All` creates one window per enumerated monitor; hot-plug add/remove is not implemented
  (display changes re-place existing windows).

## 6. Archipelago (detached strip)

Revealed by pager drag/wheel (immediate) or a 300 ms dwell in the bottom 12 DIP hot zone; auto-hides
after 1000 ms without activity (inside the 900–1200 ms design band). Opacity/translate/scale come from
Core springs (`MotionTokens.Hover`); `PagePosition` and far-page dot counts come from the pager
(clamped to 3 per side). Only the mini-island pills and dots are added to the host hit region.

## 7. Verification

```powershell
dotnet build FluidBar.sln
dotnet test FluidBar.sln -m:1
dotnet test tests/FluidBar.Ui.Tests/FluidBar.Ui.Tests.csproj --filter "FullyQualifiedName~Skia"
$env:FLUIDBAR_HWND_TESTS='1'   # opt-in real HWND test
$env:FLUIDBAR_HOST='skia'; $env:FLUIDBAR_SMOKE='1'   # smoke + PNG evidence
```

Evidence: `build/reports/skia-island/HOST-EVIDENCE.md`, `build/verify-skia-host.ps1`,
`build/reports/skia-island/surface/*.png`.

## 8. Known deviations / TODO

1. Dock placement uses `PlacePhysical` (see §5) until `LayeredSurfaceSize`/Core dock compositions are
   reconciled.
2. Unmapped page controls (seek/volume/brightness, output/queue, quick toggles) log
   `island.skia.control-unmapped`; they need platform seams.
3. Not implemented: hidden-state hover strip, UIA provider, IME/lookup window, per-monitor window
   add/remove on hot-plug, pointer-follow display targeting, anchor drag/persist, font-scale.
4. Acrylic has no real blur on a layered window; it degrades to the renderer's simulated-glass tint.
5. The renderer's waveform keeps the frame loop alive while a media activity plays (capped to ~30 fps);
   everything else is strictly idle = 0 frames.
