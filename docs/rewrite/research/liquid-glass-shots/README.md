# 液态玻璃材质截图

由 `IslandRenderer.RenderToBitmap` 直接输出（不经过窗口捕获），150% DPI。

| 文件 | 内容 |
|---|---|
| `material-compare.png` | 合成对照：Obsidian / 亚克力 / 液态玻璃 三种材质的小岛（同一媒体活动），下方为液态玻璃展开面板；背景为合成「桌面」以便观察透明度 |
| `compact-liquid.png` | 液态玻璃小岛（整张宿主表面，含透明内边距） |
| `expanded-liquid.png` | 液态玻璃展开面板（整张宿主表面） |
| `prototype-liquid.png` | `docs/rewrite/prototype2` 的液态玻璃近似（headless Chromium 截图，1440×900，设置 → 外观） |

重新生成：本目录的图片来自本地渲染探针（`IslandRenderer.RenderToBitmap` + SkiaSharp 合成），
探针脚本不入库；用任意 `IslandRenderer` 调用即可复现同样的状态（见 `LiquidGlassTests` 的 fixture 用法）。
