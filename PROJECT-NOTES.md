# FluidBar 项目说明与方向备忘（PROJECT NOTES）

> 给后续参与本项目的 AI / 贡献者：**先读这份文件，再读 `docs/rewrite/specs/` 与 `docs/rewrite/DECISIONS.md`。**
> 本文件记录产品初衷、用户明确偏好、不可回退的设计决策，以及容易走错的方向。任何与这里冲突的实现都视为方向错误。

## 1. 项目是什么

FluidBar 是一个 Windows 上的「灵动岛」桌面常驻组件，目标不是"把信息尽量塞进一个浮窗"，而是：

- **常驻但不打扰**：默认是苹果 iOS 风格的小岛（Live Activity 药丸），安静地待在屏幕顶部
- **点击才展开**：小岛 → 悬停预览 → 点击展开成控制中心/面板，操作完自动收起
- **可以作为功能栏**：可选 Dock 模式，一定程度替代 Windows 任务栏（应用、托盘、系统状态）
- **强交互**：媒体控制、通知管理、剪贴板历史、Agent 任务、系统控制、小组件
- **插件化**：内置插件 + 社区插件（签名 DLL、应用内插件市场安装/更新/回滚）
- **国际化**：中英首发，运行时切换；插件自带语言包
- **可自定义**：外观、材质、位置、动效、插件都能配置

## 2. 用户最初的设想（原话要点）

- "借鉴 iOS 的灵动岛，结合现在的代码，完全从头写一个"
- "默认搞成苹果的那种小岛样式，点击变大"
- "作为常驻的功能栏，甚至一定程度上替代 Windows 任务栏，甚至更丝滑"
- "可自定义性更强"
- "国际化 i18n 要做好"
- "亮点在于插件化：丰富的插件配置集成、应用内插件市场与管理；贡献者可以发布插件、合并到主分支进入插件市场"
- "质量最重要，你看怎么质量最好怎么做就行"（2026-10-18）

## 3. 不可回退的 UX 决策（用户已明确否决的）

| # | 决策 | 说明 |
|---|---|---|
| UX1 | **默认小岛，点击展开** | 不要默认大岛/常驻大面板 |
| UX2 | **不要底部平铺标签栏** | 第一版原型底部的 6 个平铺图标被否决 |
| UX3 | **不要左侧竖向 rail** | 第二版左侧导航栏被否决（"放在左边也不好看"） |
| UX4 | **不要常驻页点** | 页点丢失选项卡直观性，被否决 |
| UX5 | **迷你小岛在灵动岛外面** | 不能覆盖在展开面板内部；浮在面板下方、岛外面 |
| UX6 | **迷你小岛不常驻** | 只在拖动/滚轮时短暂出现，停止后淡出；不要一直显示 |
| UX7 | **迷你小岛要小、要轻** | 无边框、低对比、柔圆角/圆形；不要一排彩色标签 |
| UX8 | **展开面板不要上下黑边** | 内容要填满壳体（20px 左右 / 12px 上下内边距），不要顶部/底部黑带 |
| UX9 | **紧凑态图标不能压文字/出界** | 通知 248×36、剪贴板 264×36：图标固定槽 + 文本单行省略 + pill 裁剪 |
| UX10 | **设置面板必须保留** | 第一版的 7 分区设置面板 + 插件市场是用户"非常非常喜欢"的部分，不允许在原型/实现中丢失 |
| UX11 | **动效要丝滑、有阻尼** | 拖动跟随 0.85、吸附 T 0.38 / ζ 0.94、滚轮 140px/180ms；不要"一下飞好几页"或"卡在一页" |
| UX12 | **滚轮与内容滚动要协调** | 内容可滚就先滚内容；到边界后 40px 过滚 + 120ms 停顿才翻页；反向取消；水平滚轮/Shift 直接翻页 |

## 4. 当前已确定的技术方向（质量优先）

- **岛宿主：Win32 分层窗口**（`WS_EX_LAYERED | TOOLWINDOW | NOACTIVATE | TOPMOST`，32bpp 预乘 BGRA DIB + `UpdateLayeredWindow`）
  - 原因：Avalonia 11.3.22 与 12.1.4 在本机都会把透明窗口压成灰色矩形；WPF 分层窗口与原生 `UpdateLayeredWindow` 透明正常
  - 固定宿主表面：float/flush **488×384 DIP**、dock **488×444 DIP**（150% DPI → 732×576 / 732×666 px）
- **岛渲染：SkiaSharp + HarfBuzzSharp**（自绘 squircle/文字/图标/阴影；设置与插件市场窗口继续用 Avalonia 普通窗口）
  - 实测：展开全帧 0.55–0.61ms / p99 0.85ms；脏矩形 0.157ms；120fps 有约 9.8× 余量
- **领域层：`FluidBar.Core`**（事件、活动状态机、优先级、布局、弹簧、pager、设置；无 UI 依赖）
- **插件：`FluidBar.Sdk` + `FluidBar.PluginHost` + `FluidBar.Marketplace`**
  - 内置插件编译期注册；社区插件为签名 DLL，每插件 collectible ALC
  - 仅声明式 UI 贡献（布局/设置页/命令/菜单/小组件），禁止插件 XAML
  - 能力清单是"披露+同意"，不是沙箱；不要宣传成沙箱
- **i18n：JSON 语言目录**（en-US + zh-Hans 首发），运行时切换；插件自带 locales
- **设置：`%APPDATA%\FluidBar\settings.json`**，schemaVersion + 原子写 + v1 迁移

## 5. 性能/架构硬规则（v1 的教训）

1. 永不逐帧修改窗口 bounds；窗口只在锚点/显示器/模式变化时移动一次
2. 永不在 UI 线程做 I/O（同步 HTTP、DDC/CI、WMI、`SendMessage`、`Process.GetProcesses*`、目录枚举）
3. 每个显示器一个宿主窗口，不存在 per-island 快照 HWND
4. 不轮询可通知的东西；不可避免的轮询 ≥1s、后台、变化才发布
5. 每个缓存有预算（图片字节 LRU、歌词 TTL、通知 ID 环形缓冲、临时目录清扫）
6. 每个原生 IDisposable 都要释放；异步可取消且单飞；无空 catch
7. 所有 UI 文本走 i18n；所有可见尺寸/颜色/动效走 token，不在页面里就地取值
8. 空闲时 0 帧回调；动画收敛即停；DropShadow/模糊不要逐帧跑

## 6. 协作与流程注意事项

- 当前维护者/贡献者：**Doulor**（单人维护；插件 PR 由 Doulor 审核，CI 构建/签名/发布）
- 分支：`v2/rebuild`（重写分支）；`v1-final` 标签保留 1.x 历史
- 共享文件（`FluidBar.sln`、`Directory.*.props`、`.github/**`、`docs/**`、`schemas/**`）由 Lead 统一修改
- **原型目录协调**：`docs/rewrite/prototype` 曾因多个后台任务同时改写而损坏；当前干净原型是 `docs/rewrite/prototype2`（本地 8125 端口）。不要多个 agent 同时改同一原型目录
- **设置面板**：第一版的 7 分区设置 + 插件市场必须在新原型和新实现中保留；改版时先备份/移植，不允许直接删掉
- 不复制 GPL 项目代码（WinIsland / boring.notch / Atoll / livebridge 只读参考）；MIT 参考需保留版权声明
- 不确定的用户可见行为改动，先问 Doulor；技术实现可自行决定，但必须遵守本文件与 `DECISIONS.md`

## 7. 当前状态（2026-10-18）

- 已完成：Core（473 tests）、Platform（371 tests）、SDK/PluginHost/Marketplace（188 tests）、Builtin 插件（23 tests）、Integration（31 tests）、Avalonia UI（90 tests）、架构测试（11）
- 渲染 spike 双 GO：Win32 分层宿主（透明/点击穿透/不抢焦点/拖拽/60-120fps PASS）+ Skia 渲染器（fidelity IoU 0.9986/1.0000）
- 正在做：Skia 岛渲染器 + 分层宿主的正式迁移（MVP：紧凑岛 + 主页/媒体 + pager + 面板外 3 个小岛；约 20–25 工程日）
- 原型：`docs/rewrite/prototype2`（3 个临时小圆岛、无黑边、紧凑态无溢出；设置面板/市场待移植回来）
- 已知未完成：多显示器真机复测、UIA 无障碍、插件市场真实发布密钥/托管（Q3–Q5）

## 8. 文件地图

- `docs/rewrite/specs/2026-10-18-fluidbar-v2-design.md` — 设计规范（含修订 A1/A2）
- `docs/rewrite/DECISIONS.md` — 决策日志（D/I/Q 系列）
- `docs/rewrite/plans/` — 实施计划
- `docs/rewrite/research/` — v1 审计、技术栈、插件生态、渲染 spike 报告
- `docs/rewrite/contracts/` — Core/Platform/Plugin/UI/Layered host/Skia 契约
- `docs/plugin/` — 插件作者与维护者文档
- `src/FluidBar.Core|Sdk|PluginHost|Marketplace|Platform.Windows|Plugins.Builtin|App`
- `tests/` — 各层测试 + soak
