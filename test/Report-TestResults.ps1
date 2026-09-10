param(
    [Parameter(Mandatory)][string] $Path,
    [switch] $RequirePassed
)
$ErrorActionPreference = 'Stop'
[xml] $report = Get-Content -LiteralPath $Path -Raw
$counts = $report.TestRun.ResultSummary.Counters
if ($null -eq $counts) { throw "Missing test counters in $Path" }
$summary = "$Path : total=$($counts.total), passed=$($counts.passed), failed=$($counts.failed), skipped=$($counts.notExecuted)"
Write-Host $summary
if ($env:GITHUB_STEP_SUMMARY) { Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value $summary }
if ($RequirePassed -and [int]$counts.passed -le 0) { throw 'No native tests passed.' }
if ([int]$counts.failed -gt 0) { throw 'Test failures found.' }
