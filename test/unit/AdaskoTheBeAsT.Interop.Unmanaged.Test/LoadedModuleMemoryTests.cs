using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
#if NET5_0_OR_GREATER
using System.Runtime.Versioning;
#endif
using System.Text;
using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Unmanaged.Test;

#pragma warning disable SA1121

#if NET5_0_OR_GREATER
[SupportedOSPlatform("windows")]
#endif
[Trait("Category", "Native")]
public class LoadedModuleMemoryTests
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int UnaryIntFn(int value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ZeroArgIntFn();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr GetPtrFn();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void WriteQuadIntFn(int a, int b, int c, int d);

    [Fact]
    [SuppressMessage("IDisposableAnalyzers", "IDISP016", Justification = "Verifies disposed handle rejection.")]
    [SuppressMessage("IDisposableAnalyzers", "IDISP017", Justification = "Explicit Dispose tests ObjectDisposedException.")]
    public void InspectAndVerifyIdentity_MatchOfflineHeaders_AndRejectMismatchDatafileOrDisposed()
    {
        if (TestHelpers.SkipIfNotWindows())
        {
            return;
        }

        using var fixture = new NativeFixture();
        var hookPath = fixture.GetPath("isohook");
        var offline = PeImageEditor.FromFile(hookPath, PeImageValidationOptions.ForCurrentProcessArchitecture());

        using (var library = new UnmanagedLibrary(hookPath))
        {
            var id = LoadedModuleMemory.Inspect(library);
            id.Machine.Should().Be(offline.Machine);
            id.TimeDateStamp.Should().Be(offline.TimeDateStamp);
            id.SizeOfImage.Should().Be(offline.SizeOfImage);
            (id == new LoadedModuleIdentity(offline.Machine, offline.TimeDateStamp, offline.SizeOfImage)).Should().BeTrue();
            (id != new LoadedModuleIdentity(offline.Machine, offline.TimeDateStamp + 1u, offline.SizeOfImage)).Should().BeTrue();
            id.Equals((object)id).Should().BeTrue();
            id.GetHashCode().Should().Be(LoadedModuleMemory.Inspect(library).GetHashCode());
            id.ToString().Should().Contain("SizeOfImage=0x");

            LoadedModuleMemory.VerifyIdentity(library, id);
            Action mismatch = () => LoadedModuleMemory.VerifyIdentity(
                library,
                new LoadedModuleIdentity(id.Machine, id.TimeDateStamp ^ 0x1u, id.SizeOfImage));
            mismatch.Should().Throw<BadImageFormatException>();
        }

        using (var dataFile = new UnmanagedLibrary(hookPath, LoadLibraryFlags.LOAD_LIBRARY_AS_DATAFILE))
        {
            Action inspectDataFile = () => _ = LoadedModuleMemory.Inspect(dataFile);
            inspectDataFile.Should().Throw<InvalidOperationException>();
        }

        var disposed = new UnmanagedLibrary(hookPath);
        disposed.Dispose();
        Action inspectDisposed = () => _ = LoadedModuleMemory.Inspect(disposed);
        inspectDisposed.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    [SuppressMessage("Security", "SCS0018", Justification = "Copies test DLLs into fixture-owned private subdirectory.")]
    public void RedirectImportFunction_RedirectsNamedImportChainsOriginalAndRestoresCleanly()
    {
        if (TestHelpers.SkipIfNotWindows())
        {
            return;
        }

        using var fixture = new NativeFixture();
        var stagedHook = StagePrivateHookCopy(fixture, "named");
        using var library = new UnmanagedLibrary(stagedHook);
        var callNamed = library.GetUnmanagedFunction<UnaryIntFn>("iso_hook_call_named")!;
        callNamed(5).Should().Be(15);

        var capturedOriginal = IntPtr.Zero;
        using var callbackScope = UnmanagedLibrary.PinConcreteDelegate<UnaryIntFn>(value =>
        {
            var origFn = UnmanagedLibrary.GetDelegateForFunctionPointer<UnaryIntFn>(capturedOriginal, CallingConvention.Cdecl)!;
            return (origFn(value) * 10) + 1;
        });

        LoadedModuleMemory.TryRedirectImportFunction(library, "isohookdep.dll", "missing_export_xyz", callbackScope.Ptr, out _)
            .Should().BeFalse();

        Action missingThrow = () => LoadedModuleMemory.RedirectImportFunction(library, "isohookdep.dll", "missing_export_xyz", callbackScope.Ptr);
        missingThrow.Should().Throw<EntryPointNotFoundException>();

        capturedOriginal = LoadedModuleMemory.RedirectImportFunction(library, "isohookdep.dll", "iso_hook_dep_calc", callbackScope.Ptr);
        capturedOriginal.Should().NotBe(IntPtr.Zero);
        callNamed(5).Should().Be(151);

        var secondPrev = LoadedModuleMemory.RedirectImportFunction(library, "isohookdep.dll", "iso_hook_dep_calc", capturedOriginal);
        secondPrev.Should().Be(callbackScope.Ptr);
        callNamed(5).Should().Be(15);
    }

    [Fact]
    [SuppressMessage("Security", "SCS0018", Justification = "Copies test DLLs into fixture-owned private subdirectory.")]
    public void TryRedirectImportOrdinal_RedirectsOrdinalImportAndRestoresOriginal()
    {
        if (TestHelpers.SkipIfNotWindows())
        {
            return;
        }

        using var fixture = new NativeFixture();
        var stagedHook = StagePrivateHookCopy(fixture, "ord");
        using var library = new UnmanagedLibrary(stagedHook);
        var callOrdinal = library.GetUnmanagedFunction<UnaryIntFn>("iso_hook_call_ordinal")!;
        callOrdinal(6).Should().Be(18);

        using var replacementScope = UnmanagedLibrary.PinConcreteDelegate<UnaryIntFn>(value => (value * 100) + 7);
        LoadedModuleMemory.TryRedirectImportOrdinal(library, "isohookdep.dll", 99, replacementScope.Ptr, out _)
            .Should().BeFalse();

        LoadedModuleMemory.TryRedirectImportOrdinal(library, "isohookdep.dll", 7, replacementScope.Ptr, out var originalFn)
            .Should().BeTrue();
        originalFn.Should().NotBe(IntPtr.Zero);
        callOrdinal(6).Should().Be(607);

        LoadedModuleMemory.TryRedirectImportOrdinal(library, "isohookdep.dll", 7, originalFn, out var previousHook)
            .Should().BeTrue();
        previousHook.Should().Be(replacementScope.Ptr);
        callOrdinal(6).Should().Be(18);
    }

    [Fact]
    [SuppressMessage("Security", "SCS0018", Justification = "Copies test DLLs into fixture-owned private subdirectory.")]
    public void WriteVerifiedBytesAtRva_PatchesReadOnlySectionIdempotently_AndFailsClosedOnMismatch()
    {
        if (TestHelpers.SkipIfNotWindows())
        {
            return;
        }

        using var fixture = new NativeFixture();
        var stagedHook = StagePrivateHookCopy(fixture, "rdata");
        using var library = new UnmanagedLibrary(stagedHook);
        var identity = LoadedModuleMemory.Inspect(library);

        var rdataPtr = library.GetUnmanagedFunction<GetPtrFn>("iso_hook_get_readonly_magic_ptr")!();
        var sumMagic = library.GetUnmanagedFunction<ZeroArgIntFn>("iso_hook_sum_readonly_magic")!;
        var rdataRva = checked((uint)(rdataPtr.ToInt64() - library.BaseAddress.ToInt64()));

        var originalBytes = Encoding.ASCII.GetBytes("RDATA_V1");
        var patchedBytes = Encoding.ASCII.GetBytes("PATCH_V2");
        sumMagic().Should().Be(SumAscii(originalBytes));

        LoadedModuleMemory.WriteVerifiedBytesAtRva(library, identity, rdataRva, originalBytes, patchedBytes);
        LoadedModuleMemory.WriteVerifiedBytesAtRva(library, identity, rdataRva, originalBytes, patchedBytes);
        sumMagic().Should().Be(SumAscii(patchedBytes));

#if NET5_0_OR_GREATER
        using var handleRef = UnmanagedLibrary.LoadLibrary(stagedHook);
        ReadOnlySpan<byte> spanExpected = patchedBytes;
        var spanNext = "SPAN__V3"u8;
        LoadedModuleMemory.WriteVerifiedBytesAtRva(handleRef, identity, rdataRva, spanExpected, spanNext, flushInstructionCache: true);
        LoadedModuleMemory.WriteVerifiedBytesAtRva(handleRef, identity, rdataRva, spanExpected, spanNext);
        sumMagic().Should().Be(SumAscii(spanNext.ToArray()));
#endif

        var badIdentity = new LoadedModuleIdentity(identity.Machine, identity.TimeDateStamp + 99u, identity.SizeOfImage);
        Action wrongId = () => LoadedModuleMemory.WriteVerifiedBytesAtRva(library, badIdentity, rdataRva, originalBytes, patchedBytes);
        wrongId.Should().Throw<BadImageFormatException>();

        Action wrongExpected = () => LoadedModuleMemory.WriteVerifiedBytesAtRva(
            library,
            identity,
            rdataRva,
            Encoding.ASCII.GetBytes("NOTMATCH"),
            Encoding.ASCII.GetBytes("OTHERVAL"));
        wrongExpected.Should().Throw<BadImageFormatException>();
    }

    [Fact]
    [SuppressMessage("Security", "SCS0018", Justification = "Copies test DLLs into fixture-owned private subdirectory.")]
    public void ClearWritableBytesAtRva_ZeroesDataAndBssSections_AndRejectsReadOnlyOrCodeSections()
    {
        if (TestHelpers.SkipIfNotWindows())
        {
            return;
        }

        using var fixture = new NativeFixture();
        var stagedHook = StagePrivateHookCopy(fixture, "clear");
        using var library = new UnmanagedLibrary(stagedHook);
        using var handleRef = UnmanagedLibrary.LoadLibrary(stagedHook);
        var identity = LoadedModuleMemory.Inspect(library);

        var dataPtr = library.GetUnmanagedFunction<GetPtrFn>("iso_hook_get_data_scratch_ptr")!();
        var bssPtr = library.GetUnmanagedFunction<GetPtrFn>("iso_hook_get_bss_scratch_ptr")!();
        var rdataPtr = library.GetUnmanagedFunction<GetPtrFn>("iso_hook_get_readonly_magic_ptr")!();
        var textPtr = library.GetRequiredExport("iso_hook_sum_readonly_magic");

        library.GetUnmanagedFunction<WriteQuadIntFn>("iso_hook_write_data_scratch")!(91, 92, 93, 94);
        library.GetUnmanagedFunction<WriteQuadIntFn>("iso_hook_write_bss_scratch")!(71, 72, 73, 74);

        var dataRva = checked((uint)(dataPtr.ToInt64() - library.BaseAddress.ToInt64()));
        var bssRva = checked((uint)(bssPtr.ToInt64() - library.BaseAddress.ToInt64()));
        LoadedModuleMemory.ClearWritableBytesAtRva(library, identity, dataRva, sizeof(int) * 4);
        LoadedModuleMemory.ClearWritableBytesAtRva(handleRef, identity, bssRva, sizeof(int) * 4);

        for (var i = 0; i < 4; i++)
        {
            Marshal.ReadInt32(dataPtr, i * sizeof(int)).Should().Be(0);
            Marshal.ReadInt32(bssPtr, i * sizeof(int)).Should().Be(0);
        }

        var rdataRva = checked((uint)(rdataPtr.ToInt64() - library.BaseAddress.ToInt64()));
        var textRva = checked((uint)(textPtr.ToInt64() - library.BaseAddress.ToInt64()));
        Action clearRdata = () => LoadedModuleMemory.ClearWritableBytesAtRva(library, identity, rdataRva, 4);
        Action clearText = () => LoadedModuleMemory.ClearWritableBytesAtRva(library, identity, textRva, 4);
        clearRdata.Should().Throw<InvalidOperationException>();
        clearText.Should().Throw<InvalidOperationException>();
    }

    [SuppressMessage("Security", "SCS0018", Justification = "Stages test-owned copies inside NativeFixture.DirectoryPath.")]
    private static string StagePrivateHookCopy(NativeFixture fixture, string suffix)
    {
        var dir = Path.Combine(fixture.DirectoryPath, "hook_" + suffix);
        Directory.CreateDirectory(dir);
        File.Copy(fixture.GetPath("isohookdep"), Path.Combine(dir, "isohookdep.dll"));
        var targetHook = Path.Combine(dir, "isohook.dll");
        File.Copy(fixture.GetPath("isohook"), targetHook);
        return targetHook;
    }

    private static int SumAscii(byte[] bytes)
    {
        var total = 0;
        foreach (var b in bytes)
        {
            total += b;
        }

        return total;
    }
}
