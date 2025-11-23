using System;

namespace AdaskoTheBeAsT.Interop.Unmanaged;

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

    public IntPtr Ptr { get; }

    public void Dispose()
    {
        /* no-op; relies on scope; or free GCHandle if you use one */
    }
}
