# 可选卡片插件

更新：2026-11-02。倒计时与文件暂存独立于内置插件目录，默认不安装、不启用。不会因为配置快捷动作而自动安装。

4.1.0 发行：文件暂存 1.0.3 的最低宿主版本为 4.1.0，旧宿主不会被提供不支持的复制/拖出契约；升级宿主后再更新插件。

2026-11-02 拖动反馈与排版：宿主通过 Windows Shell 管理跨窗口拖动图像，并在原生反馈回调中持续更新，离开卡片后仍能看到文件名、数量和复制提示。文件名最多两行、父目录分行、移除按钮独立；剪贴板采用两行摘要、底部操作栏和滚动提示，文件摘要优先显示文件名。宿主兼容原有插件，1.0.3 包更新操作提示。

2026-11-02：若文件拖入时卡片会展开、松手却无法投放，需要更新宿主的鼠标钩子修复。该问题发生在全局钩子向 OLE 转交释放事件之前，文件暂存 1.0.1 插件无需重装。

2026-11-02 拖出补齐：在卡片内按住文件行，移动超过 6 DIP 后拖到目标文件夹即可复制；Esc 取消，引用与原文件均保留。宿主兼容已有 1.0.0/1.0.1 文件行协议，更新宿主即可为旧版暂存项启用拖出。1.0.2 发布显式 `DragFiles` 并更新提示。拖放期间钩子把鼠标事件交给 OLE，不阻塞松手，卡片也不会自动收起。

| 插件 | 功能 | 源码 |
|---|---|---|
| `fluidbar-timer` | 设置 1–180 分钟，开始、暂停、继续、结束；运行时显示剩余时间，到时提醒 | [README](../../plugins/fluidbar-timer/README.md) |
| `fluidbar-file-shelf` | 拖入后可直接拖出复制，也可点击复制、全部复制、移除和清空；胶囊持续显示文件状态；最多 32 个引用，分页与滚动查看 | [README](../../plugins/fluidbar-file-shelf/README.md) |

从仓库根目录依次构建、校验、打包：

```powershell
dotnet build plugins/fluidbar-timer -c Release
dotnet run --project src/FluidBar.Cli -- validate --plugin plugins/fluidbar-timer --configuration Release
dotnet run --project src/FluidBar.Cli -- pack --plugin plugins/fluidbar-timer --configuration Release --out build/plugins/fluidbar-timer-1.0.0.fbp

dotnet build plugins/fluidbar-file-shelf -c Release
dotnet run --project src/FluidBar.Cli -- validate --plugin plugins/fluidbar-file-shelf --configuration Release
dotnet run --project src/FluidBar.Cli -- pack --plugin plugins/fluidbar-file-shelf --configuration Release --out build/plugins/fluidbar-file-shelf-1.0.3.fbp
```

本地产物为未签名包，使用设置中的「从文件安装」走现有导入、权限及信任流程；市场发布仍需原有签名发布流程。插件安装后，其命令可从「设置 → 通用 → 快捷动作」选择。

`IPluginContext.FileShelf` 是可空的宿主能力，仅在文件与 UI 权限均获准时提供。`Show` 打开宿主管理的非模态窗口；`RegisterIslandDropTarget` 注册可撤销的文件拖入回调。插件应将返回的注册纳入生命周期清理；取消/停止时关闭窗口并释放回调，不接管源文件所有权。宿主接受选择、拖入与拖出复制，插件仅存储引用。

计时器使用宿主 lease 的周期调度，仅运行中每秒更新；暂停、结束和卸载取消调度。调度额度在启动任务之前检查，并回收已完成的任务计数，避免重复暂停、继续积累历史额度。

2026-11-01：文件暂存卡片不再依赖独立窗口。`IPluginFileShelf.CopyFilesAsync` 默认返回 false，宿主实现校验文件仍存在后写入 CF_HDROP；生产宿主只向声明 `clipboard.write` 的插件提供该操作。命令参数包含宿主提供的 `pageId` 与 `rowId`，插件用稳定的文件行标识查找当前引用，不接收外部任意文件路径。复制结果显示在卡片，不会清掉引用。列表模板的滚动偏移统一用于绘制、命中和无障碍节点。
