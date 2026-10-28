# FluidBar 2.0 官方网站设计 spec（日光光学）

> 日期：2026-10-27
> 状态：已确认（用户选择方向 A；并追加“灵动岛必须与真实状态一致”的硬约束）
> 方向：A. 日光光学（Daylight Optics）
> 范围：`website/` 单页官网，中文本地化首发
> 事实来源：`README.md`、`docs/rewrite/`、`docs/rewrite/prototype2/`

## 1. 背景与决策

现有旧版官网已从工作区移除，不再作为视觉、排版或代码参考。新官网从空白 `website/` 开始重建。

已确认的核心决策：

1. 不做“简单深色黑曜石”官网。
2. 页面整体为冷银白亮色，深色只属于产品 UI。
3. 产品仍是唯一主角，首屏是真实可操作的小岛，不是产品截图。
4. 全站共用一套小岛状态机，首屏、滚动故事、外观配置器复用同一组件。
5. 使用 ReactBits 做 UI 与微交互，使用 Shaders 做材质实验台的渐进增强。
6. 不做虚假下载、虚假安装、虚假评分、虚假性能数据。

一句话概念：

> 把黑曜石还给产品，把页面交给光。

## 2. 目标与非目标

### 2.1 目标

- 让访客在 5 秒内理解：FluidBar 是 Windows 上常驻、可交互、可扩展的灵动岛。
- 让访客在首屏亲手体验“小岛 → 悬停预览 → 点击展开 → 切换页面 → 收起”。
- 用明亮、精密、有工程感的视觉，把 FluidBar 从“又一个暗色效率工具”提升为“值得安装的桌面物件”。
- 让普通用户看到下载入口，让插件开发者看到文档入口。
- 静态部署，无后端，可直接托管到 Cloudflare Pages。

### 2.2 非目标

- v1 不做英文完整版；中文为唯一站点语言。
- v1 不单独做 About 页；首屏和滚动故事承担“关于”叙事。
- v1 不做暗色模式切换；页面主题锁定为日光亮色。
- v1 不做账号、评论、在线安装、真实应用内市场。
- v1 不伪造安装包、评分、下载量、用户数或未经验证的性能数字。
- 不重写应用本体，不改 `src/` 与 `tests/` 内的 .NET 代码。

## 3. 受众与核心信息

### 3.1 主要受众

1. Windows 10/11 用户：想要更顺手的桌面状态与快捷控制。
2. 效率工具与美化用户：对桌面常驻组件、媒体控制、通知、剪贴板有需求。
3. 插件开发者与贡献者：关心 SDK、manifest、签名、市场流程。

### 3.2 核心信息优先级

1. 常驻，但不打扰。
2. 点击才展开，操作完自动收起。
3. 六页控制中心：主页、媒体、通知、剪贴板、Agent、小组件。
4. 三种形态、三种材质、可自定义。
5. 插件生态与开源。
6. 性能与工程质量。

## 4. 设计概念

### 4.1 视觉隐喻

页面是一间被冷日光照射的浅色工作室，产品是一扇嵌在页面里的深色桌面窗（488 × 444）。窗内是真实 prototype2 桌面场景与灵动岛，窗外是亮色排版、尺寸标注和留白。

- 页面底色是亮色，不是暗色。
- 深色只出现在产品窗、产品 UI 与主按钮，不出现整页暗色区块。
- 小岛与桌面窗使用真实 prototype2 渲染，不重画。
- 光影不是装饰，是产品窗与材质的证据。
- 版式接近瑞士编辑设计 + 工程标本，不接近 Apple 居中英雄页。
- 用尺寸标注、细线、错位留白制造“被设计过”的感觉。

### 4.2 设计原则

1. **光在动，东西不乱动。** 主要动效是产品窗周围的光、阴影与页面排版，不是漂浮卡片。
2. **深色只属于产品。** 页面、导航、说明文字都在亮色系统里；深色产品窗是唯一大面积暗色区域。
3. **真实优先。** 直接嵌入 prototype2 的真实岛，不重画状态；下载、性能、插件信息全部真实。
4. **少即是贵。** 一个强调色、一套圆角、一套字体、最多一次大型网格。
5. **可读性优先。** 大标题不牺牲正文对比度，动效不阻碍操作。
6. **优雅降级。** 没 WebGPU、没 WebGL、减少动效、窄屏，页面都必须是完整可用的。

## 5. 信息架构

单页，锚点导航：

1. `#experience` 首屏：桌面之上，多一座岛。
2. `#story` 岛的故事：常驻、活动态、展开、切换、退场。
3. `#appearance` 外观：形态、材质、强调色配置器。与 `#story` 共用同一个真实岛实例和 sticky 舞台。
4. `#plugins` 插件生态。
5. `#performance` 性能与架构。
6. `#download` 下载。
7. 页脚：GitHub、Releases、插件文档、问题反馈、MIT License、组件 credits。

导航文案：

- 体验
- 外观
- 插件
- 下载
- GitHub（外部链接）

主导航高度 72px，桌面单行，移动端折叠。全站主 CTA 只使用一个主文案：**下载预览版**。

## 6. 逐区规格

### 6.1 全局框架

- 页面最大内容宽 1320px，12 列网格，gutter 24px。
- 桌面左右留白 48px，移动端 24px。
- 区块垂直间距：桌面 144px，移动端 88px。
- 页面背景 `#F2F3F4`，顶部偏右有极淡的冷色光池。
- 全站覆盖 1.5% 不透明度的静态颗粒。
- 使用粘性顶部导航；滚动后导航保持 72px 高，不缩小成第二条导航。

### 6.2 首屏 Hero

布局：

- 桌面 5/7 分栏。
- 左列：H1、副文案、两个 CTA。
- 右列：488 × 444 的真实深色产品窗，窗内是 prototype2 桌面场景与灵动岛，窗外是尺寸标注、细线和窗影。
- 移动端：H1、副文案、CTA 在上，产品窗在下，仍保证 CTA 在首屏内。

文案：

- H1：`桌面之上，多一座岛。`
- 副文案：`常驻，但不打扰。媒体、通知、剪贴板与系统控制，都在一枚小岛里。`
- 主 CTA：`下载预览版`
- 次 CTA：`先玩一下`

视觉：

- H1 两行，88 到 120px，黑色，紧字距。
- 产品窗是 488 × 444 的深色桌面窗，圆角 28px，1px 冷色描边，冷灰长阴影。
- 产品窗内部使用真实 prototype2 场景：壁纸、任务栏、灵动岛，不画假浏览器边框。
- 首屏默认展示真实 expanded 440×284 控制中心，立刻有信息密度；点击岛内 chevron 或按 Esc 收起为 132×36，再点击小岛可重新展开。
- 产品窗右侧有一列真实页面索引，直接驱动 prototype2 的 pager；首页/媒体/通知/剪贴板/Agent/小组件都可点。
- 产品窗外围使用 1px 细线和 mono 尺寸标注：`488 × 444`、`132 × 36`、`440 × 284`。
- 产品窗右上或左上有一束极淡的冷光，不是渐变按钮式光晕。
- 产品窗使用真实 iframe 嵌入；加载完成前显示同状态静态 poster，避免 CLS。

交互：

- 首屏默认是真实 expanded 状态；点击岛内 chevron 或按 Esc 收起。
- 悬停小岛：仅按原型缩放 1.022，不切换出一个并不存在的“预览态”。
- 收起后点击小岛：重新展开到 440×284 控制中心。
- 点击岛外或按 Esc：收起。
- 右侧页面索引与方向键、数字键 1 到 6 都切换真实 pager。
- 鼠标移动：产品窗外的光与阴影偏移 2 到 6px，产品窗内保持原型行为。
- 主 CTA 链接到 GitHub Releases。
- 次 CTA 平滑滚动到 `#story`。
- 首屏右上角显示真实状态标签：`LIVE PROTOTYPE · 真实状态`，不写虚构卖点。

首屏限制：

- 1440×900 与 1280×800 下，两个 CTA 必须在首屏内可见，不要求滚动。
- 首屏文字元素不超过四类：H1、副文案、两个 CTA。
- 不使用“向下滚动”提示、版本徽章、用户数量、logo 墙。

### 6.3 岛的故事

布局：

- 桌面：左侧 4 列步骤列表，右侧 8 列 sticky 产品窗（488 × 444）。
- 产品窗是真实 prototype2 iframe，不是 React 重画的岛；舞台随滚动保持 sticky。
- 左侧步骤可点击跳转；当前步骤高亮。
- 故事与外观配置器共用同一个 iframe 和同一套状态协议，岛在整段叙事中不重建。
- 移动端：取消 sticky，产品窗在上，步骤按钮在下；点击步骤切换真实状态。

五个步骤：

1. **常驻**
   - 文案：`默认只有 132 × 36。不抢窗口，不打断输入。`
   - 状态：idle，真实尺寸 132 × 36。
2. **活动态**
   - 文案：`媒体、通知、剪贴板、Agent 到来时，岛会按真实活动态变长。`
   - 状态：依次展示真实 compact 状态：media 226 × 40、notifications 248 × 36、clipboard 264 × 36、agent 236 × 36。可在步骤内切换。
3. **展开**
   - 文案：`点击，变成 440 × 284 的控制中心。`
   - 状态：expanded，显示主页。
4. **切换**
   - 文案：`六个页面。拖动、滚轮，或者按 1 到 6。`
   - 状态：expanded，显示六页切换控件。
5. **退场**
   - 文案：`操作完，安静收起。`
   - 状态：collapsed / idle。

六页能力在步骤 4 中完整呈现：

- 主页：时间、媒体、电源与快捷开关。
- 媒体：封面、歌词、进度、播放控制。
- 通知：应用、标题、正文、时间。
- 剪贴板：文本、图片、文件历史。
- Agent：任务状态与项目。
- 小组件：天气、日历、任务。

交互与可访问性：

- 滚动只驱动视觉状态，不锁定页面滚动，不劫持滚轮。
- 用户直接操作岛时，当前步骤同步更新。
- 每个步骤是真实按钮，支持 Tab 与 Enter/Space。
- `prefers-reduced-motion` 下取消 sticky 变形，退化为静态步骤切换。

### 6.4 外观配置器

布局：

- 桌面：左侧 4 列控制面板，右侧 8 列真实产品窗 + 实时小岛，与故事区共用同一个 iframe。
- 移动端：产品窗在前，控制面板在后。
- 控制项全部可见，不把选项藏进 hover。

控制项：

- 形态：浮岛 / 贴顶 / Dock。
- 材质：黑曜石 / 亚克力 / 液态玻璃。
- 强调色：青 / 薄荷 / 琥珀 / 玫红 / 钴蓝。

行为：

- 形态切换通过 postMessage 驱动真实 prototype2，岛在窗内移动、贴顶或下移到 Dock 上方。
- 材质切换通过真实原型 CSS 完成，保留 180ms 交叉淡入。
- Dock 模式显示 prototype2 自己的真实六应用 Dock，不使用 ReactBits Dock 另画一份。
- 所有控件有可读标签、`aria-pressed` 或等效状态。
- 页面注明：`网页中的材质为视觉近似，实际效果由 Skia 渲染器绘制。`

### 6.5 材质实验台

位置：`#appearance` 区块的下半部分，位于配置器控件与产品舞台之后，作为独立子区块。

内容：

- 三种材质并列：黑曜石、亚克力、液态玻璃。
- 黑曜石与液态玻璃使用 Shaders 的 `Obsidian` / `Glass` 渲染光学标本，合并在同一个 Shader 画布中。
- 亚克力使用与产品一致的 CSS 近似，作为不使用 WebGPU 的对照。
- 浅色图片或浅色文字作为折射背景，展示真实透镜边缘、焦散与轻微色散。
- 旁边是真实的 CSS 产品小岛作为对照。
- 文案：`同一枚岛，三种材质。`
- 说明：`实验台为 WebGPU 光学演示；产品界面由 DOM 与 CSS 渲染。`

渐进增强：

- Shaders 只在进入视口后加载。
- WebGPU 不可用时显示静态材质海报，不影响任何操作。
- 移动端与减少动效模式直接显示静态海报。
- 同一时间只允许一个重型 GPU 画布活动。

### 6.6 插件生态

布局：

- 桌面 7/5 分栏。
- 左侧：6 个内置插件卡片。
- 右侧：代码面板，展示真实 manifest 片段。

插件：

- 媒体
- 通知
- 剪贴板
- Agent
- 设备
- 小组件

行为：

- 悬停卡片出现低对比聚光与 1px 边缘高光。
- 点击卡片，右侧切换到对应能力与权限说明。
- 代码面板显示 `manifest.json` 示例，提供复制按钮。
- 只展示真实能力描述，不显示虚假的下载量或评分。
- CTA：`阅读插件文档`，链接到 GitHub 上的 `docs/plugin/quickstart.md`。
- 补充信息：签名 `.fbp` 包、应用内市场、安装/更新/回滚。

### 6.7 性能与架构

布局：

- 上方大标题与一段说明。
- 下方 4 到 5 个等宽数字。
- 右侧或下方四层架构图。

文案：

- H2：`丝滑不是形容词，是预算。`
- 副文案：`固定窗口、合成层动画、有界缓存。每一个数字都来自实测。`

数字（来源：`docs/rewrite/STATUS.md` 与发布说明，页面脚注注明测量环境与日期）：

- 单帧渲染：0.61 ms
- p99：0.85 ms
- 动画预算：120 fps
- 每帧分配：0 B
- 自动化测试：2,600+

架构四层：

1. Win32 分层宿主
2. Skia 渲染器
3. Core 状态机
4. Plugin SDK

交互：

- 悬停或键盘聚焦一层，显示一句解释。
- CountUp 只在首次进入视口时播放一次。
- 减少动效时直接显示最终数字。

### 6.8 下载

布局：

- 浅色大台座或留白舞台，中央是品牌符号与小岛图形。
- H2：`现在，把岛带走。`
- 主 CTA：`下载预览版`，链接到 GitHub Releases。
- 次 CTA：`查看源代码`，链接到 GitHub 仓库。
- 系统要求：Windows 10 / 11 x64，.NET 10 Desktop Runtime。
- 状态：当前为开发预览版，功能仍在变化。
- 提供 release notes 链接。
- 不托管安装包，不伪造版本号或 SHA。

### 6.9 页脚

- 品牌符号 + FluidBar。
- 一句话：`常驻，但不打扰。`
- 链接：GitHub、Releases、插件文档、问题反馈、完整交互原型、MIT License。
- Credits：ReactBits、Shaders、Motion。
- 版权：`© 2026 Doulor`。
- 不放装饰性版本号、构建号、城市时间或天气条。

## 7. 视觉系统

### 7.1 色彩 token

| Token | 值 | 用途 |
|---|---|---|
| `--bg` | `#F2F3F4` | 页面底色 |
| `--surface` | `#FAFBFC` | 产品台面、少量浮层 |
| `--ink` | `#0B0D10` | 主文字 |
| `--muted` | `#5A636C` | 次级文字 |
| `--line` | `rgba(11,13,16,0.12)` | 细线 |
| `--line-strong` | `rgba(11,13,16,0.22)` | 强调分隔 |
| `--accent` | `#2447FF` | 站点强调色、焦点环、当前状态 |
| `--accent-soft` | `rgba(36,71,255,0.08)` | 强调底色 |
| `--shadow` | `0 40px 80px -28px rgba(24,35,50,0.18)` | 产品投影 |
| `--island` | `#08080A` | 产品岛底色 |
| `--island-raised` | `#141418` | 岛内浮层 |
| `--island-accent` | `#4CC9F0` | 岛内强调色，仅属于产品 |

规则：

- 站点外部只使用一个强调色钴蓝。
- 产品内部的青色、绿色、琥珀色、红色是产品语义色，不参与官网品牌配色。
- 不使用紫色、彩虹渐变、彩色光晕。
- 所有阴影带冷蓝色调，不用纯黑。

### 7.2 字体

| 用途 | 字体 | 字重 |
|---|---|---|
| 中文正文与标题 | Noto Sans SC | 400 / 500 / 700 |
| 拉丁与数字 | Satoshi | 400 / 500 / 700 |
| 等宽标注 | IBM Plex Mono | 400 / 500 |

字体处理：

- 自托管 woff2。
- 中文按实际用字做子集，避免全量字体。
- `font-display: swap`。
- 回退栈：`"Satoshi", "Noto Sans SC", "Microsoft YaHei UI", sans-serif`。
- 许可：Satoshi 按 Fontshare 许可自托管；Noto Sans SC 与 IBM Plex Mono 使用 OFL。
- 不使用 Inter、不使用衬线、不使用渐变文字。

字号：

| 层级 | 桌面 | 移动 |
|---|---|---|
| H1 | clamp(88px, 7.2vw, 120px) | clamp(44px, 12vw, 58px) |
| H2 | clamp(44px, 5.2vw, 72px) | clamp(34px, 9vw, 44px) |
| H3 | 28 到 36px | 24 到 30px |
| 正文 | 17 到 18px | 15 到 16px |
| 小字 | 13 到 14px | 13px |
| 标签 | 11 到 12px mono | 10 到 11px mono |

- 中文标题 `letter-spacing: -0.04em` 到 `-0.05em`，行高 1.05 到 1.12。
- 正文行高 1.7。
- 数字使用 tabular numerals。

### 7.3 形状与材质

- 药丸：`999px`，用于小岛、胶囊按钮、chip。
- 面板：`28px`。
- 卡片：`20px`。
- 输入与小控件：`12px`。
- 细线统一 1px。
- 玻璃只用于导航、小岛、少量浮层，不铺满页面。
- 不使用“每个卡片都是毛玻璃”的模板。
- 产品台面用纸感哑光，不用高光塑料。

### 7.4 布局节奏

- 首屏 5/7。
- 故事 4/8。
- 插件 7/5。
- 外观配置器整幅大台面。
- 性能区用横向规格表与四层图，不用卡片矩阵。
- 全站最多一处 bento 式网格。
- 避免三段式、三卡片式重复。

## 8. 动效与交互系统

### 8.1 动效原则

- 动效必须有理由：层级、状态变化、反馈、讲故事。
- 页面主运动是光，不是物体。
- 小岛形变使用弹簧或高阻尼曲线，不使用线性动画。
- 所有大段动画必须支持 `prefers-reduced-motion`。
- 不使用 GSAP；统一 Motion。
- 不混用 GSAP、Three.js、多个重型动画引擎。
- 不使用自定义光标、全局粒子、故障文字、无限 marquee。

### 8.2 具体参数

- 小岛展开/收起：380ms 高阻尼弹簧。
- 材质切换：180ms 交叉淡入。
- 页面切换：跟随指针/滚轮，吸附阻尼。
- 悬停高光：120 到 180ms。
- 滚动揭示：16 到 24px 位移 + 轻微透明度，不逐段做浮夸视差。
- 光源跟随：使用 Motion values，不使用 `useState` 驱动连续值。
- CountUp：进入视口播放一次，400 到 900ms。

### 8.3 键盘与屏幕阅读器

- 小岛是 `role="group"`，可聚焦，带 `aria-expanded`。
- 页面切换支持数字键 1 到 6、左右方向键、Home、End。
- Esc 收起。
- 所有按钮有真实文本或 `aria-label`。
- 状态变化通过 `aria-live="polite"` 播报。
- 焦点环使用钴蓝 2px，偏移 3px。
- 完整交互原型链接可键盘访问。

## 9. ReactBits 与 Shaders 使用规范

### 9.1 ReactBits

通过 shadcn/jsrepo 源码安装，使用 `TS-CSS` 版本，不使用 Tailwind 变体。

只使用以下 6 个：

1. `PillNav`：顶部导航，浅色主题。
2. `SpecularButton`：主下载 CTA，黑色底，细高光。
3. `SpotlightCard`：插件卡片。
4. `BorderGlow`：插件代码面板或单张精选卡片的边缘光。
5. `Noise`：全站颗粒。
6. `CountUp`：性能数字。

不使用 ReactBits `Dock`，因为 Dock 必须来自真实 prototype2，避免出现第二个与产品不一致的 Dock。

约束：

- 每个引入的组件保留 MIT + Commons Clause 许可说明。
- 不使用 `SplitText` 等依赖 GSAP 的组件。
- 不使用 MagicBento、粒子背景、故障文字、Sticky Stack、全屏 3D。
- `SpecularButton` 在没有 WebGL2 时必须显示完整可用的 CSS 回退按钮。
- 连续值动效使用 Motion value，不用 `useState`。

### 9.2 Shaders

使用 `shaders` npm 包，MIT。

只用于材质实验台：

- `Glass`：表现液态玻璃的透镜、折射、边缘高光。
- `Obsidian`：表现黑曜石的近黑本体与斜切面虹彩。
- `RoundedRect` 或等效形状：药丸轮廓。

约束：

- WebGPU 只做装饰，不承载文字、按钮或任何必要信息。
- 进入视口后懒加载，离开视口暂停或卸载。
- DPR 上限 1.5，移动端关闭。
- WebGPU 不可用时显示静态海报。
- 减少动效时显示静态海报。
- 同一时间只运行一个重型 GPU 画布。
- 不在产品小岛内部使用 WebGPU 替代 DOM 渲染。

## 10. 技术方案

### 10.1 栈

- Vite 7
- React 19
- TypeScript 5.7+
- Motion（`motion/react`）
- 原生 CSS + CSS Modules + 设计 token
- ReactBits 源码组件（TS-CSS）
- `shaders`（懒加载）
- `@phosphor-icons/react` 站点图标
- Playwright + axe 做验收

不使用：

- Next.js / SSR / RSC
- Tailwind
- GSAP
- Three.js
- 任何后端服务
- 真实支付、账号、安装器

### 10.2 目录结构

```text
website/
  package.json
  pnpm-lock.yaml
  vite.config.ts
  tsconfig.json
  index.html
  public/
    favicon.svg
    og-image.png
    island/              # 由脚本从 docs/rewrite/prototype2 同步并补 embed 壳
      embed.html
      embed.css
      embed-bridge.js
      app.js
      icons.js
      styles.css
    posters/             # 真实状态的干净 poster，用于 iframe 加载前
  src/
    main.tsx
    App.tsx
    styles/
      tokens.css
      base.css
    sections/
      SiteNav.tsx
      Hero.tsx
      IslandLab.tsx      # 故事 + 外观配置器共用一个真实岛 iframe
      MaterialOptics.tsx
      Plugins.tsx
      Performance.tsx
      Download.tsx
      SiteFooter.tsx
    island/
      PrototypeFrame.tsx # 受控 iframe 外壳 + 缩放 + poster
      usePrototype.ts    # 状态镜像与控制协议
      protocol.ts        # postMessage 类型与常量
      compactStates.ts   # 真实 compact 尺寸与标签
    ui/
      PillNav.tsx
      SpecularButton.tsx
      SpotlightCard.tsx
      BorderGlow.tsx
      Noise.tsx
      CountUp.tsx
      MaterialSpecimen.tsx
    lib/
      useReducedMotion.ts
      useWebGPU.ts
      useInViewOnce.ts
  scripts/
    sync-island.mjs
  tests/
    site.spec.ts
    a11y.spec.ts
    island-protocol.spec.ts
```

### 10.3 真实岛嵌入与状态协议

官网不重画岛。故事区与外观配置器共用一个 `public/island/embed.html`，它加载真实 prototype2 的 `app.js` 与 `styles.css`，只额外注入：

- `embed.css`：隐藏调试控制台、桌面图标等非产品元素，保留壁纸、任务栏、岛和 Dock。
- `embed-bridge.js`：同源 `postMessage` 桥，负责把父页面指令转成原型内的真实点击/键盘事件，并把真实 DOM 状态回传。

父页面 `usePrototype` 镜像以下状态：

- `ready`: iframe 是否完成启动
- `expanded`: 是否展开
- `view`: idle / media / notifications / clipboard / agent / timer / battery / hud
- `page`: 0 到 5
- `mode`: float / flush / dock
- `material`: obsidian / acrylic / liquid
- `accent`: 当前强调色
- `width` / `height`: 真实 DOM 尺寸，用于调试与验收

协议：

- 父 → iframe：`{ type: 'fluidbar:set', state?, page?, mode?, material?, accent?, motion? }`
- iframe → 父：`{ type: 'fluidbar:state', state: { ... } }`
- 只接受同源 iframe 的 `event.source`，并校验 `event.origin`。
- 父页面不直接操作 iframe DOM，不复制原型内部状态机。

真实 compact 状态与尺寸：

| view | 尺寸 |
|---|---|
| idle | 132 × 36 |
| media | 226 × 40 |
| notifications | 248 × 36 |
| clipboard | 264 × 36 |
| agent | 236 × 36 |
| timer | 190 × 36 |
| battery | 208 × 36 |
| hud | 208 × 40 |

展开态固定 440 × 284；产品宿主窗固定 488 × 444。悬停只按原型缩放 1.022，不产生额外的“预览态”。

首屏使用独立的第二个真实实例，保证用户先玩到；故事区与配置器使用同一个 lab 实例，滚动过程中不重建。

### 10.4 资产与事实

- 旧官网截图全部退役，不使用带调试控制台的图片。
- 产品视觉以 `docs/rewrite/prototype2` 为唯一参考。
- 产品窗口内是真实 prototype2 DOM，不是截图，也不是重画的岛。
- prototype2 使用示例媒体、通知、剪贴板与 Agent 数据；页面在演示区标注“示例数据”，不冒充用户真实数据。
- 如需截图 poster，使用 Playwright 隐藏控制台后重新截取，并保留真实状态。
- 下载链接指向 GitHub Releases，不复制安装包。
- 插件 manifest 示例基于 `docs/plugin/` 与 `schemas/`。
- 性能数字带来源脚注和日期。
- 网站中的产品状态必须来自真实原型或真实应用，不编造不存在的页面、尺寸或交互。

### 10.5 SEO 与分享

- `title`：`FluidBar 2.0 · Windows 上的灵动岛`。
- `description`：一句真实产品描述，不使用夸大词。
- `lang="zh-CN"`。
- 提供 canonical、OG image、Twitter card。
- 使用 `SoftwareApplication` JSON-LD，字段只填真实信息：名称、操作系统、许可证、下载页、截图。
- 不写虚构评分、评论数、价格或用户数。
- 所有分享图由真实产品状态生成，不使用旧截图。

### 10.6 部署

- 构建输出 `website/dist`，是可直接部署的纯静态目录。
- 目标托管：Cloudflare Pages（Git 集成或 `wrangler pages deploy`）。
- Vite `base` 固定为根路径 `/`；Cloudflare Pages 项目根目录为 `website`，输出目录为 `dist`。
- `SITE_URL` 通过构建环境变量注入，用于 canonical、OG、JSON-LD；默认 `https://fluidbar.pages.dev`。
- `public/_headers` 设置静态缓存与基础安全头；`public/robots.txt` 允许抓取。
- `wrangler.toml` 配置 `pages_build_output_dir = "dist"`。
- 当前自定义域名 `fluidbar.doulor.cn` 由 `fluidbar-site` Worker 路由代理到 Pages；原因是该域名已有 A 记录，Pages 自定义域名验证要求 CNAME。Worker 配置独立在 `wrangler.worker.toml`，不改变 Pages 部署流程。
- 所有仓库内文档链接使用 GitHub 绝对 URL，不依赖部署域名。
- `pnpm build` 与 `pnpm test:e2e` 在 CI 中运行。

## 11. 可访问性与性能

### 11.1 可访问性

- 目标 WCAG 2.2 AA。
- 正文与背景对比度 ≥ 4.5:1，大标题 ≥ 3:1。
- 所有交互可用键盘完成。
- 不使用颜色作为唯一状态表达。
- 提供跳过导航链接。
- 所有动效支持减少动效。
- 焦点可见，不被 sticky 舞台遮挡。
- 视频/动图类内容提供静态替代。
- axe 无 serious 或 critical 问题。

### 11.2 性能预算

- 首屏 JS ≤ 240KB gzip，不含 iframe 内的原型脚本与懒加载 Shaders。
- 首屏 iframe 使用 poster 占位，`requestIdleCallback` 后再加载原型；CTA 与正文不等待 iframe。
- 故事区 lab iframe 进入视口前 600px 才加载；整站最多两个原型实例，不创建第三个。
- Shaders / 材质实验台 chunk 单独懒加载。
- 字体 ≤ 450KB（中文子集 + 拉丁 + mono）。
- 首屏图片 ≤ 250KB；优先 SVG、CSS 与实时 DOM。
- LCP < 2.0s（本地生产构建，桌面宽带）。
- CLS < 0.05。
- INP < 200ms。
- Shader 画布 DPR 上限 1.5，离屏暂停。
- `prefers-reduced-motion` 下不初始化连续动画，并把嵌入实例切到 motion=reduced。

## 12. 内容规则

- 全站中文，语气克制、具体、不堆形容词。
- 不使用 em dash。
- 不使用“颠覆、革命、极致、无缝、赋能”等空词。
- 不虚构用户数、评分、下载量、兼容机型和测试结果。
- 性能数字必须带来源与日期。
- 不把“能力披露”说成“沙箱”。
- 不把“预览版”说成“正式版”。
- 不把插件市场说成已大规模上线。
- 所有外部链接必须可达，并带 `rel="noopener noreferrer"`。

## 13. 测试与验收

### 13.1 自动化

- `pnpm build` 通过。
- Playwright：
  - 首屏加载无 console error / pageerror。
  - 导航锚点可用。
  - 小岛可展开、收起、Esc 关闭。
  - 数字键 1 到 6 切换页面。
  - 形态、材质、强调色切换生效。
  - 插件卡片点击切换详情。
  - 移动端菜单可开合，无横向溢出。
  - 减少动效模式无粘性滚动动画。
  - 模拟无 WebGPU 时材质实验台显示静态海报。
- axe：无 serious 或 critical。
- 外部链接检查。

### 13.2 视觉验收

- 1440×900：首屏 CTA 可见，无溢出，H1 两行。
- 1280×800：首屏 CTA 可见，导航单行。
- 390×844：无横向滚动，产品舞台不裁切关键信息。
- 全站无暗色区块，深色只出现在产品 UI 与主按钮。
- 全站只有一个站点强调色。
- 亮点：阴影、焦散、材质边缘不是贴图，是实时 CSS 或 Shader。
- 无带调试控制台的产品截图。
- 无模板化三卡片、无渐变文字、无紫色光晕。

### 13.3 内容验收

- 所有产品能力与 `README.md` / `docs/rewrite/` 一致。
- 下载、插件文档、GitHub、License 链接可达。
- 版本状态与发布说明一致。
- 性能数字标注来源与日期。

## 14. 实施阶段

1. **基础工程**：Vite + React + TS + 字体 + token + 导航 + 页面骨架。
2. **首屏**：Hero + IslandDemo 共用状态机 + 光影系统。
3. **叙事**：岛的故事 sticky 分区 + 六页能力。
4. **外观**：配置器 + 三种形态 + 三种材质 + 强调色。
5. **信息区**：插件、性能、下载、页脚。
6. **增强**：ReactBits 六个组件接入、Shaders 材质实验台、降级。
7. **验收**：响应式、可访问性、性能、测试、部署、文档更新。

## 15. 风险与回退

| 风险 | 影响 | 回退 |
|---|---|---|
| WebGPU 支持不足或初始化失败 | 材质实验台空白 | 静态海报 + CSS 焦散，不影响主流程 |
| 浅色主题对比度不足 | 可读性差 | 加深正文与次级文字，减少浅灰层级 |
| 实时小岛与原型不一致 | 产品失真 | 以 prototype2 为基准做逐状态视觉对照 |
| SpecularButton 无 WebGL2 | CTA 失效 | 保留完整 CSS 按钮回退，功能不受影响 |
| Shaders 与 ReactBits 同时占 GPU | 掉帧 | 同一时间只挂载一个重型画布，离屏卸载 |
| iframe 桥接状态漂移 | 网页与产品不一致 | 以 iframe 回传的真实 DOM 状态为唯一真值，父层只镜像 |
| iframe 内原型脚本失败 | 产品窗空白 | 保留真实状态 poster，并显示打开完整原型的外链，不伪造交互 |
| 中文字体过大 | LCP 变差 | 子集化 + woff2 + 系统回退 |
| 自定义域名 / 部署域名变化 | canonical、OG 指错 | 构建时设置 `SITE_URL`，不改运行逻辑 |
| 静态托管根路径变化 | 资源 404 | Vite `base` 固定 `/`；CF Pages 从站点根目录提供 `dist` |
| 旧站已删除 | 无可复用视觉资产 | 从 prototype2 重新截取干净状态，不复用旧截图 |

## 16. 已决议事项

- 官网主题：亮色，日光光学。
- 产品岛：深色，保留青色语义色。
- 站点强调色：钴蓝 `#2447FF`。
- 字体：Satoshi + Noto Sans SC + IBM Plex Mono。
- 不单独做 About 页。
- 中文首发，不做英文切换。
- 不引入 Tailwind、GSAP、Three.js、Next.js。
- ReactBits 只选 6 个组件，不使用 ReactBits Dock。
- Shaders 只用于材质实验台，渐进增强。
- 官网页不重画岛：首屏一个真实实例，故事与配置器共用第二个真实实例。
- 产品窗为 488 × 444 的深色原型场景，页面仍是亮色。
- 状态以 prototype2 DOM 为唯一真值，父层只通过 postMessage 镜像。
- 不虚构“悬停预览态”；悬停只缩放 1.022。
- 展示真实 compact 状态：idle / media / notifications / clipboard / agent / timer / battery / hud。
- 下载与插件文档只链接真实 GitHub 地址。
