<#
.SYNOPSIS
  Generic hook adapter: reads a hook's stdin JSON and forwards one FluidBar agent event.

.DESCRIPTION
  Most agents invoke hook commands with a JSON payload on stdin (Claude Code hooks, Codex notify,
  Copilot hooks, ...). This adapter extracts a stable session id and a tool/error detail from that
  payload (tolerant: missing fields are fine) and calls agent-emit.ps1.

.EXAMPLE
  echo '{"session_id":"abc","tool_name":"Bash"}' | pwsh -File hook-adapter.ps1 -Agent claude-code -Event running
#>
param(
    [Parameter(Mandatory = $true)][string]$Agent,
    [Parameter(Mandatory = $true)][ValidateSet('running', 'waiting', 'completed', 'failed', 'event')][string]$Event,
    [string]$Title = '',
    [string]$Message = '',
    [string]$StateFile = ''
)

$ErrorActionPreference = 'SilentlyContinue'

$raw = ''
if ([Console]::IsInputRedirected) {
    $raw = [Console]::In.ReadToEnd()
}

$payload = $null
if (-not [string]::IsNullOrWhiteSpace($raw)) {
    try { $payload = $raw | ConvertFrom-Json } catch { $payload = $null }
}

function Get-FirstValue {
    param($Object, [string[]]$Names)
    if ($Object -eq $null) { return '' }
    foreach ($name in $Names) {
        $value = $Object.$name
        if ($value -ne $null -and -not [string]::IsNullOrWhiteSpace([string]$value)) { return [string]$value }
    }
    return ''
}

$sessionId = Get-FirstValue -Object $payload -Names @('session_id', 'sessionId', 'conversation_id', 'thread_id', 'id')
$toolName = Get-FirstValue -Object $payload -Names @('tool_name', 'tool', 'command', 'name')
$errorText = Get-FirstValue -Object $payload -Names @('error', 'message', 'reason')

$detail = $Message
if ([string]::IsNullOrWhiteSpace($detail) -and -not [string]::IsNullOrWhiteSpace($toolName)) {
    $detail = '正在使用 ' + $toolName
}

if ($Event -eq 'failed' -and -not [string]::IsNullOrWhiteSpace($errorText)) {
    $detail = $errorText
}

$emit = [System.IO.Path]::Combine($PSScriptRoot, 'agent-emit.ps1')
$arguments = @('-Agent', $Agent, '-Event', $Event)
if (-not [string]::IsNullOrWhiteSpace($Title)) { $arguments += @('-Title', $Title) }
if (-not [string]::IsNullOrWhiteSpace($detail)) { $arguments += @('-Message', $detail) }
if (-not [string]::IsNullOrWhiteSpace($sessionId)) { $arguments += @('-SessionId', $sessionId) }
if ($Event -eq 'failed' -and -not [string]::IsNullOrWhiteSpace($errorText)) { $arguments += @('-ErrorText', $errorText) }

& pwsh -NoProfile -ExecutionPolicy Bypass -File $emit @arguments | Out-Null
exit $LASTEXITCODE
