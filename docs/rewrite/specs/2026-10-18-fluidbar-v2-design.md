# FluidBar v2 设计规范（已验证）

| 项 | 值 |
|---|---|
| 文档 | `docs/rewrite/specs/2026-10-18-fluidbar-v2-design.md` |
| 状态 | **待 Lead（Doulor）确认** — 第 0 节 D1–D12 与第 11.2 节 Q1–Q10 需逐条签字后冻结 |
| 日期 | 2026-10-18 |
| 目标版本 | FluidBar 2.0.0（`net10.0-windows10.0.19041.0`，Windows 10 19041+ / Windows 11） |
| 许可 | FluidBar 为 MIT。WinIsland / boring.notch / Atoll / livebridge 为 GPL-3.0，**只读参考，不复制代码/资源/字符串表**；MIT 参考（devcode90 模组、mchack、NotchDrop）可复制但须保留版权声明。 |
| 输入 | [01 参考与视觉研究](../research/01-references-design.md) · [02 v1 代码审计](../research/02-current-code-audit.md) · [03 渲染栈研究](../research/03-tech-stack-rendering.md) · [04 插件生态与 i18n](../research/04-plugin-ecosystem-i18n.md) · [交互原型](../prototype/README.md) |
| 关联 | 实施计划 [`plans/2026-10-18-v2-implementation-plan.md`](../plans/2026-10-18-v2-implementation-plan.md) |
| 原型差异 | 原型为 420 px 宽 + 底部标签栏；**本规范以最新用户反馈为准：440 px 宽、无 rail 的横向分页（鼠标拖动/滚轮翻页）**，展开面板统一 **440×284**（六个页面同尺寸，切换页面不改变外壳尺寸）；紧凑 HUD 为单行 40 高。原型其余视觉/动效结论继续有效。 |

**阅读约定**：所有尺寸为 **DIP @96 DPI**（100% 缩放）；`[审计 F-xx]` 指向 v1 缺陷编号；`未决` 项必须带 owner 与决策截止日；本规范中出现的每个数字都可被测试或验收项引用，禁止实现时"就近取值"。

## 0. 已定的关键决策（一页摘要）

| # | 决策 | 依据 |
|---|---|---|
| D1 | 产品是**悬浮药丸**，不是贴边条；`flush`（贴顶）与 `dock`（底部任务栏替身）是同一外壳的两种模式，不是三套 UI | 参考共识：Windows 无刘海，悬浮是正解 [01 §7.1] |
| D2 | 状态机五态：`Hidden / Compact / Peek / Expanded / Closing`；**没有** v1 的"每事件重建快照窗口" | [审计 F-03]，硬规则 3 |
| D3 | 展开面板 = **440 宽（左右 inset 20 → 内容 400）**；六个页面统一 440×284 的横向分页（鼠标拖动/滚轮/键盘翻页，外壳不 resize） | 最新用户反馈 |
| D4 | 动效统一以 `(T, ζ)` 授权，运行时推导 `k=(2π/T)²`、`c=4πζ/T`；定步长 1/300 s 子步 | [01 §8.1]、原型 Motion Lab |
| D5 | 渲染底座（2026-10-18 修订）：**Win32 分层窗口（`UpdateLayeredWindow`，逐像素 alpha）+ SkiaSharp 岛渲染器**；Avalonia 仅用于设置/插件市场等普通窗口。Avalonia 11/12 的 DirectComposition 在目标机器上透明失效（灰矩形），WPF/原生分层窗口透明正常 | spike 05 + 后续探针 |
| D6 | 回退顺序：Avalonia 失败 → WPF 固定尺寸分层窗口（60 fps 天花板）→ 自绘 D2D（仅在需要 120 fps 且 Avalonia 不可用时） | [03 §7 F1/F2] |
| D7 | **永不逐帧改窗口 bounds**；每显示器一个固定尺寸宿主窗口（488×332），动画只发生在其内部 | [审计 F-02/F-04]、硬规则 1/3 |
| D8 | 插件 = 内置编译期 + 社区签名 DLL（可回收 ALC），**仅声明式 UI**，禁止插件 XAML；能力是"披露+同意"，不是沙箱 | [04 §3/§6]、第 6.5 节 |
| D9 | 市场 = GitHub Pages 上的签名 `index.json` + 分片，包放 GitHub Releases，`.fbp` 确定性 ZIP，SHA-256 + ECDSA P-256，发布者密钥 TOFU | [04 §4] |
| D10 | i18n 用 **JSON 目录**（不是 resx），`en-US` 为源、`zh-Hans` 首批；插件/市场同格式 | 第 7.1 节给出理由 |
| D11 | 新增模块（日历/天气/硬件监控/文件托盘/音频频谱）**不进核心**，v2.0 之后以插件形式发布 | 第 10.3 节 |
| D12 | 性能合同：120 fps 目标（p99 ≤ 8.33 ms）、60 fps 底线、空闲 CPU < 1%（目标 < 0.3%）、常驻内存 < 120 MB（上限 150 MB） | 第 9.1 节 |

## 1. 目标 / 非目标 / 用户场景
### 1.1 目标（v2.0 必须全部成立）
1. **常驻不打扰**：小岛在屏幕顶部常驻，空闲时 0 帧回调、0 GPU 工作、CPU < 0.3%；淡出后不吞点击、不抢焦点。
2. **一点即达**：点击/热键展开为 440 px 控制中心；六页横向分页（home / media / notifications / clipboard / agent / widgets），鼠标拖动/滚轮/键盘翻页；Esc 逐级返回。
3. **可替代任务栏（可选）**：`dock` 模式在底部提供 52 高的窄条（应用瓦片 + 托盘簇），展开面板从窄条上方弹出；不注册系统 AppBar、不抢占工作区。
4. **插件市场闭环**：浏览 → 同意能力 → 安装 → 更新 → 回滚 → 卸载；全部产物由 CI 从评审过的源码构建并签名。
5. **双语与可切换**：`zh-Hans` + `en-US` 运行时切换不重启；岛内文字、设置、托盘、错误、市场页全部走资源。
6. **可测量**：每个性能/质量指标都有对应测试或验收脚本，回归即失败。
### 1.2 非目标（v2.0 明确不做）
- 不做 macOS 风格的"刘海"物理贴合（无刘海）；`flush` 只是视觉变体。
- 不做插件沙箱 / 权限强制；不做插件 XAML / 自定义渲染器 / 插件进程（v2.1 才有 out-of-proc 通道）。
- 不做评分/评论、付费插件、插件间依赖自动安装、遥测面板。
- 不做 WebView2、不做 WinUI 3 外壳（无逐像素 alpha，形状模型不匹配 [03 §2b]）。
- 不做 v1 的 `Always` 常亮描边动画、不做 50 ms 波形布局定时器、不做每事件重建快照 HWND。
- 不做天气预报、日历、CPU/GPU 监控、文件托盘、音频频谱（见 D11）。
### 1.3 用户场景与验收口径

| # | 场景 | 用户动作 | 期望（可验收） |
|---|---|---|---|
| S1 | 小岛常驻 | 无操作，看视频/写代码 10 min | 岛可见或已淡出；点击穿透到下方应用；`dotnet-counters` 采样的进程 CPU 均值 < 0.3%，帧回调计数 = 0 |
| S2 | 点击展开 | 单击 idle 药丸 | ≤ 2 帧内出现首个变化像素；展开在 0.27 s（±20%）内收敛；焦点不跳到岛（除文本框） |
| S3 | 控制中心 | 横向拖动/滚轮到第 2 页（媒体） | 面板尺寸不变（440×284）；页面切换只做横向位移动画，外壳不 resize；键盘 `2` 等效 |
| S4 | 悬停预览 | 指针停在 media 药丸 90 ms | +10 w / +6 h 的 Peek，露出上一首/下一首；移开 120 ms 后回落 |
| S5 | 通知打断 | 媒体展开时来一条通知 | 外壳状态不变；内容 220 ms 交叉淡入；+1 角标；不重建页面、不丢焦点 |
| S6 | Dock 替代 | 切到 dock 模式 | 底部 52 高窄条出现；点应用瓦片在窄条上方展开对应页；再点同一瓦片或 Esc 关闭 |
| S7 | 插件安装 | 市场 → 安装 → 同意能力 | 下载→校验→解压→暂存→激活全部可见；失败给原因与日志；旧版本保持可用 |
| S8 | 语言切换 | 设置 → English | 全部可见文字即时切换（含已打开的市场页与托盘菜单）；不重启、不丢状态 |
| S9 | 多显示器 | 在 150% 显示器上拖动岛到 100% 显示器 | 无 >2 px 跳变；文本不模糊；落点按目标显示器工作区钳制 |
| S10 | 全屏抑制 | 启动全屏游戏/放映 | 1 s 内进入 Hidden；退出后 1 s 内恢复；有按应用豁免名单 |

## 2. 产品与交互模型

> **修订 A1（2026-10-18，用户反馈）**：否决左侧 rail。展开面板导航改为**横向鼠标拖动 pager**：页宽 400 DIP、视口 400×252；拖动阻尼 0.90、端点橡皮筋 0.35；释放时 |拖动|<24 DIP 回弹，flick（<0.5 页）最多 ±1 页，长拖 ≥0.5 页按 `round(dx/400)` 可跨多页；滚轮 100 DIP/120 ms 一格一页；吸附弹簧 `motion.tab`（T .34/ζ .92）可打断；页点+标题+右上收起。本节及后文任何 rail 字样以本修订为准。
### 2.1 状态机（五态）
`Hidden ⇄ Compact ⇄ Peek`；`Compact|Peek → Expanded`；`Expanded → Closing → {Compact | Hidden}`；`Closing → Expanded`（T12 重定向）。`pinned` 与抑制集合（2.3/2.9）是状态机之上的正交标志，不增加状态。

| # | 从 | 触发 | 守卫 | 到 | 动作 |
|---|---|---|---|---|---|
| T1 | Hidden | 活动到达（优先级 ≥ 环境类） | 非全屏、非锁屏、非专注抑制、`island.enabled` | Compact | **瞬时显形**（无展开动画），1 次 nudge（−6 DIP 垂直踢） |
| T2 | Compact | 指针进入药丸 | `hoverPeek` 且未 pinned、未被拖动 | Peek | 90 ms 悬停意图后 `motion.hoverPop`，+10 w / +6 h；露出 Peek 附件 |
| T3 | Peek | 指针离开（120 ms 去抖） | — | Compact | `motion.hoverPop` 反向；重入取消去抖 |
| T4 | Compact / Peek | 主键单击、`Ctrl+Alt+D`、交互类活动（lookup） | 未被拖动 | Expanded | `motion.expand` + `motion.expand.height`；**一次布局**；焦点进入当前页项 |
| T5 | Expanded | Esc / 点击外部 / 热键 / 自动收起到期 / 活动结束 | 非 pinned（Esc/热键除外） | Closing | `motion.collapse`；内容透明度按高度派生 |
| T6 | Closing | 收敛 | 仍有活动 | Compact | 播放 1 次收尾 nudge；恢复点击穿透判定 |
| T7 | Closing | 收敛 | 无活动或刚被隐藏 | Hidden | 窗口进入 click-through 空集，帧循环停止 |
| T8 | Compact | 自动隐藏到期 / 热键 / 全屏 / 锁屏 | `autoHideMs>0` | Hidden | 同上；`Hidden` 保留 10 px 悬停热区（可关） |
| T9 | Hidden | 活动到达但被抑制 | 全屏/专注/锁屏 | Hidden | 仅入队 + 更新托盘 tooltip，不显形 |
| T10 | 任意 | 显示器/DPI/主题变更 | — | 同态（snap） | 弹簧直接 snap 到新目标并清零速度，一次 `SetWindowPos` |
| T11 | Expanded | 新活动（任意优先级） | — | Expanded | 仅走内容更新路径（2.7），外壳不动 |
| T12 | Closing | 主键单击 | — | Expanded | 速度重定向（4.3）：同向保留 100%，反向 ×0.35 |

**不变量**

- `Expanded` 期间**永不**重建页面根、永不改变内容高度（除页面切换）。
- `Closing` 只接受 T12 与 T10；其余输入排队到收敛后 1 帧处理。
- `pinned`（右键固定 / 设置固定）抑制 T5 的自动收起与 T1/T9 的抢占，但不抑制用户显式关闭。
- 状态迁移全部是纯函数：`Transition(state, event, settings, now) → (state, effects[])`，副作用由渲染层执行（第 5.4 节）。
### 2.2 三种模式（float / flush / dock）

| 维度 | `float`（默认） | `flush` | `dock` |
|---|---|---|---|
| 锚点 | 工作区顶部居中，`offsetY` 默认 12 | 显示器顶部 y=0（含任务栏区域之上） | 工作区底部，距任务栏 4 |
| 圆角 | 紧凑 `h/2`；展开 28 | 上圆角 0，下圆角 22 | 紧凑 12 / 展开 16 |
| 阴影 | 向下 8 / 模糊 24 / 黑 35% | 无（避免顶部漏边） | 向下 4 / 模糊 12 / 黑 28% |
| 可拖拽 | 水平 + 垂直 | 仅水平 | 不可拖拽（固定） |
| 展开方向 | 向下生长 | 向下生长 | **向上**生长（面板底边贴窄条顶边 −8） |
| 目标 | 悬浮药丸 | 顶部贴边（"类刘海"视觉） | 任务栏替身 |
| 与系统任务栏 | 不动 | 不动 | **不隐藏**系统任务栏；可选设置用 `SHAppBarMessage(ABM_SETSTATE, ABS_AUTOHIDE)` 自动隐藏，退出时恢复 |

> **Dock 决策**：v2.0 **不注册** AppBar（不调用 `ABM_NEW`，不保留工作区）。原因：AppBar 会永久改变全系统工作区，卸载/崩溃残留是高风险缺陷；`ABS_AUTOHIDE` 是文档化、可恢复的最小侵入方案。是否在 v2.1 做真正的任务栏替换（含 `ABM_SETPOS`）为 **未决**（owner: Doulor，截止 2026-11-14）。

模式切换规则：`mode` 变更 → snap（不做跨模式补间）→ 一次 `SetWindowPos` → 重新计算锚点与展开方向（dock 向上）。dock 模式下的紧凑尺寸统一为 `440×52`（窄条整宽），非活动包络不再使用紧凑药丸尺寸。
### 2.3 活动优先级 / 替换 / 堆叠 / 过期
优先级顺序沿用审计确认的 v1/参考排序（"短时、用户发起"高于"环境"，媒体最后）：

| 秩 | `activityKey` | 类 | 紧凑尺寸 | 过期 | 说明 |
|---|---|---|---|---|---|
| 1 | `lookup` | 交互 | 480×44 | 手动 | 搜索/取词；唯一允许抢焦点（临时去掉 `NOACTIVATE`） |
| 2 | `clipboard` | 瞬时 | 264×36 | 4 s | 文本/图片缩略 |
| 3 | `lockkeys` | 瞬时 | 180×36 | 1.6 s | Caps/Num/Scroll |
| 4 | `device` | 瞬时 | 240×44 | 3 s | USB 插拔 |
| 5 | `bluetooth` | 瞬时 | 280×44 | 3 s | 连接/断开 + 电量 |
| 6 | `focus` | 环境 | 220×36 | 持续 | 专注助手/DND 指示 |
| 7 | `volume` | 瞬时 | 236×40 | 1.6 s | **单行 HUD**（2.8） |
| 8 | `brightness` | 瞬时 | 236×40 | 1.6 s | **单行 HUD**（2.8） |
| 9 | `notification` | 瞬时 | 248×36 | 5 s | toast 摘要 + 验证码识别 |
| 10 | `battery.low` | 环境 | 208×36 | 持续 | <20% 且未充电 |
| 11 | `progress` | 持续 | 236×36 | 任务期 | 更新下载/长任务 |
| 12 | `timer` | 环境 | 190×36 | 任务期 | 番茄钟 |
| 13 | `media` | 持续 | 226×40 | 播放期 | GSMTC |
| 14 | `idle` | 默认 | 132×36 | — | 时钟 + 电量 + 页点 |

**规则（全部为纯策略，可单测）**

1. **同键替换**：相同 `activityKey`（kind+source）再次发生 → 就地更新内容，不重新入栈，不重启动画，只发 1 次 nudge（4.7）。
2. **抢占**：新活动秩 < 当前主活动秩 → 抢占主位。被抢占者若为"持续"类（`media`/`timer`/`progress`/`battery.low`/`focus`）→ 进入背景层；否则丢弃。
3. **同级**：后到者替换前者（背景层不变），前者记一次 `replaced` 计数器（诊断用）。
4. **背景层**：最多 2 个 ghost 药丸（y −6/−12，scale .96/.92，opacity .55/.35），总可见 3；超过则 `+N` 角标。队列深度上限 8，溢出丢弃最旧的非持续项。
5. **过期**：见上表；仅在"非 hover、非 pinned、非展开到该活动页"时执行；过期走 `motion.hide`，收敛后回到下一个活动或 idle。
6. **媒体不抢占**：`media` 永不主动弹出示意（等价 `MediaAutoExpand=false`）；它只在无更高活动时占据主位。用户可在设置里开"换曲时 Peek"（默认关）并维护按应用黑名单。
7. **pinned**：抑制抢占与过期，只更新内容；角标不增长（改为底部计数行）。
8. **抑制集合**：全屏、专注助手、锁屏、会话断开 → 全部活动只入队不显形（T9），恢复时只重放秩 ≤ 9 的项目。
### 2.4 输入模型

| 手势 | float | flush | dock | 说明 |
|---|---|---|---|---|
| 单击药丸/窄条 | 展开 | 展开 | 展开对应页（点空白 → home） | 500 ms 内双击不重复展开 |
| 双击药丸 | 打开来源应用 | 同 | 同 | "tap opens the related screen"（Apple 规则） |
| 悬停 90 ms | Peek | Peek | 图标 scale 1.12 + 标签 | dock 的 Peek 等价物是图标放大 |
| 拖拽 | 水平+垂直，橡皮筋 | 水平 | 禁用 | 释放吸附最近锚点（阈值 24 DIP），否则保留自由偏移并写回该显示器 |
| 面板内横向拖动 | 指针捕获，damping 0.90，端点 0.35 | 同 | — | flick≤1 页；长拖可多页；>6 DIP 取消页内 click |
| 拖动中 Esc | 取消并回弹 | 同 | — | 回弹用 `motion.hoverPop` |
| 滚轮（展开时） | 累积 100 DIP + 120 ms cooldown，一格一页 | 同 | 同 | 滑杆上不翻页；触控板 deltaX 同义 |
| 右键 | 会话级菜单（固定/模式/材质/设置…） | 同 | 同 | **不得**写入持久设置（v1 的反面教材 [01 §2.7]） |
| `Esc` | 关闭设置 → 收面板 → 无 | 同 | 同 | 逐级消费 |
| 长按（触摸屏） | 固定/取消固定 | 同 | 同 | 仅检测到触摸数字化仪时启用 |
| 点击外部 | 收起 | 同 | 同 | 实现为**焦点丢失检测**，不是全屏透明窗口 [01 §9] |

**键盘**（`RegisterHotKey`，注册失败在设置里显红并给出冲突提示，不静默失败）：

| 热键 | 动作 |
|---|---|
| `Ctrl+Alt+D` | 展开/收起 |
| `Ctrl+Alt+H` | 隐藏/显示 |
| `Ctrl+Alt+S` | 设置窗口 |
| `Esc` | 逐级返回 |
| `1`–`6` | 展开时切页（pager） |
| `LeftAlt` 长按（默认，可改） | 按住隐藏；使用 `WH_KEYBOARD_LL`，仅在岛可见时挂载，隐藏即卸载 |
### 2.5 多活动堆叠
- 主药丸 + 2 个 ghost（同宽、y 偏移、缩放、透明度递减），阴影只画在最外层锚点伪元素上，避免堆叠被阴影吞掉。
- ghost 不接收输入；`+N` 角标可点击 → 打开 `notifications`/对应页。
- 新活动入栈：与主同键 → 替换；否则按秩插入。**不做**逐项入场动画，只有主药丸 nudge 与 ghost 的 opacity 变化。
- 策略可切换：`stack`（默认）/ `latest`（仅最新，无 ghost）。切换时 snap 重排。
### 2.6 多显示器与拖拽交接
- `displayOn`: `primary`（默认）/ `all` / `follow-mouse` / `specific(monitorId)`。
- **每显示器一个宿主窗口**，按需创建（primary 启动即建；其他在目标出现时建，闲置 5 min 后置空）。
- 跨显示器交接：目标窗口先就位 → 旧窗口淡出（`motion.hide` 0.2 s）→ 旧窗口隐藏。**不做**跨窗口的连续位移动画。
- 每显示器独立持久化 `{anchor, offsetX, offsetY}`，键为 `monitorId`（`\\.\DISPLAYn` + EDID hash），回退键为 `(bounds, dpi)` 三元组。
- DPI：Per-Monitor V2，`WM_DPICHANGED` 重算 DIP→像素并用新工作区重新钳制；动画中收到则 snap（T10）。
- 工作区而非显示器边界：`MonitorInfo.rcWork`（自动隐藏任务栏时用 `ABM_GETSTATE` 判定）。
### 2.7 内容替换与幂等策略（v1 "hover card 重建"问题的正式答案）
v1 的 P0 缺陷是"每个事件都重开卡片、逐帧重排、丢焦点"（[审计 F-09]）。v2 的规则：

1. **外壳与内容解耦**：`Expanded` 打开后外壳尺寸由**页面**决定，事件只更新内容槽。
2. **内容签名**：`ContentSignature = (activityKey, kind, titleHash, bodyHash, progressBucket)`；签名相同 → 丢弃，零工作。
3. **替换而非重开**：签名变化 → 220 ms 交叉淡入 + 8 DIP 上滑；页面根、滚动位置、键盘焦点、pager 当前页与偏移全部保留。
4. **尺寸不变**：内容替换**永不**改变面板高度；溢出走滚动（单页最大滚动 1 屏）或尾部裁切 + 省略号。
5. **自动切页的克制**：仅当（a）用户在 current 页停留 > 600 ms，（b）新活动为交互/瞬时类，（c）非 pinned，（d）不在拖拽/滚动中 → 才切到该活动页；否则只 +1 角标 + nudge。
6. **打开去抖**：同一目标计划在运行中不得重启；`Wanted(Expanded, page)` 是幂等目标，重复请求被合并。
7. **离开去抖**：`MouseLeave` 120 ms 后生效，重入立即取消；指针在展开后的面板内移动不触发任何收起逻辑。
8. **不因歌词/进度重建**：歌词行、进度条以"属性更新"进入既有控件；禁止在 `SizeChanged`/`LayoutUpdated` 里重建可视树（v1 的 60 Hz 歌词重建根因）。
### 2.8 紧凑 HUD（音量 / 亮度）
单行，高 **40**，宽 **236**：`icon 16 · gap 8 · label(音量/亮度) 40 · gap 10 · track 96 · gap 10 · value 40`，左右 inset 12。

- 图标：音量 4 档（静音/低/中/高）+ 亮度 4 档，`Segoe Fluent Icons`。
- track：4 DIP 高、radius 2；填充用 `--accent`；静音时填充替换为 2 DIP 描边并显示"静音"文字。
- value：等宽数字（tabular），右对齐；`62%` 与 `62 %`（fr 等）由 i18n 数字格式决定。
- 键盘聚焦时 track 可获得焦点并响应 `←/→`（±2%），`Home/End` 到 0/100。
- HUD **不可**展开为面板（点击只到 home），避免"点音量弹出媒体页"的错位。
### 2.9 抑制、无障碍与输入法
- **全屏检测**：`SHQueryUserNotificationState` ∈ {`QUNS_RUNNING_D3D_FULL_SCREEN`, `QUNS_PRESENTATION_MODE`, `QUNS_BUSY`} 或前台窗口矩形覆盖目标显示器且非 shell 类 → Hidden。每应用豁免名单（设置可编辑）。
- **锁屏/会话切换**：`WTSRegisterSessionNotification` → 立即 Hidden，停止所有生产者（不止渲染）。
- **无障碍**：页点、transport、行内按钮均暴露 `AutomationProperties.Name`（本地化）；焦点环 2 DIP `--info`；全键盘可达；高对比主题下材质降级为不透明；屏幕阅读器朗读"活动 + 数值"。
- **减少动效**：跟随 `SPI_GETCLIENTAREAANIMATION` 与设置三态（跟随系统/开/关）；开启后 ζ→1、nudge 振幅 0、bloom 0、时长 ×0.5、封面旋转关闭。
- **输入法**：仅在前台窗口变化或 1 s 去抖时查询一次，使用 `SendMessageTimeout(SMTO_ABORTIFHUNG, 50ms)`；绝不 6.7 Hz 轮询（[审计 F-14]）。

## 3. 视觉规范
### 3.1 几何 tokens（DIP @96）

| Token | 值 | 用途 / 推导 |
|---|---|---|
| `shell.width.expanded` | **440** | 最新反馈；= 内容 400 + 2×20 inset |
| `shell.content.width` | **400** | 内容区（440 − 2×20）；横向 pager 页宽 400 |
| `shell.height.expanded` | **284** | 六个页面统一；内容 252（284 − 2×16 垂直 inset）；切换页面不改变外壳尺寸 |
| `shell.height.dock.strip` | **52** | dock 窄条 |
| `shell.window.pad` | **24** | 阴影/抗锯齿轮廓余量；窗口 = 壳体 + 2×24 |
| `shell.window.size` | **488×384**（float/flush）/ **488×444**（dock） | 每显示器固定宿主；底部额外留出面板外的 detached mini-island 条（gap 10 + strip 30）；dock 再加 52 窄条 |
| `island.idle/media/hud` | 132×36 / 226×40 / 236×40 | 时钟+电量+页点 / 28 art + 8 gap + 文本 + 28 控制 / 单行 HUD（2.8） |
| `island.notification/clipboard/agent/timer/battery` | 248×36 / 264×36 / 236×36 / 190×36 / 208×36 | 瞬时与环境类紧凑活动 |
| `island.lookup` | 480×44 | 唯一宽于 440 的紧凑态（不属于展开面板） |
| `island.lockkeys/device/bluetooth/focus/progress` | 180×36 / 240×44 / 280×44 / 220×36 / 236×36 | 其余瞬时/环境活动；文本宽度实测后按 `island.width.quantum=2` 向上取整，避免逐帧改窗口 |
| `peek.delta` | +10 w / +6 h | 悬停 Peek |
| `stack.ghost.dy` | −6 / −12 | 背景药丸偏移 |
| `stack.ghost.scale` | .96 / .92 | |
| `radius.shell` | **28** | 展开；squircle k≈2.77 @440×284 |
| `radius.compact` | `h/2`，cap 44 | 胶囊 = 椭圆端点 |
| `radius.flush.bottom` | 22 | flush 模式 |
| `radius.dock.compact` | 12 / `radius.dock.expanded` 16 | dock |
| `radius.card` | **12** | 同心规则：outer 28 − inset 16 |
| `radius.row` | 8 | 列表行/输入行 |
| `radius.button` | 10 | 32×32 命中，34 视觉 |
| `radius.art.small/large` | 8 / 12 | 28 / 64 封面 |
| `space.inset` | 12（紧凑）/ 16（内容）/ 20（分组） | |
| `space.gap` | 4 / 8 / 10 / 16 | 行内 / 行间 / art↔text / 分组 |
| `pager.dot` | 6 px 点 / active 20×6 胶囊，命中 20×20 | 页点 |
| `pager.threshold` | 24 DIP（拖动）/ 0.5 页（距离）/ 0.6 DIP·ms⁻¹（flick） | 翻页判定 | |
| `hit.min` | 32 | 桌面指针下限；transport 36 |
| `stroke.hairline` | 1 | 关键线 `#1E1E22`（暗）/ `#D8DBE1`（亮） |
| `focus.ring` | 2，offset 2 | `--info` |
| `shadow.float` | `0 8 24 rgba(0,0,0,.35)` | 仅向下 |
| `shadow.dock` | `0 4 12 rgba(0,0,0,.28)` | |
| `opacity.fill` | **0.96**（范围 0.85–1.0） | 承载文字的态不得低于 0.85 |

**横向 pager 布局**（无 rail；展开期间只有内容条在动）：

| 区域 | 规格 |
|---|---|
| 视口 | `400 × 252`（440 − 2×20 inset；284 − 2×16 垂直 inset），六页横向排列，页距 400 |
| 内容条 | 单层 `TranslateX`（transform-only，不触发布局）；页内控件只更新属性 |
| 标题 | 左上 overlay，15 px SemiBold，当前页名（本地化） |
| 页点 | 底部居中，6 点：6 px 圆点 / active 20×6 胶囊，命中 20×20，可点击跳页；`aria-current` |
| 收起 | 右上 chevron（Esc/外部点击同义） |
| 拖动 | 指针捕获；damped 1:1（0.90）+ 端点橡皮筋 0.35；移动 >6 DIP 取消页内 click；滑杆/按钮保留自身指针 |
| 翻页判定 | 阈值 24 DIP；flick（<0.5 页）最多 ±1 页；长拖 ≥0.5 页按 `round(dx/400)` 可跨多页；边界 clamp |
| 滚轮 | 累积 100 DIP + 120 ms cooldown，一格一页；滑杆上不翻页；deltaX 触控板同义 |
| 吸附 | 目标页偏移 + 弹簧 `motion.tab`（T .34 / ζ .92），可被新拖动从当前视觉偏移打断 |
| 键盘 | `1`–`6` 跳页，`←/→` 前后页，`Home/End` 首末页，`Esc` 收起 |
### 3.2 圆角与连续曲率
用超椭圆（squircle）而非圆角矩形：`Border.CornerRadius` 的圆弧读起来像"网页卡片"，不是 iOS 岛。

```
m  = min(w, h) / 2
rr = clamp(radius, 0, m)
k  = 2 + (1 - rr/m) * 1.22          // 胶囊 → k=2（真椭圆角）；小圆角 → k≈3.2
x(θ) = cx + rr·sign·|sin θ|^(2/k)
y(θ) = cy + rr·sign·|cos θ|^(2/k)   // 每象限 15 采样足够（≤44 px 半径）
```

- 采样点按 `(k*32)|0` 缓存；几何对象只在 `(w,h,radius)` 变化超过 0.5 DIP 时重建。
- 内容裁剪与命中区域**共用同一几何**（同一 `ShellGeometry` 实例），杜绝"点击目标偏 12 px"。
### 3.3 色彩
**暗色（默认，"Obsidian"）** — 岛体不透明黑 + 单根关键线，无 rim light（Apple：岛内为不透明黑；参考实现删除了内侧高光）：

| Token | Hex | 对比度 | 用途 |
|---|---|---|---|
| `--bg` | `#08080A` | — | 壳体填充（96% 不透明叠在模糊上） |
| `--bg.raised` / `--bg.inset` | `#141418` / `#0E0E11` | 1.09:1 | 内嵌卡片/轨道槽 · HUD 轨道/代码块 |
| `--stroke` | `#1E1E22` | 1.28:1 | 1 DIP 关键线 |
| `--line` / `--line.strong` | `rgba(255,255,255,.065)` / `.11` | — | 列表分隔 / 分组分隔 |
| `--fg` | `#FFFFFF` | 19.4:1 | 主文字/图标 |
| `--fg.secondary` | `#9B9BA5` | 7.2:1 | 次要文字（AA + AAA-large） |
| `--fg.tertiary` | `#6E6E78` | 3.6:1 | 仅禁用态/幽灵行 |
| `--accent` | 封面提取；回退 `#4CC9F0` | ≥8:1 | 进度、选中、强调条 |
| `--success` / `--warning` / `--danger` / `--info` | `#34C759` / `#FF9500` / `#FF453A` / `#0A84FF` | 9.1 / 8.0 / 5.6 / 5.0 | 摄像头点 / 麦克风点 / 低电量 / 链接 |

**亮色桌面（"Porcelain"）**：`--ui-bg #F5F6F8`、`--ui-fg #14161A`、`--ui-secondary #5B6270`（5.9:1）、`--ui-stroke #D8DBE1`、`--ui-raised #FFFFFF`。**岛体保持 `#08080A` 不变**——桌面主题只影响设置窗口、市场、托盘菜单与原型场景，岛是"硬件"。这是原型已验证的决策，v2 沿用。

备用调色板（随主题选择，仅 4 套 + 自定义 hex）：Obsidian / Graphite `#1C1C1E` / Slate `#111721` / Nord `#2E3440`。

规则：单一状态只有一个强调色；强调色 bloom 0–200%（默认 100%）；**禁止**内侧高光、渐变描边、紫色渐变洗底；文字承载态填充不透明度 ≥ 85%。
### 3.4 材质（obsidian / acrylic）

| 层 | 材质 | 实现 | 降级 |
|---|---|---|---|
| Tier 0 | **Obsidian**（默认） | 不透明 `#08080A` @96% + 向下阴影 | 永远可用 |
| Tier 1 | **Simulated glass（模拟玻璃）** | 自绘：2 段半透明渐变 + 1.5% 噪声 + 1 px 顶部高光；无需系统模糊 | 高对比/低端 GPU → Tier 0 |
| Tier 2（实验，默认关） | **整窗 AcrylicBlur** | Avalonia `TransparencyLevelHint=AcrylicBlur`；模糊覆盖整个固定宿主矩形，需配合 `SetWindowRgn` 才贴合壳体，动画期间会露边 | 探测失败 → Tier 1 |
| Tier 3（不采用） | 合成器 host backdrop / `SetWindowCompositionAttribute` | 无法得到"仅药丸区域"的原生模糊（spike 05 §5 NO-GO），或未文档化且为整矩形 | 不发布 |

- **原生"仅药丸区域"的 Acrylic 在 Avalonia 12 上不可行**：`AcrylicBlur`/`Mica` 作用于整个窗口矩形，而非壳体；`Compositor.CreateHostBackdropBrush` 无法进入自绘树得到药丸裁剪；`SetWindowCompositionAttribute` 未文档化且为矩形。因此 v2.0 以 **Tier 0 Obsidian 为默认、Tier 1 模拟玻璃为可选**；Tier 2 仅作实验开关（静止时可贴合，动画中会露边）。
- 材质切换在**静止时**生效（不叠在动画上），一次重绘；切换本身用 180 ms 交叉淡入。
- 透明/模拟玻璃下禁用 `BlurEffect`/实时模糊特效；文字使用 `Grayscale` 抗锯齿（透明表面无 ClearType，与 WPF 现状一致）。
- 高对比模式：强制 Tier 0 且 `opacity=1.0`。
### 3.5 字体与字阶
字体栈（显式回退，不用默认合成）：

```
UI      : "Segoe UI Variable Text", "Segoe UI", "Microsoft YaHei UI", "Yu Gothic UI", "Malgun Gothic", Arial
Display : "Segoe UI Variable Display", "Segoe UI Semibold", "Microsoft YaHei UI", "Yu Gothic UI"
Mono    : "Cascadia Mono", "Consolas", "Microsoft YaHei UI"
Icons   : "Segoe Fluent Icons" → "Segoe MDL2 Assets" → 手写矢量路径
```

| 角色 | 字号 | 字重 | 备注 |
|---|---|---|---|
| 岛内主标题 / 曲目 | 13.5 | SemiBold | 单行 + 字符省略 |
| 次要（艺术家/标签） | 11 | Normal | 全系统唯一的 Normal 用例 |
| 面板页标题 | 15 | SemiBold | 与标题 overlay 基线对齐 |
| 仪表盘时钟 | 34 | Bold | 等宽数字 |
| 仪表盘日期 | 13 | SemiBold | 本地化格式 |
| HUD 数值 | 12.5 | SemiBold | tabular |
| 徽标/角标 | 12 | Bold | |
| 区块小标题 | 11 | SemiBold | `+0.04em` 字距，全大写（CJK 不转大写） |
| 图标（行内/传输/导航） | 16 / 18 / 14 | Normal | 图标字体 |
| 代码/路径 | 12 | Normal | Mono |

自适应阶梯（用户字符串长度降级，替代纯省略号）：13.5 → 12.5 → 11.5 → 10.5 @22/32/44 字符。CJK 需额外 +4% 行高（`LineHeight` 显式设置，不用默认）。
### 3.6 图标与焦点
- 统一 24×24 网格、1.5 描边（16 px 下 1.25）；图标字体优先，其次手写 `Path`；**禁止 emoji 作为功能图标**。
- 方向性图标（chevron/back/进度方向）带 `MirrorInRtl` 元数据，RTL 下镜像。
- 焦点视觉：2 DIP `--info` 环 + 2 DIP 偏移；不使用发光或动画闪烁。

## 4. 动效规范
### 4.1 `(T, ζ)` 弹簧 tokens
授权单位是周期 `T`（秒）与阻尼比 `ζ`；运行时推导 `k=(2π/T)²`、`c=4πζ/T`。表内 `t₂%` 为 2% 包络收敛时间，用作测试期望值（±20%）。

| Token | T (s) | ζ | k | c | t₂% | 用途 |
|---|---|---|---|---|---|---|
| `motion.expand` | **0.36** | **0.82** | 304.6 | 28.6 | 0.27 | 紧凑 → 展开（宽） |
| `motion.expand.height` | **0.39** | **0.86** | 260.8 | 27.7 | 0.28 | 高度滞后 8%；ζ+0.04 |
| `motion.collapse` | **0.30** | **1.00** | 438.6 | 41.9 | 0.28 | 展开 → 紧凑（宽高同参） |
| `motion.morph` | 0.28 | 0.85 | 503.6 | 38.2 | 0.21 | 面板内页面切换 / 内容尺寸变化（仅页切换） |
| `motion.hoverPop` | 0.24 | 0.70 | 685.4 | 36.7 | 0.21 | Peek / 图标放大 / 拖拽回弹 |
| `motion.press` | 0.18 | 0.65 | 1218.5 | 45.4 | 0.17 | 按下 scale 0.97 |
| `motion.tab` | 0.34 | 0.92 | 341.5 | 34.0 | 0.23 | 页横向位移 / 页点指示 |
| `motion.nudge` | 0.26 | 0.55 | 584.0 | 26.6 | 0.29 | 新事件 −6 DIP 垂直踢 |
| `motion.hide` | 0.30 | 0.95 | 438.6 | 39.8 | 0.20 | 淡出 / 收起后消失 |
| `motion.radius` | 0.30 | 0.90 | 438.6 | 37.7 | 0.22 | 紧凑↔展开圆角（28 ↔ h/2） |

**派生的硬规则**

- 宽与高**必须**是两个独立弹簧（高度用 `motion.expand.height`），绝不共用一个进度值。
- 展开比收起"弹"（ζ 0.82 vs 1.00）；反过来会读作"黏"。
- 内容透明度由容器高度派生，不做独立动画：
  `alpha = clamp((h − 0.62·h_page) / (0.35·h_page), 0, 1)`，紧凑内容 `alpha_c = clamp(1 − 1.9·alpha, 0, 1)`。保证文字永不进入过小的容器。
- 缩小/放大变形（squash & stretch）由弹簧速度派生，不额外加弹簧：
  `W = W₀·(1 + clamp(v_w·0.000125, −.05, .062))`，`H = H₀·(1 − clamp(v_w·0.00008, −.04, .05))`。
- 动画总时长硬上限 **2000 ms**（Apple Live Activity 的公开上限），任何单次过渡不得超过。
### 4.2 定步长积分

```csharp
// 半隐式欧拉，固定子步；与帧率无关（120/144/165 Hz 结果一致）
const double H = 1.0 / 300.0;          // 3.333 ms 子步；T 最小 0.18 → 稳定性余量 ~50×
void Step(double dt) {                  // dt 已 clamp 到 [0.001, 0.050]
    for (double remaining = dt; remaining > 1e-9; ) {
        double h = Math.Min(H, remaining);
        double a = -k * (x - target) - c * v;
        v += a * h; x += v * h; remaining -= h;
    }
}
```

- `dt` clamp 到 `[1 ms, 50 ms]`；从睡眠恢复/显示器唤醒时 **snap 到 target 并清零速度**，不从陈旧值补间。
- 子步数上限保护：`dt=50 ms` → 15 次子步，可接受；禁止用闭式解替代（暂态重定向时闭式解不连续）。
- 所有弹簧共享一个 `SpringSet` 并**单循环步进**；全部 `AtRest` 时整帧短路。
### 4.3 重定向（retarget）与速度

| 情形 | 速度处理 |
|---|---|
| 方向不变（`sign(v) == sign(Δ)`） | 保留 100% 速度（"液态"手感的关键） |
| 方向相反 | `v ×= 0.35`（WinIsland 的 momentum retention） |
| 静止起步 | `v = 0`，不注入冲量 |
| 任意 | `\|v\| ≤ 0.2·\|Δ\| / dt`（防止目标很近时过冲成抖动） |
| 目标变化 < 0.5 DIP 且 `\|v\| < 1` | 直接吸附，不唤醒帧循环 |
### 4.4 收敛检测与帧循环
- 收敛判据：`|x − target| < 0.04 DIP` **且** `|v| < 0.35 DIP/s`，**连续 2 帧**成立 → 吸附、清零速度。
- 帧循环：`while (springSet.Step(dt) || dynamicContentDirty)` → 一帧一次 `InvalidateVisual()`；全部收敛后**取消帧请求并记录 `wakeups=0`**。
- 帧钩子由 `FrameClock` 抽象隔离：生产实现优先使用 Avalonia 组合器的帧回调（具体 API 在 M0 spike 确认）；退化实现为 `DispatcherTimer(DispatcherPriority.Render)` 自调度。两者都必须满足"收敛即停"。
- 高分辨率计时器：仅在动画期间 `timeBeginPeriod(1)`，静止 500 ms 后 `timeEndPeriod(1)`（引用计数配对）。
### 4.5 帧预算与功耗

| 目标 | 帧预算 | 托管侧预算 | 验收 |
|---|---|---|---|
| **120 fps（目标）** | 8.33 ms | **< 4.0 ms** p99 | 60 s 形变无掉帧（PresentMon 掉帧 < 0.5%） |
| 90 fps（最低"高级感"） | 11.11 ms | < 5.5 ms | 同上 |
| **60 fps（底线）** | 16.67 ms | < 8.0 ms | 低于此判为回归 |
| 空闲 | — | **0 帧回调** | 10 min 内 `frameCallbacks == 0`，CPU < 0.3% |
| 交互态 CPU | — | < 3% 单核 | 连续展开/收起 100 次平均值 |

不使用固定 60 Hz 定时器"假帧"；不通过增加帧数弥补视觉平滑（<120 Hz 时用速度驱动的运动模糊：`σ_h = min(|v_w|·0.3 + w·0.4·|v_scale|, cap)`，cap = 12 px 独显 / 4 px 核显，默认仅独显开启）。
### 4.6 非弹簧时长（毫秒）

| Token | 值 | 曲线 | 用途 |
|---|---|---|---|
| `dur.fadeIn` | 180 | `cubic-bezier(.2,0,0,1)` | 内容出现 |
| `dur.fadeOut` | 120 | `cubic-bezier(.4,0,1,1)` | 内容离开 |
| `dur.crossFade` | 220 | linear | 内容替换（2.7） |
| `dur.stagger` | 32 | — | 列表子项错峰 |
| `dur.hoverIntent` | 90 | — | 悬停意图 |
| `dur.pressFeedback` | 90 | — | 按下反馈 |
| `dur.leaveDebounce` | 120 | — | 离开去抖 |
| `dur.peekDwell` | 2400 | — | Peek 后自动收回（若未展开） |
| `dur.toastHold` | 3200 | — | 瞬时活动可见时长 |
| `dur.materialSwap` | 180 | — | 材质切换 |
| `dur.spin` | 600 | `cubic-bezier(.2,0,0,1)` | 换曲封面翻转 |
| `dur.max` | **2000** | — | 硬上限 |

缓动：进入 `cubic-bezier(.2,0,0,1)`；退出 `cubic-bezier(.4,0,1,1)`；移动 `cubic-bezier(.32,.72,0,1)`；过冲 `cubic-bezier(.16,1.4,.3,1)`。按压缩放 0.97、悬停 1.02（**不**超过 1.02：36 px 药丸在 1.06 会"呼吸"）。

## 5. 架构
### 5.1 目标工程布局

```
FluidBar.sln
src/FluidBar.Core                net10.0 · 状态机/纯策略/弹簧/布局/设置模型/i18n 抽象（无 Win32、无 UI）
src/FluidBar.Sdk                 net10.0 · 公开插件契约（零 WPF、零 Win32、IPC 友好）
src/FluidBar.Platform.Windows    net10.0-windows10.0.19041 · Win32/WinRT：显示器、媒体、剪贴板、通知、热键、托盘、更新
src/FluidBar.PluginHost          net10.0 · ALC 加载器、manifest 校验、安装/回滚、贡献注册表
src/FluidBar.Marketplace         net10.0 · 市场领域：索引/清单解析、版本解析、安装与回滚事务（无 UI、无网络）
src/FluidBar.App                 net10.0-windows10.0.19041 · Avalonia 外壳、组合器宿主、渲染循环、面板、设置、市场
src/FluidBar.Cli                 net10.0 · fluidbar-plugin 校验/打包/签名（CI 与本地同一份代码）
src/FluidBar.Plugins.*           内置插件（编译进 App，不经 ALC）
plugins/                         社区插件 monorepo（每插件一目录，含 manifest/locales/tests）
marketplace/                     生成的 index.json + 分片 + listings（CI 产出，不手改）
tools/marketplace-gen/           索引/分片/listings 生成器（确定性输出；签名复用 FluidBar.Cli）
tests/FluidBar.Core.Tests        xunit：策略/弹簧/布局/状态机/设置迁移/i18n
tests/FluidBar.Sdk.Tests         版本范围、能力组合、清单/本地化字符串解析
tests/FluidBar.Architecture.Tests 依赖方向 + 禁用 API（Roslyn）
tests/FluidBar.Platform.Tests    宿主内集成：显示器/DPI/命中/COM 生命周期
tests/FluidBar.PluginHost.Tests  加载卸载 100×、zip-slip、签名、清单
tests/FluidBar.Ui.Tests          头less 渲染 + 快照（面板/pager/HUD/双语）
tests/FluidBar.Soak              1 h 浸泡：句柄/内存/帧预算/插件超时
schemas/                         plugin / index / locale / listing / settings JSON Schema
```
### 5.2 依赖规则（由 `FluidBar.Architecture.Tests` 强制）

| 工程 | 允许引用 | 禁止 |
|---|---|---|
| `FluidBar.Core` | BCL、`FluidBar.Sdk` | 任何 `Windows.*`/Win32/WPF/Avalonia/IO 具体实现（除 `System.IO` 抽象接口） |
| `FluidBar.Sdk` | BCL | Core、App、WPF、Win32、任何第三方包 |
| `FluidBar.Platform.Windows` | Core、Sdk、CsWin32、WinRT 投影 | App、Avalonia、任何 UI 类型 |
| `FluidBar.PluginHost` | Core、Sdk | App、Platform.Windows（通过接口注入） |
| `FluidBar.Marketplace` | Core、Sdk | App、Platform.Windows、任何网络/IO 具体实现（HTTP 由注入的 `IIndexSource` 提供） |
| `FluidBar.App` | Core、Sdk、Platform.Windows、PluginHost、Avalonia | 直接 P/Invoke（必须经 Platform.Windows） |
| `FluidBar.Plugins.*` | **仅** Sdk | Core、App、Platform、WPF/Avalonia |
| `tests/*` | 任意被测工程 | 生产代码反向引用测试 |

补充规则：
- **无循环**（`dotnet list reference` + 图算法断言）。
- App 中 P/Invoke 出现即失败（`[DllImport]`/`LibraryImport` 白名单只在 Platform.Windows）。
- 生产工程禁止 `#if DEBUG` 分支改变行为（仅允许断言/日志）。
- 任何新增 `PackageReference` 必须在 `Directory.Packages.props` 集中管理并记录许可（MIT/Apache-2.0/BSD 白名单）。
### 5.3 事件枢纽（Event Hub）

```csharp
public readonly record struct IslandEvent(
    ActivityKey Key,            // (Kind, Source)
    IslandEventKind Kind,       // New | Replacement | Incremental | Retract
    long Sequence,              // 单调；用于丢弃乱序
    TimeSpan Timestamp,
    ActivityPayload Payload);   // 不可变快照（record + ImmutableArray/冻结数组）

public interface IIslandEventHub {
    bool TryPublish(in IslandEvent e);          // 非阻塞；满则按策略丢弃并计数
    IAsyncEnumerable<IslandEvent> ReadAllAsync(CancellationToken ct);
    IslandState Snapshot { get; }               // 不可变；UI 只读这个
}
```

- **有界 + 合并**：`Channel<IslandEvent>` 容量 256、`FullMode=DropOldest`，但**先按 `(Key, Kind)` 合并**：队列里已有同键的 `Incremental` 时原地替换（进度/位置类事件天然合并），`New`/`Replacement` 不合并。
- **线程模型**：所有生产者（WinRT 回调、WMI、`SystemEvents`、插件线程、文件监视）只调用 `TryPublish`；唯一消费者在 UI 线程，`ReadAllAsync` 的每次 `MoveNext` 最多取 1 个事件并合并当帧剩余。
- **隔离**：每个订阅者 try/catch + 计数；抛异常的订阅者被记录并继续（永不传播到生产者线程）。`Publish` 在 Debug 下对非预期线程只告警，不抛。
- **背压可见**：`dropped`、`coalesced`、`queueDepth` 进入诊断计数器；1 h 浸泡要求 `dropped == 0`。
- 事件时间用 `Stopwatch`/`TimeProvider` 注入，测试可虚拟推进。
### 5.4 活动状态机与纯策略

```
IslandEvent ──► ActivityScheduler (priority/replace/stack/expiry)  ──┐
Settings    ──► PeekPolicy / CollapsePolicy / SuppressionPolicy      ├─► IslandState (immutable) ──► Layout ──► Render
TimeProvider──►                                                     ─┘
```

| 组件 | 职责 | 纯? |
|---|---|---|
| `ActivityScheduler` | 主活动选择、抢占、背景层、`+N`、过期 | ✅ `(events, now, settings) → SchedulerState` |
| `IslandStateMachine` | 五态迁移表（2.1）、效果列表 | ✅ `(state, input, now, settings) → (state, Effect[])` |
| `ContentSignaturePolicy` | 内容是否替换（2.7） | ✅ |
| `PeekPolicy` | 悬停意图/离开去抖 | ✅ 带 `now` |
| `CollapsePolicy` | 自动收起、pinned、抑制 | ✅ |
| `StackLayout` | ghost 偏移/缩放/角标 | ✅ 纯几何 |
| `IslandLayout` | 壳体/内容/pager 视口/命中区域的 DIP 矩形 | ✅ |
| `DpiPolicy` | DIP↔物理像素、显示器选择、钳制 | ✅ |
| `MotionPolicy` | token 选择、reduced-motion 折算、速度重定向 | ✅ |
| `SettingsMigrator` | schema 版本迁移 | ✅ |

**效果（Effect）是数据，不是委托**：`Effect` = `{SetWindowMode, SnapSprings, Retarget, StartFrameLoop, StopFrameLoop, ShowWindow, HideWindow, Relayout, PersistPlacement, PlayNudge, UpdateTray}`；由 `IslandHost` 在 UI 线程逐个执行。这样状态机可在无 UI 的测试里断言。
### 5.5 展示 / 布局 / 坐标契约
**唯一坐标系是 DIP（double）**；只有一个函数把视觉坐标转成物理像素，且只用于窗口放置（不在动画路径上）：

```csharp
readonly record struct VisualRect(double X, double Y, double W, double H);

// 布局：纯函数 → { Shell, Content(Shell − inset), PagerViewport(400), HitRegion(同 Shell 的 squircle), Anchor }
IslandLayout Layout(in IslandState s, in MonitorInfo m, IslandSettings cfg);

// 唯一的 DIP → 物理像素转换；只在窗口创建/锚点/显示器变化时调用（不在动画路径上）
PixelRect ToPhysical(in VisualRect r, in MonitorInfo m) => new(
    round(m.WorkArea.X + r.X * m.Scale), round(m.WorkArea.Y + r.Y * m.Scale),
    ceil(r.W * m.Scale),                 ceil(r.H * m.Scale));
```

- 宿主窗口**固定为** `488×332` DIP（按显示器缩放换算成像素），面板在其内部按锚点对齐；窗口原点变化 = 锚点/显示器变化，**不是**动画。
- 不允许任何代码直接读 `SystemParameters` 或写 `Window.Left/Top`。
- 单元测试覆盖：3 模式 × 6 锚点 × 3 DPI × 2 策略（stack/latest）的 `Layout` 快照，以及"bottom 锚点不越出工作区"的回归用例（v1 F-04）。
### 5.6 渲染宿主规则

| 规则 | 内容 |
|---|---|
| 窗口 | `WS_POPUP \| WS_EX_NOREDIRECTIONBITMAP \| WS_EX_TOOLWINDOW \| WS_EX_NOACTIVATE`，`HWND_TOPMOST` |
| 数量 | **每显示器一个**，永不"每岛一个"；ghost 药丸是同一窗口内的视觉层 |
| 尺寸 | 创建即 `488×332` DIP 对应像素，**运行期不 resize** |
| 命中 | `WM_NCHITTEST` 解析：点在 `Shell` 的 squircle 内且处于可交互态 → `HTCLIENT`，否则 `HTTRANSPARENT`；`WS_EX_TRANSPARENT` 仅在整窗无交互时整体置位 |
| 形状区域 | `SetWindowRgn` **只在收敛时**更新一次（用当前几何的包围盒 + 圆角），不作为逐帧手段；拖拽/动画期间依赖 alpha 与 `HTTRANSPARENT` |
| 置顶 | 低频（≤1 Hz）重新断言 `HWND_TOPMOST`，**先检查前台窗口类名**：`Shell_TrayWnd`、`Shell_SecondaryTrayWnd`、`Windows.UI.Core.CoreWindow`、`Xaml_WindowedPopupClass`、`MultitaskingViewFrame`、`TopLevelWindowForOverflowXamlIsland`、`Progman`、`WorkerW`、`#32768`、`tooltips_class32` 时**不**抢 |
| 焦点 | `WM_MOUSEACTIVATE` 返回 `MA_NOACTIVATE`；仅文本框/搜索需要输入时临时清除 `WS_EX_NOACTIVATE`，失焦立即恢复 |
| 文本输入 | 宿主不承载 IME 组合窗；lookup 使用独立的小窗口（`WS_EX_NOACTIVATE` 按需去除） |
| 生命周期 | `Create → Place → Show/Hide → Dispose` 显式；`Dispose` 幂等；`Closing` 一律取消并隐藏（托盘应用语义），`App.Shutdown` 唯一出口 |
| 渲染线程 | UI 线程只做状态推入与一次 `InvalidateVisual`；合成器线程承载动画（Avalonia Composition 动画/帧回调） |
### 5.7 渲染底座决策与 GO/NO-GO

> **修订 A2（2026-10-18，质量优先）**：渲染底座改为 **Win32 分层窗口（`UpdateLayeredWindow`，32bpp 预乘 DIB）+ SkiaSharp 岛渲染器**；Avalonia 仅保留设置/插件市场等普通窗口。原因：Avalonia 12 与 11.3.22 的 DirectComposition 窗口在目标机器上均被压平成灰色矩形（WPF 分层窗口与原生 `UpdateLayeredWindow` 同时透明）；Region 裁形被 DWM 忽略且每帧 6–8ms。宿主固定表面改为 float 488×384 / dock 488×444 DIP（面板外 detached mini-island 条）；输入仍走 `IslandWindowInterop` 的 HTCLIENT/HTTRANSPARENT、no-activate、topmost 白名单。原 Avalonia 方案作为设置窗口方案保留。
> **修订 A3（2026-10-18，手感终版）**：pager 最终参数以 PagerConstants 为准：拖动跟随 **0.85**、吸附弹簧 **T 0.38 s / ζ 0.94**、翻页阈值 **26 DIP**、flick 速度 **0.55 DIP/ms**、滚轮 **140 DIP / 180 ms**、滚动链 **40 DIP + 120 ms**、边缘橡皮筋 0.35。A1 中的 0.90/24/0.6 与 A2 期间的 0.80/0.44/0.97 均为中间调参，已被本修订取代。

**决策**：`Win32 宿主 + Windows.UI.Composition（WS_EX_NOREDIRECTIONBITMAP）` 为底座，**Avalonia 12 作为 UI 层**（其 `WinUIComposition` 后端就是该底座：`Windows.UI.Composition` + `DesktopWindowTarget`，Win10 17134+，逐像素 `Transparent`，1803+ 的 `AcrylicBlur`，圆角模糊刷，默认 PerMonitorV2）。当前 v1 的 `AllowsTransparency=true` 是"分层窗口"机制：每帧 GPU→CPU→GPU 回读 + 每帧 GPU/CPU 同步，结构性无法达到 120 fps / 真 acrylic / 逐帧 resize 下的 <150 MB。

**M0 spike（GO/NO-GO，2 天内出结论）**

| # | 检查 | 通过标准 |
|---|---|---|
| S1 | 透明 + 圆角：桌面花纹上画 440×284 squircle | 角部可见桌面像素、无黑边、`ActualTransparencyLevel == Transparent` |
| S2 | Acrylic + 圆角裁剪 | 模糊只在圆角内；静止 CPU < 1% |
| S3 | 形变 132×36 ↔ 440×284 @120 Hz | p99 帧 < 8.33 ms，60 s 无掉帧，收敛后帧回调归零 |
| S4 | 命中/穿透/不抢焦点/拖拽 | 透明角 20 次点击全部穿透；点药丸不改变前台窗口 |
| S5 | 4 插件浸泡 30 min | 工作集 < 120 MB 且趋势平坦 |
| S6 | `.NET 10` + Avalonia 12 构建 | 干净构建 + 单文件发布可启动 |

**M0 结果（2026-10-18，本机）：GO** — 证据见 [`research/05-avalonia-spike.md`](../research/05-avalonia-spike.md)：

| 检查 | 结果 | 实测（两轮独立 spike，均见 `research/05-avalonia-spike.md`） |
|---|---|---|
| S1 透明圆角 | ⚠️ 本会话证据无效 | 截图把宿主矩形记为灰色（Avalonia/WPF/官方 spike 均复现，疑似 ToDesk/GameViewer 虚拟显示器的屏幕采集伪影）；`ActualTransparencyLevel=Transparent` 与 RTB 内容正确；需真机肉眼或 Windows.Graphics.Capture 复测后再判 GO |
| S2 Acrylic | ⛔ 原生药丸 acrylic NO-GO | `AcrylicBlur`/`Mica` 只覆盖整个宿主矩形（非药丸）；DirectComposition 无 acrylic；LowLatency 黑框；Redirection ~77fps。改为 Tier 1 **模拟玻璃**（见 §3.4），Tier 2 整窗 acrylic 仅实验 |
| S3 120fps 形变 | ✅ GO（尾部偶发） | 两轮：p50 6.05ms、p99 8.11ms（489 paints/3.0s）；另一轮 360 预热帧 p50 6.05 / p99 7.76 / mean 165.9fps，4/360 帧 >8.33ms（max 18.59ms）。UI 线程 `RequestAnimationFrame` 非合成器驱动，但固定 HWND 不 resize、收敛后 0 渲染 |
| S4 命中/穿透/焦点/拖拽 | ✅ GO | WM_NCHITTEST：内→HTCLIENT、外/圆角外→HTTRANSPARENT；`WS_EX_NOACTIVATE|TOOLWINDOW`；拖拽精确 (−120,−80)px 且前台未变 |
| S5 内存/空闲 | ✅ GO（Debug 基线） | 62s 空闲：0 渲染、0.151% 单核、+28.7KB 分配、GC 平坦、WS 170.5MB（Debug）；Release + 4 插件 30min 浸泡留 M6 |
| S6 构建 | ✅ GO | .NET 10 + Avalonia 12.1.4：0 warning / 0 error，增量构建 ~1s |
| 置顶策略 | ✅ GO | 前台为 Chrome 时允许置顶；`Windows.UI.Core.CoreWindow`（开始菜单）在前台时抑制；开始菜单可覆盖岛 |
| 多显示器 | ⏳ 环境未覆盖 | 本机 Avalonia 仅暴露 1 屏（虚拟显示适配器不可见）；PMv2 @144DPI/1.5× 主屏放置精确。真机双屏复测为发布前硬门槛 |

**回退路径（触发即切换，设计不变）**
1. S1/S3 失败 → **WPF 固定尺寸分层窗口**：同一套 `FrameClock`/`SpringEngine`/`Layout`/`HitTester`，`FrameworkElement.OnRender` 自绘壳体；上限 60 fps，`DropShadowEffect` 全部替换为 9-slice 烘焙阴影；定位为"能发布但不达标"的兜底。
2. S5 失败或需要跨进程插件表面 → **自绘 D2D**（Win32 + Composition + D2D/DWrite，仅壳体与面板控件集，声明式插件 UI 保持）；预算 3–4 个月。
3. S2 失败 → 材质降级 Tier 0（模拟玻璃：2 段半透明渐变 + 1.5% 噪声 + 1 px 顶部高光），其余不受影响。
### 5.8 硬性规则（"不要再犯"清单，进 CI）
1. **永不**对窗口 `Left/Top/Width/Height` 做动画；窗口只在锚点/显示器变化时移动一次。
2. **永不**在 UI 线程做 I/O：无 `GetAwaiter().GetResult()`、无同步 HTTP、无 DDC/CI、无 WMI、无 `SendMessage`、无 `Process.GetProcesses*`、无目录枚举。
3. **每显示器一个窗口**；不存在快照 HWND；多岛是同一表面内的层。
4. **不轮询可通知的东西**：GSMTC 事件、音频会话回调、`NetworkChange`、WMI watcher、`WM_SETTINGCHANGE`、`WH_KEYBOARD_LL`、`FileSystemWatcher`；不可避免的轮询 ≥1 s、走后台、只在变化时发布。
5. **每个窗口模式都有显式命中测试**；折叠/隐藏 = `HTTRANSPARENT` + 无 `HTCLIENT` 区域，绝不吞点击。
6. **每个缓存都有预算**：图片（字节 LRU）、歌词（条数+TTL）、通知 ID（环形缓冲）、临时目录（7 天清扫）；无预算的 `Dictionary` 字段禁止合入。
7. **每个原生 `IDisposable` 都 `using`/`finally` 释放**（`Process`、COM RCW、`ManagementObject`、`HICON`、流、`CancellationTokenSource`）；分析器强制。
8. **每个异步都可取消且单飞**：以 track-key 派生 `CancellationTokenSource` + 代数号；禁止用 bool 闩锁做并发控制。
9. **一个设置服务**：schema 版本 + 迁移 + 原子写 + 无副作用 getter + 不吞异常。
10. **一个坐标契约**：视觉坐标无处不在，唯一 `Visual→Pixel` 转换，六锚点 × 两策略 × 三 DPI 全部有测试。
11. **无空 `catch`**：按 key 去重记录、可见降级、诊断计数器暴露。
12. **无隐藏全局副作用**：不按定时器改写其它应用配置；媒体命令只发给目标应用（不注入全局按键）；不执行未校验的下载物。
13. **每个窗口都有生命周期**；关闭即隐藏或按需重建；不存在指向已关闭窗口的单例引用。
14. **测试覆盖真实失败模式**：派发器驱动的窗口/位置/堆叠/竞态测试 + 1 h 浸泡 + 架构测试（禁止窗口 bounds 动画、禁止 UI 线程阻塞）。
15. **无死代码、无说谎文档、无未翻译硬编码字符串**。

## 6. 插件体系
### 6.1 混合模型（Hybrid）

| 车道 | 内容 | 加载方式 | v2.0 |
|---|---|---|---|
| **内置（编译期）** | media / clipboard / notifications / agent（+ 设置页） | 直接引用，注册进同一贡献注册表 | ✅ 默认 |
| **社区（签名 DLL）** | 第三方插件 | 每插件一个**可回收** `AssemblyLoadContext` + `AssemblyDependencyResolver` | ✅ |
| **隔离（进程外）** | 不可信/带原生依赖 | 具名管道 + `StreamJsonRpc`，受限令牌 + Job Object | ⏳ v2.1 |

不变量：
- **插件不得引用/加载 `FluidBar.Sdk.dll` 或 WPF 程序集**（类型标识分裂 → `InvalidCastException`）；CI 拒绝包含共享程序集的包。SDK 引用用 `PrivateAssets="all"` + `ExcludeAssets="runtime"`。
- **插件不提供 XAML/`UserControl`**：只提供数据 + 声明式贡献（布局槽位、设置页、命令、右键项、附件 widget），宿主渲染。
- **单文件发布**：插件根目录一律来自 `AppContext.BaseDirectory`，**禁止 `Assembly.Location`**。
- **卸载是尽力而为**：`Unload()` 只是开始；失败则标记 `restartRequired` 并提示，不强制重启。卸载成功率在 M2 用 100× 循环测出。
### 6.2 `FluidBar.Sdk` 契约（v2.0 冻结面）

```csharp
namespace FluidBar.Sdk;

public interface IFluidBarPlugin : IAsyncDisposable {
    ValueTask InitializeAsync(IPluginContext ctx, CancellationToken ct);   // 5 s 超时
    ValueTask StartAsync(CancellationToken ct);                            // 5 s 超时
    ValueTask StopAsync(CancellationToken ct);                             // 5 s 超时
}

public interface IPluginContext {
    string PluginId { get; }  Version ApiVersion { get; }
    IIslandPublisher Islands { get; }          // Publish(IslandEvent) + PublishLayout(LayoutDescriptor)
    IPluginLocalization Localization { get; }  // Get/Format/Plural；绝不暴露 CultureInfo 变更
    IPluginSettings Settings { get; }          // 宿主拥有的强类型访问
    IPluginStorage Storage { get; }            // 每插件目录（$PLUGIN_DATA）
    IPluginLog Log { get; }  IHostDispatcher Dispatcher { get; }   // 结构化日志；回 UI 线程的唯一通道
    IPluginLeases Leases { get; }              // 定时器/订阅/资源租约，卸载时统一释放
    ICommandRegistry Commands { get; }  INotificationSink Notifications { get; }
    IReadOnlySet<string> GrantedCapabilities { get; }
}

public readonly record struct LayoutDescriptor(
    string LayoutId, LayoutSurface Surface, string Template,      // 模板枚举，非任意树
    ImmutableDictionary<string, SlotBinding> Slots, LayoutConstraints Constraints);
```

规则：全部回调**异步 + 可取消 + 有超时**（tick 50 ms、取数 200 ms）；连续 3 次超时/异常 → 隔离该插件并记录；SDK 在一个大版本内**只增不改**（C# 默认接口方法）；`[Obsolete]` 保留 2 个次版本。
### 6.3 manifest v2（具体示例，非占位）

```json
{
  "$schema": "https://fluidbar.app/schemas/plugin.schema.json",
  "schemaVersion": 2, "id": "now-playing", "version": "2.1.0",
  "apiVersion": "2.0.0", "minAppVersion": "2.0.0",
  "publisher": { "id": "doulor", "displayName": "Doulor", "keyId": "doulor-2026" },
  "name": { "en-US": "Now Playing", "zh-Hans": "正在播放" },
  "summary": { "en-US": "Shows the media session on the island.", "zh-Hans": "在灵动岛上显示媒体会话。" },
  "license": "MIT", "categories": ["media"], "platforms": ["win-x64"],
  "entry": { "runtime": "inproc", "assembly": "plugin/FluidBar.Plugin.NowPlaying.dll",
             "type": "FluidBar.Plugin.NowPlaying.NowPlayingPlugin",
             "targetFramework": "net10.0-windows10.0.19041.0" },
  "locales": { "default": "en-US", "available": ["en-US", "zh-Hans"], "path": "locales", "fallback": "en-US" },
  "capabilities": {
    "media": { "sessions": true, "control": true, "reason": "读取并控制当前媒体会话" },
    "network": { "domains": ["lrclib.net"], "methods": ["GET"], "reason": "下载歌词文本" },
    "ui": { "island": true, "hoverCard": true, "settings": true, "commands": true, "xaml": false }
  },
  "activation": ["startup", "onMediaSession"],
  "contributes": {
    "layouts": [
      { "id": "now-playing.island", "surface": "island.compact", "template": "media.v1",
        "slots": { "icon": { "field": "albumArt", "kind": "image" },
                   "title": { "field": "title", "maxLines": 1, "style": "title" },
                   "subtitle": { "field": "artist", "maxLines": 1, "style": "caption" },
                   "progress": { "field": "progressPercent" } },
        "constraints": { "minWidth": 200, "maxWidth": 300, "minHeight": 40, "maxHeight": 44 } }
    ],
    "settingsPages": [
      { "id": "general", "title": { "en-US": "General", "zh-Hans": "常规" },
        "controls": [ { "type": "toggle", "key": "showLyrics",
                        "title": { "en-US": "Show lyrics", "zh-Hans": "显示歌词" }, "default": true } ] }
    ],
    "commands": [ { "id": "now-playing.toggle", "title": { "en-US": "Toggle playback", "zh-Hans": "播放/暂停" },
                    "defaultHotkey": "Ctrl+Alt+M", "contexts": ["global", "island"] } ]
  },
  "integrity": { "entrySha256": "3f7c…(64 hex)", "files": "checksums.json" },
  "signature": { "scheme": "detached-v1", "algorithm": "ECDSA-P256-SHA256", "publisherKeyId": "doulor-2026", "signatureFile": "signature.json" }
}
```

校验（`FluidBar.Cli`，CI 与本地同一实现）：JSON Schema 2020-12（`additionalProperties:false`）+ 跨字段语义（版本单调、`locales.default ∈ available`、槽位合法、命令引用存在、危险能力必须有 `reason`、`ui.xaml:true` 在市场提交中拒绝、`entry.runtime:"outofproc"` 在协议 v1 前拒绝）。
### 6.4 市场拓扑

```
fluidbar/FluidBar                应用 + SDK + 内置插件 + schemas + docs
fluidbar/plugins                 monorepo：plugins/<id>/{manifest.json, src, locales, tests}
  └ .github/workflows/{validate,publish}-plugin.yml
fluidbar/marketplace (Pages)     index.json + index 签名 + keys.json + plugins/<id>.json
                                 + listings/<id>/<locale>.json + advisories.json + .nojekyll
GitHub Releases (plugins repo)   fluidbar-plugin-<id>-<version>.fbp
                                 + signature.json（分离签名）+ SBOM
```

- **索引**：根 `index.json`（最新版 + 列表渲染字段，目标 < 1 MB，gzip ≈ 150 KB）；版本历史进 `plugins/<id>.json`（详情页/回滚按需取）；本地化长描述进 `listings/`。超过 2000 插件再按首字母分片。
- **签名**：根索引整体签名（RFC 8785 规范化负载，排除 `signatures` 字段），客户端**先验索引**再信其中的 hash/URL；`generatedAt` 比缓存旧 → 拒绝（防回滚）。包签名 = 对 `.fbp` 字节的 SHA-256 做 ECDSA P-256，**分离** `signature.json`（不能嵌在被它哈希的 ZIP 里）。
- **包格式**：`.fbp` = 确定性 ZIP（固定时间戳、排序条目、无额外字段）；内容固定为 `manifest.json`、`plugin/`（DLL + deps.json）、`locales/`、`assets/`、`LICENSE`、`README.md`、`checksums.json`；上限 50 MB 压缩 / 150 MB 解压 / 5000 条目；两次构建 SHA-256 必须一致。
- **信任**：市场密钥（HSM/KMS，OIDC 短期凭证）签包 + 发布者密钥（TOFU，首次安装指纹入 `state.json`，变更需显式同意）。`keys.json` 带 `revokedAt`。
- **状态机**：`Available → Downloading → Verifying(hash→signature→checksums) → Extracting(zip-safe) → Staged → Activating(ALC load/Init/Start) → Active`；失败态 `DownloadFailed / HashMismatch / SignatureInvalid / Incompatible / LoadFailed / CrashLoop → Disabled(reason + log path)`；**原子激活**：装到 `plugins/<id>/<version>/`，`Start` 成功后才写 `state.json` 的 `activeVersion`，否则保留旧版本。
- **更新/回滚**：启动 +5 s 检查、此后 24 h；通道 `stable`/`beta`；保留最近 2 个版本；24 h 内崩 3 次 → 自动回滚 `lastGoodVersion` 并通知；卸载可保留设置。
- **封禁/撤回**：`status: blocked`（客户端禁用已安装实例）、`yanked`（仅该版本，带理由）；发布资产永不删除（取证）。
### 6.5 安全立场（必须原样进入 UI 文案）
- **进程内插件 = 用户的全部权限**：可读任何用户可读文件、浏览器 Cookie、令牌，可 P/Invoke 任意 API。**能力清单是披露与知情同意，不是沙箱**。
- 信任边界是：**评审过的源码 + CI 构建 + 签名 + 哈希固定 + 发布者密钥固定**，而非运行时限制。
- 安装同意页原文（双语，逐条列出能力）：
  - zh-Hans：「此插件可以：访问互联网（lrclib.net）、读取当前媒体会话。**插件与 FluidBar 拥有相同的权限**。请只安装你信任的插件。」
  - en-US: "This plugin can: access the internet (lrclib.net), read the current media session. **Plugins run with the same access as FluidBar.** Only install plugins you trust."
- **禁止**说 "已审核 = 安全"、"沙箱"、"权限被限制"。
- 纵深防御（不替代人审）：zip 安全（拒绝绝对路径/`..`/符号链接/重复项/加密项）、`SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_DEFAULT_DIRS)`、安装不写 `Program Files`、不请求管理员、运行期审计日志（插件 id/能力/目标/时间）、崩溃计数与自动禁用、安全模式启动（禁用最近安装集合）。
- **v2.1 进程外车道**：`StreamJsonRpc` over 当前用户具名管道、受限令牌 + 低完整性 + Job Object 内存/CPU 上限、心跳看门狗、声明式 UI（无 HWND 嵌入）。仅在进程内市场有真实用量后启动。
### 6.6 贡献流程（当前现实：只有 Doulor 一名维护者）

```
贡献者 PR → CI（validate/build/test/pack/policy/secret scan）→ Doulor 评审
   → squash merge → publish workflow（构建 → 签名 → Release → 重新生成并签名索引 → 部署 Pages）
   → 发布后校验 → PR 回帖（Release URL / hash / keyId / 索引条目）
```

- **单人评审**：不设第二评审人；高风险能力（`network:"*"`、`shell.execute`、`files.write` 出 `$PLUGIN_DATA`、`process.spawn`、`registry.write`）由 Doulor 在 PR 里写明接受理由，并在 `advisories`/发布说明中记录。
- **SLA**：首次评审 5 个工作日；恶意举报 24 h 内 `blocked`；撤回 ≤ 72 h。
- **合并→可见目标 < 10 min**（构建 2–4 min + Pages 1–2 min + 缓存 5 min）。
- **未来治理**（v2.2 起，触发条件：外部贡献者 ≥ 3 且月活插件 ≥ 20）：CODEOWNERS 分区、双人评审高风险能力、评审轮值、公开的驳回/申诉记录。在此之前**不**引入投票或委员会。

## 7. i18n / l10n
### 7.1 格式决策：JSON 目录（而非 resx）

| 判据 | resx + `ResourceManager` | **JSON 目录** | 结论 |
|---|---|---|---|
| 运行时切换 | 需重新读 `ResourceManager` 并刷新所有绑定；`x:Static` 捕获死值 | 重新加载字典 + 一次 `PropertyChanged` 即可 | JSON 胜 |
| 复数/ICU | 扁平字符串，复数要拆多个 key | 原生嵌入 ICU 子集（`{count, plural, …}`） | JSON 胜 |
| 与插件/市场统一 | 插件用 JSON，格式分叉 | 同一格式、同一校验器 | JSON 胜 |
| Avalonia 工具链 | 无 WPF 的 `Strings.Designer.cs` 生成器；需自建 | 源生成器从 `en-US.json` 产出 `LocKeys` 常量 + 编译期检查 | 持平（需自建一次） |
| 翻译协作 | 成熟（Weblate/resx） | 成熟（Weblate/JSON），diff 更干净 | 持平 |
| 卫星程序集/单文件 | 需要 `SatelliteResourceLanguages` 与发布设置配合 | 无卫星程序集，`avares://` 内嵌即可 | JSON 胜 |

**决定**：应用用 **JSON 目录**（`src/FluidBar.App/Localization/<bcp47>.json`，以 `EmbeddedResource` 嵌入，`avares://FluidBar.App/Localization/zh-Hans.json` 读取）；插件与市场列表同为 JSON。通过单一 `ILocalizer` 抽象暴露，UI 代码不感知后端。**不允许在同一组件里混用两种格式**。

```json
{
  "$schema": "https://fluidbar.app/schemas/app-locale.schema.json",
  "locale": "zh-Hans",
  "version": 1,
  "messages": {
    "Island.Peek.Hint": "点击展开",
    "Page.Home": "主页",
    "Page.Media": "媒体",
    "Page.Notifications": "通知",
    "Page.Clipboard": "剪贴板",
    "Page.Agent": "代理",
    "Page.Widgets": "小组件",
    "Hud.Volume": "音量",
    "Hud.Muted": "静音",
    "Media.TrackCount": "{count, plural, other {# 首曲目}}",
    "Error.Network.Timeout": "网络超时，请稍后重试",
    "Market.Consent.Body": "此插件可以：{capabilities}。插件与 FluidBar 拥有相同的权限。"
  }
}
```

键规范：`Area.SubArea.Element`（≤4 段、稳定、不复用于不同语义、不动态拼接 key）；`<comment>` 等价物用 `_meta.comments` 供翻译上下文。
### 7.2 首批语言、回退链、运行时切换
- 首批：**`zh-Hans`（与 `en-US` 同步维护）+ `en-US`（源语言）**；后续 `ja/de/fr/es/pt-BR/ru/ko/zh-Hant` 走社区。
- 回退链：`请求 locale（精确）→ 父级（zh-Hans-CN → zh-Hans → zh）→ en-US → key 本身`；Debug 下显示 `⟦key⟧` 让缺口可见，Release 下落盘记一次并显示 `en-US`。
- 运行时切换：`LocalizationService.SetCulture()` 抬升 `Version`，绑定到 `Version` 的转换器重读；随后按清单重刷命令式字符串（窗口标题、托盘菜单、tooltip、`AutomationProperties.Name`、已捕获到 VM 的字符串）。
- 文化设置：启动时设置 `CultureInfo.DefaultThreadCurrentCulture/UICulture` 与 `CurrentCulture/UICulture`；Avalonia 没有 WPF 的 `FrameworkElement.LanguageProperty` 陷阱，Han 统一由**字体回退顺序 + 首选语言**决定，故字体栈（3.5）必须把 `Microsoft YaHei UI` 明确写入而不能依赖默认合成。
- 若个别命令式字符串漏刷 → 显示二级动作"重启 FluidBar 以完全生效"，但 v2.0 验收要求 `zh↔en` 全量即时切换。
### 7.3 复数、数字、日期
- 自研 **ICU 子集**（`{name}`、`{count, plural, =0/one/few/many/other}`、`{count, number}`、`{date, date, short|medium|long}`、`#`、`'{'` 转义），约 500 行 + CLDR 类别表；超出子集再换 `MessageFormat.NET`（同一 `IIcuFormatter` 接口）。
- 覆盖测试：`en`(1/2)、`ru`(1/2/5/21)、`ar`(0/1/2/3/11/100)、`zh`(任意) 四组，每组断言全部分支。
- 时长/文件大小/百分比一律走模板 + 复数，禁止 `TimeSpan.ToString()` 直接上屏；法语 `%` 前用不换行空格；歌曲名/艺术家是用户数据，**永不翻译**，嵌入 RTL 时用 `\u2068…\u2069` 隔离。
### 7.4 插件与市场本地化
- 插件 `locales/<bcp47>.json`（`en-US` 必须完整）；解析顺序：插件精确 → 插件父级 → 应用 UI culture → 插件 `en-US` → 应用 `en-US` → key。
- 清单里的 `name/summary/description` 用**本地化对象**（索引生成器可直接抽取），运行时 UI 字符串用 **key**；两者同时存在时运行时 key 优先、索引对象优先。
- 市场 `listings/<id>/<locale>.json` 携带 `name/summary/description/screenshots.alt/changelog`；索引给 `localeCoverage`（0–100），以弱提示展示，不阻断安装；机器翻译必须标 `machineTranslated: true` 并可见。
- 宿主只本地化**自己的 chrome**（"LIVE"、"静音"、相对时间、徽标）；插件产出的字符串宿主不翻译。事件协议优先传 `statusToken: "live"` 而非 `statusText: "LIVE"`。
- 搜索同时索引 `en-US` 与本地化名称/摘要（中文用户能搜中文名）。
### 7.5 RTL 与字体
- `FlowDirection` 逐窗口设置并在切换时重刷；布局用 `Grid/DockPanel` 而非绝对 `Canvas.Left`；方向性图标按 `MirrorInRtl` 镜像；URL/版本号/热键/路径保持 LTR 并加隔离。
- 字体栈见 3.5；CJK 走系统字体（不打包 10–25 MB 字体）；`Language/xml:lang` 必须设置，否则 `zh-Hant` 可能出简体字形。
- 测试 locale：`ar-SA`、`he-IL`、`fa-IR`（形状/徽标位置/进度方向）+ `de-DE`（+35% 文本膨胀）+ `ja`（字形高度）。
### 7.6 伪本地化与测试
- `qps-ploc` 由 `en-US` 构建期生成：`⟦ … ⟧` 包裹、元音加符、词内重复扩 35%、占位符原样保留。
- 触发方式：`FLUIDBAR_PSEUDO=1` 环境变量或隐藏设置；**发布版不得默认开启**。
- CI 断言：key 齐全（每个 `en-US` key 在各语言存在或显式标记未翻译）；占位符集合与顺序一致；ICU 可解析；伪本地化后无裁切（在 100/125/150% 三档测量每个可见字符串是否溢出容器或允许省略）；`qps-ploc` 截图与 `zh-Hans`/`en-US` 一起入快照回归。
### 7.7 贡献者与工具（当前现实）
- **当前**：`zh-Hans` 与 `en-US` 均由 Doulor 维护；新增语言必须先有 ≥1 名语言维护者，否则不合并（避免半成品语言）。
- **后期**：Weblate（原生支持 JSON、GitHub PR、术语表、逐语言评审）；组件 `fluidbar-app` / `fluidbar-marketplace` / `plugin-<id>`；术语表：灵动岛/island/、悬停卡片/hover card/、插件/plugin/、插件市场/marketplace/、命令/command/、小组件/widget；不翻译：`FluidBar`、API 名、文件扩展名。
- **字符串冻结**：发布前 7 天冻结 `en-US`；安全修复不被翻译阻塞（英文回退 + 说明）。

## 8. 设置与持久化
### 8.1 存储

| 项 | 路径 | 说明 |
|---|---|---|
| 主设置 | `%APPDATA%\FluidBar\settings.json` | 单一真源；schema 版本化 |
| 插件设置 | `%APPDATA%\FluidBar\plugins\<id>\settings.json` | 宿主拥有；`secret` 类型走 DPAPI(CurrentUser) |
| 插件代码 | `%LOCALAPPDATA%\FluidBar\plugins\<id>\<version>\` | 绝不写 `Program Files`，不需管理员 |
| 缓存 | `%LOCALAPPDATA%\FluidBar\cache\{art,icons,lyrics}\` | 7 天清扫 + 字节上限 |
| 日志 | `%LOCALAPPDATA%\FluidBar\logs\` | 5 MB × 3 轮转，JSON Lines |
| 状态 | `%LOCALAPPDATA%\FluidBar\state.json` | 上次活动、显示器摆放、`lastGoodVersion`、崩溃标记 |

**写入协议**：序列化（`System.Text.Json`，源生成、`WriteIndented=false`）→ 写 `settings.json.tmp` → `Flush(true)` → `File.Replace(tmp, target, backup)`（首次写用 `File.Move(tmp, target, overwrite:true)`）→ 保留 `settings.json.bak`。保存去抖 500 ms，退出前强制 flush；退出时未完成即写 `state.pending`，下次启动合并。
### 8.2 schema 与迁移

```json
{
  "schemaVersion": 2,
  "general": { "language": "system", "startWithWindows": true, "trayIcon": true, "autoUpdate": "notify", "telemetry": false, "safeMode": false },
  "appearance": { "theme": "dark", "material": "obsidian", "palette": "obsidian",
                  "accent": { "mode": "artwork", "fallback": "#4CC9F0" },
                  "fillOpacity": 0.96, "fontScale": 1.0, "reducedMotion": "system", "motionStrength": 1.0 },
  "island": { "mode": "float", "anchor": "top-center", "stacking": "stack", "hoverPeek": true,
              "autoCollapseMs": 2600, "pinned": false, "autoHideMs": 0, "peekOnTrackChange": false, "displayOn": "primary" },
  "dock": { "hideSystemTaskbar": false, "height": 52, "tiles": ["media", "notifications", "clipboard", "agent", "widgets"] },
  "placement": { "monitors": { "\\\\.\\DISPLAY1": { "anchor": "top-center", "offsetX": 0, "offsetY": 12 } } },
  "hotkeys": { "toggle": "Ctrl+Alt+D", "hide": "Ctrl+Alt+H", "settings": "Ctrl+Alt+S", "holdToHide": "LeftAlt" },
  "activities": { "volume": { "enabled": true, "durationMs": 1600 }, "brightness": { "enabled": true, "durationMs": 1600 },
                  "notification": { "enabled": true, "durationMs": 5000, "detectVerificationCode": true },
                  "media": { "enabled": true, "showLyrics": true, "blocklist": ["chrome", "youtube"] } },
  "plugins": { "enabled": ["builtin.media", "builtin.clipboard"], "channel": "stable", "checkIntervalHours": 24 },
  "diagnostics": { "logLevel": "warn", "crashGuard": true, "frameStats": false }
}
```

**迁移**：`SettingsMigrator.Run(jsonIn) → (SettingsV2, MigrationReport)`。v1 → v2 映射表：

| v1 字段 | v2 目标 | 备注 |
|---|---|---|
| `Position`(TopCenter/Bottom…) | `island.anchor` + `placement.monitors` | 旧值语义按"主显示器"还原 |
| `Opacity`(96) | `appearance.fillOpacity` 0.96 | 96 → 0.96 |
| `SizeScale` / `FontScale` | `appearance.fontScale` | 合并为单值；范围 clamp 0.8–1.6 |
| `CornerRadius` | 丢弃（v2 半径由模式与状态决定） | 在迁移报告里说明 |
| `HoldToHideKey` | `hotkeys.holdToHide` | `Disabled` 映射为 null |
| `PluginEnabled` | `plugins.enabled` | 未知 id 保留并标 `orphan` |
| `MonitorFeatureSettings.*.Enabled` | `activities.<id>.enabled` | v1 该字段**未持久化**（F-19），迁移时按"全部启用" |
| `Theme`/`Accent` | `appearance.theme`/`accent` | 系统强调色 → `mode:"system"` |
| `HooksGuard*`（重复字段） | 丢弃 | 保留 `AgentHooksGuard*` 语义 |
| 未知字段 | 丢弃并写入迁移报告 | 不静默 |

迁移是**只读一次**：成功后把原文件改名为 `settings.v1.backup.json`；失败则保持原文件不动并进入安全模式（用默认值启动 + 托盘提示 + 诊断包导出）。
### 8.3 导出 / 导入 / 重置
- **导出**：单个 JSON（`{"schemaVersion":2,"exportedAt":…,"settings":{…},"plugins":{…}}`），**排除密钥与令牌**；文件名 `fluidbar-settings-<yyyyMMdd-HHmm>.json`。
- **导入**：先 schema 校验 → 迁移 → 展示差异摘要（新增/修改/删除项计数）→ 用户确认 → 原子写 + 备份；冲突策略为"整包替换"，不做逐项合并。
- **重置**：两步确认（第二次需输入 `RESET`）；范围三选：仅外观 / 全部设置 / 设置+插件+缓存；重置前自动导出备份到 `%APPDATA%\FluidBar\backups\`。
### 8.4 启动 / 更新 / 托盘 / 热键

| 项 | 规格 |
|---|---|
| 开机启动 | `HKCU\…\Run` 写 `--background`；不用计划任务、不要管理员；设置里可关；被外部删除时自愈一次并记录 |
| 单实例 | 具名 `Mutex` + 已有实例 `WM_COPYDATA` 唤起设置；第二实例静默退出 |
| 更新 | 启动 +5 s 检查，之后 24 h；`stable`/`beta`；下载前展示版本与体积；**校验 SHA-256 + Authenticode 后再执行**；失败保留旧版并提示 |
| 托盘 | 左键 → 展开/收起；右键 → 菜单（设置/插件市场/模式/材质/暂停/退出）；双击 → 设置；菜单项全部本地化且及时刷新 |
| 热键 | `RegisterHotKey`；冲突 → 设置里红字 + 建议备选；不静默失败；`holdToHide` 用 `WH_KEYBOARD_LL`（仅岛可见时挂载） |
| 安全模式 | `--safe-mode` 或连续崩溃 2 次自动进入：禁用所有社区插件、Tier 0 材质、默认设置 + 备份恢复入口 |

## 9. 性能与质量预算
### 9.1 预算表（CI/浸泡强制）

| 指标 | 目标 | 上限 | 测法 |
|---|---|---|---|
| 空闲 CPU（10 min 均值） | < 0.3% 单核 | < 1.0% | `dotnet-counters` + 进程采样 |
| 空闲帧回调 | 0 | 0 | 计数器（`frameCallbacks`） |
| 动画期 CPU | < 3% | < 6% | 100 次展开/收起均值 |
| 帧时间 p99 @120 Hz | < 4 ms 托管 | < 8.33 ms 总 | PresentMon + 自建 `FrameStats` |
| 掉帧率（60 s 形变） | < 0.5% | < 2% | PresentMon |
| 首帧输入延迟 | ≤ 2 帧 | ≤ 4 帧 | 自动注入指针事件 |
| 工作集（4 插件，30 min） | < 120 MB | < 150 MB | `Process.WorkingSet64` |
| 托管堆稳态 | < 60 MB | < 90 MB | `GC.GetTotalMemory(true)` |
| 堆增长（1 h） | < 2 MB/h | < 8 MB/h | 线性回归斜率 |
| 句柄（稳态） | < 600 | < 900；1 h 增长 < 60 | `HandleCount` 采样 |
| Gen2 GC（形变期间） | 0 | ≤ 1/min | `dotnet-counters` |
| 每帧分配 | < 32 KB | < 64 KB | `GC.GetAllocatedBytesForCurrentThread` |
| 冷启动到首像素 | < 900 ms | < 1500 ms | 事件日志时间戳 |
| 热启动到首像素 | < 400 ms | < 700 ms | 同上 |
| 设置窗口首开 | < 250 ms | < 500 ms | 自建埋点 |
### 9.2 测试策略

| 层 | 内容 | 工具 |
|---|---|---|
| 单元（`FluidBar.Core.Tests`） | 状态机全迁移（含非法迁移）、优先级/抢占/堆叠/过期、内容签名、Peek/收起策略、布局六锚点×三 DPI×三模式、弹簧收敛时间（±20%）、速度重定向、设置迁移（v1 样本 → v2 断言）、ICU 复数 | xunit |
| 架构（`FluidBar.Architecture.Tests`） | 依赖方向；`[DllImport]` 只在 Platform；**禁止** `Window.Left/Top/Width/Height` 赋值/动画；禁止 `.Result`/`.Wait()`/`GetAwaiter().GetResult()`/`Thread.Sleep`；禁止 UI 工程内 `File.`/`Directory.`/`HttpClient`；禁止空 `catch {}` | Roslyn 语法/语义分析 + 引用图断言 |
| 平台集成（`FluidBar.Platform.Tests`） | 显示器枚举与工作区、DPI 变更重排、`WM_NCHITTEST` 矩阵（每模式 200 采样点）、COM/WinRT 释放（弱引用 + GC）、托盘/热键注册与冲突、GSMTC 会话切换 | 宿主进程内 + 可注入的 Win32 包装 |
| 插件（`FluidBar.PluginHost.Tests`） | 加载/卸载 100×（`WeakReference` 回收）、zip-slip、签名/哈希失败路径、清单非法、超时隔离、崩溃计数、回滚 | xunit + 临时目录 |
| i18n | key 齐全、占位符一致、ICU 解析、伪本地化无裁切、`zh↔en` 运行时切换、RTL 冒烟、`qps-ploc` 快照 | xunit + 头less 渲染快照 |
| Soak（`FluidBar.Soak`） | **1 h**：每 3 s 一次随机活动 + 每 30 s 一次展开/收起 + 媒体进度 1 Hz + 插件 tick；断言句柄/内存/帧预算上限 | 独立进程 + 断言脚本 |
| 手工 QA | 见 9.4 | 清单 |

**架构测试示例规则（必须实现）**

```csharp
// 失败示例：任何对 Window.Left/Top/Width/Height 的赋值或动画
Forbidden.Assignments(typeof(Avalonia.Controls.Window), "Left", "Top", "Width", "Height");
Forbidden.Members("System.Windows.Media.Animation.DoubleAnimation");      // 防止回退路径被误用
Forbidden.Calls("System.Threading.Tasks.Task.Wait", "…get_Result");
Forbidden.Types("System.Windows.Forms.Screen", "System.Windows.SystemParameters");
```
### 9.3 可观测性
- **结构化日志**：JSON Lines（`ts, level, cat, evtId, pluginId, msg, data{}`），`cat` 固定枚举（`lifecycle/activity/motion/render/plugin/market/settings/platform`）；每类每秒限流 20 条；`Error` 以上永不丢。
- **诊断计数器**（设置 → 高级 → 诊断，或 `--diag` 导出 JSON）：`events.published/coalesced/dropped`、`activity.switches/replacements`、`frame.p50/p99/dropped`、`spring.wakeups`、`cache.{art,lyrics}.{bytes,hits,misses}`、`plugin.{loads,timeouts,quarantined}`、`ui.blockedMs.max`、`handles`、`workingSet`。
- **崩溃护栏**：`AppDomain.UnhandledException` / `TaskScheduler.UnobservedTaskException` / Avalonia dispatcher 异常 → 记录 + 托盘提示 + 重置到安全工作状态（不静默退出）；连续 2 次启动崩溃 → 安全模式；崩溃前写 `crash.marker`（含活动插件集），下次启动读取并隔离。
- **导出诊断包**：日志 + 计数器 + 设置（脱敏）+ 显示器/DPI/GPU 摘要 + 活动插件列表，压缩为单文件；不含用户媒体内容与剪贴板数据。
### 9.4 手工 QA 清单（每个 RC 必跑）
- [ ] 三模式 × 六锚点 × {100%, 125%, 150%, 200%} 摆放正确、不越界、不被任务栏遮挡
- [ ] 折叠/隐藏态：在药丸周围 20 px 内连点 20 次，全部穿透到下方应用；药丸本体点击生效且不改变前台窗口
- [ ] 展开态：点击面板外收起；点击面板内不收起；Esc 逐级；`pinned` 抑制自动收起
- [ ] 拖拽：橡皮筋、吸附、Esc 取消、跨显示器交接、重启后位置保持
- [ ] 全部 14 类活动的紧凑尺寸与 HUD 单行布局（含静音、0%、100%、超长标签）
- [ ] 双语即时切换（岛 + pager + 设置 + 市场 + 托盘 + 错误）；`qps-ploc` 下无裁切
- [ ] 媒体：播放/暂停/上一首/下一首/拖动进度/换源/无会话降级/浏览器源
- [ ] 通知：验证码识别与复制；免打扰时不弹
- [ ] 剪贴板：文本/图片/超长文本；复制回写；权限被占时不崩
- [ ] 插件：安装/更新/回滚/卸载/禁用/崩溃隔离/安全模式
- [ ] 全屏（DX11、无边框、PowerPoint 放映、DWM MPO 视频）与锁屏/切换用户
- [ ] 睡眠唤醒、显示器热插拔、DPI 切换、远程桌面（软件渲染回退）
- [ ] 高对比主题、键盘全流程、屏幕阅读器朗读关键控件

## 10. 迁移与功能对齐
### 10.1 功能对齐清单（来自审计 §7）

| # | 能力 | v1 状态 | v2 目标 | 验收 |
|---|---|---|---|---|
| 1 | 音量（进度/静音/波形） | 部分（150 ms COM 轮询） | **保留+重写**：`IAudioEndpointVolumeCallback` 事件驱动；单行 HUD | 改音量到 HUD 出现 < 150 ms；空闲 0 轮询 |
| 2 | 亮度（DDC/CI + WMI） | 性能缺陷（UI 线程 5 Hz） | 保留：专用 MTA 线程，2 s 轮询，仅变化发布；DDC/CI 优先，WMI 回退 | UI 线程零阻塞；无显示器支持时安静降级 |
| 3 | 电池（%/充电/低电） | 可用 | 保留：`RegisterPowerSettingNotification` 代替 15 s 轮询 | 插拔电源 1 s 内更新 |
| 4 | 时钟 | 可用 | 保留：分钟边界定时器（非 10 s 轮询） | 跨分钟刷新；隐藏时不唤醒 |
| 5 | 输入法（中/英） | 部分（6.7 Hz `SendMessage`） | **降级保留**：前台变化 + 1 s 去抖，`SendMessageTimeout(50ms)` | 无 UI 冻结；挂起应用不拖死岛 |
| 6 | 锁定键（Caps/Num） | 可用 | 保留：`WH_KEYBOARD_LL` 状态跟踪（无轮询） | 0 定时器 |
| 7 | 网络（连接/SSID） | 可用 | 保留：`NetworkChange` + WinRT 网络状态；SSID 用 `WlanQueryInterface` 真值 | 重连 1 s 内更新；SSID 正确 |
| 8 | USB | 部分（WMI 泄漏） | 保留：WMI watcher（`WITHIN 2`）+ `using` 释放 | 句柄不增长 |
| 9 | 蓝牙 | 部分（15 s WMI） | 保留：`DeviceWatcher` + `BluetoothLEDevice` 电量 | 连接/断开 2 s 内；无 WMI 轮询 |
| 10 | 通知 | 部分（1.6 s 全量历史 + 无界 set） | **重写**：`UserNotificationListener.NotificationChanged` + 水位线 + 环形缓冲 | 0 轮询；内存不增长 |
| 11 | 剪贴板插件 | 可用（HWND 依附 hack） | 重写为宿主服务 + `AddClipboardFormatListener`（在宿主窗口上） | 无 `AttachWindow` API 外泄 |
| 12 | 媒体：GSMTC 会话 | 可用（2 Hz 轮询） | **事件驱动**（`MediaPropertiesChanged`/`PlaybackInfoChanged`/`TimelinePropertiesChanged`） | 换曲 < 300 ms；0 轮询 |
| 13 | 媒体：歌词（Kugou/NetEase） | 部分（同步 HTTP、无界缓存） | 保留 provider 语义，改为后台 async + 有界缓存（32 曲/4 MB）+ 单飞取消 | 冷曲不阻塞 UI；缓存有上限 |
| 14 | 媒体：封面/应用图标 | 部分（全尺寸解码两次） | 重写：`DecodePixelWidth=64` 单次解码 + 字节 LRU（32 MB）+ 临时目录 7 天清扫 | 每曲 ≤1 次解码；磁盘不增长 |
| 15 | 媒体：传输控制 | 可用（含全局 `keybd_event`） | 保留 GSMTC 主路径；应用命令用 `PostMessage`，**删除全局按键注入** | 不影响其它应用 |
| 16 | 媒体：浏览器站点徽标 | 可用（每轮 EnumWindows） | 保留，缓存按窗口标题，≤0.2 Hz | 0.2 Hz 上限 |
| 17 | 代理状态（Agent hooks） | 部分（900 ms 扫描 + 改写用户配置） | 保留：`FileSystemWatcher` + 保留策略；hook 安装改为**显式用户操作**（备份+合并+锁），删除定时自愈 | 不静默改写外部配置 |
| 18 | 托盘/菜单/双击 | 可用 | 保留 + 全本地化 + 菜单即时刷新 | 切换语言后菜单为新语言 |
| 19 | 热键 / hold-to-hide | 可用（83 次/s 轮询） | 重写：`RegisterHotKey` + 可见时 `WH_KEYBOARD_LL` | 0 空闲轮询 |
| 20 | 多岛堆叠 | 损坏（快照 HWND 重建） | 重写：同一表面内的 ghost 层（2.5） | 事件风暴下 0 HWND 创建 |
| 21 | 悬停卡 | 部分（逐帧布局/丢焦点） | 由 `Expanded` + 内容替换策略替代（2.7） | 停留 10 s 期间布局次数 ≤ 1 |
| 22 | 更新检查 | 部分（双实现/无签名） | 重写：单一 `IUpdateService`、ETag、SHA-256 + Authenticode、显式安装 | 未校验绝不执行 |
| 23 | 设置（5 页） | 可用（2.7k 行 XAML） | 重写：分页 VM + 声明式生成（设置模型注解 → 页面） | 首开 < 250 ms；无 21 个模糊特效 |
| 24 | 主题感知 | 破损（跨线程崩溃） | 重写：`WM_SETTINGCHANGE` + `SystemParameters` 通知，主线程 | 切换主题不崩、岛跟随 |
### 10.2 明确"不移植"

| 项 | 原因 | 替代 |
|---|---|---|
| 逐帧窗口 bounds 动画 | 结构性性能缺陷（F-02） | 固定窗口 + 内部壳体几何 |
| 快照 HWND（`IslandSnapshotWindow`） | 每事件创建销毁（F-03） | 同表面 ghost 层 |
| 全局 `keybd_event` 媒体键 | 打到错误应用（F-22） | 仅 GSMTC / 定向 `PostMessage` |
| Kugou UI Automation 回退 | UI 线程 UIA 树遍历（F-10） | 窗口标题 + GSMTC |
| 50 ms 波形布局定时器 / 16 ms 滚动定时器 | 逐帧布局（§3.2） | 渲染循环内 transform，仅可见时运行 |
| `RimMode=Always` 常驻动画 | 不可见仍 60 fps（F-16） | 仅事件脉冲 |
| `DropShadowEffect` / `BlurEffect` | 每帧多趟模糊 | 9-slice 烘焙阴影 / 合成器 DropShadow |
| 通知历史全量轮询 + 无界 `_seenIds` | F-13 | `NotificationChanged` + 水位线 |
| 定时自愈改写 `~/.claude/settings.json` | F-23 | 显式安装 + 备份 + 合并 + 锁 |
| 天气 / 日历 / CPU-GPU / 文件托盘 / 频谱 | v1 从未实现，非对齐范围 | v2.0 后以插件形式提供 |
### 10.3 数据迁移
1. 首次启动 v2：检测 `%APPDATA%\FluidBar\settings.json` + 各插件 `*.json`。
2. 备份到 `%APPDATA%\FluidBar\backups\v1-<ts>\`（原样文件）。
3. 按 8.2 映射 → 写 v2 设置 → 生成 `migration-report.json`（映射/丢弃/失败项）。
4. 旧插件设置按 id 归入 `plugins/<id>/settings.json`；无法识别的 id 保留在 `plugins.enabled` 并标 `orphan`（设置页显示"未安装的插件"）。
5. `%TEMP%\FluidBar\{art,icons}` 与 `agent-events\processed` **不迁移**，启动时清扫；`agent-events` 仅保留最近 100 条。
6. 迁移后 v1 目录不改动（用户可回滚到 1.x）。
7. 迁移失败 → 安全模式 + 备份恢复入口 + 诊断包导出（绝不静默重置）。

## 11. 风险与未决问题
### 11.1 风险

| # | 风险 | 概率/影响 | 缓解 | Owner |
|---|---|---|---|---|
| R1 | Avalonia `WinUIComposition` 在部分 GPU/驱动/RDP 下透明或 acrylic 异常 | 中/高 | M0 spike 三机（NVIDIA/Intel/AMD）+ RDP；特性开关逐级降级 `DirectComposition` → Tier 0 | UI |
| R2 | 手写弹簧与 Avalonia 渲染时钟对不齐（120 Hz 抖动） | 中/中 | `FrameClock` 抽象 + 定步长 + PresentMon 验收；不达标即切 F1 | UI |
| R3 | 内存超 150 MB（Avalonia + 4 插件） | 中/高 | 声明式插件 UI（近零内存）、`ServerGC=false`、图片/歌词有界缓存；S5 提前测 | UI/Plugin |
| R4 | 插件卸载成功率低（WPF/Avalonia 资源/静态根） | 高/中 | 禁用插件 XAML、租约释放、100× 泄漏测试、`restartRequired` 诚实提示 | Plugin |
| R5 | 市场在中国大陆可达性（Pages/raw/Releases 限速或不可达） | 中/高 | 客户端内置 bootstrap 索引、raw 回退、可配置镜像源、安装包直链不依赖 API；**未决**见 11.2 | Lead |
| R6 | 签名密钥管理（丢失=无法发版，泄露=供应链攻击） | 低/严重 | HSM/KMS + OIDC 短期凭证；离线索引密钥；每年轮换 + 撤销演练；恢复流程书面化 | Lead |
| R7 | 单文件发布 + 插件共存（`Assembly.Location` 为空、ALC 解析） | 中/高 | 一律 `AppContext.BaseDirectory`；M2 用 `PublishSingleFile` 真实构建做集成测试 | Platform |
| R8 | Avalonia 12 在本项目所需的 API（帧回调、transparency 探测）不齐 | 中/中 | M0 用 2 天验证并锁定版本；`FrameClock` 退化实现已定义 | UI |
| R9 | 反病毒/EDR 将 always-on-top、无激活、置顶窗口判为覆盖层 | 低/中 | 签名二进制、不使用 `SetWindowsHookEx`（仅 `WH_KEYBOARD_LL` 且可见时才挂）、文档化行为、Defender ASR 测试 | Platform |
| R10 | 插件 API 变更破坏生态 | 中/高 | 语义化 `apiVersion`、大版本内只增不改、样例插件回归、双契约过渡一个周期 | Plugin |
| R11 | i18n 抽取遗漏（261 处中文字面量 + XAML） | 高/中 | 抽取脚本 + 架构测试（禁止中文字面量）+ 伪本地化 CI | Core |
| R12 | 商标/商业外观（"Dynamic Island" 是 Apple 术语） | 低/中 | 文档中仅作指示性引用；产品名/图标/安装器不复刻岛形或 SF Symbols；**未决**见 11.2 | Lead |
### 11.2 未决问题（必须带 owner 与截止日）

| # | 问题 | 影响 | Owner | 截止 | 决策方式 |
|---|---|---|---|---|---|
| Q1 | 渲染底座 GO/NO-GO（Avalonia 12 `WinUIComposition`） | 决定 M4 全部实现 | Doulor | **2026-10-24** | S1–S6 spike 报告 + 一次演示 |
| Q2 | Avalonia 具体版本与帧回调 API（是否需自建 `FrameClock`） | R8 | UI 负责人 | 2026-10-24 | spike 产物 |
| Q3 | `flush` 模式是否保留到 v2.0（还是只留 float/dock） | 视觉与测试面 | Doulor | 2026-10-24 | 看原型 + 用户反馈 |
| Q4 | Acrylic Tier 2（合成器 host backdrop）是否 v2.0 上线 | 视觉上限 | UI 负责人 | 2026-11-14 | S2 实测 + 功耗对比 |
| Q5 | Dock 是否在 v2.1 真替换系统任务栏（AppBar） | 产品定位 | Doulor | 2026-11-14 | 与 Q3 一起定 |
| Q6 | 插件更新是否默认要求重启（`restartRequired`） | 安装体验 | Plugin 负责人 | 2026-11-21 | 100× 卸载成功率数据 |
| Q7 | 签名密钥托管形态（HSM/KMS vs 离线 U 盘仪式） | 发版流程 | Doulor | 2026-11-07 | 成本/合规评估 |
| Q8 | 市场在 CN 的镜像方案（自建 R2/Cloudflare vs 纯 raw 回退） | R5 | Doulor | 2026-11-28 | 三地实测延迟 |
| Q9 | 产品名与图标是否需法务复核（"Dynamic Island" 商标） | 上架/宣传 | Doulor | 2026-12-05 | 外部意见 |
| Q10 | v1 用户是否提供"就地升级"安装包（而非并列安装） | 迁移体验 | Doulor | 2026-12-12 | 安装器方案评审 |

## 12. 里程碑 M0–M6

| 里程碑 | 周期 | 范围 | 验收（全部满足才算完成） |
|---|---|---|---|
| **M0** 立项与地基 | 10-18 → 10-24（1 周） | 分支 `v2/rebuild`、解决方案骨架、文档入库、CI 基线、渲染 spike | `dotnet build`/`dotnet test` 绿；CI 三作业（build/test/arch）通过；S1–S6 有书面结论（GO 或回退决定）；D1–D12 与 Q1–Q10 逐条确认 |
| **M1** Core | 10-25 → 11-07（2 周） | 事件枢纽、活动状态机、纯策略、布局、弹簧、设置模型与迁移、i18n 抽象 | Core 测试 ≥ 180 个全部通过；布局快照（3×6×3×2）锁定；弹簧收敛时间误差 < 20%；v1 样本迁移 0 失败 |
| **M2** SDK + 插件宿主 | 10-25 → 11-07（并行，2 周） | `FluidBar.Sdk` 冻结、manifest v2 + Schema、ALC 加载器、安装/更新/回滚、样例插件、CLI 工具 | 加载/卸载 100× 无泄漏（或给出 `restartRequired` 数据）；zip-slip/签名/清单负例全部拦截；两个样例插件可安装并出岛 |
| **M3** Platform.Windows | 11-08 → 11-21（2 周） | 显示器/DPI、媒体栈、剪贴板、通知、代理 hook、托盘/热键/更新；全部后台化 + 合并快照 | 空闲 0 定时器（除 ≥1 s 且变化才发布者）；UI 线程 I/O 静态扫描 0 命中；24 h 句柄增长 < 60 |
| **M4** App 外壳 | 11-08 → 11-28（3 周） | 组合器宿主、岛控件 + 弹簧循环、pager 六页、HUD、dock、设置窗口、i18n 全量、市场 UI | 120 fps p99 < 8.33 ms（或回退路径 60 fps 达标）；点击穿透矩阵通过；`zh↔en` 全量即时切换；`qps-ploc` 无裁切 |
| **M5** 插件迁移 + 市场 | 11-29 → 12-12（2 周） | 4 个内置插件迁移、签名索引、发布流水线、回滚/封禁、贡献者文档 | 首个第三方插件走完 PR→CI→签名→上线 < 10 min；公网安装到 `Active` < 60 s；回滚演练通过 |
| **M6** 加固与发布 | 12-13 → 01-09（4 周） | 性能/浸泡/QA/崩溃护栏/诊断包/安装器/发布说明 | 1 h 浸泡达标（9.1 全部上限）；手工 QA 清单全绿；RC2 无 P0/P1；发布包签名与校验通过 |

**M6 之后的 v2.1/v2.2 预告**（不在本规范承诺范围）：进程外插件车道、评分/合集、`ja/de/fr` 语言包、镜像与增量更新、命令面板。

## 附录 A — 本规范与参考实现的关系（许可纪律）
- **可复制（MIT，需保留声明）**：devcode90/Dynamic-Island-for-Windows 的设计常量与 Windows 行为结论、ng1145/dynamic-island-mchack 的 `(T, ζ)` 表与 squircle/变形公式、Lakr233/NotchDrop 的极简范围模型。
- **只读（GPL-3.0，禁止复制代码/资源/字符串）**：WinIsland、boring.notch、Atoll、livebridge。本文引用的**理念**（固定窗口、优先级列表、签名索引、声明式贡献）属于思想，实现必须独立完成；每个借用点保留一份"clean-room 说明"（`docs/rewrite/research/clean-room-notes.md`，M1 建立）。
- **禁止**：SF Symbols 字体或"suspiciously similar"字形、Apple Design Resources 模板、任何仓库的预览截图（含第三方专辑封面/图标）。
- 新增第三方依赖必须在 `Directory.Packages.props` 记录许可，且限于 MIT/Apache-2.0/BSD/MS-PL。







