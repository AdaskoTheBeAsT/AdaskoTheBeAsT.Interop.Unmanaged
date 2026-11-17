[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$Solution = 'AhuNew.slnx'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Push-Location -LiteralPath $PSScriptRoot
try {
    $solutionPath = Resolve-Path -LiteralPath $Solution

    Write-Host '==> Restoring local .NET tools...' -ForegroundColor Cyan
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet tool restore failed with exit code $LASTEXITCODE."
    }

    Write-Host "==> Step 1/3: Running dotnet format on $solutionPath..." -ForegroundColor Cyan
    dotnet format $solutionPath
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet format failed with exit code $LASTEXITCODE."
    }

    Write-Host "==> Step 2/3: Running ReSharper cleanupcode (Built-in: Reformat Code) on $solutionPath..." -ForegroundColor Cyan
    dotnet jb cleanupcode $solutionPath --profile="Built-in: Reformat Code" --exclude="lib/**;**/bin/**;**/obj/**;**/net10.0/*.xml"
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet jb cleanupcode failed with exit code $LASTEXITCODE."
    }

    Write-Host "==> Step 3/3: Running StyleCop layout fixers (SA1413, SA1500, SA1134) on $solutionPath..." -ForegroundColor Cyan
    dotnet format analyzers $solutionPath --diagnostics SA1413 SA1500 SA1134
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet format analyzers (SA1413, SA1500, SA1134) failed with exit code $LASTEXITCODE."
    }

    Write-Host '==> Formatting completed successfully.' -ForegroundColor Green
}
finally {
    Pop-Location
}
