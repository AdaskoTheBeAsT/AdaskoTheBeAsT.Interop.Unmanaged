using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Threading;
using AdaskoTheBeAsT.Interop.Unmanaged;

namespace NativeSmoke;

internal static class LifetimeChecks
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int Callback(int value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int InvokeRegistered(int value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetRegistered();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void Block(IntPtr callback);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void Release();

    public static void Run(UnmanagedLibrary library)
    {
        CheckUnregistration(library);
        CheckBlockingOperation(library);
    }

    private static void CheckUnregistration(UnmanagedLibrary library)
    {
        using var entered = new ManualResetEventSlim();
        using var finish = new ManualResetEventSlim();
        using var unregistered = new ManualResetEventSlim();
        using var registration = CreateRegistration(library, entered, finish);
        Collect();
        var invoke = library.GetUnmanagedFunction<InvokeRegistered>("fixture_invoke_registered")!;
        var isRegistered = library.GetUnmanagedFunction<GetRegistered>("fixture_registered")!;
        var result = 0;
        var caller = new Thread(() => result = invoke(41)) { IsBackground = true };
        caller.Start();
        Require(entered.Wait(TimeSpan.FromSeconds(5)));
        var remover = new Thread(() =>
        {
            registration.Dispose();
            unregistered.Set();
        })
        {
            IsBackground = true,
        };
        remover.Start();
        Require(SpinWait.SpinUntil(() => isRegistered() == 0, TimeSpan.FromSeconds(5)));
        Require(!unregistered.IsSet);
        finish.Set();
        Require(caller.Join(TimeSpan.FromSeconds(5)));
        Require(remover.Join(TimeSpan.FromSeconds(5)));
        Require(result == 42 && invoke(41) == -1);
    }

    private static void CheckBlockingOperation(UnmanagedLibrary library)
    {
        using var entered = new ManualResetEventSlim();
        using var scope = UnmanagedLibrary.PinConcreteDelegate<Callback>(_ =>
        {
            entered.Set();
            return 0;
        });
        var block = library.GetUnmanagedFunction<Block>("fixture_block")!;
        var release = library.GetUnmanagedFunction<Release>("fixture_release")!;
        var pointer = scope.Ptr;
        var worker = new Thread(() => block(pointer)) { IsBackground = true };
        worker.Start();
        Require(entered.Wait(TimeSpan.FromSeconds(5)));
        Collect();
        release();
        Require(worker.Join(TimeSpan.FromSeconds(5)));
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static RetainedCallback CreateRegistration(UnmanagedLibrary library, ManualResetEventSlim entered, ManualResetEventSlim finish)
    {
        return new RetainedCallback(library, value =>
        {
            // A native callback must translate exceptions into its native error convention.
            try
            {
                entered.Set();
                return finish.Wait(TimeSpan.FromSeconds(10)) ? value + 1 : -1;
            }
            catch (Exception)
            {
                return -1;
            }
        });
    }

    [SuppressMessage("Sonar", "S1215", Justification = "The isolated consumer verifies callbacks survive forced collection.")]
    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static void Require(bool condition)
    {
        if (!condition)
        {
            // This isolated process must not unwind and unload a module with active native work.
            Environment.FailFast("Native lifetime smoke check failed or timed out.");
        }
    }
}
