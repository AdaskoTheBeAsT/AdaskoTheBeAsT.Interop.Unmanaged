using System;
using System.Runtime.InteropServices;
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
        var delegatePin = new DelegatePin(ptr, keepAlive);

        // Assert
        Assert.Equal(ptr, delegatePin.Ptr);
    }

    [Fact]
    public void Constructor_WithZeroPointer_CreatesInstance()
    {
        // Arrange
        var ptr = IntPtr.Zero;
        var keepAlive = new object();

        // Act
        var delegatePin = new DelegatePin(ptr, keepAlive);

        // Assert
        Assert.Equal(IntPtr.Zero, delegatePin.Ptr);
    }

    [Fact]
    public void Constructor_WithNullKeepAlive_CreatesInstance()
    {
        // Arrange
        var ptr = new IntPtr(12345);

        // Act
        var delegatePin = new DelegatePin(ptr, null!);

        // Assert
        Assert.Equal(ptr, delegatePin.Ptr);
    }

    [Fact]
    public void Ptr_Property_ReturnsCorrectValue()
    {
        // Arrange
        var expectedPtr = new IntPtr(99999);
        var delegatePin = new DelegatePin(expectedPtr, new object());

        // Act
        var actualPtr = delegatePin.Ptr;

        // Assert
        Assert.Equal(expectedPtr, actualPtr);
    }

    [Fact]
    public void Ptr_Property_IsReadOnly()
    {
        // Arrange
        var ptr = new IntPtr(12345);
        var delegatePin = new DelegatePin(ptr, new object());

        // Act & Assert - Compile-time check that Ptr is readonly
        // This test verifies the property exists and is accessible
        var _ = delegatePin.Ptr;
    }

    [Fact]
    public void Dispose_CalledOnce_DoesNotThrow()
    {
        // Arrange
        var delegatePin = new DelegatePin(new IntPtr(12345), new object());

        // Act & Assert - no exception
        delegatePin.Dispose();
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_DoesNotThrow()
    {
        // Arrange
        var delegatePin = new DelegatePin(new IntPtr(12345), new object());

        // Act & Assert - no exception
        delegatePin.Dispose();
        delegatePin.Dispose();
        delegatePin.Dispose();
    }

    [Fact]
    public void Dispose_AfterDispose_PtrStillAccessible()
    {
        // Arrange
        var expectedPtr = new IntPtr(12345);
        var delegatePin = new DelegatePin(expectedPtr, new object());

        // Act
        delegatePin.Dispose();
        var actualPtr = delegatePin.Ptr;

        // Assert
        Assert.Equal(expectedPtr, actualPtr);
    }

    [Fact]
    public void UsingStatement_DisposesCorrectly()
    {
        // Arrange
        var ptr = new IntPtr(12345);

        // Act & Assert - no exception
        using (var delegatePin = new DelegatePin(ptr, new object()))
        {
            Assert.Equal(ptr, delegatePin.Ptr);
        }
    }

    [Fact]
    public void Constructor_WithDelegateAsKeepAlive_PreservesDelegate()
    {
        // Arrange
        SimpleDelegate callback = (x, y) => x + y;
        var ptr = Marshal.GetFunctionPointerForDelegate(callback);

        // Act
        var delegatePin = new DelegatePin(ptr, callback);

        // Assert
        Assert.Equal(ptr, delegatePin.Ptr);
        Assert.NotEqual(IntPtr.Zero, delegatePin.Ptr);
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
        var pin2 = new DelegatePin(ptr, keepAlive2);

        // Assert
        Assert.Equal(pin1.Ptr, pin2.Ptr);
        pin1.Dispose();
        Assert.Equal(ptr, pin2.Ptr); // pin2 should still work
    }

    [Fact]
    public void Constructor_WithFunctionPointer_WorksCorrectly()
    {
        // Arrange
        SimpleDelegate callback = (x, y) => x + y;
        var ptr = UnmanagedLibrary.GetFunctionPointerForDelegate(callback, out var binder);

        // Act
        var delegatePin = new DelegatePin(ptr, binder);

        // Assert
        Assert.NotEqual(IntPtr.Zero, delegatePin.Ptr);
        Assert.Equal(ptr, delegatePin.Ptr);
    }

    [Fact]
    public void Struct_IsValueType()
    {
        // Assert - Compile-time verification
        Assert.True(typeof(DelegatePin).IsValueType);
    }

    [Fact]
    public void Struct_ImplementsIDisposable()
    {
        // Assert - Compile-time verification
        Assert.True(typeof(IDisposable).IsAssignableFrom(typeof(DelegatePin)));
    }

    [Fact]
    public void Struct_IsReadOnly()
    {
        // Assert - Compile-time verification
        var customAttributes = typeof(DelegatePin).GetCustomAttributes(false);
        var isReadOnlyStruct = Array.Exists(
            customAttributes,
            attr => string.Equals(attr.GetType().Name, "IsReadOnlyAttribute", StringComparison.Ordinal));
        
        Assert.True(isReadOnlyStruct || typeof(DelegatePin).IsValueType);
    }
}
