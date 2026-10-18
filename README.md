# FluidBar 2.0

> Windows 上的灵动岛 — 常驻、可交互、可扩展的桌面信息与控制中心。

FluidBar 2.0 是一次完全重写（v1 代码已从本分支移除，历史保留在 `main` 与 git 记录中）。目标是做出一个真正顺滑、低占用、可长期常驻的 Windows 灵动岛：

- **默认小岛**：低打扰的 Live Activity 小药丸，悬停预览、点击展开控制中心
- **三种形态**：浮岛 / 贴顶（flush）/ Dock 桌面底栏（可一定程度替代 Windows 任务栏）
- **强交互**：媒体控制、通知管理、剪贴板历史、Agent 任务、系统控制、小组件
- **插件化**：内置插件 + 社区插件（签名 DLL、应用内市场安装/更新/回滚）
- **国际化**：中英首发，完整运行时切换，插件自带语言包
- **性能底线**：固定窗口 + 合成层动画；禁止逐帧窗口 bounds 动画、UI 线程 I/O、无界缓存

## 当前状态

**开发中（milestone M0/M1）** — v2 分支 `v2/rebuild`。

- 交互与视觉设计：[可交互原型](docs/rewrite/prototype/index.html)（浏览器打开即可）
- 设计规范与实施计划：[docs/rewrite](docs/rewrite/)
- v1 架构调研与 v2 技术选型：[docs/rewrite/research](docs/rewrite/research/)

## 技术栈（待渲染 spike 最终确认）

| 层 | 选型 |
|----|------|
| 运行时 | .NET 10 / C# 14 |
| UI | Avalonia 12（待 spike 确认：透明窗口 / 120fps / 点击穿透 / 内存） |
| 窗口与合成 | Win32 + Windows.UI.Composition（`WS_EX_NOREDIRECTIONBITMAP`），固定尺寸窗口 + 内部动画 |
| 系统集成 | WinRT（GSMTC、通知监听）、Win32 P/Invoke（音量、亮度、DPI、热键等） |
| 插件 | `FluidBar.Sdk`（无 UI 依赖）+ collectible AssemblyLoadContext + 签名 `.fbp` 包 + GitHub Pages 市场索引 |
| 测试 | xUnit + 架构约束测试 + i18n 伪语言 + 1h soak |

## 仓库结构

```
src/
  FluidBar.Core/              纯领域模型：事件、活动状态机、布局、弹簧、设置（无 UI）
  FluidBar.Sdk/               插件契约与 manifest 模型（无 UI 依赖）
  FluidBar.Platform.Windows/  Win32/WinRT 集成：监控器、媒体、通知、剪贴板、Agent、托盘
  FluidBar.App/               Avalonia 应用：岛窗口、设置、插件市场、i18n（M4）
tests/
  FluidBar.Core.Tests/
  FluidBar.Sdk.Tests/
marketplace/                  插件市场 schema / 索引生成（M5）
docs/rewrite/                 原型、设计 spec、实施计划、调研报告
```

## 构建与测试

```powershell
dotnet build FluidBar.sln
dotnet test tests/FluidBar.Core.Tests/FluidBar.Core.Tests.csproj
```

## 贡献

目前项目维护者为 **Doulor**。插件贡献流程、签名与审核规范见 v2 设计 spec 的插件章节（M2/M5 落地）。

## 许可证

MIT，详见 [LICENSE](LICENSE)。
