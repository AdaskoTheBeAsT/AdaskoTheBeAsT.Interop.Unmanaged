# AdaskoTheBeAsT.Interop.Unmanaged

[![NuGet](https://img.shields.io/nuget/v/AdaskoTheBeAsT.Interop.Unmanaged.svg)](https://www.nuget.org/packages/AdaskoTheBeAsT.Interop.Unmanaged/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

> 🚀 Typed, safer dynamic DLL loading for .NET when static `DllImport` or `LibraryImport` is not enough.

`AdaskoTheBeAsT.Interop.Unmanaged` is a Windows-focused .NET library for loading native DLLs at runtime, resolving exports as strongly typed delegates, and releasing module handles safely with `SafeHandle`.

## ✨ Why developers will like it

- 🔒 **Safer lifetime management** with `SafeLibraryHandle`
- 🎯 **Strongly typed delegates** instead of manual `IntPtr` plumbing
- 🧩 **Runtime export lookup** for optional or version-specific native APIs
- 🪝 **Managed callback support** when native code needs a function pointer
- ⚙️ **Low-level control** through `LoadLibraryEx` flags
- 🧪 **Broad automated test coverage** across classic .NET Framework and modern .NET
- 📚 **Generated XML docs** and nullable-enabled code

## 🤔 When this library shines

This package is especially useful when you need to:

- Load a native DLL by name or by full path at runtime
- Probe for exports that may or may not exist on a given machine
- Use different load flags for system DLLs, resource DLLs, or third-party binaries
- Pass managed delegates into unmanaged code as callbacks
- Support both older .NET consumers and the latest .NET runtimes from one package

## 🙌 Why not just use `DllImport`?

If your native dependency is fixed at compile time and your exports are always present, `DllImport` or `LibraryImport` may be enough.

This library becomes valuable when you need **dynamic loading**, **optional exports**, **runtime path selection**, or **callback pointer generation** without hand-rolling the native interop plumbing every time.

## 🖥️ Platform and framework support

| Area | Support |
| --- | --- |
| Runtime | Windows only |
| Library target frameworks | `netstandard2.0`, `net8.0`, `net9.0`, `net10.0` |
| Automated test matrix | `net462`, `net47`, `net471`, `net472`, `net48`, `net481`, `net8.0`, `net9.0`, `net10.0` |

> ℹ️ `netstandard2.0` support means broad API compatibility for consumers. The runtime behavior is still Windows-only because the library wraps `kernel32` APIs such as `LoadLibraryEx`, `GetProcAddress`, and `FreeLibrary`.

## 📦 Installation

```bash
dotnet add package AdaskoTheBeAsT.Interop.Unmanaged
```

Or via Package Manager:

```powershell
Install-Package AdaskoTheBeAsT.Interop.Unmanaged
```

## 🚀 Quick start

### 1) Load a DLL and call an export

```csharp
using System;
using System.Runtime.InteropServices;
using AdaskoTheBeAsT.Interop.Unmanaged;

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
delegate uint GetCurrentProcessIdDelegate();

using var library = new UnmanagedLibrary("kernel32.dll");
var getCurrentProcessId = library.GetUnmanagedFunction<GetCurrentProcessIdDelegate>("GetCurrentProcessId");

if (getCurrentProcessId is not null)
{
    Console.WriteLine($"Current PID: {getCurrentProcessId()}");
}
```

### 2) Handle optional exports safely

`GetUnmanagedFunction<TDelegate>` returns `null` when the export is missing, which makes feature probing straightforward.

```csharp
[UnmanagedFunctionPointer(CallingConvention.Winapi)]
delegate IntPtr OptionalExportDelegate();

using var library = new UnmanagedLibrary("SomeNativeSdk.dll");
var optionalExport = library.GetUnmanagedFunction<OptionalExportDelegate>("OptionalExport");

if (optionalExport is null)
{
    Console.WriteLine("This version of the native SDK does not expose OptionalExport.");
}
```

### 3) Load from a specific path with explicit flags

Use a fully qualified path when you want deterministic loading behavior for a specific DLL.

```csharp
var flags =
    LoadLibraryFlags.LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR |
    LoadLibraryFlags.LOAD_LIBRARY_SEARCH_SYSTEM32;

using var library = new UnmanagedLibrary(@"C:\Native\MyLibrary.dll", flags);
```

### 4) Use the static handle-based API

If you want to manage the handle yourself, the static helpers are available too.

```csharp
using System;
using System.Runtime.InteropServices;
using AdaskoTheBeAsT.Interop.Unmanaged;

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
delegate uint GetTickCountDelegate();

using var handle = UnmanagedLibrary.LoadLibrary("kernel32.dll");
var getTickCount = UnmanagedLibrary.GetUnmanagedFunction<GetTickCountDelegate>(handle, "GetTickCount");

if (getTickCount is not null)
{
    Console.WriteLine($"Tick count: {getTickCount()}");
}
```

### 5) Pass a managed callback to native code

When you expose a managed delegate to unmanaged code, keep the returned `binder` alive for as long as native code may store or invoke the pointer.

```csharp
using System;
using AdaskoTheBeAsT.Interop.Unmanaged;

Func<int, int, int> callback = (a, b) => a + b;

var callbackPointer = UnmanagedLibrary.GetFunctionPointerForDelegate(callback, out var binder);

// Pass callbackPointer to native code here.

GC.KeepAlive(binder);
```

## 🧠 API at a glance

### `UnmanagedLibrary`

Main entry point for loading DLLs and resolving exports.

```csharp
new UnmanagedLibrary(string fileName, LoadLibraryFlags flags = ...);
TDelegate? GetUnmanagedFunction<TDelegate>(string functionName);

static SafeLibraryHandle LoadLibrary(string fileName, LoadLibraryFlags flags = ...);
static void FreeLibrary(SafeLibraryHandle? safeLibraryHandle);
static TDelegate? GetUnmanagedFunction<TDelegate>(SafeLibraryHandle safeLibraryHandle, string functionName);
static IntPtr GetFunctionPointerForDelegate<T>(T delegateCallback, out object binder);
```

### `SafeLibraryHandle`

Wraps the native module handle using the .NET `SafeHandle` pattern, which helps prevent leaks and double-free mistakes.

### `LoadLibraryFlags`

Exposes Windows `LoadLibraryEx` flags so you can control how the loader locates and initializes modules.

## 🛡️ Safety and lifetime rules

These are the most important things to remember:

- ✅ Keep the `UnmanagedLibrary` instance or `SafeLibraryHandle` alive while retrieved delegates are still in use
- ✅ Keep the callback `binder` alive while native code may call the function pointer
- ✅ Use the exact delegate signature and calling convention expected by the native export
- ✅ Prefer explicit `LOAD_LIBRARY_SEARCH_*` flags when loading third-party binaries
- ❌ Do not unload the library and continue using delegates you obtained from it
- ❌ Do not load untrusted DLLs

## ⚠️ Important behavior notes

- Export names are **case-sensitive**
- Invalid file names throw `ArgumentException`
- Failed loads throw `Win32Exception`
- Missing exports return `null`
- `FreeLibrary` is safe to call with `null` or an already closed handle
- Flags such as `LOAD_LIBRARY_AS_DATAFILE` change loader behavior and are intended for special scenarios, not standard function invocation

## 💡 Common use cases

- Loading Windows system DLLs such as `kernel32.dll` or `user32.dll`
- Dynamically integrating with third-party native SDKs
- Supporting optional native features across multiple versions of the same DLL
- Registering managed callbacks with unmanaged code
- Choosing DLL resolution behavior explicitly to reduce surprises

## 🧪 Quality notes

The project is built with quality-oriented defaults, including:

- nullable reference types enabled
- generated XML documentation
- warnings treated as errors
- automated tests across .NET Framework 4.6.2-4.8.1 and .NET 8-10

## ❓FAQ

### Do I need to pass a full DLL path?

Not always. A bare module name such as `kernel32.dll` works when the selected flags can resolve it. Use a fully qualified path when you want deterministic loading from a specific location.

### What happens if the export does not exist?

`GetUnmanagedFunction<TDelegate>` returns `null`, so you can probe for optional functionality without exceptions.

### Do I need to use `DelegatePin` directly?

Usually no. Most consumers only need to keep the `binder` returned by `GetFunctionPointerForDelegate` rooted for the required lifetime.

### Can I use this on Linux or macOS?

No. The package may target cross-platform TFMs, but its runtime implementation depends on Windows loader APIs.

## 📄 License

This project is licensed under the [MIT License](LICENSE).