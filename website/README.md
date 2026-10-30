# FluidBar 官方网站

FluidBar 2.0 的官方单页，构建在 Vite + React + TypeScript 上。

旧版官网已经退役，当前实现从零重建。视觉方向是「日光光学」：页面是冷银白工作室，产品是一扇 488 × 444 的深色真实桌面窗。

## 最重要的一条规则

**不要重画灵动岛。**

首屏默认展示真实 expanded 440×284 控制中心，可点岛内 chevron 收起；右侧页面索引也直接驱动真实 pager。

首屏、故事区、外观配置器里的岛都来自 `docs/rewrite/prototype2` 的真实实现。网页通过 `public/island/` 的 clean embed 加载原型，React 外壳只负责：

- 用同源 `postMessage` 发送状态、页面、形态、材质；
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

## Cloudflare Pages 部署

这是一个纯静态单页，构建产物在 `website/dist/`，不依赖 Node 服务端。

Cloudflare Pages Git 集成设置：

- Root directory：`website`
- Build command：`pnpm build`
- Build output directory：`dist`
- Node.js version：`22`
- Environment variable（可选）：`SITE_URL=https://<你的域名>`，默认是 `https://fluidbar.doulor.cn`

也可以直接 CLI 部署：

```bash
cd website
pnpm build
npx wrangler pages deploy dist
```

`wrangler.toml` 已配置 `pages_build_output_dir = "dist"` 和项目名 `fluidbar`。`public/_headers` 会在构建时复制到产物，用于设置静态缓存与基础安全头。

### 当前线上部署

- Pages 项目：`fluidbar` → `https://fluidbar.pages.dev`
- 自定义域名：`https://fluidbar.doulor.cn`
- 自定义域名由 `fluidbar-site` Worker 路由代理到 Pages。原因是 `fluidbar.doulor.cn` 已有一条 A 记录，Pages 的自定义域名验证要求 CNAME；Worker 路由复用现有 Cloudflare 代理记录，不改 DNS。
- Worker 配置：`wrangler.worker.toml`，脚本：`worker-proxy.js`
- 更新流程：

```bash
cd website
SITE_URL=https://fluidbar.doulor.cn pnpm build
wrangler pages deploy dist --project-name fluidbar --branch main --commit-dirty=true
# 只有 worker-proxy.js 或路由变更时才需要：
wrangler deploy --config wrangler.worker.toml
```

注意：`SITE_URL` 只影响 canonical、OG 图和 JSON-LD 的绝对地址；不改站点运行逻辑。Worker 代理不缓存独立副本，Pages 新部署会自动生效。

## 事实边界

- axe 审计覆盖官网页面本身；iframe 内是真实产品原型，保留原型自己的文案与对比度 token，不为了让审计变绿而改产品 UI。
- 下载只跳转 GitHub Releases，不托管安装包。
- 插件 manifest 示例来自 `docs/plugin/` 与 `schemas/`。
- 性能数字来自 `docs/rewrite/STATUS.md` 和发布说明，页面底部标注了来源与测量环境。
- 演示区标注“示例数据”，不冒充用户真实数据。
- 媒体活动态使用 `Take Me Hand` 的封面与完整 29 行歌词时间轴做视觉样本；当前词按播放时钟连续填充，歌词行随时间推进。不加载、不播放音频，也不显示播放/暂停按钮。

## 插件市场静态层

- `website/public/market/index.json` 是签名源 `marketplace/index.json` 的逐字节副本；不要在 `public/` 里手改，也不要重新格式化。
- `website/public/404.html` 是必要的：Cloudflare Pages 只有在没有顶层 `404.html` 时才会把未知路径回退到 SPA `index.html`。不要删除它。
- 生成 shards、图标、截图和 listing 页面：

```powershell
python website/scripts/generate-marketplace.py
```

- 生成脚本只读取 `marketplace/index.json`；签名、URL 替换和 `generatedAt` 单调性由市场签名流程负责。
- `/market/*` 与 `/schemas/*` 的实际文件会返回真实 content-type；不存在的路径返回 404。

部署后执行 `node scripts/verify-marketplace.mjs https://fluidbar.doulor.cn`，检查线上静态文件字节、JSON content-type、CORS、ETag / 304、体积与缺失路径 404。
也可设置 `$env:PLAYWRIGHT_BASE_URL='https://fluidbar.doulor.cn'` 后运行 `pnpm test:e2e`，直接验证线上交互（不启动本地服务器）。
