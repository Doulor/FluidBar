<#
.SYNOPSIS
  Fixed-scene frame-pacing baseline for the Skia island body.
#>
[CmdletBinding()]
param(
    [string]$OutDir,
    [int]$Seconds = 8,
    [string]$Label = "after",
    [string]$AppPath,
    [string]$MediaPath,
    [string]$MediaPlayerPath,
    [string]$CompareTo
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $AppPath) { $AppPath = Join-Path $root "src\FluidBar.App\bin\Debug\net10.0-windows10.0.19041.0\FluidBar.exe" }
if (-not (Test-Path $AppPath)) { throw "FluidBar.exe not found: $AppPath" }
if (-not $OutDir) { $stamp = Get-Date -Format "yyyyMMdd-HHmmss"; $OutDir = Join-Path $root "build\reports\perf\$Label-$stamp" }
$OutDir = [IO.Path]::GetFullPath($OutDir)
$probeDir = Join-Path $OutDir "probe"
New-Item -ItemType Directory -Force -Path $probeDir | Out-Null
$probeFile = Join-Path $probeDir "frames-probe.jsonl"
Remove-Item $probeFile -ErrorAction SilentlyContinue

function Add-NativeUi {
    if ("FluidBarPerfNative" -as [type]) { return }
    Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class FluidBarPerfNative {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder name, int max);
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    public static IntPtr FindWindowForProcess(uint pid, string prefix) {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, l) => {
            uint p; GetWindowThreadProcessId(h, out p);
            if (p != pid) return true;
            var sb = new StringBuilder(256); GetClassName(h, sb, sb.Capacity);
            if (sb.ToString().StartsWith(prefix, StringComparison.Ordinal)) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }
    public static void Click(int x, int y) {
        SetCursorPos(x, y);
        mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
        mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
    }
}
"@
}

function Expand-And-Select-Media([IntPtr]$Hwnd) {
    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($Hwnd)
    $rect = $root.Current.BoundingRectangle
    if ($rect.Width -lt 300) {
        [void][FluidBarPerfNative]::SetForegroundWindow($Hwnd)
        $x = [int]($rect.X + $rect.Width / 2)
        $y = [int]($rect.Y + $rect.Height / 2)
        [FluidBarPerfNative]::Click($x, $y)
        Start-Sleep -Milliseconds 180
        [FluidBarPerfNative]::Click($x, $y)
        Start-Sleep -Seconds 2
        $root = [System.Windows.Automation.AutomationElement]::FromHandle($Hwnd)
    }
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "island.page.1")
    $tab = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if (-not $tab) { throw "media page tab not found" }
    $tab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 300
    $tab.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Seconds 2
}

function Percentile([double[]]$Values, [double]$Percent) {
    if ($Values.Count -eq 0) { return 0 }
    $sorted = $Values | Sort-Object
    $index = [Math]::Ceiling(($Percent / 100.0) * $sorted.Count) - 1
    return [double]$sorted[[Math]::Max(0, [Math]::Min($sorted.Count - 1, $index))]
}

Add-NativeUi
$nl = [Environment]::NewLine
$app = $null
$player = $null
try {
    if ($MediaPlayerPath -and $MediaPath) {
        $player = Start-Process -FilePath $MediaPlayerPath -ArgumentList @($MediaPath) -PassThru
        Start-Sleep -Seconds 3
    }
    $app = Start-Process -FilePath $AppPath -WorkingDirectory (Split-Path -Parent $AppPath) -Environment @{ FLUIDBAR_FRAME_PROBE = $probeDir; FLUIDBAR_HOST = "skia" } -PassThru
    $hwnd = [IntPtr]::Zero
    for ($attempt = 0; $attempt -lt 20 -and -not $app.HasExited; $attempt++) {
        Start-Sleep -Milliseconds 500
        $hwnd = [FluidBarPerfNative]::FindWindowForProcess([uint32]$app.Id, "FluidBar.LayeredIslandHost")
        if ($hwnd -ne [IntPtr]::Zero) { break }
    }
    if ($app.HasExited) { throw "FluidBar exited during startup (pid $($app.Id))" }
    if ($hwnd -eq [IntPtr]::Zero) { throw "layered island window not found for pid $($app.Id)" }
    Expand-And-Select-Media -Hwnd $hwnd
    Start-Sleep -Seconds 2
    Remove-Item $probeFile -ErrorAction SilentlyContinue
    Start-Sleep -Seconds $Seconds
    if (-not (Test-Path $probeFile)) { throw "frame probe produced no data (is playback active?)" }
    $rows = @(Get-Content $probeFile | ForEach-Object { $_ | ConvertFrom-Json })
    if ($rows.Count -eq 0) { throw "frame probe is empty" }

    $render = [double[]]@($rows | ForEach-Object { [double]$_.renderMs })
    $intervals = [double[]]@($rows | Where-Object { $_.intervalMs -gt 0 } | ForEach-Object { [double]$_.intervalMs })
    $full = @($rows | Where-Object { $_.fullFrame })
    $dirty = @($rows | Where-Object { -not $_.fullFrame })
    $dirtyArea = [double[]]@($dirty | ForEach-Object { [double]$_.dirtyArea })
    $dirtyW = [double[]]@($dirty | ForEach-Object { [double]$_.damageW })
    $dirtyH = [double[]]@($dirty | ForEach-Object { [double]$_.damageH })
    $maxFullStreak = 0
    $streak = 0
    foreach ($row in $rows) {
        if ($row.fullFrame) { $streak++; if ($streak -gt $maxFullStreak) { $maxFullStreak = $streak } } else { $streak = 0 }
    }
    $reasonCounts = [ordered]@{}
    foreach ($row in $full) {
        $reason = if ($row.fullReason) { [string]$row.fullReason } else { "unspecified" }
        if ($reasonCounts.Contains($reason)) { $reasonCounts[$reason]++ } else { $reasonCounts[$reason] = 1 }
    }
    $summary = [ordered]@{
        label = $Label
        measuredAt = (Get-Date).ToString("o")
        app = $AppPath
        seconds = $Seconds
        frames = $rows.Count
        fullFrames = $full.Count
        fullFrameRatio = [Math]::Round($full.Count * 100.0 / $rows.Count, 2)
        maxConsecutiveFullFrames = $maxFullStreak
        renderP50Ms = [Math]::Round((Percentile $render 50), 3)
        renderP99Ms = [Math]::Round((Percentile $render 99), 3)
        renderMaxMs = [Math]::Round(($render | Measure-Object -Maximum).Maximum, 3)
        intervalP50Ms = [Math]::Round((Percentile $intervals 50), 3)
        intervalP99Ms = [Math]::Round((Percentile $intervals 99), 3)
        intervalMaxMs = [Math]::Round(($intervals | Measure-Object -Maximum).Maximum, 3)
        dirtyFrames = $dirty.Count
        dirtyMeanArea = if ($dirtyArea.Count) { [Math]::Round(($dirtyArea | Measure-Object -Average).Average, 1) } else { 0 }
        dirtyP50Area = [Math]::Round((Percentile $dirtyArea 50), 1)
        dirtyP99Area = [Math]::Round((Percentile $dirtyArea 99), 1)
        dirtyMeanWidth = if ($dirtyW.Count) { [Math]::Round(($dirtyW | Measure-Object -Average).Average, 1) } else { 0 }
        dirtyMeanHeight = if ($dirtyH.Count) { [Math]::Round(($dirtyH | Measure-Object -Average).Average, 1) } else { 0 }
        fullReasons = $reasonCounts
    }
    $csv = Join-Path $OutDir "frames.csv"
    $index = 0
    $rows | ForEach-Object {
        $index++
        [pscustomobject]@{
            frame = $index; at = $_.at; intervalMs = $_.intervalMs; renderMs = $_.renderMs
            allocBytes = $_.allocBytes; fullFrame = $_.fullFrame; damageW = $_.damageW
            damageH = $_.damageH; dirtyRects = $_.dirtyRects; dirtyArea = $_.dirtyArea
            fullReason = $_.fullReason
        }
    } | Export-Csv -Path $csv -NoTypeInformation -Encoding UTF8
    $summaryJson = Join-Path $OutDir "summary.json"
    $summary | ConvertTo-Json -Depth 5 | Set-Content -Path $summaryJson -Encoding UTF8

    $ab = ""
    if ($CompareTo -and (Test-Path $CompareTo)) {
        $before = Get-Content $CompareTo -Raw | ConvertFrom-Json
        $areaDelta = if ($before.dirtyMeanArea -gt 0) { [Math]::Round((1 - ($summary.dirtyMeanArea / $before.dirtyMeanArea)) * 100, 1) } else { 0 }
        $ab = $nl + "## A/B against " + $before.label + $nl + $nl + "| Metric | Before | After | Delta |" + $nl + "|---|---:|---:|---:|" + $nl + "| full-frame ratio | " + $before.fullFrameRatio + "% | " + $summary.fullFrameRatio + "% | " + [Math]::Round($summary.fullFrameRatio - $before.fullFrameRatio, 2) + " pp |" + $nl + "| dirty mean area | " + $before.dirtyMeanArea + " DIP2 | " + $summary.dirtyMeanArea + " DIP2 | -" + $areaDelta + "% |" + $nl + "| render p99 | " + $before.renderP99Ms + " ms | " + $summary.renderP99Ms + " ms | " + [Math]::Round($summary.renderP99Ms - $before.renderP99Ms, 3) + " ms |" + $nl + "| max consecutive full | " + $before.maxConsecutiveFullFrames + " | " + $summary.maxConsecutiveFullFrames + " | |"
    }
    $reasonText = (@($summary.fullReasons.GetEnumerator() | ForEach-Object { $_.Key + "=" + $_.Value }) -join ", ")
    if (-not $reasonText) { $reasonText = "none" }
    $markdown = "# Frame-pacing baseline: " + $Label + $nl + $nl + "- Frames: " + $summary.frames + $nl + "- Full-frame redraws: " + $summary.fullFrames + " (" + $summary.fullFrameRatio + "%)" + $nl + "- Full reasons: " + $reasonText + $nl + "- Max consecutive full: " + $summary.maxConsecutiveFullFrames + $nl + "- Render: p50 " + $summary.renderP50Ms + " ms / p99 " + $summary.renderP99Ms + " ms / max " + $summary.renderMaxMs + " ms" + $nl + "- Frame interval: p50 " + $summary.intervalP50Ms + " ms / p99 " + $summary.intervalP99Ms + " ms / max " + $summary.intervalMaxMs + " ms" + $nl + "- Dirty rect: mean " + $summary.dirtyMeanWidth + "x" + $summary.dirtyMeanHeight + " DIP (" + $summary.dirtyMeanArea + " DIP2), p50 area " + $summary.dirtyP50Area + ", p99 area " + $summary.dirtyP99Area + $nl + $ab + $nl + "Per-frame data: frames.csv" + $nl
    Set-Content -Path (Join-Path $OutDir "summary.md") -Value $markdown -Encoding UTF8
    Write-Output $markdown
    Write-Output "OUTPUT=$OutDir"
}
finally {
    if ($app -and -not $app.HasExited) { Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue }
    if ($player -and -not $player.HasExited) { Stop-Process -Id $player.Id -Force -ErrorAction SilentlyContinue }
}

