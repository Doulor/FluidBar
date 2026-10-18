# FluidBar.Core — M1 API contracts

| Item | Value |
|---|---|
| Project | `src/FluidBar.Core/FluidBar.Core.csproj` (`net10.0`, nullable on, XML docs on) |
| Owner | core-owner (M1), integrated by Lead |
| Status | **M1 frozen for integration** (additive changes only after sign-off) |
| Spec | [`docs/rewrite/specs/2026-10-18-fluidbar-v2-design.md`](../specs/2026-10-18-fluidbar-v2-design.md) |
| Tests | `tests/FluidBar.Core.Tests` — 472 xUnit tests, all green |
| Constraints | UI-free, Win32-free, no ambient time/I/O, no unbounded collections, immutable snapshots |

All coordinates are **DIP** unless a name ends in `Pixel`. `FluidBar.Core` references BCL only
(`System.*`); it never P/Invokes, never touches `System.IO`/console, and never references
Avalonia/WPF/WinForms/WinRT. `CoreArchitectureTests` enforces the reference graph, the namespace
map and the confirmed layout tokens.

---

## 1. Namespace map

| Namespace | Responsibility | Key types |
|---|---|---|
| `FluidBar.Core` | Host abstractions shared by all areas | `IDispatcher`, `InlineDispatcher` |
| `FluidBar.Core.Events` | Producer→consumer event pipeline | `IslandEvent`, `IslandEventKind`, `IEventBus`, `EventHub`, `IEventSource`, `EventSourceHost`, `EventBusOptions`, `EventBusMetrics` |
| `FluidBar.Core.Activities` | Domain model, priority/stack/TTL policy, state machine | `ActivityKind`, `ActivityKey`, `ActivityPayload`, `ActivityContentSignature`, `ActivityPriorityPolicy`, `ActivityRecord`, `ActivitySchedulerState`, `ActivityScheduler(Update/Tick/SetPinned/Clear)`, `ActivityStackPolicy`, `ActivitySchedulerUpdate`, `IslandState`, `IslandInput`, `IslandStateMachine.Reduce`, `IslandStateController`, `IslandTransition`, `IslandEffect`, `IslandTransitionReason`, `IslandBehaviorOptions`, `PeekPolicy`, `CollapsePolicy` |
| `FluidBar.Core.Layout` | One coordinate contract, tokens, DPI, host surfaces, squircle | `VisualRect`, `PixelRect`, `VisualPoint`, `MonitorInfo`, `HostSurface`, `HostPlacement`, `WindowPlacement`, `WindowPlacementConverter`, `LayoutTokens`, `IslandMode`, `IslandAnchor`, `ShellForm`, `CornerRadii`, `ShellGeometry`, `IslandLayoutRequest`, `IslandLayoutResult`, `IslandLayout.Calculate`, `StackSlotLayout`, `LayoutHitTarget`, `LayoutHitKind`, `ArchipelagoLayout`, `ArchipelagoLayoutResult`, `ArchipelagoSlot`, `ArchipelagoRevealPolicy`, `ArchipelagoRevealAction` |
| `FluidBar.Core.Motion` | `(T, ζ)` springs, tokens, frame clock | `SpringParameters`, `Spring`, `SpringSet`, `MotionTokens`, `MotionDurations`, `MotionCurves`, `MotionProfile`, `ContentOpacityPolicy`, `SquashStretchPolicy`, `IFrameClock`, `ManualFrameClock`, `MotionLoop`, `SettleTimePredictor` |
| `FluidBar.Core.Presentation` | Activity → UI-free view model | `ActivityPresenter`, `ActivityViewModel`, `IslandViewModel`, `HudViewModel`, `ActivityIconKind`, `AccentRule` |
| `FluidBar.Core.Interaction` | Horizontal page pager (replaces the rejected left rail) | `PagePagerPolicy`, `PagerDragState`, `PagerSnapDecision`, `PagerSnapKind`, `PagerWheelPolicy`, `PagerWheelResult`, `PagerSnapMotion`, `PagerConstants` |
| `FluidBar.Core.Settings` | Versioned, atomic, migratable settings | `FluidBarSettings` (+ nested settings records), `SettingsJson`, `SettingsValidator`, `HotkeyParser`, `ISettingsFileSystem`, `InMemorySettingsFileSystem`, `SettingsStore(Options)`, `SettingsMigrator`, `MigrationReport`, `SettingsDiffCalculator`, `SettingsSaveDebounce`, `SettingsProjection` |
| `FluidBar.Core.Diagnostics` | Structured logs + counters | `IAppLogger`, `LogLevel`, `LogCategories`, `NullAppLogger`, `RecordingAppLogger`, `DiagnosticCounters`, `CounterKind`, `DiagnosticCountersSnapshot` |

Dependency direction inside the assembly: `Diagnostics` ← everything; `Events` → `Activities`
(domain payloads); `Layout` → `Activities` (kind/page); `Presentation` → `Activities`;
`Interaction` → `Motion` (pager snap spring) + `PagerConstants`; `Settings` →
`Activities`/`Layout`/`Motion` (projection); `Motion` and `Diagnostics` are leaves. No cycles.

---

## 2. Events (`FluidBar.Core.Events`)

```csharp
public readonly record struct IslandEvent(
    ActivityKey Key, IslandEventKind Kind, long Sequence, TimeSpan Timestamp,
    ActivityPayload Payload);

public enum IslandEventKind { New, Replacement, Incremental, Retract }
```

Factories: `IslandEvent.New(key,payload,sequence)`, `.Replacement(...)`, `.Incremental(...)`,
`.Retract(key, sequence)`.

`IEventBus` (producers register nothing; consumers subscribe):

```csharp
bool TryPublish(in IslandEvent evt);          // non-blocking; false only after Dispose
IDisposable Subscribe(Action<IslandEvent> h); // push subscriber (fast handlers!)
EventBusMetrics Metrics { get; }
```

`EventHub` (implementation) guarantees:

* bounded FIFO queue (`EventBusOptions.Capacity`, default **256**, drop-oldest, counted);
* same-key `Incremental` coalescing, latest sequence wins; stale sequence discarded
  (`EventsCoalesced` / `EventsDropped` counters);
* `New`/`Replacement`/`Retract` are never coalesced;
* dispatcher marshaling via constructor-injected `IDispatcher` (one posted drain at a time;
  without a dispatcher it drains inline — deterministic tests);
* subscriber isolation: exceptions are logged/`EventSubscriberFaults`-counted, never propagated;
* `ReadAllAsync(readerName, ct)` pull reader with a bounded per-reader channel (drop-oldest);
* `Drain()`/`Flush()` for deterministic host pumping.

Producer lifecycle: implement `IEventSource { string SourceId; void Start(); void Stop(); }`;
`EventSourceHost` starts/stops a set of them and isolates failures.

**Platform integration rules**

* progress/lyrics/position ⇒ `Incremental` (coalesced); track/session change ⇒ `Replacement`;
  first appearance ⇒ `New`; disappearing ⇒ `Retract`.
* `Sequence` must be monotonic per key (`0` = unsequenced, always accepted).
* Never publish from the UI thread with blocking work; `TryPublish` is O(queue) worst case.

---

## 3. Activities (`FluidBar.Core.Activities`)

### 3.1 Identity and payload

```csharp
public enum ActivityKind {
    Lookup, Clipboard, LockKeys, Device, Bluetooth, Focus, Volume, Brightness,
    Notification, Agent, BatteryLow, Progress, Timer, Media, Idle }

public readonly record struct ActivityKey(ActivityKind Kind, string Source);
// ToString(): "kind:source"; invalid default struct has IsValid == false.
```

`ActivityPayload` is the immutable content snapshot shared by all kinds:
`Title`, `Subtitle`, `Body`, `Badge`, `SourceName`, `IconToken`, `AccentColorHex`,
`ProgressPercent` (0..100), `ProgressIndeterminate`, `IsActive`, `ShowsAudioWave`,
`AlbumArtPath`, `AppIconPath`, `LyricLine`, `SecondaryLyricLine`, `DetailLines`,
`PositionTicks`, `EndTicks`, `VerificationCode`, `Level` (HUD 0..100), `Ttl`, `IsPinned`,
`PreferredPage`. `ActivityPayload.Merge(patch)` implements Incremental semantics (non-null wins).

`ActivityContentSignature` = key hash + kind + title hash + body hash + 5-point progress bucket
(`ContentSignaturePolicy.Compute`). Equal signature ⇒ discard with zero work.

### 3.2 Priority / TTL / persistence (`ActivityPriorityPolicy`)

| Kind | Rank | TTL | Persistent |
|---|---|---|---|
| Lookup | 1 | manual | – |
| Clipboard | 2 | 4 s | – |
| LockKeys | 3 | 1.6 s | – |
| Device | 4 | 3 s | – |
| Bluetooth | 5 | 3 s | – |
| Focus | 6 | manual | ✔ |
| Volume | 7 | 1.6 s | – |
| Brightness | 8 | 1.6 s | – |
| Notification | 9 | 5 s | – |
| **Agent** | **10** | 5 s | – |
| BatteryLow | 11 | manual | ✔ |
| Progress | 12 | manual | ✔ |
| Timer | 13 | manual | ✔ |
| Media | 14 | manual | ✔ |
| Idle | 15 | – | default surface |

The spec's 14-activity table is preserved in relative order; `Agent` is inserted at rank 10 per
the lead's priority brief (`user-initiated discrete feedback > notification/agent > media >
ambient system > clock`). All other ranks shift by +1 from battery downward. Settings can override
TTLs per kind (`ActivityStackPolicy.TtlOverrides`).

### 3.3 Scheduler (`ActivityScheduler`)

```csharp
ActivitySchedulerUpdate Update(ActivitySchedulerState state, in IslandEvent evt, DateTimeOffset now);
ActivitySchedulerUpdate Tick(ActivitySchedulerState state, DateTimeOffset now);        // TTL expiry
ActivitySchedulerUpdate SetPinned(state, ActivityKey key, bool pinned);
static ActivitySchedulerUpdate Clear(ActivitySchedulerState state);                    // "clear all"
```

`ActivitySchedulerState` (immutable): `Visible` (primary first, then ghosts), `Background`
(hidden live activities / `+N` badge source, bounded), `Primary`, `ActivityCount`, `BadgeCount`,
`ReplacedCount`, `ExpiredCount`, `DroppedCount`, `SwitchedCount`, `Find(key)`.

`ActivityStackPolicy`: `Stacking` (`Stacked` = primary + up to 2 ghosts, `LatestOnly` = 1 layer),
`MaxVisible`=3, `MaxBackground`=8, media/persistent retention, `TtlOverrides`.

`ActivitySchedulerUpdate`: `State`, `Kind` (`None/Added/ReplacedInPlace/ContentUpdated/Preempted/
Demoted/Expired/Retracted/PinnedChanged/Cleared`), `Key`, `ActivityKind`, `ContentChanged`,
`PrimaryChanged`, `RemovedCount`, `ShouldNudge` (Added/Preempted/ReplacedInPlace).

Rules implemented (all unit-tested):

1. same key ⇒ in-place update (no re-entry, position kept);
2. higher rank preempts; preempted **persistent** kind demoted to the background layer,
   preempted **transient** kind discarded (never lingers as a ghost);
3. equal rank ⇒ later replaces former (`ReplacedCount++`);
4. background queue bounded (8): overflow drops oldest non-persistent first;
5. pinned activities never expire/preempt and sort first;
6. expiry is time-driven (`Tick`) and never replays an entry animation — the resulting
   `ActivitySchedulerUpdate.Kind == Expired` plus `ContentChanged` drives the state machine's
   `UpdateContent`/`Retarget(hide)` path;
7. stale sequences discarded.

Always call `Tick` from the host scheduler (≥1 Hz is enough; TTL granularity is 100 ms).

### 3.4 Island state machine (`IslandStateMachine`)

```csharp
IslandTransition Reduce(IslandState state, IslandInput input,
                        IslandBehaviorOptions options, DateTimeOffset now);
```

`IslandStateKind`: `Hidden / Compact / Peek / Expanded / Closing`.
`IslandState` is a fully explicit immutable record: `Kind`, `Page`, `PrimaryKey`, `ContentKind`
(null = idle), `IsPinned`, `ActivityCount`, `BadgeCount`, `IsSuppressed`, `IsDragging`,
`IsHovering`, `ClosingReturnKind` (explicit collapse target), `DeferredInputs`, and optional
deadlines (`HoverIntentDeadline`, `LeaveDeadline`, `PeekDwellDeadline`, `AutoCollapseDeadline`,
`AutoHideDeadline`).

`IslandInput` value-type envelope with factories: `ForActivityUpdate(ActivitySchedulerUpdate)`,
`PointerEntered/Left`, `HoverIntentElapsed`, `LeaveDebounceElapsed`, `PeekDwellElapsed`,
`PrimaryClick`, `OutsideClick`, `Escape`, `ToggleHotkey`, `HideHotkey`, `ShowHotkey`,
`AutoCollapseElapsed`, `AutoHideElapsed`, `CloseSettled`, `SuppressionChanged(bool)`,
`DisplayChanged`, `ModeChanged`, `SettingsChanged`, `PageRequested(page)`,
`PinnedChanged(bool)`, `DragStarted`, `DragEnded`.

`IslandTransition`: `Previous`, `State`, `Reason` (explicit `IslandTransitionReason`), `Effects`
(ordered data-only `IslandEffect { Kind, Data }`). Effect kinds: `Relayout`, `ShowWindow`,
`HideWindow`, `StartFrameLoop`, `StopFrameLoop`, `SnapSprings`, `Retarget` (data = spring token,
see `IslandEffectData`), `PlayNudge`, `UpdateContent`, `UpdateTray`, `SyncClickThrough`,
`FocusRail`, `PersistPlacement`, `PauseProducers`, `ResumeProducers`.

Host obligations:

* schedule host timers from the deadlines in the returned state, then feed the matching
  `*Elapsed` input (the reducer is pure and never reads a clock itself);
* execute effects in order on the dispatcher; `Retarget` data maps to `MotionTokens`;
* when the collapse spring settles, send `IslandInput.CloseSettled()` — this also replays
  deferred inputs (bounded to 8, oldest dropped) and returns to `Compact` (idle pill) or `Hidden`
  depending on `ClosingReturnKind`.

Transition coverage (tests): T1 Hidden→Compact (nudge, no expand animation), T2/T3 hover
intent (90 ms) / leave debounce (120 ms) Peek (+10/+6), T4 click/hotkey→Expanded (independent
width+height retargets), T5→Closing (Esc/outside/auto-collapse/activity-end; pinned blocks
outside/auto paths but not Esc/hotkey), T6/T7 CloseSettled, T8 auto-hide/hide-hotkey→Hidden,
T9 suppressed arrivals stay hidden, T10 env changes snap, T11 expanded content updates never
move the shell (no `Relayout`/`Retarget` effects), T12 click-while-closing re-targets to
Expanded. Expanded is a **uniform 440×284** shell for every page — page switches only emit
`UpdateContent` + `Retarget(tab)`, never a height change.

`IslandStateController` is the thread-safe observable wrapper (`Current`, `Apply(input, now)`,
`Reset(state)`, `Transitioned` event, optional dispatcher marshaling). Pure reducers remain
directly callable in tests.

`IslandBehaviorOptions` maps settings: `Enabled`, `HoverPeek`, `HoverReveal`, `AutoCollapseMs`
(default 2600), `AutoHideMs` (default 0 = persistent idle pill), `HoverIntentMs`=90,
`LeaveDebounceMs`=120, `PeekDwellMs`=2400, `ReducedMotion`, `ExpandOnClick`,
`MaxDeferredInputs`=8. Use `SettingsProjection.ToBehaviorOptions(settings, systemReducedMotion)`.

---

## 4. Layout (`FluidBar.Core.Layout`)

### 4.1 Coordinate contract

* Everything visual is `VisualRect` (double DIP), expressed **work-area relative**
  (origin = monitor work-area top-left).
* `MonitorInfo.Create(id, workArea: PixelRect, dpi, bounds?, isPrimary)`; all physical fields
  are Win32 pixels; `Scale = Dpi/96`; `WorkAreaWidthDip/HeightDip` expose the DIP work area.
* Exactly **one** DIP→pixel conversion exists: `WindowPlacementConverter`.
  `ToPhysical(rect, monitor)` returns absolute pixels; the mode-aware
  `PlaceWindow(shell, monitor, hostSurface, form, anchor, offsetY)` is the only placement path.
* **Host surface vs shell**: the layered window is created at the fixed per-mode `HostSurface`
  size; the shell is still the uniform **440×284** expanded panel (or the compact pill) and lives
  at `WindowPlacement.ShellLocalOffset` inside the host. The host owns the transparent shadow
  bleed **and** the detached Page Archipelago strip below the shell, so its bottom padding is
  asymmetric (24 DIP top/sides, 36 DIP below the strip).
* Host surfaces: **float/flush 488×384 DIP**, **dock 488×444 DIP**. The legacy
  `LayoutTokens.WindowHeight` (`332`) is retained only as the pre-archipelago shell-envelope
  alias; it is **not** the window height any more. Use `HostSurface` /
  `WindowPlacement.WindowSizeDip`.
* `WindowPlacement.WindowPixel` is `ceil(width × scale)` × `ceil(height × scale)`; the origin is
  the monitor work-area origin plus the DIP host origin × scale. Padding is applied on **both**
  axes exactly once — the v1 F-04 regression (X compensated, Y not) is covered by the layout
  matrix and dedicated bottom-anchor tests.

### 4.2 Tokens (`LayoutTokens`)

| Group | Values |
|---|---|
| Expanded shell | **440 × 284 uniform for all pages**; **no rail** (superseded by the horizontal pager); content **400×252** at inset (20,16); shell padding 24 |
| Host surface | **488×384** float/flush, **488×444** dock; top/side padding **24**, bottom padding **36**; float/flush bottom allowance **76** (gap 10 + strip 30 + padding 36); dock bottom allowance **136** (dock gap 8 + dock strip 52 + 10 + 30 + 36); `HostSurfaceWidth=488`, `HostSurfaceFloatHeight=384`, `HostSurfaceDockHeight=444` |
| Page Archipelago | strip **352×30** at host-local (68,318) float/flush and (68,378) dock; 6 slots; mini **48×26**, active **72×30**, gap **8**, pitch **56**, radius **13**; track width **352**; hot zone **12**; reveal **300 ms**, hide **1200 ms** |
| Compact | idle **132×36**, media **226×40**, HUD volume/brightness **208×40**, notification 248×36, clipboard 264×36, agent 236×36, timer 190×36, battery 208×36, lookup 480×44, lockkeys 180×36, device 240×44, bluetooth 280×44, focus 220×36, progress 236×36 |
| Dock | strip 440×52, expanded gap 8 above the strip, grows upward; dock host surface adds the 52 strip + 8 gap (**+60 DIP**) |
| Peek | +10 w / +6 h (dock: no shell growth — icon scale only) |
| Stack ghosts | dy −6/−12, scale .96/.92, opacity .55/.35 (max 2) |
| Radii | expanded 28 (dock 16, flush top 0 / bottom 22); compact h/2 capped 44 (dock 12); card 12, row 8, button 10, art 8/12 |
| Insets/gaps | compact 12, content 16, group 20; gaps 4/8/10/16; hit min 32 (transport 36); focus ring 2/2; hairline 1 |
| Offsets | float offsetY 12, flush 0, dock host bottom 4 DIP above the work-area bottom; bottom/dock anchors are host-clamped (see §4.4) |
| Shadow | float 0/8 blur 24 @35 %; dock 0/4 blur 12 @28 % (data only, drawn by the renderer) |
| Hidden | 10 DIP hover strip (can be disabled), shell empty |
| UI state | `FillOpacity` 0.96 (min 0.85), `BloomStrength` 0–2 |

### 4.3 Layout calculation

```csharp
var request = IslandLayoutRequest.FromState(state, mode: IslandMode.Float,
    anchor: IslandAnchor.TopCenter, offsetX: 0, offsetY: double.NaN,
    stacking: StackingStrategy.Stacked, hiddenHoverStrip: true,
    pagePosition: pager.PagePosition, archipelagoVisible: stripVisible)
    with { WidthOverride = animatedWidth, HeightOverride = animatedHeight }; // optional
IslandLayoutResult result = IslandLayout.Calculate(request, monitor);
```

`IslandLayoutRequest` additions for the archipelago: `PagePosition` (fractional pager position,
0..5; default 0) and `ArchipelagoVisible` (default true). They never change the shell rect.

`IslandLayoutResult`: `Form`, `Mode`, `Anchor`, `IsVisible`, `Shell`, `Content` (usable; expanded =
**400×252 at (20,16) inset**, compact = shell), `Rail` (**obsolete/unused, always empty**),
`Radii`, `Geometry`
(`ShellGeometry`, superellipse), `Placement` (`WindowPlacement`), `HostSurface`,
`Archipelago` (`ArchipelagoLayoutResult?`, expanded only), `BottomBand` (transparent host band
below the expanded shell envelope; constant per mode even when compact), `StackSlots`
(`StackSlotLayout[]`, index 0 = main, ghosts after), `HoverStrip`
(hidden only), `ExpandDirection` (Down for float/flush, Up for dock), `AnchorPoint`, `HasRail`
(always false), `GhostCount`, and `HitTest(x, y)` (see §4.6).

`WindowPlacement`: `Shell` (host-adjusted shell), `WindowVisual` (fixed per-mode host rect in
DIP), `WindowPixel` (the only physical rect), `Scale`, `Padding` (24 top/side), `ShellLocalOffset`,
`WindowSizeDip` (per-mode host size), `HostSurface`, `ArchipelagoStrip`, `DockStrip`,
`BottomBand`, `IsClamped`, `BottomAllowance`.

`ShellGeometry`: superellipse with `k = 2 + (1 − rr/m)·1.22` (`m = min(w,h)/2`), `Contains(x,y)`
for `WM_NCHITTEST` and `SampleOutline(samplesPerQuadrant=15)` for drawing; one geometry instance
is shared by drawing, clipping and hit-testing.

Anchors accepted by the layout: `TopLeft/TopCenter/TopRight/BottomLeft/BottomCenter/BottomRight`.
Top anchors keep the shell at its configured offset; the host may overhang the work area by the
transparent 24 DIP bleed. Bottom and dock anchors anchor the **host** (not the shell) to the work
area so the detached strip stays on-screen; the shell is lifted by the mode's bottom allowance.
Float/flush bottom anchors place the host bottom at `work.Height − offsetY` (flush: 0); dock
places the host bottom at `work.Height − 4`. Relative to the host bottom, the dock strip sits
76 DIP above it (80 DIP above the work-area bottom) and the expanded panel 136 DIP above it
(140 DIP above the work-area bottom). Offsets are clamped so the shell always stays inside the
work area.

Test matrix: **3 modes × 6 anchors × 3 DPIs (96/144/192) × 2 stacking strategies ×
{compact, expanded}** = 108 parameterized cases, plus a **54-case archipelago matrix**
(3 modes × 6 anchors × 3 DPIs) and focused tests for peek, hidden strip, no-rail content rect
(400×252 @20/16, `Rail` empty), bottom allowance, dock clamp, anchor math, strip slots,
follow offset, reveal/hide thresholds, hit-test exclusion and the F-04 bottom-anchor regression.

### 4.4 Host surface geometry and anchors

`HostSurface` is an immutable record with one instance per mode (`HostSurface.Float`,
`.Flush`, `.Dock`, or `ForMode(mode)`). All rectangles below are host-local DIP.

| Property | Float/flush | Dock |
|---|---|---|
| `Width` / `Height` | 488 / **384** | 488 / **444** |
| `SidePadding` / `TopPadding` / `BottomPadding` | 24 / 24 / 36 | 24 / 24 / 36 |
| `ShellEnvelopeLocal` | (24,24,440,284) | (24,24,440,284) |
| `ShellBottomLocal` | 308 | 308 |
| `DockStripLocal` | empty | (24,316,440,52) |
| `ArchipelagoStripLocal` | (68,318,352,30) | (68,378,352,30) |
| `BottomAllowance` | **76** | **136** |
| `BottomBandLocal` | (0,308,488,76) | (0,308,488,136) |

Shell local offset (`Place`):

| Form / anchor | Local offset |
|---|---|
| float/flush, top anchors | (24, 24) — shell grows down |
| float/flush, bottom anchors | (24, `H − BottomAllowance − shellHeight`) — shell grows up |
| dock, expanded | (24, 24) — panel above the dock strip |
| dock, compact/peek/closing | (24, 316) — the 440×52 strip is the shell |

Horizontal local offset is anchor-aware: left anchors inset 24, right anchors leave 24 on the
right, centre anchors centre the shell in the host. Shells wider than the padded envelope (the
480 DIP lookup pill) are centred, so they never overflow the host.

`Place(shell, work, form, anchor, offsetY)` returns a `HostPlacement` with the adjusted `Shell`,
`WindowVisual`, `ShellLocalOffset`, `ArchipelagoStrip`, `DockStrip`, `BottomBand` and `IsClamped`.
`PlaceHoverStrip(...)` handles the hidden state (the 10 DIP strip is pinned to the host edge).

Measured examples (expanded, `PagePosition = 2.5`, 96 DPI, work area 1920×1040):

| Case | Host (DIP) | Host pixel | Shell | Strip |
|---|---|---|---|---|
| float top-center | (716,−12,488×384) | (716,−12,488×384) | (740,12,440×284) | (784,306,352×30) |
| float bottom-center | (716,644,488×384) | (716,644,488×384) | (740,668,440×284) | (784,962,352×30) |
| flush top-center | (716,−24,488×384) | (716,−24,488×384) | (740,0,440×284) | (784,294,352×30) |
| flush bottom-center | (716,656,488×384) | (716,656,488×384) | (740,680,440×284) | (784,974,352×30) |
| dock (any anchor) | (716,592,488×444) | (716,592,488×444) | (740,616,440×284) | (784,970,352×30) |

At 150 % (144 DPI, work area 1706.67×933.33) the same float top-center case is
host `(609.33,−12,488×384)` → pixel `(2834,−18,732×576)`; dock is `(609.33,485.33,488×444)`
→ pixel `(2834,728,732×666)`. At 200 % (192 DPI) they are `976×768` and `976×888` px.

### 4.5 Detached Page Archipelago (`ArchipelagoLayout`)

The archipelago is a filmstrip of six detached mini-islands that floats **outside** the expanded
shell, in the host's transparent bottom band. It is not part of `Content` and never overlaps the
panel.

```csharp
// The host uses the placement overload every time it recomputes the strip (pure geometry):
ArchipelagoLayoutResult archipelago =
    ArchipelagoLayout.Calculate(pagePosition, layout.Placement, isVisible: stripVisible);

// Standalone (tests/tools): computes the expanded host placement first.
var arch = ArchipelagoLayout.Calculate(2.5, HostSurface.ForMode(mode), monitor,
    anchor: IslandAnchor.TopCenter, offsetY: double.NaN, isVisible: true);
```

`ArchipelagoLayoutResult`: `PagePosition` (clamped 0..5), `ActivePage` (rounded away from zero),
`Strip` (352×30 work-area DIP), `Slots` (`ArchipelagoSlot[]`, `Page`/`Rect`/`IsActive`),
`ActiveIndicator` (continuous 72×30 pill), `HotZone` (bottom 12 DIP of the shell), `FollowOffset`
(active pill offset from its discrete slot, up to ±28 DIP), `HostSurface`, `IsVisible`, and
`HitTest(x,y)` / `ContainsSlot(x,y)` / `SlotRect(page)`.

Slot geometry: six slots fill the 352 DIP strip; exactly one is 72×30, the other five are 48×26
and vertically centred. `Slots[0].X == Strip.X`, `Slots[5].Right == Strip.Right`. The active
indicator follows the fractional pager position at `SlotPitch = 56` DIP per page, clamped to the
strip; `ActiveIndicator.X = clamp(Strip.X + pagePosition × 56, Strip.X, Strip.Right − 72)`.
`ActivePage` is `round(pagePosition)` (away from zero), so 2.5 activates page 3 and
`FollowOffset = −28`.

Reveal/hide thresholds are pure policy (`ArchipelagoRevealPolicy`), with host-owned timers:

| Rule | Value |
|---|---|
| Hot zone | bottom **12 DIP** of the expanded shell (`ArchipelagoRevealPolicy.HotZone(shell)`) |
| Reveal | pointer in hot zone for **300 ms** (`RevealDelayMs`) |
| Hide | **1200 ms** after the last drag/wheel/pointer activity (`HideDelayMs`) |
| Collapse | immediately hides; a running strip drag blocks auto-hide |
| API | `ArchipelagoRevealPolicy.Evaluate(expanded, pointerInHotZone, hotZoneDwell, isVisible, isDragging, sinceLastActivity)` → `None / Show / Hide` |

### 4.6 Hit-testing the host (transparent strip area)

The host must return `HTCLIENT` only for the shell squircle and the six mini-island pills; every
other point in the host surface — the 24 DIP shadow bleed, the gaps between pills, the strip
background and the dock strip band — is `HTTRANSPARENT`. The single shared helper is
`IslandLayoutResult.HitTest(x, y)` (work-area DIP), which returns a `LayoutHitTarget`:

```csharp
var hit = layout.HitTest(dipX, dipY);
return hit.Kind switch
{
    LayoutHitKind.Shell => HTCLIENT,
    LayoutHitKind.ArchipelagoSlot => HTCLIENT,   // hit.SlotIndex = 0..5
    _ => HTTRANSPARENT,                          // includes BottomBand / strip gaps
};
```

`LayoutHitKind` is `None / Shell / ArchipelagoSlot`; `LayoutHitTarget.IsClient` is the boolean
shortcut. The archipelago is only hit-testable while `ArchipelagoVisible`/`IsVisible` is true;
when hidden, even the pills return `None`. The shell geometry used for hit-testing is the same
`ShellGeometry` instance used for drawing and clipping.

### 4.7 Layered host + Skia renderer consumption recipe

The Win32 layered host (`UpdateLayeredWindow`, 32bpp premultiplied DIB) and the Skia renderer are
consumers of this contract; they must not re-derive geometry.

```csharp
// 1. One layout per anchor/mode/display/state change (never per animation frame).
var request = IslandLayoutRequest.FromState(state, settings.IslandMode, placement.Anchor,
    placement.OffsetX, placement.OffsetY, settings.Stacking,
    hiddenHoverStrip: settings.HoverReveal,
    pagePosition: pager.PagePosition,
    archipelagoVisible: archipelagoReveal.IsVisible);
var layout = IslandLayout.Calculate(request, monitor);

// 2. Create/move the fixed host window once. WindowPixel is the only physical rect.
var host = layout.HostSurface;                       // 488×384 or 488×444 DIP
var px = layout.Placement.WindowPixel;               // e.g. (2834,728,732×666) @150 %
// CreateWindowEx(..., px.Width, px.Height) / SetWindowPos(px) only here.

// 3. Render into the DIB in window-local DIP (Skia canvas scaled by monitor.Scale):
//    Translate work-area DIP -> window-local DIP:
var origin = layout.Placement.WindowVisual;          // DIP host rect
canvas.Translate((float)-origin.X, (float)-origin.Y);
canvas.Scale((float)layout.Placement.Scale);         // if the DIB is physical pixels

// 4. Draw the shell with the shared squircle geometry:
var outline = layout.Geometry.SampleOutline(15);
// Fill/stroke the superellipse at layout.Shell; clip content to layout.Content.
// Content strip: page i at content.X + i*400 - snap.CurrentOffsetDip.

// 5. Draw the detached strip from the same pure geometry (only when visible):
if (layout.Archipelago is { IsVisible: true } arch)
{
    foreach (var slot in arch.Slots)
    {
        // slot.Rect is work-area DIP; draw 48×26 or 72×30 pill at slot.Rect.
    }
    // Active indicator (72×30) at arch.ActiveIndicator follows the pager continuously.
}

// 6. WM_NCHITTEST: client only for shell + pills, transparent everywhere else.
var hit = layout.HitTest(clientDipX, clientDipY);
return hit.IsClient ? HTCLIENT : HTTRANSPARENT;
```

Rules for the renderer:

* The window is **never resized or moved per frame**; only `Shell`, `Content`, slot rects,
  opacity/scale/translate are animated inside the fixed host. `WidthOverride`/`HeightOverride`
  change the visual shell only (the anchored edge stays fixed, the window does not move).
* `Placement.WindowVisual` and all layout rects are work-area DIP; the Skia canvas is translated
  by `−WindowVisual.X/Y` and scaled by `Placement.Scale` when drawing into a physical DIB.
* The transparent bottom band (`Placement.BottomBand`) and the strip background are
  hit-test-through; do not paint an opaque scrim there.
* Use `ArchipelagoLayout.Calculate(pagePosition, placement, isVisible)` for the per-frame active
  pill position; it is pure and deterministic (no clock, no UI, no allocation-heavy work).
* `SetWindowRgn` is unnecessary for a per-pixel-alpha layered host; rely on `UpdateLayeredWindow`
  alpha plus the `HTTRANSPARENT` policy above. Region clipping is not a per-frame mechanism.

---

## 5. Motion (`FluidBar.Core.Motion`)

### 5.1 Springs

`SpringParameters(periodSeconds T, dampingRatio ζ)` derives `k=(2π/T)²`, `c=4πζ/T`.
`Spring` is integrated with **fixed 1/300 s substeps**, semi-implicit Euler; `dt` clamped to
`[1 ms, 50 ms]`.

`MotionTokens` (frozen): `Expand 0.36/0.82`, `ExpandHeight 0.39/0.86`, `Collapse 0.30/1.00`,
`Morph 0.28/0.85`, `Hover 0.24/0.70`, `Press 0.18/0.65`, `Tab 0.34/0.92`, `Nudge 0.26/0.55`,
`Hide 0.30/0.95`. `MotionDurations`: fadeIn 180, fadeOut 120, crossFade 220, stagger 32,
hoverIntent 90, pressFeedback 90, leaveDebounce 120, peekDwell 2400, toastHold 3200,
materialSwap 180, spin 600, **hard max 2000 ms**. `MotionCurves`: enter/exit/move/overshoot cubic
beziers.

Retarget rules (exact tests): keep 100 % velocity when the direction is unchanged; ×0.35 when
reversed; clamp `|v| ≤ 0.2·|Δ|/dt`; snap when `|Δ| < 0.5 DIP && |v| < 1`; `↓
from sleep ⇒ SnapTo`. Settle detection: `|x−target| < 0.04 DIP && |v| < 0.35 DIP/s` for 2
consecutive frames ⇒ snap + zero velocity + `IsAtRest` (frame loop stops; idle = 0 frames).

`SpringSet` steps many springs in one loop (`AllAtRest` short-circuits);
`IFrameClock` is the frame-source abstraction (`Start(onFrame)/Stop`, dt supplied). Production:
composition frame callback; fallback: render-priority dispatcher timer; tests/headless:
`ManualFrameClock.Pump(delta)`. `MotionLoop` steps a `SpringSet` per frame, raises `Stepped`,
and stops the clock automatically when everything is at rest.

Settling tests: `SettleTimePredictor` computes the reference 2 % settle time with an independent
RK4 integration; `Spring` (Euler, 240 Hz measurement) must land within **±20 %**. Note: the
spec 4.1 `t₂%` column is a rounded `0.622·T/ζ` approximation that under-estimates strongly damped
tokens (e.g. hide 0.20 vs real ≈0.26); Core documents and tests against the accurate reference.

Derived policies: `ContentOpacityPolicy` (`alpha = clamp((h − 0.62·h_page)/(0.35·h_page),0,1)`,
compact `1 − 1.9·alpha`), `SquashStretchPolicy` (velocity-driven, clamped),
`MotionProfile` (reduced motion ⇒ ζ=1, T×0.5, nudge 0, no bloom/spin; `Strength` 0.5–1.5 scales
T; non-spring durations halved and clamped to 2000 ms).

---

## 6. Presentation (`FluidBar.Core.Presentation`)

`ActivityPresenter.Present(ActivityRecord)` → `ActivityViewModel`:
`Key`, `Kind`, `Icon` (`ActivityIconKind`), `Accent` (`AccentRule`), `KindLabelKey`
(localization key, e.g. `Activity.Media`), `Title/Subtitle/Body/Badge/SourceName`,
`Page`, `ProgressPercent` (clamped; media derives % from `PositionTicks/EndTicks`),
`ProgressIndeterminate`, `ShowsWave` (media + active), `IsActive`, `PriorityRank`, `IsPinned`,
`StackIndex`, `IsGhost`, `AccentColorHex`, artwork/icon paths, `LyricLine`,
`SecondaryLyricLine`, `DetailLines`, `Signature`.
`ActivityPresenter.PresentIsland(schedulerState, islandState)` → `IslandViewModel`
(primary, up to 2 ghosts, overflow/badge, pinned, `Hud`); `PresentHud` → `HudViewModel`
(label key + fallback, value 0..100, muted, icon level 0..3, invariant numeric text).
No UI types, colors or icons are constructed — the App maps enums/keys to resources.

---

## 7. Settings v2 (`FluidBar.Core.Settings`)

### 7.1 Model

`FluidBarSettings` (record, `SchemaVersion = 2`) with nested immutable records:
`GeneralSettings`, `AppearanceSettings` (+`AccentSettings`), `IslandSettings`, `DockSettings`,
`PlacementSettings` (+`MonitorPlacement`), `HotkeySettings`, `ActivitySettings`
(+`ActivityFeatureSettings` per kind, `.For(kind)`), `PluginsSettings`, `I18nSettings`,
`MarketSettings`, `AdvancedSettings`. Enums serialize with spec tokens
(`float/flush/dock`, `top-center`, `battery.low`, `dark`, `system`, `stack/latest`, ...);
`IslandMode`/`IslandAnchor` use dedicated converters so the Layout enums stay JSON-agnostic.
`SettingsJson.Options` = camelCase, string enums, comments/trailing commas tolerated,
nulls omitted, numbers readable from strings. There are **no per-page heights** in settings —
the expanded shell is uniform.

`PlacementSettings.Resolve(monitorId, isPrimary)` falls back to the `primary` key.
`SettingsProjection` maps settings → `IslandBehaviorOptions`, `ActivityStackPolicy`,
`MotionProfile`, `MonitorPlacement` and `IslandLayoutRequest`
(`ToBehaviorOptions/ToStackPolicy/ToMotionProfile/ToLayoutRequest/ResolvePlacement/TtlFor/
IsActivityEnabled`), including the tri-state reduced-motion resolution.

### 7.2 Atomic store

`SettingsStore(ISettingsFileSystem, SettingsStoreOptions, IAppLogger, DiagnosticCounters,
TimeProvider)`:

* `Load()` → `SettingsLoadStatus.{Loaded, DefaultsCreated, MigratedFromV1,
  RecoveredFromBackup, FailedDefaults}` + validation report (+ migration report). Future schema
  versions are never overwritten; corrupt files fall back to `settings.json.bak`.
* `Save(settings)` → normalize, serialize, `CreateDirectory` → write `settings.json.tmp` →
  `ReplaceFile(tmp, target, .bak)`; if replace is unavailable it logs
  `settings.save.replaceFallback` and moves with overwrite. Failures return
  `Success=false` + `Error` and log `settings.save.failed` (counted).
* `Export(path, settings)` writes `{schemaVersion, exportedAt, settings}` (no secrets) atomically;
  `Import(path, current)` parses envelope/raw v1/v2, migrates, validates and returns
  `SettingsImportPreview { Settings, Diff, Migration, Validation }` **without writing**.
* `SettingsSaveDebounce` (500 ms) is a pure policy the host ticks with its `TimeProvider`.
* `ISettingsFileSystem` is the only I/O surface; `InMemorySettingsFileSystem` provides
  deterministic tests and fault injection (per-operation failures, recorded op log).

### 7.3 v1 migration (`SettingsMigrator`)

`DetectVersion(json)` → `null` (legacy v1), `1/2/…`, or error. `MigrateV1(json)` returns
normalized settings + `MigrationReport { Mapped, Dropped, Failed, Success, Error }`; no field is
silently ignored. Mapping (spec 8.2):

| v1 | v2 |
|---|---|
| `Position` (Top/TopCenter/TopLeft/…/BottomRight) | `island.anchor` **and** `placement.monitors.primary.anchor` |
| `OffsetX`/`OffsetY` | `placement.monitors.primary.offset` |
| `Opacity` (>1 percent) | `appearance.fillOpacity` (÷100, clamped) |
| `Theme`/`SettingsTheme` | `appearance.theme` |
| `AccentColor` (default `#0A84FF` ⇒ system) | `appearance.accent.mode/customHex` |
| `AutoHideDelayMs` | `island.autoCollapseMs` |
| `DisplayStrategy` (`LatestOnly`/`Multiple`) | `island.stacking` (`latest`/`stack`) |
| `HoldToHideKey` (`Disabled` ⇒ null) | `hotkeys.holdToHide` |
| `HideTrayIcon` | `general.trayIcon` (inverted) |
| `AutoUpdateCheck` | `general.autoUpdate` (`notify`/`off`) |
| `ClipboardEnabled` | `activities.clipboard.enabled` |
| `PluginEnabled` ids | `plugins.enabled` (`builtin.*` mapping, unknown ids preserved + `orphaned`) |
| `MonitorFeatureSettings.*` (`Enabled` absent ⇒ true, audit F-19) | `activities.<kind>.enabled/durationMs/detectVerificationCode` |
| `CornerRadius`, `AlwaysOnTop`, `AlwaysVisible`, `CollapsedWidth/Height`, `Expanded*`, `Background*`, `RimMode`, `MaxVisibleIslands`, `MultiIslandGap`, `Agent*`, duplicate `HooksGuard*`, unknown fields | dropped with a reported reason |

`SettingsStore.Load` writes the legacy text to `settings.v1.backup.json` before replacing
`settings.json` with the v2 document (migration is read-once and rollback-friendly).

---

## 8. Diagnostics (`FluidBar.Core.Diagnostics`)

* `IAppLogger` — structured `Log(level, category, eventId, message, exception?, data?)`;
  `LogCategories` is the fixed enum (`lifecycle/activity/motion/render/plugin/market/settings/
  platform`). `NullAppLogger` default; `RecordingAppLogger` is a bounded ring buffer (tests and
  diagnostics bundle) with drop counting.
* `DiagnosticCounters` — thread-safe counters (`CounterKind` covers events
  published/coalesced/dropped/delivered, subscriber faults, activity switches/replacements/
  expired/dropped/retracted, layout computations, motion frames/retargets/settles, settings
  loads/saves/failures, migrations, state transitions, deferred inputs). `Snapshot()` is
  immutable; `Reset()` for tests/diagnostics reset.
* `IDispatcher` (root namespace) is the only UI-thread marshaling contract; `InlineDispatcher`
  is the deterministic synchronous implementation.

---

## 9. Integration recipes

### 9.1 Platform.Windows → Core (producer)

```csharp
var key = new ActivityKey(ActivityKind.Volume, "system:volume");
var payload = new ActivityPayload { Title = "Volume", Level = level, IsActive = !muted };
hub.TryPublish(IslandEvent.New(key, payload, Interlocked.Increment(ref seq)));
// later, same source: hub.TryPublish(IslandEvent.Incremental(key, patch, nextSeq));
// disappearing:      hub.TryPublish(IslandEvent.Retract(key, nextSeq));
```

`CoreSnapshotMapper` maps source tokens to Core kinds: `media:gsmtc`→Media,
`system:volume`→Volume, `system:brightness`→Brightness, `system:battery`→BatteryLow,
`system:lockkeys`→LockKeys, `system:network`→Device/Bluetooth as appropriate,
`system:ime`→Focus/Device, `clipboard:host`→Clipboard, `notif:toast`→Notification.
Use `ActivityPayload` for the full snapshot; `IslandEvent.New/Incremental/Replacement` factories.
`AppLoggerPlatformAdapter` implements `IAppLogger`. `Display.CoreLayoutConversions` converts
platform monitor data to `MonitorInfo.Create(...)` (physical work area + DPI).

### 9.2 App wiring (consumer)

```csharp
var hub = new EventHub(options, logger, counters, dispatcher);
var scheduler = new ActivityScheduler(stackPolicy, counters);
ActivitySchedulerState schedulerState = ActivitySchedulerState.Empty;

var controller = new IslandStateController(
    SettingsProjection.ToBehaviorOptions(settings, systemReducedMotion), dispatcher: dispatcher);
controller.Transitioned += t => islandHost.Execute(t.Effects); // data-only effects

// UI thread loop: one event per MoveNext, coalescing already applied by the hub
await foreach (var evt in hub.ReadAllAsync("ui", ct))
{
    var update = scheduler.Update(schedulerState, evt, timeProvider.GetUtcNow());
    if (!update.HasChange) continue;
    schedulerState = update.State;

    var t = controller.Apply(IslandInput.ForActivityUpdate(update), timeProvider.GetUtcNow());
    // execute t.Effects on the dispatcher: Relayout/Retarget/UpdateContent/... 
}

// timer tick (≥1 Hz)
var expired = scheduler.Tick(schedulerState, timeProvider.GetUtcNow());
if (expired.HasChange) controller.Apply(IslandInput.ForActivityUpdate(expired), now);

// layout for the current state
var request = SettingsProjection.ToLayoutRequest(settings, controller.Current, monitor);
var layout = IslandLayout.Calculate(request, monitor);   // layout.Placement.WindowPixel
```

Host timers: when the returned state exposes a deadline (`HoverIntentDeadline`,
`LeaveDeadline`, `PeekDwellDeadline`, `AutoCollapseDeadline`, `AutoHideDeadline`), schedule one
shot and feed the matching `*Elapsed` input. When the collapse spring settles, call
`IslandInput.CloseSettled()`; it replays deferred inputs.

Motion: create `SpringSet` with the shell width/height/radius/hover springs (tokens above),
wrap it in `MotionLoop(IFrameClock, springs, counters)`, retarget from `Retarget` effects, and
derive content opacity with `ContentOpacityPolicy` from the current shell height (uniform page
height 284). The frame loop must stop when `AllAtRest` (MotionLoop does this automatically).

### 9.3 Plugin publishing (SDK lane)

Plugins do not reference Core; the PluginHost/App adapter converts a plugin's declarative event
into `IslandEvent` (Core-owned per lead decision) and publishes through the same `IEventBus`.
Plugin source identity should be stable (`plugin:<id>`), and plugin activities should use
`PreferredPage`/`IconToken` when they are not one of the 14 built-in kinds.

---

## 10. Known deviations / clarifications vs the written spec

1. **Uniform expanded shell 440×284 for every page** (lead decision 2026-10-18); spec 3.1's
   per-page heights (232/264) are superseded and no Core token exposes them. The **host surface**
   is now 488×384 float/flush and 488×444 dock (the shell is unchanged); `LayoutTokens.WindowHeight`
   (332) is retained only as a legacy shell-envelope alias.
2. **Compact HUD (volume/brightness) is 208×40** per lead confirmation; spec 3.1's 236×40 and
   the delegated "HUD 208×40 vs battery" ambiguity are resolved as: HUD 208×40, battery 208×36.
3. **`Closing` settles to `Compact` (idle pill) for user-initiated collapse**; `Hidden` is only
   reached via hide hotkey/auto-hide/suppression (T7's "no activity ⇒ Hidden" is interpreted as
   the closing-return path when the collapse was a hide, otherwise the persistent idle pill is
   restored). This keeps the product promise "idle pill always present" and is driven by the
   explicit `ClosingReturnKind` field.
4. **Agent activity rank 10** inserted between Notification and BatteryLow (lead priority brief);
   all other relative ranks match spec 2.3.
5. **Settle-time prediction** is computed with an independent RK4 reference (2 % band) rather
   than the rounded `0.622·T/ζ` table; strongly damped tokens settle slightly later than the
   spec table (e.g. hide ≈0.26 s vs listed 0.20 s). Tests assert ±20 % against the accurate
   reference.
6. `LayoutTokens` exposes layout/radius/inset/shadow numbers only; brushes, fonts and icons are
   App concerns (`Presentation` returns enum/key identities, never colors or glyphs).
7. **The left vertical rail is superseded** (user feedback 2026-10-18): the expanded panel is a
   horizontal drag/wheel pager (see §11). `IslandLayoutResult.Rail` is `[Obsolete]` and always
   empty, `HasRail` is always false, and `LayoutTokens.RailWidth`/`Rail*` item tokens are
   `[Obsolete]` (values retained for source compatibility, unused by layout). Content is
   400×252 at a (20,16) inset — the old "rail 48 + content 392/usable 360" math is gone.
8. **Pager constants are UI-contract numbers (pager v2)**: page count 6, pitch 400 DIP, pointer
   damping **0.85**, snap spring **T 0.38 s / ζ 0.94**, wheel notch **140 DIP** / **180 ms**
   cooldown, scroll-chain **40 DIP / 120 ms**, edge resistance 0.35, drag threshold 26 DIP, flick
   velocity 0.55 DIP/ms. UI code must call `PagePagerPolicy` / `PagerWheelPolicy` /
   `PagerScrollChainPolicy` / `PagerSnapMotion` instead of re-deriving these rules.
9. **Host surface growth + bottom/dock anchor semantics** (2026-10-18 quality-first migration):
   the fixed host is 488×384 (float/flush) / 488×444 (dock) so the detached Page Archipelago strip
   (gap 10 + strip 30) lives outside the 440×284 shell. Top anchors keep the shell at its
   configured offset (the transparent 24 DIP bleed may overhang); bottom and dock anchors anchor
   the **host** to the work area and lift the shell by the mode's bottom allowance (76 float/flush,
   136 dock), so the strip never falls off-screen. With the host bottom 4 DIP above the work-area
   bottom, the dock strip bottom lands 80 DIP above the work-area bottom and the expanded dock
   panel bottom 140 DIP above it (was 4/64 in the pre-archipelago contract). The host bottom
   padding is asymmetric: 24 DIP top/sides, 36 DIP below the strip.
10. **The legacy 488×332/392 host tokens are superseded** by `HostSurface` /
    `WindowPlacement.WindowSizeDip`; `LayoutTokens.WindowHeight` (332) and
    `WindowPlacementConverter.PlaceWindow(shell, monitor, padding)` remain only for source
    compatibility and must not be used for new host code.

---

## 11. Horizontal pager — `FluidBar.Core.Interaction` (supersedes the left rail)

Status: user feedback 2026-10-18 rejected the left vertical rail; navigation is now a horizontal
mouse-drag/wheel pager over the uniform 440×284 expanded panel. This namespace is pure and
deterministic (no UI, no Platform, no ambient time): the Avalonia layer feeds pointer/wheel data
and renders the returned offset — it must not re-derive any of the math below.

### 11.1 Constants (`PagerConstants`)

| Constant | Value | Meaning |
|---|---|---|
| `PageCount` | 6 | home / media / notifications / clipboard / agent / widgets |
| `PagePitchDip` | 400 | one page step in DIP |
| `PointerDamping` | **0.85** | content follows the pointer at 85 % (pager v2 final) |
| `EdgeResistance` | 0.35 | rubber-band factor beyond first/last page |
| `PageThresholdDip` | 24 | minimum drag before a release may change page |
| `FlickVelocityDipPerMs` | 0.6 | velocity that classifies a short drag as a flick |
| `LongDragPageFraction` | 0.5 | drag ≥ 0.5 page switches to distance-based paging |
| `SnapSpringPeriodSeconds` / `SnapSpringDampingRatio` | **0.38 / 0.94** | dedicated page-snap spring (pager v2 final; not `motion.tab`) |
| `WheelNotchDip` | **140** | accumulated wheel DIP per page step (pager v2) |
| `WheelCooldownMs` | **180** | minimum interval between wheel page steps (pager v2) |
| `ScrollChainOverscrollDip` | **40** | boundary overscroll DIP before a wheel page step |
| `ScrollChainDwellMs` | **120** | boundary dwell ms before a wheel page step |
| `RetargetFrameSeconds` | 1/120 | reference frame delta used for snap retarget velocity clamp |
| `ContentWidthDip` / `ContentHorizontalInsetDip` / `ContentVerticalInsetDip` | 400 / 20 / 16 | no-rail expanded content |

These supersede the first-cut pager values (damping 0.90, snap 0.34/0.92 via `motion.tab`, wheel
100 DIP / 120 ms).

Sign convention: **`dragDip` is accumulated pointer drag in DIP, positive = towards the next
page** (feed `-pointerDeltaX` if dragging content left advances pages). Offsets are content
offsets: page `p` is at `p × pitch`.

### 11.2 Drag (`PagePagerPolicy.Drag`)

```csharp
PagerDragState drag = PagePagerPolicy.Drag(startPage, accumulatedRawDragDip);
// drag.DisplayOffsetDip is what the shell/page strip renders while the pointer is down
```

* pointer damping: `damped = rawDrag × 0.85`;
* raw offset: `startPage × pitch + damped`;
* rubber band: past `[0, (pageCount−1) × pitch]`, only the overshoot is scaled by `0.35`
  (`display = max + overshoot × 0.35`); `AtStartEdge`/`AtEndEdge` flag the band;
* output also carries `PagePosition = display / pitch` for indicator rendering.

### 11.3 Release rules (`PagePagerPolicy.Release`)

```csharp
PagerSnapDecision decision = PagePagerPolicy.Release(startPage, accumulatedRawDragDip, velocityDipPerMs);
// decision.TargetPage / TargetOffsetDip / Kind / PageDelta / ChangesPage
```

Exact rules (threshold is checked first — a sub-24-DIP release always snaps back, even with a huge
velocity):

| # | Condition | Result | `Kind` |
|---|---|---|---|
| a | `\|drag\| < 24` | no page change, snap back | `None` |
| b | `24 ≤ \|drag\| < 200` (short drag or flick) | **at most ±1 page** (`start ± 1`), never skips regardless of velocity | `Flick` if `\|velocity\| ≥ 0.6`, else `Distance` |
| c | `\|drag\| ≥ 200` (long drag) | `clamp(start + round(drag / pitch))` — multiple pages per gesture (2.7 pages ⇒ +3, `Math.Round` away from zero) | `Distance` |
| d | target outside `[0, pageCount−1]` | clamped; same-page results become `None` (snap back at boundary) | `None` |

Helpers: `ClampPage(page, pageCount=6)`, `PageToOffset(page, pitch=400)`,
`OffsetToPage(offset, pitch=400)` (rounds away from zero), `ReleaseWheel(startPage, pageDelta)`,
and `Release(in PagerDragState, velocity)`.

### 11.4 Wheel / trackpad (`PagerWheelPolicy`)

```csharp
var wheel = new PagerWheelPolicy();
var result = wheel.Accumulate(deltaDip, now, controlOwnsGesture: sliderOrScrollableIsActive);
page = PagerWheelPolicy.ApplyPageDelta(page, result.PageDelta);
```

* accumulates deltas until `±140 DIP`, then emits exactly one page step and resets the
  accumulator (one page per notch/window);
* a page step starts a **180 ms cooldown**: events inside the window are dropped
  (`CooldownBlocked=true`) so trackpad inertia cannot cascade several pages; a discrete notch
  cadence ≥180 ms apart advances one page per notch;
* `controlOwnsGesture: true` (slider, scrollable, text field) suppresses the event and resets the
  accumulator (`OwnerSuppressed=true`);
* `now` is injected (the host passes `TimeProvider.GetUtcNow()`), `Reset()` clears accumulator and
  cooldown on panel close/mode switch.

### 11.5 Snap motion (`PagerSnapMotion`)

The snap uses its own dedicated spring (pager v2): **T = 0.38 s, ζ = 0.94** (exposed via
`PagerSnapMotion.SpringParameters`; it intentionally no longer reuses `motion.tab` 0.34/0.92 so
page snaps are calmer). Fixed 1/300 s substeps, `dt` clamp and settle detection are the rest of Core.
It is **interruptible**: on pointer-down call `Interrupt()`, which freezes the spring at
the current visual offset and returns it — the new drag starts from what the user sees, not from the
previous target page.

```csharp
// UI wiring sketch (Avalonia layer)
var snap = new PagerSnapMotion();               // one per expanded panel
var origin = snap.Interrupt();                  // pointer-down: freeze current visual offset
var startPage = PagePagerPolicy.OffsetToPage(origin);
double accumulated = 0;                         // pointer-move: accumulated += -dx (sign convention)
var drag = PagePagerPolicy.Drag(startPage, accumulated);
Render(drag.DisplayOffsetDip);                  // indicator: drag.PagePosition
// pointer-up:
var decision = PagePagerPolicy.Release(startPage, accumulated, velocityDipPerMs);
snap.SnapToOffset(decision.TargetOffsetDip);    // frame loop: snap.Step(dt), stop when snap.IsAtRest
// wheel (expanded panel): let the scroll chain decide the owner (see §11.6); simple contexts can use
// PagerWheelPolicy directly:
var currentPage = PagePagerPolicy.OffsetToPage(snap.CurrentOffsetDip);
var wheel = wheelPolicy.Accumulate(deltaDip, timeProvider.GetUtcNow(), controlOwnsGesture);
snap.SnapToPage(PagerWheelPolicy.ApplyPageDelta(currentPage, wheel.PageDelta));
// mode/display change or restore:
snap.JumpTo(page * PagerConstants.PagePitchDip);
```

### 11.6 Wheel scroll chain (`PagerScrollChainPolicy`) — pager v2

Wheel/trackpad input over the expanded panel must first respect the scrollable content under the
pointer (lyrics, notification list, clipboard history, plugin scrollables). The pure scroll-chain
policy decides the owner per event; it tracks boundary overscroll + dwell internally and takes an
injected clock (no timers, no UI):

```csharp
var chain = new PagerScrollChainPolicy();           // one per expanded panel; Reset() on close/leave
var decision = chain.Handle(new PagerScrollChainRequest
{
    WheelDeltaDip = deltaDip,                       // signed: + = down/next page, - = up/previous
    ContentScrollable = contentScrollable,          // caller hit-tests the content under the pointer
    ContentBoundary = boundary,                     // None | Top | Bottom
    CurrentPage = currentPage,
    PageCount = PagerConstants.PageCount,
    Now = timeProvider.GetUtcNow(),
    PageAnimationLocked = snap.IsRunning,           // page snap freezes the chain
    HorizontalWheel = isHorizontalWheel,
    ShiftHeld = shift,
});

switch (decision.Action)
{
    case PagerScrollChainAction.ScrollContent: content.ApplyWheel(deltaDip); break;
    case PagerScrollChainAction.PageNext:
    case PagerScrollChainAction.PagePrevious:  snap.SnapToPage(decision.TargetPage); break;
    case PagerScrollChainAction.None: break;   // accumulating / reversed / locked
}
```

Rules (constants `ScrollChainOverscrollDip = 40`, `ScrollChainDwellMs = 120`):

| Condition | Decision |
|---|---|
| content scrollable with room in the wheel direction | `ScrollContent` (content consumes the delta) |
| content at the boundary, `< 40 DIP` accumulated | `None` (accumulating; `decision.AccumulatedOverscrollDip` exposes state) |
| content at the boundary, `≥ 40 DIP` but `< 120 ms` dwell | `None` |
| content at the boundary, `≥ 40 DIP` **and** `≥ 120 ms` dwell | `PageNext` / `PagePrevious` (one page; state resets) |
| direction reverses before the page step | `None` with `ReverseCancelled = true`; pending step reset |
| content not scrollable | immediate `PageNext` / `PagePrevious` |
| horizontal wheel or Shift held | immediate page (content bypassed) |
| `PageAnimationLocked = true` | whole chain frozen: `None`, `PageLocked = true` |
| page target out of `[0, pageCount−1]` | clamped; no change ⇒ `None` with `TargetPage = CurrentPage` |

Diagnostics/state: `AccumulatedOverscrollDip`, `DwellStartedAt`, `HasPendingPageStep`; call
`Reset()` on pointer leave / panel close / mode switch. `PagerWheelPolicy` remains for simple wheel
contexts without content hit-testing; the expanded panel uses the scroll chain as the owner.

### 11.7 Layout consumption (no rail)

`IslandLayoutResult.Content` for the expanded panel is **400 × 252 DIP** starting at
`Shell.X + 20, Shell.Y + 16` (shell is still 440 × 284; the host surface is 488 × 384 float/flush
and 488 × 444 dock). Render the six pages as one horizontal strip inside that 400-wide clip:
page `i` occupies `content.X + (i × 400) − snap.CurrentOffsetDip`. There is no rail; `Rail` is
obsolete/empty, `HasRail` is false, and `LayoutTokens.RailWidth`/`Rail*` item tokens are obsolete
(unused). The detached Page Archipelago is drawn from `IslandLayoutResult.Archipelago` /
`Placement.ArchipelagoStrip` (outside the shell), with the active pill driven by
`ArchipelagoLayout.Calculate(pager.PagePosition, layout.Placement, stripVisible)`; see §4.5–§4.7.

### 11.8 Tests

`PagerPolicyTests` (44 cases): pager v2 constants (damping 0.85, snap spring 0.38/0.94, wheel
140/180, scroll-chain 40/120), damping, rubber band at both ends, all release rules
(small drag, 5-velocity flick cap incl. huge velocity, ±1 negative flick, 2.7-page multi-page drag
both directions, half-page rounding, bounds clamping), custom pitch/pageCount/NaN handling,
determinism; wheel one notch/negative/sub-threshold/cooldown/5-notch/rapid-notch/owner-suppression/
reset; snap spring parameters, exact settle on target page, interrupt-at-current-offset, drag
handoff continuity, instant jump, page position, wall-clock independence.
`PagerScrollChainPolicyTests` (21 cases): constants, scrollable-with-room both directions,
boundary-mismatch scroll, sub-threshold overscroll, <120 ms dwell, ≥40 DIP & ≥120 ms page step both
directions, reverse-cancel, immediate paging for non-scrollable, horizontal/Shift bypass,
page-animation lock, first/last-page clamping, fresh-gesture requirement, custom page count,
zero/NaN delta, Reset, deterministic repeats.
The 108-case layout matrix additionally asserts the no-rail content rect, empty `Rail`, per-mode
host surface (488×384/444), host-local shell offset, bottom allowance, dock clamp and strip
geometry for every mode × anchor × DPI × stacking combination. A 54-case archipelago matrix
(3 modes × 6 anchors × 3 DPIs) asserts the strip stays inside the work area, the slot widths,
the continuous active indicator and hit-test exclusion. Full Core suite:
**472 passed / 0 failed** (409 before the host-surface/archipelago increment).

