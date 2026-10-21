# 灵动岛视觉审计基线 v3 / Island visual audit sheet v3

- 日期：2026-10-21
- 性质：只读审计，未修改 src/ 或测试；所有图片由生产渲染器 IslandRenderer.RenderToBitmap 生成
- HEAD：1486a48（几何 v3）
- 参考：docs/ios-design-study.md §1 尺寸/比例、§2 材质、§3 字体排印、§5 Live Activity 密度
- 对比：build/reports/ui-audit/audit-sheet-v2/（v2）及 v2/measurements.json
- 缩放：1 DIP = 150% DPI 下 1.5 px；4× 图 = 4 px/DIP；展开页/rail 为 1.5× 生产像素密度

## 0. 产出清单

| 文件 | 状态 | 缩放 | PNG 尺寸 | v2 → v3 备注 |
| --- | --- | --- | --- | --- |
| audit-sheet.png | 汇总图 | 混合 | 2400×2800 | 网格 + 尺寸标注（A/B/C/D） |
| compact-media-4x.png | 紧凑媒体 | 4× | 902×280 | 与 v2 同（200×40 / 封面 22） |
| compact-notification-4x.png | 紧凑通知 | 4× | 866×229 | 与 v2 同（194×36） |
| compact-agent-4x.png | 紧凑 Agent | 4× | 866×229 | 与 v2 同（194×36） |
| compact-clipboard-4x.png | 紧凑剪贴板 | 4× | 866×229 | 与 v2 同（194×36） |
| expanded-0-home.png | 展开首页 | 1.5× | 700×406 | 440×284→440×244；r30→r32 |
| expanded-1-media.png | 展开媒体 | 1.5× | 700×406 | 244 重排（几何见 §3.1） |
| expanded-2-notifications.png | 展开通知 | 1.5× | 700×406 | 列表 44 行高 / hover-only × |
| expanded-3-clipboard.png | 展开剪贴板 | 1.5× | 700×406 | 列表 44 行高 / hover-only × |
| expanded-4-agent.png | 展开 Agent | 1.5× | 700×406 | fixture 仍为占位页 |
| expanded-5-widgets.png | 展开小组件 | 1.5× | 700×406 | 高度重排 |
| split-satellites-4x.png | 分裂卫星 | 4× | 1267×260 | 内框 30→24（0.60d）；r6.75→5.4 |
| rail-preview-4x.png | rail 预览条 | 4× | 804×160 | 胶囊 67×28→79×32；标签 11 |
| measurements.json | 原始测量 | — | — | 增加 mediaPage、iconSystem、list 字段 |

## 1. 测量方法

- 复用现有审计入口的同一套状态工厂：tests/FluidBar.Island.Tests/IslandTestFixtures（MediaCompact/Notification/Agent/Clipboard/Expanded(page)/RailState(page)），没有另写渲染分支。
- 临时 harness 在 %TEMP%/island-audit-harness，编译引用 src/FluidBar.Island 与 fixture 源文件；只写 build/reports/ui-audit/audit-sheet-v3/。
- 壳尺寸/圆角：IslandRenderState.ShellWidth/ShellHeight/ShellRadii；像素 bbox = alpha>32 外接框（含软阴影）。
- 媒体页：PageVisual 的 PageRect + MediaArt/MediaLyricCard/MediaSeekRow/MediaSeekTrack/MediaPlay/MediaFooter（绝对 surface DIP，page 原点 x=44,y=36）。
- 分裂球：IslandMetrics.SplitIsletRects + SplitSatelliteStyle.InnerFrameRect/InnerFrameRadius；环参数取 SplitSatelliteStyle。
- rail：IslandRenderer.MeasureStrip（IslandStripLayout，page3 剪贴板以覆盖 79 DIP 最长标签）。
- 字号：TextEngine 实际解析后的 TypographyScale.Apply(IslandType.*) 值。
- 材质：MaterialTokens.Dark 的 L0–L4 token 对照 MaterialSpecs。

## 2. A. 紧凑态（4 种，4×）

### 2.1 尺寸与 v2 Δ

| 状态 | v3 壳 W×H | v2 壳 W×H | Δ（宽/高） | v3 圆角 | v2 圆角 | Δ | v3 bbox（含阴影） | v2 bbox | Δ bbox 宽 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 媒体 media | 200×40 | 200×40 | 0 / 0 | 20（h/2） | 20 | 0 | 208.25×47.75 | 208.25×47.75 | 0 |
| 通知 notification | 194×36 | 194×36 | 0 / 0 | 18（h/2） | 18 | 0 | 201×41.25 | 201×41.25 | 0 |
| Agent | 194×36 | 194×36 | 0 / 0 | 18（h/2） | 18 | 0 | 201×41.25 | 201×41.25 | 0 |
| 剪贴板 clipboard | 194×36 | 194×36 | 0 / 0 | 18（h/2） | 18 | 0 | 201×41.25 | 201×41.25 | 0 |

### 2.2 间距/结构与参考

| 项目 | v3 实测 | v2 | Δ | 文档参考 | v3 vs 参考 |
| --- | ---: | ---: | ---: | --- | --- |
| 媒体左内边距 | 10 | 10 | 0 | 10–12 | 合规 |
| 媒体封面 | 22（0.55h） | 22 | 0 | 20–24（0.5–0.6h） | 合规 |
| 媒体右内边距 | 8 | 8 | 0 | 10–12 | 偏紧 2–4 |
| 图标-文本 / 文本-波形 gap | 9 / 9 | 9 / 9 | 0 | — | 无变化 |
| 媒体波形 | 20×15 | 20×15 | 0 | — | — |
| 通知文本列 | x=40（20 DIP 图标槽） | x=40 | 0 | iOS compact 图标 20–22 | 一致 |
| 紧凑宽高比 | 5.00 / 5.39 / 5.39 / 5.39 | 5.00 / 5.39 / 5.39 / 5.39 | 0 | iOS 3.375；文档目标 media 168–192、其余 144–180 | media +8~+32；其余 +14~+50（仍偏宽） |
| 紧凑高度 | 40 / 36 / 36 / 36 | 同 | 0 | iOS 37.33 | media +2.67（+7.1%）；其余 -1.33（-3.6%） |
| 紧凑圆角 | h/2（20/18） | 同 | 0 | h/2 | 合规 |

### 2.3 字体（实际解析值）与 v2 Δ

| 角色 | v3 实测 DIP | v2 实测 | Δ | 文档参考 | v3 vs 参考 |
| --- | ---: | ---: | ---: | --- | --- |
| Compact title | 14 | 14 | 0 | 14 semibold | 合规 |
| Compact media title | 14 | 14 | 0 | 14 semibold | 合规 |
| Compact media subtitle | 11 | 11 | 0 | 11 | 合规 |
| Compact notice | 11.5 | 11.5 | 0 | 正文 11 | +0.5 |
| Compact clipboard label | 11 | 11 | 0 | 11 | 合规 |
| Compact Agent task | 11.5 | 11.5 | 0 | 11 | +0.5 |
| Compact badge | 10 | 10 | 0 | 10 | 合规 |
| Overflow badge | 11 | 11 | 0 | 10 | +1（偏大） |

## 3. B. 展开态（6 页，1.5×）

### 3.1 几何与 v2 Δ

| 项目 | v3 实测 | v2 | Δ | 文档参考 | v3 vs 参考 |
| --- | ---: | ---: | ---: | --- | --- |
| 展开 shell | 440×244 | 440×284 | 宽 0；高 -40 | iOS 368×111（3.315）；文档建议 440×220–240（1.8–2.0） | 1.80，进入建议区 |
| 展开圆角 | 32 | 30 | +2 | 文档表格 28（旧）；建议 36–44 | 仍差 4–12；v3 已接近下限 |
| 页面内容框 | 400×220（page x44,y36） | 400×260 | 高 -40 | 旧表 440×284-20/16 | 随壳高重排 |
| PageVisual 水平 inset | 16 | 20（v2 报告） | -4 | 20/16（旧表） | 比旧表小 4（需确认文档是否更新） |
| PageVisual body top | 40 | 28（v2 报告） | +12 | 28（旧表） | 比旧表深 12（为 244 重排） |
| 卡片/行/按钮圆角 | 12 / 8 / 10 | 12 / 8 / 10 | 0 | 卡片 12 | 合规 |
| 列表行高 / gap | 44 / 4 | 通知 40/4；剪贴板 48/6（旧） | +4/−4（统一） | 文档无硬值；hover-only × | v3 统一 44，密度更一致 |

### 3.2 媒体页 244 重排（PageVisual 实测）

page rect = x44,y36,w400,h220；以下为绝对 surface DIP：

| 元素 | v3 矩形 | v2 旧布局（PageLayout） | Δ | 任务目标 | 结果 |
| --- | --- | --- | ---: | --- | --- |
| 封面 | x60,y76 64×64；r12 | x44,y64 92×92 | -28 边；x+16,y+12 | 64 r12 | 一致 |
| 歌词卡 | x60,y140 368×32 | x44,y164 400×46 | 高 -14；y -24 | 140..172 | 一致（140..172） |
| 歌词 line1/line2 | 140..158 / 158..172 | — | — | 140..172 | 一致 |
| 进度行/轨道 | 行 180..194；轨道 x104,y185.5 280×3 | 行 220..242；轨道 x83 322×3 | 行 y -40、高 -8；轨 x+21、宽 -42 | 180..194、轨 3 | 一致 |
| 传输组 | play x228,y204 32×32；prev/next 26×26 y207 | play x75,y254 30×30 | 主轴 30→32（+2）；整组居中 | 204..236、主轴 32 | 一致 |
| 底栏 | x104,y244 280×14 | 旧布局无独立底栏 | 新增 | 244..258 | 一致 |
| 媒体标题字号 | 20 | 22（v2 实测） | -2 | 20 | 修正到参考 |

结论：媒体页 5 个关键几何全部与任务目标一致；封面从 92 缩到 64（-28），歌词/进度/传输/底栏整体上移，244 面板内无溢出。

### 3.3 展开字号与 v2 Δ

| 角色 | v3 实测 DIP | v2 实测 | Δ | 文档参考 | v3 vs 参考 |
| --- | ---: | ---: | ---: | --- | --- |
| PageTitle | 13 | 13 | 0 | 13 | 合规 |
| PageNumber | 10 | 10 | 0 | 9.5 | +0.5 |
| RowTitle | 13 | 13 | 0 | 13 | 合规 |
| RowBody | 11.5 | 11.5 | 0 | 11.5 | 合规 |
| RowMeta | 10 | 10 | 0 | 10 | 合规 |
| MediaTitle | 20 | 22 | -2 | 20 | 合规（修正 v2 的 +2） |
| MediaArtist | 11.5 | 11.5 | 0 | 13 | -1.5 |
| MediaAlbum | 11.5 | 11.5 | 0 | 11.5 | 合规 |
| 行高（ExpandedBody） | 11.5 / 16 = 1.39× | 1.39× | 0 | iOS 1.20–1.30 | 仍略松 +0.09–0.19 |

## 4. C. 分裂卫星（4×）

| 项目 | v3 实测 DIP | v2 | Δ | 文档参考 | v3 vs 参考 |
| --- | ---: | ---: | ---: | --- | --- |
| 球径 | 40 | 40 | 0 | iOS 37.33；设计规则 36–40 | +2.67 vs iOS；合规 |
| 球间距 | 8 | 8 | 0 | 8–12 | 合规（偏小端） |
| 内框边长 | 24（0.60d） | 30（0.75d） | -6 | 20–24（0.5–0.6h） | 合规（上限） |
| 内框圆角 | 5.4（22.5%） | 6.75（22.5%） | -1.35 | 22.5% | 合规 |
| 黑环 | 8（内框外黑边） | v2 未单列 | 新增 | — | 视觉黑环清楚 |
| Agent 环 | 2 DIP / 180° / 120°/s；环半径 14（直径 28） | 2 DIP / 180°；v2 环半径约 14（未单列） | 速度明确化；半径一致 | 无直接 iOS 数值 | 单元素 + 120°/s |
| 第 3 个活动 | +2 芯片 22×16（主岛右侧，reserve 26） | 同 | 0 | 任务描述期待第 3 球 | 仍只有 +N 芯片 |
| 球底色 | 与主岛同色，无描边/标签 | 同 | 0 | 纯圆 | 合规 |

## 5. D. rail 预览条（4×）

| 项目 | v3 实测 DIP | v2（同标签公式） | Δ | 文档参考 | v3 vs 参考 |
| --- | ---: | ---: | ---: | --- | --- |
| rail strip | 181×32 | — | — | StripHeight 32 | 高度合规 |
| 当前胶囊（page3 剪贴板） | 79×32，r16 | 79×28 | 高度 +4 | iOS Live Activity 紧凑 37.33 | 高度 -5.33；宽度合规 |
| 前/后圆 | 26×26（y279） | 26×26 | 0 | 参考 36–40 | 小 10–14（次级预览） |
| rail 标签 | 11 | 11 | 0 | caption2 11 | 合规 |
| far dots | 3×3 | 3×3 | 0 | — | — |
| rail 内边距 | 12 | 12 | 0 | 12 | 合规 |

说明：v3 的 79 DIP 胶囊取自 page3“剪贴板”；page0“主页”仍为 67 DIP（同一公式的标签差异），不是回归。

## 6. 材质与图标/列表系统

### 6.1 材质 L0–L4（v2 → v3 未变）

| 层 | v3 token | 规格 | 结果 | v2 → v3 Δ |
| --- | --- | --- | --- | --- |
| L0 | #08080A，opaque | #000/#08080A，0.96–1.00 | 合规 | 0 |
| L1 | #141418 @60%，r12 | 55–65%，r12 | 合规 | 0 |
| L2 | white 9%，1 DIP | 8–11%，1 DIP | 合规 | 0 |
| L3 | white 5%，1.5 DIP，渐变 | 4–6%，1–2 DIP | 合规 | 0 |
| L4 | black 18%，8 DIP，blur 4 | 15–20%，6–10 DIP | 合规 | 0 |

### 6.2 图标系统

- v3：20×20 单位视框、所有描边统一 1.5 DIP、round caps/joins、墨迹在视框中心 (10,10) 光学居中（IconSet 常量）。
- v2：未统一，作为 v3 新增系统记录；视觉审计按 20 DIP 图标槽核对，未见溢出。

### 6.3 列表密度与 hover-only ×

- v3：统一行高 44、行 gap 4；删除常驻 ×，仅 hover/focus 显示（通知/剪贴板/Agent 页）。
- v2：通知 40/4、剪贴板 48/6，常驻 ×；v3 密度更统一，但总行数/滚动高度需在 244 面板内复核（当前截图无溢出）。

## 7. 仍与 iOS 有差距的 Top 5（含改法与工作量）

| # | 差距 | v3 实测 | iOS/文档参考 | 建议改法 | 预估工作量 |
| --- | --- | ---: | --- | --- | --- |
| 1 | 紧凑宽度仍偏宽 | media 200 / 其余 194；ratio 5.00/5.39 | iOS 3.375；文档目标 media 168–192、其余 144–180 | media→192、其余→180；保留标题优先/省略规则并更新 golden | S–M（1–1.5 人日） |
| 2 | 紧凑右内边距偏紧 | 8 | 10–12 | media RightPadding 8→10；同步 wave 右移与文本列宽 | S（0.5 人日 + golden） |
| 3 | 展开圆角仍小 | 32 | 建议 36–44（0.13–0.15h） | 先评估 36（仍低于 244 高度的 0.15h=36.6）；四角+阴影+命中一起改 | S–M（0.5–1 人日） |
| 4 | 展开内容网格与文档不一致 | PageVisual inset 16 / body top 40 | 旧文档 20/16 / body top 28 | 确认 v3 是否正式改为 16/40；若否，恢复 20 水平 inset并更新文档；若接受，更新 docs 表格避免后续 drift | S（0.5 人日文档+测试；改布局 M） |
| 5 | rail 胶囊高度仍低 | 79×32 | iOS 紧凑 37.33；rail 参考高度 37.33 | 32→34–36；保持 79 宽度/11 标签；同步 26 圆点与 strip 高度 | S–M（0.5–1 人日） |

次级差异：ExpandedBody 行高 1.39× vs iOS 1.20–1.30；MediaArtist 11.5 vs 13（-1.5）；PageNumber 10 vs 9.5；Overflow 11 vs 10；第 3 个活动仍无第三球；Agent 页 fixture 占位。

## 8. 复现与只读声明

- 所有 PNG 由生产 IslandRenderer.RenderToBitmap 生成，未使用截图/放大插值；4× = 每 DIP 4 px，展开/rail 1.5× = 150% DPI 生产密度。
- 状态工厂与 golden/audit 相同；现有 gated 入口可复用：FLUIDBAR_SATELLITE_EVIDENCE=1 产出卫星 4×/1.5×；本次汇总图不需要新环境开关。
- 临时 harness：%TEMP%/island-audit-harness；仓库仅新增 build/reports/ui-audit/audit-sheet-v3/。
- measurements.json 是全部实测值的机器可读副本（含 mediaPage、iconSystem、list、material）。
- 只读：未修改 src/、未修改测试。

## 9. 附：v3 字号 token（实际解析，v2→v3）

| Token | v3 DIP | v2 DIP | Δ |
| --- | ---: | ---: | ---: |
| CompactTitle | 14 | 14 | 0 |
| CompactMediaTitle | 14 | 14 | 0 |
| CompactMediaSub | 11 | 11 | 0 |
| CompactNotice | 11.5 | 11.5 | 0 |
| CompactClipLabel | 11 | 11 | 0 |
| CompactAgentTask | 11.5 | 11.5 | 0 |
| PageTitle | 13 | 13 | 0 |
| PageNumber | 10 | 10 | 0 |
| RowTitle | 13 | 13 | 0 |
| RowBody | 11.5 | 11.5 | 0 |
| RowMeta | 10 | 10 | 0 |
| MediaTitle | 20 | 22 | -2 |
| MediaArtist | 11.5 | 11.5 | 0 |
| MediaAlbum | 11.5 | 11.5 | 0 |
| MiniLabel | 11 | 11 | 0 |
| OverflowBadge | 11 | 11 | 0 |
