<#
.SYNOPSIS
  Detect AI coding agents installed on this machine and print the FluidBar integration to use.

.DESCRIPTION
  Read-only detection: checks the documented config directories / CLI commands of the known agents.
  For each agent it reports the support level (hook / wrap / watch), the config path found, and the
  copy-paste snippet under docs/agent/snippets/.

.EXAMPLE
  detect.ps1
  detect.ps1 -Json
#>
param([switch]$Json)

$ErrorActionPreference = 'SilentlyContinue'

function Test-AnyPath {
    param([string[]]$Paths)
    foreach ($path in $Paths) {
        if ([string]::IsNullOrWhiteSpace($path)) { continue }
        if (Test-Path -LiteralPath $path) { return $path }
    }
    return ''
}

function Test-CommandExists {
    param([string]$Name)
    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if ($command -ne $null) { return $command.Source }
    return ''
}

$userProfile = $env:USERPROFILE
$appData = $env:APPDATA
$localAppData = $env:LOCALAPPDATA
$dshHome = $env:DSH_HOME
if ([string]::IsNullOrWhiteSpace($dshHome)) { $dshHome = [System.IO.Path]::Combine($userProfile, '.dsh') }
$repoRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($PSScriptRoot, '..', '..'))
$snippets = [System.IO.Path]::Combine($repoRoot, 'docs', 'agent', 'snippets')

$rows = @()

$claudeConfig = Test-AnyPath @(
    [System.IO.Path]::Combine($userProfile, '.claude', 'settings.json'),
    [System.IO.Path]::Combine($appData, 'Claude', 'settings.json'))
$rows += [pscustomobject]@{
    Agent = 'claude-code'
    Detected = (-not [string]::IsNullOrWhiteSpace($claudeConfig))
    Evidence = $claudeConfig
    Support = 'hook'
    Integration = 'Claude Code hooks (PreToolUse/PostToolUse/Stop/Notification) call agent-emit.ps1'
    Snippet = [System.IO.Path]::Combine($snippets, 'claude-code.md')
}

$codexConfig = Test-AnyPath @(
    [System.IO.Path]::Combine($userProfile, '.codex', 'config.toml'),
    [System.IO.Path]::Combine($userProfile, '.codex'))
$rows += [pscustomobject]@{
    Agent = 'codex'
    Detected = (-not [string]::IsNullOrWhiteSpace($codexConfig))
    Evidence = $codexConfig
    Support = 'hook+wrapper'
    Integration = 'Codex lifecycle hooks when available; otherwise wrap the codex CLI with agent-emit'
    Snippet = [System.IO.Path]::Combine($snippets, 'codex.md')
}

$dshDetected = Test-AnyPath @($dshHome, [System.IO.Path]::Combine($env:ProgramFiles, 'DeepSeek Harness'))
$dshCli = Test-AnyPath @(
    (Join-Path $env:ProgramFiles 'DeepSeek Harness\resources\runtime\cli\bin\dsh.cmd'))
$rows += [pscustomobject]@{
    Agent = 'deepseek-harness'
    Detected = (-not [string]::IsNullOrWhiteSpace($dshDetected))
    Evidence = $dshDetected
    Support = 'plugin+watch+wrapper'
    Integration = 'Cordis plugin (push), projection-cache watch (desktop, zero install), dsh headless --json wrapper'
    Snippet = [System.IO.Path]::Combine($snippets, 'dsh.md')
}

$copilotCli = Test-CommandExists 'copilot'
$ghCli = Test-CommandExists 'gh'
$rows += [pscustomobject]@{
    Agent = 'copilot'
    Detected = ((-not [string]::IsNullOrWhiteSpace($copilotCli)) -or (-not [string]::IsNullOrWhiteSpace($ghCli)))
    Evidence = ($copilotCli + ' ' + $ghCli).Trim()
    Support = 'hook'
    Integration = 'GitHub Copilot agent hooks call agent-emit.ps1; gh-based sessions can be wrapped'
    Snippet = [System.IO.Path]::Combine($snippets, 'copilot.md')
}

$cursorPath = Test-AnyPath @(
    [System.IO.Path]::Combine($appData, 'Cursor'),
    [System.IO.Path]::Combine($localAppData, 'Programs', 'cursor'),
    (Test-CommandExists 'cursor-agent'))
$rows += [pscustomobject]@{
    Agent = 'cursor'
    Detected = (-not [string]::IsNullOrWhiteSpace($cursorPath))
    Evidence = $cursorPath
    Support = 'wrapper'
    Integration = 'Cursor is an IDE: no documented lifecycle hook; wrap the cursor-agent CLI (Agent mode) with agent-emit'
    Snippet = [System.IO.Path]::Combine($snippets, 'cursor.md')
}

$windsurfPath = Test-AnyPath @(
    [System.IO.Path]::Combine($appData, 'Windsurf'),
    [System.IO.Path]::Combine($localAppData, 'Programs', 'Windsurf'),
    (Test-CommandExists 'windsurf'))
$rows += [pscustomobject]@{
    Agent = 'windsurf'
    Detected = (-not [string]::IsNullOrWhiteSpace($windsurfPath))
    Evidence = $windsurfPath
    Support = 'wrapper'
    Integration = 'Pure IDE (Cascade): no user shell hook; only a wrapper around a launched command can report'
    Snippet = [System.IO.Path]::Combine($snippets, 'windsurf.md')
}

$aiderConfig = Test-AnyPath @(
    [System.IO.Path]::Combine($userProfile, '.aider.conf.yml'),
    [System.IO.Path]::Combine($userProfile, '.aider'))
$aiderCli = Test-CommandExists 'aider'
$rows += [pscustomobject]@{
    Agent = 'aider'
    Detected = ((-not [string]::IsNullOrWhiteSpace($aiderConfig)) -or (-not [string]::IsNullOrWhiteSpace($aiderCli)))
    Evidence = ($aiderConfig + ' ' + $aiderCli).Trim()
    Support = 'wrapper'
    Integration = 'Aider has no documented lifecycle hook; wrap the aider CLI and watch git commits / chat output'
    Snippet = [System.IO.Path]::Combine($snippets, 'aider.md')
}

foreach ($unknown in @('zcode', 'qoder', 'workbuddy')) {
    $cli = Test-CommandExists $unknown
    $candidate = @(
        [System.IO.Path]::Combine($localAppData, 'Programs', $unknown),
        [System.IO.Path]::Combine($localAppData, 'Programs', ($unknown.Substring(0,1).ToUpper() + $unknown.Substring(1))),
        [System.IO.Path]::Combine($appData, $unknown),
        [System.IO.Path]::Combine($appData, ($unknown.Substring(0,1).ToUpper() + $unknown.Substring(1))))
    $found = Test-AnyPath $candidate
    $rows += [pscustomobject]@{
        Agent = $unknown
        Detected = ((-not [string]::IsNullOrWhiteSpace($cli)) -or (-not [string]::IsNullOrWhiteSpace($found)))
        Evidence = ($cli + ' ' + $found).Trim()
        Support = 'wrapper/unknown'
        Integration = 'No documented public lifecycle hook found; use the generic CLI wrapper (agent-emit) or ask the vendor for hook support'
        Snippet = [System.IO.Path]::Combine($snippets, $unknown + '.md')
    }
}

if ($Json) {
    $rows | ConvertTo-Json -Depth 4
    exit 0
}

Write-Output ('FluidBar agent detection — ' + (Get-Date).ToString('yyyy-MM-dd HH:mm'))
Write-Output ('snippets: ' + $snippets)
Write-Output ''
$rows | Format-Table -AutoSize Agent, Detected, Support, Evidence
Write-Output ''
foreach ($row in $rows) {
    if ($row.Detected) {
        Write-Output ('[detected] ' + $row.Agent + ': ' + $row.Integration)
        Write-Output ('           snippet: ' + $row.Snippet)
    }
}
