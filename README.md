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

**开发中（M0–M5 已完成，M4 UI 收尾 / M6 硬化中）** — 分支 `v2/rebuild`。

| 里程碑 | 状态 |
|---|---|
| M0 基线（分支/骨架/CI/渲染 spike） | ✅ |
| M1 Core（事件/状态机/布局/弹簧/设置，388 tests） | ✅ |
| M2 插件 SDK / ALC 宿主 / 市场打包签名（188 tests） | ✅ |
| M3 Windows 平台（显示器/媒体/剪贴板/通知/托盘/热键，347 tests） | ✅ |
| M5 内置插件 + 市场 CI/文档（23 + 31 集成 tests） | ✅ |
| M4 Avalonia 外壳（岛/设置/插件市场/i18n，70 tests） | 🚧 桌面验收中 |

- 交互与视觉原型：[docs/rewrite/prototype/index.html](docs/rewrite/prototype/index.html)（浏览器打开即可）
- 设计规范 / 实施计划 / 决策记录：[docs/rewrite](docs/rewrite/)
- v1 架构调研与 v2 技术选型：[docs/rewrite/research](docs/rewrite/research/)
- **插件开发文档（快速开始 / manifest / 签名 / 发布 / 审核）**：[docs/plugin](docs/plugin/)

## 技术栈

| 层 | 选型 |
|----|------|
| 运行时 | .NET 10 / C# 14 |
| UI | Avalonia 12.1.4（固定 488×332 宿主 + Win32/Composition；spike GO） |
| 窗口与合成 | Win32 + Windows.UI.Composition（`WS_EX_NOREDIRECTIONBITMAP`），固定尺寸窗口 + 内部动画 |
| 系统集成 | WinRT（GSMTC、通知监听）、Win32 P/Invoke（音量、亮度、DPI、热键等） |
| 插件 | `FluidBar.Sdk`（无 UI 依赖）+ collectible AssemblyLoadContext + 签名 `.fbp` 包 + GitHub Pages 市场索引 |
| 测试 | xUnit（1054 tests）+ 架构约束测试 + i18n 伪语言 + soak/性能预算 |

## 仓库结构

```
src/
  FluidBar.Core/               纯领域模型：事件、活动状态机、布局、弹簧、pager、设置（无 UI）
  FluidBar.Sdk/                插件契约与 manifest 模型（无 UI 依赖）
  FluidBar.PluginHost/         collectible ALC 加载器、生命周期、隔离与卸载
  FluidBar.Marketplace/        .fbp 打包、签名/索引、安装/更新/回滚
  FluidBar.Platform.Windows/   Win32/WinRT：显示器、监控器、媒体、通知、剪贴板、Agent、托盘、更新
  FluidBar.Plugins.Builtin/    第一方插件（media/clipboard/notifications/agent/devices）
  FluidBar.App/                Avalonia 外壳：岛窗口、六页控制中心、设置、插件市场、i18n
  FluidBar.Cli/                fluidbar-plugin 工具（validate/pack/sign/index）
plugins/samples/               插件示例（minimal / rich）
marketplace/                   签名市场索引与发布产物
schemas/                       plugin / index / locale / signature JSON Schema
tests/                         Core / Sdk / PluginHost / Platform / Builtin / Integration / Ui / Architecture / Soak
docs/rewrite/                  原型、设计 spec、实施计划、调研、契约
docs/plugin/                   插件作者与维护者文档
```

## 构建与测试

```powershell
dotnet build FluidBar.sln
dotnet test FluidBar.sln

# 性能/浸泡预算（Release，<1s）
dotnet run --project tests/FluidBar.Soak -c Release
```

发布（M6 进行中）：

```powershell
dotnet publish src/FluidBar.App/FluidBar.App.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

## 贡献

目前项目维护者为 **Doulor**。插件贡献流程（脚手架 → validate → pack → PR → CI 签名发布）见 [docs/plugin/quickstart.md](docs/plugin/quickstart.md) 与 [docs/plugin/review-checklist.md](docs/plugin/review-checklist.md)。

## 许可证

MIT，详见 [LICENSE](LICENSE)。
