# Changelog

所有用户可见或架构级变更记录在这里。格式：日期（Asia/Shanghai）+ 摘要 + 影响。

## 2026-10-18

### 方向与文档
- 创建 v2 重写分支 `v2/rebuild`；v1 代码移除，打标签 `v1-final`（`e9ca526`）。
- 建立设计规范、实施计划、决策日志（`docs/rewrite/specs|plans|DECISIONS.md`）。
- 建立项目方向备忘 `PROJECT-NOTES.md`（产品初衷、被否决 UX、技术路线、协作规则、文档维护规则）。
- 建立本 CHANGELOG 与 `docs/rewrite/STATUS.md`，作为时效性状态文档。

### 交互设计
- 否决：底部平铺标签栏、左侧竖向 rail、常驻页点、面板内小岛、常驻小岛、展开面板上下黑边、紧凑态图标压字/出界。
- 确定：默认 iOS 小岛 → 点击展开；展开面板 440×284；横向拖动 pager；面板外 3 个临时迷你岛（26/26 + 64×28），距离当前页 2 页以外用小点表示（每侧最多 3 个）。
- 确定：拖动跟随 0.85、吸附 T 0.38 / ζ 0.94、滚轮 140px/180ms、滚动链 40px + 120ms。

### 原型
- `docs/rewrite/prototype2` 干净原型：无黑边、紧凑态像素级不溢出、3 个临时迷你岛、7 分区设置面板 + 插件市场回归。
- 旧 `docs/rewrite/prototype` 因多进程并发改写损坏，归档/弃用。

### 架构与 spike
- Avalonia 11.3.22 / 12.1.4 在本机透明窗口均被压成灰色矩形；WPF 分层窗口与原生 `UpdateLayeredWindow` 正常。
- 渲染底座改为：**Win32 分层宿主（`UpdateLayeredWindow`，32bpp 预乘 DIB）+ SkiaSharp + HarfBuzzSharp**；Avalonia 仅保留设置/插件市场普通窗口。
- 分层宿主 spike GO：全帧 ULW 0.06–0.16ms、脏矩形 0.08–0.10ms、60/120fps pacing、透明/点击穿透/不抢焦点/拖拽 PASS；宿主表面 float 488×384 / dock 488×444 DIP。
- Skia 渲染器 spike GO：展开全帧 0.55–0.61ms / p99 0.85ms，紧凑 0.46ms，脏矩形 0.157ms；fidelity IoU 0.9986（紧凑）/ 1.0000（展开）。

### 代码与测试
- `FluidBar.Core`（473 tests）：事件、状态机、优先级、布局、弹簧、pager、滚动链、设置迁移、soak 预算。
- `FluidBar.Platform.Windows`（371 tests）：Win32/WinRT 集成、监控器、媒体、剪贴板、通知、Agent hooks、USB/BT、托盘/热键、更新检查、分层宿主。
- `FluidBar.Sdk` / `PluginHost` / `Marketplace`（188 tests）：collectible ALC、manifest、签名/索引、安装/回滚、离线 E2E。
- 内置插件（23 tests）、集成测试（31 tests）、Avalonia UI（90 tests）、架构测试（11 tests）。
- 修复：后台计时器跨线程刷新 UI 导致的 30–75s 崩溃；squircle 轮廓不可连接；小岛重叠；页面底部空带；紧凑通知/剪贴板越界。

### 进行中
- `FluidBar.Island`（SkiaSharp 渲染器 MVP）：紧凑态、主页/媒体、pager、面板外小岛与小点。
- 分层宿主与 App 集成（替换 Avalonia 岛窗口，设置/市场保留 Avalonia）。

### 2026-10-18 23:00 — Skia 渲染器 MVP / prototype2 完成
- `FluidBar.Island`（SkiaSharp + HarfBuzzSharp）：紧凑 8 态、展开 home/media、dock/flush/light/transition、transient strip；62 tests、16 张 golden（732×576 @150%）。
- prototype2：设置面板（7 分区）、插件市场、3 个临时小岛、远处页小点、切换弹簧动效；14+ 验证截图。
- 待修：渲染器壳体描边误用强调色（改中性 hairline）；通知/剪贴板/Agent/小组件页面待补。
- 下一步：App 宿主集成（LayeredWindowHost + renderer），预计内部预览 ~1–1.5 周、可用初代 ~3 周、完整版 ~8–12 周。

### 2026-10-18 23:36 — 渲染器页面补全 / App 集成进行中
- Skia 渲染器扩展到 134 tests、20 张 golden：新增 notifications/clipboard/agent/widgets 页面、hit-test、页面切换 golden；壳体描边改中性 hairline。
- App Skia 集成：controller、状态映射、pager、archipelago、主题映射、hit-test、SkiaIslandHost 已落盘；正在修编译并接 FLUIDBAR_HOST=skia 开关。
- Core pager 手感终版（0.85 / T0.38 / ζ0.94 / 26 DIP / flick 0.55）已提交，spec A3 + DECISIONS I16。
- 新增 Skia host 桌面 smoke 脚本与验收清单。

### 2026-10-19 00:12 — Skia 分层宿主接入 App（MVP）
- 新增 `src/FluidBar.App/Hosting/Skia/`：`SkiaIslandHost`（每显示器一个 LayeredWindowHost，488×384 float / 488×444 dock）、controller、motion、pager、archipelago、state builder、theme mapper、hit-test、UI-thread frame clock。
- `AppBootstrap` 默认先启动 Skia 宿主；失败回退 Avalonia；`FLUIDBAR_HOST=skia|avalonia` 可强制。
- 验证：`dotnet test FluidBar.sln -m:1` = 1340/1340；smoke 386 commits/0 失败、surface 732×576、全帧 0.5–1.9ms、脏帧 220；展开/媒体/中文截图与桌面点击穿透探针通过。
- 待办：tray/hotkey、未映射页面控件、UIA/IME、多显示器热插拔、dock 放置 52 DIP 偏差。
