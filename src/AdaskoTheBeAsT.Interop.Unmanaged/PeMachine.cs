using System;
using System.Runtime.InteropServices;

namespace AdaskoTheBeAsT.Interop.Unmanaged;

/// <summary>
/// COFF machine identifiers supported by <see cref="PeImageEditor"/>.
/// </summary>
public static class PeMachine
{
    /// <summary>
    /// Intel 386 or later 32-bit x86 (<c>IMAGE_FILE_MACHINE_I386</c>).
    /// </summary>
    public const ushort I386 = 0x014C;

    /// <summary>
    /// x64 / AMD64 (<c>IMAGE_FILE_MACHINE_AMD64</c>).
    /// </summary>
    public const ushort Amd64 = 0x8664;

    /// <summary>
    /// ARM64 little-endian (<c>IMAGE_FILE_MACHINE_ARM64</c>).
    /// </summary>
    public const ushort Arm64 = 0xAA64;

    /// <summary>
    /// Gets the COFF machine identifier matching the current process architecture.
    /// </summary>
    /// <exception cref="PlatformNotSupportedException">
    /// Thrown when the current process architecture does not map to a supported PE machine constant.
    /// </exception>
    public static ushort CurrentProcessArchitecture =>
        RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X86 => I386,
            Architecture.X64 => Amd64,
            Architecture.Arm64 => Arm64,
            _ => throw new PlatformNotSupportedException(
                $"Process architecture '{RuntimeInformation.ProcessArchitecture}' is not supported for PE image validation."),
        };
}
