# FluidBar 2.0 — focused interactive prototype (`prototype2`)

A clean, standalone iteration of the FluidBar dynamic-island prototype. Vanilla HTML/CSS/JS, no dependencies, no network requests, no build step. It runs from `file://` and from any static `http://` server.

This folder is intentionally separate from `docs/rewrite/prototype/` (the archived prototype). Nothing in that folder is read at runtime; the demo console only links to it.

---

## Run

Open `index.html` directly:

```
D:\build\GitLocal\FluidBar\docs\rewrite\prototype2\index.html
```

Or serve the folder:

```powershell
cd D:\build\GitLocal\FluidBar\docs\rewrite\prototype2
python -m http.server 8080
# http://127.0.0.1:8080/index.html
```

The prototype is designed for a 1440×900 desktop viewport; it also works at smaller widths (the console narrows, scene windows drop away).

---

## What changed in this iteration

### 1. Transient preview islands — no mini-island row at rest

There is **no permanent page chrome**. No dots, no six-item strip, no resting mini-islands.

During a horizontal drag or a wheel page gesture, **at most three detached preview islands** appear **below the expanded shell**, outside its 440×284 bounds:

| slot | size | content |
| --- | --- | --- |
| previous | 26 × 26 circle | page icon only |
| current | 64 × 28 capsule | page icon + tiny localized page label |
| next | 26 × 26 circle | page icon only |

- Very soft dark glass: `rgba(26,26,31,.72)` + `backdrop-filter: blur(18px)`, **no border**, subtle shadow, low-contrast icons/label.
- Entrance spring: `translateY(6px) → 0`, `opacity 0 → 1`, `scale(.92) → 1`, 280 ms, spring-like overshoot.
- They fade out **900 ms after the last drag/wheel activity** (220 ms fade, then `visibility: hidden`).
- At the first/last page only the available neighbours render (at most three, never a placeholder row).
- `?arch=1` forces the rail visible for screenshots.

### 2. Expanded panel — no black bars

- Shell: **440 × 284**.
- Content: **400 × 260** with **20 px horizontal / 12 px vertical** insets.
- No header strip, no bottom band, no in-panel scrim, no page gutter. The page fills the content box edge to edge.
- Page title is a small top-left overlay (`01 主页`); the collapse chevron is a top-right overlay. Both are transparent; only text/icon.
- Verified pixel sample on `shot-d-expanded-home.png`: top band `(9,9,12)`, bottom band `(9,9,11)`, mid-shell `(9,9,12)` — a single uniform obsidian surface, not bars.

### 3. Pixel-tight compact states

Exact closed sizes, no border contribution, `overflow: hidden` on the shell and every pill:

| state | size |
| --- | --- |
| idle | 132 × 36 |
| media | 226 × 40 |
| HUD | 208 × 40 |
| notification | 248 × 36 |
| clipboard | 264 × 36 |
| agent | 236 × 36 |
| timer | 190 × 36 |
| battery | 208 × 36 |

Notification and clipboard use a fixed **20 × 20 icon slot**, a fixed **8 px gap**, and single-line text with `min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap`. Nothing is absolutely positioned over the text line; measured descendant overflow is **0**.

### 4. Horizontal pager

Six pages: Home, Media, Notifications, Clipboard, Agent, Widgets.

| behaviour | contract |
| --- | --- |
| drag follow | gain **0.85** |
| rubber band | **0.35** beyond first/last page |
| flick | velocity > 0.55 px/ms commits **at most one page** |
| long drag | distance decides; multi-page reachable (round of undamped offset) |
| snap | spring **T = 0.38 s, ζ = 0.94** (integrated per frame in page units) |
| wheel | **140 px** accumulated per page, one page per **180 ms** window |
| scroll chaining | scrollable pages scroll first; at a boundary **40 px overscroll + 120 ms dwell** turns the page |
| keyboard | `1–6`, `←` / `→`, `Home`, `End`, `Esc` (collapse) |

No page dots. The title updates as the nearest page changes. The current page is the only non-`inert` page for assistive tech.

---

## URL parameters

All optional; combine freely.

```
?state=idle|media|notifications|clipboard|hud|expanded
 &tab=home|media|notifications|clipboard|agent|widgets
 &mode=float|flush|dock
 &theme=dark|light
 &lang=zh|en
 &arch=1
```

- `state` defaults to `idle`.
- `state=expanded` opens the panel; `tab` selects the page (default `home`).
- `tab` also works with compact states, but is only visible when expanded.
- `arch=1` forces the transient preview islands visible and forces the expanded panel (they only exist below the panel).
- `motion=reduced` is also supported (the console exposes it); the OS `prefers-reduced-motion` is honoured by default.

Examples:

```
index.html?state=expanded&tab=home
index.html?state=expanded&tab=media&mode=dock
index.html?state=notifications
index.html?state=expanded&tab=widgets&theme=light&lang=zh
index.html?state=expanded&tab=media&arch=1
```

---

## Demo console

Bottom-right, compact and stable:

- **State** — idle, media, notifications, clipboard, volume HUD, timer, battery, agent; plus expand/collapse.
- **Mode** — float / flush / dock.
- **Theme** — dark / light (the island stays obsidian by design).
- **Language** — 中文 / English (UI strings, page titles, notification bodies, preview labels).
- **Motion** — normal / reduced.
- Link to the archived prototype.

No settings page, no marketplace, no extra panels.

### Modes

- **float** — top 12 px, stadium compact pills, 30 px expanded radius.
- **flush** — top 0, square top corners, rounded bottom corners.
- **dock** — taskbar is replaced by a compact app dock; the island lives above it (bottom 118 px) and expands upward. The rail still clears the dock.

---

## Files

```
prototype2/
  index.html      static shell: scene, island, dock, taskbar, console
  styles.css      tokens, scene, shell, compact states, pager, pages, rail, console
  app.js          state, i18n, page renderers, pager engine, rail, console, URL params
  icons.js        inline SVG icon library (24×24 grid, currentColor)
  README.md       this file
  shot-*.png      verification screenshots (see below)
```

`icons.js` is a standalone icon library; it contains no app logic.

---

## Verification

Run with **Playwright + Microsoft Edge 155**, viewport **1440×900**, `file://` and a local `http://` server. Zero console errors and zero page errors on every state.

```
node --check app.js
node --check icons.js
```

### Compact measurements (Edge, 1440×900, DSF 1)

Notification (`?state=notifications`), pill `248 × 36` at `(596, 12)`:

| element | rect |
| --- | --- |
| pill | x 596 → 844, y 12 → 48 (248 × 36), `overflow: hidden` |
| icon slot | x 604 → 624, y 20 → 40 (**20 × 20**) |
| text | x 632 → 810, y 23.09 → 36.89 (178 × 13.8) |
| icon → text gap | **8 px** |
| badge | x 818 → 834, y 22 → 38 (16 × 16) |
| descendants outside pill | **0** |
| pill scroll / client | 248 / 248 |
| text style | `overflow:hidden; text-overflow:ellipsis; white-space:nowrap; min-width:0` |

Clipboard (`?state=clipboard`), pill `264 × 36` at `(588, 12)`:

| element | rect |
| --- | --- |
| pill | x 588 → 852, y 12 → 48 (264 × 36), `overflow: hidden` |
| icon slot | x 597 → 617, y 20 → 40 (**20 × 20**) |
| text row | x 625 → 840, y 23.41 → 36.59 (215 × 13.19) |
| icon → text gap | **8 px** |
| preview | x 673 → 840, y 23.7 → 36.3 (167 × 12.59), `scrollWidth 169 / clientWidth 167` → ellipsis |
| descendants outside pill | **0** |
| pill scroll / client | 264 / 264 |

All eight compact states measured exactly at their target sizes (`sizeOk: true`), with **0** compact descendants outside the pill.

### Expanded panel

- Shell: `440 × 284` at `(500, 12)`.
- Content: `400 × 260` at `(520, 24)` → 20 px / 12 px insets.
- Title overlay: `(520, 23)`, 38 × 14; chevron `(902, 20)`, 24 × 24.
- `.expanded-layer`, `.pager`, `.page`, `.home`, title, chevron: all transparent backgrounds; `0` scrim/header/band elements.
- Pixel sample: top band `(9,9,12)`, bottom band `(9,9,11)`, mid-shell `(9,9,12)`.

### Transient rail

- During drag: `mini-rail is-visible`; **3** items; sizes `26×26`, `64×28`, `26×26`; labels `["", "媒体", ""]`.
- Shell bottom `296`, rail top `304`, rail bottom `332` → outside the shell.
- Background `rgba(26,26,31,.72)`, border `0px`, shadow present.
- After release + 1250 ms: `opacity: 0`, `visibility: hidden`, class removed.

### Pager behaviour checks

- Wheel 140 px → next page; repeated events inside 180 ms do not double-turn.
- Long drag −800 px → page 2 (distance-based).
- Short fast flick −100 px → exactly one page.
- Clipboard scroll reaches `scrollTop 48` (bottom), then 40 px + 120 ms dwell chains to Agent.
- Keyboard `3`, `→`, `Home`, `End`, `Esc` verified.
- `?arch=1` keeps the rail visible after 1200 ms.

### Screenshots

All under this folder, captured at 1440×900:

| file | contents |
| --- | --- |
| `shot-a-idle.png` | idle compact, full scene |
| `shot-b-notification.png` | notification compact, full scene |
| `shot-b2-notification-zoom.png` | notification pill at 2× |
| `shot-c-clipboard.png` | clipboard compact, full scene |
| `shot-c2-clipboard-zoom.png` | clipboard pill at 2× |
| `shot-d-expanded-home.png` | expanded Home — no top/bottom bars |
| `shot-e-expanded-media.png` | expanded Media |
| `shot-f-transient-drag.png` | mid-drag, three preview islands below the panel |
| `shot-f2-rail-zoom.png` | preview islands at 2× |
| `shot-g-transient-gone.png` | 1250 ms after release — rail gone |
| `shot-h-dock.png` | dock mode, expanded |
| `shot-i-light-en.png` | light theme, English, expanded Home |
| `shot-i2-light-zh.png` | light theme, Chinese, expanded Widgets |
| `shot-j-flush.png` | flush mode, expanded Home |

---

## Design notes

- Obsidian island palette (`#08080a` / raised `#141418`), Segoe UI Variable, tabular numerals.
- Accent cyan `#4cc9f0`; success green, warning amber, danger red. No purple UI accents; album art uses teal, amber and slate-blue gradients only.
- No rim light, no borders on the shell, no purple gradient clichés.
- Desktop scene is deliberately minimal: wallpaper, four desktop icons, Explorer, Terminal, taskbar; the island remains the hero.
- The island is constant obsidian in both themes; only the desktop and console follow light/dark.
- Demo clock is fixed at **14:32, 18 Oct 2026** for deterministic screenshots. Interactions (play/pause, sliders, quick toggles, notifications, clipboard, calendar, todos) update local state only.

---

## Scope

Focused on the island and pager contract. There is no settings surface, marketplace, persistence, or backend. The archived prototype remains at `../prototype/index.html` (read-only reference).
