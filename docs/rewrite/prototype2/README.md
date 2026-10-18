# FluidBar 2.0 — focused interactive prototype (`prototype2`)

A standalone iteration of the FluidBar dynamic-island prototype: vanilla HTML/CSS/JS, no dependencies, no network requests, no build step. It runs from `file://` or any static `http://` server.

This folder is intentionally separate from `docs/rewrite/prototype/` (the archived prototype). Nothing in that folder is read at runtime; the console only links to it.

**Last updated: 2026-10-18.**

---

## Run

Open `index.html` directly:

```
D:\build\GitLocal\FluidBar\docs\rewrite\prototype2\index.html
```

Or serve the folder:

```powershell
cd D:\build\GitLocal\FluidBar\docs\rewrite\prototype2
python -m http.server 8125
# http://127.0.0.1:8125/
```

Designed for a **1440 × 900** desktop viewport; it also works at smaller widths (the console narrows, scene windows drop away, the settings modal shrinks). Page indices in this document are **0-based** (page 0 = Home); the UI and `?tab=` use names, and the pager title shows 1-based numbers (`01`…`06`).

---

## Interaction contract

| Situation | Contract |
| --- | --- |
| **At rest** | One compact pill only. No preview islands, no dots, no page chrome. The rail exists in the DOM but is `visibility: hidden`, `opacity: 0` and not hit-testable. |
| **Expand** | Click the pill, press `Enter`/`Space` on it, use the console, a dock app, or `?state=expanded`. Shell becomes **440 × 284**. |
| **Collapse** | Chevron, `Esc` (closes settings first if it is open), or the console's close-panel trigger (收起面板 / Close panel). Clicking the desktop does not collapse the panel. |
| **Drag** | Horizontal drag on the page area; follow gain **0.85**. A flick (> **0.55 px/ms**) commits exactly one page; a long drag (> 0.75 page) uses distance and can cross several pages. |
| **Wheel** | **140 px** per page, at most one commit per **180 ms**. Scrollable page content scrolls first; at an edge, **40 px** overscroll held for **120 ms** chains to the next page. |
| **Keyboard** | `1`–`6`, `←`/`→`, `Home`, `End`; `Esc` collapses. |
| **Activity** | Any drag/wheel/key/far-dot activity shows the three preview islands. They fade out **900 ms after the last activity** (settings → island → auto-collapse, 0.4–3.6 s), with a 220 ms opacity fade. |
| **Far dots** | Only visible with the strip. Clicking a dot jumps to the **farthest page in that direction** — left → Home (`01`), right → Widgets (`06`) — locking the strip to the destination while the pager springs. |
| **Modes** | `float` (top 12 px), `flush` (top 0, square top corners), `dock` (island above a six-app dock that replaces the taskbar). |
| **Themes** | Dark/light affect the desktop, console and settings. The island itself stays obsidian (or acrylic when selected) so content contrast never changes. |

There is **no permanent six-item strip** and **no dots row at rest**.

---

## 1. Transient preview islands

During a horizontal drag, a wheel page gesture, keyboard navigation or a far-dot jump, three detached preview islands appear **8 px below the expanded shell** (outside its 440 × 284 bounds):

| slot | glass size | content |
| --- | --- | --- |
| previous | **26 × 26** circle | page icon |
| current | **64 × 28** capsule base (grows with the label) | page icon + localized page label |
| next | **26 × 26** circle | page icon |

- Glass: `rgba(26,26,31,.72)`, `blur(18px)`, no border, low-contrast icons/label (9 px).
- **Spring:** `T = 0.32 s`, `ζ = 0.85`, integrated per frame with ≤ 4 ms substeps. Settings → island → motion-strength (0.5–1.5) divides the period, so higher values morph faster.
- **What morphs:** current capsule width, current label (150 ms crossfade + 3 px vertical slide), both neighbours' positions, dot-cluster positions/opacity, and a 150 ms content pulse when an icon changes.
- **Transform/opacity only:** the capsule width is a `scaleX` on the glass layer; the icon/label layer is a sibling and is **never scaled**, so text stays crisp. Neighbours and dot clusters are positioned with `translate3d`. No CSS transition or keyframe touches the island transforms, and the rAF loop writes styles without reading layout. Label widths are measured once per language/label and cached.
- At the first/last page only the available neighbour is painted (the other slot is transparent; never a placeholder row).
- `prefers-reduced-motion` / motion = reduced: springs snap and labels swap instantly.

Measured current-capsule widths (label text at 9 px, height always 28 px):

| page | zh label | zh width | en label | en width |
| --- | --- | --- | --- | --- |
| 0 Home | 主页 | 64 | Home | 70 |
| 1 Media | 媒体 | 64 | Media | 71 |
| 2 Notifications | 通知 | 64 | Alerts | 68 |
| 3 Clipboard | 剪贴板 | 73 | Clip | 64 |
| 4 Agent | Agent | 70 | Agent | 70 |
| 5 Widgets | 小组件 | 73 | Widgets | 79 |

Neighbour circles stay 26 px. Width is clamped to 64–104 px.

---

## 2. Far-page dots

The dots answer “what is two or more pages away?” without adding a six-item strip.

| rule | behaviour |
| --- | --- |
| left cluster | pages `<= current − 2` (count = `max(0, index − 1)`) |
| right cluster | pages `>= current + 2` (count = `max(0, 5 − index − 1)`) |
| cap | at most **3 dots per side** |
| overflow | if a side holds more than 3 pages, the last dot is a **7 px pill** and only that dot's tooltip/aria-label adds “+N” |
| size | 3 px dots, 4 px gaps, 40 % white, **7 px** outside the nearest island, vertically centred on the rail (centre y = 320) |
| hit area | 7 × 15 px per dot (invisible `::after`), so 3 px dots remain clickable |
| click | jumps to the **farthest page in that direction** (left → Home `01`, right → Widgets `06`); every dot in a cluster shares that target |
| a11y | each dot is a `<button>` with localized `aria-label` + `title`; clusters are labelled `role="group"`; the empty side renders **zero** buttons |
| visibility | dots fade and disable with the strip; at rest they are not painted and not hit-testable |

Counts with the six-page pager (page indices 0-based):

| page (index) | left total | left shown | right total | right shown |
| --- | --- | --- | --- | --- |
| 0 Home | 0 | 0 | 4 | 3 (last wide, +1) |
| 1 Media | 0 | 0 | 3 | 3 |
| 2 Notifications | 1 | 1 | 2 | 2 |
| 3 Clipboard | 2 | 2 | 1 | 1 |
| 4 Agent | 3 | 3 | 0 | 0 |
| 5 Widgets | 4 | 3 (last wide, +1) | 0 | 0 |

> **Spec note.** The brief’s formula (`pages <= current−2` / `pages >= current+2`) is implemented exactly. With six pages that gives **2 left / 1 right at Clipboard (index 3)**, not 2/2 — a symmetric 2/2 would need seven pages. If 2/2 at Clipboard is wanted, either add a seventh page or change the rule (e.g. include the current page’s neighbours in the count).

---

## 3. Constants

| constant | value | meaning |
| --- | --- | --- |
| `PAGES` | 6 | home, media, notifications, clipboard, agent, widgets |
| `PAGE_W` | 400 px | page width inside the 440 px shell |
| `PAGER.gain` | 0.85 | drag follow ratio |
| `PAGER.rubber` | 0.35 | overscroll beyond first/last page |
| `PAGER.flick` | 0.55 px/ms | flick commits at most one page |
| `PAGER.snapT` / `snapZ` | 0.38 s / 0.94 | pager snap spring |
| `PAGER.wheelStep` | 140 px | wheel travel per page |
| `PAGER.wheelWindow` | 180 ms | minimum gap between wheel commits |
| `PAGER.overscroll` / `dwell` | 40 px / 120 ms | scroll-chaining edge threshold |
| `RAIL_DELAY` | 900 ms | default strip lifetime after activity |
| `RAIL_SPRING` | T 0.32 s, ζ 0.85 | preview-island morph spring |
| `RAIL_GAP` | 8 px | gap between islands |
| `RAIL_SLOT` | 26 px | prev/next circle diameter |
| `RAIL_CUR_BASE` | 64 px | current capsule base width (scaleX reference) |
| `RAIL_DOT_GAP` | 4 px | gap between dots |
| `RAIL_DOT_GAP_EDGE` | 7 px | gap between an island and its dot cluster |
| `RAIL_DOT_MAX` | 3 | dots per side |
| dot size | 3 px (wide 7 px) | 40 % white |
| settings modal | 900 × 620 | nav 208 px, pane 691 × 569 |

---

## 4. Compact states — pixel-tight

| state | size |
| --- | --- |
| idle | **132 × 36** |
| media | 226 × 40 |
| HUD | 208 × 40 |
| notification | **248 × 36** |
| clipboard | **264 × 36** |
| agent | 236 × 36 |
| timer | 190 × 36 |
| battery | 208 × 36 |

- The pill is `inset: 0`, `overflow: hidden`, single-line ellipsis, no scrollbars; the shell clips at the same bounds.
- Notification: 8 px left / 10 px right padding, 8 px gaps; 20 px app glyph, flexible text, 16 px unread badge.
- Clipboard: 9 px left / 12 px right padding, 8 px gap; 20 px slot, flexible text row with label · dot · preview.
- No compact descendant renders outside the pill (measured **0**).

---

## 5. Expanded panel — no black bars

- Shell **440 × 284**; expanded layer, pager and page **400 × 260** at a **20 px horizontal / 12 px vertical** inset.
- Page content occupies **400 × 232** below a 28 px title gutter; the title (`01 主页`) and collapse chevron are transparent overlays, not bands.
- Backgrounds: shell `rgba(8,8,10,.97)`; expanded layer / pager / page / page content are all transparent. There is no header strip, bottom band, scrim or page gutter.
- Pixel samples on `shot-04-expanded-home.png` / `shot-05-expanded-media.png`: top band ≈ `(9,9,11)`, bottom band ≈ `(9,9,11)` — identical to the shell fill, not a black bar; content mid ≈ `(23,23,27)` / `(15,15,18)`; desktop outside ≈ `(27,36,49)`.
- No element extends past the page bottom (`overflowBottom = 0`, `childrenBeyond = 0`).

---

## 6. Settings + plugin marketplace

A **900 × 620** modal (`role="dialog"`, `aria-modal`, focus moved into the dialog, `Esc` closes, scrim click closes) at (270, 140) with a 208 px left nav and seven sections:

| section | contents |
| --- | --- |
| **通用 / General** | language (zh/en), launch at startup, auto-update, tray icon, four global shortcut rows |
| **外观 / Appearance** | desktop theme, island material (Obsidian / Acrylic), five accent swatches, panel radius 16–36 px, fill opacity 85–100 %, **live island preview** |
| **灵动岛 / Island** | mode (float/flush/dock), anchor (center/left/right), display strategy (stack/latest), hover preview, motion (normal/reduced), auto-collapse 0.4–3.6 s, motion strength ×0.5–1.5 |
| **插件 / Plugins** | 6 installed plugins with badges, author, permission chips, settings button, enable switch, search, plugin-folder note |
| **插件市场 / Market** | featured card, search, six category chips, **10 plugin cards**, install progress + toast |
| **高级 / Advanced** | data folder path, open folder, diagnostic logs, export/import config, developer mode, Plugin API v2.4, reset with click-again confirmation |
| **关于 / About** | version, check-for-updates state machine, MIT license, contributors, docs/GitHub/feedback links |

**Live appearance** writes the same CSS variables the island uses:

- `--accent` (swatches), `--island-fill` (opacity), `--panel-radius` (large-panel radius).
- `body[data-material="acrylic"]` adds a translucent fill + `backdrop-filter` to the shell and preview.
- theme/material/anchor/hover are body datasets, so the desktop, console, island and modal all follow.
- The expanded island's radius follows `--panel-radius` (verified: 22 px after the control moves to 22, including flush `0 0 22 22` and dock `22`).

**Marketplace installs** are simulated: click install → the button becomes “安装中…” with a 1.15 s progress fill → the plugin joins the installed list, the nav count increments, and a toast confirms (“日历 已安装”). Search filters without re-rendering (focus is kept); category chips re-render the grid. All copy is localized in zh/en.

**Wiring:** the console footer has **设置** and **插件市场** buttons, the console header has a **gear** button, and `?panel=…` opens the modal for screenshots. The console note reads “设计原型 · 设置与市场为模拟数据”.

---

## 7. URL parameters

All optional; combine freely.

| parameter | values | effect |
| --- | --- | --- |
| `state` | `idle` \| `media` \| `notifications` \| `clipboard` \| `hud` \| `timer` \| `battery` \| `agent` \| `expanded` | compact state, or open the panel |
| `tab` | `home` \| `media` \| `notifications` \| `clipboard` \| `agent` \| `widgets` | expanded page |
| `mode` | `float` \| `flush` \| `dock` | island mode |
| `theme` | `dark` \| `light` | desktop/console/settings theme |
| `lang` | `zh` \| `en` | UI language |
| `motion` | `normal` \| `reduced` | spring vs instant |
| `material` | `obsidian` \| `acrylic` | island material |
| `accent` | `aqua` \| `mint` \| `amber` \| `rose` \| `blue` | accent color |
| `radius` | 16–36 | large-panel radius |
| `opacity` | 85–100 | island fill opacity |
| `anchor` | `center` \| `left` \| `right` | horizontal anchor shift |
| `arch` | `1` | forces expanded **and** keeps the preview islands visible (screenshot override) |
| `panel` | `settings` \| `market` | opens the settings modal on boot |
| `panelTab` | `general` \| `appearance` \| `island` \| `plugins` \| `market` \| `advanced` \| `about` | settings section (a valid `panelTab` wins over `panel`) |

- `state=expanded` opens the panel; `tab` selects the page.
- `arch=1` is screenshot-only; it keeps the strip and dots visible indefinitely.
- `motion=reduced` (or the OS `prefers-reduced-motion`) disables spring travel.

Examples:

```
index.html?state=expanded&tab=clipboard&arch=1
index.html?state=expanded&tab=home&mode=dock
index.html?panel=settings&panelTab=appearance
index.html?panel=market&lang=en&theme=light
index.html?state=expanded&tab=widgets&arch=1&theme=light&lang=zh
```

---

## 8. Verification

Environment: **Playwright 1.61 (bundled) + Microsoft Edge 155.0.4283.45**, viewport **1440 × 900**, DSF 1 (zooms at DSF 4), served from `http://127.0.0.1:8125/`. **Zero console errors and zero page errors** across every navigation below. `node --check app.js` and `node --check icons.js` pass. Document `scrollWidth` stays 1440 in every state (no page overflow).

### Rest

| item | measured |
| --- | --- |
| idle pill | 132 × 36 at (654, 12), centre 720 |
| rail | opacity 0, `visibility: hidden`, class `mini-rail`; hit test returns the scene |
| visible islands/dots | none |

### Preview islands + far dots (index 3 / `04`, `?state=expanded&tab=clipboard&arch=1`)

| element | rect |
| --- | --- |
| shell | (500, 12) → 440 × 284, bottom 296 |
| rail | x 720, y 304, 32 px tall → **8 px below the shell** |
| previous | 26 × 26 at (654, 307), centre 667, 320 |
| current | **73 × 28** at (683.5, 306), centre 720, 320, label 剪贴板 |
| next | 26 × 26 at (760, 307), centre 773, 320 |
| left dots | 2 × 3 px at x 632.5 and 639.5, y 318.5 |
| right dots | 1 × 3 px at x 797.5, y 318.5 |
| dot gap to islands | **7 px** both sides; island gaps **8 px** |

All six pages: measured counts match the table in §2 (0/3+wide, 0/3, 1/2, 2/1, 3/0, 3+wide/0), dot rects never intersect an island or the shell, and clicking a left dot lands on `01`, a right dot on `06` from every page that has one.

### Drag / wheel / spring

| frame | label | capsule width | prev centre | next centre | dots L/R |
| --- | --- | --- | --- | --- | --- |
| before | 通知 | 64 | 667 | 773 | 1 / 2 |
| mid (~128 ms) | 剪贴板 (old 通知 fading) | 69.82 | 664.1 | 775.9 | 2 / 1 |
| settled | 剪贴板 | 73.03 | 662.5 | 777.5 | 2 / 1 |

- 37 distinct width values across the morph; **max frame-to-frame width jump 1.68 px** (smooth).
- Label stays 9 px and the content layer stays unscaled; shell stays 440 × 284; page `scrollWidth` stays 400 (no reflow).
- After the last activity the strip is hidden: the `is-visible` class is removed at 900 ms, the 220 ms fade completes at ~1.12 s; the rail is then `visibility: hidden` and no dot is hit-testable.
- Page 0 → 1 wheel: prev 26 × 26, current 64 × 28, next 26 × 26.

### Compact rects

Notification (`?state=notifications`): pill **248 × 36** at (596, 12); glyph 20 × 20 at (604, 20); text 178 × 13.79 at (632, 23.1); badge 16 × 16 at (818, 22); gaps 8 px; scroll/client 248/248; descendants outside pill **0**.

Clipboard (`?state=clipboard`): pill **264 × 36** at (588, 12); slot 20 × 20 at (597, 20); text row 215 × 13.2 at (625, 23.4); label 33 px, dot 3 px, preview 167 px; gap 8 px; scroll/client 264/264; descendants outside pill **0**; preview ellipsizes inside the row.

### Settings + marketplace

- Window **900 × 620** at (270, 140); nav 208 px; pane 690.67 × 568.67.
- Seven nav items: 通用 / 外观 / 灵动岛 / 插件 (6) / 插件市场 (10) / 高级 / 关于.
- General: 4 setting rows, 3 switches, 4 shortcut rows.
- Appearance: 5 swatches, 2 ranges, live preview (`#apShell`).
- Live check: rose accent → `--accent: #ff6b8a`; radius 22 → `--panel-radius: 22px` and expanded island `border-radius: 22px`; opacity 90 → `--island-fill: 0.900`; acrylic → island `rgba(17,17,21,0.557)` + `blur(22px) saturate(1.55)`; light theme → settings `rgba(249,250,252,0.96)` while the island stays obsidian.
- Plugins: 6 rows, 6 switches (toggle flips `aria-checked`), permission chips (网络 / 媒体 / 剪贴板 / 通知), search filters to 1 row.
- Market: 10 cards, 6 categories, featured card, 6 installed; install 日历 → “安装中…” → toast “日历 已安装”; nav count 6 → 7; search “agent” → 1 card; empty search → 0 cards + empty message.
- English: nav General / Appearance / Dynamic island / Plugins / Plugin market / Advanced / About; settings titles and market copy localized.

### Modes / themes

| mode | shell | notes |
| --- | --- | --- |
| float | (500, 12) 440 × 284 | 30 px expanded radius (or `--panel-radius`) |
| flush | (500, 0) 440 × 284 | radius `0 0 30 30`; rail bottom 324 |
| dock | (500, 498) 440 × 284 | dock (599, 842) 241 × 46; rail during activity at y 790–822, clears dock top 842 |

Light theme: desktop/scene/settings follow light tokens; the island remains `rgba(8,8,10,.97)` with white island text. Keyboard: `3` → `03 通知`; `Esc` collapses.

### Screenshots

All under this folder, captured at **1440 × 900**; zooms at **4× DSF**.

| file | contents |
| --- | --- |
| `shot-01-rest.png` | rest — idle compact 132 × 36, no strip or dots |
| `shot-02-notification-zoom.png` | notification pill 4× (248 × 36 → 1088 × 208 crop) |
| `shot-03-clipboard-zoom.png` | clipboard pill 4× (264 × 36 → 1152 × 208 crop) |
| `shot-04-expanded-home.png` | expanded Home — no top/bottom bands |
| `shot-05-expanded-media.png` | expanded Media |
| `shot-06-islands-page2.png` | page index 2 — 3 preview islands + 1 left / 2 right dots |
| `shot-07-dots-page0.png` | page index 0 — right cluster only (3 dots, last wide +1) |
| `shot-08-dots-page4.png` | page index 4 — left cluster only (3 dots) |
| `shot-09-settings-general.png` | settings → 通用 |
| `shot-10-settings-appearance.png` | settings → 外观 (live controls + preview) |
| `shot-11-marketplace.png` | marketplace — featured, search, categories, 10 cards |
| `shot-12-dock.png` | dock mode, expanded |
| `shot-13-light-en.png` | light theme, English, expanded Media |

---

## Files

```
prototype2/
  index.html      static shell: scene, island, preview rail, dock, taskbar, console,
                  toast, settings modal host
  styles.css      tokens, scene, shell, compact states, pager, pages, preview islands
                  + far dots, console, toast, settings/marketplace, responsive
  app.js          state, i18n, page renderers, pager, rail spring + dots, settings,
                  marketplace, console, URL params
  icons.js        inline SVG icon library (24×24 grid, currentColor)
  README.md       this file
  shot-*.png      verification screenshots (13 files, listed above)
```

`icons.js` is a standalone icon library; it contains no app logic.

---

## Design notes

- Obsidian island palette (`#08080a` / raised `#141418`), Segoe UI Variable, tabular numerals.
- Accent cyan `#4cc9f0` (switchable: aqua / mint / amber / rose / cobalt); success green, warning amber, danger red. No purple UI accents.
- No rim light, no borders on the shell, no purple-gradient clichés.
- Settings window uses the same `--ui-*` tokens as the console, so it follows light/dark; the marketplace hero and the island preview stay obsidian for contrast.
- The desktop scene is deliberately minimal: wallpaper, four desktop icons, Explorer, Terminal, taskbar; the island remains the hero.
- Demo clock is fixed at **14:32, 18 Oct 2026** for deterministic screenshots. Interactions (play/pause, sliders, quick toggles, notifications, clipboard, calendar, todos, plugins) update local state only.

---

## Porting notes & compromises

1. **Settings/marketplace source.** The archived prototype’s settings/marketplace code was read (read-only) and ported section by section, then restyled against prototype2’s `--ui-*` tokens. The 7-section nav, live appearance preview, plugin list, featured card and 10-card grid are faithful; some archived extras (font-scale slider, design-annotation overlay, dock settings app, console motion lab) were intentionally left out.
2. **Anchor** is a modest horizontal shift (`clamp(64px, 11vw, 200px)`) instead of free dragging, so the 440 px panel never leaves the viewport.
3. **Auto-collapse** drives the preview strip’s hide delay (default 0.9 s). Prototype2 has no other transient card, so this is the honest mapping of the archived control.
4. **Display strategy** (stack / latest) is stored and rendered but has no visible effect — the shell presents one island at a time.
5. **No font-scale control**, so the verified compact metrics stay exact.
6. **Plugin installs are simulated** and reset on reload; no persistence, no filesystem access, external links show a toast.
7. **`?arch=1`** remains a screenshot-only override that forces the strip visible.
8. **Clipboard dots (index 3)** follow the brief’s formula (2 left / 1 right) rather than the brief’s illustrative “2/2”; see the spec note in §2.

---

## Scope

Focused on the island, pager, preview strip, settings and marketplace. There is no persistence or backend; the archived prototype remains at `../prototype/index.html` (read-only reference).
