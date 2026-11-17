using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace AdaskoTheBeAsT.Interop.Unmanaged;

#pragma warning disable MA0012 // BadImageFormatException is the documented public exception contract for PE validation and verification mismatches.

/// <summary>
/// Deterministic, provenance-verified editor for unmanaged 32-bit (<c>PE32</c>) and 64-bit (<c>PE32+</c>) DLL images.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="PeImageEditor"/> reads source bytes into a private in-memory buffer, validates bounded COFF/PE headers,
/// section-backed relative virtual address (RVA) ranges, ordinary import tables, and delay-import tables before mutating
/// any byte, and refuses to overwrite its original source file on disk.
/// </para>
/// <para>
/// Renaming an ordinary import descriptor (<see cref="RewriteImportModuleName"/>) affects only
/// <c>IMAGE_IMPORT_DESCRIPTOR.Name</c>; it does not implicitly rewrite delay-load import descriptors
/// (<see cref="RewriteDelayImportModuleName"/>) or dynamic <c>LoadLibrary</c> string literals
/// (<see cref="ReplaceVerifiedBytesAtRva(uint, byte[], byte[])"/>).
/// </para>
/// </remarks>
public sealed class PeImageEditor
{
    private const ushort ImageFileLargeAddressAwareFlag = 0x0020;
    private const ushort ImageDllCharacteristicsForceIntegrity = 0x0080;
    private const int ExportDirectorySize = 40;
    private const int ImportDescriptorSize = 20;
    private const int DelayImportDescriptorSize = 32;
    private const uint DelayLoadRvaAttributeFlag = 0x00000001;

    private readonly string _imageLabel;
    private readonly string? _sourceFullPath;
    private readonly PeHeaderInfo _headers;
    private byte[] _buffer;
    private bool _hasUnreconciledModification;

    private PeImageEditor(
        byte[] buffer,
        string imageLabel,
        string? sourceFullPath,
        PeHeaderInfo headers)
    {
        _buffer = buffer;
        _imageLabel = imageLabel;
        _sourceFullPath = sourceFullPath;
        _headers = headers;
    }

    /// <summary>
    /// Gets the COFF <c>Machine</c> identifier of the PE image (see <see cref="PeMachine"/>).
    /// </summary>
    public ushort Machine => _headers.Machine;

    /// <summary>
    /// Gets the current COFF <c>Characteristics</c> bitmask of the PE image.
    /// </summary>
    public ushort Characteristics => PeHeaderParser.ReadUInt16(_buffer, _headers.CharacteristicsFileOffset);

    /// <summary>
    /// Gets the COFF <c>TimeDateStamp</c> recorded in the PE file header.
    /// </summary>
    public uint TimeDateStamp => PeHeaderParser.ReadUInt32(_buffer, _headers.TimeDateStampFileOffset);

    /// <summary>
    /// Gets a value indicating whether the image uses the 64-bit <c>PE32+</c> optional header format.
    /// </summary>
    public bool IsPe32Plus => _headers.IsPe32Plus;

    /// <summary>
    /// Gets the <c>SizeOfImage</c> value recorded in the PE optional header.
    /// </summary>
    public uint SizeOfImage => _headers.SizeOfImage;

    /// <summary>
    /// Gets the current 32-bit <c>CheckSum</c> value in the PE optional header.
    /// </summary>
    public uint CheckSum => PeHeaderParser.ReadUInt32(_buffer, _headers.CheckSumFileOffset);

    /// <summary>
    /// Gets a value indicating whether the image currently carries an Authenticode certificate table entry.
    /// </summary>
    public bool HasAuthenticodeSignature =>
        _headers.SecurityTableFileOffset != 0 || _headers.SecurityTableSize != 0;

    /// <summary>
    /// Loads a PE32/PE32+ image from disk into a private in-memory copy and validates its headers and import tables.
    /// </summary>
    /// <param name="sourcePath">Path to the source PE image file.</param>
    /// <param name="validation">Optional validation constraints.</param>
    /// <returns>A validated <see cref="PeImageEditor"/> holding a private copy of the file bytes.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="sourcePath"/> is null, whitespace, or contains NUL characters.
    /// </exception>
    /// <exception cref="BadImageFormatException">
    /// Thrown when headers, sections, or import/delay-import tables fail structural or provenance validation.
    /// </exception>
    [SuppressMessage("Security", "SCS0018", Justification = "Reads caller-specified PE binary path into an isolated memory buffer after path normalization.")]
    [SuppressMessage("Security", "SEC0116", Justification = "Intentional library API for reading caller-specified native PE image files.")]
    public static PeImageEditor FromFile(
        string sourcePath,
        PeImageValidationOptions? validation = null)
    {
        ValidatePathArgument(sourcePath, nameof(sourcePath));
        var fullPath = Path.GetFullPath(sourcePath);
        var bytes = File.ReadAllBytes(fullPath);
        return CreateValidated(bytes, Path.GetFileName(fullPath), fullPath, validation);
    }

    /// <summary>
    /// Creates a validated <see cref="PeImageEditor"/> over a cloned copy of <paramref name="imageBytes"/>.
    /// </summary>
    /// <param name="imageBytes">Raw PE image bytes.</param>
    /// <param name="imageName">Optional diagnostic label used in <see cref="BadImageFormatException"/> messages.</param>
    /// <param name="validation">Optional validation constraints.</param>
    /// <returns>A validated <see cref="PeImageEditor"/> holding a private copy of <paramref name="imageBytes"/>.</returns>
    public static PeImageEditor FromBytes(
        byte[] imageBytes,
        string? imageName = null,
        PeImageValidationOptions? validation = null)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(imageBytes);
#endif
#if NET472_OR_GREATER
        if (imageBytes is null)
        {
            throw new ArgumentNullException(nameof(imageBytes));
        }
#endif

        var label = imageName is null || string.IsNullOrWhiteSpace(imageName) ? "<memory>" : imageName;
        return CreateValidated((byte[])imageBytes.Clone(), label, sourceFullPath: null, validation);
    }

    /// <summary>
    /// Translates a relative virtual address (RVA) inside a file-backed PE section to its raw file byte offset.
    /// </summary>
    /// <param name="rva">Relative virtual address to translate.</param>
    /// <returns>Zero-based file offset corresponding to <paramref name="rva"/>.</returns>
    /// <exception cref="BadImageFormatException">
    /// Thrown when <paramref name="rva"/> does not map uniquely to a file-backed section byte.
    /// </exception>
    public int RvaToFileOffset(uint rva)
    {
        return MapRvaRangeToFileOffset(rva, length: 1, out _);
    }

    /// <summary>
    /// Translates a raw file byte offset inside a file-backed PE section to its relative virtual address (RVA).
    /// </summary>
    /// <param name="fileOffset">Zero-based file byte offset to translate.</param>
    /// <returns>The relative virtual address corresponding to <paramref name="fileOffset"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="fileOffset"/> is negative or greater than or equal to the PE image length.
    /// </exception>
    /// <exception cref="BadImageFormatException">
    /// Thrown when <paramref name="fileOffset"/> does not map uniquely to a file-backed section byte.
    /// </exception>
    public uint FileOffsetToRva(int fileOffset)
    {
        if (fileOffset < 0 || fileOffset >= _buffer.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fileOffset),
                fileOffset,
                string.Format(CultureInfo.InvariantCulture, "File offset must be within [0, {0}).", _buffer.Length));
        }

        var foundIndex = -1;
        var resolvedRva = 0u;
        for (var i = 0; i < _headers.Sections.Length; i++)
        {
            var section = _headers.Sections[i];
            var backedLength = section.FileBackedSpanLength;
            if (backedLength == 0 || (ulong)fileOffset < section.PointerToRawData)
            {
                continue;
            }

            var offsetInSection = (ulong)fileOffset - section.PointerToRawData;
            if (offsetInSection >= backedLength)
            {
                continue;
            }

            if (foundIndex >= 0)
            {
                throw PeHeaderParser.CreateBadImage(
                    _imageLabel,
                    string.Format(CultureInfo.InvariantCulture, "Ambiguous overlapping PE raw sections cover file offset 0x{0:X}.", fileOffset));
            }

            foundIndex = i;
            resolvedRva = checked((uint)((ulong)section.VirtualAddress + offsetInSection));
        }

        if (foundIndex < 0)
        {
            throw PeHeaderParser.CreateBadImage(
                _imageLabel,
                string.Format(CultureInfo.InvariantCulture, "File offset 0x{0:X} does not reside inside any file-backed PE section.", fileOffset));
        }

        return resolvedRva;
    }

    /// <summary>
    /// Gets the internal module name recorded in <c>IMAGE_EXPORT_DIRECTORY.Name</c>, or <see langword="null"/> when the image has no export directory.
    /// </summary>
    /// <returns>The validated ASCII export module name, or <see langword="null"/> when <c>DataDirectory[0]</c> is empty.</returns>
    public string? GetExportModuleName()
    {
        if (_headers.ExportTableRva == 0 && _headers.ExportTableSize == 0)
        {
            return null;
        }

        var nameRva = ReadValidatedExportNameRva();
        return ReadValidatedModuleCString(nameRva, out _);
    }

    /// <summary>
    /// Returns the ordered list of module names referenced by ordinary PE import descriptors (<c>IMAGE_IMPORT_DESCRIPTOR</c>).
    /// </summary>
    /// <returns>An immutable snapshot of ordinary imported module names.</returns>
    public IReadOnlyList<string> GetImportedModuleNames()
    {
        var descriptors = ParseOrdinaryImportDescriptors();
        var names = new string[descriptors.Count];
        for (var i = 0; i < descriptors.Count; i++)
        {
            names[i] = descriptors[i].ModuleName;
        }

        return names;
    }

    /// <summary>
    /// Returns the ordered list of module names referenced by PE delay-load import descriptors (<c>IMAGE_DELAYLOAD_DESCRIPTOR</c>).
    /// </summary>
    /// <returns>An immutable snapshot of delay-imported module names.</returns>
    public IReadOnlyList<string> GetDelayImportedModuleNames()
    {
        var descriptors = ParseDelayImportDescriptors();
        var names = new string[descriptors.Count];
        for (var i = 0; i < descriptors.Count; i++)
        {
            names[i] = descriptors[i].ModuleName;
        }

        return names;
    }

    /// <summary>
    /// Rewrites the internal module name recorded in <c>IMAGE_EXPORT_DIRECTORY.Name</c> (<c>DataDirectory[0]</c>) in place
    /// with a same-or-shorter ASCII module name.
    /// </summary>
    /// <param name="originalModuleName">Expected unpatched ASCII export module name (matched case-insensitively; provides slot capacity).</param>
    /// <param name="replacementModuleName">Replacement ASCII module name without path characters.</param>
    /// <returns>This <see cref="PeImageEditor"/> instance for fluent chaining.</returns>
    public PeImageEditor RewriteExportModuleName(
        string originalModuleName,
        string replacementModuleName)
    {
        ValidateModuleRewriteArguments(originalModuleName, replacementModuleName);
        var nameRva = ReadValidatedExportNameRva();
        var currentName = ReadValidatedModuleCString(nameRva, out _);
        var slotLength = originalModuleName.Length + 1;
        var fileOffset = MapRvaRangeToFileOffset(nameRva, slotLength, out _);
        var replacementBytes = EncodeZeroPaddedAscii(replacementModuleName, slotLength);

        if (string.Equals(currentName, originalModuleName, StringComparison.OrdinalIgnoreCase))
        {
            if (_buffer[fileOffset + originalModuleName.Length] != 0)
            {
                throw CreateRvaMismatchException(nameRva, fileOffset);
            }

            WriteBufferIfDifferent(fileOffset, replacementBytes);
            return this;
        }

        if (string.Equals(currentName, replacementModuleName, StringComparison.OrdinalIgnoreCase)
            && IsAllZero(_buffer, fileOffset + replacementModuleName.Length, slotLength - replacementModuleName.Length))
        {
            WriteBufferIfDifferent(fileOffset, replacementBytes);
            return this;
        }

        throw PeHeaderParser.CreateBadImage(
            _imageLabel,
            $"Export directory module name '{currentName}' does not match expected '{originalModuleName}'.");
    }

    /// <summary>
    /// Rewrites an ordinary <c>IMAGE_IMPORT_DESCRIPTOR.Name</c> entry in place with a same-or-shorter ASCII module name.
    /// </summary>
    /// <param name="originalModuleName">Expected unpatched ASCII module name (provides slot capacity for idempotent re-application).</param>
    /// <param name="replacementModuleName">Replacement ASCII module name without path characters.</param>
    /// <returns>This <see cref="PeImageEditor"/> instance for fluent chaining.</returns>
    public PeImageEditor RewriteImportModuleName(
        string originalModuleName,
        string replacementModuleName)
    {
        ValidateModuleRewriteArguments(originalModuleName, replacementModuleName);
        var descriptors = ParseOrdinaryImportDescriptors();
        RewriteDescriptorModuleNameCore(descriptors, originalModuleName, replacementModuleName, "import");
        return this;
    }

    /// <summary>
    /// Compacts all ordinary <c>IMAGE_IMPORT_DESCRIPTOR</c> entries matching <see cref="PeImportModuleRemoval.ModuleName"/>
    /// out of <c>DataDirectory[1]</c>, shifting subsequent descriptors left and zero-filling vacated tail slots.
    /// </summary>
    /// <param name="removal">Verified import module removal specification.</param>
    /// <returns>This <see cref="PeImageEditor"/> instance for fluent chaining.</returns>
    public PeImageEditor RemoveImportModule(PeImportModuleRemoval removal)
    {
        ValidateImportModuleRemovalArgument(removal);
        var descriptors = ParseOrdinaryImportDescriptors();
        var remaining = new List<ImportModuleEntry>(descriptors.Count);
        var removedCount = 0;
        foreach (var entry in descriptors)
        {
            if (string.Equals(entry.ModuleName, removal.ModuleName, StringComparison.OrdinalIgnoreCase))
            {
                removedCount++;
            }
            else
            {
                remaining.Add(entry);
            }
        }

        if (removedCount == 0)
        {
            if (removal.WitnessRemainingModuleName is not null
                && ContainsImportModule(remaining, removal.WitnessRemainingModuleName))
            {
                return this;
            }

            throw PeHeaderParser.CreateBadImage(
                _imageLabel,
                $"Ordinary import descriptor for module '{removal.ModuleName}' was not found.");
        }

        if (removal.WitnessRemainingModuleName is not null
            && !ContainsImportModule(remaining, removal.WitnessRemainingModuleName))
        {
            throw PeHeaderParser.CreateBadImage(
                _imageLabel,
                $"Witness remaining import module '{removal.WitnessRemainingModuleName}' was not found alongside '{removal.ModuleName}'.");
        }

        CompactOrdinaryImportDescriptors(descriptors, remaining);
        return this;
    }

    /// <summary>
    /// Explicitly rewrites a delay-load <c>IMAGE_DELAYLOAD_DESCRIPTOR.DllNameRVA</c> entry in place with a same-or-shorter ASCII module name.
    /// </summary>
    /// <param name="originalModuleName">Expected unpatched ASCII delay-imported module name.</param>
    /// <param name="replacementModuleName">Replacement ASCII module name without path characters.</param>
    /// <returns>This <see cref="PeImageEditor"/> instance for fluent chaining.</returns>
    public PeImageEditor RewriteDelayImportModuleName(
        string originalModuleName,
        string replacementModuleName)
    {
        ValidateModuleRewriteArguments(originalModuleName, replacementModuleName);
        var descriptors = ParseDelayImportDescriptors();
        RewriteDescriptorModuleNameCore(descriptors, originalModuleName, replacementModuleName, "delay-import");
        return this;
    }

    /// <summary>
    /// Rewrites an ordinary <c>IMAGE_IMPORT_BY_NAME</c> symbol entry for <paramref name="moduleName"/> in place with a same-or-shorter ASCII symbol name.
    /// </summary>
    /// <param name="moduleName">Case-insensitive ASCII module name in the ordinary import table.</param>
    /// <param name="originalSymbolName">Expected unpatched ASCII symbol name.</param>
    /// <param name="replacementSymbolName">Same-or-shorter replacement ASCII symbol name.</param>
    /// <returns>This <see cref="PeImageEditor"/> instance for fluent chaining.</returns>
    public PeImageEditor RewriteImportSymbol(
        string moduleName,
        string originalSymbolName,
        string replacementSymbolName)
    {
        return RewriteImportSymbol(new PeImportSymbolRewrite(moduleName, originalSymbolName, replacementSymbolName));
    }

    /// <summary>
    /// Rewrites an ordinary <c>IMAGE_IMPORT_BY_NAME</c> symbol entry described by <paramref name="rewrite"/> in place.
    /// </summary>
    /// <param name="rewrite">Verified symbol rewrite descriptor.</param>
    /// <returns>This <see cref="PeImageEditor"/> instance for fluent chaining.</returns>
    public PeImageEditor RewriteImportSymbol(PeImportSymbolRewrite rewrite)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(rewrite);
#endif
#if NET472_OR_GREATER
        if (rewrite is null)
        {
            throw new ArgumentNullException(nameof(rewrite));
        }
#endif

        var matchingModule = FindSingleDescriptorByModuleName(ParseOrdinaryImportDescriptors(), rewrite.ModuleName);
        var thunks = CollectModuleNamedThunks(matchingModule);
        ApplySymbolRewriteToThunks(rewrite, thunks);
        return this;
    }

    /// <summary>
    /// Applies a provenance-verified byte or string-literal patch described by <paramref name="patch"/>.
    /// </summary>
    /// <param name="patch">Verified RVA byte patch descriptor.</param>
    /// <returns>This <see cref="PeImageEditor"/> instance for fluent chaining.</returns>
    public PeImageEditor ApplyVerifiedBytePatch(PeVerifiedBytePatch patch)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(patch);
#endif
#if NET472_OR_GREATER
        if (patch is null)
        {
            throw new ArgumentNullException(nameof(patch));
        }
#endif

        ReplaceVerifiedBytesCore(patch.Rva, patch.ExpectedOriginalBuffer, patch.ReplacementBuffer);
        return this;
    }

    /// <summary>
    /// Verifies that the bytes at <paramref name="rva"/> match <paramref name="expectedOriginal"/> (or <paramref name="replacement"/> for idempotence)
    /// and writes <paramref name="replacement"/> in place.
    /// </summary>
    /// <param name="rva">Target relative virtual address in a file-backed section.</param>
    /// <param name="expectedOriginal">Expected original byte sequence.</param>
    /// <param name="replacement">Replacement byte sequence of the exact same length.</param>
    /// <returns>This <see cref="PeImageEditor"/> instance for fluent chaining.</returns>
    public PeImageEditor ReplaceVerifiedBytesAtRva(
        uint rva,
        byte[] expectedOriginal,
        byte[] replacement)
    {
        var patch = new PeVerifiedBytePatch(rva, expectedOriginal, replacement);
        ReplaceVerifiedBytesCore(patch.Rva, patch.ExpectedOriginalBuffer, patch.ReplacementBuffer);
        return this;
    }

#if NET8_0_OR_GREATER
    /// <summary>
    /// Verifies that the bytes at <paramref name="rva"/> match <paramref name="expectedOriginal"/> (or <paramref name="replacement"/> for idempotence)
    /// and writes <paramref name="replacement"/> in place.
    /// </summary>
    /// <param name="rva">Target relative virtual address in a file-backed section.</param>
    /// <param name="expectedOriginal">Expected original byte span.</param>
    /// <param name="replacement">Replacement byte span of the exact same length.</param>
    /// <returns>This <see cref="PeImageEditor"/> instance for fluent chaining.</returns>
    public PeImageEditor ReplaceVerifiedBytesAtRva(
        uint rva,
        ReadOnlySpan<byte> expectedOriginal,
        ReadOnlySpan<byte> replacement)
    {
        if (rva == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rva), rva, "RVA must be non-zero.");
        }

        if (expectedOriginal.IsEmpty)
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

        var fileOffset = MapRvaRangeToFileOffset(rva, expectedOriginal.Length, out _);
        var currentSlice = _buffer.AsSpan(fileOffset, expectedOriginal.Length);
        if (currentSlice.SequenceEqual(expectedOriginal))
        {
            if (!expectedOriginal.SequenceEqual(replacement))
            {
                replacement.CopyTo(currentSlice);
                _hasUnreconciledModification = true;
            }

            return this;
        }

        if (currentSlice.SequenceEqual(replacement))
        {
            return this;
        }

        throw CreateRvaMismatchException(rva, fileOffset);
    }
#endif

    /// <summary>
    /// Verifies and replaces a fixed-slot ASCII string literal at <paramref name="rva"/>, zero-padding unused trailing bytes.
    /// </summary>
    /// <param name="rva">Target relative virtual address in a file-backed section.</param>
    /// <param name="expectedOriginal">Expected original printable ASCII string.</param>
    /// <param name="replacement">Same-or-shorter printable ASCII replacement string.</param>
    /// <param name="requireNullTerminator">
    /// When <see langword="true"/>, verifies the trailing NUL terminator on <paramref name="expectedOriginal"/> and zero-fills through it.
    /// </param>
    /// <returns>This <see cref="PeImageEditor"/> instance for fluent chaining.</returns>
    public PeImageEditor ReplaceVerifiedAsciiStringAtRva(
        uint rva,
        string expectedOriginal,
        string replacement,
        bool requireNullTerminator = true)
    {
        var patch = PeVerifiedBytePatch.FromAsciiLiteral(rva, expectedOriginal, replacement, requireNullTerminator);
        ReplaceVerifiedBytesCore(patch.Rva, patch.ExpectedOriginalBuffer, patch.ReplacementBuffer);
        return this;
    }

    /// <summary>
    /// Verifies and replaces a fixed-slot UTF-16LE string literal at <paramref name="rva"/>, zero-padding unused trailing code units.
    /// </summary>
    /// <param name="rva">Target relative virtual address in a file-backed section.</param>
    /// <param name="expectedOriginal">Expected original UTF-16 string without embedded NUL characters.</param>
    /// <param name="replacement">Same-or-shorter UTF-16 replacement string without embedded NUL characters.</param>
    /// <param name="requireNullTerminator">
    /// When <see langword="true"/>, verifies the trailing 16-bit NUL terminator on <paramref name="expectedOriginal"/> and zero-fills through it.
    /// </param>
    /// <returns>This <see cref="PeImageEditor"/> instance for fluent chaining.</returns>
    public PeImageEditor ReplaceVerifiedUtf16StringAtRva(
        uint rva,
        string expectedOriginal,
        string replacement,
        bool requireNullTerminator = true)
    {
        var patch = PeVerifiedBytePatch.FromUtf16Literal(rva, expectedOriginal, replacement, requireNullTerminator);
        ReplaceVerifiedBytesCore(patch.Rva, patch.ExpectedOriginalBuffer, patch.ReplacementBuffer);
        return this;
    }

    /// <summary>
    /// Verifies and replaces a length-prefixed Pascal / Borland Delphi <c>ShortString</c> literal (<c>[lengthByte, ascii...]</c>)
    /// at <paramref name="rva"/>, zero-padding unused trailing character slots.
    /// </summary>
    /// <param name="rva">Target relative virtual address in a file-backed section.</param>
    /// <param name="expectedOriginal">Expected original printable ASCII <c>ShortString</c> payload (<c>1..255</c> characters).</param>
    /// <param name="replacement">Same-or-shorter printable ASCII replacement payload.</param>
    /// <param name="allowAnyPrintableAsciiOfSameLength">
    /// When <see langword="true"/>, also accepts any existing printable ASCII <c>ShortString</c> whose length byte equals
    /// <c>expectedOriginal.Length</c> (for base images whose fixed-length <c>.INI</c> literal was previously customized).
    /// </param>
    /// <returns>This <see cref="PeImageEditor"/> instance for fluent chaining.</returns>
    public PeImageEditor ReplaceVerifiedShortStringAtRva(
        uint rva,
        string expectedOriginal,
        string replacement,
        bool allowAnyPrintableAsciiOfSameLength = false)
    {
        ValidateShortStringPatchArguments(rva, expectedOriginal, replacement);
        var slotLength = 1 + expectedOriginal.Length;
        var fileOffset = MapRvaRangeToFileOffset(rva, slotLength, out _);
        var expectedBytes = EncodeShortStringSlot(expectedOriginal, slotLength);
        var replacementBytes = EncodeShortStringSlot(replacement, slotLength);

        if (SliceEquals(_buffer, fileOffset, expectedBytes))
        {
            WriteBufferIfDifferent(fileOffset, replacementBytes);
            return this;
        }

        if (SliceEquals(_buffer, fileOffset, replacementBytes))
        {
            return this;
        }

        if (allowAnyPrintableAsciiOfSameLength
            && IsPrintableAsciiShortStringOfLength(_buffer, fileOffset, expectedOriginal.Length))
        {
            WriteBufferIfDifferent(fileOffset, replacementBytes);
            return this;
        }

        throw CreateRvaMismatchException(rva, fileOffset);
    }

    /// <summary>
    /// Sets or clears the <c>IMAGE_FILE_LARGE_ADDRESS_AWARE</c> (<c>0x0020</c>) COFF characteristic on the in-memory PE header.
    /// </summary>
    /// <param name="enabled"><see langword="true"/> to set <c>0x0020</c>; <see langword="false"/> to clear it.</param>
    /// <returns>This <see cref="PeImageEditor"/> instance for fluent chaining.</returns>
    public PeImageEditor SetLargeAddressAware(bool enabled = true)
    {
        var current = PeHeaderParser.ReadUInt16(_buffer, _headers.CharacteristicsFileOffset);
        var updated = enabled
            ? (ushort)(current | ImageFileLargeAddressAwareFlag)
            : (ushort)(current & ~ImageFileLargeAddressAwareFlag);
        if (updated != current)
        {
            PeHeaderParser.WriteUInt16(_buffer, _headers.CharacteristicsFileOffset, updated);
            _hasUnreconciledModification = true;
        }

        return this;
    }

    /// <summary>
    /// Strips any invalidated Authenticode signature entry (<c>IMAGE_DIRECTORY_ENTRY_SECURITY</c>), clears
    /// <c>IMAGE_DLLCHARACTERISTICS_FORCE_INTEGRITY</c> on the private buffer, and recalculates <c>OptionalHeader.CheckSum</c>.
    /// </summary>
    /// <returns>This <see cref="PeImageEditor"/> instance for fluent chaining.</returns>
    public PeImageEditor ClearAuthenticodeAndRecalculateChecksum()
    {
        if (_headers.SecurityTableFileOffset != 0 || _headers.SecurityTableSize != 0)
        {
            var secStart = checked((int)_headers.SecurityTableFileOffset);
            var secEnd = checked((int)(_headers.SecurityTableFileOffset + _headers.SecurityTableSize));
            if (secEnd == _buffer.Length && !AnySectionOverlapsTail(secStart))
            {
                Array.Resize(ref _buffer, secStart);
            }
            else
            {
                Array.Clear(_buffer, secStart, checked((int)_headers.SecurityTableSize));
            }

            if (_headers.SecurityDirectoryEntryFileOffset >= 0)
            {
                PeHeaderParser.WriteUInt32(_buffer, _headers.SecurityDirectoryEntryFileOffset, 0);
                PeHeaderParser.WriteUInt32(_buffer, _headers.SecurityDirectoryEntryFileOffset + 4, 0);
            }

            _headers.SecurityTableFileOffset = 0;
            _headers.SecurityTableSize = 0;
        }

        var dllCharacteristics = PeHeaderParser.ReadUInt16(_buffer, _headers.DllCharacteristicsFileOffset);
        if ((dllCharacteristics & ImageDllCharacteristicsForceIntegrity) != 0)
        {
            dllCharacteristics = (ushort)(dllCharacteristics & ~ImageDllCharacteristicsForceIntegrity);
            PeHeaderParser.WriteUInt16(_buffer, _headers.DllCharacteristicsFileOffset, dllCharacteristics);
        }

        var checksum = ComputePeCheckSum(_buffer, _headers.CheckSumFileOffset);
        PeHeaderParser.WriteUInt32(_buffer, _headers.CheckSumFileOffset, checksum);
        _hasUnreconciledModification = false;
        return this;
    }

    /// <summary>
    /// Returns a cloned byte array representing the current patched PE image.
    /// </summary>
    /// <returns>A copy of the current PE image bytes.</returns>
    public byte[] ToArray()
    {
        return (byte[])_buffer.Clone();
    }

    /// <summary>
    /// Atomically writes the current PE image to <paramref name="destinationPath"/> only when the destination file is missing or differs in content.
    /// </summary>
    /// <param name="destinationPath">Destination path for the private worker copy (must not equal the original source file path).</param>
    [SuppressMessage("Security", "SCS0018", Justification = "Writes verified PE image to caller-chosen private staging path while refusing in-place source modification.")]
    public void SaveIfChanged(string destinationPath)
    {
        ValidatePathArgument(destinationPath, nameof(destinationPath));
        if (_hasUnreconciledModification && HasAuthenticodeSignature)
        {
            throw new InvalidOperationException(
                $"Modified PE image '{_imageLabel}' still carries an invalidated Authenticode signature; call ClearAuthenticodeAndRecalculateChecksum() before saving.");
        }

        var fullDestination = Path.GetFullPath(destinationPath);
        var comparison = NativeLoader.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (_sourceFullPath is not null && string.Equals(fullDestination, _sourceFullPath, comparison))
        {
            throw new InvalidOperationException(
                $"Refusing to overwrite source binary '{_sourceFullPath}' in place; save to a private copy path instead.");
        }

        var directory = Path.GetDirectoryName(fullDestination);
        if (directory?.Length > 0)
        {
            Directory.CreateDirectory(directory);
        }

        if (FileMatchesBuffer(fullDestination, _buffer))
        {
            return;
        }

        WriteAtomically(fullDestination, directory, _buffer);
    }

    internal static uint ComputePeCheckSum(byte[] buffer, int checkSumOffset)
    {
        ulong sum = 0;
        var length = buffer.Length;
        var evenLength = length & ~1;
        for (var i = 0; i < evenLength; i += 2)
        {
            if (i == checkSumOffset || i == checkSumOffset + 2)
            {
                continue;
            }

            var word = (ushort)(buffer[i] | (buffer[i + 1] << 8));
            sum += word;
            sum = (sum & 0xFFFFUL) + (sum >> 16);
        }

        if ((length & 1) != 0)
        {
            sum += buffer[length - 1];
            sum = (sum & 0xFFFFUL) + (sum >> 16);
        }

        sum = (sum & 0xFFFFUL) + (sum >> 16);
        return checked((uint)(sum + (ulong)length));
    }

    [SuppressMessage("Security", "SCS0018", Justification = "Compares private destination copy with in-memory buffer.")]
    [SuppressMessage("Security", "SEC0112", Justification = "Compares caller-chosen destination file against verified PE image buffer.")]
    internal static bool FileMatchesBuffer(string fullPath, byte[] expected)
    {
        if (!File.Exists(fullPath))
        {
            return false;
        }

        var info = new FileInfo(fullPath);
        if (info.Length != expected.Length)
        {
            return false;
        }

        using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 8192);
        var chunk = new byte[8192];
        var position = 0;
        while (position < expected.Length)
        {
            var toRead = Math.Min(chunk.Length, expected.Length - position);
            var read = stream.Read(chunk, 0, toRead);
            if (read <= 0)
            {
                return false;
            }

            for (var i = 0; i < read; i++)
            {
                if (chunk[i] != expected[position + i])
                {
                    return false;
                }
            }

            position += read;
        }

        return stream.ReadByte() == -1;
    }

    [SuppressMessage("Security", "SCS0018", Justification = "Writes verified PE image via temporary file in the same private directory.")]
    [SuppressMessage("Security", "SEC0112", Justification = "Creates random temporary file adjacent to caller-chosen private staging destination.")]
    internal static void WriteAtomically(string fullDestination, string? directory, byte[] content)
    {
        var tempDirectory = string.IsNullOrEmpty(directory) ? "." : directory;
        var tempPath = Path.Combine(
            tempDirectory,
            Path.GetFileName(fullDestination) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 8192))
            {
                stream.Write(content, 0, content.Length);
                stream.Flush(flushToDisk: true);
            }

#if NET8_0_OR_GREATER
            File.Move(tempPath, fullDestination, overwrite: true);
#else
            if (File.Exists(fullDestination))
            {
                File.Replace(tempPath, fullDestination, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, fullDestination);
            }
#endif
        }
        catch
        {
            TryDeleteTempFile(tempPath);
            throw;
        }
    }

    private static PeImageEditor CreateValidated(
        byte[] clonedBuffer,
        string imageLabel,
        string? sourceFullPath,
        PeImageValidationOptions? validation)
    {
        var effectiveValidation = validation ?? new PeImageValidationOptions();
        var headers = PeHeaderParser.Parse(clonedBuffer, imageLabel, effectiveValidation);
        var editor = new PeImageEditor(clonedBuffer, imageLabel, sourceFullPath, headers);
        editor.PreflightValidateImportTables();
        return editor;
    }

    private static void ValidatePathArgument(string? path, string paramName)
    {
        if (path is null || string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path cannot be null or whitespace.", paramName);
        }

#pragma warning disable CA2249,MA0001
        if (path.IndexOf('\0') >= 0)
        {
            throw new ArgumentException("Path cannot contain a NUL character.", paramName);
        }
#pragma warning restore CA2249,MA0001
    }

    private static void ValidateModuleRewriteArguments(string originalModuleName, string replacementModuleName)
    {
        PeNameValidator.ValidateModuleNameArgument(originalModuleName, nameof(originalModuleName));
        PeNameValidator.ValidateModuleNameArgument(replacementModuleName, nameof(replacementModuleName));
        if (replacementModuleName.Length > originalModuleName.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(replacementModuleName),
                replacementModuleName.Length,
                $"Replacement module name '{replacementModuleName}' cannot exceed original module name '{originalModuleName}' length ({originalModuleName.Length}).");
        }
    }

    private static void ValidateImportModuleRemovalArgument(PeImportModuleRemoval removal)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(removal);
#endif
#if NET472_OR_GREATER
        if (removal is null)
        {
            throw new ArgumentNullException(nameof(removal));
        }
#endif

        PeNameValidator.ValidateModuleNameArgument(removal.ModuleName, nameof(removal));
        if (removal.WitnessRemainingModuleName is not null)
        {
            PeNameValidator.ValidateModuleNameArgument(removal.WitnessRemainingModuleName, nameof(removal));
            if (string.Equals(removal.ModuleName, removal.WitnessRemainingModuleName, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "WitnessRemainingModuleName cannot be the same module as ModuleName.",
                    nameof(removal));
            }
        }
    }

    private static bool ContainsImportModule(List<ImportModuleEntry> descriptors, string moduleName)
    {
        return descriptors.Exists(entry => string.Equals(entry.ModuleName, moduleName, StringComparison.OrdinalIgnoreCase));
    }

    private static void ValidateShortStringPatchArguments(uint rva, string expectedOriginal, string replacement)
    {
        if (rva == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rva), rva, "RVA must be non-zero.");
        }

        PeNameValidator.ValidateAsciiLiteralArgument(expectedOriginal, nameof(expectedOriginal));
        if (expectedOriginal.Length > 255)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedOriginal),
                expectedOriginal.Length,
                "Pascal ShortString length must not exceed 255 characters.");
        }

        PeNameValidator.ValidateAsciiLiteralArgument(replacement, nameof(replacement));
        if (replacement.Length > expectedOriginal.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(replacement),
                replacement.Length,
                $"Replacement ShortString '{replacement}' cannot exceed original length ({expectedOriginal.Length}).");
        }
    }

    private static byte[] EncodeShortStringSlot(string value, int slotByteLength)
    {
        var result = new byte[slotByteLength];
        result[0] = checked((byte)value.Length);
        Encoding.ASCII.GetBytes(value, 0, value.Length, result, 1);
        return result;
    }

    private static bool IsPrintableAsciiShortStringOfLength(byte[] buffer, int offset, int expectedLength)
    {
        if (buffer[offset] != expectedLength)
        {
            return false;
        }

        for (var i = 1; i <= expectedLength; i++)
        {
            var b = buffer[offset + i];
            if (b is < 0x20 or > 0x7E)
            {
                return false;
            }
        }

        return true;
    }

    private static byte[] EncodeZeroPaddedAscii(string value, int totalByteLength)
    {
        var result = new byte[totalByteLength];
        Encoding.ASCII.GetBytes(value, 0, value.Length, result, 0);
        return result;
    }

    private static bool SliceEquals(byte[] buffer, int offset, byte[] expected)
    {
        for (var i = 0; i < expected.Length; i++)
        {
            if (buffer[offset + i] != expected[i])
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAllZero(byte[] buffer, int offset, int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (buffer[offset + i] != 0)
            {
                return false;
            }
        }

        return true;
    }

    [SuppressMessage("Security", "SCS0018", Justification = "Cleans up temporary staging file created by WriteAtomically.")]
    [SuppressMessage("Security", "SEC0116", Justification = "Deletes only the temporary file created by WriteAtomically.")]
    private static void TryDeleteTempFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = ex;
        }
    }

    private static void PartitionModuleMatches(
        List<ImportModuleEntry> descriptors,
        string originalModuleName,
        string replacementModuleName,
        bool isSameName,
        out List<ImportModuleEntry> originalMatches,
        out List<ImportModuleEntry> alreadyPatchedMatches)
    {
        originalMatches = [];
        alreadyPatchedMatches = [];
        foreach (var entry in descriptors)
        {
            if (string.Equals(entry.ModuleName, originalModuleName, StringComparison.Ordinal))
            {
                originalMatches.Add(entry);
            }
            else if (!isSameName && string.Equals(entry.ModuleName, replacementModuleName, StringComparison.Ordinal))
            {
                alreadyPatchedMatches.Add(entry);
            }
        }
    }

    private void ApplySymbolRewriteToThunks(PeImportSymbolRewrite spec, List<NamedImportThunk> thunks)
    {
        var slotLength = spec.OriginalSymbolName.Length + 1;
        var expectedBytes = EncodeZeroPaddedAscii(spec.OriginalSymbolName, slotLength);
        var replacementBytes = EncodeZeroPaddedAscii(spec.ReplacementSymbolName, slotLength);
        var isSameSymbol = string.Equals(spec.OriginalSymbolName, spec.ReplacementSymbolName, StringComparison.Ordinal);

        var originalMatches = new List<NamedImportThunk>();
        var alreadyPatchedMatches = new List<NamedImportThunk>();
        foreach (var thunk in thunks)
        {
            if (string.Equals(thunk.SymbolName, spec.OriginalSymbolName, StringComparison.Ordinal))
            {
                originalMatches.Add(thunk);
            }
            else if (!isSameSymbol && string.Equals(thunk.SymbolName, spec.ReplacementSymbolName, StringComparison.Ordinal))
            {
                alreadyPatchedMatches.Add(thunk);
            }
        }

        if (originalMatches.Count > 1)
        {
            throw PeHeaderParser.CreateBadImage(
                _imageLabel,
                $"Ambiguous duplicate import symbol '{spec.OriginalSymbolName}' in module '{spec.ModuleName}'.");
        }

        if (originalMatches.Count == 1)
        {
            if (!isSameSymbol && alreadyPatchedMatches.Count > 0)
            {
                throw PeHeaderParser.CreateBadImage(
                    _imageLabel,
                    $"Ambiguous import symbol rewrite in '{spec.ModuleName}': target symbol '{spec.ReplacementSymbolName}' already exists alongside '{spec.OriginalSymbolName}'.");
            }

            ApplyThunkSymbolRewrite(originalMatches[0], expectedBytes, replacementBytes, clearHint: !isSameSymbol);
            return;
        }

        if (alreadyPatchedMatches.Count == 1
            && VerifyAlreadyPatchedThunk(alreadyPatchedMatches[0], replacementBytes, requireZeroHint: !isSameSymbol))
        {
            return;
        }

        throw PeHeaderParser.CreateBadImage(
            _imageLabel,
            $"Import symbol '{spec.OriginalSymbolName}' was not found in module '{spec.ModuleName}'.");
    }

    private void PreflightValidateImportTables()
    {
        var imports = ParseOrdinaryImportDescriptors();
        foreach (var import in imports)
        {
            _ = CollectModuleNamedThunks(import);
        }

        _ = ParseDelayImportDescriptors();
    }

    private List<ImportModuleEntry> ParseOrdinaryImportDescriptors()
    {
        var result = new List<ImportModuleEntry>();
        if (_headers.ImportTableRva == 0 && _headers.ImportTableSize == 0)
        {
            return result;
        }

        if (_headers.ImportTableRva == 0 || _headers.ImportTableSize < ImportDescriptorSize)
        {
            throw PeHeaderParser.CreateBadImage(_imageLabel, "Ordinary import directory entry is truncated or invalid.");
        }

        var cursorRva = _headers.ImportTableRva;
        while (true)
        {
            var descOffset = MapRvaRangeToFileOffset(cursorRva, ImportDescriptorSize, out _);
            if (IsAllZero(_buffer, descOffset, ImportDescriptorSize))
            {
                _ = MapRvaRangeToFileOffset(
                    _headers.ImportTableRva,
                    checked((result.Count + 1) * ImportDescriptorSize),
                    out _);
                return result;
            }

            var originalFirstThunk = PeHeaderParser.ReadUInt32(_buffer, descOffset);
            var timeDateStamp = PeHeaderParser.ReadUInt32(_buffer, descOffset + 4);
            var nameRva = PeHeaderParser.ReadUInt32(_buffer, descOffset + 12);
            var firstThunk = PeHeaderParser.ReadUInt32(_buffer, descOffset + 16);
            if (nameRva == 0 || (originalFirstThunk == 0 && firstThunk == 0))
            {
                throw PeHeaderParser.CreateBadImage(_imageLabel, "Malformed IMAGE_IMPORT_DESCRIPTOR entry.");
            }

            var moduleName = ReadValidatedModuleCString(nameRva, out var nameFileOffset);
            result.Add(new ImportModuleEntry(
                descOffset,
                nameRva,
                nameFileOffset,
                moduleName,
                originalFirstThunk,
                firstThunk,
                timeDateStamp));
            cursorRva = checked(cursorRva + (uint)ImportDescriptorSize);
        }
    }

    private List<ImportModuleEntry> ParseDelayImportDescriptors()
    {
        var result = new List<ImportModuleEntry>();
        if (_headers.DelayImportTableRva == 0 && _headers.DelayImportTableSize == 0)
        {
            return result;
        }

        if (_headers.DelayImportTableRva == 0 || _headers.DelayImportTableSize < DelayImportDescriptorSize)
        {
            throw PeHeaderParser.CreateBadImage(_imageLabel, "Delay-import directory entry is truncated or invalid.");
        }

        var cursorRva = _headers.DelayImportTableRva;
        while (true)
        {
            var descOffset = MapRvaRangeToFileOffset(cursorRva, DelayImportDescriptorSize, out _);
            if (IsAllZero(_buffer, descOffset, DelayImportDescriptorSize))
            {
                return result;
            }

            var attributes = PeHeaderParser.ReadUInt32(_buffer, descOffset);
            var dllNameRva = PeHeaderParser.ReadUInt32(_buffer, descOffset + 4);
            var iatRva = PeHeaderParser.ReadUInt32(_buffer, descOffset + 12);
            var intRva = PeHeaderParser.ReadUInt32(_buffer, descOffset + 16);
            if ((attributes & DelayLoadRvaAttributeFlag) == 0 || dllNameRva == 0 || intRva == 0)
            {
                throw PeHeaderParser.CreateBadImage(
                    _imageLabel,
                    "Unsupported or malformed IMAGE_DELAYLOAD_DESCRIPTOR layout (expected RVA-based v2 descriptor).");
            }

            var moduleName = ReadValidatedModuleCString(dllNameRva, out var nameFileOffset);
            ValidateThunkArray(intRva);
            result.Add(new ImportModuleEntry(
                descOffset,
                dllNameRva,
                nameFileOffset,
                moduleName,
                intRva,
                iatRva,
                timeDateStamp: 0));
            cursorRva = checked(cursorRva + (uint)DelayImportDescriptorSize);
        }
    }

    private void RewriteDescriptorModuleNameCore(
        List<ImportModuleEntry> descriptors,
        string originalModuleName,
        string replacementModuleName,
        string tableKind)
    {
        var slotLength = originalModuleName.Length + 1;
        var expectedBytes = EncodeZeroPaddedAscii(originalModuleName, slotLength);
        var replacementBytes = EncodeZeroPaddedAscii(replacementModuleName, slotLength);
        var isSameName = string.Equals(originalModuleName, replacementModuleName, StringComparison.Ordinal);

        PartitionModuleMatches(
            descriptors,
            originalModuleName,
            replacementModuleName,
            isSameName,
            out var originalMatches,
            out var alreadyPatchedMatches);

        if (originalMatches.Count > 1)
        {
            throw PeHeaderParser.CreateBadImage(
                _imageLabel,
                $"Ambiguous duplicate {tableKind} descriptor for module '{originalModuleName}'.");
        }

        if (originalMatches.Count == 1)
        {
            if (!isSameName && alreadyPatchedMatches.Count > 0)
            {
                throw PeHeaderParser.CreateBadImage(
                    _imageLabel,
                    $"Ambiguous {tableKind} module rewrite: '{replacementModuleName}' already exists alongside '{originalModuleName}'.");
            }

            var fileOffset = MapRvaRangeToFileOffset(originalMatches[0].NameRva, slotLength, out _);
            if (!SliceEquals(_buffer, fileOffset, expectedBytes))
            {
                throw CreateRvaMismatchException(originalMatches[0].NameRva, fileOffset);
            }

            WriteBufferIfDifferent(fileOffset, replacementBytes);
            return;
        }

        if (alreadyPatchedMatches.Count == 1
            && SliceEquals(_buffer, MapRvaRangeToFileOffset(alreadyPatchedMatches[0].NameRva, slotLength, out _), replacementBytes))
        {
            return;
        }

        throw PeHeaderParser.CreateBadImage(
            _imageLabel,
            $"Expected {tableKind} descriptor for module '{originalModuleName}' was not found.");
    }

    private ImportModuleEntry FindSingleDescriptorByModuleName(
        List<ImportModuleEntry> descriptors,
        string moduleName)
    {
        ImportModuleEntry? found = null;
        foreach (var candidate in descriptors)
        {
            if (!string.Equals(candidate.ModuleName, moduleName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (found.HasValue)
            {
                throw PeHeaderParser.CreateBadImage(
                    _imageLabel,
                    $"Ambiguous duplicate import descriptors for module '{moduleName}'.");
            }

            found = candidate;
        }

        if (!found.HasValue)
        {
            throw PeHeaderParser.CreateBadImage(
                _imageLabel,
                $"Import descriptor for module '{moduleName}' was not found.");
        }

        return found.Value;
    }

    private List<NamedImportThunk> CollectModuleNamedThunks(ImportModuleEntry module)
    {
        if (module.OriginalFirstThunk == 0 && module.TimeDateStamp != 0)
        {
            throw PeHeaderParser.CreateBadImage(
                _imageLabel,
                $"Unsupported bound import descriptor without OriginalFirstThunk for module '{module.ModuleName}'.");
        }

        var primaryRva = module.OriginalFirstThunk != 0 ? module.OriginalFirstThunk : module.FirstThunk;
        var primaryNameRvas = ParseThunkNameRvas(primaryRva);

        uint[]? secondaryNameRvas = null;
        if (module.OriginalFirstThunk != 0 && module.FirstThunk != 0 && module.TimeDateStamp == 0)
        {
            secondaryNameRvas = ParseThunkNameRvas(module.FirstThunk);
            if (secondaryNameRvas.Length != primaryNameRvas.Length)
            {
                throw PeHeaderParser.CreateBadImage(
                    _imageLabel,
                    $"Mismatched ILT/IAT thunk counts in import descriptor for '{module.ModuleName}'.");
            }
        }

        var result = new List<NamedImportThunk>(primaryNameRvas.Length);
        for (var i = 0; i < primaryNameRvas.Length; i++)
        {
            var primaryByNameRva = primaryNameRvas[i];
            if (primaryByNameRva == 0)
            {
                continue;
            }

            var primarySymbol = ReadValidatedImportByName(primaryByNameRva, out var primaryHint, out _);
            var secondaryByNameRva = 0u;
            if (secondaryNameRvas is not null && secondaryNameRvas[i] != 0 && secondaryNameRvas[i] != primaryByNameRva)
            {
                var secondarySymbol = ReadValidatedImportByName(secondaryNameRvas[i], out _, out _);
                if (!string.Equals(primarySymbol, secondarySymbol, StringComparison.Ordinal))
                {
                    throw PeHeaderParser.CreateBadImage(
                        _imageLabel,
                        $"Divergent ILT/IAT import-by-name entries in module '{module.ModuleName}'.");
                }

                secondaryByNameRva = secondaryNameRvas[i];
            }

            result.Add(new NamedImportThunk(module.DescriptorFileOffset, primaryByNameRva, secondaryByNameRva, primaryHint, primarySymbol));
        }

        return result;
    }

    private void ValidateThunkArray(uint thunkTableRva)
    {
        var nameRvas = ParseThunkNameRvas(thunkTableRva);
        for (var i = 0; i < nameRvas.Length; i++)
        {
            var rva = nameRvas[i];
            if (rva != 0)
            {
                _ = ReadValidatedImportByName(rva, out _, out _);
            }
        }
    }

    private uint[] ParseThunkNameRvas(uint thunkTableRva)
    {
        var entrySize = _headers.IsPe32Plus ? 8 : 4;
        var cursorRva = thunkTableRva;
        var rvas = new List<uint>();
        while (true)
        {
            var offset = MapRvaRangeToFileOffset(cursorRva, entrySize, out _);
            if (_headers.IsPe32Plus)
            {
                var value64 = PeHeaderParser.ReadUInt64(_buffer, offset);
                if (value64 == 0)
                {
                    return rvas.ToArray();
                }

                if ((value64 & 0x8000000000000000UL) != 0)
                {
                    rvas.Add(0);
                }
                else
                {
                    if (value64 > uint.MaxValue)
                    {
                        throw PeHeaderParser.CreateBadImage(_imageLabel, "PE32+ import-by-name RVA overflows 32-bit RVA range.");
                    }

                    rvas.Add((uint)value64);
                }
            }
            else
            {
                var value32 = PeHeaderParser.ReadUInt32(_buffer, offset);
                if (value32 == 0)
                {
                    return rvas.ToArray();
                }

                rvas.Add((value32 & 0x80000000u) != 0 ? 0u : value32);
            }

            cursorRva = checked(cursorRva + (uint)entrySize);
        }
    }

    private void ApplyThunkSymbolRewrite(
        NamedImportThunk thunk,
        byte[] expectedBytes,
        byte[] replacementBytes,
        bool clearHint)
    {
        var primaryNameRva = checked(thunk.PrimaryByNameRva + 2u);
        var primaryOffset = MapRvaRangeToFileOffset(thunk.PrimaryByNameRva, 2 + expectedBytes.Length, out _);
        if (!SliceEquals(_buffer, primaryOffset + 2, expectedBytes))
        {
            throw CreateRvaMismatchException(primaryNameRva, primaryOffset + 2);
        }

        var secondaryOffset = -1;
        if (thunk.SecondaryByNameRva != 0)
        {
            secondaryOffset = MapRvaRangeToFileOffset(thunk.SecondaryByNameRva, 2 + expectedBytes.Length, out _);
            if (!SliceEquals(_buffer, secondaryOffset + 2, expectedBytes))
            {
                throw CreateRvaMismatchException(checked(thunk.SecondaryByNameRva + 2u), secondaryOffset + 2);
            }
        }

        if (clearHint)
        {
            PeHeaderParser.WriteUInt16(_buffer, primaryOffset, 0);
        }

        Buffer.BlockCopy(replacementBytes, 0, _buffer, primaryOffset + 2, replacementBytes.Length);
        if (secondaryOffset >= 0)
        {
            if (clearHint)
            {
                PeHeaderParser.WriteUInt16(_buffer, secondaryOffset, 0);
            }

            Buffer.BlockCopy(replacementBytes, 0, _buffer, secondaryOffset + 2, replacementBytes.Length);
        }

        if (clearHint && PeHeaderParser.ReadUInt32(_buffer, thunk.DescriptorFileOffset + 4) != 0)
        {
            PeHeaderParser.WriteUInt32(_buffer, thunk.DescriptorFileOffset + 4, 0);
        }

        if (clearHint || !SliceEquals(expectedBytes, 0, replacementBytes))
        {
            _hasUnreconciledModification = true;
        }
    }

    private bool VerifyAlreadyPatchedThunk(
        NamedImportThunk thunk,
        byte[] replacementBytes,
        bool requireZeroHint)
    {
        if (requireZeroHint && thunk.Hint != 0)
        {
            return false;
        }

        var primaryOffset = MapRvaRangeToFileOffset(thunk.PrimaryByNameRva, 2 + replacementBytes.Length, out _);
        if (!SliceEquals(_buffer, primaryOffset + 2, replacementBytes))
        {
            return false;
        }

        if (thunk.SecondaryByNameRva != 0)
        {
            var secondaryOffset = MapRvaRangeToFileOffset(thunk.SecondaryByNameRva, 2 + replacementBytes.Length, out _);
            if ((requireZeroHint && PeHeaderParser.ReadUInt16(_buffer, secondaryOffset) != 0)
                || !SliceEquals(_buffer, secondaryOffset + 2, replacementBytes))
            {
                return false;
            }
        }

        return true;
    }

    private void ReplaceVerifiedBytesCore(
        uint rva,
        byte[] expectedOriginal,
        byte[] replacement)
    {
        var fileOffset = MapRvaRangeToFileOffset(rva, expectedOriginal.Length, out _);
        if (SliceEquals(_buffer, fileOffset, expectedOriginal))
        {
            WriteBufferIfDifferent(fileOffset, replacement);
            return;
        }

        if (SliceEquals(_buffer, fileOffset, replacement))
        {
            return;
        }

        throw CreateRvaMismatchException(rva, fileOffset);
    }

    private void WriteBufferIfDifferent(int fileOffset, byte[] replacement)
    {
        if (SliceEquals(_buffer, fileOffset, replacement))
        {
            return;
        }

        Buffer.BlockCopy(replacement, 0, _buffer, fileOffset, replacement.Length);
        _hasUnreconciledModification = true;
    }

    private uint ReadValidatedExportNameRva()
    {
        if (_headers.ExportTableRva == 0 || _headers.ExportTableSize < ExportDirectorySize)
        {
            throw PeHeaderParser.CreateBadImage(
                _imageLabel,
                "PE image does not contain a valid export directory (IMAGE_DIRECTORY_ENTRY_EXPORT).");
        }

        var exportDirOffset = MapRvaRangeToFileOffset(_headers.ExportTableRva, ExportDirectorySize, out _);
        var nameRva = PeHeaderParser.ReadUInt32(_buffer, exportDirOffset + 12);
        if (nameRva == 0)
        {
            throw PeHeaderParser.CreateBadImage(_imageLabel, "IMAGE_EXPORT_DIRECTORY.Name RVA is zero.");
        }

        return nameRva;
    }

    private void CompactOrdinaryImportDescriptors(
        List<ImportModuleEntry> allDescriptors,
        List<ImportModuleEntry> remainingDescriptors)
    {
        var totalSlotsBytes = checked((allDescriptors.Count + 1) * ImportDescriptorSize);
        var tableFileOffset = MapRvaRangeToFileOffset(_headers.ImportTableRva, totalSlotsBytes, out _);
        var compacted = new byte[totalSlotsBytes];
        for (var i = 0; i < remainingDescriptors.Count; i++)
        {
            Buffer.BlockCopy(
                _buffer,
                remainingDescriptors[i].DescriptorFileOffset,
                compacted,
                i * ImportDescriptorSize,
                ImportDescriptorSize);
        }

        Buffer.BlockCopy(compacted, 0, _buffer, tableFileOffset, totalSlotsBytes);

        if (remainingDescriptors.Count == 0)
        {
            if (_headers.ImportDirectoryEntryFileOffset >= 0)
            {
                PeHeaderParser.WriteUInt32(_buffer, _headers.ImportDirectoryEntryFileOffset, 0);
                PeHeaderParser.WriteUInt32(_buffer, _headers.ImportDirectoryEntryFileOffset + 4, 0);
            }

            _headers.ImportTableRva = 0;
            _headers.ImportTableSize = 0;
        }
        else if (_headers.ImportDirectoryEntryFileOffset >= 0)
        {
            var paddedOriginal = checked((uint)((allDescriptors.Count + 1) * ImportDescriptorSize));
            var unpaddedOriginal = checked((uint)(allDescriptors.Count * ImportDescriptorSize));
            if (_headers.ImportTableSize == paddedOriginal)
            {
                var updatedSize = checked((uint)((remainingDescriptors.Count + 1) * ImportDescriptorSize));
                PeHeaderParser.WriteUInt32(_buffer, _headers.ImportDirectoryEntryFileOffset + 4, updatedSize);
                _headers.ImportTableSize = updatedSize;
            }
            else if (_headers.ImportTableSize == unpaddedOriginal)
            {
                var updatedSize = checked((uint)(remainingDescriptors.Count * ImportDescriptorSize));
                PeHeaderParser.WriteUInt32(_buffer, _headers.ImportDirectoryEntryFileOffset + 4, updatedSize);
                _headers.ImportTableSize = updatedSize;
            }
        }

        _hasUnreconciledModification = true;
    }

    private string ReadValidatedModuleCString(uint stringRva, out int fileOffset)
    {
        var text = ReadBoundedAsciiCString(stringRva, allowSpaces: true, out fileOffset);
#pragma warning disable CA2249,MA0001
        if (text.IndexOf('/') >= 0
            || text.IndexOf('\\') >= 0
            || text.IndexOf(':') >= 0
            || string.Equals(text, ".", StringComparison.Ordinal)
            || string.Equals(text, "..", StringComparison.Ordinal))
        {
            throw PeHeaderParser.CreateBadImage(
                _imageLabel,
                $"PE import module name '{text}' contains prohibited path components.");
        }
#pragma warning restore CA2249,MA0001

        return text;
    }

    private string ReadValidatedImportByName(uint byNameRva, out ushort hint, out int nameFileOffset)
    {
        var hintOffset = MapRvaRangeToFileOffset(byNameRva, length: 3, out _);
        hint = PeHeaderParser.ReadUInt16(_buffer, hintOffset);
        return ReadBoundedAsciiCString(checked(byNameRva + 2u), allowSpaces: false, out nameFileOffset);
    }

    private string ReadBoundedAsciiCString(uint stringRva, bool allowSpaces, out int fileOffset)
    {
        fileOffset = MapRvaRangeToFileOffset(stringRva, length: 1, out var remainingInSection);
        var minChar = allowSpaces ? 0x20 : 0x21;
        var length = 0;
#pragma warning disable RCS1239 // Use 'for' statement instead of 'while' statement
        while (length < remainingInSection)
        {
            var b = _buffer[fileOffset + length];
            if (b == 0)
            {
                if (length == 0)
                {
                    throw PeHeaderParser.CreateBadImage(
                        _imageLabel,
                        string.Format(CultureInfo.InvariantCulture, "Empty string in PE import table at RVA 0x{0:X}.", stringRva));
                }

                return Encoding.ASCII.GetString(_buffer, fileOffset, length);
            }

            if (b < minChar || b > 0x7E)
            {
                throw PeHeaderParser.CreateBadImage(
                    _imageLabel,
                    string.Format(CultureInfo.InvariantCulture, "Non-ASCII or invalid character 0x{0:X2} in PE import string at RVA 0x{1:X}.", b, stringRva));
            }

            length++;
        }
#pragma warning restore RCS1239 // Use 'for' statement instead of 'while' statement

        throw PeHeaderParser.CreateBadImage(
            _imageLabel,
            string.Format(CultureInfo.InvariantCulture, "Unterminated string in PE import table starting at RVA 0x{0:X}.", stringRva));
    }

    private int MapRvaRangeToFileOffset(uint rva, int length, out int remainingBytesInSection)
    {
        if (rva == 0 || length <= 0)
        {
            throw PeHeaderParser.CreateBadImage(
                _imageLabel,
                string.Format(CultureInfo.InvariantCulture, "RVA 0x{0:X} is not mapped to any file-backed section.", rva));
        }

        var foundIndex = -1;
        var resolvedOffset = 0;
        var resolvedRemaining = 0;
        for (var i = 0; i < _headers.Sections.Length; i++)
        {
            var section = _headers.Sections[i];
            var backedLength = section.FileBackedSpanLength;
            if (backedLength == 0 || rva < section.VirtualAddress)
            {
                continue;
            }

            var offsetInSection = (ulong)rva - section.VirtualAddress;
            if (offsetInSection >= backedLength || offsetInSection + (ulong)length > backedLength)
            {
                continue;
            }

            if (foundIndex >= 0)
            {
                throw PeHeaderParser.CreateBadImage(
                    _imageLabel,
                    string.Format(CultureInfo.InvariantCulture, "Ambiguous overlapping PE sections cover RVA 0x{0:X}.", rva));
            }

            foundIndex = i;
            resolvedOffset = checked((int)((ulong)section.PointerToRawData + offsetInSection));
            resolvedRemaining = (int)Math.Min((ulong)int.MaxValue, backedLength - offsetInSection);
        }

        if (foundIndex < 0)
        {
            throw PeHeaderParser.CreateBadImage(
                _imageLabel,
                string.Format(CultureInfo.InvariantCulture, "RVA range 0x{0:X}+0x{1:X} is not backed by raw section data.", rva, length));
        }

        remainingBytesInSection = resolvedRemaining;
        return resolvedOffset;
    }

    private bool AnySectionOverlapsTail(int fileOffset)
    {
        foreach (var section in _headers.Sections)
        {
            if (section.SizeOfRawData > 0
                && (ulong)section.PointerToRawData + section.SizeOfRawData > (ulong)fileOffset)
            {
                return true;
            }
        }

        return false;
    }

    private BadImageFormatException CreateRvaMismatchException(uint rva, int fileOffset)
    {
        return PeHeaderParser.CreateBadImage(
            _imageLabel,
            string.Format(
                CultureInfo.InvariantCulture,
                "Byte verification failed at RVA 0x{0:X} (file offset 0x{1:X}); binary layout does not match expected original or replacement bytes.",
                rva,
                fileOffset));
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly struct ImportModuleEntry
    {
        public ImportModuleEntry(
            int descriptorFileOffset,
            uint nameRva,
            int nameFileOffset,
            string moduleName,
            uint originalFirstThunk,
            uint firstThunk,
            uint timeDateStamp)
        {
            DescriptorFileOffset = descriptorFileOffset;
            NameRva = nameRva;
            NameFileOffset = nameFileOffset;
            ModuleName = moduleName;
            OriginalFirstThunk = originalFirstThunk;
            FirstThunk = firstThunk;
            TimeDateStamp = timeDateStamp;
        }

        public int DescriptorFileOffset { get; }

        public uint NameRva { get; }

        public int NameFileOffset { get; }

        public string ModuleName { get; }

        public uint OriginalFirstThunk { get; }

        public uint FirstThunk { get; }

        public uint TimeDateStamp { get; }
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly struct NamedImportThunk
    {
        public NamedImportThunk(
            int descriptorFileOffset,
            uint primaryByNameRva,
            uint secondaryByNameRva,
            ushort hint,
            string symbolName)
        {
            DescriptorFileOffset = descriptorFileOffset;
            PrimaryByNameRva = primaryByNameRva;
            SecondaryByNameRva = secondaryByNameRva;
            Hint = hint;
            SymbolName = symbolName;
        }

        public int DescriptorFileOffset { get; }

        public uint PrimaryByNameRva { get; }

        public uint SecondaryByNameRva { get; }

        public ushort Hint { get; }

        public string SymbolName { get; }
    }
}
