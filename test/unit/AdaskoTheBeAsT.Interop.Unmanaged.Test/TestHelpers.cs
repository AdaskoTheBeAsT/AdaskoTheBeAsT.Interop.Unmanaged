using System.Diagnostics;
using System.Runtime.InteropServices;
using Xunit;

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

    /// <summary>
    /// Returns <see langword="true"/> on non-Windows platforms so that Windows-only tests can
    /// early-return. On xUnit v3 (net8+) this also calls <c>Assert.Skip</c> so the test is
    /// reported as <em>skipped</em> rather than passed. On xUnit v2 (net4x) there is no runtime
    /// skip primitive, so the caller short-circuits with an early <see langword="return"/> and
    /// the test is reported as passing; CI runs on Windows only so this path is not exercised
    /// in automation.
    /// </summary>
    /// <param name="reason">Reason displayed in the test report when the test is skipped.</param>
    /// <returns>
    /// <see langword="true"/> when the current OS is not Windows; otherwise <see langword="false"/>.
    /// </returns>
    public static bool SkipIfNotWindows(string reason = "Windows-only test")
    {
        if (IsWindows())
        {
            return false;
        }

#if NET8_0_OR_GREATER
        Assert.Skip(reason);
#else
        _ = reason;
#endif
        return true;
    }
}
