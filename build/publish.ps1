#Requires -Version 7.0
<#
.SYNOPSIS
    Release build/publish for FluidBar (M6). Runs the first-party warning gate + tests, publishes a
    single-file win-x64 build, stamps the version, hashes the artifact and produces a zip.

.DESCRIPTION
    Steps:
      1. Resolve the version (explicit -Version, else latest `v*` git tag, else Directory.Build.props VersionPrefix).
      2. Warning gate: `dotnet build -warnaserror` for the platform + first-party plugin projects
         (the projects this repo owns outright; third-party/Avalonia warnings are not gated here).
      3. Tests: platform tests + built-in plugin tests (skippable with -SkipTests).
      4. `dotnet publish src/FluidBar.App` single-file win-x64 (self-contained optional).
      5. Zip artifact under build/artifacts/ + sha256 manifest JSON (feed hash for the update workflow).
      6. Emit build/reports/publish-<version>.json for CI/doc evidence.

.EXAMPLE
    pwsh build/publish.ps1
    pwsh build/publish.ps1 -Version 2.0.0 -SelfContained -SkipTests
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$Version = '',
    [string]$OutputRoot = '',
    [switch]$SelfContained,
    [switch]$SkipTests
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
    if (-not [string]::IsNullOrWhiteSpace($Explicit)) {
        $candidate = $Explicit.Trim().TrimStart('v', 'V')
        if (-not (Test-SemanticVersion $candidate)) {
            throw "Invalid -Version '$Explicit'. Expected semver like 2.0.0 or 2.0.0-rc.1."
        }

        return $candidate
    }

    # Prefer a semver git tag (v2.0.0); legacy archived tags (v1-final) are ignored.
    $tag = (& git -C $repoRoot describe --tags --match 'v[0-9]*' --abbrev=0 2>$null)
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($tag)) {
        $candidate = $tag.Trim().TrimStart('v', 'V')
        if (Test-SemanticVersion $candidate) {
            return $candidate
        }
    }

    $props = Join-Path $repoRoot 'Directory.Build.props'
    if (Test-Path $props) {
        $match = Select-String -Path $props -Pattern '<VersionPrefix>([^<]+)</VersionPrefix>'
        if ($match -and (Test-SemanticVersion $match.Matches[0].Groups[1].Value)) {
            return $match.Matches[0].Groups[1].Value
        }
    }

    return '0.0.0-dev'
}

function Get-ShortGitSha {
    $sha = (& git -C $repoRoot rev-parse --short=12 HEAD 2>$null)
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($sha)) {
        return 'unknown'
    }

    return $sha.Trim()
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
foreach ($project in $gatedProjects) {
    Invoke-DotNet @('build', $project, '-c', $Configuration, '-warnaserror', '--nologo', '-v', 'minimal')
}

# --- 2. Tests --------------------------------------------------------------------------------------
if (-not $SkipTests) {
    Write-Host '== Tests ==' -ForegroundColor Cyan
    Invoke-DotNet @('test',
        (Join-Path $repoRoot 'tests/FluidBar.Platform.Tests/FluidBar.Platform.Tests.csproj'),
        '-c', $Configuration, '--nologo', '-v', 'minimal')
    Invoke-DotNet @('test',
        (Join-Path $repoRoot 'tests/FluidBar.BuiltinPlugins.Tests/FluidBar.BuiltinPlugins.Tests.csproj'),
        '-c', $Configuration, '--nologo', '-v', 'minimal')
}

# --- 3. Publish ------------------------------------------------------------------------------------
Write-Host '== Publish (single-file) ==' -ForegroundColor Cyan
$appProject = Join-Path $repoRoot 'src/FluidBar.App/FluidBar.App.csproj'
if (Test-Path $publishRoot) {
    Remove-Item -Recurse -Force $publishRoot
}
New-Item -ItemType Directory -Force -Path $publishRoot | Out-Null

$publishArgs = @(
    'publish', $appProject,
    '-c', $Configuration,
    '-r', $Runtime,
    "--self-contained=$($SelfContained.IsPresent.ToString().ToLowerInvariant())",
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    "-p:Version=$version",
    "-p:FileVersion=$version",
    "-p:InformationalVersion=$informationalVersion",
    '--nologo',
    '-v', 'minimal',
    '-o', $publishRoot
)
Invoke-DotNet $publishArgs

$exePath = Join-Path $publishRoot 'FluidBar.exe'
if (-not (Test-Path $exePath)) {
    throw "Publish did not produce $exePath (single-file bundle missing)."
}

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
