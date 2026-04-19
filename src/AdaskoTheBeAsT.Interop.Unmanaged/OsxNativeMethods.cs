using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Security;

namespace AdaskoTheBeAsT.Interop.Unmanaged;

#pragma warning disable CA1060
#pragma warning disable SA1300
#pragma warning disable SA1310
#pragma warning disable IDE1006
#pragma warning disable SYSLIB1054

/// <summary>
/// P/Invoke signatures for the POSIX dynamic loader on macOS.
/// </summary>
/// <remarks>
/// On macOS the dynamic loader symbols are exposed by <c>libSystem.dylib</c>.
/// </remarks>
[ExcludeFromCodeCoverage]
internal static class OsxNativeMethods
{
    internal const int RTLD_NOW = 2;

    private const string LibSystem = "libSystem.dylib";

    [SuppressUnmanagedCodeSecurity]
    [DllImport(LibSystem, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true, EntryPoint = nameof(dlopen))]
    internal static extern IntPtr dlopen(string fileName, int flags);

    [SuppressUnmanagedCodeSecurity]
    [DllImport(LibSystem, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true, EntryPoint = nameof(dlsym))]
    internal static extern IntPtr dlsym(IntPtr handle, string symbol);

    [SuppressUnmanagedCodeSecurity]
    [DllImport(LibSystem, EntryPoint = nameof(dlclose))]
    internal static extern int dlclose(IntPtr handle);

    [SuppressUnmanagedCodeSecurity]
    [DllImport(LibSystem, EntryPoint = nameof(dlerror))]
    internal static extern IntPtr dlerror();
}

#pragma warning restore SYSLIB1054
#pragma warning restore IDE1006
#pragma warning restore SA1310
#pragma warning restore SA1300
#pragma warning restore CA1060
