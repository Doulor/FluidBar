# zcode → FluidBar

**Status: 待确认 / 仅通用包装。**

- 本机未检测到 zcode 的 CLI（`Get-Command zcode` 为空）或安装目录；公开文档没有可引用的
  agent 生命周期钩子规范（本环境 web search 不可用，仅能核对已抓取的公开页面）。
- 因此无法给出经过验证的官方钩子接入；可用的兜底方案是通用包装器：

```powershell
pwsh -NoProfile -File tools\agent-emit\wrap.ps1 -Agent zcode -Title "zcode" -Command "zcode"
```

- 如果你能提供 zcode 的实际 CLI 名称、配置目录或 hook 文档，按
  `docs/agent/snippets/claude-code.md` 的模式补一个 `hook-adapter.ps1 -Agent zcode -Event ...`
  即可升级为实时状态。
