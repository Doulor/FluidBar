# 视觉验收清单（每轮 UI/动效迭代必过）

> 目的：把「看起来更 iOS 了」变成可检查的条目，避免每轮凭感觉。

## A. 硬性闸门（必须全绿，否则不算完成）

| 闸门 | 位置 | 判据 |
| --- | --- | --- |
| 卫星材质区间 | `SatelliteVisualQualityTests` | rim 6–10%、hairline 5–10%、球底阴影 15–25%、press 0.18、环速 110–130°/s、无 accent |
| 卫星像素断言 | `SplitSatellitePolishTests` | rim 亮度差 0.05–0.11、hairline 7.2、frames 路径消费 RingAngleDegrees |
| 材质对比度 | `MaterialTokenTests` | 正文 ≥4.5、次要 ≥3.0，失败抛异常 |
| 8 DIP 网格 | `PageMetricsLayout` 断言 | 间距仅 4/8/12/16/20/24 |
| 性能 | `FrameSchedulerTests` | 无连续 3 帧全帧；动画期间无连续 2 帧 >33ms |
| 字体标尺 | `FontScaleTests` | PageTitle 13 / RowTitle 13 / RowBody 11.5 / RowMeta 10 |

## B. 视觉检查（每轮出图对照）

1. **紧凑态 4 种**：宽度 200/194/194/194，圆角 = h/2，封面 22，左 padding 10；截断后至少 12 字符。
2. **展开页 6 个**：标题 13、行高 44、卡片 L1 + 顶部高光、空态统一、底部 16 DIP 安全区。
3. **卫星球**：纯圆、黑球 + 圆角方形完整封面（内边距 5、内框 30、r=6.75 或按 22.5%）、Agent 仅蓝环、hover 外发光、press 0.94。
4. **rail**：标签 11、胶囊 67–79、圆点中心随宽度、hover/press 状态正确。
5. **设置面板**：导航选中态左条、行高 48、滚动条 6 DIP、按压 overlay + 0.99 缩放。

## C. 动效检查（逐帧）

| 动效 | 时长 | 检查点 |
| --- | --- | --- |
| 展开 | 0.31s + 内容 60ms/chrome 120ms 编排 | 容器先动、内容后跟，无元素同时齐动 |
| 收起 | 反向 | 无跳变 |
| 切页 | 0.38s / ζ0.94 | 有方向感（±1.8% 缩放 + 4% 透明度深度），快速连切从当前值续接 |
| 卫星出生 | 0.445s | 预压 0.92×60ms → 拉伸 8% → 过冲 4.6% |
| 卫星融合 | 反向 + 2% 过冲 | 落点精确、无回弹抖动 |
| 按压 | 0.18s / ζ0.65 | 0.94 + 亮度 −6% + 内阴影 0.18 |

## D. 每轮必须产出

- `build/reports/ui-audit/audit-sheet/audit-sheet.png`（全部状态 4× 网格）
- `build/reports/ui-audit/motion-capture/motion-strip.png`（四类动效各 8 帧）
- 与上一轮的**并排差异图**（哪几处变了、变了多少 DIP）

## E. 已知平台事实（不要重复踩）

1. Avalonia 12 不渲染 `BoxShadow.IsInset` → 用 overlay 渐变。
2. 无边框 popup 窗口 DWM 不给圆角 → 加 `WS_CAPTION|WS_THICKFRAME` + `WM_NCCALCSIZE`。
3. `UpdateLayeredWindow` 与 Avalonia TopLevel 互斥 → 设置窗口不做自绘分层。
4. 屏幕锁定时 `CopyFromScreen` 只能取到壁纸 → 用 `IslandRenderer.RenderToBitmap` 离屏渲染取证。
