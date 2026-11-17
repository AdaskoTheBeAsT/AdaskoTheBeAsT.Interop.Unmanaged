using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
#if NET5_0_OR_GREATER
using System.Runtime.Versioning;
#endif
using System.Text;
using System.Threading;

namespace AdaskoTheBeAsT.Interop.Unmanaged;

#pragma warning disable MA0012 // BadImageFormatException is the documented contract for mapped PE verification failures.
#pragma warning disable SA1121

/// <summary>
/// Provides Windows-only bounded in-memory PE inspection, runtime import address table (IAT) redirection,
/// provenance-verified RVA byte patching, and writable-section clearing on mapped native module handles.
/// </summary>
#if NET5_0_OR_GREATER
[SupportedOSPlatform("windows")]
#endif
public static class LoadedModuleMemory
{
    private const ushort DosSignature = 0x5A4D;
    private const uint PeSignature = 0x00004550;
    private const ushort Pe32Magic = 0x010B;
    private const ushort Pe32PlusMagic = 0x020B;
    private const int DosHeaderSize = 64;
    private const int CoffHeaderSize = 20;
    private const int SectionHeaderSize = 40;
    private const int ImportDescriptorSize = 20;
    private const int MaxSections = 96;
    private const uint MaxDataDirectories = 64;
    private const uint ImageScnMemExecute = 0x20000000;
    private const uint ImageScnMemWrite = 0x80000000;

    private static readonly byte[] ZeroChunk = new byte[4096];

    /// <summary>
    /// Inspects the mapped PE headers of <paramref name="libraryHandle"/> and returns its <see cref="LoadedModuleIdentity"/>.
    /// </summary>
    /// <param name="libraryHandle">Handle for the loaded Windows native module.</param>
    /// <returns>The module's <see cref="LoadedModuleIdentity"/>.</returns>
    public static LoadedModuleIdentity Inspect(SafeLibraryHandle libraryHandle)
    {
        if (libraryHandle is null)
        {
            throw new ArgumentNullException(nameof(libraryHandle));
        }

        var addedRef = false;
        try
        {
            var baseAddress = AcquireMappedModuleBase(libraryHandle, ref addedRef);
            return InspectMappedPe(baseAddress, ResolveModuleLabel(libraryHandle)).Identity;
        }
        finally
        {
            if (addedRef)
            {
                libraryHandle.DangerousRelease();
            }
        }
    }

    /// <summary>
    /// Inspects the mapped PE headers of <paramref name="library"/> and returns its <see cref="LoadedModuleIdentity"/>.
    /// </summary>
    /// <param name="library">Loaded Windows native module.</param>
    /// <returns>The module's <see cref="LoadedModuleIdentity"/>.</returns>
    public static LoadedModuleIdentity Inspect(UnmanagedLibrary library)
    {
        if (library is null)
        {
            throw new ArgumentNullException(nameof(library));
        }

        return Inspect(library.SafeHandle);
    }

    /// <summary>
    /// Verifies that <paramref name="libraryHandle"/> matches <paramref name="expected"/>, throwing <see cref="BadImageFormatException"/> on mismatch.
    /// </summary>
    /// <param name="libraryHandle">Handle for the loaded Windows native module.</param>
    /// <param name="expected">Expected PE header identity.</param>
    public static void VerifyIdentity(SafeLibraryHandle libraryHandle, LoadedModuleIdentity expected)
    {
        var actual = Inspect(libraryHandle);
        EnsureIdentityMatches(actual, expected, ResolveModuleLabel(libraryHandle));
    }

    /// <summary>
    /// Verifies that <paramref name="library"/> matches <paramref name="expected"/>, throwing <see cref="BadImageFormatException"/> on mismatch.
    /// </summary>
    /// <param name="library">Loaded Windows native module.</param>
    /// <param name="expected">Expected PE header identity.</param>
    public static void VerifyIdentity(UnmanagedLibrary library, LoadedModuleIdentity expected)
    {
        if (library is null)
        {
            throw new ArgumentNullException(nameof(library));
        }

        VerifyIdentity(library.SafeHandle, expected);
    }

    /// <summary>
    /// Redirects a named import thunk in <paramref name="libraryHandle"/>'s runtime IAT to <paramref name="replacementFunction"/>,
    /// returning the previous IAT slot pointer. Throws <see cref="EntryPointNotFoundException"/> if the import is absent.
    /// </summary>
    /// <param name="libraryHandle">Handle for the loaded Windows module whose IAT slot will be updated.</param>
    /// <param name="importedModuleName">Case-insensitive ASCII name of the imported module in <c>IMAGE_IMPORT_DESCRIPTOR</c>.</param>
    /// <param name="exportName">Case-sensitive ASCII symbol name in <c>IMAGE_IMPORT_BY_NAME</c>.</param>
    /// <param name="replacementFunction">Non-zero target function pointer.</param>
    /// <returns>The function pointer previously stored in the IAT slot.</returns>
    public static IntPtr RedirectImportFunction(
        SafeLibraryHandle libraryHandle,
        string importedModuleName,
        string exportName,
        IntPtr replacementFunction)
    {
        if (TryRedirectImportFunction(libraryHandle, importedModuleName, exportName, replacementFunction, out var original))
        {
            return original;
        }

        throw new EntryPointNotFoundException(
            $"Unable to find imported symbol '{exportName}' from module '{importedModuleName}' in '{ResolveModuleLabel(libraryHandle)}'.");
    }

    /// <summary>
    /// Redirects a named import thunk in <paramref name="library"/>'s runtime IAT to <paramref name="replacementFunction"/>,
    /// returning the previous IAT slot pointer. Throws <see cref="EntryPointNotFoundException"/> if the import is absent.
    /// </summary>
    /// <param name="library">Loaded Windows module whose IAT slot will be updated.</param>
    /// <param name="importedModuleName">Case-insensitive ASCII name of the imported module in <c>IMAGE_IMPORT_DESCRIPTOR</c>.</param>
    /// <param name="exportName">Case-sensitive ASCII symbol name in <c>IMAGE_IMPORT_BY_NAME</c>.</param>
    /// <param name="replacementFunction">Non-zero target function pointer.</param>
    /// <returns>The function pointer previously stored in the IAT slot.</returns>
    public static IntPtr RedirectImportFunction(
        UnmanagedLibrary library,
        string importedModuleName,
        string exportName,
        IntPtr replacementFunction)
    {
        if (library is null)
        {
            throw new ArgumentNullException(nameof(library));
        }

        return RedirectImportFunction(library.SafeHandle, importedModuleName, exportName, replacementFunction);
    }

    /// <summary>
    /// Attempts to redirect a named import thunk in <paramref name="libraryHandle"/>'s runtime IAT to <paramref name="replacementFunction"/>.
    /// </summary>
    /// <param name="libraryHandle">Handle for the loaded Windows module whose IAT slot will be updated.</param>
    /// <param name="importedModuleName">Case-insensitive ASCII name of the imported module in <c>IMAGE_IMPORT_DESCRIPTOR</c>.</param>
    /// <param name="exportName">Case-sensitive ASCII symbol name in <c>IMAGE_IMPORT_BY_NAME</c>.</param>
    /// <param name="replacementFunction">Non-zero target function pointer.</param>
    /// <param name="originalFunction">Receives the function pointer previously stored in the IAT slot when successful.</param>
    /// <returns><see langword="true"/> if the matching IAT slot was found and redirected; otherwise <see langword="false"/>.</returns>
    public static bool TryRedirectImportFunction(
        SafeLibraryHandle libraryHandle,
        string importedModuleName,
        string exportName,
        IntPtr replacementFunction,
        out IntPtr originalFunction)
    {
        if (libraryHandle is null)
        {
            throw new ArgumentNullException(nameof(libraryHandle));
        }

        PeNameValidator.ValidateModuleNameArgument(importedModuleName, nameof(importedModuleName));
        PeNameValidator.ValidateSymbolNameArgument(exportName, nameof(exportName));
        ValidateReplacementFunction(replacementFunction);

        return TryRedirectImportCore(
            libraryHandle,
            importedModuleName,
            exportName,
            ordinal: 0,
            matchByOrdinal: false,
            replacementFunction,
            out originalFunction);
    }

    /// <summary>
    /// Attempts to redirect a named import thunk in <paramref name="library"/>'s runtime IAT to <paramref name="replacementFunction"/>.
    /// </summary>
    /// <param name="library">Loaded Windows module whose IAT slot will be updated.</param>
    /// <param name="importedModuleName">Case-insensitive ASCII name of the imported module in <c>IMAGE_IMPORT_DESCRIPTOR</c>.</param>
    /// <param name="exportName">Case-sensitive ASCII symbol name in <c>IMAGE_IMPORT_BY_NAME</c>.</param>
    /// <param name="replacementFunction">Non-zero target function pointer.</param>
    /// <param name="originalFunction">Receives the function pointer previously stored in the IAT slot when successful.</param>
    /// <returns><see langword="true"/> if the matching IAT slot was found and redirected; otherwise <see langword="false"/>.</returns>
    public static bool TryRedirectImportFunction(
        UnmanagedLibrary library,
        string importedModuleName,
        string exportName,
        IntPtr replacementFunction,
        out IntPtr originalFunction)
    {
        if (library is null)
        {
            throw new ArgumentNullException(nameof(library));
        }

        return TryRedirectImportFunction(library.SafeHandle, importedModuleName, exportName, replacementFunction, out originalFunction);
    }

    /// <summary>
    /// Attempts to redirect an ordinal import thunk in <paramref name="libraryHandle"/>'s runtime IAT to <paramref name="replacementFunction"/>.
    /// </summary>
    /// <param name="libraryHandle">Handle for the loaded Windows module whose IAT slot will be updated.</param>
    /// <param name="importedModuleName">Case-insensitive ASCII name of the imported module in <c>IMAGE_IMPORT_DESCRIPTOR</c>.</param>
    /// <param name="ordinal">Non-zero 16-bit import ordinal.</param>
    /// <param name="replacementFunction">Non-zero target function pointer.</param>
    /// <param name="originalFunction">Receives the function pointer previously stored in the IAT slot when successful.</param>
    /// <returns><see langword="true"/> if the matching IAT slot was found and redirected; otherwise <see langword="false"/>.</returns>
    public static bool TryRedirectImportOrdinal(
        SafeLibraryHandle libraryHandle,
        string importedModuleName,
        ushort ordinal,
        IntPtr replacementFunction,
        out IntPtr originalFunction)
    {
        if (libraryHandle is null)
        {
            throw new ArgumentNullException(nameof(libraryHandle));
        }

        PeNameValidator.ValidateModuleNameArgument(importedModuleName, nameof(importedModuleName));
        if (ordinal == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ordinal), ordinal, "Import ordinal must be non-zero.");
        }

        ValidateReplacementFunction(replacementFunction);
        return TryRedirectImportCore(
            libraryHandle,
            importedModuleName,
            exportName: null,
            ordinal,
            matchByOrdinal: true,
            replacementFunction,
            out originalFunction);
    }

    /// <summary>
    /// Attempts to redirect an ordinal import thunk in <paramref name="library"/>'s runtime IAT to <paramref name="replacementFunction"/>.
    /// </summary>
    /// <param name="library">Loaded Windows module whose IAT slot will be updated.</param>
    /// <param name="importedModuleName">Case-insensitive ASCII name of the imported module in <c>IMAGE_IMPORT_DESCRIPTOR</c>.</param>
    /// <param name="ordinal">Non-zero 16-bit import ordinal.</param>
    /// <param name="replacementFunction">Non-zero target function pointer.</param>
    /// <param name="originalFunction">Receives the function pointer previously stored in the IAT slot when successful.</param>
    /// <returns><see langword="true"/> if the matching IAT slot was found and redirected; otherwise <see langword="false"/>.</returns>
    public static bool TryRedirectImportOrdinal(
        UnmanagedLibrary library,
        string importedModuleName,
        ushort ordinal,
        IntPtr replacementFunction,
        out IntPtr originalFunction)
    {
        if (library is null)
        {
            throw new ArgumentNullException(nameof(library));
        }

        return TryRedirectImportOrdinal(library.SafeHandle, importedModuleName, ordinal, replacementFunction, out originalFunction);
    }

    /// <summary>
    /// Verifies <paramref name="expectedIdentity"/> and that the mapped bytes at <paramref name="rva"/> match
    /// <paramref name="expectedCurrentBytes"/> (or <paramref name="replacementBytes"/> for idempotence), then writes
    /// <paramref name="replacementBytes"/> while temporarily adjusting page protection when required.
    /// </summary>
    /// <param name="libraryHandle">Handle for the loaded Windows module.</param>
    /// <param name="expectedIdentity">Expected PE module identity.</param>
    /// <param name="rva">Non-zero relative virtual address inside a mapped PE section.</param>
    /// <param name="expectedCurrentBytes">Expected unpatched bytes at <paramref name="rva"/>.</param>
    /// <param name="replacementBytes">Replacement bytes of the exact same length.</param>
    /// <param name="flushInstructionCache">When <see langword="true"/>, flushes the process instruction cache even on non-executable sections.</param>
    public static void WriteVerifiedBytesAtRva(
        SafeLibraryHandle libraryHandle,
        LoadedModuleIdentity expectedIdentity,
        uint rva,
        byte[] expectedCurrentBytes,
        byte[] replacementBytes,
        bool flushInstructionCache = false)
    {
        if (libraryHandle is null)
        {
            throw new ArgumentNullException(nameof(libraryHandle));
        }

        if (expectedCurrentBytes is null)
        {
            throw new ArgumentNullException(nameof(expectedCurrentBytes));
        }

        if (replacementBytes is null)
        {
            throw new ArgumentNullException(nameof(replacementBytes));
        }

        ValidateWriteBytesArguments(rva, expectedCurrentBytes.Length, replacementBytes.Length);
        WriteVerifiedBytesCore(libraryHandle, expectedIdentity, rva, expectedCurrentBytes, replacementBytes, flushInstructionCache);
    }

    /// <summary>
    /// Verifies <paramref name="expectedIdentity"/> and writes <paramref name="replacementBytes"/> at <paramref name="rva"/> on <paramref name="library"/>.
    /// </summary>
    /// <param name="library">Loaded Windows module.</param>
    /// <param name="expectedIdentity">Expected PE module identity.</param>
    /// <param name="rva">Non-zero relative virtual address inside a mapped PE section.</param>
    /// <param name="expectedCurrentBytes">Expected unpatched bytes at <paramref name="rva"/>.</param>
    /// <param name="replacementBytes">Replacement bytes of the exact same length.</param>
    /// <param name="flushInstructionCache">When <see langword="true"/>, flushes the process instruction cache even on non-executable sections.</param>
    public static void WriteVerifiedBytesAtRva(
        UnmanagedLibrary library,
        LoadedModuleIdentity expectedIdentity,
        uint rva,
        byte[] expectedCurrentBytes,
        byte[] replacementBytes,
        bool flushInstructionCache = false)
    {
        if (library is null)
        {
            throw new ArgumentNullException(nameof(library));
        }

        WriteVerifiedBytesAtRva(library.SafeHandle, expectedIdentity, rva, expectedCurrentBytes, replacementBytes, flushInstructionCache);
    }

#if NET5_0_OR_GREATER
    /// <summary>
    /// Verifies <paramref name="expectedIdentity"/> and that the mapped bytes at <paramref name="rva"/> match
    /// <paramref name="expectedCurrentBytes"/> (or <paramref name="replacementBytes"/> for idempotence), then writes
    /// <paramref name="replacementBytes"/> while temporarily adjusting page protection when required.
    /// </summary>
    /// <param name="libraryHandle">Handle for the loaded Windows module.</param>
    /// <param name="expectedIdentity">Expected PE module identity.</param>
    /// <param name="rva">Non-zero relative virtual address inside a mapped PE section.</param>
    /// <param name="expectedCurrentBytes">Expected unpatched byte span at <paramref name="rva"/>.</param>
    /// <param name="replacementBytes">Replacement byte span of the exact same length.</param>
    /// <param name="flushInstructionCache">When <see langword="true"/>, flushes the process instruction cache even on non-executable sections.</param>
    public static void WriteVerifiedBytesAtRva(
        SafeLibraryHandle libraryHandle,
        LoadedModuleIdentity expectedIdentity,
        uint rva,
        ReadOnlySpan<byte> expectedCurrentBytes,
        ReadOnlySpan<byte> replacementBytes,
        bool flushInstructionCache = false)
    {
        if (libraryHandle is null)
        {
            throw new ArgumentNullException(nameof(libraryHandle));
        }

        ValidateWriteBytesArguments(rva, expectedCurrentBytes.Length, replacementBytes.Length);
        WriteVerifiedBytesCore(
            libraryHandle,
            expectedIdentity,
            rva,
            expectedCurrentBytes.ToArray(),
            replacementBytes.ToArray(),
            flushInstructionCache);
    }

    /// <summary>
    /// Verifies <paramref name="expectedIdentity"/> and writes <paramref name="replacementBytes"/> at <paramref name="rva"/> on <paramref name="library"/>.
    /// </summary>
    /// <param name="library">Loaded Windows module.</param>
    /// <param name="expectedIdentity">Expected PE module identity.</param>
    /// <param name="rva">Non-zero relative virtual address inside a mapped PE section.</param>
    /// <param name="expectedCurrentBytes">Expected unpatched byte span at <paramref name="rva"/>.</param>
    /// <param name="replacementBytes">Replacement byte span of the exact same length.</param>
    /// <param name="flushInstructionCache">When <see langword="true"/>, flushes the process instruction cache even on non-executable sections.</param>
    public static void WriteVerifiedBytesAtRva(
        UnmanagedLibrary library,
        LoadedModuleIdentity expectedIdentity,
        uint rva,
        ReadOnlySpan<byte> expectedCurrentBytes,
        ReadOnlySpan<byte> replacementBytes,
        bool flushInstructionCache = false)
    {
        if (library is null)
        {
            throw new ArgumentNullException(nameof(library));
        }

        WriteVerifiedBytesAtRva(library.SafeHandle, expectedIdentity, rva, expectedCurrentBytes, replacementBytes, flushInstructionCache);
    }
#endif

    /// <summary>
    /// Verifies <paramref name="expectedIdentity"/> and zeroes <paramref name="byteCount"/> bytes at <paramref name="rva"/>
    /// inside a non-executable writable PE section (<c>.data</c> or <c>.bss</c>).
    /// </summary>
    /// <param name="libraryHandle">Handle for the loaded Windows module.</param>
    /// <param name="expectedIdentity">Expected PE module identity.</param>
    /// <param name="rva">Non-zero relative virtual address inside a writable non-executable section.</param>
    /// <param name="byteCount">Positive number of bytes to clear.</param>
    public static void ClearWritableBytesAtRva(
        SafeLibraryHandle libraryHandle,
        LoadedModuleIdentity expectedIdentity,
        uint rva,
        int byteCount)
    {
        if (libraryHandle is null)
        {
            throw new ArgumentNullException(nameof(libraryHandle));
        }

        if (rva == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rva), rva, "RVA must be non-zero.");
        }

        if (byteCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteCount), byteCount, "Byte count must be greater than zero.");
        }

        var addedRef = false;
        try
        {
            var baseAddress = AcquireMappedModuleBase(libraryHandle, ref addedRef);
            var label = ResolveModuleLabel(libraryHandle);
            var pe = InspectMappedPe(baseAddress, label);
            EnsureIdentityMatches(pe.Identity, expectedIdentity, label);

            var section = FindContainingMappedSection(pe, rva, byteCount, label);
            if ((section.Characteristics & ImageScnMemWrite) == 0
                || (section.Characteristics & ImageScnMemExecute) != 0)
            {
                throw new InvalidOperationException(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "ClearWritableBytesAtRva requires RVA range 0x{0:X}+0x{1:X} in '{2}' to reside in a non-executable writable section (.data/.bss); section characteristics are 0x{3:X8}.",
                        rva,
                        byteCount,
                        label,
                        section.Characteristics));
            }

            ZeroMappedBytes(AddRva(baseAddress, rva), byteCount);
            Thread.MemoryBarrier();
        }
        finally
        {
            if (addedRef)
            {
                libraryHandle.DangerousRelease();
            }
        }
    }

    /// <summary>
    /// Verifies <paramref name="expectedIdentity"/> and zeroes <paramref name="byteCount"/> bytes at <paramref name="rva"/>
    /// inside a non-executable writable PE section (<c>.data</c> or <c>.bss</c>) of <paramref name="library"/>.
    /// </summary>
    /// <param name="library">Loaded Windows module.</param>
    /// <param name="expectedIdentity">Expected PE module identity.</param>
    /// <param name="rva">Non-zero relative virtual address inside a writable non-executable section.</param>
    /// <param name="byteCount">Positive number of bytes to clear.</param>
    public static void ClearWritableBytesAtRva(
        UnmanagedLibrary library,
        LoadedModuleIdentity expectedIdentity,
        uint rva,
        int byteCount)
    {
        if (library is null)
        {
            throw new ArgumentNullException(nameof(library));
        }

        ClearWritableBytesAtRva(library.SafeHandle, expectedIdentity, rva, byteCount);
    }

    private static void ValidateReplacementFunction(IntPtr replacementFunction)
    {
        if (replacementFunction == IntPtr.Zero)
        {
            throw new ArgumentException("Replacement function pointer cannot be zero.", nameof(replacementFunction));
        }
    }

    private static void ValidateWriteBytesArguments(
        uint rva,
        int expectedLength,
        int replacementLength,
        string expectedParamName = "expectedCurrentBytes",
        string replacementParamName = "replacementBytes")
    {
        if (rva == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rva), rva, "RVA must be non-zero.");
        }

        if (expectedLength <= 0)
        {
            throw new ArgumentException("Expected byte sequence cannot be empty.", expectedParamName);
        }

        if (replacementLength != expectedLength)
        {
            throw new ArgumentOutOfRangeException(
                replacementParamName,
                replacementLength,
                $"Replacement byte length ({replacementLength}) must equal expected byte length ({expectedLength}).");
        }
    }

    private static IntPtr AcquireMappedModuleBase(SafeLibraryHandle libraryHandle, ref bool addedRef)
    {
        libraryHandle.DangerousAddRef(ref addedRef);
        if (libraryHandle.IsInvalid)
        {
            throw new ObjectDisposedException(nameof(SafeLibraryHandle));
        }

        if (!NativeLoader.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "LoadedModuleMemory runtime PE inspection and IAT/memory patching are supported only on Windows.");
        }

#pragma warning disable S3869
        var rawHandle = libraryHandle.DangerousGetHandle();
#pragma warning restore S3869
        if ((rawHandle.ToInt64() & 3L) != 0L)
        {
            throw new InvalidOperationException(
                "LoadedModuleMemory requires an executable mapped module handle and rejects LOAD_LIBRARY_AS_DATAFILE or LOAD_LIBRARY_AS_IMAGE_RESOURCE mappings.");
        }

        return rawHandle;
    }

    private static string ResolveModuleLabel(SafeLibraryHandle libraryHandle)
    {
        var moduleName = libraryHandle.ModuleName;
        return moduleName is null || string.IsNullOrWhiteSpace(moduleName)
            ? "<loaded-module>"
            : moduleName;
    }

    private static void EnsureIdentityMatches(LoadedModuleIdentity actual, LoadedModuleIdentity expected, string label)
    {
        if (!actual.Equals(expected))
        {
            throw PeHeaderParser.CreateBadImage(
                label,
                $"Loaded module identity ({actual}) does not match expected identity ({expected}).");
        }
    }

    private static bool TryRedirectImportCore(
        SafeLibraryHandle libraryHandle,
        string importedModuleName,
        string? exportName,
        ushort ordinal,
        bool matchByOrdinal,
        IntPtr replacementFunction,
        out IntPtr originalFunction)
    {
        var addedRef = false;
        try
        {
            var baseAddress = AcquireMappedModuleBase(libraryHandle, ref addedRef);
            var label = ResolveModuleLabel(libraryHandle);
            var pe = InspectMappedPe(baseAddress, label);
            var matches = FindMatchingIatSlots(baseAddress, pe, importedModuleName, exportName, ordinal, matchByOrdinal, label);
            if (matches.Count == 0)
            {
                originalFunction = IntPtr.Zero;
                return false;
            }

            if (matches.Count > 1)
            {
                throw PeHeaderParser.CreateBadImage(
                    label,
                    $"Ambiguous duplicate IAT slots matched imported module '{importedModuleName}'.");
            }

            originalFunction = PatchIatPointerSlot(matches[0], replacementFunction);
            return true;
        }
        finally
        {
            if (addedRef)
            {
                libraryHandle.DangerousRelease();
            }
        }
    }

    private static List<IntPtr> FindMatchingIatSlots(
        IntPtr baseAddress,
        MappedPeInfo pe,
        string importedModuleName,
        string? exportName,
        ushort ordinal,
        bool matchByOrdinal,
        string label)
    {
        var matches = new List<IntPtr>(1);
        if (pe.ImportTableRva == 0 || pe.ImportTableSize < ImportDescriptorSize)
        {
            return matches;
        }

        var cursorRva = pe.ImportTableRva;
        while (true)
        {
            EnsureMappedRvaRange(pe, cursorRva, ImportDescriptorSize, label);
            if (IsMappedDescriptorZero(baseAddress, cursorRva))
            {
                return matches;
            }

            var originalFirstThunk = ReadUInt32(baseAddress, cursorRva);
            var nameRva = ReadUInt32(baseAddress, cursorRva + 12);
            var firstThunk = ReadUInt32(baseAddress, cursorRva + 16);
            if (nameRva == 0 || (originalFirstThunk == 0 && firstThunk == 0))
            {
                throw PeHeaderParser.CreateBadImage(label, "Malformed IMAGE_IMPORT_DESCRIPTOR in mapped module.");
            }

            var currentModuleName = ReadMappedAsciiString(baseAddress, pe, nameRva, allowSpaces: true, label);
            if (string.Equals(currentModuleName, importedModuleName, StringComparison.OrdinalIgnoreCase))
            {
                if (originalFirstThunk == 0 || firstThunk == 0)
                {
                    throw PeHeaderParser.CreateBadImage(
                        label,
                        $"Import descriptor for '{currentModuleName}' lacks OriginalFirstThunk metadata required for runtime IAT redirection.");
                }

                CollectDescriptorMatches(
                    baseAddress,
                    pe,
                    originalFirstThunk,
                    firstThunk,
                    exportName,
                    ordinal,
                    matchByOrdinal,
                    label,
                    matches);
            }

            cursorRva = checked(cursorRva + ImportDescriptorSize);
        }
    }

    private static void CollectDescriptorMatches(
        IntPtr baseAddress,
        MappedPeInfo pe,
        uint originalFirstThunk,
        uint firstThunk,
        string? exportName,
        ushort ordinal,
        bool matchByOrdinal,
        string label,
        List<IntPtr> matches)
    {
        var pointerSize = (uint)IntPtr.Size;
        var slotIndex = 0u;
        while (true)
        {
            var iltSlotRva = checked(originalFirstThunk + checked(slotIndex * pointerSize));
            var iatSlotRva = checked(firstThunk + checked(slotIndex * pointerSize));
            EnsureMappedRvaRange(pe, iltSlotRva, IntPtr.Size, label);
            EnsureMappedRvaRange(pe, iatSlotRva, IntPtr.Size, label);

            var iatAddress = AddRva(baseAddress, iatSlotRva);
            if ((iatAddress.ToInt64() & (IntPtr.Size - 1)) != 0)
            {
                throw PeHeaderParser.CreateBadImage(label, "Unaligned IAT pointer slot in mapped module.");
            }

            if (!TryReadIltEntry(baseAddress, iltSlotRva, out var isOrdinal, out var thunkOrdinal, out var byNameRva, label))
            {
                return;
            }

            if (matchByOrdinal)
            {
                if (isOrdinal && thunkOrdinal == ordinal)
                {
                    matches.Add(iatAddress);
                }
            }
            else if (!isOrdinal)
            {
                EnsureMappedRvaRange(pe, byNameRva, 3, label);
                var symbolName = ReadMappedAsciiString(baseAddress, pe, checked(byNameRva + 2u), allowSpaces: false, label);
                if (string.Equals(symbolName, exportName, StringComparison.Ordinal))
                {
                    matches.Add(iatAddress);
                }
            }

            slotIndex = checked(slotIndex + 1u);
        }
    }

    private static bool TryReadIltEntry(
        IntPtr baseAddress,
        uint iltSlotRva,
        out bool isOrdinal,
        out ushort ordinal,
        out uint byNameRva,
        string label)
    {
        if (IntPtr.Size == 8)
        {
            var raw64 = unchecked((ulong)Marshal.ReadInt64(AddRva(baseAddress, iltSlotRva)));
            if (raw64 == 0)
            {
                isOrdinal = false;
                ordinal = 0;
                byNameRva = 0;
                return false;
            }

            if ((raw64 & 0x8000000000000000UL) != 0)
            {
                isOrdinal = true;
                ordinal = (ushort)(raw64 & 0xFFFFUL);
                byNameRva = 0;
                return true;
            }

            if (raw64 > uint.MaxValue)
            {
                throw PeHeaderParser.CreateBadImage(label, "PE32+ ILT import-by-name RVA overflows 32-bit RVA bounds.");
            }

            isOrdinal = false;
            ordinal = 0;
            byNameRva = (uint)raw64;
            return true;
        }

        var raw32 = ReadUInt32(baseAddress, iltSlotRva);
        if (raw32 == 0)
        {
            isOrdinal = false;
            ordinal = 0;
            byNameRva = 0;
            return false;
        }

        if ((raw32 & 0x80000000u) != 0)
        {
            isOrdinal = true;
            ordinal = (ushort)(raw32 & 0xFFFFu);
            byNameRva = 0;
            return true;
        }

        isOrdinal = false;
        ordinal = 0;
        byNameRva = raw32;
        return true;
    }

    private static IntPtr PatchIatPointerSlot(IntPtr slotAddress, IntPtr replacementFunction)
    {
        if (!WindowsMemoryNativeMethods.VirtualProtect(
                slotAddress,
                (UIntPtr)IntPtr.Size,
                WindowsMemoryNativeMethods.PageReadWrite,
                out var oldProtect))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "VirtualProtect failed while unlocking mapped IAT slot.");
        }

        try
        {
            Thread.MemoryBarrier();
            var previous = Marshal.ReadIntPtr(slotAddress);
            Marshal.WriteIntPtr(slotAddress, replacementFunction);
            Thread.MemoryBarrier();
            return previous;
        }
        finally
        {
            _ = WindowsMemoryNativeMethods.VirtualProtect(
                slotAddress,
                (UIntPtr)IntPtr.Size,
                oldProtect,
                out _);
        }
    }

    private static void WriteVerifiedBytesCore(
        SafeLibraryHandle libraryHandle,
        LoadedModuleIdentity expectedIdentity,
        uint rva,
        byte[] expectedCurrentBytes,
        byte[] replacementBytes,
        bool flushInstructionCache)
    {
        var addedRef = false;
        try
        {
            var baseAddress = AcquireMappedModuleBase(libraryHandle, ref addedRef);
            var label = ResolveModuleLabel(libraryHandle);
            var pe = InspectMappedPe(baseAddress, label);
            EnsureIdentityMatches(pe.Identity, expectedIdentity, label);

            var section = FindContainingMappedSection(pe, rva, expectedCurrentBytes.Length, label);
            var targetAddress = AddRva(baseAddress, rva);
            var currentBytes = new byte[expectedCurrentBytes.Length];
            Marshal.Copy(targetAddress, currentBytes, 0, currentBytes.Length);

            if (BytesEqual(currentBytes, expectedCurrentBytes))
            {
                if (!BytesEqual(expectedCurrentBytes, replacementBytes))
                {
                    WriteProtectedBytes(targetAddress, replacementBytes, section.Characteristics, flushInstructionCache);
                }
                else if (flushInstructionCache)
                {
                    FlushCpuCache(targetAddress, replacementBytes.Length);
                }

                return;
            }

            if (BytesEqual(currentBytes, replacementBytes))
            {
                if (flushInstructionCache)
                {
                    FlushCpuCache(targetAddress, replacementBytes.Length);
                }

                return;
            }

            throw PeHeaderParser.CreateBadImage(
                label,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "In-memory byte verification failed at RVA 0x{0:X}; mapped bytes match neither expected current bytes nor replacement bytes.",
                    rva));
        }
        finally
        {
            if (addedRef)
            {
                libraryHandle.DangerousRelease();
            }
        }
    }

    private static void WriteProtectedBytes(
        IntPtr targetAddress,
        byte[] replacementBytes,
        uint sectionCharacteristics,
        bool flushInstructionCache)
    {
        var isExecutable = (sectionCharacteristics & ImageScnMemExecute) != 0;
        var tempProtect = isExecutable
            ? WindowsMemoryNativeMethods.PageExecuteReadWrite
            : WindowsMemoryNativeMethods.PageReadWrite;

        if (!WindowsMemoryNativeMethods.VirtualProtect(
                targetAddress,
                (UIntPtr)replacementBytes.Length,
                tempProtect,
                out var oldProtect))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "VirtualProtect failed while unlocking mapped RVA range.");
        }

        try
        {
            Marshal.Copy(replacementBytes, 0, targetAddress, replacementBytes.Length);
            Thread.MemoryBarrier();
        }
        finally
        {
            _ = WindowsMemoryNativeMethods.VirtualProtect(
                targetAddress,
                (UIntPtr)replacementBytes.Length,
                oldProtect,
                out _);
        }

        if (isExecutable || flushInstructionCache)
        {
            FlushCpuCache(targetAddress, replacementBytes.Length);
        }
    }

    private static void FlushCpuCache(IntPtr targetAddress, int length)
    {
        _ = WindowsMemoryNativeMethods.FlushInstructionCache(
            WindowsMemoryNativeMethods.GetCurrentProcess(),
            targetAddress,
            (UIntPtr)length);
    }

    private static MappedPeInfo InspectMappedPe(IntPtr baseAddress, string label)
    {
        var committedHeaderBytes = QueryCommittedRegionLengthFrom(baseAddress, label);
        ReadAndValidateMappedCoffHeader(
            baseAddress,
            committedHeaderBytes,
            label,
            out var coffOffset,
            out var machine,
            out var sectionCount,
            out var timeDateStamp,
            out var optionalHeaderSize);

        var optOffset = coffOffset + (uint)CoffHeaderSize;
        var minOptSize = IntPtr.Size == 8 ? 112u : 96u;
        if (optionalHeaderSize < minOptSize || (ulong)optOffset + optionalHeaderSize > committedHeaderBytes)
        {
            throw PeHeaderParser.CreateBadImage(label, "Mapped PE optional header is truncated.");
        }

        var magic = ReadUInt16(baseAddress, optOffset);
        var expectedMagic = IntPtr.Size == 8 ? Pe32PlusMagic : Pe32Magic;
        if (magic != expectedMagic)
        {
            throw PeHeaderParser.CreateBadImage(
                label,
                string.Format(CultureInfo.InvariantCulture, "Mapped PE optional header magic 0x{0:X4} does not match process bitness.", magic));
        }

        var sizeOfImage = ReadUInt32(baseAddress, optOffset + 56u);
        var sizeOfHeaders = ReadUInt32(baseAddress, optOffset + 60u);
        var numRvaOffset = IntPtr.Size == 8 ? optOffset + 108u : optOffset + 92u;
        var dataDirOffset = IntPtr.Size == 8 ? optOffset + 112u : optOffset + 96u;
        var numDataDirs = ReadUInt32(baseAddress, numRvaOffset);
        if (numDataDirs > MaxDataDirectories
            || (ulong)dataDirOffset + ((ulong)numDataDirs * 8UL) > (ulong)optOffset + optionalHeaderSize)
        {
            throw PeHeaderParser.CreateBadImage(label, "Mapped PE data directory count exceeds optional header bounds.");
        }

        var sectionTableOffset = optOffset + optionalHeaderSize;
        var sectionTableEnd = (ulong)sectionTableOffset + ((ulong)sectionCount * SectionHeaderSize);
        if (sectionTableEnd > committedHeaderBytes || sectionTableEnd > sizeOfHeaders || sizeOfImage <= sizeOfHeaders)
        {
            throw PeHeaderParser.CreateBadImage(label, "Mapped PE section table exceeds header or image bounds.");
        }

        var importRva = numDataDirs > 1 ? ReadUInt32(baseAddress, dataDirOffset + 8u) : 0u;
        var importSize = numDataDirs > 1 ? ReadUInt32(baseAddress, dataDirOffset + 12u) : 0u;
        var sections = ParseMappedSections(baseAddress, sectionTableOffset, sectionCount, sizeOfHeaders, sizeOfImage, label);
        return new MappedPeInfo(
            new LoadedModuleIdentity(machine, timeDateStamp, sizeOfImage),
            sizeOfHeaders,
            sizeOfImage,
            importRva,
            importSize,
            sections);
    }

    private static void ReadAndValidateMappedCoffHeader(
        IntPtr baseAddress,
        ulong committedHeaderBytes,
        string label,
        out uint coffOffset,
        out ushort machine,
        out ushort sectionCount,
        out uint timeDateStamp,
        out ushort optionalHeaderSize)
    {
        if (committedHeaderBytes < DosHeaderSize || ReadUInt16(baseAddress, 0) != DosSignature)
        {
            throw PeHeaderParser.CreateBadImage(label, "Mapped module is missing DOS 'MZ' signature.");
        }

        var peOffsetSigned = Marshal.ReadInt32(baseAddress, 0x3C);
        if (peOffsetSigned < DosHeaderSize || (ulong)peOffsetSigned + 4UL + CoffHeaderSize > committedHeaderBytes)
        {
            throw PeHeaderParser.CreateBadImage(label, "Mapped module has an out-of-range e_lfanew PE header offset.");
        }

        var peOffset = (uint)peOffsetSigned;
        if (ReadUInt32(baseAddress, peOffset) != PeSignature)
        {
            throw PeHeaderParser.CreateBadImage(label, "Mapped module is missing 'PE\\0\\0' signature.");
        }

        coffOffset = peOffset + 4u;
        machine = ReadUInt16(baseAddress, coffOffset);
        sectionCount = ReadUInt16(baseAddress, coffOffset + 2u);
        timeDateStamp = ReadUInt32(baseAddress, coffOffset + 4u);
        optionalHeaderSize = ReadUInt16(baseAddress, coffOffset + 16u);

        if (machine != PeMachine.CurrentProcessArchitecture)
        {
            throw PeHeaderParser.CreateBadImage(
                label,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Mapped module COFF machine 0x{0:X4} does not match process architecture 0x{1:X4}.",
                    machine,
                    PeMachine.CurrentProcessArchitecture));
        }

        if (sectionCount is 0 or > MaxSections)
        {
            throw PeHeaderParser.CreateBadImage(label, $"Unsupported mapped PE section count {sectionCount}.");
        }
    }

    private static MappedSection[] ParseMappedSections(
        IntPtr baseAddress,
        uint sectionTableOffset,
        ushort sectionCount,
        uint sizeOfHeaders,
        uint sizeOfImage,
        string label)
    {
        var sections = new MappedSection[sectionCount];
        for (var i = 0; i < sectionCount; i++)
        {
            var secOffset = sectionTableOffset + (uint)(i * SectionHeaderSize);
            var virtualSize = ReadUInt32(baseAddress, secOffset + 8u);
            var virtualAddress = ReadUInt32(baseAddress, secOffset + 12u);
            var sizeOfRawData = ReadUInt32(baseAddress, secOffset + 16u);
            var characteristics = ReadUInt32(baseAddress, secOffset + 36u);
            var mappedLength = virtualSize != 0 ? virtualSize : sizeOfRawData;

            if (virtualAddress < sizeOfHeaders
                || virtualAddress >= sizeOfImage
                || (ulong)virtualAddress + mappedLength > sizeOfImage)
            {
                throw PeHeaderParser.CreateBadImage(label, $"Mapped section #{i} exceeds SizeOfImage bounds.");
            }

            sections[i] = new MappedSection(virtualAddress, mappedLength, characteristics);
        }

        return sections;
    }

    private static MappedSection FindContainingMappedSection(
        MappedPeInfo pe,
        uint rva,
        int length,
        string label)
    {
        EnsureMappedRvaRange(pe, rva, length, label);
        for (var i = 0; i < pe.Sections.Length; i++)
        {
            var sec = pe.Sections[i];
            if (sec.MappedLength == 0 || rva < sec.VirtualAddress)
            {
                continue;
            }

            var offsetInSection = (ulong)rva - sec.VirtualAddress;
            if (offsetInSection < sec.MappedLength && offsetInSection + (ulong)length <= sec.MappedLength)
            {
                return sec;
            }
        }

        throw PeHeaderParser.CreateBadImage(
            label,
            string.Format(CultureInfo.InvariantCulture, "RVA range 0x{0:X}+0x{1:X} does not lie completely within a mapped PE section.", rva, length));
    }

    private static void EnsureMappedRvaRange(MappedPeInfo pe, uint rva, int length, string label)
    {
        if (rva < pe.SizeOfHeaders
            || length <= 0
            || (ulong)rva + (ulong)length > pe.SizeOfImage)
        {
            throw PeHeaderParser.CreateBadImage(
                label,
                string.Format(CultureInfo.InvariantCulture, "RVA range 0x{0:X}+0x{1:X} falls outside mapped image bounds.", rva, length));
        }
    }

    private static ulong QueryCommittedRegionLengthFrom(IntPtr address, string label)
    {
        var infoSize = (UIntPtr)Marshal.SizeOf<WindowsMemoryNativeMethods.MemoryBasicInformation>();
        var queried = WindowsMemoryNativeMethods.VirtualQuery(address, out var mbi, infoSize);
        if (queried == UIntPtr.Zero || mbi.State != WindowsMemoryNativeMethods.MemCommit)
        {
            throw PeHeaderParser.CreateBadImage(label, "Module base address does not point to committed virtual memory.");
        }

        var offsetIntoRegion = (ulong)(address.ToInt64() - mbi.BaseAddress.ToInt64());
        var regionSize = IntPtr.Size == 8
            ? unchecked((ulong)mbi.RegionSize.ToInt64())
            : unchecked((uint)mbi.RegionSize.ToInt32());
        return regionSize > offsetIntoRegion ? regionSize - offsetIntoRegion : 0UL;
    }

    private static string ReadMappedAsciiString(
        IntPtr baseAddress,
        MappedPeInfo pe,
        uint stringRva,
        bool allowSpaces,
        string label)
    {
        EnsureMappedRvaRange(pe, stringRva, 1, label);
        var maxBytes = (int)Math.Min(1024UL, (ulong)pe.SizeOfImage - stringRva);
        var minChar = allowSpaces ? 0x20 : 0x21;
        var bytes = new byte[maxBytes];
        var length = 0;
        while (length < maxBytes)
        {
            var b = Marshal.ReadByte(AddRva(baseAddress, stringRva), length);
            if (b == 0)
            {
                if (length == 0)
                {
                    throw PeHeaderParser.CreateBadImage(
                        label,
                        string.Format(CultureInfo.InvariantCulture, "Empty ASCII string in mapped PE table at RVA 0x{0:X}.", stringRva));
                }

                return Encoding.ASCII.GetString(bytes, 0, length);
            }

            if (b < minChar || b > 0x7E)
            {
                throw PeHeaderParser.CreateBadImage(
                    label,
                    string.Format(CultureInfo.InvariantCulture, "Invalid byte 0x{0:X2} in mapped PE string at RVA 0x{1:X}.", b, stringRva));
            }

            bytes[length] = b;
            length++;
        }

        throw PeHeaderParser.CreateBadImage(
            label,
            string.Format(CultureInfo.InvariantCulture, "Unterminated ASCII string in mapped PE table at RVA 0x{0:X}.", stringRva));
    }

    private static bool IsMappedDescriptorZero(IntPtr baseAddress, uint descriptorRva)
    {
        var ptr = AddRva(baseAddress, descriptorRva);
        for (var i = 0; i < ImportDescriptorSize; i += 4)
        {
            if (Marshal.ReadInt32(ptr, i) != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static void ZeroMappedBytes(IntPtr targetAddress, int byteCount)
    {
        var offset = 0;
        while (offset < byteCount)
        {
            var chunk = Math.Min(ZeroChunk.Length, byteCount - offset);
            Marshal.Copy(ZeroChunk, 0, AddOffset(targetAddress, offset), chunk);
            offset += chunk;
        }
    }

    private static bool BytesEqual(byte[] first, byte[] second)
    {
        for (var i = 0; i < first.Length; i++)
        {
            if (first[i] != second[i])
            {
                return false;
            }
        }

        return true;
    }

    private static ushort ReadUInt16(IntPtr baseAddress, uint rva)
    {
        return unchecked((ushort)Marshal.ReadInt16(AddRva(baseAddress, rva)));
    }

    private static uint ReadUInt32(IntPtr baseAddress, uint rva)
    {
        return unchecked((uint)Marshal.ReadInt32(AddRva(baseAddress, rva)));
    }

    private static IntPtr AddRva(IntPtr baseAddress, uint rva)
    {
        return IntPtr.Size == 8
            ? new IntPtr(checked(baseAddress.ToInt64() + rva))
            : new IntPtr(checked((int)(unchecked((uint)baseAddress.ToInt32()) + rva)));
    }

    private static IntPtr AddOffset(IntPtr address, int byteOffset)
    {
        return IntPtr.Size == 8
            ? new IntPtr(checked(address.ToInt64() + byteOffset))
            : new IntPtr(checked(address.ToInt32() + byteOffset));
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly struct MappedSection
    {
        public MappedSection(uint virtualAddress, uint mappedLength, uint characteristics)
        {
            VirtualAddress = virtualAddress;
            MappedLength = mappedLength;
            Characteristics = characteristics;
        }

        public uint VirtualAddress { get; }

        public uint MappedLength { get; }

        public uint Characteristics { get; }
    }

    private sealed class MappedPeInfo
    {
        public MappedPeInfo(
            LoadedModuleIdentity identity,
            uint sizeOfHeaders,
            uint sizeOfImage,
            uint importTableRva,
            uint importTableSize,
            MappedSection[] sections)
        {
            Identity = identity;
            SizeOfHeaders = sizeOfHeaders;
            SizeOfImage = sizeOfImage;
            ImportTableRva = importTableRva;
            ImportTableSize = importTableSize;
            Sections = sections;
        }

        public LoadedModuleIdentity Identity { get; }

        public uint SizeOfHeaders { get; }

        public uint SizeOfImage { get; }

        public uint ImportTableRva { get; }

        public uint ImportTableSize { get; }

        public MappedSection[] Sections { get; }
    }
}
