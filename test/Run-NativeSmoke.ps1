param(
    [Parameter(Mandatory)][string] $Executable,
    [Parameter(Mandatory)][string] $Library,
    [int] $TimeoutSeconds = 60
)
$ErrorActionPreference = 'Stop'
$start = [Diagnostics.ProcessStartInfo]::new((Resolve-Path -LiteralPath $Executable).Path)
$start.UseShellExecute = $false
$start.ArgumentList.Add((Resolve-Path -LiteralPath $Library).Path)
$process = [Diagnostics.Process]::Start($start)
try {
    if (!$process.WaitForExit($TimeoutSeconds * 1000)) {
        $process.Kill($true)
        $process.WaitForExit()
        throw "Native smoke process timed out after $TimeoutSeconds seconds."
    }
    if ($process.ExitCode -ne 0) { throw "Native smoke process failed: $($process.ExitCode)" }
}
finally {
    $process.Dispose()
}
