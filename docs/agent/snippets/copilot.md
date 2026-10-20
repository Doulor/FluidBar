# GitHub Copilot → FluidBar

GitHub documents **agent hooks** for Copilot (`docs.github.com/en/copilot/concepts/agents/hooks`:
"executing custom shell commands at key points during agent execution"), so the same
`hook-adapter.ps1` works for Copilot agent sessions. Copilot CLI does not expose a stable per-step
hook on every version, so the wrapper is the fallback.

Detected on this machine: `gh` CLI (`C:\Program Files\GitHub CLI\gh.exe`).

## Hook entry (where your Copilot version supports hooks)

```json
{
  "hooks": {
    "Stop": [
      { "matcher": "", "hooks": [ { "type": "command",
        "command": "pwsh -NoProfile -ExecutionPolicy Bypass -File \"D:\\build\\GitLocal\\FluidBar\\tools\\agent-emit\\hook-adapter.ps1\" -Agent copilot -Event completed -Title \"GitHub Copilot\"" } ] }
    ]
  }
}
```

## Wrapper (universal)

```powershell
pwsh -NoProfile -File tools\agent-emit\wrap.ps1 -Agent copilot -Title "GitHub Copilot" -Command "copilot"
```

Wrapper output: running with a "正在使用 …" detail from the process start, completed on exit 0,
failed with the last error line otherwise. If your Copilot build exposes hook stdin JSON, prefer the
hook entry so tool-level detail is available.
