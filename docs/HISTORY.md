# 变更历史

## 2026-10-21

- 十二个真机 bug 修复：pin 被心跳覆盖、可见容量挤占、Stacked 轮换无 pin、滚轮门限混用、歌词晚一拍、歌词 1fps、逐词回退、Agent 完成后不消失、完成态仍转圈、环不转（无脏区）、小岛与主岛转圈两套实现、GSMTC 为空时媒体岛消失。
- 新增：功耗模式（性能/省电，设置面板可切）、`AgentSpinner` 共享图元、persistent 预留、媒体 fallback 探测。
- 基线：Core 640 · Island 441 · UI 490 · Platform 660 · BuiltinPlugins 25，全绿。
- 详见 `docs/HANDOVER.md` 与 `docs/2026-10-21-split-wheel-fixes.md`。
