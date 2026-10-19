# FluidBar v2 — 当前状态（STATUS）

> 最后更新：2026-10-20 03:45（Asia/Shanghai）
> 维护规则：每个检查点由 Lead 更新本文件；数字必须来自实测。

## 一句话状态

**渲染提交根因已修复**：分层窗口在“隐式几何”更新下返回成功但不刷新（只有首帧上屏），导致展开/翻页全部不可见；现在每次提交都显式传 `pptDst`/`psize`，真实鼠标点击 → 弹簧展开 → 面板可见、悬停不收起、移开 2.6s 后收起、点击穿透恢复。

## 里程碑

| 里程碑 | 状态 | 证据 |
|---|---|---|
| M0 分支/骨架/CI/渲染决策 | ✅ | `v2/rebuild`、`v1-final`、spec/plan/DECISIONS |
| M1 Core 领域层 | ✅ | 473 tests |
| M2 插件 SDK/宿主/市场 | ✅ | 188 tests；离线 E2E |
| M3 Windows 平台 | ✅ | 371 tests；M3+P3 |
| M5 内置插件/市场 CI/文档 | ✅ | 23 builtin + 31 integration；`docs/plugin/` |
| M4 UI / Skia 宿主 | ✅（MVP） | 新分层宿主 + Skia 渲染已接入，默认 `FLUIDBAR_HOST=skia`；108 UI tests；旧 Avalonia 宿主保留 fallback |
| M6 硬化/发布 | ⏳ | soak/性能预算已就绪；Release 浸泡待做 |

## 测试（当前）

| 套件 | 数量 | 状态 |
|---|---:|---|
| Core | 491 | ✅ |
| Platform | 409 | ✅（偶发 HWND/网络 flaky，单独运行稳定） |
| SDK | 152 | ✅ |
| PluginHost | 39 | ✅ |
| Builtin | 23 | ✅ |
| Integration | 31 | ✅ |
| UI | 184 | ✅ |
| Architecture | 11 | ✅ |

构建：`dotnet build FluidBar.sln` = 0 errors；`dotnet test FluidBar.sln -m:1` = **1493 passed / 0 failed**
（Core 495 · Platform 418 · SDK 152 · UI 188 · Island 136 · PluginHost 39 · Builtin 23 · Integration 31 · Architecture 11）。

## 2026-10-20 05:35 检查点（市场/关于复核 + 媒体真机二次攻坚）

- **设置面板收口**：市场页修掉 3 个真实缺陷（featured 卡缺暖色径向层且层序反了 → 下半部暗到与页底同色；chips 与网格之间多 10px；滚动条不占 gutter 导致内容偏宽 9px）；关于页不再插入空状态行、失败文案中性化、6 种更新状态 × 中英齐备。设置窗口在 FontScale 1.0/1.6 下渲染字节完全相同。
- **媒体真机回归**（酷狗正在播放）：修掉『封面永远不显示』的两个真根因——① 快照发布早于封面入缓存且结构签名不含 ArtworkKey；② 酷狗缓存为 WebP 且文件夹全角/半角与标题不一致。现在紧凑胶囊与展开页都显示真实专辑图；进度 1:50→2:13→2:36 三帧推进；逐行歌词随进度滚动；岛上 暂停/恢复/上一首/下一首 全部真机生效。另修：歌词身份行（『歌名 - 歌手』）被当歌词、Next 与当前行重复时不隐藏；播放态改读无障碍树「播放/暂停」图标可见性，隐藏时间文本也能 1s 内反映。
- 测试：**1860 passed / 0 failed**（Core 567 · Island 274 · Ui 250 · Platform 498 · Sdk 167 · PluginHost 39 · Integration 31 · Builtin 23 · Architecture 11）。
- Release：`build/release/win-x64/FluidBar.exe` SHA256 `3757257F21E6F54F57CF48C2461A5EB691B71132373FB50368A53AD8B7043994`，HEAD `8778c8e`。

## 2026-10-20 04:50 检查点（多 agent 通宵轮：原型对齐 + 插件攻坚）

多 agent 协同完成 6 批工作流（marketplace-flows / island-audit / rail-pixel / settings-pixel / media-lyrics / settings-audit / island-polish / island-body）。

- **预览小岛**：用 playwright 实测 prototype2 DOM，17 项像素级对齐（rail 8/32、胶囊按标签 64–104、末点 7×3、命中区 7×15、远点左簇→01 右簇→06、删除 6 处非原型绘制、换页 smoothstep 插值）；补 strip 命中区修复（此前点击被当桌面穿透）。
- **岛本体**：展开/收起改为 CSS 等价 `380ms cubic-bezier(.3,.8,.25,1)`（52 采样点误差 ≤0.05px）、三层淡入时序独立（消除 0→0.5 跳变）、删除发明的页切换 scale/parallax、壳层去描边+原型双层阴影、内容区 400×260、幽灵胶囊视觉删除、紧凑五形态对齐。
- **设置面板**：`SettingsTokens` 逐数值搬自 styles.css、按 icons.js 原始 SVG path 重绘图标、chrome/行高/开关/segmented/keycap/预览画布/市场卡片全对齐。
- **设置项生效**：6 个「只写 json 不生效」项接上消费方（调色板此前 mapper 根本不读）、开机启动真实写 HKCU\Run、安全模式真正阻止拉岛、崩溃护栏、帧统计实时；修 2 个死按钮（重置确认框听错事件、chip 处理器被 Handled 挡掉）。
- **自动更新**：从「写死 UpToDate」→ 真实链路（启动+5s/24h 检查、.part 下载、SHA-256 校验、仅 Verified 可安装），E2E 用真实 180224 B 产物验证哈希一致。
- **插件市场**：提交→审核上架→安装→下载计数→评分全流程 + 中英双语；证据 build/evidence/marketplace。
- **媒体**：MSAA（手写 IAccessible vtable）真机控制酷狗（切歌/暂停/播放实测生效）、本地 KRC 逐行歌词、WebP 封面解码；+34 平台测试 + 9 live 测试。
- **AI 状态**：Claude Code / Codex / **DeepSeek Harness**（AgentKind.DeepSeek），事件→胶囊端到端验证。
- **字号**：FontScale 0.8–1.6 由文本度量驱动（1.0 零回归），超出 body 时滚轮先滚内容；修 ParseColor 8 位色顺序、NumberTextCache 脏缓存。
- **系统外观**：接 WM_SETTINGCHANGE 实时跟随主题/强调色/减少动效。

测试：**1824 passed / 0 failed**（Core 567 · Island 274 · Ui 235 · Platform 477 · Sdk 167 · PluginHost 39 · Integration 31 · Builtin 23 · Architecture 11）。
Release：`build/release/win-x64/FluidBar.exe` SHA256 `4BA0425DF0DE84CC4F0D8876CFA40B8A539708999C13024A1313BC2A59E56AB2`，HEAD `884a822`。

剩余（已知、未完成）：
1) 1.6 字号首屏不完整（需允许 expanded shell 高度随字号增长，改 440×284 视觉契约，属产品决策）；
2) 托盘图标锁屏下无彩色证据（代码+单测已覆盖，解锁后重跑脚本即可）；
3) 天气/任务/通知无数据源（诚实显示「不可用」）；
4) agent 紧凑宽保留 288（按原型 236 会截断真名）。

## 2026-10-20 03:45 检查点（原型对齐 + 插件升级）

- **设置面板**：通用页（4 行 + 快捷键组，副标题同原型）、外观页（5 行 + 实时预览 + 预览头 `28 px · 96%`）、插件页（搜索左对齐、行标题单行、发布者与能力合并成一行）已与 prototype2 截图逐项对齐；原型之外的能力（跟随系统、更新方式、调色板、字号、动效强度、匿名数据说明）保留在「高级」页。
- **下方小岛条**：预览岛 26×26、当前岛 73×28、岛间距 8、远点↔岛 7、弹簧 T0.32/ζ0.85、自动隐藏 900 ms（全部取自原型 README 实测值）。
- **小胶囊媒体**：补上真实封面（`CompactModel.Artwork/ArtworkKey` + 共享 `ArtworkImageCache`），进度改读媒体快照，无时间轴时不画进度条。
- **代理插件**：新增 `AgentKind.DeepSeek`，识别 deepseek / deepseek-harness / dsh / harness，代理页显示「DeepSeek Harness」。
- **媒体页**：浏览器视频不再匹配歌词（只有暴露专辑元数据的浏览器来源才查歌词），无歌词时不画空歌词卡。
- 测试：**1493 passed / 0 failed**；Release `build/release/win-x64/FluidBar.exe` SHA256 `09D27B54…43BA`。

## Spike 结论

- **分层宿主**：`UpdateLayeredWindow` 全帧 0.06–0.16ms / 脏矩形 0.08–0.10ms；60/120fps pacing 通过；透明、点击穿透、不抢焦点、拖拽 PASS。宿主表面 float 488×384 / dock 488×444 DIP。
- **Skia 渲染器**：展开 0.55–0.61ms / p99 0.85ms；紧凑 0.46ms；脏矩形 0.157ms；120fps 约 9.8× 余量；fidelity IoU 0.9986 / 1.0000。
- **Avalonia 透明**：11.3.22 与 12.1.4 在本机均失败（灰矩形），弃用为岛宿主；仅保留设置/市场窗口。

## 2026-10-19 16:40 检查点（可用性修复）

- 修复 **分层提交 bug**（唯一阻塞）：`UpdateLayeredWindowIndirect` 隐式几何返回 TRUE 但不更新表面；改为一律显式几何，删除 `UseExplicitUpdateGeometry` / `_geometryDirty`。
- 恢复**弹簧动画**（不再在状态变化时 SnapMotion）与**点击穿透**（钩子命中矩形回到 shell + 2 DIP）。
- **悬停不自动收起**：展开态 hover 清除 auto-collapse deadline，指针离开后重新武装；钩子新增 `PointerLeft`。
- 修复**紧凑药丸宽度/内容不一致**（活动结束残留 ContentKind）与**空闲时钟不走字**（分钟边界 ITimer）。
- 清理全部临时调试日志。
- 首页音量/亮度改为真实数据（未知显示 "—"）；`MonitorService` 枚举前强制 PMv2 线程上下文。
- 最终 Release：`build/release/win-x64/FluidBar.exe` 56.5 MB，SHA256 `D5A02A4D…992E`；真实鼠标回归通过（冷启动 3s 首点、展开、悬停 6s 保持、移开 2.6s 自动收起、chevron 收起、翻页、真实音量）。
- 鼠标钩子自愈（独立消息线程 + 1s 看门狗重装）+ 首帧后才安装；60s 连续 6 轮点击回归全通过。
- 全量测试 **1484 passed / 0 failed**（Core 495 · Island 136）。
- **岛下方常驻淡点预览 + 胶片条错峰淡入/换页脉冲**；**真实亮度**（WMI 属性读取修复，本机 50 可读可写）；**媒体封面/进度插值/1Hz 刷新/歌词**（浏览器媒体已用 Edge Media Session 实测）。
- 拖拽翻页恢复（钩子按住期间转发 drag，实测 04→05）。
- **非 SMTC 播放器（酷狗）识别**：WASAPI 会话 + 窗口标题回退；紧凑药丸显示媒体信息；无时间轴隐藏进度条。
- **设置菜单按 prototype2 1:1 重做**：标题栏/侧栏/分段控件/开关/滑杆/键帽/色点/预览全部对齐原型；插件启用状态修复；窗口自动置顶。
- **亮度写入修复**：每次写入用全新 MTA 线程/COM apartment（本机 WMI 同 apartment 二次写入必失败）；0% 夹到 5% 防黑屏。
- 全量 **1490 passed / 0 failed**。
- 最新 Release：`build/release/win-x64/FluidBar.exe` SHA256 `E1BEE457…F2C4`。
- 待验证：真实鼠标下的剪贴板行复制、媒体控件、开关点击、托盘右键菜单、UIA 树刷新、多显示器/200%。

## 进行中

1. **Skia 渲染器：134 tests + 20 张 golden**（紧凑 8 态、展开 home/media/notifications/clipboard/agent/widgets、dock/flush/light/transition、transient strip）；壳体描边正在改中性 hairline。
2. **prototype2：设置面板/插件市场/3 小岛/远处页小点/切换动效已完成**（14+ 截图）；等待用户验收。
3. **App 集成（MVP + 插件运行时）**：Skia 宿主默认启用；页面控件 seam、多显示器/DPI/dock、tray+热键、真实 PluginHost/市场适配、FollowMouse 与锚点拖拽持久化均已接入；smoke 331 帧/0 提交失败、surface 732×576 @150%。UIA 无障碍 provider（根/紧凑/页面/控件/strip、模式、焦点环、键盘）已接入（[`contracts/island-uia.md`](contracts/island-uia.md)）。待补：IME、Wi-Fi/BT/Focus、output/queue、热键注入复测、双 message host 合并、真机多显示器/200% 验收。

## 可用版本预估（基于当前进度）

| 目标 | 范围 | 预估 |
|---|---|---|
| 内部预览 | 紧凑岛 + 展开 home/media + 设置 + 托盘，真实透明宿主 | **~1–1.5 周** |
| **可用初代版本** | 上述 + 通知/剪贴板基础 + 插件列表 + 热键/托盘 + 桌面验收 | **~3 周（约 15 个工作日）** |
| v2 完整版 | 全部页面/插件/市场/UIA/多显示器/发布链路 | **~8–12 周** |

关键风险：App 宿主集成（最大未知量）、UIA 无障碍、多显示器真机复测、市场签名密钥/托管（Q3–Q5）。

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








