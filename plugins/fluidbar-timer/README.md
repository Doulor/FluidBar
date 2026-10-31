# 倒计时插件

2026-10-31。独立可选插件，默认不安装、不启用。

安装后在插件页设置时长（1–180 分钟），点击开始；支持暂停、继续、结束。运行期间小岛显示剩余时间，到时提醒；暂停和结束后不保留轮询。

从仓库根目录构建：`dotnet build plugins/fluidbar-timer -c Release`。
打包：`dotnet run --project src/FluidBar.Cli -- pack --plugin plugins/fluidbar-timer --configuration Release --out build/plugins/fluidbar-timer-1.0.0.fbp`。
通过设置中的「从文件安装」选择 .fbp 后启用。此仓库不自动安装该插件。
