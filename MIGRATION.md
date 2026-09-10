# Migration guide

This guide covers upgrading to **4.0.0**, including the changes from 3.0.0 and
additional notes for consumers upgrading from 1.x or 2.x.

See the [changelog](CHANGELOG.md) for release history and the [README](README.md)
for complete examples.

## Upgrade checklist

1. Check your target framework against the table below.
2. Review every use of `GetDelegateForFunctionPointer<T>` for raw ABI compatibility.
3. Reject embedded NUL characters in names from configuration or external input.
4. Check Windows module names, absolute paths, and custom search flags.
5. Review callback roots, unregister/wait ordering, and exception handling.
6. Address trimming and dynamic-code warnings using appropriate API alternatives.
7. Test with the actual native SDK on each supported OS and architecture, including
   missing exports, dependency failures, callbacks, and shutdown.

## From 3.0.0 to 4.0.0

### Target frameworks

| Consumer target | 4.0.0 action |
| --- | --- |
| `net8.0`, `net9.0`, `net10.0` | Package assets remain available. |
| `net472`, `net48`, `net481` | Package assets remain available. |
| `net462`, `net47`, `net471` | Retarget to a supported framework before upgrading. |
| `netstandard2.0` | No asset in 3.0.0 or 4.0.0. See [upgrading from 1.x or 2.x](#upgrading-from-1x-or-2x). |

If you cannot retarget a .NET Framework 4.6.2, 4.7, or 4.7.1 application yet,
keep its package reference pinned to 3.0.0 while planning the runtime upgrade.
An installed newer in-place runtime does not change a project's target framework
for NuGet asset selection. Multi-targeted libraries may need conditional package
references until they can drop their older targets.

The SDK pin in this repository is a **source-build requirement**, not a
requirement that every consumer use that SDK.

### Library and export names

4.0.0 rejects embedded NUL characters with `ArgumentException`, rather than
allowing a native string boundary to truncate a name. This applies to the
constructor, `LoadLibrary`, both typed lookup methods, and both `TryGetExport`
methods.

For example, `"fixture.dll\0ignored"` and `"fixture_add\0ignored"` are now invalid.
Fix the source of malformed names; do not strip NUL characters silently and risk
loading a different module or resolving a different export.

Null, empty, and whitespace-only names remain invalid. Valid spaces and Unicode
characters are preserved. Missing exports still return `null` or `false` for
valid requests on undisposed owners.

### Windows loading and errors

With the default flags, a bare name such as `"kernel32.dll"` now uses only
`LOAD_LIBRARY_SEARCH_SYSTEM32`. Previously the default combination also passed
`LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR`, which requires a fully qualified path.

- For system libraries, use the bare name or an appropriate explicit search policy.
- For third-party libraries, supply a trusted, fully qualified path.
- Relative paths with directory components are still not converted to absolute
  paths. Resolve a trusted configured path explicitly before loading it.
- Custom flag combinations are passed through unchanged. The adjustment applies
  only when the flag value equals the default combination.
- Non-Windows platforms still ignore Windows loading flags.

Load failures remain `Win32Exception`, but diagnostic messages change:

| Platform | Diagnostics |
| --- | --- |
| Windows | Native error code, requested name, and system error description. |
| Modern Linux/macOS | Original loader exception in `InnerException`; `NativeErrorCode` is zero, not a POSIX code. |
| Legacy Mono fallback | Loader error text from `dlerror`; no equivalent managed loader exception. |

Avoid matching whole exception messages, including in tests. Use exception types
and, on Windows, native error codes where appropriate.

### Raw delegate wrappers

`UnmanagedLibrary.GetDelegateForFunctionPointer<T>(pointer, convention)` emits a
raw unmanaged call. It has never performed parameter marshaling. 4.0.0 rejects
unsupported signatures before invocation rather than allowing unsafe calls.

The public `where T : class` constraint is unchanged, but runtime validation now
requires a concrete, closed delegate type.

| Signature requirement | Recommended API |
| --- | --- |
| Strings, managed references, marshaled Booleans, `ref`/`out`, or `[MarshalAs]` | `GetUnmanagedFunction<TDelegate>` or `Marshal.GetDelegateForFunctionPointer<TDelegate>`. |
| Native last-error capture through `SetLastError` | A marshaled delegate with `[UnmanagedFunctionPointer(..., SetLastError = true)]`. |
| Exact native-compatible signature and fixed calling convention | On modern .NET, `TryGetExport` plus `delegate* unmanaged[...]`. |
| Exact native-compatible signature and runtime-selected calling convention | The raw wrapper, restricted to `Cdecl`, `StdCall`, or `Winapi`, on a dynamic runtime. |

#### Replace a marshaling-dependent raw wrapper

Before, this passed marshaling metadata to an API that could not honor it:

```csharp
// Fragment: pointer is a valid export address from a still-loaded module.
var readText = UnmanagedLibrary.GetDelegateForFunctionPointer<ReadText>(
    pointer, CallingConvention.Cdecl);
```

After, use the runtime marshaler:

```csharp
using System;
using System.Runtime.InteropServices;
using AdaskoTheBeAsT.Interop.Unmanaged;

// Pass an absolute native library path as the first command-line argument.
using var library = new UnmanagedLibrary(args[0]);
if (!library.TryGetExport("read_text", out var pointer))
{
    throw new EntryPointNotFoundException("read_text");
}

var readText = Marshal.GetDelegateForFunctionPointer<ReadText>(pointer);
Console.WriteLine(readText("example"));

// Example contract: int read_text(const char* text), with UTF-8 text.
// Use your SDK's actual signature, encoding, and ownership rules.
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate int ReadText([MarshalAs(UnmanagedType.LPUTF8Str)] string text);
```

You can also resolve the same declaration directly with
`library.GetUnmanagedFunction<ReadText>("read_text")`.

#### Keep a truly raw signature

The raw wrapper supports numeric primitives other than `bool` and `char`,
`IntPtr`/`UIntPtr`, pointers, enums, and nonempty sequential/explicit structs made
recursively of supported fields. `void` is supported as a return type.

It rejects managed references, auto-layout structs, `decimal`, nullable values,
managed by-reference parameters or returns, and marshaling attributes, including
attributes on nested fields.

Do not mechanically replace every `bool` with `int`: check the native header.
An 8-bit native Boolean can use `byte`, Windows `BOOL` uses `int`, and a UTF-16
code unit can use `ushort`. Native `long`, `char`, and `wchar_t` widths vary by
platform. Struct layout, packing, alignment, and ABI rules still need verification
against the native library.

Expected failures include `InvalidOperationException` for an invalid delegate
type, `ArgumentOutOfRangeException` for an unsupported convention, and
`NotSupportedException` for an unsupported signature. A zero pointer continues
to throw `ArgumentException`. These checks cannot detect a nonzero dangling
pointer or a mismatch with the actual exported function.

### Callback scopes and proxy behavior

The existing `GetFunctionPointerForDelegate(callback, out binder)` API remains.
Correctly rooted callback code does not need to adopt the new scope factories.
Do not rely on the binder's concrete runtime type; its contract is to keep the
callback alive.

For a synchronous callback, 4.0.0 adds:

- `PinConcreteDelegate(callback)` for a concrete, non-generic delegate, without
  emitting a proxy.
- `PinDelegate(callback)` for concrete or constructed generic delegates, with
  runtime proxy generation when needed.

Prefer a named delegate annotated with `[UnmanagedFunctionPointer]`. Generic
types such as `Func<int, int>` do not declare your SDK's calling convention.
See the [complete callback example](README.md#synchronous-callbacks).

Generic proxy fixes can change observable behavior:

- A multicast callback now invokes its full invocation list, not just its last
  target. If only one handler is intended, pass that handler explicitly.
- Proxies use the actual callback type, including when the argument is held as
  `Delegate`.
- Parameter and return-value marshaling metadata are preserved, in addition to
  the delegate-level interop metadata already copied in 3.0.0.
- Concurrent proxy creation is cached, and modern generated assemblies are
  collectible. Legacy generated assemblies remain non-collectible.

#### Retained callbacks still require explicit shutdown

`DelegatePin.Dispose()` is a keep-alive boundary, not an unregister operation,
memory unpin, pointer invalidation, or synchronization mechanism.

For callbacks stored by native code:

1. Store the callback root in an owner that outlives the registration.
2. Unregister and prevent new callback invocations.
3. Wait for callbacks already in flight to finish.
4. Release the root.
5. Dispose the module only after all calls, callbacks, and native objects are done.

Keep these operations serialized according to the native SDK's contract. Do not
wait for callback completion from inside a callback if that can deadlock.
Translate exceptions into the SDK's error convention inside the callback.
The [retained callback sample](samples/NativeSmoke/RetainedCallback.cs) demonstrates
this ordering with an unregister operation that also waits.

### Trimming and NativeAOT

On modern targets, these public APIs now declare dynamic-code and reflection
requirements:

- `GetDelegateForFunctionPointer<T>`
- `GetFunctionPointerForDelegate<T>`
- `PinDelegate<TDelegate>`

Consumers may see new `IL2026` or `IL3050` warnings, or build failures when treating
warnings as errors. A concrete delegate passed to an annotated helper does not
remove that helper's public warning contract.

Prefer changing APIs rather than broadly suppressing warnings:

| Existing use | Alternative |
| --- | --- |
| Raw emitted delegate thunk | `TryGetExport` plus an unmanaged function pointer. |
| Concrete callback passed to an annotated helper | `PinConcreteDelegate` or direct `Marshal.GetFunctionPointerForDelegate`, with a managed root. |
| Generic callback that captures state | A concrete callback delegate with an explicit calling convention; verify compiler marshaling support. |
| Non-capturing callback on modern .NET | A static `UnmanagedCallersOnly` method and an unmanaged function pointer. |

Generic proxy generation and raw thunk emission throw
`PlatformNotSupportedException` when runtime code generation is unavailable.
Concrete delegate marshaling still requires compiler-supported stubs for that
signature; choosing a no-emission API is not a guarantee of universal AOT support.

When retaining dynamic proxy generation in a trimmed JIT application, preserve
the delegate and marshaling metadata it inspects. Review the
[consumer sample](samples/README.md) for narrowly scoped examples and test the
published application, not only a normal debug build.

### Repository contributors

The checkout now uses .NET SDK `10.0.401` and Microsoft.Testing.Platform through
`global.json`, with xUnit v3. Use explicit project/solution options:

```powershell
dotnet build .\AdaskoTheBeAsT.Interop.Unmanaged.slnx -c Release
dotnet test --solution .\AdaskoTheBeAsT.Interop.Unmanaged.slnx -c Release --no-build
```

These commands require the [native test toolchain](test/native/README.md).
Older validation notes and scripts refer to nine targets, `3.0.1-*` local package
versions, and the previous test runner. Those records are historical evidence,
not proof that the current 4.0.0 matrix passes. Workflow and script alignment is
still needed; see the [README validation caveat](README.md#supported-frameworks-and-platforms).

## Upgrading from 1.x or 2.x

Apply the 3.0.0 changes as well as the 4.0.0 guidance above:

- **Framework assets:** 1.0.0 and 2.0.0 targeted `netstandard2.0`, `net8.0`,
  `net9.0`, and `net10.0`. 3.0.0 removed `netstandard2.0` and added explicit
  .NET Framework targets. A library that must still target only .NET Standard
  cannot simply update that reference; retarget, multi-target with compatible
  references, or retain its older package version.
- **Platform behavior:** 1.x and 2.x used the Windows loader. 3.0.0 added
  Linux/macOS loader paths. Loading Windows binaries on Unix is not supported;
  supply platform-appropriate native binaries and declarations.
- **Raw lookup:** 3.0.0 added instance and static `TryGetExport`. Prefer it over
  reaching into internal loader methods or exposing handles through reflection.
- **Callback metadata:** 3.0.0 began propagating `[UnmanagedFunctionPointer]`
  settings to generic proxies. Recheck declarations that accidentally depended
  on the previous default convention.
- **Argument validation:** 2.0.0 added explicit checks for null callbacks,
  invalid export names/handles, zero raw pointers, and non-delegate type
  arguments. Fix invalid call sites rather than relying on older failure behavior.

For every version, a looked-up delegate or pointer does **not** keep the module
loaded. Callback roots and module ownership remain separate responsibilities.
