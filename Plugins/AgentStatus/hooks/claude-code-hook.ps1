# Claude Code Stop hook wrapper for FluidBar
# Add this to .claude/settings.json or %APPDATA%\Claude Code\settings.json:
#
# "Stop": [
#   {
#     "matcher": ".*",
#     "hooks": [
#       {
#         "type": "command",
#         "command": "powershell -ExecutionPolicy Bypass -File \"path\\to\\Plugins\\AgentStatus\\hooks\\claude-code-hook.ps1\"",
#         "timeout": 10
#       }
#     ]
#   }
# ]
#
# The Stop hook receives a JSON object on stdin with session info.
# This script reads stdin, extracts relevant fields, and posts to FluidBar.

param()

$inbox = Join-Path $env:APPDATA "FluidBar\agent-events\inbox"
New-Item -ItemType Directory -Force -Path $inbox | Out-Null

# Read the Stop hook JSON from stdin
try {
    $rawInput = $input | Out-String
    if ([string]::IsNullOrWhiteSpace($rawInput)) {
        # No stdin data; write a basic completion event
        $hookData = @{}
    } else {
        $hookData = $rawInput | ConvertFrom-Json -ErrorAction Stop
    }
} catch {
    $hookData = @{}
}

# Extract fields from hook context
$projectName = if ($hookData.PSObject.Properties['cwd']) {
    Split-Path $hookData.cwd -Leaf
} elseif ($hookData.PSObject.Properties['project']) {
    $hookData.project
} else {
    ""
}

$branchName = ""
try {
    $branchName = git -C $hookData.cwd rev-parse --abbrev-ref HEAD 2>$null
} catch { }

$summary = if ($hookData.PSObject.Properties['result']) {
    $hookData.result
} elseif ($hookData.PSObject.Properties['stop_reason']) {
    $hookData.stop_reason
} else {
    "任务完成"
}

$sessionId = if ($hookData.PSObject.Properties['session_id']) {
    $hookData.session_id
} else {
    ""
}

$event = [ordered]@{
    tool       = "claude-code"
    status     = "completed"
    project    = $projectName
    summary    = $summary
    branch     = $branchName
    sessionId  = $sessionId
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
