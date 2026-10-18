# FluidBar.Platform.Windows — integration contract (M3)

Status: **delivered** (branch `v2/rebuild`). Owner: platform-owner. Consumers: `FluidBar.App` (host +
UI), `FluidBar.PluginHost` (only through interfaces the App passes in), tests: `FluidBar.Platform.Tests`.

The platform project is the **only** place allowed to P/Invoke, do COM/WinRT activation, read the
registry or touch device APIs (spec §5.2). It contains **no UI framework types** (no Avalonia/WPF): the
App owns the island HWND and the composition surface, and calls the interop helpers documented in §3.2.

---

## 1. Dependency position and threading model

```
FluidBar.Core  ←  FluidBar.Platform.Windows  ←  FluidBar.App
      ↑                    (WinRT/CsWinRT      (Avalonia + the only P/Invoke caller of this project)
      └── DTOs for adapters   projection, no
                              extra packages)
```

Threading rules enforced by construction:

| Thread | Owns | Must never |
|---|---|---|
| App UI thread (Avalonia) | island HWND, render loop, `IslandWindowInterop` calls, activity scheduler | perform I/O/COM/device work |
| `PlatformMessageHost` thread (1 dedicated STA thread) | every platform window: hotkeys, tray, clipboard listener, power/session/device notifications, WinEvent hooks | block on device I/O; it only enqueues/callbacks |
| `ComWorker` / `BackgroundPoller` threads | WASAPI endpoint, WMI brightness fallback, DDC/CI | touch UI objects |
| Thread-pool | monitor event pumps, async refresh work, subscriber callbacks | be assumed to be a single thread (`ComWorker` is used where apartment affinity matters) |
| GSMTC / WinRT thread pool | media + notification callbacks | call back into UI synchronously |

All monitors publish **immutable snapshots** through `SnapshotPublisher<T>`: change-only, coalesced,
delivered on a thread-pool thread. Consumers (App) may marshal to the UI thread themselves; the platform
never references a dispatcher.

## 2. Bootstrap sequence (App)

```csharp
var counters = new PlatformCounters();
var coreLogger = /* App's IAppLogger */;
var logger = new AppLoggerPlatformAdapter(coreLogger);           // or new RollingFileLogger(options, ...)
await using var logFile = new RollingFileLogger(new RollingLogOptions {
    DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                 "FluidBar", "logs"),
}, counters: counters);
await logFile.StartAsync();

await using var host = new PlatformMessageHost(logger, counters);
await host.StartAsync();                                          // dedicated STA message pump

await using var monitors = new MonitorService(host, logger, counters);
await using var media     = new GsmtcMediaSessionService(logger, counters);
await using var clipboard = new ClipboardService(host, logger, counters);
await using var notifs    = new NotificationService(logger, counters);
await using var volume    = new VolumeMonitor(logger, counters);
await using var brightness= new BrightnessMonitor(logger, counters);
await using var battery   = new BatteryMonitor(host, logger, counters);
await using var clock     = new ClockMonitor(logger, counters);
await using var ime       = new InputMethodMonitor(host, logger, counters);
await using var lockKeys  = new LockKeyMonitor(host, logger, counters);
await using var network   = new NetworkMonitor(logger, counters);
await using var theme     = new SystemThemeMonitor(host, logger, counters);

await monitors.StartAsync();     // display service drives per-monitor windows in the App
// ... start the monitors the user enabled, then wire SnapshotChanged → Core EventHub (see §5).

var hotkeys = new HotkeyService(host, logger, counters);
await hotkeys.StartAsync();
var registration = await hotkeys.RegisterAsync("toggle", "Ctrl+Alt+D");
if (!registration.Success) { /* surface conflict in settings — required by design */ }

var tray = new TrayIconService(host, logger, counters);
await tray.StartAsync(new TrayIconOptions { ToolTip = "FluidBar", IconFilePath = iconPath });
tray.SetMenuItems(new[] { new TrayMenuItem("settings", settingsLabel), TrayMenuItem.Separator,
                          new TrayMenuItem("exit", exitLabel) });
tray.MenuItemInvoked += /* … */;
```

Dispose order: UI/window interop first, then services (each is `IAsyncDisposable`; `DisposeAsync` is
idempotent and stops producers before releasing handles). `PlatformMessageHost` goes last.

## 3. Public API surface

### 3.1 Threading, lifecycle, snapshots (`FluidBar.Platform.Windows.Threading`)

| Type | Purpose |
|---|---|
| `PlatformMessageHost(IPlatformLogger, PlatformCounters)` | one dedicated STA message-pump thread for all platform windows. `Task StartAsync(ct)`, `Task<PlatformWindow> CreateWindowAsync(name, PlatformWindowKind, handler, ct)`, `Task<T> InvokeAsync<T>(ct)`, `void Post(Action)`, `Task StopAsync()` via `DisposeAsync`. `PlatformWindowKind`: `MessageOnly` (directed messages) or `HiddenTopLevel` (broadcasts). |
| `PlatformWindow` | handle wrapper; `IntPtr Handle`, `PostMessage(...)`; `DisposeAsync` destroys on the owning thread. |
| `IPlatformMonitor<TSnapshot>` / `PlatformMonitor<TSnapshot>` | lifecycle contract: `MonitorId`, `State` (`Stopped/Starting/Running/Stopping/Failed/Disposed`), `Current`, `event SnapshotChanged`, `Task StartAsync/StopAsync`, `IAsyncDisposable`. Start/stop are single-flight and idempotent; start failure is logged + counted and rethrown. |
| `SnapshotPublisher<T>` | change-only coalescing publisher (`Publish(T)`, `Current`, `Changed`). Also usable directly by App-owned producers. |
| `ComWorker` | dedicated MTA thread + queued invocation for COM objects (audio). `ReleaseComObject` helper. |
| `LowLevelKeyboardHookService` | `WH_KEYBOARD_LL` installed only while started; never swallows keys, never injects; `event KeyEvent`. Used by lock keys + App hold-to-hide. |
| `BackgroundPoller` (internal) | ≥1 s signal-driven background cadence for unavoidable device polls (brightness). |

### 3.2 Display + island window hosting

`FluidBar.Platform.Windows.Display`:

| Type | Purpose |
|---|---|
| `PixelRect(int X, int Y, int Width, int Height)` | physical pixels. |
| `DipRect(double X, double Y, double Width, double Height)` | device-independent pixels. |
| `MonitorInfo` | `Id, Index, Bounds, WorkArea, DpiX, DpiY, IsPrimary, Scale` (all physical). |
| `MonitorSet` | immutable value-equal set: `Monitors`, `Primary`, `Count`. |
| `IMonitorService : IPlatformMonitor<MonitorSet>` | `Primary`, `PrimaryWorkArea`, `event MonitorsChanged`, `RefreshAsync`, `MonitorAtPoint`, `MonitorForRect`, `MonitorForWindow` + inherited `Current`/`SnapshotChanged`/`StartAsync`/`StopAsync`. |
| `MonitorService(PlatformMessageHost, IPlatformLogger, PlatformCounters, TimeProvider?)` | enumeration + `WM_DISPLAYCHANGE`/`WM_SETTINGCHANGE`/`WM_DPICHANGED` driven, debounced 300 ms, off-thread enumeration. |
| `IslandAnchor` | `TopCenter/TopLeft/TopRight/BottomCenter/BottomLeft/BottomRight`. |
| `DisplayMetrics` | pure DIP↔px math: `ToPhysical`, `ToDips`, `PlaceAtAnchor`, `ClampToWorkArea`, `ReanchorForDpiChange`, `DecodeDpiChangedWParam`, `SelectMonitor`, `MonitorAtPoint`, `IntersectionArea`, `ScaleFromDpi`. |
| `CoreLayoutConversions` | single seam to `FluidBar.Core.Layout` (`ToCore/ToPlatform` for monitor/pixel rect/anchor; `ToHostingShellGeometry`). |

`FluidBar.Platform.Windows.Hosting.IslandWindowInterop` — the App's Win32 surface (no P/Invoke in App):

| Member | Contract |
|---|---|
| `ApplyIslandStyles(IntPtr hwnd)` | ORs `WS_EX_NOACTIVATE \| WS_EX_TOOLWINDOW \| WS_EX_NOREDIRECTIONBITMAP`. Call right after HWND creation. |
| `RegisterHitTest(IntPtr hwnd, Func<int,int,bool> isInteractiveInClientPixels)` → `IDisposable` | WndProc subclass: `WM_NCHITTEST` → `HTCLIENT` iff predicate true (client pixels) else `HTTRANSPARENT`; `WM_MOUSEACTIVATE` → `MA_NOACTIVATE`. Dispose restores the original proc. Call on the UI thread. Predicate exceptions degrade to `HTTRANSPARENT` + are logged. |
| `IsHitTestRegistered(IntPtr)` | diagnostic. |
| `PlacePhysical(IntPtr, int xPx, int yPx, int widthPx, int heightPx)` | **primary placement API**: one `SetWindowPos` with `SWP_NOACTIVATE\|SWP_NOZORDER\|SWP_NOOWNERZORDER`, no clamping, negative origin allowed. Feed with Core `WindowPlacement.WindowPixel`. |
| `PlaceOnMonitor(IntPtr, MonitorInfo, int wDip, int hDip, IslandAnchor)` (+ Core-overload) | convenience: DIP→physical via monitor scale, work-area clamp. Never per frame. |
| `UpdateWindowRegionOnSettle(IntPtr, ShellGeometry, MonitorInfo)` (+ Core overload) | `SetWindowRgn` round-rect from DIPs, settle-only (click-through correctness). `ShellGeometry(XDip,YDip,WidthDip,HeightDip,CornerRadiusDip)`. |
| `SetTopmost(IntPtr)` | `SetWindowPos(HWND_TOPMOST, SWP_NOACTIVATE)` once. |
| `TryReassertTopmost(IntPtr)` → bool | policy-gated re-assert: never above shell foreground classes, never while full-screen/presentation/busy, ≤1 Hz. |
| `SetClickThrough(IntPtr, bool)` | whole-window `WS_EX_TRANSPARENT` — only when no part is interactive. |
| `SetNoActivate(IntPtr, bool)` | temporary activation for text input; restore on focus loss. |
| static `GetForegroundWindowClass()`, `IsShellForeground()`, `QueryUserNotificationState()` | helpers used by the policy. |

Pure policies (`FluidBar.Platform.Windows.Policies`): `TopmostPolicy.Evaluate(TopmostContext)` →
`Reassert/SkipHidden/SkipShellForeground/SkipSuppressed/SkipThrottled`; `ShellWindowClasses.IsShellWindowClass`;
`HitTestPolicy.Evaluate(HitTestGeometry)` → `Client/Transparent/Nowhere`,
`IsPointInsideShell`, `ShouldWindowBeClickThrough`, `ToNativeHitTest`.

**Layered fallback host (spike, not wired into the App yet)** — contract:
[`layered-host.md`](layered-host.md). `Hosting.LayeredWindowHost` is a framework-free Win32
per-pixel-alpha host: one `WS_EX_LAYERED` HWND per monitor (float 488×384 DIP, dock 488×444 DIP),
a persistent 32 bpp premultiplied DIB (`LayeredSurface`), full-frame and dirty-rect
`UpdateLayeredWindow` (`CommitFrame()` / `CommitFrame(ReadOnlySpan<LayeredRect>)`), client-pixel
input callbacks, the squircle + detached-pill `HTTRANSPARENT` policy (`LayeredHitRegion`) and
`DwmFlush`/deadline pacing (`LayeredFramePacer`). It exists because Avalonia/DirectComposition
flattens to opaque grey in this environment while canonical layered windows stay transparent
(§10 of [`host-transparency.md`](host-transparency.md)).

### 3.3 Shell services (`FluidBar.Platform.Windows.Shell`)

| Type | Contract |
|---|---|
| `HotkeyGesture` | `TryParse("Ctrl+Alt+D", out gesture, out error)`, `Parse`, `ToString`, modifiers mirror Win32 `MOD_*`. Supported keys: A–Z, 0–9, F1–F24, Space/Tab/Enter/Esc/Backspace/Insert/Delete/Home/End/PageUp/PageDown/arrows/PrintScreen/Pause/CapsLock/NumLock/ScrollLock/VolumeUp/Down/Mute/Media keys. |
| `HotkeyService(PlatformMessageHost, …)` | `StartAsync`, `Task<HotkeyRegistrationResult> RegisterAsync(name, gesture\|string)`, `UnregisterAsync/UnregisterAllAsync`, `StopAsync`, `event Pressed` (thread pool). Conflicts are explicit results (`AlreadyRegisteredByAnotherApp`, `AlreadyRegisteredByName`, `InvalidGesture`, `Win32Failure`) — never silent. |
| `TrayIconService(PlatformMessageHost, …)` | `StartAsync(TrayIconOptions)`, `SetToolTipAsync`, `SetMenuItems(IReadOnlyList<TrayMenuItem>)`, `ShowBalloonAsync`, events `MenuItemInvoked/LeftClicked/DoubleClicked/BalloonClicked`. `NIM_DELETE` + icon/menu release on stop. Context menu is user-initiated and modal on the host thread. |
| `HotkeyKeyFormatter.Format(uint vk)` | stable key names. |
| `StartupRegistrationService(IRegistryValueStore, IPlatformLogger)` | `QueryAsync(executablePath)`, `EnableAsync(executablePath, args)`, `DisableAsync` — HKCU Run only; registry I/O on the thread pool. `StartupCommandBuilder.BuildCommand` quotes absolute paths. |
| `IRegistryValueStore` | test seam for the Run key. |

### 3.4 Monitors (`FluidBar.Platform.Windows.Monitors`)

All are `PlatformMonitor<TSnapshot>`, i.e. `await using` + `StartAsync`/`StopAsync` + `Current` +
`event SnapshotChanged`. All run off the UI thread and publish change-only.

| Monitor | Snapshot | Change source (no polling) |
|---|---|---|
| `VolumeMonitor` | `VolumeSnapshot(Percent, IsMuted, HasDevice, ChannelCount)` | `IAudioEndpointVolumeCallback` + `IMMNotificationClient` (default device change re-activates). Writes: `SetVolumeAsync/StepVolumeAsync/SetMuteAsync`; `TryReadPeakLevelAsync` (App polls only while visible). Pure: `VolumeLevelPolicy`. |
| `BrightnessMonitor` | `BrightnessSnapshot(Percent, Source, IsSupported)` | DDC/CI via dxva2 preferred, WMI fallback; dedicated MTA poll thread every 2 s, change-only (unavoidable poll: ≥1 s, off UI). `SetPercentAsync`, `TriggerRefresh`. Pure: `BrightnessMath`, `BrightnessWmiParser`. `IBrightnessProvider` injectable. |
| `BatteryMonitor` | `BatterySnapshot(HasBattery, Percent, IsCharging, Source, IsLow, IsCritical, MinutesRemaining, MinutesToFull)` | `WM_POWERBROADCAST` + `RegisterPowerSettingNotification(AC/DC, %remaining)`. Pure: `BatteryStatusParser`. `BatteryMonitorOptions.LowThresholdPercent`. |
| `ClockMonitor` | `ClockSnapshot(LocalTime)` (`Date`, `Time`, `MinutesSinceMidnight`) | one aligned timer per minute boundary; hidden → no wakeups. Pure: `ClockAlignment`. |
| `InputMethodMonitor` | `InputMethodSnapshot(IsChineseMode, IsImeAvailable, KeyboardLayoutId)` | `EVENT_SYSTEM_FOREGROUND` (1 s debounce) + `EVENT_OBJECT_IME_*` (300 ms); query = `GetKeyboardLayout` + `SendMessageTimeout(WM_IME_CONTROL, 50 ms, SMTO_ABORTIFHUNG)`. `Baseline` property. Pure: `ImeLayoutMapper`, `ImeConversionMode`, `ImeChangePolicy`. |
| `LockKeyMonitor` | `LockKeysSnapshot(CapsLockOn, NumLockOn, ScrollLockOn)` | `WH_KEYBOARD_LL` state tracking (installed while running); 1 s change-only polling fallback if the hook fails (`IsUsingPollingFallback`). Pure: `LockKeyTracker`. |
| `NetworkMonitor` | `NetworkSnapshot(IsConnected, AdapterName, Kind, Ssid, SignalQuality)` | `NetworkChange.NetworkAvailabilityChanged/NetworkAddressChanged` → debounced off-thread refresh; real SSID via native WLAN API. Pure: `NetworkAdapterSelector`, `WlanSsidParser`. |
| `SystemThemeMonitor` | `SystemThemeSnapshot(AppTheme, SystemTheme, IsHighContrast, AccentColorArgb)` | `RegNotifyChangeKeyValue` watchers on Personalize/DWM keys + `WM_SETTINGCHANGE`. Pure: `ThemeValueMapper`. |

### 3.5 Media (`FluidBar.Platform.Windows.Media`)

| Type | Contract |
|---|---|
| `GsmtcMediaSessionService(IPlatformLogger, PlatformCounters, ArtworkCache?, TimeProvider?)` | `PlatformMonitor<MediaSnapshot>`. Events: `SessionsChanged/CurrentSessionChanged/MediaPropertiesChanged/PlaybackInfoChanged/TimelinePropertiesChanged`; refreshes are coalesced/debounced. `PlayAsync/PauseAsync/TogglePlayPauseAsync/NextAsync/PreviousAsync/SeekAsync` target the active session only (no global key injection). `event ArtworkAvailable`, `TryGetArtwork(trackKey)`, `ArtworkCache`. |
| `MediaSnapshot` | `SourceAppUserModelId, SourceDisplayName, Title, Artist, Album, PlaybackState, IsPlaying, Position, PositionUpdatedAtUtc, Duration, CanSeek, CanGoNext, CanGoPrevious, TrackKey`; `BuildContentSignature()` for Core coalescing. |
| `MediaSourceSelectionPolicy` | pure `SelectActive(IReadOnlyList<MediaSessionDescriptor>)`, `Score`, `GetSourcePriority`, `IsAppNameOnlyTitle`, `IsSamePlayer`, `BuildTrackKey`. |
| `ArtworkCache` | byte/entry bounded LRU: `Add(key, ReadOnlySpan<byte>)` rejects unknown/oversized images (header-parsed pixels), `TryGet`, `Remove`, `Clear`; counters for hits/misses/evictions. The App decodes once with `DecodePixelWidth=64` (platform never decodes pixels). |
| `ImageHeaderReader` | pure PNG/JPEG/GIF/BMP header parsing (`TryReadDimensions`, `DetectContentType`). |
| `LyricsService(IEnumerable<ILyricsProvider>, LyricsCache, …)` | async, single-flight per track, cancellable, provider fallback with 8 s timeout; `CancelPending(exceptTrackKey)`; bounded `LyricsCache`. |
| `LrcParser` | pure LRC parser (metadata, multi-timestamp lines, offset, malformed-line skip), `MergeTranslations`, `TryDecodeBase64`. |
| `NeteaseLyricsProvider`, `KugouLyricsProvider` | async HTTP providers with injected `HttpClient`; response-size capped; parsing is internal+tested; no synchronous HTTP. |
| `LyricsDocument` | `Lines`, `FindLineIndex`, `LineAt`, `NextLine`, `Title/Artist/Source`. |

### 3.6 Clipboard (`FluidBar.Platform.Windows.Clipboard`)

| Type | Contract |
|---|---|
| `ClipboardService(PlatformMessageHost, …)` | `PlatformMonitor<ClipboardSnapshot>`. `AddClipboardFormatListener` on a message-only window (no main-window coupling), reads off the UI thread with open-retry, dedupes by Win32 sequence number + payload, bounded history (`ClipboardServiceOptions`), `SetTextAsync`, `TriggerReadAsync`, `GetHistory`, `ClearHistory`. |
| `ClipboardSnapshot` / `ClipboardEntry` | `Latest`, `History` (newest first); entry: `Sequence, CapturedAtUtc, Kind(Text/Image/Files/Unknown), Text, OriginalTextLength, FilePaths` (text truncated to cap, `IsTextTruncated`). |
| `ClipboardHistory` | pure bounded ring + consecutive-duplicate suppression (`TryAdd`, `IsDuplicate`). |

### 3.7 Notifications (`FluidBar.Platform.Windows.Notifications`)

| Type | Contract |
|---|---|
| `NotificationService(IPlatformLogger, PlatformCounters, NotificationServiceOptions?, TimeProvider?)` | `PlatformMonitor<NotificationsSnapshot>` on `UserNotificationListener.NotificationChanged` + bounded seen ring + monotonic watermark; primes on start so existing toasts never replay; `AccessState`, `Task<NotificationAccessState> RequestAccessAsync`, snapshot keeps ≤ `MaxRecentNotifications`. Zero polling. |
| `NotificationsSnapshot` | `Access`, `Recent[]`, `Latest`. |
| `NotificationEntry` | `Id, AppName, Title, Body, CreatedAt, VerificationCode`. |
| `VerificationCodeDetector` | pure 6-digit code detection; prefers a code preceded by a keyword (验证码/otp/code/pin…). |
| `NotificationHistory` | pure ring/watermark/batch cap, `Prime`, `Process`, `Reset`. |

### 3.8 Diagnostics (`FluidBar.Platform.Windows.Diagnostics`)

| Type | Contract |
|---|---|
| `IPlatformLogger` | `IsEnabled(level)`, `Log(in PlatformLogEntry)`; never throws. Extensions `LogTrace/Debug/Info/Warning/Error/Critical`. |
| `PlatformLogEntry` | `Timestamp, Level, Category (closed enum), EventId, Message, Data, Exception`. |
| `RollingFileLogger(RollingLogOptions)` | JSONL sink: `%APPDATA%\FluidBar\logs\fluidbar-*.jsonl` by default, 4 MB rotation × 5 files, 7-day sweep, per-category 20/s rate limit (Error+ bypass), bounded channel, `StartAsync/FlushAsync/DisposeAsync`. |
| `PlatformCounters` / `PlatformCountersSnapshot` | lock-free counters for drops/coalescing/monitor failures/COM/Win32/**cache budgets**/…; `Snapshot().ToDictionary()` for the diagnostics bundle. |
| `NullPlatformLogger`, `DelegatingPlatformLogger` | no-op / callback adapters. |
| `AppLoggerPlatformAdapter(IAppLogger)` | maps platform logging into Core's `IAppLogger` using Core's closed category set (`AppLoggerPlatformAdapter.MapLevel`). |

### 3.9 Core integration (`FluidBar.Platform.Windows.CoreIntegration`)

`CoreSnapshotMapper` maps platform snapshots to `FluidBar.Core.Activities.ActivityPayload` and builds
`FluidBar.Core.Events.IslandEvent`s. Source tokens are stable and non-localized:
`media:gsmtc`, `system:volume`, `system:brightness`, `system:battery`, `system:lockkeys`,
`system:network`, `system:ime`, `clipboard:host`, `notif:toast`.

Presence/labels are *not* produced by the platform layer (i18n rule): the mapper fills content
(title/artist/album, clipboard body, notification title/body/verification code, SSID) and structural
fields (`Level`, `IsActive`, `ShowsAudioWave`, `PositionTicks/EndTicks`, `IconToken`, stable
`DetailLines` tokens such as `capslock:on`, `ime:native`, `remaining_minutes:45`). The Core presenter +
i18n layer turns those into localized text.

## 4. Snapshot → Core activity mapping

| Snapshot | `ActivityKind` | Mapper | Notes |
|---|---|---|---|
| `MediaSnapshot` | `Media` | `ToPayload(MediaSnapshot)` | incremental updates for position/lyrics; content signature via `BuildContentSignature()`. |
| `VolumeSnapshot` | `Volume` (`Incremental` after first) | `ToPayload(VolumeSnapshot)` | `Level`, `IsActive = !muted`; HUD label localized by presenter. |
| `BrightnessSnapshot` | `Brightness` | `ToPayload(BrightnessSnapshot)` | `Level`, `IsActive = supported`. |
| `BatterySnapshot` | `BatteryLow` when `IsLow`, else ambient/idle | `ToPayload(BatterySnapshot)` | `Level`, `IsActive = charging`. |
| `LockKeysSnapshot` | `LockKeys` | `ToPayload(LockKeysSnapshot)` | `DetailLines` = ON-key tokens. |
| `NetworkSnapshot` | ambient (network) | `ToPayload(NetworkSnapshot)` | `Body = SSID`, `SourceName = adapter`. |
| `InputMethodSnapshot` | ambient | `ToPayload(InputMethodSnapshot)` | tokens `ime:native` / `ime:latin`. |
| `ClipboardSnapshot` | `Clipboard` | `ToPayload(ClipboardSnapshot)` | latest entry body + file detail lines. |
| `NotificationsSnapshot` | `Notification` | `ToPayload(NotificationsSnapshot)` | latest toast + `VerificationCode`. |
| `ClockSnapshot` | `Idle` (presenter only) | — | no event; drives the idle clock. |
| `SystemThemeSnapshot` | settings/appearance only | — | App re-themes on change; no activity. |

Event construction helper:
`CoreSnapshotMapper.ToEvent(kind, source, IslandEventKind, ActivityPayload, long sequence, TimeSpan timestamp)`
plus `CoreSnapshotMapper.Key(kind, source)`. The App owns sequencing (`Interlocked.Increment`) and passes
elapsed-since-start timestamps per the Core event-hub spec.

## 5. Guardrail compliance map (audit §9)

| Guardrail | Where enforced |
|---|---|
| 2 — no I/O/COM/WMI/SendMessage/`Process`/dir scans on UI thread | all producers are `PlatformMonitor` services on dedicated threads/thread pool; UI thread only calls interop helpers. `BackgroundPoller` refuses intervals < 1 s. |
| 3 — one window per monitor, no snapshot HWNDs | platform exposes only the interop surface; no window-per-activity code. |
| 4 — never poll what Windows can notify | volume callback, power notifications, `NetworkChange`, registry notifications, WinEvent hooks, GSMTC events, clipboard listener, notification listener. Only brightness polls (documented, ≥1 s, off UI, change-only). |
| 5 — explicit hit-testing per mode | `IslandWindowInterop.RegisterHitTest` + `HitTestPolicy` (`HTTRANSPARENT` outside the squircle, `MA_NOACTIVATE`); `SetClickThrough` for fully non-interactive states. |
| 6 — every cache has a budget | `ArtworkCache` (bytes/entries/decoded pixels), `LyricsCache` (bytes/entries/TTL), `ClipboardHistory` (entries), `NotificationHistory` (seen ids ring/batch), log files (bytes/files/age). Each has an eviction test. |
| 7 — deterministic native release | `SafeHandle` types (`SafeIconHandle`, `SafeMenuHandle`, `SafeRegistryKey`, `SafePowerNotificationHandle`, `SafeDeviceNotificationHandle`, `SafeHookHandle`, `SafeWinEventHookHandle`, `SafeSessionNotificationHandle`, `SafeWlanHandle`, `SafeKernelHandle`); COM released via `ComWorker.ReleaseComObject` on the owning MTA thread; windows destroyed on their owning thread; `NIM_DELETE` before icon release. |
| 8 — async cancellable + single-flight | every refresh uses a `SemaphoreSlim(0)` single-flight guard; `LyricsService` per-track single-flight with cancellation; media artwork fetch cancels on track change; monitor start/stop serialized by a lifecycle gate. No bool-latch concurrency control. |
| 11 — no empty catch | every catch logs via `IPlatformLogger` and/or counts; the only intentional swallow is a log sink's own exception (documented, prevents recursion). |
| 12 — no hidden global side effects | media commands target the active GSMTC session only (no `keybd_event` anywhere); startup registration is explicit user action; no timer rewrites third-party config. |
| 13 — window lifecycle | `PlatformMessageHost`/`PlatformWindow` explicit create/destroy; services explicit `StartAsync/StopAsync/DisposeAsync`; `DisposeAsync` idempotent. |

## 6. Known gaps / deferred (explicitly out of delivered scope)

- **P3 parity services are delivered** in the follow-up (see §8): update-check client, agent hook
  inbox/installer, USB/Bluetooth device events. Remaining gaps from the full v1 parity list are
  App-side only (media window-title scraping is intentionally not ported; artwork temp files are
  App-owned).
- Media fallback scrapers (window-title/UIA for Kugou) are intentionally **not** ported (design §10.2:
  removed global key injection/UIA tree walking); GSMTC is the single media source.
- `GsmtcMediaSessionService` returns `AlbumArtPath = null`: artwork is exposed as encoded bytes through
  `ArtworkCache`; the App decodes once and, if Core needs a path for a plugin, writes its own temp file
  (platform temp sweep for v1 leftovers is App/bootstrap scope).
- Fullscreen/session suppression is policy-only (`TopmostPolicy.IsSuppressingUserNotificationState`,
  `IsFullScreenState`); the App decides to hide the host.
- System theme monitor emits light/dark/high-contrast + accent; the App maps it to its theme system.

## 7. Verification (repository state at delivery)

```powershell
dotnet build FluidBar.sln                                             # 0 errors
dotnet test tests/FluidBar.Platform.Tests/FluidBar.Platform.Tests.csproj
#   → 266 passed, 0 failed, 0 skipped
dotnet test tests/FluidBar.Architecture.Tests/FluidBar.Architecture.Tests.csproj
#   → passes (no P/Invoke outside Platform, no UI-thread blocking patterns)
```

Test coverage highlights: DPI/work-area/anchor math (3 DPIs × 6 anchors + WM_DPICHANGED decode),
hit-test squircle matrix, topmost/shell-whitelist policy, hotkey parsing, battery/clock/network/IME/
lock-key pure policies, DDC/CI + WMI brightness parsing, media source selection, image-header parsing,
artwork/lyrics cache eviction + TTL, clipboard dedupe/bounded history, notification verification-code
detection + watermark ring, rolling-log rotation/rate-limit/truncation, monitor lifecycle + snapshot
coalescing, Core snapshot mapping/logger adapter, plus guarded in-host integration tests (message host,
monitor enumeration, battery, volume, hotkeys, clipboard, tray, island interop).

## 8. P3 parity services (follow-up delivery)

All three follow the same rules as the M3 services: event-driven, off the UI thread, immutable
coalesced snapshots, explicit lifecycle, bounded history, and no periodic config rewriting.

### 8.1 Update-check client (`FluidBar.Platform.Windows.Update`)

| Type | Contract |
|---|---|
| `SemanticVersion` | pure `TryParse`/`Parse`/`CompareTo`; supports `2.1.0`, `v2.1.0`, prerelease labels; stable beats prerelease. |
| `UpdateClientOptions` | `ReleaseFeedUrl`, `UserAgent`, `Timeout` (8 s), `MaxManifestBytes`, `MaxInstallerBytes`, `InstallerAssetPattern`, `DownloadDirectory`. |
| `ReleaseManifestParser` | pure GitHub Releases JSON parser: newest release for channel, installer asset by pattern, SHA-256 from `digest`/`sha256`/`integrity`; malformed feed → null. |
| `Sha256UpdateVerifier` | computes file SHA-256 async and compares against the feed digest. |
| `IBinarySignatureVerifier` / `WinVerifyTrustSignatureVerifier` | Authenticode verification hook (`WinVerifyTrust`), returns `Verified/Unsigned/Invalid/UntrustedRoot/CheckFailed`. |
| `CompositeUpdateVerifier` | hash + Authenticode; **only `Verified` may expose an install action**. |
| `UpdateCheckClient(HttpClient, UpdateClientOptions, Version currentVersion, IUpdatePackageVerifier, IPlatformLogger, PlatformCounters, TimeProvider?)` | `CreateHttpClient(options)` (single shared client), `Task<UpdateCheckResult> CheckAsync(UpdateCheckRequest)` (ETag/If-None-Match, size caps, cancellable), `Task<UpdateDownloadResult> DownloadInstallerAsync(release, IProgress<double>?, ct)` (explicit only, `.part` + verify + atomic move), `ClearDownloadedInstaller`, `event Changed`, `Current` (`UpdateStatusSnapshot`). Never executes the installer. |

Snapshot: `UpdateStatusSnapshot(State, CurrentVersion, AvailableRelease, DownloadedFilePath, Verification,
LastCheckedAtUtc, Message)` with `UpdateState Idle/Checking/UpToDate/Available/Downloading/ReadyToInstall/Failed`.
`UpdateDownloadResult.CanInstall` is the single install-gate the App must check.

### 8.2 Agent hooks (`FluidBar.Platform.Windows.Agent`)

| Type | Contract |
|---|---|
| `AgentInboxFile` / `AgentEventParser` | pure parser for hook JSON (`agent`, `event`, `title`, `message`, `timestamp`); maps to `AgentKind Claude/Codex/Generic` and `AgentEventKind Completed/Failed/WaitingForInput/Other`; malformed payloads are rejected (quarantined), never displayed. |
| `AgentEventEntry` / `AgentEventSnapshot` / `AgentEventHistory` | immutable entry + bounded newest-first snapshot (default 100) with duplicate-id suppression. |
| `AgentInboxWatcher(AgentInboxOptions, IPlatformLogger, PlatformCounters, TimeProvider?)` | `PlatformMonitor<AgentEventSnapshot>` on `FileSystemWatcher` over `%APPDATA%\FluidBar\agent-events`; bounded channel (drop-oldest), write-settle retry, valid files → snapshot + `processed/` (≤100 retained), malformed → `quarantine/`; existing inbox files replayed once on start. No polling, no timer. |
| `AgentHookConfigMerger` | pure settings-JSON merge/remove for the agent's `hooks` object: preserves unknown keys and unrelated hooks, idempotent, removes only FluidBar-owned commands; invalid JSON throws. |
| `AgentHookInstaller(HookInstallOptions, IPlatformLogger)` | `InstallAsync(bool userConfirmed)`, `UninstallAsync(bool userConfirmed)`, `IsInstalledAsync()`: **explicit only**, exclusive lock file, timestamped backup before every write, atomic temp+move, aborts on invalid JSON. Never runs on a timer and never rewrites config spontaneously. |

### 8.3 USB / Bluetooth device events (`FluidBar.Platform.Windows.Devices`)

| Type | Contract |
|---|---|
| `DeviceEventEntry` / `DeviceEventSnapshot` / `DeviceEventHistory` | immutable `DeviceId/Kind(Usb,Bluetooth)/Change(Connected,Disconnected)/Name/Timestamp`; bounded newest-first snapshot (50) with consecutive duplicate suppression. |
| `UsbDevicePathParser` | pure `\\?\USB#VID_..&PID_..#serial#{guid}` parsing → `(VendorId, ProductId, Serial)`, `FormatFriendlyName` (`USB 0466:C52B`), `TryCreateEntry`. |
| `UsbDeviceMonitor(PlatformMessageHost, IPlatformLogger, PlatformCounters, TimeProvider?)` | `PlatformMonitor<DeviceEventSnapshot>`: `RegisterDeviceNotification(DBT_DEVTYP_DEVICEINTERFACE)` for the USB device interface + `WM_DEVICECHANGE` arrival/removal on the platform host window. No WMI watcher, no polling. |
| `BluetoothDeviceMonitor(IPlatformLogger, PlatformCounters, TimeProvider?)` | `PlatformMonitor<DeviceEventSnapshot>`: WinRT `DeviceWatcher` for paired devices prime-silently then publishes connected/disconnected on Added/Removed/Updated (`BluetoothDevice.ConnectionStatus`). Degrades to an empty snapshot when no radio is present. |

New mapper methods (`CoreSnapshotMapper`): `ToPayload(AgentEventSnapshot)`, `ToPayload(DeviceEventSnapshot)`,
`ToPayload(UpdateStatusSnapshot)`; new stable source tokens `agent:claude`, `agent:codex`, `system:usb`,
`system:bluetooth`, `update:check`. Visual detail tokens (`agent:completed|failed|waiting`,
`usb:arrival|removal`, `bluetooth:connected|disconnected`, `update:available|downloading|ready|failed`)
are non-localized; the presenter localizes them.

Tests added (all offline/deterministic): semantic-version comparison, release-manifest parsing incl.
prerelease channel + digest formats, conditional-GET/ETag flow with a stub `HttpMessageHandler`, oversized
manifest rejection, SHA-256 match/mismatch, composite Authenticode gating, download success/verification
failure cleanup, agent JSON parsing/mapping/history bounds, hook merge/remove idempotency + backup on
temp dirs, agent inbox replay/quarantine, USB path parsing, device history bounds, P3 snapshot→Core
mapping, plus guarded integration starts for the USB monitor and Bluetooth watcher.
