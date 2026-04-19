using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AdaskoTheBeAsT.Interop.Unmanaged.Test;

internal static class TestHelpers
{
    public static uint GetCurrentProcessId()
    {
#if NET5_0_OR_GREATER
        return (uint)System.Environment.ProcessId;
#else
        return (uint)Process.GetCurrentProcess().Id;
#endif
    }

    public static bool IsWindows()
    {
#if NET5_0_OR_GREATER
        return System.OperatingSystem.IsWindows();
#else
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
#endif
    }
}
