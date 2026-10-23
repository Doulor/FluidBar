# 液态玻璃（Liquid Glass）材质调研与实现说明

> 日期：2026-10-23 · 维护者：Lead
> 触发：用户要求「灵动岛现在有两种材质（Obsidian / 亚克力），再加一种液态玻璃材质」。
> 参考入口：[emilkowalski/skills](https://github.com/emilkowalski/skills)（Apple Design / Emil Design Eng skill）。

## 1. 结论先行

- 分层窗口（`WS_EX_LAYERED` + `UpdateLayeredWindow`）**不经过 DWM 的 GPU 合成路径**，拿不到
  `backdrop-filter` 那种实时背景模糊；「真·液态玻璃」的折射/模糊无法直接照搬 Web 方案。
- 因此 Tier 2 用**绘制光学结果**的方式实现：透镜边缘（rim lensing）+ 顶部高光（specular）+
  底部焦散（caustic）+ 极低透明度色散（dispersion）+ 斜向光泽（sheen）+ 指针跟随光晕。
- 静态层**烘焙成一张带预乘 alpha 的图片**（与 shadow cache 同一套预算规则），稳定帧只剩一次裁剪 blit；
  指针光晕是唯一逐帧绘制的图层。
- 顺带修掉一个老问题：半透明外壳会把**烘焙阴影透过填充显示出来**，导致「玻璃」实际接近不透明。
  现在对玻璃类材质把阴影从壳体轮廓中挖掉（`ClipOperation.Difference`），玻璃才真正透出桌面。

## 2. 参考仓库（17 个液态玻璃仓库 + emilkowalski/skills，已拉取到 `third_party/liquid-glass-references/`，`.gitignore` 已忽略）

| # | 仓库 | 语言 | 借鉴点 |
|---|---|---|---|
| 1 | [conorluddy/LiquidGlassReference](https://github.com/conorluddy/LiquidGlassReference) | Markdown | iOS 26 API 语义：`.regular`/`.clear`、tint、interactive、`GlassEffectContainer`、morphing、无障碍降级 |
| 2 | [DnV1eX/LiquidGlassKit](https://github.com/DnV1eX/LiquidGlassKit) | Swift | Apple API 对齐的回移实现，材质分层（highlight / shadow / tint）的工程拆分 |
| 3 | [GetStream/awesome-liquid-glass](https://github.com/GetStream/awesome-liquid-glass) | Swift | 交互案例集合：按压/拖拽下的形变与高光变化 |
| 4 | [ryanashcraft/FabBar](https://github.com/ryanashcraft/FabBar) | Swift | iOS 26 玻璃 tab bar 的实测比例：圆角、边缘亮度、浮起阴影 |
| 5 | [rdev/liquid-glass-react](https://github.com/rdev/liquid-glass-react) | TypeScript | 圆角矩形 SDF → `smoothStep` 位移贴图（lensing 的数学原型） |
| 6 | [shuding/liquid-glass](https://github.com/shuding/liquid-glass) | JavaScript | 同上，最小可复制的 shader 片段（`roundedRectSDF` + 归一化） |
| 7 | [AndrewPrifer/liquid-dom](https://github.com/AndrewPrifer/liquid-dom) | TypeScript | DOM 级折射：分层容器、边缘采样区域共享 |
| 8 | [ybouane/liquidglass](https://github.com/ybouane/liquidglass) | TypeScript | 参数化：blur / refraction / chromatic aberration / specular 的默认强度 |
| 9 | [dashersw/liquid-glass-js](https://github.com/dashersw/liquid-glass-js) | JavaScript | CSS 兜底层（无 shader 时的渐变+高光近似） |
| 10 | [archisvaze/liquid-glass](https://github.com/archisvaze/liquid-glass) | HTML/CSS | `inset 0 0 20px -5px rgba(255,255,255,.45)` 的「内发光=玻璃壁」写法 |
| 11 | [deepika-builds/liquid-glass](https://github.com/deepika-builds/liquid-glass) | JavaScript | 单文件 SVG displacement，零依赖版本的位移参数 |
| 12 | [VII-Cae/hyalite--liquid-glass](https://github.com/VII-Cae/hyalite--liquid-glass) | JavaScript | SDF lens map + 边缘色散，说明「色散只在边缘 2–4 px」 |
| 13 | [Kyant0/AndroidLiquidGlass](https://github.com/Kyant0/AndroidLiquidGlass) | Kotlin | Compose 2D canvas 版本：无 shader 时用多层渐变模拟折射 |
| 14 | [QWEA0/Liquid-Glass-Android](https://github.com/QWEA0/Liquid-Glass-Android) | Kotlin | SDF 折射 + 物理色散 + 传感器高光（陀螺仪→高光位置） |
| 15 | [hyprnux/hyprglass](https://github.com/hyprnux/hyprglass) | C++ | 合成器层 lens/refraction：厚度、边缘法线、光源方向的参数化 |
| 16 | [whynotmake-it/flutter_liquid_glass](https://github.com/whynotmake-it/flutter_liquid_glass) | Dart | 组件化玻璃（container / button / tab bar）与降级策略 |
| 17 | [sdegenaar/liquid_glass_widgets](https://github.com/sdegenaar/liquid_glass_widgets) | Dart | 完整 widget kit：玻璃层叠顺序与可读性处理 |
| 18 | [emilkowalski/skills](https://github.com/emilkowalski/skills) | Markdown | `apple-design` skill §12 Materials：半透明层级、vibrancy、materialize 而非淡入；§4 弹簧参数 |

> 仅作设计/算法参考；未复制任何仓库代码。GitHub 上 Apple 官方没有独立的 Liquid Glass 仓库，
> WWDC 2025 的材质描述以 HIG + SwiftUI `glassEffect` API 为准，上表 1–4 为最接近官方的社区实现。

## 3. 从参考里提炼的「液态玻璃」构成

1. **透镜边缘（lensing）**：圆角矩形 SDF 在边缘 `smoothStep` 处产生位移；视觉上表现为壳体内侧
   一圈亮度/暗度带（约 4–15 DIP，随尺寸缩放），这也是玻璃「有厚度」的主要线索。
2. **顶部高光（specular）**：光源在上方时，顶边出现 1–2 DIP 的接近白色亮线 + 柔和 bloom；
   底边出现反向的焦散亮线（光从玻璃里穿出）。
3. **色散（dispersion）**：仅在边缘 2–5 DIP 内，冷色（青）在上、暖色（品红）在下，alpha ≤ 12%。
4. **斜向光泽（sheen）**：一条 45°–70° 的低透明度白色带，静态（避免为了它每秒跑 120 帧）。
5. **自适应阴影**：玻璃浮起，阴影更软更大；但**不能透过玻璃看到阴影**，否则会变成一块深色板。
6. **指针照明（interactive illumination）**：iOS 的 touch-point illumination，指针处 12% 白色径向光斑。

## 4. 参数（`LiquidGlassTokens`，全部 DIP / 直通 alpha）

| Token | 值 | 说明 |
|---|---|---|
| `FillOpacity` | 0.68 | 比亚克力（0.72）略透，桌面透出约 1/3 |
| `RimBandHeightFactor` / Min / Max | 0.18 / 4.5 / 15 | 透镜壁厚度：`clamp(h×0.18, 4.5, 15)`，且 ≤ h/3 |
| `RimLightTopAlpha` | 0x4A (29%) | 顶部壁面亮度 |
| `RimShadeBottomAlpha` | 0x38 (22%) | 底部壁面暗度（厚度背光面） |
| `SpecularTopAlpha` | 0x9E (62%) | 顶边高光线 |
| `CausticBottomAlpha` | 0x30 (19%) | 底边焦散 |
| `DispersionCoolAlpha` / Warm | 0x1E / 0x16 | 青 / 品红色散 |
| `SheenAlpha` | 0x14 (8%) | 斜向光泽 |
| `PointerGlowRadiusDip` / Alpha | 132 / 0x1F | 指针照明 |

## 5. 实现落点

| 文件 | 作用 |
|---|---|
| `src/FluidBar.Island/LiquidGlassTokens.cs` | 材质 token（尺寸/透明度），被渲染器与设置预览共用 |
| `src/FluidBar.Island/LiquidGlassPainter.cs` | 烘焙 + 绘制：body veil / rim light / rim shade / specular / caustic / dispersion / edge key / sheen / pointer glow |
| `src/FluidBar.Island/IslandRenderer.cs` | `DrawShellBody` / `DrawSplitIslets` / `DrawGhostStack` / `DrawShadow`（玻璃挖空阴影）接入 |
| `src/FluidBar.Island/IslandTheme.cs` | `IslandMaterial.LiquidGlass`（Tier 2） |
| `src/FluidBar.App/Hosting/Skia/SkiaIslandThemeMapper.cs` | App 材质 → 渲染器材质（SafeMode 一律降级 Obsidian） |
| `src/FluidBar.App/Settings/*` · `FluidBar.Core/Settings/*` | 枚举、序列化 token `liquid-glass`（兼容 `liquidglass`/`liquid`/`glass`）、Core↔App 映射 |
| `src/FluidBar.App/Views/Settings/IslandPreview.cs` | 设置页 300×62 实时预览同步液态玻璃外观 |
| `src/FluidBar.App/Localization/*.json` | `Settings.Appearance.Material.LiquidGlass`、`FillOpacityGlassHint` |

### 缓存与预算

- 静态层按 `(宽, 高, DPI, 圆角哈希)` 烘焙，最多保留 4 张（`LiquidGlassOverlayBytes` 可诊断），
  与 `ShadowCache` 同一套「有界 + 尺寸桶」策略。
- 展开/收起弹簧期间（`ShellMorphing`）改为 1/4 分辨率、8 DIP 桶的临时烘焙并复用同一张 scratch，
  避免每帧一次全分辨率烘焙。
- 稳定帧：1 次 `ClipPath` + 1 次 `DrawImage`（+ 悬停时 1 次径向光斑）。

## 6. 实测（Release，150% DPI，440×284 展开面板）

| 指标 | Obsidian | 液态玻璃 |
|---|---|---|
| 稳定帧 mean / p95（多次运行） | 0.38–0.44 / 0.43–0.68 ms | 0.62–0.68 / 0.68–1.02 ms |
| 稳定帧分配 | 0 B | 0 B |
| 首帧（烘焙） | — | 5 ms（一次性） |
| 材质切换交叉淡入帧（180 ms） | — | 2.4–3.0 ms |
| 展开弹簧帧 mean / max（前半段，含阴影） | 1.66–1.76 / 3.73–4.13 ms | 2.11–2.17 / 4.21–4.24 ms |
| 展开弹簧帧 mean / max（后半段，无阴影） | — | 0.91 / 1.61 ms |
| 主体 alpha（中心） | 251 | 176（亚克力 183） |
| 顶边 / 中心 / 底边亮度（深色桌面） | — | 190 / 22 / 88 |

结论：液态玻璃比 Obsidian 多约 0.25 ms/帧；展开弹簧期间多约 0.45 ms/帧（阴影烘焙占大头，两种材质都有）。
仍然远低于 120 fps 的 8.33 ms 预算；空闲不产生额外帧。

对照截图见 [liquid-glass-shots](liquid-glass-shots/README.md)（三种材质同屏 + 展开面板 + 原型近似）。

## 7. 已知限制 / 后续可做

1. **没有真实背景采样**：如果未来把岛改成 DWM 合成窗口（非分层），可以接
   `DwmEnableBlurBehindWindow`/`SetWindowCompositionAttribute` 做真模糊，再把本 painter 降级为「光学叠加层」。
2. **色散是固定的**：真实色散随背景亮度变化；需要背景采样才能做动态强度。
3. **光晕不扩散到邻近玻璃**：iOS 的 illumination 会在 `GlassEffectContainer` 内扩散，当前只在指针所在壳体内。
4. ~~材质切换过渡~~：已按规范 3.4 实现——`SkiaIslandController` 在材质变化时记录旧 theme，
   `IslandRenderer` 用 premultiplied `Plus` 把旧/新壳体各画一遍（(1-t) / t），180 ms 完成。
   首版用整场景 `SaveLayer` 交叉淡入，实测 7.7 ms/帧（超 8.33 ms 预算），改为壳体双 pass 后
   降到 2.4–3.0 ms；文字内容只画一次，不会重影。
5. `docs/rewrite/prototype2` 已支持 `data-material="liquid"`（CSS 近似：内阴影透镜边缘 + 顶部高光 +
   底边焦散 + 一条固定光泽带）；它只是设计参考，真实透镜/焦散/色散以渲染器和 `liquid-glass-shots/` 为准。

## 8. 2026-10-23 返工：先做网页原型（用户反馈"像老式电视"）

用户反馈第一版不合格：只有深色填充 + 一圈透镜/高光，没有背景模糊。结论：液态玻璃的第一要素是
**背景高斯模糊**，不是描边。已按"先原型、再移植"重做：

- 原型：`docs/rewrite/liquid-glass-lab/index.html`（6 个版本，Edge/Chrome 打开，1–6 切换、B 开关模糊、G 全览）。
- 6 个版本：A 磨砂 / B 折射 / C 清透 / D 厚镜 / E 水色 / F 烟灰；B/D/E 用 `backdrop-filter: url(#svgFilter)`
  做 SDF 位移 + 色散，A/C/F 用纯 CSS blur。
- 截图：`docs/rewrite/liquid-glass-lab/shots/`（含 `A-frosted-blur-off.jpg` 对照，证明"模糊"与"只是变暗"的差别）。
- 关键技术验证：分层窗口挡不住背景，但 **`SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` 之后
  `BitBlt` 能抓到窗口背后的桌面**（探针实测：窗口区域像素从 255,0,0 变成桌面 67,69,74）。所以 App 里
  可以做"抓屏 → 高斯模糊 → 贴进壳体"的真背景模糊，再把原型的折射/色散在 Skia 里逐像素实现。
- 原型实现细节：`lab.js` 里的 `refractionProfile` / `displacementMap` / `specularMap` 移植自
  `archisvaze/liquid-glass`（MIT），滤镜链参考 `rdev/liquid-glass-react`（MIT）。注意 SVG 滤镜必须用
  `DOMParser` 以 SVG 方式插入，`div.innerHTML` 会把 `feGaussianBlur` 小写化导致滤镜静默失效。

## 9. 2026-10-23 落地：App 内的真背景模糊（壁纸级）

第一版液态玻璃只有深色填充 + 描边，没有背景模糊（用户："跟老式 TV 一样"）。本轮把"抓屏 → 模糊 → 贴壳"
真正接进了 Skia 渲染：

- `FluidBar.Platform.Windows/Hosting/ScreenBackdropCapture.cs`：`WDA_EXCLUDEFROMCAPTURE` + `CreateDIBSection` +
  `BitBlt`，按窗口物理像素矩形抓取（失败返回 null，渲染回落到染色填充）。
- `FluidBar.App/Hosting/Skia/IslandBackdropProvider.cs`：1/6 降采样 + `SKImageFilter.CreateBlur(2,2)` +
  1.55× 保亮度饱和度矩阵烘焙，按窗口矩形缓存 ≤2 份，节流 400 ms，硬下限 120 ms；移动窗口时重抓；
  只在真正渲染的帧里触发，空闲仍是 0 帧。
- **抓屏必须异步**：实测 `BitBlt` 732×576 = **6.5 ms/次**（`StretchBlt` 缩到 1/6 仍是 6.75 ms——开销在"读屏幕"本身，
  不在拷贝）。6.5 ms 落在 8.33 ms 帧预算里必然掉帧，所以抓屏放到线程池：渲染线程永远画"最新已完成"的那张，
  抓完再替换；被替换的 `SKImage` 延后 2 帧释放（`DrainRetired`），避免绘制中途被 dispose。
- `IslandRenderState.Backdrop` + `IslandRenderer.DrawBackdrop`：在壳体路径内绘制模糊桌面，再叠
  `BackdropTintAlpha = 0.30` 的染色；`LiquidGlassPainter.DrawEdgeOnly` 只画 1 px 键线 + 顶部高光 + 底部焦散 +
  冷色边缘（没有 veil、没有 rim band、没有 sheen）——即"去掉老式电视的厚边"。
- 交叉淡入期间（`Plus` 混合）不画背景图，避免两份叠加。
- 实测：全量测试 2637 绿；真实 App 冒烟 113 帧、退出码 0（首帧回落染色填充，抓屏落地后切换为背景图）；展开态壳体横向采样
  #09121e → #2d353f → #27303b → #3c444e（同一帧内有明显空间变化，说明确实是背景内容而不是纯色填充）。

- 烘焙时叠 **1.55× 饱和度**（`SKColorFilter.CreateColorMatrix`，保亮度矩阵）：参考实现（rdev/archisvaze/Apple .regular）
  都会给模糊背景提饱和，玻璃是"聚色"而不是"灰掉"。

**已知限制 1**：GDI 抓屏只拿得到壁纸层（见上），所以岛后面如果是窗口/视频，模糊里不会包含它们。
下一步若要"连窗口一起糊"，需要 `Windows.Graphics.Capture`（D3D11 + 帧池 + staging 回读），成本明显更高，
建议等用户选定材质版本后再评估。

**已知限制 2（验证环境）**：本会话所在的桌面是一个合成/虚拟桌面——抓屏拿到的是稳定的深蓝色表面，
用 `SystemParametersInfo` 换壁纸（注册表已更新为白图/草地图）后抓到的像素仍是 (14,30,52)，`CopyFromScreen`
也不反映变化。因此"模糊里是否出现用户真实桌面内容"只能由用户在真实会话里目视确认；
代码路径本身已确认走通（渲染帧内的壳体颜色 = 抓屏内容 × 0.7 + 染色）。

## 10. 2026-10-23 定稿 B（折射玻璃）+ 窗口也能模糊（WGC）

用户选定原型 **B · 折射玻璃**：`blur 10px / tint rgba(8,10,14,.30) / saturate 160% / edgeHighlight .40 / refraction 1.00×`，
并明确要求"窗口也要能模糊"（GDI 只抓壁纸层不够）。

### 10.1 参数落位（B）

| 原型参数 | App 实现 |
|---|---|
| blur 10 px | `IslandBackdropProvider.BakeSigma = 2.5`（1/6 降采样空间，150% DPI ≈ 10 DIP） |
| saturate 160% | `BackdropSaturation = 1.60`（保亮度矩阵） |
| tint rgba(8,10,14,.30) | `LiquidGlassTokens.BackdropTintAlpha = 0.30` × 自适应 0.90–1.40 |
| edgeHighlight 0.40 | `DrawEdgeOnly`：1 DIP 键线 0.28α + 镜面弧 0.55α + 底部焦散 + 冷边 |
| refraction 1.00× | `RefractionScale = 1.05`，band 自动 = clamp(h×0.18, 4.5, 15) DIP |

折射实现是"透镜带"：在壳体轮廓的 rim 带内用 `SKPaint.GetFillPath(描边宽度=2×band)` 得到带状裁剪，
再把同一张背景图按 `RefractionScale` 绕壳体中心放大后重绘一次——边缘因此采样到轮廓外侧的内容，
等价于原型 SDF 位移的宏观效果，但只多一次 blit。测试 `Refraction_band_refracts_the_backdrop_near_the_rim`
用棋盘背景验证 rim 内平均亮度差 38/255。

### 10.2 窗口模糊：Windows.Graphics.Capture

- 新增 `FluidBar.Platform.Windows/Hosting/WgcBackdropCapture.cs`（依赖 `Vortice.Direct3D11` 3.6.2）：
  `D3D11CreateDevice` → `CreateDirect3D11DeviceFromDXGIDevice` → `IGraphicsCaptureItemInterop.CreateForMonitor`
  → `Direct3D11CaptureFramePool.CreateFreeThreaded(B8G8R8A8)` → `GraphicsCaptureSession`；
  按需把帧纹理 `CopySubresourceRegion` 到同尺寸 staging 纹理再 `Map` 读回（只读岛所在的小矩形）。
- `ScreenBackdropCapture.TryCapture` 先走 WGC，失败才回落 GDI（老系统/锁屏/远程会话）。
- 帧池只保留"最新一帧"，桌面不变时 WGC 不产帧——正好符合"空闲 0 帧"；岛每 400 ms 取一次最新帧。
- 会话空闲 5 s 自动停止（切回黑曜石/亚克力时不再占用 GPU）。
- 抓屏完成后由 `IslandBackdropProvider.Updated` 事件通知宿主**补画一帧**（`PaintBackdropFrame`），
  否则空闲中的岛要等到下次交互才显示模糊背景。

### 10.3 验证

- WGC 探针（`build/wgcprobe`）：`GraphicsCaptureSession.IsSupported = True`，显示器 2560×1600，
  3 帧内取到画面；同一区域 WGC 读到 (207,180,202) 而 GDI 读到 (14,30,52)——两者看的是不同表面，
  证实 GDI 拿不到合成桌面。
- App 冒烟 121 帧退出码 0；紧凑态壳体在首帧后由 `#131315`（回落填充）变为 `#6c6f73` / `#93989d`
  （模糊后的合成桌面），展开态 `#b19d9a`。
- 全量测试：见本轮 STATUS（`Backdrop_mode…` 与 `Refraction_band…` 两个用例覆盖背景图与折射带）。

**新增依赖**：`Vortice.Direct3D11`（Platform 项目）。

### 10.4 亮背景可读性：vibrancy scrim

纯白背景上 30% 染色 + 白字仍只有 ~2.7:1。按 iOS 的 vibrancy 思路加了"顶部压暗"：
`BackdropLuminance > 0.45` 时，在壳体顶部 55% 高度画一层从主题背景色（α=0.30×强度）到透明的渐变，
强度随背景亮度线性增长。照片类中等亮度背景完全不受影响（阈值为 0），白底时顶部被压暗、白字可读。
测试 `Bright_backdrops_get_a_vibrancy_scrim_behind_the_top_text_band` 用纯白背景对比 0.98 / 0.0 两种亮度，
断言顶部相对底部的亮度差至少降低 10。

### 10.5 暗背景：边缘随背景亮度缩放 + 5% 雾面

用户反馈"纯黑的地方周围一圈白色高光很显眼，不像液态玻璃"。原因是键线/镜面写成固定 alpha：
黑底上 0.28α 的白线就是"画上去的环"。

- 键线：`0.04 + 0.24 × 背景亮度`（纯黑 0.04、纯白 0.28）
- 顶部镜面弧：`0.18 + 0.37 × 亮度`；焦散 ×0.55、冷边 ×0.5（同比例）
- 新增 `HazeMaxAlpha = 0.05`：越暗加得越多，纯黑上垫 5% 白，让板子仍像一块玻璃而不是黑洞
- 测试 `Glass_edges_fade_out_on_a_black_backdrop`：纯黑 rim 峰值 < 60，且比亮背景低 25 以上

### 10.6 空闲帧验证（排除"背景抓屏导致空转"）

`FLUIDBAR_PERF_PROBE=1` + `FLUIDBAR_PERF_LOG=1`（新增开关，让 1 s 一次的采样以 Warning 落盘）实测：

| 配置 | redrawsPerSec | frames |
|---|---|---|
| 全新配置 + 黑曜石 | 0 | 22 |
| 全新配置 + 液态玻璃 | **0** | 41 |
| 冒烟脚本配置（带媒体页动画） | 30 | 852 |

结论：液态玻璃（含 WGC 抓屏 + 补帧）**空闲仍是 0 帧**；早期看到的 ~15% CPU 来自冒烟配置里持久化的媒体页动画，不是本次改动。
诊断法：临时 `Diag` 文件日志确认 15 s 内只有 3 次抓屏 + 3 次补帧（启动首秒），之后无活动。

#### 边缘实测（紧凑胶囊 132×36，亮度 0–255）

| 背景 | 左边缘峰值 | 上边缘 | 下边缘 | 中心 |
|---|---:|---:|---:|---:|
| 纯黑 | **14** | 14 | 33 | 14 |
| 灰（照片级 0.45） | 82 | 82 | 96 | 83 |
| 纯白 | 146 | 111 | 157 | 146 |

纯黑时左右边缘与中心完全一致（14 = 无白圈），只有下边缘 33（焦散）略亮；白背景才出现 146 的边缘提亮。
预览图：`docs/rewrite/liquid-glass-lab/shots/app-compact-black.jpg` / `app-compact-white.jpg` / `app-expanded-black.jpg`。

### 10.7 预览开关：`FLUIDBAR_MATERIAL`

调试/预览时不用改配置：`FLUIDBAR_MATERIAL=liquid-glass`（也接受 `acrylic` / `obsidian`）会覆盖**本次进程**渲染用的材质，
不写入配置。实现：`SkiaIslandThemeMapper.OverrideMaterial`；测试
`Material_env_override_forces_the_rendered_material_without_touching_settings`。

> 踩坑记录：手写一个只有 `{"appearance":{"material":"liquid-glass"}}` 的配置目录会被 schema 校验判为无效 →
> 回落到默认（黑曜石），看起来就像"液态玻璃没生效"。预览请用这个环境变量，别手写配置。

### 10.8 窗口级模糊 + 岛可见（自抠图）+ 折射带羽化

用户要求"必须要窗口级模糊"，但 `WDA_EXCLUDEFROMCAPTURE` 会让岛从所有抓屏/串流画面消失（实测：排除开 23 vs 21，
等于不可见；关掉 59 vs 21，清晰可见）。两者不可兼得，于是改成**不排除窗口 + 自抠图**：

- 抓屏始终走 WGC（合成桌面，含窗口内容），默认不再设 `WDA_EXCLUDEFROMCAPTURE`。
- `IslandBackdropProvider.MaskIslandArea`：抓到的帧里，把岛自身的矩形（壳体外扩 14 DIP）用它左右两侧的像素
  做水平插值填掉，再进 1/6 降采样 + 高斯——涂抹痕迹在模糊后不可见，岛不会"糊到自己"。
- `FLUIDBAR_WDA=1` 仍保留给"不录屏、只要最好画质"的场景（走排除 + 原生 WGC，无自抠图）。

**折射带羽化**：卡片面板"边界和中间之间有一条明显的分界"来自 rim 带是**单层硬裁剪**——带内是放大 5% 的图，
带外是原图，内边缘必然出现阶跃。改成**三层嵌套带**（宽度 1.0 / 0.68 / 0.40 × band，alpha 0.34 / 0.30 / 0.26），
折射强度向外衰减；测试 `Refraction_band_fades_out_instead_of_stepping_at_its_inner_edge` 以 1 DIP 步长扫过
1–18 DIP，要求相邻步长 < 22/255（硬边会远超）。

### 10.9 液态玻璃是"所有岛面"的材质，不是主壳专属

用户要求：分裂小岛、多岛堆叠的小岛、以及以后插件新增的岛，都必须和主壳**同一地位**，而不是补丁。

实现：抽出一个共享入口 `IslandRenderer.TryPaintBackdropSurface(canvas, state, path, bounds, alpha, originX, originY)`——
任何"岛面"（壳体、分裂胶囊、堆叠残影、将来的插件岛）只要给出轮廓路径和边界，就得到同一套处理：
模糊桌面（按路径裁剪）→ 自适应染色 → 暗底 haze → 边缘高光（`DrawEdgeOnly`）。没有背景图时返回 false，
调用方保留自己的不透明回退。

已接入的三处：

| 岛面 | 轮廓 | 说明 |
|---|---|---|
| 主壳 | 圆角矩形路径 | 额外加折射带 / vibrancy scrim（壳体的专属增强） |
| 分裂胶囊 | 圆 | 原来是不透明圆球 + 独立的液体叠加层 |
| 多岛堆叠残影 | 圆角矩形 | 原来是不透明 pill + 独立叠加层 |

测试：`Split_capsules_render_the_backdrop_like_the_shell`、`Stacked_ghost_layers_render_the_backdrop_like_the_shell`
（亮背景 vs 无背景，采样点亮度差 > 40 / > 30）。次生紧凑岛（secondary compact）复用了 `DrawShellBody`，
自动继承同一材质。

## 11. 智能让位（Smart Yield）· 第一块：核心状态机

按用户提供的《FluidBar 智能让位系统设计方案》开工。先把与平台无关的核心做成纯逻辑，便于测试与后续接入：

- `src/FluidBar.Core/Interaction/YieldEngine.cs`：
  - `YieldState`（Interactive / Candidate / Yielding / Cooldown）、`YieldSensitivity`（保守 / 平衡 / 积极）。
  - 打分信号与设计文档 §5.2 对齐：轨迹指向壳体 +25、快速掠过 +20、前台最大化 +15（全屏 +30）、
    停留交互控件 −20、在壳体内停留 −35、刚点过岛 −60、拖动 −100；裁剪到 [−100, 100]。
  - 滞回：进入 Candidate / Yielding 的阈值高于退出阈值；`MinYieldMs` 最短保持、`CooldownMs` 冷却、
    `CandidateTimeoutMs` 候选超时、`MaxYieldMs` 兜底回 Interactive；`ForceInteractive` 供手动恢复。
  - 不吞点击：引擎只输出"是否该穿透"，切换由调用方在鼠标按下前完成（设计文档 §2.4）。
- 测试 `tests/FluidBar.Core.Tests/YieldEngineTests.cs`（11 例）：空闲不触发、快速接近触发、缓慢移动不触发、
  壳体内停留抑制、拖动永不触发、刚交互抑制、最短保持、冷却期不重复触发、手动恢复、历史有界、灵敏度差异。
- 过程中修掉两个真 bug：`long.MinValue` 哨兵相减溢出（导致"刚交互"恒为真，永远不会让位）、
  以及停留时间用了"距上一帧"（恒为 0）而不是"进入壳体以来的时长"。

## 12. 智能让位 · 阶段二：真正能"让位"（输入穿透）

- `LayeredWindowHost.SetInputPassthrough(bool)`：置位后 `HitTest` 对所有坐标返回 `HTTRANSPARENT`，
  窗口继续绘制但不再接收鼠标输入；`InputPassthrough` 暴露给状态机与测试。
- `IslandMouseHook`：
  - 新增 `PointerMovedAnywhere(x, y)`：每次 WM_MOUSEMOVE 都回调（钩子回调内只做一次委托调用，保持轻量），
    给意图引擎提供"接近轨迹"，不需要轮询 `GetCursorPos`。
  - 新增 `PassThrough`：让位时钩子不再吞掉岛矩形内的点击/滚轮（`Classify` 直接返回 Outside），
    桌面拿到点击。注意这是**真正的输入路径**：分层窗口上的点击本来就由这个钩子消费。
- `ForegroundWindowProbe`（Platform）：按需 `GetForegroundWindow` + `GetWindowRect` + 监视器工作区，
  判断"前台窗口是否最大化/全屏并覆盖到岛的位置"；不枚举窗口列表。
- `SmartYieldCoordinator`（App）：把钩子位置流喂给 Core 的 `YieldEngine`，把引擎输出翻译成
  `SetInputPassthrough` + `hook.PassThrough`；上下文（前台窗口/拖动）最多每 100 ms 刷新一次。
  隐藏时（壳不可见）自动回到 Interactive。
- 开关：`FLUIDBAR_SMART_YIELD=1`（设置项待接）。日志事件 `island.yield.transition` / `island.yield.restore`。
- 测试：`SmartYieldCoordinatorTests`（4 例：快速接近让位并在离开后恢复、刚点过岛不让位、拖动不让位、隐藏时恢复）。
- 又抓到两个同源 bug：`long.MinValue` 当作"从未"哨兵时与 `nowMs` 相减溢出，导致上下文缓存永不刷新 /
  交互抑制恒为真。统一改成可空时间戳。

## 13. 让位动效：柔化（Soften）— 三种材质通用

用户在 6 个网页原型候选里选了 **E 柔化**，并明确要求"适配的不只是玻璃外观，三种外观都要适配"。

实现方式决定了它天然与材质无关：**在合成后的整幕上做一次图层处理**，而不是给每种材质各写一份。

- `YieldTokens`（Island）：`SoftenBlurDip 6`、`SoftenFade 0.75`（终值 25% 不透明度）、
  `SoftenSaturation 0.55`、`SoftenDurationMs 240`、`RestoreDurationMs 260`、`MaxBlurAreaDip2 60_000`。
- `IslandRenderer.DrawScene`：当 `state.YieldProgress > 0` 时，`ClipRect(壳体±28 DIP)` + `SaveLayer`，
  layer 的 paint 同时带 alpha（淡出）、`SKColorFilter`（亮度保持的去饱和矩阵）与
  `SKImageFilter.CreateBlur`（模糊）；先把原场景画进 layer，restore 时统一作用。黑曜石 / 模拟玻璃 / 液态玻璃
  走的都是同一条路径，因为效果作用在"画完之后的整幕"上。
- 成本控制：模糊只在小面积壳上启用（壳体面积 ≤ 60 000 DIP²，即紧凑态/胶囊）；展开面板（440×284）只淡出+去饱和，
  避免整面板 SaveLayer 的重绘开销。
- 动画：`SmartYieldCoordinator.Step(now)` 用 ease-out cubic 在 240 ms（让位）/ 260 ms（恢复）内推进
  `YieldProgress`；宿主 `OnFrame` 里每帧推进并强制整帧重绘，动画结束自动停表。
- 测试：`YieldSoftenTests`（3 材质 × 2 项：淡到 ~25%、边缘变软）、`SmartYieldCoordinatorTests` 增加
  progress 动画用例。

## 14. 让位控制：右键手动 + 3 秒自动恢复 + 进程降权

用户定稿：「固定柔化」「右键手动柔化/恢复」「不要托盘菜单」「3 秒没交互就恢复」「降权」。

- **右键**：`IslandMouseHook` 新增 `WM_RBUTTONDOWN` 处理——在岛矩形内**始终由我们消费**（即使正在让位，
  点击也不会漏到下面的窗口），转发成 `RightPressed` → `SmartYieldCoordinator.Toggle()`：
  - 当前可交互 → `YieldEngine.ForceYield()` 立即柔化；
  - 当前已柔化 → `Restore()` 立即恢复。
- **3 秒自动恢复**：`YieldOptions.RestoreAfterMs = 3000` + `ActivityRadiusPx = 56`（岛周围 56 px 内的指针
  活动会刷新计时）。关键实现细节：
  - `YieldEngine.Tick(shell, ctx, now)`：只推进时间类规则，**不产生指针活动**，否则"停在岛上的指针"会
    永远刷新 3 秒窗口、永远不恢复；
  - `Tick` 也**不允许从 Interactive 发起新的让位**（分数只在真实样本里更新，否则恢复后会被旧高分立刻
    再次让位）；
  - 宿主帧循环必须为让位保活（`IslandFrameLoopPolicy` 只看 controller 的动画标志，柔化不在其中），
    否则淡出结束后再也没有 tick。
- **默认开启**：`FLUIDBAR_SMART_YIELD=0` 可整体关闭（含右键手势）。
- **降权**：`Program.ApplyProcessPriority()` 启动即把进程设为 `BelowNormal`（`FLUIDBAR_PRIORITY=normal` 可回退），
  岛是纯展示层，不和前台应用抢 CPU。
- 端到端实测（DPI-aware 注入输入，岛区域亮度）：静止 44 → 右键 25（柔化）→ 3 秒后 44（恢复）；
  另一次右键序列 44 → 25 → 44 → 27（再次柔化），进程优先级 `BelowNormal`。
- 测试：引擎新增「tick 不算活动」「手动柔化保持到计时结束」；协调器新增「右键柔化/恢复」「淡出结束后宿主
  仍需 tick」。

## 15. 定稿：删除预测式让位，只保留右键手动 + 穿透修复

用户决定：「把这个智能让位直接删掉，纯手动右键触发」，并反馈「穿透不了」。

### 15.1 删除

- 删除 `YieldEngine`（四状态机 / 打分 / Candidate / Cooldown / 前景窗口上下文）、`ForegroundWindowProbe`、
  `PointerMovedAnywhere` 全局指针流、以及对应的 12 个引擎测试。
- 新增 `ManualYieldController`（Core，纯逻辑）：`Soften/Restore/Tick`，只有"是否柔化 + 3 秒安全计时"，
  没有任何预测。
- `SoftYieldCoordinator`（App）负责淡入淡出动画与输入穿透开关；`SmartYieldCoordinator` 已删除。

### 15.2 穿透 bug 的根因（实测）

用 `WindowFromPoint` 对比柔化前后的命中窗口：

| 状态 | 命中窗口 | exStyle |
|---|---|---|
| 正常 | `FluidBar.LayeredIslandHost`(pid 自身) | 0x8080088 |
| 柔化（修复前） | 仍是 `FluidBar.LayeredIslandHost` | 0x8080088（没有 0x20） |
| 柔化（修复后） | `Chrome_WidgetWin_1`(下方应用) | — |

根因：只返回 `HTTRANSPARENT` 不够——它只把消息转给**同一线程**的窗口，跨进程应用收不到点击。
修复：`LayeredWindowHost.SetInputPassthrough` 同时增删 `WS_EX_TRANSPARENT`（`GWL_EXSTYLE`），
保留 `HTTRANSPARENT` 作为第二道保险。

### 15.3 行为

右键（岛矩形内始终被消费）→ 柔化；再右键 → 恢复；3 秒无操作自动恢复（`ManualYieldController.Tick`）。
`FLUIDBAR_SOFT_YIELD=0`（或旧的 `FLUIDBAR_SMART_YIELD=0`）可整体关闭。

### 15.4 柔化的两个显示 bug（2026-10-23 反馈）

**主岛有时变"纯透明"**，两个原因：

1. 帧时钟在空闲时是停的。右键切换只改了状态、没有请求帧，淡出可能停在任意中间值（暗背景上 25% 就等于看不见）。
   修复：`ApplyYieldPassthrough` 结束处调用 `EnsureFrameLoop()`；`IsActive` 让帧循环在柔化期间持续运行。
2. `YieldProgress` 若是非有限值，layer alpha 是 `(byte)NaN` = 0，整岛直接透明。修复：`double.IsFinite` 守卫。
3. 另外发现：分裂胶囊出现时壳体内填充被强制 `SKBlendMode.Src`（为了让 pill 和胶囊的底色字节一致），
   在液态玻璃下这条路径会把模糊背景**替换**成 30% 染色 → 壳体看起来是透明的。修复：只有"没有背景图"时才用 Src。

**右键隐藏把小岛截断**：柔化图层原来裁剪到 `壳体 ± 28 DIP`，分裂胶囊 / 堆叠残影都在壳体之外，被裁掉。
修复：有胶囊或残影时**完全不裁剪**（图层覆盖 damage 区域），其余情况放宽到 ±48 DIP。

实测：idle 41 → 柔化 29 → 3 秒后 49（恢复）。

### 15.5 白底白字看不清（2026-10-23）

用户反馈：白色字体在白底上糊成一片。原因是亮背景下的"vibrancy scrim"太弱（上限 0.30），
纯白背景下壳体亮度仍在 0.58 左右，白色标签的对比度只有 ~1.8:1。

修复三处：

1. `ScrimMaxAlpha` 0.30 → **0.62**，`ScrimHeightFactor` 0.55 → **0.72**（暗色顶部渐变覆盖文字带）。
2. `BackdropTintMaxFactor` 1.40 → **1.75**（纯白背景下染色加深到 ~0.52 alpha；暗背景仍是 0.90 倍，观感不变）。
3. `TextEngine`：浅色文字额外画一次**深色投影**（向下 0.9 DIP、45% 黑），照片/中间调背景上也能读。
   成本是一次额外文字绘制，无模糊滤镜、无分配。

回归测试 `White_backdrops_stay_dark_enough_for_white_labels`：纯白背景 + 液态玻璃，采样壳体空白处的亮度必须 < 0.45
（修前 0.58，修后 <0.45）。

### 15.6 白底上的音乐卡片 + 亮暗切换的线性过渡（2026-10-23 反馈）

**音乐卡片灰字看不清**：上一轮只加强了 scrim，纯白背景下壳体仍在 0.49 亮度。这轮：

- `BackdropTintMaxFactor` 1.75 → **2.60**（纯白背景下染色约 0.78 alpha，壳体亮度 < 0.35）。
- 新增 `IslandRenderer.AdaptThemeForBackdrop`：亮背景时把 `ForegroundSecondary` / `ForegroundTertiary`
  向亮色插值（#9B9BA5 → #D2D8E2），灰字在深色玻璃上有对比度。
- 测试 `Expanded_media_panel_on_white_keeps_the_card_readable`（纯白背景，展开面板 5 个采样点亮度 < 0.35；
  修前 0.49）。

**亮暗切换瞬变**：玻璃的染色/遮罩/文字都是背景亮度的纯函数，桌面一变就跳。这轮改成缓动：

- `BackdropLuminanceSmoother`（Core）：指数缓动，时间常数 160 ms，单帧步长上限 50 ms（避免空闲后第一帧跳变），
  与帧率无关；测试 4 例（首帧只走一点 / 几个时间常数内收敛 / 33ms 与 16.5ms 步进结果一致 / Reset 直接到位）。
- `SkiaIslandHost`：每帧推进平滑值，采到的背景图做 **160 ms 交叉淡入**（`PreviousBackdrop` + `BackdropBlend`，
  背景图按 1/6 缩放后很小，复制一份安全且便宜），期间帧循环保持运行。
- 测试 `Backdrop_captures_cross_fade_instead_of_swapping`：黑图→白图 交叉 0.5 时亮度落在两者之间。

### 15.7 玻璃跟随桌面明暗（修正上一轮的"变黑"）

用户反馈："从黑色变到白色确实有渐变，但渐变完变回黑色"。根因是上一轮的方向错了：亮背景下把玻璃**染黑**
（tint 2.6）来保证白字对比度，于是过渡结束后岛确实"变成黑的"。

正确模型：**玻璃跟着桌面走** —— 白底 → 浅色"瓷玻璃" + 深色文字；黑底 → 黑曜石玻璃 + 白色文字。

- `IslandRenderer.AdaptThemeForBackdrop` 改为在 `IslandTheme.Dark` 与 `IslandTheme.Light` 之间按背景亮度插值
  （t = (lum − 0.42) / 0.33，即 0.42–0.75 之间过渡），插值的是整套调色板：Background / BackgroundRaised /
  BackgroundInset / Line / LineStrong / Foreground / ForegroundSecondary / ForegroundTertiary / Accent / …
- 关键：**整幕统一适配** —— 在 `DrawSceneWithMaterialBlend` 里把适配后的 theme 写回 state，因为
  CompactRenderer / PageRenderer / IconSet 都从 state 读主题；只改壳体调色板时，文字仍是白的（这就是"白字看不清"的第二层原因）。
- 染色/遮罩回到温和值（`BackdropTintMaxFactor` 2.60 → 1.15、`ScrimMaxAlpha` 0.62 → 0.30、高度 0.72 → 0.55）：
  对比度现在由**墨色**保证，而不是把玻璃压黑。
- 因为亮度本身已经过 160 ms 缓动，调色板插值也就跟着缓动 —— 亮暗切换是"渐变换色"，不是跳变也不是变黑。
- 测试：`White_backdrops_flip_the_shell_to_light_glass_with_dark_ink`（白底：玻璃亮度 > 127 且标题带存在
  亮度 < 0.4 的深色像素）、`Bright_backdrops_use_light_glass_and_dark_backdrops_dark_glass`、
  `Expanded_media_panel_on_white_flips_to_light_glass`。

### 15.8 小岛转圈圈把主岛"带闪"（2026-10-23）

用户反馈：主岛旁边一个蓝色 agent 小岛一直转圈，主岛就一闪一闪。

根因不在渲染，而在**抓屏自抠图**：为了"窗口可见 + 窗口级模糊"，抓屏包含我们自己的窗口，再把岛自身的像素抹掉。
最开始只抹了**壳体矩形**，而分裂胶囊画在壳体**外面**——于是胶囊（含旋转的蓝色环）被原样抓进背景图，
经 1/6 降采样 + 高斯后变成壳体里一团随旋转脉动的蓝色，随 400 ms 抓屏节奏"闪"。

修复：`IslandBackdropProvider` 的 mask 从单个矩形改成**矩形列表**；宿主把壳体（外扩 18 DIP）和
**每个分裂胶囊**（外扩 8 DIP，`IslandMetrics.SplitIsletRects`）都交给它，逐个做水平边缘延展。
这样任何岛面自身的动画都不会再进到别的岛的玻璃里——"岛与岛光效同步"保留（它们仍是同一材质、同一套光效），
但不再互相串扰。

测试 `IslandBackdropMaskTests`：两个矩形都被抹平（左暗右亮的测试帧上，被遮区域变成渐变）。

### 15.9 主岛"一瞬间透明一瞬间清晰"（2026-10-23）

用户反馈：不是胶囊串扰，而是**整个岛**在一瞬间变透明再变清晰。

排查到的两个可导致"空帧"的路径，都已加固：

1. **背景帧缺失**：`IslandBackdropProvider.Get` 在缓存过期的那一帧返回 `Latest()`，若此时缓存为空（首帧、rect 变化、
   抓取失败）就是 `null` → 液态玻璃退回"30% 半透明填充" → 视觉上就是"一瞬间透明"。现在宿主做**粘性回退**：
   `backdrop ??= _backdropCurrent`，任何一帧都不会再退化成半透明。
2. **图片过早释放**：`RetireFrames = 2`（约 33 ms @60fps）对"渲染线程持有上一帧图片"来说太紧，
   改成 12（约 200 ms）。图片是 1/6 缩放的小图，多留 10 帧成本可忽略。

另外加了 `FLUIDBAR_BLINK_LOG=1` 的帧诊断（变化时才写一行到 `%TEMP%\fluidbar-frames.log`）：
下次若仍有闪烁，可以直接看 visible/opacity/backdrop/yield/luminance 到底是哪一个在跳。

### 15.10 1 Hz 闪透明 = 置顶复位的副作用（2026-10-23）

用户描述："大部分时间是模糊的，每秒有一小部分时间是透明的" —— **1 Hz** 这个节奏是决定性线索：
`IslandWindowInterop.TryReassertTopmost` 的节流就是 **≤1 Hz**（"always on top" 复位）。

一次 `SetWindowPos(HWND_TOPMOST, SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE)` 会让 DWM 重新合成这个分层窗口，
而我们的内容要等下一次 `UpdateLayeredWindow` 才写回去 —— 中间那一帧窗口是空的，就是"一瞬间透明"。

修复：

- 置顶复位加 `SWP_NOREDRAW`（系统不擦除、不重绘），并且复位成功后**立刻重新上传当前表面**
  （`RepaintAfterZOrderChange` → `TryUpdate(null)`），保证任何一帧都不会出现空表面。
- 与前一轮的"背景图粘性回退 + 图片保留 12 帧"一起，构成空帧的三道防线。

### 15.11 取色范围、响应速度、以及"闪"的第三轮排查（2026-10-23）

**取色范围（用户提问）**：之前 `Bake` 对**整张抓屏**（= 岛宿主窗口，约 1895×576 px，远大于胶囊）求平均，
所以旁边一个亮窗口靠过来（哪怕没接触）也会把平均值拉上去 → 变色的确"不只取岛底下"。
现在宿主把**岛自身足迹**（壳体 ±6 DIP，换算成 px）作为 `luminanceRect` 传给 provider，
`Bake` 只在这个窗口内求平均 —— 只有真正在岛后面的内容会影响颜色。

**响应滞后**：抓屏 400 ms + 亮度缓动 τ=160 ms + 交叉淡入 160 ms ≈ 0.9–1 s，与用户感知一致。
现在：抓屏 **120 ms**、τ=**80 ms**、交叉淡入 **110 ms** ≈ 0.3 s。

**"闪透明"第三轮**：前两轮（背景帧粘性、置顶复位）都没根治。第三轮改提交路径：
分层窗口是 per-pixel-alpha 的，**局部更新**（`prcDirty`）时未被更新的区域在某些驱动/DWM 路径下会被合成成
透明 —— 正好对应"每秒有一小部分时间透明"。现在 `LayeredHostOptions.FullFrameCommits = true`（App 侧开启），
每帧上传整张表面；平台层仍保留局部更新能力与对应测试。

### 15.12 闪透明的真正原因：直接画进"正在显示"的 DIB（2026-10-23）

决定性线索：上一轮把抓屏间隔 400 → 120 ms 后，**闪烁频率也跟着变成 2–3 倍**。频率跟着抓屏走 → 问题在
"每次抓屏触发的那次重绘"，而不是抓屏本身。

根因：我们一直把场景**直接画进分层窗口的 DIB**（`SKSurface.Create(info, frame.Bits, frame.Stride)`）。
这个 DIB 就是 Windows 正在显示的内容 —— 只要某一帧画到一半（异常、并发重绘、只画了一部分），
**屏幕立刻就能看到半张透明表面**。抓屏越频繁，打断重绘的机会越多，所以闪得越快。

修复（原子帧）：

1. `EnsureSurface` 改为**离屏 SKSurface**（自己分配像素），场景画在离屏表面上。
2. 每帧画完后 `CopySurfaceToFrame` 一次性 memcpy 进 DIB（约 4.4 MB，<1 ms），再做 `UpdateLayeredWindow`。
   任何绘制失败/中断都不会再出现在屏幕上。
3. 离屏表面刚重建时置 `SurfaceNeedsFull`，保证第一帧是整帧绘制（否则只有局部内容）。
4. 顺手修了一个真 bug：`OnBackdropUpdated` 之前在**抓屏线程**上调 `EnsureFrameLoop()`（帧时钟/控制器非线程安全），
   现在整段都在 UI 线程的 `_dispatcher.Post` 里。

### 15.13 抓屏时刻的"带外提交"（2026-10-23）

原子帧没解决，且**频率一直跟着抓屏间隔走**（120 ms）。那么每次抓屏时发生的动作只剩一件：
`OnBackdropUpdated` 里的 **`PaintBackdropFrame()`** —— 在 WGC 帧刚到达的那一刻插一次
`UpdateLayeredWindow`。这一帧与 WGC 的合成时机重叠，被怀疑就是屏幕上一闪的透明。

处置：

1. **删掉带外提交**：抓屏回调只 `EnsureFrameLoop()` 唤醒帧时钟，由常规帧循环应用新背景。
2. **渲染器兜底**：`DrawBackdrop` 里若 `PreviousBackdrop` 为空，强制 `blend = 1` ——
   否则 blend=0 会让"当前图 × 0 + 上一张 × 1"在上一张缺失时退化成一层纯 tint（透明帧）。
3. **常驻闪光探针**：`DetectTransparentFrame` 每帧检查提交后的岛中心像素，若 alpha < 16 就写一行
   `%TEMP%\fluidbar-flash.log`（含 backdrop/blend/lum/yield/opacity/material 与离屏表面同点 alpha）。

本机 20 秒无命中（说明本机不触发），需要用户机器上复现时提供该日志。

### 15.14 诊断结果与双缓冲 DIB（2026-10-23）

用户要求"自己查日志"。实际检查：

- `%TEMP%\fluidbar-flash.log` **不存在** —— 但发现探针只挂在**整帧提交**分支上，而动画期间多数帧走的是局部提交分支，
  所以它从来没被调用过（这是我的疏漏，已修复：局部分支也会检测）。
- 用 WGC 连续采样岛区域（`build/flickerprobe`，160–200 帧 @≈14 fps）：亮度序列稳定在 34.1–34.9，
  **没有任何一帧跌落**。也就是说**我们提交给系统的内容始终是完整的**。

结论：闪不是"内容错了"，而是**呈现**问题 —— 而 `LayeredSurface` 的注释本身就写着：
"draws directly into the memory the compositor uploads"（直接画进合成器要上传的那块内存）。
即使我们保证"画完再提交"，**绘制过程本身**就发生在那块内存里：60 fps 更新时，合成器随时可能读到
一半新一半旧（或已清空)的表面 → 屏幕上一闪。

修复：分层窗口改**双缓冲 DIB**（ping-pong）——

- `LayeredWindowHost` 持有两块 `LayeredSurface`，`BeginFrame`/绘制总是落在**背缓冲**；
  `CommitCore` 用背缓冲的 HDC 调 `UpdateLayeredWindow`，成功后才翻转。
- 因为局部更新在背缓冲里会读到两帧前的内容，App 侧改为**每帧整帧重绘**（`SurfaceNeedsFull`），
  渲染成本 ~1 ms，可接受。
- 顺带把集成测试对齐仓库里新出现的通知优先级策略（commit `a352e7a`，通知恢复最高优先级）。
