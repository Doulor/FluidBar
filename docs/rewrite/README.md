# FluidBar v2 文档索引

> 最后更新：2026-10-18 22:45（Asia/Shanghai）

先读顺序：根目录 [`PROJECT-NOTES.md`](../../PROJECT-NOTES.md) → [`DECISIONS.md`](DECISIONS.md) → [`specs/`](specs/) → [`contracts/`](contracts/)。

| 文档 | 内容 |
|---|---|
| [`STATUS.md`](STATUS.md) | 当前状态、测试数、进行中、阻塞、下一步（每个检查点更新） |
| [`DECISIONS.md`](DECISIONS.md) | 决策日志（D/I/Q 系列，含日期与影响） |
| [`specs/2026-10-18-fluidbar-v2-design.md`](specs/2026-10-18-fluidbar-v2-design.md) | v2 设计规范（含修订 A1/A2） |
| [`plans/2026-10-18-v2-implementation-plan.md`](plans/2026-10-18-v2-implementation-plan.md) | M0–M6 实施计划 |
| [`prototype2/`](prototype2/) | 当前干净交互原型（本地 8125） |
| [`contracts/core.md`](contracts/core.md) | Core API/状态机/布局/弹簧/pager/设置契约 |
| [`contracts/platform.md`](contracts/platform.md) | Windows 平台服务与互操作契约 |
| [`contracts/plugin.md`](contracts/plugin.md) | SDK/manifest/加载器/市场契约 |
| [`contracts/builtin-plugins.md`](contracts/builtin-plugins.md) | 第一方插件与注册方式 |
| [`contracts/ui.md`](contracts/ui.md) | Avalonia UI / i18n / 设置/市场 UI 契约 |
| [`contracts/layered-host.md`](contracts/layered-host.md) | Win32 分层宿主 API/尺寸/帧成本/输入 |
| [`contracts/skia-island.md`](contracts/skia-island.md) | Skia 岛渲染器架构/缓存/输入/文本 |
| [`contracts/skia-host-acceptance.md`](contracts/skia-host-acceptance.md) | 分层宿主验收清单（自动 smoke + 手工 QA） |
| [`contracts/skia-host.md`](contracts/skia-host.md) | App 内 Skia 宿主集成（生命周期/输入/帧循环/已知偏差） |
| [`contracts/island-uia.md`](contracts/island-uia.md) | 岛 UIA 无障碍（元素树/模式/键盘/限制） |
| [`contracts/host-transparency.md`](contracts/host-transparency.md) | 透明问题调查与结论 |
| [`contracts/integration.md`](contracts/integration.md) | 跨项目集成测试与 SDK↔Core 映射 |
| [`liquid-glass-lab/`](liquid-glass-lab/index.html) | 液态玻璃网页原型（6 个版本，选型用） |
| [`research/liquid-glass.md`](research/liquid-glass.md) | 液态玻璃材质调研（17 个参考仓库 + emilkowalski/skills）、参数、实测、截图 |
| [`research/`](research/) | v1 审计、参考设计、技术栈、插件生态、渲染 spike |
| [`docs/plugin/`](../plugin/) | 插件作者/维护者文档（快速开始、签名、发布、审核） |
| [`CHANGELOG.md`](../../CHANGELOG.md) | 变更记录 |

## 维护要求

- 状态类文档（STATUS/CHANGELOG）必须带"最后更新"日期。
- 设计变更追加修订（A1/A2…），不删除旧结论。
- 测试数、尺寸、帧耗时必须来自实测。
- 任何公共 API/契约变化同步更新对应 `contracts/*.md`。



