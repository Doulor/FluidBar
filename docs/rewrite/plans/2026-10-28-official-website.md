# FluidBar 2.0 官方网站 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 从空白 `website/` 实现 FluidBar 2.0「日光光学」官网，产品窗内直接运行真实 prototype2，完成响应式、无障碍、测试与 Cloudflare Pages 静态构建。

**Architecture:** Vite + React 19 + TypeScript 单页。页面为冷银白亮色；产品窗是 488×444 的真实 prototype2 iframe。首屏一个真实实例，故事与外观配置器共用一个 lab 实例，父页面通过同源 postMessage 控制并镜像真实 DOM 状态。ReactBits 只做导航、CTA、卡片、颗粒和数字；Shaders 只做材质光学实验台。

**Tech Stack:** Vite 7、React 19、TypeScript、Motion、ReactBits 源码组件（TS-CSS）、`shaders`（懒加载）、Phosphor Icons、Playwright、axe、Vitest。

---

## 0. Fixed decisions

- Page theme: light only. Dark appears only in the 488×444 product window and the primary button.
- Product window: real `docs/rewrite/prototype2` embed, not a React re-drawing.
- Real compact sizes: idle 132×36, media 226×40, notifications 248×36, clipboard 264×36, agent 236×36, timer 190×36, battery 208×36, hud 208×40.
- Expanded: 440×284. Host window: 488×444.
- Hover only scales 1.022. No invented preview state.
- Real states only. Demo data is labeled `示例数据`.
- No ReactBits Dock. Dock comes from the real prototype.
- Main CTA label everywhere: `下载预览版`.

## 1. Scaffold and design tokens

**Files:**
- Create: `website/package.json`
- Create: `website/vite.config.ts`
- Create: `website/tsconfig.json`
- Create: `website/index.html`
- Create: `website/src/main.tsx`
- Create: `website/src/App.tsx`
- Create: `website/src/styles/tokens.css`
- Create: `website/src/styles/base.css`
- Create: `website/.gitignore`

- [ ] **Step 1: create package**

```json
{
  "name": "fluidbar-website",
  "private": true,
  "version": "2.0.0",
  "type": "module",
  "scripts": {
    "dev": "vite --port 4173",
    "build": "tsc -b && vite build",
    "preview": "vite preview --port 4173",
    "test": "vitest run",
    "test:e2e": "playwright test",
    "check": "pnpm build && pnpm test && pnpm test:e2e"
  }
}
```

Install:

```bash
cd website
pnpm add react react-dom motion shaders @phosphor-icons/react
pnpm add -D vite @vitejs/plugin-react typescript @types/react @types/react-dom vitest jsdom @playwright/test @axe-core/playwright
```

- [ ] **Step 2: tokens**

Write `tokens.css` with the exact values from the spec: `--bg:#F2F3F4`, `--surface:#FAFBFC`, `--ink:#0B0D10`, `--muted:#5A636C`, `--line:rgba(11,13,16,.12)`, `--accent:#2447FF`, `--island:#08080A`, `--island-raised:#141418`, `--island-accent:#4CC9F0`, and the type/spacing/radius tokens.

- [ ] **Step 3: verify**

Run: `pnpm build`
Expected: Vite build succeeds, `dist/index.html` exists.

## 2. Real prototype embed

**Files:**
- Create: `website/scripts/sync-island.mjs`
- Create: `website/public/island/embed.html`
- Create: `website/public/island/embed.css`
- Create: `website/public/island/embed-bridge.js`
- Create: `website/public/posters/` (generated)
- Source: `docs/rewrite/prototype2/app.js`, `icons.js`, `styles.css`, `index.html`

- [ ] **Step 1: sync script**

`sync-island.mjs` copies `docs/rewrite/prototype2/{app.js,icons.js,styles.css}` into `website/public/island/`, reads `index.html`, injects `embed.css` + `embed-bridge.js`, sets `data-theme="light"`, and writes `embed.html`. It must keep the real prototype files unmodified.

- [ ] **Step 2: embed.css**

Hide `.console`, `.toast`, `.modal-layer`, `.boot-shade`, `.scene-hint`. Keep `.scene`, `.wallpaper`, `.taskbar`, `.dock`, `.island-layer`. Set `html,body { width:100%; height:100%; overflow:hidden; }`.

- [ ] **Step 3: bridge**

`embed-bridge.js` implements:

```js
// parent -> iframe
{ type: 'fluidbar:set', state?, page?, mode?, material?, accent?, motion? }

// iframe -> parent
{ type: 'fluidbar:state', state: {
  ready, expanded, view, page, mode, material, theme, accent, width, height
} }
```

Mapping:
- `state: 'expanded'` → click hidden `[data-trigger="expanded"]` if not expanded.
- `state: 'idle' | 'media' | 'notifications' | 'clipboard' | 'agent' | 'timer' | 'battery' | 'hud'` → click hidden `[data-trigger="<state>"]`.
- `page` → ensure expanded, dispatch key `1`..`6`.
- `mode` → click hidden `[data-set="mode"][data-value="<mode>"]`.
- `material` / `accent` → body dataset / CSS variable.
- `motion` → click hidden `[data-set="motion"][data-value="<motion>"]`.
- Observe `#island[data-expanded]`, `#island[data-view]`, `body[data-mode|material|theme]`, `#pagerTitle` and post state after 300ms settle.

- [ ] **Step 4: verify**

Run `node website/scripts/sync-island.mjs`, then `python -m http.server 4180 -d website/public` and open `/island/embed.html`.
Expected: light surroundings not visible; real dark desktop scene, idle island 132×36, console absent; click expands to 440×284.

## 3. PrototypeFrame and state protocol

**Files:**
- Create: `website/src/island/protocol.ts`
- Create: `website/src/island/compactStates.ts`
- Create: `website/src/island/usePrototype.ts`
- Create: `website/src/island/PrototypeFrame.tsx`
- Test: `website/src/island/protocol.test.ts`

- [ ] **Step 1: protocol types**

```ts
export type CompactView =
  | 'idle' | 'media' | 'notifications' | 'clipboard'
  | 'agent' | 'timer' | 'battery' | 'hud';

export type IslandMode = 'float' | 'flush' | 'dock';
export type IslandMaterial = 'obsidian' | 'acrylic' | 'liquid';

export interface PrototypeState {
  ready: boolean;
  expanded: boolean;
  view: CompactView;
  page: number;
  mode: IslandMode;
  material: IslandMaterial;
  theme: 'dark' | 'light';
  accent: string;
  width: number;
  height: number;
}

export interface PrototypeCommand {
  type: 'fluidbar:set';
  state?: CompactView | 'expanded';
  page?: number;
  mode?: IslandMode;
  material?: IslandMaterial;
  accent?: string;
  motion?: 'normal' | 'reduced';
}
```

- [ ] **Step 2: reducer + tests**

```ts
export function isPrototypeState(value: unknown): value is PrototypeState { ... }
export function normalizePage(page: number): number { ... }
```

Tests:
- rejects missing `ready`
- clamps page to 0..5
- accepts only known compact views
- keeps unknown fields out

Run: `pnpm test`
Expected: protocol tests pass.

- [ ] **Step 3: frame component**

`PrototypeFrame` props:

```ts
interface PrototypeFrameProps {
  title: string;
  className?: string;
  onState?: (state: PrototypeState) => void;
  onReady?: () => void;
  scaleToFit?: boolean;
  lazy?: boolean;
}
```

Behavior:
- `iframe src={`${import.meta.env.BASE_URL}island/embed.html`}`.
- `postMessage` command API exposed via `useImperativeHandle` ref.
- Validate `event.origin === window.location.origin` and `event.source === iframe.contentWindow`.
- Me           asure width with `ResizeObserver`; set `transform: scale(width / 488)`; wrapper height `444 * scale`.
- Render a real-state poster under the iframe until `ready`; fade iframe in.
- `loading="lazy"` for lab frame; hero loads after `requestIdleCallback`.

- [ ] **Step 4: verify**

Run: `pnpm dev`, open `/`, click island, inspect state caption.
Expected: idle 132×36 → expanded 440×284, no console, no invented state.

## 4. Nav and hero

**Files:**
- Create: `website/src/sections/SiteNav.tsx`
- Create: `website/src/sections/Hero.tsx`
- Create: `website/src/ui/PillNav.tsx` (vendored ReactBits)
- Create: `website/src/App.tsx`

- [ ] **Step 1: nav**

Use ReactBits `PillNav`, light theme, items `体验 / 外观 / 插件 / 下载`, GitHub icon link, CTA `下载预览版`. Desktop one line; mobile menu.

- [ ] **Step 2: hero**

Layout 5/7. H1 `桌面之上，多一座岛。` two lines. Subtext and two CTAs. Right is 488×444 `PrototypeFrame` with real scene and dimension labels. `LIVE PROTOTYPE · 真实状态` label. Poster under iframe. Stage shadow tinted cold blue.

- [ ] **Step 3: verify**

Playwright checks:
- H1 and both CTAs visible at 1440×900 and 1280×800.
- iframe exists and island is 132×36 at rest.
- clicking island expands to 440×284.
- no console/page errors.

## 5. Island Lab: story + configurator

**Files:**
- Create: `website/src/sections/IslandLab.tsx`
- Create: `website/src/ui/GlassPanel.tsx`
- Modify: `website/src/App.tsx`

- [ ] **Step 1: layout**

Two-column: left steps/controls (4 cols), right sticky 488×444 `PrototypeFrame` (8 cols). One iframe for both `#story` and `#appearance`. Sticky top 96px. On mobile, stage above steps; no sticky.

- [ ] **Step 2: five steps**

1. `常驻` → `state: 'idle'`; caption `132 × 36`.
2. `活动态` → sub-buttons `媒体 / 通知 / 剪贴板 / Agent` → real compact views and sizes.
3. `展开` → `state: 'expanded'`, page 0.
4. `切换` → six page buttons → page 0..5; keyboard supported by iframe.
5. `退场` → `state: 'idle'`.

Click step sends command; active step from IntersectionObserver. The parent caption mirrors real iframe state, not local assumption.

- [ ] **Step 3: configurator**

Controls:
- mode `浮岛 / 贴顶 / Dock`
- material `黑曜石 / 亚克力 / 液态玻璃`
- accent `青 / 薄荷 / 琥珀 / 玫红 / 钴蓝`

Each sends command. Mode/material changes are reflected in real iframe. Add note: `网页中的材质为视觉近似。`

- [ ] **Step 4: verify**

Playwright:
- click each step, assert iframe state `view` / `expanded` / `page` matches.
- click material liquid, assert iframe `data-material="liquid"`.
- click Dock, assert iframe dock visible.
- no duplicate iframe (hero + lab = 2 max).

## 6. Material optics and ReactBits integration

**Files:**
- Create: `website/src/sections/MaterialOptics.tsx`
- Create: `website/src/ui/MaterialSpecimen.tsx`
- Vendor: `website/src/ui/{PillNav,SpecularButton,SpotlightCard,BorderGlow,Noise,CountUp}.tsx` + CSS
- Create: `website/src/vendor/reactbits/LICENSE.md`
- Create: `website/src/lib/useWebGPU.ts`, `useReducedMotion.ts`, `useInViewOnce.ts`

- [ ] **Step 1: vendor ReactBits**

Use `TS-CSS` source files from `https://reactbits.dev/r/<Name>-TS-CSS.json`. Keep CSS co-located. Add attribution. Do not install GSAP, Three.js, OGL or hugeicons. Components used: PillNav, SpecularButton, SpotlightCard, BorderGlow, Noise, CountUp. Drop Dock.

- [ ] **Step 2: shaders optics**

`MaterialSpecimen` lazy-loads `shaders/react` only when in view and WebGPU available. Composition: `<Shader><Glass><RoundedRect/></Glass><Obsidian/></Shader>` over a light wordmark/backdrop. Static poster fallback when unavailable or reduced motion. Never block main content. One GPU canvas max.

- [ ] **Step 3: verify**

- No WebGPU: static poster, no console error.
- WebGPU: canvas fades in after ready.
- Lighthouse/INP unaffected; chunk lazy.

## 7. Plugins, performance, download, footer

**Files:**
- Create: `website/src/sections/Plugins.tsx`
- Create: `website/src/sections/Performance.tsx`
- Create: `website/src/sections/Download.tsx`
- Create: `website/src/sections/SiteFooter.tsx`
- Create: `website/src/data/plugins.ts`
- Create: `website/src/data/specs.ts`

- [ ] **Step 1: plugins**

Six real built-in plugin categories: media, notifications, clipboard, agent, devices, widgets. `SpotlightCard` for each. Click updates a code panel with real manifest snippet. `BorderGlow` on code panel. CTA links to GitHub `docs/plugin/quickstart.md`. No fake download counts.

- [ ] **Step 2: performance**

Real numbers from docs: 0.61 ms frame, 0.85 ms p99, 120 fps, 0 B/frame, 2,600+ tests. `CountUp` on view. Footnote source/date. Four-layer architecture: Win32 host, Skia renderer, Core state machine, Plugin SDK.

- [ ] **Step 3: download**

`下载预览版` → GitHub Releases. `查看源代码` → GitHub repo. Requirements: Windows 10/11 x64, .NET 10 Desktop Runtime. Status: development preview. Release notes link. No hosted installer.

- [ ] **Step 4: footer**

GitHub, Releases, plugin docs, issues, full prototype, MIT. Credits ReactBits, Shaders, Motion. `© 2026 Doulor`.

## 8. Responsive, accessibility, reduced motion

**Files:**
- Modify: `website/src/styles/base.css`
- Modify: all section CSS
- Create: `website/src/lib/useReducedMotion.ts`

- [ ] **Step 1: responsive**

Breakpoints: 1280, 1024, 768, 480. At <1024, product window scales to fit. At <768, nav collapses, lab non-sticky, config controls stack. No horizontal overflow at 390×844.

- [ ] **Step 2: accessibility**

- Skip link.
- Semantic headings and landmarks.
- All controls have visible labels and `aria-pressed` where relevant.
- Focus rings cobalt 2px.
- `iframe title` describes the real prototype.
- Status caption `aria-live="polite"`.
- Reduced motion: send `motion: reduced` to iframe; disable page scroll reveals; shader poster.
- axe no serious/critical.

- [ ] **Step 3: verify**

Playwright at 1440×900, 1280×800, 390×844; axe audits; no overflow; keyboard path.

## 9. Tests, visual QA, and performance

**Files:**
- Create: `website/playwright.config.ts`
- Create: `website/tests/site.spec.ts`
- Create: `website/tests/a11y.spec.ts`
- Create: `website/tests/island-protocol.spec.ts`

- [ ] **Step 1: config**

`webServer: { command: 'pnpm dev --port 4173', url: 'http://127.0.0.1:4173', reuseExistingServer: true }`; use `channel: 'msedge'`.

- [ ] **Step 2: tests**

Cover:
- hero CTA visible, nav anchors work
- island idle 132×36 → expanded 440×284
- compact states sizes
- page switching via step buttons
- mode/material/accent changes reflected in iframe DOM
- no console errors / failed requests (except optional favicon)
- mobile menu and no overflow
- reduced motion path
- WebGPU-unavailable fallback
- axe

- [ ] **Step 3: visual snapshots**

Capture 1440×900 and 390×844 full page. Inspect manually. Keep only if stable.

## 10. SEO, deploy, docs

**Files:**
- Modify: `website/index.html`
- Create: `website/public/og-image.png`
- Create: `website/wrangler.toml`
- Create: `website/public/_headers`
- Create: `website/public/robots.txt`
- Modify: `README.md`
- Modify: `docs/rewrite/STATUS.md`
- Modify: `CHANGELOG.md`
- Create: `website/README.md`

- [ ] **Step 1: SEO**

Title, description, canonical, OG/Twitter, JSON-LD `SoftwareApplication` with real fields only, favicon, `lang="zh-CN"`.

- [ ] **Step 2: Cloudflare Pages**

Build with `pnpm build` at root base `/`; deploy `website/dist` via Cloudflare Pages Git integration or `npx wrangler pages deploy dist`. `wrangler.toml` sets `pages_build_output_dir = "dist"`; `SITE_URL` injects canonical/OG absolute URLs. Ensure assets use root-relative URLs and external docs use GitHub URLs.

- [ ] **Step 3: docs**

Update root README website link, STATUS checkpoint, CHANGELOG entry, website README with sync/build/deploy instructions.

- [ ] **Step 4: final verify**

```bash
cd website
pnpm check
```

Expected: build passes, Vitest passes, Playwright passes, axe no serious/critical, no console errors, no horizontal overflow.
