# FluidBar v1 — Current-Code Audit (memory, CPU/GPU, position/animation, architecture, feature parity)

**Audit type:** read-only static audit. No repository file was modified, the app was not launched, and no GUI window was created.
**Target:** `D:\build\GitLocal\FluidBar` (root checkout), git `e9ca526` ("notification verification code detection + settings theme transition animation").
**Date:** 2026-10-18.
**Method:** full reads of `MainWindow.xaml.cs` (3,466 lines), `SettingsWindow.xaml.cs` (1,888), `App.xaml.cs` (412), `IslandPresentation.cs` (898), `IslandSnapshotWindow.cs` (273), all 10 monitors, all 4 plugin folders, `EventSystem.cs`, `PluginManager.cs`, `Settings.cs`, `MainWindow.xaml`; targeted greps for timers, animations, subscriptions, COM/P/Invoke, caches, and window lifecycle; cross-check of the test harness (98 tests).

> **Important scope note — divergent worktree.** The root checkout is dated 2026-07-18. A newer copy exists at
> `.claude\worktrees\desktop-main-6ac0084e` (dated 2026-10-05) with **199 changed lines in `MainWindow.xaml.cs`**,
> **92 in `IslandPresentation.cs`**, a new `IslandEventIdentityPolicy` in `EventSystem.cs`, and new
> `docs/superpowers/specs/2026-09-09-interaction-core-design.md`. Where relevant the report marks whether a finding
> **persists in the newer worktree**, is **partially fixed there**, or is **root-only**. Unless stated otherwise, line
> numbers refer to the root checkout (the requested target).

> **Line-count correction.** The brief says "MainWindow.xaml.cs 3100 lines, SettingsWindow.xaml 2673 / .cs 1674".
> Measured with `[IO.File]::ReadAllLines`: `MainWindow.xaml.cs` = **3,466**, `MainWindow.xaml` = 628,
> `SettingsWindow.xaml` = **2,750**, `SettingsWindow.xaml.cs` = **1,888**, `FluidBar.Tests/Program.cs` = 1,854.
> `MainWindow` contains **8** `new DispatcherTimer` sites (5 long-lived fields + 1 art-retry field + 2 transient locals),
> not 15; the repo total is **18** timer-creation sites. The brief's "98 `BeginAnimation` calls" is exact for
> `MainWindow.xaml.cs` (repo total 146: 98 + 36 Settings + 11 snapshot + 1 App).

---

## 0. Verdict in one page

The app is a single 3.5k-line `Window` that owns every concern: event routing, layout policy, seven animation systems,
nine timers, media parsing, image decoding, color analysis, and window management. Its dominant failure mode is not one
leak but **a feedback loop between UI-thread blocking work and per-frame window-bounds animation on layered
(per-pixel-alpha) windows**:

1. **UI-thread I/O** — brightness DDC/CI + WMI at 5 Hz, volume COM activation at 6.7 Hz, cross-process `SendMessage`
   at 6.7 Hz, and **synchronous HTTP** (3.5 s × up to 4 hosts) inside the media poll. All on the dispatcher thread.
2. **Window-bounds animation** — every shell morph animates `Left/Top/Width/Height` of a `Window` (4 animations per
   window), so every frame is a `SetWindowPos` + a full re-render of a layered window; multi-island does this for up to
   8 windows.
3. **Snapshot window churn** — every event destroys all snapshot HWNDs and recreates them (and several early-return
   paths never recreate them, so the small islands simply disappear).
4. **Retention** — full-resolution album art (no `DecodePixelWidth`), unbounded lyric/metadata/accent caches, unbounded
   `%TEMP%\FluidBar\art` files, undisposed `Process`/COM/WMI objects.
5. **Input traps** — topmost, layered, hit-testable windows over the top-center of the screen; no `WS_EX_TRANSPARENT`,
   no `WS_EX_NOACTIVATE`. Clicking the strip is swallowed and can steal focus from the user's app.

That combination explains the reported symptom precisely: the process is cheap to kill (it is one process whose damage
is contained to its own dispatcher + native handles + temp files), but while it runs it degrades the whole desktop's
input latency and responsiveness.

**Severity counts:** P0 = 10, P1 = 13, P2 = 14. Full ranked list below.

---

## 1. Measured code metrics (evidence baseline)

| Metric | Value | Where |
|---|---|---|
| `MainWindow.xaml.cs` | 3,466 lines, 145 methods, 98 `BeginAnimation` sites | root |
| `MainWindow.xaml` | 628 lines, 3 `DropShadowEffect` | `MainWindow.xaml:154,295,434` |
| `SettingsWindow.xaml` / `.cs` | 2,750 / 1,888 lines, 21 `DropShadowEffect`, 36 `BeginAnimation` sites | root |
| `IslandSnapshotWindow.cs` | 273 lines, 11 `BeginAnimation`, 1 `DropShadowEffect` | `IslandSnapshotWindow.cs:88` |
| `DispatcherTimer` creation sites | 18 repo-wide; 8 in `MainWindow`; 3 Settings; 8 monitors; 3 plugins; 1 App | `grep "new DispatcherTimer"` |
| Live timers after startup (defaults) | ≥ 15 (5 MainWindow + 3 plugin × 2 + 8 monitors + App update timer) | see §3 |
| Idle UI-thread timer wakeups | **~46/s** (volume 6.7 + brightness 5 + IME 6.7 + lock 6.7 + hold-key 16.7 + media 2 + agent 1.1 + notifications 0.6 + clock 0.1) | §3 |
| `CompositionTarget.Rendering` | exactly 1 subscribe/unsubscribe pair | `MainWindow.xaml.cs:1391,1400` |
| Per-event animation clocks (typical) | ~20–30 (14 in `AnimateShell`, 7 in `ApplyIconAccent`, 2–4 content, 4 per snapshot window) | §3 |
| Unbounded collections | 8 | §2.1 F-06/F-12/F-22 |
| Empty `catch { }` | 31 single-line + 46 `catch` sites total | repo |
| Hard-coded zh-CN literals in C# | 261 | no `.resx` anywhere |
| Tests | 98, all pure-policy; 0 covering windows/timers/threading | `FluidBar.Tests/Program.cs` |

---

## 2. Findings, ranked by severity

Severity meaning: **P0** = can freeze, crash, lose user input, or grow without bound in normal use; **P1** = sustained
CPU/memory/handle cost or a visible correctness bug; **P2** = debt, hygiene, or latent risk.

### P0 findings

---

#### F-01 (P0) — Synchronous HTTP and blocking device queries run on the WPF dispatcher thread

**Evidence**

* Media poll starts on the UI thread: `MediaPlugin` timer tick `_ = SafePollAsync()` (`Plugins/Media/MediaPlugin.cs:100`),
  then `await _sessionProvider.ReadAsync(...)` (`:168`). `ReadAsync` awaits `RequestAsync()` and
  `TryGetMediaPropertiesAsync()` **without `ConfigureAwait(false)`** (`Plugins/Media/MediaSessionProvider.cs:29,67`), so
  every continuation resumes on the dispatcher.
* Inside `ReadAsync`, for every playing track with lyrics enabled:
  `lyricsProvider.TryGetCurrentLine(...)` (`MediaSessionProvider.cs:157`) → `KugouLyricsProvider.EnrichSnapshot`
  (`KugouLyricsProvider.cs:27`) → `GetMetadata` / `GetLyrics` (`:188,225`) → **`Http.GetStringAsync(url).GetAwaiter().GetResult()`**
  (`:609`) and **`Http.GetByteArrayAsync(url).GetAwaiter().GetResult()`** (`:590`).
* `GetMetadata` loops over **four hosts** (`KugouLyricsProvider.cs:201-217`) with a **3.5 s** client timeout
  (`:630-633`): worst case ≈ **14 s of UI-thread block on a cold track**. `NeteaseLyricsProvider` has the same
  sync-over-async pattern (`NeteaseLyricsProvider.cs:214,226-227`) with a 5 s timeout (`:244`).
* Brightness monitor polls every **200 ms** on a `DispatcherTimer` (`Monitors/BrightnessMonitor.cs:62`), and each poll
  performs a synchronous **DDC/CI** round trip (`EnumDisplayMonitors` → `GetPhysicalMonitorsFromHMONITOR` →
  `GetMonitorBrightness`, `:122-137`). On failure it permanently switches to **WMI**
  (`new ManagementObjectSearcher(...)`, `:97-113`) — also on the UI thread, every 200 ms.
* Volume monitor polls every **150 ms** and performs a COM activation + endpoint read each time
  (`Monitors/VolumeMonitor.cs:55,91-113`).
* Input-method monitor polls every **150 ms** and calls **`SendMessage`** into the foreground window's IME window
  (`Monitors/InputMethodMonitor.cs:63,159-165`) — a synchronous cross-process call that blocks until the target
  processes it (can hang if the foreground app is hung).

**Root cause.** There is no background/worker layer. Every data source is a `DispatcherTimer`; every I/O is either
synchronous or an `await` that captures the UI `SynchronizationContext`. The comment in
`MediaSessionProvider.cs:222` ("Run entire GSMTC operation on thread pool to avoid blocking UI thread") shows the team
already knew the problem — it was applied only to media *commands*, not to the read path.

**Recommended fix.** One `BackgroundService`-style host with a bounded `Channel<IslandEvent>` and a coalescing
dispatcher pump. All monitors become pure producers on a shared worker scheduler; I/O uses `ConfigureAwait(false)` and
`CancellationToken`; no `GetAwaiter().GetResult()` anywhere; DDC/CI and WMI move to a dedicated MTA thread with a
5–10 s interval and change-only publishing; brightness/volume use OS event APIs (`IAudioEndpointVolumeCallback`,
`WM_SETTINGCHANGE`/`SystemEvents.DisplaySettingsChanged`, `RegisterPowerSettingNotification`) instead of polling.

**Estimate.** Brightness DDC/CI 5 Hz at 5–30 ms/query ≈ **2.5–15 % of one core continuously**, plus 25–150 ms input
latency spikes; WMI fallback at 20–150 ms/query ≈ **10–75 % of one core**. Kugou cold-track path freezes the UI for
**3.5–14 s per new track / lyric miss**. (Static estimates; confirm with `dotnet-trace` + `dotnet-counters`.)

**Newer worktree:** **persists** (`MediaSessionProvider.cs` is unchanged there; brightness/volume polling unchanged).

---

#### F-02 (P0) — Every transition animates `Window.Left/Top/Width/Height`, on layered windows, per frame

**Evidence**

* `AnimateShell` clears then starts four window-property animations (`MainWindow.xaml.cs:3043-3057`):
  `AnimateProperty(WidthProperty…)`, `HeightProperty`, `LeftProperty`, `TopProperty` — plus 6 scale/skew animations and
  2 opacity/content animations.
* `MorphWindowTo` does the same (`:1210-1216`); `Collapse` does the same (`:3158-3170`); `RestoreWindowToCurrentView`
  does the same (`:1425-1429`).
* `IslandSnapshotWindow.Place(animated: true)` starts **four `DoubleAnimation`s on the Window itself**
  (`IslandSnapshotWindow.cs:227-230`), called for every snapshot on every sync (`MainWindow.xaml.cs:1034-1039`).
* The main window is `AllowsTransparency="True"` (`MainWindow.xaml:6`) with `Background="Transparent"`, and each
  snapshot window is also per-pixel-alpha with a `DropShadowEffect` blur (`IslandSnapshotWindow.cs:70-71,88-94`).
* WPF per-pixel-alpha windows take the layered-window composition path; animating `Window` bounds forces a
  `SetWindowPos` + window-surface resize + full re-render **per frame** (and the blur effect re-runs per resize).

**Root cause.** The animation model animates the OS window rectangle instead of the visual tree inside a fixed-size
host. `HoverCardMotionPlan` even carries an explicit `AnimatesWindowBoundsEveryFrame` flag
(`IslandPresentation.cs:186`) that is **hard-coded `false`** while the code actually resizes the window on every hover
event (`MainWindow.xaml.cs:1283-1284`).

**Recommended fix.** One topmost, non-resizing, max-size host window (or one per monitor) that is `WS_EX_TOOLWINDOW |
WS_EX_NOACTIVATE`. Animate a `ScaleTransform`/`TranslateTransform` (or a `Viewbox` + layout rounding) on the island
visual; never touch `Window.Width/Height/Left/Top` after creation. Replace the blur with a pre-rendered 9-slice shadow
or a drop shadow drawn in the composition layer. If multiple islands are needed, draw them in the same window.

**Estimate.** One transition = ~60 frames × (2 windows × 4 `SetWindowPos`) ≈ **480 window ops/s** during a morph; with
3 snapshots ≈ **960 ops/s**. Each op re-renders a transparent window (and blur) in software composition. Multi-island
event bursts become a whole-desktop stutter source.

**Newer worktree:** **persists** (same `AnimateProperty(WidthProperty/HeightProperty/LeftProperty/TopProperty)` and
snapshot `Place` animations).

---

#### F-03 (P0) — Snapshot islands are destroyed on every event and recreated per event

**Evidence**

* `ProcessEvent` begins with: `if (_snapshotWindows.Count > 0) CloseSnapshotWindows(immediate: true);`
  (`MainWindow.xaml.cs:548-549`). That is `Hide()` + `Close()` per window (`:1044-1064`).
* `SyncSnapshotWindows` then recreates windows from scratch: `while (_snapshotWindows.Count < snapshotCount)
  _snapshotWindows.Add(new IslandSnapshotWindow());` (`:1009-1010`) — a new WPF `Window`, new HWND, new visual tree,
  new `DropShadowEffect`, new brushes (`IslandSnapshotWindow.cs:67-161`).
* Several paths return **after** closing snapshots without ever recreating them, so the small islands vanish:
  lyric-only fast path (`MainWindow.xaml.cs:554-646`, `return` at `:646`), media-inactive path (`:660-682`),
  clock path (`:655-656`), media-active/non-multi path (`:711-712`).
* Snapshot `SetView` allocates 2 new `SolidColorBrush` per call (`IslandSnapshotWindow.cs:171-188`), and `Reveal()`
  starts a new 210 ms opacity animation every sync (`:233-245`), so each sync also allocates an animation clock per
  window.

**Root cause.** The snapshot list is treated as disposable per event rather than as a pooled, stateful presentation
layer. `SyncSnapshotWindows` conflates "reconcile count" with "recreate everything".

**Recommended fix.** Pool N snapshot windows for the app lifetime (N = `MaxVisibleIslands - 1`); show/hide and update
content in place; never destroy on event. Reconcile only on strategy change / monitor removal / app exit. Keep a
`Dictionary<source, SnapshotWindow>` so a source keeps the same window (and its animation state) across updates.

**Estimate.** Each event costs 1–7 HWND creates + destroys, each 10–40 ms of UI thread (window creation, HwndSource,
D3D/software surface, effect initialization), plus 2–8 MB transient native/window resources. With lyric updates every
2–5 s this is a continuous **~0.5–3 % CPU burn and handle churn even when idle-looking**, and a visible flicker.

**Newer worktree:** **partially fixed** — `IslandEventIdentityPolicy.ShouldPreserveMediaSnapshots` keeps snapshots for
incremental media events (`worktree MainWindow.xaml.cs:571-583`). Clock/other early-return paths, replacement events,
and HWND recreation on every non-incremental event still destroy/recreate.

---

#### F-04 (P0) — Vertical anchor is off by the 14 px shell bleed; bottom-anchored stacked island is pushed off-screen

**Evidence**

* The shell deliberately adds a 14 DIP transparent bleed on every side: `ShellBleedMargin = 14`, `ShellBleed = 28`
  (`MainWindow.xaml.cs:59-60`), root `Margin="14"` (`MainWindow.xaml:21`), window size = visual + 28
  (`ToWindowSize`, `:1463`).
* Horizontal placement compensates: `left = layout.Left + currentSlot.OffsetX - ShellBleedMargin` (`:993`).
* **Vertical placement does not**: `top = layout.Top + currentSlot.OffsetY` (`:994`). The pill is therefore rendered at
  `window.Top + 14`, i.e. **+14 DIP below the intended anchor** for `Top*` positions.
* For `Bottom`, `IslandGroupLayout` computes `top = screenH - height - 12` (`IslandPresentation.cs:894`), the window is
  `height + 28` tall and is placed at that `top`, so the **window extends 16 px below the screen** and the pill's bottom
  edge lands at `screenH + 2` (clipped).
* Non-stacked path is inconsistent in the opposite direction: `CalculatePosition` sizes `w/h` with `ToWindowSize`
  (`:493-494`) but keeps the 8/12 px anchor for the **window**, so the pill's top is at 22 px (not 8), and bottom
  pill margin is 26 px (not 12).
* Tests never assert Y: only `layout.Left`, `Slots[i].OffsetX`, `VisualWidth` are checked
  (`FluidBar.Tests/Program.cs:1376-1460`).

**Root cause.** Two coordinate systems (visual vs window) leak into each other ad hoc; the bleed is compensated on X
only, and the compensation lives in the caller instead of a single conversion layer.

**Recommended fix.** Define one coordinate contract: all layout math in *visual* coordinates; a single
`VisualRect → WindowRect` conversion applies `-ShellBleedMargin` on **both** axes and adds `ShellBleed` to size exactly
once. Unit-test the conversion for all six positions, both strategies, and both anchor axes. Prefer no bleed at all
(draw the shadow inside the visual via composition) so window rect == visual rect.

**Estimate.** Visible 14 DIP misalignment everywhere; bottom mode visibly clipped, and hit-testing rect extends below
the screen. Not a perf bug, but it is the anchor bug the user is most likely to notice.

**Newer worktree:** **persists** — `top = layout.Top + currentSlot.OffsetY` at `worktree MainWindow.xaml.cs:1077`.

---

#### F-05 (P0) — Topmost transparent windows swallow mouse input and can steal focus (no click-through)

**Evidence**

* Main window: `AllowsTransparency="True"`, `Topmost="True"`, `Background="Transparent"` (`MainWindow.xaml:6-8`).
  A non-null `Background` makes the whole window rect hit-testable — including the 28 px bleed border.
* The window keeps its 154 × 66 rect at all times. When collapsed, `PillBorder.Opacity` is set to `0`
  (`MainWindow.xaml.cs:334-336`), but neither the window nor the pill is made hit-test-invisible; `IsHitTestVisible` is
  changed only for hold-to-hide (`:1936,1949`). So an invisible strip at the top-center of the screen eats clicks.
* During hover, the window is resized to the *host* size (`:1283-1284`), i.e. card size + 32 px, so the input-blocking
  rect grows with the card.
* Snapshot windows: `Topmost = true`, `AllowsTransparency = true`, `IsHitTestVisible = false`
  (`IslandSnapshotWindow.cs:70-77`). `IsHitTestVisible` only removes WPF *element* hit-testing; the HWND still answers
  `WM_NCHITTEST` with `HTCLIENT`, and there is **no `WS_EX_TRANSPARENT` / `WS_EX_NOACTIVATE`** anywhere in the repo
  (`grep WS_EX` → 0 hits), so clicking a snapshot island can activate a non-activated topmost window and steal focus
  from the user's foreground app.
* During hold-to-hide the main window is `Opacity = 0` + `IsHitTestVisible = false` (`:1934-1938`) yet still a topmost
  HWND over the top-center strip.

**Root cause.** "Transparent" was treated as "click-through". In Win32 they are unrelated. There is no idle/active
window-mode state machine (interactive when expanded/hovered; click-through when collapsed/hidden).

**Recommended fix.** Explicit window modes: `Idle` → `WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW` and
return `HTTRANSPARENT` from `WM_NCHITTEST` (or use `SetWindowRgn`/`WM_NCHITTEST` to restrict the interactive region to
the pill); `Interactive` → remove `WS_EX_TRANSPARENT`, keep `NOACTIVATE` except for real controls. Snapshot islands
should always be click-through. Mirror the region with the visual bounds so the bleed never captures input.

**Estimate.** Every click in a 154 × 66 strip at the top-center (and up to ~790 × 300 while hovered, plus each snapshot
rect) is lost. On a maximized browser this is the tab strip — it reads as "the PC is broken", not "an app is buggy".

**Newer worktree:** **persists** (same window flags; no `WS_EX_*` in the newer tree).

---

#### F-06 (P0) — Unbounded retention: full-resolution album art, lyric/metadata caches, accent cache, temp files

**Evidence**

1. **Album art is decoded at full resolution twice.** `LoadBitmapImage` uses `BitmapCacheOption.OnLoad` +
   `IgnoreImageCache` but **never sets `DecodePixelWidth/Height`** (`MainWindow.xaml.cs:2645-2657`); the image is shown
   in a 20–44 px slot. `ResolveMediaAccent` then decodes the same file **again** through GDI+
   (`new Bitmap(path)`, `Plugins/Media/MediaArtworkColorAnalyzer.cs:17`), full size, and samples it with `GetPixel`
   (`:23-44`). Typical 1000² JPEG ≈ **4 MB**; 3000² ≈ **36 MB** per decode, on the LOH, twice per track, on the UI
   thread.
2. **Unbounded caches:**
   * `KugouLyricsProvider._metadataCache`, `_lyricsCache`, `_lyricsMissUntil`
     (`KugouLyricsProvider.cs:13-15`; inserts at `:215,221,262,248`) — **no eviction, no cap**.
   * `NeteaseLyricsProvider._lyricsCache`, `_missCache` (`NeteaseLyricsProvider.cs:17-18`, inserts `:53,61,65`) — no cap.
   * `MainWindow._mediaAccentCache` (`MainWindow.xaml.cs:62`, insert `:2673`) — one entry per artwork path ever seen;
     unbounded.
   * `NotificationMonitor._seenIds` (`Monitors/NotificationMonitor.cs:18`, `:70`) — every notification ID ever observed.
   * `MediaPlugin` static process caches (`MediaPlugin.cs:58-61`) — bounded, but static (survive plugin disable).
   * `ProcessAudioActivity.RecentAudioTicksByProcessId` (`ProcessAudioActivity.cs:16`) — removed only when the same pid
     is queried again (`:248`).
3. **Disk growth:** artwork is written to `%TEMP%\FluidBar\art` and never cleaned —
   `art_{HashCode.Combine(title, artist)}.jpg` (`MediaSessionProvider.cs:390-397`) and
   `kugou_{cacheKey.GetHashCode()}.jpg` (`KugouLyricsProvider.cs:580-584`). `string.GetHashCode` is randomized per
   process, so filenames are unstable across runs and accumulate. App icons go to `%TEMP%\FluidBar\icons`
   (`MediaSourceVisuals.cs:50-64`). Agent hook files accumulate in `%APPDATA%\FluidBar\agent-events\processed`
   (`AgentStatusPlugin.cs:108-123`) — never pruned.
4. **`LoadBitmapImage` result is also retained** by `_currentMediaIcon` and `_lastTriedIconResult`
   (`MainWindow.xaml.cs:63,2504-2505`), so the previous track's full bitmap stays alive until the next successful load.

**Root cause.** No cache policy layer; caches are plain `Dictionary` fields with no size/age budget; images are decoded
at source resolution; temp files have no owner/lifetime.

**Recommended fix.** Central `ImageCache` (LRU, byte-budgeted, `DecodePixelWidth = 64`, one decode per track shared by
icon + accent analysis); lyric cache with TTL + LRU (e.g. 32 tracks / 4 MB) and a separate `ConditionalWeakTable` for
in-flight requests; temp dirs under a single cache root with a startup/exit sweep (or `Path.GetTempPath` +
`FileOptions.DeleteOnClose` not possible for reused art → use a 7-day sweep); notification IDs as a fixed-size ring
buffer; hook processed files pruned to the last N.

**Estimate.** Transient: 4–36 MB LOH allocation per track (twice) → LOH growth/fragmentation and gen2 pauses.
Retained: ~5–30 KB per song of parsed lyrics → **2–10 MB/day**, 50–300 MB/month for all-day listeners; temp art
**20–200 MB/month**; accent cache ~0.3 KB/song. `_seenIds` ~10–100 IDs/day (small but permanent).

**Newer worktree:** **persists** — no `DecodePixelWidth` in `worktree MainWindow.xaml.cs:2728`, no eviction in the
worktree Kugou provider (searched for `Remove/Count/Evict`).

---

#### F-07 (P0) — `SystemEvents.UserPreferenceChanged` publishes into the WPF UI from a non-UI thread

**Evidence.** `App.SetupThemeWatcher` subscribes a lambda to `Microsoft.Win32.SystemEvents.UserPreferenceChanged`
(`App.xaml.cs:118-127`) and calls `_bus?.Publish(new IslandEvent(...))` directly. `EventBus.Publish` invokes handlers
synchronously (`EventSystem.cs:57-60`), and `MainWindow.OnEventTriggered` → `ProcessEvent` immediately touches WPF
objects (`MainWindow.xaml.cs:539-543`). `SystemEvents` raises handlers on its own dedicated message-pump thread — there
is no dispatcher marshaling anywhere in the chain. Result: a cross-thread `InvalidOperationException` from any WPF
object. `DispatcherUnhandledException` logs but **does not set `args.Handled = true`** (`App.xaml.cs:32-33`), so the
process terminates.

**Root cause.** The event bus has no thread-affinity contract; handlers assume "events are always raised on the UI
thread" (comment at `MainWindow.xaml.cs:541`), but one producer violates it.

**Recommended fix.** `EventBus.Publish` must capture the UI `Dispatcher` at construction and either (a) throw in debug
when called off-thread or (b) `BeginInvoke` automatically. Alternatively subscribe to WPF's
`SystemParameters.StaticPropertyChanged` (marshaled by WPF) or handle `WM_SETTINGCHANGE` on the main window's
`HwndSource`.

**Estimate.** Rare (theme/accent change) but a hard crash; also the theme event currently *shows the island* on every
`UserPreferenceCategory.General` change, which includes non-theme settings.

**Newer worktree:** **persists** (`EventBus` in the worktree is still a bare synchronous event).

---

#### F-08 (P0) — Window lifecycle: zombie process after island close; `Show()` on a closed SettingsWindow

**Evidence**

* `ShutdownMode="OnExplicitShutdown"` (`App.xaml:4`) and `MainWindow` has no `Closing` cancellation
  (`MainWindow.xaml.cs:227-238` only unsubscribes/stops). Closing the island window (Alt+F4, or a future close
  gesture) leaves the process alive with **all monitors and plugins still running** (they are owned by `App`, not by
  the window: `App.xaml.cs:52-92`) and no UI to stop them. The tray menu still works; nothing recreates the main window.
* `App.OpenSettings` creates the window once and later calls `_settingsWindow.Show()` (`App.xaml.cs:219-237`). The
  custom close button only `Hide()`s (`SettingsWindow.xaml.cs:902-913`), but the window has **no `Closing` handler** and
  `WindowStyle=None` does not disable Alt+F4. After a real close, the retained reference is a closed `Window`, and the
  next `Show()` throws `InvalidOperationException` (unhandled).
* `ExitApp` disposes managers then calls `Shutdown()` (`App.xaml.cs:258-269`), and `OnExit` disposes them **again**
  (`:271-281`) → `PluginManager.Dispose` and `SystemMonitorManager.Dispose` run twice; `MediaPlugin.Dispose` calls
  `_config.Save()` twice, `ClipboardPlugin.Dispose` saves twice.

**Root cause.** No `IWindowLifecycle`/window manager; `App` treats windows as permanent singletons and assumes they are
never closed.

**Recommended fix.** `Closing` either cancels and hides (tray-app semantics) or re-creates the window on demand;
`ShutdownMode=OnMainWindowClose` is *not* appropriate for a tray app — instead, explicit `App.Shutdown()` from tray
only, and `MainWindow.Closing` must be cancel-and-hide. Make disposal idempotent. On close, also stop/restart the
producer layer.

**Estimate.** A zombie process retains the full timer/COM/HTTP load (~46 wakeups/s, GDI/USER handles, caches) with zero
user-visible value — exactly the "invisible thing is eating my PC" report.

**Newer worktree:** **persists** (same shutdown mode and window handling).

---

#### F-09 (P0) — Hover card: per-frame layout thrash, per-event re-open, and show/hide races

**Evidence**

* `ApplyHoverSpringFrame` writes `IslandRoot.Width/Height`, `PillBorder.Width/Height`, `PillBorder.MaxWidth` **every
  rendering frame** (`MainWindow.xaml.cs:1340-1359`), gated only by a 0.75 px threshold (`:1344-1350`). Each write
  invalidates measure/arrange of the pill's text content at up to 60 fps.
* `PillBorder_SizeChanged` allocates a **new `RectangleGeometry` and assigns `Clip` on every size change**
  (`:207-213`) — so the spring also allocates ~60 geometries/s and invalidates rendering.
* While `_isHoverCard`, **every incoming event** calls `ApplyHoverCardContent` **and** `MorphHoverCard(...)`
  (`:764-779`), which resets the spring plan, sets `_hoverSpringHasRenderTime=false`, `_lastApplied* = NaN`, and
  **directly resizes the window** to the host size (`:1249-1284`). With media lyric events every few seconds, the card
  re-opens/restarts while the pointer is stationary — jitter and repeated window resizes.
* Lyrics: `UpdateHoverLyrics` attaches `parent.SizeChanged` **every lyric update** (`:1668-1673`), and the handler
  rebuilds the whole lyric canvas (`:1676-1683` → `:1685-1754`): creates new `TextBlock`s, calls `tb.Measure(...)`,
  starts an 80 ms fade-out whose `Completed` callback clears and re-adds children. During the hover open animation the
  parent size changes every frame, so this runs ~60×/s, each time queuing another `Completed` closure.
* `MouseLeave` starts a 170 ms timer (`HideHoverCard`, `:1184-1197`); because the host window is larger than the pill
  and the pill is being resized per frame, a `MouseLeave`/`MouseEnter` pair can fire during opening, producing
  close→open oscillation.

**Root cause.** Two animation systems (spring on layout properties + WPF window animation) and no idempotent
"desired card state" model; content rebuild is coupled to size changes.

**Recommended fix.** Animate only `ScaleTransform`/`TranslateTransform`; keep the card's visual tree at a fixed max
size and cross-fade content (`ContentPresenter` swap) instead of relaying out per frame; compute lyrics layout once per
content change (or use a `Viewbox`/`Canvas` with transform), never on `SizeChanged`; ignore card-open requests when a
plan for the same target is already running; debounce `MouseLeave` by ~120 ms and cancel on re-enter; set the window's
interactive region to the card bounds only.

**Estimate.** During a 400 ms open: ~24 full pill layout passes + ~24 clip geometries + ~24 lyric rebuilds if lyrics
exist; repeated per event while hovering. Visible as stutter/flicker and high UI-thread time on hover.

**Newer worktree:** **persists** (`ApplyHoverSpringFrame` still writes `PillBorder.Width` per frame at
`worktree MainWindow.xaml.cs:1423-1439`; `CompositionTarget.Rendering` still used).

---

#### F-10 (P0) — Media poll performs process/window/UI-Automation/COM scans every 500 ms

**Evidence** (`Plugins/Media/MediaPlugin.cs` and `MediaSessionProvider.cs`)

* Timer: 500 ms default (`MediaPlugin.cs:96-100`, `MediaPluginSettings.cs:18`).
* Every poll calls `GlobalSystemMediaTransportControlsSessionManager.RequestAsync()` and enumerates **all** sessions
  (`MediaSessionProvider.cs:29-43`); for each candidate it awaits `TryGetMediaPropertiesAsync` (`:67`) and reads the
  thumbnail (`:129`, `:378-401`) — **the byte array is read before the `File.Exists` short-circuit**
  (`:386-397`), so it copies the full image on every poll even when the file already exists.
* For browser sources, `FindBrowserSiteBadge` calls `EnumWindows` over all top-level windows and, for each visible
  window, `Process.GetProcessById(pid)` (`:298-326`).
* When GSMTC has no playing session, `FindPlayingMediaFallback` calls `Process.GetProcesses()` for **every** process
  every 5 s (`MediaPlugin.cs:534-556`), `EnumWindows` + `EnumChildWindows` **recursively**
  (`:567-610`, `:681-711`), and for Kugou a full **UI Automation** subtree walk
  (`AutomationElement.FromHandle` + two levels of `FindAll`, `:714-772`). It then enumerates **WASAPI** audio sessions
  per render device (`ProcessAudioActivity.cs:45-72`, `:128-190`).
* `ExtractAppIconPath` runs on every poll and calls `Process.GetProcessesByName` per candidate name
  (`MediaSourceVisuals.cs:28-56`), then writes a PNG the first time.
* `MediaAppCommandFallback.TrySend` also enumerates processes/windows on every control click
  (`MediaAppCommandFallback.cs:119-150`).

**Root cause.** Polling at 2 Hz for data that is event-driven in Windows (GSMTC has `MediaPropertiesChanged`,
`PlaybackInfoChanged`, `TimelinePropertiesChanged`; audio sessions have `IAudioSessionEvents`). Each poll re-does
expensive system enumeration.

**Recommended fix.** Subscribe to GSMTC session events and audio-session notifications; keep one cached
`ISessionManager`; on poll, diff only current session properties; move thumbnail reading to a background task with a
cached path keyed by a stable track hash (and check the file **before** downloading); cache browser badge per window
title; drop UI Automation entirely (use GSMTC title/artist or the window title); run fallback scans at ≤0.2 Hz and off
the UI thread.

**Estimate.** Per poll with a browser playing: 1–10 ms GSMTC + 0.2–4 MB image copy + 1–10 ms window enum + N process
opens (N = visible windows, often 50–150). With no GSMTC session: 200–400 `Process` objects + 2–10 ms window recursion
+ 10–40 ms WASAPI (20+ RCWs) + potentially 100s of ms UIA. At 2 Hz this is **1–5 % CPU and 0.4–8 MB/s allocation** at
idle, with multi-hundred-ms UI stalls on Kugou.

**Newer worktree:** **persists** (`MediaSessionProvider.cs` unchanged; `MediaSourceVisuals`/`ProcessAudioActivity` only
slightly changed).

---

### P1 findings

---

#### F-11 (P1) — COM/WinRT RCWs are created continuously and never released deterministically

**Evidence**

* `VolumeMonitor.GetVolume` creates `MMDeviceEnumerator` via `Activator.CreateInstance` and an endpoint object on **every
  150 ms poll** (`VolumeMonitor.cs:91-113`). No `Marshal.ReleaseComObject`, no cached endpoint.
* `ProcessAudioActivity.GetAnyProcessPlaybackState` creates enumerator, device collection, one `IMMDevice` per active
  render endpoint, `IAudioSessionManager2` per device, a session enumerator, and one `IAudioSessionControl(2)` +
  `IAudioMeterInformation` per session (`ProcessAudioActivity.cs:45-72`, `:85-106`, `:128-190`). None are released.
  Called up to 2×/s from the media poll.
* `MediaSessionProvider` obtains a new GSMTC manager per poll and WinRT session/property objects per session
  (`MediaSessionProvider.cs:29-43`) — no `Dispose`/release; WinRT RCWs finalize late.
* WMI: `BrightnessMonitor.GetBrightnessWmi` and `BluetoothMonitor.GetConnectedDevices` dispose the searcher but not the
  returned `ManagementObject`s/collection (`BrightnessMonitor.cs:101-109`, `BluetoothMonitor.cs:71-80`);
  `UsbMonitor.OnDeviceChanged` reads `e.NewEvent["TargetInstance"]` without disposing the event/object
  (`UsbMonitor.cs:66-67`).

**Root cause.** P/Invoke + COM interop without an ownership policy; the project even re-declares COM interfaces by hand,
so the compiler cannot help.

**Recommended fix.** Cache the endpoint enumerator + default endpoint per device change; implement
`IAudioEndpointVolumeCallback` for volume; use `Marshal.ReleaseComObject` in `finally` (or CsWin32/WinRT wrappers that
own lifetimes); keep one GSMTC manager; wrap WMI results in `using`.

**Estimate.** Volume: ~2–3 RCWs × 6.7/s ≈ **15–20 RCW allocations/s**; fallback audio: 10–40 RCWs × 2/s ≈
**20–80/s**. Finalizer pressure and native memory growth between GCs; COM objects can outlive their logical use by
seconds.

**Newer worktree:** **persists** (interfaces and call sites unchanged in substance).

---

#### F-12 (P1) — `Process` objects (and their OS handles) are never disposed

**Evidence**

* `MediaPlugin.FindPlayingMediaFallback`: `foreach (var process in Process.GetProcesses())` — no `using`
  (`MediaPlugin.cs:539-553`).
* `MediaSourceVisuals.ExtractAppIconPath`: `Process.GetProcessesByName(name)` results stored in `List<Process>`, never
  disposed (`MediaSourceVisuals.cs:36-45`); called on every media poll.
* `MediaAppCommandFallback.FindTargetWindows`: `Process.GetProcessesByName` not disposed
  (`MediaAppCommandFallback.cs:126-134`).
* `MediaSessionProvider.FindBrowserSiteBadge` *does* use `using var proc` (`:306`) — the one place it is correct.

**Root cause.** `Process` implements `IDisposable` (holds a `SafeProcessHandle`); the code relies on finalizers, which
is exactly how handle counts balloon under GC pressure.

**Recommended fix.** `using`/`Dispose()` every `Process`; better, avoid `Process.GetProcesses*` in a polling path —
cache pids per 10–30 s and enumerate windows by pid.

**Estimate.** ~200–400 `Process` objects per `GetProcesses()` scan at 0.2 Hz, plus ~N per media poll (N = player
process count, e.g. Chrome 20–40) → **hundreds of handle allocations per minute**, reclaimed only by finalization;
observed as rising handle count between gen2 collections.

**Newer worktree:** **persists**.

---

#### F-13 (P1) — `NotificationMonitor` grows a HashSet forever and sorts the entire notification history at 1.6 Hz

**Evidence.** `_seenIds` is a `HashSet<uint>` (`NotificationMonitor.cs:18`); `if (!_seenIds.Add(notification.Id))
continue;` (`:70`) adds **every** notification returned by `GetNotificationsAsync`, including ones older than `_startTime`
(the age check happens *after* the add, `:73-74`). No removal path. `GetNotificationsAsync` returns the full history;
the code sorts it every poll: `notifications.OrderBy(item => item.CreationTime)` (`:68`).

**Root cause.** "Seen" tracking by unbounded set; polling API that returns history.

**Recommended fix.** Track only IDs newer than a watermark `CreationTime` (plus a small ring buffer for out-of-order
delivery); query incrementally; or use `UserNotificationListener.NotificationChanged` event.

**Estimate.** HashSet ~30–50 B/ID → negligible memory (10–100 IDs/day), but a full-history fetch + `OrderBy` every
1.6 s is a steady allocation/latency cost that grows with notification count.

**Newer worktree:** **persists** (notification monitor unchanged in size).

---

#### F-14 (P1) — Input-method monitor uses synchronous cross-process `SendMessage` at 6.7 Hz

**Evidence.** `SendMessage(imeWnd, WM_IME_CONTROL, IMC_GETOPENSTATUS/IMC_GETCONVERSIONMODE)` on the dispatcher timer
(`InputMethodMonitor.cs:159-165`, timer `:63-68`). If the foreground app's IME window is hung, the call blocks the
FluidBar UI thread for the system timeout (up to seconds). `GetForegroundWindow` + `GetWindowThreadProcessId` +
`GetKeyboardLayout` also 6.7×/s.

**Root cause.** Polling an inherently event-driven subsystem; `SendMessage` instead of `SendMessageTimeout`.

**Recommended fix.** Use `WM_IME_NOTIFY`/`IMN_SETCONVERSIONMODE` via an `HwndSource` hook, or at minimum
`SendMessageTimeout(..., SMTO_ABORTIFHUNG, 50 ms)` and a 500 ms poll; skip when the foreground window is unchanged.

**Estimate.** Normally <0.2 % CPU; pathological case: a full UI freeze whenever the foreground app hangs. Also
contributes to the "everything stutters when some app is busy" experience.

**Newer worktree:** **persists**.

---

#### F-15 (P1) — Hold-to-hide polls five virtual keys every 60 ms, enabled by default

**Evidence.** `_holdToHideTimer` interval 60 ms (`MainWindow.xaml.cs:187-188`); `HoldToHideKeyPolicy` default is
`LeftAlt` (`Settings.cs:67`), `UpdateHoldToHideTimer` starts it whenever the key is not `Disabled`
(`MainWindow.xaml.cs:1916-1926`); each tick calls `GetAsyncKeyState` up to 5× (`:1903-1909`).
`GetAsyncKeyState` is a user32 call per key — 83 calls/s at idle.

**Root cause.** Global hotkey implemented by polling instead of a keyboard hook or `RegisterHotKey`.

**Recommended fix.** `WH_KEYBOARD_LL` hook (or `RegisterHotKey` for a discrete shortcut) with a debounce; when hidden,
also stop wave/scroll timers and make the window click-through.

**Estimate.** ~0.1–0.3 % CPU and 16.7 scheduler wakeups/s forever; measurable battery impact on laptops.

**Newer worktree:** **persists**.

---

#### F-16 (P1) — Rim "Always" mode runs 60 fps animations even when the island is invisible

**Evidence.** `StartRimContinuous` starts an `AutoReverse/RepeatBehavior.Forever` opacity animation (1.8 s) and a
360° rotation (12 s) on `RimBrush`/`RimRotation` (`MainWindow.xaml.cs:1986-2011`). It is called from `ApplyRimMode`
whenever `RimMode == "Always"` (`:1973-1983`), independently of `_isExpanded`/`PillBorder.Opacity`. `Collapse` does not
stop it; `Window_Closing` does (`StopRimBreathing`, `:236`).

**Root cause.** Continuous animation not tied to visibility.

**Recommended fix.** Start rim animation only while the shell is visible & `IsVisible`; stop on collapse/hide; prefer a
composited rotation (or a shader) and a shared `Timeline` with `DesiredFrameRate` capped.

**Estimate.** A perpetual render loop keeps WPF's animation/render thread busy at 60 fps → **1–5 % CPU + GPU** for an
invisible element; on battery it prevents low-power states.

**Newer worktree:** **persists**.

---

#### F-17 (P1) — Position/DPI/multi-monitor: setting changes don't apply, display changes aren't handled

**Evidence**

* `ApplySettings()` never calls `PositionWindow()`/`PositionAtCurrentSize()` except when `_settingsPanelOpen`
  (`MainWindow.xaml.cs:400-409`); changing `Position`/`OffsetX`/`OffsetY` in settings while the panel is closed leaves
  the window where it was until the next event/collapse.
* After a collapse, placement goes through the **non-stacked** `CalculatePosition` (`:3164`), ignoring the stack layout
  for the anchor.
* All placement uses `SystemParameters.PrimaryScreenWidth/Height` (`:495-496`, `:986-987`) — primary monitor only, in
  system-DPI DIPs; no `SystemParameters.WorkArea` (taskbar) and no per-monitor DPI conversion. Secondary monitors with a
  different scale factor get wrong coordinates; the island cannot be placed on a non-primary monitor at all.
* No handler for `SystemEvents.DisplaySettingsChanged` / `WM_DPICHANGED` / `SystemParameters.StaticPropertyChanged`;
  after resolution/DPI/monitor changes the window stays at stale coordinates (possibly off-screen).
* Fixed pixel margins (8/12/16) and `Math.Clamp(x, margin, screenW - w - margin)` with offsets can produce negative
  math when `w > screenW`.

**Root cause.** Positioning was written for "one monitor, one DPI, fixed top-center" and never generalized.

**Recommended fix.** A `DisplayLayout` service: enumerate monitors (`EnumDisplayMonitors`/`MonitorFromWindow`), convert
visual→physical with the target monitor's DPI, clamp to that monitor's **work area**, and re-place on
`DisplaySettingsChanged`/`DpiChanged`. Add a per-monitor + per-anchor position setting (persisted). Make
`ApplySettings` call a single `Relayout()` that recomputes anchor + stack in one place.

**Estimate.** Correctness/UX, not CPU; but the "position keeps resetting / island on the wrong screen" class of bug.

**Newer worktree:** **persists**.

---

#### F-18 (P1) — Dangling artwork read tasks (no cancellation) and overlapping enrichment tasks

**Evidence**

* `MediaArtworkReadPolicy.ReadWithInitialTimeoutAsync` races the read against a 180 ms delay and, on timeout, **returns
  null while the original task keeps running**; it only observes faults (`MediaModels.cs:25-39`). No
  `CancellationToken`, no disposal of the WinRT stream. If the thumbnail read hangs, a new task accumulates per poll
  (2/s) with its stream/socket alive.
* `MediaPlugin.EnrichInBackground` is `async void` and launched via `Task.Run` (`MediaPlugin.cs:278,437`); there is no
  cancellation and no single-flight guard beyond the `_bgEnrichmentPending` bool. Rapid track changes spawn multiple
  concurrent enriching tasks (each blocking on HTTP up to 3.5 s × hosts), racing on `_lastEnrichedSnapshot`,
  `_lastSignature`, `_bgEnrichmentPending`. If a task never completes, `if (_bgEnrichmentPending) return;`
  (`:338-339`) **stops publishing media indefinitely**, i.e. the island freezes on the old track.
* `EnrichInBackground` also marshals only the final publish through `_timer.Dispatcher.BeginInvoke` (`:466-467`);
  earlier writes to shared fields happen on the thread pool while the UI thread reads them (data race on
  `MediaSnapshot` reference and strings — reference writes are atomic, but the logic is unsynchronized).

**Root cause.** Fire-and-forget async with a boolean latch as a concurrency control; no persistence of "request
generation".

**Recommended fix.** A single-flight `CancellationTokenSource` per track key + monotonically increasing generation
number; cancel the previous enrichment; await the timeout wrapper with a real cancellation token; publish completion
through the event channel (marshaled) rather than `Dispatcher.BeginInvoke` from a field; replace the bool with
`Interlocked` state.

**Estimate.** Under slow network/thumbnail hangs: unbounded task/stream accumulation; under normal use, wasted HTTP and
2–4 MB allocations per track.

**Newer worktree:** **persists** (`MediaArtworkReadPolicy` and `EnrichInBackground` unchanged).

---

#### F-19 (P1) — Settings persistence gaps: monitor toggles not saved, reset incomplete, dead/duplicated fields

**Evidence**

* Monitor enable/disable only mutates the runtime object: `MonitorToggle_Changed` → `_monitorManager.SetEnabled(...)`
  (`SettingsWindow.xaml.cs:1876-1885`, `Monitors/SystemMonitorManager.cs:32-37`). `MonitorFeatureSettings` has **no
  `Enabled` property** (`Settings.cs:185-191`), and nothing saves after the toggle → **the setting is lost on restart**.
* `ResetToDefaults` omits `SettingsTheme`, `AutoUpdateCheck`, `HooksGuardEnabled`, `HooksGuardIntervalMs`
  (`Settings.cs:152-182`).
* Duplicated, partly dead config: `HooksGuardEnabled`/`HooksGuardIntervalMs` (`Settings.cs:85-86`) vs
  `AgentHooksGuardEnabled`/`AgentHooksGuardIntervalMs` (`:78-79`); only the `Agent*` pair is used
  (`App.xaml.cs:59-60`). `ClipboardEnabled` (`Settings.cs:59`) is written (`:109`) but never read.
* `Settings.Save` swallows all failures and is a non-atomic `File.WriteAllText` (`Settings.cs:115-127`); `Load` swallows
  JSON errors and silently falls back to defaults (`:132-147`) — a corrupted/partial write resets all user settings on
  next start.
* `GetMonitorFeatureSettings` **mutates the dictionary on read** (`Settings.cs:88-96`) and is called from event paths
  (`MainWindow.xaml.cs:1086`) — a getter with side effects.
* The per-monitor "display duration" can be 1–8 s (`SettingsWindow.xaml.cs:1832-1839`), but `MainWindow` reads it only
  when resetting the collapse timer (`:2156-2161`), so events during a media hover ignore it.

**Root cause.** Settings model evolved ad hoc (`v1.0.1`, no schema version); two config layers (main settings + each
plugin's own JSON) with no migration.

**Recommended fix.** Versioned settings (`schemaVersion` + migrations), atomic write (temp + `File.Replace`), single
`Enabled` per monitor persisted alongside its feature settings, one canonical guard-config block, remove dead fields,
never mutate in a getter. Debounced save should be centralized and flushed on exit (it partly is:
`FlushPendingSettingsWork`, `SettingsWindow.xaml.cs:110-132`).

**Estimate.** Data loss (monitor toggles, settings on corruption); user-visible "my settings don't stick".

**Newer worktree:** **persists** (settings model identical).

---

#### F-20 (P1) — Settings window is a 2,750-line XAML tree that is never destroyed, with 21 blur effects

**Evidence.** `SettingsWindow` is created once on first open and only `Hide()`n (`App.xaml.cs:219-237`;
`SettingsWindow.xaml.cs:902-913`). Its full visual tree — 2,750 XAML lines across 5 tabs, 21 `DropShadowEffect`s — is
built in `InitializeComponent` (`:73`) and kept alive for the process lifetime. Plugin/monitor detail pages are rebuilt
imperatively on every selection: `PluginSettingsContainer.Children.Clear()` then re-add rows
(`:1294-1307`, `:1817-1818`). There is no MVVM/data binding; 89 methods manipulate controls directly; 261 hard-coded
Chinese strings.

**Root cause.** Code-behind-first UI with a singleton window; no view models; no disposal.

**Recommended fix.** Split per-tab views, data-bind to view models, virtualize lists (`SettingsPerformancePolicy.
UseVirtualizedLists` is declared but never used — `IslandPresentation.cs:797`), load tabs lazily, and either close or
explicitly release the visual tree on hide. Remove most `DropShadowEffect`s in favor of pre-rendered shadows/borders.

**Estimate.** 20–60 MB managed/WPF retained after the first time settings is opened; 200–500 ms first-open hitch while
parsing/building 2.7k lines and 21 effects.

**Newer worktree:** **persists**.

---

#### F-21 (P1) — Event bus: synchronous, no marshaling, no coalescing, no backpressure

**Evidence.** `EventBus` is a single `event Action<IslandEvent>` invoked synchronously (`EventSystem.cs:53-61`). Every
producer (timer ticks, WMI event threads, WinRT continuations, network callbacks, plugin threads, SystemEvents) calls
`Publish` and the UI handler runs inline. There is no queue, no coalescing of progress/position updates, no exception
isolation (a throwing subscriber takes down the producer thread), and no notion of event priority/thread affinity.

**Root cause.** The simplest possible pub/sub was enough for the prototype and never grew up.

**Recommended fix.** The v2 interaction core should own: (a) a single UI pump on the dispatcher, (b) typed events with
coalescing keys (e.g. `media.progress` replaces the previous pending one), (c) producer threads that never touch WPF,
(d) per-subscriber exception isolation + logging, (e) explicit `New/Replacement/Incremental` update kinds (the newer
worktree already started this with `IslandEventIdentityPolicy`).

**Estimate.** Not directly measurable, but it is the architectural reason *why* every P0 above reaches the UI thread.

**Newer worktree:** **partially addressed** — `IslandEventIdentityPolicy` (`worktree EventSystem.cs`) adds update
classification, but `EventBus.Publish` is still a bare synchronous invoke.

---

#### F-22 (P1) — Media control fallback injects global media keys and uses cross-process `SendMessage`

**Evidence.** `MediaAppCommandFallback.TrySend` calls `keybd_event(VK_MEDIA_*)` globally (`MediaAppCommandFallback.cs:
102-103`) — this targets **whatever app has focus**, not Kugou — then `SendMessage(hWnd, WM_APPCOMMAND, ...)` to every
Kugou window (`:105-114`, `:140-147`). `SendMessage` is synchronous and can block the UI thread; `FindTargetWindows`
leaks `Process` objects (`:130-132`). `_currentAumid` in `MediaSessionProvider` is never cleared when the session ends
(`MediaSessionProvider.cs:155`), so commands can target a stale source.

**Root cause.** A workaround for one player's GSMTC quirks became a global input-injection mechanism.

**Recommended fix.** Use GSMTC first; for players that need app commands, post `WM_APPCOMMAND` with `PostMessage` and
never inject global keys; scope by pid/window and clear the cached source on session change; run the lookup off the UI
thread.

**Estimate.** Wrong-app media toggles (e.g. your video pauses instead of Kugou), plus a potential multi-second UI freeze
if the player is hung.

**Newer worktree:** **persists** (only policy tests were added).

---

#### F-23 (P1) — Agent hooks: 900 ms directory scan and a guardian that rewrites the user's `~/.claude/settings.json`

**Evidence.**

* `_timer` interval 900 ms, `PollInbox` enumerates `*.json`, sorts by `File.GetCreationTimeUtc`, reads and parses each
  file (`AgentStatusPlugin.cs:34-35`, `:81-106`) — filesystem I/O on the UI thread 1.1×/s forever.
* `_guardTimer` every 30 s (`:37-38`) calls `CheckHooks`, which reads `~/.claude/settings.json`, and if not "installed"
  calls `RepairHooks` which **rewrites the user's global Claude Code settings** (`:137-151`, `:219-287`). This is an app
  silently mutating another tool's configuration on a timer; concurrent edits can be clobbered (read-modify-write
  without locking).
* `processed/` and `failed/` accumulate one timestamped JSON per hook event forever (`:108-123`); no pruning.

**Root cause.** Cross-process integration via a watched folder + a self-healing config writer, both unbounded.

**Recommended fix.** Use a named pipe/HTTP local endpoint or a proper IPC (or `FileSystemWatcher` instead of polling);
make hook installation an explicit user action with backup + merge + lock, never a timer; prune processed/failed with a
retention policy.

**Estimate.** ~1 % CPU I/O at idle; risk of corrupting the user's Claude Code configuration; unbounded small-file disk
growth.

**Newer worktree:** **persists**.

---

#### F-24 (P1) — Update check: per-call `HttpClient`, duplicated implementations, silent failure, 2 Hz island events

**Evidence.** Startup check creates two `HttpClient`s (`App.xaml.cs:304,357`), a timer started 4 s after launch
(`:97-104`), downloads with an 80 KB buffer and publishes a progress `IslandEvent` every 500 ms (`:369-385`), and
swallows every exception (`:397-400`). Settings has a **second, divergent** implementation
(`SettingsWindow.xaml.cs:809-875`) that instead copies the stream (`:858`) and shuts down the app to install (`:868`).
`PendingUpdateInstallerPath` is a mutable static (`App.xaml.cs:297`), and clicking the island executes a downloaded exe
(`MainWindow.xaml.cs:251-266`). No signature/hash verification of the downloaded installer.

**Root cause.** Two teams/times implementing the same feature; no shared `UpdateService`.

**Recommended fix.** One `IUpdateService` with a singleton `HttpClient`, GitHub ETag caching, SHA-256 + Authenticode
verification, a proper progress channel (throttled to ≤2 Hz, coalesced), and an explicit install flow (never
`Process.Start` an unverified temp file).

**Estimate.** One 250 KB–5 MB download per launch check (only when an update exists); negligible CPU otherwise. Security
risk from unverified execution.

**Newer worktree:** **persists**.

---

#### F-25 (P1) — Unbounded per-event allocations and duplicated presentation constants

**Evidence**

* 20 `new SolidColorBrush` sites in `MainWindow.xaml.cs`; `ShowStatusIndicator` allocates 3 brushes per event
  (`:2262-2291`), `SetStatusBadgeColors` 3 (`:2782-2793`), `ApplyHoverCardContent` 4–6 (`:1483,1495,1514,1783,1797`),
  snapshot `SetView` 2 (`IslandSnapshotWindow.cs:171-188`).
* `ShowProgressBar` allocates a new `LinearGradientBrush` per event (`MainWindow.xaml.cs:2237-2248`).
* `ApplyWaveAccent` allocates a new array per event (`:1881`).
* `IslandPresentationFactory.ParsePercent` calls `Regex.Match` per event with no compiled/cached regex
  (`IslandPresentation.cs:472-478`).
* Icon glyph/color/glow maps are **duplicated** between `MainWindow.xaml.cs:83-148` and
  `IslandSnapshotWindow.cs:25-65` (18 entries each) — guaranteed drift; accent/glow colors are also hard-coded in XAML.
* `MediaHoverTransportLayout` constants and compact layout constants live in `IslandPresentation.cs:82-113` while
  `MainWindow` duplicates the 24/50/30/50 math ad hoc.

**Root cause.** No shared theme/resource layer; brushes are created instead of mutated (`Color` set) or frozen resources.

**Recommended fix.** Frozen `SolidColorBrush` resources with animated `Color` on unfrozen instances only where needed;
one `IslandTheme` resource dictionary; one icon catalog; `[GeneratedRegex]`; avoid arrays in hot paths.

**Estimate.** ~10–20 small allocations per event; at 5–10 events/s during interaction → **50–200 allocations/s** plus
GC gen0 churn. Not the main killer, but it compounds F-02/F-09.

**Newer worktree:** **persists**.

---

### P2 findings

| ID | Finding | Evidence | Fix |
|---|---|---|---|
| F-26 | **31 empty `catch { }`** and 46 catch blocks; no logging except a crash log. Failures in media, COM, settings, update, clipboard, WMI are invisible. | `grep -c "catch {"`; `App.xaml.cs:283-292` is the only persistent log | Central `ILog` with rate-limited file sink; never swallow silently; log once per failure key |
| F-27 | **Global handlers log but don't handle**: `DispatcherUnhandledException` / `AppDomain.UnhandledException` / `TaskScheduler.UnobservedTaskException` (`App.xaml.cs:32-37`); `args.Handled` never set, so any dispatcher exception terminates the app | same | Set `Handled=true` for recoverable UI exceptions, show a tray notification, then reset state |
| F-28 | **Dead files/artifacts**: `MainWindow.xaml.bak` (9.9 KB), `MainWindow.xaml.cs.bak` (27 KB), `example.txt` (263 KB Windhawk mod source), `FluidBar-1.0.0dev-win-x64.zip` (6.7 MB), `.claude/worktrees/` (3 full copies), `publish/` | root listing | Delete; add `.gitignore`/CI guard |
| F-29 | **Catalog references a nonexistent plugin**: `Plugins/catalog.json:35` points at `Plugins/Notifications/NotificationsPlugin.cs`; only `NotificationModels.cs` exists. "4 plugins" is really 3 plugins + 1 monitor. | `Plugins/catalog.json:30-38` | Either implement the plugin or describe the monitor as a built-in feature |
| F-30 | **No i18n**: 261 zh-CN literals in C#, hundreds more in XAML, no `.resx`, no language setting, `en-US` impossible | `grep` | Resource-based strings + `CultureInfo` + language selector (see report 04) |
| F-31 | **Timer lifecycle duplication**: 18 creation sites, intervals hard-coded (60/150/150/150/200/500/900/1600/10 000/15 000/30 000/60 000 ms), start/stop logic repeated per monitor, no shared scheduler | §3 table | One `Scheduler` with named cadences, pause/resume tied to session lock/visibility |
| F-32 | **Snapshot opacity/color drift**: snapshots use `settings.Opacity * 0.94` clamped 0.62–0.96 (`IslandSnapshotWindow.cs:195`) while main uses `settings.Opacity` (`MainWindow.xaml.cs:334-336`); snapshot `Reveal()` always fades to 0.88 (`:241-245`) | same | One opacity source; no per-window fudge |
| F-33 | **Dead policy logic**: `ShouldSuppressFallbackAfterGsmLoss` ignores all arguments and returns `false` (`MediaSnapshotSelectionPolicy.cs:57-71`); `HoverCardMotionPlan.AnimatesWindowBoundsEveryFrame` always false while code does resize; `IslandAnimationPerformancePolicy.UsesElasticShellEase` unused | same | Delete or implement; policies must not lie |
| F-34 | **Test suite has no UI/threading coverage**: 98 tests, all pure policies; no test constructs `MainWindow`, a snapshot window, a timer, or an animation; no leak/perf harness. One test performs a **live network call** (`Kugou online lyrics smoke test`, `FluidBar.Tests/Program.cs:941`) | same | Add a WPF test host, dispatcher-pumped tests for position/stack/race, a soak test that asserts handle/memory ceilings, and mark network tests as opt-in |
| F-35 | **README overclaims**: "低资源占用", "全局快捷键 — 支持自定义快捷键快速隐藏" (there is no `RegisterHotKey`; only Ctrl+Alt+click and hold-Alt), "4 个内置插件" (see F-29), "主题感知" (theme watcher is broken cross-thread, F-07) | `README.md:45-58` | Rewrite after v2; do not ship claims that aren't implemented |
| F-36 | **Version/build inconsistencies**: `Version=1.0.1` (`FluidBar.csproj:15`) vs README "1.0.0dev"; no `app.manifest`; `PublishSingleFile=true` with WPF + `AllowsTransparency` can complicate startup/perf; `UseWindowsForms=true` only for tray/PowerStatus | csproj | Single source of version truth; explicit PerMonitorV2 manifest; review trimming/single-file |
| F-37 | **`GetMonitorFeatureSettings` side-effect getter** creates entries during event handling (`Settings.cs:88-96`) | same | Pure read with default; populate on load |
| F-38 | **`Application.Current.Dispatcher` reach-ins** from monitors (`NetworkMonitor.cs:41`, `UsbMonitor.cs:69`) and `_timer.Dispatcher` from a plugin (`MediaPlugin.cs:466`) — coupling and a null-`Application` danger in tests/headless | same | Inject `IUiDispatcher` |
| F-39 | **XAML/perf**: `TextOptions.TextRenderingMode="ClearType"` on a transparent window (ClearType is disabled/ignored for layered windows) and `UseLayoutRounding` + `SnapsToDevicePixels` everywhere, which forces extra layout passes during per-frame resizes | `MainWindow.xaml:16-19` | Remove ClearType from layered windows; keep layout rounding only where needed |

---

## 3. CPU / GPU budget: every timer and per-frame source

### 3.1 Timers (default configuration)

| Owner | Interval | Work per tick | Thread | Cost estimate |
|---|---|---|---|---|
| VolumeMonitor | 150 ms | COM activation + endpoint volume/mute | UI | 6.7 activations/s, ~2–3 RCWs each; 0.3–1 % CPU + finalizer churn |
| BrightnessMonitor | 200 ms | DDC/CI (or WMI fallback) | UI | 5 queries/s; **2.5–15 % CPU (DDC) / 10–75 % (WMI)**, 5–150 ms stalls |
| InputMethodMonitor | 150 ms | foreground window + `SendMessage` ×2 | UI | 6.7/s; normally small, hang risk |
| LockKeyMonitor | 150 ms | `GetKeyState` ×2 | UI | 6.7/s; negligible |
| HoldToHide (MainWindow) | 60 ms | `GetAsyncKeyState` ×5 | UI | 16.7/s, 83 calls/s; 0.1–0.3 % |
| MediaPlugin | 500 ms | GSMTC + sessions + artwork + fallback scan + WASAPI | UI | 2/s; **1–5 % CPU, 0.4–8 MB/s alloc**, stall risk |
| AgentStatusPlugin | 900 ms | directory enumerate + sort + read JSON | UI | 1.1/s dir scan |
| AgentStatus guard | 30 s | read (and maybe rewrite) `~/.claude/settings.json` | UI | negligible CPU, config risk |
| NotificationMonitor | 1.6 s | `GetNotificationsAsync` + sort | UI (async continuation) | 0.6/s history fetch |
| ClockMonitor | 10 s | string formatting + event | UI | negligible |
| BatteryMonitor | 15 s | PowerStatus | UI | negligible |
| BluetoothMonitor | 15 s | WMI `Win32_PnPEntity` query | UI | 0.07/s, 20–150 ms WMI spikes |
| MainWindow wave | 50 ms | 4 scale sets + progress `Width` layout | UI | 20/s while media/volume visible |
| MainWindow scroll | 16 ms | marquee transform | UI | 62.5/s while long text visible |
| MainWindow stack cleanup | 500 ms | prune + possibly re-layout/sync | UI | 2/s while stack non-empty |
| MainWindow collapse | event | collapse | UI | — |
| SettingsWindow ×3 | 120/220/280 ms | debounce | UI | only while settings is used |
| App update timer | 4 s one-shot | network | UI | once per launch |
| Local oneshots | 170/800 ms | UI cleanup | UI | per interaction |

**Idle floor:** ~46 timer wakeups/s on the dispatcher, before any animation frame. **During media:** +20 Hz wave + up
to 62.5 Hz scroll + 60 Hz spring (if hovering) + 60 Hz rim (if Always) + window-bounds animations. **Effective
UI-thread frame rate can stay pinned at 60 fps indefinitely** even when the user is doing nothing, because the rim,
wave, and spring animations are independent and can overlap.

### 3.2 Per-frame / per-event GPU and layout

| Source | Mechanism | Per-frame cost |
|---|---|---|
| Shell morph | 4 `Window` property animations + 6 transform animations (`MainWindow.xaml.cs:3041-3098`) | `SetWindowPos` + layered-window re-render + `PillBorder_SizeChanged` geometry (60 fps for 220–340 ms) |
| Snapshot place | 4 `Window` property animations per window (`IslandSnapshotWindow.cs:227-230`) | N × 4 window ops/frame + layered surfaces + `DropShadowEffect` re-render |
| Hover spring | `CompositionTarget.Rendering` → 4 layout properties (`MainWindow.xaml.cs:1298-1359`) | 60 fps measure/arrange + clip allocation + lyric rebuild |
| Rim | forever rotation/opacity (`:1986-2011`) | 60 fps brush/transform invalidation |
| Wave | 50 ms scale writes + `ProgressFill.Width` (`:3245-3277`) | 20 fps layout of the progress track |
| Scroll | 16 ms transform (`:3229-3243`) | 62.5 fps render invalidation |
| Blur | 3 effects on main (`MainWindow.xaml:154,295,434`), 1 per snapshot, 21 in settings | re-rasterized on every resize/scale; expensive on layered windows |
| Content | per-event text/metric/brush changes (`:2186-2480`) | measure/arrange of the pill per event |

**Main architectural point:** the app uses *window geometry* as its animation surface. That is the single biggest CPU
and GPU cost, and it scales with the number of islands.

### 3.3 GC pressure

* Album art twice at full resolution per track (F-06).
* Thumbnail bytes copied every 500 ms (F-10) — `stream.Size` byte array, even when cached.
* 10–20 brushes + 1–3 gradients + 2–4 arrays per event (F-25).
* 4–8 animation clocks + closures per event; `fadeOut.Completed` closures capture UI element lists.
* `Process`/COM/WMI RCWs finalization (F-11/F-12).
* `OrderBy` + full notification list every 1.6 s (F-13).
* Regex match per event (`IslandPresentation.cs:474`).

Static estimate: **0.5–8 MB/s steady allocation** when media is playing and a browser session is visible; LOH spikes
of 4–36 MB per track change.

---

## 4. Position & animation correctness (detail)

1. **Coordinate contract broken on Y** — F-04. Left compensates the 14 px bleed, top does not; bottom is pushed
   off-screen. Non-stacked math compensates size but not anchor.
2. **Snapshot lifetime coupled to events** — F-03. `ProcessEvent` closes first, then most paths forget to recreate.
3. **Snapshot stacking math** — `BuildStackedSlotMetrics` uses compact snapshot widths (`SnapshotWidth`, lines
   933-947) and full height (`SnapshotHeight`), while the main slot uses `_activeTargetWidth/Height`. During a main
   morph, `TryCalculateStackedMainPosition(targetWidth, targetHeight)` is called with the target, but `SyncSnapshotWindows`
   is called before the animation finishes; snapshots animate to the target while main is still mid-size → group looks
   misaligned for the duration. Main window `Left/Top` animation is 240 ms, size 220 ms, snapshot `Place` 360 ms —
   **three different durations** mean the group is never synchronized (evidence: `IslandAnimationPerformancePolicy`
   `:130-139`, `Place` 360 ms `IslandSnapshotWindow.cs:227-230`).
4. **Hover vs stack**: `MorphHoverCard` recomputes the group position with `_hoverHostWidth/_hoverHostHeight`
   (`MainWindow.xaml.cs:1249-1281`), which is `Math.Max` accumulated (never shrinks until `ClearHoverHostLayout`), so
   snapshots shift left/right as cards change; then `RestoreWindowToCurrentView` snaps them back with a different
   animation. This is the "main island vs small snapshot island coordination" bug the user reports.
5. **Show/hide races**:
   * New event during collapse: `Expand` sets `_isExpanded=true` and clears position animations mid-collapse
     (`:3167-3170` vs `:3054-3057`); the old collapse `fade.Completed` still fires later and resets `PillScale`
     (guarded for `HideAllPanels`, unguarded for scale).
   * `HideHoverCard` while `MorphHoverCard` is mid-open: it starts a close plan from the current spring value, but the
     earlier `MorphHoverCard` already resized the window to the *host* size; the close plan's target is the compact
     view, so the window shrinks only after the spring settles (`:1324-1328`). If a new event arrives in between, the
     close plan is replaced and the card may stay open at the wrong size.
   * `MouseEnter` on the window (not the pill) means moving the mouse across the 14 px bleed opens the card.
6. **NaN-origin workaround** (`IslandSnapshotWindow.cs:219-224`): confirms WPF .NET 10 throws when animating `Left`
   from NaN. The guard is correct but is a symptom of creating/closing windows instead of reusing them; also it leaves
   `Left/Top` NaN until first `Place`, so an unplaced window shown by `Reveal()` appears at (0,0).
7. **DPI/multi-monitor** — F-17. `SystemParameters.PrimaryScreenWidth` + DIP math + no `WM_DPICHANGED` handling.
8. **Settings apply** — `ApplySettings` does not reposition (F-17); `OnSettingsPanelClosed` only repositions when
   expanded with a last event (`MainWindow.xaml.cs:300-313`).
9. **Opacity/scale drift** — F-32; snapshot opacity/scale is not derived from the same source as the main pill.
10. **Window activation**: main window is created with default `ShowActivated=true` and `Topmost=true`; at startup it
    takes focus from the previously active app. Snapshot windows set `ShowActivated=false`/`Focusable=false` but not
    `WS_EX_NOACTIVATE`, so a click on one can activate it.

---

## 5. Memory / handle / unbounded-growth inventory

| Resource | Retained by | Growth bound | Rate (estimate) | Fix |
|---|---|---|---|---|
| Full-res album art | `_currentMediaIcon`, `_lastTriedIconResult` (`MainWindow.xaml.cs:63,2504`) | last 1–2 images | 4–36 MB per track transient | `DecodePixelWidth=64` + single shared decode |
| Kugou metadata cache | `KugouLyricsProvider.cs:13` | **∞** | ~0.2–1 KB/song | LRU/TTL |
| Kugou lyrics cache | `:14` | **∞** | 5–30 KB/song → 2–10 MB/day | LRU (cap 32 tracks / 4 MB) |
| Kugou miss TTL map | `:15` | **∞** | small | prune on read |
| NetEase lyrics/miss caches | `NeteaseLyricsProvider.cs:17-18` | **∞** | similar | LRU |
| Media accent cache | `MainWindow.xaml.cs:62` | **∞** | ~0.3 KB/song | LRU |
| Notification IDs | `NotificationMonitor.cs:18` | **∞** | 10–100/day | watermark + ring |
| Temp artwork | `%TEMP%\FluidBar\art` | **∞ on disk** | 20–200 MB/month | sweep > 7 days |
| Temp icons | `%TEMP%\FluidBar\icons` | bounded by players | small | sweep |
| Agent processed/failed JSON | `%APPDATA%\FluidBar\agent-events\processed` | **∞ on disk** | 1/event | retention |
| `_mediaAccentCache` paths | see above | **∞** | — | LRU |
| Snapshot windows | `List<IslandSnapshotWindow>` | bounded by stack | churn, not leak | pool |
| Animation clocks | WPF property system | one per property | churn | fewer animations |
| `Process`/COM/WMI | finalizers | transient | high churn | `using`/release |
| SettingsWindow tree | `App._settingsWindow` | permanent after first open | 20–60 MB | dispose/lazy |
| Static plugin caches | `MediaPlugin.cs:58-61` | bounded | — | instance + TTL |
| Task/stream leak | timed-out artwork reads (`MediaModels.cs:35-38`) | **∞ under hang** | 2/s | cancellation |

---

## 6. Architecture debt

| Area | State | Evidence | Recommendation |
|---|---|---|---|
| `MainWindow` god object | 3,466 lines, 145 methods, 8 timers, 7 animation systems, media parsing, image decode, color analysis, window mgmt | whole file | Split into `IslandShell` (window/visuals), `IslandController` (state machine), `MotionSystem`, `MediaPresenter`, `StackPresenter`, `InputRouter` |
| `SettingsWindow` | 2,750 XAML + 1,888 code-behind, no MVVM, 89 methods, 21 effects | whole file | Per-tab views + view models, lazy tabs |
| Event model | one synchronous `Action<IslandEvent>`, no thread affinity/coalescing/backpressure | `EventSystem.cs:53-61` | Typed, coalescing, dispatcher-owned queue (v2 interaction core) |
| Settings persistence | flat POCO JSON, no schema version, non-atomic, silent failures, duplicated fields | `Settings.cs` | Versioned + atomic + migrations; one config service |
| Plugin lifecycle | source-only, `IIslandPlugin` has no window/host context, `ClipboardPlugin.AttachWindow` hack, double dispose, no async init | `PluginManager.cs`, `App.xaml.cs:52-73` | Host context object, async lifecycle, idempotent dispose, load/unload tests |
| Catalog | lies about a notifications plugin | `Plugins/catalog.json:30-38` | Implement or re-label |
| Exceptions | 31 empty catches, no telemetry | repo | Log + rate limit + user-visible degradation |
| Tests | 98 pure-policy tests; no window/threading/timer/perf tests; one live-network test | `FluidBar.Tests/Program.cs` | Add WPF harness + soak/leak gates |
| Docs | README overclaims; `AGENTS.md`/`CLAUDE.md` only in worktree; `.bak`/`example.txt`/zip in repo | root | Clean + rewrite |
| i18n | none; 261 zh-CN C# literals + XAML strings | repo | resx + language setting |
| Versioning | 1.0.1 vs README 1.0.0dev; no manifest | csproj | Single source of truth |

---

## 7. Feature inventory (parity checklist for v2)

Legend: **Working** = code path exists and is plausible; **Partial** = exists with a correctness/perf caveat; **Broken** =
exists but has a known bug; **Missing** = documented but not implemented.

### 7.1 System monitors (10)

| # | Feature | Where | State | Notes |
|---|---|---|---|---|
| 1 | Volume (progress bar, mute, wave) | `Monitors/VolumeMonitor.cs`; UI `IslandPresentation.cs:503`, `MainWindow.xaml.cs:2186-2250` | **Partial** | COM churn every 150 ms; no OS event; mute state not persisted |
| 2 | Brightness (DDC/CI + WMI fallback) | `Monitors/BrightnessMonitor.cs` | **Broken perf** | 200 ms blocking DDC/CI on UI thread; WMI fallback per tick |
| 3 | Battery (percent/charging/low) | `Monitors/BatteryMonitor.cs` | Working | 15 s; stops on desktops (`:47-52`); ignores >100 % (`:60`) |
| 4 | Clock (idle time/date) | `Monitors/ClockMonitor.cs`; `MainWindow.ShowIdleClock` | Working | 10 s; only shown when `AlwaysVisible`; clock event kills snapshots (F-03) |
| 5 | Input method (中/英) | `Monitors/InputMethodMonitor.cs` | **Partial** | 150 ms `SendMessage`; window-change debounce |
| 6 | Lock keys (Caps/Num) | `Monitors/LockKeyMonitor.cs` | Working | 150 ms `GetKeyState`; LED glow |
| 7 | Network (connect/disconnect + SSID/name) | `Monitors/NetworkMonitor.cs` | Working | `NetworkChange` events → dispatcher; `GetWifiSsid` returns adapter name, not SSID |
| 8 | USB (attach/detach) | `Monitors/UsbMonitor.cs` | Partial | WMI `WITHIN 2` watchers; `e.NewEvent` not disposed |
| 9 | Bluetooth (connect/disconnect) | `Monitors/BluetoothMonitor.cs` | Partial | WMI PnP query every 15 s; object leaks |
| 10 | Windows notifications (toast, app name, title, body, 6-digit code) | `Monitors/NotificationMonitor.cs`; `Plugins/Notifications/NotificationModels.cs` | Partial | 1.6 s full-history poll; unbounded `_seenIds`; access request only via Settings |

### 7.2 Plugins (marketed as 4)

| Plugin | Where | State | Notes |
|---|---|---|---|
| Clipboard | `Plugins/Clipboard/ClipboardPlugin.cs` | Working | `AddClipboardFormatListener` on main HWND; `HwndSource.AddHook`; last text retained; long-text marquee |
| Media | `Plugins/Media/*` (13 files) | **Partial** | See 7.3 |
| Agent status | `Plugins/AgentStatus/*` | Partial | Inbox polling 900 ms; hooks guardian rewrites Claude settings; unbounded processed files |
| Notifications | **missing as a plugin** | Broken catalog | Implemented as monitor; catalog points to nonexistent `NotificationsPlugin.cs` |

### 7.3 Media stack

| Sub-feature | Where | State |
|---|---|---|
| GSMTC session discovery/selection | `MediaSessionProvider.cs:27-181`, `MediaSnapshotSelectionPolicy.cs` | Working (heavy poll) |
| Fallback window-title scraping (Kugou/NetEase/QQ/Spotify/WMP) | `MediaPlugin.cs:530-711` | Partial (recursive `EnumChildWindows`, leaks) |
| UI Automation fallback for Kugou | `MediaPlugin.cs:714-772` | Risky (UIA on UI thread) |
| WASAPI process audio gate (peak meter) | `ProcessAudioActivity.cs` | Working (COM leaks) |
| Album art (GSMTC thumbnail) | `MediaSessionProvider.cs:378-401` | Partial (re-reads every poll, temp files) |
| Album art (Kugou URL) | `KugouLyricsProvider.cs:578-603` | Partial (sync HTTP, temp files) |
| App icon extraction | `MediaSourceVisuals.cs:28-119` | Partial (process/handle churn, HICON borrowed correctly) |
| Dominant-color accent | `MediaArtworkColorAnalyzer.cs:10-60` | Working (full GDI+ decode, `GetPixel` sampling) |
| Lyrics: Kugou provider + LRC parse + cache | `KugouLyricsProvider.cs` | Partial (sync HTTP on UI, unbounded caches) |
| Lyrics: NetEase provider | `NeteaseLyricsProvider.cs` | Partial (same) |
| Lyrics: page/line selection + secondary line | `KugouLyricsProvider.SelectCurrentLine/NextLine`, `ReSelectLyrics` | Working |
| Playback position interpolation | `MainWindow.WaveTimer_Tick` (`:3245-3277`), `MediaSessionProvider:91-108` | Working (layout per tick) |
| Seek detection | `MainWindow.IsSeekFromInterpolated` (`:2488-2502`), `MediaPlugin` signature logic | Working |
| Media transport controls (prev/play/next) | `MainWindow.xaml.cs:3320-3422`, `MediaSessionProvider.Try*` | Working |
| App-command fallback (Kugou) | `MediaAppCommandFallback.cs` | Risky (global `keybd_event`, `SendMessage`) |
| Browser badge/site detection | `MediaModels.cs:42-114`, `MediaSessionProvider.FindBrowserSiteBadge` | Working (window enum per poll) |

### 7.4 Other user-visible features

| Feature | Where | State |
|---|---|---|
| Tray icon + context menu (设置/退出) + double-click | `App.xaml.cs:158-179,181-213` | Working; HICON from `GetHicon` never destroyed (F-30-ish); menu not disposed |
| Hide tray icon setting | `App.xaml.cs:107-108,252-256`; `SettingsWindow` toggle | Working |
| Open settings: Ctrl+Alt+left-click | `MainWindow.xaml.cs:240-267` | Working |
| Hold-to-hide (Alt by default, configurable key) | `MainWindow.xaml.cs:1894-1952`, `HoldToHideKeyPolicy` | Working (16.7 Hz polling) |
| Auto-hide after delay / AlwaysVisible clock | `MainWindow._collapseTimer`, `ShowIdleClock` | Working |
| Always-on-top | `MainWindow.xaml:8`, `ApplySettings` | Working |
| Opacity/background color/accent/corner radius/size/offsets/position | `SettingsWindow` + `ApplySettings` | Partial (position not applied live; Y bug) |
| Multi-island stack (latest-only vs multiple, max 1–8, gap, 5 s expiry, media pinned) | `IslandStackPolicy`, `IslandStackVisibilityPolicy`, `IslandSnapshotWindow` | Broken coordination (F-03/F-04) |
| Hover card (detail, lyrics, progress, transport, verification-code copy, meta) | `MainWindow.ShowHoverCard/HideHoverCard`, `HoverCardPresentation` | Partial (F-09) |
| Spring physics motion | `SpringValue`, `HoverCardMotionPlan` | Working but drives layout |
| Rim glow (Always/Event/Plugin) + outer bloom | `ApplyRimMode`, `TriggerRimPulse`, `StartRimContinuous` | Working (always-on cost) |
| Icon pulse/scale/nudge/skew | `UpdateIcon`, `NudgePill`, `AnimateShell` | Working |
| Scrolling marquee text | `StartScrolling`, `ScrollTimer_Tick` | Working |
| Verification-code detection/copy | `NotificationMonitor:97-109`, `MainWindow:3432-3465`, Settings toggle | Working |
| Update check (startup silent + settings manual) | `App.CheckUpdateOnStartupAsync`, `SettingsWindow.CheckUpdate_Click` | Partial (F-24) |
| Agent hooks install/repair | `AgentStatusPlugin` + `Plugins/AgentStatus/hooks/*.ps1` | Risky (F-23) |
| System theme awareness | `App.SetupThemeWatcher`, `GetSystemTheme` | Broken (F-07) + does not actually theme the island |
| Settings: 5 tabs (general/monitor/plugin/agent/about) | `SettingsWindow.xaml` 1,521–2,677 | Working |
| i18n | — | **Missing** (zh-CN hard-coded) |
| Plugin catalog/about | `PluginCatalog.cs`, `Plugins/catalog.json`, Settings about tab | Partial (F-29) |

---

## 8. Test coverage & what is missing

* `FluidBar.Tests/Program.cs` is a custom console harness (`Test(...)`, `AssertEqual`, `AssertNear`) with **98 tests**,
  all exercising static policies: presentation mapping, stack policy, layout math (X only), media selection,
  Kugou parsing, hooks/notification models, hover policy, hold-to-hide policy, spring stepping, animation plan
  constants.
* **No test** constructs `MainWindow`, `IslandSnapshotWindow`, a `DispatcherTimer`, or a `CompositionTarget.Rendering`
  handler; no test asserts window coordinates, Y anchors, DPI, click-through, lifecycle, or disposal.
* `Kugou online lyrics smoke test` (`:941`) performs a live HTTP call from the unit harness — flaky/offline-hostile.
* No soak/leak test (e.g. 10k events, assert handle/memory ceiling), no dispatcher-pumped race tests, no performance
  budget test.
* No CI config in the root checkout (build/test instructions only in README).

**Recommended gates for v2:** (1) 60 s hover/morph soak with `GC.GetTotalMemory` + `GetProcessHandleCount` ceilings;
(2) deterministic dispatcher tests for `PositionAt`/`StackLayout` round-trips; (3) a "no `Window.Width` animation"
analyzer/architecture test; (4) a "no `Process` without `using`" Roslyn test; (5) network tests behind an env flag.

---

## 9. Rewrite guardrails — "never do this again"

1. **Never animate `Window.Left/Top/Width/Height`.** Animate transforms inside a fixed-size, per-monitor host. Window
   geometry changes are allowed only on monitor/anchor changes (one `SetWindowPos`).
2. **Never do I/O on the dispatcher.** No `GetAwaiter().GetResult()`, no sync HTTP, no DDC/CI, no WMI, no
   `SendMessage`, no `Process.GetProcesses*`, no `Directory.EnumerateFiles` on the UI thread. Producers are background
   services; the UI only consumes immutable snapshots via a bounded/coalescing channel.
3. **One window per monitor, never one window per island.** Multiple islands are one composition surface; snapshot
   HWNDs must not exist.
4. **Never poll what Windows can notify.** GSMTC events, audio-session callbacks, `NetworkChange`, WMI watchers,
   `SystemEvents`/`WM_SETTINGCHANGE`, `WH_KEYBOARD_LL`, `FileSystemWatcher`. If a poll is unavoidable: ≥1 s, off the UI
   thread, change-only publish.
5. **Every window mode has explicit hit-testing.** Collapsed/hidden ⇒ `WS_EX_TRANSPARENT | WS_EX_NOACTIVATE` +
   `HTTRANSPARENT`; interactive only over the visible pill/card. Never ship a topmost window that swallows clicks.
6. **Every cache has a budget.** `ImageCache` (bytes, LRU), `LyricsCache` (entries/bytes, TTL), `_seenIds` (ring),
   temp dirs (age sweep). No `Dictionary` field may be written without an eviction policy and a test.
7. **Every `IDisposable` native object is `using`/released in `finally`.** `Process`, COM RCWs, `ManagementObject`,
   `HICON`, streams, `CancellationTokenSource`. CI analyzer enforces it.
8. **Every async operation is cancellable and single-flight.** Track-keyed generation + `CancellationTokenSource`;
   no `async void` outside event handlers; no bool latch as concurrency control.
9. **One settings service.** Schema version + migrations, atomic write, persisted monitor enable state, no duplicate
   fields, no mutating getters, no silently swallowed load/save errors.
10. **One presentation coordinate contract.** Visual coordinates everywhere; exactly one `Visual→Window` conversion
    that applies bleed on **both** axes; unit tests for all six anchors × 2 strategies × 3 DPIs; work-area aware.
11. **No empty `catch`.** Log once per key, degrade visibly, and surface a diagnostics bundle (counters: timers, events,
    handles, caches, UI-thread latency).
12. **No hidden global side effects.** Never rewrite another app's config on a timer; media commands target the
    requested app only (no global `keybd_event`); never execute an unverified downloaded binary.
13. **Every window has a lifecycle.** Define create/show/hide/close/dispose; `Closing` must cancel-and-hide or
    re-create on demand; shutdown is explicit and idempotent; no singleton references to closed windows.
14. **Tests cover the real failure modes.** Dispatcher-pumped window/position/stack/race tests + a soak test with
    handle/memory/CPU ceilings + an architecture test forbidding window-bounds animation and UI-thread I/O.
15. **No dead code, no lying docs, no untranslated string literals.** Catalog entries must point at real types; README
    claims must be testable; all user-visible strings go through resources.

---

## 10. How to confirm these findings quickly (read-only, no app launch required for most)

1. **UI-thread stalls:** run the published app with `dotnet-counters monitor -n FluidBar --counters
   System.Runtime,Microsoft.Windows.App` and watch `ThreadPool`/`% Time in GC`; use `dotnet-trace collect -p <pid>
   --providers Microsoft-Windows-DotNETRuntime` and look for `GC` + long `Dispatcher` operations.
2. **Handle growth:** `Get-Process FluidBar | Select HandleCount,WorkingSet64` sampled every 10 s for 10 min while a
   browser plays media; correlate with F-10/F-12.
3. **Window churn:** in DebugView/ETW, watch `Win32k` window create/destroy counts; or count `HWND`s of class
   `HwndWrapper[FluidBar.exe;...]` with Spy++/`EnumWindows` during lyric updates (F-03).
4. **Freeze reproduction:** play a Kugou track with no cached lyrics and watch the island/UI stall for 3.5–14 s
   (F-01); switch monitors' brightness with a DDC/CI-incapable display to force WMI (F-01).
5. **Click-through:** click the top-center 154 × 66 strip while the island is collapsed; observe the click is eaten
   and focus changes (F-05).
6. **Memory:** let the app run 8 h with music; sample `GC.GetTotalMemory(true)` and `%TEMP%\FluidBar\art` size
   (F-06).

---

## 11. Cross-report hooks

* **01-references-design.md** covers what the reference apps do for click-through/DPI/composition; use its
  Win32/`WS_EX_*` and per-monitor sections as the target design for F-05/F-17.
* **04-plugin-ecosystem-i18n.md** covers the plugin host/ALC/i18n architecture; F-19/F-20/F-29/F-30 here are the
  concrete v1 debts that justify that rework.
* This file: `02-current-code-audit.md` — evidence and severity ranking for the rewrite plan.

---

### Appendix A — Verbatim metric commands used

```powershell
# line counts (authoritative, includes blank lines)
[IO.File]::ReadAllLines("D:\build\GitLocal\FluidBar\MainWindow.xaml.cs").Count   # 3466
# timer creation sites
Get-ChildItem D:\build\GitLocal\FluidBar -Recurse -File -Include *.cs |
  Where-Object FullName -notmatch '\\(bin|obj|worktrees)\\' |
  Select-String 'new DispatcherTimer' | Measure-Object                     # 18
# BeginAnimation per file
Select-String MainWindow.xaml.cs 'BeginAnimation' | Measure-Object          # 98
Select-String SettingsWindow.xaml.cs 'BeginAnimation' | Measure-Object      # 36
Select-String IslandSnapshotWindow.cs 'BeginAnimation' | Measure-Object     # 11
# effects
Select-String MainWindow.xaml 'DropShadowEffect' | Measure-Object            # 3
Select-String SettingsWindow.xaml 'DropShadowEffect' | Measure-Object        # 21
# swallowed exceptions
Get-ChildItem ... -Include *.cs | Select-String 'catch\s*\{\s*\}' | Measure-Object  # 31
# tests
Select-String FluidBar.Tests\Program.cs '^Test\(' | Measure-Object          # 98
```

### Appendix B — Caveats

* All rates/percentages in this report are **static-analysis estimates**, not profiler measurements. The instructions
  forbade launching the app; confirm with the instrumentation in §10 before quoting numbers to users.
* The newer worktree (`desktop-main-6ac0084e`) contains partial fixes (event identity/snapshot preservation). Do not
  spend rewrite effort re-fixing those specific deltas without diffing first; the P0 findings marked "persists" were
  re-checked in that tree.
* The repo contains three `.claude/worktrees` copies of the source plus `.bak` files; only the root checkout was
  audited. If the shipping binary is built from a worktree, re-run the greps in Appendix A against that tree.
