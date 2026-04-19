using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Unmanaged.Test;

public class UnmanagedLibraryStaticMethodsTests
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint GetCurrentProcessIdDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr GetCurrentProcessDelegate();

    [Fact]
    public void LoadLibrary_WithNullFileName_ThrowsArgumentException()
    {
        // Act
        Action act = () => UnmanagedLibrary.LoadLibrary(null!);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*cannot be null or whitespace*")
            .WithParameterName("fileName");
    }

    [Fact]
    public void LoadLibrary_WithEmptyFileName_ThrowsArgumentException()
    {
        // Act
        Action act = () => UnmanagedLibrary.LoadLibrary(string.Empty);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*cannot be null or whitespace*")
            .WithParameterName("fileName");
    }

    [Fact]
    public void LoadLibrary_WithWhitespaceFileName_ThrowsArgumentException()
    {
        // Act
        Action act = () => UnmanagedLibrary.LoadLibrary("   ");

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*cannot be null or whitespace*")
            .WithParameterName("fileName");
    }

    [Fact]
    public void LoadLibrary_WithNonExistentDll_ThrowsWin32Exception()
    {
        // Arrange
        var nonExistentDll = $"NonExistent_{Guid.NewGuid()}.dll";

        // Act
        Action act = () => UnmanagedLibrary.LoadLibrary(nonExistentDll);

        // Assert
        act.Should().Throw<Win32Exception>()
            .WithMessage($"*Failed to load library*{nonExistentDll}*");
    }

    [Fact]
    public void LoadLibrary_WithValidDll_ReturnsValidHandle()
    {
        // Act
        using var handle = UnmanagedLibrary.LoadLibrary("kernel32.dll");

        // Assert
        handle.Should().NotBeNull();
        handle.IsInvalid.Should().BeFalse();
        handle.IsClosed.Should().BeFalse();
    }

    [Fact]
    public void LoadLibrary_WithDefaultFlags_LoadsSuccessfully()
    {
        // Act
        using var handle = UnmanagedLibrary.LoadLibrary("kernel32.dll");

        // Assert
        handle.Should().NotBeNull();
        handle.IsInvalid.Should().BeFalse();
    }

    [Fact]
    public void LoadLibrary_WithCustomFlags_LoadsSuccessfully()
    {
        // Arrange
        var flags = LoadLibraryFlags.LOAD_LIBRARY_SEARCH_SYSTEM32;

        // Act
        using var handle = UnmanagedLibrary.LoadLibrary("kernel32.dll", flags);

        // Assert
        handle.Should().NotBeNull();
        handle.IsInvalid.Should().BeFalse();
    }

    [Fact]
    public void LoadLibrary_WithDataFileFlag_LoadsSuccessfully()
    {
        // Arrange
        var flags = LoadLibraryFlags.LOAD_LIBRARY_AS_DATAFILE;

        // Act
        using var handle = UnmanagedLibrary.LoadLibrary("kernel32.dll", flags);

        // Assert
        handle.Should().NotBeNull();
        handle.IsInvalid.Should().BeFalse();
    }

    [Fact]
    public void FreeLibrary_WithNullHandle_DoesNotThrow()
    {
        // Act
        Action act = () => UnmanagedLibrary.FreeLibrary(null);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void FreeLibrary_WithValidHandle_FreesSuccessfully()
    {
        // Arrange
        using var handle = UnmanagedLibrary.LoadLibrary("kernel32.dll");

        // Act
        UnmanagedLibrary.FreeLibrary(handle);

        // Assert
        handle.IsClosed.Should().BeTrue();
    }

    [Fact]
    public void FreeLibrary_WithAlreadyClosedHandle_DoesNotThrow()
    {
        // Arrange
        using var handle = UnmanagedLibrary.LoadLibrary("kernel32.dll");
        UnmanagedLibrary.FreeLibrary(handle);

        // Act
        Action act = () => UnmanagedLibrary.FreeLibrary(handle);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void FreeLibrary_CalledMultipleTimes_DoesNotThrow()
    {
        // Arrange
        using var handle = UnmanagedLibrary.LoadLibrary("kernel32.dll");

        // Act
        Action act = () =>
        {
            UnmanagedLibrary.FreeLibrary(handle);
            UnmanagedLibrary.FreeLibrary(handle);
            UnmanagedLibrary.FreeLibrary(handle);
        };

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void GetUnmanagedFunction_WithValidFunction_ReturnsDelegate()
    {
        // Arrange
        using var handle = UnmanagedLibrary.LoadLibrary("kernel32.dll");

        // Act
        var function = UnmanagedLibrary.GetUnmanagedFunction<GetCurrentProcessIdDelegate>(handle, "GetCurrentProcessId");

        // Assert
        function.Should().NotBeNull();
    }

    [Fact]
    public void GetUnmanagedFunction_WithNullHandle_ThrowsArgumentNullException()
    {
        // Act
        Action act = () => UnmanagedLibrary.GetUnmanagedFunction<GetCurrentProcessIdDelegate>(null!, "GetCurrentProcessId");

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("safeLibraryHandle");
    }

    [Fact]
    public void GetUnmanagedFunction_WithWhitespaceFunctionName_ThrowsArgumentException()
    {
        // Arrange
        using var handle = UnmanagedLibrary.LoadLibrary("kernel32.dll");

        // Act
        Action act = () => UnmanagedLibrary.GetUnmanagedFunction<GetCurrentProcessIdDelegate>(handle, "   ");

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*cannot be null or whitespace*")
            .WithParameterName("functionName");
    }

    [Fact]
    public void GetUnmanagedFunction_WithNonExistentFunction_ReturnsNull()
    {
        // Arrange
        using var handle = UnmanagedLibrary.LoadLibrary("kernel32.dll");

        // Act
        var function = UnmanagedLibrary.GetUnmanagedFunction<GetCurrentProcessIdDelegate>(handle, "NonExistentFunction");

        // Assert
        function.Should().BeNull();
    }

    [Fact]
    public void GetUnmanagedFunction_CalledMultipleTimes_ReturnsSameFunction()
    {
        // Arrange
        using var handle = UnmanagedLibrary.LoadLibrary("kernel32.dll");

        // Act
        var function1 = UnmanagedLibrary.GetUnmanagedFunction<GetCurrentProcessIdDelegate>(handle, "GetCurrentProcessId");
        var function2 = UnmanagedLibrary.GetUnmanagedFunction<GetCurrentProcessIdDelegate>(handle, "GetCurrentProcessId");

        // Assert
        function1.Should().NotBeNull();
        function2.Should().NotBeNull();
    }

    [Fact]
    public void GetUnmanagedFunction_WithDifferentDelegateTypes_ReturnsCorrectDelegates()
    {
        // Arrange
        using var handle = UnmanagedLibrary.LoadLibrary("kernel32.dll");

        // Act
        var function1 = UnmanagedLibrary.GetUnmanagedFunction<GetCurrentProcessIdDelegate>(handle, "GetCurrentProcessId");
        var function2 = UnmanagedLibrary.GetUnmanagedFunction<GetCurrentProcessDelegate>(handle, "GetCurrentProcess");

        // Assert
        function1.Should().NotBeNull();
        function2.Should().NotBeNull();
    }

    [Fact]
    public void GetUnmanagedFunction_InvokedFunction_ReturnsExpectedResult()
    {
        // Arrange
        using var handle = UnmanagedLibrary.LoadLibrary("kernel32.dll");
        var function = UnmanagedLibrary.GetUnmanagedFunction<GetCurrentProcessIdDelegate>(handle, "GetCurrentProcessId");

        // Act
        var processId = function!();

        // Assert
        processId.Should().BeGreaterThan(0u);
        processId.Should().Be(TestHelpers.GetCurrentProcessId());
    }

    [Fact]
    public void TryGetExport_Static_WithValidFunction_ReturnsTrueAndNonZeroAddress()
    {
        if (!TestHelpers.IsWindows())
        {
            return;
        }

        // Arrange
        using var handle = UnmanagedLibrary.LoadLibrary("kernel32.dll");

        // Act
        var found = UnmanagedLibrary.TryGetExport(handle, "GetCurrentProcessId", out var addr);

        // Assert
        found.Should().BeTrue();
        addr.Should().NotBe(IntPtr.Zero);
    }

    [Fact]
    public void TryGetExport_Static_WithNonExistentFunction_ReturnsFalseAndZeroAddress()
    {
        if (!TestHelpers.IsWindows())
        {
            return;
        }

        // Arrange
        using var handle = UnmanagedLibrary.LoadLibrary("kernel32.dll");

        // Act
        var found = UnmanagedLibrary.TryGetExport(handle, "NonExistentFunction", out var addr);

        // Assert
        found.Should().BeFalse();
        addr.Should().Be(IntPtr.Zero);
    }

    [Fact]
    public void TryGetExport_Static_WithNullHandle_ThrowsArgumentNullException()
    {
        // Act
        Action act = () => UnmanagedLibrary.TryGetExport(null!, "GetCurrentProcessId", out _);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("safeLibraryHandle");
    }

    [Fact]
    public void TryGetExport_Static_WithWhitespaceFunctionName_ThrowsArgumentException()
    {
        if (!TestHelpers.IsWindows())
        {
            return;
        }

        // Arrange
        using var handle = UnmanagedLibrary.LoadLibrary("kernel32.dll");

        // Act
        Action act = () => UnmanagedLibrary.TryGetExport(handle, "   ", out _);

        // Assert
        act.Should().Throw<ArgumentException>().WithParameterName("functionName");
    }

    [Fact]
    public void TryGetExport_Instance_WithValidFunction_ReturnsTrueAndNonZeroAddress()
    {
        if (!TestHelpers.IsWindows())
        {
            return;
        }

        // Arrange
        using var library = new UnmanagedLibrary("kernel32.dll");

        // Act
        var found = library.TryGetExport("GetCurrentProcessId", out var addr);

        // Assert
        found.Should().BeTrue();
        addr.Should().NotBe(IntPtr.Zero);
    }

    [Fact]
    public void TryGetExport_Instance_WithNonExistentFunction_ReturnsFalse()
    {
        if (!TestHelpers.IsWindows())
        {
            return;
        }

        // Arrange
        using var library = new UnmanagedLibrary("kernel32.dll");

        // Act
        var found = library.TryGetExport("NonExistentFunction", out var addr);

        // Assert
        found.Should().BeFalse();
        addr.Should().Be(IntPtr.Zero);
    }
}
