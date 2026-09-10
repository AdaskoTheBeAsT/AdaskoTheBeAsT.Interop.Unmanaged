using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using AdaskoTheBeAsT.Interop.Unmanaged;

namespace CallbackBenchmarks;

internal static class Program
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Callback<T>();

    [SuppressMessage("Sonar", "S3400", Justification = "An actual static method is needed to bind each benchmark delegate type.")]
    public static int ReturnValue()
    {
        return 42;
    }

    private static void Main()
    {
        // Separate process runs are needed to compare cold measurements. No timing assertions.
        var callbacks = CreateDistinctCallbacks();
        var warm = callbacks[0];
        Measure("cold first proxy", 1, () => Create(warm));
        Measure("warm same runtime type", 10000, () => Create(warm));
        Measure("cold distinct runtime types", callbacks.Length - 1, CreateNext);
        Measure("warm distinct runtime types", callbacks.Length - 1, CreateNext);
        void CreateNext(int index)
        {
            Create(callbacks[index + 1]);
        }
    }

    private static void Create(Delegate callback)
    {
        _ = UnmanagedLibrary.GetFunctionPointerForDelegate(callback, out var binder);
        GC.KeepAlive(binder);
    }

    private static void Measure(string name, int count, Action operation)
    {
        Measure(name, count, _ => operation?.Invoke());
    }

    private static void Measure(string name, int count, Action<int> operation)
    {
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        for (var index = 0; index < count; index++)
        {
            operation?.Invoke(index);
        }

        watch.Stop();
        var bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{name}: operations={count}, elapsed-ms={watch.Elapsed.TotalMilliseconds:F3}, allocated-bytes={bytes}"));
    }

    private static Delegate[] CreateDistinctCallbacks()
    {
        var callbacks = new Delegate[64];
        var marker = typeof(int);
        var method = typeof(Program).GetMethod(
            nameof(ReturnValue),
            BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly,
            Type.EmptyTypes)!;
        for (var index = 0; index < callbacks.Length; index++)
        {
            var type = typeof(Callback<>).MakeGenericType(marker);
            callbacks[index] = Delegate.CreateDelegate(type, method);
            marker = typeof(ValueTuple<>).MakeGenericType(marker);
        }

        return callbacks;
    }
}
