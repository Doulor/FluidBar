<#
.SYNOPSIS
  Emit one FluidBar agent-status event into the island's agent inbox.

.DESCRIPTION
  Writes one schema-v2 JSON file into %APPDATA%\FluidBar\agent-events. The island's AgentInboxWatcher
  picks the file up immediately (file-system watcher, no polling) and shows/merges the session row.

  Every hook/agent integration ultimately calls this script, so all agents share one event schema.

  The session is the merge key: emit consecutive events for the same agent session with the SAME
  -SessionId and the island updates one row (running -> completed/failed) instead of flooding the list.
  -StartedAt on the first running event drives the "已运行 X 分 Y 秒" elapsed label.

.EXAMPLE
  agent-emit.ps1 -Agent claude-code -Event running -Title 'Claude Code' -SessionId $pid -Message 'Edit src/App.cs'

.EXAMPLE
  agent-emit.ps1 -Agent dsh -Event completed -SessionId session-abc -StartedAt 2026-10-20T10:00:00Z -Message done

.NOTES
  Windows PowerShell 5.1 compatible (no PS7-only syntax). Exit codes: 0 ok, 2 bad arguments, 3 write failure.
#>
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('claude-code', 'codex', 'deepseek-harness', 'dsh', 'cursor', 'windsurf', 'aider',
        'copilot', 'github-copilot', 'zcode', 'qoder', 'workbuddy', 'generic')]
    [string]$Agent,

    [Parameter(Mandatory = $true)]
    [ValidateSet('running', 'waiting', 'completed', 'failed', 'event')]
    [string]$Event,

    [string]$Title = '',
    [string]$Message = '',
    [string]$SessionId = '',
    [string]$StartedAt = '',
    [string]$ErrorText = '',
    [string]$InboxDir = '',
    [switch]$Quiet
)

$ErrorActionPreference = 'Stop'

function Get-InboxDirectory {
    param([string]$Requested)
    if (-not [string]::IsNullOrWhiteSpace($Requested)) { return $Requested }
    if (-not [string]::IsNullOrWhiteSpace($env:FLUIDBAR_AGENT_INBOX)) { return $env:FLUIDBAR_AGENT_INBOX }
    $appData = $env:APPDATA
    if ([string]::IsNullOrWhiteSpace($appData)) { $appData = $env:USERPROFILE }
    return [System.IO.Path]::Combine($appData, 'FluidBar', 'agent-events')
}

try {
    $inbox = Get-InboxDirectory -Requested $InboxDir
    if (-not (Test-Path -LiteralPath $inbox)) {
        New-Item -ItemType Directory -Path $inbox -Force | Out-Null
    }

    $eventJson = [ordered]@{
        schemaVersion = 2
        agent = $Agent
        event = $Event
        timestamp = (Get-Date).ToUniversalTime().ToString('o')
    }
    if (-not [string]::IsNullOrWhiteSpace($Title)) { $eventJson['title'] = $Title }
    if (-not [string]::IsNullOrWhiteSpace($Message)) { $eventJson['message'] = $Message }
    if (-not [string]::IsNullOrWhiteSpace($SessionId)) { $eventJson['sessionId'] = $SessionId }
    if (-not [string]::IsNullOrWhiteSpace($StartedAt)) { $eventJson['startedAt'] = $StartedAt }
    if (-not [string]::IsNullOrWhiteSpace($ErrorText)) { $eventJson['error'] = $ErrorText }

    $json = $eventJson | ConvertTo-Json -Compress -Depth 4
    $name = 'agent-' + [Guid]::NewGuid().ToString('N') + '.json'
    $target = [System.IO.Path]::Combine($inbox, $name)
    $temp = $target + '.tmp'
    [System.IO.File]::WriteAllText($temp, $json, (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::Move($temp, $target)

    if (-not $Quiet) { Write-Output $target }
    exit 0
}
catch {
    [Console]::Error.WriteLine('agent-emit: ' + $_.Exception.Message)
    exit 3
}
