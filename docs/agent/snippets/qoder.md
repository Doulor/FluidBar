# Qoder → FluidBar

**Status: 待确认 / 仅通用包装。**

- Qoder 是 IDE 形态的 agent（本机未安装、无可检测配置目录）；其公开 CLI/agent 事件接口没有可
  引用的 hook 文档（web search 在本环境不可用，无法进一步核实）。
- 兜底方案与 Cursor/Windsurf 相同：包装启动命令（若以 CLI 方式运行 Qoder）：

```powershell
pwsh -NoProfile -File tools\agent-emit\wrap.ps1 -Agent qoder -Title "Qoder" -Command "qoder"
```

- 纯 IDE 内部会话无法观测（无公开 hook/事件流），这与 Windsurf 的结论一致。
- 若 Qoder 后续提供 CLI hook（例如 `qoder hook ...`），用 `hook-adapter.ps1 -Agent qoder` 接入。
