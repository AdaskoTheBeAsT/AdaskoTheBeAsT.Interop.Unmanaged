using System.Security.Permissions;
using Microsoft.Win32.SafeHandles;

namespace AdaskoTheBeAsT.Interop.Unmanaged;

/// <summary>
/// Represents a Windows module handle that releases the loaded library when disposed.
/// </summary>
/// <remarks>
/// Instances are created by the loading APIs in this package and should be disposed, or passed to
/// <see cref="UnmanagedLibrary.FreeLibrary(SafeLibraryHandle?)"/>, when no longer needed.
/// </remarks>
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
