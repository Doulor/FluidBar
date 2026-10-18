<#
.SYNOPSIS
  Desktop smoke test for the Skia layered island host.

.DESCRIPTION
  Builds FluidBar.App, launches it with FLUIDBAR_HOST=skia, captures the desktop,
  samples the island corner/panel pixels, records process metrics, then closes it.
  The output is written to build/reports/skia-host-smoke/ (desktop.png + report.json).

  This is a developer/QA tool; it does not modify the app or settings.
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [int]$WaitSeconds = 6,
    [string]$OutDir = 'build/reports/skia-host-smoke',
    [switch]$KeepRunning
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not (Test-Path (Join-Path $repo 'FluidBar.sln'))) {
    throw "Cannot locate FluidBar.sln from $PSScriptRoot"
}

$out = Join-Path $repo $OutDir
New-Item -ItemType Directory -Force -Path $out | Out-Null

Write-Host "Building FluidBar.App ($Configuration)..."
dotnet build (Join-Path $repo 'src/FluidBar.App/FluidBar.App.csproj') -c $Configuration -v q --nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)" }

$exe = Join-Path $repo "src/FluidBar.App/bin/$Configuration/net10.0-windows10.0.19041.0/FluidBar.exe"
if (-not (Test-Path $exe)) { throw "App exe not found: $exe" }

$psi = [System.Diagnostics.ProcessStartInfo]::new()
$psi.FileName = $exe
$psi.WorkingDirectory = Split-Path -Parent $exe
$psi.UseShellExecute = $false
$psi.Environment['FLUIDBAR_HOST'] = 'skia'
$psi.Environment['FLUIDBAR_SMOKE'] = '1'

Write-Host "Launching $exe (FLUIDBAR_HOST=skia)..."
$proc = [System.Diagnostics.Process]::Start($psi)
if ($null -eq $proc) { throw 'Failed to start FluidBar' }

$report = [ordered]@{
    startedAt = (Get-Date).ToString('o')
    pid = $proc.Id
    exe = $exe
    waitSeconds = $WaitSeconds
    alive = $null
    workingSetMb = $null
    privateMb = $null
    handles = $null
    desktopPng = $null
    cornerSample = $null
    panelSample = $null
    smokeLog = $null
    error = $null
}

try {
    Start-Sleep -Seconds $WaitSeconds
    $proc.Refresh()
    $report.alive = -not $proc.HasExited
    if ($proc.HasExited) { throw "FluidBar exited early with code $($proc.ExitCode)" }
    $report.workingSetMb = [math]::Round($proc.WorkingSet64 / 1MB, 1)
    $report.privateMb = [math]::Round($proc.PrivateMemorySize64 / 1MB, 1)
    $report.handles = $proc.HandleCount

    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing
    $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bmp = [System.Drawing.Bitmap]::new($bounds.Width, $bounds.Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.CopyFromScreen($bounds.X, $bounds.Y, 0, 0, $bmp.Size)
    }
    finally {
        $g.Dispose()
    }
    $png = Join-Path $out 'desktop.png'
    $bmp.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
    $report.desktopPng = $png

    # The default float host is 488x384 DIP centred at the top of the work area.
    # Sample the top-left corner of the host surface (should show the desktop) and
    # the panel centre (should be island obsidian). At 150% the surface is 732x576 px.
    $scale = 1.5
    $hostW = [int](488 * $scale)
    $hostH = [int](384 * $scale)
    $left = [int](($bounds.Width - $hostW) / 2)
    $top = 0
    $corner = $bmp.GetPixel([math]::Min($bounds.Width - 1, $left + 6), [math]::Min($bounds.Height - 1, $top + 6))
    $panel = $bmp.GetPixel([math]::Min($bounds.Width - 1, $left + [int]($hostW / 2)), [math]::Min($bounds.Height - 1, $top + [int]($hostH / 2)))
    $report.cornerSample = [ordered]@{ r = $corner.R; g = $corner.G; b = $corner.B }
    $report.panelSample = [ordered]@{ r = $panel.R; g = $panel.G; b = $panel.B }
    $bmp.Dispose()

    $smokeLog = Join-Path $env:TEMP 'fluidbar-smoke.log'
    if (Test-Path $smokeLog) { $report.smokeLog = $smokeLog }
}
catch {
    $report.error = $_.Exception.Message
}
finally {
    if (-not $KeepRunning) {
        try { if (-not $proc.HasExited) { $proc.CloseMainWindow() | Out-Null; Start-Sleep -Milliseconds 600; if (-not $proc.HasExited) { $proc.Kill($true) } } } catch { }
    }
    $report | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $out 'report.json') -Encoding utf8NoBOM
    $report | Format-List
    Write-Host "Report: $(Join-Path $out 'report.json')"
}

if ($report.error) { exit 1 }
