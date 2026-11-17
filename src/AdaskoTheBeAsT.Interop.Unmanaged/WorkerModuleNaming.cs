using System;
using System.Globalization;
using System.IO;

namespace AdaskoTheBeAsT.Interop.Unmanaged;

/// <summary>
/// Deterministic fixed-length ASCII filename helpers for per-worker PE import-descriptor rewriting.
/// </summary>
public static class WorkerModuleNaming
{
    private const string Base36Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    /// <summary>
    /// Formats a fixed-length DLL filename by replacing the last two stem characters before <c>.dll</c>
    /// with a zero-padded worker index (<c>00..99</c>), preserving exact ASCII byte length.
    /// </summary>
    /// <param name="originalFileName">Bare ASCII DLL filename (for example, <c>"DFORRT.dll"</c>).</param>
    /// <param name="workerIndex">Zero-based worker slot index in the range <c>0..99</c>.</param>
    /// <returns>A fixed-length ASCII DLL filename (for example, <c>"DFOR00.dll"</c>).</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="originalFileName"/> is null, contains path separators, is not ASCII,
    /// does not end with <c>.dll</c>, or has a stem shorter than 2 characters.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="workerIndex"/> is outside <c>0..99</c>.
    /// </exception>
    public static string FormatFixedLengthName(string originalFileName, int workerIndex)
    {
        if (workerIndex is < 0 or > 99)
        {
            throw new ArgumentOutOfRangeException(
                nameof(workerIndex),
                workerIndex,
                "Worker index must be between 0 and 99 inclusive for 2-digit fixed-length naming.");
        }

        var suffix = workerIndex.ToString("D2", CultureInfo.InvariantCulture);
        return FormatFixedLengthToken(originalFileName, suffix);
    }

    /// <summary>
    /// Formats a fixed-length DLL filename encoding an optional bounded pool namespace, recycle generation,
    /// and worker slot without changing the total ASCII byte length of <paramref name="originalFileName"/>.
    /// </summary>
    /// <param name="originalFileName">Bare ASCII DLL filename (for example, <c>"DFORRT.dll"</c>).</param>
    /// <param name="namespaceId">Optional 1–4 character ASCII alphanumeric namespace tag (<c>[A-Za-z0-9]</c>) distinguishing separate worker pools.</param>
    /// <param name="workerIndex">Zero-based worker index in the range <c>0..99</c>.</param>
    /// <param name="generation">Zero-based worker recycle generation in the range <c>0..1295</c>.</param>
    /// <returns>A deterministic fixed-length ASCII DLL filename.</returns>
    public static string FormatScopedFixedLengthName(
        string originalFileName,
        string? namespaceId,
        int workerIndex,
        int generation = 0)
    {
        ValidateNamespaceId(namespaceId, nameof(namespaceId));
        if (workerIndex is < 0 or > 99)
        {
            throw new ArgumentOutOfRangeException(
                nameof(workerIndex),
                workerIndex,
                "Worker index must be between 0 and 99 inclusive.");
        }

        if (generation is < 0 or > 1295)
        {
            throw new ArgumentOutOfRangeException(
                nameof(generation),
                generation,
                "Generation must be between 0 and 1295 inclusive.");
        }

        if ((namespaceId is null || namespaceId.Length == 0) && generation == 0)
        {
            return FormatFixedLengthName(originalFileName, workerIndex);
        }

        var nsPart = namespaceId ?? string.Empty;
        var workerPart = workerIndex.ToString("D2", CultureInfo.InvariantCulture);
        string token;
#pragma warning disable CC0014
        if (generation == 0)
        {
            token = nsPart + workerPart;
        }
        else if (nsPart.Length == 0)
        {
            token = "_" + FormatBase36Compact(generation) + workerPart;
        }
        else
        {
            token = nsPart + FormatBase36Compact(generation) + workerPart;
        }
#pragma warning restore CC0014

        return FormatFixedLengthToken(originalFileName, token);
    }

    /// <summary>
    /// Formats a fixed-length DLL filename by replacing the trailing characters of the filename stem before <c>.dll</c>
    /// with <paramref name="suffixToken"/>, preserving exact ASCII byte length.
    /// </summary>
    /// <param name="originalFileName">Bare ASCII DLL filename.</param>
    /// <param name="suffixToken">ASCII identifier token (<c>[A-Za-z0-9_-]</c>) to place at the end of the stem.</param>
    /// <returns>A fixed-length ASCII DLL filename of the exact same length as <paramref name="originalFileName"/>.</returns>
    public static string FormatFixedLengthToken(string originalFileName, string suffixToken)
    {
        SplitValidatedDllFileName(originalFileName, nameof(originalFileName), out var stem, out var extension);
        ValidateSuffixToken(suffixToken, nameof(suffixToken));
        if (stem.Length < suffixToken.Length)
        {
            throw new ArgumentException(
                $"DLL stem '{stem}' (length {stem.Length}) is too short to fit suffix token '{suffixToken}' (length {suffixToken.Length}) while preserving fixed length. Supply an explicit target filename or shorter namespace/generation token.",
                nameof(originalFileName));
        }

        var prefixLength = stem.Length - suffixToken.Length;
        return stem.Substring(0, prefixLength) + suffixToken + extension;
    }

    internal static void ValidateNamespaceId(string? namespaceId, string paramName)
    {
        if (namespaceId is null || namespaceId.Length == 0)
        {
            return;
        }

        if (namespaceId.Length > 4)
        {
            throw new ArgumentException("NamespaceId must be between 1 and 4 ASCII alphanumeric characters.", paramName);
        }

        foreach (var ch in namespaceId)
        {
            var isAlphaNum = (ch is >= '0' and <= '9')
                || (ch is >= 'A' and <= 'Z')
                || (ch is >= 'a' and <= 'z');
            if (!isAlphaNum)
            {
                throw new ArgumentException("NamespaceId may contain only ASCII letters and digits ([A-Za-z0-9]).", paramName);
            }
        }
    }

    internal static void SplitValidatedDllFileName(
        string? fileName,
        string paramName,
        out string stem,
        out string extension)
    {
        if (fileName is null)
        {
            throw new ArgumentException("Module name cannot be null or whitespace.", paramName);
        }

        PeNameValidator.ValidateModuleNameArgument(fileName, paramName);
        var ext = Path.GetExtension(fileName);
        if (!string.Equals(ext, ".dll", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Module filename '{fileName}' must end with a '.dll' extension.", paramName);
        }

        stem = fileName.Substring(0, fileName.Length - ext.Length);
        if (stem.Length == 0)
        {
            throw new ArgumentException($"Module filename '{fileName}' must have a non-empty stem before '.dll'.", paramName);
        }

        extension = ext;
    }

    private static void ValidateSuffixToken(string? token, string paramName)
    {
        if (token is null || token.Length == 0)
        {
            throw new ArgumentException("Suffix token cannot be null or empty.", paramName);
        }

        foreach (var ch in token)
        {
            var valid = (ch is >= '0' and <= '9')
                || (ch is >= 'A' and <= 'Z')
                || (ch is >= 'a' and <= 'z')
                || ch is '_' or '-';
            if (!valid)
            {
                throw new ArgumentException("Suffix token may contain only ASCII letters, digits, '_', or '-'.", paramName);
            }
        }
    }

    private static string FormatBase36Compact(int value)
    {
        if (value < 36)
        {
            return Base36Alphabet[value].ToString();
        }

        var high = Base36Alphabet[value / 36];
        var low = Base36Alphabet[value % 36];
        return new string([high, low]);
    }
}
