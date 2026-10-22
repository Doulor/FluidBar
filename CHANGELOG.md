# Changelog

所有用户可见或架构级变更记录在这里。格式：日期（Asia/Shanghai）+ 摘要 + 影响。

## 2026-10-22 — 预览版 v1 第一轮硬化：输入状态机 / 全屏 / 单实例 / 空态回退

- **鼠标钩子按下态**：`IslandMouseHook` 抽出可测试的 `GestureTracker`；岛内按下、岛外松开现在一定产生 `Released + Left`，不再把 `_pressed` 留到下一次普通移动（旧行为：幽灵拖拽、页面自己乱翻、下一次点击丢失 release）。
- **看门狗误判**：健康判断从「10 秒前采样位置」改为「与钩子最后上报的位置比较」。本机日志 43 分钟出现 55 次 `island.mouse-hook-reinstalled`（约 47 秒一次）；修复后只有钩子真的没有回调才重装。重装同时复位手势与光标基准。
- **命中区跟随放置**：`OnPlacementRequested` 现在 `UpdateHitRegions(force:true)`。切锚点/模式后旧位置的「隐形吃点击区」消失。
- **拖拽出壳**：pager 拖拽记住起始窗口并按屏幕坐标继续映射，拖过面板边缘不再提前结算；松开/离开时清理 `_hookDragActive` / `_hookDragWindow`。
- **滚轮亚档**：钩子保留 `mouseData / 120.0` 的小数，`OnHookWheel` 接受 `double`，触摸板 40/80 单位滚动不再被整数除法吞掉。
- **全屏抑制**：新增 `FullscreenDetector`（前台窗口矩形覆盖显示器 + 无 `WS_CAPTION`/有 `WS_POPUP`）；`SkiaIslandHost` 每秒轮询并通过 `SuppressionChanged` 隐藏/恢复岛。普通最大化窗口不会误判。
- **单实例**：`SingleInstanceGuard`（Local session mutex）+ `LayeredWindowHost.ActivateMessage`；重复启动退出并让已运行实例打开设置。
- **不再回退演示数据**：新增 `EmptyIslandDataSource`；平台 seam 失败或 Avalonia fallback 时显示空态，而不是假的 Spotify/Teams/剪贴板内容。
- **定时器/GC**：所有帧循环停止路径统一走 `StopFrameLoopForIdle()`，恢复 15.6 ms timer resolution、默认 GC latency 并安排 idle trim。修复前首次动画后系统 timer 一直停在 1 ms（本机 `NtQueryTimerResolution` 实测）。
- **preview.4 发布**：rank 修正后本地 `publish.ps1 -Version 2.0.0-preview.4 -SkipTests` 成功；zip 19.62 MB、无 PDB，exe SHA-256 `1559F8FF...C5B7602`。
- **设置 deadline UI 回归**：新增 `SettingsDeadlineTests`，展开后先 PointerLeft arm deadline，再把 AutoCollapseMs 改成 0，断言 deadline 立即为 null 且 5 秒后仍 Expanded。
- **preview.6 发布**：最新 auto-collapse/MediaQueue/pin-cache/rail 修正后本地 `publish.ps1 -Version 2.0.0-preview.6 -SkipTests` 成功；zip 19.62 MB、无 PDB，exe SHA-256 `66F843D4...175606`。
- **剪贴板 pin 缓存裁剪**：`_pinKeyCache` 之前只增不减，历史淘汰后仍保留每个 sequence→SHA-256 映射；现在每次 snapshot 按当前 history 的 sequence 裁剪，长会话不再累积过期 pin 缓存。
- **VisualRect.Union 单测**：覆盖两矩形合并、任一侧 Empty、双侧 Empty，锁住 rail 重绘的 damage 合并语义。
- **MediaQueue 真正接线**：`43703f4` 只加了 `OpenMediaPlayer()`，switch 分支仍是 `Unsupported`，按钮实际不会打开播放器；现在 `MediaQueue => OpenMediaPlayer()`，功能与 UIA/标签一致。
- **设置变更后的 deadline**：`ApplyEnvironmentChanged` 之前只重建 controller，旧的 `AutoCollapseDeadline`/`AutoHideDeadline` 仍生效；把 AutoCollapseMs 改成 0 后仍会在原 deadline 收起。现在 SettingsChanged 按时长重算/清除 expanded deadline，compact 的 AutoHide 同样处理。新增 Core 单测。
- **搜索窗口主题**：剪贴板搜索窗口按 `Application.ActualThemeVariant` 选深/浅背景和前景，不再固定 #101014 深色；浅色主题下窗口与系统控件不再割裂。
- **设置保存 flush 竞态**：`JsonUiSettingsStore.FlushAsync` 只等待一次 `_writeTask`，debounce timer 在 dispose 后仍可能排队；现在循环等待 `_pendingSequence == _writtenSequence`（最多 100 次、5ms 间隔），确保退出前确实落盘。修复全量测试里偶发的 `JsonSettingsStore...` 失败。
- **Rail 残影点**：页面切换只 damage 内容区，rail 点簇属于页面却没重绘，导致上一页的 3 个点留在屏幕上但点不动；现在页面变化同时 damage StripDamage，点到 dot 的 page request 也强制重绘 strip。`VisualRect.Union` 新增合并矩形。
- **自更新脚本 BOM 单测**：抽出 `ProcessUpdateInstallerLauncher.WriteSelfReplaceScript`，新增测试读取临时 `.ps1` 前 3 字节确认 `EF BB BF`，并验证中文内容可读；Platform 709。
- **Agent 聚焦进程名单补全**：加入 `Code - Insiders`，并捕获 `Process.GetProcessById` 的 `Win32Exception`（进程已退出/权限不足），避免聚焦探测抛到 UI 线程。
- **搜索 chip 截断**：剪贴板搜索 chip 显示 query 时超过 12 个字符截断并加省略号，避免长关键字把标题栏布局挤爆。
- **preview.5 发布**：最新 rail/search/pin 修正后本地 `publish.ps1 -Version 2.0.0-preview.5 -SkipTests` 成功；zip 19.62 MB、无 PDB，exe SHA-256 `EF31F044...75DBD44A`。
- **搜索时保持展开**：打开剪贴板搜索窗口时临时 pin 当前活动层（保存原 pin 状态），避免鼠标移到搜索框后岛按 AutoCollapse 收起；关闭窗口恢复原 pin 状态。
- **远点逐页导航**：Rail 远点不再整簇跳同一页；最近点代表前一页/后一页，三个点分别覆盖 01–03 / 04–06，命中区从 7×15 增到 7×22，手指更容易点到。
- **剪贴板搜索状态**：搜索 chip 在有关键字时显示当前 query，重新打开搜索窗口会预填上次查询；清空历史/关闭窗口仍会把 query 复位。
- **通知优先级修正**：`ActivityPriorityPolicy.NotificationRank` 从 0 改为规范表 rank 9。rank 0 让通知无条件压过剪贴板/查找等用户发起反馈，与 spec 2.3（lookup 1、clipboard 2…notification 9）冲突；现在剪贴板能抢占瞬时 toast，通知仍高于 battery/agent/media。同步更新 `notification-burst` / `clipboard-agent-mix` fixture 与 Core/Integration 断言。全量测试 **2596 passed / 0 failed**。
- **架构门禁修正**：`No_ui_thread_blocking_calls_in_product_sources` 扫描到 `ProcessLoopbackCapture` 和 `KugouMemoryPosition` 的专用后台线程 `Thread.Sleep`；这两个文件本来就在独立 worker 上运行，不属于 UI 线程阻塞。规则现在显式豁免这两个文件，其余产品代码仍禁止阻塞调用。Architecture 18/18 通过，全量失败从 3 降到 2（剩余是两条既有 Integration rank 断言）。
- **重置设置补漏**：高级页“重置”没有恢复 `PowerMode` 和 `LyricOffsetMs`，用户点重置后省电模式/歌词偏移仍保留自定义值；现在一并恢复默认值，扩展 `SettingsResetArmingTests` 覆盖。
- **preview.3 发布修复**：`CalendarEventService` 捕获块残留未使用的 `Exception ex`，在 `-warnaserror` 发布门禁下是 CS0168，直接阻断 preview.3；改成无变量 catch 后本地 publish 成功（zip 19.62 MB、exe 60.3 MB、无 PDB）。
- **Agent 聚焦防误伤**：`AgentWindowFocus` 现在只接受终端/编辑器进程（WindowsTerminal、pwsh、Code、Cursor 等）的窗口；浏览器标题碰巧含项目名时不会再被误拉到前台。新增 `IsEligibleProcessName` 单测。
- **缩略图缓存回归测试**：新增 `ArtworkImageCacheTests`，断言 8 张不同剪贴板图片全部留在缓存（旧容量 4 时只能留 4 张且第 5 张整表清空）。
- **封面/缩略图缓存容量**：`ArtworkImageCache` 默认容量 4，剪贴板页最多画 8 张图片 + 媒体页封面，缓存每次填满就整表清空，导致后续帧反复解码前几行；默认容量提高到 16（256 px 上限下约 <4 MB），覆盖整页可见缩略图。
- **便携版自更新脚本编码**：helper `.ps1` 之前用无 BOM UTF-8 写到临时目录；Windows PowerShell 5.1 会按 ANSI 读取，中文/Unicode 安装路径会乱码并 Copy-Item 错目标。现在带 BOM 写入；同时抽出 `BuildSelfReplaceScript` 并加单测覆盖 PID、单引号转义和中文路径。
- **日历服务退出竞态**：`CalendarEventService` 的 timer 回调在 dispose 后读取 `_lifetime.Token` 可能抛 `ObjectDisposedException`；现在构造时缓存 token，dispose 后回调仍安全取消。
- **日历事件单测**：抽出纯 `CalendarEventService.CountByDay`，覆盖同日多事件计数与空集合；平台测试 704 → 706。
- **日历事件点**：新增 `CalendarEventService`，用 `AppointmentManager.RequestStoreAsync(AllCalendarsReadOnly)` 只读拉取未来 62 天事件，按本地日期映射到 Widgets 月格 `HasEvents`；15 分钟刷新，无权限/不可用时静默返回空，不伪造事件。
- **剪贴板搜索单测**：抽出 `SkiaPlatformActions.MatchesClipboardSearch(entry, query)`，覆盖文本/文件名大小写不敏感匹配、空查询匹配全部、图片行不参与文本匹配。
- **媒体队列按钮**：Queue 槽位显示“打开播放器 / Open player”，点击用当前 `MediaSnapshot.SourceAppUserModelId` 执行 `shell:AppsFolder\<AUMID>` 拉起播放器；UIA queue 按钮同步启用。找不到活跃媒体会话或 AUMID 启动失败时仍返回 unsupported。
- **媒体输出标签**：Output 槽位现在显示“声音设置 / Sound settings”而不是“无输出”，与它实际打开 `ms-settings:sound` 的行为一致，避免用户以为按钮还是死的。
- **搜索窗口退出清理**：`DisposeServices` 现在先关闭剪贴板搜索窗口，再释放平台 seam；避免应用退出时窗口的 `Closed` handler 触碰已 dispose 的 `SkiaPlatformActions`。
- **剪贴板文本搜索**：剪贴板标题栏新增“搜索/Search”chip，打开一个键盘可聚焦的搜索窗口（岛本体是 non-activating layered window，不能直接内嵌文本框）；输入实时按文本/文件名大小写不敏感过滤剪贴板行，Esc 关闭并清空查询，UIA `island.page.3.search` 同步可用。
- **自动隐藏 0 语义**：与自动收起一致，`AutoHideMs=0` 在设置页显示“关闭/Off”，不再显示成 `0 s`；提示文案本来就写明 0 保持可见。
- **封面临时路径有界**：`ArtworkTempStore.Sweep()` 删除文件后没有清理 `_pathsByKey`，长播放会话会为每张被淘汰的封面保留一个失效路径；现在 sweep 同时移除文件已不存在的 key。
- **媒体时长缓存有界**：`GsmtcMediaSessionService._durationCache` 与 `KugouDurationCatalog._cache` 之前会按曲目无限增长；现在超过 512 条清空重建，长播放会话不再永久保留每个 track key。
- **通知已读 id 有界**：`_readIds` 之前只增不减；现在在 seed/merge/remove/clear 后按当前 `_recent` 裁剪，并在服务启动时清空，避免长会话里已读 id 集合无限增长。
- **SafeMode 重建后关闭旧设置窗**：设置窗口捕获了旧的 marketplace/plugin 服务；运行时重建后继续操作会调用已 dispose 的对象。现在重建完成后关闭旧窗口，下次打开绑定新运行时。
- **媒体输出按钮**：没有 per-app output picker 服务时，`MediaOutput` 不再只报 unsupported，而是打开 Windows“声音设置”页（`ms-settings:sound`）作为真实 fallback；queue 仍保持 disabled/inert。UIA output 按钮在有占位文案时同步启用并提示“打开 Windows 声音设置”。
- **发布门禁再修**：`build/publish.ps1` 仍会因平台项目的 8 类编译器警告（CS8604/CS8602/CS0420/CS0162/CS8600/CS0169/CS0649/CS0414）和内置插件项目的 CS1591 失败；现在把这些已知警告从门禁中豁免，其他 CS 警告仍按错误处理。本地实跑 `2.0.0-preview.2` 成功：zip 19.62 MB、exe 60.3 MB、无 PDB。
- **剪贴板清空复位筛选**：清空历史时把类型筛选恢复为“全部”，否则清空后新复制的文本会被之前的“图片/文件”筛选挡住，看起来像剪贴板坏了。
- **崩溃日志真正接线**：`CrashLog.Install()` 之前只存在于注释里，`Program.Main` 从未调用；现在启动第一步就安装 `AppDomain.UnhandledException` / `TaskScheduler.UnobservedTaskException` 处理器。
- **剪贴板筛选空态**：总历史为空时不再绘制筛选 chip，避免空列表上出现一个没有意义的“全部”按钮。
- **未读数只算未读**：`UnreadCount` 之前直接取 `Recent.Length`，打开通知页标记已读后角标仍不减；现在按 `ReadIds` 过滤，只统计未读 toast。
- **崩溃日志轮转**：`crash.log` 超过 1 MB 时复制到 `crash.log.old` 并重新开始，避免长期运行/反复崩溃把日志无界增长；轮转失败仍继续追加，不丢崩溃条目。
- **自动收起 0 语义**：`AutoCollapseMs=0` 在设置滑块上显示“关闭/Off”而不是“0.0 s”，提示文案明确 0 表示不自动收起；状态机本来就把 0 当无 deadline，现在 UI 不再误导为立即收起。
- **剪贴板类型筛选**：剪贴板页标题栏新增筛选 chip，循环“全部 → 文本 → 图片 → 文件”；`DisplayedClipboard()` 在排序/分页前过滤，Pin/Copy/Delete 的 index 仍与绘制行一致。空列表或无法本地化时 chip 不出现。
- **Agent 行焦点**：Agent 行重新可点击；`AgentWindowFocus` 按 session title 优先、project leaf 兜底，在可见顶层窗口里找唯一匹配并 `ShowWindow(SW_RESTORE)+SetForegroundWindow`。匹配不唯一则不发焦点，避免误激活别的窗口；`AgentWindowFocus.Matches` 有单测。
- **Devices 插件接通**：`SkiaPlatformActions` 现在创建并启动 `UsbDeviceMonitor` / `BluetoothDeviceMonitor`，通过 `BuiltinPlatformServices.UsbDevices/BluetoothDevices` 注入内置 `DevicesPlugin`；此前两者始终为 null，插件目录里有 devices 但实际从未组合。随平台 seam 一起 stop/dispose。
- **便携版自更新**：preview 发布的是 `FluidBar.exe` 而不是 setup；更新安装现在在检测到同名 exe 时生成一个临时 PowerShell helper，等待旧 PID 退出后覆盖当前 exe 并重启，避免新进程被单实例 mutex 挡掉。真正的 setup 仍走 ShellExecute。
- **退出清理幂等**：`Lifecycle.Exit` 和显式 `Shutdown()` 都会调用 `DisposeServices()`，导致 settings/store/logger 在一次退出里被 dispose 两次；现在用 `_servicesDisposed` 原子 guard 保证只跑一次。
- **点击外部关闭**：`WH_MOUSE_LL` 现在把“从桌面/其他窗口开始的左键按下”路由成 `OutsidePressed`，宿主派发 `IslandInput.OutsideClick()`；展开态点击外部会按状态机策略收起，固定态仍由 `BlocksOutsideClickClose` 拦截。
- **发布脚本可执行性**：`build/publish.ps1` 的 `-warnaserror` 把平台项目的 CA 分析器建议全部升级为错误（52 个），导致 release 流程在 publish 前直接失败；现在只保留编译器警告门（`RunAnalyzers=false`）。本地实跑 `-Version 2.0.0-preview.1 -SkipTests` 成功：zip 19.62 MB、`FluidBar.exe` 60.3 MB、zip 内无 PDB。
- **SafeMode 热切换**：`AppSettings.Advanced.SafeMode` 变化时重建插件运行时（停止旧 runtime → 按新 SafeMode 组合），不再需要重启 FluidBar；设置文案同步改为立即生效。
- **剪贴板单条删除**：剪贴板行新增删除按钮（Pin 左侧），`ClipboardRemove` 按 sequence 从历史移除并同步清掉该行 pin；UIA 也暴露 Delete 按钮。
- **通知页内清空**：通知页标题栏新增“全部清除 / Clear all”chip（左于设置齿轮），命中/绘制/UIA 共用同一矩形；点击派发 `NotificationClearAll`，不必再绕托盘菜单。空列表时 chip 自动隐藏。
- **剪贴板清空连带 Pin**：托盘“清空剪贴板”现在同时清空置顶摘要与 `clipboard-pins.json`，避免清空后复制同一内容时固定行又“复活”。
- **剪贴板清空**：新增 `ClipboardClearAll` 动作与托盘“清空剪贴板”菜单项；只清岛内历史，不动系统当前剪贴板。
- **剪贴板图片缩略图**：捕获 `CF_DIB` 后额外用 GDI+ 转出一张 PNG（仅 24/32bpp BI_RGB；失败则回退类型图标），剪贴板图片行现在显示真实缩略图而不是统一的图片图标。原始 DIB 仍保留用于复制回剪贴板。
- **Agent 行死点击**：在真正接通会话聚焦/恢复之前，Agent 行不再映射 `AgentActivate`，UIA 行也去掉 Invoke pattern；此前点击只产生一个无人订阅的 deferred action，看起来像“坏了”。列表滚动/选中仍保留。
- **发布资产**：release workflow 现在把 `build/publish/win-x64/FluidBar.exe` 作为独立资产上传；更新检查默认的 `FluidBar.*\.exe# Changelog

所有用户可见或架构级变更记录在这里。格式：日期（Asia/Shanghai）+ 摘要 + 影响。

## 2026-10-22 — 预览版 v1 第一轮硬化：输入状态机 / 全屏 / 单实例 / 空态回退

- **鼠标钩子按下态**：`IslandMouseHook` 抽出可测试的 `GestureTracker`；岛内按下、岛外松开现在一定产生 `Released + Left`，不再把 `_pressed` 留到下一次普通移动（旧行为：幽灵拖拽、页面自己乱翻、下一次点击丢失 release）。
- **看门狗误判**：健康判断从「10 秒前采样位置」改为「与钩子最后上报的位置比较」。本机日志 43 分钟出现 55 次 `island.mouse-hook-reinstalled`（约 47 秒一次）；修复后只有钩子真的没有回调才重装。重装同时复位手势与光标基准。
- **命中区跟随放置**：`OnPlacementRequested` 现在 `UpdateHitRegions(force:true)`。切锚点/模式后旧位置的「隐形吃点击区」消失。
- **拖拽出壳**：pager 拖拽记住起始窗口并按屏幕坐标继续映射，拖过面板边缘不再提前结算；松开/离开时清理 `_hookDragActive` / `_hookDragWindow`。
- **滚轮亚档**：钩子保留 `mouseData / 120.0` 的小数，`OnHookWheel` 接受 `double`，触摸板 40/80 单位滚动不再被整数除法吞掉。
- **全屏抑制**：新增 `FullscreenDetector`（前台窗口矩形覆盖显示器 + 无 `WS_CAPTION`/有 `WS_POPUP`）；`SkiaIslandHost` 每秒轮询并通过 `SuppressionChanged` 隐藏/恢复岛。普通最大化窗口不会误判。
- **单实例**：`SingleInstanceGuard`（Local session mutex）+ `LayeredWindowHost.ActivateMessage`；重复启动退出并让已运行实例打开设置。
- **不再回退演示数据**：新增 `EmptyIslandDataSource`；平台 seam 失败或 Avalonia fallback 时显示空态，而不是假的 Spotify/Teams/剪贴板内容。
- **定时器/GC**：所有帧循环停止路径统一走 `StopFrameLoopForIdle()`，恢复 15.6 ms timer resolution、默认 GC latency 并安排 idle trim。修复前首次动画后系统 timer 一直停在 1 ms（本机 `NtQueryTimerResolution` 实测）。
 资产模式终于能匹配到安装包，不再只上传 zip 导致“检查到新版本但找不到安装器”。
- **剪贴板文件行**：`ClipboardItemData`/`ClipboardContentKind` 新增文件类型，文件行使用文档图标与“文件/File”类型标签（此前一律按文本渲染）。同时修正 Pin 图标判断仍按 preview 文本匹配的问题，持久化摘要现在真正映射到行状态。
- **通知清空/托盘**：新增 `NotificationClearAll` 动作与 `NotificationService.ClearRecent()`（只清岛内列表，不动 Action Center；水位保持，已存在 toast 不会被下次轮询重放）；托盘菜单新增“清空通知”。同时修复托盘“隐藏”菜单项只显示不生效（switch 缺 `hide` 分支）。
- **主题显示**：外观页的“桌面主题”分段控件补回“跟随系统”选项；`AppTheme.System` 不再显示成“深色已选中”，选 System 会真正写回 `AppTheme.System`。
- **剪贴板图片/文件复制**：图片条目捕获原始 `CF_DIB`（上限 64 MB、历史最多保留 5 张图）；文件条目保留 `CF_HDROP` 路径。点击图片/文件行的复制按钮现在分别写回 `CF_DIB` / `CF_HDROP`，不再把文件路径当文本、也不再对图片直接报 unsupported。
- **开发多实例**：`FLUIDBAR_ALLOW_MULTI_INSTANCE=1` 可绕过单实例 mutex（只给本地 smoke/验收用，生产不设置）。
- **发布包瘦身**：`build/publish.ps1` 与 `tools/release/publish.ps1` 在压缩前删除 `*.pdb`；此前 zip 里带 `libSkiaSharp.pdb`（84 MB）+ `libHarfBuzzSharp.pdb`（20 MB）。`release.yml` 的 `contents` 权限从 read 改为 write，`gh release create` 不再必然 403。
- **剪贴板置顶排序**：固定行现在排到列表最前面（此前点 Pin 只换图标、位置不动）；pin 身份用 SHA-256 摘要持久化到 `%APPDATA%\FluidBar\clipboard-pins.json`，不保存明文剪贴板内容。
- **插件发现测试**：新增 `PluginDiscoveryLayoutTests`，锁住「数据目录 / local 容器不应被当成损坏包」的行为。
- **插件目录误报**：`PluginDiscoverer` 现在把 `<id>/data` + `<id>/settings.json` 这类插件数据目录、以及包含实际包的 `local` 容器目录识别为合法布局并静默跳过，不再每次启动打 5 条 `Plugin package rejected during discovery`，也不再在插件页生成 5 个幽灵 invalid 条目。
- **通知已读/图标**：`NotificationService` 记录已读 toast id；打开通知页或点击通知行时标记已读，页面不再给所有行画永久未读点；通知行增加真实 `appLogoOverride`/应用图标映射（之前只有通用铃铛）。新增 `NotificationMarkAllRead` 平台动作，保持 headless 可测。
- **首页状态**：`IslandContent` 新增真实 `IsWifiEnabled`/`IsBluetoothEnabled`（来自 Radio 服务）；Wi-Fi 不再用 `IsOnline` 代替无线电状态，蓝牙不再恒为关。首页天气 chip 改用小组件页同一份真实天气；禁用的 Focus 磁贴不再作为可点击命中目标（之前只写一条 control-unsupported 日志）。
- **托盘/热键**：托盘菜单接入 `LocKeys.Tray.*`（中英随语言），新增独立「隐藏」项；`Ctrl+Alt+H` 走 `HandleHideHotkey` 而不是 toggle；`HotkeyGesture.ToNativeModifiers` 补 `MOD_NOREPEAT`，长按不再重复触发。
- **测试**：新增 `IslandMouseHook.GestureTracker` 6 个用例、`FullscreenDetector` 7 个用例；Platform 115 项定向测试通过；全量 `dotnet test` 2600 passed / 0 failed（Architecture 的 `KugouMemoryPosition.Thread.Sleep`、Integration 的 notification rank 断言，均不由本轮改动引入）。


## 2026-10-21 — 修复灵动岛动画卡顿（帧时钟被 50 ms 截止计时器反复停掉）

- **根因**：`SkiaIslandHost.EnsureFrameLoop` 把「上一次渲染尝试有没有画出像素」(`_lastRenderHappened`) 当作停止条件；50 ms 的 `OnDeadlineTick` 每个 tick 都会走一次不推进动画的 `RenderAllLocked`，于是每 ~50 ms 把 60 fps 帧时钟停掉一次。展开/悬停/波浪动画只能画 2–3 帧就冻住，整岛实际帧率 3–13 fps；Avalonia 设置窗口有独立渲染线程，所以不受影响。
- **修法**：帧时钟只由动画状态决定（新增 `IslandFrameLoopPolicy.ShouldRun(animating, agentAnimating, keepIdleFrames)`）；`RenderAllLocked` 改为返回「是否真的画了一帧」，空帧计数只用这个返回值，`_lastRenderHappened` 字段删除。
- **实测**（published exe，真实鼠标点击）：展开 380 ms 动画 24 帧、frame interval p50 **15.9 ms**（修复前 3–13 fps、最长 1 s 空洞）；展开/收起几何逐帧连续（132×36 → 440×284 → 236×36）；空闲 CPU 12.8% → **2.1%**，工作集 330 MB → 199 MB。
- **回归测试**：`tests/FluidBar.Ui.Tests/IslandFrameLoopPolicyTests.cs`（帧时钟真值表 + 展开动画跨 deadline tick 不中断 + 静止后不再请求帧）。
- 顺带修正过期基线：`PowerModeProbe` 仍断言小岛环 120°/s，实际已统一为 `AgentSpinner` 的 400°/s（0.9 s/圈）；`IslandMetrics.SplitRingPeriodSeconds` 注释同步。
- 基线：**2522 passed / 0 failed**（Core 640 · Platform 660 · Ui 497 · Island 445 · Sdk 167 · PluginHost 39 · Builtin 25 · Integration 31 · Architecture 18）。

## 2026-10-21 — 修复展开后媒体卡片按钮全部点不动（点击区域不跟随动画壳体）

- **根因**：可点击区域（分层窗口命中模型 + 全局鼠标钩子矩形）只在 `RegionSignature` 变化时重建，而签名里只有 `ShellForm`，**没有壳体尺寸**。展开动画 380 ms 里壳体每帧在变大，签名却不变；只要媒体/Agent 动画让 `IsAnimating` 一直为真，签名就再也不变 —— 钩子仍然只把旧药丸那一小块当可点区域，面板主体的按钮（媒体卡播放/上一首/下一首、开关、列表行）全部穿透到桌面。
- **修法**：签名抽成 `IslandHitRegionSignature`（含壳体宽高，单位 0.1 DIP）；动画期间每帧刷新命中区域（`UpdateHitRegions` 是纯内存模型，无 Win32 调用）。
- **证据**（临时输入探针，已删除）：修复前点媒体卡播放键根本没有 `hook-down`；修复后同一坐标得到 `PageControl(PlayPause)` → `MediaTogglePlay`。通知页/媒体页 seek 一直正常，只有壳体扩大后的按钮受影响。
- **回归测试**：`tests/FluidBar.Ui.Tests/IslandHitRegionSignatureTests.cs`（展开改变签名 / 过渡期每帧改变 / 相同状态签名稳定）。
- **顺带**：媒体回退探测（酷狗无 SMTC）原来每个 250 ms tick 都整树遍历 MSAA（约 900 节点跨进程调用，采样占全进程 66%）。给"找时间文本"的整树遍历加 1 s 节流（按钮已缓存，不受影响）：整机 CPU 2.06% → **1.25%**。
- 基线：**2525 passed / 0 failed**（Core 640 · Platform 660 · Ui 500 · Island 445 · Sdk 167 · PluginHost 39 · Builtin 25 · Integration 31 · Architecture 18）。
- Release：`build/release/win-x64/FluidBar.exe` 57.4 MB，SHA256 `D929B1453F2EC329DC7BF45BC56C806E4ECE985ADE2D7EB6458358DC4A1481A4`。
- 决策：工作集 ~191 MB 高于 D12 的 120 MB 目标，属原生内存（Skia/DIB/字体），单独一轮 native 审计，不在本次热修范围。

## 2026-10-21 — 分裂小岛：点哪个岛就开哪张卡

- **现象**：分裂模式下主岛显示的是音乐，点开却是 Agent 卡片。
- **原因**：`IslandHitKind.Compact` 只发了 `IslandInput.PrimaryClick()`，展开页由调度器的 `PrimaryPage` 决定；当后台刚来了一个更新的活动（Agent），调度器的主页就是它，和药丸**实际画的内容**不一致。
- **修复**：点击时先取药丸当前绘制内容的 `_compact.Kind.ToPage()`，展开后若与 `State.Page` 不同，追加 `PageRequested` + `HandlePageRequest`，与副岛（`ActivateSplitIslet`）同一套逻辑。
- 测试：Ui 502 全绿。发布 `EFE77288…936B`。
## 2026-10-21 — 崩溃修复：插件页返回后整程序退出（"启动后没有灵动岛"）

- **根因**：`SettingsSectionBuilder.BuildPlugins` 的 `ShowList()` 每次都 `new Border { Child = search }`。从插件详情返回列表时，`search` 仍挂在旧 Border 上，Avalonia 抛 `InvalidOperationException: The Control already has a parent`，未捕获 → 进程退出（Windows 事件日志 CLR20r3 / .NET Runtime 已确认）。
- 连续两次崩溃后 `CrashGuard` 进入 safe mode，用户看到的就是"程序启动了但没有灵动岛"。
- **修复**：搜索框宿主 Border 只创建一次（`searchHost`），`ShowList` 复用它，不再重新挂父级。
- **运维**：清掉 `%LOCALAPPDATA%\FluidBar\state\crash-guard.json` 复位安全模式；已重新发布并确认岛窗口存在（hwnd rect 609,-12,488x384，visible=True）。
- 测试：Ui 502 全绿。
- 教训：本地化 JSON 与 Avalonia 的"控件只能有一个父级"都属于**运行期才炸**的坑，改设置页后手动点一遍"插件 → 详情 → 返回"。
## 2026-10-21 — 媒体链路：卡拉OK 精度 + 只读进度条

- **秒跳变中点锚定**：酷狗的无障碍时间文本只有 1 秒分辨率，旧实现把"读到 13"当成"此刻=13.000s"，平均偏晚半个轮询（~125 ms）且方向恒定，正是"歌词总比唱的慢"的来源。`PlayerTimeline` 新增 `Age`，秒数跳变时按两次读数的中点回填，`PositionSampledAtUtc` 相应前移。
- **歌词偏移**：`AppSettings.LyricOffsetMs`（±800 ms，50 ms 步进），在 `SyncKaraoke` 锚点时叠加，用于补偿每首歌残余的压制偏移。设置 → 岛屿 分组新增「歌词偏移」滑杆（中英双语，`Settings.Island.LyricOffset`），也可直接改 `settings.ui.json` 的 `appearance.lyricOffsetMs`。
- **只读进度条**：`MediaSessionData`/`MediaPageModel` 新增 `CanSeek`（来自 `MediaSnapshot.CanSeek`）。不可 seek 的音源（酷狗，无 SMTC）进度条降到 45% 不透明度、旋钮缩小，`SkiaIslandControlRouter` 不再产出 `MediaSeek` —— 不再假装能拖。
- 测试：Island 446（新增 `MediaSeekReadOnlyTests`）· Ui 502 · Platform 660 全绿。
- 教训：本地化 JSON 手改漏逗号会让整套 Avalonia 测试全线失败（解析期抛错），改完必须 `JSON.parse` 校验一次。
## 2026-10-21 — 媒体配色/Agent 小岛/播放态/设置窗口 四个修正

- **配色**：封面主色改为「主色调聚类 + 可用性归一化」（`ArtworkAccent`）。原来对所有像素做加权平均，彩色封面会被平均成米灰（波形条发白灰），饱和度 <0.18 又让逐字高亮退回主题粉。现在按色相聚类取占比最大的簇，并把饱和度提到 ≥0.55、明度夹到 0.62–0.92；逐字高亮与波形条都吃这个颜色。
- **Agent 主岛**：`IsAgentAnimating` 原来在指针离开且无副岛环时直接返回 false → 主岛圆环冻结、只有悬停才转。现在主岛自身是运行中的 Agent 时始终请求帧。
- **Agent 副岛**：副岛环改用主岛药丸的细环比例（1.5/9.25 ≈ 0.162，原来用 0.3158 显得肥厚），并补一圈 0.18 透明度的底环，和主岛同一个圆环语言。
- **播放态**：酷狗类回退源在 MSAA 读写含糊时（播放/暂停两个字形同时可见）会用峰值启发式猜状态，导致图标偶尔反了；现在保留上一次确认的字形状态，启发式只用于首帧。
- **设置窗口**：窗口已存在但被最小化时，`BringToFront` 只 `Activate()`，Windows 不会还原 → 点设置像没反应。现在先 `WindowState = Normal` 再激活（必要时 `Show()`）。
- **Agent 主岛帧率**：主岛环常转后按 `IsAgentOnly` 把帧率压到 30 fps（20 DIP 的环在 30 fps 下看不出差别，60 fps 白烧一倍 CPU）。
- 测试：**2527 passed / 0 failed**（Core 640 · Platform 660 · Ui 502 · Island 445 · Sdk 167 · PluginHost 39 · Builtin 25 · Integration 31 · Architecture 18）。
- Release：57.4 MB，SHA256 `6D0B2466706DC02BC9FD6F136E91090AFFA457D86EAA38EDE4F74F92329BA62E`。

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

### 2026-10-19 00:28 — 生产 smoke + 托盘/热键 + dock 对齐
- 生产 Skia 宿主 smoke（`FLUIDBAR_SMOKE=1 FLUIDBAR_HOST=skia`）：9 个状态、197 帧、0 提交失败、surface 732×576 @150%、脏帧 130、上传 193–223µs、空闲无帧；展开 home/media/notifications/clipboard/agent/widgets 与中文均正常。
- 新增 tray icon + `HotkeyService`：Ctrl+Alt+D 注册成功；Ctrl+Alt+H/S 被占用时回退 Ctrl+Alt+Shift+H/S。
- 修复 `LayeredSurfaceSize.ShellLocalOffset(Dock)` 与 Core dock 组合不一致（旧值 368，现 24），Platform 371 tests 通过。
- 剩余：页面控件平台 seam、多显示器热插拔、UIA/IME、真实热键注入复测、settings 热键可配置。

### 2026-10-19 00:50 — 页面控件 / 多显示器 / DPI / dock 完成
- 页面控件平台 seam：`IIslandPlatformActions` + `SkiaIslandControlRouter` + `SkiaPlatformActions`；媒体播放/上一首/下一首/seek、音量、亮度、主题切换、剪贴板复制已接；通知/Agent/任务/日历为 deferred 事件；Wi-Fi/BT/Focus/output/queue 明确 unsupported。
- 多显示器：`SkiaMonitorPlanner` + `SkiaIslandHost` 热插拔 reconcile（Primary/All、稳定 primary、300ms debounce、移除/新增/保留、DisplayOn 变更）。
- DPI：`WM_DPICHANGED` 先应用 OS 建议再通知 App；surface 按 ceil(DIP×scale) 重建；150%/200% 数学与 placement 测试通过。
- dock：`PlaceForWindowPlacement` 统一 float/flush/dock；Platform 组合常量与 Core 对齐（panel 24..308、strip 316..368、arch 378..408）。
- 测试：**1402 passed / 0 failed**（Core 473, Platform 382, UI 159, Island 135, SDK 152, PluginHost 36, Builtin 23, Integration 31, Architecture 11）。

### 2026-10-19 00:56 — 发布脚本 / 应用图标
- 新增 `tools/release/publish.ps1`：win-x64/win-arm64、单文件、可选 self-contained、复制 README/PROJECT-NOTES/CHANGELOG/插件文档、生成 SHA256 + publish.json + zip。
- 新增 `src/FluidBar.App/Assets/fluidbar.ico` 并设置 `ApplicationIcon`。
- 首次 Release 发布验证：`build/release/win-x64/FluidBar.exe` 53.5 MB（framework-dependent single-file），SHA256 7DC72D16…D1730。

### 2026-10-19 00:58 — Release build smoke
- `build/release/win-x64/FluidBar.exe`（framework-dependent single-file，53.5 MB）以 `FLUIDBAR_HOST=skia FLUIDBAR_SMOKE=1` 运行通过：241 帧、0 提交失败、surface 732×576 @150%、上传 164–182µs、空闲无帧。
- 说明发布产物可正常启动分层宿主；下一步补 UIA/IME、插件运行时、Wi-Fi/BT/Focus 服务与完整验收。

### 2026-10-19 02:12 — 插件运行时 + FollowMouse/锚点拖拽
- 真实 PluginHost/Marketplace 接入 App：插件发现/加载/启用、市场浏览/安装/回滚、权限同意、插件设置页；设计态服务保留为离线回退。
- FollowMouse 目标选择（cursor→monitor seam）与锚点拖拽持久化（click-vs-drag 阈值、吸附/写回、恢复）接入 Skia 宿主。
- 修复插件生命周期 dispose 未走 Unloading→Unloaded；修复 App 控制测试对 deferred compact click 的假设。
- 全量：`dotnet test FluidBar.sln -m:1` = **1423 passed / 0 failed**（Core 491, Platform 382, UI 159, Island 135, SDK 152, PluginHost 39, Builtin 23, Integration 31, Architecture 11）。
- 生产 smoke：331 帧、0 提交失败、surface 732×576 @150%、上传 ~192µs、空闲无帧。

### 2026-10-19 02:50 — UIA 无障碍 + Wi-Fi/BT/Focus 服务
- UIA：`FluidBar.Platform.Windows.Accessibility`（UiaElementProvider/UiaInterop/UiaNative/UiaTypes）+ `LayeredWindowHost.Accessibility` + `SkiaIslandUiaSource`；根元素/页面/控件/小岛树、Invoke/ExpandCollapse/Selection/RangeValue/Toggle 模式、WM_GETOBJECT、键盘焦点；文档 `contracts/island-uia.md`。
- 快捷开关：`RadioToggleService`（Wi-Fi/Bluetooth，Windows.Devices.Radios）+ `FocusAssistService`（FocusSessionManager，LAF 受限时显式 unsupported）；`SkiaPlatformActions` 接线；平台测试 +13。
- 构建：`WindowsSdkPackageVersion=10.0.26100.57`（FocusSessionManager 投影；OS floor 仍 19041）。
- 全量：**1475 passed / 0 failed**（Core 491, Platform 409, UI 184, Island 135, SDK 152, PluginHost 39, Builtin 23, Integration 31, Architecture 11）。

### 2026-10-19 03:02 — v2.0.0-mvp 发布
- 发布说明：`docs/rewrite/RELEASE-v2-mvp.md`；Release 产物 `build/release/win-x64/FluidBar.exe`（55.2 MB，SHA256 65324EE8…CB44），发布 smoke 通过（238 帧/0 失败/上传 ~153µs）。
- 全量测试 **1475 passed / 0 failed**；UIA/Wi-Fi/BT/Focus/插件运行时/FollowMouse/锚点拖拽均已接入。
- 已知限制：Focus LAF、output/queue、IME/MSAA、真机多显示器/200%、Narrator 手工验收、市场签名密钥、双 message host、托盘图标。

### 2026-10-19 03:08 — MVP 发布包
- `tools/release/publish.ps1` 生成 `build/release/FluidBar-win-x64.zip`（47 MB）；exe 56.5 MB，SHA256 86A4F33A…A4021；Release smoke 通过。

### 2026-10-19 12:30 — 可编辑全局热键 + 实时重注册
- `AppSettings` 新增 `HotkeyToggle/HotkeyHide/HotkeySettings`，设置 → 通用改为可编辑 TextBox（Enter/失焦提交），JSON 往返与默认回退有测试。
- `SkiaShellIntegration.ApplyBindingsAsync`：保存后立即 unregister/register，无需重启；冲突时回退 Ctrl+Alt+Shift。
- 修复 AppBootstrap 退出时未释放 tray/hotkey 集成。
- 全量：**1477 passed / 0 failed**（UI 186）。

### 2026-10-19 12:35 — 最终发布校验
- 最终 Release：`build/release/win-x64/FluidBar.exe` 56.5 MB，SHA256 `DA422BA5…37362`；发布 smoke 420 帧、0 提交失败、surface 732×576 @150%、上传 ~273µs。
- 全量 1477 tests 绿；热键可在设置中编辑并实时重注册。

### 2026-10-19 13:45 — 可用性热修（顶层窗口 / 托盘菜单 / 点击）
- **根因 1**：`LayeredWindowHost.Show()` 使用 `SWP_NOZORDER`，岛窗口虽带 `WS_EX_TOPMOST` 但没有真正进入 topmost 带；其他窗口可能盖在上面，导致点击落不到岛、视觉却仍可见。现在 `Show()` 用 `SetWindowPos(HWND_TOPMOST, ...)` 强制置顶。
- **根因 2**：托盘菜单使用 message-only HWND，`TrackPopupMenu` 无法在 `HWND_MESSAGE` 上显示；托盘窗口改为 `PlatformWindowKind.HiddenTopLevel`，右键菜单可正常弹出。
- 紧凑岛改为**按下即展开**（拖动移动作为次级手势），避免 click-vs-drag 让单击感觉无响应；命中区域保留 8 DIP 防御性 padding。
- 测试：1478 passed / 0 failed；Release smoke 330 帧/0 失败。
- 注意：如果桌面上还有旧版 FluidBar 进程/自启动项，请先全部退出再运行新 exe。

### 2026-10-19 14:40 — 真实数据源接入（修复"贴图/假剪切板"）
- 根因：`SkiaPlatformActions` 构造函数在 `_focus` 赋值前订阅 `_focus.SnapshotChanged`，抛 NullReferenceException；`AppBootstrap` 捕获后回退到 `DesignIslandDataSource`（演示数据），所以剪贴板/通知都是假的。
- 修复：调整构造顺序；`SkiaPlatformActions` 实现 `IIslandDataSource`，`Snapshot()` 映射真实剪贴板历史、GSMTC 媒体、电池/充电/专注状态；通知/Agent/任务不再伪造，返回空列表。
- 剪贴板行复制改为按索引取**完整文本**（不再复制渲染预览）；媒体播放/seek/音量/亮度/主题继续走真实平台服务。
- 验证：`actions created` 日志确认真实数据源已启用；1478 tests（PluginHost 偶发 flaky）；Release 构建通过。

### 2026-10-19 16:40 — 修复“状态已展开但画面停在紧凑药丸”（渲染提交根因）
- **根因（已用独立 Win32 实验复现）**：`UpdateLayeredWindowIndirect` 在 `pptDst = psize = NULL`（隐式几何）时返回 TRUE 但**分层表面完全不更新**。宿主只在首帧（`_geometryDirty` 为真）显式传几何，所以只有开机第一帧真正上屏；之后展开/翻页/收起的提交全部“成功但不可见”。
- 修复：`LayeredWindowHost.TryUpdate` 每次提交都显式传 `pptDst`/`psize`（每帧一次 `GetWindowRect`，相对 DIB 上传可忽略）；删除失效的 `UseExplicitUpdateGeometry` 选项与 `_geometryDirty` 死状态。
- 恢复弹簧动画：`OnStateChanged` 不再 `SnapMotion()`，展开/收起走 Core 弹簧；实测点击后连续 4 帧可见尺寸增长（132 → 中态 → 440×284）。
- 恢复点击穿透：`IslandMouseHook` 命中矩形从整个 488×384 宿主改回 shell 矩形（+2 DIP 容差），透明 bleed 区域点击回到下方窗口（`WindowFromPoint` 验证）。
- 悬停不再被自动收起打断：Core `EnterExpanded` 在指针悬停时不武装 `AutoCollapseDeadline`；展开态 `PointerEntered` 清除、`PointerLeft` 重新武装；钩子新增 `PointerLeft` 事件（分层窗口可能收不到 `WM_MOUSELEAVE`），控制器把展开态纳入 hover 派发。
- 修复紧凑药丸“宽度与内容不一致”：活动在展开态结束时，Core 现在从清空后的 baseState 进入 Closing（`ContentKind`/`ActivityCount` 不再残留）；App `BuildTarget` 与 `BuildCompact` 统一用 view model 的活动类型取宽度。
- 空闲时钟恢复走字：`SkiaPlatformActions` 增加对齐到分钟边界的 `ITimer`，每分钟 raise `Changed`（此前只有媒体/剪贴板/电池变化才会重绘，药丸时间会冻在首次绘制那一刻）。
- 清理上一轮遗留的全部临时日志（`fluidbar-trace.log` / `fluidbar-hook.log` 写入点、`Trace()` helper）。
- 测试：Core 494（+3 状态机回归）、UI 188（+1 音量/亮度映射）；`dotnet build FluidBar.sln` 0 错误。

### 2026-10-19 17:05 — 真实音量/亮度 + 物理显示器枚举 + 最终发布
- 首页音量/亮度不再写死演示值（62/78）：`IslandContent` 新增 `VolumePercent`/`IsMuted`/`BrightnessPercent`，`SkiaPlatformActions` 映射真实 WASAPI 音量与 DDC/CI 亮度并订阅其变化；未知时渲染 "—" 且轨道归零（不伪造数字）。设计态 `DesignIslandDataSource` 保留 62/78 供预览/测试。
- `MonitorService` 枚举前显式 `SetThreadDpiAwarenessContext(PMv2)`：显示器枚举不再受调用线程 DPI 上下文影响（线程池线程/消息线程下曾可能拿到 96 DPI 的虚拟化工作区）。
- 全量测试 **1482 passed / 0 failed**（Core 494 · Platform 409 · SDK 152 · UI 188 · Island 135 · PluginHost 39 · Builtin 23 · Integration 31 · Architecture 11）。
- 最终 Release：`build/release/win-x64/FluidBar.exe` 56.5 MB，SHA256 `D5A02A4DBD5F64563878D646AD35C0A42ECE3CB8B0BEFD37135CD9496734992E`。
- 真实鼠标回归（DPI-aware 取证脚本）：窗口 732×576 @150% → 点击药丸弹簧展开 660×424 → 悬停 4s 保持 → 移开 3.8s 自动收起 196×52 → 再展开 → chevron 收起；首页显示真实音量 60 / 亮度 "—" / 电量 99% 充电 / Wi-Fi 开。
- 鼠标钩子自愈：`IslandMouseHook` 独立消息线程（避免和剪贴板/显示消息排队导致 WH_MOUSE_LL 回调超时被系统摘除）+ 1s 看门狗（光标位置变化但 800ms 内没有回调 → 重新安装钩子）。60s 连续 6 轮点击/热键回归全部通过。
- 注意：`FLUIDBAR_SMOKE=1` 会把 `settings.ui.json` 的 `autoCollapseMs` 改成 0（smoke 需要），跑完冒烟记得改回 2600，否则展开态不会自动收起。
- 展开态悬停时不再因“活动结束”被强制收起（临时剪贴板/通知活动到期不应把用户正在操作的面板关掉）；内容原地更新，指针离开后走 2.6s 自动收起。
- 鼠标钩子改为在首帧渲染完成后才安装：此前启动早期（控制器还没有 render state）钩子会吞掉点击但不处理，表现为“启动后第一次点击没反应”。3 次冷启动 3 秒内首点全部成功。

### 2026-10-19 18:10 — 岛下方预览动效 + 真实亮度 + 媒体封面/歌词/进度
- **岛下方常驻预览**：展开且未切换时显示 6 个很淡的小点（4 DIP，α≈0.20），当前页为 14×4 胶囊（α≈0.52）并跟随翻页连续滑动；滚轮/拖拽/热区唤出 3 岛胶片条时小点先淡出、胶片条错峰淡入（当前岛先、邻岛随后并轻微外滑），换页时当前岛 1.06× 脉冲、标签淡出淡入。收起时全部隐藏。
- **亮度根因修复**：WMI 的 `CurrentBrightness` 不能直接当 IDispatch 属性读（`instance.CurrentBrightness` 永远为空），必须走 `instance.Properties_.Item("CurrentBrightness").Value`。修复后本机真实亮度 50 可读、滑块可写（`WmiSetBrightness`）。
- **媒体真实封面**：GSMTC `ArtworkCache` 的原始字节进入 `MediaSessionData`，岛内 `ArtworkImageCache`（按 track key，LRU 4）解码为 `SKImage`，媒体页大图与首页卡片小图都画真实封面（无封面时回退渐变占位）。
- **媒体进度**：用 `Position + (now - PositionUpdatedAtUtc)` 插值；有播放会话时 1 Hz tick 刷新进度条与歌词行（无会话时不创建定时器）。
- **歌词**：复用 `LyricsService`（网易云/酷狗 + LRC 解析 + 30min 缓存），按插值位置取当前/下一行；取不到就隐藏，不伪造。
- **浏览器媒体验证**：用独立 Edge 实例 + Media Session API 页面实测，GSMTC 正确上报标题/艺术家/专辑/封面/进度；岛首页卡片显示封面 + “Midnight City / M83” + 走动进度条，媒体页显示大封面、标题、歌词（网易云命中）与 0:04/0:05 进度。
- 展开时按下指针会同步 hover 状态，避免“展开后在光标下自动收起”。
- 测试：**1484 passed / 0 failed**（Core 495 · Platform 409 · SDK 152 · UI 188 · Island 136 · PluginHost 39 · Builtin 23 · Integration 31 · Architecture 11）。
- 胶片条自动隐藏 1000 → **1200 ms**（设计区间上限），切换动画可读性更好。
- **拖拽翻页恢复**：全局钩子吞掉按下事件后，分层窗口拿不到鼠标捕获，pager 拖动一直失效；现在钩子在按住期间转发 `PointerDragged`，宿主用同一套 `HandleDragStarted/Moved/Ended` 重建拖拽手势。实测按住拖动 300px 从 04 翻到 05，胶片条同步出现。
- Release：`build/release/win-x64/FluidBar.exe` SHA256 `65A793359FFEA8A2F11F65F34F7A9E770837FA7E61954B784348CF3C01360F97`。

### 2026-10-20 04:35 代理事件链路真机验证通过
- 在应用运行中向 `%APPDATA%\FluidBar\agent-events` 写入 `{"agent":"deepseek-harness","event":"Stop"}`，约 0.9 s 后岛上出现紧凑胶囊：**「DeepSeek Ha… 」+「已完成 ●」**，证明 收件箱 → 代理插件 → 岛活动 全链路打通（含新加的 DeepSeek Harness 类型）。
- 之前的「链路不通」是验证方法问题：活动有存活时长，启动前写入的事件会在启动瞬间播放并很快过期，截图时已看不见。
- 已知瑕疵：胶囊标题被截断成「DeepSeek Ha…」，需要加宽或缩短显示名（下一轮）。

### 2026-10-20 04:05 代理页显示事件时间
- `AgentItemData.TimeText` 之前恒为空，代理列表看不出事件先后；现在按收件箱事件的本地时间填 `HH:mm`（与界面语言无关，不需要新翻译键）。
- 与上一项合计：代理页每行 = 代理名 + 状态（运行中/已完成/失败/等待输入）+ 时间 + 详情。
- 全量 **1493 passed / 0 failed**。
- Release：`build/release/win-x64/FluidBar.exe` SHA256 `E8FA7342D1513E7DA9720E3681CF964CC645A06B790551C8B79CBFDC8F7FAF40`。

### 2026-10-20 03:40 代理插件支持 DeepSeek Harness
- 钩子事件里的 `agent` 字段现在识别 **deepseek / deepseek-harness / dsh / harness**，映射到新的 `AgentKind.DeepSeek`；此前所有非 Claude/Codex 的代理都会落到 `Generic`，岛上只能显示成「Agent」。
- 代理页显示名「DeepSeek Harness」，与 Claude Code、Codex 并列。
- `AgentTests` 增加 3 个解析用例（Platform 415 -> 418）。
- 全量 **1493 passed / 0 failed**。
- Release：`build/release/win-x64/FluidBar.exe` SHA256 `09D27B541AFD858CC34BC9F239A7FB78F2949E2A52CA498B39494A061CFE43BA`。

### 2026-10-20 03:10 — 插件页布局修复
- **搜索框跑到中间**：`TextBox` 默认 `HorizontalAlignment=Stretch`，一旦显式设置 `Width` 就会居中。改为 `Left` 对齐并加下边距。
- **每行标题错位**：名称/版本/「内置」标签原来放在横向 `StackPanel` 里，导致版本和标签被挤到第二行、行高被撑高。改成 `Auto,Auto,Auto` 的 `Grid` + 8 DIP 列间距，三者回到同一行。
- **行内文案合并**：发布者与能力说明合成一行（`FluidBar · 读取当前媒体会话 · 在后台运行`），行高从约 110 px 降到 67 DIP，接近原型密度。
- 全量 **1490 passed / 0 failed**。
- Release：`build/release/win-x64/FluidBar.exe` SHA256 `774535C6631E726246187857A8C860B2A9D1297689D12488FCC15C2D8B57CB0D`。

### 2026-10-20 02:40 — 设置「外观」页与原型一致
- **外观页只留原型的 5 行 + 预览**：桌面主题（两段：深色 / 浅色）、灵动岛材质（黑曜石 / 亚克力）、强调色（5 个色点）、大面板圆角（滑杆 + 数值 chip）、填充不透明度（滑杆 + 数值 chip），下面是「灵动岛预览」大面板与页脚说明。
- **副标题改用原型文案**：「桌面主题与灵动岛材质。浅色主题下灵动岛仍保持黑曜石，以确保内容对比度。」
- **能力没丢**：调色板、强调色跟随封面、字号比例、减少动效、动效强度、主题跟随系统全部移到「高级」页的同一分组里。
- 全量 **1490 passed / 0 failed**。
- Release：`build/release/win-x64/FluidBar.exe` SHA256 `D0AEB9E99EADEE62BF8F0DA2F676926C758AE0956D0AEEC66774DFE780B63857`。

### 2026-10-20 02:05 — 设置「通用」页与原型一致
- **通用页改为原型的 4 行结构**：界面语言（两段：简体中文 / English）、随 Windows 启动（开关）、**自动检查更新（开关，原为三段）**、显示托盘图标（开关）。移除了原型没有的「匿名使用数据」行。
- **副标题改用原型文案**：「启动方式、更新策略与全局快捷键。」
- **能力没丢**：跟随系统语言、更新方式（通知我/自动安装/从不检查）、匿名使用数据说明都移到「高级」页新分组，功能完整保留。
- 全量 **1490 passed / 0 failed**。
- Release：`build/release/win-x64/FluidBar.exe` SHA256 `A45FE7DFA250AEC81D7728FEC412F3E2ECFA080C4EA73804804758ADFFF73EFB`。

### 2026-10-20 01:35 — 设置窗口密度对齐原型
- **键帽**：22 DIP 高、圆角 5 → **18 DIP 高、圆角 4、8 px 字号、20 最小宽**（原型快捷键 chip 为 24×16 CSS）。
- **行密度**：设置行最小高度 53 → **46**，行内上下留白 8 → 6，让首页信息量与原型一致（原型整页无滚动条）。
- 这轮同时确认了小胶囊封面/进度与小岛条尺寸（见上一条目）。
- 全量 **1490 passed / 0 failed**。
- Release：`build/release/win-x64/FluidBar.exe` SHA256 `94D2FBA8EEE34A0C327DEF8AD3E4CBDBD4F84DDEBB76BBC4A83CC678F80510C6`。

### 2026-10-20 01:05 — 小胶囊封面/进度 + 小岛条按原型尺寸
- **小胶囊媒体封面**：`CompactModel` 之前完全没有封面字段，媒体活动只画了渐变占位图 + 音符图标。现在补上 `Artwork`/`ArtworkKey`，从 `IslandPresenter.ResolveMedia` 取真实封面字节，`CompactRenderer` 复用 `ArtworkImageCache` 裁圆绘制（与媒体页同一套图）。
- **小胶囊进度条恒为 0**：进度以前只取活动负载里的 `ProgressPercent`（常有缺失），现在改为取媒体快照的真实进度；`HasTimeline=false` 时**不再画空进度条**（之前画的就是那条永远为 0 的杠）。
- **下方小岛条尺寸对齐原型**实测值：左右预览岛 **26×26**（原 40×24）、当前岛 **73×28**（原 76×24）、岛间距 8、远点与岛间距 7（原 8），点直径 3、点距 4 不变。
- 全量 **1490 passed / 0 failed**。
- Release：`build/release/win-x64/FluidBar.exe` SHA256 `98DDFD87158CC4071DB8DFF492E6CDC65046876ED9BCF4F83A37AC60B6E14773`。

### 2026-10-20 00:10 — 媒体页：浏览器视频不再显示错误歌词
- **歌词来源收窄**：浏览器媒体会话默认是视频，之前拿视频标题/作者去匹配歌词库，会把无关文本（例如视频字幕「下一步」）当歌词显示。现在只有**暴露专辑元数据的浏览器来源**（音乐站点）才做歌词匹配。
- **空歌词卡片**：媒体页在没有歌词时不再画一张只剩「下一步」标签的空卡，版面更干净。
- 全量 **1490 passed / 0 failed**。
- Release：`build/release/win-x64/FluidBar.exe` SHA256 `79CC1477F6B3935793154757266E5903D374059282682030A493EC349B052B02`。

### 2026-10-19 23:20 — 小岛条动效对齐原型
- **弹簧参数**：小岛条的展开/位移/缩放原来复用 Hover 弹簧（T0.24 / ζ0.70，偏弹），改为原型的 RAIL_SPRING **T0.32 / ζ0.85**（新增 `MotionTokens.Rail`）。
- **自动隐藏**：最后一次操作后 1200 ms → **900 ms**（原型规范），Core 的 `ArchipelagoHideDelayMs` 与 App 常量同步，策略测试边界从 1199/1200 改为 899/900。
- 全量 **1490 passed / 0 failed**。
- Release：`build/release/win-x64/FluidBar.exe` SHA256 `7DC93ADEDD6C43D75EEA6775DCD26FC3A57A2563D0642EBB3D1783EC4801A7EC`。

### 2026-10-19 23:05 — 灵动岛页面本地化 + 空态
- **中文界面下的英文残留**：日期格式改为「10月19日 周一」；蓝牙标签、主题切换标签（深色/浅色，之前误显示「系统」）走本地化；通知/剪贴板/代理/媒体四个页面的空态标题与提示全部接本地化键（之前渲染器回退成英文）。
- **首页媒体卡空态**：没有媒体时显示「没有正在播放的内容」，不再是一张只有封面占位和播放键的空卡。
- 与上一项合计：小岛条胶囊化 + 远点命中修复。
- 全量 **1490 passed / 0 failed**。
- Release：`build/release/win-x64/FluidBar.exe` SHA256 `3701805DC062CE4DE31074D8F0C0292E2825CDA8DC2D7BFD3EF9F5749BB4A6B8`。

### 2026-10-19 23:10 — 下方小岛条按原型对齐
- **小岛尺寸/形状对齐 prototype2**：左右预览岛由 26×26 改为 **40×24 胶囊**，当前岛 64×24 → **76×24**（短标题不再被截断），远点间距 5 → **4**，当前岛标签由强调色改为**白色**。
- **远点点击修复**：远点命中区原来两个方向都按 6px 膨胀（12×12），在 4px 间距下相邻命中区重叠，点最远的点会命中相邻点。改为原型的 **7×15** 命中区（水平 ±2、垂直 ±6），岛测试恢复全绿。
- 与上一项合计：`AppSettings.PanelRadius`（16–36）贯通渲染、外观改动即时重绘、插件不可用文案本地化。
- 全量 **1490 passed / 0 failed**。
- Release：`build/release/win-x64/FluidBar.exe` SHA256 `DA8A34E9D28137987EB6297A54EDB118D7C6796B43F03BE7492EE893B6F9DD59`。

### 2026-10-19 22:55 — 设置：大面板圆角可调 + 插件错误本地化
- **大面板圆角**：新增 `AppSettings.PanelRadius`（16–36 DIP，默认 28，持久化到 `appearance.panelRadius`），经 `LayoutTokens.RadiiFor` → `SkiaIslandMotion.PanelRadius` → `IslandRenderState.ShellRadii` 贯通到灵动岛渲染；外观页新增「大面板圆角」滑杆（16px / 28px 拐角实测不同）。顺带修复外观类设置改动后灵动岛不重绘的问题（`OnSettingsChanged` 现在重建渲染状态并请求整帧）。
- **插件不可用文案本地化**：运行时改为返回 `Settings.Plugins.ErrorPlatformServiceUnavailable` 键，插件页解析为中文/英文，不再直接显示英文诊断。
- 全量 **1490 passed / 0 failed**。
- Release：`build/release/win-x64/FluidBar.exe` SHA256 `E4320B0E57DC7E3B1FF63A634E4D26CB7CF265B189453E16DCE2B8FEF8E86F57`。

### 2026-10-19 22:45 — 设置：插件市场按原型重排
- **精选卡**：渐变底（青色径向光晕）+ 8px 大写 kicker、20px 标题、10px 描述、浅色安装按钮与安装量、80px 圆角图标块。
- **工具条**：34px 搜索框（放大镜 + 占位符）+ 分类 chips（全部/媒体/效率/系统/开发者/其他，选中态白底深字）。
- **双列卡片网格**：34px 分类色图标、名称 + 已验证勾、作者 · 版本、两行描述、安装量 + 安装/更新/卸载/回滚按钮；底部原型脚注（签名校验说明）。
- 全量 **1490 passed / 0 failed**。
- Release：`build/release/win-x64/FluidBar.exe` SHA256 `E5D19099F15049F334DCA9B52B309AE5AC843C3F84A5879DB11186436C7F5C23`。

### 2026-10-19 22:25 — 设置：高级 / 关于 对齐原型
- **高级**：等宽字体数据目录路径框 + 四个小动作（打开目录 / 查看日志 / 导出设置 / 导入设置）、帧统计/崩溃保护/安全模式开关、重置区（分段选择重置范围 + 输入 RESET 解锁的危险按钮，按范围执行真实重置）。
- **关于**：居中的应用标记（强调色光晕）、23px 名称、版本与构建号、检查更新 / 网站 / 源代码 胶囊按钮、许可证行、贡献者胶囊。
- **插件市场**：修复分类下拉框空白（先加选项再设选中项）。
- 全量 **1490 passed / 0 failed**。
- Release：`build/release/win-x64/FluidBar.exe` SHA256 `56AEF3A1586159000703B3BDFBBC2F346DA7C6BA318C0E7C56C9A46F03429FB2`。

### 2026-10-19 21:55 — 设置菜单按网页原型 1:1 重做
- **窗口外壳对齐 prototype2**：900×620、22px 圆角、50 DIP 标题栏（岛标 + “设置” + “FLUIDBAR 2.0” + 悬停变红的关闭键）、208 DIP 侧栏（“偏好设置” kicker、图标 + 计数徽章、底部版本块）、30/24 内容内边距。
- **控件按原型重建**：分段控件（自适应宽度、活动段白底深字）、iOS 风格开关（38×22、强调色 #4CC9F0）、3px 细滑杆 + 数值胶囊、快捷键键帽（点击可切换为编辑框）、强调色圆点（活动环）、hairline 分隔的设置行（12px 标题 / 10px 说明）。
- **分区内容对齐**：通用（语言/开机启动/更新/托盘/快捷键）、外观（主题/材质/配色/强调色/透明度/字号/减少动效/动效强度 + 渐变画布上的实时灵动岛预览）、灵动岛（模式/锚点/堆叠/悬停/自动收起/自动隐藏/切歌预览/显示于）、插件（本地化名称 + 真实启用状态 + 原型行样式）。
- **修复插件启用状态错乱**：早期版本把设计期 id（`builtin.media`）写进设置，运行时用的是 `fluidbar-media`，导致设置里全部显示“已停用”。默认值与读取迁移都改为运行时 id。
- **设置窗口置顶**：后台进程调用 `Show()` 会被前台窗口遮挡，改为单实例 + Activate + 短暂 Topmost 切换。
- 测试：**1490 passed / 0 failed**。
- Release：`build/release/win-x64/FluidBar.exe` SHA256 `4AE296D7B24C928B7C778B926965651F74FE8E619239BF49C2DCD73C03311303`。

### 2026-10-19 20:55 — 酷狗等非 SMTC 播放器识别 + 亮度写入彻底修复
- **媒体识别（酷狗）**：酷狗不注册 Windows SMTC，GSMTC 永远返回 0 个会话。新增 WASAPI 音频会话探测（`IAudioSessionManager2` → 进程 PID + Active 状态 + 峰值）+ 播放器窗口标题解析（`MediaWindowFallback`）作为回退源：仅当 GSMTC 没有活动会话时，每 1s 检查一次；识别到就发布 `MediaSnapshot`（标题/艺术家/播放中），无封面与时间轴时如实留空。
- **紧凑药丸**：媒体播放时药丸显示 标题 / 艺术家 / 波形 / 播放暂停图标（酷狗实测 “Cameras / Justice Skolnik、Jeremy Zucker”）。
- **无时间轴不伪造进度**：新增 `HasTimeline`，回退源隐藏进度条与 0:00 时间行；同时修复 `ResolveMedia` 丢失 `HasTimeline`/`Artwork` 的问题（此前媒体页会画出假进度）。
- **亮度写入彻底修复**：本机 WMI provider 在同一个 COM apartment 上第二次写入必然失败（“未指定的错误”），且失败后同进程长时间不可用。改为**每次写入都在全新的 MTA 线程/COM apartment 上执行**（写后立即释放 COM 对象），实测连续 35→50→81→5 四次写入全部成功。
- **0% 保护**：滑块最低夹到 **5%**（`BrightnessMath.MinimumPercent`），避免把屏幕调到全黑后无法恢复；读失败自动重连，不再永久禁用亮度。
- 测试：**1490 passed / 0 failed**（Platform 415，新增 6 个回退解析/亮度下限测试）。
- Release：`build/release/win-x64/FluidBar.exe` SHA256 `E1BEE457255CAF01C66423955D8B26144158EE21B3E42B3305E5F056FAA5F2C4`。
