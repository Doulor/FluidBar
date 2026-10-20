# Cursor → FluidBar

Cursor is an IDE with an embedded agent. Its public docs (cursor.com/agent/cli, checked 2026-10-20)
describe the `cursor-agent` CLI but **no user lifecycle hook** comparable to Claude Code hooks, and
the IDE has no shell-hook extension point. Integration is therefore a **wrapper around the CLI/Agent
session**, or an external watcher of the agent log if you accept an approximation.

## Wrapper

```powershell
pwsh -NoProfile -File tools\agent-emit\wrap.ps1 -Agent cursor -Title "Cursor" -Command "cursor-agent"
```

## Limitations

- The IDE's Agent mode (inside the Cursor window) cannot be observed without Cursor's cooperation: no
  documented hooks, no documented event stream. The row is only emitted when you launch the CLI
  through the wrapper.
- No tool-level detail: the row shows running + exit state.
