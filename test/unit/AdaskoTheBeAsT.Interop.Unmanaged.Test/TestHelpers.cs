using System.Diagnostics;

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
}
