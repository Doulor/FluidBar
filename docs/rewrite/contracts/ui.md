# FluidBar v2 — UI Contract (`FluidBar.App`)

| Item | Value |
|---|---|
| Owner | ui-owner (M4, task-4) |
| Scope | `src/FluidBar.App/**`, `tests/FluidBar.Ui.Tests/**`, this contract |
| Inputs | spec `2026-10-18-fluidbar-v2-design.md` §2/§3/§4/§5/§7/§8, prototype, Avalonia spike `research/05-avalonia-spike.md`, Core pager contract `FluidBar.Core.Interaction` |
| Date | 2026-10-18 |

This document is the integration contract for the Avalonia shell: host windows, shell rendering, motion,
navigation pager, settings/marketplace UI, i18n, and the seams other teams plug into.

---

## 1. Architecture map

```
IslandHost (Hosting/IslandHost.cs)                    composition + orchestration (UI thread)
 ├─ EventHub (Core) ──► ActivityScheduler (Core) ──► IslandStateController (Core)
 │        ▲                       │                              │
 │   Platform producers      ActivitySchedulerState          IslandTransition.Effects
 │                                ▼                              ▼
 │                     ActivityPresenter (Core)        Effect executor (host code)
 │                                ▼                              │
 │                     IslandPresenter (App)                     │
 │   DesignIslandDataSource ──► CompactContentModel + ExpandedPageContext
 │                                │                              │
 └─ per monitor: IslandWindow (fixed 488×332 / dock 488×392 DIP, transparent, topmost, no-activate)
        ├─ IslandShell (custom squircle render + ghost stack + shadow, no per-frame layout)
        │     ├─ CompactSurface (one per activity style, exact compact tokens)
        │     └─ ExpandedPanel (horizontal pager: title / chevron / dots overlays + page strip)
        └─ IIslandWindowBackend → Platform.Windows IslandWindowInterop (styles, hit-test, placement,
                                 topmost policy, settle region, click-through)
ShellMotionDriver (two spring axes + hover/press/nudge/hide) drives IslandShell frames; it never
touches window bounds. ExpandedPanel snap uses Core PagerSnapMotion (motion.tab).
```

Rules enforced by the code and the headless tests:

- The HWND is created once per monitor at its mode size and **never moved/resized during animation**;
  placement is one physical `SetWindowPos` per monitor/anchor/mode change (`IslandWindow.PlaceOn`).
- The island morph is **render-only**: `ShellMotionDriver` produces `ShellFrame` values; `IslandShell`
  applies them via `RenderTransform` + `Opacity` + a render-only clip and one `InvalidateVisual()`.
- Business rules (priorities, expiry, stacking, state transitions) live in Core; App executes Core
  `IslandEffect` data only.
- Paging is **transform-only**: the six page roots are created once and cached; switching pages never
  re-creates a page, never changes the shell size. Content cross-fade is applied where content is
  replaced by events (spec 2.7), not to page switches.

---

## 2. Visual token contract (DIP @96)

| Token | Value | Notes |
|---|---|---|
| host window | **488×332** float/flush; **488×392** dock | fixed per mode, one window per target monitor |
| window padding | 24 | shadow/antialias bleed applied once on both axes (Core `WindowPlacementConverter`) |
| expanded shell | **440×284** uniform | six pages identical; page switch never resizes |
| pager viewport | **400×252**, inset **(20, 16)** | Core `PagerConstants.ContentWidthDip/HorizontalInset/VerticalInset` |
| compact idle / media / HUD | 132×36 / 226×40 / 208×40 | HUD is the single-row icon·label·track·value form |
| compact notification / clipboard / agent / timer / battery | 248×36 / 264×36 / 236×36 / 190×36 / 208×36 | all from Core `LayoutTokens` |
| compact progress / lockkeys / device / bluetooth / focus / lookup | 236×36 / 180×36 / 240×44 / 280×44 / 220×36 / 480×44 (clamped to 440 host) | remaining activity classes |
| peek delta | +10 w / +6 h | spring `motion.hover` |
| radii | expanded 28; compact h/2 cap 44; flush bottom 22; dock 12/16; card 12; row 8; button 10 | squircle k derived from `(w,h,radii)` (Core `ShellGeometry`) |
| island colors | `#08080A` fill @0.96, raised `#141418`, inset `#0E0E11`, stroke `#1E1E22`, fg `#FFFFFF`, secondary `#9B9BA5`, tertiary `#6E6E78` | palettes: obsidian/graphite/slate/nord; island stays dark in every desktop theme |
| accents | accent `#4CC9F0` fallback, success `#34C759`, warning `#FF9500`, danger `#FF453A`, info `#0A84FF` | one accent per state; no rim light/gradients on the island |
| typography | UI: Segoe UI Variable Text → Segoe UI → Microsoft YaHei UI → Yu Gothic UI → Arial; Display/Mono stacks in `Theme/Fonts.cs` | 13.5 title, 11 caption, 15 page, 34 dashboard clock, 12.5 HUD value, 12 badge |
| page dots | 6 dots, active = 20×5 DIP pill, inactive = 5×5 | bottom-centre overlay; clickable |
| title / chevron overlays | title top-left, collapse chevron top-right | drawn by `ExpandedPanel` overlay (not by pages) |

Materials (final spike, 2026-10-18): **Tier 0 Obsidian is the default**. Native pill-acrylic is
NO-GO on Avalonia 12 (whole-window rectangular blur). Tier 1 = self-drawn simulated glass
(gradient + top highlight, lower fill alpha) behind the `IslandMaterial.Acrylic` setting; Tier 2
(experimental whole-window `AcrylicBlur`) remains a diagnostic-only path. The island fill never goes
below 0.85 opacity for text-bearing states.

---

## 3. Navigation pager contract (frozen 2026-10-18, replaces the left rail)

The rail is **superseded**; Core no-rail layout is authoritative
(`IslandLayoutResult.Content` = 400×252, `LayoutTokens.ContentWidth = 400`,
`ContentUsableHeight = 252`, `Rail*`/`HasRail` obsolete).

Implementation: `Island/Expanded/ExpandedPanel.cs` (pointer/wheel/keyboard/events + overlays) over
`PagerMotion` (gesture bookkeeping), which delegates every decision to Core:

| Concern | Core API | App behavior |
|---|---|---|
| drag offset + edge rubber-band | `PagePagerPolicy.Drag(startPage, accumulatedDragDip)` | damped 1:1 (0.90), 0.35 rubber band past first/last |
| release/snap decision | `PagePagerPolicy.Release(startPage, drag, velocityDipPerMs)` | distance decides (round); multi-page allowed only beyond the long-drag fraction; quick flick clamps to ±1 page; <24 DIP without flick snaps back |
| snap animation | `PagerSnapMotion` (motion.tab: T 0.34 s, ζ 0.92) | interrupted by a new drag from the current visual offset (`Interrupt()`) |
| wheel | `PagerWheelPolicy` (notch 100 DIP, 120 ms cooldown, `controlOwnsGesture`) | one notch = one page; horizontal trackpad axis supported; suppressed while a slider owns the gesture |
| keyboard | `ExpandedPanel.HandleKey` | 1–6 jump, ←/→ one page, Home/End, Esc collapse (host) |
| click suppression | `MovedPastClickThreshold` (>6 DIP) | page-internal buttons/sliders only fire when the gesture did not drag |

Sign convention: positive drag = towards the next page (App feeds `-pointerDeltaX`).

Invariants:
1. Quick flick → **at most one page**. 2. Long continuous drag → multiple pages by distance.
3. Small drag (<24 DIP) → snap back, no page change. 4. Snap spring interruptible when a new drag
starts. 5. Dots + title + chevron are overlays; page content never reflows during paging.

---

## 4. Motion contract

| Element | Token / rule |
|---|---|
| width axis | Core `motion.expand` (T .36, ζ .82) |
| height axis | Core `motion.expand.height` (T .39, ζ .86), independent spring |
| collapse | Core `motion.collapse` (T .30, ζ 1.0) |
| peek / press / nudge / hide | `motion.hoverPop` / `motion.press` / `motion.nudge` (−6 DIP) / `motion.hide` |
| pager snap | `motion.tab` via Core `PagerSnapMotion` |
| integration | Core `Spring` fixed 1/300 s substeps, settle thresholds 0.04 DIP / 0.35 DIP/s for 2 consecutive frames |
| frame source | `TopLevel.RequestAnimationFrame` one-shot re-arm (Avalonia 12; `CompositionTarget.Rendering` is not public); 250 ms watchdog falls back to a Render-priority `DispatcherTimer`; **loop stops at settle** (0 frame callbacks when idle) |
| content opacity | `ContentOpacityPolicy` derived from shell height; compact fades inversely |
| T1 reveal | hidden → compact is instantaneous (snap), no expand animation |
| reduce motion | `MotionProfile` (ζ→1, T×0.5, nudge 0, durations halved); motion strength scales T |
| host bounds | **never animated** — verified by unit tests (no window-bounds API in the motion path) |

---

## 5. i18n contract

- **Backend**: JSON catalogs at `src/FluidBar.App/Localization/en-US.json` and `zh-Hans.json`
  (Avalonia resources, `avares://FluidBar/Localization/<locale>.json`), spec 7.1. `en-US` is the
  source; `zh-Hans` is first-party; `qps-ploc` is generated from `en-US` at startup
  (`PseudoCatalogBuilder`: brackets, vowel accents, +35 % growth, placeholders preserved).
- **Fallback**: requested → parent (zh-Hans-CN → zh-Hans → zh) → en-US → key. Debug builds can show
  `⟦key⟧` (`LocalizationService.CreateDefault(showMissingKeys: true)`); Release falls back to en-US
  content then the key.
- **Runtime switch**: `LocalizationService.SetCulture(tag)` bumps `Version` and raises `Changed`; the
  host re-runs `ApplyLocalization` on cached compact surfaces, all six pager pages, and the settings
  window (no restart, no page rebuild). The App sets culture when `SettingsController.Language` changes.
- **Formatting**: ICU subset (`Localization/MessageFormatter.cs`): named args, `plural`
  (`=0/one/two/few/many/other`, CLDR categories for en/zh/ru/ar/pl), `number`, `date`, `select`, `#`,
  quote escaping. Durations/percentages go through templates, never `TimeSpan.ToString()`.
- **Fonts**: explicit fallback stacks (`Theme/Fonts.cs`); Microsoft YaHei UI is mandatory for CJK
  coverage. No bundled CJK font.
- **Keys**: `LocKeys` (generated from `en-US.json`; tests enforce parity with the catalog and with
  `zh-Hans`). Visible plugin-produced strings stay user data (never translated); host chrome (LIVE,
  muted, relative time, badges) is localized.
- **Locale keys**: `system`, `en-US`, `zh-Hans`, `qps-ploc` (`FLUIDBAR_PSEUDO=1` enables pseudo).

---

## 6. Settings & marketplace UI contract

`Views/Settings/SettingsWindow.cs` — 900×620 custom chrome, seven sections:
General / Appearance / Island / Plugins / Marketplace / Advanced / About. All changes are applied live
through `SettingsController.Update(...)`; the island host listens to the same controller (appearance:
palette/material/accent/fill opacity/font scale/motion; island: mode/anchor/stacking/hover peek/timers/
display target; general: language/startup/tray/updates; plugins: enable toggles; advanced: diagnostics,
safe mode, export/import, two-step reset; about: version/build/license/links).

Persistence: `Settings/AppSettings.cs` is the UI-facing strongly-typed model (spec 8.2 field names,
clamped in `Normalize()`, atomic debounced JSON via `JsonUiSettingsStore` at
`%APPDATA%\FluidBar\settings.ui.json`). **Integration note for Lead/Core**: Core's
`FluidBarSettings`/`SettingsStore` has landed; `IUiSettingsStore` is the seam — implement a
`CoreSettingsStoreAdapter` that maps `AppSettings ↔ FluidBarSettings`, call `SettingsStore.Load/Save`
inside `Task.Run` (no UI-thread I/O), and delete `settings.ui.json`. The UI code does not change.

Marketplace: `Views/Market/MarketplaceUi.cs` defines `IMarketplaceUiService` (browse/search/category,
install with staged progress, rollback, uninstall, consent plan). `DesignMarketplaceUiService` is the
deterministic preview/test implementation; production adapter wraps `FluidBar.Marketplace`
(index + install transaction). Install always goes through `Market.Consent` copy:
**“Plugins run with the same access as FluidBar. Only install plugins you trust.”** — capabilities are
disclosure, not a sandbox (spec 6.5); no “reviewed = safe” wording anywhere.

Plugins: `Views/Settings/PluginUi.cs` defines `IPluginUiService` (list/version/author/capabilities/
enable toggle); production adapter wraps `FluidBar.PluginHost` and `BuiltinPluginCatalog`. Declarative
plugin settings pages (`contributes.settingsPages`: toggle/select/slider/number/text/path/secret/
button/link/info/status) are **not yet rendered** — the contract is frozen by plugin-owner; the
generic renderer is the next UI increment (no plugin XAML, ever).

---

## 7. Hosting contract (App ↔ Platform.Windows)

| Item | Contract |
|---|---|
| window styles | `IIslandWindowBackend.Attach` → Platform `ApplyIslandStyles` (NOACTIVATE/TOOLWINDOW/NOREDIRECTIONBITMAP) + topmost |
| hit-test | `RegisterHitTest(hwnd, Func<px,px,bool>)`; App predicate converts client px → DIP via monitor scale and reuses `IslandShell.ContainsPoint` (same squircle geometry that draws) — transparent margin and corners return HTTRANSPARENT, `WM_MOUSEACTIVATE` → MA_NOACTIVATE |
| placement | `IslandWindow.PlaceOn(monitor, mode, anchor)` computes Core `IslandLayout`/`WindowPlacement` and calls `PlacePhysical` once |
| settle region | `UpdateRegionOnSettle(hwnd, ShellGeometry, MonitorInfo)` only when springs settle (never per frame) |
| click-through | whole-window `WS_EX_TRANSPARENT` only in Hidden; interactive states rely on per-point hit-test |
| topmost | low-frequency reassert (≤1 Hz) through Platform policy (shell whitelist + full-screen suppression) |
| lifecycle | close = hide; explicit `Dispose` on shutdown; `WM_DPICHANGED`/display changes re-place then snap |
| no P/Invoke in App | enforced by only exposing HWND through `TryGetPlatformHandle()`; all native calls go through Platform |

---

## 8. Test contract (`tests/FluidBar.Ui.Tests`, 59 tests, headless-safe)

Avalonia.Headless + `HeadlessUnitTestSession` (xunit 2; `Avalonia.Headless.XUnit` is incompatible with
xunit 2.9.3 and must not be re-added). Test parallelization is disabled; UI tests dispatch through
`AvaloniaTest.Run/RunAsync`; motion/host tests use Core `ManualFrameClock` and a `FakeTimeProvider`.

| Area | Coverage |
|---|---|
| i18n | en/zh key parity, placeholder parity, LocKeys↔catalog parity, fallback chain, runtime switch + Version/Changed, pseudo locale, ICU plural branches (en/zh/ru/ar), escaping, parser negative cases |
| tokens/geometry | all 15 compact size tokens, uniform 440×284, 488×332 window, pager viewport 400×252/insets, squircle exponent parity with Core + cache bounds, content-opacity policy |
| motion | expand settle (amplitude-relative window), height lag, collapse + reverse retarget, hide fade, reduced motion (no overshoot, nudge 0), profile strength, frame geometry stays inside the fixed window, loop stops at settle |
| shell/compact | squircle hit-test matrix (inside/corner/outside/hidden), compact hit regions (media play/peek prev-next), surfaces measure at token sizes, localization repaint without rebuild |
| pager | flick ≤1 page, long drag multi-page, small drag snap-back, rubber band at edges, wheel notch/cooldown/trackpad axis/control suppression, interruptible snap, keyboard jump/bounds |
| host | start → idle pill, one placement per start, hover-peek via deadline, expand/collapse without resize or page rebuild, hidden → click-through + loop stopped + region update, activity event → compact 248×36 → TTL expiry, media/HUD model mapping |
| settings/market | normalization clamps, JSON round-trip, invalid payload fallback, controller Changed/persist semantics, seven sections build + instant zh relocalization, consent copy honesty (both locales), marketplace install stage order/rollback/uninstall, plugin toggle persistence |

Acceptance-adjacent manual harness (not a unit test):
`FLUIDBAR_SMOKE=1 <apphost exe>` captures nine `RenderTargetBitmap` PNGs
(`%TEMP%\fluidbar-evidence\fluidbar-01-idle … 09-collapsed`) plus a geometry/state log
(`%TEMP%\fluidbar-smoke.log`), used for the design review evidence in
`tests/FluidBar.Ui.Tests/Evidence/`.

---

## 8b. Integration update (2026-10-18, after Lead hand-off)

The App's `IUiSettingsStore` seam is now backed by Core `SettingsStore`:
`src/FluidBar.App/Settings/CoreSettingsStoreAdapter.cs` (+`SystemSettingsFileSystem`) maps
`AppSettings ↔ FluidBarSettings` via `ToAppSettings`/`ApplyToCore` (read-modify-write preserves
Core-owned hotkeys/activities/placement/dock tiles), loads/saves on the thread pool, debounces 500 ms,
uses Core's atomic protocol, logs failed loads/saves and never blocks the UI thread.
`CreateDefault` also performs the requested one-time legacy migration (`Settings/LegacySettingsImporter.cs`):
if `%APPDATA%\FluidBar\settings.ui.json` exists and no Core `settings.json` exists yet, it parses the
legacy App model, maps it through `ApplyToCore`, saves it via Core, and renames the legacy file to
`settings.ui.json.imported` (corrupt legacy files are preserved; an existing Core file always wins).
This runs inside the first background `LoadAsync`, never on the UI thread.
**Applied 2026-10-22**: the paused-file one-liner was applied when `AppBootstrap` was handed back: replace
`new JsonUiSettingsStore(JsonUiSettingsStore.DefaultPath, _logger)` with
`CoreSettingsStoreAdapter.CreateDefault(_logger)`, then remove the `settings.ui.json` adapter.
Tests cover mapping round-trip, Core-owned preservation and persistence via `InMemorySettingsFileSystem`.

Declarative plugin UI is implemented (`Views/Plugins/PluginContributionsUi.cs`):
`BuiltinPluginContributionUiService` reads `BuiltinPluginCatalog.Descriptors` (id, `LocalizedString`
name, capabilities, `PluginContributions.SettingsPages/Commands`) plus the Core-backed enable list;
`PluginSettingsPageRenderer` renders the frozen contract generically — loose controls then groups,
toggle/select/slider/number/text/path/secret/button/link/info/status, `LocalizedString` resolution
(catalog key → manifest locale map → key), read-only status formats (text/number/boolean/timestamp)
with unit, secrets always redacted (`••••••••`, read-only, never bound), buttons disabled unless a
command is declared and dispatched through `InvokeCommand`. `IPluginSettingsValues` is the host-owned
store seam. The Plugins settings section exposes a per-plugin Settings button that renders those pages.
Constructing plugin instances (`BuiltinPluginCatalog.CreateAll(BuiltinPlatformServices)`) and command
dispatch remain host composition (Platform-owner service bundle; paused files).

Marketplace adapter is implemented (`Views/Market/MarketplaceServiceAdapter.cs`): signed-index browsing
through `MarketplaceCatalogClient`, `MarketplaceIndexDocument` → `MarketPlugin` mapping (localized
names/summaries via host localization, verified publisher, downloads, featured, update state),
channel/category/search filters, and install/rollback/uninstall delegated to the
`IPluginInstallOperations` seam (host wires `PluginInstallService` + package sources). Index URL is
injected via `FLUIDBAR_MARKET_URL` until Core settings exposes it; without it the settings UI falls back
to the deterministic design catalog and installs fail with a localized message rather than pretending.
Tests cover index mapping/filtering/update state and install-state derivation.

Test suite after the archipelago increment: **83/83 passing** (`tests/FluidBar.Ui.Tests`), adding
`ArchipelagoAndStabilityTests`, `WheelScrollChainTests` and the idle-stability regression.

### Page Archipelago navigation (user-chosen variant A, 2026-10-18, replaces the page dots)

`Island/Expanded/ArchipelagoStrip.cs` + `ExpandedPanel` wiring: title top-left and collapse chevron
top-right stay; the dots are gone. A 34 DIP floating strip (subtle scrim) holds six 48×26 mini-islands
(8 DIP gap, radius 13); the active island grows to 72×30 and shows the localized page name. Each island
has a live micro-preview (home clock, media 16 px art + wave bars, notifications dot + lines, clipboard
lines, agent pulsing dot, widgets mini calendar). Reveal rules: hidden at rest, shown on page drag/wheel
and after a 300 ms hover in the bottom 12 DIP band, auto-hidden 1200 ms after the last activity; the
strip follows the page offset through a ~30 ms lag. Clicking an island jumps to its page; dragging the
strip scrubs pages by distance (tap-without-movement = jump). Wheel over the strip pages only.

Version-A tuning (App overrides until Core publishes the tuned constants): pointer follow **0.80**,
snap spring **T 0.44 s ζ 0.97** (Core `Spring`), wheel threshold **140 DIP**, cooldown **180 ms**,
snap-back threshold **26 DIP**; incoming pages scale **0.985→1** with **6 DIP** parallax; rubber band
0.35 and the flick≤1-page / long-drag-distance rules unchanged. All constants are asserted by tests;
when core-owner lands the updated Core pager constants + `PagerScrollChainPolicy`, only
`PagerMotion`/`WheelScrollChain` need swapping (single seam).

Wheel vs content scroll: interim single-owner gate (`WheelScrollChain`): content keeps the wheel while
it can scroll; at a boundary 40 DIP overscroll + 120 ms dwell turns the page, reverse cancels,
horizontal wheel and Shift+wheel always page, and a running page animation locks content scroll. Pages
are still custom-drawn (not scrollable), so the boundary path is what currently executes; swap to Core
`PagerScrollChainPolicy` on hand-off.

### Idle-crash fix (Lead report 0xE0434352)

Root cause found and fixed: `DesignIslandDataSource` clock `ITimer` callbacks run on the thread pool and
raised `Changed` there, so the host updated Avalonia controls off-thread (`InvalidOperationException`
"calling thread cannot access this object") ~30–75 s after startup. `RaiseChanged()` now marshals to the
UI thread (`Action<Action> uiPost`, injectable for tests; defaults to the Avalonia dispatcher). Verified:
apphost exe stayed alive 80 s with empty stderr (previously crashed). `Diagnostics/CrashLog.cs` adds a
last-resort unhandled-exception log (`%LOCALAPPDATA%\FluidBar\logs\crash.log`) and task-exception
logging; it needs one line in the paused `Program.Main`: `CrashLog.Install();` (platform-owner).

## 9. Known gaps / blockers (honest status)

1. **Transparency (desktop)**: the host window paints an opaque light-grey rectangle over the full
   host bounds in `CopyFromScreen` captures while diagnostics report
   `ActualTransparencyLevel=Transparent`. New evidence (Lead, 2026-10-18) shows the approved spike and
   a WPF layered probe are also grey in captures on this machine, so it may be a capture artifact of
   the ToDesk/GameViewer virtual displays rather than a render failure. Platform-owner owns the
   verdict/hotfix; no App changes are pending from UI. `src/FluidBar.App` must not change host code
   until that verdict.
2. **DPI**: `dotnet run` launches through the .NET host and bypasses `app.manifest`
   (PerMonitorV2) → the window behaves DPI-unaware. Always launch the apphost exe
   (`src/FluidBar.App/bin/Debug/net10.0-windows10.0.19041.0/FluidBar.exe`); Lead re-verifies 732×498 px
   once transparency is settled.
3. **Click-through desktop verification** blocked by (1): the WM_NCHITTEST hook is installed by
   Platform and the hit-test matrix is unit-tested, but the transparent-corner QA needs a verified
   visible transparent window.
4. **Dock mode**: 488×392 per-mode fixed window executed (no AppBar registration); accepted by Lead
   and being folded into spec/DECISIONS.
5. **Multi-monitor / 150 % RDP** unverified in this environment (one virtual display).
6. **Settings persistence**: `CoreSettingsStoreAdapter` + legacy import are implemented and tested;
   only the paused-`AppBootstrap` one-liner swap remains (see §8b). **Plugin settings pages /
   built-in registration UI** are live from descriptors (declarative pages, commands, redacted
   secrets); `BuiltinPluginCatalog.CreateAll(BuiltinPlatformServices)` and PluginHost command dispatch
   remain host composition for platform-owner. **Marketplace install** browse + `IPluginInstallOperations`
   delegation implemented; host wires `PluginInstallService`/package sources.
7. **Navigation**: Page Archipelago variant A is implemented (see §8b). Pending hand-off: Core
   `PagerScrollChainPolicy` + tuned Core pager constants (App currently carries the version-A
   overrides in `PagerMotion`/`WheelScrollChain`); visual review with the user is next.
8. **Idle crash**: fixed (thread-pool `Changed` marshalling) and verified 80 s idle; `CrashLog` is
   implemented but its one-line `CrashLog.Install()` hook lives in the paused `Program.cs`
   (platform-owner). Watch for platform-owner handback to wire it.
9. **Native acrylic/mica** not shipped (Tier 0 default + self-drawn simulated glass), per final spike.
