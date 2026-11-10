# FluidBar 图标

`fluidbar.svg` 是软件图标的统一源文件，使用原版蓝色液面方案。EXE、系统托盘、窗口 / 任务栏与设置中的品牌标识均从它生成。

从仓库根目录运行：

```powershell
node scripts/generate-app-icons.mjs
```

生成脚本使用网站已有的 Playwright、Microsoft Edge 和 Python Pillow，按 SVG 原样渲染，再导出：

- `fluidbar.ico`：16、20、24、32、40、48、64、128、256 像素，用于 EXE、系统托盘及窗口/任务栏。
- `fluidbar.png`：512 像素，用于设置窗口标题栏及关于页。
- `build/reports/app-icon/sizes.png`：浅色和深色背景上的尺寸检查图。

衍生文件已保存在仓库中，日常构建不需要运行生成脚本。托盘直接使用内嵌 ICO，不再从 EXE 抽取单一尺寸。
