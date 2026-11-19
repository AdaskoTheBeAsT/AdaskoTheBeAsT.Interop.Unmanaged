using System;

namespace AdaskoTheBeAsT.Interop.Unmanaged;

internal static class PeNameValidator
{
    public static void ValidateModuleNameArgument(
        string? value,
        string paramName)
    {
        if (value is null
            || string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Module name cannot be null or whitespace.", paramName);
        }

        foreach (var ch in value)
        {
            if (ch < 0x20
                || ch > 0x7E)
            {
                throw new ArgumentException("Module name must contain only printable ASCII characters.", paramName);
            }

            if (ch is '/' or '\\' or ':')
            {
                throw new ArgumentException("Module name must not contain path or drive separators.", paramName);
            }
        }

        if (string.Equals(value, ".", StringComparison.Ordinal)
            || string.Equals(value, "..", StringComparison.Ordinal))
        {
            throw new ArgumentException("Module name cannot be a relative directory segment.", paramName);
        }
    }

    public static void ValidateSymbolNameArgument(
        string? value,
        string paramName)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new ArgumentException("Symbol name cannot be null or empty.", paramName);
        }

#pragma warning disable CS8604
        if (HasCharOutsideAsciiRange(value, minInclusive: (char)0x21, maxInclusive: (char)0x7E))
        {
            throw new ArgumentException(
                "Symbol name must contain only non-whitespace printable ASCII characters.",
                paramName);
        }
#pragma warning restore CS8604
    }

    public static void ValidateAsciiLiteralArgument(
        string? value,
        string paramName)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new ArgumentException("ASCII literal cannot be null or empty.", paramName);
        }

#pragma warning disable CS8604
        if (HasCharOutsideAsciiRange(value, minInclusive: (char)0x20, maxInclusive: (char)0x7E))
        {
            throw new ArgumentException(
                "Literal must contain only printable ASCII characters without embedded NULs.",
                paramName);
        }
#pragma warning restore CS8604
    }

    public static void ValidateUtf16LiteralArgument(
        string? value,
        string paramName)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new ArgumentException("UTF-16 literal cannot be null or empty.", paramName);
        }

        var index = 0;
#pragma warning disable CS8602,CS8604,RCS1239
        while (index < value.Length)
        {
            var ch = value[index];
            if (ch == '\0'
                || char.IsSurrogate(ch))
            {
                throw new ArgumentException(
                    "Literal must not contain embedded NULs or unpaired surrogates.",
                    paramName);
            }

            index++;
        }
#pragma warning restore CS8602,CS8604,RCS1239
    }

    private static bool HasCharOutsideAsciiRange(
        string value,
        char minInclusive,
        char maxInclusive)
    {
        var index = 0;
#pragma warning disable RCS1239 // Use 'for' statement instead of 'while' statement
        while (index < value.Length)
        {
            var ch = value[index];
            if (ch < minInclusive
                || ch > maxInclusive)
            {
                return true;
            }

            index++;
        }
#pragma warning restore RCS1239 // Use 'for' statement instead of 'while' statement

        return false;
    }
}
