# 可选卡片插件

更新：2026-10-31。倒计时与文件暂存独立于内置插件目录，默认不安装、不启用。不会因为配置快捷动作而自动安装。

| 插件 | 功能 | 源码 |
|---|---|---|
| `fluidbar-timer` | 设置 1–180 分钟，开始、暂停、继续、结束；运行时显示剩余时间，到时提醒 | [README](../../plugins/fluidbar-timer/README.md) |
| `fluidbar-file-shelf` | 文件拖入灵动岛、选择文件、拖出复制；最多 32 个引用，清空不删除源文件 | [README](../../plugins/fluidbar-file-shelf/README.md) |

从仓库根目录依次构建、校验、打包：

```powershell
dotnet build plugins/fluidbar-timer -c Release
dotnet run --project src/FluidBar.Cli -- validate --plugin plugins/fluidbar-timer --configuration Release
dotnet run --project src/FluidBar.Cli -- pack --plugin plugins/fluidbar-timer --configuration Release --out build/plugins/fluidbar-timer-1.0.0.fbp

dotnet build plugins/fluidbar-file-shelf -c Release
dotnet run --project src/FluidBar.Cli -- validate --plugin plugins/fluidbar-file-shelf --configuration Release
dotnet run --project src/FluidBar.Cli -- pack --plugin plugins/fluidbar-file-shelf --configuration Release --out build/plugins/fluidbar-file-shelf-1.0.0.fbp
```

本地产物为未签名包，使用设置中的「从文件安装」走现有导入、权限及信任流程；市场发布仍需原有签名发布流程。插件安装后，其命令可从「设置 → 通用 → 快捷动作」选择。

`IPluginContext.FileShelf` 是可空的宿主能力，仅在文件与 UI 权限均获准时提供。`Show` 打开宿主管理的非模态窗口；`RegisterIslandDropTarget` 注册可撤销的文件拖入回调。插件应将返回的注册纳入生命周期清理；取消/停止时关闭窗口并释放回调，不接管源文件所有权。宿主接受选择、拖入与拖出复制，插件仅存储引用。

计时器使用宿主 lease 的周期调度，仅运行中每秒更新；暂停、结束和卸载取消调度。调度额度在启动任务之前检查，并回收已完成的任务计数，避免重复暂停、继续积累历史额度。
