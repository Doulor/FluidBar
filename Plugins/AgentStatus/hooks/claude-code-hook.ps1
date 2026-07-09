# Claude Code hook for FluidBar (supports all 6 hook events)
# Add this to .claude/settings.json or %APPDATA%\Claude Code\settings.json:
#
# "hooks": {
#   "SessionStart": [{ "matcher": ".*", "hooks": [{ "type": "command", "command": "powershell -ExecutionPolicy Bypass -File \"path\\to\\hooks\\claude-code-hook.ps1\"", "timeout": 10 }] }],
#   "UserPromptSubmit": [{ "matcher": ".*", "hooks": [{ "type": "command", "command": "powershell -ExecutionPolicy Bypass -File \"path\\to\\hooks\\claude-code-hook.ps1\"", "timeout": 10 }] }],
#   "PreToolUse": [{ "matcher": ".*", "hooks": [{ "type": "command", "command": "powershell -ExecutionPolicy Bypass -File \"path\\to\\hooks\\claude-code-hook.ps1\"", "timeout": 10 }] }],
#   "PostToolUse": [{ "matcher": ".*", "hooks": [{ "type": "command", "command": "powershell -ExecutionPolicy Bypass -File \"path\\to\\hooks\\claude-code-hook.ps1\"", "timeout": 10 }] }],
#   "Notification": [{ "matcher": ".*", "hooks": [{ "type": "command", "command": "powershell -ExecutionPolicy Bypass -File \"path\\to\\hooks\\claude-code-hook.ps1\"", "timeout": 10 }] }],
#   "Stop": [{ "matcher": ".*", "hooks": [{ "type": "command", "command": "powershell -ExecutionPolicy Bypass -File \"path\\to\\hooks\\claude-code-hook.ps1\"", "timeout": 10 }] }]
# }
#
# Each hook event receives a JSON object on stdin.
# This script reads stdin, detects the event type, and writes to FluidBar inbox.

param()

$inbox = Join-Path $env:APPDATA "FluidBar\agent-events\inbox"
New-Item -ItemType Directory -Force -Path $inbox | Out-Null

# Read hook JSON from stdin
try {
    $rawInput = $input | Out-String
    if ([string]::IsNullOrWhiteSpace($rawInput)) {
        $hookData = @{}
    } else {
        $hookData = $rawInput | ConvertFrom-Json -ErrorAction Stop
    }
} catch {
    $hookData = @{}
}

# Detect hook event name (Claude Code sends hook_event_name or hookEventName)
$hookEvent = ""
if ($hookData.PSObject.Properties['hook_event_name']) {
    $hookEvent = $hookData.hook_event_name
} elseif ($hookData.PSObject.Properties['hookEventName']) {
    $hookEvent = $hookData.hookEventName
} elseif ($hookData.PSObject.Properties['event']) {
    $hookEvent = $hookData.event
}

# Extract project name
$projectName = if ($hookData.PSObject.Properties['cwd']) {
    Split-Path $hookData.cwd -Leaf
} elseif ($hookData.PSObject.Properties['project']) {
    $hookData.project
} else {
    ""
}

# Extract git branch
$branchName = ""
try {
    $branchName = git -C $hookData.cwd rev-parse --abbrev-ref HEAD 2>$null
} catch { }

# Extract session ID
$sessionId = if ($hookData.PSObject.Properties['session_id']) {
    $hookData.session_id
} else {
    ""
}

# Map hook event to tool name and status
$tool = "claude-code"
$status = "running"
$summary = ""
$toolName = ""

switch ($hookEvent) {
    "SessionStart" {
        $status = "running"
        $summary = "会话已开始"
    }
    "UserPromptSubmit" {
        $status = "running"
        $summary = "思考中…"
    }
    "PreToolUse" {
        $status = "running"
        $toolName = if ($hookData.PSObject.Properties['tool_name']) { $hookData.tool_name } else { "" }
        $summary = if ($toolName) { "正在使用 $toolName" } else { "正在使用工具" }
    }
    "PostToolUse" {
        $status = "running"
        $toolName = if ($hookData.PSObject.Properties['tool_name']) { $hookData.tool_name } else { "" }
        $summary = if ($toolName) { "$toolName 完成" } else { "工具调用完成" }
    }
    "Notification" {
        $status = "waiting"
        $msg = if ($hookData.PSObject.Properties['message']) { $hookData.message } else { "" }
        $summary = if ($msg) { $msg } else { "需要确认" }
    }
    "Stop" {
        # Check for errors
        $stopReason = if ($hookData.PSObject.Properties['stop_reason']) { $hookData.stop_reason } else { "" }
        $errorText = if ($hookData.PSObject.Properties['error']) { $hookData.error } else { "" }
        $result = if ($hookData.PSObject.Properties['result']) { $hookData.result } else { "" }

        if ($stopReason -eq "error" -or $errorText) {
            $status = "failed"
            $summary = if ($errorText) { $errorText } elseif ($result) { $result } else { "任务失败" }
        } else {
            $status = "completed"
            $summary = if ($result) { $result } else { "任务完成" }
        }
    }
    default {
        $status = "running"
        $summary = if ($hookEvent) { "$hookEvent" } else { "状态更新" }
    }
}

# Build event JSON
$event = [ordered]@{
    tool       = $tool
    status     = $status
    project    = $projectName
    summary    = $summary
    branch     = $branchName
    sessionId  = $sessionId
}

# Include toolName for PreToolUse/PostToolUse
if ($toolName) {
    $event["toolName"] = $toolName
}

# Include duration if available
if ($hookData.PSObject.Properties['duration_ms']) {
    $event["durationMs"] = $hookData.duration_ms
} elseif ($hookData.PSObject.Properties['durationMs']) {
    $event["durationMs"] = $hookData.durationMs
}

# Remove empty fields
$keysToRemove = @()
foreach ($key in $event.Keys) {
    if ([string]::IsNullOrWhiteSpace("$($event[$key])")) {
        $keysToRemove += $key
    }
}
foreach ($key in $keysToRemove) {
    $event.Remove($key)
}

$filename = "claude-{0:yyyyMMdd-HHmmss}-{1}.json" -f (Get-Date), (Get-Random -Min 1000 -Max 9999)
$path = Join-Path $inbox $filename
$event | ConvertTo-Json -Depth 4 | Out-File -FilePath $path -Encoding UTF8
