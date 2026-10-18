#Requires -Version 7.0
<#
.SYNOPSIS
    M6 performance baseline. Either samples the published app (startup time + idle CPU/WS/handles/
    threads) or, when the app is not runnable yet, runs the guarded headless platform soak and renders a
    report from its JSON evidence. Writes JSON + Markdown into build/reports/.

.EXAMPLE
    pwsh build/perf-baseline.ps1                              # auto: published app if present, else headless
    pwsh build/perf-baseline.ps1 -ExePath build/publish/win-x64/FluidBar.exe -IdleMinutes 3
    pwsh build/perf-baseline.ps1 -Headless -HeadlessMinutes 3
#>
[CmdletBinding()]
param(
    [string]$ExePath = '',
    [int]$IdleMinutes = 3,
    [int]$HeadlessMinutes = 3,
    [string]$OutputDirectory = '',
    [switch]$Headless,
    [switch]$Soak
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot 'build/reports'
}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')

function Get-MachineInfo {
    $cpu = $null
    $ram = $null
    $gpu = $null
    try { $cpu = Get-CimInstance Win32_Processor | Select-Object -First 1 Name, NumberOfCores, NumberOfLogicalProcessors } catch { }
    try { $ram = (Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory } catch { }
    try { $gpu = (Get-CimInstance Win32_VideoController | Select-Object -First 1).Name } catch { }
    [ordered]@{
        machineName = $env:COMPUTERNAME
        os = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
        osVersion = [Environment]::OSVersion.VersionString
        processorCount = [Environment]::ProcessorCount
        cpu = if ($cpu) { "$($cpu.Name) ($($cpu.NumberOfCores)C/$($cpu.NumberOfLogicalProcessors)T)" } else { 'unknown' }
        memoryGb = if ($ram) { [math]::Round($ram / 1GB, 1) } else { 0 }
        gpu = if ($gpu) { $gpu } else { 'unknown' }
        dotnet = (& dotnet --version)
        powershell = $PSVersionTable.PSVersion.ToString()
    }
}

function Write-Report {
    param([string]$Scenario, [hashtable]$Data, [string]$Markdown)
    $jsonPath = Join-Path $OutputDirectory "perf-$Scenario-$stamp.json"
    $data | ConvertTo-Json -Depth 8 | Set-Content -Path $jsonPath -Encoding utf8
    $mdPath = Join-Path $OutputDirectory "perf-$Scenario-$stamp.md"
    Set-Content -Path $mdPath -Value $Markdown -Encoding utf8
    Write-Host "JSON report     : $jsonPath" -ForegroundColor Green
    Write-Host "Markdown report : $mdPath" -ForegroundColor Green
}

# --- Headless fallback: guarded platform soak test --------------------------------------------------
function Invoke-HeadlessBaseline {
    Write-Host "== Headless platform soak ($HeadlessMinutes min idle) ==" -ForegroundColor Cyan
    $env:FLUIDBAR_SOAK = '1'
    $env:FLUIDBAR_SOAK_MINUTES = "$HeadlessMinutes"
    $env:FLUIDBAR_SOAK_REPORT_DIR = $OutputDirectory
    $testProject = Join-Path $repoRoot 'tests/FluidBar.Platform.Tests/FluidBar.Platform.Tests.csproj'
    $testDll = Join-Path $repoRoot 'tests/FluidBar.Platform.Tests/bin/Debug/net10.0-windows10.0.19041.0/FluidBar.Platform.Tests.dll'
    $testArgs = @('test', $testProject, '--nologo', '-v', 'q', '--filter', 'FullyQualifiedName~Soak_IdlePlatformGraph')
    if (Test-Path $testDll) { $testArgs += '--no-build' }
    & dotnet @testArgs
    if ($LASTEXITCODE -ne 0) {
        throw "headless soak failed with exit code $LASTEXITCODE"
    }

    $latest = Get-ChildItem -Path $OutputDirectory -Filter 'soak-idle-platform-*.json' |
        Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if (-not $latest) {
        throw 'soak report JSON was not produced'
    }

    $soak = Get-Content -Raw $latest.FullName | ConvertFrom-Json
    $data = @{
        scenario = 'headless-platform-soak'
        machine = Get-MachineInfo
        sampledAtUtc = [DateTime]::UtcNow.ToString('O')
        durationSeconds = $soak.DurationSeconds
        startupMs = $soak.Metadata.startupMs
        idleCpuPercent = $soak.AverageCpuPercent
        workingSetStartMb = [math]::Round($soak.WorkingSetStartBytes / 1MB, 1)
        workingSetEndMb = [math]::Round($soak.WorkingSetEndBytes / 1MB, 1)
        handleStart = $soak.HandleStart
        handleEnd = $soak.HandleEnd
        threadStart = $soak.ThreadStart
        threadEnd = $soak.ThreadEnd
        gen2Collections = $soak.Gen2Collections
        sourceReport = $latest.Name
        note = 'Headless platform composition-root soak (App not runnable/absent). App numbers come from the published EXE mode.'
    }
    $markdown = @"
# FluidBar performance baseline — headless platform host ($stamp)

**Mode:** headless (guarded \`Soak_IdlePlatformGraph\`) — published App not sampled.
**Scenario duration:** $($soak.DurationSeconds) s

| Metric | Value |
|---|---|
| Startup (graph start) | $($soak.Metadata.startupMs) ms |
| Idle CPU (avg) | $($soak.AverageCpuPercent) % |
| Working set start → end | $([math]::Round($soak.WorkingSetStartBytes / 1MB, 1)) MB → $([math]::Round($soak.WorkingSetEndBytes / 1MB, 1)) MB |
| Handles start → end | $($soak.HandleStart) → $($soak.HandleEnd) |
| Threads start → end | $($soak.ThreadStart) → $($soak.ThreadEnd) |
| Gen2 GCs during run | $($soak.Gen2Collections) |

Machine: $($data.machine.cpu), $($data.machine.memoryGb) GB RAM, $($data.machine.os), dotnet $($data.machine.dotnet)
Source evidence: ``$($latest.Name)``
"@
    Write-Report -Scenario 'headless-soak' -Data $data -Markdown $markdown
}

# --- Published app mode ----------------------------------------------------------------------------
function Invoke-AppBaseline {
    param([string]$Executable)
    Write-Host "== Published App baseline: $Executable ($IdleMinutes min idle) ==" -ForegroundColor Cyan
    $startWatch = [System.Diagnostics.Stopwatch]::StartNew()
    $process = Start-Process -FilePath $Executable -PassThru
    $startupMs = -1
    $startupNote = ''
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    while ([DateTime]::UtcNow -lt $deadline) {
        $process.Refresh()
        if ($process.HasExited) {
            throw "FluidBar exited during startup (code $($process.ExitCode))."
        }

        if ($process.MainWindowHandle -ne 0) {
            $startupMs = [int]$startWatch.ElapsedMilliseconds
            $startupNote = 'first visible top-level window'
            break
        }

        Start-Sleep -Milliseconds 100
    }

    if ($startupMs -lt 0) {
        # The island host is an ownerless tool window; MainWindowHandle can stay 0.
        if ($process.WaitForInputIdle(5000)) {
            $startupMs = [int]$startWatch.ElapsedMilliseconds
            $startupNote = 'process input-idle (tool-window host: no MainWindowHandle)'
        }
        else {
            $startupMs = -1
            $startupNote = 'startup not observed within 20 s'
            Write-Warning 'Could not observe startup within 20 s; recording -1.'
        }
    }

    $startWatch.Stop()
    Write-Host "Startup: $startupMs ms ($startupNote)"
    $samples = @()
    $interval = 5
    $iterations = [math]::Max(1, [int]($IdleMinutes * 60 / $interval))
    $previousCpu = $process.TotalProcessorTime.TotalSeconds
    $crashNote = $null
    for ($i = 0; $i -lt $iterations; $i++) {
        Start-Sleep -Seconds $interval
        $process.Refresh()
        if ($process.HasExited) {
            $crashNote = "App exited during the idle sample with code $($process.ExitCode) after $($samples.Count) samples; partial baseline recorded."
            Write-Warning $crashNote
            break
        }

        $cpuNow = $process.TotalProcessorTime.TotalSeconds
        $cpuPercent = [math]::Round(($cpuNow - $previousCpu) / $interval / [Environment]::ProcessorCount * 100, 3)
        $previousCpu = $cpuNow
        $samples += [ordered]@{
            tSeconds = ($i + 1) * $interval
            cpuPercent = $cpuPercent
            workingSetMb = [math]::Round($process.WorkingSet64 / 1MB, 1)
            privateMb = [math]::Round($process.PrivateMemorySize64 / 1MB, 1)
            handles = $process.HandleCount
            threads = $process.Threads.Count
        }
        Write-Host ("  t+{0,3}s cpu {1,5}% ws {2,6} MB handles {3,4} threads {4}" -f `
            (($i + 1) * $interval), $cpuPercent, $samples[-1].workingSetMb, $samples[-1].handles, $samples[-1].threads)
    }

    if (-not $process.HasExited) {
        [void]$process.CloseMainWindow()
        if (-not $process.WaitForExit(3000)) {
            Stop-Process -Id $process.Id -Force
        }
    }

    if ($samples.Count -eq 0) {
        throw "FluidBar exited before the first idle sample; no baseline could be recorded. ($crashNote)"
    }

    $cpuValues = @($samples | ForEach-Object { $_.cpuPercent })
    $averageCpu = if ($cpuValues.Count -gt 0) { [math]::Round(($cpuValues | Measure-Object -Average).Average, 3) } else { 0 }
    $data = @{
        scenario = 'published-app-idle'
        executable = $Executable
        machine = Get-MachineInfo
        sampledAtUtc = [DateTime]::UtcNow.ToString('O')
        idleMinutes = $IdleMinutes
        startupMs = $startupMs
        idleCpuPercent = $averageCpu
        workingSetStartMb = $samples[0].workingSetMb
        workingSetEndMb = $samples[-1].workingSetMb
        privateEndMb = $samples[-1].privateMb
        handleStart = $samples[0].handles
        handleEnd = $samples[-1].handles
        threadStart = $samples[0].threads
        threadEnd = $samples[-1].threads
        samples = $samples
        crashed = [bool]$crashNote
        note = if ($crashNote) { $crashNote } elseif ($startupNote) { "startup: $startupNote" } else { "" }
    }
    $markdown = @"
# FluidBar performance baseline — published App ($stamp)

**Executable:** ``$Executable``
**Idle sample:** $IdleMinutes min @ 5 s interval$(
if ($crashNote) { "`n**NOTE:** $crashNote" })

| Metric | Value |
|---|---|
| Startup | $startupMs ms ($startupNote) |
| Idle CPU (avg) | $averageCpu % |
| Working set start → end | $($data.workingSetStartMb) MB → $($data.workingSetEndMb) MB |
| Private bytes (end) | $($data.privateEndMb) MB |
| Handles start → end | $($data.handleStart) → $($data.handleEnd) |
| Threads start → end | $($data.threadStart) → $($data.threadEnd) |

Machine: $($data.machine.cpu), $($data.machine.memoryGb) GB RAM, $($data.machine.os), dotnet $($data.machine.dotnet)
"@
    Write-Report -Scenario 'app-idle' -Data $data -Markdown $markdown
}

# --- Dispatch --------------------------------------------------------------------------------------
if (-not $Headless -and [string]::IsNullOrWhiteSpace($ExePath)) {
    $candidate = Join-Path $repoRoot 'build/publish/win-x64/FluidBar.exe'
    if (Test-Path $candidate) {
        $ExePath = $candidate
    }
}

if ($Headless -or [string]::IsNullOrWhiteSpace($ExePath)) {
    if (-not $Headless) {
        Write-Warning 'No published app found; falling back to headless platform-host baseline.'
    }

    Invoke-HeadlessBaseline
} else {
    Invoke-AppBaseline -Executable (Resolve-Path -LiteralPath $ExePath).Path
}

if ($Soak) {
    Write-Host '== Running full soak suite (idle + clipboard + notification storm) ==' -ForegroundColor Cyan
    $env:FLUIDBAR_SOAK = '1'
    $env:FLUIDBAR_SOAK_REPORT_DIR = $OutputDirectory
    $testProject = Join-Path $repoRoot 'tests/FluidBar.Platform.Tests/FluidBar.Platform.Tests.csproj'
    $testDll = Join-Path $repoRoot 'tests/FluidBar.Platform.Tests/bin/Debug/net10.0-windows10.0.19041.0/FluidBar.Platform.Tests.dll'
    $testArgs = @('test', $testProject, '--nologo', '-v', 'q', '--filter', 'Category=Soak')
    if (Test-Path $testDll) { $testArgs += '--no-build' }
    & dotnet @testArgs
    if ($LASTEXITCODE -ne 0) {
        throw "soak suite failed with exit code $LASTEXITCODE"
    }
}
