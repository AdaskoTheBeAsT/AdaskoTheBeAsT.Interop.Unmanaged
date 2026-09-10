using System;
using System.IO;
using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Unmanaged.Test;

public class NativeLibraryLoadingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DefaultFlags_LoadSystemLibraryByName(bool useInstance)
    {
        if (TestHelpers.SkipIfNotWindows())
        {
            return;
        }

        AssertExportCanBeLoaded("xinput9_1_0.dll", useInstance);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DefaultFlags_LoadSystemLibraryByAbsolutePath(bool useInstance)
    {
        if (TestHelpers.SkipIfNotWindows())
        {
            return;
        }

        AssertExportCanBeLoaded(Path.Combine(Environment.SystemDirectory, "xinput9_1_0.dll"), useInstance);
    }

    private static void AssertExportCanBeLoaded(string fileName, bool useInstance)
    {
        bool found;
        IntPtr address;
        if (useInstance)
        {
            using var library = new UnmanagedLibrary(fileName);
            found = library.TryGetExport("XInputGetState", out address);
        }
        else
        {
            using var handle = UnmanagedLibrary.LoadLibrary(fileName);
            found = UnmanagedLibrary.TryGetExport(handle, "XInputGetState", out address);
        }

        found.Should().BeTrue();
        address.Should().NotBe(IntPtr.Zero);
    }
}
