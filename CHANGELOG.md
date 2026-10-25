# Changelog

Notable changes to `AdaskoTheBeAsT.Interop.Unmanaged`.
Historical release dates reflect tagged commit dates, not independently verified
NuGet publication dates.

See the [migration guide](MIGRATION.md) for upgrade steps.

## [4.0.1] - 2026-10-25

### Fixed

- Add `where TDelegate : class, Delegate` to `PinDelegate<TDelegate>` and `PinConcreteDelegate<TDelegate>`, reject abstract `Delegate` and `MulticastDelegate` type arguments in `PinConcreteDelegate<TDelegate>`, and call `Marshal.GetFunctionPointerForDelegate<TDelegate>(callback)`.
- Enable `ContinuousIntegrationBuild` and `Deterministic` when `GITHUB_ACTIONS` is `true` in `AdaskoTheBeAsT.Interop.Unmanaged.csproj`, and use `$(MSBuildThisFileDirectory)` for the SARIF `ErrorLog` path in `Directory.Build.targets`.
- Align `test/Validate-Package.ps1`, `test/Verify-DeterministicBuild.ps1`, and `test/Directory.Build.props` with the six supported `4.0.x` target frameworks (`net10.0`, `net9.0`, `net8.0`, `net481`, `net48`, `net472`).

### Changed

- Update `global.json` to .NET SDK `10.0.402` and upgrade build analyzers (`StyleCop.Analyzers 1.3.151-beta`, `SonarAnalyzer.CSharp 10.35.0.4138`, `Meziantou.Analyzer 3.0.316`, `AdaskoTheBeAsT.CodeCracker.CSharp 1.1.0.270`, `Microsoft.CodeAnalysis.NetAnalyzers 10.0.402`, `Microsoft.VisualStudio.Threading.Analyzers 18.12.1`, `Roslynator.* 5.0.1`) and unit-test packages (`Microsoft.NET.Test.Sdk 18.11.0`, `Microsoft.Testing.Extensions.GitHubActionsReport 2.5.1`, `Moq 4.21.0`, `xunit.v3 4.0.2`, `xunit.analyzers 2.2.0`, `xunit.runner.visualstudio 4.0.1`).
- Preserve explicit `IntPtr` and `UIntPtr` types across interop declarations with `#pragma warning disable SA1121` and add targeted `CC0022` suppressions in disposal unit tests.
- Expand unit tests for `PinConcreteDelegate` abstract delegate validation and collectible `AssemblyLoadContext` callbacks using a collectible struct parameter.
- Add [ADR 0001](docs/adr/0001-delegate-pin-constraints-and-analyzer-baseline.md) and update documentation and validation notes.

## [4.0.0]

Public callback lifetime scopes, stricter validation, and improved loader
diagnostics. See the [migration guide](MIGRATION.md#from-300-to-400) for changes
that require attention when upgrading from 3.0.0.

### Breaking changes

- Remove the `net462`, `net47`, and `net471` package targets. Remaining targets
  are `net10.0`, `net9.0`, `net8.0`, `net481`, `net48`, and `net472`.
- Reject embedded NUL characters in library and export names with
  `ArgumentException` before calling a native loader.
- Validate `GetDelegateForFunctionPointer<T>` as a raw, non-marshaling API.
  Unsupported signatures now fail before invocation. Rejected types include
  managed references, `bool`, `char`, `decimal`, nullable values, managed
  `ref`/`out` parameters or returns, and unsupported struct layouts.
  `[MarshalAs]` and `SetLastError` are also rejected.
- Require a concrete, closed delegate and restrict raw calling conventions to
  `Cdecl`, `StdCall`, and `Winapi`.
- Add dynamic-code and trimming annotations to reflection/emission-based public
  APIs on modern targets. Consumers treating analyzer warnings as errors may need
  to change call sites.

### Added

- Public `PinDelegate` and `PinConcreteDelegate` factories for `DelegatePin`
  callback lifetime scopes. `PinConcreteDelegate` avoids proxy emission and
  rejects generic callback types.
- Explicit dynamic-code guards with guidance toward no-emission alternatives.
- Controlled native fixtures, callback lifecycle and collectible-context tests,
  trimmed/NativeAOT consumer samples, callback benchmarks, and package and
  deterministic-build validation scripts.
- A migration guide and this changelog.

### Fixed

- Adapt the default Windows search flags for bare module names by omitting
  `LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR` while retaining System32-only search.
  Fully qualified paths retain the original defaults.
- Include the system error description in Windows load failures while preserving
  the native error code.
- Preserve the original loader exception on modern non-Windows load failures,
  set the wrapper's native error code to zero, and preserve the ambient last error.
- Generate generic callback proxies from the callback's runtime type, including
  when it is passed through a base `Delegate` reference.
- Preserve full multicast invocation behavior and parameter/return marshaling
  metadata in callback proxies.
- Cache proxies safely for concurrent creation. Use weak-key caching and
  collectible generated assemblies on modern .NET so released proxies do not
  permanently root collectible plugin contexts. Legacy generated assemblies
  remain non-collectible.

### Changed

- Use .NET SDK `10.0.401`, xUnit v3, and Microsoft.Testing.Platform for repository
  tests; update test and analyzer dependencies.
- Clarify module ownership, callback shutdown, platform validation limits, raw
  ABI restrictions, and trimming/NativeAOT boundaries in the README.

## [3.0.0] - 2026-04-20

### Breaking changes

- Replace the `netstandard2.0` target with explicit .NET Framework targets:
  `net462`, `net47`, `net471`, `net472`, `net48`, and `net481`.
  Keep `net8.0`, `net9.0`, and `net10.0`.

### Added

- Cross-platform loading and export lookup: `NativeLibrary` on modern
  non-Windows .NET and a legacy Mono `dlopen`/`dlsym` fallback for Linux/macOS.
- Instance and static `TryGetExport` methods for raw export addresses.
- Platform-aware safe-handle release and explicit handle references during
  export lookup.

### Fixed

- Copy `[UnmanagedFunctionPointer]` calling convention and attribute fields to
  generated generic callback proxies.

### Changed

- Expand cross-platform API documentation, tests, CI, and repository tooling.

## [2.0.0] - 2026-04-07

### Changed

- Centralize library loading and typed export lookup.
- Add explicit argument checks for typed export lookup, null callbacks, zero raw
  pointers, and non-delegate type arguments.
- Expand XML documentation and README guidance for loading flags, callback roots,
  module ownership, and marshaling.
- Update dependencies. Retain `netstandard2.0`, `net8.0`, `net9.0`, and `net10.0`.

## [1.0.0] - 2025-11-23

### Added

- First tagged release with Windows native library loading, `LoadLibraryFlags`,
  `SafeLibraryHandle` ownership, and instance/static typed export lookup.
- Managed callback pointers with keep-alive binders, generic callback proxy
  support, and an IL-emitted raw function-pointer-to-delegate helper.
- Unit tests, XML documentation, and package metadata.
- Targets for `netstandard2.0`, `net8.0`, `net9.0`, and `net10.0`.

[4.0.1]: https://github.com/AdaskoTheBeAsT/AdaskoTheBeAsT.Interop.Unmanaged/compare/v4.0.0...v4.0.1
[4.0.0]: https://github.com/AdaskoTheBeAsT/AdaskoTheBeAsT.Interop.Unmanaged/compare/v3.0.0...v4.0.0
[3.0.0]: https://github.com/AdaskoTheBeAsT/AdaskoTheBeAsT.Interop.Unmanaged/compare/v2.0.0...v3.0.0
[2.0.0]: https://github.com/AdaskoTheBeAsT/AdaskoTheBeAsT.Interop.Unmanaged/compare/v1.0.0...v2.0.0
[1.0.0]: https://github.com/AdaskoTheBeAsT/AdaskoTheBeAsT.Interop.Unmanaged/tree/v1.0.0
