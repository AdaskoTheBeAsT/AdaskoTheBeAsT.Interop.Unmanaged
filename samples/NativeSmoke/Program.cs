using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using AdaskoTheBeAsT.Interop.Unmanaged;

namespace NativeSmoke;

[SuppressMessage("Sonar", "S6640", Justification = "Exercises exact C fixture signatures using unmanaged function pointers, with the module owner undisposed.")]
internal static class Program
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Binary(int first, int second);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Unary(int value);

    private static unsafe int Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: NativeSmoke <absolute fixture library path>");
            return 2;
        }

        using var library = new UnmanagedLibrary(args[0]);
        if (!library.TryGetExport("fixture_add", out var address))
        {
            return 3;
        }

        var add = (delegate* unmanaged[Cdecl]<int, int, int>)address;
        Check(add(20, 22));
        Check(library.GetUnmanagedFunction<Binary>("fixture_add")!(20, 22));

        using var handle = UnmanagedLibrary.LoadLibrary(args[0]);
        Check(UnmanagedLibrary.GetUnmanagedFunction<Binary>(handle, "fixture_add")!(20, 22));
        if (!UnmanagedLibrary.TryGetExport(handle, "fixture_invoke", out var invokeAddress))
        {
            return 4;
        }

        var invoke = (delegate* unmanaged[Cdecl]<IntPtr, int, int>)invokeAddress;
        Check(invoke((IntPtr)(delegate* unmanaged[Cdecl]<int, int>)&Increment, 41));
        using (var scope = UnmanagedLibrary.PinConcreteDelegate<Unary>(value => value + 1))
        {
            Check(invoke(scope.Ptr, 41));
        }

        if (RuntimeFeature.IsDynamicCodeSupported)
        {
            CheckDynamicCallbacks(invokeAddress);
        }
        else
        {
            CheckDynamicApisAreRejected(address);
        }

        LifetimeChecks.Run(library);
        Console.WriteLine("Native loading, exports, callbacks, and synchronized callback unregistration passed.");
        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Increment(int value)
    {
        return value + 1;
    }

    private static void Check(int result)
    {
        if (result != 42)
        {
            throw new InvalidOperationException("Native smoke test returned an unexpected result.");
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The known callback is Func<int,int>; its signature is preserved by this concrete call site.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Called only when RuntimeFeature.IsDynamicCodeSupported is true.")]
    [DynamicDependency("Invoke", typeof(Func<int, int>))]
    private static unsafe void CheckDynamicCallbacks(IntPtr address)
    {
        using var scope = UnmanagedLibrary.PinDelegate<Func<int, int>>(value => value + 1);
        var invoke = (delegate* unmanaged[Cdecl]<IntPtr, int, int>)address;
        Check(invoke(scope.Ptr, 41));
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Negative test: the runtime capability guard throws before reflection.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Negative test: these APIs must throw before attempting code generation on NativeAOT.")]
    private static void CheckDynamicApisAreRejected(IntPtr address)
    {
        try
        {
            _ = UnmanagedLibrary.GetDelegateForFunctionPointer<Func<int, int, int>>(address, CallingConvention.Cdecl);
            throw new InvalidOperationException("The dynamic thunk API did not reject NativeAOT.");
        }
        catch (PlatformNotSupportedException error)
        {
            Console.WriteLine(error.Message);
        }

        try
        {
            using var scope = UnmanagedLibrary.PinDelegate<Func<int, int>>(value => value + 1);
            throw new InvalidOperationException("The generic callback API did not reject NativeAOT.");
        }
        catch (PlatformNotSupportedException error)
        {
            Console.WriteLine(error.Message);
        }
    }
}
