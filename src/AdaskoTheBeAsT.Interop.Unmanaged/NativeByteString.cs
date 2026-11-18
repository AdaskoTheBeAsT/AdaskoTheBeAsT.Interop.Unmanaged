using System;
using System.Globalization;
using System.Text;

namespace AdaskoTheBeAsT.Interop.Unmanaged;

/// <summary>
/// Provides deterministic fixed-buffer byte-string marshaling helpers for C NUL-terminated ASCII/Latin-1 buffers,
/// space-padded Fortran <c>CHARACTER*N</c> buffers, and length-prefixed Pascal/Borland Delphi <c>ShortString</c> buffers.
/// </summary>
public static class NativeByteString
{
    private const int MaxShortStringPayloadBytes = 255;
    private const int MaxShortStringBufferBytes = MaxShortStringPayloadBytes + 1;
    private const byte SpaceByte = 0x20;

    /// <summary>
    /// Encodes <paramref name="value"/> as 7-bit ASCII into <paramref name="destination"/>, writes a trailing NUL terminator,
    /// and zeroes all remaining bytes in <c>destination[offset .. offset + count)</c>.
    /// </summary>
    /// <param name="value">7-bit ASCII string without embedded NUL characters.</param>
    /// <param name="destination">Destination byte array.</param>
    /// <param name="offset">Zero-based start offset in <paramref name="destination"/>.</param>
    /// <param name="count">Fixed buffer byte length (must be at least <c>value.Length + 1</c>).</param>
    /// <returns>The number of non-NUL ASCII character bytes written (<c>value.Length</c>).</returns>
    public static int WriteNullTerminatedAscii(
        string value,
        byte[] destination,
        int offset,
        int count)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(value);
#endif
#if NET472_OR_GREATER
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }
#endif

        ValidateBufferSlice(destination, offset, count, nameof(destination), nameof(count));
        if (count < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(count),
                count,
                "Null-terminated buffer length must be at least 1 byte.");
        }

        ValidateAsciiCharactersNoEmbeddedNull(value);
        if (value.Length > count - 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value.Length,
                $"ASCII string length ({value.Length}) exceeds null-terminated buffer capacity ({(count - 1).ToString(CultureInfo.InvariantCulture)} characters).");
        }

        for (var i = 0; i < value.Length; i++)
        {
            destination[offset + i] = (byte)value[i];
        }

        Array.Clear(destination, offset + value.Length, count - value.Length);
        return value.Length;
    }

#if NET10_0_OR_GREATER
    /// <summary>
    /// Encodes <paramref name="value"/> as 7-bit ASCII into <paramref name="destination"/>, writes a trailing NUL terminator,
    /// and zeroes all remaining bytes in <paramref name="destination"/>.
    /// </summary>
    /// <param name="value">7-bit ASCII character span without embedded NUL characters.</param>
    /// <param name="destination">Fixed destination byte span (must have length at least <c>value.Length + 1</c>).</param>
    /// <returns>The number of non-NUL ASCII character bytes written (<c>value.Length</c>).</returns>
    public static int WriteNullTerminatedAscii(
        ReadOnlySpan<char> value,
        Span<byte> destination)
    {
        if (destination.IsEmpty)
        {
            throw new ArgumentOutOfRangeException(
                nameof(destination),
                0,
                "Null-terminated destination span must have length >= 1.");
        }

        ValidateAsciiCharactersNoEmbeddedNull(value);
        if (value.Length > destination.Length - 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value.Length,
                $"ASCII span length ({value.Length}) exceeds null-terminated destination capacity ({(destination.Length - 1).ToString(CultureInfo.InvariantCulture)} characters).");
        }

        for (var i = 0; i < value.Length; i++)
        {
            destination[i] = (byte)value[i];
        }

        destination.Slice(value.Length).Clear();
        return value.Length;
    }
#endif

    /// <summary>
    /// Decodes a NUL-terminated (or fixed-width when no NUL byte is present) 7-bit ASCII string from <paramref name="source"/>.
    /// </summary>
    /// <param name="source">Source byte array.</param>
    /// <param name="offset">Zero-based start offset in <paramref name="source"/>.</param>
    /// <param name="count">Maximum number of bytes to inspect.</param>
    /// <returns>The decoded ASCII string.</returns>
    public static string ReadNullTerminatedAscii(
        byte[] source,
        int offset,
        int count)
    {
        ValidateBufferSlice(source, offset, count, nameof(source), nameof(count));
        var length = 0;
        while (length < count)
        {
            var b = source[offset + length];
            if (b == 0)
            {
                break;
            }

            if (b > 0x7F)
            {
                throw new DecoderFallbackException(
                    $"Non-ASCII byte 0x{b:X2} encountered at buffer index {length.ToString(CultureInfo.InvariantCulture)}.");
            }

            length++;
        }

        return length == 0 ? string.Empty : Encoding.ASCII.GetString(source, offset, length);
    }

#if NET10_0_OR_GREATER
    /// <summary>
    /// Decodes a NUL-terminated (or fixed-width when no NUL byte is present) 7-bit ASCII string from <paramref name="source"/>.
    /// </summary>
    /// <param name="source">Source byte span.</param>
    /// <returns>The decoded ASCII string.</returns>
    public static string ReadNullTerminatedAscii(ReadOnlySpan<byte> source)
    {
        var length = 0;
        while (length < source.Length)
        {
            var b = source[length];
            if (b == 0)
            {
                break;
            }

            if (b > 0x7F)
            {
                throw new DecoderFallbackException(
                    $"Non-ASCII byte 0x{b:X2} encountered at span index {length.ToString(CultureInfo.InvariantCulture)}.");
            }

            length++;
        }

        return length == 0 ? string.Empty : Encoding.ASCII.GetString(source.Slice(0, length));
    }
#endif

    /// <summary>
    /// Decodes a NUL-terminated (or fixed-width when no NUL byte is present) ISO-8859-1 (Latin-1) string from <paramref name="source"/>.
    /// </summary>
    /// <param name="source">Source byte array.</param>
    /// <param name="offset">Zero-based start offset in <paramref name="source"/>.</param>
    /// <param name="count">Maximum number of bytes to inspect.</param>
    /// <returns>The decoded ISO-8859-1 string.</returns>
    public static string ReadNullTerminatedLatin1(
        byte[] source,
        int offset,
        int count)
    {
        ValidateBufferSlice(source, offset, count, nameof(source), nameof(count));
        var length = 0;
        while (length < count
               && source[offset + length] != 0)
        {
            length++;
        }

        if (length == 0)
        {
            return string.Empty;
        }

        var chars = new char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = (char)source[offset + i];
        }

        return new string(chars);
    }

#if NET10_0_OR_GREATER
    /// <summary>
    /// Decodes a NUL-terminated (or fixed-width when no NUL byte is present) ISO-8859-1 (Latin-1) string from <paramref name="source"/>.
    /// </summary>
    /// <param name="source">Source byte span.</param>
    /// <returns>The decoded ISO-8859-1 string.</returns>
    public static string ReadNullTerminatedLatin1(ReadOnlySpan<byte> source)
    {
        var nulIndex = source.IndexOf((byte)0);
        var slice = nulIndex >= 0 ? source.Slice(0, nulIndex) : source;
        return slice.IsEmpty ? string.Empty : Encoding.Latin1.GetString(slice);
    }
#endif

    /// <summary>
    /// Encodes <paramref name="value"/> as 7-bit ASCII into a fixed-width Fortran <c>CHARACTER*N</c> buffer,
    /// padding any remaining trailing bytes with ASCII spaces (<c>0x20</c>) without writing a NUL byte.
    /// </summary>
    /// <param name="value">7-bit ASCII string without embedded NUL characters.</param>
    /// <param name="destination">Destination byte array.</param>
    /// <param name="offset">Zero-based start offset in <paramref name="destination"/>.</param>
    /// <param name="count">Fixed character buffer width (<c>N</c>).</param>
    public static void WriteSpacePaddedAscii(
        string value,
        byte[] destination,
        int offset,
        int count)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(value);
#endif
#if NET472_OR_GREATER
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }
#endif

        ValidateBufferSlice(destination, offset, count, nameof(destination), nameof(count));
        ValidateAsciiCharactersNoEmbeddedNull(value);
        if (value.Length > count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value.Length,
                $"ASCII string length ({value.Length}) exceeds fixed CHARACTER*{count.ToString(CultureInfo.InvariantCulture)} buffer width.");
        }

        for (var i = 0; i < value.Length; i++)
        {
            destination[offset + i] = (byte)value[i];
        }

        for (var i = value.Length; i < count; i++)
        {
            destination[offset + i] = SpaceByte;
        }
    }

#if NET10_0_OR_GREATER
    /// <summary>
    /// Encodes <paramref name="value"/> as 7-bit ASCII into a fixed-width Fortran <c>CHARACTER*N</c> span,
    /// padding trailing bytes with ASCII spaces (<c>0x20</c>) without writing a NUL terminator.
    /// </summary>
    /// <param name="value">7-bit ASCII character span without embedded NUL characters.</param>
    /// <param name="destination">Fixed-width destination byte span.</param>
    public static void WriteSpacePaddedAscii(
        ReadOnlySpan<char> value,
        Span<byte> destination)
    {
        ValidateAsciiCharactersNoEmbeddedNull(value);
        if (value.Length > destination.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value.Length,
                $"ASCII span length ({value.Length}) exceeds fixed CHARACTER*{destination.Length} span width.");
        }

        for (var i = 0; i < value.Length; i++)
        {
            destination[i] = (byte)value[i];
        }

        destination.Slice(value.Length).Fill(SpaceByte);
    }
#endif

    /// <summary>
    /// Decodes a fixed-width Fortran <c>CHARACTER*N</c> 7-bit ASCII buffer, trimming trailing ASCII spaces (<c>0x20</c>)
    /// (and stopping at any earlier NUL byte).
    /// </summary>
    /// <param name="source">Source byte array.</param>
    /// <param name="offset">Zero-based start offset in <paramref name="source"/>.</param>
    /// <param name="count">Fixed character buffer width (<c>N</c>).</param>
    /// <returns>The trimmed ASCII string.</returns>
    public static string ReadSpacePaddedAscii(
        byte[] source,
        int offset,
        int count)
    {
        ValidateBufferSlice(source, offset, count, nameof(source), nameof(count));
        var effectiveEnd = 0;
        while (effectiveEnd < count)
        {
            var b = source[offset + effectiveEnd];
            if (b == 0)
            {
                break;
            }

            if (b > 0x7F)
            {
                throw new DecoderFallbackException(
                    $"Non-ASCII byte 0x{b:X2} encountered at Fortran buffer index {effectiveEnd.ToString(CultureInfo.InvariantCulture)}.");
            }

            effectiveEnd++;
        }

        while (effectiveEnd > 0
               && source[offset + effectiveEnd - 1] == SpaceByte)
        {
            effectiveEnd--;
        }

        return effectiveEnd == 0 ? string.Empty : Encoding.ASCII.GetString(source, offset, effectiveEnd);
    }

#if NET10_0_OR_GREATER
    /// <summary>
    /// Decodes a fixed-width Fortran <c>CHARACTER*N</c> 7-bit ASCII span, trimming trailing ASCII spaces (<c>0x20</c>).
    /// </summary>
    /// <param name="source">Source byte span.</param>
    /// <returns>The trimmed ASCII string.</returns>
    public static string ReadSpacePaddedAscii(ReadOnlySpan<byte> source)
    {
        var effectiveEnd = 0;
        while (effectiveEnd < source.Length)
        {
            var b = source[effectiveEnd];
            if (b == 0)
            {
                break;
            }

            if (b > 0x7F)
            {
                throw new DecoderFallbackException(
                    $"Non-ASCII byte 0x{b:X2} encountered at Fortran span index {effectiveEnd.ToString(CultureInfo.InvariantCulture)}.");
            }

            effectiveEnd++;
        }

        while (effectiveEnd > 0
               && source[effectiveEnd - 1] == SpaceByte)
        {
            effectiveEnd--;
        }

        return effectiveEnd == 0 ? string.Empty : Encoding.ASCII.GetString(source.Slice(0, effectiveEnd));
    }
#endif

    /// <summary>
    /// Encodes <paramref name="value"/> as a length-prefixed Pascal / Borland Delphi <c>ShortString</c>
    /// (<c>destination[offset] = length</c>, followed by ASCII payload and zeroed trailing bytes).
    /// </summary>
    /// <param name="value">7-bit ASCII string without embedded NUL characters (<c>0..255</c> bytes).</param>
    /// <param name="destination">Destination byte array.</param>
    /// <param name="offset">Zero-based start offset in <paramref name="destination"/>.</param>
    /// <param name="capacity">Total <c>ShortString</c> slot byte length including the leading length byte (<c>1..256</c>).</param>
    public static void WriteShortStringAscii(
        string value,
        byte[] destination,
        int offset,
        int capacity)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(value);
#endif
#if NET472_OR_GREATER
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }
#endif

        ValidateBufferSlice(destination, offset, capacity, nameof(destination), nameof(capacity));
        if (capacity is < 1 or > MaxShortStringBufferBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacity),
                capacity,
                $"ShortString buffer capacity must be between 1 and {MaxShortStringBufferBytes} bytes (including length byte).");
        }

        ValidateAsciiCharactersNoEmbeddedNull(value);
        var maxChars = Math.Min(MaxShortStringPayloadBytes, capacity - 1);
        if (value.Length > maxChars)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value.Length,
                $"ASCII string length ({value.Length}) exceeds ShortString payload capacity ({maxChars.ToString(CultureInfo.InvariantCulture)} characters).");
        }

        destination[offset] = (byte)value.Length;
        for (var i = 0; i < value.Length; i++)
        {
            destination[offset + 1 + i] = (byte)value[i];
        }

        var trailingStart = offset + 1 + value.Length;
        var trailingCount = capacity - 1 - value.Length;
        if (trailingCount > 0)
        {
            Array.Clear(destination, trailingStart, trailingCount);
        }
    }

#if NET10_0_OR_GREATER
    /// <summary>
    /// Encodes <paramref name="value"/> as a length-prefixed Pascal / Borland Delphi <c>ShortString</c> into <paramref name="destination"/>,
    /// zeroing unused trailing bytes.
    /// </summary>
    /// <param name="value">7-bit ASCII character span without embedded NUL characters (<c>0..255</c> chars).</param>
    /// <param name="destination">Total <c>ShortString</c> slot byte span (<c>1..256</c> bytes including leading length byte).</param>
    public static void WriteShortStringAscii(
        ReadOnlySpan<char> value,
        Span<byte> destination)
    {
        if (destination.IsEmpty
            || destination.Length > MaxShortStringBufferBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(destination),
                destination.Length,
                $"ShortString destination span length must be between 1 and {MaxShortStringBufferBytes} bytes.");
        }

        ValidateAsciiCharactersNoEmbeddedNull(value);
        var maxChars = Math.Min(MaxShortStringPayloadBytes, destination.Length - 1);
        if (value.Length > maxChars)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value.Length,
                $"ASCII span length ({value.Length}) exceeds ShortString payload capacity ({maxChars.ToString(CultureInfo.InvariantCulture)} characters).");
        }

        destination[0] = (byte)value.Length;
        for (var i = 0; i < value.Length; i++)
        {
            destination[1 + i] = (byte)value[i];
        }

        destination.Slice(1 + value.Length).Clear();
    }
#endif

    /// <summary>
    /// Decodes a length-prefixed Pascal / Borland Delphi <c>ShortString</c> from <paramref name="source"/>.
    /// </summary>
    /// <param name="source">Source byte array.</param>
    /// <param name="offset">Zero-based start offset of the leading length byte in <paramref name="source"/>.</param>
    /// <param name="maxLength">Available byte slice length starting at <paramref name="offset"/> (must be at least <c>1 + source[offset]</c>).</param>
    /// <returns>The decoded 7-bit ASCII string.</returns>
    public static string ReadShortStringAscii(
        byte[] source,
        int offset,
        int maxLength)
    {
        ValidateBufferSlice(source, offset, maxLength, nameof(source), nameof(maxLength));
        if (maxLength < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxLength),
                maxLength,
                "ShortString slice length must be at least 1 byte.");
        }

        var payloadLength = source[offset];
        if (payloadLength > maxLength - 1)
        {
            throw new ArgumentException(
                $"ShortString length prefix ({payloadLength}) exceeds available buffer payload capacity ({(maxLength - 1).ToString(CultureInfo.InvariantCulture)} bytes).",
                nameof(source));
        }

        for (var i = 1; i <= payloadLength; i++)
        {
            var b = source[offset + i];
            if (b is 0 or > 0x7F)
            {
                throw new DecoderFallbackException(
                    $"Invalid ShortString ASCII byte 0x{b:X2} at payload index {(i - 1).ToString(CultureInfo.InvariantCulture)}.");
            }
        }

        return payloadLength == 0 ? string.Empty : Encoding.ASCII.GetString(source, offset + 1, payloadLength);
    }

#if NET10_0_OR_GREATER
    /// <summary>
    /// Decodes a length-prefixed Pascal / Borland Delphi <c>ShortString</c> from <paramref name="source"/>.
    /// </summary>
    /// <param name="source">Source byte span starting at the leading length byte.</param>
    /// <returns>The decoded 7-bit ASCII string.</returns>
    public static string ReadShortStringAscii(ReadOnlySpan<byte> source)
    {
        if (source.IsEmpty)
        {
            throw new ArgumentOutOfRangeException(nameof(source), 0, "ShortString source span cannot be empty.");
        }

        var payloadLength = source[0];
        if (payloadLength > source.Length - 1)
        {
            throw new ArgumentException(
                $"ShortString length prefix ({payloadLength}) exceeds available span payload capacity ({(source.Length - 1).ToString(CultureInfo.InvariantCulture)} bytes).",
                nameof(source));
        }

        for (var i = 1; i <= payloadLength; i++)
        {
            var b = source[i];
            if (b is 0 or > 0x7F)
            {
                throw new DecoderFallbackException(
                    $"Invalid ShortString ASCII byte 0x{b:X2} at payload index {(i - 1).ToString(CultureInfo.InvariantCulture)}.");
            }
        }

        return payloadLength == 0 ? string.Empty : Encoding.ASCII.GetString(source.Slice(1, payloadLength));
    }
#endif

    private static void ValidateBufferSlice(
        byte[] buffer,
        int offset,
        int length,
        string bufferParam,
        string lengthParam)
    {
        if (buffer is null)
        {
            throw new ArgumentNullException(bufferParam);
        }

        if (offset < 0
            || offset > buffer.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), offset, "Buffer offset is out of range.");
        }

        if (length < 0
            || offset > buffer.Length - length)
        {
            throw new ArgumentOutOfRangeException(lengthParam, length, "Buffer slice length exceeds array bounds.");
        }
    }

    private static void ValidateAsciiCharactersNoEmbeddedNull(string value)
    {
        foreach (var ch in value)
        {
            if (ch == '\0')
            {
                throw new ArgumentException("String value cannot contain embedded NUL characters.", nameof(value));
            }

            if (ch > 0x7F)
            {
                throw new ArgumentException("String value must contain only 7-bit ASCII characters.", nameof(value));
            }
        }
    }

#if NET10_0_OR_GREATER
    private static void ValidateAsciiCharactersNoEmbeddedNull(ReadOnlySpan<char> value)
    {
        foreach (var ch in value)
        {
            if (ch == '\0')
            {
                throw new ArgumentException("String value cannot contain embedded NUL characters.", nameof(value));
            }

            if (ch > 0x7F)
            {
                throw new ArgumentException("String value must contain only 7-bit ASCII characters.", nameof(value));
            }
        }
    }
#endif
}
