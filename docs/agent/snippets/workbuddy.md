# WorkBuddy → FluidBar

**Status: 仅通用包装 / 实时状态不可行（截至 2026-10-20）。**

本机实测：
- 安装目录 `C:\Program Files\WorkBuddy` 是 **Electron 桌面客户端**（WorkBuddy.exe、resources、
  locales；无 CLI 可执行文件）。
- 用户配置 `%APPDATA%\WorkBuddy` 与 `%APPDATA%\WorkBuddyManager` 是应用数据目录，没有可写入的
  hook/agent 事件配置层。
- 没有公开的 agent 生命周期 hook 或 CLI 事件流（本环境无法 web search，按已抓取公开资料与本地
  安装形态判断）。

## 能做的

如果 WorkBuddy 允许以命令方式拉起任务，或它有可调用的命令行入口，用通用包装器：

```powershell
pwsh -NoProfile -File tools\agent-emit\wrap.ps1 -Agent workbuddy -Title "WorkBuddy" -Command "WorkBuddy.exe"
```

## 做不到的部分及原因

- IDE/客户端内部会话没有对外 hook：无法在不修改 WorkBuddy 的前提下拿到 running/completed 事件；
  轮询窗口标题或日志属于猜测，不实现（避免伪造状态）。
- 建议向 WorkBuddy 反馈：需要 `agent lifecycle hook`（start/tool/finish）或 CLI JSON 事件流，
  届时用 `hook-adapter.ps1 -Agent workbuddy` 一行接入。
