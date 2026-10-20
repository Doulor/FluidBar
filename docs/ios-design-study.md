# iOS 美学与动效研究（FluidBar 可执行版）

> 研究对象：Dynamic Island / Live Activity / Telegram iOS 的公开实现。仅研究，不改 src。
> 1 DIP = 100% 缩放下的逻辑点；FluidBar 150% DPI 时 1 DIP = 1.5 px。
> 当前证据图：build/reports/ui-audit/split/split-settled.png、build/reports/ui-audit/motion/page-before.png。

## 0. 来源与版本

| 仓库 | 浅克隆版本 | 主要文件 |
| --- | --- | --- |
| Telegram-iOS | f1dd7a2 | Display/Source/CAAnimationUtils.swift、ContainedViewLayoutTransition.swift、InteractiveTransitionGestureRecognizer.swift、ListViewAnimation.swift；TelegramUI/Components/ButtonComponent 等 |
| MioIsland（macOS notch） | e534d0ef | ClaudeIsland/Core/NotchGeometry.swift、NotchViewModel.swift、UI/Views/ChatView.swift、CompletionPanel/ClaudeStopVariantView.swift |
| Hello-Dynamic-Island（SwiftUI） | 91f4991 | DynamicIsland/DynamicIslandView.swift |
| iOS16-Live-Activities | 07e4d29 | WidgetDemo/WidgetDemo.swift、iOS16-Live-Activities/ContentView.swift |

MioIsland 是 macOS notch（物理 notch + wings），只作为黑底活动/交互参考；iPhone 数值取自 Hello-Dynamic-Island 教程常量，真实机型会随屏宽变化。

## 1. 尺寸与比例

| 形态 | 宽 × 高 (DIP/pt) | 宽高比 | 圆角 | 内容 |
| --- | --- | --- | --- | --- |
| Dynamic Island 紧凑（Hello 教程） | 126 × 37.33 | 3.375 | 63（连续圆角；被高度裁成胶囊，有效半径 18.67 = h/2） | HStack；图标 controlSize(.large) 约 20–22；无显式 padding |
| Dynamic Island 展开（390 pt 屏） | 368 × 111 | 3.315 | 50（≈0.45h） | 整体 scale 0.5→1 |
| Live Activity 紧凑 leading/trailing | 系统胶囊；示例图标 28×28；计时 frame 40 | — | 系统胶囊 | caption2 ≈11 pt；外 padding 15，区域 padding 5 |
| FluidBar 紧凑 media | 226 × 40 | 5.65 | 20（h/2） | left 8 + artwork 28 + gap 9 + 文本 + gap 9 + wave 20 + right 10 |
| FluidBar 紧凑 notification | 248 × 36 | 6.89 | 18（h/2） | 文本列约从 x=40 起 |
| FluidBar 紧凑 agent | 236 × 36 | 6.56 | 18（h/2） | 同 notification 体系 |
| FluidBar 紧凑 clipboard | 264 × 36 | 7.33 | 18（h/2） | 同上 |
| FluidBar expanded | 440 × 284 | 1.55 | 28（0.099h） | 内容 inset 20/16；page body top 28 |

结论：
- 紧凑态高度 36–40 与 iPhone 37.33 对齐，保持。
- 紧凑宽度 5.6–7.3 比 Dynamic Island 3.3 细长；FluidBar 内容更多，不应硬压到 3.3。建议 Media 目标 4.2–4.8（168–192 宽），Notification/Clipboard 4.0–5.0，保留 token 自适应。
- Expanded 1.55 太方；Dynamic Island 是 3.3，但桌面多页面板不必复制。建议 440×220–240（1.8–2.0）或保留 284 只把圆角提到 40–44。
- 紧凑圆角 = h/2 是硬规则；展开圆角建议从 28 → 36–44（0.13–0.15h），更接近 iOS 的柔和转角。
- 紧凑 padding 水平 10–12（当前 8 偏紧）；封面/图标 20–24（当前 media 28/40=.70，iOS 约 .5–.6h）；展开 20/16 基本正确，卡片间距 12。
- 字号换算：iOS pt = FluidBar DIP；所有字号取 0.5 DIP 倍数，避免 8.5/9.5 过密值。

## 2. 材质与层次

参考做法：
- Hello-Dynamic-Island：容器 .fill(.black)，无渐变、无描边，层级全由内容/白色/彩色承担。
- MioIsland Classic：background #0A0A0B、overlay #191919、border #2A2A2C；primary #EDEDEE、secondary #C7C7CB、muted #8B8B90；状态色只用于状态点/语义。
- Telegram：容器/卡片用系统 material；pressed 才加高亮；普通态不加 accent 边框。
- Live Activity：黑色/系统材质容器；green/red/accent 只用于 delivered/cancel/contact 等内容。

| 层 | 用途 | 建议 |
| --- | --- | --- |
| L0 外容器 | island/展开面板 | #000000 或 #08080A；0.96–1.00；无渐变/accent 边框 |
| L1 分区/卡片 | 展开页卡片、列表行组 | #141418，55–65%；圆角 12；不要每行填色 |
| L2 分隔线 | 行间/卡片内分区 | 白 8–11%，1 DIP；只在需要分组时出现 |
| L3 顶部微高光 | 面板/卡片上缘 | 白 4–6%，1–2 DIP 渐变到透明 |
| L4 内阴影 | 按压/卡片 inset | 黑 15–20%，top inset 6–10 DIP |
| 内容色 | 图标/进度/状态/真实封面 | 允许；只在内容层 |
| 容器色 | 背景/边框 | 禁止 accent；只用黑/白/灰 |

FluidBar 现状：Background #08080A、Raised #141418、Inset #0E0E11、Line white 8%、LineStrong white 11%、Secondary #9B9BA5。方向正确；差距：缺少 L3 高光/L4 内阴影；行间缺少 8–10% 分隔；外容器不是纯黑。建议外容器 #000000、卡片 #141418 55–60%，行组用留白/分隔线，不给容器 accent。
对比度（#08080A）：#EDEDEE ≈17:1；#9B9BA5 ≈8:1；#6F6F78 ≈4.2:1（仅非关键 meta）。

## 3. 字体排印

iOS 系统标尺（SF Pro）：

| 样式 | 字号/行高 pt | 字重 |
| --- | --- | --- |
| Large Title | 34/41 | Regular |
| Title 1 | 28/34 | Regular |
| Title 2 | 22/28 | Regular |
| Title 3 | 20/25 | Regular |
| Headline | 17/22 | Semibold |
| Body | 17/22 | Regular |
| Callout | 16/21 | Regular |
| Subheadline | 15/20 | Regular |
| Footnote | 13/18 | Regular |
| Caption 1 | 12/16 | Regular |
| Caption 2 | 11/13 | Regular |

字距：17 pt 约 0；12–13 pt 约 +0.1；34 pt 约 −0.4；全大写小标签 +0.5–1.0。行高 1.20–1.30×字号。
Telegram 实测：按钮 15 semibold / badge 11；BottomButtonPanel 18 semibold + 11；Alert 15 semibold（小 14），lineSpacing 0.1–0.2；Camera 14 medium 全大写 kern −0.5（仅相机 overlay）；Live Activity expanded title3 20 / title2 22 / caption 12，compact caption2 11。

FluidBar 当前 vs 建议：

| 角色 | 当前 DIP | 建议 DIP | 备注 |
| --- | --- | --- | --- |
| Compact title | 13.5 semibold | 14 semibold | 行高 1.25 |
| Compact media title | 13 semibold | 14 semibold | 统一 |
| Compact subtitle | 10 regular | 11 regular | 当前偏小 |
| Compact meta/caption | 10.5–11 | 11 | 正文不再 8–9 |
| Expanded PageTitle | 10 semibold | 13 semibold | 最大可读性缺口 |
| Expanded eyebrow | 8 semibold | 9 semibold +0.5 tracking | 仅短标签可大写 |
| Media title | 20 display semibold | 20 | 保持 |
| Media artist | 11 | 13 | 偏小 |
| Media lyric | 13 medium | 13.5–14 medium | 行高 1.25 |
| Row title | 10.5 semibold | 13 semibold | 列表主行 |
| Row body | 9.5 | 11.5 | 正文至少 11 |
| Row meta | 8 | 10 | 时间/来源 |
| Button label | 11 semibold | 12 semibold | 触控区 ≥28 |
| Badge | 9 | 10 semibold | tabular |

规则：标题/副标题字号比 1.25–1.4，字重 600/400，颜色 #EDEDEE/#9B9BA5；大写只用于 8–10 DIP eyebrow；compact 标题 1 行、副标题 1 行；expanded 卡片标题 1 行、正文 1–2 行；数字 tabular；行高 round(1.25×size×2)/2。

## 4. 动效语言

参考：
- Telegram：custom spring mass 5/stiffness 900/damping 100；iOS26 mass 1/stiffness 555.027/damping 47.118（0.3832s）；fallback cubic-bezier(.380,.700,.125,1)；bounce duration×1.25, damping 88, stiffness 750；interactive 完成 max(.05,.2,distance/velocity) 从当前 presentation value 续接；list spring .5s + per-item beginAt；context delay .12、scale=max(.7,(width−15)/width)。
- MioIsland：notch open/close easeOut .25s，boot 1.0s；activity .smooth；press .94–.96, spring .2/.6, release .3/.6；chat .35/.85；content enter opacity+scale(.95–.98)+offset，exit 只 opacity。
- Hello Dynamic Island：interactiveSpring response .6/damping .6/blend .6；content opacity 0↔1、scale .5↔1 anchor top；1s 后收起。

FluidBar 建议 vs 现状：

| 动效 | 建议 | 当前 | 差距 |
| --- | --- | --- | --- |
| 展开容器 | T .40/ζ .82；高 .42/.86 | Expand .36/.82 / Height .39/.86 | 已接近 |
| 内容跟随 | 延迟 60ms，T .30/.80 | 60ms stagger | 已落地 |
| Chrome | 延迟 120ms | 120ms | 已落地 |
| 收起 | T .28–.30/ζ .95；chrome→content→shell | Collapse .30/1.0 | 已接近 |
| 按压 | T .18/.65；scale .96；亮度 6%；内阴影 18% | Press .18/.65 + .96/6%/18% | 已落地 |
| 活动切换 | T .30/ζ .85；位移 16–24 DIP + scale .98；stagger 40ms | 页面有 1.8% scale/4% alpha；活动 slide 未统一 | 补活动卡进入位移 |
| 多活动堆叠 | T .28/ζ .80；每层 8 DIP；stagger 40ms | Split .46/.68；stack .28/.80 | Split 较软，可降 .42/.72 |
| Live Activity 数字 | .25–.35s ease-out，tabular；不弹跳 | 基本即时 | 增加数字 transition |
| 可打断 | 从当前 visual value 续接，不归零 | Pager Interrupt 已做；spring 保留当前值 | 新动效必须复用 |
| Reduced motion | snap/仅 fade | MotionProfile 已有 | 新动效必须检查 |

## 5. Live Activity / 小组件形态密度

| 形态 | 结构 | 内容 | 字号/图标 | 留白 |
| --- | --- | --- | --- | --- |
| DI 紧凑 leading | 系统胶囊左半 | 图标 + 一个短标签（2 Pizza） | caption2 11；图标 16–20 | 外 15 / 内 5 |
| DI 紧凑 trailing | 右半 | 一个数字/计时器，frame 40 | caption2 11，monospacedDigit | 同左 |
| DI minimal | 最窄 | 一个图标 + 一个值 | caption2 | 不放标题 |
| Expanded leading | 区域 | 图标 + 标题 | title3 20 | 外 15 / 内 5 |
| Expanded trailing | 区域 | 计时/完成 | title2 22；计时 caption2 | 数字宽 50 |
| Expanded center | 区域 | 一行状态 + 进度条 | caption 12 | VStack spacing 5 |
| Expanded bottom | 区域 | 1–2 个按钮 | caption 12；按钮圆角 15 | padding 15 |
| 锁屏 widget | 64×64 logo + 文本 | headline + caption；图标 28/44 | 外 15 / 内 5 |

FluidBar 密度规则：紧凑最多 2 个文本 run + 1 trailing 值；圆形只放 1 图标/1 数字（直径 36–40，圆角=直径/2）；矩形/卡片最多 3 行，标题 13/正文 11.5/meta 10，padding 12，卡片间距 12；数字 tabular；正文超过 1 行截断不缩字号。

## 6. FluidBar 对照与差距清单

证据：build/reports/ui-audit/split/split-settled.png、split-circles-zoom.png；build/reports/ui-audit/motion/page-before.png、expand-before-mid-after.png。代码 token：LayoutTokens.cs、IslandMetrics.IslandType、CompactMediaLayout.cs、IslandTheme.cs。

| # | 影响 | 当前 | 参考 | 建议改法 | 预估 |
| --- | --- | --- | --- | --- | --- |
| 1 | 高 | PageTitle 10、RowTitle 10.5、RowBody 9.5、Meta 8 | iOS Title3 20/Headline 17/Body 17/Footnote 13（桌面可缩到 13/15/11/10） | 先改 IslandType：PageTitle 13、RowTitle 13、RowBody 11.5、RowMeta 10、Eyebrow 9+0.5 tracking；同步 PageMetrics 行高，防裁切 | S–M（0.5–1.5 天） |
| 2 | 高 | Media 226×40=5.65；Notification 248×36=6.89 | Dynamic Island 3.375 | Media 内容自适应 180–200；通知 220–228；不要同一条超宽 pill 承载所有 kind | M（1–2 天） |
| 3 | 高 | Expanded 440×284=1.55，radius 28（0.099h） | DI 3.3；iPhone 圆角约 0.4–0.45h | A 推荐：保持 440 宽，高 220–240；B：保留 284，radius 40–44；不要同时做 | M（1–2 天） |
| 4 | 中高 | 封面 28/40=.70；left 8/right 10 | 图标 0.5–0.6h；水平 10–12 | CompactMediaLayout：Artwork 24、Left 10、Gap 8（TextLeft 42 不变）、Right 12；封面 24 squircle | S（0.5 天） |
| 5 | 中高 | 缺 L3 高光/L4 内阴影；行间分隔不足 | Mio overlay/border；iOS 8–11% | IslandTheme 加 TopHighlight white 5%、InnerShadow black 18%；行间 10% separator；只边缘，不给容器 accent | S（0.5 天） |
| 6 | 中 | Compact subtitle 10；artist 11、album 9.5 | Subheadline 15；Footnote 13（桌面 11–13） | subtitle 11、artist 13、album 11、lyric 14；行高 1.25 | S（0.5 天） |
| 7 | 中 | 大量 8–9 DIP 全大写 RowApp/Eyebrow | 全大写只做小标签，+0.5–1.0 tracking | 只保留 eyebrow 9+tracking；其他 meta sentence case | S（0.5 天） |
| 8 | 中 | 容器/卡片均 #08080A/#141418，层次平；外容器非纯黑 | Hello 纯黑；Mio #0A0A0B + overlay #191919 | 外容器 #000000；卡片 #141418 55–60%；行组留白 8–12；不要 accent 边框 | S–M（0.5–1 天） |
| 9 | 中 | 展开页信息密度大；字号 8–10 | Live Activity 每区 1–2 行、12–20 | 每卡最多 3 行；标题 13/正文 11.5/meta 10；截断不缩字号；间距 12 | M（1–2 天） |
| 10 | 中 | Split .46/.68；活动切换无统一方向位移 | iOS 0.35–0.6；press .2/.6 | 保留 Press .18/.65；活动卡进入 16 DIP + stagger 40ms；数字 .3 ease-out；Split .42/.72 | S–M（0.5–1 天） |
| 11 | 低–中 | 部分时间/百分比非 tabular | Live Activity monospacedDigit | 时间/百分比/倒计时统一 Mono；数字更新不改变布局 | S（0.5 天） |
| 12 | 低 | PageTitle 10/页码 8.5 与内容比例失衡 | iOS 层级更高 | PageTitle 13、页码 9.5、eyebrow 9；标题左对齐 | S（0.5 天） |
| 13 | 低（已实施，有意偏离原型） | Rail 当前胶囊标签 **9 DIP**（原型 `.mini-label` 9 pt 的实测基线：胶囊 64/70/73、标签宽 18.188/24.406/27.271） | iOS caption2 ≈ 11 pt；rail 在 150 % DPI 下 9 pt 偏小，低于可读性下限 | **已实施**：`IslandMetrics.MiniLabelSize` 9 → **11**（对齐 `TypographyScale.RailLabel` 11/14）；胶囊宽度随标签自动增长（64→67、70→75、73→79），远点簇随之平移 1.5–3 DIP；`MiniRailPrototypeTests` 已迁移到 11 pt 基线并在文件头保留原型值（可读性优先，非随手改动） | 已做（2026-10-21） |

实施顺序：1（字体）→4（紧凑 padding/封面）→5（材质）→2/3（比例，需设计确认）→9（密度）→10/11（动效/数字）。1/4/5 低风险高收益；2/3 需同步 layout 测试与 golden，独立 PR。

验收方法：
1. 按 build/reports/ui-audit 现有图取样：外容器 #000000–#08080A；卡片 #141418；分隔 8–11%。
2. 文本测量：compact title/subtitle 字号比 ≥1.25；expanded PageTitle ≥12；RowBody ≥11；行高 ≥1.2×字号。
3. 动效帧：expand 0/60/120ms 三层出现；press scale .96、亮度 +6%、顶部内阴影可见；page 中途 scale 降 1.8% 且可连续打断。
4. 每改一项保留前后 PNG；不只给口述结论。
