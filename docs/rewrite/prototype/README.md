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
| [`app.js`](app.js) | Spring engine, state machine, all rendering, i18n (zh-CN/en-US), demo data, console, settings/market, interactions, URL params. |
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
| `state` | `home` · `widgets` | Expanded panel on that tab |
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
| `state=expanded&tab=…` | `home` · `media` · `notifications` · `clipboard` · `agent` · `widgets` | Open the expanded island on a specific tab (works with `mode=dock` too). All six render the same 440×284 shell |
| `state=hud&hud=…` | `volume` · `brightness` | Seed the compact HUD with a specific control (defaults to volume) |
| `state=settings&tab=…` | `general` · `appearance` · `island` · `plugins` · `market` · `advanced` · `about` | Open settings directly on a page |

```
?state=expanded&tab=clipboard
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
| Collapse chevron in the panel header | Collapse (top-right of the expanded panel) |
| Expanded navigation rail | Six sections stacked vertically at the left edge: Home · Media · Notifications · Clipboard · Agent · Widgets. Identical 34px visuals with 8px gaps and 36px hit targets on every section; the active icon gets a raised pill, accent glyph and 2px edge indicator; hover shows its localized label to the right. Switching sections keeps the shell at 440×284 |
| Keyboard `1`–`6` | Switch expanded sections |
| Keyboard `↑` / `↓` | Move up / down through the expanded navigation rail |
| Keyboard `Esc` | Close settings → close island → stop auto demo, in that order |
| New transient events | Multi-activity ghost pills stack behind the main pill with a `+N` badge (disable via Settings → 灵动岛 → 显示策略 → 仅显示最新) |

Other panel actions: clear/dismiss notifications, copy/pin clipboard items, open agents, navigate the mini calendar, tick the three widget tasks, seek, switch tracks, etc.

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
3. **One shell, one expanded size.** A single spring-driven `.island` element owns width/height/radius; content is clipped inside. All six expanded panes use the same **440 × 284** shell (20px inset, 48px rail, 12px gap, 340px content), so switching sections never resizes the shell — only the pane content crossfades while the rail and header stay put. Content never reflows during the morph.
4. **Spring engine.** Authored as `(T, ζ)`; `k = (2π/T)²`, `c = 4πζ/T`; fixed 1/300s substeps; `dt` clamped to 1–50ms; retarget velocity retained at 100% when direction is unchanged, otherwise ×0.35 and clamped; the rAF loop stops at rest. Expanded content opacity is derived from shell height (`(h − 0.62·hLarge) / (0.35·hLarge)`) and compact content fades inversely. Height uses a separate, ~8% slower spring (`expand.height`), adjustable in Motion Lab.
5. **Geometry tokens.** Compact widths/heights exactly follow the brief: 132×36 idle, 226×40 media, 248×36 notification, 208×40 HUD, 264×36 clipboard, 236×36 agent, 190×36 timer, 208×36 battery. Every expanded section is **440 × 284**; there are no per-page heights. The 20px inset, 48px rail and 12px gap leave 340px for content. The rail keeps identical metrics on every section: six 34×34 visual buttons with 8px gaps, vertically centered (6 × 34 + 5 × 8 = 244, exactly the 284 shell minus 2 × 20 padding), each with a 36×36 hit target, active pill, 2px accent indicator and hover tooltip. Panes fill the 284 height: media uses a 146px artwork, title/artist/album, a current + next lyric card, elapsed/remaining seek and transport/output/queue rows; notifications and clipboard use five 40px rows with slightly taller line-height; agent uses four 48px rows. Large radius 28; flush bottom radius 22; dock height 52.
6. **Typography.** Segoe UI Variable Display/Text with Segoe UI and Microsoft YaHei UI fallbacks; Cascadia Mono/Consolas for code. Compact clock 13.5 semibold; dashboard time 28 bold; body 13.5; secondary 11; rail/panel icons 15–18. The compact HUD is a single 40px row: 17px icon, 11px secondary label, flexible 3px accent track, 13.5px semibold right-aligned value. Settings/console/market use their own dense scale but remain legible at 900px.
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
- Re-verified at 1440 × 900 in Microsoft Edge (Chromium) at device scale 1.
- Zero console errors and zero page errors across the URL-state sweep and while clicking every expanded section.
- Measured with `getBoundingClientRect()` while switching all six rail sections: **home, media, notifications, clipboard, agent and widgets are each exactly 440 × 284**. The inline `width`/`height` stay `440px`/`284px`; only the 340px-wide pane content is replaced/crossfaded.
- Rail metrics measured on every section: six 36×36 hit buttons (34×34 painted surface via a 1px inset), 8px visual gaps, vertically centered; active pill plus 2px indicator; keyboard `1`–`6` and `↑`/`↓` keep the shell at 440×284.
- Compact sizes unchanged: idle 132×36, media 226×40, notifications 248×36, clipboard 264×36, agent 236×36, HUD (volume and brightness) 208×40, timer 190×36, battery 208×36. Expanded `home` and `widgets` are 440×284; settings/market modals remain 900×620.
- The live design-spec overlay reports `440 × 284` for every section (width×height tag, height tag and radius tag all update from the live DOM).
- Tested idle → expand → all six rail sections → `Esc` collapse; rail hover tooltips, `1`–`6` and `↑`/`↓` navigation; close chevron; outside click; hover peek (132×36 → 142×42); drag with clamped drop; flush mode (top 0, bottom radius 22); dock open/close/above-dock panel (440×284 above the dock); mode switching; language switch; theme, material, accent, opacity; media prev/next plus output/queue; notification open/dismiss/clear; clipboard copy/pin; widget todo and calendar navigation; volume/brightness HUD layout, hover and auto-collapse; auto demo start/stop; settings nav and market install; advanced two-step reset; `file://` render.
- Capture set in [`_shots/`](_shots/): `fix2-expanded-home.png`, `fix2-expanded-media.png`, `fix2-expanded-notifications.png`, `fix2-expanded-clipboard.png`, `fix2-expanded-agent.png`, `fix2-expanded-widgets.png`, `fix2-compact-volume-hud.png`, `fix2-compact-brightness-hud.png`.
- Additional captures: `fix2-rail-tooltip.png`, `fix2-spec-overlay.png`, `fix2-expanded-en-light.png`.
- Zero external assets; all icons inline SVG; artwork is CSS gradients + SVG grain.

---

*Prototype-only. Not the production implementation. If it looks too good to be a web dashboard, that was the point.*
