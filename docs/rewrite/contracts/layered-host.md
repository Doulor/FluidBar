# Layered host — Win32 per-pixel-alpha island host (`LayeredWindowHost`)

Status: **spike delivered (2-day, branch `v2/rebuild`, not committed)**. Owner: platform-owner.
Consumers: `FluidBar.App` (island renderer). Tests: `FluidBar.Platform.Tests`
(`LayeredHostTests` + one guarded integration test). Spike + evidence: `tools/spikes/layered-host/`.

This contract defines the fallback host selected after the transparency investigation
([`host-transparency.md`](host-transparency.md) §10): canonical Win32 `UpdateLayeredWindow` stays
per-pixel transparent in this environment, while Avalonia/DirectComposition surfaces flatten to
opaque grey. The host is **framework-free** (no Avalonia/WPF; `FluidBar.Platform.Windows` keeps zero
package references). Rendering (SkiaSharp) lives in the App/spike, not in this project.

---

## 1. Why this host (decision record)

| Fact (measured) | Consequence |
|---|---|
| Canonical layered windows are transparent in captures *and* on the physical panel; Avalonia 12 `WinUIComposition` flattens to `#D3D3D3` | Layered is the only proven transparent mechanism on this machine |
| `UpdateLayeredWindow` accepts a 732×576 premultiplied DIB frame in **0.06–0.16 ms p50** | A full-frame upload is not the bottleneck; the CPU raster is |
| A dirty-rect upload of two small areas costs **0.08–0.10 ms p50** (one merged call) | Animation can stay in the dirty path and leave the rest of the surface untouched |
| The host performs **zero** uploads and ~0.3–0.5 % of one core when idle | "Idle = no frames" is a property of the design, not an optimisation |
| `WM_MOUSEACTIVATE` → `MA_NOACTIVATE` + `WS_EX_NOACTIVATE` keeps the foreground window unchanged across clicks and drags | No focus-steal regression |
| `HTTRANSPARENT` outside the shell/pills is honoured by the OS (`WindowFromPoint` returns the window below) | Click-through is a real hit-test, not an alpha illusion |

The cost we accept: the island is CPU-rastered (no GPU compositor, no real acrylic) and the frame
budget is dominated by Skia, not by the upload. See §8 for the numbers and §10 for risks.

## 2. Surface sizes and placement

Two **fixed** per-mode surfaces, one window per monitor, created at the final size and never resized
per frame (spec §5.6). All values in DIP at 96 DPI; physical size is `ceil(DIP × scale)`.

| Mode | Surface (DIP) | 150 % physical | Composition | Anchor shell rect (surface-local DIP) |
|---|---|---|---|---|
| `Float` | 488 × 384 | 732 × 576 | 24 bleed + 284 panel + 10 gap + **30 archipelago** + 36 bottom bleed | panel at (24, 24) |
| `Dock` | 488 × 444 | 732 × 666 | 24 bleed + 284 panel + 8 gap + **52 strip** + 10 gap + 30 archipelago + 36 bottom bleed | panel at (24, 24); compact strip at (24, 316) |

- Width is always `440 shell + 2 × 24 bleed` = 488 DIP.
- The **bottom band** is the whole point of the extra height: the archipelago mini-islands (and, for
  dock, the dock strip) live in the same HWND but outside the panel, and the band between them is
  click-through (§6).
- `LayeredSurfaceSize.ForMode(mode)`, `.ToPixels(scale)`, `.ShellLocalOffset(mode)` are the single
  source of truth and mirror Core's `HostSurface`: `DockStripTop` = 316, `ArchipelagoTop(Dock)` = 378,
  `DetachedLaneHeight` = 76 (float bottom band), `DockBottomAllowance` = 136. A unit test pins the
  Platform↔Core parity.
- `ShellLocalOffset(mode)` is the **expanded panel** top (24, 24) for both modes. The overload
  `ShellLocalOffset(mode, ShellForm)` returns (24, 316) for dock compact/peek/closing/hidden.

Placement is **one** `SetWindowPos` (spec §5.6/§5.5), delegated to
`IslandWindowInterop.PlacePhysical` when an interop instance was supplied, otherwise issued directly
with the same flags (`SWP_NOACTIVATE | SWP_NOZORDER | SWP_NOOWNERZORDER`, no clamping, negative
origins allowed):

```
windowOriginPx = workArea.Origin + round((shellDip − shellLocalOffsetDip) × scale)
windowSizePx   = ceil(surfaceDip × scale)
```

`PlaceForWindowPlacement(in WindowPlacement, mode)` is the authoritative seam: it consumes Core's
single placement product (`WindowPlacementConverter.PlaceWindow`) and uses
`placement.WindowPixel` **verbatim** as the window origin (both modes now have the same surface size
as Core's `HostSurface`, so there is no dock offset correction). `ComputePlacement(...)` is the pure
static math behind it (no HWND/thread affinity) and is pinned by tests at 100/150/200 %.
`Place(monitor, shellDip, mode, form = Compact)` remains for legacy callers/spikes and derives the
origin from the form-aware shell offset. Placement never happens inside the frame path.

**DPI.** The host is Per-Monitor-V2 by contract (the process must declare it; the spike's manifest
does). `WM_DPICHANGED` rebuilds the DIB at the new physical size, applies the OS suggested origin
(but keeps the fixed surface size), updates `Placement`, then raises `LayeredDpiChanged`; the event is
raised **after** the suggestion so the App's `PlaceForWindowPlacement` is the final word. The next
commit is forced full. `SetMonitor(monitor)` does the same for a programmatic monitor move.

## 3. Public API (`FluidBar.Platform.Windows.Hosting`)

### 3.1 `LayeredWindowHost : IDisposable`

Thread-affine to the thread that constructs it (the UI thread). Every mutating member throws
`InvalidOperationException` from another thread; `Dispose` is idempotent and must run on the owning
thread.

| Member | Contract |
|---|---|
| `LayeredWindowHost(LayeredHostOptions?, IslandWindowInterop?, IPlatformLogger?, PlatformCounters?, TimeProvider?)` | Constructs the object only; no HWND. |
| `void Create(MonitorInfo monitor, LayeredHostMode mode = Float)` | Registers the window class once per process, creates `WS_POPUP \| WS_EX_LAYERED \| WS_EX_TOOLWINDOW \| WS_EX_NOACTIVATE \| WS_EX_TOPMOST`, asserts `HWND_TOPMOST` with `SWP_NOACTIVATE`, creates the DIB at the mode's physical size, creates the pacer. Window starts **hidden**. |
| `void Show()` / `void Hide()` | `ShowWindow(SW_SHOWNOACTIVATE)` **plus** `SetWindowPos(SWP_SHOWWINDOW)` (the first `ShowWindow` of a process is overridden by `STARTUPINFO`); `Show` asserts topmost once. `Hide` releases capture, hides, keeps the DIB. |
| `LayeredHostPlacement Place(MonitorInfo, in VisualRect shellDip, LayeredHostMode, ShellForm form = Compact)` | One `SetWindowPos`; rebuilds the DIB if the mode/scale changed. `shellDip` is work-area-relative (Core layout output); `form` selects the dock strip vs expanded-panel local offset. |
| `LayeredHostPlacement PlaceForWindowPlacement(in WindowPlacement, LayeredHostMode)` | Authoritative Core placement seam: origin = `WindowPixel` verbatim (see §2). |
| `static LayeredHostPlacement ComputePlacement(in WindowPlacement, LayeredHostMode)` | Pure placement math (no HWND/thread affinity) used by the seam and pinned by tests. |
| `LayeredHostPlacement PlacePhysical(int xPx, int yPx)` | Moves the window, keeps the surface size (drag/legacy path; the App no longer uses it for placement). |
| `LayeredHostPlacement SetMode(LayeredHostMode)` | Rebuilds the DIB at the other fixed size, keeps the origin, forces a full frame. Re-`Place` afterwards. |
| `LayeredHostPlacement SetMonitor(MonitorInfo)` | Rebuilds the DIB when the scale changed, keeps the origin. |
| `LayeredFrame BeginFrame()` | Opens a frame and returns a zero-copy view over the DIB (`Surface`, `Bits`, `Pixels`, `Pixels32`, `Stride`, `WidthPx`, `HeightPx`, `Scale`, `Index`). Throws if a frame is already open. |
| `LayeredCommitResult CommitFrame()` | Uploads the whole surface (one `UpdateLayeredWindowIndirect` call). |
| `LayeredCommitResult CommitFrame(ReadOnlySpan<LayeredRect> dirtyRects)` | Uploads only the given rectangles (surface pixels). Empty span ⇒ no upload. |
| `LayeredCommitResult CommitFrame(in LayeredRect dirtyRect)` | Single-rect convenience. |
| `void DiscardFrame()` | Closes an open frame without uploading. |
| `void WaitForNextFrame()` | Pacing wait (`DwmFlush` / deadline / none). |
| `void ConfigurePacing(LayeredPacingMode, double fps = 60)` | Replaces the pacer at runtime (settings/refresh change). |
| `LayeredPacingStats GetPacingStats()` | Counters since `Reset()` (frames, late frames, mean/p99/max interval, effective fps). |
| `void SetCursorKind(LayeredCursorKind)` | Cursor for the hover state (arrow/hand/I-beam/size-all/WE/NS). |
| `void ApplyWindowRegionOnSettle(ShellGeometry)` | Optional `SetWindowRgn` via `IslandWindowInterop`; **not required** (alpha clips the pixels, `LayeredHitRegion` clips the clicks) and never per frame. |
| `Dispose()` | Releases capture, destroys the HWND, releases the DIB and the screen DC, disposes the pacer, frees the `GCHandle`. |
| Properties | `Handle`, `IsCreated`, `IsVisible`, `HasPendingFrame`, `Mode`, `SurfaceSizeDip`, `SurfaceSizePx`, `Scale`, `Dpi`, `Monitor`, `Placement`, `Surface`, `Pacer`, `HitRegion`, `Options`, `MergeDirtyRects` (settable: merge vs per-rect upload). |

Events (all `Action<T>` with struct payloads — no per-event allocation):

| Event | Payload |
|---|---|
| `PointerMoved`, `PointerPressed`, `PointerReleased`, `PointerDoubleClicked` | `LayeredPointerEvent(ClientX, ClientY, ClientXDip, ClientYDip, ScreenX, ScreenY, Buttons, Modifiers, Timestamp)` |
| `PointerLeft` | — (`WM_MOUSELEAVE` via `TrackMouseEvent`) |
| `PointerWheel` | `LayeredWheelEvent(Pointer, Delta (multiples of 120), IsHorizontal)` |
| `PointerCaptureLost` | — (capture taken by another window) |
| `Dragged` | `LayeredDragEvent(Phase Started/Moved/Ended/Cancelled, Pointer, DeltaX, DeltaY, StepX, StepY)` in **screen** pixels |
| `LayeredDpiChanged` | `LayeredDpiChange(OldDpi, NewDpi, OldScale, NewScale, SurfaceSizePx)` |

### 3.2 `LayeredSurface : IDisposable`

A 32 bpp **top-down premultiplied BGRA** `CreateDIBSection` plus the memory DC that has it selected —
exactly what `UpdateLayeredWindow(ULW_ALPHA)` consumes.

| Member | Contract |
|---|---|
| `static Create(int widthPx, int heightPx)` | Allocates the section and DC; throws `Win32Exception` on failure. |
| `WidthPx`, `HeightPx`, `Stride` (= `width × 4`), `ByteCount`, `Size` | Geometry. |
| `Bits`, `Hdc`, `DibHandle` | Raw handles (renderers wrap `Bits`; the host passes `Hdc` to the update). |
| `Pixels` / `Pixels32` | `Span<byte>` / `Span<uint>` over the section, row 0 = top. Zero-copy. |
| `Clear()`, `CopyFrom(ReadOnlySpan<byte>)`, `CopyRow(int y, ReadOnlySpan<byte>)` | Bulk writes. |
| `GetPixel(x,y)` / `SetPixel(x,y,uint bgra)` | Bounds-checked single pixels (diagnostics/tests). |
| `Recreate(int widthPx, int heightPx)` | Releases and reallocates at a new size; content is **not** preserved (next commit must be full). |
| `Dispose()` | Restores the old bitmap, deletes the section and the DC; idempotent. |

### 3.3 `LayeredHitRegion`

The click-through policy in client-DIP space: one shell squircle (`Core.Layout.ShellGeometry`, so the
click target is the drawn shape) plus up to 32 detached interactive rectangles (mini-islands). Everything
else — bleed, corners, the lane between pills — is `HTTRANSPARENT`.

`SetShell(in VisualRect, CornerRadii)`, `SetShell(ShellGeometry?)`, `SetInteractiveRects(ReadOnlySpan<VisualRect>)`
(copies into a fixed buffer, no allocation), `Clear()`, `HasInteractiveContent`, `IsInteractive(xDip, yDip)`,
`IsInteractivePixels(xPx, yPx, scale)`, `IsEmpty`, `Shell`, `InteractiveRectCount`.

### 3.4 `LayeredFramePacer : IDisposable`

| Mode | Behaviour |
|---|---|
| `Composition` (default) | `DwmFlush()` — blocks until the compositor presents; the layered window's vsync. No CPU burn. Falls back to `Thread.Yield()` when DWM is unavailable. |
| `TargetFrameRate` | Deadline clock at N fps, waited on with a **high-resolution waitable timer** (`CreateWaitableTimerExW(CREATE_WAITABLE_TIMER_HIGH_RESOLUTION)`) and a bounded `Thread.SpinWait` for the last sub-millisecond. `Thread.Sleep` is deliberately not used (its 15.6 ms granularity would cap a 120 fps loop at ~64 fps). |
| `None` | No wait (measurement baseline). |

`WaitForNextFrame()` counts a *late* frame instead of bursting catch-up frames. `Reset()`,
`GetStats()` (`LayeredPacingStats`), `FramePeriodMilliseconds`, `TargetFramesPerSecond`,
`IsHighResolutionTimerAvailable`.

### 3.5 Options (`LayeredHostOptions`)

`WindowTitle`, `MergeDirtyRects` (default true), `MaxDirtyRects` (16), `MinDirtyAreaPixels`,
`UseExplicitUpdateGeometry` (default false: rely on the window's current geometry; the host falls back
to the canonical explicit `UpdateLayeredWindow` automatically), `ReassertTopmost` (true),
`TopmostReassertInterval` (1 s), `EnableDrag` (true), `EnableWheel` (true), `DefaultCursor`,
`Pacing` (`Composition`), `TargetFramesPerSecond` (60), `LogFrames` (false).

## 4. Frame path (DIB + dirty strategy)

```
BeginFrame() ──► LayeredFrame { Bits, Pixels, Stride, Scale }
      renderer draws premultiplied BGRA straight into the DIB (SkiaSharp surface on Bits)
CommitFrame()            → one UpdateLayeredWindowIndirect (whole surface)
CommitFrame(dirtyRects)  → one UpdateLayeredWindowIndirect with prcDirty = merged bounding box
                           (or N calls when MergeDirtyRects = false)
```

Rules the implementation guarantees:

1. **No allocation per frame.** The DIB, memory DC, pacer, hit region and every update struct are
   created once; the update builds its `POINT`/`SIZE`/`RECT`/`BLENDFUNCTION` on the stack and calls
   `UpdateLayeredWindowIndirect` (a `ReadOnlySpan<LayeredRect>` input is passed through without copying).
   Measured: **1–3 B/frame** (i.e. GC noise) for both paths.
2. **No copy, no readback.** The renderer writes the exact bytes that are uploaded. SkiaSharp creates its
   surface on `LayeredSurface.Bits` (`SKImageInfo(w, h, Bgra8888, Premul)`), so there is no intermediate
   bitmap.
3. **Idle = no uploads.** The host owns no timer and posts no messages to itself; a static island simply
   never calls `CommitFrame`. Measured idle: 0.3–0.5 % of one core over 6 s, 0 uploads.
4. **Full frame when the surface is new.** After `Create`/`Place`/`SetMode`/`SetMonitor`/`WM_DPICHANGED`
   the next commit is forced full (`_needsFullFrame`), because the DIB was reallocated.
5. **Dirty rectangles are clamped** to the surface and dropped when empty (`MinDirtyAreaPixels`); the
   number of uploads per frame is capped by `MaxDirtyRects`.
6. **Merge vs per-rect.** Merging is the default because each `UpdateLayeredWindow` call has a fixed
   cost (~0.1 ms here): in the spike, 2 small rects cost **0.10 ms merged vs 0.28 ms as 2 calls**. Merge
   unless the dirty areas are large and far apart (a full-screen progress bar + a distant pill).
7. **Failure handling.** A rejected indirect update is retried once through the canonical
   `UpdateLayeredWindow` with explicit geometry; if that also fails the commit returns
   `Updated = false`, counts a Win32 failure and forces the next frame to be full. The spike recorded
   **0 fallbacks and 0 failures** across ~1 200 uploads.

## 5. Input contract

All coordinates are **window-client physical pixels** plus the same point in **client DIP**
(`/ scale`). Screen coordinates come from `GetCursorPos`, *not* from `ClientToScreen(client)`: a drag
moves the window while the message is handled, and converting client→screen would feed the window's own
movement back into the delta (observed as a 20 px drift before the fix).

| Message | Behaviour |
|---|---|
| `WM_MOUSEMOVE` | `PointerMoved`; arms `TrackMouseEvent(TME_LEAVE)` once per hover; during a drag also emits `Dragged(Moved)` with cumulative and step deltas. |
| `WM_MOUSELEAVE` | `PointerLeft`. |
| `WM_LBUTTONDOWN` | `PointerPressed`, then `SetCapture` + `Dragged(Started)`. Capture success is verified with `GetCapture() == hwnd` (`SetCapture` returns the *previous* capture, usually NULL). |
| `WM_LBUTTONDBLCLK` | Treated as a press **and** `PointerDoubleClicked` (the class has `CS_DBLCLKS`, so the second click replaces `WM_LBUTTONDOWN`; without this the drag would be silently lost). |
| `WM_LBUTTONUP` | `PointerReleased`, then `Dragged(Ended)` + `ReleaseCapture`. |
| `WM_CAPTURECHANGED` | Cancels the drag (`Dragged(Cancelled)` + `PointerCaptureLost`) **only** when capture moved to another window; re-capturing to ourselves is ignored (Windows sends the message anyway). |
| `WM_MOUSEWHEEL` / `WM_MOUSEHWHEEL` | `PointerWheel` with `Delta` (multiples of 120) and `IsHorizontal`; lParam is converted from screen to client. |
| `WM_SETCURSOR` | Over `HTCLIENT` returns `TRUE` after `SetCursor(cursor)`; otherwise `DefWindowProc`. |
| `WM_MOUSEACTIVATE` | `MA_NOACTIVATE` — clicking never activates the window. |
| `WM_DPICHANGED` | Rebuilds the surface at `ceil(DIP × scale)`, applies the suggested origin/size (fixed surface size), updates `Placement`, then raises `LayeredDpiChanged` so the App's Core re-place wins over the suggestion; forces a full frame. |
| `WM_ERASEBKGND` / `WM_PAINT` | Never paint with GDI: `1` / `BeginPaint`+`EndPaint` (validate only). |
| `WM_NCDESTROY` | Clears `GWLP_USERDATA` before `DefWindowProc`. |
| Any handler exception | Logged + counted; the message falls through to `DefWindowProc` (a managed exception never unwinds through the native proc). |

The host never moves itself. The App applies `Dragged` to the placement (`PlacePhysical(origin + Step)`),
which is what the spike's `DragSupport` does; the window then reports the exact pointer delta.

## 6. Hit-testing / click-through

`WM_NCHITTEST` is answered by the host itself (it owns its window procedure) using
`LayeredHitRegion.IsInteractivePixels`:

- inside the shell squircle → `HTCLIENT`
- inside a detached pill rectangle → `HTCLIENT`
- bleed, corners, the lane between pills, and **everything while hidden** → `HTTRANSPARENT`

**Do not also call `IslandWindowInterop.RegisterHitTest` for this HWND** (double subclassing); pass the
interop instance to the host instead and it will delegate placement/topmost to it. `IslandWindowInterop`
stays the API for non-layered windows.

Evidence (spike, real `WindowFromPoint`, not a simulated check): corner → backdrop window, panel
centre → host, gap between pills → backdrop, pill centre → host, bottom bleed → backdrop; `WM_NCHITTEST`
returns `-1`/`1` for the same points.

## 7. Topmost / focus / lifecycle

- `WS_EX_TOOLWINDOW` (no taskbar entry, no Alt-Tab), `WS_EX_NOACTIVATE` (never foreground),
  `WS_EX_TOPMOST` + one `SetWindowPos(HWND_TOPMOST, SWP_NOACTIVATE)` at create/show.
- Re-assertion while visible is **policy-gated** (≤1 Hz, `TopmostPolicy`): never above the shell
  foreground classes (`Shell_TrayWnd`, `Windows.UI.Core.CoreWindow`, `Xaml_WindowedPopupClass`,
  `MultitaskingViewFrame`, `Progman`, `WorkerW`, `#32768`, `tooltips_class32`, …), never in
  full-screen/presentation/busy states. When an `IslandWindowInterop` is supplied the host calls
  `TryReassertTopmost`; otherwise it evaluates `TopmostPolicy` itself.
- Lifecycle: `Create → Place → Show → frames → Hide/Show → Dispose`; `Dispose` is idempotent; there is
  no `Closing` path (the host never receives one: no system menu, no close box).
- The class is registered once per process and kept for the process lifetime (one small class-name
  allocation); multiple hosts (one per monitor) share it.

## 8. Measured cost (150 % DPI, 2560×1600, Windows 10.0.26200, .NET 10.0.9)

Spike run of 2026-10-18, `tools/spikes/layered-host/results.md` (300-frame loops, warm, unpaced):

| Path | p50 | p95 | p99 | max | frame total p50 | alloc/frame |
|---|---|---|---|---|---|---|
| Full frame, `UpdateLayeredWindow` only (488×384 DIP = 732×576 px) | **0.06–0.16 ms** | 0.22–0.36 ms | 0.39–0.69 ms | 0.65–3.34 ms | 4.5–5.4 ms | 3 B |
| Full frame, shadow off (same surface) | 0.05–0.08 ms | 0.13–0.25 ms | 0.24–1.18 ms | 0.55–4.49 ms | **1.25–1.57 ms** | 3 B |
| Dirty rects, merged (2 rects → 1 box) | **0.08–0.10 ms** | 0.22–0.24 ms | 0.37–0.42 ms | 0.87 ms | **0.15–0.18 ms** | 1 B |
| Dirty rects, 2 separate uploads | 0.19–0.28 ms | 0.43–0.63 ms | 0.62–1.34 ms | 1.61 ms | 0.26–0.38 ms | 1–3 B |
| Dock full frame (488×444 DIP = 732×666 px) | 0.09–0.12 ms | 0.23–0.44 ms | 0.30–1.04 ms | 1.06–2.64 ms | 5.2–5.6 ms | 7 B |

Pacing (the panel is a 165 Hz display: `DwmFlush` returns 164.9 fps):

| Target | Achieved | p99 interval | late frames |
|---|---|---|---|
| 60 fps (deadline + high-res timer) | 60.0 fps | 17.4 ms | 0 |
| 120 fps (deadline + high-res timer) | 120.0 fps | 9.0 ms | 0 |
| Composition (`DwmFlush`) | 164.9 fps | 6.9 ms | 0 |

Idle: **0.3–0.5 % of one core** over 6 s with zero commits. CPU during the unpaced full-frame loop is
~100 % of one core because the loop is deliberately unpaced; the dirty loop at 120 fps sits far below
that (it does ~0.15 ms of work per 8.33 ms budget).

**Reading the numbers.** The upload is cheap (≈0.1 ms); the CPU raster dominates. The drop-shadow
image filter alone costs ≈3 ms/frame (4.5–5.4 ms with it, 1.25–1.6 ms without). Production must bake
the shadow (9-slice or a cached blurred bitmap) and draw it as a blit — that is the single largest
win, not the upload path. With the shadow baked, a full frame is ≈1.3–1.6 ms and a dirty frame
≈0.15–0.18 ms, both comfortably inside an 8.33 ms (120 fps) budget.

## 9. 60 fps acceptance test

Reproduce on the target machine (no admin required; the spike moves the pointer and restores it):

```powershell
dotnet build tools/spikes/layered-host/LayeredHostSpike.csproj
tools/spikes/layered-host/bin/Debug/net10.0-windows10.0.19041.0/LayeredHostSpike.exe --auto
# exit code 0 = every check passed; evidence + results are written next to the project
```

Pass criteria (all are asserted by the spike; `results.md` records the detail):

1. Surface is 732×576 px (float) / 732×666 px (dock) at 150 %.
2. Transparent probes (corner, lane gap, bottom/right bleed) are **unchanged from the pre-host
   baseline** and/or show the checkerboard; the panel/pill probes are island pixels; a saturated
   artwork pixel proves opacity independently of the desktop colour.
3. `WindowFromPoint` returns a window **other than the host** at the corner/gap/bleed, and the host at
   the panel/pill; `WM_NCHITTEST` returns `HTTRANSPARENT`/`HTCLIENT` for the same points.
4. A synthetic click does not change the foreground window and never makes the host foreground.
5. A synthetic 120×80 px drag moves the window by **exactly** the measured pointer delta and does not
   activate the island.
6. `UpdateLayeredWindow` succeeds for every full and dirty frame (0 fallbacks, 0 Win32 failures).
7. Allocation ≤ 1 KB/frame (measured 1–3 B), idle ≤ 1 % of one core with 0 uploads.
8. Paced 60 fps run reaches ≥57 fps with p99 interval ≤20 ms; the 120 fps run reaches ≥100 fps.
9. Dock mode still passes 1–3 at its own surface size.

For a human eyeball check: `--manual` shows the same island on the checkerboard for 60 s.

## 10. Risks and open items

| Risk | Impact | Mitigation |
|---|---|---|
| **Skia shadow filter cost** (≈3 ms/frame) | Full-frame redraws miss the 120 fps budget on slower CPUs | Bake the shadow once per mode/size into a bitmap (or 9-slice it) and blit; the spike measures both paths |
| **CPU raster on the UI thread** | The island competes with App/UI work; no GPU compositor | Keep the animated area dirty-rect small (0.15 ms/frame); consider a dedicated render thread that only uploads on the UI thread |
| `UpdateLayeredWindow` cost varies 2–3× run to run (0.06–0.16 ms p50) on this machine | Budget planning needs headroom | Treat ~0.3 ms p99 as the planning number; re-measure on the target hardware |
| **Core `ShellGeometry.SampleOutline` ordering** was not a plain clockwise polyline (quadrants 0/1/3 emitted edge→corner, quadrant 2 corner→edge) | A naive consumer drew a twisted shape (observed in the first spike render) | **Fixed in Core on 2026-10-18** (parallel change): the sampler now starts on the left edge and returns directly connectable clockwise samples. The spike builds the path in order; `IslandRenderer.BuildSquirclePath` keeps a note so the contract is not regressed. |
| **Detached-island lane constants** (float 76 vs dock 52) were easy to confuse | Host placement and renderer disagreed by 24 DIP (caught by a unit test) | `LayeredSurfaceSize` owns both constants and pins the derivation in tests |
| Layered windows are **not** composited by DWM's GPU path | No real acrylic/blur; a 732×576 surface must be rastered on the CPU | Accepted (quality-first: transparency beats acrylic here); material stays the simulated-glass Tier 1 |
| `DwmFlush` pacing couples to the compositor | On a 60 Hz panel a 120 fps animation is wasted work | `LayeredFramePacer` picks the mode from the settings/refresh; the App should animate on the display clock |
| Synthetic-input evidence depends on a live desktop | Another app (or the user) can steal the pointer mid-test; other topmost windows (the DSH browser) can cover the backdrop | The spike retries the drag, compares against the *measured* cursor delta, and the transparency proof also uses the pre-host baseline (z-order independent) |
| Topmost re-assert while another app is topmost | The island can be covered by a topmost app (observed with the DSH browser) | Policy keeps the island below the shell surfaces by design; a topmost third-party app winning is acceptable |
| One HWND per monitor with a 488 DIP width | A shell wider than 440 DIP does not fit | Fixed by spec §5.6 (the panel is 440 DIP); revisit only if the design changes |
| `WM_DPICHANGED` requires PMv2 | Without the manifest/API call the surface stays at 100 % size | Documented requirement; the host also rebuilds on `SetMonitor`/`Place` |
| No IME/text input in the host | Lookup/search needs a separate small window | Same rule as the Avalonia host (spec §5.6: the island host carries no IME composition window) |

## 11. Fallback note

If the layered host is rejected (e.g. the CPU raster cannot meet the budget on low-end hardware, or a
future Windows build breaks `UpdateLayeredWindow` alpha in this environment), the documented fallbacks
are, in order:

1. **WPF fixed-size layered window** — WPF `AllowsTransparency=true` *is* `UpdateLayeredWindow`, so it
   inherits the same transparency guarantee, at 60 fps and with the same CPU-raster cost; the island
   drawing would be ported to WPF `OnRender`.
2. **Avalonia 11.3 host** — only if an external machine proves `WinUIComposition` transparency works
   there; the change is 4 lines (`WindowDecorations.None` → `SystemDecorations.None`) plus package bumps
   (see [`host-transparency.md`](host-transparency.md) §10.2).
3. **Documented degradation** — keep Avalonia and disable transparency for remote-desktop sessions.

The layered host keeps every Core/Platform contract (layout, placement, hit-test policy, topmost
policy, activity state machine); only the render host changes, so a later switch back does not touch
Core.

## 12. Files

| File | Contents |
|---|---|
| `src/FluidBar.Platform.Windows/Hosting/LayeredWindowHost.cs` | Public API, lifecycle, frame commit, topmost policy, disposal |
| `src/FluidBar.Platform.Windows/Hosting/LayeredWindowHost.Messages.cs` | Window class + procedure, input translation, hit-test, DPI change |
| `src/FluidBar.Platform.Windows/Hosting/LayeredSurface.cs` | DIB section + memory DC abstraction |
| `src/FluidBar.Platform.Windows/Hosting/LayeredTypes.cs` | Modes, sizes, rects, input/commit types, options |
| `src/FluidBar.Platform.Windows/Hosting/LayeredHitRegion.cs` | Squircle + detached-rect click-through policy |
| `src/FluidBar.Platform.Windows/Hosting/LayeredFramePacer.cs` | Composition / deadline / none pacing |
| `src/FluidBar.Platform.Windows/Interop/NativeMethods.cs` | Added: `UpdateLayeredWindow[Indirect]`, DIB/DC, input, `DwmFlush`, waitable timer, `SendMessageW` |
| `tools/spikes/layered-host/**` | Spike app, evidence PNGs, `results.md`/`results.json`, `spike.log` |
| `tests/FluidBar.Platform.Tests/LayeredHostTests.cs` | 34 pure tests (sizes, form-aware offsets, Core placement parity at 100/150/200 %, rects, hit region, pacer, surface) |
| `tests/FluidBar.Ui.Tests/SkiaHostDockParityTests.cs` | Headless Core `HostSurface.Dock` ↔ renderer dock local-rect + placement parity |
| `tests/FluidBar.Ui.Tests/SkiaMonitorPlannerTests.cs` | Headless monitor targeting/add-remove diff for the App host |
| `tests/FluidBar.Platform.Tests/PlatformIntegrationTests.cs` | Guarded real-HWND test (styles, placement, full+dirty upload, hit-test) |

Verification commands:

```powershell
dotnet build FluidBar.sln
dotnet test tests/FluidBar.Platform.Tests/FluidBar.Platform.Tests.csproj --filter "FullyQualifiedName~LayeredHost"
dotnet test tests/FluidBar.Platform.Tests/FluidBar.Platform.Tests.csproj
tools/spikes/layered-host/bin/Debug/net10.0-windows10.0.19041.0/LayeredHostSpike.exe --auto
```
