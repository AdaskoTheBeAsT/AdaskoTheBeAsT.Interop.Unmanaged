using System;

namespace AdaskoTheBeAsT.Interop.Unmanaged;

/// <summary>
/// Couples an unmanaged function pointer with a managed object that keeps the originating callback alive.
/// </summary>
/// <remarks>
/// This type does not allocate or free unmanaged memory. It only extends the lifetime of a managed
/// reference for the duration of the surrounding scope.
/// It does not pin memory, unregister callbacks, invalidate pointers, or synchronize native work.
/// Unregister retained callbacks and wait for them to finish before releasing this callback root.
/// </remarks>
public readonly struct DelegatePin : IDisposable
{
    private readonly object _keepAlive;

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
    /// Keeps the managed reference alive through the end of the scope, even when the JIT
    /// would otherwise consider its last use to have occurred earlier.
    /// </remarks>
    public void Dispose()
    {
        GC.KeepAlive(_keepAlive);
    }
}
