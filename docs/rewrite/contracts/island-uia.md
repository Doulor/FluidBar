# Island UI Automation (`island-uia`) — UIA provider for the custom-drawn Skia island

Status: **MVP delivered (branch `v2/rebuild`, not committed)**. Owner: App/graphics + Platform.
Consumers: `FluidBar.App` (Skia island path), Narrator/UI Automation clients. Tests:
`tests/FluidBar.Platform.Tests/UiaProviderTests.cs` (pure), `UiaWindowIntegrationTests.cs`
(guarded real HWND), `tests/FluidBar.Ui.Tests/SkiaIslandUiaTests.cs` (headless App tree/keyboard).
Spec: `2026-10-18-fluidbar-v2-design.md` §2.9 (accessibility/focus/IME), §3.6 (focus ring),
§5.6 (host/focus rules), §9.4 (manual QA).

The island is a custom-drawn, per-pixel-alpha layered HWND
([`layered-host.md`](layered-host.md)) rendered by Skia ([`skia-host.md`](skia-host.md)). Windows has
no idea what is painted inside it, so the App builds an explicit UI Automation element tree and the
Platform host answers `WM_GETOBJECT`. No Avalonia control tree is involved on this path.

---

## 1. Architecture

```
App state change / focus change (UI thread)
  └─ SkiaIslandHost.RefreshAccessibility()
       └─ SkiaIslandUiaSource.BuildSnapshot()          immutable UiaElementSnapshot (host-local DIP)
            └─ LayeredWindowHost.SetUiaSnapshot()      one snapshot per monitor window
                 └─ (lazily, on WM_GETOBJECT) UiaElementProvider.CreateRoot(...)

UIA client thread
  └─ WM_GETOBJECT(UiaRootObjectId) → LayeredWindowHost.OnGetObject()
       └─ UiaReturnRawElementProvider(hwnd, wParam, lParam, rootProvider)
            └─ IRawElementProviderSimple / Fragment / FragmentRoot + pattern interfaces
                 ├─ reads: snapshot + live UiaWindowGeometry (atomic)
                 └─ actions: host PostToUi → SkiaIslandUiaSource.Perform → controller/platform seam
```

| Layer | File | Responsibility |
|---|---|---|
| Platform seam | `src/FluidBar.Platform.Windows/Accessibility/UiaTypes.cs` | `UiaElementSnapshot`, roles/patterns/state, `UiaWindowGeometry`, `IUiaElementSource`, `IUiaKeyboardSink`. Pure data; no App/Core types. |
| Platform provider | `Accessibility/UiaElementProvider.cs` | Managed COM provider: property/pattern/navigation/hit-test/focus. Immutable tree; no per-frame work. |
| COM contracts | `Accessibility/UiaInterop.cs` | `IRawElementProviderSimple/Fragment/FragmentRoot`, `IInvokeProvider`, `IExpandCollapseProvider`, `IValueProvider`, `IRangeValueProvider`, `ISelectionItemProvider`, `IToggleProvider`, UIA ids. GUIDs/vtable order copied from `UIAutomationCore.h`. |
| UIAutomationCore | `Accessibility/UiaNative.cs` | `UiaReturnRawElementProvider`, `UiaClientsAreListening`, `UiaDisconnectProvider`, `UiaRaiseStructureChangedEvent`, `UiaRaiseAutomationEvent`. |
| Host integration | `Hosting/LayeredWindowHost.Accessibility.cs` | `AttachAccessibility`, `SetUiaSnapshot`, `WM_GETOBJECT`, `WM_GETDLGCODE`, `WM_KEYDOWN`, `WM_SETFOCUS/KILLFOCUS`, UI-thread dispatch, provider cache/disconnect. |
| Host messages | `Hosting/LayeredWindowHost.Messages.cs` | Message cases for the above. |
| App source | `src/FluidBar.App/Hosting/Skia/SkiaIslandUiaSource.cs` | Render state → element tree; actions → `SkiaIslandController`/`IIslandPlatformActions`; logical focus order; keyboard handling. |
| App wiring | `SkiaIslandHost.cs` | Attaches the source to every per-monitor host; refreshes on state/strip/DPI/monitor/animation-settle changes. |
| Controller | `SkiaIslandController.cs` | `SetAccessibilityFocus` (focus ring), `ActivateAccessibleControl`, `SetAccessibleSlider`. |
| Renderer | `FluidBar.Island/IslandRenderer.cs` | Draws the 2 DIP `--info` focus ring from `IslandRenderState.FocusRing`. |

**Threading.** The provider is created on the UI thread. UIA clients call it from their own threads;
the provider tree is immutable and bounds are read from an atomically replaced `UiaWindowGeometry`
(a small lock in the host). Actions and focus requests are posted to the UI thread through the
synchronization context captured by `LayeredWindowHost`; the UIA thread never blocks on UI work.
`ProviderOptions_UseComThreading` is set so UIA may call directly. If no synchronization context is
available (headless tests), off-thread requests are logged and dropped rather than executed unsafely.

**Snapshot lifecycle.** `BuildSnapshot()` runs on state transitions, content changes, strip
visibility changes, focus changes, DPI/monitor changes and when animation settles — never per frame.
A state change disconnects the previous root provider and raises a UIA structure-changed event
(`ChildrenInvalidated`) only when a UIA client is listening. The provider
tree is rebuilt lazily on the next `WM_GETOBJECT`. Media waveform animation does not rebuild the tree.

---

## 2. Element tree (MVP)

All bounds below are host-local DIP; the provider adds the window origin and multiplies by the
monitor scale before returning `BoundingRectangle` in physical screen pixels. Names come from the
live render state and the localization catalogs; help text/keyboard hints are localized too.

### 2.1 Root (always present)

| Id | Role | Name | Patterns | Bounds | Notes |
|---|---|---|---|---|---|
| `island.root` | Group (50026) | **"FluidBar"** | Invoke, ExpandCollapse | shell squircle | Invoke toggles expand/collapse; `ExpandCollapseState` = Expanded/Collapsed. Hidden island: `IsOffscreen=true`, no children. |

### 2.2 Compact (`ShellForm.Compact` / `Peek` / `Closing`)

| Id | Role | Name | Patterns | Bounds |
|---|---|---|---|---|
| `island.compact` | Group | activity summary (title · subtitle · body · badge · app · clock · status) | — | shell |
| `island.compact.media.play` | Button | Play / Pause | Invoke | renderer's compact play rect (`shell.Right-34..-8`, `top+7..33`) — only for a media activity |

### 2.3 Expanded

| Id | Role | Name | Patterns | Bounds |
|---|---|---|---|---|
| `island.collapse` | Button | Collapse | Invoke | `PageLayout.CollapseRect(content)` |
| `island.pages` | Tab | Pages | — | strip envelope when visible, empty otherwise |
| `island.page.N` (N=0..5) | TabItem | page title | SelectionItem, Invoke | mini-island / dot rect when the strip is visible; empty + `IsOffscreen` when hidden. Selected = committed page. |
| `island.page.N.body` | Group | page title | — | `IslandMetrics.PageRect(state, N)` for the current page |
| `island.strip` | Group | Page navigation | — | strip envelope — only while the strip is visible |
| `island.strip.prev.{page}`, `.current.{page}`, `.next.{page}` | Button | "Previous/Current/Next page: {title}" | Invoke | previous/current/next mini-island |
| `island.strip.dot.left.{page}`, `.right.{page}` | Button | "Page {n}: {title}" | Invoke | dot hit target (12 DIP) |

Page controls are built only for the committed page (bounds are on-screen):

**Home (0)**

| Id | Role | Name | Patterns | Bounds |
|---|---|---|---|---|
| `island.page.0.clock` | Text | clock + date | — | `HomeClock` |
| `island.page.0.weather` | Text | temp + condition | — | `HomeWeather` (when shown) |
| `island.page.0.toggle.0..3` | Button | toggle label | Toggle, Invoke | `HomeToggle(page, i)`; `ToggleState` from `HomeToggleModel.IsOn` |
| `island.page.0.card` | Button | "Now playing: {title} — {subtitle}" | Invoke | `HomeMediaCard` (opens the media page) |
| `island.page.0.card.play` | Button | Play/Pause | Invoke | `HomeMediaPlay` |
| `island.page.0.volume` | Slider | Volume | RangeValue (0..100) | `HomeVolumeTrack` ±8 DIP; value = `HomePageModel.Volume` |
| `island.page.0.brightness` | Slider | Brightness | RangeValue (0..100) | `HomeBrightnessTrack` ±8 DIP |
| `island.page.0.battery` | Text | "Battery {n}%…" | — | `HomeBattery` |

**Media (1)**

| Id | Role | Name | Patterns | Bounds |
|---|---|---|---|---|
| `island.page.1.track` | Text | title — artist — album (item status = position/duration) | — | `MediaTitle` |
| `island.page.1.previous` / `.play` / `.next` | Button | Previous / Play·Pause / Next | Invoke | `MediaPrevious` / `MediaPlay` / `MediaNext` |
| `island.page.1.seek` | Slider | Seek | RangeValue (0..100) | `MediaSeekRow`; value = `MediaPageModel.ProgressPercent` |
| `island.page.1.output` / `.queue` | Button | Audio output / Play queue | Invoke | `MediaOutput` / `MediaQueue`; **`IsEnabled=false`** with the exact missing-service reason in `ItemStatus`/`HelpText` (no audio-output/queue service exists) |

**Notifications (2) / Clipboard (3) / Agent (4)**

| Id | Role | Name | Patterns | Bounds |
|---|---|---|---|---|
| `island.page.{2,3,4}.list` | List | page title | — | `PageLayout.ListBody` |
| `island.page.{2,3,4}.row.{i}` | ListItem | joined row text | Invoke | `NotificationRow` / `ClipboardRow` / `AgentRow` at the model scroll offset; `IsOffscreen` when outside the list body; item status = time/status/code |
| `island.page.2.row.{i}.dismiss` | Button | Close | Invoke | `NotificationDismiss` |
| `island.page.3.row.{i}.pin` | Button | Pin/Unpin | Toggle, Invoke | `ClipboardPin`; `ToggleState` from `IsPinned` |
| `island.page.3.row.{i}.copy` | Button | Copy | Invoke | `ClipboardCopy` |

Rows are capped at 32 per page (MVP tree-size bound); `PageLayout.ClampScroll` uses the same
scroll offset/row count as hit-testing, so bounds never drift from painted rows.

**Widgets (5)**

| Id | Role | Name | Patterns | Bounds |
|---|---|---|---|---|
| `island.page.5.calendar.prev` / `.next` | Button | Previous/Next month | Invoke | `WidgetsCalendarNav` |
| `island.page.5.calendar.title` | Text | month title (status = today/event) | — | calendar header |
| `island.page.5.weather` | Text | temp, condition, high/low, wind | — | `WidgetsWeather` |
| `island.page.5.stat.{i}` | Text | "CPU 42%" etc. | — | stats card |
| `island.page.5.task.{i}` | Button | task title | Toggle, Invoke | `WidgetsTaskRow`; `ToggleState` from `Done` |

**Dismissal / activation map.** Tab / strip buttons → `HandlePageRequest`; root → toggle hotkey or
Escape; collapse → Escape; media transport/seek → `IIslandPlatformActions` (GSMTC); volume/brightness
→ their monitors; quick toggles → radio/focus/theme seams; notification/agent/task/calendar/clipboard
→ the existing deferred/App-owned actions. The UIA path reuses `SkiaIslandControlRouter.Map`, so a
Narrator invocation behaves exactly like a click.

---

## 3. Patterns and properties

| Pattern | Elements | Behaviour |
|---|---|---|
| Invoke | root, buttons, tab items, list rows | Default action; posted to the UI thread. |
| ExpandCollapse | root | `Expand()` toggles compact→expanded; `Collapse()` collapses when expanded. |
| RangeValue | volume, brightness, seek | `Value`/`Minimum`/`Maximum`/`SmallChange`/`LargeChange`; `SetValue` clamps to 0..100 and dispatches `VolumeSet`/`BrightnessSet`/`MediaSeek` with a 0..1 fraction. |
| SelectionItem | page tabs | `IsSelected` = committed page; `Select()` changes page. `SelectionContainer` = `island.pages`. Single selection. |
| Toggle | quick toggles, clipboard pin, widget task | `ToggleState` from the render model; `Toggle()` dispatches the same action as a click. |
| Value | — | Advertised nowhere in MVP (no text fields); the interface is implemented defensively. |

Properties: `ControlType`, `Name`, `AutomationId` (element id), `HelpText`, `IsEnabled`,
`IsKeyboardFocusable`, `HasKeyboardFocus`, `IsOffscreen`, `IsControlElement`/`IsContentElement`
(Text nodes are content-only), pattern-availability flags, `ValueValue`, range properties,
`ExpandCollapseState`, `SelectionItemIsSelected`, `ToggleState`, `ItemStatus`, `ClassName`
(`FluidBar.LayeredIslandHost`), `FrameworkId` (`FluidBar`), `ProcessId`.

**Runtime ids.** The root returns `null` (the HWND identifies the fragment root). Every child returns
a full, stable single-int runtime id derived from `FNV-1a(elementId)`. It is deliberately **not**
`[UiaAppendRuntimeId, id]`: the root has no runtime id to append, and a real UIA client rejected the
append form with `E_FAIL` in that configuration (verified with `AutomationElement.GetRuntimeId`).
The id is deterministic across provider rebuilds while the element id is stable.

**Hit-testing.** `IRawElementProviderFragmentRoot.ElementProviderFromPoint` walks the immutable tree
deepest-first in screen coordinates and skips offscreen nodes; outside every element it returns
`null` (the bleed/corners are click-through and have no element). It does **not** alter
`WM_NCHITTEST`/click-through.

---

## 4. Bounds and DPI

- Snapshot bounds are **host-local DIP** from `IslandMetrics`, `PageLayout` and
  `SkiaIslandHitTest.MeasureStrip` — the same geometry the renderer paints and the click hit-test
  consumes.
- `UiaWindowGeometry(originPx, scale, sizePx, visible)` is refreshed by the host on create, place,
  drag, mode change, DPI change (`WM_DPICHANGED`) and show/hide. It is replaced atomically.
- `BoundingRectangle = originPx + localDip × scale`; `UiaRect` is in physical screen pixels, as UIA
  requires. Hidden or explicitly offscreen elements return an empty rect and `IsOffscreen=true`.
- The page tab bounds follow the transient strip: when the strip is hidden the tabs are still in the
  tree (so Narrator can select a page) but report empty bounds/offscreen; when the strip is revealed
  the previous/current/next mini-islands and far-page dots get their measured rects.

---

## 5. Focus and keyboard

**Focus model.** The island keeps `WS_EX_NOACTIVATE` and `WM_MOUSEACTIVATE → MA_NOACTIVATE`: a
pointer click never steals focus. Keyboard/UIA focus is a separate, explicit path:

1. A UIA client calls `IRawElementProviderFragment.SetFocus` (Narrator's Focus command), or the App
   requests focus through the host's accessibility path.
2. The host attaches to the foreground thread, calls `SetForegroundWindow` + `SetFocus` on the island
   HWND, then detaches. If Windows denies foreground activation, logical focus and UIA patterns still
   work; raw key delivery may not (documented limit).
3. `WM_SETFOCUS`/`WM_KILLFOCUS` update `HasKeyboardFocus` and the logical focus ring.

**Keys handled while the island has focus** (`WM_GETDLGCODE` asks for Tab/arrows/all keys):

| Key | Behaviour |
|---|---|
| Tab / Shift+Tab | Next/previous focusable, non-offscreen element in depth-first tree order (root first). Wraps. |
| Left / Right | If the focused element is a slider: step the value by 5% of its range. Otherwise, when expanded: previous/next page. |
| Up / Down | If the focused element is a slider: step the value. Otherwise move focus previous/next. |
| Home / End | Expanded: first/last page. Otherwise first/last focusable element. |
| Enter / Space | Invoke the focused element (root toggle, button, tab select, row activate, toggle, strip navigation). |
| Escape | Collapse (the same Core `Escape` path as the chevron); from compact it is a no-op. |

**Focus ring.** `SkiaIslandController.SetAccessibilityFocus` stores the focused element's bounds in
`IslandRenderState.FocusRing`; the renderer paints a 2 DIP `--info` outline (spec §3.6) after the
strip. The ring is only shown while the HWND has focus and the focused element is visible. Focus
changes trigger one full repaint (not a frame-loop subscription).

**Focus order.** Root → collapse → page tabs (only when the strip is visible) → current page controls
→ strip buttons. Offscreen nodes are skipped by Tab. Page navigation remains available through
Left/Right even when the strip is hidden.

---

## 6. Known limits (honest MVP)

1. **No text editing / IME.** There are no text fields on the island; `IValueProvider` is not
   advertised. The layered host carries no IME composition window (spec §5.6); a future search/lookup
   surface needs a separate small window. This is unchanged by UIA.
2. **MSAA/IAccessible is not implemented.** Only the UIA raw-element-provider path is exposed
   (`WM_GETOBJECT` with `UiaRootObjectId`). Legacy `OBJID_CLIENT` clients see the default window
   provider. Narrator and modern UIA clients use the new path.
3. **No UIA event stream beyond structure/focus.** Property-changed events for media progress or
   toggle state are not raised; clients re-query after a structure-changed event or on demand. The
   snapshot is rebuilt on state changes, not per frame.
4. **Focus acquisition can be denied.** `WS_EX_NOACTIVATE` is intentional (spec §5.6). If Windows
   refuses foreground activation for the island process, Narrator can still invoke patterns and move
   logical focus, but raw Tab/arrow key delivery may not reach the HWND. The ring and
   `HasKeyboardFocus` reflect the actual `WM_SETFOCUS` state.
5. **List rows are capped at 32** per page in the accessibility tree; the painted list can contain
   more. Rows beyond the cap remain reachable with the pointer/scroll but are not enumerated.
6. **No Scroll pattern and no SelectionPattern on the tab list.** List scrolling is driven by content
   updates (the model scroll offset) and keyboard focus; `ScrollPattern`/`ScrollItemPattern` are not
   implemented. Tabs expose selection per item (`SelectionItemPattern.IsSelected`/`Select`); the
   `Pages` container does not implement `SelectionPattern`.
7. **Offscreen page tabs** (strip hidden) are in the tree with empty bounds; some clients may hide
   them in the control view. Use Left/Right or the page strip to navigate.
8. **Output/queue buttons are exposed but disabled** because no platform service exists; the exact
   reason is in `ItemStatus`/`HelpText`. This is deliberate honesty rather than a fake action.
9. **Bounds during animation.** The snapshot is refreshed when motion settles; during a 200–400 ms
   morph the UIA bounds are the last settled bounds. The provider still translates them live, so
   window movement/DPI is exact.
10. **No automated Narrator test.** The real-HWND test proves `WM_GETOBJECT` returns a provider and
    the managed tree/patterns are correct; a human Narrator pass is still required (checklist below).
11. **Per-monitor snapshots.** One snapshot is shared by all monitor windows (same surface-local
    geometry); each host translates with its own origin/scale. The focused element is global, not
    per-monitor.

---

## 7. Manual Narrator checklist

Run with `FLUIDBAR_HOST=skia` (or the default Skia path) on a real desktop. No admin required.

1. **Discoverability.** Start Narrator (Ctrl+Win+Enter). Focus the island via Narrator's element
   navigation; it announces **"FluidBar, group"** and the compact activity summary. It must not be
   silent.
2. **Expand.** Invoke the root (`Enter` or Narrator's Invoke) or press the toggle hotkey. Narrator
   announces the expanded state; the page controls become visible in the tree.
3. **Pages.** Move through the six `Pages` tabs (when the strip is visible) or use Left/Right. Each tab
   announces its localized title and selected state; selecting one changes the page.
4. **Home controls.** Volume and brightness announce as sliders with 0–100 values; changing the value
   with Narrator's RangeValue commands changes the real system value. Quick toggles announce on/off
   and toggle state.
5. **Media.** Previous/Play/Next invoke the real GSMTC session. Seek announces 0–100 and seeks.
   Output/queue announce as unavailable with the reason.
6. **Lists.** Notifications/clipboard/agent rows announce their text and row status; row Invoke
   activates, dismiss/pin/copy child buttons work. Rows outside the visible body are offscreen.
7. **Strip.** With the page strip revealed (pager drag/wheel or bottom hot-zone dwell), the
   mini-islands and dots appear as buttons and navigate to their pages.
8. **Focus ring.** `SetFocus` on any element shows the 2 DIP info outline around it; losing focus
   removes the ring and `HasKeyboardFocus` becomes false.
9. **Keyboard-only.** With focus on the island: Tab/Shift+Tab cycle controls, Left/Right change pages
   (or slider values), Enter invokes, Escape collapses. No key is swallowed by the desktop.
10. **Click-through.** Clicking outside the shell still reaches the app underneath; clicking the pill
    still expands without stealing focus from the foreground app.
11. **DPI / multi-monitor.** At 125/150/200 %, Narrator's bounding rectangle highlights the painted
    control (not the whole 488×384 surface). On a second monitor the bounds follow that window's
    origin.
12. **Collapse/hide.** Escape collapses; hiding the island (hotkey) makes the root offscreen and the
    children disappear. Re-show restores the tree.

---

## 8. Verification

```powershell
dotnet build FluidBar.sln
# pure provider: control types, patterns, bounds translation, navigation, actions
dotnet test tests/FluidBar.Platform.Tests/FluidBar.Platform.Tests.csproj --filter "FullyQualifiedName~UiaProviderTests"
# guarded real HWND: WM_GETOBJECT returns a provider, key messages reach the sink
dotnet test tests/FluidBar.Platform.Tests/FluidBar.Platform.Tests.csproj --filter "FullyQualifiedName~UiaWindowIntegrationTests"
# App tree/keyboard: roles, names, bounds, actions, focus ring, strip, hidden state
dotnet test tests/FluidBar.Ui.Tests/FluidBar.Ui.Tests.csproj --filter "FullyQualifiedName~SkiaIslandUiaTests"
```

The real-HWND test requires `Environment.UserInteractive` (a normal desktop session); it is skipped
on headless agents. It does not need Narrator or a UIA client — it proves the `WM_GETOBJECT` seam and
the managed provider tree; the manual checklist covers the client-facing behavior.

During development a throwaway WPF `AutomationElement.FromHandle` probe (not committed, because it
would add a `Microsoft.WindowsDesktop.App` framework reference to the test project) verified the
client-facing path end-to-end: root/child enumeration by `AutomationId`, `Name`/`ControlType`,
`BoundingRectangle`, `GetRuntimeId`, and the Invoke, ExpandCollapse, RangeValue, SelectionItem and
Toggle patterns all dispatched the expected actions. Two marshaling bugs were found and fixed that
way: `GetPatternProvider` must return `IUnknown` (not a VARIANT), and the `IsReadOnly`/`IsSelected`
BOOL properties must marshal as 4-byte `BOOL` (getter-level `[return: MarshalAs(UnmanagedType.Bool)]`).

---

## 9. Files

| File | Contents |
|---|---|
| `src/FluidBar.Platform.Windows/Accessibility/UiaTypes.cs` | Snapshot/state/roles/patterns, geometry, source/keyboard interfaces |
| `src/FluidBar.Platform.Windows/Accessibility/UiaElementProvider.cs` | Managed COM provider + provider context |
| `src/FluidBar.Platform.Windows/Accessibility/UiaInterop.cs` | COM interfaces, GUIDs, UIA ids |
| `src/FluidBar.Platform.Windows/Accessibility/UiaNative.cs` | UIAutomationCore P/Invokes |
| `src/FluidBar.Platform.Windows/Hosting/LayeredWindowHost.Accessibility.cs` | Host UIA seam, focus, dispatch, provider cache |
| `src/FluidBar.Platform.Windows/Hosting/LayeredWindowHost.Messages.cs` | `WM_GETOBJECT`, keyboard/focus messages |
| `src/FluidBar.Platform.Windows/Interop/NativeMethods.cs` | `WM_*`, `VK_*`, `DLGC_*`, `SetFocus`, `AttachThreadInput` |
| `src/FluidBar.App/Hosting/Skia/SkiaIslandUiaSource.cs` | Element tree, actions, focus order, keyboard |
| `src/FluidBar.App/Hosting/Skia/SkiaIslandController.cs` | `SetAccessibilityFocus`, accessible control/slider dispatch |
| `src/FluidBar.App/Hosting/Skia/SkiaIslandHost.cs` | Attach/refresh/dispose wiring |
| `src/FluidBar.Island/IslandRenderState.cs` | `FocusRing` |
| `src/FluidBar.Island/IslandRenderer.cs` | 2 DIP info focus ring |
| `src/FluidBar.App/Localization/{en-US,zh-Hans}.json` | `Island.Accessibility.*`, `Page.Media.Output/Queue` |
| `tests/FluidBar.Platform.Tests/UiaProviderTests.cs` | Pure provider tests |
| `tests/FluidBar.Platform.Tests/UiaWindowIntegrationTests.cs` | Guarded real-HWND tests |
| `tests/FluidBar.Ui.Tests/SkiaIslandUiaTests.cs` | Headless App tree/keyboard tests |
