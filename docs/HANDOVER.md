# FluidBar 交接文档（2026-10-21）

## 一、当前状态

| 项 | 值 |
|---|---|
| 仓库 | `D:\build\GitLocal\FluidBar` |
| 交付分支 | `preview/0.2`（HEAD 见 `git log -1`） |
| 最近发布 | `A7B92B7F…D134`（`tools/release/publish.ps1 -SkipZip` 产出 `build/release/win-x64/FluidBar.exe`） |
| 可用回退点 | tag `good-2025-10-21-night`（183734b）+ `build/release/backup/FluidBar-good-183734b.exe` |
| 目标 | 向 iOS 灵动岛看齐：主岛与小岛同一套玻璃语言、纯圆、无强调色 |

## 二、如何跑

```powershell
# 构建
dotnet build FluidBar.sln -v q --nologo

# 测试（必须隔离输出，避免多 testhost 抢同一路径）
dotnet test tests/FluidBar.Core.Tests    -p:BaseOutputPath=artifacts/t-c/
dotnet test tests/FluidBar.Island.Tests  -p:BaseOutputPath=artifacts/t-i/
dotnet test tests/FluidBar.Ui.Tests      -p:BaseOutputPath=artifacts/t-u/

# 发布
pwsh -NoProfile -File tools\release\publish.ps1 -SkipZip
```

当前基线：Core **640/640**、Island **441/441**、UI **490/490**、Platform **660/660**、BuiltinPlugins **25/25**。

## 三、架构速览

- **岛本体**：Win32 layered window（`UpdateLayeredWindow`，premultiplied BGRA）+ SkiaSharp；不透明区域外全透明。
- **设置窗口**：Avalonia 12；注意 Avalonia 12 **不渲染** `BoxShadow.IsInset`（用渐变 overlay 模拟按压）。
- **帧驱动**：`SkiaIslandHost`/`SkiaIslandController`；`IsAnimating` 决定是否持续出帧，**任何持续动画都必须 (a) 并入 IsAnimating 且 (b) 每帧产生 dirty rect**，否则画面冻结（这是 ring 不转的根因）。
- **两种呈现形态**：`Stacking=Split`（primary + SplitIslet 并排小球）与 `Stacking=Stacked`（primary + Ghost capsule）。**两条路径的滚轮/选中逻辑必须共用函数**。
- **活动调度**：`ActivityScheduler.Normalize`（排序 → 截断 `VisibleCapacity=3`）；persistent（播放中媒体、active agent）优先占位；心跳每 2s 重发布。
- **功耗模式**：`AppSettings.PowerMode`（Performance 60fps / Saver 30fps），设置面板「外观 → 性能模式」可切，立即生效。

## 四、今天修掉的关键 bug（每条都有运行时证据）

1. **手动换岛 2 秒弹回**：心跳 upsert 以 `IsPinned=false` 新建记录冲掉策略 pin → `ActivityScheduler.Upsert` 按 key 继承 pin。
2. **岛会少一个**：`VisibleCapacity=3` 被剪贴板/锁屏键/通知占满，媒体落入 background（不生成小球）→ persistent 预留名额。
3. **Stacked 路径没有 pin**：pin 只写在 Split 的 `ReorderSplitVisible`，而用户实际走 `RotateStack` → 抽共享 `PinTo/ReleasePin`。
4. **滚轮门限混用**：260ms 过渡锁被当成所有翻页门限 → 同手势 260ms 丢弃、新意图 150ms。
5. **歌词晚一拍**：新行 40ms 延迟 + 260ms 淡入 + 起始 opacity 0 → 改为延后 0、180ms、起始 0.3。
6. **歌词卡 1fps**：宿主不请求帧 → 过渡纳入 `IsAnimating`，只重绘歌词带。
7. **逐词回退半个字**：快照晚到导致负漂移被缓动 → 显示位置单调钳制，仅真 seek（>800ms）可回退。
8. **Agent 完成后不消失**：`WithPayload` 继承旧 `IsPersistent=true` + 默认 TTL 5s → 按 payload 重算，completed TTL 3s。
9. **完成态仍转蓝圈**：环只看 kind 不看状态 → Completed 静态灰勾、角度冻结、不转。
10. **环不转（真机）**：环推进了角度但**没产生 dirty rect** → `SplitRingDamage()` 每帧 InvalidateDirty + 环存在时主动 FrameRequested。
11. **小岛转圈与大岛不一致**：两份实现 → 抽 `AgentSpinner`（180°、butt cap、线宽:半径 1.5:4.75、0.9s/圈），大岛与卫星共用。
12. **没有音乐岛**：GSMTC `sessions=0` 时直接 retract + MSAA 退避 30s → 有音频输出即走 fallback、退避降到 2.5s、启动不发布空快照。

## 五、已知问题 / 下一步（按优先级）

**P0（发布前必须）**
1. 九套全量测试一次跑完（Platform/Sdk/PluginHost/Builtin/Integration/Architecture 尚未在最终分支合跑）。
2. 打包 zip + 版本号 + 首次启动默认态（无配置时的空态）。
3. **插件首次启用引导**：用户曾因 `PluginEnabled` 为空/媒体插件关闭而"没有音乐岛"，建议首次启动默认启用 builtin 插件，或在空态给出一键启用入口。

**P1**
4. 设置面板逐项验证（每个开关真的生效）。
5. 空闲 CPU：性能 0.692% / 省电 0.444%（目标 0.3%，剩余为平台轮询）。
6. 多显示器与 125%/150% DPI 未验证。
7. 展开面板 440×244（1.80 比例）需要"内容驱动自适应高度"，是独立工程（曾直接压高度导致页面裁切，已回滚）。

**P2**
8. 卫星第三球、更多 iOS 细节打磨。

## 六、纪律（血泪版，务必遵守）

1. **任何涉及输入/命中/几何的改动，必须在真机上点一遍**——离屏渲染看不出来"点了没反应"。
2. **同一功能若有多条分支路径（Split/Stacked、绘制/命中、测量/渲染），先抽共享函数，且每条路径各写一条测试**。今天半数 bug 源于"只修了一条路"。
3. **诊断设施不得改变被测系统性能**：每帧路径禁止 I/O；诊断开关只在单次运行用环境变量开启，**禁止 `setx` 永久打开**（曾因每帧同步写日志导致整机 UI 卡成 1fps）。
4. **改共用判定（如 persistence）必须同时跑两侧回归**（媒体与 Agent）。
5. **git 只按路径 add，禁止 `git add -A`**（曾把别人 staged 的改动与 `third_party/` 混进提交）。
6. **测试期望必须来自当前实现的真实采样**，不许删测试、不许放宽容差；契约变更要在提交信息里写明依据。
7. **发布门槛**：测试全绿 + 真机截图/点击验证 + 可回退点。

## 七、关键文件地图

| 领域 | 文件 |
|---|---|
| 岛绘制 | `src/FluidBar.Island/IslandRenderer.cs`、`PageRenderer.cs`/`PageRenderer.Pages.cs`、`PageMetricsLayout.cs` |
| 度量与令牌 | `IslandMetrics.cs`、`TypographyScale.cs`、`SplitSatelliteStyle.cs`、`AgentSpinner.cs` |
| 活动调度 | `src/FluidBar.Core/Activities/ActivityScheduler.cs`、`SplitStackPolicy.cs`、`ActivityPriorityPolicy.cs` |
| 宿主/帧 | `src/FluidBar.App/Hosting/Skia/SkiaIslandController.cs`、`SkiaIslandHost.cs`、`IslandPowerPolicy.cs` |
| 媒体探测 | `GsmtcMediaSessionService.cs`、`MediaPlugin.cs`、`MediaPositionClock.cs` |
| 设置面板 | `src/FluidBar.App/Views/Settings/*` |
| 规范文档 | `docs/ios-design-study.md`、`docs/satellite-visual-spec.md`、`docs/visual-acceptance-checklist.md`、`docs/2026-10-21-split-wheel-fixes.md` |

## 八、证据位置

- 动效逐帧：`build/reports/ui-audit/motion/`、`build/reports/island-motion/`
- 卫星：`build/reports/ui-audit/satellite/`
- 性能：`build/reports/perf/`（idle、power-mode、media-fallback）
- 审计基线：`build/reports/ui-audit/audit-sheet-v3/`

> 注意：`build/` 不入库（已在 .gitignore），这些图表是运行产物。
