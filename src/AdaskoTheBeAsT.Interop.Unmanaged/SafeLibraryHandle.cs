using System;
#if NETFRAMEWORK
using System.Security.Permissions;
#endif
using Microsoft.Win32.SafeHandles;

namespace AdaskoTheBeAsT.Interop.Unmanaged;

/// <summary>
/// Represents a loaded native module handle that releases the library when disposed.
/// </summary>
/// <remarks>
/// On Windows this wraps an <c>HMODULE</c> from <c>LoadLibraryEx</c> and releases it with
/// <c>FreeLibrary</c>. On Linux and macOS this wraps the handle returned by <c>dlopen</c>
/// and releases it with <c>dlclose</c>. Instances are created by the loading APIs in this
/// package and should be disposed, or passed to
/// <see cref="UnmanagedLibrary.FreeLibrary(SafeLibraryHandle?)"/>, when no longer needed.
/// </remarks>
// ReSharper disable ClassNeverInstantiated.Global
#pragma warning disable S3453
#if NETFRAMEWORK
[SecurityPermission(SecurityAction.LinkDemand, UnmanagedCode = true)]
#endif
public sealed class SafeLibraryHandle : SafeHandleZeroOrMinusOneIsInvalid
#pragma warning restore S3453
{
    /// <summary>
    /// Initializes a new, empty <see cref="SafeLibraryHandle"/>. Used by the P/Invoke marshaller.
    /// </summary>
    internal SafeLibraryHandle()
        : base(true)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="SafeLibraryHandle"/> that wraps an existing native handle.
    /// </summary>
    /// <param name="existingHandle">Native handle to take ownership of.</param>
    /// <param name="ownsHandle">Whether the handle should be released when disposed.</param>
    internal SafeLibraryHandle(IntPtr existingHandle, bool ownsHandle)
        : base(ownsHandle)
    {
        SetHandle(existingHandle);
    }

    /// <inheritdoc />
    protected override bool ReleaseHandle()
    {
        return NativeLoader.Free(handle);
    }
}

// ReSharper restore ClassNeverInstantiated.Global
