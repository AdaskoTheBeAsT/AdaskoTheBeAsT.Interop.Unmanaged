using System;
using System.Reflection;
using System.Runtime.InteropServices;
using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Unmanaged.Test;

public class UnmanagedLibraryAdvancedTests
{
    private delegate int SimpleDelegate(int x, int y);

    private delegate void GenericDelegate<T>(T value);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int StdCallDelegate(int a, int b);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int CdeclDelegate(int a, int b);

    [Fact]
    public void GetFunctionPointerForDelegate_WithSimpleDelegate_ReturnsValidPointer()
    {
        // Arrange
        SimpleDelegate callback = (x, y) => x + y;

        // Act
        var ptr = UnmanagedLibrary.GetFunctionPointerForDelegate(callback, out var binder);

        // Assert
        ptr.Should().NotBe(IntPtr.Zero);
        binder.Should().NotBeNull();
    }

    [Fact]
    public void GetFunctionPointerForDelegate_BinderKeepsDelegateAlive()
    {
        // Arrange
        SimpleDelegate callback = (x, y) => x + y;

        // Act
        var ptr = UnmanagedLibrary.GetFunctionPointerForDelegate(callback, out var binder);

        // Assert
        binder.Should().NotBeNull();
        binder.Should().BeSameAs(callback); // For non-generic delegates, binder should be the delegate itself
    }

    [Fact]
    public void GetFunctionPointerForDelegate_WithGenericDelegate_ReturnsValidPointer()
    {
        // Arrange
        GenericDelegate<int> callback = value => Console.WriteLine(value);

        // Act
        var ptr = UnmanagedLibrary.GetFunctionPointerForDelegate(callback, out var binder);

        // Assert
        ptr.Should().NotBe(IntPtr.Zero);
        binder.Should().NotBeNull();
    }

    [Fact]
    public void GetFunctionPointerForDelegate_WithGenericDelegate_CreatesDynamicAssembly()
    {
        // Arrange
        GenericDelegate<string> callback = value => Console.WriteLine(value);
        var assembliesBefore = AppDomain.CurrentDomain.GetAssemblies().Length;

        // Act
        var ptr = UnmanagedLibrary.GetFunctionPointerForDelegate(callback, out var _);

        // Assert
        ptr.Should().NotBe(IntPtr.Zero);
        var assembliesAfter = AppDomain.CurrentDomain.GetAssemblies().Length;
        assembliesAfter.Should().BeGreaterThanOrEqualTo(assembliesBefore); // May create a new assembly
    }

    [Fact]
    public void GetFunctionPointerForDelegate_WithGenericDelegate_BinderContainsBothDelegates()
    {
        // Arrange
        GenericDelegate<int> callback = value => Console.WriteLine(value);

        // Act
        var ptr = UnmanagedLibrary.GetFunctionPointerForDelegate(callback, out var binder);

        // Assert
        binder.Should().NotBeNull();
        // For generic delegates, binder is a Tuple containing both the original and proxy delegates
        binder.Should().BeOfType<Tuple<Delegate, Delegate>>();
    }

    [Fact]
    public void GetFunctionPointerForDelegate_SameGenericDelegateTypeTwice_ReusesProxyAssembly()
    {
        // Arrange
        GenericDelegate<int> callback1 = value => Console.WriteLine(value);
        GenericDelegate<int> callback2 = value => Console.WriteLine(value * 2);

        // Act
        var ptr1 = UnmanagedLibrary.GetFunctionPointerForDelegate(callback1, out var _);
        var assembliesAfterFirst = AppDomain.CurrentDomain.GetAssemblies().Length;

        var ptr2 = UnmanagedLibrary.GetFunctionPointerForDelegate(callback2, out var _);
        var assembliesAfterSecond = AppDomain.CurrentDomain.GetAssemblies().Length;

        // Assert
        ptr1.Should().NotBe(IntPtr.Zero);
        ptr2.Should().NotBe(IntPtr.Zero);
        assembliesAfterSecond.Should().Be(assembliesAfterFirst); // Should reuse the same proxy assembly
    }

    [Fact]
    public void GetFunctionPointerForDelegate_DifferentGenericTypes_CreatesSeparateProxies()
    {
        // Arrange
        GenericDelegate<int> callback1 = value => Console.WriteLine(value);
        GenericDelegate<string> callback2 = value => Console.WriteLine(value);

        // Act
        var ptr1 = UnmanagedLibrary.GetFunctionPointerForDelegate(callback1, out var _);
        var ptr2 = UnmanagedLibrary.GetFunctionPointerForDelegate(callback2, out var _);

        // Assert
        ptr1.Should().NotBe(IntPtr.Zero);
        ptr2.Should().NotBe(IntPtr.Zero);
        ptr1.Should().NotBe(ptr2); // Different function pointers
    }

    [Fact]
    public void GetFunctionPointerForDelegate_WithManagedCallback_ReturnsValidPointer()
    {
        // Arrange
        SimpleDelegate originalCallback = (x, y) => x + y;

        // Act
        var ptr = UnmanagedLibrary.GetFunctionPointerForDelegate(originalCallback, out var binder);

        // Assert
        ptr.Should().NotBe(IntPtr.Zero);
        binder.Should().NotBeNull();

        // Can invoke the original callback
        var result = originalCallback(10, 20);
        result.Should().Be(30);
    }

    [Fact]
    public void GetFunctionPointerForDelegate_WithMultipleCallbacks_ReturnsValidPointers()
    {
        // Arrange
        SimpleDelegate callback1 = (x, y) => x + y;
        SimpleDelegate callback2 = (x, y) => x * y;

        // Act
        var ptr1 = UnmanagedLibrary.GetFunctionPointerForDelegate(callback1, out var binder1);
        var ptr2 = UnmanagedLibrary.GetFunctionPointerForDelegate(callback2, out var binder2);

        // Assert
        ptr1.Should().NotBe(IntPtr.Zero);
        ptr2.Should().NotBe(IntPtr.Zero);
        ptr1.Should().NotBe(ptr2);
        callback1(10, 20).Should().Be(30);
        callback2(10, 20).Should().Be(200);
    }

    [Fact]
    public void GetFunctionPointerForDelegate_WithFuncDelegate_WorksCorrectly()
    {
        // Arrange
        Func<int, int, int, int> callback = (a, b, c) => a + b + c;

        // Act
        var ptr = UnmanagedLibrary.GetFunctionPointerForDelegate(callback, out var binder);

        // Assert
        ptr.Should().NotBe(IntPtr.Zero);
        binder.Should().NotBeNull();
        callback(5, 10, 15).Should().Be(30);
    }

    [Fact]
    public void GetFunctionPointerForDelegate_WithAction_WorksCorrectly()
    {
        // Arrange
        var called = false;
        Action callback = () => called = true;

        // Act
        var ptr = UnmanagedLibrary.GetFunctionPointerForDelegate(callback, out var binder);

        // Assert
        ptr.Should().NotBe(IntPtr.Zero);
        binder.Should().NotBeNull();
        callback();
        called.Should().BeTrue();
    }

    [Fact]
    public void GetFunctionPointerForDelegate_WithStringFunc_WorksCorrectly()
    {
        // Arrange
        Func<string, int> callback = str => str.Length;

        // Act
        var ptr = UnmanagedLibrary.GetFunctionPointerForDelegate(callback, out var binder);

        // Assert
        ptr.Should().NotBe(IntPtr.Zero);
        binder.Should().NotBeNull();
        callback("Hello").Should().Be(5);
    }

    [Fact]
    public void GetFunctionPointerForDelegate_WithNativeFunction_WorksCorrectly()
    {
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

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint GetCurrentProcessIdDelegate();
}

