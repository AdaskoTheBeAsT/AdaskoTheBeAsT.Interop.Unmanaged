using System;

namespace AdaskoTheBeAsT.Interop.Unmanaged;

/// <summary>
/// Specifies Windows error-dialog suppression flags valid for both thread-scoped (<c>SetThreadErrorMode</c>)
/// and process-wide (<c>SetErrorMode</c>) error handling.
/// </summary>
[Flags]
public enum NativeErrorMode : uint
{
    /// <summary>
    /// No additional error-mode flags.
    /// </summary>
    None = 0x0000,

    /// <summary>
    /// The system does not display the critical-error-handler message box (<c>SEM_FAILCRITICALERRORS</c>).
    /// </summary>
    FailCriticalErrors = 0x0001,

    /// <summary>
    /// The system does not display the Windows Error Reporting fault dialog (<c>SEM_NOGPFAULTERRORBOX</c>).
    /// </summary>
    NoGeneralProtectionFaultErrorBox = 0x0002,

    /// <summary>
    /// The <c>OpenFile</c> function does not display a message box when it fails to find a file (<c>SEM_NOOPENFILEERRORBOX</c>).
    /// </summary>
    NoOpenFileErrorBox = 0x8000,

    /// <summary>
    /// Suppresses all critical-error, fault, and missing-file modal error dialogs (<c>0x8003</c>).
    /// </summary>
    SuppressErrorDialogs = FailCriticalErrors | NoGeneralProtectionFaultErrorBox | NoOpenFileErrorBox,
}
