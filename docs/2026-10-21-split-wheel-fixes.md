# 2026-10-21 · 多岛轮换与手感修复记录

## 结论
四个独立 bug 叠加，全部有运行时日志证据，并在真机上用真实鼠标验证。

| # | 现象 | 根因 | 修法 | 证据 |
|---|---|---|---|---|
| 1 | 手动换岛 2 秒后弹回 | 活动心跳 upsert 以 `IsPinned=false` 新建记录，冲掉策略层 pin | `ActivityScheduler.Upsert` 按 key 从 visible/background 继承 pin | 日志 `scheduler upsert ... pinned=False` 紧随 `pin set` |
| 2 | 岛会突然少一个 | `VisibleCapacity=3` 被剪贴板/锁屏键/通知占满，媒体被挤进 background，而 background 不生成小球 | persistent（播放中的媒体、active agent）优先占位，瞬态只占剩余名额 | `normalize capacity=3 live=4` 明细 |
| 3 | 滚了不保持（真机实际路径） | pin 只写在 `ReorderSplitVisible`（Split 路径）；Stacked 走 `RotateStack`，从未设 pin | 抽共享 `PinTo`/`ReleasePin`，Split 与 Stacked 共用 | `wheel-route=stack-rotate` → `pin set (stack-rotate)` → 15 秒后 primary 仍 Media |
| 4 | 轻滚不翻/连翻异常 | 260 ms 过渡锁被当作所有翻页的统一门限 | 按手势类型分别取：同手势 260 ms 丢弃、新意图 150 ms | WheelGestureFeelTests 5/5 |

## 最终手感参数
- 一页/一次轮换：`WheelDipPerNotch = WheelNotchDip = 60 DIP`（1 个物理 notch 即触发）
- snap 弹簧：T = 0.35 s，ζ = 0.75，过冲约 2.8%
- 同手势过渡锁 260 ms、手势空闲 120 ms、新意图 150 ms

## 测试基线
Core 637/637 · Island 119/119 · UI 480/480

## 纪律
同一功能若存在多条分支路径（Split/Stacked、绘制/命中、测量/渲染），必须先抽共享函数，且每条路径各有一条测试。
请用 `FLUIDBAR_PIN_DIAG=1` 启动以输出 `%TEMP%\fluidbar-pin-diag.log`（primary 变化、pin、scheduler upsert、normalize、wheel 路由）。
