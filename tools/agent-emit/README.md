# agent-emit — FluidBar agent status tools

One event schema, many agents. Everything here writes a schema-v2 JSON file into
`%APPDATA%\FluidBar\agent-events`; the island's AgentInboxWatcher displays/merges it (one row per
session id) without polling.

## Files

| File | Purpose |
| --- | --- |
| `agent-emit.ps1` / `.cmd` | Emit one event (`-Agent -Event -Title -Message -SessionId -StartedAt -ErrorText`). |
| `hook-adapter.ps1` | Read a hook's stdin JSON (session id/tool/error) and call agent-emit. |
| `wrap.ps1` | Run any agent CLI: running on start, completed/failed on exit. |
| `dsh-bridge.ps1` | DeepSeek Harness: `-Watch` the desktop projection cache, or `-Run` a headless task via `dsh --json`. |
| `detect.ps1` | Detect installed agents and print the snippet to use (`-Json` for machines). |
| `dsh-headless-patch.example.yml` | Provider overlay example for `dsh --profile headless`. |

Snippets per agent: `docs/agent/snippets/*.md`. Support matrix: `docs/agent/AGENT-INTEGRATION.md`.

## Event schema (v2, v1 still accepted)

```json
{
  "schemaVersion": 2,
  "agent": "dsh",                  // dsh|claude-code|codex|copilot|cursor|windsurf|aider|zcode|qoder|workbuddy
  "event": "running",              // running|waiting|completed|failed|event
  "title": "DeepSeek Harness · FluidBar",
  "message": "正在使用 pwsh",       // detail line
  "sessionId": "session-abc",      // merge key; optional for legacy hooks
  "startedAt": "2026-10-20T10:00:00Z",
  "timestamp": "2026-10-20T10:02:30Z",
  "error": "MISSING_CREDENTIAL"    // folded into the detail line on failure
}
```

## Examples

```powershell
# one event
.\agent-emit.cmd -Agent claude-code -Event running -Title "Claude Code" -SessionId abc -Message "Edit App.cs"

# generic wrapper: running -> completed/failed
pwsh -NoProfile -File .\wrap.ps1 -Agent codex -Title "Codex" -Command codex

# DeepSeek Harness desktop sessions
pwsh -NoProfile -File .\dsh-bridge.ps1 -Watch

# DeepSeek Harness one-shot run (needs a provider overlay)
pwsh -NoProfile -File .\dsh-bridge.ps1 -Run "run the tests" -PatchPath .\dsh-headless-patch.example.yml
```

Verification: each command prints the written JSON path; the island agent page shows one merged row.
