param(
    [string]$AppPath,
    [int]$Seconds = 30,
    [string]$OutDir = "build/reports/perf"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $AppPath) { $AppPath = Join-Path $root "src/FluidBar.App/bin/Debug/net10.0-windows10.0.19041.0/FluidBar.exe" }
if (-not [IO.Path]::IsPathRooted($OutDir)) { $OutDir = Join-Path $root $OutDir }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
Get-Process -Name ([IO.Path]::GetFileNameWithoutExtension($AppPath)) -ErrorAction SilentlyContinue | Stop-Process -Force
$probe = Join-Path $OutDir "idle-probe"
Remove-Item $probe -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $probe | Out-Null
$process = Start-Process -FilePath $AppPath -WorkingDirectory (Split-Path -Parent $AppPath) -Environment @{ FLUIDBAR_FRAME_PROBE = $probe; FLUIDBAR_HOST = "skia" } -PassThru
Start-Sleep -Seconds 8
$frames = Join-Path $probe "frames-probe.jsonl"
Remove-Item $frames -ErrorAction SilentlyContinue
$proc = Get-Process -Id $process.Id
$cpuBefore = $proc.TotalProcessorTime.TotalSeconds
$wsBefore = $proc.WorkingSet64
$privateBefore = $proc.PrivateMemorySize64
Start-Sleep -Seconds $Seconds
$proc.Refresh()
$cpuAfter = $proc.TotalProcessorTime.TotalSeconds
$cpuPct = (($cpuAfter - $cpuBefore) / $Seconds / [Environment]::ProcessorCount) * 100
$framesAfter = if (Test-Path $frames) { @(Get-Content $frames).Count } else { 0 }
$result = [pscustomobject]@{
    seconds = $Seconds
    cpuPercent = [Math]::Round($cpuPct, 3)
    frames = $framesAfter
    workingSetMB = [Math]::Round($proc.WorkingSet64 / 1MB, 1)
    privateMB = [Math]::Round($proc.PrivateMemorySize64 / 1MB, 1)
    workingSetDeltaMB = [Math]::Round(($proc.WorkingSet64 - $wsBefore) / 1MB, 1)
    privateDeltaMB = [Math]::Round(($proc.PrivateMemorySize64 - $privateBefore) / 1MB, 1)
}
$result | ConvertTo-Json | Set-Content (Join-Path $OutDir "idle.json") -Encoding UTF8
$result | Format-List
if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }

