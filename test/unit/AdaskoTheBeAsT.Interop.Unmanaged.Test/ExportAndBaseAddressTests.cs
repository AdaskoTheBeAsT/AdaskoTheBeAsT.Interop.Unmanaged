using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
#if NET10_0_OR_GREATER
using System.Runtime.Versioning;
#endif
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Unmanaged.Test;

#pragma warning disable SA1121

[Trait("Category", "Native")]
public class ExportAndBaseAddressTests
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Add(int left, int right);

    [Fact]
    public void RequiredTypedExport_IsCallableAndReportsMissingExport()
    {
        using var fixture = new NativeFixture();
        using var library = new UnmanagedLibrary(fixture.LibraryPath);
        var add = library.GetRequiredUnmanagedFunction<Add>("fixture_add");
        add(19, 23).Should().Be(42);

        Action missing = () => library.GetRequiredUnmanagedFunction<Add>("fixture_missing");
        var error = missing.Should().Throw<EntryPointNotFoundException>().Which;
        error.Message.Should().Contain("fixture_missing").And.Contain(fixture.LibraryPath);
    }

    [Fact]
    public void GetRequiredExport_ReturnsCallableExportAddress_ForInstanceAndStaticOverloads()
    {
        using var fixture = new NativeFixture();
        using var library = new UnmanagedLibrary(fixture.LibraryPath);
        using var handle = UnmanagedLibrary.LoadLibrary(fixture.LibraryPath);

        var instancePtr = library.GetRequiredExport("fixture_add");
        var staticPtr = UnmanagedLibrary.GetRequiredExport(handle, "fixture_add");
        library.TryGetExport("fixture_add", out var expectedPtr).Should().BeTrue();

        instancePtr.Should().Be(expectedPtr);
        staticPtr.Should().Be(expectedPtr);

        var fn = UnmanagedLibrary.GetDelegateForFunctionPointer<Func<int, int, int>>(
            instancePtr,
            CallingConvention.Cdecl)!;
        fn(19, 23).Should().Be(42);
    }

    [Fact]
    public void GetRequiredExport_MissingSymbol_ThrowsEntryPointNotFoundWithSymbolAndModule()
    {
        using var nativeFixture = new NativeFixture();
        using var library = new UnmanagedLibrary(nativeFixture.LibraryPath);
        using var handle = UnmanagedLibrary.LoadLibrary(nativeFixture.LibraryPath);

        Action instanceLookup = () => library.GetRequiredExport("nonexistent_export_xyz");
        Action staticLookup = () => UnmanagedLibrary.GetRequiredExport(handle, "nonexistent_export_xyz");

        var instanceEx = instanceLookup.Should().Throw<EntryPointNotFoundException>().Which;
        instanceEx.Message.Should().Contain("nonexistent_export_xyz");
        instanceEx.Message.Should().Contain(nativeFixture.LibraryPath);

        var staticEx = staticLookup.Should().Throw<EntryPointNotFoundException>().Which;
        staticEx.Message.Should().Contain("nonexistent_export_xyz");
        staticEx.Message.Should().Contain(nativeFixture.LibraryPath);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("\0")]
    [InlineData("fixture_add\0extra")]
    public void GetRequiredExport_RejectsInvalidExportNames(string? invalidName)
    {
        using var fixture = new NativeFixture();
        using var library = new UnmanagedLibrary(fixture.LibraryPath);
        using var handle = UnmanagedLibrary.LoadLibrary(fixture.LibraryPath);

        Action instanceCall = () => library.GetRequiredExport(invalidName!);
        Action staticCall = () => UnmanagedLibrary.GetRequiredExport(handle, invalidName!);

        instanceCall.Should().Throw<ArgumentException>().WithParameterName("functionName");
        staticCall.Should().Throw<ArgumentException>().WithParameterName("functionName");
    }

    [Fact]
    public void StaticHandleOverloads_RejectNullSafeLibraryHandle()
    {
        Action baseAddr = () => UnmanagedLibrary.GetBaseAddress(null!);
        Action reqExport = () => UnmanagedLibrary.GetRequiredExport(null!, "fixture_add");

        baseAddr.Should().Throw<ArgumentNullException>().WithParameterName("safeLibraryHandle");
        reqExport.Should().Throw<ArgumentNullException>().WithParameterName("safeLibraryHandle");
    }

    [Fact]
    public void BaseAddress_OnWindows_PointsToMappedMzHeaderAndMasksDataFileTagBits()
    {
        if (TestHelpers.SkipIfNotWindows())
        {
            return;
        }

        using var fixture = new NativeFixture();
        using var normalLib = new UnmanagedLibrary(fixture.LibraryPath);
        using var dataFileLib = new UnmanagedLibrary(fixture.LibraryPath, LoadLibraryFlags.LOAD_LIBRARY_AS_DATAFILE);

        normalLib.BaseAddress.Should().NotBe(IntPtr.Zero);
        Marshal.ReadInt16(normalLib.BaseAddress).Should().Be(0x5A4D);

        dataFileLib.BaseAddress.Should().NotBe(IntPtr.Zero);
        (dataFileLib.BaseAddress.ToInt64() & 3L).Should().Be(0L);
        Marshal.ReadInt16(dataFileLib.BaseAddress).Should().Be(0x5A4D);

        using var taggedHandle = new SafeLibraryHandle(new IntPtr(0x10000003), ownsHandle: false);
        UnmanagedLibrary.GetBaseAddress(taggedHandle).Should().Be(new IntPtr(0x10000000));
    }

    [Fact]
    [SuppressMessage("IDisposableAnalyzers", "IDISP016", Justification = "Tests disposed exception behavior.")]
    [SuppressMessage("IDisposableAnalyzers", "IDISP017", Justification = "Explicit Dispose is under test.")]
    public void DisposedInstanceAndHandle_ThrowObjectDisposedException_ForBaseAddressAndRequiredExport()
    {
        using var fixture = new NativeFixture();
        using var library = new UnmanagedLibrary(fixture.LibraryPath);
        using var handle = UnmanagedLibrary.LoadLibrary(fixture.LibraryPath);
        library.Dispose();
        handle.Dispose();

        Action instanceBase = () => _ = library.BaseAddress;
        Action staticBase = () => _ = UnmanagedLibrary.GetBaseAddress(handle);
        Action instanceExport = () => _ = library.GetRequiredExport("fixture_add");
        Action staticExport = () => _ = UnmanagedLibrary.GetRequiredExport(handle, "fixture_add");

        instanceBase.Should().Throw<ObjectDisposedException>();
        staticBase.Should().Throw<ObjectDisposedException>();
        instanceExport.Should().Throw<ObjectDisposedException>();
        staticExport.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void GetRequiredExportAndBaseAddress_RacingDisposal_CompleteOrThrowObjectDisposed()
    {
        if (TestHelpers.SkipIfNotWindows())
        {
            return;
        }

        using var fixture = new NativeFixture();
        for (var i = 0; i < 60; i++)
        {
            using var library = new UnmanagedLibrary(fixture.LibraryPath);
            Parallel.Invoke(
                () =>
                {
                    try
                    {
                        library.GetRequiredExport("fixture_add").Should().NotBe(IntPtr.Zero);
                        library.BaseAddress.Should().NotBe(IntPtr.Zero);
                    }
                    catch (ObjectDisposedException ex)
                    {
                        ex.Should().NotBeNull();
                    }
                },
                library.Dispose);
        }
    }

    [Fact]
#if NET10_0_OR_GREATER
    [SupportedOSPlatform("windows")]
#endif
    [SuppressMessage("IDisposableAnalyzers", "IDISP001", Justification = "IsolatedLibrarySet owns module handles.")]
    [SuppressMessage("IDisposableAnalyzers", "IDISP004", Justification = "IsolatedLibrarySet owns module handles.")]
    [SuppressMessage("Security", "SCS0018", Justification = "Uses test-owned isolation root.")]
#pragma warning disable MA0051 // Method is too long
    public void GetLoadedFilePath_AndContainsAddress_DistinguishConcurrentIsolatedWorkers()
#pragma warning restore MA0051 // Method is too long
    {
        if (TestHelpers.SkipIfNotWindows())
        {
            return;
        }

        using var fixture = new NativeFixture();
        var isoRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "unm-prov-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var worker0 = IsolatedLibrarySet.Create(
                new IsolatedLibrarySetOptions
                {
                    SourceDirectory = fixture.DirectoryPath,
                    IsolationRootDirectory = isoRoot,
                    NamespaceId = "prv0",
                    WorkerIndex = 70,
                    Modules =
                    [
                        new IsolatedModuleSpecification("isoleaf0.dll")
                        {
                            AlwaysRenameWithWorkerSuffix = true,
                        },
                    ],
                });
            using var worker1 = IsolatedLibrarySet.Create(
                new IsolatedLibrarySetOptions
                {
                    SourceDirectory = fixture.DirectoryPath,
                    IsolationRootDirectory = isoRoot,
                    NamespaceId = "prv0",
                    WorkerIndex = 71,
                    Modules =
                    [
                        new IsolatedModuleSpecification("isoleaf0.dll")
                        {
                            AlwaysRenameWithWorkerSuffix = true,
                        },
                    ],
                });

            var lib0 = worker0.GetLibrary("isoleaf0");
            var lib1 = worker1.GetLibrary("isoleaf0");
            var export0 = lib0.GetRequiredExport("iso_leaf_add_and_get");
            var export1 = lib1.GetRequiredExport("iso_leaf_add_and_get");

            lib0.GetLoadedFilePath().Should().StartWith(worker0.WorkerDirectory);
            using var rawHandle1 = UnmanagedLibrary.LoadLibrary(lib1.GetLoadedFilePath());
            UnmanagedLibrary.GetLoadedFilePath(rawHandle1).Should().StartWith(worker1.WorkerDirectory);
            lib0.ContainsAddress(export0).Should().BeTrue();
            lib0.ContainsAddress(export1).Should().BeFalse();
            lib0.ContainsAddress(IntPtr.Zero).Should().BeFalse();

            UnmanagedLibrary.TryGetLoadedModuleFromAddress(export0, out var modBase, out var modPath).Should().BeTrue();
            modBase.Should().Be(lib0.BaseAddress);
            modPath.Should().Be(lib0.GetLoadedFilePath());
        }
        finally
        {
            if (System.IO.Directory.Exists(isoRoot))
            {
                System.IO.Directory.Delete(isoRoot, recursive: true);
            }
        }
    }

    [Fact]
#if NET10_0_OR_GREATER
    [SupportedOSPlatform("windows")]
#endif
    public void TryGetLoadedModuleFromAddress_AndGetRequiredSystemExport_ValidateEdgeCases()
    {
        if (TestHelpers.SkipIfNotWindows())
        {
            return;
        }

        UnmanagedLibrary.TryGetLoadedModuleFromAddress(IntPtr.Zero, out var zeroBase, out var zeroPath)
            .Should()
            .BeFalse();
        zeroBase.Should().Be(IntPtr.Zero);
        zeroPath.Should().BeNull();

        var heapBlock = Marshal.AllocHGlobal(64);
        try
        {
            UnmanagedLibrary.TryGetLoadedModuleFromAddress(heapBlock, out _, out _).Should().BeFalse();
        }
        finally
        {
            Marshal.FreeHGlobal(heapBlock);
        }

        var compareString = UnmanagedLibrary.GetRequiredSystemExport("kernel32.dll", "CompareStringW");
        compareString.Should().NotBe(IntPtr.Zero);

        Action missingExport = () =>
            UnmanagedLibrary.GetRequiredSystemExport("kernel32.dll", "NonExistentWin32Api_9999");
        Action rootedModule = () =>
            UnmanagedLibrary.GetRequiredSystemExport("C:\\Windows\\System32\\kernel32.dll", "CompareStringW");
        Action relativeModule = () => UnmanagedLibrary.GetRequiredSystemExport("..\\kernel32.dll", "CompareStringW");

        missingExport.Should().Throw<EntryPointNotFoundException>();
        rootedModule.Should().Throw<ArgumentException>();
        relativeModule.Should().Throw<ArgumentException>();
    }
}
