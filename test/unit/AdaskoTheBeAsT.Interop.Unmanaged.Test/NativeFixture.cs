using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;

namespace AdaskoTheBeAsT.Interop.Unmanaged.Test;

internal sealed class NativeFixture : IDisposable
{
    [SuppressMessage("Security", "SCS0018", Justification = "Copies only build outputs to a test-owned random temporary directory.")]
    public NativeFixture()
    {
        DirectoryPath = Path.Combine(Path.GetTempPath(), "Unmanaged fixture żółć 日本語 " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "native")))
        {
            File.Copy(file, Path.Combine(DirectoryPath, Path.GetFileName(file)));
        }
    }

    public string DirectoryPath { get; }

    public string LibraryPath => GetPath("fixture");

    public string GetPath(string name)
    {
#if NET8_0_OR_GREATER
        var extension = OperatingSystem.IsMacOS() ? ".dylib" : ".so";
#else
        var extension = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? ".dylib" : ".so";
#endif
        var file = TestHelpers.IsWindows()
            ? name + ".dll"
            : "lib" + name + extension;
        return Path.Combine(DirectoryPath, file);
    }

    [SuppressMessage("Security", "SCS0018", Justification = "Deletes only the random temporary directory created and owned by this fixture.")]
    public void Dispose()
    {
        Directory.Delete(DirectoryPath, recursive: true);
    }
}
