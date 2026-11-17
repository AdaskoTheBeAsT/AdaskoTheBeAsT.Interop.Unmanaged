using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Security;

namespace AdaskoTheBeAsT.Interop.Unmanaged;

#pragma warning disable CA1060
#pragma warning disable SA1121
#pragma warning disable SYSLIB1054

[ExcludeFromCodeCoverage]
internal static class WindowsExecutionNativeMethods
{
    internal const uint McwExceptionMask = 0x0008001F;
    internal const uint McwRoundingMask = 0x00000300;
    internal const uint McwPrecisionMask = 0x00030000;
    internal const uint McwInfinityMask = 0x00040000;
    internal const uint McwDenormalMask = 0x03000000;

    internal const uint X87ControlPreserveMask =
        McwExceptionMask | McwRoundingMask | McwPrecisionMask | McwInfinityMask;

    internal const uint Sse2ControlPreserveMask =
        McwExceptionMask | McwRoundingMask | McwDenormalMask;

    internal const uint BasicControlPreserveMask =
        McwExceptionMask | McwRoundingMask;

    private const string KernelLib = "kernel32";
    private const string UcrtLib = "ucrtbase";
    private const string MsvcrtLib = "msvcrt";

    [SuppressUnmanagedCodeSecurity]
    [DllImport(KernelLib, ExactSpelling = true, EntryPoint = nameof(GetErrorMode))]
    internal static extern uint GetErrorMode();

    [SuppressUnmanagedCodeSecurity]
    [DllImport(KernelLib, ExactSpelling = true, EntryPoint = nameof(SetErrorMode))]
    internal static extern uint SetErrorMode(uint uMode);

    [SuppressUnmanagedCodeSecurity]
    [DllImport(KernelLib, ExactSpelling = true, EntryPoint = nameof(GetThreadErrorMode))]
    internal static extern uint GetThreadErrorMode();

    [SuppressUnmanagedCodeSecurity]
    [DllImport(KernelLib, ExactSpelling = true, SetLastError = true, EntryPoint = nameof(SetThreadErrorMode))]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetThreadErrorMode(uint dwNewMode, out uint lpOldMode);

    internal static uint ClearFloatingPointStatus()
    {
        try
        {
            return UcrtClearFp();
        }
        catch (DllNotFoundException)
        {
            return MsvcrtClearFp();
        }
        catch (EntryPointNotFoundException)
        {
            return MsvcrtClearFp();
        }
    }

    internal static int ControlFpSafe(out uint currentState, uint newControl, uint mask)
    {
        try
        {
            return UcrtControlFpS(out currentState, newControl, mask);
        }
        catch (DllNotFoundException)
        {
            return MsvcrtControlFpS(out currentState, newControl, mask);
        }
        catch (EntryPointNotFoundException)
        {
            return MsvcrtControlFpS(out currentState, newControl, mask);
        }
    }

    internal static void ReadX86ControlWords(out uint x87Cw, out uint sse2Cw)
    {
        try
        {
            _ = UcrtControl87TwoOut(0, 0, out x87Cw, out sse2Cw);
        }
        catch (DllNotFoundException)
        {
            _ = MsvcrtControl87TwoOut(0, 0, out x87Cw, out sse2Cw);
        }
        catch (EntryPointNotFoundException)
        {
            _ = MsvcrtControl87TwoOut(0, 0, out x87Cw, out sse2Cw);
        }
    }

    internal static void WriteX86ControlWords(uint x87Value, uint x87Mask, uint sse2Value, uint sse2Mask)
    {
        try
        {
            _ = UcrtControl87X87Only(x87Value, x87Mask, out _, IntPtr.Zero);
            _ = UcrtControl87Sse2Only(sse2Value, sse2Mask, IntPtr.Zero, out _);
        }
        catch (DllNotFoundException)
        {
            _ = MsvcrtControl87X87Only(x87Value, x87Mask, out _, IntPtr.Zero);
            _ = MsvcrtControl87Sse2Only(sse2Value, sse2Mask, IntPtr.Zero, out _);
        }
        catch (EntryPointNotFoundException)
        {
            _ = MsvcrtControl87X87Only(x87Value, x87Mask, out _, IntPtr.Zero);
            _ = MsvcrtControl87Sse2Only(sse2Value, sse2Mask, IntPtr.Zero, out _);
        }
    }

    [SuppressUnmanagedCodeSecurity]
    [DllImport(UcrtLib, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true, EntryPoint = "_clearfp")]
    private static extern uint UcrtClearFp();

    [SuppressUnmanagedCodeSecurity]
    [DllImport(MsvcrtLib, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true, EntryPoint = "_clearfp")]
    private static extern uint MsvcrtClearFp();

    [SuppressUnmanagedCodeSecurity]
    [DllImport(UcrtLib, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true, EntryPoint = "_controlfp_s")]
    private static extern int UcrtControlFpS(out uint currentState, uint newControl, uint mask);

    [SuppressUnmanagedCodeSecurity]
    [DllImport(MsvcrtLib, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true, EntryPoint = "_controlfp_s")]
    private static extern int MsvcrtControlFpS(out uint currentState, uint newControl, uint mask);

    [SuppressUnmanagedCodeSecurity]
    [DllImport(UcrtLib, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true, EntryPoint = "__control87_2")]
    private static extern int UcrtControl87TwoOut(uint newValue, uint mask, out uint x86Cw, out uint sse2Cw);

    [SuppressUnmanagedCodeSecurity]
    [DllImport(MsvcrtLib, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true, EntryPoint = "__control87_2")]
    private static extern int MsvcrtControl87TwoOut(uint newValue, uint mask, out uint x86Cw, out uint sse2Cw);

    [SuppressUnmanagedCodeSecurity]
    [DllImport(UcrtLib, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true, EntryPoint = "__control87_2")]
    private static extern int UcrtControl87X87Only(uint newValue, uint mask, out uint x86Cw, IntPtr sse2CwNull);

    [SuppressUnmanagedCodeSecurity]
    [DllImport(MsvcrtLib, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true, EntryPoint = "__control87_2")]
    private static extern int MsvcrtControl87X87Only(uint newValue, uint mask, out uint x86Cw, IntPtr sse2CwNull);

    [SuppressUnmanagedCodeSecurity]
    [DllImport(UcrtLib, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true, EntryPoint = "__control87_2")]
    private static extern int UcrtControl87Sse2Only(uint newValue, uint mask, IntPtr x86CwNull, out uint sse2Cw);

    [SuppressUnmanagedCodeSecurity]
    [DllImport(MsvcrtLib, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true, EntryPoint = "__control87_2")]
    private static extern int MsvcrtControl87Sse2Only(uint newValue, uint mask, IntPtr x86CwNull, out uint sse2Cw);
}
#pragma warning restore SYSLIB1054
#pragma warning restore CA1060
