# FluidBar v2 — 当前状态（STATUS）

> 最后更新：2026-10-19 16:40（Asia/Shanghai）
> 维护规则：每个检查点由 Lead 更新本文件；数字必须来自实测。

## 一句话状态

**渲染提交根因已修复**：分层窗口在“隐式几何”更新下返回成功但不刷新（只有首帧上屏），导致展开/翻页全部不可见；现在每次提交都显式传 `pptDst`/`psize`，真实鼠标点击 → 弹簧展开 → 面板可见、悬停不收起、移开 2.6s 后收起、点击穿透恢复。

## 里程碑

| 里程碑 | 状态 | 证据 |
|---|---|---|
| M0 分支/骨架/CI/渲染决策 | ✅ | `v2/rebuild`、`v1-final`、spec/plan/DECISIONS |
| M1 Core 领域层 | ✅ | 473 tests |
| M2 插件 SDK/宿主/市场 | ✅ | 188 tests；离线 E2E |
| M3 Windows 平台 | ✅ | 371 tests；M3+P3 |
| M5 内置插件/市场 CI/文档 | ✅ | 23 builtin + 31 integration；`docs/plugin/` |
| M4 UI / Skia 宿主 | ✅（MVP） | 新分层宿主 + Skia 渲染已接入，默认 `FLUIDBAR_HOST=skia`；108 UI tests；旧 Avalonia 宿主保留 fallback |
| M6 硬化/发布 | ⏳ | soak/性能预算已就绪；Release 浸泡待做 |

## 测试（当前）

| 套件 | 数量 | 状态 |
|---|---:|---|
| Core | 491 | ✅ |
| Platform | 409 | ✅（偶发 HWND/网络 flaky，单独运行稳定） |
| SDK | 152 | ✅ |
| PluginHost | 39 | ✅ |
| Builtin | 23 | ✅ |
| Integration | 31 | ✅ |
| UI | 184 | ✅ |
| Architecture | 11 | ✅ |

构建：`dotnet build FluidBar.sln` = 0 errors；`dotnet test FluidBar.sln -m:1` = **1482 passed / 0 failed**
（Core 494 · Platform 409 · SDK 152 · UI 188 · Island 135 · PluginHost 39 · Builtin 23 · Integration 31 · Architecture 11）。

## Spike 结论

- **分层宿主**：`UpdateLayeredWindow` 全帧 0.06–0.16ms / 脏矩形 0.08–0.10ms；60/120fps pacing 通过；透明、点击穿透、不抢焦点、拖拽 PASS。宿主表面 float 488×384 / dock 488×444 DIP。
- **Skia 渲染器**：展开 0.55–0.61ms / p99 0.85ms；紧凑 0.46ms；脏矩形 0.157ms；120fps 约 9.8× 余量；fidelity IoU 0.9986 / 1.0000。
- **Avalonia 透明**：11.3.22 与 12.1.4 在本机均失败（灰矩形），弃用为岛宿主；仅保留设置/市场窗口。

## 2026-10-19 16:40 检查点（可用性修复）

- 修复 **分层提交 bug**（唯一阻塞）：`UpdateLayeredWindowIndirect` 隐式几何返回 TRUE 但不更新表面；改为一律显式几何，删除 `UseExplicitUpdateGeometry` / `_geometryDirty`。
- 恢复**弹簧动画**（不再在状态变化时 SnapMotion）与**点击穿透**（钩子命中矩形回到 shell + 2 DIP）。
- **悬停不自动收起**：展开态 hover 清除 auto-collapse deadline，指针离开后重新武装；钩子新增 `PointerLeft`。
- 修复**紧凑药丸宽度/内容不一致**（活动结束残留 ContentKind）与**空闲时钟不走字**（分钟边界 ITimer）。
- 清理全部临时调试日志。
- 首页音量/亮度改为真实数据（未知显示 "—"）；`MonitorService` 枚举前强制 PMv2 线程上下文。
- 最终 Release：`build/release/win-x64/FluidBar.exe` 56.5 MB，SHA256 `1DE62352…31BAA`；真实鼠标回归通过（展开/悬停保持/chevron 收起/翻页/真实音量）。
- 待验证：真实鼠标下的剪贴板行复制、媒体控件、开关点击、托盘右键菜单、UIA 树刷新、多显示器/200%。

## 进行中

1. **Skia 渲染器：134 tests + 20 张 golden**（紧凑 8 态、展开 home/media/notifications/clipboard/agent/widgets、dock/flush/light/transition、transient strip）；壳体描边正在改中性 hairline。
2. **prototype2：设置面板/插件市场/3 小岛/远处页小点/切换动效已完成**（14+ 截图）；等待用户验收。
3. **App 集成（MVP + 插件运行时）**：Skia 宿主默认启用；页面控件 seam、多显示器/DPI/dock、tray+热键、真实 PluginHost/市场适配、FollowMouse 与锚点拖拽持久化均已接入；smoke 331 帧/0 提交失败、surface 732×576 @150%。UIA 无障碍 provider（根/紧凑/页面/控件/strip、模式、焦点环、键盘）已接入（[`contracts/island-uia.md`](contracts/island-uia.md)）。待补：IME、Wi-Fi/BT/Focus、output/queue、热键注入复测、双 message host 合并、真机多显示器/200% 验收。

## 可用版本预估（基于当前进度）

| 目标 | 范围 | 预估 |
|---|---|---|
| 内部预览 | 紧凑岛 + 展开 home/media + 设置 + 托盘，真实透明宿主 | **~1–1.5 周** |
| **可用初代版本** | 上述 + 通知/剪贴板基础 + 插件列表 + 热键/托盘 + 桌面验收 | **~3 周（约 15 个工作日）** |
| v2 完整版 | 全部页面/插件/市场/UIA/多显示器/发布链路 | **~8–12 周** |

关键风险：App 宿主集成（最大未知量）、UIA 无障碍、多显示器真机复测、市场签名密钥/托管（Q3–Q5）。

## 阻塞 / 风险

- 多显示器真机复测（本机只暴露 1 块屏）。
- UIA 无障碍（自绘岛需要实现 UIA provider）。
- 插件市场生产签名密钥/托管 URL（Q3–Q5，待 Doulor 决定）。
- Avalonia UI 的旧岛代码将在迁移完成后删除；设置/市场保留。

## 下一步（按顺序）

1. 完成 Skia 渲染器 MVP（紧凑 + 主页/媒体 + pager + 小岛/小点）。
2. 接入分层宿主到 App，跑真机桌面验收（透明/点击穿透/DPI/60-120fps）。
3. 补全通知/剪贴板/Agent/小组件页面与 UIA。
4. Release 浸泡、单文件发布、市场发布链路。








