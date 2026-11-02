[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$NotesFile = ''
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($NotesFile)) {
    $NotesFile = Join-Path $PSScriptRoot "../docs/rewrite/RELEASE-$Version.md"
}
if (-not (Test-Path -LiteralPath $NotesFile -PathType Leaf)) {
    throw "Release notes file is missing: $NotesFile"
}
$json = (& gh release view "v$Version" --json body) -join "`n"
if ($LASTEXITCODE -ne 0) { throw "Could not read GitHub release v$Version." }
$body = ($json | ConvertFrom-Json).body
if ($body -isnot [string]) { throw "GitHub release v$Version has no text body." }
# Native multiline output is an array in PowerShell. Read JSON and normalize checkout CRLF,
# retaining exact Unicode content so a real encoding regression still fails validation.
$expected = [System.IO.File]::ReadAllText((Resolve-Path -LiteralPath $NotesFile).Path,
    [System.Text.UTF8Encoding]::new($false)).Replace("`r`n", "`n").Trim()
$actual = $body.Replace("`r`n", "`n").Trim()
if (-not [string]::Equals($actual, $expected, [StringComparison]::Ordinal)) {
    throw "GitHub release body does not match $NotesFile (possible encoding loss)."
}
Write-Host "Verified v$Version release notes: UTF-8 content matches, ignoring CRLF/LF."
