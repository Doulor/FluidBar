# Codex hook wrapper for FluidBar
# Use this with Codex custom hooks or as a post-task wrapper.
#
# Usage:
#   codex task "your prompt" && powershell -File codex-hook.ps1 -Project "MyProject" -Summary "task done" -Status "completed"
#   codex task "your prompt" || powershell -File codex-hook.ps1 -Project "MyProject" -Status "failed" -Error "task failed"

param(
    [string]$Project = "",
    [string]$Summary = "任务完成",
    [string]$Status = "completed",
    [string]$Branch = "",
    [long]$DurationMs = 0,
    [string]$Error = ""
)

$inbox = Join-Path $env:APPDATA "FluidBar\agent-events\inbox"
New-Item -ItemType Directory -Force -Path $inbox | Out-Null

# Try to detect project name from current directory
if ([string]::IsNullOrWhiteSpace($Project)) {
    $Project = Split-Path (Get-Location) -Leaf
}

# Try to detect git branch
if ([string]::IsNullOrWhiteSpace($Branch)) {
    try { $Branch = git rev-parse --abbrev-ref HEAD 2>$null } catch { }
}

$event = [ordered]@{
    tool       = "codex"
    status     = $Status
    project    = $Project
    summary    = $Summary
    branch     = $Branch
    durationMs = $DurationMs
    error      = $Error
}

$keysToRemove = @()
foreach ($key in $event.Keys) {
    if ([string]::IsNullOrWhiteSpace("$($event[$key])")) {
        $keysToRemove += $key
    }
}
foreach ($key in $keysToRemove) {
    $event.Remove($key)
}

$filename = "codex-{0:yyyyMMdd-HHmmss}-{1}.json" -f (Get-Date), (Get-Random -Min 1000 -Max 9999)
$path = Join-Path $inbox $filename
$event | ConvertTo-Json -Depth 4 | Out-File -FilePath $path -Encoding UTF8
