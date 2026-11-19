using System;
using System.Reflection;
using System.Runtime.InteropServices;
using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Unmanaged.Test;

#pragma warning disable SA1121

public class UnmanagedLibraryAdvancedTests
{
    private delegate int SimpleDelegate(
        int x,
        int y);

    private delegate void GenericDelegate<T>(T value);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint GetCurrentProcessIdDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr GetModuleHandleWByIntPtrDelegate(IntPtr lpModuleName);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int CdeclGenericDelegate<T>(T value);

    [UnmanagedFunctionPointer(
        CallingConvention.StdCall,
        CharSet = CharSet.Unicode,
        BestFitMapping = false,
        ThrowOnUnmappableChar = true,
        SetLastError = true)]
    private delegate int FullyDecoratedGenericDelegate<T>(T value);

    [Fact]
    public void GetFunctionPointerForDelegate_WithSimpleDelegate_ReturnsValidPointer()
    {
        // Arrange
        static int Callback(
int x,
int y) => x + y;

        // Act
        var ptr = UnmanagedLibrary.GetFunctionPointerForDelegate((SimpleDelegate)Callback, out var binder);

        // Assert
        ptr.Should().NotBe(IntPtr.Zero);
        binder.Should().NotBeNull();
    }

    [Fact]
    public void GetFunctionPointerForDelegate_WithNullDelegate_ThrowsArgumentNullException()
    {
        // Act
        Action act = () => _ = UnmanagedLibrary.GetFunctionPointerForDelegate<Action>(null!, out _);

        // Assert
        act.Should()
            .Throw<ArgumentNullException>()
            .WithParameterName("delegateCallback");
    }

    [Fact]
    public void GetFunctionPointerForDelegate_BinderKeepsDelegateAlive()
    {
        // Arrange
        SimpleDelegate callback = (
            x,
            y) => x + y;

        // Act
        _ = UnmanagedLibrary.GetFunctionPointerForDelegate(callback, out var binder);

        // Assert
        binder.Should().NotBeNull();
        binder.Should().BeSameAs(callback); // For non-generic delegates, binder should be the delegate itself
    }

    [Fact]
    public void GetFunctionPointerForDelegate_WithGenericDelegate_ReturnsValidPointer()
    {
        // Arrange
        static void Callback(int value) => Console.WriteLine(value);

        // Act
        var ptr = UnmanagedLibrary.GetFunctionPointerForDelegate((GenericDelegate<int>)Callback, out var binder);

        // Assert
        ptr.Should().NotBe(IntPtr.Zero);
        binder.Should().NotBeNull();
    }

    [Fact]
    public void GetFunctionPointerForDelegate_WithGenericDelegate_CreatesDynamicAssembly()
    {
        // Arrange
        static void Callback(string value) => Console.WriteLine(value);
        var assembliesBefore = AppDomain.CurrentDomain.GetAssemblies().Length;

        // Act
        var ptr = UnmanagedLibrary.GetFunctionPointerForDelegate((GenericDelegate<string>)Callback, out var _);

        // Assert
        ptr.Should().NotBe(IntPtr.Zero);
        var assembliesAfter = AppDomain.CurrentDomain.GetAssemblies().Length;
        assembliesAfter.Should().BeGreaterThanOrEqualTo(assembliesBefore); // May create a new assembly
    }

    [Fact]
    public void GetFunctionPointerForDelegate_WithGenericDelegate_BinderContainsBothDelegates()
    {
        // Arrange
        static void Callback(int value) => Console.WriteLine(value);

        // Act
        _ = UnmanagedLibrary.GetFunctionPointerForDelegate((GenericDelegate<int>)Callback, out var binder);

        // Assert
        binder.Should().NotBeNull();

        // For generic delegates, binder is a Tuple containing both the original and proxy delegates
        binder.Should().BeOfType<Tuple<Delegate, Delegate>>();
    }

    [Fact]
    public void GetFunctionPointerForDelegate_SameGenericDelegateTypeTwice_ReusesProxyAssembly()
    {
        // Arrange
        static void Callback1(int value) => Console.WriteLine(value);
        static void Callback2(int value) => Console.WriteLine(value * 2);

        // Act
        var ptr1 = UnmanagedLibrary.GetFunctionPointerForDelegate((GenericDelegate<int>)Callback1, out var binder1);
        var ptr2 = UnmanagedLibrary.GetFunctionPointerForDelegate((GenericDelegate<int>)Callback2, out var binder2);

        // Assert
        ptr1.Should().NotBe(IntPtr.Zero);
        ptr2.Should().NotBe(IntPtr.Zero);
        var proxy1 = ((Tuple<Delegate, Delegate>)binder1).Item2;
        var proxy2 = ((Tuple<Delegate, Delegate>)binder2).Item2;
        proxy2.GetType().Should().BeSameAs(proxy1.GetType());
    }

    [Fact]
    public void GetFunctionPointerForDelegate_DifferentGenericTypes_CreatesSeparateProxies()
    {
        // Arrange
        static void Callback1(int value) => Console.WriteLine(value);
        static void Callback2(string value) => Console.WriteLine(value);

        // Act
        var ptr1 = UnmanagedLibrary.GetFunctionPointerForDelegate((GenericDelegate<int>)Callback1, out var _);
        var ptr2 = UnmanagedLibrary.GetFunctionPointerForDelegate((GenericDelegate<string>)Callback2, out var _);

        // Assert
        ptr1.Should().NotBe(IntPtr.Zero);
        ptr2.Should().NotBe(IntPtr.Zero);
        ptr1.Should().NotBe(ptr2); // Different function pointers
    }

    [Fact]
    public void GetDelegateForFunctionPointer_WithZeroPointer_ThrowsArgumentException()
    {
        // Act
        Action act = () =>
            _ = UnmanagedLibrary.GetDelegateForFunctionPointer<SimpleDelegate>(IntPtr.Zero, CallingConvention.StdCall);

        // Assert
        act.Should()
            .Throw<ArgumentException>()
            .WithParameterName("ptr");
    }

    [Fact]
    public void GetDelegateForFunctionPointer_WithNonDelegateType_ThrowsInvalidOperationException()
    {
        // Act
        Action act = () =>
            _ = UnmanagedLibrary.GetDelegateForFunctionPointer<object>(new IntPtr(1), CallingConvention.StdCall);

        // Assert
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*delegate type*");
    }

    [Fact]
    public void GetDelegateForFunctionPointer_RoundTrip_NoArgs_InvokesNativeFunction()
    {
        if (TestHelpers.SkipIfNotWindows())
        {
            return;
        }

        // Arrange - obtain raw native function pointer for kernel32!GetCurrentProcessId
        using var handle = UnmanagedLibrary.LoadLibrary("kernel32.dll");
        var original =
            UnmanagedLibrary.GetUnmanagedFunction<GetCurrentProcessIdDelegate>(handle, "GetCurrentProcessId");
        var ptr = Marshal.GetFunctionPointerForDelegate(original!);

        // Act - re-wrap via IL-emit path with unmanaged calling convention
        var rewrapped = UnmanagedLibrary.GetDelegateForFunctionPointer<GetCurrentProcessIdDelegate>(
            ptr,
            CallingConvention.Winapi);
        var pid = rewrapped!();

        // Assert
        pid.Should().BeGreaterThan(0u);
        pid.Should().Be(TestHelpers.GetCurrentProcessId());
        GC.KeepAlive(original);
    }

    [Fact]
    public void GetDelegateForFunctionPointer_RoundTrip_WithPointerArg_InvokesNativeFunction()
    {
        if (TestHelpers.SkipIfNotWindows())
        {
            return;
        }

        // NOTE: the IL-emit calli path does NOT perform parameter marshaling. All parameters must
        // already be native-compatible (primitives, IntPtr, structs with blittable layout). For
        // marshaling (string <-> LPWStr etc.) use Marshal.GetDelegateForFunctionPointer<T>(ptr).

        // Arrange - kernel32!GetModuleHandleW (LPCWSTR -> HMODULE); we pass IntPtr.Zero (NULL)
        // which asks for the exe module handle.
        using var handle = UnmanagedLibrary.LoadLibrary("kernel32.dll");
        var original = UnmanagedLibrary.GetUnmanagedFunction<GetModuleHandleWByIntPtrDelegate>(
            handle,
            "GetModuleHandleW");
        var ptr = Marshal.GetFunctionPointerForDelegate(original!);

        // Act
        var rewrapped = UnmanagedLibrary.GetDelegateForFunctionPointer<GetModuleHandleWByIntPtrDelegate>(
            ptr,
            CallingConvention.Winapi);
        var module = rewrapped!(IntPtr.Zero);

        // Assert
        module.Should().NotBe(IntPtr.Zero);
        GC.KeepAlive(original);
    }

    [Fact]
    public void GetFunctionPointerForDelegate_GenericDelegate_PropagatesUnmanagedFunctionPointerAttribute()
    {
        // Arrange - generic delegate decorated with [UnmanagedFunctionPointer(Cdecl)]
        static int Callback(int x) => x + 1;

        // Act
        _ = UnmanagedLibrary.GetFunctionPointerForDelegate((CdeclGenericDelegate<int>)Callback, out var binder);

        // Assert - binder is Tuple<originalDelegate, proxyDelegate>; the proxy type should carry the attribute
        binder.Should().BeOfType<Tuple<Delegate, Delegate>>();
        var tuple = (Tuple<Delegate, Delegate>)binder;
        var proxyType = tuple.Item2.GetType();

        var attr = proxyType.GetCustomAttribute<UnmanagedFunctionPointerAttribute>();
        attr.Should().NotBeNull();
        attr.CallingConvention.Should().Be(CallingConvention.Cdecl);
    }

    [Fact]
    public void GetFunctionPointerForDelegate_GenericDelegate_PropagatesAllUnmanagedFunctionPointerFields()
    {
        // Arrange - generic delegate decorated with every UnmanagedFunctionPointer field
        static int Callback(int x) => x + 1;

        // Act
        _ = UnmanagedLibrary.GetFunctionPointerForDelegate((FullyDecoratedGenericDelegate<int>)Callback, out var binder);

        // Assert - the proxy type should carry ALL configured fields, not only CallingConvention
        binder.Should().BeOfType<Tuple<Delegate, Delegate>>();
        var tuple = (Tuple<Delegate, Delegate>)binder;
        var proxyType = tuple.Item2.GetType();

        var attr = proxyType.GetCustomAttribute<UnmanagedFunctionPointerAttribute>();
        attr.Should().NotBeNull();
        attr.CallingConvention.Should().Be(CallingConvention.StdCall);
        attr.CharSet.Should().Be(CharSet.Unicode);
        attr.BestFitMapping.Should().BeFalse();
        attr.ThrowOnUnmappableChar.Should().BeTrue();
        attr.SetLastError.Should().BeTrue();
    }

    [Fact]
    public void GetFunctionPointerForDelegate_WithManagedCallback_ReturnsValidPointer()
    {
        // Arrange
        static int OriginalCallback(
int x,
int y) => x + y;

        // Act
        var ptr = UnmanagedLibrary.GetFunctionPointerForDelegate((SimpleDelegate)OriginalCallback, out var binder);

        // Assert
        ptr.Should().NotBe(IntPtr.Zero);
        binder.Should().NotBeNull();

        // Can invoke the original callback
        var result = OriginalCallback(10, 20);
        result.Should().Be(30);
    }

    [Fact]
    public void GetFunctionPointerForDelegate_WithMultipleCallbacks_ReturnsValidPointers()
    {
        // Arrange
        static int Callback1(
int x,
int y) => x + y;
        static int Callback2(
int x,
int y) => x * y;

        // Act
        var ptr1 = UnmanagedLibrary.GetFunctionPointerForDelegate((SimpleDelegate)Callback1, out _);
        var ptr2 = UnmanagedLibrary.GetFunctionPointerForDelegate((SimpleDelegate)Callback2, out _);

        // Assert
        ptr1.Should().NotBe(IntPtr.Zero);
        ptr2.Should().NotBe(IntPtr.Zero);
        ptr1.Should().NotBe(ptr2);
        Callback1(10, 20).Should().Be(30);
        Callback2(10, 20).Should().Be(200);
    }

    [Fact]
    public void GetFunctionPointerForDelegate_WithFuncDelegate_WorksCorrectly()
    {
        // Arrange
        static int Callback(
int a,
int b,
int c) => a + b + c;

        // Act
        var ptr = UnmanagedLibrary.GetFunctionPointerForDelegate(Callback, out var binder);

        // Assert
        ptr.Should().NotBe(IntPtr.Zero);
        binder.Should().NotBeNull();
        Callback(5, 10, 15).Should().Be(30);
    }

    [Fact]
    public void GetFunctionPointerForDelegate_WithAction_WorksCorrectly()
    {
        // Arrange
        var called = false;
        void Callback() => called = true;

        // Act
        var ptr = UnmanagedLibrary.GetFunctionPointerForDelegate(Callback, out var binder);

        // Assert
        ptr.Should().NotBe(IntPtr.Zero);
        binder.Should().NotBeNull();
        Callback();
        called.Should().BeTrue();
    }

    [Fact]
    public void GetFunctionPointerForDelegate_WithStringFunc_WorksCorrectly()
    {
        // Arrange
        static int Callback(string str) => str.Length;

        // Act
        var ptr = UnmanagedLibrary.GetFunctionPointerForDelegate(Callback, out var binder);

        // Assert
        ptr.Should().NotBe(IntPtr.Zero);
        binder.Should().NotBeNull();
        Callback("Hello").Should().Be(5);
    }

    [Fact]
    public void GetFunctionPointerForDelegate_WithNativeFunction_WorksCorrectly()
    {
        if (TestHelpers.SkipIfNotWindows())
        {
            return;
        }

        // Arrange - Get a real native function pointer
        using var library = new UnmanagedLibrary("kernel32.dll");
        var nativeDelegate = library.GetUnmanagedFunction<GetCurrentProcessIdDelegate>("GetCurrentProcessId");

        // Act - Get function pointer from native delegate
        var nativePtr = Marshal.GetFunctionPointerForDelegate(nativeDelegate!);

        // Assert
        nativePtr.Should().NotBe(IntPtr.Zero);
        var processId = nativeDelegate!();
        processId.Should().Be(TestHelpers.GetCurrentProcessId());
    }

    [Fact]
    public void
        GetFunctionPointerForDelegate_GenericDelegate_WithoutUnmanagedFunctionPointerAttribute_DoesNotCopyAttribute()
    {
        // Arrange - a generic delegate type WITHOUT [UnmanagedFunctionPointer]
        static void Callback(double value) => Console.WriteLine(value);

        // Act
        _ = UnmanagedLibrary.GetFunctionPointerForDelegate((GenericDelegate<double>)Callback, out var binder);

        // Assert - proxy exists but has no UFP attribute (we do not synthesize one)
        binder.Should().BeOfType<Tuple<Delegate, Delegate>>();
        var tuple = (Tuple<Delegate, Delegate>)binder;
        var proxyType = tuple.Item2.GetType();
        proxyType.GetCustomAttribute<UnmanagedFunctionPointerAttribute>().Should().BeNull();
    }

    [Fact]
    public void GetDelegateForFunctionPointer_CdeclConvention_EmitsDelegate()
    {
        if (TestHelpers.SkipIfNotWindows())
        {
            return;
        }

        // Arrange - kernel32!GetCurrentProcessId uses Winapi/StdCall on x64; request Cdecl to
        // exercise the CallingConvention argument path (IL is still emitted; a mismatched
        // calling convention only manifests when invoking, so we don't invoke here).
        using var handle = UnmanagedLibrary.LoadLibrary("kernel32.dll");
        var found = UnmanagedLibrary.TryGetExport(handle, "GetCurrentProcessId", out var ptr);
        found.Should().BeTrue();
        ptr.Should().NotBe(IntPtr.Zero);

        // Act
        var rewrapped = UnmanagedLibrary.GetDelegateForFunctionPointer<GetCurrentProcessIdDelegate>(
            ptr,
            CallingConvention.Cdecl);

        // Assert
        rewrapped.Should().NotBeNull();
    }
}
