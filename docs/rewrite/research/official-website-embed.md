# Official website — real prototype island embed (2026-10-28)

| Item | Value |
|---|---|
| Scope | Production clean embed of `docs/rewrite/prototype2` for the official website |
| Source (read-only) | [`docs/rewrite/prototype2`](../prototype2/) — `index.html`, `app.js`, `icons.js`, `styles.css` |
| Target | `website/public/island/`, `website/public/posters/`, `website/scripts/sync-island.mjs` |
| Spec | [Official website design §10.3](../specs/2026-10-27-official-website-design.md) (真实岛嵌入与状态协议) |
| Plan | [Official website plan §2](../plans/2026-10-28-official-website.md) (Real prototype embed) |
| Verdict | **PASS** — 33/33 automated checks, headed Edge smoke check, two real-state posters generated |
| Verified | Node v22.21.0, Python 3.13.7, Edge 156.0.4314.42 (`@playwright/test` 1.64.1), 2026-10-28 |

This is the production embed, not a re-drawing: the iframe loads the prototype's real
`app.js` / `icons.js` / `styles.css`, and only the demo chrome is removed. All island sizes,
states, page transitions and materials come from the prototype's own DOM and CSS.

---

## 1. What was built

| File | Bytes | Role |
|---|---:|---|
| `website/scripts/sync-island.mjs` | 4,616 | Copies prototype runtime files, generates `embed.html`; `--check` mode for CI |
| `website/public/island/embed.html` | 4,868 | **Generated** clean shell: prototype markup + `embed.css` link + `embed-bridge.js`, static `data-theme="light"` |
| `website/public/island/embed.css` | 2,851 | Hand-written; hides demo chrome, keeps the real dark desktop scene |
| `website/public/island/embed-bridge.js` | 11,065 | Hand-written; same-origin `postMessage` bridge and state mirror |
| `website/public/island/app.js`, `icons.js`, `styles.css` | 140,079 / 13,930 / 94,517 | Verbatim copies of the prototype (generated, do not edit) |
| `website/public/posters/hero-idle.png` | 150,715 | 488×444 real idle state, clean scene (sha256 `0dfb8e98…47b79b`) |
| `website/public/posters/lab-expanded.png` | 85,322 | 488×444 real expanded home page 01 (sha256 `78e45707…843dfa2`) |

The sync script never writes to `docs/rewrite/prototype2`; `git status docs/rewrite/prototype2`
stays clean after a run.

---

## 2. Sync pipeline

```powershell
node website/scripts/sync-island.mjs          # copy + generate
node website/scripts/sync-island.mjs --check  # exit 1 if embed.html is stale (CI)
```

`sync-island.mjs` resolves the repo root from its own URL, then:

1. copies `app.js`, `icons.js`, `styles.css` from `docs/rewrite/prototype2` into
   `website/public/island/` (verbatim `copyFile`, no content edits);
2. reads the prototype's `index.html` and patches exactly three anchors — it **fails loudly**
   if any anchor is missing instead of silently generating a broken shell:
   - inserts a "generated, do not edit" comment after `<head>`;
   - inserts `<link rel="stylesheet" href="embed.css">` directly after the prototype
     `styles.css` link;
   - sets `<body data-theme="light">` (the static shell theme);
   - inserts `<script src="embed-bridge.js"></script>` directly after the prototype
     `app.js` script so the bridge observes a booted prototype;
3. writes `embed.html`. `embed.css` and `embed-bridge.js` are hand-written and never touched.

The output is deterministic: a second run produces a byte-identical `embed.html`
(`--check` passes). `website/package.json` already exposes this as `pnpm sync:island`.

### 2.1 Why `data-theme="light"` and a dark scene at the same time

The generated static markup carries `data-theme="light"` (the website shell is a light
"Daylight Optics" page), but the prototype overwrites the runtime theme during boot:
[`app.js`](../prototype2/app.js) has `theme: 'dark'` as its initial state, and
`applyAppearance()` → `syncChrome()` (`app.js:1864-1875`) sets
`document.body.dataset.theme = S.theme` right after `parseParams()`. Unless the URL passes
`?theme=light`, the embedded prototype is therefore **dark at runtime** — the product window
shows the real dark desktop scene exactly as the design spec requires (§4.1, §6.2:
"深色只出现在产品窗"; hero is a 488×444 dark desktop window).

`embed.css` also paints the desktop dark while `html[data-ready="0"]`, so the first frame
before boot can never flash the light static theme.

---

## 3. Clean embed shell (`embed.css`)

Hidden (kept in the DOM so the bridge can still click the hidden console):

| Selector | Why |
|---|---|
| `.console` | Demo/debug console (the parent controls state through the bridge instead) |
| `.toast` | Prototype demo toasts |
| `.modal-layer` | Settings + plugin marketplace modal (900×620, not part of the product window) |
| `.boot-shade` | Prototype boot overlay (`#05070a`); hidden because `embed.css` paints the desktop dark itself |
| `.scene-hint` | "Drag or scroll to page 1–6 · Esc to close" hint |
| `.desktop-icons`, `.scene-windows` | Demo-only desktop icons and simulated Explorer/Terminal windows; the design spec keeps the product window to wallpaper + taskbar + island (+ dock) — design §6.2 and §10.3 |

Kept (owned by the prototype): `.scene`, `.wallpaper` (base, glows, grain, vignette),
`.taskbar` (48 px), `.dock` (only in dock mode — `float`/`flush` keep it hidden),
`.island-layer` / `.island`.

`html, body { width:100%; height:100%; overflow:hidden; background:#05070a }` — the embed
fills its iframe exactly and never shows scrollbars or a light page behind the scene.

---

## 4. `postMessage` protocol (`embed-bridge.js`)

Same-origin only. The bridge accepts a message only when **all** of these hold:

- `event.source === window.parent` — only the embedding window may steer the island;
- `event.origin === location.origin` — same-origin, per design spec §10.3;
- `event.data.type === 'fluidbar:set'` and `event.data` is a plain object.

Invalid/unknown fields are ignored individually (never forwarded into the prototype DOM);
messages with no valid field apply nothing. Cross-origin, wrong-`source` and malformed
messages are dropped silently — the iframe's state is unchanged (verified).

### 4.1 Parent → iframe

```js
{ type: 'fluidbar:set', state?, page?, mode?, material?, accent?, motion? }
```

| Field | Accepted values | Mapping to the real prototype |
|---|---|---|
| `state` | `idle` · `media` · `notifications` · `clipboard` · `agent` · `timer` · `battery` · `hud` · `expanded` | Clicks the hidden demo-console `[data-trigger="<state>"]` / `[data-trigger="expanded"]`; `expanded` is not re-clicked when already expanded; compact states collapse automatically (the prototype's own `showCompact`) |
| `page` | integer `0..5`, clamped | Ensures the island is expanded, then dispatches a real `keydown` `1..6` on `document`, exactly like the prototype's page shortcut |
| `mode` | `float` · `flush` · `dock` | Clicks hidden `[data-set="mode"][data-value="<mode>"]` (`applySetting` → `setMode`) |
| `material` | `obsidian` · `acrylic` · `liquid` | Sets `document.body.dataset.material` (the prototype's material CSS contract) |
| `accent` | any CSS color accepted by `CSS.supports('color', …)`, ≤ 64 chars | Sets the `--accent` custom property on `documentElement` |
| `motion` | `normal` · `reduced` | Clicks hidden `[data-set="motion"][data-value="<motion>"]` (`setMotion`) |

Notes:

- **Material/accent pinning.** The prototype re-applies `body.dataset.material` from its own
  internal state on every `syncChrome()` (expand/collapse, mode change, theme change), which
  would revert a directly-set material. The bridge remembers the last requested material and
  accent and re-applies them whenever the prototype resets them, so a parent-requested
  `liquid` material survives expanding, page changes and mode switches (verified).
- **Pre-ready buffering.** The prototype builds its hidden triggers during boot. Commands that
  arrive after the bridge is attached but before `body.ready` are queued (max 16) and replayed
  in order once boot finishes, so a parent that sends its initial state immediately on iframe
  load does not lose it (verified with a deliberately slowed boot).
- Commands are applied in a fixed order — mode/motion, accent, material, state, page — because
  the prototype's own chrome sync runs on the same turn as the mode/state clicks.

### 4.2 iframe → parent

```js
{ type: 'fluidbar:state', state: {
    ready, expanded, view, page, mode, material, theme, accent, width, height
} }
```

| Field | Source |
|---|---|
| `ready` | `body.classList.contains('ready')` (prototype boot finished) |
| `expanded` | `#island[data-expanded]` |
| `view` | `#island[data-view]` (compact state id) |
| `page` | `01`–`06` from `.pager-title .page-no`, mapped to `0..5` |
| `mode`, `material`, `theme` | `body.dataset.*` |
| `accent` | computed `--accent` |
| `width`, `height` | `offsetWidth` / `offsetHeight` of `#island` — layout size (132×36 idle, 440×284 expanded, 226×40 media, …), not the 1.022 hover transform |

Posting behaviour:

- the bridge watches `#island[data-expanded|data-view|aria-expanded]`,
  `body[class|data-mode|data-material|data-theme|data-motion]`, `#pagerTitle`, and the island's
  `width`/`height` `transitionend`;
- state is posted **~350 ms after the last change** (debounced), plus once more on
  `transitionend`, so the parent only sees settled sizes/pages (the shell transition is 380 ms);
- `ready: true` is posted as soon as boot completes (no 350 ms wait) so the parent can fade the
  poster out promptly;
- duplicate snapshots are de-duplicated; the target origin is `location.origin`, not `*`.

Minimal same-origin parent harness (used for verification):

```html
<iframe id="f" src="/island/embed.html" width="488" height="444" title="FluidBar"></iframe>
<script>
  const f = document.getElementById('f');
  const send = (payload) => f.contentWindow.postMessage(
    Object.assign({ type: 'fluidbar:set' }, payload), location.origin);
  addEventListener('message', (e) => {
    if (e.origin !== location.origin) return;
    if (e.data?.type !== 'fluidbar:state') return;
    console.log(e.data.state); // { ready, expanded, view, page, mode, material, theme, accent, width, height }
  });
  // after the first postMessage state report with ready:true:
  send({ state: 'expanded', page: 2 });
</script>
```

The future React `PrototypeFrame` must therefore embed the island **same-origin**
(`${import.meta.env.BASE_URL}island/embed.html`) and must not use a sandbox that blocks
scripts or same-origin access.

---

## 5. Posters

Both posters are screenshots of the **real production embed** at exactly 488×444 px
(viewport 488×444, `deviceScaleFactor: 1`, Edge headless, no browser chrome in the capture),
generated with Playwright `page.screenshot()`:

| Poster | State on capture | Verification |
|---|---|---|
| `hero-idle.png` | idle island **132×36**, dark desktop scene, taskbar visible, console hidden | DOM measured before capture: `theme=dark`, `view=idle`, island 132×36; PNG 488×444 RGB, 150,715 B |
| `lab-expanded.png` | real click on the island → **440×284** expanded, home page **01 主页**, dark scene | DOM measured before capture: `expanded=true`, 440×284, page `01`; PNG 488×444 RGB, 85,322 B |

Capture procedure (reproducible):

```powershell
cd website/public
python -m http.server 4180 --bind 127.0.0.1
# in another shell, with @playwright/test installed in website/node_modules:
#   chromium.launch({ channel: 'msedge' }) → new context 488x444 @1x
#   goto http://127.0.0.1:4180/island/embed.html
#   wait for document.documentElement.dataset.ready === '1' + ~800 ms
#   page.screenshot({ path: 'website/public/posters/hero-idle.png', animations: 'disabled' })
#   page.click('#island'); wait for data-expanded=true and width 440 and page 01 + ~900 ms
#   page.screenshot({ path: 'website/public/posters/lab-expanded.png', animations: 'disabled' })
```

The captures match the earlier spike reference shots (`island-scene-rest.png`,
`island-scene-expanded.png` at the repo root) to within a mean per-channel difference of
0.77 / 1.77 (wallpaper grain rendering only) — i.e. the same real states, with the console
and non-product furniture removed.

Notes: the posters are square-cornered like the iframe content; the React product window
applies its own 28 px radius / 1 px border / shadow. They are 1× assets (the spec fixes the
product window at 488×444); re-capture at `deviceScaleFactor: 2` if a retina 2× asset is
needed later. The prototype uses system UI fonts, so no webfont is needed inside the iframe.

---

## 6. Verification

Verification run on 2026-10-28 against `http://127.0.0.1:4180` (`website/public` served by
`python -m http.server 4180 --bind 127.0.0.1`), Edge 156.0.4314.42, Node v22.21.0.

```powershell
node website/scripts/sync-island.mjs
node website/scripts/sync-island.mjs --check         # up to date
# serve: cd website/public; python -m http.server 4180 --bind 127.0.0.1
node %TEMP%\fluidbar-island-verify\verify.mjs         # Playwright suite (33 checks)
node %TEMP%\fluidbar-island-verify\capture-posters.mjs
playwright-cli -s=island open --browser=msedge http://127.0.0.1:4180/island/embed.html
playwright-cli -s=island resize 488 444
```

### 6.1 Results — 33/33 checks passed

Standalone embed at 488×444 (`/island/embed.html`):

- HTTP 200; prototype `ready` class after boot; runtime `theme=dark` (real dark desktop scene);
- idle island measured **132×36**, `data-expanded="false"`, `data-view="idle"`;
- `.console` / `.toast` / `.modal-layer` / `.boot-shade` / `.scene-hint` / `.desktop-icons` /
  `.scene-windows` all `display:none`; `.scene`, `.wallpaper`, `.taskbar` visible; dock hidden in float mode;
- no scrollbars (`overflow:hidden`, `scrollWidth == clientWidth`), viewport 488×444, body `#05070a`;
- real click on the island → **440×284**, `aria-expanded=true`, `aria-hidden=false`, `inert=false`, page `01`;
- `body.dataset.material = 'liquid'` → island computed style shows the liquid-glass
  `linear-gradient(...)` body and `backdrop-filter: blur(16px) saturate(1.75)`;
- `mode=dock` shows the real dock and hides the taskbar (real prototype mapping);
- **zero console errors, zero page errors, zero failed requests** (the embed's favicon is an
  inline data URI, so there is not even a favicon 404);
- generated-artifact checks: `embed.html` links `embed.css` after `styles.css`, loads
  `app.js` then `embed-bridge.js`, static `<body data-theme="light">`, source prototype still
  has `<body data-theme="dark">`.

Same-origin parent harness via `postMessage` (iframe at 488×444):

| Step | Observed state |
|---|---|
| initial | `ready:true, expanded:false, view:idle, page:0, mode:float, material:obsidian, theme:dark, 132×36` |
| `{state:'expanded'}` | `expanded:true, 440×284` |
| `{state:'expanded', page:2}` | `page:2` |
| `{state:'media'}` | `view:media, 226×40` |
| `{mode:'dock', material:'liquid', accent:'#ff3366'}` | `mode:dock, material:liquid`, `--accent:#ff3366`, island has the liquid gradient |
| invalid + cross-origin messages | ignored, state unchanged, no errors |
| pre-ready commands (slowed boot, 49 early sends) | buffered and replayed → `expanded:true, page:4, 440×284` after ready |

The headed-browser smoke check via `playwright-cli` (headed Edge, `--browser=msedge`,
viewport resized to 488×444) reported the same values: `theme=dark`, scene/wallpaper/taskbar
visible, console hidden, island 132×36 → click → 440×284, page `01`, and a clean 488×444
viewport screenshot.

---

## 7. Known deviations / limitations

1. **Static `data-theme="light"` vs runtime dark.** Per the plan the generated shell sets
   `data-theme="light"`; the prototype's `app.js` applies its real dark theme during boot
   (default `S.theme = 'dark'`). All captured/verified states are dark; `embed.css` also
   keeps the pre-boot first paint dark. No URL parameter is needed.
2. **Extra hidden furniture.** `embed.css` additionally hides `.desktop-icons` and
   `.scene-windows` beyond the five selectors listed in the task — the design spec keeps the
   product window to wallpaper/taskbar/island/dock and forbids demo chrome, and the hero
   poster reference shows no desktop icons or demo windows.
3. **Material/accent are DOM-contract values**, not prototype-internal settings (the
   prototype only exposes material in the settings modal). The bridge pins them across the
   prototype's chrome syncs; `state.material` therefore reports the requested material, which
   matches what is rendered. Clicking the prototype's own theme toggle inside the expanded
   home page can still change the theme to light (real prototype interaction); the parent
   mirrors `theme` in its state.
4. **Posters are 1× (488×444)** as specified; they are not committed as generated build
   artifacts under `dist`. Regenerate them with the procedure above if the prototype changes.
5. **Bridge requires same-origin iframe.** Cross-origin or heavily sandboxed embeds are
   rejected by design (spec §10.3) and will show the poster only.

---

## 8. Regeneration checklist

```powershell
cd <repo>
node website/scripts/sync-island.mjs
node website/scripts/sync-island.mjs --check
# re-capture posters (see §5) and re-run the Playwright checks (see §6)
git status docs/rewrite/prototype2   # must stay clean
```
