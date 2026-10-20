# skia 模式 Avalonia 内存审计（task-37）

- 日期：2026-10-20
- 范围：src/FluidBar.App（Program/AppBootstrap/渲染策略）、tests；未改 Island 渲染与 Views/Settings 样式
- 构建：Release net10.0-windows10.0.19041.0；基线来源任务说明 2277 全绿（本工作树并行任务后实测 2287 全绿）
- 机器：AMD Ryzen 9 9955HX（16C/32T），Windows 10.0.26200，150% DPI
- 测量工具：SkiaHostPerfProbe（FLUIDBAR_PERF_PROBE=1，task-35 产物）+ 独立 Avalonia 初始化成本探针

## 0. 结论（先给结果）

按任务要求先量化两条路径，再决定是否“惰性初始化 Avalonia”。测量结果：

1. 真正的大头不是“Avalonia 生命周期”，而是 Avalonia 的 **GPU 后端**：平台默认渲染会加载 ANGLE/D3D/NVIDIA 用户态驱动（进程内模块镜像 +~190MB，工作集 +~40MB、私有字节 +~53MB）。
2. 把 skia 模式下（设置/市场窗口是唯一 Avalonia UI）的 Avalonia 后端改为 **CPU 软件渲染**，实测：
   - 工作集 **-29 ~ -34MB（-14% ~ -16%）**
   - 私有字节 **-54 ~ -66MB（-48% ~ -54%）**
   - 模块镜像 390MB -> 199MB，模块数 202 -> 176，线程 73 -> 35
   - 首帧时间 **-30 ~ -520ms**（GPU 初始化成本不固定）
   - 设置窗口首次打开 611ms -> 582ms（无退化；两种后端截图一致、UI 可正常操作）
3. “完全不初始化 Avalonia（惰性桌面生命周期）”的**额外**收益经独立探针量化只有：工作集 ~-13.7MB、私有字节 ~-2.6MB（Avalonia 软件路径 48.9MB/12.2MB 减去纯 .NET 35.4MB/9.8MB），低于任务要求的 20MB 阈值；且需要重写应用生命周期/调度器、重入 Avalonia 启动、改设置窗口所属线程与插件 dispatcher，破坏托盘/热键/退出路径的风险很高。
4. 因此按任务第 4 条“收益 <20MB 或风险高就如实说明，不要硬改”：**没有强行改成惰性生命周期**；改为落地低风险、收益更大的“skia 模式 Avalonia CPU 渲染策略”。不满足“未打开设置前不创建 Avalonia”的字面验收（Avalonia 运行时仍在进程内），但满足“未打开设置前不创建 GPU/Avalonia 图形栈”，内存收益是惰性方案的 2.5 倍以上。

## 1. 测量方法

1. 独立探针（消除 App 其他服务干扰）：临时 Release 控制台工程，同机运行三种模式各 5 秒后自报 工作集/私有字节/托管堆/模块数与模块镜像，以及关键模块列表：
   - none：不初始化 Avalonia
   - avalonia：Avalonia 平台默认（GPU/ANGLE）
   - software：Avalonia Win32RenderingMode.Software
2. 真实应用：FluidBar.exe（Release）skia host，探针 FLUIDBAR_PERF_PROBE=1，静置 10 秒采样；用环境变量 FLUIDBAR_AVALONIA_RENDERING=gpu 复现“改动前”，默认（不设）即“改动后”的软件策略。
3. 设置窗口：UIA 展开岛 -> Invoke island.settings -> 轮询 Avalonia 窗口出现并计时 -> PrintWindow 截图 -> 关闭；GPU 与软件两条路径各一次。
4. 全量测试：dotnet test FluidBar.sln。

环境噪声说明：测量期间同机有其他 agent 进程与系统通知流，工作集绝对值会有 ±5–10MB 波动；但“GPU 模块是否存在”是二值、稳定的，两种路径各测量多次结论一致。

## 2. 独立探针：Avalonia 初始化成本

原始输出：build/reports/perf/raw/avalonia-cost-probe.txt

| 模式 | 工作集 | 私有字节 | 托管堆 | 模块数 | 模块镜像 | 关键模块 |
|---|---:|---:|---:|---:|---:|---|
| none（纯 .NET 控制台） | 35.4MB | 9.8MB | 0.9MB | 68 | 85.3MB | 无 Avalonia/GPU |
| Avalonia 平台默认（GPU） | 85.1–89.7MB | 65.4–65.7MB | 1.3MB | 122 | 308.9MB | nvwgf2umx 83.7MB、nvgpucomp 79.3MB、av_libGLESv2 5.2MB、d2d1 6.2MB |
| Avalonia 软件渲染 | 48.9–49.0MB | 12.2MB | 1.2MB | 90 | 107.7MB | 无 NVIDIA/ANGLE 模块 |

推导：
- GPU 后端相对无 Avalonia：+50~54MB 工作集、+55.6MB 私有字节。
- 软件后端相对无 Avalonia：+13.5~13.7MB 工作集、+2.4~2.6MB 私有字节（这就是“彻底惰性”的额外上限收益，<20MB）。
- GPU -> 软件：约 -36~40MB 工作集、-53MB 私有字节（本次落地方案的主要收益来源）。

## 3. 真实应用前后对比（skia host，静置 10s）

改动前 = FLUIDBAR_AVALONIA_RENDERING=gpu；改动后 = 默认策略（软件）。

| 指标 | 改动前（GPU） | 改动后（软件策略） | 差异 |
|---|---:|---:|---:|
| 工作集（探针末次采样） | 212.6MB | 178.2MB | -34.4MB（-16.2%） |
| 私有字节 | 122.4MB | 56.4MB | -66.0MB（-54.0%） |
| 托管堆 | 10.4MB | 9.5MB | -0.9MB |
| GC committed | 25.6MB | 25.2MB | 基本不变 |
| 模块数 / 模块镜像 | 202 / 389.9MB | 176 / 199.4MB | -26 / -190.5MB |
| 线程数 | 73 | 35 | -38 |
| 进程内 GPU 模块 | nvwgf2umx 83.7MB、nvgpucomp 79.3MB、ANGLE 5.2MB、d2d1 6.2MB | 无 | GPU 栈不再加载 |
| 启动 -> 首帧 | 698.5ms（另一次 1035.2ms） | 668.1ms（另一次 514.6ms） | -30 ~ -520ms |
| 设置窗口首次打开（Invoke -> 窗口出现） | 611ms（另一次 586ms） | 582ms（另一次 588ms） | 无退化 |
| 设置窗口渲染 | 正常 | 正常（截图一致，UIA 结构一致） | — |
| 岛渲染（Skia） | 不变 | 不变 | 策略只作用于 Avalonia 窗口 |

多次独立运行的稳定观测：工作集 211–212.6 -> 177.3–183.2（-29 ~ -34MB）；私有字节 110.9–122.4 -> 56.3–58.0（-54 ~ -66MB）。运行期间静置 CPU 均为 1.7–1.8%（通知流驱动，与渲染后端无关）。

## 4. 验收材料

- 探针数值（真实窗口截图）：
  - build/reports/perf/raw/probe-gpu-values.png：工作集 212.6MB / 私有 122.4MB / modules 202，topModules 含 NVIDIA/ANGLE
  - build/reports/perf/raw/probe-software-values.png：工作集 178.2MB / 私有 56.4MB / modules 176，topModules 无 NVIDIA/ANGLE
- 设置窗口可用（同一版本、两种后端）：
  - build/reports/perf/raw/policy-settings.png（软件策略，582ms 打开，功能/排版正常）
  - build/reports/perf/raw/gpu-final-settings.png（GPU 后端，611ms）
- 原始 JSONL：raw/av-policy.log.jsonl、raw/av-gpu4.log.jsonl
- 汇总：raw/av-policy.summary.json、raw/av-gpu4.summary.json
- 复现脚本：raw/run-perf.ps1（-AvaloniaSoftware 0 = GPU，不传 = 默认策略）、raw/settings-open.ps1

## 5. 实施内容

1. 新增 src/FluidBar.App/AvaloniaRenderingPolicy.cs：
   - 默认/skia host（FLUIDBAR_HOST 为空或 skia）=> Avalonia 使用 Win32RenderingMode.Software
   - FLUIDBAR_HOST=avalonia（旧窗口岛宿主）=> 保持平台默认（GPU），不影响其整窗渲染路径
   - 覆盖开关：FLUIDBAR_AVALONIA_RENDERING=gpu|software；兼容 perf 脚本的 FLUIDBAR_AVALONIA_SOFTWARE=1/0
2. Program.cs：把策略作为最后一个 Win32PlatformOptions 应用（后应用的选项生效）；软件渲染只影响 Avalonia 的设置/市场窗口，岛仍是 Skia CPU 光栅，无视觉变化。
3. AppBootstrap：启动时记录 app.avalonia-rendering 日志（backend/host/override），便于探针与现场诊断。
4. 新增 tests/FluidBar.Ui.Tests/AvaloniaRenderingPolicyTests.cs：8 条策略用例（skia/默认/avalonia/大小写/覆盖 gpu/software/1/0）+ 1 条“软件策略下设置窗口可构建、可显示、内容存在”。
5. 附带修复：并行任务在 Theme/DesignTokens.cs 与 Views/Settings/SettingsTokens.cs 留下的 Color.FromUint32 拼写错误（Avalonia API 为 FromUInt32）阻塞了 App 编译，已按原意修正以恢复构建；未改样式语义。

## 6. 未实施“惰性 Avalonia 桌面生命周期”的原因与后续方案

收益：独立探针显示彻底不初始化 Avalonia 只剩 13.5–13.7MB 工作集/2.4–2.6MB 私有字节的额外空间（<20MB 阈值）。
风险/成本：
- 当前 AppBootstrap 运行在 Avalonia ClassicDesktopLifetime 内，直接依赖 IClassicDesktopStyleApplicationLifetime（退出）、Dispatcher.UIThread（托盘/热键/更新回调、插件 dispatcher、设置窗口重建）和 Avalonia 线程模型；去掉它需要新增生命周期/调度器抽象并迁移多处调用点。
- 设置窗口必须在 Avalonia UI 线程创建；惰性启动要么在主线程重入 StartWithClassicDesktopLifetime（阻塞式 lifetime 与现有消息循环/岛窗口消息竞争），要么把 Avalonia 放到独立 STA 线程（窗口所有权、退出序列、托盘回调全部跨线程）。
- 插件运行时、marketplace、设置窗口重建、FLUIDBAR_SMOKE 截图路径都需要回归；任何一处死锁都会破坏托盘/设置/退出，风险显著高于 13.7MB 收益。
若后续仍要做：建议单独任务，先抽出 IAppLifetime/IAppDispatcher，再实现“首开设置时启动 Avalonia（独立 STA 线程）+ 显式窗口服务”，配 3 个集成测试（无 Avalonia 启动/托盘回调/设置开关窗）和完整人工回归；本次按任务第 4 条不硬改。

## 7. 测试

dotnet test FluidBar.sln：**2287 passed / 0 failed / 0 skipped**（9 个测试工程；基线任务说明 2277，本任务新增 9 条，另有并行任务新增 1 条）。既有 golden/timing/allocation/设置窗口测试全部通过。

## 8. 复现

1. dotnet build src/FluidBar.App/FluidBar.App.csproj -c Release
2. 改动前：设置 FLUIDBAR_AVALONIA_RENDERING=gpu、FLUIDBAR_HOST=skia、FLUIDBAR_PERF_PROBE=1，启动 FluidBar.exe，静置 10s，取 %LOCALAPPDATA%\FluidBar\logs\*.jsonl 中 island.skia.perf / island.skia.memory
3. 改动后：去掉 FLUIDBAR_AVALONIA_RENDERING（或 =software）重复；raw/run-perf.ps1 已封装两种模式与设置窗口验证。
