<#
.SYNOPSIS
  Run any agent CLI with FluidBar status reporting (running -> completed/failed).

.EXAMPLE
  wrap.ps1 -Agent codex -Title "Codex" -Command "codex"
  wrap.ps1 -Agent cursor -Title "Cursor" -Command "cursor-agent" -Arguments "chat"
#>
param(
    [Parameter(Mandatory = $true)][String]$Agent,
    [Parameter(Mandatory = $true)][String]$Command,
    [string]$Title = '',
    [string]$Arguments = '',
    [string]$WorkingDirectory = '',
    [switch]$Quiet
)

$ErrorActionPreference = 'Stop'

$emit = [System.IO.Path]::Combine($PSScriptRoot, 'agent-emit.ps1')
$sessionId = 'wrap-' + [Guid]::NewGuid().ToString('N')
$startedAt = (Get-Date).ToUniversalTime().ToString('o')

function Send-Event {
    param([string]$EventName, [string]$Detail, [string]$ErrorText = '')
    $arguments = @('-Agent', $Agent, '-Event', $EventName, '-SessionId', $sessionId, '-StartedAt', $startedAt)
    if (-not [string]::IsNullOrWhiteSpace($Title)) { $arguments += @('-Title', $Title) }
    if (-not [string]::IsNullOrWhiteSpace($Detail)) { $arguments += @('-Message', $Detail) }
    if (-not [string]::IsNullOrWhiteSpace($ErrorText)) { $arguments += @('-ErrorText', $ErrorText) }
    if ($Quiet) { $arguments += '-Quiet' }
    & pwsh -NoProfile -ExecutionPolicy Bypass -File $emit @arguments | Out-Null
}

Send-Event -EventName 'running' -Detail '已启动'

try {
    $start = @{
        FilePath = $Command
        NoNewWindow = $true
        PassThru = $true
    }
    if (-not [string]::IsNullOrWhiteSpace($Arguments)) { $start['ArgumentList'] = $Arguments }
    if (-not [string]::IsNullOrWhiteSpace($WorkingDirectory)) { $start['WorkingDirectory'] = $WorkingDirectory }

    $process = Start-Process @start
    $process.WaitForExit()
    if ($process.ExitCode -eq 0) {
        Send-Event -EventName 'completed' -Detail '已完成'
    }
    else {
        Send-Event -EventName 'failed' -Detail '运行失败' -ErrorText ('exit ' + $process.ExitCode)
    }
    exit $process.ExitCode
}
catch {
    Send-Event -EventName 'failed' -Detail '无法启动' -ErrorText $_.Exception.Message
    [Console]::Error.WriteLine('wrap: ' + $_.Exception.Message)
    exit 3
}
