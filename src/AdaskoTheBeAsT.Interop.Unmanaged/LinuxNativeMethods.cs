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
/// P/Invoke signatures for the POSIX dynamic loader on Linux.
/// </summary>
/// <remarks>
/// Uses <c>libdl.so.2</c> which still exists as a stub on modern glibc (2.34+) that forwards
/// the symbols to <c>libc.so.6</c>, and is the canonical location on older systems.
/// </remarks>
[ExcludeFromCodeCoverage]
internal static class LinuxNativeMethods
{
    internal const int RTLD_NOW = 2;

    private const string LibDl = "libdl.so.2";

    [SuppressUnmanagedCodeSecurity]
    [DllImport(LibDl, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true, EntryPoint = nameof(dlopen))]
    internal static extern IntPtr dlopen(string fileName, int flags);

    [SuppressUnmanagedCodeSecurity]
    [DllImport(LibDl, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true, EntryPoint = nameof(dlsym))]
    internal static extern IntPtr dlsym(IntPtr handle, string symbol);

    [SuppressUnmanagedCodeSecurity]
    [DllImport(LibDl, EntryPoint = nameof(dlclose))]
    internal static extern int dlclose(IntPtr handle);

    [SuppressUnmanagedCodeSecurity]
    [DllImport(LibDl, EntryPoint = nameof(dlerror))]
    internal static extern IntPtr dlerror();
}

#pragma warning restore SYSLIB1054
#pragma warning restore IDE1006
#pragma warning restore SA1310
#pragma warning restore SA1300
#pragma warning restore CA1060
