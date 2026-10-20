<#
.SYNOPSIS
  DeepSeek Harness (DSH) -> FluidBar agent bridge.
.DESCRIPTION
  -Run <task>  wraps 'dsh --profile headless --json' and mirrors session/turn/step/tool events.
  -Watch       mirrors desktop DSH sessions from the projection cache (read-only).
.NOTES
  Windows PowerShell 5.1 compatible.
#>
param(
    [string]$Run = '',
    [string]$PatchPath = '',
    [string]$DshPath = '',
    [string]$Profile = 'headless',
    [switch]$Watch,
    [switch]$Once,
    [int]$LoopSeconds = 2,
    [int]$HeartbeatSeconds = 10,
    [int]$ActiveMinutes = 30,
    [string]$ProjectionDir = '',
    [string]$InboxDir = '',
    [string]$StatePath = '',
    [string]$Title = 'DeepSeek Harness',
    [switch]$Quiet
)

$ErrorActionPreference = 'Stop'

function Resolve-Inbox {
    param([string]$Requested)
    if (-not [string]::IsNullOrWhiteSpace($Requested)) { return $Requested }
    if (-not [string]::IsNullOrWhiteSpace($env:FLUIDBAR_AGENT_INBOX)) { return $env:FLUIDBAR_AGENT_INBOX }
    $appData = $env:APPDATA
    if ([string]::IsNullOrWhiteSpace($appData)) { $appData = $env:USERPROFILE }
    return [System.IO.Path]::Combine($appData, 'FluidBar', 'agent-events')
}

function Resolve-Dsh {
    param([string]$Requested)
    if (-not [string]::IsNullOrWhiteSpace($Requested)) { return $Requested }
    if (-not [string]::IsNullOrWhiteSpace($env:FLUIDBAR_DSH_CLI)) { return $env:FLUIDBAR_DSH_CLI }
    $command = Get-Command dsh -ErrorAction SilentlyContinue
    if ($command -ne $null) { return $command.Source }
    $bundled = Join-Path $env:ProgramFiles 'DeepSeek Harness\resources\runtime\cli\bin\dsh.cmd'
    if (Test-Path -LiteralPath $bundled) { return $bundled }
    return 'dsh'
}

function Resolve-ProjectionDir {
    param([string]$Requested)
    if (-not [string]::IsNullOrWhiteSpace($Requested)) { return $Requested }
    $dshHome = $env:DSH_HOME
    if ([string]::IsNullOrWhiteSpace($dshHome)) { $dshHome = [System.IO.Path]::Combine($env:USERPROFILE, '.dsh') }
    return [System.IO.Path]::Combine($dshHome, 'storages', 'session_projcache', 'sessions')
}

$script:Inbox = Resolve-Inbox -Requested $InboxDir
if (-not (Test-Path -LiteralPath $script:Inbox)) {
    New-Item -ItemType Directory -Path $script:Inbox -Force | Out-Null
}

function Write-AgentEvent {
    param(
        [string]$Event,
        [string]$SessionId = '',
        [string]$Message = '',
        [string]$StartedAt = '',
        [string]$ErrorText = '',
        [string]$EventTitle = ''
    )

    $payload = [ordered]@{
        schemaVersion = 2
        agent = 'dsh'
        event = $Event
        timestamp = (Get-Date).ToUniversalTime().ToString('o')
    }
    if (-not [string]::IsNullOrWhiteSpace($EventTitle)) { $payload['title'] = $EventTitle }
    if (-not [string]::IsNullOrWhiteSpace($Message)) { $payload['message'] = $Message }
    if (-not [string]::IsNullOrWhiteSpace($SessionId)) { $payload['sessionId'] = $SessionId }
    if (-not [string]::IsNullOrWhiteSpace($StartedAt)) { $payload['startedAt'] = $StartedAt }
    if (-not [string]::IsNullOrWhiteSpace($ErrorText)) { $payload['error'] = $ErrorText }

    $json = $payload | ConvertTo-Json -Compress -Depth 4
    $name = 'agent-' + [Guid]::NewGuid().ToString('N') + '.json'
    $target = [System.IO.Path]::Combine($script:Inbox, $name)
    $temp = $target + '.tmp'
    [System.IO.File]::WriteAllText($temp, $json, (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::Move($temp, $target)
    if (-not $Quiet) { Write-Verbose ('emitted ' + $Event + ' ' + $Message) }
}

function Get-LeafName {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) { return '' }
    $trimmed = $Path.TrimEnd([char]92, [char]47)
    return [System.IO.Path]::GetFileName($trimmed)
}

function Invoke-HeadlessRun {
    param([string]$Task, [string]$Patch, [string]$Dsh, [string]$ProfileName)

    $dshPath = Resolve-Dsh -Requested $Dsh
    $arguments = '--profile ' + $ProfileName
    if (-not [string]::IsNullOrWhiteSpace($Patch)) {
        $arguments += ' --patch "' + $Patch + '"'
    }
    $arguments += ' --json "' + ($Task -replace '"', '\"') + '"'

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $dshPath
    $startInfo.Arguments = $arguments
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $startInfo
    [void]$process.Start()

    $sessionId = ''
    $startedIso = (Get-Date).ToUniversalTime().ToString('o')
    $finished = $false
    $lastTool = ''
    $lastToolError = ''
    $title = $Title

    while ($true) {
        $line = $process.StandardOutput.ReadLine()
        if ($line -eq $null) { break }
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        try { $event = $line | ConvertFrom-Json } catch { continue }
        if ($event -eq $null) { continue }

        $eventType = [string]$event.type
        if ($eventType -eq 'session') {
            if (-not [string]::IsNullOrWhiteSpace($event.sessionId)) { $sessionId = [string]$event.sessionId }
            $project = Get-LeafName -Path ([string]$event.cwd)
            if (-not [string]::IsNullOrWhiteSpace($project)) { $title = $Title + ' · ' + $project }
            Write-AgentEvent -Event 'running' -SessionId $sessionId -StartedAt $startedIso -Message '会话已启动' -EventTitle $title
        }
        elseif ($eventType -eq 'status') {
            if ($finished) { continue }
            $phase = [string]$event.phase
            if ($phase -eq 'turn_start') {
                Write-AgentEvent -Event 'running' -SessionId $sessionId -StartedAt $startedIso -Message ('第 ' + $event.turn + ' 轮') -EventTitle $title
            }
            elseif ($phase -eq 'step_start') {
                Write-AgentEvent -Event 'running' -SessionId $sessionId -StartedAt $startedIso -Message ('第 ' + $event.turn + ' 轮 · 第 ' + $event.step + ' 步') -EventTitle $title
            }
            elseif ($phase -eq 'turn_end') {
                $finished = $true
                $isError = $false
                $errorText = ''
                $reason = $event.reason
                if ($reason -ne $null -and [string]$reason.kind -eq 'error') {
                    $isError = $true
                    if ($reason.error -ne $null) { $errorText = [string]$reason.error.message }
                    if ([string]::IsNullOrWhiteSpace($errorText)) { $errorText = 'turn failed' }
                }
                elseif (-not [string]::IsNullOrWhiteSpace($lastToolError)) {
                    $isError = $true
                    $errorText = $lastToolError
                }

                if ($isError) {
                    Write-AgentEvent -Event 'failed' -SessionId $sessionId -StartedAt $startedIso -Message '运行失败' -ErrorText $errorText -EventTitle $title
                }
                else {
                    Write-AgentEvent -Event 'completed' -SessionId $sessionId -StartedAt $startedIso -Message '已完成' -EventTitle $title
                }
            }
        }
        elseif ($eventType -eq 'tool_call') {
            if ($finished) { continue }
            $lastTool = [string]$event.tool
            Write-AgentEvent -Event 'running' -SessionId $sessionId -StartedAt $startedIso -Message ('正在使用 ' + $lastTool) -EventTitle $title
        }
        elseif ($eventType -eq 'tool_result') {
            if ($finished) { continue }
            if ([string]$event.status -eq 'error') {
                $lastToolError = [string]$event.result
                if ($lastToolError.Length -gt 300) { $lastToolError = $lastToolError.Substring(0, 300) + '…' }
                Write-AgentEvent -Event 'running' -SessionId $sessionId -StartedAt $startedIso -Message ('工具 ' + $lastTool + ' 失败，继续处理') -EventTitle $title
            }
        }
    }

    [void]$process.WaitForExit()
    if (-not $finished) {
        Write-AgentEvent -Event 'failed' -SessionId $sessionId -StartedAt $startedIso -Message 'DSH 进程结束但没有 turn_end 事件' -ErrorText ('exit ' + $process.ExitCode) -EventTitle $title
    }

    return $process.ExitCode
}

function Load-BridgeState {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) { return @{} }
    if (-not (Test-Path -LiteralPath $Path)) { return @{} }
    try {
        $raw = Get-Content -LiteralPath $Path -Raw
        if ([string]::IsNullOrWhiteSpace($raw)) { return @{} }
        $doc = $raw | ConvertFrom-Json
        $map = @{}
        if ($doc.sessions -ne $null) {
            foreach ($property in $doc.sessions.PSObject.Properties) {
                $map[$property.Name] = $property.Value
            }
        }
        return $map
    }
    catch { return @{} }
}

function Save-BridgeState {
    param([string]$Path, $Sessions)
    if ([string]::IsNullOrWhiteSpace($Path)) { return }
    $directory = [System.IO.Path]::GetDirectoryName($Path)
    if (-not [string]::IsNullOrWhiteSpace($directory) -and -not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }
    $document = [ordered]@{ version = 1; sessions = $Sessions }
    $json = $document | ConvertTo-Json -Depth 8 -Compress
    [System.IO.File]::WriteAllText($Path, $json, (New-Object System.Text.UTF8Encoding($false)))
}

function Read-ProjectionState {
    param($Record)

    $state = [ordered]@{
        Running = $false
        Waiting = $false
        Detail = ''
        Turn = 0
        Step = 0
        StartedAt = ''
        Project = ''
        LastTurn = 0
        Question = ''
    }

    $stats = $Record.record.rows.sessionStats.val
    $boundary = $Record.record.rows.turnBoundary.val
    $working = $Record.record.rows.workingDirectory.val
    if ($boundary -ne $null) { $state.LastTurn = [int]$boundary.lastTurn }
    if (-not [string]::IsNullOrWhiteSpace($working)) { $state.Project = Get-LeafName -Path $working }

    $openStep = $null
    if ($stats -ne $null) { $openStep = $stats.openStep }
    if ($openStep -ne $null) {
        $state.Running = $true
        $state.Turn = [int]$openStep.turn
        $state.Step = [int]$openStep.step
        if ($openStep.startTime -ne $null) {
            $startMs = [double]$openStep.startTime
            $state.StartedAt = [System.DateTimeOffset]::FromUnixTimeMilliseconds([long]$startMs).UtcDateTime.ToString('o')
        }

        $pendingNames = @()
        if ($stats.pendingCalls -ne $null) {
            foreach ($property in $stats.pendingCalls.PSObject.Properties) {
                $pendingNames += [string]$property.Name
            }
        }
        if ($pendingNames.Count -gt 0) {
            $state.Detail = '正在使用 ' + ($pendingNames -join ', ')
        }
        else {
            $state.Detail = '第 ' + $state.Turn + ' 轮 · 第 ' + $state.Step + ' 步'
        }
    }

    $questions = $Record.record.rows.userQuestions.val
    if ($questions -ne $null -and $questions.questions -ne $null) {
        $active = @($questions.questions.active)
        if ($active.Count -gt 0 -and $active[0] -ne $null) {
            $state.Waiting = $true
            $text = [string]$active[0].question
            if ([string]::IsNullOrWhiteSpace($text)) { $text = [string]$active[0].title }
            if (-not [string]::IsNullOrWhiteSpace($text)) {
                if ($text.Length -gt 160) { $text = $text.Substring(0, 160) + '…' }
                $state.Question = $text
            }
        }
    }

    return $state
}

function Invoke-WatchScan {
    param([string]$Directory, [int]$ActiveMinutes, [int]$HeartbeatSeconds, $Sessions)

    if (-not (Test-Path -LiteralPath $Directory)) {
        Write-Error ('projection directory not found: ' + $Directory)
        return $Sessions
    }

    $cutoff = (Get-Date).AddMinutes(-1 * [Math]::Max(1, $ActiveMinutes))
    foreach ($file in Get-ChildItem -LiteralPath $Directory -Filter '*.json' -File) {
        if ($file.LastWriteTime -lt $cutoff) { continue }
        $sessionId = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
        try { $record = (Get-Content -LiteralPath $file.FullName -Raw) | ConvertFrom-Json }
        catch { continue }
        if ($record -eq $null -or $record.record -eq $null) { continue }

        $state = Read-ProjectionState -Record $record
        $entry = $Sessions[$sessionId]
        if ($entry -eq $null) {
            $entry = [ordered]@{ StartedAt = ''; LastTurn = 0; LastStatus = ''; LastEmitMs = 0; Project = '' }
            $Sessions[$sessionId] = $entry
        }

        $titleText = $Title
        if (-not [string]::IsNullOrWhiteSpace($state.Project)) { $titleText = $Title + ' · ' + $state.Project }

        if ($state.Running) {
            # The projection's openStep start is authoritative (a new turn can start after adoption).
            if (-not [string]::IsNullOrWhiteSpace($state.StartedAt)) { $entry.StartedAt = $state.StartedAt }
            elseif ([string]::IsNullOrWhiteSpace($entry.StartedAt)) { $entry.StartedAt = (Get-Date).ToUniversalTime().ToString('o') }
            $nowMs = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
            $elapsedMs = [Math]::Abs($nowMs - [int]$entry.LastEmitMs)
            $shouldEmit = ($entry.LastStatus -ne 'running') -or ($elapsedMs -ge ($HeartbeatSeconds * 1000))
            if ($state.Waiting) {
                Write-AgentEvent -Event 'waiting' -SessionId ('session-' + $sessionId) -StartedAt $entry.StartedAt -Message ('等待输入：' + $state.Question) -EventTitle $titleText
                $entry.LastStatus = 'waiting'
                $entry.LastEmitMs = $nowMs
            }
            elseif ($shouldEmit) {
                Write-AgentEvent -Event 'running' -SessionId ('session-' + $sessionId) -StartedAt $entry.StartedAt -Message $state.Detail -EventTitle $titleText
                $entry.LastStatus = 'running'
                $entry.LastEmitMs = $nowMs
            }
        }
        elseif ($state.LastTurn -gt 0 -and [int]$entry.LastTurn -lt $state.LastTurn) {
            if ([string]::IsNullOrWhiteSpace($entry.StartedAt)) {
                # A session already finished when the bridge started: adopt its turn without emitting
                # a row, so starting the bridge does not replay yesterday's completions.
                $entry.LastStatus = 'adopted'
            }
            else {
                Write-AgentEvent -Event 'completed' -SessionId ('session-' + $sessionId) -StartedAt $entry.StartedAt -Message '已完成' -EventTitle $titleText
                $entry.LastStatus = 'completed'
                $entry.LastEmitMs = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
            }

            $entry.LastTurn = $state.LastTurn
        }

        $entry.Project = $state.Project
    }

    return $Sessions
}

try {
    if (-not [string]::IsNullOrWhiteSpace($Run)) {
        $code = Invoke-HeadlessRun -Task $Run -Patch $PatchPath -Dsh $DshPath -ProfileName $Profile
        exit $code
    }

    $projection = Resolve-ProjectionDir -Requested $ProjectionDir
    if ([string]::IsNullOrWhiteSpace($StatePath)) {
        $local = $env:LOCALAPPDATA
        if ([string]::IsNullOrWhiteSpace($local)) { $local = $env:TEMP }
        $StatePath = [System.IO.Path]::Combine($local, 'FluidBar', 'agent-bridge-state.json')
    }

    $sessions = Load-BridgeState -Path $StatePath
    if ($Once) {
        [void](Invoke-WatchScan -Directory $projection -ActiveMinutes $ActiveMinutes -HeartbeatSeconds $HeartbeatSeconds -Sessions $sessions)
        Save-BridgeState -Path $StatePath -Sessions $sessions
        exit 0
    }

    if (-not $Watch) {
        Write-Error 'Specify -Run <task> or -Watch (optionally with -Once).'
        exit 2
    }

    Write-Output ('watching ' + $projection)
    while ($true) {
        try {
            [void](Invoke-WatchScan -Directory $projection -ActiveMinutes $ActiveMinutes -HeartbeatSeconds $HeartbeatSeconds -Sessions $sessions)
            Save-BridgeState -Path $StatePath -Sessions $sessions
        }
        catch {
            [Console]::Error.WriteLine('dsh-bridge scan failed: ' + $_.Exception.Message)
        }
        Start-Sleep -Seconds ([Math]::Max(1, $LoopSeconds))
    }
}
catch {
    [Console]::Error.WriteLine('dsh-bridge: ' + $_.Exception.Message)
    exit 3
}
