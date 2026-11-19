using System;
using System.Runtime.InteropServices;

namespace AdaskoTheBeAsT.Interop.Unmanaged;

/// <summary>
/// Provides floating-point control-word hygiene and Win32 error-dialog suppression around legacy unmanaged calls.
/// </summary>
/// <remarks>
/// <para>
/// Legacy 32-bit and 64-bit C, C++, Delphi, and Fortran DLLs can unmask x87 or SSE floating-point exceptions
/// (such as divide-by-zero or invalid-operation) or alter rounding/precision modes during <c>DllMain</c> or exported
/// entry points, and may trigger modal OS error dialogs when dependencies or data files are missing.
/// </para>
/// <para>
/// <see cref="EnterScope"/> is strictly thread-scoped: it captures the current thread's floating-point control words
/// and thread error mode, applies thread-level error-mode flags without mutating the process-wide error mode, and on
/// disposal clears pending floating-point exception status bits before restoring the caller's exact rounding,
/// precision, denormal, and exception-mask settings.
/// </para>
/// <para>
/// Process-wide error dialog suppression is performed only when explicitly requested via
/// <see cref="SuppressProcessErrorDialogs"/>.
/// </para>
/// </remarks>
public static class NativeExecutionGuard
{
    /// <summary>
    /// Explicitly suppresses critical-error, fault, and missing-file modal dialogs at both the process level
    /// (<c>SetErrorMode</c>) and the current thread level (<c>SetThreadErrorMode</c>) on Windows.
    /// </summary>
    /// <returns>
    /// The previous process-wide error mode on Windows, or <c>0</c> on non-Windows platforms where this method is a no-op.
    /// </returns>
    public static uint SuppressProcessErrorDialogs()
    {
        if (!NativeLoader.IsWindows())
        {
            return 0;
        }

        const uint suppressFlags = (uint)NativeErrorModes.SuppressErrorDialogs;
        var existingProcessMode = WindowsExecutionNativeMethods.GetErrorMode();
        var previousProcessMode = WindowsExecutionNativeMethods.SetErrorMode(existingProcessMode | suppressFlags);

        var existingThreadMode = WindowsExecutionNativeMethods.GetThreadErrorMode();
        _ = WindowsExecutionNativeMethods.SetThreadErrorMode(existingThreadMode | suppressFlags, out _);
        return previousProcessMode;
    }

    /// <summary>
    /// Clears pending floating-point exception flags and masks all hardware floating-point exceptions on the current
    /// thread on Windows without resetting caller rounding or precision modes. No-op on non-Windows platforms.
    /// </summary>
    public static void ResetFloatingPointState()
    {
        if (!NativeLoader.IsWindows())
        {
            return;
        }

        _ = WindowsExecutionNativeMethods.ClearFloatingPointStatus();
        if (RuntimeInformation.ProcessArchitecture == Architecture.X86)
        {
            WindowsExecutionNativeMethods.WriteX86ControlWords(
                WindowsExecutionNativeMethods.McwExceptionMask,
                WindowsExecutionNativeMethods.McwExceptionMask,
                WindowsExecutionNativeMethods.McwExceptionMask,
                WindowsExecutionNativeMethods.McwExceptionMask);
            return;
        }

        _ = WindowsExecutionNativeMethods.ControlFpSafe(
            out _,
            WindowsExecutionNativeMethods.McwExceptionMask,
            WindowsExecutionNativeMethods.McwExceptionMask);
    }

    /// <summary>
    /// Enters a thread-affine execution scope that preserves the caller's floating-point control modes and thread error mode.
    /// </summary>
    /// <param name="threadErrorMode">
    /// Thread-scoped error-dialog suppression flags to enable on the current thread while the scope is active.
    /// Process-wide error mode is never modified by this method.
    /// </param>
    /// <returns>A disposable <see cref="NativeExecutionScope"/> bound to the calling thread.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="threadErrorMode"/> contains unsupported flag bits outside <see cref="NativeErrorModes.SuppressErrorDialogs"/>.
    /// </exception>
    public static NativeExecutionScope EnterScope(
        NativeErrorModes threadErrorMode = NativeErrorModes.SuppressErrorDialogs)
    {
        if ((threadErrorMode & ~NativeErrorModes.SuppressErrorDialogs) != NativeErrorModes.None)
        {
            throw new ArgumentOutOfRangeException(
                nameof(threadErrorMode),
                threadErrorMode,
                "Only FailCriticalErrors, NoGeneralProtectionFaultErrorBox, and NoOpenFileErrorBox are valid for thread error mode.");
        }

        var ownerThreadId = Environment.CurrentManagedThreadId;
        if (!NativeLoader.IsWindows())
        {
            return new NativeExecutionScope(
                ownerThreadId,
                isWindows: false,
                isX86: false,
                savedPrimaryFpControl: 0,
                savedSecondaryFpControl: 0,
                hasSavedThreadErrorMode: false,
                savedThreadErrorMode: 0);
        }

        var isX86 = RuntimeInformation.ProcessArchitecture == Architecture.X86;
        uint savedPrimary;
        uint savedSecondary;
        if (isX86)
        {
            WindowsExecutionNativeMethods.ReadX86ControlWords(out savedPrimary, out savedSecondary);
        }
        else
        {
            _ = WindowsExecutionNativeMethods.ControlFpSafe(out savedPrimary, 0, 0);
            savedSecondary = 0;
        }

        var currentThreadMode = WindowsExecutionNativeMethods.GetThreadErrorMode();
        var targetThreadMode = currentThreadMode | (uint)threadErrorMode;
        var hasSavedErrorMode =
            WindowsExecutionNativeMethods.SetThreadErrorMode(targetThreadMode, out var previousThreadMode);
        if (!hasSavedErrorMode)
        {
            previousThreadMode = currentThreadMode;
        }

        return new NativeExecutionScope(
            ownerThreadId,
            isWindows: true,
            isX86,
            savedPrimary,
            savedSecondary,
            hasSavedErrorMode,
            previousThreadMode);
    }
}
