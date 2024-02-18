using System;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;
using System.Security;

namespace AdaskoTheBeAsT.Interop.Unmanaged;

internal static class NativeMethods
{
    private const string KernelLib = "kernel32";

    [SuppressUnmanagedCodeSecurity]
    [DllImport(KernelLib, CharSet = CharSet.Auto, BestFitMapping = false, SetLastError = true)]
    internal static extern SafeLibraryHandle LoadLibrary(string fileName);

    [SuppressUnmanagedCodeSecurity]
    [DllImport(KernelLib, CharSet = CharSet.Auto, BestFitMapping = false, SetLastError = true)]
    internal static extern SafeLibraryHandle LoadLibraryEx(
        string fileName,
        IntPtr hFile,
        [MarshalAs(UnmanagedType.U4)] LoadLibraryFlags dwFlags);

#if NETSTANDARD2_0
    [ReliabilityContract(Consistency.WillNotCorruptState, Cer.Success)]
#endif
    [SuppressUnmanagedCodeSecurity]
    [DllImport(KernelLib, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool FreeLibrary(IntPtr hModule);

    [SuppressUnmanagedCodeSecurity]
    [DllImport(KernelLib, CharSet = CharSet.Ansi, EntryPoint = nameof(GetProcAddress), ExactSpelling = true, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    internal static extern IntPtr GetProcAddress(SafeLibraryHandle hModule, string procname);
}
