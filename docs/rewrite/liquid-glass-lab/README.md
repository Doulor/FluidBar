# 液态玻璃 · 网页原型（选型用）

> 直接双击 `index.html` 用 **Edge / Chrome** 打开（`backdrop-filter` + SVG 滤镜只在 Chromium 内核可用）。
> 默认打开 B（折射玻璃）；全览模式会把 7 个版本同时渲染在一个页面里。
> 右侧「微调」可以实时改 **模糊 / 染色 / 饱和 / 边缘高光 / 折射强度**，点「复制参数」把数值发我。
> 「真实尺寸」把预览切到 App 的实际几何：紧凑胶囊 **132×36**、展开面板 **440×284**（截图 `shots/real-size.jpg`）。
> 顶部可切换 6 个版本；也可以按 `1`–`6` 切换、`B` 开关背景模糊、`G` 全览对比。

## 为什么重做

现在 App 里的液态玻璃是"画"出来的（深色填充 + 一圈透镜/高光），没有背景模糊，所以看起来像老式电视的塑料壳。
真正的液态玻璃必须**先拿到背后的画面再做高斯模糊**，然后才是折射、色散和高光。这套原型就是按这个顺序做的：

1. `backdrop-filter: blur(…) saturate(…)` —— 真·背景高斯模糊 + 提饱和；
2. 低透明度染色（tint）—— 决定玻璃"厚不厚"；
3. 边缘折射（SDF 位移，Chromium 的 `backdrop-filter: url(#filter)`）；
4. 1px 镜面高光 + 柔和外阴影。

## 六个版本

| # | 名称 | 技术 | 关键参数 | 适合 |
|---|---|---|---|---|
| A | 磨砂玻璃 | CSS blur | blur 28px · sat 185% · tint 34% | 最稳，接近 iOS `.regular`，任何壁纸都好看 |
| B | 折射玻璃 | blur + SDF 位移 + 色散 | blur 10px · 位移 ±18px · 色散 0.05–0.1 | 最像"液态玻璃"的网页实现，动效/媒体态 |
| C | 清透玻璃 | CSS blur | blur 12px · sat 210% · tint 14% | 照片/视频背景，接近 iOS `.clear` |
| D | 厚镜玻璃 | 强折射 | blur 16px · IOR 2.4 · 折射带 26px | 最接近 iOS 26 lensing，视觉冲击最强 |
| E | 水色玻璃 | 折射 + 青色染色 | blur 12px · tint rgba(26,96,122,.28) | 媒体播放态 / 品牌色，但会改变背景色相 |
| F | 烟灰玻璃 | CSS blur | blur 34px · sat 150% · tint 46% | 深色桌面/亮壁纸，最接近现在的深色岛 |
| G | 纯玻璃 | CSS blur | blur 26px · sat 170% · tint 30% · **无描边** | 觉得"边"很假时选这个，只有模糊+染色+柔和阴影 |

截图：

| 文件 | 内容 |
|---|---|
| [`shots/grid.jpg`](shots/grid.jpg) | 页面「全览」模式实拍（6 个版本同屏，含真实 backdrop-filter） |
| [`shots/overview.jpg`](shots/overview.jpg) | 6 个版本各裁一张的 3×2 拼图 |
| `shots/A-frosted.jpg` … `G-pure.jpg` | 单版本截图（7 张） |
| [`shots/A-frosted-blur-off.jpg`](shots/A-frosted-blur-off.jpg) | 同一版本关掉 `backdrop-filter` 的对照（"模糊" vs "只是变暗"） |
| [`shots/B-vs-reference.jpg`](shots/B-vs-reference.jpg) | B 版本 vs 参考实现 `archisvaze/liquid-glass` |
| [`shots/real-size.jpg`](shots/real-size.jpg) | 真实尺寸模式（132×36 胶囊 + 440×284 面板，与 App 一致） |

## 可读性（同一张亮壁纸实测）

白色文字压在玻璃上的对比度（平均，整块玻璃面积）：

| 版本 | 玻璃平均亮度 | 白字对比度 | 结论 |
|---|---:|---:|---|
| A 磨砂 | 0.49 | 4.2:1 | 接近 AA |
| B 折射 | 0.48 | 4.2:1 | 接近 AA |
| C 清透 | 0.61 | 2.8:1 | 亮背景下偏弱 |
| D 厚镜 | 0.54 | 3.4:1 | 亮背景下偏弱 |
| E 水色 | 0.59 | 3.0:1 | 亮背景下偏弱 |
| F 烟灰 | 0.43 | 5.1:1 | AA |
| G 纯玻璃 | 0.51 | 3.9:1 | 接近 AA |

> 选 C/D/E 的话，移植时我会加"按背景亮度自适应加深染色"（iOS 的 adaptive tint），否则亮壁纸上白字不够清楚。

## 分享参数

右侧微调后的数值可以用 URL 直接分享（也是我移植时的输入）：

`index.html?v=b&tune=blur:10;tint:30;sat:160;edge:55;refract:100`

「复制参数」按钮会把这组数字复制到剪贴板。

## 参考实现

- `rdev/liquid-glass-react`（7k★）：`feImage` 位移图 + `feDisplacementMap` + RGB 通道错位色散
- `archisvaze/liquid-glass`：`calculateRefractionProfile`（Snell）+ `generateDisplacementMap`（圆角矩形 SDF）+ `generateSpecularMap`
- `shuding/liquid-glass`、`hyalite`、`ybouane/liquidglass`：位移图/色散参数
- Apple HIG / WWDC25：`.regular` / `.clear` 两种变体与"材质必须随背景自适应"
- 参考截图：`shots/reference-archisvaze.jpg`

本原型的位移图/镜面图生成移植自 `archisvaze/liquid-glass`（MIT），滤镜链参考 `rdev/liquid-glass-react`（MIT），
背景照片来自 `deepika-builds/liquid-glass` 的 demo（MIT）。

## 选中之后

告诉我编号（或"B + F 的混合"），我再把它移植到 `FluidBar.Island`：

- 背景模糊：分层窗口挡住 `DWM` 的 backdrop，但已验证 `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` 后
  `BitBlt` 能抓到窗口**背后**的桌面（探针实测：窗口内像素从纯红 255,0,0 变成桌面 67,69,74），
  再把抓到的画面做两级下采样高斯模糊 + 饱和提升，贴进壳体；
- 边缘折射/色散：在 Skia 里对模糊后的背景做逐像素位移（SDF），比浏览器滤镜更可控；
- 高光/阴影：按选中的参数重建，去掉现在那圈"厚黑边"。
