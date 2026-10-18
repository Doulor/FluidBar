# FluidBar v2 实施计划

| 项 | 值 |
|---|---|
| 文档 | `docs/rewrite/plans/2026-10-18-v2-implementation-plan.md` |
| 状态 | 可执行（Lead 已批准实施；本文件随进度勾选） |
| 日期 | 2026-10-18 |
| 分支 | **`v2/rebuild`**（工作树共用，不另开 worktree） |
| 依据 | 设计规范 [`specs/2026-10-18-fluidbar-v2-design.md`](../specs/2026-10-18-fluidbar-v2-design.md)（下称"规范"，`§x.y` 指其章节） |
| 目标 | 2027-01-09 发布 FluidBar 2.0.0（M6 结束）；2027-01-15 公开 RC |

## 0. 执行约定

**当前工作树状态（2026-10-18 核对；并行开发中，最终以 `git log` / `git status` 为准）**：分支 `v2/rebuild` 已有提交 `ee2794b`（删除 v1 代码 + 骨架）；`FluidBar.sln` 已注册 10 个工程（Core / Sdk / Platform.Windows / Marketplace / PluginHost + Core.Tests / Sdk.Tests / Platform.Tests / Architecture.Tests / PluginHost.Tests）；已落地 `src/FluidBar.Sdk/{Versioning,Capabilities}`、`src/FluidBar.Platform.Windows/{Diagnostics,Interop}`、`.github/workflows/ci.yml`（Release + `-warnaserror` + trx）；`Directory.Build.props` 设 `VersionPrefix=2.0.0`、`Deterministic=true`、`Nullable=enable`；`global.json` 锁 SDK `10.0.301`（`rollForward: latestFeature`）；测试栈 xunit 2.9.3 + Test.Sdk 17.14.1 + coverlet 6.0.4。**尚缺**：`src/FluidBar.App`、`src/FluidBar.Cli`、`src/FluidBar.Plugins.*`、`tests/FluidBar.Ui.Tests`、`tests/FluidBar.Soak`、`spikes/`、`schemas/`、`tools/marketplace-gen`。

**规则**

1. **一个工作树，一条分支**。禁止 `git worktree`、禁止各自建分支；每人每天开工前 `git pull --rebase`，提交前 `git fetch && git rebase origin/v2/rebuild`。
2. **写入范围互斥**（见 0.1）。跨范围修改必须先在该范围的 owner 处开 issue/留言，由 owner 执行或授权。
3. **共享文件只有 Lead 能改**：`FluidBar.sln`、`Directory.Build.props`、`Directory.Packages.props`、`.github/**`、`tests/FluidBar.Architecture.Tests/**`、`docs/**`。
4. **每个任务以"可验证命令"结束**，禁止"应该能跑"。分支必须始终可 `dotnet build`；红的主干超过 4 小时回滚该提交。
5. **不提交**：`bin/`、`obj/`、`artifacts/`、`node_modules`、任何 `.pfx`/私钥、本地 `settings.json`。
6. **集成节奏**：每日 18:00 由 Lead 合并并跑全量；每周五出一次可运行快照（`dotnet run --project src/FluidBar.App`）。
7. **决策记录**：所有对规范的偏离写入 `docs/rewrite/DECISIONS.md`（含日期、原因、影响、是否需要改规范），未记录即视为未批准。

### 0.1 角色与写入范围

| 角色 | 可写（owner） | 只读 | 主要交付 |
|---|---|---|---|
| **Lead（Doulor）** | `FluidBar.sln`、`Directory.*.props`、`.github/**`、`docs/**`、`tests/FluidBar.Architecture.Tests/**`、`schemas/**`、`spikes/**` | 全部 | 基线、评审、决策、发布、签名 |
| **Core owner** | `src/FluidBar.Core/**`、`tests/FluidBar.Core.Tests/**` | 其余 | 状态机/策略/布局/弹簧/设置/i18n |
| **Plugin owner** | `src/FluidBar.Sdk/**`、`src/FluidBar.PluginHost/**`、`src/FluidBar.Marketplace/**`、`src/FluidBar.Cli/**`、`plugins/**`、`tests/FluidBar.PluginHost.Tests/**`、`tests/FluidBar.Sdk.Tests/**` | 其余 | SDK、加载器、manifest、市场领域、样例插件 |
| **Platform owner** | `src/FluidBar.Platform.Windows/**`、`tests/FluidBar.Platform.Tests/**` | 其余 | 显示器/媒体/剪贴板/通知/托盘/热键/更新 |
| **UI owner** | `src/FluidBar.App/**`、`tests/FluidBar.Ui.Tests/**` | 其余 | 组合器宿主、岛、rail 六页、dock、设置、市场 UI |
| **共同** | — | — | `tests/FluidBar.Soak/**` 由 Lead 建骨架，M6 由四人共同补场景 |

### 0.2 完成定义（每个里程碑都适用）

- `dotnet build FluidBar.sln -c Release -warnaserror` 通过；`dotnet test FluidBar.sln -c Release` 全绿。
- 新增/修改的公开 API 有 XML 注释；`dotnet format --verify-no-changes` 通过。
- 架构测试全绿（禁窗口 bounds 动画、禁 UI 线程阻塞、禁中文字面量、依赖方向）。
- 该里程碑"验收"列中的**可测量项**有对应脚本或测试，并在 PR 描述里贴出实际数值。
- 文档同步：`docs/rewrite/` 下相关章节更新；`DECISIONS.md` 已记录偏离。

---

## M0 — 基线：分支 / 骨架 / 文档 / CI（Lead）

**目标**：把"能构建、能测试、能跑空壳、有 CI、有渲染结论"这五件事一次性立起来；后续所有工作在此基础上并行。

**任务**

- [ ] 打标签 `v1-final`（`git tag v1-final e9ca526`，v1 代码已在 `ee2794b` 删除），保证 1.x 可回溯。
- [x] `FluidBar.sln` 已注册 10 个工程（Core / Sdk / Platform.Windows / Marketplace / PluginHost + 5 个测试工程）。
- [ ] 补齐缺失工程：`src/FluidBar.App`（Avalonia 12）、`src/FluidBar.Cli`、`src/FluidBar.Plugins.Builtin`、`tests/FluidBar.Ui.Tests`、`tests/FluidBar.Soak`，加入 `FluidBar.sln`（`spikes/` 不入解决方案）。
- [ ] 引入中央包管理 `Directory.Packages.props`（单一版本真源）+ `NuGet.config`（仅 nuget.org）；CI 使用 `RestorePackagesWithLockFile=true`。
- [x] `Platform.Windows` 已是 `net10.0-windows10.0.19041.0`（`App` 创建时同）；`Core`/`Sdk`/`PluginHost`/`Marketplace` 保持 `net10.0`。
- [ ] 架构测试第一条规则落地：扫描 `FluidBar.App` 与 `FluidBar.Platform.Windows`，拒绝 `Window.Left/Top/Width/Height` 赋值/动画与 `System.Windows.SystemParameters`。
- [x] CI `.github/workflows/ci.yml`：restore → build(Release, `-warnaserror`) → test(trx 上传)。
- [ ] CI 补两项：`dotnet format --verify-no-changes`；NuGet 缓存；`marketplace.yml` 骨架（`workflow_dispatch`，M5 启用）。
- [ ] **渲染 spike**（`spikes/avalonia-composition/`，2 天）：S1 透明圆角、S2 Acrylic 圆角裁剪、S3 120 Hz 形变、S4 命中/穿透/不抢焦点、S5 4 插件内存、S6 .NET 10 + Avalonia 12 构建。
- [ ] 产出 `docs/rewrite/spikes/2026-10-24-rendering-go-no-go.md`：逐项通过/失败 + 数值 + GO/NO-GO 结论。
- [ ] `docs/rewrite/DECISIONS.md` 建立，录入规范 D1–D12 的确认状态与 Q1–Q10 的 owner/截止。
- [ ] `README.md` 改写为 v2 状态说明（"2.0 开发中，1.x 见 `v1-final` 标签"），删除过度承诺（§F-35）。
- [ ] 环境清单写入 `docs/rewrite/dev-setup.md`：SDK 10.0.301、VS 2022 17.14+/Rider、Windows 10 19041/11、可选 120 Hz 显示器。

**触碰**：`FluidBar.sln`、`Directory.Packages.props`、`NuGet.config`、`src/FluidBar.App/*`、`src/FluidBar.PluginHost/*`、`src/FluidBar.Cli/*`、`tests/*/`（骨架）、`.github/workflows/*`、`spikes/*`、`docs/rewrite/*`、`README.md`。

**验收**：`dotnet build FluidBar.sln -c Release` 与 `dotnet test` 全绿（0 失败）；CI 首次运行通过；spike 文档给出明确 GO/NO-GO；Q1/Q2/Q3 有结论；`git tag v1-final` 存在。

**依赖**：无（关键路径）。**并行**：M0 由 Lead 独占共享文件；Core/Plugin/Platform/UI 四人在 M0 期间只做**只读调研 + 写测试草稿**（分支外草稿），不写 `src/**`，避免与骨架冲突。

---

## M1 — FluidBar.Core（Core owner）

**目标**：所有"可纯函数化"的东西一次做对：事件枢纽、活动状态机、布局、弹簧、设置、i18n 抽象。**不含任何 Win32/Avalonia 代码**。

**任务**

- [ ] `Events/`：`IslandEvent`(record struct)、`ActivityKey`、`IslandEventKind`、`IIslandEventHub` + `BoundedEventHub`（容量 256、`DropOldest`、按键合并 `Incremental`、计数器 `published/coalesced/dropped`）。
- [ ] `Activities/`：`ActivityRank`（14 类，规范 §2.3 表逐行落地）、`ActivityScheduler`（抢占/替换/背景层 2/队列 8/过期）、`StackState`、`ActivityExpiry`。
- [ ] `State/`：`IslandState`（不可变）、`IslandStateMachine`（T1–T12 表驱动）、`InputEvent`、`Effect`（数据枚举）、`EffectRunner` 接口。
- [ ] `Layout/`：`VisualRect`、`IslandLayout`（Shell/Content/Rail/HitRegion/Anchor）、`LayoutEngine`（3 模式 × 6 锚点 × 3 DPI）、`RailLayout`（48 宽、六项；设置/市场为标题栏动作，不占 rail）、`DpiPolicy`（DIP↔px、工作区钳制）。
- [ ] `Motion/`：`Spring`（定步长 1/300、dt clamp、squash 派生）、`SpringSet`（单循环 + 全静止短路）、`MotionTokens`（规范 §4.1 全表）、`MotionPolicy`（reduced-motion 折算）、`VelocityRetarget`、`SettleDetector`。
- [ ] `Settings/`：`SettingsV2` 模型（§8.2）、`SettingsMigrator`（v1→v2 映射表 + 报告）、`ISettingsStore` 抽象 + `AtomicJsonSettingsStore`（tmp→Flush→Replace→bak）、`SettingsSchema`（版本、默认值、校验）。
- [ ] `Localization/`：`ILocalizer`、`JsonCatalog`（解析/回退链）、`IcuFormatter`（复数/数字/日期子集）、`PluralRules`（CLDR 表）、`LocKeys` 源生成器（从 `en-US.json` 生成常量 + 编译期校验）。
- [ ] `Diagnostics/`：`ICounters`（规范 §9.3 计数器清单）、`ILog`（结构化字段）、`TimeProvider` 注入点。
- [ ] 测试：状态机全迁移（含非法迁移）、优先级/抢占/过期、布局快照（3×6×3×2 = 108 例）、弹簧收敛时间（10 个 token × ±20%）、速度重定向、迁移（4 个 v1 样本含损坏文件）、ICU（en/ru/ar/zh 各分支）、事件合并与背压。
- [ ] `docs/rewrite/research/clean-room-notes.md`：记录从 MIT/GPL 参考中"只取思想"的每一条。

**触碰**：`src/FluidBar.Core/**`、`tests/FluidBar.Core.Tests/**`（唯一写入者）。

**验收**：Core 测试 ≥ 180 且全绿；`dotnet test --filter Category=Layout` 输出 108 个快照用例；弹簧收敛实测与 `t₂%` 偏差 < 20%；v1 四个样本迁移 0 失败且报告项齐全；Core 工程 `dotnet list package` 除 BCL 外为空（或仅 `System.Collections.Immutable`）。

**依赖**：M0 骨架 + D1–D4 决策。**并行**：与 M2 完全并行（不同目录）；与 M3/M4 并行但 M3/M4 只消费接口桩。

---

## M2 — FluidBar.Sdk + 插件宿主 + manifest + 样例插件（Plugin owner）

**目标**：把插件边界钉死：SDK 冻结、manifest v2 + Schema、可回收 ALC、安装/更新/回滚、CLI 工具、两个样例插件。**不含市场 UI**。

**任务**

- [ ] `FluidBar.Sdk`：`IFluidBarPlugin`、`IPluginContext`、`IIslandPublisher`、`IPluginSettings`、`IPluginLocalization`、`IPluginLog`、`IPluginLease(s)`、`ICommandRegistry`、`INotificationSink`、`IHostDispatcher`、`LayoutDescriptor`、`IslandEvent`（迁入 SDK）、`SdkInfo.ApiVersion = 2.0.0`；**零依赖、零 WPF/Win32**。
- [ ] `FluidBar.PluginHost`：`PluginLoadContext`（TPA 共享 + `AssemblyDependencyResolver`）、`PluginHost`（load/start/stop/unload/超时/隔离计数）、`PluginHandle`、`ManifestV2` DTO + `ManifestValidator`（Schema + 跨字段规则）、`PackageVerifier`（SHA-256 → ECDSA P-256 → checksums）、`PackageReader`（zip-slip/大小/条目数防护）、`Installer`（暂存→原子激活→`state.json`）、`ContributionRegistry`（布局/设置页/命令/菜单/widget 注册与撤销）、`PluginLease`、`PluginLocalizer`、`PluginStateStore`。
- [ ] `FluidBar.Marketplace`（工程已建，引用 Sdk + Core）：`MarketIndex`/`MarketPlugin`/`MarketVersion` DTO、`IndexParser`（RFC 8785 验签前的规范化）、`VersionResolver`（兼容性过滤 + 通道 + staged rollout）、`InstallTransaction`（下载→校验→解压→暂存→激活→回滚的状态机，IO 经注入接口）。
- [ ] `FluidBar.Cli`：`fluidbar-plugin validate|pack|sign|policy|dev`；与 CI、客户端安装器共用同一校验实现；`pack` 产出确定性 `.fbp`（固定时间戳/排序/无多余字段）。
- [ ] `schemas/plugin.schema.json`（2020-12，`additionalProperties:false`）+ `plugin-locale` / `listing` / `signature` / `settings` / `marketplace-index`。
- [ ] 样例插件：`plugins/sample-hello`（最小：定时发事件 + 1 个设置页 + 1 个命令）、`plugins/sample-now-playing`（媒体布局 + 双语 locales + 图标 + 测试）。
- [ ] 测试：加载/卸载 100×（`WeakReference` + GC 循环）、zip-slip/超限/加密项、签名与哈希失败、清单非法（12 个负例）、初始化/启动超时、崩溃计数与禁用、回滚到 `lastGoodVersion`、确定性打包（两次 SHA-256 相同）、**`PublishSingleFile` 宿主内加载**。
- [ ] `docs/plugin/quickstart.md` 起稿（15 分钟：脚手架→运行→打包→PR）。

**触碰**：`src/FluidBar.Sdk/**`、`src/FluidBar.PluginHost/**`、`src/FluidBar.Marketplace/**`、`src/FluidBar.Cli/**`、`plugins/**`、`schemas/**`（与 Lead 共管，改动需 Lead 复核）、`tests/FluidBar.PluginHost.Tests/**`、`tests/FluidBar.Sdk.Tests/**`。

**验收**：100× 卸载的 ALC 回收率 ≥ 95%（不足则给出 `restartRequired` 数据与结论）；全部负例被拦截且错误信息可本地化；两个样例插件可在宿主中安装并产出岛事件；`.fbp` 两次构建字节一致；`PublishSingleFile` 下加载成功。

**依赖**：M0；SDK 形状需 Core owner 复核 `IslandEvent` 迁移（只读评审）。**并行**：与 M1 并行；与 M4 的接口约定在 M2 第 3 天冻结（`IIslandPublisher` 形状）。

---

## M3 — FluidBar.Platform.Windows（Platform owner）

**目标**：把 v1 所有"UI 线程上的系统访问"变成后台事件源，只发布合并后的不可变快照。**零 UI 引用**。

**任务**

- [ ] `Displays/`：`MonitorService`（`EnumDisplayMonitors` + work area + 热插拔 `WM_DISPLAYCHANGE`）、`DpiService`（PMv2、`GetDpiForMonitor`）、`FullscreenDetector`（`SHQueryUserNotificationState` + 前台窗口几何 + 豁免名单）。
- [ ] `Input/`：`HitTester`（解析 squircle 命中，供 `WM_NCHITTEST`）、`HotkeyService`（`RegisterHotKey` + 冲突上报）、`KeyboardHook`（`WH_KEYBOARD_LL`，仅岛可见时挂载）、`PointerRouter`（拖拽/橡皮筋/吸附状态）。
- [ ] `Media/`：`GsmTcService`（事件驱动：`MediaPropertiesChanged` / `PlaybackInfoChanged` / `TimelinePropertiesChanged`）、`MediaSessionCache`、`ArtworkCache`（`DecodePixelWidth=64`、字节 LRU 32 MB）、`LyricsService`（Kugou/NetEase provider 保留语义、后台 async、32 曲/4 MB 上限、单飞取消）、`TransportService`（GSMTC 优先 + 定向 `PostMessage`，**删除全局 `keybd_event`**）、`SourceBadge`（≤0.2 Hz，缓存按窗口标题）。
- [ ] `Audio/`：`VolumeService`（`IAudioEndpointVolumeCallback`，COM 缓存 + `ComScope`）、`BrightnessService`（专用 MTA 线程、2 s、DDC/CI 优先、WMI 回退、变化才发布）、`ProcessAudioGate`（WASAPI，明确释放 RCW）。
- [ ] `Clipboard/`：`ClipboardService`（宿主窗口 `AddClipboardFormatListener`，文本/图片缩略，去重与上限）。
- [ ] `Notifications/`：`NotificationService`（`NotificationChanged` + 水位线 + 环形缓冲）、`VerificationCode`（6 位码识别）。
- [ ] `Agent/`：`AgentHookService`（`FileSystemWatcher` + 保留 100 条）、`HookInstaller`（**显式安装**：备份 + 合并 + 文件锁，永不定时自愈）。
- [ ] `Tray/`、`Update/`、`Shell/`：`TrayService`（`Shell_NotifyIcon` + 本地化菜单）、`UpdateService`（单例 `HttpClient`、ETag、SHA-256 + Authenticode 校验、显式安装）、`TopmostGuard` + `ShellWhitelist`（规范 §5.6 类名白名单）、`SessionWatcher`（锁屏/切换用户）。
- [ ] `Interop/`：CsWin32 生成的 P/Invoke 集中入口；`ComScope`（`ReleaseComObject` 的 `finally` 包装）；`SetDefaultDllDirectories`。
- [ ] `Scheduling/`：`WorkerScheduler`（命名节奏、会话锁暂停）、`Coalescer`（进度/位置类事件合并）。
- [ ] 测试：显示器/工作区/DPI 变更重排、`WM_NCHITTEST` 矩阵（每模式 200 点）、COM 释放（弱引用 + GC）、热键冲突、GSMTC 会话切换、通知水位线、更新校验失败路径。
- [ ] 性能：空闲 10 min 采样（CPU/线程数/定时器数）写入 PR。

**触碰**：`src/FluidBar.Platform.Windows/**`、`tests/FluidBar.Platform.Tests/**`。

**验收**：空闲态除"≥1 s 且变化才发布"的轮询外无定时器（列表随 PR 提交）；UI 线程 I/O 静态扫描 0 命中；24 h 句柄增长 < 60；媒体换曲到事件发布 < 300 ms；亮度/音量查询不再出现在 UI 线程（ETW/调用栈证据）。

**依赖**：M1 的事件契约与 `WorkerScheduler` 接口（M1 第 5 天可用桩）；M0 的 `net10.0-windows` 目标框架。**并行**：与 M1/M2 并行；与 M4 通过 `IIslandEventHub` 对接，M3 不写任何 UI。

---

## M4 — FluidBar.App：Avalonia 外壳（UI owner）

**目标**：把规范 §2/§3/§4 变成可点的东西：固定窗口 + 组合器宿主、岛与弹簧循环、rail 六页、HUD、dock、设置、i18n 全量、市场 UI。

**任务**

- [ ] `Hosting/`：`IslandWindow`（`WS_POPUP|NOREDIRECTIONBITMAP|TOOLWINDOW|NOACTIVATE`、`488×332`、每显示器一个）、`WindowFactory`、`CompositionHost`（`TransparencyLevelHint=Transparent` + `Win32CompositionMode=WinUIComposition` + backdrop corner radius）、`HitTestHook`（`WM_NCHITTEST`→`HTTRANSPARENT`）、`MouseActivateHook`（`MA_NOACTIVATE`）、`TopmostGuardHost`（≤1 Hz + 白名单）。
- [ ] `Rendering/`：`FrameClock`（组合器帧回调优先，`DispatcherTimer(Render)` 退化）、`SpringSet` 驱动 + 收敛即停、`ShellGeometry`/`SquirclePath`（超椭圆 + 缓存）、`ShadowNineSlice`、`BackdropService`（Tier 0/1/2 探测与降级）、`IslandShellVisual`（一次 `InvalidateVisual`）。
- [ ] `Controls/`：`Rail`（48 宽、六项、34/36 命中、2×16 强调条、键盘导航）、`HudRow`（40 高单行：icon+label+track+value）、`TransportBar`、`ProgressTrack`（可拖拽 seek）、`ListRow`、`Badge`、`FocusRing`。
- [ ] `Pages/`：Home/Media/Notifications/Clipboard/Agent/Widgets —— 全部统一 440×284 外壳（内容宽 392）；页面切换只做内容交叉淡入（不 resize、不重排、不丢焦点、220 ms）。
- [ ] `Dock/`：`DockStrip`（52 高、8 瓦片 + 托盘簇、向上展开、`ABM_SETSTATE` 可选自动隐藏并在退出恢复）。
- [ ] `Settings/`：七页（通用/外观/灵动岛/插件/市场/高级/关于），外部预览岛、声明式生成（从 `SettingsV2` 注解），首开 < 250 ms。
- [ ] `Localization/`：`LocalizationService`（运行时切换 + `Version` 抬升）、`LocExtension`、`en-US.json`/`zh-Hans.json`（全量 key）、`qps-ploc` 生成、字体栈与 RTL 开关。
- [ ] `Market/`：列表/详情/安装同意/进度/版本历史/回滚/卸载/更新检查（消费 `FluidBar.Marketplace` 的领域模型 + `PluginHost` 的安装器，不含签名逻辑）。
- [ ] `Diagnostics/`：诊断页（计数器）、`CrashGuard`（连续 2 次崩溃进安全模式）、日志查看/导出诊断包。
- [ ] 测试：头less 渲染快照（六页 × 明暗 × 双语 × 三 DPI）、`WM_NCHITTEST` 集成、运行时切语言断言（绑定文字变化且窗口不重建）、HUD 数值格式（含 fr 空格/百分比）。

**触碰**：`src/FluidBar.App/**`、`tests/FluidBar.Ui.Tests/**`。

**验收**：120 fps p99 < 8.33 ms（或按 M0 结论走 WPF 回退并达 60 fps）；点透矩阵通过（透明角 20 次点击全穿透、点药丸不改前台）；`zh↔en` 全量即时切换；`qps-ploc` 无裁切；六页统一 440×284、切换不改变外壳尺寸（实测 ±1 DIP）；首帧输入延迟 ≤ 2 帧。

**依赖**：M0 的 GO/NO-GO（Q1）；M1 的 `IslandState`/`Layout`/`MotionTokens`；M3 的事件源（M3 未完成时用 M1 的录制回放夹具）。**并行**：与 M2 并行；`Rail`/`HudRow`/`Pages` 可由 UI owner 内部按页拆分串行推进（同目录，避免冲突）。

---

## M5 — 内置插件迁移 + 市场流水线 + 文档（Plugin owner 主责，UI/Platform 配合）

**目标**：闭合"PR 合并 → 10 分钟内可见 → 客户端可装/更/滚/卸"的环路，并把 4 个内置能力搬到统一贡献模型。

**任务**

- [ ] 迁移 `media` / `clipboard` / `notifications` / `agent` 为 `FluidBar.Plugins.*`（编译期内置，但走同一 `ContributionRegistry` 与 manifest 元数据）；删除 `catalog.json` 谎言（`NotificationsPlugin.cs` 不存在，§F-29）。
- [ ] 签名索引：`tools/marketplace-gen`（确定性排序/键序、`generatedAt` 取源提交时间）+ `index.json` + `plugins/<id>.json` + `listings/<id>/<locale>.json` + `keys.json`。
- [ ] 发布流水线 `marketplace.yml`：更改检测 → build/test/pack → 确定性校验 → ECDSA 签名（Key Vault/OIDC 或离线密钥文件占位，按 Q7）→ Release 上传 → 索引重建+签名 → Pages 部署 → 发布后回验。
- [ ] 客户端链路打通：索引 ETag 缓存、bootstrap 内置索引、raw 回退、离线 `.fbp` 安装（未签名强提示）、`blocked`/`yanked` 状态、3 次崩溃自动回滚。
- [ ] 回滚/封禁演练：yank 一个版本 → 客户端 10 min 内不再提供；block 一个插件 → 已安装实例被禁用并给理由。
- [ ] 文档：`docs/plugin/{manifest,contributions,security,localization,testing,publishing}.md`、`docs/marketplace/{architecture,review-policy}.md`、PR 模板与评审清单（单人版）。
- [ ] 首个第三方（或第二个维护者账号的）插件端到端演练，记录耗时。

**触碰**：`src/FluidBar.Plugins.*/**`、`plugins/**`、`marketplace/**`、`tools/marketplace-gen/**`、`docs/plugin/**`、`docs/marketplace/**`、`.github/workflows/marketplace.yml`、`schemas/marketplace-*.json`。

**验收**：合并到索引可见 < 10 min（p95）；公网安装到 `Active` < 60 s；回滚与封禁演练通过；内置插件与社区插件在设置页同一列表、同一能力展示；`docs/plugin/quickstart.md` 让新人 15 分钟跑通样例。

**依赖**：M2（加载器/CLI）、M4（市场 UI 骨架）。**并行**：UI 只提供市场页容器，签名/索引由 Plugin owner 独立完成；Platform 只需在安装进度事件上暴露 `IslandEventKind.Progress`。

---

## M6 — 加固 / 浸泡 / 性能 / QA / 发布（全员，Lead 收口）

**目标**：从"功能完成"到"可以发给陌生人"。

**任务**

- [ ] 性能收敛：帧预算、分配、GC、句柄、启动时间逐项对照规范 §9.1，超标的写进 `DECISIONS.md` 并给出取舍。
- [ ] `tests/FluidBar.Soak`：1 h 场景（随机活动 3 s/次、展开收起 30 s/次、媒体 1 Hz、插件 tick、市场检查），断言句柄 < 900、工作集 < 150 MB、堆增长 < 8 MB/h、p99 帧达标。
- [ ] 崩溃护栏与安全模式验证：注入异常 → 记录 + 托盘提示 + 不退出；连续两次崩溃 → 安全模式。
- [ ] 诊断包导出（脱敏）与支持流程文档。
- [ ] 安装器：`-single-file` + Authenticode 签名 + 卸载项；就地升级 vs 并列安装按 Q10 结论实现；`v1` 数据迁移在真实 1.0.1 机器上验证。
- [ ] 手工 QA 清单（规范 §9.4）全量执行，记录设备矩阵（Win10 19041、Win11 23H2/24H2、100/125/150/200%、双屏/三屏、核显/独显、RDP）。
- [ ] 无障碍：键盘全流程、屏幕阅读器朗读、高对比主题、减少动效。
- [ ] 发布物料：`CHANGELOG.md`、Release notes（中英）、隐私说明（无遥测）、许可与第三方声明、`v2.0.0` 标签。
- [ ] 发布后 72 h 观察：崩溃率、插件安装失败率、市场可达性（CN/非 CN 各一次）。

**触碰**：`tests/FluidBar.Soak/**`、`src/**`（仅缺陷修复，按 owner 范围）、`installer/**`、`docs/**`、`.github/workflows/release.yml`。

**验收**：规范 §9.1 全表达标（或书面豁免 + 影响说明）；QA 清单 100% 执行且无 P0/P1 未决；RC2 连续运行 8 h 无崩溃；发布包签名可验证、干净虚拟机安装成功；`v2.0.0` 标签与 Release 产物齐全。

**依赖**：M1–M5 全部完成。**并行**：四人按"性能 / 插件与市场 / 平台与安装器 / UI 与无障碍"分区并行，Lead 负责发布链路与最终签字。

---

## 7. 关键路径、检查点与风险闸门

```
M0 ──┬── M1 ──┐
     ├── M2 ──┼── M5 ── M6 ── release
     ├── M3 ──┤
     └── M4 ──┘
关键路径：M0(spike) → M4(外壳) → M6
```

| 检查点 | 日期 | 闸门（不通过即调整计划，不降低标准） |
|---|---|---|
| G0 渲染结论 | 2026-10-24 | S1/S3 任一失败 → 立即切 WPF 回退（规范 §5.7），M4 工期 +1 周 |
| G1 Core 冻结 | 2026-11-07 | `IslandState`/`Layout`/`MotionTokens` 公开面冻结；此后变更需 Lead 批准 |
| G2 SDK 冻结 | 2026-11-07 | `apiVersion 2.0.0` 冻结；样例插件回归通过 |
| G3 平台事件驱动 | 2026-11-21 | 空闲定时器清单 = 0 违规；否则 M3 延期 3 天 |
| G4 外壳达标 | 2026-11-28 | 120 fps（或回退 60 fps）+ 点透矩阵 + 双语切换三项全过 |
| G5 市场闭环 | 2026-12-12 | 合并→可见 < 10 min；安装 < 60 s；回滚演练通过 |
| G6 发布 | 2027-01-09 | §9.1 全表 + QA 清单 + RC2 8 h |

## 8. 每日 / 每周节奏

- **每日**：09:30 站会（15 min，只报"昨天完成 / 今天做 / 阻塞"）；18:00 Lead 合并 + 全量构建测试；主干红 > 4 h 由 Lead 回滚。
- **每周五**：可运行快照 + 15 min 演示 + 规范/DECISIONS 差异复核。
- **每两周**：性能基线复测（帧预算、工作集、启动时间），数据入 `docs/rewrite/perf-log.md`。

## 9. 提交与评审约定

- 提交信息：`<area>: <imperative summary>`（area ∈ core/sdk/host/platform/app/plugins/market/docs/ci）。
- 每个 PR 必须包含：改了什么、怎么验证（命令 + 输出摘要）、影响了哪个规范章节、是否新增依赖（许可）。
- 评审：默认 1 人（当前仅 Doulor）；涉及 `schemas/**`、`Directory.*.props`、`FluidBar.sln`、架构测试的 PR 必须 Lead 评审。
- 禁止：注释掉的代码、`TODO` 无 issue 号、`catch { }`、`Thread.Sleep`、`.Result`、未本地化的字符串。

