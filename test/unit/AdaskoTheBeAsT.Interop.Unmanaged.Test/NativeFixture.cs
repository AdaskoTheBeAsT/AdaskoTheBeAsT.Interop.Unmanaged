using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace AdaskoTheBeAsT.Interop.Unmanaged.Test;

#pragma warning disable SA1121

internal sealed class NativeFixture : IDisposable
{
    [SuppressMessage(
        "Security",
        "SCS0018",
        Justification = "Copies only build outputs to a test-owned random temporary directory.")]
    public NativeFixture()
    {
        DirectoryPath = Path.Combine(Path.GetTempPath(), "Unmanaged fixture żółć 日本語 " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "native")))
        {
            File.Copy(file, Path.Combine(DirectoryPath, Path.GetFileName(file)));
        }

        CopyCompanionDirectoryIfPresent("native-x86", "x86");
        CopyCompanionDirectoryIfPresent("native-x64", "x64");
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

    public string GetExecutablePath(string name)
    {
        var file = TestHelpers.IsWindows() ? name + ".exe" : name;
        return Path.Combine(DirectoryPath, file);
    }

    [SuppressMessage("Security", "SCS0018", Justification = "Resolves test-owned fixture copy path.")]
    public string? TryGetPe32ExecutablePath(string name)
    {
        if (!TestHelpers.IsWindows())
        {
            return null;
        }

        if (IntPtr.Size == 4)
        {
            return GetExecutablePath(name);
        }

        var candidate = Path.Combine(DirectoryPath, "x86", name + ".exe");
        return File.Exists(candidate) ? candidate : null;
    }

    [SuppressMessage("Security", "SCS0018", Justification = "Resolves test-owned fixture copy path.")]
    public string? TryGetPe32PlusExecutablePath(string name)
    {
        if (!TestHelpers.IsWindows())
        {
            return null;
        }

        if (IntPtr.Size == 8)
        {
            return GetExecutablePath(name);
        }

        var candidate = Path.Combine(DirectoryPath, "x64", name + ".exe");
        return File.Exists(candidate) ? candidate : null;
    }

    [SuppressMessage("Security", "SCS0018", Justification = "Resolves test-owned fixture copy path.")]
    public string? TryGetPe32Path(string name)
    {
        if (!TestHelpers.IsWindows())
        {
            return null;
        }

        if (IntPtr.Size == 4)
        {
            return GetPath(name);
        }

        var candidate = Path.Combine(DirectoryPath, "x86", name + ".dll");
        return File.Exists(candidate) ? candidate : null;
    }

    [SuppressMessage("Security", "SCS0018", Justification = "Resolves test-owned fixture copy path.")]
    public string? TryGetPe32PlusPath(string name)
    {
        if (!TestHelpers.IsWindows())
        {
            return null;
        }

        if (IntPtr.Size == 8)
        {
            return GetPath(name);
        }

        var candidate = Path.Combine(DirectoryPath, "x64", name + ".dll");
        return File.Exists(candidate) ? candidate : null;
    }

    [SuppressMessage(
        "Security",
        "SCS0018",
        Justification = "Deletes only the random temporary directory created and owned by this fixture.")]
    public void Dispose()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                if (Directory.Exists(DirectoryPath))
                {
                    Directory.Delete(DirectoryPath, recursive: true);
                }

                return;
            }
            catch (Exception exception) when (attempt < 9 && (exception is IOException or UnauthorizedAccessException))
            {
                Thread.Sleep(25);
            }
        }
    }

    [SuppressMessage(
        "Security",
        "SCS0018",
        Justification = "Copies build-generated companion architecture binaries into a test-owned directory.")]
    private void CopyCompanionDirectoryIfPresent(
        string sourceFolderName,
        string targetFolderName)
    {
        var sourceDir = Path.Combine(AppContext.BaseDirectory, sourceFolderName);
        if (!Directory.Exists(sourceDir))
        {
            return;
        }

        var targetDir = Path.Combine(DirectoryPath, targetFolderName);
        Directory.CreateDirectory(targetDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)));
        }
    }
}
