param(
  [string]$Tag = 'idle',
  [ValidateSet('idle','animate')][string]$Mode = 'idle',
  [int]$Seconds = 10,
  [int]$Cycles = 10,
  [bool]$Probe = $true,
  [string]$LogLevel = 'info',
  [int]$IdleTrimMs = 0
)
$ErrorActionPreference = 'Stop'
$ev = Join-Path $env:TEMP 'fluidbar-perf'
$exe = 'D:/build/GitLocal/FluidBar/src/FluidBar.App/bin/Release/net10.0-windows10.0.19041.0/FluidBar.exe'
$logs = Join-Path $env:LOCALAPPDATA 'FluidBar\logs'
$appdata = Join-Path $env:APPDATA 'FluidBar'
$userSettings = Join-Path $appdata 'settings.ui.json.media2-backup'
New-Item -ItemType Directory -Force -Path $ev | Out-Null

$p = $null
try {
  Add-Type -AssemblyName UIAutomationClient
  Add-Type -AssemblyName UIAutomationTypes
  Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Generic;
public static class RunPerfNative {
  public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  public static List<string> Windows(uint pid) {
    var list = new List<string>();
    EnumWindows((h, l) => {
      uint wp; GetWindowThreadProcessId(h, out wp);
      if (wp == pid) {
        var sb = new StringBuilder(256); GetClassName(h, sb, 256);
        RECT r; GetWindowRect(h, out r);
        list.Add(h.ToInt64() + "|" + sb.ToString() + "|" + r.Left + "," + r.Top + "," + (r.Right - r.Left) + "," + (r.Bottom - r.Top));
      }
      return true;
    }, IntPtr.Zero);
    return list;
  }
}
"@
  Get-Process FluidBar -ErrorAction SilentlyContinue | Where-Object { $_.Path -like '*GitLocal*' } | Stop-Process -Force -ErrorAction SilentlyContinue
  Start-Sleep -Milliseconds 500
  if (Test-Path $logs) { Remove-Item (Join-Path $logs '*') -Force -ErrorAction SilentlyContinue }

  $settings = Get-Content $userSettings -Raw | ConvertFrom-Json
  $settings.advanced.logLevel = $LogLevel
  $settings.general.autoUpdate = 'off'
  $settings.general.startWithWindows = $false
  $settings.advanced.frameStats = $false
  $settings.advanced.safeMode = $false
  $settings | ConvertTo-Json -Depth 8 -Compress | Set-Content (Join-Path $appdata 'settings.ui.json') -Encoding UTF8

  $env:FLUIDBAR_HOST = 'skia'
  if ($IdleTrimMs -gt 0) { $env:FLUIDBAR_PERF_IDLE_TRIM_MS = [string]$IdleTrimMs } else { Remove-Item Env:\FLUIDBAR_PERF_IDLE_TRIM_MS -ErrorAction SilentlyContinue }
  if ($Probe) { $env:FLUIDBAR_PERF_PROBE = '1' } else { Remove-Item Env:\FLUIDBAR_PERF_PROBE -ErrorAction SilentlyContinue }
  $startTime = Get-Date
  $p = Start-Process -FilePath $exe -PassThru
  $proc = Get-Process -Id $p.Id

  function Get-OurLogFiles {
    return @(Get-ChildItem $logs -Filter '*.jsonl' -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -ge $startTime })
  }
  function Get-ProbeDumps {
    $out = @()
    foreach ($f in (Get-OurLogFiles)) {
      $out += @(Get-Content $f.FullName -ErrorAction SilentlyContinue | Where-Object { $_ -match 'island.skia.perf' })
    }
    return $out
  }
  function Get-FirstFrameLine {
    foreach ($f in (Get-OurLogFiles)) {
      $line = Get-Content $f.FullName -ErrorAction SilentlyContinue | Where-Object { $_ -match 'island.skia.first-frame' } | Select-Object -First 1
      if ($line) { return $line }
    }
    return $null
  }

  # Wait for the first frame (or 4 s when the probe is off).
  $firstFrame = $null
  $deadline = (Get-Date).AddSeconds(20)
  while ((Get-Date) -lt $deadline) {
    $firstFrame = Get-FirstFrameLine
    if ($firstFrame) { break }
    if (-not $Probe -and (Get-Date) -gt $deadline.AddSeconds(-16)) { break }
    Start-Sleep -Milliseconds 150
  }
  Start-Sleep -Milliseconds 1200

  $islandHwnd = [IntPtr]::Zero
  $root = $null
  $wins = [RunPerfNative]::Windows([uint32]$p.Id)
  $island = $wins | Where-Object { $_ -like '*LayeredIslandHost*' } | Select-Object -First 1
  if ($island) {
    $islandHwnd = [IntPtr][long](($island -split '\|')[0])
    for ($i = 0; $i -lt 25 -and $null -eq $root; $i++) {
      try { $root = [System.Windows.Automation.AutomationElement]::FromHandle($islandHwnd) } catch { Start-Sleep -Milliseconds 200 }
    }
  }

  $idleProc = Get-Process -Id $p.Id
  $cpuBefore = $idleProc.TotalProcessorTime
  $wsBefore = $idleProc.WorkingSet64

  if ($Mode -eq 'animate' -and $null -ne $root) {
    $expandCollapse = $root.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $animStart = Get-Date
    for ($i = 0; $i -lt $Cycles; $i++) {
      try { $expandCollapse.Expand() } catch { }
      Start-Sleep -Milliseconds 540
      try { $expandCollapse.Collapse() } catch { }
      Start-Sleep -Milliseconds 540
    }
    $animMs = ((Get-Date) - $animStart).TotalMilliseconds
    Start-Sleep -Milliseconds 800
  } else {
    $animMs = 0
  }

  Start-Sleep -Seconds $Seconds
  $idleProc.Refresh()
  $cpuAfter = $idleProc.TotalProcessorTime
  $wsAfter = $idleProc.WorkingSet64
  $cpuPct = 100.0 * ($cpuAfter - $cpuBefore).TotalMilliseconds / (($Seconds + ($animMs / 1000.0)) * 1000.0 * [Environment]::ProcessorCount)

  $dumps = @()
  foreach ($line in (Get-ProbeDumps)) {
    $dumps += ($line | ConvertFrom-Json).data
  }
  $last = if ($dumps.Count -gt 0) { $dumps[-1] } else { $null }
  $zeroFrameSeconds = @($dumps | Where-Object { $_.framesInWindow -eq 0 }).Count

  $summary = [pscustomobject]@{
    Tag = $Tag; Mode = $Mode; Probe = $Probe; Seconds = $Seconds
    FirstFrameMs = if ($firstFrame) { ($firstFrame | ConvertFrom-Json).data.sinceProcessStartMs } else { $null }
    CpuPct = [Math]::Round($cpuPct, 3)
    WsBeforeMB = [Math]::Round($wsBefore / 1MB, 1)
    WsAfterMB = [Math]::Round($wsAfter / 1MB, 1)
    Dumps = $dumps.Count
    ZeroFrameSeconds = $zeroFrameSeconds
    Frames = if ($last) { $last.frames } else { $null }
    IntervalP50 = if ($last) { $last.intervalP50Ms } else { $null }
    IntervalP95 = if ($last) { $last.intervalP95Ms } else { $null }
    IntervalP99 = if ($last) { $last.intervalP99Ms } else { $null }
    RenderP50 = if ($last) { $last.renderP50Ms } else { $null }
    RenderP99 = if ($last) { $last.renderP99Ms } else { $null }
    Dropped = if ($last) { $last.droppedFrames } else { $null }
    AllocPerFrame = if ($last) { $last.allocPerFrameBytes } else { $null }
    AllocAdvance = if ($last) { $last.allocAdvanceBytes } else { $null }
    AllocRender = if ($last) { $last.allocRenderBytes } else { $null }
    AllocOther = if ($last) { $last.allocOtherBytes } else { $null }
    Gen0 = if ($last) { $last.gen0 } else { $null }
    Gen1 = if ($last) { $last.gen1 } else { $null }
    Gen2 = if ($last) { $last.gen2 } else { $null }
    ManagedHeapMB = if ($last) { [Math]::Round($last.managedHeapBytes / 1MB, 1) } else { $null }
    ManagedCommittedMB = if ($last) { [Math]::Round($last.managedCommittedBytes / 1MB, 1) } else { $null }
    ShadowCount = if ($last) { $last.shadowImages } else { $null }
    ShadowMB = if ($last) { [Math]::Round($last.shadowBytes / 1MB, 2) } else { $null }
    ShadowBuilds = if ($last) { $last.shadowBuilds } else { $null }
    ArtworkMB = if ($last) { [Math]::Round($last.artworkBytes / 1MB, 2) } else { $null }
    TextCacheKB = if ($last) { [Math]::Round($last.textCacheBytes / 1KB, 1) } else { $null }
    SurfaceMB = if ($last) { [Math]::Round($last.surfaceBytes / 1MB, 2) } else { $null }
  }
  $summary | Format-List
  $summary | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $ev "$Tag.summary.json") -Encoding UTF8

  $merged = @()
  foreach ($f in (Get-OurLogFiles)) { $merged += @(Get-Content $f.FullName -ErrorAction SilentlyContinue) }
  if ($merged.Count -gt 0) { $merged | Set-Content (Join-Path $ev "$Tag.log.jsonl") -Encoding UTF8 }
}
finally {
  if ($p -and (Get-Process -Id $p.Id -ErrorAction SilentlyContinue)) {
    Get-Process -Id $p.Id -ErrorAction SilentlyContinue | Stop-Process -Force
  }
  Start-Sleep -Milliseconds 400
  Copy-Item $userSettings (Join-Path $appdata 'settings.ui.json') -Force
  Remove-Item (Join-Path $ev 'settings.ui.json.user-orig') -ErrorAction SilentlyContinue
}