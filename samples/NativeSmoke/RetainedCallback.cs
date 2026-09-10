using System;
using System.Runtime.InteropServices;
using AdaskoTheBeAsT.Interop.Unmanaged;

namespace NativeSmoke;

internal sealed class RetainedCallback : IDisposable
{
    private readonly UnmanagedLibrary _library;
    private readonly Unregister _unregister;
    private object? _binder;

    public RetainedCallback(UnmanagedLibrary library, LifetimeChecks.Callback callback)
    {
        _library = library;
        _unregister = library.GetUnmanagedFunction<Unregister>("fixture_unregister")!;

        // This no-emission example uses a statically known concrete delegate.
        _binder = callback;
        var pointer = Marshal.GetFunctionPointerForDelegate(callback);
        library.GetUnmanagedFunction<Register>("fixture_register")!(pointer);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void Register(IntPtr callback);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void Unregister();

    public void Dispose()
    {
        if (_binder == null)
        {
            return;
        }

        // This fixture's unregister waits for in-flight callbacks. Other APIs may need a separate wait.
        _unregister();
        GC.KeepAlive(_binder);
        _binder = null;
        GC.KeepAlive(_library);
    }
}
