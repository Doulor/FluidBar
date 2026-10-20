# FluidBar 性能画像与优化审计（Skia 岛）

- 日期：2026-10-20
- 范围：src/FluidBar.Island、src/FluidBar.App/Hosting/Skia、tests（未改 Views/Settings）
- 构建：Release（net10.0-windows10.0.19041.0），基线提交 09a4fee；工作树含本任务改动
- 机器：AMD Ryzen 9 9955HX（16C/32T，31.2GB），Windows 10.0.26200，.NET 10，150% DPI
- 基线参考：Lead 提供 Release 49B56C2E 静置 10 秒：空闲 CPU 0.32%，工作集 216.2MB

## 1. 测量方法

1. 新增 opt-in 探针 SkiaHostPerfProbe（FLUIDBAR_PERF_PROBE=1）：逐帧记录帧间隔/渲染耗时（固定桶直方图，记录路径 0 分配）、掉帧（>21ms）、active 区间（连续动画帧，间隔 <=100ms，排除空闲缝隙）、GC/分配/工作集/私有内存、渲染缓存字节数；每秒输出一行 island.skia.perf，首帧输出 island.skia.first-frame（含进程启动到首帧、host start 到首帧），第 2 次采样输出 island.skia.memory（模块镜像/托管堆/句柄/线程分解）。
2. 新增 RenderResourceStats：IslandRenderer 暴露 shadow/images、artwork、path、text 缓存计数与实际像素字节（SKImage.Info 尺寸×4）。
3. 端到端场景用 PowerShell 驱动真实 Release 进程（锁屏桌面、PrintWindow 不参与，纯真实运行）：启动 -> 等首帧 -> 静置采样 -> 用 UI Automation 的 ExpandCollapsePattern 驱动 10 轮展开/收起（每轮 540ms）-> 静置采样 -> 退出。环境变量 FLUIDBAR_PERF_IDLE_TRIM_MS 可缩短 idle trim 延时用于测量。
4. 渲染/分配基线用现有 Island.Tests（RenderTimingTests、AllocationTests）复跑，新增 PerfAuditTests 断言缓存上界与 morph 复用。

环境说明：测量时同机有其他 agent 进程与系统通知流（手机同步 toast 每 ~2s 到达），因此静置期仍有 26fps 左右的通知动画；CPU 绝对值不能与 Lead 的 0.32% 直接横比，已在第 3/4 节给出活动归因。锁屏下窗口不可见但分层窗口仍真实渲染/上传。

## 2. 基线数据（改动前）

启动：进程启动 -> 首帧 676–1056ms；其中 host start -> 首帧 118–150ms（其余为 Avalonia 应用/设置/本地化/外观探测等 App 组合时间，不在本任务可改范围）。
静置 10s（探针开启，通知流活跃）：CPU 1.67–1.84%，工作集 188 -> 211MB，私有 109–126MB，托管堆 9.6–10.5MB，GC committed 25.6MB，渲染 shadow 3.52MB/8 张，surface 1.61MB，text 13KB，artwork 0（当前无封面；有封面时每张 640x640 全尺寸解码约 1.63MB × 4 张 = 6.5MB）。
渲染路径：render p50 1ms、p99 2.5ms；每帧托管分配 render 0B、advance 0B（既有 AllocationTests 的 <1024B/帧门槛继续通过）；每帧之外的分配约 0.25–0.32MB/s（后台通知/天气/插件事件，不在逐帧路径）。
动画（10 轮展开/收起，改动前）：16s 内 739 帧，active 帧间隔 p50 15.5ms、p95 18ms、p99 45.5ms，掉帧 40（>21ms），render p50 3ms、p95 11.5ms、p99 12.5ms，shadow 烘焙 477 次，gen0/1/2 = 5/3/2。

## 3. 热点与优化项

H1 阴影每帧重烘焙：展开/收起时 shell 宽高与圆角每帧变化，ShadowCache 按 0.5 DIP 精确 key 命中失败，16s 内烘焙 477 张模糊图（每张一次 SKSurface+blur+snapshot，约 2.7MB 上限），直接造成 render p95/p99 抬升。
修复：新增 IslandRenderState.ShellMorphing（controller 在 spring 未静止时置 true）；morph 帧把阴影 box 量化到 24 DIP、圆角量化到 4 DIP 复用；静止帧仍用精确 key（golden 不变）。烘焙 477 -> 323（-32%），render p95 11.5 -> 8ms、p99 12.5 -> 11ms。

H2 120fps 目标被 Windows 定时器粒度吃掉：host 要求 120fps（8.33ms），但默认系统定时器粒度 15.6ms，帧间隔 p50 只有 15.5ms，偶发双 tick 抖动到 31ms。
修复：动画帧循环运行期间 timeBeginPeriod(1)，空闲立即 timeEndPeriod(1)（仅动画窗口内改变全局定时器精度）。帧数 739 -> 1340/16s（真的跑到 ~84fps 均值、动画中 120fps），active p50 15.5 -> 8ms、p95 18 -> 14ms。

H3 动画期间发生 gen2 全回收（16s 内 2 次），造成 30–45ms 的帧间隔尖峰（render 本身只有 1–3ms）。
修复：帧循环运行期间 GCSettings.LatencyMode = SustainedLowLatency，空闲恢复原模式。p99 45.5 -> 27ms。另试过 GC.TryStartNoGCRegion：p99 可到 18ms，但动画期间工作集 +55MB（堆 22MB、committed 146MB），与内存目标冲突，已放弃并在代码注释中记录。

H4 隐藏岛仍在跑 30fps 波形动画：IsWaveActive 未判断 Form==Hidden，隐藏后仍持续请求帧。
修复：Hidden 直接返回 false。

H5 阴影图像缓存过大：默认 8 张，最坏 ~21MB，实测 3.52MB（8 张）。
修复：默认容量 8 -> 4，实测 1.72–1.93MB（-47%）。

H6 封面按原始尺寸解码：640x640 每张约 1.6MB，4 张约 6.5MB，且解码 CPU 与解码后 long-lived 内存都高；实际最大绘制仅 92 DIP（150% 下 138px）。
修复：ArtworkImageCache 用 SKCodec 缩放解码到长边 <=256px，编解码器不支持缩放时回退 decode + SKBitmap.Resize；每张约 0.26MB（-84%），并暴露 PixelBytes 计数。测试 1024x1024 封面 -> 缓存 256px（262144B）。

H7 emoji 字体在 TextEngine 构造时总是加载 seguiemj.ttf（Blob.FromFile + HarfBuzz Face，约数 MB 文件映射），但多数会话不画 emoji。
修复：懒加载（首次 emoji 文本才创建 Face），EmojiFaceLoaded 供测试断言。

H8 空闲不释放内存：分层窗口程序常驻后，字体栅格缓存与增长过的 GC 堆一直占着。
修复：帧循环停止 5s 后执行一次 idle trim（SKGraphics.PurgeFontCache + 一次 compacting GC，可被新动画取消，带 idle-trim 日志）。实测触发一次：托管堆 11.3 -> 7.0MB，工作集 224.8 -> 216.4MB（-8.4MB）。通知持续到达时不会触发（不会牺牲动画）。

H9 缺少可观测性：原先只有渲染耗时 p50/p99 的 256 窗口，没有帧间隔/掉帧/分配归因/首帧/内存分解，无法验收“空闲 0 重绘”“每帧分配接近 0”。
修复：SkiaHostPerfProbe（上述指标 + 每秒 JSONL）与 ResourceStats；新增 8 条测试覆盖直方图 p50/p95/p99、掉帧、active 区间、分配归因、idle 0 帧、ShellMorphing、隐藏岛停帧。

## 4. 优化后数据（同机同场景，改动后）

启动：进程启动 -> 首帧 676–852ms（host 部分 118ms，未变；无新增启动工作）。
静置：CPU 1.67–1.81%（探针开销约 0.2–0.3%，无探针 1.37%），render p99 2.5ms，工作集 211MB，私有 122.5MB，托管堆 10.1MB，committed 25.6MB，shadow 1.79MB，surface 1.61MB，text 21KB；idle trim 可再释放约 8MB。
动画（10 轮展开/收起，16s）：1340 帧，active p50 8ms、p95 14ms、p99 20.5ms（<21ms 目标达成），掉帧 30（2.2%），render p50 1ms、p95 8ms、p99 11ms，shadow 烘焙 323 次，render/advance 每帧分配 0B（仅首帧路径 8KB 一次），gen0/1/2 = 5/3/2。

### 前后对比表

| 指标 | 改动前 | 改动后 | 目标 |
|---|---:|---:|---:|
| 空闲重绘（无变化时） | 探针/测试证明 host 不请求帧；环境通知流导致活动秒仍有动画 | 同左 + 隐藏岛不再动画 | 0 重绘 |
| 静置 CPU（本机噪声环境） | 1.67–1.84% | 1.37–1.81%（探针开关差异） | 接近 0（安静环境） |
| 启动到首帧 | 676–1056ms（host 118–150ms） | 676–852ms（host 118ms） | 未劣化 |
| 动画帧间隔 p50 | 15.5ms | 8.0ms | — |
| 动画帧间隔 p95 | 18ms | 14ms | — |
| 动画帧间隔 p99 | 45.5ms | 20.5ms | <21ms |
| 掉帧（>21ms） | 40 / 739（5.4%） | 30 / 1340（2.2%） | 越低越好 |
| 每帧渲染 p99 | 12.5ms | 11ms | — |
| 每帧 render/advance 分配 | 0B / 0B | 0B / 0B | 接近 0 |
| shadow 烘焙次数/16s | 477 | 323 | — |
| shadow 驻留 | 3.52MB | 1.79MB | 降低 |
| 封面驻留（4 张 640x640 时） | ~6.5MB | ~1.05MB（4×256px） | 降低 |
| 工作集（静置） | 211–214MB | 209–211MB（idle trim 后再 -8MB） | 显著下降 |
| 空闲 trim 释放 | 无 | 堆 -4.3MB、WS -8.4MB | — |

## 5. 216MB 工作集构成与可降手段

静置采样（第 2 次 dump）分解：workingSet 187MB（启动早期），private 109–126MB；托管堆 ~10MB，GC committed 25.6MB；应用自有缓存（shadow 1.79 + surface 1.61 + text 0.02 + artwork 0）= 3.4MB。
模块镜像（ModuleMemorySize 合计 410MB，其中大量共享页）：nvwgf2umx.dll 83.7MB、nvgpucomp64.dll 79.3MB、Microsoft.Windows.SDK.NET.dll 25.1MB、System.Private.CoreLib 15.3MB、libSkiaSharp 11.1MB、windows.storage 8.7MB、SHELL32 7.6MB、nvgpucomp/nvppex、d2d1 6.2MB、ANGLE av_libGLESv2 5.2MB、coreclr 4.5MB。
结论：216MB 中真正由岛渲染缓存持有的只有 ~3–7MB；主体是 .NET 运行时/Avalonia/Win32 图形栈（NVIDIA D3D/ANGLE/D2D 共享页）与 GC 堆。可降手段（按收益排序）：
1. skia host 模式下不初始化 Avalonia ClassicDesktopLifetime（Program/AppBootstrap，本任务范围外）：可省掉 ANGLE/D3D/DWM 相关共享工作集与 JIT，是唯一“显著”级的杠杆（预估几十 MB）。
2. 本任务已做：shadow 8->4（-1.7MB）、封面限 256px（-5.4MB 峰值）、emoji 懒加载、idle trim（静置 -8MB 左右）、空闲/隐藏停帧。
3. 事件路径分配约 1MB/s（通知/媒体生产者），会推高 GC committed 与 gen2 频率；优化通知 toast 解析/对象复用时能进一步降低堆峰值和动画 p99 尖峰。
4. 如需严格 <21ms 的 p99 且不改 Avalonia 启动：可考虑持续 timeBeginPeriod + 专用帧调度线程/DwmFlush，但会改变宿主 pacing 合同，需评审。

## 6. 改动文件

- 新增 src/FluidBar.App/Hosting/Skia/SkiaHostPerfProbe.cs：opt-in 探针（直方图/掉帧/active/分配归因/内存分解/首帧）
- 新增 src/FluidBar.Island/RenderResourceStats.cs：渲染缓存字节统计
- src/FluidBar.Island/IslandRenderer.cs：ResourceStats 暴露；构造/shadow 容量默认 4；morph bucket 传入
- src/FluidBar.Island/IslandRenderState.cs：ShellMorphing 标志
- src/FluidBar.Island/SquirclePathCache.cs：ShadowCache morph 量化（box 24 DIP / radius 4 DIP）+ 容量默认 4
- src/FluidBar.Island/RenderResources.cs：封面缩放解码（<=256px）+ PixelBytes/Builds 统计；NumberTextCache.Count
- src/FluidBar.Island/TextEngine.cs：emoji 字体懒加载 + EmojiFaceLoaded
- src/FluidBar.App/Hosting/Skia/SkiaIslandHost.cs：探针接线（帧/分配/内存）、idle trim、动画期 SustainedLowLatency、动画期 timeBeginPeriod(1)、隐藏/空闲停帧路径
- src/FluidBar.App/Hosting/Skia/SkiaIslandController.cs：ShellMorphing、隐藏岛不请求波形帧
- 测试：tests/FluidBar.Island.Tests/PerfAuditTests.cs（4）、tests/FluidBar.Ui.Tests/SkiaHostPerfProbeTests.cs（4）

## 7. 测试

dotnet test FluidBar.sln：2277 passed / 0 failed / 0 skipped（9 个测试工程；含既有 RenderTimingTests、AllocationTests、GoldenImageTests，均通过）。新增 8 条本任务测试。基线上报 2244，本工作树在并行任务中已增长到 2269，本任务 +8 = 2277，全绿。
说明：首次全量运行有 2 条与本任务无关的既有用例抖动失败（Platform 硬件/UI 时序），立即重跑均通过；最终两次全量均为 0 失败。

## 8. 原始数据

- build/reports/perf/raw/anim-before.log.jsonl（改动前动画探针数据）
- build/reports/perf/raw/anim-final.log.jsonl（改动后动画）
- build/reports/perf/raw/idle-final2.log.jsonl（改动后静置）
- build/reports/perf/raw/idle-trim2.log.jsonl（idle trim 触发证据）
- build/reports/perf/raw/animation-summary.json、idle-summary.json（本次运行汇总）
- 复现脚本：build/reports/perf/raw/run-perf.ps1（Release exe + FLUIDBAR_PERF_PROBE=1；锁屏可用）
