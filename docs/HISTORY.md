# FluidBar 开发阶段记录

> 记录 v2 重构分支（现已升为 `main`）的阶段性提交；完整历史用 `git log --oneline` 查看。

## 分支约定

| 分支 | 含义 |
| --- | --- |
| `main` | **当前主线**（原 `v2/rebuild`） |
| `archive/main-before-v2` | 重构前的老主线，已归档只读 |

## 阶段总览

| 阶段 | 主题 | 代表提交 |
| --- | --- | --- |
| P1 | 预览小岛 rail 像素级还原 + 命中修复 | rail geometry / strip hook |
| P2 | 设置面板逐数值还原 + 统一控件层 SettingsControls | settings tokens |
| P3 | 岛本体动效（CSS 等价曲线、三层淡入、去幽灵层） | 0df75c5 |
| P4 | 媒体（MSAA 控酷狗、KRC 逐行逐词、真实封面 + 兜底源） | 8e57e71 |
| P5 | AI Agent（DSH 原生只读 + Claude/Codex/Copilot/Aider/WorkBuddy） | task-25/26/27 |
| P6 | 通知真实数据 + 单实例、天气/任务真实数据 | task-13/14/15 |
| P7 | 文字可读性（对比度/字号体检 + 三级文字 token 统一） | 71237cc |
| P8 | 性能（帧间隔 p99 45.5→20.5ms、软件渲染降内存） | b6fa2af |
| P9 | 活动堆叠（分裂小岛默认 / 多岛堆叠 / 仅最新 + 滚轮轮换） | 60d7eca |
| P10 | 分裂球视觉（纯圆、无强调色、黑球内圆角方形完整封面） | 183734b |
| P11 | 插件架构（页面注册表、12 页 rail overflow、本地插件渠道） | fb5a60e / 28b451f |

## 关键里程碑

```
183734b feat(island): rounded-square contain-fit media preview
28b451f refactor(island): page identity by id, no fixed page count, architecture guards
fb5a60e feat(plugin): descriptor driven pages, twelve page rail with overflow and user page order
78416c5 fix(island): neutral split capsules, no accent colour
60d7eca feat(island): split-islands display strategy with primary policy and wheel stack rotation
b6fa2af perf(app): software render Avalonia in the skia host to shed the GPU stack
71237cc fix(theme): one shared tertiary text token for island and settings
8e57e71 fix(media): per-track artwork lookups and end-to-end verification dumps
0df75c5 feat(island): prototype2 shell motion, compact metrics and no ghost paint
```

## 查看方式

```bash
git log --oneline --graph --decorate -40
git log --oneline main..archive/main-before-v2
git log --stat <commit>
```

## 发布

- 产物 `build/release/win-x64/FluidBar.exe`（`tools/release/publish.ps1`）
- SHA256 与检查点记录在 `docs/rewrite/STATUS.md`
- 测试基线 2466 passed / 0 failed
