# FluidBar 官方网站

FluidBar 2.0 的官方单页，构建在 Vite + React + TypeScript 上。

旧版官网已经退役，当前实现从零重建。视觉方向是「日光光学」：页面是冷银白工作室，产品是一扇 488 × 444 的深色真实桌面窗。

## 最重要的一条规则

**不要重画灵动岛。**

首屏、故事区、外观配置器里的岛都来自 `docs/rewrite/prototype2` 的真实实现。网页通过 `public/island/` 的 clean embed 加载原型，React 外壳只负责：

- 用同源 `postMessage` 发送状态、页面、形态、材质、强调色；
- 镜像 iframe 回传的真实 DOM 状态；
- 不复制、不猜测、不发明产品状态。

真实 compact 状态：idle 132×36、media 226×40、notifications 248×36、clipboard 264×36、agent 236×36、timer 190×36、battery 208×36、hud 208×40。展开态固定 440×284。悬停只有原型自己的 1.022 缩放，不存在额外的“悬停预览”页面。

如果修改了 `docs/rewrite/prototype2`，运行：

```bash
pnpm sync:island
```

`prebuild` 也会自动同步。`public/island/embed.css` 和 `public/island/embed-bridge.js` 是手写文件，同步脚本不会覆盖。

## 开发

```bash
pnpm install
pnpm dev
# http://127.0.0.1:4173/
```

## 构建与检查

```bash
pnpm build        # 先同步真实原型，再 tsc + vite build
pnpm test         # Vitest：协议校验
pnpm test:e2e     # Playwright：真实岛状态、移动端、可达性
pnpm check        # build + unit + e2e
```

Playwright 使用本机 Microsoft Edge（`channel: 'msedge'`），不下载额外浏览器。

## 目录

```text
website/
  public/
    island/        # 由 prototype2 同步的真实岛 clean embed
    posters/       # 真实状态的 iframe 加载前 poster
    fonts/         # 字体 license 与来源说明
    og-image.png
  src/
    island/        # PrototypeFrame、协议、状态镜像
    sections/      # Hero / IslandLab / Optics / Plugins / Performance / Download
    ui/            # 站点组件 + ReactBits vendored 源码
    assets/fonts/  # Satoshi / Noto Sans SC 子集 / IBM Plex Mono
    data/          # 插件、性能与架构事实
  scripts/
    sync-island.mjs
  tests/
```

## 运行时依赖

- **ReactBits**：六个组件以 TS-CSS 源码 vendored 在 `src/ui/reactbits/`，许可见同目录 `LICENSE.md`。
- **Shaders**：只用于材质研究的 WebGPU 黑曜石样本，进入视口后才动态加载；WebGPU 不可用时保留 CSS 材质研究。
- 字体：Satoshi、Noto Sans SC 子集、IBM Plex Mono，全部自托管。字体来源、许可和再生成方式见 `public/fonts/FONT-SOURCES.md`。

## 部署

GitHub Actions 工作流 `.github/workflows/pages.yml` 构建并部署到 GitHub Pages。

```bash
BASE_PATH=/FluidBar/ pnpm build
```

本地预览 GitHub Pages 子路径时，将 `dist/` 放在服务器的 `/FluidBar/` 路径下，或直接使用工作流。

## 事实边界

- 下载只跳转 GitHub Releases，不托管安装包。
- 插件 manifest 示例来自 `docs/plugin/` 与 `schemas/`。
- 性能数字来自 `docs/rewrite/STATUS.md` 和发布说明，页面底部标注了来源与测量环境。
- 演示区标注“示例数据”，不冒充用户真实数据。
