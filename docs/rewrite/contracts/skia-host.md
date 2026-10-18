# Skia island host — Win32 layered host integration (`SkiaIslandHost`)

Status: **delivered (branch `v2/rebuild`, not committed)**. Owner: App/graphics integration.
Consumers: `FluidBar.App`. Tests: `tests/FluidBar.Ui.Tests/SkiaIslandHostMappingTests.cs` (headless),
`SkiaMonitorPlannerTests.cs` + `SkiaHostDockParityTests.cs` (headless monitor/DPI/dock),
`SkiaLayeredWindowIntegrationTests.cs` (guarded real HWND), and `LayeredHostTests.cs` in
`FluidBar.Platform.Tests` (placement/DIB math).

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
Expanded page controls are dispatched through the App-owned `IIslandPlatformActions` seam
(`SkiaPlatformActions`): media transport/seek use the GSMTC service, volume/brightness use their
monitors, and App-owned list actions are emitted as deferred events (see §8). The legacy
`MediaCommandRequested` event remains the no-seam fallback.

## 2. Composition (`src/FluidBar.App/Hosting/Skia/`)

| File | Contents |
|---|---|
| `SkiaIslandHost.cs` | One `LayeredWindowHost` per target monitor, fixed surfaces, placement, monitor-set reconciliation, DPI change, frame loop, DIB→Skia surface, input subscription, hit-region sync, stats, `StartAsync`/`Dispose`. |
| `SkiaIslandController.cs` | Window-free pipeline: `EventHub` → `ActivityScheduler` → `IslandStateController`, deadlines, effects, content mapping, pager/archipelago, input routing, invalidation. |
| `SkiaMonitorPlanner.cs` | Pure monitor targeting + add/remove diff: `DisplayOn` (primary/all), stable primary, primary-first ordering, work-area/DPI change detection. No HWND, no service. |
| `SkiaIslandMotion.cs` | Avalonia-free shell springs (width/height/hover/press/nudge/hide), radii blend, origin, content opacity (mirrors the old `ShellMotionDriver`). |
| `SkiaPager.cs` | `PagePagerPolicy` drag/release + `PagerSnapMotion`; wheel via `PagerWheelPolicy` (notch/cooldown) and `PagerScrollChainPolicy` (ownership/lock/dwell). |
| `SkiaArchipelago.cs` | Transient strip reveal/hide (hot zone 300 ms, auto-hide 1000 ms) with Core springs and pager dot counts. |
| `SkiaIslandStateBuilder.cs` | Core state/presenter/content → `CompactActivity`, `ExpandedPageContent`, theme, structural/dynamic signatures. |
| `SkiaIslandThemeMapper.cs` | `AppSettings` → `IslandTheme` (dark/light, obsidian; acrylic → simulated glass, accent, fill clamp, reduced motion). |
| `SkiaIslandHitTest.cs` | Renderer-equivalent hit testing + strip geometry (parity-tested against `IslandRenderer.MeasureStrip`); list rows are hit-tested against the model's row count/scroll offset. |
| `SkiaIslandControlRouter.cs` | Pure hit-target → `IslandPlatformAction` mapping (slider fractions, row validation, quick-toggle identity). |
| `IIslandPlatformActions.cs` | Narrow App seam: semantic action/result types + `IIslandPlatformActions.Invoke`. No Avalonia/Win32 types, so controller tests use a fake. |
| `SkiaPlatformActions.cs` | Production seam: GSMTC media (play/pause/next/previous/seek), `VolumeMonitor`, `BrightnessMonitor`, `ClipboardService`, settings theme toggle; App-owned actions raise `DeferredActionRequested`; missing services return an exact unsupported reason. |
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
| `LayeredDpiChanged` | entry DPI update + `NotifyDisplayChanged` | update the cached monitor DPI, drop the Skia wrapper, snap, re-place from Core, full frame |
| `PointerCaptureLost` | `HandleDragEnded` | cancel the pager drag |

Esc/hotkeys are exposed as `HandleEscape`/`HandleToggleHotkey`/`HandleShowHotkey`/`HandleHideHotkey`
(the platform hotkey service is not wired in this milestone). Click-through outside the shell and the
strip pills comes from `LayeredHitRegion` (shell squircle + mini-island/dot rectangles); the strip
background and the lane stay `HTTRANSPARENT`.

## 5. Placement / monitors / DPI / modes

### 5.1 One placement source (all modes)

`IslandLayoutRequest.FromState` → `IslandLayout.Calculate` → `WindowPlacementConverter.PlaceWindow`
is the single placement source. The window is created at the mode's fixed surface (488×384
float/flush, 488×444 dock) and is never resized or moved during animation.

`LayeredWindowHost.PlaceForWindowPlacement(WindowPlacement, mode)` is used for **float, flush and
dock**; Core's `WindowPixel` is the host window origin verbatim and the DIB is
`ceil(surfaceDIP × scale)`. The old dock `PlacePhysical` workaround is gone: Core's `HostSurface`
and `LayeredSurfaceSize` now describe the same composition.

| Dock local rect (DIP) | Range | Source |
|---|---|---|
| Expanded panel | x 24..464, y **24..308** (440×284) | `HostSurface.Dock.ShellEnvelopeLocal` |
| Dock strip (compact shell) | x 24..464, y **316..368** (440×52) | `HostSurface.Dock.DockStripLocal` |
| Archipelago strip envelope | x 68..420, y **378..408** (352×30) | `HostSurface.Dock.ArchipelagoStripLocal` |

- `LayeredSurfaceSize.ShellLocalOffset(Dock)` is `(24,24)` (expanded panel, matching Core);
  `ShellLocalOffset(Dock, Compact)` is `(24,316)` for the dock strip.
- `LayeredSurfaceSize.DockStripTop = 316`, `ArchipelagoTop(Dock) = 378`; the renderer
  (`TransientStripRenderer`) and the App hit-test read Core's `ArchipelagoTopLocal`, so the dock
  strip and the archipelago never overlap.
- Core anchors the dock host bottom 4 DIP above the work-area bottom; the strip bottom therefore
  lands 80 DIP above it and the expanded panel bottom 140 DIP above it (see
  [`core.md`](core.md) §4.3/§10.9).

**Measured local→pixel placement** (DIB origin = Core `WindowPixel`, local rect × scale):

| Scale | Surface px | Panel px | Dock strip px | Archipelago px |
|---|---|---|---|---|
| 100 % | 488×444 | 24..308 | 316..368 | 378..408 |
| 150 % | 732×666 | 36..462 | 474..552 | 567..612 |
| 200 % | 976×888 | 48..616 | 632..736 | 756..816 |

Float at 150 %: surface 732×576, panel 36..462, archipelago 477..522. Pinned by
`SkiaHostDockParityTests` (Ui) and `LayeredHostTests.ComputePlacement_*` (Platform).

### 5.2 Monitors (add/remove/display change)

`MonitorService` watches `WM_DISPLAYCHANGE` / `WM_SETTINGCHANGE` / `WM_DPICHANGED`, debounces for
300 ms, re-enumerates on the thread pool and publishes an immutable `MonitorSet`. The host posts the
snapshot to its UI-thread dispatcher and applies it there — never per frame:

1. `SkiaMonitorPlanner.Plan(currentWindows, available, DisplayOn, previousPrimaryId)` resolves the
   target set and the exact create/dispose diff.
2. `DisplayOn.Primary` keeps exactly one window on the primary; `DisplayOn.All` keeps one per
   enumerated monitor. `DisplayOn.FollowMouse` falls back to the primary (see §8).
3. The primary is **stable**: the OS-flagged primary wins; with no flag the previous primary is kept
   if it still exists; otherwise the first monitor is used. The primary is always first in the list,
   so `PrimaryWindow` never changes identity on a hot-plug.
4. Removed monitors dispose their `LayeredWindowHost` (HWND destroyed on the UI thread); added
   monitors create a host, are placed through Core and are shown afterwards (no flash at a stale
   origin); retained windows adopt the fresh work area/DPI and are re-placed.
5. A `DisplayOn` settings change reconciles the window set the same way. A work-area change re-places
   without recreating; a DPI change rebuilds the DIB through `SetMonitor`.
6. An empty snapshot is treated as a transient enumeration failure: it keeps the last known windows
   instead of tearing them all down. The result of a real change is one full frame; idle stays at
   zero frames.

### 5.3 DPI

- The process is Per-Monitor-V2. `WM_DPICHANGED` rebuilds the DIB at
  `ceil(DIP × scale)` (surface px: float 732×576 @150 %, 976×768 @200 %; dock 732×666 @150 %,
  976×888 @200 %), follows the OS suggested origin, then raises `LayeredDpiChanged`.
- The host updates the entry's monitor DPI immediately (the monitor snapshot is debounced by
  300 ms), drops the Skia wrapper, and calls `NotifyDisplayChanged()`: springs snap, every window is
  re-placed from Core's `WindowPlacement` (the App's placement wins over the OS suggestion) and a
  full frame is rendered. `EnsureSurface` re-wraps the new DIB bits/size.
- The renderer receives the per-window `frame.Scale`; there is no software scaling of a stale frame.
- Mode switch: `SetMode` (rebuild DIB) + re-place + full frame.

## 6. Archipelago (detached strip)

Revealed by pager drag/wheel (immediate) or a 300 ms dwell in the bottom 12 DIP hot zone; auto-hides
after 1000 ms without activity (inside the 900–1200 ms design band). Opacity/translate/scale come from
Core springs (`MotionTokens.Hover`); `PagePosition` and far-page dot counts come from the pager
(clamped to 3 per side). Only the mini-island pills and dots are added to the host hit region.

The strip is anchored at Core's `HostSurface.ArchipelagoTopLocal`: **318 float** (below the panel) and
**378 dock** (below the 52 DIP dock strip). The renderer, the App hit-test and the dirty-rect damage
all use that one value, so the dock strip is never overdrawn by the archipelago.

## 7. Verification

```powershell
dotnet build FluidBar.sln
dotnet test FluidBar.sln -m:1
dotnet test tests/FluidBar.Ui.Tests/FluidBar.Ui.Tests.csproj --filter "FullyQualifiedName~Skia"
# monitor targeting / add-remove diff / dock local-rect + placement parity (headless):
dotnet test tests/FluidBar.Ui.Tests/FluidBar.Ui.Tests.csproj --filter "FullyQualifiedName~SkiaMonitorPlannerTests|FullyQualifiedName~SkiaHostDockParityTests"
# DIB ceil rule + Core WindowPixel placement (headless):
dotnet test tests/FluidBar.Platform.Tests/FluidBar.Platform.Tests.csproj --filter "FullyQualifiedName~LayeredHostTests"
$env:FLUIDBAR_HWND_TESTS='1'   # opt-in real HWND test
$env:FLUIDBAR_HOST='skia'; $env:FLUIDBAR_SMOKE='1'   # smoke + PNG evidence
```

Evidence: `build/reports/skia-island/HOST-EVIDENCE.md`, `build/verify-skia-host.ps1`,
`build/reports/skia-island/surface/*.png`.

Tests added for this hardening pass:

| Test | Proves |
|---|---|
| `SkiaMonitorPlannerTests` (Ui, 14 headless) | primary/all targeting, stable-primary fallback, add/remove diff, geometry/DPI change detection, primary-first ordering, empty snapshot removes all, FollowMouse fallback |
| `SkiaHostDockParityTests` (Ui, 15 headless) | Core `HostSurface.Dock` panel 24..308 / strip 316..368 / arch 378..408 equals the renderer's local rects; float strip at 318; `ceil(DIP×scale)` at 100/150/200 %; dock compact/expanded placement keeps Core local offsets and `WindowPixel`; measured origins (716,592 / 594,368 / 472,144 for a 1920×1040 work area) |
| `LayeredHostTests` (Platform) | form-aware `ShellLocalOffset`, Platform↔Core surface parity, fractional ceil rule, `ComputePlacement` equals Core `WindowPixel` at 100/150/200 % |
| `SkiaLayeredWindowIntegrationTests` (guarded) | real HWND dock placement through `PlaceForWindowPlacement` (no `PlacePhysical`) |

## 8. Known deviations / TODO

1. Page controls are dispatched through `IIslandPlatformActions`; there is no
   `island.skia.control-unmapped` path any more. Unsupported controls log
   `island.skia.control-unsupported` with the exact missing service, deferred App-owned controls log
   `island.skia.control-deferred` (see §8.1).
2. Not implemented: hidden-state hover strip, UIA provider, IME/lookup window, pointer-follow display
   targeting, anchor drag/persist, font-scale.
3. **FollowMouse** targets the primary monitor. A correct implementation needs a cursor-position seam
   (Platform exposes `MonitorService.MonitorAtPoint`, but the App cannot P/Invoke `GetCursorPos`);
   the TODO is on `SkiaMonitorPlanner.ResolveTargets`.
4. **Anchor drag/persist** is not implemented. A compact press expands immediately
   (`IslandInput.PrimaryClick`), so drag-to-move needs click-vs-drag discrimination first; the
   expanded-shell drag is owned by the pager. Persisting the result also needs a per-monitor
   `PlacementSettings` write (`MonitorPlacement(anchor, offsetX, offsetY)`), which `IUiSettingsStore`
   does not expose yet. The exact TODO is on `IslandEffectKind.PersistPlacement`.
5. **Monitor identity** uses `\\.\DISPLAYn` (Core `MonitorInfo.Id`); the EDID-hash stable id from
   spec 2.6 is not wired. A monitor that changes its DISPLAYn is treated as a new window (dispose +
   create) rather than a moved one.
6. **Hot-plug latency** is bounded by the monitor service's 300 ms debounce; add/remove is then a
   single UI-thread reconciliation, not a per-frame cost. Enumeration failure keeps the last known
   windows.
7. **Per-monitor placement** (Core `PlacementSettings`) is not consumed by the App yet; every window
   uses the single `AppSettings` anchor. `OffsetX/OffsetY` are therefore always 0 on the Skia path.
8. **Dock composition** is Core's: the strip is *below* the panel, and the host bottom is 4 DIP above
   the work-area bottom (strip bottom 80 DIP, panel bottom 140 DIP above it). The old platform model
   (52 DIP lane above the panel, strip at 368) is gone; `LayeredSurfaceSize.ShellLocalOffset(Dock)`
   is the panel top (24,24) and the form-aware overload returns (24,316) for the compact strip.
9. Acrylic has no real blur on a layered window; it degrades to the renderer's simulated-glass tint.
10. The renderer's waveform keeps the frame loop alive while a media activity plays (capped to ~30 fps);
    everything else is strictly idle = 0 frames.

### 8.1 Page-control platform seam

The controller never touches a platform service. `HandlePointerPressed` → `SkiaIslandHitTest` →
`SkiaIslandControlRouter.Map` produces a semantic `IslandPlatformAction`, and `DispatchControl`
calls the injected `IIslandPlatformActions.Invoke`. A null seam keeps the legacy
`MediaCommandRequested` event for media transport; every other action is logged unsupported.

```csharp
public interface IIslandPlatformActions
{
    IslandActionResult Invoke(in IslandPlatformAction action); // never blocks; async work is fire-and-forget
}

public readonly record struct IslandPlatformAction(
    IslandPlatformActionKind Kind,
    int Index = -1,          // row / quick-toggle slot
    double Value = 0,        // 0..1 slider fraction, or -1/+1 calendar delta
    IslandQuickToggle Toggle = IslandQuickToggle.Wifi,
    string? Text = null);    // clipboard row text for copy

public readonly record struct IslandActionResult(IslandActionOutcome Outcome, string? Reason = null);
// Outcome: Handled | Deferred | Unsupported
```

**Mapped controls**

| Page | Control (`IslandPageControl`) | Action | Implementation |
|---|---|---|---|
| Home (0) | volume track | `VolumeSet` (0..1) | `VolumeMonitor.SetVolumeAsync(percent)` |
| Home (0) | brightness track | `BrightnessSet` (0..1) | `BrightnessMonitor.SetPercentAsync(percent)` |
| Home (0) | quick toggle 0..3 | `QuickToggle` (`Wifi`/`Bluetooth`/`Focus`/`Theme`) | theme → `SettingsController.Theme` toggle; the other three return unsupported (see below) |
| Home (0) | now-playing card | navigation only | opens the media page (no platform action) |
| Home (0) | now-playing play | `MediaTogglePlay` | `GsmtcMediaSessionService.TogglePlayPauseAsync` |
| Media (1) | previous / play / next | `MediaPrevious` / `MediaTogglePlay` / `MediaNext` | `PreviousAsync` / `TogglePlayPauseAsync` / `NextAsync` |
| Media (1) | seek row/track | `MediaSeek` (0..1 of `MediaSeekTrack`) | `SeekAsync(duration × fraction)` |
| Media (1) | output / queue | `MediaOutput` / `MediaQueue` | unsupported (no service) |
| Notifications (2) | row | `NotificationActivate(index)` | deferred App event |
| Notifications (2) | dismiss | `NotificationDismiss(index)` | deferred App event |
| Clipboard (3) | row / copy | `ClipboardCopy(index, text)` | `ClipboardService.SetTextAsync(text)` |
| Clipboard (3) | pin | `ClipboardPin(index)` | deferred App event (clipboard pin state is App-owned) |
| Agent (4) | row | `AgentActivate(index)` | deferred App event |
| Widgets (5) | calendar ‹ / › | `CalendarNavigate(-1/+1)` | deferred App event |
| Widgets (5) | task row | `TaskToggle(index)` | deferred App event |
| Compact | media play | `MediaTogglePlay` | same media path |

`Scroll` hits and the page title are not actions. List rows are validated against the page model
(`Items.Count`, `ScrollOffset`), so a click below the last row is a scroll-region hit, never an
out-of-range action.

**Seek fallback (documented, not faked):** seeking requires a running `GsmtcMediaSessionService`, an
active non-empty `MediaSnapshot`, `CanSeek == true` and `Duration > 0`. Otherwise the controller logs
`island.skia.control-unsupported` with that exact reason and leaves the progress indicator untouched;
there is no synthetic position update.

**Known display gaps (action works, indicator does not follow yet):** the home volume/brightness
models still use the design-data levels because `IslandContent` carries no level field; the slider
action changes the real system value, but the painted track updates only once the App data source
feeds `VolumeSnapshot`/`BrightnessSnapshot` into `HomePageModel`. Clipboard copy sends the rendered
row preview (the App content model has no untruncated text yet). Deferred list actions do not mutate
the App's content snapshot; `DeferredActionRequested` is the extension point for that owner.

**Unsupported controls (exact missing service)**

| Control | Reason logged |
|---|---|
| Wi-Fi toggle | `NetworkMonitor` is read-only; needs a `Windows.Devices.Radios`/`WlanSetInterface` seam |
| Bluetooth toggle | `BluetoothDeviceMonitor` is read-only; needs a `Windows.Devices.Radios.Radio` seam |
| Focus toggle | Windows exposes no supported public Focus Assist/quiet-hours toggle |
| Output | no `IAudioOutputService` (IMMDeviceEnumerator picker) exists |
| Queue | no media queue service; the GSMTC snapshot carries no queue |

These return `IslandActionResult.Unsupported(reason)`; `DispatchControl` logs
`island.skia.control-unsupported` with `control`, `page`, `index`, `value`, `toggle` and `reason`.
Deferred actions log `island.skia.control-deferred` and raise
`SkiaPlatformActions.DeferredActionRequested` for a later App owner.

`SkiaPlatformActions` owns only the services the controls need (one `PlatformMessageHost` plus
volume/brightness/GSMTC/clipboard) and starts them best-effort off the UI thread. A failed service
leaves its controls unsupported; it never reports a fake success.

**Tests** (`tests/FluidBar.Ui.Tests/SkiaIslandControlTests.cs`, headless, fake seam): media
transport/seek (including fraction clamping), volume/brightness fractions, all four quick toggles,
notification row/dismiss, clipboard pin/copy/row, agent row, calendar nav/task toggle, page-model row
counts, theme-toggle state, unsupported vs deferred logging, the no-seam legacy media fallback, and
hit-target → action mapping including out-of-range rows.
