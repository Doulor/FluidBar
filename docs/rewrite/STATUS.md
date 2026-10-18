# FluidBar v2 — 当前状态（STATUS）

> 最后更新：2026-10-18 22:45（Asia/Shanghai）
> 维护规则：每个检查点由 Lead 更新本文件；数字必须来自实测。

## 一句话状态

渲染底座与渲染器 spike 均已 GO，正在把岛从 Avalonia 迁到 **Win32 分层宿主 + SkiaSharp**；干净原型 `docs/rewrite/prototype2` 已包含设置面板与插件市场。

## 里程碑

| 里程碑 | 状态 | 证据 |
|---|---|---|
| M0 分支/骨架/CI/渲染决策 | ✅ | `v2/rebuild`、`v1-final`、spec/plan/DECISIONS |
| M1 Core 领域层 | ✅ | 473 tests |
| M2 插件 SDK/宿主/市场 | ✅ | 188 tests；离线 E2E |
| M3 Windows 平台 | ✅ | 371 tests；M3+P3 |
| M5 内置插件/市场 CI/文档 | ✅ | 23 builtin + 31 integration；`docs/plugin/` |
| M4 Avalonia UI（旧宿主） | 🚧 | 90 tests；岛将迁移到 Skia 宿主 |
| M6 硬化/发布 | ⏳ | soak/性能预算已就绪；Release 浸泡待做 |

## 测试（当前）

| 套件 | 数量 | 状态 |
|---|---:|---|
| Core | 473 | ✅ |
| Platform | 371 | ✅（偶发 HWND/网络 flaky，单独运行稳定） |
| SDK | 152 | ✅ |
| PluginHost | 36 | ✅ |
| Builtin | 23 | ✅ |
| Integration | 31 | ✅ |
| UI | 90 | ✅ |
| Architecture | 11 | ✅ |

构建：`dotnet build FluidBar.sln` = 0 errors。

## Spike 结论

- **分层宿主**：`UpdateLayeredWindow` 全帧 0.06–0.16ms / 脏矩形 0.08–0.10ms；60/120fps pacing 通过；透明、点击穿透、不抢焦点、拖拽 PASS。宿主表面 float 488×384 / dock 488×444 DIP。
- **Skia 渲染器**：展开 0.55–0.61ms / p99 0.85ms；紧凑 0.46ms；脏矩形 0.157ms；120fps 约 9.8× 余量；fidelity IoU 0.9986 / 1.0000。
- **Avalonia 透明**：11.3.22 与 12.1.4 在本机均失败（灰矩形），弃用为岛宿主；仅保留设置/市场窗口。

## 进行中

1. `src/FluidBar.Island`（SkiaSharp + HarfBuzzSharp）MVP：紧凑态、主页/媒体页、pager、面板外小岛 + 远处页小点；golden/hit-test 测试。
2. `docs/rewrite/prototype2`：设置面板/插件市场已回归；迷你岛切换动效 + 远处页小点收尾验证。
3. App 集成（下一步）：用 `LayeredWindowHost` 替换 Avalonia 岛窗口，保留设置/市场 Avalonia 窗口；`PlatformServiceGraph` 统一装配。

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
