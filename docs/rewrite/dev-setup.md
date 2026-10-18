# FluidBar v2 — 开发环境

## 必需

| 项 | 版本 / 说明 |
|---|---|
| Windows | Windows 10 2004+（build 19041+）或 Windows 11 |
| .NET SDK | **10.0.301**（`global.json` 已锁定，`rollForward: latestFeature`） |
| IDE | Visual Studio 2022 17.14+ / JetBrains Rider 2025+ / VS Code + C# Dev Kit |
| Git | 2.40+ |

## 可选（性能/体验验收）

- 120Hz+ 显示器（渲染 spike 与 M6 帧预算验收）
- 支持 DDC/CI 的显示器（亮度监控）
- 支持 GSMTC 的媒体应用（Spotify / 网易云 / QQ 音乐 / 浏览器）
- 多显示器（DPI 与锚点验收）

## 构建与测试

```powershell
dotnet restore FluidBar.sln
dotnet build FluidBar.sln -c Debug
dotnet test FluidBar.sln -c Debug
```

发布/单文件（M6 起）：

```powershell
dotnet publish src/FluidBar.App/FluidBar.App.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

## 运行

- 应用：`dotnet run --project src/FluidBar.App`
- 原型：直接打开 `docs/rewrite/prototype/index.html`，或
  `python -m http.server 8125 --directory docs/rewrite/prototype`

## 目录约定

- 只改自己写入范围内的文件（见 `plans/2026-10-18-v2-implementation-plan.md` §0.1）。
- 共享文件（`FluidBar.sln`、`Directory.*.props`、`.github/**`、`docs/**`、`schemas/**`）只有 Lead 可改。
- 不提交 `bin/`、`obj/`、`artifacts/`、私钥、本机设置。
- 分支：`v2/rebuild`（唯一开发分支）；`v1-final` 标签保留 1.x 历史。

## 常用诊断

```powershell
# 句柄/内存/线程
Get-Process FluidBar | Select-Object HandleCount,WorkingSet64,Threads
# 计数器（需 dotnet-counters）
dotnet-counters monitor -n FluidBar --counters System.Runtime,Microsoft.Windows.App
# 架构测试
dotnet test tests/FluidBar.Architecture.Tests
```
