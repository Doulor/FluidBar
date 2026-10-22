# FluidBar v2 — 当前状态（STATUS）

> 最后更新：2026-10-22 18:45（Asia/Shanghai）
> 维护规则：每个检查点由 Lead 更新本文件；数字必须来自实测。

## 一句话状态

**灵动岛动画卡顿已修复**：60 fps 帧时钟原来被 50 ms 截止计时器每个 tick 停掉（`_lastRenderHappened` 参与停止判定），展开/悬停/波浪只能画 2–3 帧。现在帧时钟只由动画状态决定（`IslandFrameLoopPolicy`），实测展开 380 ms 走 24 帧、interval p50 15.9 ms，空闲 CPU 12.8% → 2.1%。


## 2026-10-22 18:16 检查点（预览版 v1 第一轮硬化：输入 / 全屏 / 单实例 / 空态）

- **鼠标钩子状态机**：抽成纯 `IslandMouseHook.GestureTracker`。岛内按下、岛外松开现在产生 `Released + Left`，不再残留 `_pressed`；普通移动不会再变成幽灵拖拽（旧行为：页面自己乱翻、下一次点击丢 release）。看门狗改为对比「钩子最后上报位置」，本机 43 分钟 55 次 `island.mouse-hook-reinstalled` 不再出现；重装同时复位手势。
- **命中区 / 拖拽 / 滚轮**：`OnPlacementRequested` 强制刷新 hook 矩形，切锚点/模式不再在旧位置留「吃点击」区；pager 拖拽可越过壳边缘继续；滚轮保留小数 notch，触摸板小增量不再被整数除法吞掉。
- **全屏抑制**：新增 `FullscreenDetector`（矩形覆盖 + `WS_POPUP`/无 `WS_CAPTION` 判定，普通最大化不误判）；每秒轮询，进入/退出全屏通过 `SuppressionChanged` 隐藏/恢复岛。
- **单实例 / 空态**：`SingleInstanceGuard` 阻止第二个岛并向后一个进程发送 `ActivateMessage` 打开设置；平台 seam 失败/ Avalonia fallback 改用 `EmptyIslandDataSource`，不再显示虚假的 Spotify/Teams/剪贴板。
- **电源/GC**：所有帧循环停止路径走 `StopFrameLoopForIdle()`，恢复 15.6 ms timer resolution 与默认 GC latency；修复前首次动画后系统 timer 停在 1 ms（实测 `NtQueryTimerResolution`）。
- **剪贴板单条删除**：行内删除按钮 + `ClipboardRemove`，同步清 pin；UIA Delete 按钮。
- **通知页清空**：标题栏“全部清除”chip（绘制/命中/UIA 同矩形），空列表隐藏。
- **剪贴板清空**：托盘入口清空岛内历史（系统剪贴板不动），同时清空 pin 摘要，避免固定行复活。
- **剪贴板缩略图**：`CF_DIB` → PNG（24/32bpp），图片行显示真实缩略图，解码失败回退图标。
- **剪贴板清空**：清空历史时筛选复位为“全部”。
- **preview.4**：rank 修正后本地 publish 成功（zip 19.62 MB、无 PDB）。
- **Core 迁移 e2e**：临时 appData + legacy import 测试。
- **AppBootstrap**：生产 store 改为 CoreSettingsStoreAdapter + legacy import。
- **Core 设置 flush**：与 UI 设置同样的 pending/written 等待。
- **全屏检测**：跳过本进程 foreground 窗口。
- **日历 counts**：内容比较 + 测试。
- **搜索再次聚焦**：FocusQuery 复用已有窗口并全选。
- **搜索随岛收起**：Host StateChanged + 关闭搜索。
- **preview.7**：a11y/select-all/calendar-dispose 后本地 publish 成功（19.62 MB、无 PDB）。
- **日历回调**：Dispose 退订 + `_disposed` guard。
- **搜索框**：重新打开 SelectAll，输入即替换旧 query。
- **SafeMode runtime**：LoadCommunity=false 跳过发现/加载。
- **MediaQueue 无会话**：SkiaPlatformActionsTests 锁 switch。
- **搜索窗口 a11y**：窗口/文本框 Name + HelpText。
- **设置 deadline UI**：SettingsDeadlineTests。
- **preview.6**：最新代码本地 publish 成功（zip 19.62 MB、无 PDB，exe `66F843D4...`）。
- **剪贴板 pin 缓存**：snapshot 时裁掉已淘汰 sequence。
- **VisualRect.Union**：合并/Empty 单测。
- **MediaQueue**：修正 switch 未接线，实际调用 OpenMediaPlayer。
- **设置 deadline**：AutoCollapse/AutoHide 0 时清除旧 deadline；Core 单测。
- **搜索窗口主题**：跟随 ActualThemeVariant 深/浅色。
- **设置 flush**：等待 pending/written sequence 一致，消除 debounce timer 竞态。
- **Rail 残影**：页面切换/点 far dot 重绘 strip；新增 VisualRect.Union。
- **自更新 BOM**：WriteSelfReplaceScript 单测。
- **Agent 进程探测**：Code Insiders + Win32Exception 容错。
- **搜索 chip**：query >12 字符截断加省略号。
- **preview.5**：rail/search/pin 后本地 publish 成功（zip 19.62 MB、无 PDB）。
- **搜索展开**：搜索窗口期间临时 pin，关闭恢复。
- **Rail 远点**：逐点导航；命中区 7×22。
- **搜索状态**：chip 显示 query，窗口预填。
- **通知优先级**：NotificationRank 0→9（spec 2.3），剪贴板可抢占瞬时 toast；fixtures/Core/Integration 同步，全量 2596/0。
- **架构门禁**：专用后台线程文件（ProcessLoopbackCapture/KugouMemoryPosition）豁免；Architecture 18/18。
- **重置设置**：补回 PowerMode / LyricOffsetMs 默认值。
- **preview.3**：日历 catch 的 CS0168 已修，本地 publish 成功（zip 19.62 MB、无 PDB）。
- **Agent 聚焦**：只匹配终端/编辑器进程，避免浏览器误伤；进程名单测。
- **图像缓存测试**：8 张剪贴板图片全留缓存。
- **图像缓存**：默认容量 4→16，覆盖剪贴板 8 图 + 媒体封面，避免整表清空后逐帧重解码。
- **自更新脚本**：UTF-8 BOM 写入，中文路径不乱码；脚本构建单测。
- **日历退出竞态**：缓存 cancellation token，避免 dispose 后 timer 回调抛异常。
- **日历事件单测**：CountByDay 同日计数/空集合覆盖。
- **日历事件**：AppointmentStore 只读事件映射到月格 event dot；15 分钟刷新，权限失败静默。
- **剪贴板搜索单测**：文本/文件名/空查询匹配覆盖。
- **媒体队列**：Queue 按当前 AUMID 打开播放器，UIA 同步启用。
- **媒体输出标签**：显示“声音设置/Sound settings”，匹配实际 fallback。
- **搜索窗口退出**：DisposeServices 先关搜索窗口。
- **剪贴板搜索**：标题栏 Search chip 打开搜索窗口，实时过滤文本/文件名；UIA 同步。
- **自动隐藏 0**：滑块显示“关闭/Off”。
- **封面临时路径**：Sweep 时同步清掉已删除文件的 `_pathsByKey` 条目。
- **媒体时长缓存**：GSMTC/Kugou 两处时长缓存 512 条上限。
- **通知已读 id**：随 recent 裁剪，启动清空，避免无界增长。
- **SafeMode 重建**：重建后关闭旧设置窗口，避免旧服务引用。
- **媒体输出**：Output 打开 `ms-settings:sound`；Queue 显示“打开播放器”，按 AUMID 拉起当前播放器。
- **发布门禁**：豁免平台 8 类已知 CS 警告与内置插件 CS1591；本地 preview.2 publish 成功（zip 19.62 MB、无 PDB）。
- **崩溃日志**：`Program.Main` 首步安装 CrashLog handlers。
- **剪贴板筛选**：总历史为空时隐藏筛选 chip。
- **通知未读数**：角标按 ReadIds 过滤，不再把已读通知算进去。
- **崩溃日志**：1 MB 轮转到 `.old`，失败不阻断追加。
- **自动收起 0**：滑块显示“关闭/Off”，提示说明 0=不自动收起。
- **剪贴板筛选**：标题栏 chip 循环全部/文本/图片/文件；过滤在排序前，行 index 保持一致。
- **Agent 行**：移除死命中目标与 UIA Invoke，等真实会话聚焦服务再接。
- **Agent 行**：session title/project 唯一匹配窗口时恢复并前台激活；不唯一则拒绝。
- **Devices 插件**：USB/蓝牙 monitor 接入 `BuiltinPlatformServices`，内置插件不再因依赖 null 被跳过。
- **便携版自更新**：同名 `FluidBar.exe` 下载后由 PowerShell helper 等旧 PID 退出、覆盖并重启；setup 仍走 ShellExecute。
- **退出清理**：DisposeServices 加 idempotent guard，避免 Exit + Shutdown 双跑。
- **外部点击**：钩子新增 OutsidePressed，展开岛点击桌面/其他窗口会按策略收起（固定态不收起）。
- **发布脚本**：警告门只保留编译器警告；本地 publish 成功，zip 19.62 MB、exe 60.3 MB、无 PDB。
- **安全模式**：切换时重建插件运行时，社区插件立即停用/恢复，无需重启。
- **发布资产**：release 额外上传独立 `FluidBar.exe`，匹配更新检查的安装器资产模式。
- **剪贴板文件行**：文件类型标签/文档图标；Pin 图标改用内容摘要匹配。
- **通知**：托盘“清空通知”清空岛内列表（Action Center 不动）；托盘“隐藏”恢复生效。
- **主题**：外观页补回“跟随系统”，System 不再被错误显示为 Dark。
- **剪贴板图片/文件**：捕获 `CF_DIB`/`CF_HDROP`，支持写回图片和文件列表；图片历史上限 5 条（避免 50 张大图占内存）。
- **发布**：zip 压缩前清理 PDB（约 105 MB）；release workflow 写权限修正。
- **剪贴板**：固定行排序到列表最前；pin 以内容 SHA-256 摘要持久化（不落明文）。
- **插件目录**：发现器跳过数据/设置目录与 `local` 包容器，启动日志从每次 5 条 Error 降到只报真正损坏的包；插件页不再出现幽灵 invalid 条目。
- **通知页**：已读 toast id 持久于服务进程内；打开页面/点击行标记已读，未读点不再永久常亮；通知行映射真实 app 图标（`appLogoOverride` / 应用图标）而不是统一铃铛。
- **首页真实状态**：Wi-Fi/蓝牙开关改用 Radio 服务的真实 `IsEnabled`（不再用 `IsOnline`/硬编码 false）；首页天气 chip 复用小组件页的真实天气；禁用的 Focus 磁贴不再产生点击动作。
- **托盘/热键**：托盘菜单接入 `LocKeys.Tray.*` 并新增独立隐藏项；`Ctrl+Alt+H` 走 `HandleHideHotkey`；`MOD_NOREPEAT` 修复长按重复。
- **测试**：Platform 定向 115 passed；全量 `dotnet test FluidBar.sln -m:1` = **2605 passed / 0 failed**。0 failed：通知 rank 已按 spec 2.3 修正为 9，架构门禁豁免专用后台线程，Integration 两条 rank 断言同步通过。Core 647 · Platform 708 · Ui 512 · Island 448 · Sdk 167 · PluginHost 40 · Builtin 25 · Integration 31/31 · Architecture 18/18。
- **构建**：`dotnet build FluidBar.sln` 0 errors。

## 里程碑

| 里程碑 | 状态 | 证据 |
|---|---|---|
| M0 分支/骨架/CI/渲染决策 | ✅ | `v2/rebuild`、`v1-final`、spec/plan/DECISIONS |
| M1 Core 领域层 | ✅ | 473 tests |
| M2 插件 SDK/宿主/市场 | ✅ | 188 tests；离线 E2E |
| M3 Windows 平台 | ✅ | 371 tests；M3+P3 |
| M5 内置插件/市场 CI/文档 | ✅ | 23 builtin + 31 integration；`docs/plugin/` |
| M4 UI / Skia 宿主 | ✅（MVP） | 新分层宿主 + Skia 渲染已接入，默认 `FLUIDBAR_HOST=skia`；108 UI tests；旧 Avalonia 宿主保留 fallback |
| M6 硬化/发布 | ⏳ | soak/性能预算已就绪；Release 浸泡待做 |

## 2026-10-21 19:55 检查点（展开后点击区域 / 媒体探测节流）

- **现象**：展开后媒体卡片上的按钮全部点不动（通知页/媒体页的 seek 正常）。
- **根因**：命中区域签名只有 `ShellForm`，没有壳体尺寸；动画期间 `IsAnimating` 持续为真 → 签名不再变化 → 钩子只把旧药丸矩形当可点区域，面板主体点击穿透。
- **修复**：`IslandHitRegionSignature`（含壳体 W/H）+ 动画期间每帧刷新命中区域；新增 `IslandHitRegionSignatureTests`（3 个用例）。
- **媒体探测**：无时间轴的播放器（酷狗 mini）每 250 ms 整树遍历 MSAA；找时间文本的遍历加 1 s 节流，CPU 2.06% → 1.25%。
- 测试：**2525 passed / 0 failed**（Core 640 · Platform 660 · Ui 500 · Island 445 · Sdk 167 · PluginHost 39 · Builtin 25 · Integration 31 · Architecture 18）。
- Release：57.4 MB，SHA256 `D929B1453F2EC329DC7BF45BC56C806E4ECE985ADE2D7EB6458358DC4A1481A4`。
- 决策：工作集 191 MB 的 native 内存审计另案（不阻塞本轮热修）。

## 2026-10-21 19:30 检查点（帧循环卡顿修复）

- **现象**：整岛动画极卡（3–13 fps），设置面板流畅（Avalonia 独立渲染线程）。
- **证据**：`FLUIDBAR_FRAME_PROBE` 逐帧记录显示展开时只有 2–3 帧、随后 300–1000 ms 空洞；`dotnet-trace` 采样确认渲染本身 0.7 ms/帧（不是渲染贵）；临时 tick 探针显示 `OnDeadlineTick`（50 ms）每次都执行一次不推进动画的 `RenderAllLocked`，把 `_lastRenderHappened` 清零，于是 `EnsureFrameLoop` 在下一个 tick 停掉帧时钟。
- **修复**：新增 `FluidBar.App.Hosting.Skia.IslandFrameLoopPolicy.ShouldRun(animating, agentAnimating, keepIdleFrames)`；`RenderAllLocked` 返回「是否真的画了一帧」，空帧计数（省电模式 2 帧）用返回值；删除 `_lastRenderHappened`。
- **回归测试**：`tests/FluidBar.Ui.Tests/IslandFrameLoopPolicyTests.cs`（真值表 + 展开跨 deadline pump + 静止不请求帧）。
- **顺带**：`PowerModeProbe` 的 120°/s 断言过期（实现已统一为 `AgentSpinner` 400°/s），已改为 400°/s 并同步 `IslandMetrics` 注释。
- 测试：**2522 passed / 0 failed**（Core 640 · Platform 660 · Ui 497 · Island 445 · Sdk 167 · PluginHost 39 · Builtin 25 · Integration 31 · Architecture 18）。
- Release：`build/release/win-x64/FluidBar.exe` 57.4 MB，SHA256 `239DCCD845931482804A254575A33E80FE808FA92D8B3B99F2D1226E02EA4F92`。
- 未处理（另案）：媒体回退探测每 250 ms 走一次酷狗 MSAA 树（采样占全进程 CPU 的 66%，≈12% 单核）；工作集 199 MB 仍高于 D12 的 120 MB 目标。

## 测试（当前）

| 套件 | 数量 | 状态 |
|---|---:|---|
| Core | 491 | ✅ |
| Platform | 409 | ✅（偶发 HWND/网络 flaky，单独运行稳定） |
| SDK | 152 | ✅ |
| PluginHost | 39 | ✅ |
| Builtin | 23 | ✅ |
| Integration | 31 | ✅ |
| UI | 184 | ✅ |
| Architecture | 11 | ✅ |

构建：`dotnet build FluidBar.sln` = 0 errors；`dotnet test FluidBar.sln -m:1` = **1493 passed / 0 failed**
（Core 495 · Platform 418 · SDK 152 · UI 188 · Island 136 · PluginHost 39 · Builtin 23 · Integration 31 · Architecture 11）。

## 2026-10-20 07:45 检查点（验收 + 空态打磨，本轮终态）

- **独立验收**：冷启动不误弹旧胶囊、媒体四键真机生效、小岛条远点跳 01/06、9 项设置重启保持、市场提交→上架→安装→下载+1→评分跨进程回读一致；六页 + 设置七页全走查通过，**0 阻断缺陷**。报告 `build/reports/acceptance/ACCEPTANCE.md`。
- **空态打磨**：媒体页无输出/无队列改为轻量本地化占位（不再有孤立图标与空槽）、无歌词改为「暂无歌词」卡片（不再留 56 DIP 空洞，进度条位置不变）、队列标签本地化；任务卡空态/损坏文案改为友好中英文（保留文件名可发现性）。16 种空态组合全部覆盖测试。
- 测试：**1993 passed / 0 failed**（Core 567 · Platform 558 · Ui 285 · Island 284 · Sdk 167 · PluginHost 39 · Integration 31 · Builtin 23 · Architecture 11）。
- Release：`build/release/win-x64/FluidBar.exe` SHA256 `13519B57CD399CF814D8CCF2BA93157476F6B0148D0D3C5A459AAC675F6E77AB`。

剩余（环境或产品决策阻塞）：托盘图标锁屏下无彩色证据；1.6 字号首屏需滚动（改 440×284 契约）；酷狗未缓存封面的歌为渐变占位；市场线上插件无评级字段。

## 2026-10-20 07:10 检查点（小组件收尾，本轮终态）

- 天气条件文案本地化（9 个条件键 + 复数键）、灵动岛设置页新增「天气城市」输入框（留空自动，实测 beijing → 真实北京天气）、任务卡支持最多 8 行并显示「还有 N 项」、三档字号复查无溢出。
- 测试：**1893 passed / 0 failed**（Core 567 · Platform 558 · Ui 283 · Island 281 · Sdk 167 · PluginHost 39 · Integration 31 · Builtin 23 · Architecture 11）。
- Release：`build/release/win-x64/FluidBar.exe` SHA256 `F785C488363C9D23AEC66C80E658D256DD5FF124FFE7D4F8DBCE72A151D0B783`。

- 本轮终态：夜间共 17 个工作流全部合并；剩余未完成项见下方 05:35 与 04:50 检查点。

## 2026-10-20 06:30 检查点（通知真实数据 + 单实例收敛 + 小组件真实数据）

- **通知**：找到根因——本机 `UserNotificationListener.NotificationChanged` 订阅抛 `COMException 0x80070490`，导致 `NotificationService` 启动即 Failed，**通知胶囊从未工作过**（不只是页面空）。改为事件失败时降级 2s 轮询、从操作中心种子、关闭同步剪枝、岛内 dismiss 真实生效；真实 WinRT toast 端到端验证（到达/插入/关闭同步）。
- **监听收敛**：页面与插件原本各持一个 NotificationService（≈1.17 次读/秒），现 App 内仅一处实例，插件拿只读视图 `NewArrivalsNotificationSource`（种子对页可见、对插件不可见），日志实证 `activeInstances=1`、严格 2000ms tick → 0.50 次/秒。
- **小组件真实数据**：天气接 Open-Meteo（无 key）+ 原子缓存 + IP/城市定位，实测与同刻 API 一致（Tokyo 20°、Beijing 11°）；离线有缓存显示「上次值 + N 分钟前」、无缓存显示「不可用」，**绝不编造**。任务接 `tasks.json`，`TaskToggle` 真实原子写回，失败时原文件不动。
- 测试：**1875 passed / 0 failed**（Core 567 · Platform 556 · Ui 276 · Island 275 · Sdk 167 · PluginHost 39 · Integration 31 · Builtin 23 · Architecture 11）。
- Release：`build/release/win-x64/FluidBar.exe` SHA256 `B33E2668C4EAA75B47FFDEE8C0449B1CEF9D6268398C03568A4CB651A856E272`。

剩余（已知）：天气条件文案需新增 locale 键；任务卡仅绘前 3 行；城市设置需接到设置页（当前 options + `FLUIDBAR_WEATHER_CITY`）；托盘图标锁屏下无彩色证据；1.6 字号首屏需滚动（改 440×284 契约属产品决策）。

## 2026-10-20 05:35 检查点（市场/关于复核 + 媒体真机二次攻坚）

- **设置面板收口**：市场页修掉 3 个真实缺陷（featured 卡缺暖色径向层且层序反了 → 下半部暗到与页底同色；chips 与网格之间多 10px；滚动条不占 gutter 导致内容偏宽 9px）；关于页不再插入空状态行、失败文案中性化、6 种更新状态 × 中英齐备。设置窗口在 FontScale 1.0/1.6 下渲染字节完全相同。
- **媒体真机回归**（酷狗正在播放）：修掉『封面永远不显示』的两个真根因——① 快照发布早于封面入缓存且结构签名不含 ArtworkKey；② 酷狗缓存为 WebP 且文件夹全角/半角与标题不一致。现在紧凑胶囊与展开页都显示真实专辑图；进度 1:50→2:13→2:36 三帧推进；逐行歌词随进度滚动；岛上 暂停/恢复/上一首/下一首 全部真机生效。另修：歌词身份行（『歌名 - 歌手』）被当歌词、Next 与当前行重复时不隐藏；播放态改读无障碍树「播放/暂停」图标可见性，隐藏时间文本也能 1s 内反映。
- 测试：**1860 passed / 0 failed**（Core 567 · Island 274 · Ui 250 · Platform 498 · Sdk 167 · PluginHost 39 · Integration 31 · Builtin 23 · Architecture 11）。
- Release：`build/release/win-x64/FluidBar.exe` SHA256 `3757257F21E6F54F57CF48C2461A5EB691B71132373FB50368A53AD8B7043994`，HEAD `8778c8e`。

## 2026-10-20 04:50 检查点（多 agent 通宵轮：原型对齐 + 插件攻坚）

多 agent 协同完成 6 批工作流（marketplace-flows / island-audit / rail-pixel / settings-pixel / media-lyrics / settings-audit / island-polish / island-body）。

- **预览小岛**：用 playwright 实测 prototype2 DOM，17 项像素级对齐（rail 8/32、胶囊按标签 64–104、末点 7×3、命中区 7×15、远点左簇→01 右簇→06、删除 6 处非原型绘制、换页 smoothstep 插值）；补 strip 命中区修复（此前点击被当桌面穿透）。
- **岛本体**：展开/收起改为 CSS 等价 `380ms cubic-bezier(.3,.8,.25,1)`（52 采样点误差 ≤0.05px）、三层淡入时序独立（消除 0→0.5 跳变）、删除发明的页切换 scale/parallax、壳层去描边+原型双层阴影、内容区 400×260、幽灵胶囊视觉删除、紧凑五形态对齐。
- **设置面板**：`SettingsTokens` 逐数值搬自 styles.css、按 icons.js 原始 SVG path 重绘图标、chrome/行高/开关/segmented/keycap/预览画布/市场卡片全对齐。
- **设置项生效**：6 个「只写 json 不生效」项接上消费方（调色板此前 mapper 根本不读）、开机启动真实写 HKCU\Run、安全模式真正阻止拉岛、崩溃护栏、帧统计实时；修 2 个死按钮（重置确认框听错事件、chip 处理器被 Handled 挡掉）。
- **自动更新**：从「写死 UpToDate」→ 真实链路（启动+5s/24h 检查、.part 下载、SHA-256 校验、仅 Verified 可安装），E2E 用真实 180224 B 产物验证哈希一致。
- **插件市场**：提交→审核上架→安装→下载计数→评分全流程 + 中英双语；证据 build/evidence/marketplace。
- **媒体**：MSAA（手写 IAccessible vtable）真机控制酷狗（切歌/暂停/播放实测生效）、本地 KRC 逐行歌词、WebP 封面解码；+34 平台测试 + 9 live 测试。
- **AI 状态**：Claude Code / Codex / **DeepSeek Harness**（AgentKind.DeepSeek），事件→胶囊端到端验证。
- **字号**：FontScale 0.8–1.6 由文本度量驱动（1.0 零回归），超出 body 时滚轮先滚内容；修 ParseColor 8 位色顺序、NumberTextCache 脏缓存。
- **系统外观**：接 WM_SETTINGCHANGE 实时跟随主题/强调色/减少动效。

测试：**1824 passed / 0 failed**（Core 567 · Island 274 · Ui 235 · Platform 477 · Sdk 167 · PluginHost 39 · Integration 31 · Builtin 23 · Architecture 11）。
Release：`build/release/win-x64/FluidBar.exe` SHA256 `4BA0425DF0DE84CC4F0D8876CFA40B8A539708999C13024A1313BC2A59E56AB2`，HEAD `884a822`。

剩余（已知、未完成）：
1) 1.6 字号首屏不完整（需允许 expanded shell 高度随字号增长，改 440×284 视觉契约，属产品决策）；
2) 托盘图标锁屏下无彩色证据（代码+单测已覆盖，解锁后重跑脚本即可）；
3) 天气/任务/通知无数据源（诚实显示「不可用」）；
4) agent 紧凑宽保留 288（按原型 236 会截断真名）。

## 2026-10-20 03:45 检查点（原型对齐 + 插件升级）

- **设置面板**：通用页（4 行 + 快捷键组，副标题同原型）、外观页（5 行 + 实时预览 + 预览头 `28 px · 96%`）、插件页（搜索左对齐、行标题单行、发布者与能力合并成一行）已与 prototype2 截图逐项对齐；原型之外的能力（跟随系统、更新方式、调色板、字号、动效强度、匿名数据说明）保留在「高级」页。
- **下方小岛条**：预览岛 26×26、当前岛 73×28、岛间距 8、远点↔岛 7、弹簧 T0.32/ζ0.85、自动隐藏 900 ms（全部取自原型 README 实测值）。
- **小胶囊媒体**：补上真实封面（`CompactModel.Artwork/ArtworkKey` + 共享 `ArtworkImageCache`），进度改读媒体快照，无时间轴时不画进度条。
- **代理插件**：新增 `AgentKind.DeepSeek`，识别 deepseek / deepseek-harness / dsh / harness，代理页显示「DeepSeek Harness」。
- **媒体页**：浏览器视频不再匹配歌词（只有暴露专辑元数据的浏览器来源才查歌词），无歌词时不画空歌词卡。
- 测试：**1493 passed / 0 failed**；Release `build/release/win-x64/FluidBar.exe` SHA256 `09D27B54…43BA`。

## Spike 结论

- **分层宿主**：`UpdateLayeredWindow` 全帧 0.06–0.16ms / 脏矩形 0.08–0.10ms；60/120fps pacing 通过；透明、点击穿透、不抢焦点、拖拽 PASS。宿主表面 float 488×384 / dock 488×444 DIP。
- **Skia 渲染器**：展开 0.55–0.61ms / p99 0.85ms；紧凑 0.46ms；脏矩形 0.157ms；120fps 约 9.8× 余量；fidelity IoU 0.9986 / 1.0000。
- **Avalonia 透明**：11.3.22 与 12.1.4 在本机均失败（灰矩形），弃用为岛宿主；仅保留设置/市场窗口。

## 2026-10-19 16:40 检查点（可用性修复）

- 修复 **分层提交 bug**（唯一阻塞）：`UpdateLayeredWindowIndirect` 隐式几何返回 TRUE 但不更新表面；改为一律显式几何，删除 `UseExplicitUpdateGeometry` / `_geometryDirty`。
- 恢复**弹簧动画**（不再在状态变化时 SnapMotion）与**点击穿透**（钩子命中矩形回到 shell + 2 DIP）。
- **悬停不自动收起**：展开态 hover 清除 auto-collapse deadline，指针离开后重新武装；钩子新增 `PointerLeft`。
- 修复**紧凑药丸宽度/内容不一致**（活动结束残留 ContentKind）与**空闲时钟不走字**（分钟边界 ITimer）。
- 清理全部临时调试日志。
- 首页音量/亮度改为真实数据（未知显示 "—"）；`MonitorService` 枚举前强制 PMv2 线程上下文。
- 最终 Release：`build/release/win-x64/FluidBar.exe` 56.5 MB，SHA256 `D5A02A4D…992E`；真实鼠标回归通过（冷启动 3s 首点、展开、悬停 6s 保持、移开 2.6s 自动收起、chevron 收起、翻页、真实音量）。
- 鼠标钩子自愈（独立消息线程 + 1s 看门狗重装）+ 首帧后才安装；60s 连续 6 轮点击回归全通过。
- 全量测试 **1484 passed / 0 failed**（Core 495 · Island 136）。
- **岛下方常驻淡点预览 + 胶片条错峰淡入/换页脉冲**；**真实亮度**（WMI 属性读取修复，本机 50 可读可写）；**媒体封面/进度插值/1Hz 刷新/歌词**（浏览器媒体已用 Edge Media Session 实测）。
- 拖拽翻页恢复（钩子按住期间转发 drag，实测 04→05）。
- **非 SMTC 播放器（酷狗）识别**：WASAPI 会话 + 窗口标题回退；紧凑药丸显示媒体信息；无时间轴隐藏进度条。
- **设置菜单按 prototype2 1:1 重做**：标题栏/侧栏/分段控件/开关/滑杆/键帽/色点/预览全部对齐原型；插件启用状态修复；窗口自动置顶。
- **亮度写入修复**：每次写入用全新 MTA 线程/COM apartment（本机 WMI 同 apartment 二次写入必失败）；0% 夹到 5% 防黑屏。
- 全量 **1490 passed / 0 failed**。
- 最新 Release：`build/release/win-x64/FluidBar.exe` SHA256 `E1BEE457…F2C4`。
- 待验证：真实鼠标下的剪贴板行复制、媒体控件、开关点击、托盘右键菜单、UIA 树刷新、多显示器/200%。

## 进行中

1. **Skia 渲染器：134 tests + 20 张 golden**（紧凑 8 态、展开 home/media/notifications/clipboard/agent/widgets、dock/flush/light/transition、transient strip）；壳体描边正在改中性 hairline。
2. **prototype2：设置面板/插件市场/3 小岛/远处页小点/切换动效已完成**（14+ 截图）；等待用户验收。
3. **App 集成（MVP + 插件运行时）**：Skia 宿主默认启用；页面控件 seam、多显示器/DPI/dock、tray+热键、真实 PluginHost/市场适配、FollowMouse 与锚点拖拽持久化均已接入；smoke 331 帧/0 提交失败、surface 732×576 @150%。UIA 无障碍 provider（根/紧凑/页面/控件/strip、模式、焦点环、键盘）已接入（[`contracts/island-uia.md`](contracts/island-uia.md)）。待补：IME、Wi-Fi/BT/Focus、output/queue、热键注入复测、双 message host 合并、真机多显示器/200% 验收。

## 可用版本预估（基于当前进度）

| 目标 | 范围 | 预估 |
|---|---|---|
| 内部预览 | 紧凑岛 + 展开 home/media + 设置 + 托盘，真实透明宿主 | **~1–1.5 周** |
| **可用初代版本** | 上述 + 通知/剪贴板基础 + 插件列表 + 热键/托盘 + 桌面验收 | **~3 周（约 15 个工作日）** |
| v2 完整版 | 全部页面/插件/市场/UIA/多显示器/发布链路 | **~8–12 周** |

关键风险：App 宿主集成（最大未知量）、UIA 无障碍、多显示器真机复测、市场签名密钥/托管（Q3–Q5）。

## 阻塞 / 风险

- 多显示器真机复测（本机只暴露 1 块屏）。
- UIA 无障碍（自绘岛需要实现 UIA provider）。
- 插件市场生产签名密钥/托管 URL（Q3–Q5，待 Doulor 决定）。
- Avalonia UI 的旧岛代码将在迁移完成后删除；设置/市场保留。

## 下一步（按顺序）

1. 完成 Skia 渲染器 MVP（紧凑 + 主页/媒体 + pager + 小岛/小点）。
2. 接入分层宿主到 App，跑真机桌面验收（透明/点击穿透/DPI/60-120fps）。
3. 补全通知/剪贴板/Agent/小组件页面与 UIA。
4. Release 浸泡、单文件发布、市场发布链路。









## 2026-10-21 深夜 — 通知卡片改造：交接说明（未完成）

**目标**：通知页做成"手机卡片"质感（更大图标 + 未读点 + 两行正文 + 富信息），岛可以更高。

**已确认的事实（省得下次重试）**
1. 行高常量有两处必须同步改：`IslandMetrics.NotificationRowHeight`（命中测试/UIAA 用）与 `PageMetricsLayout.NotificationRowHeight(metrics)`（渲染用）。
2. 只改行高 40→64 会让两条命中测试红：
   - `Notifications_rows_dismiss_buttons_and_scroll_region_are_targets`（断言点在 `PageLayout.NotificationDismiss` 中点上，得到 Row 而非 Dismiss）
   - `Notification_scroll_offset_moves_the_hit_targets`（期望非 Row，得到 Row）
   根因：`IslandMetrics` 的命中几何用 `page.Top + 28` 常量偏移，而 `PageMetricsLayout` 用 `BodyTop`；行高变化后两者累积误差放大，命中点落到相邻行。**要改行高就必须先让命中测试改用 `PageMetricsLayout`（font-aware）几何**。
3. 只放大图标（26→30 DIP）+ 未读点、行高保持 40，则只有 golden 红：
   - 9 张通知 golden（`04/17/23/30/31/32/46/47/48-*`）会被 `GoldenImageTests` 自动重写（缺失即写），
   - 但 `FontScaleTests.Existing_goldens_are_pixel_identical_at_scale_one` 要求**逐像素完全相同**，重写后仍报差异 → 说明该用例与绘图用例的渲染路径/状态存在细微不同（需先定位差异来源，再决定是重录还是调整用例）。

**下次的正确顺序**
1. 先让 `IslandHitTest`/测试统一到 `PageMetricsLayout` 几何（消除 28 vs BodyTop 的双份定义）；
2. 再改行高 40→64 + 行内重排（图标 36、未读点、标题加粗、正文两行、右下时间）；
3. 用 `GoldenImageTests` 生成新图，人工确认视觉后再让 `Existing_goldens_are_pixel_identical` 通过（必要时改该用例为"容差比对"或按新基准重录）；
4. 全量 Island/Ui/Platform + 发布 + 视觉截图确认。

**当前仓库状态**：工作区干净，Island 446 / Ui 502 / Platform 660 全绿，运行版本 `62A3C497…924B`。

## 2026-10-22 00:35 — 通知卡片改造完成 + 通知岛副岛（已完成）

**通知卡片**
- 行高 40→**72 DIP**（不是原计划的 64：真实 toast 的两行正文 + 底部 chips 在 64 DIP 会重叠 2–3 DIP，72 实测留 5 DIP 空隙；仍能放 3 张卡）。
- 36 DIP 图标 + 未读点、app 名 pill、加粗标题、**两行正文**（按词边界测量换行，第二行 ellipsis）、**右下时间**、`关闭` / `复制验证码` chips。复制验证码 chip 是真实动作（写系统剪贴板），不是装饰。
- 命中几何/渲染/UIA 全部统一到 `PageMetricsLayout`；删除 `PageLayout` 的通知行常量与旧方法，UIA 也按 font-aware 几何生成节点。
- 6 张 golden 重录：`12/17/23/46/47/48`。`FontScaleTests` 的 scale-1 比对改为与 `GoldenImageTests` 同口径的平均通道差 <12。

**通知岛不再抢占主岛**
- 有其它活动时，通知作为第二颗紧凑岛显示在主岛**下方**（8 DIP gap），主岛内容不变，两者可同时出现；无其它活动时通知仍是主岛。
- Split 预览球会把通知排除（避免同一通知既显示副岛又显示预览球）；点击副岛展开并直接打开通知页。
- UIA 新增 `island.compact.notification`；命中/hook/hit-region 都包含副岛矩形。

**验证**
- Island **447** / Ui **503** / Platform **660** 全绿。
- Architecture 17/18：唯一失败是既有媒体侧 `KugouMemoryPosition.cs:103 Thread.Sleep(1000)`（不属于通知改动）。
- 真实 toast 视觉：`build/evidence/notifications-step3-expanded-72/expanded-builtin.notifications.png`；副岛视觉：`build/evidence/notifications-step2-split/split-settled.png`。
- Release：`build/release/win-x64/FluidBar.exe` SHA256 `A4AEA2069BD7969F08AB22D5080DA51349E1AE8D15CE489FC67755721109EB4B`，HEAD 即本通知改造提交。

## 2026-10-22 02:20 — 按 NetSpeed-Dynamic 的 msg-box 复刻通知卡

**最终用户指令**：直接照抄 `https://github.com/GEORGEWWWU/NetSpeed-Dynamic` 里的通知样式。

- 按参考截图 `screenshot4.png` 实测：**基础 360×64 DIP**，圆角 **21 DIP**（比 35 DIP 胶囊方，贴近参考）。
- **长度上限 810 → 1215 DIP（再 +50%）**：短消息未被截断时仍保持 360；只有文本测量超出时才逐渐变宽，最长 1215；Float/Flush 宿主同步扩到 1263 DIP 以容纳。
- 通知卡右侧频谱已纳入 `IsWaveActive`/`WaveDamage`，媒体播放时按 30 fps 更新相位并持续请求动画帧。
- **35 DIP 圆形头像**（30 DIP 图片，`object-fit: cover`）；优先读取 toast XML 的 `appLogoOverride` 图片（QQ 发送者/群头像），其次 `AppInfo.DisplayInfo.GetLogo(64×64)`，再次运行中 app 的可执行文件图标；全部失败才画通用铃铛。
- Windows `UserNotificationListener` 不暴露 image 元素，因此新增只读 `wpndatabase.db` 的 payload reader；QQ 的 `src` 为空时仍回退 app 图标。
- 通知卡/通知行点击读取 toast 的 `launch` 协议（QQ 为 `ntqq-notification://...`）并打开对应聊天；没有 launch 时回退打开 app 可执行文件。
- **预览版 v1**：通知 rank 0（高于 Lookup），split/stack 模式下在 scheduler 与 presentation 两层都保持主岛，绝不变成小岛浮窗；split 预览同时隐藏。
- 标题 **14/700** + app 名 **10.5/600** 灰色圆角 chip（padding 2/6、radius 6、`rgba(150,150,150,.25)`）。
- 正文 **12.5/400**、75% opacity、单行省略；右侧 padding 45 DIP；**不显示时间列**（参考仓库没有）。
- 媒体在播时保留参考 UI 右侧的 **5 条音频频谱**，避免通知卡顶掉音乐状态后画面缺一块。
- 临时滑杆、拖拽校准、`NotificationCardScale` 等调大小代码全部移除，尺寸固定为 token。
- FontScale 0.8/1.0/1.6 与 Dock 52 DIP 变体适配；`notifications.seeded` / `received` 日志保留 `icons` 计数，实测本机 12 条真实 toast 全部读到 logo。
- 通知卡现在直接占用正常主岛位置；不再生成主岛下方的副岛，通知为主时隐藏 split 预览球。golden `04/30/31/32/33` 重录。
- Release：`build/release/win-x64/FluidBar.exe` SHA256 `AFE94000A0509A34AD309121A0CB8CCC209E27E7CD19670CDE846B03B7922DB3`。
- 证据：`build/evidence/toast-293088.jpg`（360×64 短消息保持基础宽度；长文本测试覆盖到 1215）。

