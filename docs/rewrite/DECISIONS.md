# FluidBar v2 — Decision Log

本文件记录 v2 重写过程中**已确认的决策**与对设计规范的偏离。规范见
[`specs/2026-10-18-fluidbar-v2-design.md`](specs/2026-10-18-fluidbar-v2-design.md)。
未记录在此的偏离视为未批准。

## 状态

| 日期 | 状态 |
|---|---|
| 2026-10-18 | v2 设计规范经用户（Doulor）评审通过；交互原型通过；开始实现 |
| 2026-10-18 | 分支 `v2/rebuild` 创建，v1 代码移除并打标签 `v1-final`（commit `e9ca526`） |

## 已锁定决策（D 系列）

| # | 决策 | 状态 | 备注 |
|---|---|---|---|
| D1 | 悬浮药丸为默认形态；`flush` / `dock` 是同一外壳的模式 | ✅ | 原型验证 |
| D2 | 状态机 `Hidden / Compact / Peek / Expanded / Closing`；无 per-island 快照窗口 | ✅ | |
| D3 | 展开面板 **统一 440×284**（所有页面），左侧 rail 48，内容随页面切换但不改变外壳尺寸 | ✅ | **修订**：规范早期草案的 per-page 高度（284/232/…）作废；Media 页在 284 高度内填充大封面 + 歌词卡 + 队列行 |
| D4 | 动效统一 `(T, ζ)` 弹簧，定步长 1/300 s 子步 | ✅ | |
| D5 | 渲染底座 = Win32 + `Windows.UI.Composition`（`WS_EX_NOREDIRECTIONBITMAP`）+ Avalonia 12 UI 层 | ⏳ | 以 M0 GO/NO-GO spike 结论为准 |
| D6 | 回退顺序：Avalonia 失败 → WPF 固定尺寸分层窗口（60fps 上限）→ 自绘 D2D | ⏳ | 触发即记录 |
| D7 | 永不逐帧修改窗口 bounds；每显示器一个固定尺寸宿主（488×332 DIP） | ✅ | CI 架构测试强制 |
| D8 | 插件 = 内置编译期 + 社区签名 DLL（collectible ALC）；仅声明式 UI；能力=披露+同意，非沙箱 | ✅ | |
| D9 | 市场 = GitHub Pages 签名 `index.json` + 分片；包在 GitHub Releases；`.fbp` 确定性 ZIP；SHA-256 + ECDSA P-256；发布者密钥 TOFU | ✅ | 具体 URL 待 Q4/Q5 |
| D10 | i18n 用 JSON 目录（非 resx），`en-US` 源 + `zh-Hans` 首发；运行时切换不重启 | ✅ | |
| D11 | 日历/天气/硬件监控/文件托盘/音频频谱不进核心，后续以插件发布 | ✅ | |
| D12 | 性能合同：120fps 目标（p99 ≤ 8.33ms）、60fps 底线、空闲 CPU <1%（目标 <0.3%）、常驻内存 <120MB（上限 150MB） | ✅ | M6 浸泡验收 |

## 实现决策（I 系列）

| # | 决策 | 理由 / 影响 |
|---|---|---|
| I1 | 解决方案使用经典 `FluidBar.sln`，不用 `.slnx` | `dotnet`/IDE/CI 工具链兼容性更稳 |
| I2 | `v1-final` 标签指向 `e9ca526`（v1 最后提交） | 1.x 可回溯；`main` 不删除历史 |
| I3 | 展开面板 rail 只放 6 个页面项（home/media/notifications/clipboard/agent/widgets），**设置与插件市场不作为 rail 项** | 6 项在 284 高度内可用 34px 视觉/36px 命中；设置/市场从标题栏图标、托盘与快捷键进入。规范草案的 8 项 rail 作废 |
| I4 | rail 规格：34×34 视觉、36×36 命中、8px 间距、面板内边距 20；选中态 raised pill + 2px accent 指示条 | 与原型 fix 版本一致；保证可操作性 |
| I5 | Core 拥有 `IslandEvent` 领域模型；`FluidBar.Sdk` 通过 `IIslandPublisher` 抽象发布插件事件，不把 Core 事件类型搬进 SDK | 避免 Core↔SDK 类型迁移造成跨团队返工；SDK 保持零依赖、未来可 IPC |
| I6 | SDK 的 `SdkInfo.ApiVersion = 2.0.0`；manifest `apiVersion` 采用 semver range，宿主做兼容性 gate | 参考研究 §4.8 |
| I7 | 插件根目录来自 `AppContext.BaseDirectory`，禁止 `Assembly.Location` | 单文件发布兼容 |
| I8 | 设置文件 `%APPDATA%\FluidBar\settings.json`，schemaVersion 提升；写盘采用 tmp→flush→replace + `.bak`；v1 迁移为 best-effort 并输出迁移报告 | 审计 F-32 / 规范 §8 |
| I9 | 贡献者/维护者目前只有 **Doulor**：插件 PR 由 Doulor 审核，CI 构建/签名/发布；不引入社区评分、付费或自动依赖安装 | 用户确认（2026-10-18） |
| I10 | 签名开发阶段使用测试密钥（测试内生成）；生产公钥通过 `marketplace/keys.json` 注入，私钥不入库 | 安全底线 |

## 待决问题（Q 系列）

| # | 问题 | Owner | 截止 | 状态 |
|---|---|---|---|---|
| Q1 | 渲染 spike GO/NO-GO（透明/acrylic/120fps/命中/内存） | Lead | M0 结束 | ✅ **GO**（2026-10-18，见下方 R1） |
| Q2 | Acrylic 不可用时的材质降级等级 | Lead + UI owner | M0 结束 | ✅ Tier0 始终可用；Tier1 AcrylicBlur + 圆角 region 裁剪；失败降级 Tier0 |

### Rendering spike 结果（Q1/Q2 结案，2026-10-18）

报告：[`research/05-avalonia-spike.md`](research/05-avalonia-spike.md)。Avalonia 12.1.4 + .NET 10，固定 488×332 DIP 窗口：

| 检查 | 结果 | 证据 |
|---|---|---|
| 逐像素透明 | ✅ GO | ActualTransparencyLevel=Transparent；截图四角为桌面像素 `#D3D3D3`、中心 `#08080A` |
| 120fps 帧预算 | ✅ GO | 3.003s / 489 paints：p50 6.05ms（165.2fps）、p99 8.11ms（预算 8.33ms，余量 37%），Debug 冷启动 |
| 命中/点击穿透 | ✅ GO | WM_NCHITTEST：内→HTCLIENT，外/圆角外→HTTRANSPARENT（SetWindowLongPtrW subclass） |
| 不抢焦点 | ✅ GO（需真机复测） | WS_EX_NOACTIVATE\|TOOLWINDOW 验证；有效点击时前台仍为 Chrome。`Window.IsActive` 不可靠，按 HWND pid 判定 |
| 空闲开销 | ✅ GO | 收敛后 0 次渲染；CPU ≤0.52% 单核；Debug WS 157MB/156 线程（Release 复测后写预算） |
| Acrylic | ✅ 可用（矩形） | AcrylicBlur 生效；圆角需 `UpdateWindowRegionOnSettle` 或降级 Tier0 |
| 多显示器 | ⏳ 环境未覆盖 | 本机 Avalonia 仅暴露 1 屏（虚拟显示适配器不可见），需真机双屏复测；不阻塞 M4 |
| 结论 | **GO** | 不需要 WPF 回退；采用 Avalonia 12 + Win32/Composition 宿主 |
| Q3 | 生产签名密钥生成与保管（ECDSA P-256） | Doulor | M5 前 | 未开始 |
| Q4 | 市场索引托管的最终 URL（GitHub Pages / raw / 国内镜像） | Doulor | M5 前 | 未开始 |
| Q5 | 是否在本仓库发布插件市场索引（gh-pages 分支），还是独立仓库 | Doulor | M5 前 | 未开始 |
| Q6 | 发布渠道（GitHub Releases / winget / 安装包格式） | Doulor | M6 | 未开始 |
| Q7 | 是否保留 v1 的 Agent hooks 安装流程（改为显式安装） | Platform owner | M3 | 设计已定，实现中 |
| Q8 | 首发语言是否只做 zh-Hans + en-US | Doulor | M4 | 默认是，社区语言后续 |
| Q9 | Dock 模式的默认开启策略（默认关闭 / 首次启动询问） | Doulor | M4 | 未开始 |
| Q10 | 安装包是否内置 "安全模式"（禁用全部插件启动） | Lead | M6 | 未开始 |

## 变更流程

1. 任何对规范的偏离 → 在本文件新增 I/Q 条目（日期、原因、影响面）。
2. 涉及跨团队接口的偏离 → Lead 在团队任务板留言并同步相关 owner。
3. 涉及用户可见行为的偏离 → 由 Doulor 确认后生效。
