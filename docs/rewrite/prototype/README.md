# FluidBar 2.0 — Interactive Prototype

A high-fidelity, dependency-free interaction prototype for the FluidBar 2.0 Windows dynamic-island rewrite. It is designed to be clicked through — and judged — before any production implementation starts.

Everything is vanilla HTML/CSS/JS. No build step, no package install, no network requests, no external fonts or images. Inline SVG only.

**Open [`index.html`](index.html) in the latest Edge or Chrome.**

Designed at **1440 × 900**; usable from 1280 × 800. On viewports narrower than 1120px the demo console starts collapsed (the handle at the right edge reopens it).

---

## Files

| File | Purpose |
|---|---|
| [`index.html`](index.html) | Scene, island shell, modal and annotation mount points. |
| [`styles.css`](styles.css) | Design tokens, Windows scene, island states, expanded panes, console, settings/market, annotation overlay, responsive rules. |
| [`app.js`](app.js) | Spring engine, state machine, all rendering, pager gestures/snap logic, i18n (zh-CN/en-US), demo data, console, settings/market, interactions, URL params. |
| [`icons.js`](icons.js) | Inline SVG icon set (24×24 grid). No icon font, no emoji. |
| `README.md` | This file. |

---

## Run

### Option 1 — directly from disk

Double-click [`index.html`](index.html) or drag it into Edge/Chrome. It works from `file://`.

Clipboard writes can be blocked by the browser on `file://`; the prototype still shows its toast. For the most faithful clipboard behavior use the HTTP option.

### Option 2 — local HTTP server (recommended)

From the repository root:

```powershell
python -m http.server 8080 --directory docs\rewrite\prototype
```

Then open:

```
http://127.0.0.1:8080/
```

or, from the prototype folder itself:

```powershell
cd docs\rewrite\prototype
python -m http.server 8080
```

No other server, bundler or dependency is required.

---

## URL parameters

All parameters are applied on load and snap to their state without an opening animation, so a headless browser can capture any state directly.

| Parameter | Values | Effect |
|---|---|---|
| `state` | `idle` · `media` · `notifications` · `clipboard` · `agent` · `hud` · `timer` · `battery` | Compact live-activity pill |
| `state` | `home` · `widgets` | Expanded pager on that page |
| `state` | `settings` · `market` | Open the settings window / plugin market |
| `mode` | `float` · `flush` · `dock` | Floating pill, top-flush bar, or taskbar-replacing dock |
| `material` | `obsidian` · `acrylic` | Opaque black or acrylic island |
| `theme` | `dark` · `light` | Surrounding desktop, console and settings only — the island stays obsidian |
| `lang` | `zh` · `en` | Complete zh-CN / en-US UI switch, no reload |
| `motion` | `normal` · `reduced` | Springs become critically damped, nudge disabled, durations halved |

Examples:

```
?state=media
?state=home&mode=dock&lang=en
?theme=light&material=acrylic&state=home
?state=settings&lang=en
?state=notifications&mode=flush
```

### Extra verification parameters (prototype convenience)

These are not part of the required parameter list, but make it possible to capture any expanded section or settings page without interaction:

| Parameter | Values | Effect |
|---|---|---|
| `state=expanded&tab=…` | `home` · `media` · `notifications` · `clipboard` · `agent` · `widgets` | Open the expanded pager on a specific page (works with `mode=dock` too). All six render the same 440×284 shell and 400×244 viewport |
| `state=expanded&slide=…` | `0` · `1` · `2` · `3` · `4` · `5` | Zero-based page index for the expanded pager; useful for direct captures of any pager position. Takes precedence over `tab` when both are present |
| `state=hud&hud=…` | `volume` · `brightness` | Seed the compact HUD with a specific control (defaults to volume) |
| `state=settings&tab=…` | `general` · `appearance` · `island` · `plugins` · `market` · `advanced` · `about` | Open settings directly on a page |

```
?state=expanded&tab=clipboard
?state=expanded&slide=4
?state=hud&hud=brightness
?mode=dock&state=expanded&tab=media
?state=settings&tab=appearance&theme=light
```

In `mode=dock`, compact-only states (`media`, `notifications`, etc.) open the equivalent page of the panel above the dock; `state=idle` leaves the dock closed.

---

## Interaction cheat sheet

### Island

| Interaction | Result |
|---|---|
| Click the idle / activity pill | Morphs into the expanded panel on the relevant tab |
| Hover a compact pill for **90ms** | Peek: +10w/+6h. Idle reveals “点击展开”; media reveals previous / next controls |
| Click the media play button | Play / pause. Waveform pauses; progress freezes |
| Click / drag the seek bar | Seek within the current track |
| Press and hold | Press scale 0.97 (spring) |
| Drag horizontally | Repositions the island; release keeps the drop position, clamped to a 24px screen margin |
| Double-click empty desktop / press `Esc` | Collapse |
| Click outside the island | Collapse (clicking the demo console or settings window does not) |
| Collapse chevron (top-right overlay) | Collapse the expanded pager |
| Drag the page background horizontally | Grab cursor; content follows at 0.9× with rubber-band resistance at either end. A short flick commits one page, a long continuous drag can cross several pages by distance |
| Mouse wheel / trackpad | One notch = exactly one page. Vertical `deltaY` and horizontal `deltaX` are both accepted; 100px accumulation and a 120ms commit cadence prevent multi-page jumps. Wheel over a slider or scroll area never pages |
| Page dots (bottom center) | Six 6px dots with a 20px active pill. Click to jump; each dot has a localized tooltip and aria-label; the active dot gets `aria-current="page"` |
| Keyboard `1`–`6` | Jump to Home · Media · Notifications · Clipboard · Agent · Widgets |
| Keyboard `←` / `→` | Previous / next page |
| Keyboard `Home` / `End` | First / last page |
| Keyboard `Esc` | Close settings → close island → stop auto demo, in that order |
| New transient events | Multi-activity ghost pills stack behind the main pill with a `+N` badge (disable via Settings → 灵动岛 → 显示策略 → 仅显示最新) |

Other panel actions: clear/dismiss notifications, copy/pin clipboard items, open agents, navigate the mini calendar, tick the three widget tasks, seek, switch tracks, etc.

### Expanded pager — interaction contract

- **Geometry.** The expanded shell is fixed at **440 × 284** with a **20px inset**, leaving a **400 × 244** page viewport. Six 400 × 244 pages live in a single 2400px flex strip. Paging only changes `transform: translate3d(…)` on the strip: no layout, no remount, no resize, no crossfade. The top-left title/count, top-right chevron and bottom-center dots are separate overlays above the strip.
- **Title + dots.** The overlay title is 15px semibold and localized; counts/badges appear for Notifications, Clipboard and Agent, and the notification clear action sits beside the count. Dots sit over a subtle bottom scrim; inactive dots are 6px, the active dot is a 20px accent pill. Inactive pages are `inert` and `aria-hidden`; page changes are announced through an `aria-live="polite"` region.
- **Drag.** A primary-button pointerdown on page background (not a button, slider, list or scroll area) starts a drag. The strip follows the pointer at **0.9×** gain (10% resistance) for a weighted 1:1 feel, with **0.35×** rubber-band resistance beyond page 0 or page 5. The cursor is `grab` / `grabbing`.
- **Release.** A drag shorter than **28px** always snaps back. A short flick under **0.9 page** of damped travel commits at most one page no matter how fast it is released. Once a gesture travels **0.9 page** or more, the target is chosen by distance (`round(damped page offset)`), so a 2.7-page-width drag commits two pages because 2.7 × 0.9 = 2.43 pages rounds to page +2. Snaps use the shell's spring family at **T = 0.34s, ζ = 0.90** and are interruptible: a new drag starts from the current visual offset, not from the last committed page.
- **Wheel.** Wheel deltas accumulate to 100px per page. A single physical wheel event can commit at most one page; the 120ms cooldown queues additional notches so five rapid notches produce five sequential one-page commits rather than a five-page jump. Horizontal trackpad deltas use the same path. Wheels over sliders/scroll areas are ignored for paging.
- **Click safety.** If a pointer gesture moves more than 6px, the synthetic click is suppressed for 220ms. Buttons and native range inputs still receive normal clicks and pointer capture when pressed without dragging; sliders keep their own thumb drag.

| Constant | Value | Role |
|---|---|---|
| `dragGain` | **0.90** | Page strip follows 90% of pointer travel |
| `rubberBand` | **0.35** | Overscroll resistance past first/last page |
| `thresholdPx` | **28px** | Sub-threshold drags snap back |
| `flickVelocity` | **0.55 px/ms** | Fast short-flick classification |
| `flickMaxPages` | **0.9 page** | A gesture under this travel can never commit more than ±1 page |
| `snapT` / `snapZ` | **0.34s / 0.90** | Spring settle period / damping ratio |
| `wheelThreshold` | **100px** | Accumulated wheel delta per page |
| `wheelCooldown` | **120ms** | Minimum cadence between wheel page commits |
| `wheelLinePx` | **16px** | Normalization for line-mode wheel deltas |

### Notification stack

Trigger **通知** in the console two or three times. The latest notification remains the main pill; one or two ghost pills sit behind it, with `+N` on the top-right. Opening the notifications tab reveals the list. The last trigger is visibly highlighted in the console.

### Demo console (fixed right panel)

- Mode / Material / Theme / Language / Motion segmented controls — all live.
- Trigger event grid:
  - 音乐播放 / 暂停
  - 通知
  - 剪贴板
  - 音量 HUD / 亮度 HUD (auto-collapse after the configured delay)
  - Agent 完成 / Agent 失败
  - 番茄钟
  - 电池充电
  - 自动演示 — cycles through states every 3s; click again, click the island, or click a trigger to stop.
- **Motion Lab** — live sliders for expand/collapse `T`/`ζ`, height lag; `k`/`c`/settle readout; replay the expand animation; reset tokens.
- **设置** / **插件市场** open the large modal.
- **设计标注** toggles dashed measurements around the live island, size/radius tags, and a live token summary card. Measurements update while the island morphs or is dragged.
- Collapse chevron; a floating handle reopens the console.

### Settings + plugin market

- 900 × 620 custom-chrome window, seven nav sections.
- **通用** — language, startup, auto-update, tray icon, keyboard shortcut reference.
- **外观** — dark/light desktop theme, obsidian/acrylic material, five accent swatches, live large-panel radius, fill opacity, font scale, plus a miniature **live island preview** that reacts as you drag.
- **灵动岛** — mode, anchor position, stacking strategy, hover preview, auto-collapse delay, motion strength. These apply live to the prototype.
- **插件** — installed plugin list (built-in / community badges, version, author, permissions, enable toggles, settings gear), search, open folder.
- **插件市场** — featured banner, search, categories, 10 realistic plugins. Install animates a progress fill, then becomes “已安装” and shows a toast.
- **高级** — data folder, mock logs/export/import, developer mode, Plugin API v2.4, two-step reset-all.
- **关于** — app mark, version/build, update check (simulated), MIT license, contributors, links (disabled in prototype with a toast).
- All settings text and demo data switch instantly with the language toggle. Theme, material and language changes made here stay in sync with the console.

### Dock mode

- In **Dock**, the mock Windows taskbar fades out and the FluidBar dock takes its place.
- 8 app icons (36px tiles, 40px hit areas), running dots, separator, tray cluster with Wi-Fi / volume / battery / clock.
- Hover an icon: scale 1.12 plus a label above.
- Click Music → Media panel above the dock; Notifications → Notifications; Clipboard → Clipboard; Agent → Agent; Widgets → Widgets; tray / clock → Home.
- Click the same app again or press `Esc` to close the panel. The active app gets a wider indicator line.
- The panel morphs up from the dock and the console sits above it.

---

## Design decisions

1. **“Obsidian instrument” direction.** The desktop is a deep blue/graphite radial mesh with fine grain, vignette and a subtle contour pattern. The island is the only fully opaque, hard-edged object, with a single soft downward shadow and a 1px key line — no rim light, no glow ring, no purple-gradient page wash.
2. **The island stays black in light theme.** Switching to light changes the wallpaper, windows, taskbar, console and settings to a cool porcelain system, while the island remains `#08080A`. This makes the island read as hardware, like iOS, and creates a dramatic contrast. Verified visually before deciding; no porcelain island variant was needed.
3. **One shell, one expanded pager viewport.** A single spring-driven `.island` element owns width/height/radius; content is clipped inside. Every page opens in the same **440 × 284** shell with a 20px inset, giving a **400 × 244** viewport. Six 400 × 244 pages sit on one 2400px strip; changing page only changes the strip's `translate3d` transform. The shell, title overlay, chevron and dots never move or resize, and page content is never remounted for a swipe.
4. **Spring engine.** Authored as `(T, ζ)`; `k = (2π/T)²`, `c = 4πζ/T`; fixed 1/300s substeps; `dt` clamped to 1–50ms; retarget velocity retained at 100% when direction is unchanged, otherwise ×0.35 and clamped; the rAF loop stops at rest. Expanded content opacity is derived from shell height (`(h − 0.62·hLarge) / (0.35·hLarge)`) and compact content fades inversely. Height uses a separate, ~8% slower spring (`expand.height`), adjustable in Motion Lab.
5. **Geometry tokens.** Compact widths/heights exactly follow the brief: 132×36 idle, 226×40 media, 248×36 notification, 208×40 HUD, 264×36 clipboard, 236×36 agent, 190×36 timer, 208×36 battery. Every expanded page is **440 × 284**; there are no per-page heights. The 20px inset yields a **400 × 244** viewport; each page uses 26px top / 18px bottom safe insets (leaving a 400 × 200 content band) and 14px side padding (372px usable width). Page compositions fit that band: Home is 40 + 44 + 34 + 34 + 18px with four 6px gaps; Media uses a 132px artwork, lyric card, 18px seek and 32px transport row; Notifications and Clipboard use five 40px rows; Agent uses four 48px rows; Widgets fills the band with a 172px calendar and a weather/stats/todo column. Large radius 28; flush bottom radius 22; dock height 52.
6. **Typography.** Segoe UI Variable Display/Text with Segoe UI and Microsoft YaHei UI fallbacks; Cascadia Mono/Consolas for code. Compact clock 13.5 semibold; dashboard time 28 bold; body 13.5; secondary 11. The pager overlay title is **15px semibold**, page counts 9.5px, and dots are 6px (20px active pill). The compact HUD is a single 40px row: 17px icon, 11px secondary label, flexible 3px accent track, 13.5px semibold right-aligned value. Settings/console/market use their own dense scale but remain legible at 900px.
7. **Layered stack.** Ghost pills are real DOM layers with softened fills and key lines. The island's soft shadow lives on a dedicated anchor pseudo-element so the stack remains readable instead of being swallowed by the shadow. The `+N` badge uses a dark glass chip with an accented key line so it doesn't compete with the unread count.
8. **Content is localized at render time.** A single i18n dictionary plus localized data objects. Language switching re-renders the scene, taskbar, dock, island, console, open modal and annotation card without a reload. Dates, weekdays, month labels and terminal output are localized too.
9. **Appearance preview.** A miniature media island lives inside the Appearance pane so radius, opacity, material, accent and font scale changes provide immediate feedback even while the settings window covers the real island.
10. **Prototype console as a product surface.** The console uses hairline sections, tabular numbers, custom sliders, a live frame-cadence readout and a miniature variant of the island's visual language — not a generic debug panel.

---

## Intentional prototype-only behavior

- **No Windows integration.** Wi-Fi, Bluetooth, focus, volume, brightness, battery and media are simulated. Sliders and toggles update the prototype only.
- **No persistence.** Reloading resets all settings to defaults; URL params are the only way to seed a state. Installed market plugins, language, theme, etc. are per-session.
- **Settings actions are simulated.** “Open plugin folder”, “View logs”, “Export config”, “Import config”, “Check for updates” and external links show a toast instead of touching the file system/network. Reset-all is real within the prototype session (two-step confirmation).
- **Plugin install is animated but simulated** (~1.2s progress → installed + toast).
- **No real media playback or audio.** Playback progress advances on a timer; track switching and seeking are functional UI state only.
- **Clipboard writes are attempted** with `navigator.clipboard` and fall back to a confirmation toast; file:// may block the actual write.
- **Notifications are a fixed five-item dataset.** “Clear all” empties the list; triggering a new notification repopulates it for demo purposes.
- **The Windows scene is decorative.** Explorer, Terminal, desktop icons and the taskbar are non-interactive context, except the FluidBar dock replacement.
- **FPS readout** measures the render loop's active frame cadence (capped at 120). It may show 60 when nothing is morphing, and headless capture can produce unusual cadence spikes.
- **Auto demo** exists only in this prototype; it is not part of the shipped app.
- **Timer finish** raises a toast rather than a real Windows notification.
- The **backend/layout notes** for WPF in the original research (single render element, no per-frame HWND resizing) are respected conceptually by this CSS/JS shell, but this is a browser prototype, not the WPF implementation.

---

## Verification performed

- `node --check app.js` and `node --check icons.js` — clean.
- Playwright / Microsoft Edge (Chromium), device scale 1. Interaction and screenshot pass at **1440 × 900**; the 2.7-page-width drag test used a **2560 × 1200** viewport so the pointer could travel 1080px in one gesture.
- **Zero console errors and zero page errors** across the URL sweep (all six pages, HUDs, dock, light/acrylic/English) and the UI pass (drag, wheel, dots, keyboard, mode switches, settings open/close).
- **Geometry:** the island measures exactly **440 × 284** and the page viewport exactly **400 × 244** on all six pages. Each `.pager-page` is **400 × 244**, the track is 2400px, and every page reports `scrollWidth/scrollHeight = 400/244`. A descendant scan found zero visible elements outside the 26px top / 18px bottom / 14px side safe box: no clipping or overflow. The shell does not resize during drag or snap.
- **Pager gesture measurements:**
  - **1 — sub-threshold:** from page index 2, a 20px quick flick ends on **index 2**, offset 2.000 (`snap-back`).
  - **2 — fast flick:** from page index 2, a 170px fast left flick ends on **index 3** (`flick-one`, 11.2px/ms); from page index 2, a 170px fast right flick ends on **index 1**. No ±2 jumps.
  - **3 — long drag:** from page index 0, a continuous 1080px drag (2.7 × 400px) ends on **index 2**. Damped displacement is 2.43 pages, so this is distance-based page +2 (the accepted +2/+3 outcome), not stuck at +1.
  - **4 — rubber-band:** at page 0, a 260px rightward drag shows offset **−0.2048** page while held, then settles back to **index 0, offset 0**. At page 5, a 300px leftward drag shows offset **5.2363**, then settles back to **index 5, offset 5**. Both ends rubber-band.
  - **5 — wheel:** one 100px notch lands on **index 1**. Five rapid notches reach **index 5** with the mutation log `0 → 1 → 2 → 3 → 4 → 5` — every commit is exactly one page, never a multi-page jump. A single 500px wheel event still commits only **index 1**, horizontal `deltaX=100` lands on **index 1**, and a wheel over the Home volume slider leaves the pager at **index 0**.
  - **6 — interrupt snap:** after a long drag targets page 2, the in-flight snap is interrupted with the visual offset at **2.2081**; the new pointerdown starts at **2.1921** (current visual position, not page 2), and that second drag settles on **index 3**. The new gesture reports `startOffset: 2.192`.
- **Keyboard + assistive state:** dot click → Agent (index 4), `←` → Clipboard (index 3), `Home` → index 0, `End` → index 5, `3` → index 2; `Esc` and outside-click collapse. Active dot exposes `aria-current="page"`; the live region announces e.g. “通知 · 第 3 页，共 6 页” / “Notifications · page 3 of 6”. Inactive pages are `inert` + `aria-hidden`. `↑`/`↓` no longer change pages. Dots are buttons and respond to Enter.
- **Page controls still work:** quick toggles flip, native range sliders seek/change value without paging, and the notification clear action remains in the top-left overlay (clicking it clears all five rows, hides itself, and keeps the pager on the notifications page).
- **Modes / theme / i18n:** switching Float → Flush → Dock through the console keeps the expanded shell at **440 × 284** and viewport at **400 × 244** above the dock; language, dark/light, obsidian/acrylic and horizontal dock interaction all switch live. Compact HUDs remain **208 × 40** (volume and brightness); other compact sizes are unchanged. Settings window still measures **900 × 620**.
- **Design-spec overlay:** reports **440 × 284**, page **通知**, index **3 / 6**, plus the existing radius, alpha, spring and k/c rows.
- **Capture set in [`_shots/`](_shots/):** `fix3-pager-home.png`, `fix3-pager-media.png`, `fix3-pager-notifications.png`, `fix3-pager-dots.png`, `fix3-pager-drag.png`, `fix3-pager-rubberband.png`, plus `fix3-pager-clipboard.png`, `fix3-pager-agent.png`, `fix3-pager-widgets.png`, `fix3-pager-en-light-acrylic.png`, `fix3-pager-dock-en.png`, `fix3-compact-volume-hud.png`, `fix3-compact-brightness-hud.png`, `fix3-spec-overlay.png`.
- Zero external assets; all icons inline SVG; artwork is CSS gradients + SVG grain.

---

*Prototype-only. Not the production implementation. If it looks too good to be a web dashboard, that was the point.*
