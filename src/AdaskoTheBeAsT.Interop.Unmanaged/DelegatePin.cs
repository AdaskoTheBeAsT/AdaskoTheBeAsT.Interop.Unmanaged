using System;

namespace AdaskoTheBeAsT.Interop.Unmanaged;

/// <summary>
/// Couples an unmanaged function pointer with a managed object that keeps the originating callback alive.
/// </summary>
/// <remarks>
/// This type does not allocate or free unmanaged memory. It only extends the lifetime of a managed
/// reference for the duration of the surrounding scope.
/// </remarks>
public readonly struct DelegatePin : IDisposable
{
#pragma warning disable S4487
    private readonly object _keepAlive;
#pragma warning restore S4487

    internal DelegatePin(
        IntPtr ptr,
        object keepAlive)
    {
        Ptr = ptr;
        _keepAlive = keepAlive;
    }

    /// <summary>
    /// Gets the unmanaged function pointer associated with this lifetime scope.
    /// </summary>
    public IntPtr Ptr { get; }

    /// <summary>
    /// Ends the keep-alive scope.
    /// </summary>
    /// <remarks>
    /// This method intentionally performs no work because the managed reference is released by
    /// leaving the surrounding scope.
    /// </remarks>
    public void Dispose()
    {
        /* no-op; relies on scope; or free GCHandle if you use one */
    }
}
