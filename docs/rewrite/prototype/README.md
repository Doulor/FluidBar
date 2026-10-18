# FluidBar 2.0 — Interactive Prototype

A high-fidelity, dependency-free interaction prototype for the FluidBar 2.0 Windows dynamic-island rewrite. It is designed to be clicked through — and judged — before any production implementation starts.

Everything is vanilla HTML/CSS/JS. No build step, no package install, no network requests, no external fonts or images. Inline SVG only.

The expanded pager deliberately has **no page dots and no permanent tabs**. Page switching is owned by a transient **Page Archipelago** strip of mini-islands with live micro-previews, revealed by a drag, a wheel gesture, or a short rest in the bottom hot zone.

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
| `state=expanded&tab=…` | `home` · `media` · `notifications` · `clipboard` · `agent` · `widgets` | Open the expanded pager on a specific page (works with `mode=dock` too). All six render the same 440×284 shell and 400×252 viewport |
| `state=expanded&slide=…` | `0` · `1` · `2` · `3` · `4` · `5` | Zero-based page index for the expanded pager; useful for direct captures of any pager position. Takes precedence over `tab` when both are present |
| `arch=1` | `1` | Pin the **Page Archipelago** strip open (otherwise it is hidden at rest and only appears on drag/wheel/hot-zone activity) |
| `state=hud&hud=…` | `volume` · `brightness` | Seed the compact HUD with a specific control (defaults to volume) |
| `state=settings&tab=…` | `general` · `appearance` · `island` · `plugins` · `market` · `advanced` · `about` | Open settings directly on a page |

```
?state=expanded&tab=clipboard
?state=expanded&slide=4
?state=expanded&tab=home&arch=1
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
| Drag the page background horizontally | Grab cursor; content follows at 0.80× with rubber-band resistance at either end. A short flick commits at most one page; a long continuous drag can cross several pages by distance |
| Mouse wheel / trackpad | Pages after **140px** of accumulated delta with a **180ms** cooldown (one page per window). `deltaX` always pages, `Shift`+wheel forces a page. On a page with scrollable content the wheel scrolls that content first — see scroll chaining below |
| Page Archipelago (bottom strip) | Hidden at rest; appears on drag, wheel, or 300ms of pointer rest in the bottom 12px. Six mini-islands with live previews; active island expands to 72×30 and shows the localized page name. Click to jump, drag to scrub, wheel to page |
| Keyboard `1`–`6` | Jump to Home · Media · Notifications · Clipboard · Agent · Widgets |
| Keyboard `←` / `→` | Previous / next page |
| Keyboard `Home` / `End` | First / last page |
| Keyboard `Esc` | Close settings → close island → stop auto demo, in that order |
| New transient events | Multi-activity ghost pills stack behind the main pill with a `+N` badge (disable via Settings → 灵动岛 → 显示策略 → 仅显示最新) |

Other panel actions: clear/dismiss notifications, copy/pin clipboard items, open agents, navigate the mini calendar, tick the three widget tasks, seek, switch tracks, etc.

### Expanded pager — interaction contract

- **Geometry.** The expanded shell is fixed at **440 × 284** with **20px side** and **16px top/bottom** insets, leaving a **400 × 252** content viewport. Six pages live on one 2304px flex strip: each card is **384 × 252** with an **8px gutter** on each side, so the neighbouring cards peek about **8px** into the left/right edges as rounded 18px card edges. Paging only changes `transform: translate3d(…)` on the strip: no layout, no remount, no resize, no crossfade. Rest chrome is deliberately bare — only the localized top-left page title and the top-right collapse chevron. **No page dots, no permanent tabs.**
- **Page Archipelago — reveal.** The strip is hidden at rest. It appears when (a) a pager or strip drag starts, (b) a wheel gesture starts over the pager, or (c) the pointer rests inside the bottom **12px** hot zone for **300ms**. It fades/shrinks out **1200ms** after the last drag/wheel/pointer activity, and stays visible while a drag is in flight. `?arch=1` pins it open for screenshots and spec review.
- **Page Archipelago — layout.** A **34px** strip floats over the bottom of the page, horizontally centred, with a subtle scrim and 7px backdrop blur. Six mini-islands are **48 × 26**, 8px apart, **13px** radius, dark glass with a hairline border. The active island expands to **72 × 30** and shows the localized page name. Inactive islands show cheap CSS/SVG live micro-previews: Home a tiny clock, Media a 16px album-art gradient plus three waveform bars, Notifications a red unread dot plus two mini lines, Clipboard two mini text lines, Agent a pulsing spinner dot, Widgets a tiny calendar grid.
- **Page Archipelago — interaction.** Click an island to jump (spring snap). Drag the strip to scrub: the same **0.80** drag gain and **0.35** rubber band, mapped at **56px of strip travel per page**, with the same flick/distance release rules. Wheel over the strip pages only and never scrolls page content. The strip follows the pager offset through a **~30ms** exponential lag and shifts up to **±14px** while a drag is in progress.
- **Drag.** A primary-button pointerdown on page background (not a button, slider, list or scroll area) starts a drag. The strip follows the pointer at **0.80×** gain (20% resistance), with **0.35×** rubber-band resistance beyond page 0 or page 5. The cursor is `grab` / `grabbing`. Window-level `pointermove`/`pointerup` keep the gesture alive even if pointer capture is lost outside the island.
- **Release.** A drag shorter than **26px** always snaps back. A gesture under **0.9 page** of damped travel commits at most one page, regardless of release velocity (`flick-one` / `short-drag-one`). At **0.9 page** or more the target is chosen by distance (`round(damped page offset)`), so long drags can cross several pages (`distance-drag`). Snaps use **T = 0.44s, ζ = 0.97** and are interruptible: a new drag starts from the current visual offset.
- **Incoming-page depth.** The incoming page sits at **0.985×** scale with **6px** of parallax until it owns the viewport, then settles to 1× / 0px.
- **Wheel — no scroll owner.** On pages without scrollable content, wheel deltas accumulate to **140px** per page with a **180ms** cooldown. A single physical event can commit at most one page. Horizontal `deltaX` always pages; `Shift`+wheel forces a page.
- **Wheel — scroll chaining.** The Clipboard list is genuinely scrollable (**370px** of content in a **208px** viewport). Single owner: the wheel scrolls the list while it can. At the top/bottom boundary the surplus becomes overscroll; a page turn is armed only after **40px** of overscroll **and 120ms of dwell** in the same direction. Reversing direction cancels the armed turn. `deltaX` still pages immediately; `Shift`+wheel bypasses the scroll owner. A running page snap locks content scrolling, and content scrolling keeps the pager locked while the list can still move. A subtle cyan edge glow (`edge-top` / `edge-bottom`) appears at the boundary before the page commits.
- **Click safety.** If a pointer gesture moves more than 6px, the synthetic click is suppressed for 220ms. Buttons and native range inputs still receive normal clicks and pointer capture when pressed without dragging; sliders keep their own thumb drag. Archipelago taps are committed from the pointer-up path so pointer capture cannot swallow them.

| Constant | Value | Role |
|---|---|---|
| `dragGain` | **0.80** | Page strip follows 80% of pointer travel |
| `rubberBand` | **0.35** | Overscroll resistance past first/last page |
| `thresholdPx` | **26px** | Sub-threshold drags snap back |
| `flickVelocity` | **0.55 px/ms** | Fast short-flick classification |
| `flickMaxPages` | **0.9 page** | A gesture under this travel can never commit more than ±1 page |
| `snapT` / `snapZ` | **0.44s / 0.97** | Spring settle period / damping ratio |
| `wheelThreshold` | **140px** | Accumulated wheel delta per page (no scroll owner) |
| `wheelCooldown` | **180ms** | Minimum cadence between wheel page commits |
| `wheelLinePx` | **16px** | Normalization for line-mode wheel deltas |
| `scrollOverscroll` | **40px** | Overscroll past a content edge before paging is armed |
| `scrollDwell` | **120ms** | Same-direction dwell required before the boundary page turn |
| `pageScale` / `pageParallax` | **0.985 / 6px** | Incoming-page depth cue |
| page step / gutter | **384px / 8px** | Card pitch inside the 400px viewport |
| `ARCH.hotZone` / `hotDelay` | **12px / 300ms** | Bottom hot zone and reveal dwell |
| `ARCH.hideDelay` | **1200ms** | Auto-hide after the last drag/wheel/pointer activity |
| `ARCH.lagMs` / `followPx` | **30ms / 14px** | Strip follow lag and maximum horizontal shift |
| `ARCH.dragStep` | **56px** | Strip travel per page while scrubbing |

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
3. **One shell, one expanded pager viewport.** A single spring-driven `.island` element owns width/height/radius; content is clipped inside. Every page opens in the same **440 × 284** shell with 20px side and 16px top/bottom insets, giving a **400 × 252** viewport. Six **384 × 252** cards sit on one 2304px strip with an 8px gutter on each side; the neighbouring cards peek as rounded edges. Changing page only changes the strip's `translate3d` transform. The shell, title overlay and chevron never move or resize; the transient **Page Archipelago** is the only page-switching chrome, and there are no dots or permanent tabs.
4. **Spring engine.** Authored as `(T, ζ)`; `k = (2π/T)²`, `c = 4πζ/T`; fixed 1/300s substeps; `dt` clamped to 1–50ms; retarget velocity retained at 100% when direction is unchanged, otherwise ×0.35 and clamped; the rAF loop stops at rest. Expanded content opacity is derived from shell height (`(h − 0.62·hLarge) / (0.35·hLarge)`) and compact content fades inversely. Height uses a separate, ~8% slower spring (`expand.height`), adjustable in Motion Lab.
5. **Geometry tokens.** Compact widths/heights exactly follow the brief: 132×36 idle, 226×40 media, 248×36 notification, 208×40 HUD, 264×36 clipboard, 236×36 agent, 190×36 timer, 208×36 battery. Every expanded page is **440 × 284**; there are no per-page heights. The 20px side / 16px top-bottom insets yield a **400 × 252** viewport; each card is **384 × 252** with 12px side padding (360px usable width) and 26px top / 18px bottom safe insets (a 384 × 208 content band). Page compositions fit that band: Home is 40 + 44 + 34 + 34 + 18px with four 6px gaps; Media uses a 132px artwork, lyric card, 18px seek and 32px transport row; Notifications and Agent fit their lists exactly; Clipboard deliberately overflows (eight 42px rows + 4px gaps = 370px in 208px) so scroll chaining is real; Widgets fills the band with a calendar and a weather/stats/todo column. Large radius 28; flush bottom radius 22; dock height 52.
6. **Typography.** Segoe UI Variable Display/Text with Segoe UI and Microsoft YaHei UI fallbacks; Cascadia Mono/Consolas for code. Compact clock 13.5 semibold; dashboard time 28 bold; body 13.5; secondary 11. The pager overlay title is **15px semibold**; archipelago names are 9.5px, and the active island grows from 48×26 to 72×30 to make room for them. The compact HUD is a single 40px row: 17px icon, 11px secondary label, flexible 3px accent track, 13.5px semibold right-aligned value. Settings/console/market use their own dense scale but remain legible at 900px.
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
- Playwright / Microsoft Edge (Chromium) headed, device scale 1, **1440 × 900**; each capture called `page.bringToFront()` so rAF was not occluded.
- **Zero console errors and zero page errors** across the URL sweep (all six pages, HUDs, dock, light/acrylic/English, `arch=1`) and the interaction pass (drag, wheel, scroll chaining, strip, keyboard, mode switches, settings open/close).
- **Geometry:** the island measures exactly **440 × 284**; `#pager` is **400 × 252**; every `.pager-page` is **384 × 252**; the track is **2304px**; the base offset is **8px**, so neighbouring cards peek 8px into each edge. No page reports horizontal overflow. Clipboard content is **370px** tall inside a **208px** scroll area.
- **Archipelago reveal / hide:** pointer rest in the bottom 12px armed at 160ms, strip visible at 380ms, auto-hidden by 1680ms (1200ms after the last activity). A wheel showed it at 500ms and it was hidden at 1500ms. A 1.6s slow drag kept it visible for the whole gesture (`offset 0.833`, `lag 0.824`, shift `11.53px`).
- **Wheel:** Home → wheel 160px → **index 1 (media)**, strip visible with live previews. A second wheel during the snap was ignored (still index 1); the next wheel after settle reached **index 2**.
- **Scroll chaining (Clipboard):** from the top, a 140px wheel scrolled the list to **scrollTop 140** and left the pager on **index 3**. At the bottom, one wheel produced `edge-bottom` with `scrollAccum 67.5` and stayed on **index 3**; after the **120ms** dwell it committed **index 4 (agent)** with `kind: "scroll-dwell"`. Down-then-up before the dwell cancelled the turn (index 3, scrollTop 102). Horizontal `deltaX=160` paged to **index 4**; `Shift`+wheel also paged to **index 4** with scrollTop 0; wheel over the strip paged to **index 4** with scrollTop 0.
- **Drag:** a fast 120px flick ended on **index 1** with `pagesMoved 0.25`, `kind: "flick-one"` (never more than one page). An 800px continuous drag ended on **index 2** with `pagesMoved 1.667`, `kind: "distance-drag"` (multi-page by distance). Mid-drag the strip followed at `offset 0.375 / lag 0.351` with a `4.92px` shift.
- **Archipelago:** clicking the Clipboard island jumped to **index 3**; dragging the strip 160px scrubbed to **index 2 (notifications)** with `pagesMoved 2.286`. Strip clicks survive pointer capture because the tap is committed on pointer-up.
- **Keyboard + outside click:** `3` → index 2, `→` → index 3, `End` → index 5, `Home` → index 0, `Esc` → collapsed; clicking the desktop collapsed the shell and hid the strip.
- **Design-spec overlay:** reports **440 × 284**, page **主页**, index **1 / 6**, plus radius/alpha/spring rows.
- **Modes / theme / i18n:** Float → Dock keeps the expanded shell at **440 × 284** above the dock; English, light theme, acrylic material, dock mode and the archipelago all switch live.
- **Capture set in [`_shots/`](_shots/):**
  - `fix4-a-home-rest.png` (+ `-island`) — expanded Home at rest, clean title/chevron, no dots, 8px edge peeks.
  - `fix4-b-arch-after-wheel.png` (+ `-island`) — archipelago visible after a wheel, live previews, active Media.
  - `fix4-c-mid-drag.png` (+ `-island`) — mid-drag, strip following the pager offset.
  - `fix4-d-clipboard-scrolled.png` (+ `-island`) — Clipboard scrolled internally, strip auto-hidden.
  - `fix4-e-boundary-glow.png` (+ `-island`) — Clipboard at the bottom boundary with the cyan edge glow.
  - `fix4-f-after-dwell.png` (+ `-island`) — after the 120ms dwell, the pager has turned to Agent.
  - `fix4-g-flick-one-page.png`, `fix4-g-long-drag-multipage.png` — flick ≤1 page vs. long drag multi-page.
  - `fix4-h-island-jump.png` (+ `-island`), `fix4-h-strip-scrub.png` (+ `-island`) — click-to-jump and strip scrub.
  - `fix4-i-en-light-acrylic.png` (+ `-island`), `fix4-i-zh-light-acrylic-arch.png`, `fix4-i-en-arch-notifications.png`, `fix4-i-dock.png` — i18n, light/acrylic and dock.
  - `fix4-j-strip-autohide.png` — strip gone after the 1.2s auto-hide.
  - `fix4-spec-overlay.png` — 440 × 284 spec overlay.
- Zero external assets; all icons inline SVG; artwork is CSS gradients + SVG grain.

---

*Prototype-only. Not the production implementation. If it looks too good to be a web dashboard, that was the point.*
