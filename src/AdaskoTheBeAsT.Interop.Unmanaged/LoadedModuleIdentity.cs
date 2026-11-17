using System;
using System.Globalization;
using System.Runtime.InteropServices;
#if NET10_0_OR_GREATER
using System.Runtime.Versioning;
#endif

namespace AdaskoTheBeAsT.Interop.Unmanaged;

/// <summary>
/// Captures the PE identity header fields (<see cref="Machine"/>, <see cref="TimeDateStamp"/>, and <see cref="SizeOfImage"/>)
/// of a mapped Windows native module for provenance verification before in-memory patching or writable-section clearing.
/// </summary>
#if NET10_0_OR_GREATER
[SupportedOSPlatform("windows")]
#endif
[StructLayout(LayoutKind.Auto)]
public readonly struct LoadedModuleIdentity : IEquatable<LoadedModuleIdentity>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LoadedModuleIdentity"/> struct.
    /// </summary>
    /// <param name="machine">COFF machine constant (see <see cref="PeMachine"/>).</param>
    /// <param name="timeDateStamp">COFF <c>TimeDateStamp</c> value.</param>
    /// <param name="sizeOfImage">PE optional header <c>SizeOfImage</c> value.</param>
    public LoadedModuleIdentity(ushort machine, uint timeDateStamp, uint sizeOfImage)
    {
        Machine = machine;
        TimeDateStamp = timeDateStamp;
        SizeOfImage = sizeOfImage;
    }

    /// <summary>
    /// Gets the COFF machine constant (see <see cref="PeMachine"/>).
    /// </summary>
    public ushort Machine { get; init; }

    /// <summary>
    /// Gets the 32-bit COFF <c>TimeDateStamp</c> of the mapped PE image.
    /// </summary>
    public uint TimeDateStamp { get; init; }

    /// <summary>
    /// Gets the 32-bit <c>SizeOfImage</c> of the mapped PE image.
    /// </summary>
    public uint SizeOfImage { get; init; }

    /// <summary>
    /// Determines whether two <see cref="LoadedModuleIdentity"/> values are equal.
    /// </summary>
    public static bool operator ==(LoadedModuleIdentity left, LoadedModuleIdentity right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Determines whether two <see cref="LoadedModuleIdentity"/> values are not equal.
    /// </summary>
    public static bool operator !=(LoadedModuleIdentity left, LoadedModuleIdentity right)
    {
        return !left.Equals(right);
    }

    /// <inheritdoc />
    public bool Equals(LoadedModuleIdentity other)
    {
        return Machine == other.Machine
            && TimeDateStamp == other.TimeDateStamp
            && SizeOfImage == other.SizeOfImage;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is LoadedModuleIdentity other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = (int)Machine;
            hash = (hash * 397) ^ (int)TimeDateStamp;
            hash = (hash * 397) ^ (int)SizeOfImage;
            return hash;
        }
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "Machine=0x{0:X4}, TimeDateStamp=0x{1:X8}, SizeOfImage=0x{2:X}",
            Machine,
            TimeDateStamp,
            SizeOfImage);
    }
}
