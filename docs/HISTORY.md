# 变更历史

## 2026-10-21

- 十二个真机 bug 修复：pin 被心跳覆盖、可见容量挤占、Stacked 轮换无 pin、滚轮门限混用、歌词晚一拍、歌词 1fps、逐词回退、Agent 完成后不消失、完成态仍转圈、环不转（无脏区）、小岛与主岛转圈两套实现、GSMTC 为空时媒体岛消失。
- 新增：功耗模式（性能/省电，设置面板可切）、`AgentSpinner` 共享图元、persistent 预留、媒体 fallback 探测。
- 基线：Core 640 · Island 441 · UI 490 · Platform 660 · BuiltinPlugins 25，全绿。
- **动画卡顿修复**：帧时钟被 50 ms 截止计时器反复停掉（`_lastRenderHappened` 参与停止判定）→ 抽出 `IslandFrameLoopPolicy`，`RenderAllLocked` 返回“是否真的画了一帧”。实测展开 24 帧 / p50 15.9 ms，空闲 CPU 12.8% → 2.1%。
- 测试基线更新：**2522 passed / 0 failed**（Ui 497 含新增 `IslandFrameLoopPolicyTests`）。
- **点击区域修复**：`RegionSignature` 不含壳体尺寸 → 展开后钩子仍只认旧药丸矩形，面板按钮全灭；抽 `IslandHitRegionSignature` 并按帧刷新命中区域。新增 `IslandHitRegionSignatureTests`。
- **媒体回退探测节流**：酷狗 MSAA 整树遍历 4 Hz → 1 Hz（CPU 2.06% → 1.25%）。
- 测试基线：**2525 passed / 0 failed**（Ui 500）。
- 详见 `docs/HANDOVER.md` 与 `docs/2026-10-21-split-wheel-fixes.md`。
