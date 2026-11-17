using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Security;

namespace AdaskoTheBeAsT.Interop.Unmanaged;

#pragma warning disable CA1060
#pragma warning disable SA1121
#pragma warning disable SYSLIB1054

[ExcludeFromCodeCoverage]
internal static class WindowsMemoryNativeMethods
{
    internal const uint MemCommit = 0x00001000;
    internal const uint PageReadWrite = 0x00000004;
    internal const uint PageExecuteReadWrite = 0x00000040;

    private const string KernelLib = "kernel32";

    [SuppressUnmanagedCodeSecurity]
    [DllImport(KernelLib, ExactSpelling = true, SetLastError = true, EntryPoint = nameof(VirtualProtect))]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool VirtualProtect(
        IntPtr lpAddress,
        UIntPtr dwSize,
        uint flNewProtect,
        out uint lpflOldProtect);

    [SuppressUnmanagedCodeSecurity]
    [DllImport(KernelLib, ExactSpelling = true, SetLastError = true, EntryPoint = nameof(VirtualQuery))]
    internal static extern UIntPtr VirtualQuery(
        IntPtr lpAddress,
        out MemoryBasicInformation lpBuffer,
        UIntPtr dwLength);

    [SuppressUnmanagedCodeSecurity]
    [DllImport(KernelLib, ExactSpelling = true, SetLastError = true, EntryPoint = nameof(FlushInstructionCache))]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool FlushInstructionCache(
        IntPtr hProcess,
        IntPtr lpBaseAddress,
        UIntPtr dwSize);

    [SuppressUnmanagedCodeSecurity]
    [DllImport(KernelLib, ExactSpelling = true, EntryPoint = nameof(GetCurrentProcess))]
    internal static extern IntPtr GetCurrentProcess();

    [StructLayout(LayoutKind.Sequential)]
    internal struct MemoryBasicInformation
    {
        public IntPtr BaseAddress;
        public IntPtr AllocationBase;
        public uint AllocationProtect;
        public IntPtr RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }
}
#pragma warning restore SYSLIB1054
#pragma warning restore CA1060
