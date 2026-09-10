using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace AdaskoTheBeAsT.Interop.Unmanaged;

/// <summary>
/// Platform-dispatched native library loading primitives.
/// </summary>
/// <remarks>
/// On Windows this type always uses <c>LoadLibraryEx</c>/<c>GetProcAddress</c>/<c>FreeLibrary</c>
/// to honor <see cref="LoadLibraryFlags"/>, adapting the default flag combination for bare names.
/// On Linux and macOS running on .NET 8.0 and newer it delegates to
/// <c>System.Runtime.InteropServices.NativeLibrary</c>; on .NET Framework
/// (net4.6.2 through net4.8.1, including Mono) it dispatches to platform-specific
/// <c>dlopen</c>/<c>dlsym</c>/<c>dlclose</c> P/Invokes.
/// </remarks>
internal static class NativeLoader
{
    public static IntPtr Load(string fileName, LoadLibraryFlags flags)
    {
        if (IsWindows())
        {
            return LoadWindowsLibrary(fileName, flags);
        }

#if NET8_0_OR_GREATER
        _ = flags;
        try
        {
            return NativeLibrary.Load(fileName);
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or FileLoadException)
        {
            // The message/inner constructor otherwise captures an unrelated ambient last error.
            var lastError = Marshal.GetLastPInvokeError();
            try
            {
                Marshal.SetLastPInvokeError(0);
                throw new Win32Exception($"Failed to load library '{fileName}'. {ex.Message}", ex);
            }
            finally
            {
                Marshal.SetLastPInvokeError(lastError);
            }
        }
#else
        if (IsOsx())
        {
            OsxNativeMethods.dlerror();
            var handle = OsxNativeMethods.dlopen(fileName, OsxNativeMethods.RTLD_NOW);
            if (handle == IntPtr.Zero)
            {
                var error = ReadDlError(isOsx: true);
                throw new Win32Exception(
                    0,
                    $"Failed to load library '{fileName}'. {error}");
            }

            return handle;
        }

        if (IsLinux())
        {
            LinuxNativeMethods.dlerror();
            var handle = LinuxNativeMethods.dlopen(fileName, LinuxNativeMethods.RTLD_NOW);
            if (handle == IntPtr.Zero)
            {
                var error = ReadDlError(isOsx: false);
                throw new Win32Exception(
                    0,
                    $"Failed to load library '{fileName}'. {error}");
            }

            return handle;
        }

        throw new PlatformNotSupportedException(
            "AdaskoTheBeAsT.Interop.Unmanaged supports Windows, Linux, and macOS only.");
#endif
    }

    public static bool Free(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return true;
        }

        if (IsWindows())
        {
            return WindowsNativeMethods.FreeLibrary(handle);
        }

#if NET8_0_OR_GREATER
        NativeLibrary.Free(handle);
        return true;
#else
        if (IsOsx())
        {
            return OsxNativeMethods.dlclose(handle) == 0;
        }

        if (IsLinux())
        {
            return LinuxNativeMethods.dlclose(handle) == 0;
        }

        return false;
#endif
    }

    public static IntPtr GetExport(IntPtr handle, string name)
    {
        if (IsWindows())
        {
            return WindowsNativeMethods.GetProcAddress(handle, name);
        }

#if NET8_0_OR_GREATER
        return NativeLibrary.TryGetExport(handle, name, out var addr) ? addr : IntPtr.Zero;
#else
        if (IsOsx())
        {
            return OsxNativeMethods.dlsym(handle, name);
        }

        if (IsLinux())
        {
            return LinuxNativeMethods.dlsym(handle, name);
        }

        return IntPtr.Zero;
#endif
    }

    private static IntPtr LoadWindowsLibrary(string fileName, LoadLibraryFlags flags)
    {
        // DLL_LOAD_DIR requires a full path. For a bare name, keep the default
        // search restricted to System32 rather than falling back to legacy search.
        if (flags == (LoadLibraryFlags.LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LoadLibraryFlags.LOAD_LIBRARY_SEARCH_SYSTEM32)
            && string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
        {
            flags = LoadLibraryFlags.LOAD_LIBRARY_SEARCH_SYSTEM32;
        }

        var handle = WindowsNativeMethods.LoadLibraryEx(fileName, IntPtr.Zero, flags);
        if (handle == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            throw new Win32Exception(
                error,
                $"Failed to load library '{fileName}'. {new Win32Exception(error).Message}");
        }

        return handle;
    }

    private static bool IsWindows()
    {
#if NET5_0_OR_GREATER
        return OperatingSystem.IsWindows();
#else
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
#endif
    }

#if !NET8_0_OR_GREATER
    private static bool IsLinux()
    {
#if NET5_0_OR_GREATER
        return OperatingSystem.IsLinux();
#else
        return RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
#endif
    }

    private static bool IsOsx()
    {
#if NET5_0_OR_GREATER
        return OperatingSystem.IsMacOS();
#else
        return RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
#endif
    }

    private static string ReadDlError(bool isOsx)
    {
        var errorPtr = isOsx ? OsxNativeMethods.dlerror() : LinuxNativeMethods.dlerror();
        if (errorPtr == IntPtr.Zero)
        {
            return string.Empty;
        }

        return Marshal.PtrToStringAnsi(errorPtr) ?? string.Empty;
    }
#endif
}
