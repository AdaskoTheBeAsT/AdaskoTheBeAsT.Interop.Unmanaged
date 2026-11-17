using System;
using System.Text;

namespace AdaskoTheBeAsT.Interop.Unmanaged;

/// <summary>
/// Describes an explicit, provenance-verified byte or string-literal patch at a known relative virtual address (RVA).
/// </summary>
public sealed class PeVerifiedBytePatch
{
    private readonly byte[] _expectedOriginal;
    private readonly byte[] _replacement;

    /// <summary>
    /// Initializes a new instance of the <see cref="PeVerifiedBytePatch"/> class.
    /// </summary>
    /// <param name="rva">Target relative virtual address in a file-backed PE section.</param>
    /// <param name="expectedOriginal">Expected original byte sequence at <paramref name="rva"/>.</param>
    /// <param name="replacement">Replacement byte sequence of the exact same length.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="rva"/> is zero or when <paramref name="replacement"/> length differs from <paramref name="expectedOriginal"/>.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="expectedOriginal"/> or <paramref name="replacement"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="expectedOriginal"/> is empty.
    /// </exception>
    public PeVerifiedBytePatch(uint rva, byte[] expectedOriginal, byte[] replacement)
    {
        if (rva == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rva), rva, "RVA must be non-zero.");
        }

        if (expectedOriginal is null)
        {
            throw new ArgumentNullException(nameof(expectedOriginal));
        }

        if (replacement is null)
        {
            throw new ArgumentNullException(nameof(replacement));
        }

        if (expectedOriginal.Length == 0)
        {
            throw new ArgumentException("Expected byte sequence cannot be empty.", nameof(expectedOriginal));
        }

        if (replacement.Length != expectedOriginal.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(replacement),
                replacement.Length,
                $"Replacement byte length ({replacement.Length}) must equal expected original length ({expectedOriginal.Length}).");
        }

        Rva = rva;
        _expectedOriginal = (byte[])expectedOriginal.Clone();
        _replacement = (byte[])replacement.Clone();
    }

    /// <summary>
    /// Gets the target relative virtual address in the PE image.
    /// </summary>
    public uint Rva { get; }

    internal byte[] ExpectedOriginalBuffer => _expectedOriginal;

    internal byte[] ReplacementBuffer => _replacement;

    /// <summary>
    /// Creates a verified ASCII string-literal patch (for example, a verified <c>LoadLibraryA</c> operand) with zero-filled trailing bytes.
    /// </summary>
    /// <param name="rva">Target relative virtual address of the literal in the PE image.</param>
    /// <param name="expectedOriginal">Expected original printable ASCII literal.</param>
    /// <param name="replacement">Same-or-shorter printable ASCII replacement literal.</param>
    /// <param name="includeNullTerminator">
    /// When <see langword="true"/>, verifies the trailing NUL byte in <paramref name="expectedOriginal"/> and zero-fills through it.
    /// </param>
    /// <returns>A configured <see cref="PeVerifiedBytePatch"/> instance.</returns>
    public static PeVerifiedBytePatch FromAsciiLiteral(
        uint rva,
        string expectedOriginal,
        string replacement,
        bool includeNullTerminator = true)
    {
        PeNameValidator.ValidateAsciiLiteralArgument(expectedOriginal, nameof(expectedOriginal));
        PeNameValidator.ValidateAsciiLiteralArgument(replacement, nameof(replacement));
        if (replacement.Length > expectedOriginal.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(replacement),
                replacement.Length,
                $"Replacement literal '{replacement}' cannot exceed original literal '{expectedOriginal}' length ({expectedOriginal.Length}).");
        }

        var slotLength = expectedOriginal.Length + (includeNullTerminator ? 1 : 0);
        var expectedBytes = new byte[slotLength];
        var replacementBytes = new byte[slotLength];
        Encoding.ASCII.GetBytes(expectedOriginal, 0, expectedOriginal.Length, expectedBytes, 0);
        Encoding.ASCII.GetBytes(replacement, 0, replacement.Length, replacementBytes, 0);
        return new PeVerifiedBytePatch(rva, expectedBytes, replacementBytes);
    }

    /// <summary>
    /// Creates a verified UTF-16LE string-literal patch (for example, a verified <c>LoadLibraryW</c> operand) with zero-filled trailing code units.
    /// </summary>
    /// <param name="rva">Target relative virtual address of the UTF-16LE literal in the PE image.</param>
    /// <param name="expectedOriginal">Expected original UTF-16 literal without embedded NUL characters.</param>
    /// <param name="replacement">Same-or-shorter UTF-16 replacement literal without embedded NUL characters.</param>
    /// <param name="includeNullTerminator">
    /// When <see langword="true"/>, verifies the trailing 16-bit NUL terminator in <paramref name="expectedOriginal"/> and zero-fills through it.
    /// </param>
    /// <returns>A configured <see cref="PeVerifiedBytePatch"/> instance.</returns>
    public static PeVerifiedBytePatch FromUtf16Literal(
        uint rva,
        string expectedOriginal,
        string replacement,
        bool includeNullTerminator = true)
    {
        PeNameValidator.ValidateUtf16LiteralArgument(expectedOriginal, nameof(expectedOriginal));
        PeNameValidator.ValidateUtf16LiteralArgument(replacement, nameof(replacement));
        if (replacement.Length > expectedOriginal.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(replacement),
                replacement.Length,
                $"Replacement literal '{replacement}' cannot exceed original literal '{expectedOriginal}' length ({expectedOriginal.Length}).");
        }

        var slotByteLength = checked((expectedOriginal.Length + (includeNullTerminator ? 1 : 0)) * 2);
        var expectedBytes = new byte[slotByteLength];
        var replacementBytes = new byte[slotByteLength];
        Encoding.Unicode.GetBytes(expectedOriginal, 0, expectedOriginal.Length, expectedBytes, 0);
        Encoding.Unicode.GetBytes(replacement, 0, replacement.Length, replacementBytes, 0);
        return new PeVerifiedBytePatch(rva, expectedBytes, replacementBytes);
    }

    /// <summary>
    /// Returns a copy of the expected original byte sequence.
    /// </summary>
    /// <returns>A copy of the expected original bytes.</returns>
    public byte[] GetExpectedOriginal()
    {
        return (byte[])_expectedOriginal.Clone();
    }

    /// <summary>
    /// Returns a copy of the replacement byte sequence.
    /// </summary>
    /// <returns>A copy of the replacement bytes.</returns>
    public byte[] GetReplacement()
    {
        return (byte[])_replacement.Clone();
    }
}
