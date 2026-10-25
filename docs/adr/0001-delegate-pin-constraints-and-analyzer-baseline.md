# ADR 0001: DelegatePin constraints, explicit IntPtr types, and 4.0.1 validation alignment

- Status: Accepted
- Date: 2026-10-25
- Deciders: @AdaskoTheBeAsT
- Release: v4.0.1

## Context

Following the `4.0.0` release, upgrading the .NET SDK (`10.0.402`), build analyzers (`StyleCop.Analyzers 1.3.151-beta`, `SonarAnalyzer.CSharp 10.35.0.4138`, `Meziantou.Analyzer 3.0.316`, `AdaskoTheBeAsT.CodeCracker.CSharp 1.1.0.270`, `Microsoft.CodeAnalysis.NetAnalyzers 10.0.402`, `Microsoft.VisualStudio.Threading.Analyzers 18.12.1`, `Roslynator.* 5.0.1`), and xUnit v3 test packages surfaced several maintenance and consistency items:

1. **`SA1121` on `IntPtr` and `UIntPtr`:** `StyleCop.Analyzers 1.3.151-beta` treats `IntPtr` and `UIntPtr` under C# 11+ as having built-in type aliases (`nint` and `nuint`) and flags every `IntPtr`/`UIntPtr` usage in code and XML `<see cref="IntPtr"/>` documentation comments.
2. **`CC0022` on expression-bodied disposal lambdas:** `AdaskoTheBeAsT.CodeCracker.CSharp 1.1.0.270` reports `CC0022` on test variables whose `.Dispose()` method is invoked inside an expression-bodied `Action` lambda (`Action act = () => instance.Dispose();`).
3. **`PinDelegate<TDelegate>` and `PinConcreteDelegate<TDelegate>` constraints:** `GetFunctionPointerForDelegate<T>` constrained `T` to `where T : class, Delegate`, whereas `PinDelegate<TDelegate>` and `PinConcreteDelegate<TDelegate>` used `where TDelegate : Delegate`. In `PinConcreteDelegate<TDelegate>`, passing `TDelegate = Delegate` or `MulticastDelegate` with a concrete delegate instance was not rejected, and the non-generic `Marshal.GetFunctionPointerForDelegate(Delegate)` overload was called instead of `Marshal.GetFunctionPointerForDelegate<TDelegate>(TDelegate)`.
4. **Validation scripts and documentation drift:** `test/Validate-Package.ps1`, `test/Verify-DeterministicBuild.ps1`, and `test/Directory.Build.props` still referenced the `net462`, `net47`, and `net471` targets removed in `4.0.0`, and `Directory.Build.targets` referenced an undefined `$(RepositoryRoot)` property for SARIF output.
5. **`.NET Framework` Debug `DynamicMethod` `calli` stub recycling and transient fixture cleanup locks:** On `.NET Framework`, creating an uncached `DynamicMethod` owned by `typeof(UnmanagedLibrary)` for each `GetDelegateForFunctionPointer<T>` call causes Debug builds (`DisableOptimizations`) to emit a non-inlined `PInvokeCalliStub` associated with the `DynamicMethodDesc` and signature token `0x11000001`. Once a `DynamicMethod` is garbage-collected and its `DynamicMethodDesc` is recycled from `DynamicMethodTable::m_FreeList` for a different delegate signature, `.NET Framework` can invoke the stale `PInvokeCalliStub`, corrupting return values or raising `AccessViolationException`. In addition, `NativeFixture.Dispose()` deleting the temporary directory immediately after `FreeLibrary` can transiently encounter `UnauthorizedAccessException` on Windows when a coverage profiler or antivirus filter still holds an image section reference on `fixture.dll`.

## Decision

1. **Release as patch version `4.0.1`:**
   - No new public types or methods are added, and tightening `PinDelegate<TDelegate>` / `PinConcreteDelegate<TDelegate>` to `where TDelegate : class, Delegate` (and rejecting abstract `Delegate`/`MulticastDelegate` in `PinConcreteDelegate`) matches the existing documented contract of concrete delegate pinning. Increment the package version from `4.0.0` to `4.0.1`.
2. **Preserve explicit `IntPtr`/`UIntPtr` types with `#pragma warning disable SA1121`:**
   - Keep `IntPtr` and `UIntPtr` in public signatures, P/Invoke declarations, and XML documentation, and add `#pragma warning disable SA1121` in files that use native pointer types rather than disabling `SA1121` globally in `.editorconfig` or `AdaskoTheBeAsT.ruleset`.
   - Use targeted `#pragma warning disable CC0022` around explicitly disposed test instances in `DelegatePinTests` and `UnmanagedLibraryConstructorTests`.
3. **Align `PinDelegate` and `PinConcreteDelegate` with `GetFunctionPointerForDelegate`:**
   - Constrain both `PinDelegate<TDelegate>` and `PinConcreteDelegate<TDelegate>` to `where TDelegate : class, Delegate`.
   - Reject `typeof(TDelegate) == typeof(Delegate)` and `typeof(TDelegate) == typeof(MulticastDelegate)` in `PinConcreteDelegate<TDelegate>` and call `Marshal.GetFunctionPointerForDelegate<TDelegate>(callback)`.
   - Verify collectible `AssemblyLoadContext` unloading when a generic callback uses a struct parameter type defined inside the collectible assembly.
4. **Align build scripts, CI properties, and documentation with the six `4.0.x` targets:**
   - Update `test/Validate-Package.ps1`, `test/Verify-DeterministicBuild.ps1`, and `test/Directory.Build.props` to the six shipped target frameworks (`net10.0`, `net9.0`, `net8.0`, `net481`, `net48`, `net472`).
   - Enable `ContinuousIntegrationBuild` and `Deterministic` when `GITHUB_ACTIONS` is `true`, and use `$(MSBuildThisFileDirectory)` in `Directory.Build.targets`.
   - Align `README.md`, `CHANGELOG.md`, `MIGRATION.md`, and `docs/validation.md` with the current workflow and `4.0.1` validation pass.
5. **Cache `GetDelegateForFunctionPointer<T>` `DynamicMethod` thunks and harden `NativeFixture.Dispose()`:**
   - Cache one `DynamicMethod` per `(Type delegateType, CallingConvention conv)` in a `ConditionalWeakTable<Type, Lazy<DynamicMethod>[]>`, hosted on `typeof(object)` with `skipVisibility: true`, and bind each returned delegate to a lightweight `RawCallTarget` holding the target `IntPtr`.
   - Retry `Directory.Delete` in `NativeFixture.Dispose()` on transient `IOException` or `UnauthorizedAccessException` so Windows image-section teardown under `dotnet-coverage` completes cleanly.

## Consequences

- Positive: Full solution builds, sample builds, benchmark builds, package validation, and deterministic build checks pass with 0 warnings and 0 errors under the updated SDK and analyzer stack.
- Positive: All delegate pinning APIs share consistent generic constraints and type validation, and collectible callback unloading is verified for both primitive and collectible-assembly struct signatures.
- Trade-off: Files using `IntPtr` or `UIntPtr` include `#pragma warning disable SA1121` so built-in type alias enforcement remains active for other C# types.
