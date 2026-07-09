# Agent Status Hooks

FluidBar reads local JSON files from:

```
%APPDATA%\FluidBar\agent-events\inbox
```

## Quick Setup

### Claude Code (all hook events)

Create or edit your Claude Code settings file:

- **Windows:** `%APPDATA%\Claude Code\settings.json`
- **macOS:** `~/Library/Application Support/Claude Code/settings.json`
- **Linux:** `~/.config/Claude Code/settings.json`

```json
{
  "hooks": {
    "SessionStart": [{ "matcher": ".*", "hooks": [{ "type": "command", "command": "powershell -ExecutionPolicy Bypass -File \"D:\\build\\GitLocal\\FluidBar\\Plugins\\AgentStatus\\hooks\\claude-code-hook.ps1\"", "timeout": 10 }] }],
    "UserPromptSubmit": [{ "matcher": ".*", "hooks": [{ "type": "command", "command": "powershell -ExecutionPolicy Bypass -File \"D:\\build\\GitLocal\\FluidBar\\Plugins\\AgentStatus\\hooks\\claude-code-hook.ps1\"", "timeout": 10 }] }],
    "PreToolUse": [{ "matcher": ".*", "hooks": [{ "type": "command", "command": "powershell -ExecutionPolicy Bypass -File \"D:\\build\\GitLocal\\FluidBar\\Plugins\\AgentStatus\\hooks\\claude-code-hook.ps1\"", "timeout": 10 }] }],
    "PostToolUse": [{ "matcher": ".*", "hooks": [{ "type": "command", "command": "powershell -ExecutionPolicy Bypass -File \"D:\\build\\GitLocal\\FluidBar\\Plugins\\AgentStatus\\hooks\\claude-code-hook.ps1\"", "timeout": 10 }] }],
    "Notification": [{ "matcher": ".*", "hooks": [{ "type": "command", "command": "powershell -ExecutionPolicy Bypass -File \"D:\\build\\GitLocal\\FluidBar\\Plugins\\AgentStatus\\hooks\\claude-code-hook.ps1\"", "timeout": 10 }] }],
    "Stop": [{ "matcher": ".*", "hooks": [{ "type": "command", "command": "powershell -ExecutionPolicy Bypass -File \"D:\\build\\GitLocal\\FluidBar\\Plugins\\AgentStatus\\hooks\\claude-code-hook.ps1\"", "timeout": 10 }] }]
  }
}
```

> **Note:** Update the path to match your FluidBar checkout location. On macOS/Linux, use `bash /path/to/notify-fluidbar.sh claude-code` instead.

**Supported hook events:**

| Hook Event | When it fires | Island behavior |
|---|---|---|
| `SessionStart` | New session begins | Shows "运行中" with wave animation |
| `UserPromptSubmit` | User submits a prompt | Shows "思考中…" |
| `PreToolUse` | Before a tool executes | Shows tool name (e.g. "Bash", "Read") |
| `PostToolUse` | After a tool finishes | Shows tool completion |
| `Notification` | Permission needed | Shows "需要确认" |
| `Stop` | Session ends | Shows "完成" or "失败" |

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
  "status": "running",
  "project": "FluidBar",
  "summary": "正在使用 Bash",
  "branch": "main",
  "toolName": "Bash",
  "durationMs": 46000,
  "sessionId": "abc123",
  "error": ""
}
```

**Fields:**

| Field | Required | Description |
|-------|----------|-------------|
| `tool` | Yes | `"claude-code"` or `"codex"` |
| `status` | Yes | `"completed"`, `"failed"`, `"cancelled"`, `"running"`, `"waiting"` |
| `project` | No | Project name (shown in hover card) |
| `summary` | No | One-line summary of the task |
| `branch` | No | Git branch name |
| `toolName` | No | Specific tool being used (e.g. `"Bash"`, `"Read"`, `"Edit"`) |
| `durationMs` | No | Task duration in milliseconds |
| `sessionId` | No | Session identifier |
| `error` | No | Error message if status is `"failed"` |

## How It Works

1. Hook script writes a `.json` file to the inbox
2. FluidBar's `AgentStatusPlugin` polls the inbox every 900ms
3. Valid events are consumed and shown as Dynamic Island notifications
4. Consumed files move to `processed/`, malformed files move to `failed/`
5. Running events show a wave animation; completed events show a static badge

## Hooks Guardian

FluidBar includes a built-in hooks guardian that periodically checks whether Claude Code's `settings.json` contains the correct hook entries. If any are missing or broken, it automatically repairs them.

**How it works:**

1. Every 30 seconds (configurable, 5–60s), the guardian reads `~/.claude/settings.json`
2. It checks all 6 required hook events (`SessionStart`, `UserPromptSubmit`, `PreToolUse`, `PostToolUse`, `Notification`, `Stop`)
3. For each event, it verifies the `command` field matches FluidBar's hook script path
4. If any event is missing, it writes the correct entry back to `settings.json`

**Configuration (FluidBar settings UI → Agent 状态 plugin):**

| Setting | Default | Description |
|---|---|---|
| Hooks 守护 | Enabled | Toggle the guardian on/off |
| 守护间隔 | 30s | How often to check (5–60 seconds) |

**Manual operations (via the plugin's C# API):**

```csharp
var plugin = new AgentStatusPlugin();
var status = plugin.CheckHooks();     // Check current state
plugin.RepairHooks(status);           // Fix missing hooks
plugin.RemoveHooks();                 // Remove all FluidBar hooks
```

## Troubleshooting

- **No island appears:** Check that the Agent Status plugin is enabled in FluidBar settings
- **Files pile up in inbox:** The plugin may be disabled or not running. Check `%APPDATA%\FluidBar\agent-events\`
- **Hook timeout:** Increase the `timeout` value in settings.json if your hook script needs more time
- **Permission denied:** Ensure FluidBar has permission to read/write `%APPDATA%\FluidBar\`
