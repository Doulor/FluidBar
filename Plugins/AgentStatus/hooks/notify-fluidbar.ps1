# FluidBar Agent Status hook script
# Invoke from Claude Code Stop hook or Codex wrapper.
# Writes a completion JSON to the FluidBar agent-events inbox.
param(
    [string]$Tool = "claude-code",
    [string]$Status = "completed",
    [string]$Project = "",
    [string]$Summary = "",
    [string]$Branch = "",
    [long]$DurationMs = 0,
    [string]$SessionId = "",
    [string]$Error = ""
)

$inbox = Join-Path $env:APPDATA "FluidBar\agent-events\inbox"
New-Item -ItemType Directory -Force -Path $inbox | Out-Null

$event = @{
    tool       = $Tool
    status     = $Status
    project    = $Project
    summary    = $Summary
    branch     = $Branch
    durationMs = $DurationMs
    sessionId  = $SessionId
    error      = $Error
}

# Remove null/empty values for cleaner JSON
$event.Keys | ForEach-Object {
    if ([string]::IsNullOrWhiteSpace($event[$_]) -and $_ -ne "status" -and $_ -ne "tool") {
        $event.Remove($_)
    }
}

$filename = "hook-{0:yyyyMMdd-HHmmss}-{1}.json" -f (Get-Date), (Get-Random -Min 1000 -Max 9999)
$path = Join-Path $inbox $filename

$event | ConvertTo-Json -Depth 4 | Out-File -FilePath $path -Encoding UTF8
