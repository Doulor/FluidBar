# Claude Code → FluidBar

Claude Code has first-class **hooks** (`PreToolUse`, `PostToolUse`, `Notification`, `Stop`,
`SubagentStop`, `UserPromptSubmit`, ...). Every hook receives a JSON payload on stdin that contains
the session id; `tools/agent-emit/hook-adapter.ps1` reads it and forwards one event.

Detected config: `%USERPROFILE%\.claude\settings.json` (the FluidBar settings UI's built-in installer
already handles `Stop` + `Notification`; the block below adds the running transitions).

## Copy-paste hook block

Replace the install path if your FluidBar checkout is elsewhere. Merge into the existing `hooks`
object; keep your own hooks untouched.

```json
{
  "hooks": {
    "UserPromptSubmit": [
      { "matcher": "", "hooks": [ { "type": "command",
        "command": "pwsh -NoProfile -ExecutionPolicy Bypass -File \"D:\\build\\GitLocal\\FluidBar\\tools\\agent-emit\\hook-adapter.ps1\" -Agent claude-code -Event running -Title \"Claude Code\" -Message \"thinking\"" } ] }
    ],
    "PreToolUse": [
      { "matcher": "", "hooks": [ { "type": "command",
        "command": "pwsh -NoProfile -ExecutionPolicy Bypass -File \"D:\\build\\GitLocal\\FluidBar\\tools\\agent-emit\\hook-adapter.ps1\" -Agent claude-code -Event running -Title \"Claude Code\"" } ] }
    ],
    "PostToolUse": [
      { "matcher": "", "hooks": [ { "type": "command",
        "command": "pwsh -NoProfile -ExecutionPolicy Bypass -File \"D:\\build\\GitLocal\\FluidBar\\tools\\agent-emit\\hook-adapter.ps1\" -Agent claude-code -Event running -Title \"Claude Code\" -Message \"tool done\"" } ] }
    ],
    "Notification": [
      { "matcher": "", "hooks": [ { "type": "command",
        "command": "pwsh -NoProfile -ExecutionPolicy Bypass -File \"D:\\build\\GitLocal\\FluidBar\\tools\\agent-emit\\hook-adapter.ps1\" -Agent claude-code -Event waiting -Title \"Claude Code\" -Message \"waiting for input\"" } ] }
    ],
    "Stop": [
      { "matcher": "", "hooks": [ { "type": "command",
        "command": "pwsh -NoProfile -ExecutionPolicy Bypass -File \"D:\\build\\GitLocal\\FluidBar\\tools\\agent-emit\\hook-adapter.ps1\" -Agent claude-code -Event completed -Title \"Claude Code\" -Message \"turn finished\"" } ] }
    ]
  }
}
```

Verify after the next turn: `%APPDATA%\FluidBar\agent-events` gains JSON files and the island's agent
page shows one running → completed row per session. `Stop` is the authoritative completion point;
`Notification` fires on permission prompts.
