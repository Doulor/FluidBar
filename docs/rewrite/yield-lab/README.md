# 让位动效 · 网页原型（选型用）

> 双击 `index.html`（Edge / Chrome 打开）。
> **Space** = 让位/恢复，**1–6** = 切预设，**G** = 全览（六个同时停在"让位后"的状态）。

场景：屏幕顶部中间的胶囊，下面压着一个**最大化窗口**。让位 = 岛停止接收点击，但"它还要不要留在视野里、留多少"是这六个预设的区别。

| # | 预设 | 让位动作 | 时长 / 缓动 | 恢复 | 特点 |
|---|---|---|---|---|---|
| A | 淡出 | opacity 1 → 0.12，不移动 | 160 ms ease-out | 淡入 160 ms | 最安静，像"岛还在但不理你" |
| B | 滑出屏幕顶部 | translateY(0 → −190%) | 200 ms cubic-bezier(.4,0,.2,1) | 滑回 260 ms | 让位最彻底，视野里完全没有 |
| C | 后退 | scale .84 + 下沉 10 px + opacity .45 | 200 ms cubic-bezier(.34,1.4,.64,1) | 260 ms | 像往后退一步，仍看得见 |
| D | 角落停靠 | 移到右上角 + scale .62 + opacity .8 | 260 ms cubic-bezier(.32,.72,0,1) | 滑回 300 ms | 始终在视野边缘，随时能找到 |
| E | 柔化 | blur 6 px + saturate .55 + opacity .25 | 240 ms ease-out | 240 ms | 玻璃"化开"，视觉噪音最低 |
| F | 只剩轮廓 | 背景 .04 + 1 px 描边 .30 + 内容 .12 | 200 ms ease-out | 200 ms | 最不打扰但不丢位置 |

截图：`shots/overview.jpg`（六格全览，均为"让位后"状态）、`shots/A-fade.jpg` … `shots/F-outline.jpg`。

## 反馈方式

- 直接说编号（"要 C"），或者组合（"B 的时长 + C 的幅度"、"想要 D 但缩得再小一点"）。
- 也可以说"这几个都不对"，我按你的描述再做一批。

## 落进 App 时要改的地方

`IslandRenderer` 的屈服态（`IslandRenderState` 加 `YieldProgress` 0..1）：

- A：壳体 opacity × (1 − .88·p)
- B：整个 surface 沿 Y 平移 −190% × p（宿主窗口不动，画在 surface 里）
- C：scale(.84) + translateY(10) + opacity .45，全部按 p 插值
- D：平移到角落（需要 `ShellOrigin` 插值）+ scale(.62)
- E：先渲染到 layer 再 blur（成本最高，需实测是否进 8.33 ms 预算）
- F：只画 `DrawEdgeOnly`，填充/内容 opacity × .12
