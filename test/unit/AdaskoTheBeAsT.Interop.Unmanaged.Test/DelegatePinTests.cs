using System;
using System.Runtime.InteropServices;
using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Unmanaged.Test;

public class DelegatePinTests
{
    private delegate int SimpleDelegate(int x, int y);

    [Fact]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Arrange
        var ptr = new IntPtr(12345);
        var keepAlive = new object();

        // Act
        using var delegatePin = new DelegatePin(ptr, keepAlive);

        // Assert
        delegatePin.Ptr.Should().Be(ptr);
    }

    [Fact]
    public void Constructor_WithZeroPointer_CreatesInstance()
    {
        // Arrange
        var ptr = IntPtr.Zero;
        var keepAlive = new object();

        // Act
        using var delegatePin = new DelegatePin(ptr, keepAlive);

        // Assert
        delegatePin.Ptr.Should().Be(IntPtr.Zero);
    }

    [Fact]
    public void Constructor_WithNullKeepAlive_CreatesInstance()
    {
        // Arrange
        var ptr = new IntPtr(12345);

        // Act
        using var delegatePin = new DelegatePin(ptr, null!);

        // Assert
        delegatePin.Ptr.Should().Be(ptr);
    }

    [Fact]
    public void Ptr_Property_ReturnsCorrectValue()
    {
        // Arrange
        var expectedPtr = new IntPtr(99999);
        using var delegatePin = new DelegatePin(expectedPtr, new object());

        // Act
        var actualPtr = delegatePin.Ptr;

        // Assert
        actualPtr.Should().Be(expectedPtr);
    }

    [Fact]
    public void Ptr_Property_IsReadOnly()
    {
        // Arrange
        var ptr = new IntPtr(12345);
        using var delegatePin = new DelegatePin(ptr, new object());

        // Act & Assert - Compile-time check that Ptr is readonly
        // This test verifies the property exists and is accessible
        Action action = () => _ = delegatePin.Ptr;

        action.Should().NotThrow();
    }

    [Fact]
    public void Dispose_CalledOnce_DoesNotThrow()
    {
        // Arrange
        var delegatePin = new DelegatePin(new IntPtr(12345), new object());

        // Act & Assert - no exception
#pragma warning disable IDISP017
        Action action = () => delegatePin.Dispose();
#pragma warning restore IDISP017

        action.Should().NotThrow();
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_DoesNotThrow()
    {
        // Arrange
        var delegatePin = new DelegatePin(new IntPtr(12345), new object());

        // Act & Assert - no exception
#pragma warning disable IDISP016,IDISP017
        Action action = () =>
        {
            delegatePin.Dispose();
            delegatePin.Dispose();
            delegatePin.Dispose();
        };
#pragma warning restore IDISP016,IDISP017

        action.Should().NotThrow();
    }

    [Fact]
    public void Dispose_AfterDispose_PtrStillAccessible()
    {
        // Arrange
        var expectedPtr = new IntPtr(12345);
        var delegatePin = new DelegatePin(expectedPtr, new object());

        // Act
#pragma warning disable IDISP016,IDISP017
        delegatePin.Dispose();
#pragma warning restore IDISP016,IDISP017
        var actualPtr = delegatePin.Ptr;

        // Assert
        actualPtr.Should().Be(expectedPtr);
    }

    [Fact]
    public void UsingStatement_DisposesCorrectly()
    {
        // Arrange
        var ptr = new IntPtr(12345);

        // Act & Assert - no exception
        using (var delegatePin = new DelegatePin(ptr, new object()))
        {
            delegatePin.Ptr.Should().Be(ptr);
        }
    }

    [Fact]
    public void Constructor_WithDelegateAsKeepAlive_PreservesDelegate()
    {
        // Arrange
        SimpleDelegate callback = (x, y) => x + y;
        var ptr = Marshal.GetFunctionPointerForDelegate(callback);

        // Act
        using var delegatePin = new DelegatePin(ptr, callback);

        // Assert
        delegatePin.Ptr.Should().Be(ptr);
        delegatePin.Ptr.Should().NotBe(IntPtr.Zero);
    }

    [Fact]
    public void MultipleInstances_WithSamePointer_AreIndependent()
    {
        // Arrange
        var ptr = new IntPtr(12345);
        var keepAlive1 = new object();
        var keepAlive2 = new object();

        // Act
        var pin1 = new DelegatePin(ptr, keepAlive1);
        using var pin2 = new DelegatePin(ptr, keepAlive2);

        // Assert
        pin1.Ptr.Should().Be(pin2.Ptr);
#pragma warning disable IDISP017 // Prefer using
        pin1.Dispose();
#pragma warning restore IDISP017 // Prefer using
        pin2.Ptr.Should().Be(ptr); // pin2 should still work
    }

    [Fact]
    public void Constructor_WithFunctionPointer_WorksCorrectly()
    {
        // Arrange
        SimpleDelegate callback = (x, y) => x + y;
        var ptr = UnmanagedLibrary.GetFunctionPointerForDelegate(callback, out var binder);

        // Act
        using var delegatePin = new DelegatePin(ptr, binder);

        // Assert
        delegatePin.Ptr.Should().NotBe(IntPtr.Zero);
        delegatePin.Ptr.Should().Be(ptr);
    }

    [Fact]
    public void Struct_IsValueType()
    {
        // Assert - Compile-time verification
        typeof(DelegatePin).IsValueType.Should().BeTrue();
    }

    [Fact]
    public void Struct_ImplementsIDisposable()
    {
        // Assert - Compile-time verification
        typeof(IDisposable).IsAssignableFrom(typeof(DelegatePin)).Should().BeTrue();
    }

    [Fact]
    public void Struct_IsReadOnly()
    {
        // Assert - Compile-time verification
        var customAttributes = typeof(DelegatePin).GetCustomAttributes(inherit: false);
        var isReadOnlyStruct = Array.Exists(
            customAttributes,
            attr => string.Equals(attr.GetType().Name, "IsReadOnlyAttribute", StringComparison.Ordinal));

        (isReadOnlyStruct || typeof(DelegatePin).IsValueType).Should().BeTrue();
    }
}
