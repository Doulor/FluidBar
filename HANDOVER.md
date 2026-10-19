# FluidBar v2 接手文档（Handover）

> 更新时间：2026-10-19 16:40（Asia/Shanghai）
> 仓库：`D:\build\GitLocal\FluidBar`
> 分支：`v2/rebuild`
> HEAD：`b41b293 fix(host): make layered frames visible (explicit ULW geometry) and restore interaction`
> 结论先写：**渲染阻塞已解决，MVP 可运行**——分层窗口在“隐式几何”下 `UpdateLayeredWindowIndirect` 返回 TRUE 但不刷新；现在一律显式传几何。真实鼠标：点击药丸弹簧展开、悬停保持、移开 2.6s 收起、chevron 收起、滚轮翻页、岛外点击穿透；首页显示真实音量/电量（亮度不支持时显示 "—"）。全量 1482 tests 绿，Release 已发布。下一步：剪贴板行复制/媒体/开关的真实回归、托盘右键、UIA 刷新、多显示器验收。

---

## 1. 用户与产品背景（接手必须知道）

- 用户是 Doulor，唯一维护者/贡献者。要求：**质量第一**，宁可慢，不要糊弄。
- 项目：Windows 灵动岛（iOS Dynamic Island 风格），**持久功能条**，部分替代任务栏，高可定制，完整 i18n，**插件化是最大亮点**：
  - 内置编译插件 + 签名社区 DLL + 应用内插件市场/管理
  - 贡献者发布插件合并进主仓库，市场可见
- 已确定的产品方向：
  - 默认小岛 + 悬停 Peek + 点击展开 + Dock 模式
  - 不做底部 tabs、左侧 rail、页点、面板内群岛；mini-island 在面板外、瞬态
  - 3 个岛 + 远页页点；弹簧动画
  - 设置窗口/市场保留
- 用户明确要求：**中途用正文给阶段性回复**，不要一直沉默思考；每一步要能自己验证，不要只说“应该好了”。
- 文档时效性是硬要求：每次状态变化更新 `STATUS.md` / `CHANGELOG.md`（见 `PROJECT-NOTES.md` §9）。
- 当前用户情绪：多次反馈“无法使用/就是贴图”，已经很不耐烦。接手后先修可用性，不要先做新功能。

---

## 2. 技术栈与关键架构

- .NET 10 / C# 14；Avalonia 12.1.4 **只用于设置/市场窗口**，不再用于岛本体。
- 岛本体：**Win32 分层窗口**（`WS_POPUP | WS_EX_LAYERED | TOOLWINDOW | NOACTIVATE | TOPMOST`，32bpp premultiplied BGRA DIB，`UpdateLayeredWindow`）+ **SkiaSharp 3.119.4 / HarfBuzzSharp 8.3.1.3** 渲染。
- 项目分层：
  - `FluidBar.Core`：UI-free 领域/状态机/布局/分页/动画曲线
  - `FluidBar.Sdk`：插件契约
  - `FluidBar.PluginHost`：collectible ALC 插件宿主
  - `FluidBar.Marketplace`：`.fbp` ZIP + ECDSA P-256 签名索引
  - `FluidBar.Platform.Windows`：Win32/WinRT 平台服务
  - `FluidBar.App`：组合、设置/市场、Skia 岛宿主
  - `FluidBar.Island`：无 UI 依赖的岛渲染/布局
- 固定宿主表面：
  - Float/Flush：**488×384 DIP**
  - Dock：**488×444 DIP**
  - 150% 缩放：**732×576 / 732×666 px**
  - 展开壳：**440×284 DIP**；内容 400×252，20/12 inset
- 分页常量：`PointerDamping=0.85`、`SnapSpringPeriodSeconds=0.38`、`SnapSpringDampingRatio=0.94`、`PageThresholdDip=26`、`FlickVelocityDipPerMs=0.55`、wheel 140/180、scroll chain 40 DIP/120ms、edge 0.35。
- 发布：单文件 framework-dependent：`build/release/win-x64/FluidBar.exe`，发布脚本 `tools/release/publish.ps1`。

关键文件：

| 文件 | 作用 |
|---|---|
| `src/FluidBar.App/Hosting/Skia/SkiaIslandHost.cs` | 岛宿主：窗口、帧循环、输入、UIA、placement、渲染提交 |
| `src/FluidBar.App/Hosting/Skia/SkiaIslandController.cs` | Core 状态机管道、点击/拖动、页面控件路由 |
| `src/FluidBar.App/Hosting/Skia/SkiaPlatformActions.cs` | 真实平台数据源 + 页面动作 |
| `src/FluidBar.App/Hosting/Skia/SkiaShellIntegration.cs` | 托盘/热键组合 |
| `src/FluidBar.Platform.Windows/Hosting/LayeredWindowHost*.cs` | 分层 HWND、`WM_NCHITTEST`、DIB、`UpdateLayeredWindow` |
| `src/FluidBar.Platform.Windows/Hosting/IslandMouseHook.cs` | **新增** WH_MOUSE_LL 全局鼠标钩子 |
| `src/FluidBar.Platform.Windows/Shell/TrayIconService.cs` | 托盘图标/菜单 |
| `src/FluidBar.App/AppBootstrap.cs` | 宿主选择、插件运行时、组合 |
| `src/FluidBar.App/State/IslandContent.cs` | 岛内容模型（真实/演示数据） |
| `src/FluidBar.App/State/IslandPresenter.cs` | `IIslandDataSource` / `DesignIslandDataSource` |

---

## 3. 当前构建/测试状态

- 工作区有**未提交调试代码**：
  - `src/FluidBar.App/Hosting/Skia/SkiaIslandController.cs`
  - `src/FluidBar.App/Hosting/Skia/SkiaIslandHost.cs`
  - `src/FluidBar.Platform.Windows/Hosting/IslandMouseHook.cs`
- 最近一次完整测试（调试代码之前）：**1478 passed / 0 failed**。
  - Core 491、Sdk 152、Platform 409、Architecture 11、PluginHost 39（偶发 flaky，重跑通过）、Ui 187、Builtin 23、Integration 31、Island 135。
- 最新发布（调试构建，**不建议用户使用**）：
  - 路径：`build/release/win-x64/FluidBar.exe`
  - SHA256：`A0ECD9141F1B2AC080C16701367898C999133C01A91ACB9046B6D7B1FFEAF0DB`
- 当前有一个实例在跑（PID 约 37296，可能已退出，先 `Get-Process FluidBar` 检查）。

常用命令：

```powershell
# 构建
dotnet build FluidBar.sln -v q --nologo

# 全量测试（-m:1 避免并行抢桌面资源）
dotnet test FluidBar.sln -m:1 -v q --nologo

# 发布单文件（先杀掉运行中的 FluidBar，否则 exe 被锁）
Get-Process FluidBar -ErrorAction SilentlyContinue | Stop-Process -Force
pwsh -NoProfile -File tools\release\publish.ps1 -SkipZip

# 正常运行（Skia 宿主）
$env:FLUIDBAR_HOST='skia'
.\build\release\win-x64\FluidBar.exe

# 冒烟（会模拟点击/翻页并截图，不需要真实鼠标）
$env:FLUIDBAR_HOST='skia'; $env:FLUIDBAR_SMOKE='1'
.\build\release\win-x64\FluidBar.exe
# 日志：%TEMP%\fluidbar-smoke.log
# 证据：%TEMP%\fluidbar-evidence\*.png

# 强制 Avalonia 宿主（透明在用户机器上会灰块，仅排障用）
$env:FLUIDBAR_HOST='avalonia'
```

---

## 4. 已经修好的问题（有证据）

### 4.1 假数据（“贴图”/假剪贴板/假通知）

根因：
- `SkiaPlatformActions` 构造函数在 `_focus` 赋值前订阅 `_focus.SnapshotChanged`，抛 `NullReferenceException`。
- `AppBootstrap.TryCreatePlatformActions()` 捕获后返回 null，`SkiaIslandHost` 的 `Data` 回退到 `DesignIslandDataSource`，于是剪贴板/通知都是演示数据。

修复（已提交 `199c172`）：
- 调整构造顺序，移除过早的 `_focus` 订阅。
- `SkiaPlatformActions` 实现 `IIslandDataSource`：
  - `Snapshot()` 映射真实 `ClipboardService` 历史、GSMTC 媒体、电池/充电/专注状态。
  - 通知/Agent/任务返回空数组，不再伪造。
- `AppBootstrap`：Skia 宿主 `Data = (IIslandDataSource?)_platformActions ?? _data!`。
- 剪贴板行复制按索引取完整文本（`CopyClipboard(action)`）。
- 证据：真实剪贴板截图 `build/reports/skia-host-smoke/verify-click-crop.png`，显示用户真实复制过的 `FluidBar clipboard test…` 条目。

注意：
- `DesignIslandDataSource` 仍保留给 Avalonia 回退/测试/设计预览；生产 Skia 必须走 `_platformActions`。
- 若 `TryCreatePlatformActions` 再次抛异常，仍会静默回退到演示数据。**接手后第一件事建议把 fallback 改成显式日志/错误提示，避免再次“假数据”。**

### 4.2 岛窗口没有真正置顶

根因：`LayeredWindowHost.Show()` 用 `SWP_NOZORDER`，窗口带 `WS_EX_TOPMOST` 样式但没进 topmost 带，点击被上层窗口吃掉。

修复（`7692b04`）：
- `Show()` 改为 `SetWindowPos(HWND_TOPMOST, ...)`。
- 真实点击测试中 `WindowFromPoint` 已能返回岛窗口。

### 4.3 托盘 owner 窗口

- 托盘窗口从 `PlatformWindowKind.MessageOnly` 改为 `HiddenTopLevel`（`TrayIconService`）。
- 之后又把 owner 放到屏幕外 1×1 可见窗口（`SetWindowPos(-32000,-32000,1,1, SWP_SHOWWINDOW)`），让 `TrackPopupMenu` 能前台。
- **右键菜单仍未完成真实回归验证**；`WM_CONTEXTMENU` 注入未稳定弹出菜单。

### 4.4 输入层：WH_MOUSE_LL 全局鼠标钩子（新方案，已部分验证）

根因判断：
- Skia 分层窗口的 per-pixel 命中/消息投递在用户机器上不稳定：`WindowFromPoint` 有时返回岛、有时返回浏览器；`WM_NCHITTEST` 返回 `HTCLIENT` 也不代表 `WM_LBUTTONDOWN` 能稳定到。
- 已放弃继续修 layered 命中，改为**全局低级鼠标钩子** `WH_MOUSE_LL`（`src/FluidBar.Platform.Windows/Hosting/IslandMouseHook.cs`）。

实现：
- 钩子安装在 `PlatformMessageHost` 的消息线程上（`SetWindowsHookExW(WH_MOUSE_LL, ...)`）。
- 钩子按屏幕物理像素矩形判断是否属于岛；命中则：
  - `WM_LBUTTONDOWN/UP` 返回 1（吞掉），转发 `PointerPressed/Released`
  - `WM_MOUSEMOVE` 转发但不吞
  - `WM_MOUSEWHEEL` 吞掉并转发 notch
- App 侧 `SkiaIslandHost` 订阅钩子事件，`_dispatcher.Post` 到 UI 线程，再换算成 host-local DIP 交给 `SkiaIslandController`。
- **当前为了先保证点击命中，钩子矩形暂时用了整个 host 窗口**（`placement.X/Y/Width/Height`），不是 shell。副作用：岛周围的透明 bleed 区域也会被吞点击，click-through 暂时失效。渲染修好后要改回 shell 矩形。
- 冒烟输出已带 `hook=True`，证明钩子安装成功。

已验证：
- `%TEMP%\fluidbar-hook.log` 里能看到 `hit 1280,45`（真实鼠标事件进入钩子）。
- 控制器状态确实到 `Expanded`：`state kind=Expanded form=Expanded w=440.0 h=284.0`。
- 但**画面仍然 compact**，见下一节。

---

## 5. 【已解决 2026-10-19 16:40】状态 Expanded，画面不展开

> **根因（已用独立 Win32 实验复现）**：`UpdateLayeredWindowIndirect` 在 `pptDst = psize = NULL`
> （隐式几何）时返回 TRUE，但分层表面**完全不更新**。宿主只在首帧显式传几何（`_geometryDirty=true`），
> 所以只有开机第一帧上屏，之后所有提交“成功但不可见”。
> **修复**：`LayeredWindowHost.TryUpdate` 每次提交都显式传 `pptDst`/`psize`（每帧一次
> `GetWindowRect`）；删除 `UseExplicitUpdateGeometry` 与 `_geometryDirty`。
> 同时恢复了弹簧动画（`OnStateChanged` 不再 SnapMotion）、点击穿透（钩子矩形回到 shell + 2 DIP）、
> 悬停不自动收起（Core + 钩子 `PointerLeft`）、紧凑宽度/内容一致性与分钟时钟。
> 下面的原始记录保留作历史。

### 现象

真实鼠标点击时间药丸后：

- 钩子收到 `hit`
- 控制器状态机到 `Expanded`
- `OnStateChanged` 里 `SnapMotion()` 后 `RenderState.Form=Expanded, ShellWidth=440, ShellHeight=284`
- `RenderNow(forceFull:true)` 被调用
- `RenderAllLocked` 里也看到 `state form=Expanded w=440.0 full=True`
- 但屏幕上仍然是紧凑药丸（截图 `build/reports/skia-host-smoke/hook-full-crop.png`、`deferred2-crop.png`）

### 已加临时日志（未提交）

`%TEMP%\fluidbar-trace.log` 最近输出：

```
rendernow enter rendering=False windows=1 disposed=False
renderall force=True windows=1 hasState=True
renderall state form=Expanded w=440.0 full=True
rendernow exit
```

说明渲染入口和 renderer 拿到的状态都是对的；问题在 **渲染结果没有真正提交/显示**，或者提交到了错误的 surface/DIB。

### 最可疑的位置

1. `SkiaIslandHost.RenderEntry`（`SkiaIslandHost.cs` 约 1240 行）：
   - `_renderer.Render(surface.Canvas, state, scale)`
   - `surface.Canvas.Flush()`
   - `entry.Host.CommitFrame()`
   - `RecordCommit(entry, result, dirty:false)`
2. `LayeredWindowHost.CommitFrame` / `LayeredCommitResult`（`src/FluidBar.Platform.Windows/Hosting/`）：
   - `CommitFrame` 是否返回 `Updated=false`？
   - `UpdateLayeredWindow` 是否失败（返回 false / GetLastError）？
   - `BeginFrame` 拿到的 DIB 是否仍是 compact 尺寸/旧 surface？
3. `EnsureSurface`：surface 尺寸来自 `frame.WidthPx/HeightPx`，host 表面固定 732×576；如果 `BeginFrame` 返回的 bits 变了但 surface 没重建，可能画到旧 buffer。
4. `SkiaIslandHost._rendering` 重入：`RenderNow` 有 `_rendering` guard；日志显示 `rendering=False`，排除。
5. `SkiaIslandRenderChange.Full` 分支：`_controller.TakeInvalidation(out _)` 清掉 invalidation 后 `change=Full`；如果 `RenderEntry` 里 `change.FullFrame` 为 false，会走 damage 分支，damage 可能为空 → `DiscardFrame()`，画面保持旧帧。

### 下一步具体调试（建议按顺序）

1. 在 `RecordCommit` 里加 `Trace($"commit updated={result.Updated} dirty={dirty}")`（之前尝试加过，但 PowerShell 替换因 CRLF 没生效；用 `edit` 工具改）。
2. 在 `RenderEntry` 的 `CommitFrame` 前后加：
   - `Trace($"renderentry form={state.Form} w={state.ShellWidth} full={change.FullFrame} pending={entry.Host.HasPendingFrame}")`
   - `Trace($"commit result={result.Updated} us={result.UpdateMicroseconds}")`
3. 直接看 `LayeredWindowHost.CommitFrame` 实现：
   - 检查 `UpdateLayeredWindow` 返回值、`SetLastError`、`DirtyRect`。
   - 检查 `BeginFrame` 返回的 `frame.Bits` 是否与 `EnsureSurface` 缓存的 `SurfaceBits` 一致。
4. 如果 `CommitFrame` 返回 `Updated=true` 但画面不变：
   - 检查 `LayeredWindowHost` 是否在 `Place`/`Show` 后又被别的窗口盖住（`WindowFromPoint`/`GetWindowRect`）。
   - 检查是否画到了 **另一个 host 窗口**（`_windows` 只有一个，但可能有旧进程/旧实例）。
5. 用 `FLUIDBAR_SMOKE=1` 对照：
   - smoke 里 `SimulatePrimaryClick` 后 `SavePrimaryFramePng` 的证据图是否展开？如果 smoke 展开而真实点击不展开，差异在 dispatcher/帧循环时序；如果 smoke 也不展开，问题在 renderer/commit。
   - 证据目录：`%TEMP%\fluidbar-evidence\fluidbar-02-media.png`、`fluidbar-05-clipboard.png`。

### 重要提醒

- 用户机器会锁屏（`LogonUI` 进程存在时），锁屏下所有真实鼠标测试无效。测试前先确认：
  ```powershell
  Get-Process LogonUI -ErrorAction SilentlyContinue
  ```
  有输出就是锁屏，先让用户解锁。
- 用户可能同时在用鼠标，注入点击会和真实鼠标冲突；测试时先请用户停手，或看 `%TEMP%\fluidbar-hook.log` 判断坐标是不是自己的点击。
- 不要再用 `PostMessage` 的坐标当 DIP：`WM_LBUTTONDOWN` 的 lParam 是**物理 client px**，`ClientXDip = px / scale`。之前多次测试点错位置就是这个原因。

---

## 6. 【已清理 2026-10-19 16:40】临时调试代码

上一轮加的 `fluidbar-trace.log` / `fluidbar-hook.log` 写入点、`SkiaIslandHost.Trace()` helper 已全部删除；
`git diff` 里不应再出现 `%TEMP%` 调试写入。原始说明保留在下方。

### 原始记录

`git status`：

```
 M src/FluidBar.App/Hosting/Skia/SkiaIslandController.cs
 M src/FluidBar.App/Hosting/Skia/SkiaIslandHost.cs
 M src/FluidBar.Platform.Windows/Hosting/IslandMouseHook.cs
```

调试点：

- `SkiaIslandController.cs`：`ApplyInput` 里写 `%TEMP%\fluidbar-hook.log`（`input before->after`）。
- `SkiaIslandHost.cs`：
  - `Trace()` helper（`%TEMP%\fluidbar-trace.log`）
  - `RenderNow` 入口/出口
  - `RenderAllLocked` 入口/无 invalidation/状态
  - `PostHookPointer` 的 `map before/after`
  - `OnStateChanged` 的 `state kind/form/w/h`
  - `RecordCommit` 的 commit 日志（如果加上了）
- `IslandMouseHook.cs`：`SetRegions` 的 `regions N`、`HookCallback` 的 `hit x,y`。

**渲染 bug 修好后必须全部删掉**，不要把调试日志带进发布版。搜索关键字：`fluidbar-hook.log`、`fluidbar-trace.log`、`Trace(`（注意 `IAppLogger.Trace` 是正常 API，不要误删）。

---

## 7. 其他已知问题/未完成项

### 7.1 托盘右键菜单

- owner 已改成可见 1×1 离屏窗口（`TrayIconService`），但真实右键/注入 `WM_CONTEXTMENU` 都还没稳定弹出菜单。
- 备选方案：**自绘 popup 菜单窗口**（不依赖 `TrackPopupMenu` 对 owner 前台的要求），或 `SetForegroundWindow` + `PostMessage(WM_NULL)` 后 `TrackPopupMenuEx(TPM_RETURNCMD)`。
- 需要真实右键托盘图标验证（不是只发消息）。

### 7.2 UIA 树不刷新

- 展开后 `AutomationElement.FromHandle(host).FindAll(Descendants)` 仍只有 `island.root` + `island.compact`，看不到页面/控件。
- `SkiaIslandUiaSource`/`RefreshAccessibility` 可能只在状态变化时刷新，但 provider 快照/根节点缓存没更新；需要检查 `SetUiaSnapshot` 与 `UiaElementProvider` 的 tree 构建。
- 这会影响读屏/Narrator/UIA 自动化。

### 7.3 页面控件交互

- 真实数据已接；剪贴板行复制改为按索引取全文。
- 但“点击剪贴板行是否真的写入剪贴板”还没有在真实鼠标下完成回归（之前测试点错坐标/锁屏/渲染未展开）。
- 媒体播放/seek、音量/亮度/主题、通知/Agent/任务等控件需要逐个真实点击验证。
- 通知/Agent/任务目前是**空态**（不伪造），这是有意的；不要为了“看起来丰富”再塞演示数据。

### 7.4 动画/帧循环

- 当前 `OnStateChanged` 里 `SnapMotion()` + `RenderNow(forceFull:true)` 是为了绕开“状态变了但画面不动”的临时方案；动画被牺牲。
- 根因可能是 `SkiaFrameClock`/`UiThreadTicker` 在真实 App 的 Avalonia dispatcher 下回调不稳定。`tests/FluidBar.Ui.Tests/FrameClockProbeTests.cs` 证明 ticker 在测试上下文能 tick，但 App 里不一定。
- 渲染修好后要恢复弹簧动画，并验证 `frames` 持续增长。

### 7.5 其他 MVP 已知限制

- Focus LAF、输出/队列、IME/MSAA、真实多显示器/200% 硬件、Narrator 手工回归、市场签名密钥、重复 `PlatformMessageHost`、UIA 真实客户端动作/树边界。
- 详见 `docs/rewrite/RELEASE-v2-mvp.md`、`docs/rewrite/STATUS.md`、`PROJECT-NOTES.md`。

---

## 8. 接手后的建议顺序（P0 → P3）

1. ~~P0 修渲染提交~~ ✅ 已完成（每次提交显式几何；见 §5）。
2. ~~P0 真实回归：点击展开~~ ✅ 已完成（弹簧展开、悬停保持、移开 2.6s 收起、点击穿透）。
3. **P0 发布**：全量测试（1481 passed）→ `tools/release/publish.ps1` → 真实桌面跑 Release 产物。
4. **P1 页面控件真实回归**：剪贴板行复制（`Get-Clipboard` 校验）、媒体播放/seek、音量/亮度、Wi-Fi/蓝牙开关、主题切换、Esc/chevron 收起。
5. **P1 托盘右键菜单**：真实右键托盘图标，必要时改自绘 popup。
6. **P2 UIA**：展开后树/焦点/Invoke 可用（`SkiaIslandUiaSource` 刷新时机）。
7. **P2 多显示器 / 200% 真机验收**。
8. **P3 文档已在本次检查点更新；后续每次改动继续同步。**

---

## 9. 验证证据位置

- 真实数据截图：`build/reports/skia-host-smoke/verify-click-crop.png`（真实剪贴板条目）
- 状态 Expanded 但画面 compact：`build/reports/skia-host-smoke/hook-full-crop.png`、`deferred2-crop.png`
- 锁屏证据：`build/reports/skia-host-smoke/hook-click-crop.png`（Windows 锁屏）
- 冒烟日志：`%TEMP%\fluidbar-smoke.log`
- 冒烟截图：`%TEMP%\fluidbar-evidence\*.png`
- ~~调试日志：`%TEMP%\fluidbar-hook.log`、`%TEMP%\fluidbar-trace.log`~~（2026-10-19 16:40 已移除写入点）

---

## 10. 给接手 AI 的硬性提醒

- **不要只改代码不验证**。用户明确说“自行验证”，每一步至少给：
  - 构建/测试输出
  - 真实鼠标/截图/剪贴板内容
  - 失败时的日志
- **不要回退到演示数据**来让界面“看起来正常”。`DesignIslandDataSource` 只用于预览/测试。
- **不要在锁屏状态下做真实鼠标测试**；先查 `LogonUI`。
- **不要用 `PostMessage` 的坐标当 DIP**；物理 client px / scale 才是 DIP。
- **不要声称“可用”**，直到：点击展开、页面控件、剪贴板复制、托盘右键都在真实鼠标下通过。
- 用户要求中途正文回复；每完成一个阶段，用简短中文说明“改了什么/证据/下一步”，不要长时间静默。
- 文档时效性：每次修复更新 `CHANGELOG.md`、`docs/rewrite/STATUS.md`，必要时更新本文件。
