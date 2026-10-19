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
