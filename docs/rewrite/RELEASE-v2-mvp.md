# FluidBar 2.0 — MVP 发布说明

> 版本：v2.0.0-mvp（2026-10-19）
> 分支：`v2/rebuild`；基线标签：`v1-final`
> 目标：Windows 10/11 x64，Per-Monitor V2，150% DPI 已验证

## 这是什么

FluidBar 2.0 是从零重写的 Windows 灵动岛：默认 iOS 风格小岛，点击展开为控制中心，可选 Dock 模式，支持媒体/通知/剪贴板/Agent/小组件页面、托盘与全局热键、插件运行时与市场、中英文运行时切换。

这一版是 **MVP（可日常运行）**：核心宿主/渲染/交互/插件/设置/市场链路已打通，剩余差距见文末。

## 运行

- 发布产物：`build/release/win-x64/FluidBar.exe`（framework-dependent single-file，56.5 MB，SHA256 `86A4F33A…A4021`）；压缩包 `build/release/FluidBar-win-x64.zip`（47 MB）
- 或从源码：`dotnet run --project src/FluidBar.App`（开发） / `tools/release/publish.ps1`
- 默认宿主：Win32 分层窗口 + Skia（`FLUIDBAR_HOST=skia` 强制；`avalonia` 回退旧宿主）
- 设置/市场：托盘菜单或 `Ctrl+Alt+S`（若被占用回退 `Ctrl+Alt+Shift+S`）

## 已实现

### 宿主 / 渲染
- Win32 分层窗口（`UpdateLayeredWindow`，32bpp 预乘 DIB），逐像素透明，固定宿主表面 488×384 float / 488×444 dock
- SkiaSharp + HarfBuzz 渲染：紧凑 8 态、展开 home/media/notifications/clipboard/agent/widgets、面板外 3 个临时小岛 + 远处页小点
- 150% DPI 实测：surface 732×576；全帧 ~0.5–1.9 ms，脏帧 ~0.1–0.2 ms；60/120 fps pacing
- 透明、点击穿透、不抢焦点、拖拽、多显示器热插拔、DPI 重建、dock 组合与 Core 对齐

### 交互
- 小岛默认 → hover 预览 → 点击展开；Esc/点击外部收起
- 横向 pager：拖动跟随 0.85、吸附 T 0.38/ζ 0.94、滚轮 140px/180ms、滚动链 40px+120ms、轻扫≤1页/长拖多页
- 面板外 3 个临时小岛（26/26 + 64×28）与远处页小点（每侧≤3，可点击）
- FollowMouse、锚点拖拽持久化（click-vs-drag 阈值、吸附/写回/恢复）
- 托盘 + 全局热键（Ctrl+Alt+D 切换；H/S 冲突回退 Ctrl+Alt+Shift）

### 页面与平台
- 媒体播放/暂停/上下曲/seek、音量、亮度、主题切换、剪贴板复制
- 通知/Agent/任务/日历为 deferred App 事件；Wi-Fi/Bluetooth 真实开关；Focus 在 Windows LAF 受限时显式 unsupported
- 设置 7 分区（通用/外观/灵动岛/插件/插件市场/高级/关于），实时外观预览

### 插件 / i18n
- 真实 PluginHost + Marketplace：发现/加载/启用、浏览/安装/回滚、权限同意、插件设置页；设计态服务作为离线回退
- 中英文运行时切换；插件自带语言包；pseudo-locale 支持

### 无障碍
- UIA raw provider：根/紧凑/页面/控件/小岛元素树，Invoke/ExpandCollapse/RangeValue/SelectionItem/Toggle 模式，Tab/方向键/Enter/Escape，焦点环
- 文档：`docs/rewrite/contracts/island-uia.md`（含 Narrator 手工清单）

## 验证

- `dotnet build FluidBar.sln` = 0 errors
- `dotnet test FluidBar.sln -m:1` = **1475 passed / 0 failed**
  - Core 491 · Platform 409 · UI 184 · Island 135 · SDK 152 · PluginHost 39 · Builtin 23 · Integration 31 · Architecture 11
- 生产 smoke（`FLUIDBAR_SMOKE=1 FLUIDBAR_HOST=skia`）：surface 732×576 @150%，200+ 帧、0 提交失败、上传 ~110–220 µs、空闲无帧
- Release 发布 smoke 通过（`build/release/win-x64/FluidBar.exe`）

## 已知限制（非阻塞）

1. **Focus/专注**：Windows `FocusSessionManager` 写方法是 Limited Access Feature（需 Microsoft 令牌）；当前显式 `unsupported`，不伪造开关。
2. **输出/队列**：无公开的音频输出选择器 / GSMTC 队列 API；按钮 disabled + 原因。
3. **IME/文本编辑**：岛内没有文本输入控件；设置/市场窗口由 Avalonia 处理 IME。
4. **MSAA/OBJID_CLIENT**：仅实现 UIA raw provider；旧式 MSAA 客户端未覆盖。
5. **多显示器/200%**：逻辑与单元测试覆盖，真机只有单显示器 150% 环境；需在双屏/200% 机器上复测。
6. **Narrator 手工验收**：UIA 自动化测试通过，仍需按清单人工跑一次 Narrator。
7. **托盘图标**：已从 exe 提取 FluidBar 图标（`System.Drawing.Common` 9.0.0）；若提取失败回退系统默认图标。
8. **双 PlatformMessageHost**：tray/hotkey 与页面控件各持一个消息宿主；功能正常，后续可合并。
9. **插件市场生产密钥/托管**：CI 已就绪，签名密钥与发布环境需维护者配置。
10. **热键编辑**：设置 → 通用中的三个全局热键可直接编辑，保存后立即重新注册；无效/被占用的组合会回退到 Ctrl+Alt+Shift 变体。

## 下一步（完整版）

- IME/文本、MSAA、属性变更/实时区域事件
- 输出/队列服务、Focus LAF 令牌（如微软提供）
- 多显示器/200% 真机验收、Narrator 手工验收
- 合并 message host、托盘自定义图标
- 市场生产签名/托管、发布硬化

## 文件

- 设计规范：`docs/rewrite/specs/2026-10-18-fluidbar-v2-design.md`
- 决策日志：`docs/rewrite/DECISIONS.md`
- 状态：`docs/rewrite/STATUS.md`
- 契约：`docs/rewrite/contracts/`（core/platform/plugin/ui/layered-host/skia-host/island-uia/skia-host-acceptance）
- 原型：`docs/rewrite/prototype2/`
- 发布脚本：`tools/release/publish.ps1`
- 验收清单：`docs/rewrite/contracts/skia-host-acceptance.md`



