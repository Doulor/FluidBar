# 酷狗 seek 可行性实验结论（2026-10-21）

> 目标：确认能否绕过"酷狗没有 SMTC / 没有 seek API"，直接操作它自己的播放条。
> 方法：独立小进程（PowerShell + UIAutomationClient / oleacc），不触碰主程序。

## 实测

| 路径 | 结果 |
|---|---|
| UIA（UI Automation）顶层窗口枚举 | KuGou 进程有 **28 个 HWND**，可见主窗口 class=`k`，**UIA 只暴露 1–2 个节点** |
| UIA 查找 Slider/ProgressBar | **0 个**（RangeValuePattern 完全不可用） |
| MSAA（oleacc）| 主窗口能拿到 `IAccessible`，但 PowerShell 无法把 RCW 转成 `Accessibility.IAccessible`（双接口 RCW 限制），需 C# 侧探测 |
| 我们自己的 `PlayerAccessibilitySession`（同一棵树） | 只稳定看到：上一首/播放暂停/下一首 **三个按钮** + 一行 `mm:ss/mm:ss` 文本 |

## 结论

1. **没有可编程的 seek 句柄**：UIA 侧彻底空，MSAA 侧只有按钮和时间文本，没有 slider/progress 元素；
2. 唯一剩下的路是**坐标注入**：算出播放条矩形，按比例点一下。但——
   - KuGou 主窗口 1230×788，播放条位置随皮肤/窗口尺寸/迷你模式变化；
   - 需要先"校准"一次（用户手动点一下播放条，记录相对坐标），否则会误点；
   - 任何一次误点都可能触发"收藏/下载/切歌"。
3. **结论：不做**。收益（能拖进度条）远小于风险（误点、失效、维护成本）。

## 已经交付的替代方案

- 不可 seek 的音源（酷狗）进度条**只读化**：半透明 + 小旋钮 + 不派发 seek（`CanSeek` 透传）；
- SMTC 播放器（Edge/Spotify/系统媒体）仍然可拖；
- 歌词精度靠"秒跳变中点锚定 + 歌词偏移滑杆"补齐，不依赖 seek。
