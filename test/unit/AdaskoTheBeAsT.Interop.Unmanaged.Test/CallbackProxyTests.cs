using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Unmanaged.Test;

public class CallbackProxyTests
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool MarshaledCallback<T>([MarshalAs(UnmanagedType.LPWStr)] T value);

    private delegate int ConcurrentCallback<T>(T value);

    [Fact]
    public void DifferentReturnTypes_CreateDistinctProxies()
    {
        Func<int> first = () => 42;
        Func<long> second = () => long.MaxValue;

        var firstPointer = UnmanagedLibrary.GetFunctionPointerForDelegate(first, out var firstBinder);
        var secondPointer = UnmanagedLibrary.GetFunctionPointerForDelegate(second, out var secondBinder);

        var firstCallback = UnmanagedLibrary.GetDelegateForFunctionPointer<Func<int>>(firstPointer, CallingConvention.Winapi);
        var secondCallback = UnmanagedLibrary.GetDelegateForFunctionPointer<Func<long>>(secondPointer, CallingConvention.Winapi);
        firstCallback!().Should().Be(42);
        secondCallback!().Should().Be(long.MaxValue);
        GetProxy(firstBinder).GetType().Should().NotBe(GetProxy(secondBinder).GetType());
        GC.KeepAlive(firstBinder);
        GC.KeepAlive(secondBinder);
    }

    [Fact]
    public void SameSimpleName_PreservesEachCallingConvention()
    {
        CdeclCallbacks.Callback<int> first = value => value + 1;
        StdCallCallbacks.Callback<int> second = value => value + 2;

        _ = UnmanagedLibrary.GetFunctionPointerForDelegate(first, out var firstBinder);
        _ = UnmanagedLibrary.GetFunctionPointerForDelegate(second, out var secondBinder);

        var firstType = GetProxy(firstBinder).GetType();
        var secondType = GetProxy(secondBinder).GetType();
        firstType.Should().NotBe(secondType);
        var firstAttribute = firstType.GetCustomAttribute<UnmanagedFunctionPointerAttribute>();
        var secondAttribute = secondType.GetCustomAttribute<UnmanagedFunctionPointerAttribute>();
        firstAttribute.Should().NotBeNull();
        secondAttribute.Should().NotBeNull();
        firstAttribute.CallingConvention.Should().Be(CallingConvention.Cdecl);
        secondAttribute.CallingConvention.Should().Be(CallingConvention.StdCall);
    }

    [Fact]
    public void CallbackTypedAsDelegate_UsesRuntimeSignature()
    {
        Delegate callback = new Func<int, int>(value => value + 1);

        var pointer = UnmanagedLibrary.GetFunctionPointerForDelegate(callback, out var binder);

        var invoke = UnmanagedLibrary.GetDelegateForFunctionPointer<Func<int, int>>(pointer, CallingConvention.Winapi);
        invoke!(41).Should().Be(42);
        GC.KeepAlive(binder);
    }

    [Fact]
    public void MulticastCallback_InvokesEverySubscriber()
    {
        var firstResult = 0;
        var secondResult = 0;
        Action<int> callback = value => firstResult = value;
        callback += value => secondResult = value * 2;

        var pointer = UnmanagedLibrary.GetFunctionPointerForDelegate(callback, out var binder);

        var invoke = UnmanagedLibrary.GetDelegateForFunctionPointer<Action<int>>(pointer, CallingConvention.Winapi);
        invoke!(21);
        firstResult.Should().Be(21);
        secondResult.Should().Be(42);
        GC.KeepAlive(binder);
    }

    [Fact]
    public void MarshaledCallback_PreservesReturnAndParameterAttributes()
    {
        MarshaledCallback<string> callback = value => value.Length == 2;

        _ = UnmanagedLibrary.GetFunctionPointerForDelegate(callback, out var binder);

        var invoke = GetProxy(binder).GetType().GetMethod("Invoke")!;
        var returnAttribute = invoke.ReturnParameter.GetCustomAttribute<MarshalAsAttribute>();
        var parameterAttribute = invoke.GetParameters()[0].GetCustomAttribute<MarshalAsAttribute>();
        returnAttribute.Should().NotBeNull();
        parameterAttribute.Should().NotBeNull();
        returnAttribute.Value.Should().Be(UnmanagedType.I1);
        parameterAttribute.Value.Should().Be(UnmanagedType.LPWStr);
    }

    [Fact]
    public void MarshaledCallback_InvokesWithUnicodeString()
    {
        MarshaledCallback<string> callback = value => string.Equals(value, "AB", StringComparison.Ordinal);
        var pointer = UnmanagedLibrary.GetFunctionPointerForDelegate(callback, out var binder);
        var text = Marshal.StringToHGlobalUni("AB");
        try
        {
            var invoke = UnmanagedLibrary.GetDelegateForFunctionPointer<Func<IntPtr, byte>>(pointer, CallingConvention.Cdecl);

            invoke!(text).Should().Be(1);
        }
        finally
        {
            Marshal.FreeHGlobal(text);
            GC.KeepAlive(binder);
        }
    }

    [Fact]
    public void ConcurrentCallbacks_ReuseOneCompletedProxyType()
    {
        var types = new Type[32];
        Parallel.For(
            0,
            types.Length,
            index =>
            {
                ConcurrentCallback<int> callback = value => value + 1;
                _ = UnmanagedLibrary.GetFunctionPointerForDelegate(callback, out var binder);
                types[index] = GetProxy(binder).GetType();
            });

        types.Should().OnlyContain(type => type == types[0]);
    }

    private static Delegate GetProxy(object binder)
    {
        return ((Tuple<Delegate, Delegate>)binder).Item2;
    }

    private static class CdeclCallbacks
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int Callback<T>(T value);
    }

    private static class StdCallCallbacks
    {
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate int Callback<T>(T value);
    }
}
