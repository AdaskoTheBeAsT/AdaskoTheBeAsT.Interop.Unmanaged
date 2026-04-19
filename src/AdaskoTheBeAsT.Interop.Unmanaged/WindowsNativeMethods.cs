using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;
using System.Security;

namespace AdaskoTheBeAsT.Interop.Unmanaged;

#pragma warning disable CA1060

[ExcludeFromCodeCoverage]
internal static class WindowsNativeMethods
{
    private const string KernelLib = "kernel32";

    [SuppressUnmanagedCodeSecurity]
    [DllImport(KernelLib, CharSet = CharSet.Unicode, BestFitMapping = false, SetLastError = true, EntryPoint = nameof(LoadLibraryEx))]
    internal static extern IntPtr LoadLibraryEx(
        string fileName,
        IntPtr hFile,
        [MarshalAs(UnmanagedType.U4)] LoadLibraryFlags dwFlags);

#if NETFRAMEWORK
    [ReliabilityContract(Consistency.WillNotCorruptState, Cer.Success)]
#endif
    [SuppressUnmanagedCodeSecurity]
    [DllImport(KernelLib, SetLastError = true, EntryPoint = nameof(FreeLibrary))]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool FreeLibrary(IntPtr hModule);

    [SuppressUnmanagedCodeSecurity]
    [DllImport(KernelLib, CharSet = CharSet.Ansi, EntryPoint = nameof(GetProcAddress), ExactSpelling = true, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    internal static extern IntPtr GetProcAddress(IntPtr hModule, string procname);
}
#pragma warning restore CA1060
