# 设置窗口圆角：最终结论归档（task-49）

## 1. 现象

窗口四角外侧有一条 4–8 px 的中灰带（`84,84,84`），位于「窗口边缘」与「面板内容」之间；浅色主题下尤其明显（桌面 `21,21,23` → 灰 `84,84,84` ×2 → 面板白 `249,250,252`）。深色主题因为面板接近黑，灰带不明显。

## 2. 四项决定性诊断（本机，150% DPI，GPU 与 software 各测一次）

| 项 | 结果 | 说明 |
|---|---|---|
| `compositionEnabled` | **True** | `DwmIsCompositionEnabled()` 成功且为真 |
| `transparencyEffects` | **1（开）** | `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\EnableTransparency` |
| `regionHonored`（GPU） | **False** | `SetWindowRgn` 返回 `rgnType=3`、`rgnBox=1350x930`（区域完美等于窗口），但窗口内 `(607,337)` 仍是 `84,84,84`；DWM 不按区域裁剪 |
| `alphaSelfCheck`（GPU） | **False** | 窗口内 3px 与窗外 12px 的像素差值 > 12 → 合成表面不透明，产品自动降级为 `chrome=Opaque` |

结论：**系统透明效果是开启的，但这条合成路径既不尊重窗口区域，也不交付逐像素 alpha**；Avalonia 的 `ActualTransparencyLevel` 报告为 `Transparent`，与 DWM 实际合成结果不一致（平台层事实，不是配置问题）。

## 3. 排除实验（证明灰带不是我们画的）

| 实验 | 结果 |
|---|---|
| `DwmExtendFrameIntoClientArea({-1,-1,-1,-1})` | `hr=0` 成功，灰带**不变** |
| `WS_EX_LAYERED` | 设置成功（exstyle 含 `0x80000`），灰带**不变** |
| `hbrBackground` 改纯红 `COLORREF 0x0000FF` | `SetClassLongPtr` 返回旧刷子（生效），灰带**没有变红** |
| `DWMWA_BORDER_COLOR = COLOR_NONE` | 灰带不变 |
| `DWMWA_NCRENDERING_POLICY = DISABLED` | 灰带不变 |
| Avalonia 满铺面板色底衬 / 窗口背景按主题刷新 | 灰带不变 |

→ 灰带位于客户区之外，是 **DWM 给圆角窗口合成的边缘/阴影层**，与我们的画刷、类背景、区域都无关。

## 4. 原生窗口对照（Windows 计算器，同样的对角线取样）

```
30,30,30 / 0,0,0 / 0,0,0 / 0,0,0 / 28,28,28 / 28,28,28 / 27,27,27 / 26,26,26 / 62,62,62 / 26,33,46 ×4
```

系统原生圆角窗口也有同样形态：桌面 → 若干过渡灰 → 一条更亮的边缘（`62,62,62`）→ 内容。**这说明「圆角窗口边缘有一层 DWM 合成带」是 Win11 的通用观感**，不是本应用特有缺陷；差别只在宽度（原生约 2px，我们这条约 4px，因为我们的面板比系统窗口更暗、对比更强）。

## 5. 当前实现（主干）

1. **GPU 默认**：`AvaloniaRenderingPolicy` 默认 GPU；software 仅显式 override（内存 +41MB 工作集 / +85MB 私有）。
2. **透明优先**：`Window.Background=Transparent` + 面板 22 DIP 圆角 + 1px 内描边。
3. **运行时降级**（已实测触发）：
   - `ActualTransparencyLevel != Transparent` 或远程会话 → Opaque；
   - **alpha 自检**：抓屏比对窗口角内/窗外像素，差值 > 12 → Opaque；
   - Opaque 路径 = DWM 圆角（8 DIP）+ 窗口背景与类背景均取面板色（随主题刷新）+ 去掉 DWM 边框/内描边；
   - `SetWindowRgn` 保留为兜底（本机不生效，但在尊重区域的合成路径上有用）。
4. 诊断输出（RegionDiag）：`chrome / actual / radiusDip / bgAlpha / dwmRounding / compositionEnabled / transparencyEffects / rgnType+rgnBox / workingSetMB / privateMB` + 对角线取样。

## 6. 三个选项与工作量

| 选项 | 观感 | 工作量/风险 |
|---|---|---|
| **A. 接受 DWM 边缘** | 原生 Win11 圆角观感（半径 8 DIP，不可到 22），保留 4px 边缘带 | 已完成；只需按选定值微调（半径/是否保留 1px 描边） |
| **B. 方角面板色窗口** | 无灰带、无阴影、无圆角（radius 0） | 1 处常量改动，10 分钟 |
| **C. 真圆角 hack（抓桌面角部填充 + 移动/缩放时重采样）** | 可做到 22 DIP 真透明观感 | **2–3 天**：需要常驻抓屏（每次移动/缩放/切主题重采样）、多显示器/DPI 变化处理、性能与隐私风险（持续抓屏）、失败回退路径；不建议 |

## 7. 归档

- 诊断命令：`$env:FLUIDBAR_AVALONIA_RENDERING='software'; artifacts\settings-ui\harnbin\SettingsShot.exe <out> zh-Hans RegionDiag`
- 取样/截图：`artifacts/settings-ui/round13`（浅/深主题对角线）、`round14`（三个排除实验）、`round15`（GPU/software 四项诊断）、`round16`（原生计算器对照）
- 全量测试：Island 421 / Platform 658 / Ui 453 / 其余 645 —— **2373 全绿**

## 8. 最终决策（方案 B）与退路 5 的结构性分析

**决策：默认采用方案 B——方角、面板色窗口、无 DWM 圆角、无边缘带。** 取样验证：浅色 桌面 x3 → 描边 → 面板白 x9，深色 桌面 x3 → 描边 → 面板深色 x10，84,84,84 完全消失。

分层窗口（WS_EX_LAYERED + UpdateLayeredWindow）已在 LayeredWindowSurface / LayeredWindowPresenter 中实现并可通过 FLUIDBAR_LAYERED=1 启用：容器层工作正常（radiusDip=22、bgAlpha=0、窗口位置正确），但 Avalonia 的 RenderTargetBitmap.Render(visual) 在本窗口被 DWM/DComp 承接的情况下拿不到面板内容，推上去的是一块纯色位图（抓屏取样全为同一值）。要继续需要改成渲染前强制 Measure/Arrange，或直接从 Skia 合成结果取像素。

**退路 5（layered 父层 + 圆角内子窗口）的结构性障碍**：方案要求子窗口把内容满铺到面板边缘，而子窗口是矩形的；要让子窗口的直角不覆盖父层圆角弧，就必须让子窗口内缩大约半径距离（22 DIP）。对满铺式设置窗口（导航栏/标题栏贴边）来说，这会产生一圈明显的边框，视觉上比方案 B 更差。因此该退路不适合本设计，不再投入。

**归档结论**：在该合成路径上（默认 DComp；软件渲染与 GPU 均实测）客户端 alpha 与窗口区域都不可用，DWM 圆角又必然带 2-4 px 合成边缘带；在内容满铺的设计约束下无法实现真 22 DIP 圆角。最终采用方案 B：方角面板色窗口 + 1 px 内侧描边。若未来平台交付真 alpha（换渲染后端或新系统版本），可用 FLUIDBAR_WINDOW_ALPHA=1 走 22 DIP 透明路径（代码与测试均已就绪）。

## 9. 已尝试的六条路线（全部实测）与结论

| # | 路线 | 结果 |
|---|---|---|
| 1 | SetWindowRgn 裁剪圆角 + 类背景改面板色 + 吞 WM_ERASEBKGND | 区域报告成功（rgnType=3、rgnBox == 窗口矩形），但 DWM 合成时不裁剪，四角仍是平台清屏灰 |
| 2 | DWM 自身圆角（DWMWA_WINDOW_CORNER_PREFERENCE=ROUND）+ 窗口底色铺面板色 | 灰块主因消失，但 DWM 在圆角外合成一条 2-4px 边缘带（84,84,84），且半径固定 8 DIP，无法对齐设计 22 DIP |
| 3 | 请求客户端逐像素 alpha（TransparencyLevelHint=Transparent + Background=Transparent + 22 DIP 圆角） | ActualTransparencyLevel 报 Transparent，但合成结果不透明（alpha 自检判否），四角仍灰 |
| 4 | 强制六条配方（SystemDecorations=None、Transparent hint、透明背景、GPU、不设 layered、DWM DONOTROUND） | 带反而更宽（2 → 5 个采样点），确认平台不交付客户端 alpha |
| 5 | layered 父层 + 圆角内子窗口 | 结构性不可行：子窗口为矩形，必须内缩约 22 DIP 才能不盖住圆角弧，满铺式布局会出现明显边框，比方案 B 更差 |
| 6 | UpdateLayeredWindow 直接承接 Avalonia 渲染（WS_EX_LAYERED + RenderTargetBitmap 推送） | 容器层可用（radius=22、bgAlpha=0、位置正确），但 Avalonia 在本窗口被 DWM 承接时拿不到内容位图，推送帧为纯色；继续需要改为渲染前强制 Measure/Arrange 或直取 Skia 合成结果 |

**结论性建议**：若必须在满铺式布局下得到真圆角，唯一可行路线是设置窗口改用与岛相同的自绘 Skia 渲染（工作量大，不建议在现有 Avalonia UI 上继续尝试）。

## 10. 更正：DWM 圆角本身可用（之前判定为测量/实现假象）

置顶窗口 + 窗口矩形内部取样（GetWindowRect 起点沿对角线 20 点）后重测，结论推翻此前判断：

- 假象 1（取样错窗口）：采样时其他应用（聊天窗口）盖在设置窗口上，读到的像素来自别的窗口。现在探针在 Show() 后强制 Topmost + SetWindowPos(HWND_TOPMOST) + SetForegroundWindow 再取样。
- 假象 2（灰色来自我们自己的圆角壳）：此前 _shell 也切成 8 DIP 圆角，它的圆角让窗口表面在四角露出一块未绘制区域（平台清屏灰 84,84,84）。把 _shell 改回方角、满铺后，DWM 直接在面板上切圆角，灰色楔形彻底消失。

实测对角线（浅色）：205,205,205 / 202,202,202 / 198,198,198 / 221,222,222 / 249,250,252 x16
实测对角线（深色）：177,194,201 / 174,191,197 / 171,188,194 / 94,102,107 / 12,12,15 x16

前 3-4 个点的数值随桌面内容变化（= DWM 圆角 AA/阴影混合），没有恒定的 84,84,84 带。8 倍放大图：artifacts/settings-ui/round23/corner-light-8x.png 与 corner-dark-8x.png。

**结论：默认启用 DWM 圆角（系统半径 8 DIP）+ 方角满铺面板 + 面板同色 + 无描边。** 用户要的圆角已成立；22 DIP 半径受系统限制不可得（DWM 只提供 8/4），但不再是方角。
