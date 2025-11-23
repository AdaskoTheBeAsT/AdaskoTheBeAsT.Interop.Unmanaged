# AdaskoTheBeAsT.Interop.Unmanaged

[![NuGet](https://img.shields.io/nuget/v/AdaskoTheBeAsT.Interop.Unmanaged.svg)](https://www.nuget.org/packages/AdaskoTheBeAsT.Interop.Unmanaged/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

> 🚀 A robust, type-safe, and memory-efficient .NET library for loading and managing unmanaged DLLs with zero hassle!

## Why This Library?

Working with native DLLs in .NET can be tricky - memory leaks, crashes, and platform-specific quirks. This library provides a **battle-tested**, **safe**, and **elegant** wrapper around Windows `LoadLibrary`, `GetProcAddress`, and `FreeLibrary` APIs.

### ✨ Key Features

- 🛡️ **Memory Safe** - Uses `SafeHandle` pattern to prevent resource leaks
- 🔒 **Type Safe** - Strongly-typed delegate mapping for native functions
- ⚡ **High Performance** - Minimal overhead with optimized P/Invoke
- 🎯 **Developer Friendly** - Simple, intuitive API that just works
- 🔧 **Flexible** - Support for custom calling conventions and generic delegates
- 📦 **Multi-Framework** - Supports .NET Standard 2.0, .NET 8.0, and .NET 9.0
- 🧪 **Production Ready** - Analyzed by 20+ code quality tools

## Installation

```bash
dotnet add package AdaskoTheBeAsT.Interop.Unmanaged
```

Or via Package Manager:

```powershell
Install-Package AdaskoTheBeAsT.Interop.Unmanaged
```

## Quick Start

### Basic Usage

```csharp
using AdaskoTheBeAsT.Interop.Unmanaged;

// Define your native function signature
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate int Add(int a, int b);

// Load the DLL and get the function
using var library = new UnmanagedLibrary("MyNative.dll");
var addFunction = library.GetUnmanagedFunction<Add>("add");

if (addFunction != null)
{
    int result = addFunction(5, 3); // result = 8
    Console.WriteLine($"Result: {result}");
}
```

### Advanced: Custom Load Flags

```csharp
using AdaskoTheBeAsT.Interop.Unmanaged;

// Load with custom search paths
var flags = LoadLibraryFlags.LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR 
          | LoadLibraryFlags.LOAD_LIBRARY_SEARCH_SYSTEM32;

using var library = new UnmanagedLibrary(@"C:\MyLibs\custom.dll", flags);
var myFunc = library.GetUnmanagedFunction<MyDelegate>("MyFunction");
```

### Static Helper Methods

```csharp
// Load library without wrapper
var handle = UnmanagedLibrary.LoadLibrary("kernel32.dll");

// Get function from handle
var func = UnmanagedLibrary.GetUnmanagedFunction<MyDelegate>(handle, "GetVersion");

// Clean up when done
UnmanagedLibrary.FreeLibrary(handle);
```

### Generic Delegates with Custom Calling Conventions

```csharp
// Get function pointer for delegate
var ptr = UnmanagedLibrary.GetFunctionPointerForDelegate(myCallback, out var binder);

// Create delegate from function pointer with custom calling convention
var del = UnmanagedLibrary.GetDelegateForFunctionPointer<MyDelegate>(
    ptr, 
    CallingConventions.Standard
);
```

## API Overview

### Core Classes

#### `UnmanagedLibrary`
Main class for loading and managing native DLLs.

```csharp
// Constructor
UnmanagedLibrary(string fileName, LoadLibraryFlags flags = ...)

// Get function as delegate
TDelegate? GetUnmanagedFunction<TDelegate>(string functionName)

// Static helpers
static SafeLibraryHandle LoadLibrary(string fileName, LoadLibraryFlags flags)
static void FreeLibrary(SafeLibraryHandle handle)
static TDelegate? GetUnmanagedFunction<TDelegate>(SafeLibraryHandle handle, string functionName)
```

#### `SafeLibraryHandle`
Thread-safe handle wrapper that ensures proper cleanup.

#### `LoadLibraryFlags`
Comprehensive enum of Windows LoadLibraryEx flags for fine-grained control over DLL loading behavior.

#### `DelegatePin`
Utility struct for pinning delegates in memory to prevent garbage collection during native callbacks.

## Common Use Cases

### Loading a 3rd Party Native Library

```csharp
using var lib = new UnmanagedLibrary("opencv_world.dll");
var cvVersion = lib.GetUnmanagedFunction<GetVersionDelegate>("cvGetVersion");
```

### Platform-Specific DLL Loading

```csharp
#if WINDOWS
var flags = LoadLibraryFlags.LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR 
          | LoadLibraryFlags.LOAD_LIBRARY_SEARCH_SYSTEM32;
using var lib = new UnmanagedLibrary("native_win.dll", flags);
#elif LINUX
// Use DllImport or different interop mechanism
#endif
```

### Extracting Resources from DLLs

```csharp
// Load DLL as data file without executing code
var flags = LoadLibraryFlags.LOAD_LIBRARY_AS_DATAFILE;
using var lib = new UnmanagedLibrary("resource.dll", flags);
// Extract resources here
```

## Error Handling

The library throws meaningful exceptions with context:

```csharp
try
{
    using var lib = new UnmanagedLibrary("missing.dll");
}
catch (Win32Exception ex)
{
    // "Failed to load library 'missing.dll'."
    Console.WriteLine($"Error: {ex.Message}");
    Console.WriteLine($"Native Error Code: {ex.NativeErrorCode}");
}

try
{
    using var lib = new UnmanagedLibrary(null!);
}
catch (ArgumentException ex)
{
    // "Value cannot be null or whitespace. (Parameter 'fileName')"
    Console.WriteLine($"Error: {ex.Message}");
}
```

## Performance Tips

1. **Reuse library instances** - Keep the `UnmanagedLibrary` instance alive if you need multiple function calls
2. **Cache delegates** - Store retrieved delegates instead of calling `GetUnmanagedFunction` repeatedly
3. **Use static methods** - For one-off calls, static helper methods avoid object allocation

```csharp
// ❌ Bad - Repeated loading
for (int i = 0; i < 1000; i++)
{
    using var lib = new UnmanagedLibrary("my.dll");
    var func = lib.GetUnmanagedFunction<MyDelegate>("MyFunc");
    func?.Invoke();
}

// ✅ Good - Load once, use many times
using var lib = new UnmanagedLibrary("my.dll");
var func = lib.GetUnmanagedFunction<MyDelegate>("MyFunc");
for (int i = 0; i < 1000; i++)
{
    func?.Invoke();
}
```

## Security Considerations

- The library uses `SuppressUnmanagedCodeSecurity` for performance - only load trusted DLLs
- Always validate file paths before loading to prevent DLL hijacking attacks
- Use `LOAD_LIBRARY_SEARCH_*` flags to control DLL search paths and prevent loading from untrusted locations
- Dispose library instances promptly to prevent holding locks on DLL files

## Requirements

- **.NET Standard 2.0** or higher
- **.NET 8.0** or higher
- **.NET 9.0** or higher
- **Windows OS** (uses Windows API under the hood)

## Contributing

Contributions are welcome! This project maintains high code quality standards with:
- 20+ static analyzers (StyleCop, Roslynator, SonarAnalyzer, etc.)
- Treat warnings as errors
- Nullable reference types enabled
- Comprehensive documentation required

## License

This project is licensed under the **MIT License** - see the [LICENSE](LICENSE) file for details.

## Credits

Developed with ❤️ by [Adam Pluciński](https://github.com/AdaskoTheBeAsT)

---

**⭐ If this library saved you time, give it a star on GitHub!**