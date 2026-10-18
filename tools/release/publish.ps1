<#
.SYNOPSIS
  Publishes a self-contained FluidBar build for Windows.

.DESCRIPTION
  Produces build/release/<runtime>/FluidBar.exe (single-file, framework-dependent by default)
  plus README/PROJECT-NOTES/CHANGELOG and a zip next to the folder. Run from the repo root or
  let the script resolve it.

  Examples:
    pwsh tools/release/publish.ps1
    pwsh tools/release/publish.ps1 -SelfContained
    pwsh tools/release/publish.ps1 -Runtime win-arm64
#>
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',
    [switch]$SelfContained,
    [switch]$SkipZip
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not (Test-Path (Join-Path $repo 'FluidBar.sln'))) {
    throw "Cannot locate FluidBar.sln from $PSScriptRoot"
}

$out = Join-Path $repo "build/release/$Runtime"
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Force -Path $out | Out-Null

Write-Host "Publishing FluidBar ($Runtime, self-contained=$SelfContained)..."
$args = @(
    'publish', (Join-Path $repo 'src/FluidBar.App/FluidBar.App.csproj'),
    '-c', 'Release',
    '-r', $Runtime,
    "--self-contained:$($SelfContained.ToString().ToLowerInvariant())",
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true'
)
if ($SelfContained) {
    # Only valid for self-contained bundles.
    $args += '-p:EnableCompressionInSingleFile=true'
}
$args += @(
    '-p:DebugType=None',
    '-p:DebugSymbols=false',
    '-o', $out,
    '-v', 'minimal',
    '--nologo'
)
& dotnet @args
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

foreach ($file in @('README.md', 'PROJECT-NOTES.md', 'CHANGELOG.md')) {
    $source = Join-Path $repo $file
    if (Test-Path $source) { Copy-Item $source (Join-Path $out $file) -Force }
}
$docs = Join-Path $repo 'docs/plugin'
if (Test-Path $docs) { Copy-Item $docs (Join-Path $out 'docs-plugin') -Recurse -Force }

$exe = Join-Path $out 'FluidBar.exe'
if (-not (Test-Path $exe)) { throw "Published exe missing: $exe" }
$sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
$hash = (Get-FileHash $exe -Algorithm SHA256).Hash
Write-Host "Published: $exe ($sizeMb MB)"
Write-Host "SHA256: $hash"

$manifest = [ordered]@{
    product = 'FluidBar'
    version = (Get-Date).ToString('yyyy.MM.dd')
    runtime = $Runtime
    selfContained = [bool]$SelfContained
    exe = $exe
    sizeMb = $sizeMb
    sha256 = $hash
    publishedAt = (Get-Date).ToString('o')
}
$manifest | ConvertTo-Json | Set-Content (Join-Path $out 'publish.json') -Encoding utf8NoBOM

if (-not $SkipZip) {
    $zip = Join-Path $repo "build/release/FluidBar-$Runtime.zip"
    if (Test-Path $zip) { Remove-Item -Force $zip }
    Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip -CompressionLevel Optimal
    Write-Host "Zip: $zip"
}

