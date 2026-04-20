# AdaskoTheBeAsT.Interop.Unmanaged

> 🚀 Typed, safe, lifetime-managed dynamic native library loading for .NET — on Windows, Linux, and macOS.

[![NuGet](https://img.shields.io/nuget/v/AdaskoTheBeAsT.Interop.Unmanaged.svg?label=AdaskoTheBeAsT.Interop.Unmanaged&logo=nuget)](https://www.nuget.org/packages/AdaskoTheBeAsT.Interop.Unmanaged/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/AdaskoTheBeAsT.Interop.Unmanaged.svg?logo=nuget)](https://www.nuget.org/packages/AdaskoTheBeAsT.Interop.Unmanaged/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](./LICENSE)
![TFMs](https://img.shields.io/badge/TFMs-net10.0%20%7C%20net9.0%20%7C%20net8.0%20%7C%20net4.6.2%E2%80%93net4.8.1-512BD4?logo=dotnet)
![Platforms](https://img.shields.io/badge/platforms-Windows%20%7C%20Linux%20%7C%20macOS-blue)
![Warnings](https://img.shields.io/badge/warnings--as--errors-on-green)
![Deterministic](https://img.shields.io/badge/deterministic%20build-on-blue)

### 🔬 Code quality — SonarCloud

[![Quality Gate Status](https://sonarcloud.io/api/project_badges/measure?project=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop.Unmanaged&metric=alert_status)](https://sonarcloud.io/summary/new_code?id=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop.Unmanaged)
[![Coverage](https://sonarcloud.io/api/project_badges/measure?project=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop.Unmanaged&metric=coverage)](https://sonarcloud.io/component_measures?id=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop.Unmanaged&metric=coverage)
[![Maintainability Rating](https://sonarcloud.io/api/project_badges/measure?project=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop.Unmanaged&metric=sqale_rating)](https://sonarcloud.io/component_measures?id=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop.Unmanaged&metric=sqale_rating)
[![Reliability Rating](https://sonarcloud.io/api/project_badges/measure?project=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop.Unmanaged&metric=reliability_rating)](https://sonarcloud.io/component_measures?id=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop.Unmanaged&metric=reliability_rating)
[![Security Rating](https://sonarcloud.io/api/project_badges/measure?project=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop.Unmanaged&metric=security_rating)](https://sonarcloud.io/component_measures?id=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop.Unmanaged&metric=security_rating)
[![Bugs](https://sonarcloud.io/api/project_badges/measure?project=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop.Unmanaged&metric=bugs)](https://sonarcloud.io/component_measures?id=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop.Unmanaged&metric=bugs)
[![Vulnerabilities](https://sonarcloud.io/api/project_badges/measure?project=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop.Unmanaged&metric=vulnerabilities)](https://sonarcloud.io/component_measures?id=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop.Unmanaged&metric=vulnerabilities)
[![Code Smells](https://sonarcloud.io/api/project_badges/measure?project=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop.Unmanaged&metric=code_smells)](https://sonarcloud.io/component_measures?id=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop.Unmanaged&metric=code_smells)
[![Duplicated Lines (%)](https://sonarcloud.io/api/project_badges/measure?project=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop.Unmanaged&metric=duplicated_lines_density)](https://sonarcloud.io/component_measures?id=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop.Unmanaged&metric=duplicated_lines_density)
[![Technical Debt](https://sonarcloud.io/api/project_badges/measure?project=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop.Unmanaged&metric=sqale_index)](https://sonarcloud.io/component_measures?id=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop.Unmanaged&metric=sqale_index)
[![Lines of Code](https://sonarcloud.io/api/project_badges/measure?project=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop.Unmanaged&metric=ncloc)](https://sonarcloud.io/component_measures?id=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop.Unmanaged&metric=ncloc)

---

## 👋 Hello, native-loving friend

You've got a native library. The static `DllImport` attribute is *fine*, right up until it isn't:

- 🧬 the DLL name depends on runtime config or an installer location
- 🎯 some exports only exist on certain versions of the SDK
- 🏢 you need `LOAD_LIBRARY_SEARCH_SYSTEM32` and friends to survive DLL-hijacking audits
- 🐧 you want the same loader story on Linux and macOS
- 🪝 native code wants a *function pointer* to a managed callback
- ♻️ you want `SafeHandle` lifetime — not a `Marshal.FreeLibrary` landmine

`AdaskoTheBeAsT.Interop.Unmanaged` is the tiny, focused library that parks all that plumbing somewhere safe so your code stays readable. 📦

---

## ✨ Why you'll love this

- 🔒 **`SafeHandle`-backed lifetime.** `SafeLibraryHandle` cleans up on its own — no more "did I remember to `FreeLibrary`?" moments.
- 🎯 **Strongly typed delegates.** Resolve exports into real delegate types, not `IntPtr` soup.
- 🪝 **Managed-to-native callbacks.** `GetFunctionPointerForDelegate` gives you a stable pointer *plus* a `binder` root so the GC can't rip it out from under native code.
- 🔀 **Open-generic delegate support.** Callbacks typed as `Delegate<T>`? The library IL-emits a concrete proxy on the fly and copies your `[UnmanagedFunctionPointer]` attribute onto it so the calling convention matches.
- 🧪 **Runtime export probing.** Missing function? `GetUnmanagedFunction<T>` returns `null`, `TryGetExport` returns `false`. No exceptions, no try/catch dances.
- 🧭 **Modern `delegate* unmanaged` friendly.** `TryGetExport` hands you the raw `IntPtr` so you can cast to `delegate* unmanaged[Stdcall]<int, int>` on `net5+`.
- 🪟🐧🍎 **Windows + Linux + macOS.** Uses `LoadLibraryEx` on Windows; delegates to `NativeLibrary` on modern .NET and raw `dlopen` on .NET Framework via Mono.
- ⚙️ **Full `LoadLibraryFlags` control.** `LOAD_LIBRARY_SEARCH_SYSTEM32`, `LOAD_WITH_ALTERED_SEARCH_PATH`, `LOAD_LIBRARY_AS_DATAFILE` — all there, faithfully honored on Windows.
- 🧬 **9 TFMs, all green.** `net10.0`, `net9.0`, `net8.0`, `net481`, `net48`, `net472`, `net471`, `net47`, `net462` — the full matrix on every build.
- 🛡️ **Quality-first.** `TreatWarningsAsErrors=true`, deterministic builds, nullable annotations, generated XML docs, and a SonarCloud quality gate on every commit.
- ✏️ **Source Link + snupkg.** Step into the library from your debugger without guessing.

---

## 📦 Installation

```bash
dotnet add package AdaskoTheBeAsT.Interop.Unmanaged
```

Or via Package Manager:

```powershell
Install-Package AdaskoTheBeAsT.Interop.Unmanaged
```

Symbols ship as `.snupkg` with Source Link and embedded untracked sources. Step in. Look around. It's fine. 🔍

---

## 🗺️ Target framework matrix

| TFM | Status | Notes |
| --- | :-: | --- |
| `net10.0` | ✅ | Uses `System.Runtime.InteropServices.NativeLibrary` under the hood on non-Windows. |
| `net9.0` | ✅ | Same. |
| `net8.0` | ✅ | Same. |
| `net481` | ✅ | Windows desktop; other platforms go through hand-rolled `dlopen`/`dlsym` P/Invokes under Mono. |
| `net48` | ✅ | Same. |
| `net472` | ✅ | Same. |
| `net471` | ✅ | Same. |
| `net47` | ✅ | Same. |
| `net462` | ✅ | Same. |

Every cell is built with `TreatWarningsAsErrors=true`, `ContinuousIntegrationBuild=true`, `Deterministic=true`, and exercised by the test suite.

### 🪟🐧🍎 Platform behavior

| Platform | Loader path | `LoadLibraryFlags` honored? |
| --- | --- | :-: |
| 🪟 **Windows** (all TFMs) | `LoadLibraryEx` / `GetProcAddress` / `FreeLibrary` from `kernel32.dll` | ✅ fully |
| 🐧 **Linux** on `net8+` | `NativeLibrary.Load` | ❌ flags silently ignored (`RTLD_NOW` semantics) |
| 🍎 **macOS** on `net8+` | `NativeLibrary.Load` | ❌ flags silently ignored (`RTLD_NOW` semantics) |
| 🐧 **Linux** on `net4.x` (Mono) | `dlopen(..., RTLD_NOW)` from `libdl.so.2` | ❌ flags silently ignored |
| 🍎 **macOS** on `net4.x` (Mono) | `dlopen(..., RTLD_NOW)` from `libSystem.dylib` | ❌ flags silently ignored |

> 💡 The `LoadLibraryFlags` argument is accepted on every platform for call-site compatibility — it's only *applied* on Windows, which is exactly what most interop callers expect.

---

## 🚀 Quick start

### 1️⃣ Load a DLL and call an export

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

### 2️⃣ Probe for optional exports safely

`GetUnmanagedFunction<TDelegate>` returns `null` when the export is missing, which makes feature-probing trivial — no exceptions, no `Marshal.GetLastWin32Error` rituals.

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

### 3️⃣ Load from an explicit path with explicit flags

Use a fully qualified path when you want deterministic loading behavior for a specific DLL. Combine flags with `|`.

```csharp
var flags =
    LoadLibraryFlags.LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR |
    LoadLibraryFlags.LOAD_LIBRARY_SEARCH_SYSTEM32;

using var library = new UnmanagedLibrary(@"C:\Native\MyLibrary.dll", flags);
```

### 4️⃣ Static, handle-based API

Prefer to manage the handle yourself? The static helpers are there.

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

### 5️⃣ Pass a managed callback to native code

When you hand a managed delegate to unmanaged code, keep the returned `binder` alive for as long as native code may store or invoke the pointer.

```csharp
using System;
using AdaskoTheBeAsT.Interop.Unmanaged;

Func<int, int, int> callback = (a, b) => a + b;

var callbackPointer = UnmanagedLibrary.GetFunctionPointerForDelegate(callback, out var binder);

// Pass callbackPointer into native code here.
// ...

GC.KeepAlive(binder);
```

> 💡 For open-generic delegate types (e.g. `Action<T>`), the library IL-emits a non-generic proxy delegate at runtime *and* copies your `[UnmanagedFunctionPointer]` attribute onto the proxy so the generated function pointer uses the correct unmanaged calling convention.

### 6️⃣ Modern `delegate* unmanaged` via `TryGetExport`

On `net5+` you can skip the `Marshal` layer entirely and use a function-pointer type (`delegate* unmanaged[Stdcall]<...>`). `TryGetExport` gives you the raw `IntPtr`.

```csharp
using System;
using AdaskoTheBeAsT.Interop.Unmanaged;

using var library = new UnmanagedLibrary("kernel32.dll");

if (library.TryGetExport("GetCurrentProcessId", out var addr))
{
    unsafe
    {
        var fn = (delegate* unmanaged[Stdcall]<uint>)addr;
        Console.WriteLine($"PID: {fn()}");
    }
}
```

Static overload exists too:

```csharp
using var handle = UnmanagedLibrary.LoadLibrary("kernel32.dll");
UnmanagedLibrary.TryGetExport(handle, "GetCurrentProcessId", out var addr);
```

---

## 🧠 API at a glance

### `UnmanagedLibrary`

Main entry point for loading DLLs and resolving exports.

```csharp
// Instance API
new UnmanagedLibrary(string fileName, LoadLibraryFlags flags = ...);
TDelegate? GetUnmanagedFunction<TDelegate>(string functionName);
bool TryGetExport(string functionName, out IntPtr functionPointer);

// Static / handle-based API
static SafeLibraryHandle LoadLibrary(string fileName, LoadLibraryFlags flags = ...);
static void FreeLibrary(SafeLibraryHandle? safeLibraryHandle);
static TDelegate? GetUnmanagedFunction<TDelegate>(SafeLibraryHandle handle, string functionName);
static bool TryGetExport(SafeLibraryHandle handle, string functionName, out IntPtr functionPointer);

// Managed-to-native callbacks
static IntPtr GetFunctionPointerForDelegate<T>(T delegateCallback, out object binder);

// IL-emit re-wrap (advanced; prefer `Marshal.GetDelegateForFunctionPointer<T>`)
static T? GetDelegateForFunctionPointer<T>(IntPtr ptr, CallingConvention callingConvention);
```

### `SafeLibraryHandle`

Wraps the native module handle using the .NET `SafeHandle` pattern, which helps prevent leaks and double-free mistakes. `using`-friendly.

### `LoadLibraryFlags`

`[Flags]` enum mirroring the Windows `LoadLibraryEx` flags — `LOAD_LIBRARY_SEARCH_SYSTEM32`, `LOAD_WITH_ALTERED_SEARCH_PATH`, `LOAD_LIBRARY_AS_DATAFILE`, `LOAD_IGNORE_CODE_AUTHZ_LEVEL`, and friends.

### `DelegatePin`

Internal helper that roots generic-delegate proxies so the JIT-generated bridge stays alive. Most consumers only need to keep the returned `binder` rooted — `DelegatePin` is exposed for edge cases where you're doing the wrapping yourself.

---

## 🛡️ Safety and lifetime rules

These are the few things you actually need to remember:

- ✅ Keep the `UnmanagedLibrary` / `SafeLibraryHandle` alive while retrieved delegates or function pointers are still in use
- ✅ Keep the callback `binder` alive while native code may call the function pointer
- ✅ Use the exact delegate signature *and* calling convention expected by the native export
- ✅ Prefer explicit `LOAD_LIBRARY_SEARCH_*` flags when loading third-party binaries (audit-friendly)
- ❌ Do not unload the library and continue using delegates or function pointers you obtained from it
- ❌ Do not load untrusted DLLs 🚫

---

## ⚠️ Important behavior notes

- Export names are **case-sensitive** 🔠 (this matches native loader semantics on Linux/macOS and avoids surprises on Windows)
- Invalid file names throw `ArgumentException`
- Failed loads throw `Win32Exception` with `Failed to load library '<name>'`; the trailing message is the native loader error string on Windows and on the .NET Framework/Mono `dlopen` path, and the wrapped managed-exception message (`DllNotFoundException` / `BadImageFormatException` / `FileLoadException`) on .NET 8+ non-Windows
- Missing exports return `null` (classic API) or `false` (`TryGetExport`) — never throw
- `FreeLibrary` is safe to call with `null` or an already closed handle (idempotent)
- `LoadLibraryFlags` are silently ignored on Linux/macOS; the library always passes `RTLD_NOW` on those platforms
- The IL-emit path in `GetDelegateForFunctionPointer<T>` does **not** perform parameter marshaling — for string / struct marshaling use `Marshal.GetDelegateForFunctionPointer<T>(ptr)` instead

---

## 🤔 When to reach for this library

Use this when you need **any** of:

| Scenario | Why `DllImport` isn't enough |
| --- | --- |
| 🔀 DLL path chosen at runtime | `DllImport` wants a compile-time string |
| 🎯 Optional / version-specific exports | `DllImport` throws `EntryPointNotFoundException` |
| 🏢 Explicit `LOAD_LIBRARY_SEARCH_*` flags | `DllImport` uses default loader search order |
| 🪝 Managed callback ↔ native function pointer | `Marshal.GetFunctionPointerForDelegate` is OK, but you have to manage the GC root yourself |
| 🧬 `delegate* unmanaged[X]<...>` from a dynamically loaded DLL | `DllImport` doesn't apply; you need `dlsym`/`GetProcAddress` |
| 🔒 `SafeHandle`-backed native module lifetime | `Marshal.FreeLibrary` is a foot-gun |

If the DLL is fixed at compile time *and* every export is always present, `DllImport` (or `[LibraryImport]` on `net7+`) is absolutely the right tool. 👍

---

## 💡 Common use cases

- 🔧 Loading Windows system DLLs such as `kernel32.dll` or `user32.dll`
- 🏢 Dynamically integrating with third-party native SDKs whose install path you read at runtime
- 🎯 Supporting optional native features across multiple versions of the same DLL
- 🪝 Registering managed callbacks with unmanaged code
- 🐧🍎 Writing cross-platform interop that speaks to platform-specific shared libraries (`libfoo.so`, `libfoo.dylib`, `foo.dll`)
- 🧱 Choosing DLL resolution behavior explicitly to reduce DLL-hijacking risk

---

## 🧪 Build and test

```powershell
dotnet build .\AdaskoTheBeAsT.Interop.Unmanaged.slnx
dotnet test  .\AdaskoTheBeAsT.Interop.Unmanaged.slnx --no-build
```

The test suite runs across the full 9-TFM matrix. Windows-specific tests (things that call `kernel32`) self-skip on non-Windows hosts.

---

## 🧪 Quality notes

This project is built with quality-oriented defaults:

- 🛡️ Nullable reference types enabled
- 📝 Generated XML documentation in every package
- 🚨 `TreatWarningsAsErrors=true` across all projects
- 🧪 Automated tests across `net462`–`net481` and `net8`–`net10`
- 🔬 Static analysis via Roslyn analyzers + SonarCloud quality gate
- 🧬 Deterministic, `ContinuousIntegrationBuild=true` packages
- 🔍 Source Link + `snupkg` symbols for step-in debugging

---

## ❓ FAQ

### Do I need to pass a full DLL path?

Not always. A bare module name like `kernel32.dll` works when the selected flags can resolve it. Use a fully qualified path when you want deterministic loading from a specific location.

### What happens if the export does not exist?

`GetUnmanagedFunction<TDelegate>` returns `null`. `TryGetExport` returns `false` with `IntPtr.Zero`. No exceptions.

### Do I need to use `DelegatePin` directly?

Usually no. Most consumers only need to keep the `binder` returned by `GetFunctionPointerForDelegate` rooted for the required lifetime.

### Can I use this on Linux and macOS?

Yes. 🎉 On `net8+` the non-Windows path delegates to `System.Runtime.InteropServices.NativeLibrary`; on .NET Framework under Mono it falls back to direct `dlopen`/`dlsym` P/Invokes. Note that `LoadLibraryFlags` are silently ignored on non-Windows platforms and `RTLD_NOW` is used.

### Which should I use: `GetUnmanagedFunction<T>` or `TryGetExport` + `delegate*`?

On `net5+` with `unsafe` code, `TryGetExport` + `delegate* unmanaged[Stdcall]<...>` gives you zero-alloc direct calls. On older TFMs, or when you want marshaling (strings, structs), stick with `GetUnmanagedFunction<T>`.

### Can I re-wrap a raw `IntPtr` back into a delegate?

Yes — `GetDelegateForFunctionPointer<T>(ptr, callingConvention)` IL-emits a thunk using `calli`. **Note:** this path does not marshal parameters. For marshaling (e.g. `string` ↔ `LPWStr`), use `Marshal.GetDelegateForFunctionPointer<T>(ptr)` instead.

### Does it support musl-based Linux (Alpine)?

On `net8+` it does — `NativeLibrary.Load` handles the search. On .NET Framework under Mono, the current hard-coded soname is `libdl.so.2`, which works on glibc distros; Alpine/musl is not explicitly supported on the Mono path.

---

## 🙋 Contributing

Found a bug? Got an idea? Spotted a typo that's been haunting you? 👻

1. 🐙 Open an issue describing the problem or the proposal.
2. 🛠️ Fork + branch (`feature/your-idea`).
3. ✅ Run `dotnet build` + `dotnet test` across the full matrix.
4. ✨ Add/update tests — the strict-build settings will tell you if something's off.
5. 🚀 Open a PR — the CI will do the rest.

---

## 📄 License

This project is licensed under the [MIT License](LICENSE).

---

<p align="center">
  Because <em>dynamic</em> native DLL loading in .NET shouldn't feel like defusing a bomb. 💣➡️🕊️<br/>
  Made with ❤️ (and a lot of coffee ☕) by <a href="https://github.com/AdaskoTheBeAsT">AdaskoTheBeAsT</a>.
</p>
