param([string]$Tag='soft', [int]$Software=1)
$ErrorActionPreference='Stop'
$ev = Join-Path $env:TEMP 'fluidbar-perf'
$exe = 'D:/build/GitLocal/FluidBar/src/FluidBar.App/bin/Release/net10.0-windows10.0.19041.0/FluidBar.exe'
$logs = Join-Path $env:LOCALAPPDATA 'FluidBar\logs'
$appdata = Join-Path $env:APPDATA 'FluidBar'
$userSettings = Join-Path $appdata 'settings.ui.json.media2-backup'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Generic;
public static class SettingsProbeNative {
  public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr w, IntPtr l);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  public static List<string> Windows(uint pid) {
    var list = new List<string>();
    EnumWindows((h, l) => {
      uint wp; GetWindowThreadProcessId(h, out wp);
      if (wp == pid) { var sb = new StringBuilder(256); GetClassName(h, sb, 256); RECT r; GetWindowRect(h, out r);
        list.Add(h.ToInt64() + "|" + sb.ToString() + "|" + r.Left + "," + r.Top + "," + (r.Right-r.Left) + "," + (r.Bottom-r.Top)); }
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
$settings.advanced.logLevel='info'; $settings.general.autoUpdate='off'; $settings.general.startWithWindows=$false
$settings | ConvertTo-Json -Depth 8 -Compress | Set-Content (Join-Path $appdata 'settings.ui.json') -Encoding UTF8
$env:FLUIDBAR_HOST='skia'
$env:FLUIDBAR_PERF_PROBE='1'
if ($Software -eq 1) {
  $env:FLUIDBAR_AVALONIA_SOFTWARE='1'
  Remove-Item Env:\FLUIDBAR_AVALONIA_RENDERING -ErrorAction SilentlyContinue
} elseif ($Software -eq 0) {
  Remove-Item Env:\FLUIDBAR_AVALONIA_SOFTWARE -ErrorAction SilentlyContinue
  $env:FLUIDBAR_AVALONIA_RENDERING='gpu'
} else {
  Remove-Item Env:\FLUIDBAR_AVALONIA_SOFTWARE -ErrorAction SilentlyContinue
  Remove-Item Env:\FLUIDBAR_AVALONIA_RENDERING -ErrorAction SilentlyContinue
}
$start = Get-Date
$p = Start-Process $exe -PassThru
try {
  Start-Sleep -Seconds 2
  $wins = [SettingsProbeNative]::Windows([uint32]$p.Id)
  $island = ($wins | Where-Object { $_ -like '*LayeredIslandHost*' } | Select-Object -First 1)
  $ih = [IntPtr][long](($island -split '\|')[0])
  $root = $null
  for ($i=0;$i -lt 25 -and $null -eq $root;$i++){ try { $root=[System.Windows.Automation.AutomationElement]::FromHandle($ih) } catch { Start-Sleep -Milliseconds 200 } }
  if ($null -eq $root) { throw 'no island uia root' }
  $ec = $root.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
  $ec.Expand()
  Start-Sleep -Milliseconds 700
  $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'island.settings')
  $btn = $null
  $deadline = (Get-Date).AddSeconds(6)
  while ((Get-Date) -lt $deadline -and $null -eq $btn) {
    Start-Sleep -Milliseconds 300
    try { $root = [System.Windows.Automation.AutomationElement]::FromHandle($ih) } catch { }
    if ($root) { $btn = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond) }
  }
  if ($null -eq $btn) {
    $ids = @()
    try { $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition); foreach ($e in $all) { if ($e.Current.AutomationId) { $ids += $e.Current.AutomationId } } } catch { }
    Write-Output ("UIA ids: " + [string]::Join(',', $ids))
    throw 'settings button not found'
  }
  $invStart = Get-Date
  $btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  $setWin = $null
  $deadline = (Get-Date).AddSeconds(20)
  while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 100
    $setWin = [SettingsProbeNative]::Windows([uint32]$p.Id) | Where-Object { $_ -like '*Avalonia-*' -and $_ -notlike '*MessageWindow*' } | Select-Object -First 1
    if ($setWin) { break }
  }
  $openMs = ((Get-Date) - $invStart).TotalMilliseconds
  if (-not $setWin) { throw "settings window did not appear (openMs=$openMs)" }
  Start-Sleep -Milliseconds 800
  $sh = [IntPtr][long](($setWin -split '\|')[0])
  Add-Type -AssemblyName System.Drawing
  $rectText = ($setWin -split '\|')[2]
  $r = $rectText -split ','
  $bmp = New-Object System.Drawing.Bitmap ([int]$r[2]), ([int]$r[3])
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $hdc = $g.GetHdc()
  Add-Type -MemberDefinition '[DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr d, uint f);' -Name 'PW' -Namespace 'Native'
  [Native.PW]::PrintWindow($sh, $hdc, 2) | Out-Null
  $g.ReleaseHdc($hdc); $g.Dispose()
  $shot = Join-Path $ev "$Tag-settings.png"
  $bmp.Save($shot, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
  $uiaTexts = 0
  try { $sr = [System.Windows.Automation.AutomationElement]::FromHandle($sh); $uiaTexts = $sr.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition).Count } catch { }
  [SettingsProbeNative]::PostMessage($sh, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
  Start-Sleep -Milliseconds 400
  Write-Output ("TAG=$Tag openMs={0} window={1} uiaDescendants={2} shot={3}" -f [math]::Round($openMs,0), $setWin, $uiaTexts, $shot)
}
finally {
  Get-Process -Id $p.Id -ErrorAction SilentlyContinue | Stop-Process -Force
  Start-Sleep -Milliseconds 400
  Copy-Item $userSettings (Join-Path $appdata 'settings.ui.json') -Force
}