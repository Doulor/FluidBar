# Windsurf → FluidBar

Windsurf (Codeium) is a pure IDE; Cascade runs inside the editor and its docs expose MCP servers and
rules but **no user shell hook / event stream** for agent lifecycle (checked 2026-10-20; the old
docs.windsurf.com page redirects to docs.devin.ai and still documents no hooks). 

## Feasible only as a wrapper

```powershell
pwsh -NoProfile -File tools\agent-emit\wrap.ps1 -Agent windsurf -Title "Windsurf" -Command "windsurf"
```

## Why full support is not possible

No documented hook or CLI event stream: the editor owns the agent loop. Any "real-time" row would have
to be inferred from file changes or window titles (unreliable, not implemented).
