# Validation notes

This file separates local evidence from remaining cross-platform CI gaps.

## Current 4.0.1 validation (2026-10-25, Windows x64)

The repository defaults to `Version=4.0.1`, targets **six frameworks** (`net10.0`,
`net9.0`, `net8.0`, `net481`, `net48`, and `net472`), and selects .NET SDK
`10.0.402` with `Microsoft.Testing.Platform` in `global.json`.

| Check | Result |
| --- | --- |
| Release solution build | Passed for all six TFMs with 0 warnings and 0 errors. |
| Full test matrix | Passed 858 tests across `net10.0`, `net9.0`, `net8.0`, `net481`, `net48`, and `net472` (144 each on modern .NET, 142 each on .NET Framework); 0 failed and 0 skipped. |
| Package contents | `test/Validate-Package.ps1` verified all six DLL and XML documentation files, portable PDBs in the `.snupkg`, Source Link mappings, license, README, empty dependency groups, and absence of test/native artifact leakage. |
| Deterministic builds | `test/Verify-DeterministicBuild.ps1` verified identical SHA256 hashes for `net10.0` and `net472` DLL and portable PDB outputs across independent checkout paths. |
| Consumer smoke and benchmark projects | `samples/NativeSmoke` and `benchmarks/CallbackBenchmarks` built with 0 warnings and 0 errors; `NativeSmoke` passed against `fixture.dll`. |

```powershell
dotnet build .\AdaskoTheBeAsT.Interop.Unmanaged.slnx -c Release
dotnet test --solution .\AdaskoTheBeAsT.Interop.Unmanaged.slnx -c Release --no-build
dotnet pack .\src\AdaskoTheBeAsT.Interop.Unmanaged\AdaskoTheBeAsT.Interop.Unmanaged.csproj -c Release -o artifacts\packages
.\test\Validate-Package.ps1 -Directory artifacts\packages
.\test\Verify-DeterministicBuild.ps1
```

## Historical nine-target validation pass (2026-09-09, Windows x64)

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

## Remaining evidence gaps

- Linux, macOS, and ARM64 behavior has compile-time support, but no local execution evidence from this Windows x64 pass.
- Mono execution on the legacy `net4.x` targets remains unverified.
- musl/Alpine remains unsupported by the legacy `libdl.so.2` fallback and unverified for modern targets.
- Windows resource-only loading behavior is covered only on Windows and cannot be claimed as portable inspection behavior.
- Source Link document mappings are embedded in local symbols; remote source checksums can only be fully resolved after the corresponding commit is published.
- Remote reusable-workflow execution and SonarCloud coverage/quality-gate results require a GitHub Actions run on the target branch or pull request.
