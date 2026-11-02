# FluidBar v2 — Decision Log

本文件记录 v2 重写过程中**已确认的决策**与对设计规范的偏离。规范见
[`specs/2026-10-18-fluidbar-v2-design.md`](specs/2026-10-18-fluidbar-v2-design.md)。
未记录在此的偏离视为未批准。

## 2026-11-02 — 4.1.0 发行与插件最低版本

用户要求提交并发布 4.1，按 SemVer 使用 4.1.0，沿用自包含 win-x64 单文件 EXE、ZIP、SHA-256 清单及现有 GitHub 标签发布流程。文件暂存 1.0.3 为独立可选插件，最低宿主改为 4.1.0，防止旧 SDK 缺少 CopyFilesAsync/DragFiles 时加载失败；生产插件运行时使用实际宿主构建 SemVer，替换旧的固定 2.0.0。保持现有未签名宿主及强制资产校验策略，不增加默认安装插件。

## 2026-11-02 — 内置插件能力兼容与诊断收敛

用户授权逐项修复运行审查的问题。内置合成清单没有社区插件的 Capabilities 对象，文件暂存工厂必须容忍其缺失；同时声明 Files 和 UI 才构造暂存服务，Clipboard.Write 另行校验。回归覆盖生产工厂和真实 PluginRuntime 启动五个内置插件，平台源仅构造、不启动监听或更改系统设置。

通知解析按单条隔离：应用注册元数据失效退回 Windows 名称，其他字段异常保留完整异常并有界退避。读取当前 74 条 Windows 通知未复现旧故障，不能将旧日志缺失的异常根因视为已确认；退避与桥接异常详情分别保证当前容错和后续定位。

FLUIDBAR_PIN_DIAG=1 是媒体与固定位置诊断的唯一显式开关。插值处理器在关闭时跳过参数求值，开启时只保留数值、布尔和时间等元数据，文本/对象统一脱敏，旧字符串调用省略正文。复用后台 RollingFileLogger（Debug 级、256 队列、两个 1 MiB 文件）避免错误日志满队列的同步写入分支；轮转计数包含活动文件，不清理用户已有的旧临时诊断日志。

## 2026-11-02 — 文件拖动反馈与卡片内容层级

用户确认原生拖出已能复制，但跨出卡片没有标记，要求补可见反馈并一起调整文件暂存、剪贴板排版。使用 Shell 原生拖动图像和数据对象缓存私有格式，源的 GiveFeedback 负责持续更新位置；不依赖外部目标实现拖动图像，也不创建常驻预览窗口。原生接收暂停期间保留文件引用，完成和取消释放图像、位图与 COM 对象。

文件卡片采用两行名称、单独父目录、右上角移除；动作行压缩。剪贴板采用两行摘要和独立底部操作栏，名称优先于完整路径，复制仍使用原始内容。共用布局计算用于绘制、命中、滚动和 UIA，溢出显示细滚动条，末尾不显示不完整的行。新增样式遵循现有主题和文字缩放，不改变默认可选插件策略。

## 状态

| 日期 | 状态 |
|---|---|
| 2026-10-18 | v2 设计规范经用户（Doulor）评审通过；交互原型通过；开始实现 |
| 2026-10-18 | 分支 `v2/rebuild` 创建，v1 代码移除并打标签 `v1-final`（commit `e9ca526`） |
| 2026-10-18 | 渲染底座 spike：**GO**（Avalonia 12 + Win32/Composition）；材质改为 Obsidian + 模拟玻璃 |
| 2026-10-18 | M1 Core（327 tests）、M3 Platform（341 tests）、M5 内置插件（23 tests）、M2 插件平台（175 tests）完成 |

## 已锁定决策（D 系列）

| # | 决策 | 状态 | 备注 |
|---|---|---|---|
| D1 | 悬浮药丸为默认形态；`flush` / `dock` 是同一外壳的模式 | ✅ | 原型验证 |
| D2 | 状态机 `Hidden / Compact / Peek / Expanded / Closing`；无 per-island 快照窗口 | ✅ | Core 已实现 327 tests |
| D3 | 展开面板 **统一 440×284**（所有页面），**无 rail**；内容区 400×252，横向鼠标拖动 pager 切换页面，不改变外壳尺寸 | ✅ | **修订**：早期 per-page 高度与左侧 rail 均作废（用户 2026-10-18 反馈） |
| D4 | 动效统一 `(T, ζ)` 弹簧，定步长 1/300 s 子步 | ✅ | |
| D5 | 渲染底座 = Win32 + `Windows.UI.Composition`（`WS_EX_NOREDIRECTIONBITMAP`）+ Avalonia 12 UI 层 | ✅ | spike GO（见 R 节） |
| D6 | 回退顺序：Avalonia 失败 → WPF 固定尺寸分层窗口（60fps 上限）→ 自绘 D2D | ✅ | 未触发，无需回退 |
| D7 | 永不逐帧修改窗口 bounds；每显示器一个固定尺寸宿主（488×332 DIP） | ✅ | 架构测试强制 |
| D8 | 插件 = 内置编译期 + 社区签名 DLL（collectible ALC）；仅声明式 UI；能力=披露+同意，非沙箱 | ✅ | M2 + 内置插件已落地 |
| D9 | 市场 = GitHub Pages 签名 `index.json` + 分片；包在 GitHub Releases；`.fbp` 确定性 ZIP；SHA-256 + ECDSA P-256；发布者密钥 TOFU | ✅ | M2 已实现；CI 流水线 M5 进行中 |
| D10 | i18n 用 JSON 目录（非 resx），`en-US` 源 + `zh-Hans` 首发；运行时切换不重启 | ✅ | App 已实现 340 keys ×2 |
| D11 | 日历/天气/硬件监控/文件托盘/音频频谱不进核心，后续以插件发布 | ✅ | |
| D12 | 性能合同：120fps 目标（p99 ≤ 8.33ms）、60fps 底线、空闲 CPU <1%（目标 <0.3%）、常驻内存 <120MB（上限 150MB） | ✅ | Debug 基线已测；Release 浸泡在 M6 |

## 实现决策（I 系列）

| # | 决策 | 理由 / 影响 |
|---|---|---|
| I1 | 解决方案使用经典 `FluidBar.sln`，不用 `.slnx` | 工具链兼容性更稳 |
| I2 | `v1-final` 标签指向 `e9ca526`（v1 最后提交） | 1.x 可回溯；`main` 不删除历史 |
| I3 | 展开面板**无 rail**；导航为横向鼠标拖动 pager（页点 + 左上标题 + 右上收起）；设置/市场从标题栏图标、托盘、快捷键进入 | 用户 2026-10-18 反馈：左侧 rail 不好看 |
| I4 | pager 规格：视口 400×252、页距 400、拖动 damping 0.90、端点橡皮筋 0.35、阈值 24 DIP；flick（<0.5 页）≤1 页、长拖 ≥0.5 页按距离可多页；滚轮 100 DIP/120ms 一格一页；吸附 `motion.tab` T.34/ζ.92 可打断；页点 6/20px 可点击 | Core `PagePagerPolicy` 为唯一实现（用户反馈：阻尼要好、不能一滑多页、也不能卡在一页） |
| I5 | Core 拥有 `IslandEvent` 领域模型；`FluidBar.Sdk` 通过 `IIslandPublisher` 抽象发布插件事件 | 避免 Core↔SDK 类型迁移；SDK 零依赖、IPC 友好 |
| I6 | `SdkInfo.ApiVersion = 2.0.0`；manifest `apiVersion` semver range + 宿主兼容 gate | 参考研究 §4.8 |
| I7 | 插件根目录来自 `AppContext.BaseDirectory`，禁止 `Assembly.Location` | 单文件发布兼容（spike 已验证） |
| I8 | 设置 `%APPDATA%\FluidBar\settings.json`，schemaVersion 2；原子写 tmp→flush→replace + `.bak`；v1 迁移 best-effort 并输出报告 | 审计 F-32 |
| I9 | 维护者/贡献者目前只有 **Doulor**：插件 PR 由 Doulor 审核，CI 构建/签名/发布；不做评分/付费/自动依赖安装 | 用户确认 |
| I10 | 签名：测试内生成开发密钥；生产公钥经 `marketplace/keys.json` 注入，私钥不入库；市场包用 detached signature | 安全底线 |
| I11 | **材质策略修订**：Avalonia 12 无法实现"仅药丸区域"的原生 Acrylic（`AcrylicBlur`/`Mica` 作用于整个宿主矩形；DirectComposition 无 acrylic）。v2.0 默认 Tier 0 Obsidian，可选 **Tier 1 模拟玻璃**（自绘渐变+噪声+高光）；Tier 2 整窗 Acrylic 仅实验开关 | spike 05 §5 NO-GO |
| I12 | 帧循环采用 Avalonia `TopLevel.RequestAnimationFrame`（一次性、需重复挂载）+ 固定 HWND 内自绘；接受"p99 达标但尾部偶发帧 >8.33ms"（两轮：p99 8.11/7.76ms，尾帧 4/360）。若未来需严格无尾帧，走自绘 D2D 备选 | spike 两种实现一致 |
| I13 | Dock 模式使用独立的固定宿主高度 **488×392**（24+52 窄条+8 gap+284 面板+24 bleed）；float/flush 保持 488×332；不注册 AppBar | 52+8+284+48=392 > 332，按模式固定窗口 |
| I14 | 展开导航最终形态待用户确认：候选为"页签群岛"（交互时浮现一排带活预览的小岛，无页点）、单岛目的地提示、扇形总览；Core `PagePagerPolicy` 的拖动/吸附/滚轮规则不变 | 用户 2026-10-18 反馈：页点丢失选项卡直观性，希望创新多页展示 |
| I15 | **材质 Tier 2 = 液态玻璃**：分层窗口拿不到 DWM 背景模糊，改为自绘光学结果（透镜边缘 + 顶部高光 + 底部焦散 + 边缘色散 + 静态光泽 + 指针光晕）；静态层按尺寸/DPI/圆角烘焙进有界图片缓存（≤4 张），morph 期间 1/4 分辨率 + 8 DIP 桶；玻璃类材质的烘焙阴影从壳体轮廓挖空，避免半透明填充把阴影显示成深色板 | 用户 2026-10-23 要求新增第三种材质；参考 18 个液态玻璃仓库（`docs/rewrite/research/liquid-glass.md`）；实测稳定帧 0.65 ms / 0 分配 |

## 待决问题（Q 系列）

| # | 问题 | Owner | 截止 | 状态 |
|---|---|---|---|---|
| Q1 | 渲染底座 GO/NO-GO（透明/120fps/命中/内存） | Lead | M0 | ✅ **GO**（R 节） |
| Q2 | 材质降级等级 | Lead + UI owner | M0 | ✅ Obsidian 默认 + 模拟玻璃（I11） |
| Q3 | 生产签名密钥生成与保管（ECDSA P-256） | Doulor | M5 | 未开始 |
| Q4 | 市场索引最终 URL（GitHub Pages / raw / 国内镜像） | Doulor | M5 | 未开始 |
| Q5 | 市场索引放本仓库 gh-pages 还是独立仓库 | Doulor | M5 | 未开始 |
| Q6 | 发布渠道（GitHub Releases / winget / 安装包格式） | Doulor | M6 | 未开始 |
| Q7 | Agent hooks 显式安装流程 | Platform owner | M3 | ✅ 已实现（consent-gated installer） |
| Q8 | 首发语言是否只做 zh-Hans + en-US | Doulor | M4 | 默认是 |
| Q9 | Dock 模式默认开启策略 | Doulor | M4 | 未开始 |
| Q10 | 安装包是否内置安全模式（禁用全部插件启动） | Lead | M6 | 未开始 |

## Rendering spike 结果（Q1/Q2 结案，2026-10-18）

报告：[`research/05-avalonia-spike.md`](research/05-avalonia-spike.md)（两轮独立 spike，Avalonia 12.1.4 + .NET 10；固定 488×332 DIP 宿主）。

| 检查 | 结果 | 证据 |
|---|---|---|
| 逐像素透明 | ⚠️ 本会话截图不可作证 | Avalonia/WPF/官方 spike 的窗口在 CopyFromScreen 中都显示灰色宿主矩形（疑似 ToDesk/GameViewer 虚拟显示器采集伪影）；ActualTransparencyLevel=Transparent、RTB 正确；需真机肉眼/Windows.Graphics.Capture 复测 |
| 120fps 帧预算 | ✅ GO（有尾帧） | 轮 A：489 paints/3.0s，p50 6.05ms、p99 8.11ms；轮 B：360 预热帧 p50 6.05 / p99 7.76 / mean 165.9fps，4/360 帧 >8.33ms（max 18.59ms）。UI 线程 RAF，非合成器驱动；收敛后 0 渲染 |
| 命中/穿透/拖拽 | ✅ GO | WM_NCHITTEST 内→HTCLIENT、外/圆角外→HTTRANSPARENT；拖拽精确 (−120,−80)px 且前台未变；MA_NOACTIVATE |
| 不抢焦点 | ✅ GO | `WS_EX_NOACTIVATE|TOOLWINDOW`；`Window.IsActive` 不可靠，按 HWND pid/前台窗口判定 |
| 置顶策略 | ✅ GO | Chrome 前台可置顶；`Windows.UI.Core.CoreWindow`（开始菜单）前台时抑制；开始菜单可覆盖岛 |
| 空闲开销 | ✅ GO | 62s 空闲：0 渲染、0.151% 单核、+28.7KB 分配、GC 平坦、WS 170.5MB（Debug） |
| Acrylic（原生药丸） | ⛔ NO-GO | `AcrylicBlur`/`Mica` 只覆盖整窗；DirectComposition 无 acrylic；LowLatency 黑框；Redirection ~77fps。→ 模拟玻璃（I11） |
| 多显示器 | ⏳ 未验证 | 本机仅暴露 1 屏（虚拟显示适配器不可见）；PMv2 @144DPI/1.5× 主屏放置精确；发布前必须真机双屏复测 |
| 单文件宿主下的插件 | ✅ GO | `Assembly.Location==""`、`AppContext.BaseDirectory` 可用；外部插件加载/卸载/禁扫正常（06 spike 13/13） |
| 结论 | **GO** | 不需要 WPF 回退；采用 Avalonia 12 + Win32/Composition 宿主 + 模拟玻璃 |

## 变更流程

2026-11-02 文件拖出交互：声明式文件行新增可选 DragFiles；轻点在松手提交原命令，超过 6 DIP 后由宿主交给 Windows OLE，只允许 Copy。原生源同时支持 CF_HDROP 与 Preferred DropEffect，源不读取/移动/删除原文件。
拖放期间钩子暂停拦截并记录物理释放信号，既避免起始按下被钩子消费后原生源误判已松手，也避免队列中晚到的移动在物理释放后启动拖放。完成/取消后恢复输入，暂存引用保留。兼容逻辑仅适配有 Files 能力的第一方旧文件行契约，不把任意插件的副标题解释为路径。

2026-11-01 修订：文件暂存以独立 `FileShelf` 持久活动显示，点击进入可选插件自身页面，清空/停用时撤回。
优先级使用通知提升至 rank 0 后空出的 rank 9，系统 HUD 可以暂时覆盖文件状态，文件状态优先于 Agent/媒体；已有活动的 rank 与相对顺序不变。
原因：复用普通剪贴板活动会在 4 秒 TTL 后消失、点击进入错误页面；复用其他活动的 rank 又会触发同 rank 替换。

1. 任何对规范的偏离 → 在本文件新增 I/Q 条目（日期、原因、影响面）。
2. 涉及跨团队接口的偏离 → Lead 在团队任务板留言并同步相关 owner。
3. 涉及用户可见行为的偏离 → 由 Doulor 确认后生效。

| I15 | **渲染底座最终修订**：岛窗口改为 **Win32 分层窗口（UpdateLayeredWindow，32bpp 预乘 DIB）+ SkiaSharp 渲染器**；Avalonia 仅用于设置/市场普通窗口。宿主表面 float 488×384 / dock 488×444 DIP（面板外 detached mini-island 条）。Avalonia 11.3.22 与 12.1.4 在本机均透明失效（灰矩形），WPF/原生分层窗口透明正常；Region 裁形被 DWM 忽略且每帧 6–8ms，故放弃 | spike 05 + 后续探针/决策（2026-10-18） |
| I16 | **pager 手感终版**：跟随 0.85、吸附 T 0.38 s / ζ 0.94、阈值 26 DIP、flick 0.55 DIP/ms、滚轮 140 DIP / 180 ms、滚动链 40 DIP + 120 ms、橡皮筋 0.35；PagerConstants 与 prototype2/UI 已对齐 | prototype2 手感验收（2026-10-18） |




