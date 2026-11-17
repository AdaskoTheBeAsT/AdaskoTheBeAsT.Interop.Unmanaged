# AdaskoTheBeAsT.Interop.Unmanaged

> 🚀 Native libraries. Typed delegates. Explicit lifetimes. Less boilerplate.

Load the right native library at runtime, call its exports, and keep callbacks
alive without scattering loader plumbing throughout your .NET application.
Windows, Linux, and macOS, with the platform details spelled out below.

[![NuGet](https://img.shields.io/nuget/v/AdaskoTheBeAsT.Interop.Unmanaged.svg)](https://www.nuget.org/packages/AdaskoTheBeAsT.Interop.Unmanaged/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/AdaskoTheBeAsT.Interop.Unmanaged.svg)](https://www.nuget.org/packages/AdaskoTheBeAsT.Interop.Unmanaged/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
![Target frameworks](https://img.shields.io/badge/.NET-10%20%7C%2011-512BD4?logo=dotnet)
![Framework targets](https://img.shields.io/badge/.NET_Framework-4.7.2%20%7C%204.8%20%7C%204.8.1-512BD4)
![Platforms](https://img.shields.io/badge/platforms-Windows%20%7C%20Linux%20%7C%20macOS-blue)

### 🔬 Code quality

[![Quality Gate Status](https://sonarcloud.io/api/project_badges/measure?project=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop.Unmanaged&metric=alert_status)](https://sonarcloud.io/summary/new_code?id=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop.Unmanaged)
[![Coverage](https://sonarcloud.io/api/project_badges/measure?project=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop.Unmanaged&metric=coverage)](https://sonarcloud.io/component_measures?id=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop.Unmanaged&metric=coverage)

> 🎉 **Welcome to 5.0.0!** Native `.NET 11` (`net11.0`) package support alongside
> `.NET 10` and `.NET Framework 4.7.2–4.8.1`, plus `GetRequiredExport`, Windows
> module `BaseAddress` and path/address provenance, verified PE32/PE32+ patching
> (`PeImageEditor`), runtime IAT redirection and verified RVA patching
> (`LoadedModuleMemory`), per-worker native DLL graph and companion-file isolation
> (`IsolatedLibrarySet`, `WorkerSlotAllocator`), fixed-buffer native byte strings
> (`NativeByteString`), pinned library caches (`PinnedLibraryCache`), unmanaged
> scratch buffers (`UnmanagedScratchBuffer`), opt-in `win-x86` `LargeAddressAware`
> apphost stamping, and FPU / thread error-mode execution guards
> (`NativeExecutionGuard`). Out-of-support `net8.0` and `net9.0` targets were
> removed. Check the [changelog](CHANGELOG.md) and
> [migration guide](MIGRATION.md) before upgrading.

## 🧭 Find your way

- [✨ Why you'll like it](#when-to-use-it)
- [📦 Installation](#installation)
- [🚀 Quick start](#quick-start)
- [🌍 Supported frameworks and platforms](#supported-frameworks-and-platforms)
- [🪝 Callbacks](#callbacks)
- [⚡ Raw function pointers](#raw-function-pointers)
- [🧩 Per-worker DLL isolation, companion staging, and PE patching (Windows)](#isolation-and-pe-patching)
- [🔬 Runtime IAT redirection and verified module memory (Windows)](#loaded-module-memory)
- [🔢 Worker slots, pinned caches, scratch buffers, and fixed byte strings](#slots-caches-and-byte-strings)
- [🛡️ Floating-point and error-dialog guards](#execution-guards)
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
- 🪟 **Windows search flags and per-worker DLL isolation.** Choose an explicit
  `LoadLibraryEx` search policy, or stage isolated worker copies of stateful
  multi-DLL native graphs with `IsolatedLibrarySet` and `PeImageEditor`.
- 🧮 **FPU and error-dialog hygiene.** Mask native x87/SSE trap mutations and
  suppress thread-scoped Windows error popups with `NativeExecutionGuard`.
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

The current project builds **five target frameworks**:

| Runtime family | Package targets | Loader behavior |
| --- | --- | --- |
| Modern .NET | `net11.0`, `net10.0` | Windows: `LoadLibraryEx`. Linux/macOS: `NativeLibrary`. |
| .NET Framework | `net481`, `net48`, `net472` | Windows: `LoadLibraryEx`. Legacy Mono fallback: `dlopen`/`dlsym`. |

5.0.0 adds `net11.0` and removes the out-of-support `net8.0` and `net9.0` targets.
4.0.0 removed `net462`, `net47`, and `net471`. There is no `netstandard2.0`
asset; it was removed in 3.0.0.

Windows honors `LoadLibraryFlags`, with the default bare-name adjustment described
[below](#loading-and-error-behavior). Linux and macOS ignore these Windows flags.
Modern non-Windows loading follows the runtime's behavior; only the legacy
fallback explicitly requests `RTLD_NOW`.

**Targeting is not execution evidence.** The [validation notes](docs/validation.md)
record local Windows validation passes and remaining cross-platform CI gaps.

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

Prefer `delegate* unmanaged`? On modern .NET, `GetRequiredExport` (new in 5.0.0)
and `TryGetExport` give you the raw export address without creating a managed
delegate. Enable `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` in your consumer
project. This complete example is Windows-only.

```csharp
using System;
using AdaskoTheBeAsT.Interop.Unmanaged;

using var library = new UnmanagedLibrary("kernel32.dll");
var address = library.GetRequiredExport("GetCurrentProcessId");

unsafe
{
    var getCurrentProcessId = (delegate* unmanaged[Stdcall]<uint>)address;
    Console.WriteLine($"Current PID: {getCurrentProcessId()}");
}
```

Use `TryGetExport(name, out address)` (or `UnmanagedLibrary.TryGetExport(handle, name, out address)`)
when an export is optional, and `GetRequiredExport(name)` (or
`UnmanagedLibrary.GetRequiredExport(handle, name)`) when a missing symbol should
throw `EntryPointNotFoundException` naming both the export and module.

On Windows, `library.BaseAddress` and `UnmanagedLibrary.GetBaseAddress(handle)`
return the mapped module base address with low loader data-file tag bits (`~3`)
masked off. `library.GetLoadedFilePath()` (and `UnmanagedLibrary.GetLoadedFilePath(handle)`),
`library.ContainsAddress(address)`, `UnmanagedLibrary.TryGetLoadedModuleFromAddress(...)`,
and `UnmanagedLibrary.GetRequiredSystemExport(systemModuleName, exportName)` provide
loaded-module provenance and `System32`-scoped symbol lookup without extra Win32
boilerplate. Raw export and base addresses do not extend module lifetime: keep
the owning `UnmanagedLibrary` or `SafeLibraryHandle` undisposed and prevent
concurrent disposal for the entire duration of native execution.

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

<a name="isolation-and-pe-patching"></a>

## 🧩 Per-worker DLL isolation, companion staging, and PE patching (Windows)

Many legacy C, C++, Delphi, PowerBASIC, and Fortran DLLs store mutable state in
`.data`/`.bss` sections, read adjacent `.ini`/`.cfg`/`.dat` files inside
`DllMain(DLL_PROCESS_ATTACH)`, and depend on companion runtime DLLs (for example
`DFORRT.dll`). Calling `LoadLibraryEx` repeatedly on the same path returns the
same `HMODULE`, and copying only the top-level DLL still shares companion
modules because the Windows loader unifies implicit imports by module name.

`IsolatedLibrarySet` (Windows-only, new in 5.0.0) preflights PE edits in memory,
stages patched DLLs and companion files/directories *before* any `LoadLibraryEx`
call runs `DllMain`, loads modules in topological dependency order without
changing the process working directory or global DLL search path, and unloads
them in reverse topological order on `Dispose()`.

```csharp
using System.Text;
using AdaskoTheBeAsT.Interop.Unmanaged;

var options = new IsolatedLibrarySetOptions
{
    SourceDirectory = @"C:\Native\CalcEngine",
    IsolationRootDirectory = @"C:\Temp\IsolatedWorkers",
    WorkerIndex = 0,
    NamespaceId = "A",
    Generation = 1,
    MaxWorkerFilePathLength = 240,
    RequireAsciiWorkerPaths = true,
    Modules =
    [
        new IsolatedModuleSpecification
        {
            LogicalName = "runtime",
            SourceFileName = "DFORRT.dll",
            RenameForWorker = true,         // -> DFA100.dll (same 10-byte ASCII length, process-unique)
            RewriteExportModuleName = true, // updates IMAGE_EXPORT_DIRECTORY.Name in the same pass
        },
        new IsolatedModuleSpecification
        {
            LogicalName = "engine",
            SourceFileName = "Engine.dll",
            Dependencies = ["runtime"],
            RewriteDelayImports = true,
            ImportModuleRemovals =
            [
                // Removes dead desktop import descriptors absent on minimal Windows Server / Nano Server images
                new PeImportModuleRemoval
                {
                    ModuleName = "OLEDLG.DLL",
                    WitnessRemainingModuleName = "KERNEL32.dll",
                },
            ],
        },
    ],
    CompanionFiles =
    [
        new IsolatedCompanionFileSpecification
        {
            SourceRelativePath = "engine.ini",
            ClearReadOnlyAttribute = true,
            TransformBytes = bytes =>
                Encoding.ASCII.GetBytes(
                    Encoding.ASCII.GetString(bytes).Replace("{WORKER}", "00")),
        },
    ],
    CompanionDirectories =
    [
        new IsolatedCompanionDirectorySpecification
        {
            SourceRelativeDirectory = "Tables",
            TargetRelativeDirectory = "Tables",
            Recursive = true,
            ClearReadOnlyAttribute = true,
        },
    ],
};

using var worker0 = IsolatedLibrarySet.Create(options);
var calculate = worker0["engine"].GetRequiredExport("CALCULATE");
```

Key isolation rules:

- **Process-unique companion names.** Use `NamespaceId` and `Generation` when
  multiple pools or overlapping recycle generations coexist in one process.
  `IsolatedLibrarySet` reserves active worker directories and renamed companion
  module names process-wide and rolls back cleanly if preflight or staging fails.
- **Explicit binary patch boundaries.** Renaming ordinary import descriptors
  does not automatically rewrite delay-load imports unless
  `RewriteDelayImports = true`, and never guesses or scan-replaces `LoadLibrary`
  string literals. Supply `ImportModuleRemovals`, `ImportSymbolRewrites`, or
  `VerifiedBytePatches` (`PeVerifiedBytePatch`) explicitly when a binary needs them.
- **Pre-`DllMain` companion safety & path preflight.** `CompanionFiles` and
  `CompanionDirectories` reject rooted paths, `..` traversal, symlinks/reparse
  points, and collisions with staged DLL names, and check
  `MaxWorkerFilePathLength` / `RequireAsciiWorkerPaths` before writing files.
- **Owning-thread affinity and process-lifetime pinning.** Set
  `EnforceOwningThreadAffinity = true` when native TLS or `DllMain` teardown must
  run on the loading thread. Set `PinModulesForProcessLifetime = true` for
  unload-unsafe runtimes that crash if `FreeLibrary` runs before process exit;
  `Dispose()` then blocks further wrapper access while retaining module handles,
  in-process reservations, and the `.unmanaged-isolation.lease` lock file.
- **Crash-leftover cleanup.** Call
  `IsolatedLibrarySet.SweepStaleWorkerDirectories(isolationRootDirectory)` at
  startup to remove stale directories carrying `.unmanaged-isolation.marker`
  whose exclusive `.unmanaged-isolation.lease` is no longer held.

### Standalone PE editing (`PeImageEditor`) and `win-x86` `LargeAddressAware`

Use `PeImageEditor.FromFile(...)` or `PeImageEditor.FromBytes(...)` directly for
offline PE32 (`x86`) and PE32+ (`x64`/`ARM64`) inspection and idempotent binary
patching:

```csharp
using AdaskoTheBeAsT.Interop.Unmanaged;

PeImageEditor.FromFile(@"C:\Native\CalcEngine\Engine.dll")
    .RewriteExportModuleName("Engine.dll", "EngW00.dll")
    .RemoveImportModule(new PeImportModuleRemoval
    {
        ModuleName = "WINMM.DLL",
        WitnessRemainingModuleName = "KERNEL32.dll",
    })
    .ReplaceVerifiedAsciiStringAtRva(0x14200, "DEFAULT.CFG", "WORK00.CFG")
    .ReplaceVerifiedUtf16StringAtRva(0x14310, "DefaultPool", "WorkerPool0")
    .ReplaceVerifiedShortStringAtRva(
        0x14400,
        expectedOriginal: "APPCONFIG.IN",
        replacement: "WORKER00.INI",
        allowAnyPrintableAsciiOfSameLength: true)
    .ClearAuthenticodeAndRecalculateChecksum()
    .SaveIfChanged(@"C:\Temp\IsolatedWorkers\w00\EngW00.dll");
```

- Pass `PeImageValidationOptions.ForExecutable(PeMachine.I386, expectedLength)`
  when validating or editing unmanaged `.exe` binaries (for example calling
  `SetLargeAddressAware(true)` or `RemoveImportModule(...)` on a helper `.exe`).
- `SaveIfChanged(destinationPath)` refuses to overwrite the source file and
  throws `InvalidOperationException` if a signed image (`Authenticode`) was
  modified without calling `ClearAuthenticodeAndRecalculateChecksum()`.
- To stamp `IMAGE_FILE_LARGE_ADDRESS_AWARE` (`0x0020`, raising 32-bit user VA
  space from 2 GiB to 4 GiB on 64-bit Windows) on a `win-x86` `.NET` executable
  apphost during `dotnet build` and `dotnet publish` without external
  `editbin.exe`, opt in via your `.csproj`:

```xml
<PropertyGroup>
  <EnableLargeAddressAwareAppHost>true</EnableLargeAddressAwareAppHost>
</PropertyGroup>
```

<a name="loaded-module-memory"></a>

## 🔬 Runtime IAT redirection and verified module memory (Windows)

When an import target is only known inside the running process (such as a C#
callback stub or a `System32` export) or a reusable worker session must reset
mutable `.data`/`.bss` scratch globals between calls without reloading the DLL,
use Windows-only `LoadedModuleMemory` and module provenance helpers:

```csharp
using System;
using System.Runtime.InteropServices;
using AdaskoTheBeAsT.Interop.Unmanaged;

using var library = new UnmanagedLibrary(@"C:\Temp\IsolatedWorkers\w00\EngW00.dll");

// 1. Inspect or verify exact mapped PE headers (Machine, TimeDateStamp, SizeOfImage)
var expectedIdentity = new LoadedModuleIdentity(
    PeMachine.I386,
    timeDateStamp: 0x65F01234u,
    sizeOfImage: 0x00048000u);
LoadedModuleMemory.VerifyIdentity(library, expectedIdentity);

// 2. Provenance & System32 export lookup
string loadedPath = library.GetLoadedFilePath();
IntPtr calcExport = library.GetRequiredExport("CALCULATE");
bool ownsExport = library.ContainsAddress(calcExport);
IntPtr sysTick = UnmanagedLibrary.GetRequiredSystemExport("kernel32.dll", "GetTickCount");

// 3. Redirect a live IAT entry (named or ordinal) and keep the replacement rooted
using var hookPin = UnmanagedLibrary.PinConcreteDelegate<GetTickCountHook>(() => 42_000u);
IntPtr original = LoadedModuleMemory.RedirectImportFunction(
    library,
    importedModuleName: "kernel32.dll",
    exportName: "GetTickCount",
    replacementFunction: hookPin.Ptr);

try
{
    // Execute native routine while hookPin remains alive...
    // Clear leaked writable .data/.bss scratch state before the next session call:
    LoadedModuleMemory.ClearWritableBytesAtRva(library, expectedIdentity, rva: 0x32000, byteCount: 256);
}
finally
{
    // Restore original IAT target before releasing hookPin
    LoadedModuleMemory.RedirectImportFunction(library, "kernel32.dll", "GetTickCount", original);
}

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
delegate uint GetTickCountHook();
```

- `TryRedirectImportFunction`, `RedirectImportFunction`, and
  `TryRedirectImportOrdinal` require an unbound import lookup table
  (`OriginalFirstThunk != 0`), temporarily change only the single IAT pointer
  slot to `PAGE_READWRITE` via `VirtualProtect` when read-only, perform a
  volatile pointer write, and restore prior protection.
- `WriteVerifiedBytesAtRva` verifies current bytes against `expectedOriginal` or
  `replacement` (idempotent), scopes `VirtualProtect` when patching read-only
  sections, and calls `FlushInstructionCache` when executable pages are touched.
- `ClearWritableBytesAtRva` rejects read-only and executable sections
  (`InvalidOperationException`) rather than ever relaxing page protection.
- Quiesce native execution on the target module before patching IAT/RVA memory,
  and keep replacement delegates rooted until the IAT slot is restored or the
  module is unloaded.

<a name="slots-caches-and-byte-strings"></a>

## 🔢 Worker slots, pinned caches, scratch buffers, and fixed byte strings

5.0.0 includes four zero-dependency building blocks for multi-worker native
hosts across all five target frameworks:

```csharp
using System;
using AdaskoTheBeAsT.Interop.Unmanaged;

// 1. Bounded 00..99 worker slot leasing with excluded slots & thread-sticky reuse
var slots = new WorkerSlotAllocator(
    maxExclusive: 100,
    excludedSlots: [0],
    preferThreadStickySlot: true);
using WorkerSlotLease lease = slots.Acquire(); // e.g. SlotIndex == 1, reused on same worker thread

// 2. Single-flight, fault-evicting cache for process-pinned library handle bundles
var cache = PinnedLibraryCache.CreateOrdinalIgnoreCase<UnmanagedLibrary>();
UnmanagedLibrary pinned = cache.GetOrLoad(
    @"C:\Temp\IsolatedWorkers\pinned\Engine.dll",
    static path => new UnmanagedLibrary(path));

// 3. Zero-initialized SafeBuffer scratch memory with ref-counted pointer leases
using var scratch = new UnmanagedScratchBuffer(byteLength: 1024, zeroMemory: true);
using (UnmanagedScratchBuffer.PointerLease ptrLease = scratch.AcquirePointerLease())
{
    IntPtr rawBuffer = ptrLease.Pointer;
    // Pass rawBuffer to native P/Invoke safely...
}

scratch.Clear(byteOffset: 0, byteCount: 128);

// 4. Fixed-width C/Latin-1 NUL-terminated, Fortran space-padded, and Pascal ShortString fields
var cField = new byte[16];
NativeByteString.WriteNullTerminatedAscii("MODE_A", cField, 0, cField.Length);

var fortranField = new byte[12]; // CHARACTER*12 padded with 0x20 (' '), no NUL
NativeByteString.WriteSpacePaddedAscii("WATER", fortranField, 0, fortranField.Length);

var shortStringField = new byte[16]; // [lengthByte, ascii..., zero-pad]
NativeByteString.WriteShortStringAscii("CONFIG.INI", shortStringField, 0, shortStringField.Length);
```

On `net10.0` and `net11.0`, `NativeByteString` also exposes zero-allocation
`Span<byte>` / `ReadOnlySpan<byte>` overloads alongside `UnmanagedScratchBuffer.AsSpan()`.

<a name="execution-guards"></a>

## 🛡️ Floating-point and error-dialog guards

Some 32-bit and legacy native DLLs unmask x87/SSE floating-point exceptions
(`#ZE`, `#OE`, `#IE`) inside `DllMain` or calculation routines, causing later
managed `double` math (`1.0 / 0.0` or `double.NaN`) on the same thread to fault
at hardware level. Legacy DLLs may also trigger modal system error dialogs on
missing files.

`NativeExecutionGuard` (new in 5.0.0) provides thread-affine execution scopes:

```csharp
using AdaskoTheBeAsT.Interop.Unmanaged;

using (var scope = NativeExecutionGuard.EnterScope())
{
    // Masks x87/SSE FP traps while preserving caller rounding/precision modes,
    // and applies thread-scoped SetThreadErrorMode on Windows.
    InvokeLegacyRoutine();

    // Optional mid-scope reset before managed floating-point code runs:
    scope.RestoreFloatingPointState();
}
```

- `NativeExecutionGuard.EnterScope(threadErrorMode)` saves the thread's
  floating-point control state and Windows thread error mode, masks FP traps,
  and on `Dispose()` clears sticky exception flags (`_clearfp`) *before*
  restoring saved masks while preserving caller rounding and x86 precision
  settings (`__control87_2` on x86, `_controlfp_s` on x64/ARM64).
- `NativeExecutionScope` enforces owning-thread affinity on
  `RestoreFloatingPointState()` and `Dispose()`, and `Dispose()` is idempotent.
- `NativeExecutionGuard.SuppressProcessErrorDialogs()` is an explicit opt-in
  helper for startup code that intentionally wants process-wide `SetErrorMode`
  suppression (`0x8007`); `EnterScope` never mutates process-wide `SetErrorMode`.

<a name="api-reference"></a>

## 🧰 API at a glance

Here's the toolbox. All types live in the
`AdaskoTheBeAsT.Interop.Unmanaged` namespace.

| API | Purpose |
| --- | --- |
| `new UnmanagedLibrary(fileName, flags)` | Load a module owned by a disposable instance. Flags are optional. |
| `library.BaseAddress` / `UnmanagedLibrary.GetBaseAddress(handle)` | Get the mapped Windows module base address (`~3` masked). New in 5.0.0. |
| `library.GetLoadedFilePath()` / `ContainsAddress(addr)` | Query the loaded Windows module file path, image-range containment, address-to-module provenance (`TryGetLoadedModuleFromAddress`), and `System32` exports (`GetRequiredSystemExport`). New in 5.0.0. |
| `library.GetUnmanagedFunction<TDelegate>(name)` | Marshal an export as a delegate, or return `null` if missing. |
| `library.TryGetExport(name, out address)` | Get a raw export address without creating a delegate. |
| `library.GetRequiredExport(name)` | Get a raw export address or throw `EntryPointNotFoundException`. New in 5.0.0. |
| `UnmanagedLibrary.LoadLibrary(fileName, flags)` | Return an owning `SafeLibraryHandle`. Flags are optional. |
| `UnmanagedLibrary.FreeLibrary(handle)` | Dispose a handle; `null` and already closed handles are accepted. |
| `UnmanagedLibrary.GetUnmanagedFunction<TDelegate>(handle, name)` | Handle-based typed lookup. |
| `UnmanagedLibrary.TryGetExport(handle, name, out address)` | Handle-based raw lookup. |
| `UnmanagedLibrary.GetRequiredExport(handle, name)` | Handle-based required raw lookup. New in 5.0.0. |
| `UnmanagedLibrary.GetFunctionPointerForDelegate(callback, out binder)` | Create a callback pointer and a managed keep-alive root. |
| `UnmanagedLibrary.PinDelegate(callback)` | Create a `DelegatePin` scope, including generic proxy support. New in 4.0.0. |
| `UnmanagedLibrary.PinConcreteDelegate(callback)` | Create a scope for a non-generic delegate without emission. New in 4.0.0. |
| `UnmanagedLibrary.GetDelegateForFunctionPointer<T>(pointer, convention)` | Emit a raw unmanaged call thunk; no marshaling. |
| `PeImageEditor` | Bounds-checked Windows PE32/PE32+ export, import-removal, import/delay-import, symbol, verified string/byte RVA, `LargeAddressAware`, and checksum editor. New in 5.0.0. |
| `LoadedModuleMemory` / `LoadedModuleIdentity` | Runtime Windows module identity checks, named/ordinal IAT redirection, verified RVA patching, and writable `.data`/`.bss` scratch clearing. New in 5.0.0. |
| `IsolatedLibrarySet` / `WorkerModuleNaming` | Per-worker Windows native DLL graph and pre-`DllMain` companion staging, path-constraint guards, process-lifetime pinning, and stale-directory sweeping. New in 5.0.0. |
| `WorkerSlotAllocator` / `WorkerSlotLease` | Thread-safe bounded `00..99` worker slot leasing with optional thread-sticky affinity. New in 5.0.0. |
| `NativeByteString` | Fixed-width NUL-terminated ASCII/Latin-1, Fortran space-padded ASCII, and Delphi `ShortString` byte-buffer marshaling. New in 5.0.0. |
| `PinnedLibraryCache<TKey, THandles>` / `UnmanagedScratchBuffer` | Single-flight fault-evicting process-lifetime handle cache and `SafeBuffer`-backed unmanaged scratch memory. New in 5.0.0. |
| `NativeExecutionGuard` / `NativeExecutionScope` | Thread-affine FPU/SSE trap masking, sticky-flag clearing, and error-mode control. New in 5.0.0. |

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
| Missing export on an undisposed owner with a valid name | `null` for typed lookup; `false` and `IntPtr.Zero` for `TryGetExport`; `EntryPointNotFoundException` for `GetRequiredExport`. |
| Lookup or `BaseAddress` on a disposed owner | `ObjectDisposedException`. |
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
| Load/free, `TryGetExport`, `GetRequiredExport`, and Windows `BaseAddress` | No runtime emission. Covered by the consumer smoke sample. |
| `PeImageEditor`, `LoadedModuleMemory`, `IsolatedLibrarySet`, `WorkerModuleNaming`, `WorkerSlotAllocator`, `NativeByteString`, `PinnedLibraryCache`, `UnmanagedScratchBuffer`, and `NativeExecutionGuard` | No runtime emission or reflection. Safe under trimming and NativeAOT on supported platforms. |
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
(currently .NET SDK `11.0.100`). Native tests also require CMake 3.20+ and a C
compiler. On Windows, install the Visual Studio C++ build tools and ensure
`cmake` is on `PATH`, or pass `-p:CMakeCommand="<path-to-cmake.exe>"` when building.
Install the .NET 10 runtime alongside .NET SDK 11.0.100 to run all test targets.

From the repository root on Windows:

```powershell
dotnet build .\AdaskoTheBeAsT.Interop.Unmanaged.slnx -c Release
dotnet test --solution .\AdaskoTheBeAsT.Interop.Unmanaged.slnx -c Release --no-build
```

For one modern target, including on Linux/macOS:

```shell
dotnet test --project test/unit/AdaskoTheBeAsT.Interop.Unmanaged.Test/AdaskoTheBeAsT.Interop.Unmanaged.Test.csproj -c Release -f net11.0
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
- [ADR 0001: DelegatePin constraints, explicit IntPtr types, and 4.0.1 validation alignment](docs/adr/0001-delegate-pin-constraints-and-analyzer-baseline.md)
- [ADR 0002: .NET 11 upgrade, C# 14 polyfills, and 5.0.0 target framework lifecycle](docs/adr/0002-net11-upgrade-and-target-framework-lifecycle.md)
- [ADR 0003: Zero-dependency verified PE32 and PE32+ image editor](docs/adr/0003-verified-pe-image-editor.md)
- [ADR 0004: Per-worker native DLL graph isolation and lease-based stale directory sweeping](docs/adr/0004-isolated-native-library-sets-and-directory-leases.md)
- [ADR 0005: Thread-affine FPU and error-mode execution guards and raw export ergonomics](docs/adr/0005-native-execution-guards-and-export-ergonomics.md)
- [ADR 0006: PE export-name rewrites, import-descriptor compaction, verified string patches, and LargeAddressAware apphost stamping](docs/adr/0006-pe-export-rewrites-import-compaction-and-string-patches.md)
- [ADR 0007: Loaded-module runtime IAT redirection, verified in-memory RVA primitives, and module provenance](docs/adr/0007-loaded-module-iat-redirection-and-provenance.md)
- [ADR 0008: Pre-DllMain companion file staging, path-constraint preflight, and process-lifetime module pinning](docs/adr/0008-companion-staging-path-guards-and-process-lifetime-pinning.md)
- [ADR 0009: Bounded two-digit worker slot leasing, fixed-buffer native byte strings, and pinned library caches](docs/adr/0009-worker-slot-leasing-native-byte-strings-and-pinned-caches.md)
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
