# Agent Status Hooks

FluidBar reads local JSON files from:

```
%APPDATA%\FluidBar\agent-events\inbox
```

## Quick Setup

### Claude Code

Add a Stop hook to your Claude Code settings. Create or edit:

- **Windows:** `%APPDATA%\Claude Code\settings.json`
- **macOS:** `~/Library/Application Support/Claude Code/settings.json`
- **Linux:** `~/.config/Claude Code/settings.json`

```json
{
  "Stop": [
    {
      "matcher": ".*",
      "hooks": [
        {
          "type": "command",
          "command": "powershell -ExecutionPolicy Bypass -File \"D:\\build\\GitLocal\\FluidBar\\Plugins\\AgentStatus\\hooks\\claude-code-hook.ps1\"",
          "timeout": 10
        }
      ]
    }
  ]
}
```

> **Note:** Update the path to match your FluidBar checkout location. On macOS/Linux, use `bash /path/to/notify-fluidbar.sh claude-code completed` instead.

Once configured, every time a Claude Code session stops (task completed, cancelled, or errored), FluidBar will show a Dynamic Island notification.

### Codex

Codex doesn't have built-in hooks yet, but you can chain the hook script after Codex commands:

**PowerShell (Windows):**
```powershell
# Create an alias in your $PROFILE
function codex-notify {
    codex task $args
    if ($LASTEXITCODE -eq 0) {
        powershell -File "D:\build\GitLocal\FluidBar\Plugins\AgentStatus\hooks\codex-hook.ps1" -Project "FluidBar" -Summary "$args"
    } else {
        powershell -File "D:\build\GitLocal\FluidBar\Plugins\AgentStatus\hooks\codex-hook.ps1" -Project "FluidBar" -Status "failed" -Error "执行失败"
    }
}
```

**Bash (Linux/macOS/WSL):**
```bash
codex-notify() {
    codex task "$@"
    local exit_code=$?
    if [ $exit_code -eq 0 ]; then
        bash /path/to/notify-fluidbar.sh codex completed "$(basename $PWD)" "$*"
    else
        bash /path/to/notify-fluidbar.sh codex failed "$(basename $PWD)" "" "" 0 "" "执行失败"
    fi
}
```

## Event JSON Format

Write one `.json` file per event:

```json
{
  "tool": "claude-code",
  "status": "completed",
  "project": "FluidBar",
  "summary": "任务完成",
  "branch": "main",
  "durationMs": 46000,
  "sessionId": "abc123",
  "error": ""
}
```

**Fields:**

| Field | Required | Description |
|-------|----------|-------------|
| `tool` | Yes | `"claude-code"` or `"codex"` |
| `status` | Yes | `"completed"`, `"failed"`, `"cancelled"`, `"running"` |
| `project` | No | Project name (shown in hover card) |
| `summary` | No | One-line summary of the task |
| `branch` | No | Git branch name |
| `durationMs` | No | Task duration in milliseconds |
| `sessionId` | No | Session identifier |
| `error` | No | Error message if status is `"failed"` |

## How It Works

1. Hook script writes a `.json` file to the inbox
2. FluidBar's `AgentStatusPlugin` polls the inbox every 900ms
3. Valid events are consumed and shown as Dynamic Island notifications
4. Consumed files move to `processed/`, malformed files move to `failed/`
5. Duplicate events (same signature) are ignored

## Troubleshooting

- **No island appears:** Check that the Agent Status plugin is enabled in FluidBar settings
- **Files pile up in inbox:** The plugin may be disabled or not running. Check `%APPDATA%\FluidBar\agent-events\`
- **Hook timeout:** Increase the `timeout` value in settings.json if your hook script needs more time
- **Permission denied:** Ensure FluidBar has permission to read/write `%APPDATA%\FluidBar\`
