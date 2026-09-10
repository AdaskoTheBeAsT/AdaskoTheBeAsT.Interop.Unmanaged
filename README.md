# AdaskoTheBeAsT.Interop.Unmanaged

> 🚀 Native libraries. Typed delegates. Explicit lifetimes. Less boilerplate.

Load the right native library at runtime, call its exports, and keep callbacks
alive without scattering loader plumbing throughout your .NET application.
Windows, Linux, and macOS, with the platform details spelled out below.

[![NuGet](https://img.shields.io/nuget/v/AdaskoTheBeAsT.Interop.Unmanaged.svg)](https://www.nuget.org/packages/AdaskoTheBeAsT.Interop.Unmanaged/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/AdaskoTheBeAsT.Interop.Unmanaged.svg)](https://www.nuget.org/packages/AdaskoTheBeAsT.Interop.Unmanaged/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
![Target frameworks](https://img.shields.io/badge/.NET-8%20%7C%209%20%7C%2010-512BD4?logo=dotnet)
![Framework targets](https://img.shields.io/badge/.NET_Framework-4.7.2%20%7C%204.8%20%7C%204.8.1-512BD4)
![Platforms](https://img.shields.io/badge/platforms-Windows%20%7C%20Linux%20%7C%20macOS-blue)

### 🔬 Code quality

[![Quality Gate Status](https://sonarcloud.io/api/project_badges/measure?project=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop.Unmanaged&metric=alert_status)](https://sonarcloud.io/summary/new_code?id=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop.Unmanaged)
[![Coverage](https://sonarcloud.io/api/project_badges/measure?project=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop.Unmanaged&metric=coverage)](https://sonarcloud.io/component_measures?id=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop.Unmanaged&metric=coverage)

> 🎉 **Welcome to 4.0.0!** Public callback scopes, stricter input and raw-signature
> validation, and improved loader diagnostics. Check the [changelog](CHANGELOG.md)
> for what's new and the [migration guide](MIGRATION.md) before upgrading.

## 🧭 Find your way

- [✨ Why you'll like it](#when-to-use-it)
- [📦 Installation](#installation)
- [🚀 Quick start](#quick-start)
- [🌍 Supported frameworks and platforms](#supported-frameworks-and-platforms)
- [🪝 Callbacks](#callbacks)
- [⚡ Raw function pointers](#raw-function-pointers)
- [🧰 API at a glance](#api-reference)
- [🛡️ Safety and lifetime rules](#safety-and-lifetime-rules)
- [🔎 Loading and error behavior](#loading-and-error-behavior)
- [✂️ Trimming and NativeAOT](#trimming-and-nativeaot)
- [🧪 Build and test](#build-and-test)

<a name="when-to-use-it"></a>

## ✨ Why you'll like it

You've got a native library. Its path comes from configuration, one customer has
an older SDK, and native code wants to call you back. Sound familiar? This library
keeps that plumbing in one place.

- 🎯 **Real delegates, not just addresses.** Resolve exports using your delegate
  declarations and call them from ordinary C#.
- 🔍 **Optional means optional.** Check whether an SDK exposes a symbol without
  treating a missing export as an error.
- 🔒 **Module ownership you can follow.** Use a `SafeLibraryHandle` or
  `UnmanagedLibrary` scope instead of scattering `FreeLibrary` calls around.
- 🪝 **Callbacks with a keep-alive plan.** Root managed delegates while native
  code holds their function pointers.
- 🪟 **Windows search flags when you need them.** Choose an explicit
  `LoadLibraryEx` search policy for your native dependencies.
- 🌍 **One API, platform-specific loaders.** Windows `LoadLibraryEx`, modern .NET
  `NativeLibrary` on Linux/macOS, and a legacy Mono fallback.

> 💡 **Use the simplest tool that fits.** Fixed library names and signatures?
> `DllImport` or `LibraryImport` may be exactly what you need. Just raw loading on
> modern .NET? `NativeLibrary` may be enough. This library helps with ownership
> and boilerplate; it cannot verify your native ABI or make untrusted code safe.

<a name="installation"></a>

## 📦 Installation

One command gets you the latest published NuGet package:

```shell
dotnet add package AdaskoTheBeAsT.Interop.Unmanaged
```

XML API docs come with the package, so IntelliSense can help along the way.
Symbol packages (`.snupkg`) and Source Link let you step into the library when
you want to see what's happening under the hood. 🔍

<a name="quick-start"></a>

## 🚀 Quick start

### 1. Load a library and call an export

Let's start small: ask Windows for the current process ID. This is a complete
**Windows-only** example. Declare the exact native signature and calling
convention, check the lookup result, and keep the library undisposed through the
call.

```csharp
using System;
using System.Runtime.InteropServices;
using AdaskoTheBeAsT.Interop.Unmanaged;

using var library = new UnmanagedLibrary("kernel32.dll");
var getCurrentProcessId =
    library.GetUnmanagedFunction<GetCurrentProcessId>("GetCurrentProcessId")
    ?? throw new EntryPointNotFoundException("GetCurrentProcessId");

Console.WriteLine($"Current PID: {getCurrentProcessId()}");

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
delegate uint GetCurrentProcessId();
```

The same lookup API works with `.so` and `.dylib` files on Linux and macOS.
Library names, export names, signatures, and calling conventions must match the
native library on each platform. For a complete cross-platform example using the
repository's native fixture, see [consumer smoke tests](samples/README.md).

### 2. Probe for an optional export

Different SDK versions, different exports? Check for the feature before using it.

`GetUnmanagedFunction<TDelegate>` returns `null` for a missing export.
`TryGetExport` returns `false` and `IntPtr.Zero`. These results apply only to
valid requests on an undisposed owner; invalid names and disposed handles still
throw.

```csharp
using System;
using System.Runtime.InteropServices;
using AdaskoTheBeAsT.Interop.Unmanaged;

// Pass an absolute path to your native SDK as the first command-line argument.
using var library = new UnmanagedLibrary(args[0]);
var optionalExport =
    library.GetUnmanagedFunction<OptionalExport>("OptionalExport");

Console.WriteLine(optionalExport is null
    ? "This SDK version does not expose OptionalExport."
    : $"OptionalExport returned {optionalExport()}.");

// Replace this declaration with the signature from your SDK's native header.
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate int OptionalExport();
```

### 3. Manage a handle directly

Prefer to own the handle yourself? The static API returns a `SafeLibraryHandle`
instead of an `UnmanagedLibrary` instance. Same lookup pattern, a different owner.
This example is Windows-only.

```csharp
using System;
using System.Runtime.InteropServices;
using AdaskoTheBeAsT.Interop.Unmanaged;

using var handle = UnmanagedLibrary.LoadLibrary("kernel32.dll");
var getTickCount =
    UnmanagedLibrary.GetUnmanagedFunction<GetTickCount>(handle, "GetTickCount")
    ?? throw new EntryPointNotFoundException("GetTickCount");

Console.WriteLine($"Tick count: {getTickCount()}");

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
delegate uint GetTickCount();
```

<a name="supported-frameworks-and-platforms"></a>

## 🌍 Supported frameworks and platforms

The current project builds **six target frameworks**:

| Runtime family | Package targets | Loader behavior |
| --- | --- | --- |
| Modern .NET | `net10.0`, `net9.0`, `net8.0` | Windows: `LoadLibraryEx`. Linux/macOS: `NativeLibrary`. |
| .NET Framework | `net481`, `net48`, `net472` | Windows: `LoadLibraryEx`. Legacy Mono fallback: `dlopen`/`dlsym`. |

4.0.0 removes the `net462`, `net47`, and `net471` targets present in 3.0.0.
There is no `netstandard2.0` asset; it was removed in 3.0.0.

Windows honors `LoadLibraryFlags`, with the default bare-name adjustment described
[below](#loading-and-error-behavior). Linux and macOS ignore these Windows flags.
Modern non-Windows loading follows the runtime's behavior; only the legacy
fallback explicitly requests `RTLD_NOW`.

**Targeting is not execution evidence.** The [validation notes](docs/validation.md)
record an earlier Windows validation pass against nine targets and local
`3.0.1-*` packages, not a validation of the six-target 4.0.0 release.
The [native validation workflow](.github/workflows/native-validation.yml) contains
cross-platform, x86, ARM64, trimmed, and NativeAOT jobs, but still includes removed
targets and older test commands. It needs alignment before it can validate 4.0.0.

Mono execution remains unverified. Modern musl/Alpine execution is also
unverified; the legacy Linux fallback requires `libdl.so.2` and does not support
musl. .NET Framework tests on a current Windows machine run on its installed
in-place runtime, not on separate historical runtime installations.

<a name="callbacks"></a>

## 🪝 Callbacks

Native code wants to call your C#? Give it a function pointer, and give the
managed callback a lifetime that covers every native call.

### Synchronous callbacks

**New in 4.0.0:** `PinConcreteDelegate` creates a keep-alive scope without emitting
a proxy. Prefer a concrete, non-generic delegate with an explicit calling
convention.

This complete example uses the repository's [native fixture](test/native/README.md).
Pass its absolute path as `args[0]`: `fixture.dll`, `libfixture.so`, or
`libfixture.dylib`. The expected output is `42`.

```csharp
using System;
using System.Runtime.InteropServices;
using AdaskoTheBeAsT.Interop.Unmanaged;

using var library = new UnmanagedLibrary(args[0]);
var invoke = library.GetUnmanagedFunction<InvokeCallback>("fixture_invoke")
    ?? throw new EntryPointNotFoundException("fixture_invoke");
using var callback = UnmanagedLibrary.PinConcreteDelegate<Callback>(value => value + 1);

Console.WriteLine(invoke(callback.Ptr, 41));

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate int Callback(int value);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate int InvokeCallback(IntPtr callback, int value);
```

`PinDelegate` also accepts constructed generic delegate types. That path emits a
concrete proxy on dynamic runtimes and preserves calling convention, marshaling
metadata, and the full multicast invocation list. Prefer a named delegate when
the native API requires explicit interop attributes.

> 📌 **A root, not a memory pin.** Despite its name, `DelegatePin` does not pin
> memory. Its `Dispose` method is only a keep-alive boundary. It does not
> unregister a callback, invalidate the pointer, or wait for native work.

### Retained callbacks

If native code stores a callback after the registration call returns, a local
`using` scope or a single `GC.KeepAlive` after registration is not enough.

1. Keep the delegate, or the `binder` returned by `GetFunctionPointerForDelegate`,
   in a field for the full registration lifetime.
2. Unregister using the native API.
3. Wait for all in-flight callbacks to finish.
4. Release the managed root, then unload the module only when nothing else uses it.

See the compilable [retained callback example](samples/NativeSmoke/RetainedCallback.cs)
and [isolated lifecycle checks](samples/NativeSmoke/LifetimeChecks.cs). The
fixture's unregister operation waits synchronously; another SDK may require a
separate wait.

Translate exceptions inside callbacks into the native API's error convention.
Do not let managed exceptions escape across the native boundary.

<a name="raw-function-pointers"></a>

## ⚡ Raw function pointers

Prefer `delegate* unmanaged`? On modern .NET, `TryGetExport` gives you the address
without creating a managed delegate. Enable
`<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` in your consumer project.
This complete example is Windows-only.

```csharp
using System;
using AdaskoTheBeAsT.Interop.Unmanaged;

using var library = new UnmanagedLibrary("kernel32.dll");
if (!library.TryGetExport("GetCurrentProcessId", out var address))
{
    throw new EntryPointNotFoundException("GetCurrentProcessId");
}

unsafe
{
    var getCurrentProcessId = (delegate* unmanaged[Stdcall]<uint>)address;
    Console.WriteLine($"Current PID: {getCurrentProcessId()}");
}
```

The static equivalent is `UnmanagedLibrary.TryGetExport(handle, name, out address)`.
Raw calls do not marshal parameters. You must match the native signature, calling
convention, architecture, and module lifetime.

### Wrapping a raw pointer in a delegate

Prefer `Marshal.GetDelegateForFunctionPointer<TDelegate>(pointer)` when the
signature needs marshaling. Put `[UnmanagedFunctionPointer]` and any required
`[MarshalAs]` attributes on the delegate declaration.

`UnmanagedLibrary.GetDelegateForFunctionPointer<T>(pointer, convention)` instead
emits a raw `calli` thunk. It is intended for native-compatible signatures when
the calling convention must be selected at runtime, not as a replacement for the
marshaler.

- Supports `Cdecl`, `StdCall`, and `Winapi`.
- Accepts fixed-width numeric primitives other than `bool`/`char`, `IntPtr`,
  `UIntPtr`, pointers, enums, and nonempty sequential/explicit structs composed
  recursively of supported fields.
- Rejects managed references, auto-layout structs, `bool`, `char`, `decimal`,
  nullable values, managed `ref`/`out` parameters or returns, `[MarshalAs]`, and
  `SetLastError`.
- Requires a concrete, closed delegate type and runtime code generation.

Represent an 8-bit native Boolean as `byte`, a Windows `BOOL` as `int`, and a
16-bit UTF-16 code unit as `ushort` for raw calls. Native `char`, `wchar_t`,
`long`, and Boolean widths depend on the API and platform. Validation cannot
prove that your declaration matches the native ABI.

<a name="api-reference"></a>

## 🧰 API at a glance

Here's the toolbox. All types live in the
`AdaskoTheBeAsT.Interop.Unmanaged` namespace.

| API | Purpose |
| --- | --- |
| `new UnmanagedLibrary(fileName, flags)` | Load a module owned by a disposable instance. Flags are optional. |
| `library.GetUnmanagedFunction<TDelegate>(name)` | Marshal an export as a delegate, or return `null` if missing. |
| `library.TryGetExport(name, out address)` | Get a raw export address without creating a delegate. |
| `UnmanagedLibrary.LoadLibrary(fileName, flags)` | Return an owning `SafeLibraryHandle`. Flags are optional. |
| `UnmanagedLibrary.FreeLibrary(handle)` | Dispose a handle; `null` and already closed handles are accepted. |
| `UnmanagedLibrary.GetUnmanagedFunction<TDelegate>(handle, name)` | Handle-based typed lookup. |
| `UnmanagedLibrary.TryGetExport(handle, name, out address)` | Handle-based raw lookup. |
| `UnmanagedLibrary.GetFunctionPointerForDelegate(callback, out binder)` | Create a callback pointer and a managed keep-alive root. |
| `UnmanagedLibrary.PinDelegate(callback)` | Create a `DelegatePin` scope, including generic proxy support. New in 4.0.0. |
| `UnmanagedLibrary.PinConcreteDelegate(callback)` | Create a scope for a non-generic delegate without emission. New in 4.0.0. |
| `UnmanagedLibrary.GetDelegateForFunctionPointer<T>(pointer, convention)` | Emit a raw unmanaged call thunk; no marshaling. |

`SafeLibraryHandle` owns the native module. `LoadLibraryFlags` mirrors Windows
`LoadLibraryEx` flags. `DelegatePin.Ptr` exposes a scoped callback pointer.

<a name="safety-and-lifetime-rules"></a>

## 🛡️ Safety and lifetime rules

The library handles the loader plumbing. These responsibilities stay with your
application:

- 🔒 **Keep the module owner undisposed.** Returned delegates and pointers do not
  extend its lifetime. The same rule applies to native objects created by exports.
- ⏳ **Prevent concurrent unloading.** `SafeHandle` protects ownership and lookup-time
  access, not subsequent calls through a returned pointer or delegate.
- 🪝 **Root callbacks separately.** Keeping the library alive does not keep your
  managed callbacks alive, or vice versa.
- 🎯 **Match the native ABI exactly.** Incorrect signatures, layouts, or calling
  conventions can corrupt the process.
- 🛑 **Load only trusted binaries.** Search flags reduce search-path risks; they do
  not sandbox native code or make arbitrary binaries safe.

<a name="loading-and-error-behavior"></a>

## 🔎 Loading and error behavior

Missing feature, invalid request, or failed load? Here's what to expect.

| Situation | Result |
| --- | --- |
| Null, empty, whitespace-only, or embedded-NUL library/export name | `ArgumentException`. |
| Null handle passed to static lookup | `ArgumentNullException`. |
| Missing export on an undisposed owner with a valid name | `null` for typed lookup; `false` and `IntPtr.Zero` for raw lookup. |
| Lookup on a disposed owner with a valid name | `ObjectDisposedException`. |
| Native library load failure | `Win32Exception`; see platform-specific diagnostics below. |

Export names are case-sensitive. Spaces and Unicode in valid paths are preserved;
names are not trimmed or normalized.

On Windows, the default flag combination is
`LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32`. For a **bare
module name**, the loader removes `LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR` and searches
System32. Fully qualified paths retain both flags. Other flag combinations are
passed through unchanged.

Use a fully qualified path for third-party libraries. Relative paths containing
directory components are not automatically made absolute and are not suitable
for the default Windows flags. For example:

```csharp
using AdaskoTheBeAsT.Interop.Unmanaged;

using var library = new UnmanagedLibrary(
    @"C:\Native\MyLibrary.dll",
    LoadLibraryFlags.LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR |
    LoadLibraryFlags.LOAD_LIBRARY_SEARCH_SYSTEM32);
```

Windows load failures preserve `Win32Exception.NativeErrorCode` and include the
requested name and system error description. Modern non-Windows failures preserve
the underlying loader exception as `InnerException`; `NativeErrorCode` is zero,
not a POSIX error number.

Linux/macOS ignore `LoadLibraryFlags`, including resource-only and
signature-related flags. Loading there is a normal executable load, **not**
portable safe inspection of a binary.

<a name="trimming-and-nativeaot"></a>

## ✂️ Trimming and NativeAOT

Shipping a trimmed or NativeAOT app? Choose the no-emission paths deliberately.
The package as a whole is **not advertised as AOT-compatible**.

| Capability | Trimming / NativeAOT considerations |
| --- | --- |
| Load/free and raw export lookup | No runtime emission. Covered by the consumer smoke sample. |
| Raw unmanaged pointers and `UnmanagedCallersOnly` callbacks | Preferred no-emission path. Exact signatures and lifetimes remain your responsibility. |
| Concrete delegate marshaling and `PinConcreteDelegate` | Require compiler-supported marshaling stubs for the concrete signature. The sample covers integer callbacks, not all signatures. |
| Generic callback proxies, `GetFunctionPointerForDelegate`, and `PinDelegate` | Public helpers carry dynamic-code and reflection annotations on modern targets. Generic proxies need preserved metadata when trimmed and cannot be generated under NativeAOT. |
| Raw `calli` delegate wrapper | Requires dynamic code and reflected signature metadata; throws under NativeAOT. |

The [consumer sample](samples/README.md) demonstrates trimmed and NativeAOT paths
and expected dynamic-code rejection. See the [migration guide](MIGRATION.md#trimming-and-nativeaot)
for alternatives to warning suppression.

Modern callback proxies use weak-key caching and collectible generated assemblies.
The [collectible-context test](test/unit/AdaskoTheBeAsT.Interop.Unmanaged.Test/CollectibleCallbackTests.cs)
checks plugin unload behavior after callbacks are released. Legacy generated
assemblies are not collectible; weak keys alone cannot unload them.

<a name="build-and-test"></a>

## 🧪 Build and test

Want to try the latest changes or contribute a fix? Here's the local setup.

Building from source requires the SDK pinned in [global.json](global.json)
(currently .NET SDK `10.0.401`). Native tests also require CMake 3.20+ and a C
compiler. On Windows, install the Visual Studio C++ build tools and ensure
`cmake` is on `PATH`, or pass `-p:CMakeCommand="<path-to-cmake.exe>"` when building.
Install the .NET 8 and 9 runtimes to run those test targets.

From the repository root on Windows:

```powershell
dotnet build .\AdaskoTheBeAsT.Interop.Unmanaged.slnx -c Release
dotnet test --solution .\AdaskoTheBeAsT.Interop.Unmanaged.slnx -c Release --no-build
```

For one modern target, including on Linux/macOS:

```shell
dotnet test --project test/unit/AdaskoTheBeAsT.Interop.Unmanaged.Test/AdaskoTheBeAsT.Interop.Unmanaged.Test.csproj -c Release -f net10.0
```

The current checkout uses Microsoft.Testing.Platform through `global.json`.
Use `--solution` or `--project`, not the older positional `dotnet test` syntax.
Native fixtures are built for the test architecture; a missing fixture is a
failure, not a silent skip. Windows-specific smoke tests report skips on modern
non-Windows runtimes.

More information:

- [Native fixture and toolchain](test/native/README.md)
- [Trimmed, NativeAOT, and retained-callback samples](samples/README.md)
- [Callback benchmarks](benchmarks/README.md)
- [Historical validation evidence and remaining gaps](docs/validation.md)
- [Changelog](CHANGELOG.md) and [migration guide](MIGRATION.md)

### 🤝 Contributing

Found a bug, a confusing example, or a native SDK edge case? Contributions are
welcome, including documentation improvements.

1. Open an issue describing the bug or proposed change. For interop failures,
   include the package version, target framework, OS, architecture, and a minimal
   reproduction.
2. Add tests for behavior changes and run the relevant build and test targets.
3. Open a pull request and tell us which platform checks you could not run.

## 📄 License

[MIT](LICENSE).

---

Made with ❤️ by [AdaskoTheBeAsT](https://github.com/AdaskoTheBeAsT) for .NET
developers who occasionally need to speak native.
