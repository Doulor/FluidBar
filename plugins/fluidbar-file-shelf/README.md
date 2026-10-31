# 文件暂存插件

2026-10-31。独立可选插件，默认不安装、不启用。

安装并启用后，将文件拖到灵动岛，或在插件页打开文件暂存窗口，通过拖入/选择文件添加引用。拖动窗口中的文件名可复制到其他应用。最多保留 32 项引用；移除和清空只改变引用列表，从不移动、删除或重命名原文件。

构建：`dotnet build plugins/fluidbar-file-shelf -c Release`。
打包：`dotnet run --project src/FluidBar.Cli -- pack --plugin plugins/fluidbar-file-shelf --configuration Release --out build/plugins/fluidbar-file-shelf-1.0.0.fbp`。
安装入口为设置中的「从文件安装」。新宿主提供文件暂存 UI 能力；不支持该能力的旧宿主会明确报告不可用。
