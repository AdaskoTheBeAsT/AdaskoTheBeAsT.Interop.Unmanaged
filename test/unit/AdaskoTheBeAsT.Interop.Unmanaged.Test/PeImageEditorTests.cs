using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Unmanaged.Test;

#pragma warning disable SA1121

[Trait("Category", "Native")]
public class PeImageEditorTests
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int CallIntFn();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int UnaryIntFn(int value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr GetPtrFn();

    [Fact]
    [SuppressMessage("Security", "SCS0018", Justification = "Reads fixture-owned PE test binaries.")]
    public void RealPe32AndPe32PlusBinaries_ExposeExpectedHeadersAndImportTables()
    {
        if (TestHelpers.SkipIfNotWindows())
        {
            return;
        }

        using var fixture = new NativeFixture();
        foreach (var (archPath, expectedMachine, expectedPe32Plus) in EnumerateArchitectures(fixture, "isoparent"))
        {
            var info = new FileInfo(archPath);
            var editor = PeImageEditor.FromFile(
                archPath,
                new PeImageValidationOptions
                {
                    ExpectedFileLength = info.Length,
                    ExpectedMachine = expectedMachine,
                });

            editor.Machine.Should().Be(expectedMachine);
            editor.IsPe32Plus.Should().Be(expectedPe32Plus);
            editor.SizeOfImage.Should().BeGreaterThan(0u);
            editor.GetImportedModuleNames()
                .Should()
                .Contain(m => string.Equals(m, "isochild.dll", StringComparison.OrdinalIgnoreCase));
            editor.GetDelayImportedModuleNames()
                .Should()
                .Contain(m => string.Equals(m, "isodelay.dll", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void RewriteImportAndDelayImportModuleNames_SupportsShorterIdempotentRewritesAcrossPe32AndPe32Plus()
    {
        if (TestHelpers.SkipIfNotWindows())
        {
            return;
        }

        using var fixture = new NativeFixture();
        foreach (var (archPath, _, _) in EnumerateArchitectures(fixture, "isoparent"))
        {
            var editor = PeImageEditor.FromFile(archPath);

            Action ordinaryOnDelay = () => editor.RewriteImportModuleName("isodelay.dll", "isd0.dll");
            ordinaryOnDelay.Should().Throw<BadImageFormatException>();

            editor.RewriteImportModuleName("isochild.dll", "isch0.dll");
            editor.RewriteImportModuleName("isochild.dll", "isch0.dll");
            editor.GetImportedModuleNames().Should().Contain("isch0.dll");

            editor.RewriteDelayImportModuleName("isodelay.dll", "isdl0.dll");
            editor.RewriteDelayImportModuleName("isodelay.dll", "isdl0.dll");
            editor.GetDelayImportedModuleNames().Should().Contain("isdl0.dll");

            var reparsed = PeImageEditor.FromBytes(editor.ToArray(), "reparsed.dll");
            reparsed.GetImportedModuleNames().Should().Contain("isch0.dll");
            reparsed.GetDelayImportedModuleNames().Should().Contain("isdl0.dll");
        }
    }

    [Fact]
    [SuppressMessage(
        "Security",
        "SCS0018",
        Justification = "Stages patched test DLL into fixture-owned temp directory.")]
    public void RewriteImportSymbol_RedirectsShorterAndSameLengthSymbolsAndExecutesPatchedExport()
    {
        if (TestHelpers.SkipIfNotWindows())
        {
            return;
        }

        using var fixture = new NativeFixture();
        foreach (var (childPath, _, _) in EnumerateArchitectures(fixture, "isochild"))
        {
            var shorterEditor = PeImageEditor.FromFile(childPath);
            var shorterRewrite = new PeImportSymbolRewrite("isoleaf0.dll", "iso_sym_primary", "iso_sym_alt");
            shorterEditor.RewriteImportSymbol(shorterRewrite);
            shorterEditor.RewriteImportSymbol(shorterRewrite);
            _ = PeImageEditor.FromBytes(shorterEditor.ToArray(), "shorter.dll");
        }

        var hostChildPath = fixture.GetPath("isochild");
        var patchedShortPath = Path.Combine(fixture.DirectoryPath, "isochild_short.dll");
        PeImageEditor.FromFile(hostChildPath)
            .RewriteImportSymbol("isoleaf0.dll", "iso_sym_primary", "iso_sym_alt")
            .ClearAuthenticodeAndRecalculateChecksum()
            .SaveIfChanged(patchedShortPath);

        using (var shortLib = new UnmanagedLibrary(patchedShortPath))
        {
            shortLib.GetUnmanagedFunction<CallIntFn>("iso_child_call_symbol")!().Should().Be(222);
        }

        var patchedSamePath = Path.Combine(fixture.DirectoryPath, "isochild_same.dll");
        PeImageEditor.FromFile(hostChildPath)
            .RewriteImportSymbol("isoleaf0.dll", "iso_sym_primary", "iso_sym_same_ln")
            .ClearAuthenticodeAndRecalculateChecksum()
            .SaveIfChanged(patchedSamePath);

        using var sameLib = new UnmanagedLibrary(patchedSamePath);
        sameLib.GetUnmanagedFunction<CallIntFn>("iso_child_call_symbol")!().Should().Be(333);
    }

    [Fact]
    [SuppressMessage(
        "Security",
        "SCS0018",
        Justification = "Stages patched test DLL into fixture-owned temp directory.")]
    public void ReplaceVerifiedBytesAtRva_PatchesAsciiAndUtf16LiteralsIdempotentlyAndRejectsMismatch()
    {
        if (TestHelpers.SkipIfNotWindows())
        {
            return;
        }

        using var fixture = new NativeFixture();
        var parentPath = fixture.GetPath("isoparent");
        uint asciiRva;
        uint utf16Rva;
        using (var unpatched = new UnmanagedLibrary(parentPath))
        {
            var baseAddr = unpatched.BaseAddress.ToInt64();
            var asciiPtr = unpatched.GetUnmanagedFunction<GetPtrFn>("iso_parent_get_ascii_tag_ptr")!().ToInt64();
            var utf16Ptr = unpatched.GetUnmanagedFunction<GetPtrFn>("iso_parent_get_utf16_tag_ptr")!().ToInt64();
            asciiRva = checked((uint)(asciiPtr - baseAddr));
            utf16Rva = checked((uint)(utf16Ptr - baseAddr));
        }

        var editor = PeImageEditor.FromFile(parentPath, PeImageValidationOptions.ForCurrentProcessArchitecture());
        editor.RvaToFileOffset(asciiRva).Should().BePositive();

        var asciiPatch = PeVerifiedBytePatch.FromAsciiLiteral(asciiRva, "ORIG_TAG_000000", "WORK_TAG_42");
        var utf16Patch = PeVerifiedBytePatch.FromUtf16Literal(utf16Rva, "WKR_ORIG", "WKR_42");
        editor.ApplyVerifiedBytePatch(asciiPatch).ApplyVerifiedBytePatch(asciiPatch);
        editor.ApplyVerifiedBytePatch(utf16Patch).ApplyVerifiedBytePatch(utf16Patch);

        Action mismatch = () => editor.ReplaceVerifiedBytesAtRva(
            asciiRva,
            Encoding.ASCII.GetBytes("WRONG_EXPECTED!"),
            Encoding.ASCII.GetBytes("OTHER_VALUE_123"));
        mismatch.Should().Throw<BadImageFormatException>().WithMessage("*RVA 0x*");

        var stagedPath = Path.Combine(fixture.DirectoryPath, "isoparent_patched.dll");
        editor.ClearAuthenticodeAndRecalculateChecksum().SaveIfChanged(stagedPath);

        using var patched = new UnmanagedLibrary(stagedPath);
        var newAsciiPtr = patched.GetUnmanagedFunction<GetPtrFn>("iso_parent_get_ascii_tag_ptr")!();
        var newUtf16Ptr = patched.GetUnmanagedFunction<GetPtrFn>("iso_parent_get_utf16_tag_ptr")!();
        Marshal.PtrToStringAnsi(newAsciiPtr).Should().Be("WORK_TAG_42");
        Marshal.PtrToStringUni(newUtf16Ptr).Should().Be("WKR_42");
    }

#if NET8_0_OR_GREATER
    [Fact]
    public void ReplaceVerifiedBytesAtRva_SpanOverload_AppliesIdempotentlyAndValidatesLengths()
    {
        if (TestHelpers.SkipIfNotWindows())
        {
            return;
        }

        using var fixture = new NativeFixture();
        var editor = PeImageEditor.FromFile(fixture.GetPath("isoleaf0"));
        var raw = editor.ToArray();
        var optOffset = BitConverter.ToInt32(raw, 0x3C) + 24;
        var sectionTableOffset = optOffset + BitConverter.ToUInt16(raw, optOffset - 4);
        var textRva = BitConverter.ToUInt32(raw, sectionTableOffset + 12);
        var fileOffset = editor.RvaToFileOffset(textRva);

        ReadOnlySpan<byte> originalFour = raw.AsSpan(fileOffset, 4);
        Span<byte> replacementFour = [0x90, 0x90, 0x90, 0x90];
        editor.ReplaceVerifiedBytesAtRva(textRva, originalFour, replacementFour);
        editor.ReplaceVerifiedBytesAtRva(textRva, originalFour, replacementFour);

        Action badLen = () => editor.ReplaceVerifiedBytesAtRva(textRva, [1, 2], [1]);
        badLen.Should().Throw<ArgumentOutOfRangeException>();
    }
#endif

    [Fact]
    [SuppressMessage("Security", "SCS0018", Justification = "Reads fixture-owned PE test binary.")]
    [SuppressMessage("Security", "SEC0116", Justification = "Reads fixture-owned PE test binary.")]
    public void ClearAuthenticodeAndRecalculateChecksum_StripsTrailingSignatureClearsForceIntegrityAndUpdatesCheckSum()
    {
        if (TestHelpers.SkipIfNotWindows())
        {
            return;
        }

        using var fixture = new NativeFixture();
        var raw = File.ReadAllBytes(fixture.GetPath("isoleaf0"));
        var signed = new byte[raw.Length + 32];
        Buffer.BlockCopy(raw, 0, signed, 0, raw.Length);
        for (var i = 0; i < 32; i++)
        {
            signed[raw.Length + i] = 0xAB;
        }

        var peOffset = BitConverter.ToInt32(signed, 0x3C);
        var optOffset = peOffset + 24;
        var isPe32Plus = BitConverter.ToUInt16(signed, optOffset) == 0x020B;
        var securityDirOffset = optOffset + (isPe32Plus ? 144 : 128);
        PeHeaderParser.WriteUInt32(signed, securityDirOffset, (uint)raw.Length);
        PeHeaderParser.WriteUInt32(signed, securityDirOffset + 4, 32u);
        var dllChars = PeHeaderParser.ReadUInt16(signed, optOffset + 70);
        PeHeaderParser.WriteUInt16(signed, optOffset + 70, (ushort)(dllChars | 0x0080));

        var editor = PeImageEditor.FromBytes(signed, "signed_leaf.dll");
        editor.HasAuthenticodeSignature.Should().BeTrue();

        editor.ClearAuthenticodeAndRecalculateChecksum();
        editor.HasAuthenticodeSignature.Should().BeFalse();
        editor.CheckSum.Should().NotBe(0u);
        var result = editor.ToArray();
        result.Should().HaveCount(raw.Length);
        (PeHeaderParser.ReadUInt16(result, optOffset + 70) & 0x0080).Should().Be(0);
    }

    [Fact]
    [SuppressMessage("Security", "SCS0018", Justification = "Writes only inside fixture-owned temporary directory.")]
    [SuppressMessage("Security", "SEC0116", Justification = "Reads only inside fixture-owned temporary directory.")]
    public void SaveIfChanged_RefusesInPlaceSourceOverwrite_AndSkipsUnchangedRewrites()
    {
        if (TestHelpers.SkipIfNotWindows())
        {
            return;
        }

        using var fixture = new NativeFixture();
        var sourcePath = fixture.GetPath("isoleaf0");
        var editor = PeImageEditor.FromFile(sourcePath);

        Action overwriteSame = () => editor.SaveIfChanged(sourcePath);
        overwriteSame.Should().Throw<InvalidOperationException>();

        var destPath = Path.Combine(fixture.DirectoryPath, "sub", "isoleaf0_copy.dll");
        editor.SaveIfChanged(destPath);
        var sentinelUtc = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(destPath, sentinelUtc);

        editor.SaveIfChanged(destPath);
        File.GetLastWriteTimeUtc(destPath).Should().Be(sentinelUtc);

        editor.ClearAuthenticodeAndRecalculateChecksum();
        editor.SaveIfChanged(destPath);
        File.ReadAllBytes(destPath).Should().Equal(editor.ToArray());
    }

    [Fact]
    [SuppressMessage("Security", "SCS0018", Justification = "Reads fixture-owned PE test binary.")]
    [SuppressMessage("Security", "SEC0116", Justification = "Reads fixture-owned PE test binary.")]
    public void Parser_RejectsManagedAssembliesTruncatedHeadersAndMalformedSections()
    {
        if (TestHelpers.SkipIfNotWindows())
        {
            return;
        }

        var managedAssemblyPath = typeof(UnmanagedLibrary).Assembly.Location;
        Action loadManaged = () => PeImageEditor.FromFile(managedAssemblyPath);
        loadManaged.Should().Throw<BadImageFormatException>().WithMessage("*CLI/CLR*");

        var allowManaged = PeImageEditor.FromFile(
            managedAssemblyPath,
            new PeImageValidationOptions
            {
                RequireUnmanagedImage = false,
                RequireDll = false,
            });
        allowManaged.SizeOfImage.Should().BeGreaterThan(0u);

        Action tooShort = () => PeImageEditor.FromBytes([0x4D, 0x5A, 0x00], "tiny.dll");
        tooShort.Should().Throw<BadImageFormatException>();

        using var fixture = new NativeFixture();
        var validBytes = File.ReadAllBytes(fixture.GetPath("isoleaf0"));

        Action wrongLen = () => PeImageEditor.FromBytes(
            validBytes,
            "len.dll",
            new PeImageValidationOptions
            {
                ExpectedFileLength = validBytes.Length + 1,
            });
        wrongLen.Should().Throw<BadImageFormatException>();

        Action wrongMachine = () => PeImageEditor.FromBytes(
            validBytes,
            "mach.dll",
            new PeImageValidationOptions
            {
                ExpectedMachine = PeMachine.Arm64,
            });
        wrongMachine.Should().Throw<BadImageFormatException>();

        var nonDll = (byte[])validBytes.Clone();
        var coffOffset = BitConverter.ToInt32(nonDll, 0x3C) + 4;
        PeHeaderParser.WriteUInt16(nonDll, coffOffset + 18, 0x0002);
        Action loadExe = () => PeImageEditor.FromBytes(nonDll, "exe.dll");
        loadExe.Should().Throw<BadImageFormatException>();
    }

    [Fact]
    public void DescriptorsAndEditor_RejectPathCharactersLongerReplacementsAndUnmappedRvas()
    {
        if (TestHelpers.SkipIfNotWindows())
        {
            return;
        }

        using var fixture = new NativeFixture();
        var editor = PeImageEditor.FromFile(fixture.GetPath("isochild"));

        Action longerModule = () => editor.RewriteImportModuleName("isoleaf0.dll", "isoleaf0_longer.dll");
        longerModule.Should().Throw<ArgumentOutOfRangeException>();

        Action pathModule = () => editor.RewriteImportModuleName("isoleaf0.dll", "..\\evil.dll");
        pathModule.Should().Throw<ArgumentException>();

        Action longerSymbol = () => _ = new PeImportSymbolRewrite("isoleaf0.dll", "short", "longer_symbol");
        longerSymbol.Should().Throw<ArgumentOutOfRangeException>();

        Action rvaZero = () => editor.RvaToFileOffset(0);
        rvaZero.Should().Throw<BadImageFormatException>();

        Action rvaUnmapped = () => editor.RvaToFileOffset(0x7FFF0000u);
        rvaUnmapped.Should().Throw<BadImageFormatException>();
    }

    [Fact]
    public void CharacteristicsTimeDateStampAndFileOffsetToRva_RoundTripAndValidateBounds()
    {
        if (TestHelpers.SkipIfNotWindows())
        {
            return;
        }

        using var fixture = new NativeFixture();
        foreach (var (archPath, _, _) in EnumerateArchitectures(fixture, "isoparent"))
        {
            var editor = PeImageEditor.FromFile(archPath);
            (editor.Characteristics & 0x2000).Should().Be(0x2000);
            editor.TimeDateStamp.Should().BeGreaterThan(0u);

            var raw = editor.ToArray();
            var optOffset = BitConverter.ToInt32(raw, 0x3C) + 24;
            var sectionOffset = optOffset + BitConverter.ToUInt16(raw, optOffset - 4);
            var firstSectionRva = BitConverter.ToUInt32(raw, sectionOffset + 12);
            var fileOffset = editor.RvaToFileOffset(firstSectionRva);
            editor.FileOffsetToRva(fileOffset).Should().Be(firstSectionRva);

            Action negOffset = () => editor.FileOffsetToRva(-1);
            Action pastEnd = () => editor.FileOffsetToRva(raw.Length);
            Action headerOffset = () => editor.FileOffsetToRva(0);
            negOffset.Should().Throw<ArgumentOutOfRangeException>();
            pastEnd.Should().Throw<ArgumentOutOfRangeException>();
            headerOffset.Should().Throw<BadImageFormatException>();
        }
    }

    [Fact]
    public void RewriteExportModuleName_UpdatesHeaderIdempotentlyAcrossPe32AndPe32Plus_AndValidatesErrors()
    {
        if (TestHelpers.SkipIfNotWindows())
        {
            return;
        }

        using var fixture = new NativeFixture();
        foreach (var (archPath, _, _) in EnumerateArchitectures(fixture, "isoparent"))
        {
            var editor = PeImageEditor.FromFile(archPath);
            editor.GetExportModuleName().Should().BeEquivalentTo("isoparent.dll");

            editor.RewriteExportModuleName("isoparent.dll", "isoprn00.dll");
            editor.RewriteExportModuleName("isoparent.dll", "isoprn00.dll");
            editor.GetExportModuleName().Should().Be("isoprn00.dll");

            var reparsed = PeImageEditor.FromBytes(editor.ToArray(), "reparsed_export.dll");
            reparsed.GetExportModuleName().Should().Be("isoprn00.dll");

            Action wrongOrig = () => editor.RewriteExportModuleName("othername.dll", "other00.dll");
            wrongOrig.Should().Throw<BadImageFormatException>();

            Action longer = () => editor.RewriteExportModuleName("isoprn00.dll", "isoparent_too_long.dll");
            longer.Should().Throw<ArgumentOutOfRangeException>();
        }

        var exePath = fixture.GetExecutablePath("isohelper");
        var exeEditor = PeImageEditor.FromFile(exePath, PeImageValidationOptions.ForExecutable());
        exeEditor.GetExportModuleName().Should().BeNull();
        Action rewriteNoExport = () => exeEditor.RewriteExportModuleName("isohelper.exe", "isohlp.exe");
        rewriteNoExport.Should().Throw<BadImageFormatException>();
    }

    [Fact]
    [SuppressMessage("Security", "SCS0018", Justification = "Stages compacted PE into fixture-owned subfolder.")]
#pragma warning disable MA0051 // Method is too long
    public void RemoveImportModule_CompactsDescriptorIdempotently_AndLoadsWhenRemovedDllIsAbsent()
#pragma warning restore MA0051 // Method is too long
    {
        if (TestHelpers.SkipIfNotWindows())
        {
            return;
        }

        using var fixture = new NativeFixture();
        foreach (var (archPath, _, _) in EnumerateArchitectures(fixture, "isocompact"))
        {
            var editor = PeImageEditor.FromFile(archPath);
            editor.GetImportedModuleNames()
                .Should()
                .Contain(m => string.Equals(m, "isounused.dll", StringComparison.OrdinalIgnoreCase));

            Action typoNoWitness = () => editor.RemoveImportModule(
                new PeImportModuleRemoval
                {
                    ModuleName = "not_imported.dll",
                });
            typoNoWitness.Should().Throw<BadImageFormatException>();

            Action sameWitness = () => editor.RemoveImportModule(
                new PeImportModuleRemoval
                {
                    ModuleName = "isounused.dll",
                    WitnessRemainingModuleName = "isounused.dll",
                });
            sameWitness.Should().Throw<ArgumentException>();

            var removal = new PeImportModuleRemoval
            {
                ModuleName = "isounused.dll",
                WitnessRemainingModuleName = "isoleaf0.dll",
            };
            editor.RemoveImportModule(removal);
            editor.RemoveImportModule(removal);
            editor.GetImportedModuleNames()
                .Should()
                .NotContain(m => string.Equals(m, "isounused.dll", StringComparison.OrdinalIgnoreCase));
        }

        var isolatedDir = Path.Combine(fixture.DirectoryPath, "no_unused_dll");
        Directory.CreateDirectory(isolatedDir);
        File.Copy(fixture.GetPath("isoleaf0"), Path.Combine(isolatedDir, "isoleaf0.dll"));
        var unpatchedCopy = Path.Combine(isolatedDir, "isocompact_orig.dll");
        File.Copy(fixture.GetPath("isocompact"), unpatchedCopy);

        Action loadWithoutUnused = () =>
        {
            using var failLib = new UnmanagedLibrary(unpatchedCopy);
        };
        loadWithoutUnused.Should().Throw<Win32Exception>();

        var compactedPath = Path.Combine(isolatedDir, "isocompact_clean.dll");
        PeImageEditor.FromFile(fixture.GetPath("isocompact"))
            .RemoveImportModule(
                new PeImportModuleRemoval
                {
                    ModuleName = "isounused.dll",
                    WitnessRemainingModuleName = "isoleaf0.dll",
                })
            .ClearAuthenticodeAndRecalculateChecksum()
            .SaveIfChanged(compactedPath);

        using var cleanLib = new UnmanagedLibrary(compactedPath);
        cleanLib.GetUnmanagedFunction<UnaryIntFn>("iso_compact_compute")!(7).Should().Be(5107);
    }

    [Fact]
    [SuppressMessage("Security", "SCS0018", Justification = "Stages patched DLL into fixture-owned directory.")]
    public void ReplaceVerifiedStringHelpers_PatchAsciiUtf16AndPascalShortStringIdempotently()
    {
        if (TestHelpers.SkipIfNotWindows())
        {
            return;
        }

        using var fixture = new NativeFixture();
        var parentPath = fixture.GetPath("isoparent");
        var (asciiRva, utf16Rva, shortRva) = ProbeParentStringRvas(parentPath);

        var editor = PeImageEditor.FromFile(parentPath)
            .ReplaceVerifiedAsciiStringAtRva(asciiRva, "ORIG_TAG_000000", "ASC_42")
            .ReplaceVerifiedAsciiStringAtRva(asciiRva, "ORIG_TAG_000000", "ASC_42")
            .ReplaceVerifiedUtf16StringAtRva(utf16Rva, "WKR_ORIG", "UTF_99")
            .ReplaceVerifiedUtf16StringAtRva(utf16Rva, "WKR_ORIG", "UTF_99")
            .ReplaceVerifiedShortStringAtRva(shortRva, "APPCONFIG.IN", "CUSTOM123.IN")
            .ReplaceVerifiedShortStringAtRva(shortRva, "APPCONFIG.IN", "CUSTOM123.IN");

        Action disallowCustomMismatch = () => editor.ReplaceVerifiedShortStringAtRva(
            shortRva,
            "APPCONFIG.IN",
            "WKR01.INI",
            allowAnyPrintableAsciiOfSameLength: false);
        disallowCustomMismatch.Should().Throw<BadImageFormatException>();

        editor.ReplaceVerifiedShortStringAtRva(
            shortRva,
            "APPCONFIG.IN",
            "WKR01.INI",
            allowAnyPrintableAsciiOfSameLength: true);

        var stagedPath = Path.Combine(fixture.DirectoryPath, "isoparent_str_helpers.dll");
        editor.ClearAuthenticodeAndRecalculateChecksum().SaveIfChanged(stagedPath);

        using var patched = new UnmanagedLibrary(stagedPath);
        Marshal.PtrToStringAnsi(patched.GetUnmanagedFunction<GetPtrFn>("iso_parent_get_ascii_tag_ptr")!())
            .Should()
            .Be("ASC_42");
        Marshal.PtrToStringUni(patched.GetUnmanagedFunction<GetPtrFn>("iso_parent_get_utf16_tag_ptr")!())
            .Should()
            .Be("UTF_99");
        var shortPtr = patched.GetUnmanagedFunction<GetPtrFn>("iso_parent_get_short_string_ptr")!();
        var rawShort = new byte[13];
        Marshal.Copy(shortPtr, rawShort, 0, rawShort.Length);
        NativeByteString.ReadShortStringAscii(rawShort, 0, rawShort.Length).Should().Be("WKR01.INI");
    }

    [Fact]
    [SuppressMessage("Security", "SCS0018", Justification = "Reads and writes fixture-owned PE test binaries.")]
    [SuppressMessage("Security", "SEC0116", Justification = "Reads fixture-owned PE test binary.")]
    public void ForExecutableAndSetLargeAddressAware_ToggleFlag_AndRequireClearingInvalidatedAuthenticodeBeforeSave()
    {
        if (TestHelpers.SkipIfNotWindows())
        {
            return;
        }

        using var fixture = new NativeFixture();
        foreach (var (exePath, expectedMachine) in EnumerateExecutables(fixture, "isohelper"))
        {
            var info = new FileInfo(exePath);
            var editor = PeImageEditor.FromFile(
                exePath,
                PeImageValidationOptions.ForExecutable(expectedMachine, info.Length));
            editor.SetLargeAddressAware(enabled: true);
            (editor.Characteristics & 0x0020).Should().Be(0x0020);
            editor.SetLargeAddressAware(enabled: false);
            (editor.Characteristics & 0x0020).Should().Be(0);
        }

        var raw = File.ReadAllBytes(fixture.GetPath("isoleaf0"));
        var signed = new byte[raw.Length + 16];
        Buffer.BlockCopy(raw, 0, signed, 0, raw.Length);
        var optOffset = BitConverter.ToInt32(signed, 0x3C) + 24;
        var secDirOffset = optOffset + (BitConverter.ToUInt16(signed, optOffset) == 0x020B ? 144 : 128);
        PeHeaderParser.WriteUInt32(signed, secDirOffset, (uint)raw.Length);
        PeHeaderParser.WriteUInt32(signed, secDirOffset + 4, 16u);

        var signedEditor = PeImageEditor.FromBytes(signed, "signed.dll");
        signedEditor.SetLargeAddressAware(enabled: (signedEditor.Characteristics & 0x0020) == 0);
        var saveTarget = Path.Combine(fixture.DirectoryPath, "signed_modified.dll");
        Action saveWithoutClear = () => signedEditor.SaveIfChanged(saveTarget);
        saveWithoutClear.Should().Throw<InvalidOperationException>().WithMessage("*Authenticode*");

        signedEditor.ClearAuthenticodeAndRecalculateChecksum().SaveIfChanged(saveTarget);
        File.Exists(saveTarget).Should().BeTrue();
    }

    private static (uint AsciiRva, uint Utf16Rva, uint ShortRva) ProbeParentStringRvas(string parentPath)
    {
        using var unpatched = new UnmanagedLibrary(parentPath);
        var baseAddr = unpatched.BaseAddress.ToInt64();
        var asciiRva =
            checked((uint)(unpatched.GetUnmanagedFunction<GetPtrFn>("iso_parent_get_ascii_tag_ptr")!().ToInt64()
                           - baseAddr));
        var utf16Rva =
            checked((uint)(unpatched.GetUnmanagedFunction<GetPtrFn>("iso_parent_get_utf16_tag_ptr")!().ToInt64()
                           - baseAddr));
        var shortRva =
            checked((uint)(unpatched.GetUnmanagedFunction<GetPtrFn>("iso_parent_get_short_string_ptr")!().ToInt64()
                           - baseAddr));
        return (asciiRva, utf16Rva, shortRva);
    }

    private static IEnumerable<(string Path, ushort ExpectedMachine)> EnumerateExecutables(
        NativeFixture fixture,
        string executableName)
    {
        var pe32 = fixture.TryGetPe32ExecutablePath(executableName);
        if (pe32 is not null)
        {
            yield return (pe32, PeMachine.I386);
        }

        var pe64 = fixture.TryGetPe32PlusExecutablePath(executableName);
        if (pe64 is not null)
        {
            yield return (pe64, PeMachine.Amd64);
        }
    }

    private static IEnumerable<(string Path, ushort ExpectedMachine, bool ExpectedPe32Plus)> EnumerateArchitectures(
        NativeFixture fixture,
        string moduleName)
    {
        var pe32 = fixture.TryGetPe32Path(moduleName);
        if (pe32 is not null)
        {
            yield return (pe32, PeMachine.I386, false);
        }

        var pe64 = fixture.TryGetPe32PlusPath(moduleName);
        if (pe64 is not null)
        {
            yield return (pe64, PeMachine.Amd64, true);
        }
    }
}
