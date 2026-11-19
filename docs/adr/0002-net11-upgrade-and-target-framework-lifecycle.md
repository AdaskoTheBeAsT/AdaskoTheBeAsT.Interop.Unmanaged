# ADR 0002: .NET 11 upgrade, C# 14 polyfills, and 5.0.0 target framework lifecycle

- Status: Accepted
- Date: 2026-11-17
- Deciders: @AdaskoTheBeAsT
- Release: v5.0.0

## Context

Preparing `AdaskoTheBeAsT.Interop.Unmanaged` for `5.0.0` combined two repository-wide lifecycle changes with new configuration-driven public types (`PeImageValidationOptions`, `PeImportSymbolRewrite`, `PeVerifiedBytePatch`, `IsolatedModuleSpecification`, and `IsolatedLibrarySetOptions`):

1. **Runtime support lifecycle:** `.NET 11` (`net11.0`) is now the current target alongside `.NET 10` (`net10.0`) LTS and supported `.NET Framework` servicing baselines (`net481`, `net48`, and `net472`), while `.NET 8` (`net8.0`) and `.NET 9` (`net9.0`) have reached end of support in this repository's maintenance policy.
2. **SDK and analyzer baseline:** Adopting `<LangVersion>14.0</LangVersion>` and `.NET SDK 11.0.100` in `global.json` required upgrading Roslyn and third-party analyzers (`Microsoft.CodeAnalysis.* 5.12.0`, `Microsoft.CodeAnalysis.NetAnalyzers 11.0.100`, `AdaskoTheBeAsT.* *.300`, `Meziantou.Analyzer 3.0.341`, `Microsoft.VisualStudio.Threading.Analyzers 18.12.1`, `Roslynator.* 5.0.1`, `SonarAnalyzer.CSharp 10.36.0.15337`, `StyleCop.Analyzers 1.3.180`) and test tooling (`Microsoft.NET.Test.Sdk 18.12.0`, `Microsoft.Testing.Extensions.GitHubActionsReport 2.5.3`, `xunit.v3 4.0.3`) so `TreatWarningsAsErrors` remains clean across every target.
3. **Immutable option records on `.NET Framework`:** C# 14 `init` accessors and `required` members make complex interop specifications (`IsolatedLibrarySetOptions`, `IsolatedModuleSpecification`, `PeVerifiedBytePatch`) self-documenting and immutable after construction, but `net472`, `net48`, and `net481` BCLs do not include the compiler-synthesized metadata attributes required by Roslyn (`IsExternalInit`, `RequiredMemberAttribute`, `CompilerFeatureRequiredAttribute`, `SetsRequiredMembersAttribute`).

## Decision

1. **Ship `5.0.0` targeting five supported frameworks (`net11.0`, `net10.0`, `net481`, `net48`, `net472`):**
   - Add `net11.0` to `src/AdaskoTheBeAsT.Interop.Unmanaged` and `test/unit/AdaskoTheBeAsT.Interop.Unmanaged.Test`, move `samples/NativeSmoke` and `benchmarks/CallbackBenchmarks` to `net11.0`, and enable trimming and NativeAOT analyzers (`EnableTrimAnalyzer`, `EnableAotAnalyzer`) on `net11.0`.
   - Remove the out-of-support `net8.0` and `net9.0` target frameworks as an explicit `5.0.0` major-version breaking change, and align `test/Validate-Package.ps1`, `README.md`, `CHANGELOG.md`, `MIGRATION.md`, and `docs/validation.md` with the five shipped targets.
2. **Pin `.NET SDK 11.0.100` and C# `14.0` with zero-warning analyzer enforcement:**
   - Keep `<LangVersion>14.0</LangVersion>` and `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` in `Directory.Build.props` across all library, test, sample, and benchmark projects.
3. **Provide internal compiler polyfills instead of external polyfill packages:**
   - Add `CompilerPolyfills.cs` (`internal static class IsExternalInit`, `internal sealed class RequiredMemberAttribute`, `internal sealed class CompilerFeatureRequiredAttribute`, and `internal sealed class SetsRequiredMembersAttribute`) conditionally compiled `#if !NET5_0_OR_GREATER` inside `AdaskoTheBeAsT.Interop.Unmanaged` so `init` and `required` members work identically across all five target frameworks while keeping NuGet dependency groups completely empty.

## Consequences

- Positive: Package consumers on `net11.0` and `net10.0` bind directly to modern assets with full `[SupportedOSPlatform]`, trimming, and NativeAOT annotations, while `.NET Framework 4.7.2–4.8.1` consumers retain zero-dependency package installation and identical C# `init`/`required` API ergonomics.
- Positive: Package validation (`Validate-Package.ps1`) and deterministic build verification (`Verify-DeterministicBuild.ps1`) enforce identical artifact quality across all five shipped targets.
- Trade-off: Applications still targeting `net8.0` or `net9.0` must retarget to `net10.0` or `net11.0` before upgrading to `5.0.0`, or remain on `4.0.1`.
