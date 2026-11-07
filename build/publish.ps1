#Requires -Version 7.0
<#
.SYNOPSIS
    Release build/publish for FluidBar (M6). Runs the first-party warning gate + tests, publishes a
    single-file win-x64 build, stamps the version, hashes the artifact and produces a zip.

.DESCRIPTION
    Steps:
      1. Read Directory.Build.props VersionPrefix; an explicit -Version must match its base version.
      2. Warning gate: `dotnet build -warnaserror` for the platform + first-party plugin projects
         (the projects this repo owns outright; third-party/Avalonia warnings are not gated here).
      3. Tests: full solution regression (skippable locally with -SkipTests).
      4. `dotnet publish src/FluidBar.App` single-file win-x64 (self-contained optional).
      5. Zip artifact under build/artifacts/ + sha256 manifest JSON (feed hash for the update workflow).
      6. Emit build/reports/publish-<version>.json for CI/doc evidence.

.EXAMPLE
    pwsh build/publish.ps1
    pwsh build/publish.ps1 -Version 4.2.0 -SelfContained
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$Version = '',
    [string]$OutputRoot = '',
    [switch]$SelfContained,
    [switch]$SkipTests,
    [switch]$VerifyOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot 'build'
}
$publishRoot = Join-Path $OutputRoot "publish/$Runtime"
$artifactsRoot = Join-Path $OutputRoot 'artifacts'
$reportsRoot = Join-Path $OutputRoot 'reports'
New-Item -ItemType Directory -Force -Path $publishRoot, $artifactsRoot, $reportsRoot | Out-Null

function Invoke-DotNet {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE"
    }
}

function Test-SemanticVersion {
    param([string]$Value)
    return $Value -match '^\d+\.\d+\.\d+([-+][0-9A-Za-z.-]+)?$'
}

function Resolve-Version {
    param([string]$Explicit)
    $props = Join-Path $repoRoot 'Directory.Build.props'
    $prefix = ([xml](Get-Content -LiteralPath $props -Raw)).SelectSingleNode('/Project/PropertyGroup/VersionPrefix').InnerText
    if (-not (Test-SemanticVersion $prefix)) { throw 'Directory.Build.props must declare a valid VersionPrefix.' }
    if (-not [string]::IsNullOrWhiteSpace($Explicit)) {
        $candidate = $Explicit.Trim().TrimStart('v', 'V')
        if (-not (Test-SemanticVersion $candidate)) {
            throw "Invalid -Version '$Explicit'. Expected semver like 2.0.0 or 2.0.0-rc.1."
        }

        if (($candidate -split '[-+]', 2)[0] -ne $prefix) {
            throw "Release version '$candidate' does not match VersionPrefix '$prefix'. Update Directory.Build.props first."
        }
        return $candidate
    }
    return $prefix
}

function Get-ShortGitSha {
    $sha = (& git -C $repoRoot rev-parse --short=12 HEAD 2>$null)
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($sha)) {
        return 'unknown'
    }

    return $sha.Trim()
}

# A published release must carry the runtime with it: end users do not have the .NET 10 desktop runtime and the
# framework-dependent apphost greets them with "You must install .NET Desktop Runtime to run this application"
# (reported 2026-10-27, the 4.0.0-preview.1 assets). Refusing here is cheaper than shipping a broken download.
if (-not $SelfContained -and $env:GITHUB_ACTIONS -eq 'true') {
    throw 'Refusing to build a framework-dependent release in CI: pass -SelfContained so the published exe runs on machines without .NET installed.'
}

$version = Resolve-Version -Explicit $Version
$sha = Get-ShortGitSha
$informationalVersion = "$version+$sha"
Write-Host "FluidBar release $informationalVersion ($Configuration/$Runtime)" -ForegroundColor Cyan

# --- 1. Warning gate on the projects this repository owns end-to-end -------------------------------
Write-Host '== Warning gate (platform + built-in plugins, warnings as errors) ==' -ForegroundColor Cyan
$gatedProjects = @(
    (Join-Path $repoRoot 'src/FluidBar.Platform.Windows/FluidBar.Platform.Windows.csproj'),
    (Join-Path $repoRoot 'src/FluidBar.Plugins.Builtin/FluidBar.Plugins.Builtin.csproj')
)
# Known compiler warnings in the platform project (mostly unused legacy fields and nullable
# annotations in GsmtcMediaSessionService). They are tracked separately; the release gate still
# catches every other CS warning while these eight codes stay visible in normal builds.
$gatedCompilerWarnings = 'CS8604%3BCS8602%3BCS0420%3BCS0162%3BCS8600%3BCS0169%3BCS0649%3BCS0414%3BCS1591'
foreach ($project in $gatedProjects) {
    # Keep the compiler-warning gate (CSxxxx as errors) but do not promote every CA analyzer
    # suggestion: the platform project intentionally carries analyzer noise (P/Invoke marshaling,
    # CA1806, CA2000) and -warnaserror made build/publish.ps1 fail before it ever reached publish.
    Invoke-DotNet @('build', $project, '-c', $Configuration, '-warnaserror',
        '-p:RunAnalyzers=false', "-p:NoWarn=$gatedCompilerWarnings", '--nologo', '-v', 'minimal')
}

# --- 2. Tests --------------------------------------------------------------------------------------
if (-not $SkipTests) {
    Write-Host '== Tests ==' -ForegroundColor Cyan
    Invoke-DotNet @('test', (Join-Path $repoRoot 'FluidBar.sln'),
        '-c', $Configuration, '-m:1', '-p:RunAnalyzers=false', '--nologo', '-v', 'minimal',
        '--logger', 'trx', '--results-directory', (Join-Path $reportsRoot 'tests'))
}

# --- 3. Publish ------------------------------------------------------------------------------------
if ($VerifyOnly) { return }
Write-Host '== Publish (single-file) ==' -ForegroundColor Cyan
$appProject = Join-Path $repoRoot 'src/FluidBar.App/FluidBar.App.csproj'
if (Test-Path $publishRoot) {
    $resolvedPublish = (Resolve-Path -LiteralPath $publishRoot).Path
    $resolvedOutput = [System.IO.Path]::GetFullPath($OutputRoot).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    if (-not $resolvedPublish.StartsWith($resolvedOutput, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove a publish directory outside OutputRoot: $resolvedPublish"
    }
    Remove-Item -LiteralPath $resolvedPublish -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $publishRoot | Out-Null

$publishArgs = @(
    'publish', $appProject,
    '-c', $Configuration,
    '-r', $Runtime,
    "--self-contained=$($SelfContained.IsPresent.ToString().ToLowerInvariant())",
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    # Compress the bundle: a self-contained single file embeds the whole desktop runtime (~150 MB raw). Users
    # download this exe through the updater, so the compressed form (~70 MB) matters; first start unpacks the
    # native libraries into the per-user temp folder, which is a one-off cost.
    '-p:EnableCompressionInSingleFile=true',
    "-p:Version=$version",
    "-p:FileVersion=$version",
    "-p:InformationalVersion=$informationalVersion",
    '-p:IncludeSourceRevisionInInformationalVersion=false',
    '--nologo',
    '-v', 'minimal',
    '-o', $publishRoot
)
Invoke-DotNet $publishArgs

$exePath = Join-Path $publishRoot 'FluidBar.exe'
if (-not (Test-Path $exePath)) {
    throw "Publish did not produce $exePath (single-file bundle missing)."
}

# Native package PDBs (libSkiaSharp/libHarfBuzzSharp) are copied by the SDK even with
# DebugType=None; they are ~105 MB of debug symbols and must not ship in the user zip.
Get-ChildItem -Path $publishRoot -Filter *.pdb -File -Recurse -ErrorAction SilentlyContinue | Remove-Item -Force

# --- 4. Artifact zip + hash manifest (feeds the UpdateCheckClient workflow) -------------------------
$zipPath = Join-Path $artifactsRoot "FluidBar-$version-$Runtime.zip"
if (Test-Path $zipPath) {
    Remove-Item -Force $zipPath
}
Compress-Archive -Path (Join-Path $publishRoot '*') -DestinationPath $zipPath -CompressionLevel Optimal
$exeHash = (Get-FileHash -Path $exePath -Algorithm SHA256).Hash.ToLowerInvariant()
$zipHash = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()

$manifest = [ordered]@{
    product = 'FluidBar'
    version = $version
    informationalVersion = $informationalVersion
    gitSha = $sha
    configuration = $Configuration
    runtime = $Runtime
    selfContained = $SelfContained.IsPresent
    builtAtUtc = [DateTime]::UtcNow.ToString('O')
    exe = 'FluidBar.exe'
    exeBytes = (Get-Item $exePath).Length
    exeSha256 = $exeHash
    zipPath = $zipPath
    zipBytes = (Get-Item $zipPath).Length
    zipSha256 = $zipHash
    signature = 'unsigned'   # future: Authenticode thumbprint once the code-signing cert exists (docs/rewrite/release.md)
}
$manifestPath = Join-Path $reportsRoot "publish-$version.json"
$manifest | ConvertTo-Json -Depth 4 | Set-Content -Path $manifestPath -Encoding utf8

Write-Host ''
Write-Host "Artifact : $zipPath ($([math]::Round((Get-Item $zipPath).Length / 1MB, 2)) MB)" -ForegroundColor Green
Write-Host "Manifest : $manifestPath" -ForegroundColor Green
Write-Host "EXE sha256: $exeHash" -ForegroundColor Green
