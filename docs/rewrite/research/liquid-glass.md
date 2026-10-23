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
