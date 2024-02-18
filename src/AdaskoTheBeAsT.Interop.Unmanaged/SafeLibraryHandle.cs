using System.Security.Permissions;
using Microsoft.Win32.SafeHandles;

namespace AdaskoTheBeAsT.Interop.Unmanaged;

/// <summary>
/// See http://msdn.microsoft.com/msdnmag/issues/05/10/Reliability/ for more about safe handles.
/// </summary>
// ReSharper disable ClassNeverInstantiated.Global
#pragma warning disable S3453
#if NETSTANDARD2_0
[SecurityPermission(SecurityAction.LinkDemand, UnmanagedCode = true)]
#endif
public sealed class SafeLibraryHandle : SafeHandleZeroOrMinusOneIsInvalid
#pragma warning restore S3453
{
    private SafeLibraryHandle()
        : base(true)
    {
    }

    protected override bool ReleaseHandle()
    {
        return NativeMethods.FreeLibrary(handle);
    }
}

// ReSharper restore ClassNeverInstantiated.Global
