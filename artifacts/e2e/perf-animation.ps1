param(
    [string]$OutDir = "build/reports/perf",
    [int]$Runs = 3
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not [IO.Path]::IsPathRooted($OutDir)) { $OutDir = Join-Path $root $OutDir }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$repo = $root
for ($run = 1; $run -le $Runs; $run++) {
    dotnet test (Join-Path $repo "tests/FluidBar.Ui.Tests/FluidBar.Ui.Tests.csproj") -c Debug --nologo -p:BaseOutputPath=artifacts/testbin/island-body/ --filter FullyQualifiedName~MotionRenderBench --logger "console;verbosity=quiet"
    $source = Join-Path $repo "build/reports/perf/motion-render-bench.csv"
    if (Test-Path $source) {
        Copy-Item $source (Join-Path $OutDir ("animation-run{0}.csv" -f $run)) -Force
    }
}
Write-Output "Wrote $Runs motion runs to $OutDir"

