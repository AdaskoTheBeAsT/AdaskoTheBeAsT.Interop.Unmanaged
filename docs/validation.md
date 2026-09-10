# Validation notes

This file separates local evidence from configured-but-not-yet-executed CI coverage.
The final local pass was run on 2026-09-09 on Windows x64.

## Local results

| Check | Result |
| --- | --- |
| Release solution build | Passed for all nine TFMs with 0 warnings and 0 errors. |
| Full test matrix | Passed 1,281 tests: 143 each on `net10.0`, `net9.0`, and `net8.0`; 142 each on `net481`, `net48`, `net472`, `net471`, `net47`, and `net462`; 0 failed and 0 skipped. |
| Windows x86 native tests | Passed all 143 `net10.0` tests with an architecture-matched x86 fixture. |
| Package build and compatibility | `dotnet pack` for `3.0.1-ci` passed package validation against the released `3.0.0` baseline. |
| Package contents | All nine DLL and XML documentation files, portable PDBs in the `.snupkg`, Source Link mappings, license, README, empty dependency groups, and absence of test/native artifact leakage were verified. |
| Trimmed package consumer | Windows x64 publish and isolated native lifecycle execution passed. |
| NativeAOT package consumer | Windows x64 publish and isolated native lifecycle execution passed. Dynamic-code APIs produced the expected `PlatformNotSupportedException` rejection. |
| Deterministic builds | Independent checkout paths produced identical SHA256 hashes for `net10.0` and `net462` DLL and portable PDB outputs. |
| Callback proxy benchmark utility | Built and ran successfully. Results are diagnostic measurements, not pass/fail thresholds. |
| Coverage collection | All 1,281 tests passed again under `dotnet-coverage`. The XML contains nine library modules and no test assemblies. |
| Workflow and script syntax | YAML parsed, SDK setup and matrix assertions passed, and all six rendered workflow PowerShell blocks plus four validation scripts parsed successfully. `actionlint` was unavailable. |

The pre-change baseline was 110 passing tests on each of the nine TFMs.

## Local coverage evidence

The XML report contains nine distinct module IDs, all named
`AdaskoTheBeAsT.Interop.Unmanaged.dll`. No test `.dll` or `.exe` modules appear.
All referenced source files exist and predate the report.

| Module group | Count | Line coverage per module | Block coverage per module |
| --- | --- | --- | --- |
| Modern .NET | 3 | 91.02% | 90.93% |
| .NET Framework | 6 | 83.93% | 83.22% |

These are per-module metrics, not a combined source-line or SonarCloud percentage.
Each modern module has 233 fully covered, 3 partially covered, and 20 uncovered
lines; each Framework module has 235 fully covered, 3 partially covered, and 42
uncovered lines.

Managed `NativeLoader` functions are included. `LoadWindowsLibrary`, including its
failure diagnostics, has 100% line and block coverage in every module. Unix
branches and some defensive paths remain uncovered by this Windows-only run.
The report was kept in the system temporary directory; no report conversion or
SonarCloud analysis was run.

## Local commands

```powershell
dotnet build .\AdaskoTheBeAsT.Interop.Unmanaged.slnx -c Release
dotnet test .\AdaskoTheBeAsT.Interop.Unmanaged.slnx -c Release --no-build
dotnet test .\test\unit\AdaskoTheBeAsT.Interop.Unmanaged.Test\AdaskoTheBeAsT.Interop.Unmanaged.Test.csproj -c Release -f net10.0 --arch x86 -p:PlatformTarget=x86
dotnet pack .\src\AdaskoTheBeAsT.Interop.Unmanaged\AdaskoTheBeAsT.Interop.Unmanaged.csproj -c Release -p:Version=3.0.1-ci -o <temporary-package-directory>
.\test\Validate-Package.ps1 -Directory <temporary-package-directory>
.\test\Verify-DeterministicBuild.ps1
$coverageReport = Join-Path $env:TEMP ("interop-coverage-" + [guid]::NewGuid().ToString('N') + '.xml')
dotnet-coverage collect "dotnet test .\AdaskoTheBeAsT.Interop.Unmanaged.slnx -c Release --no-build" -s .\coverage.settings.xml -f xml -o $coverageReport --nologo
```

The package consumer pass packed a separate local `3.0.1-final` package, restored
`samples/NativeSmoke` from an explicit temporary NuGet configuration, published both
`Trimmed` and `Aot` modes for `win-x64`, and executed both outputs through
`test/Run-NativeSmoke.ps1`.

## Configured remote coverage

`.github/workflows/native-validation.yml` defines 18 test jobs:

- `net8.0`, `net9.0`, and `net10.0` on Windows, Linux, and Intel macOS x64
- all six legacy .NET Framework TFMs on Windows x64
- `net10.0` on Windows x86
- `net10.0` on Linux ARM64 and macOS ARM64

It also defines trimmed and NativeAOT package-consumer jobs for Windows x64,
Linux x64, and Intel macOS x64. Package validation and deterministic-build
verification run in that workflow. SDK setup reads `global.json` in both job
groups; test jobs additionally install .NET 8 and 9 for those runtime tests.
`ci.yml` requires this validation workflow before
the existing SonarQube/package workflow and passes the NuGet secret only for `v*` tags.

These workflow definitions are not execution evidence. They still need an actual
GitHub Actions run on the target branch or pull request.

## Remaining evidence gaps

- Linux and macOS behavior has compile-time support and configured CI jobs, but no local execution evidence from this pass.
- ARM64 behavior is configured for Linux and macOS CI, but has not been executed locally.
- Mono execution on the legacy `net4.x` targets remains unverified.
- musl/Alpine remains unsupported by the legacy `libdl.so.2` fallback and unverified for modern targets.
- Windows resource-only loading behavior is covered only on Windows and cannot be claimed as portable inspection behavior.
- Source Link document mappings are embedded in local symbols; remote source checksums can only be fully resolved after the corresponding commit is published.
- Remote workflow syntax, runner images, secrets, repository variables, and reusable-workflow behavior require a real GitHub Actions execution.
- SonarCloud coverage and quality-gate results require a fresh remote analysis; the local XML report does not update them.
