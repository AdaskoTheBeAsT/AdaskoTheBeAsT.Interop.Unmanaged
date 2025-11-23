using System;
using System.Runtime.InteropServices;
using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Unmanaged.Test;

public class UnmanagedLibraryInstanceMethodsTests
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint GetCurrentProcessIdDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr GetCurrentProcessDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint GetLastErrorDelegate();

    [Fact]
    public void GetUnmanagedFunction_WithValidFunction_ReturnsDelegate()
    {
        // Arrange
        using var library = new UnmanagedLibrary("kernel32.dll");

        // Act
        var function = library.GetUnmanagedFunction<GetCurrentProcessIdDelegate>("GetCurrentProcessId");

        // Assert
        function.Should().NotBeNull();
    }

    [Fact]
    public void GetUnmanagedFunction_WithNonExistentFunction_ReturnsNull()
    {
        // Arrange
        using var library = new UnmanagedLibrary("kernel32.dll");

        // Act
        var function = library.GetUnmanagedFunction<GetCurrentProcessIdDelegate>("NonExistentFunction");

        // Assert
        function.Should().BeNull();
    }

    [Fact]
    public void GetUnmanagedFunction_CalledMultipleTimes_ReturnsDelegates()
    {
        // Arrange
        using var library = new UnmanagedLibrary("kernel32.dll");

        // Act
        var function1 = library.GetUnmanagedFunction<GetCurrentProcessIdDelegate>("GetCurrentProcessId");
        var function2 = library.GetUnmanagedFunction<GetCurrentProcessIdDelegate>("GetCurrentProcessId");

        // Assert
        function1.Should().NotBeNull();
        function2.Should().NotBeNull();
    }

    [Fact]
    public void GetUnmanagedFunction_WithDifferentFunctions_ReturnsCorrectDelegates()
    {
        // Arrange
        using var library = new UnmanagedLibrary("kernel32.dll");

        // Act
        var function1 = library.GetUnmanagedFunction<GetCurrentProcessIdDelegate>("GetCurrentProcessId");
        var function2 = library.GetUnmanagedFunction<GetCurrentProcessDelegate>("GetCurrentProcess");
        var function3 = library.GetUnmanagedFunction<GetLastErrorDelegate>("GetLastError");

        // Assert
        function1.Should().NotBeNull();
        function2.Should().NotBeNull();
        function3.Should().NotBeNull();
    }

    [Fact]
    public void GetUnmanagedFunction_InvokedFunction_ReturnsExpectedResult()
    {
        // Arrange
        using var library = new UnmanagedLibrary("kernel32.dll");
        var function = library.GetUnmanagedFunction<GetCurrentProcessIdDelegate>("GetCurrentProcessId");

        // Act
        var processId = function!();

        // Assert
        processId.Should().BeGreaterThan(0u);
        processId.Should().Be(TestHelpers.GetCurrentProcessId());
    }

    [Fact]
    public void GetUnmanagedFunction_MultipleFunctionsInvoked_AllWorkCorrectly()
    {
        // Arrange
        using var library = new UnmanagedLibrary("kernel32.dll");
        var getCurrentProcessId = library.GetUnmanagedFunction<GetCurrentProcessIdDelegate>("GetCurrentProcessId");
        var getCurrentProcess = library.GetUnmanagedFunction<GetCurrentProcessDelegate>("GetCurrentProcess");

        // Act
        var processId = getCurrentProcessId!();
        var processHandle = getCurrentProcess!();

        // Assert
        processId.Should().BeGreaterThan(0u);
        processHandle.Should().NotBe(IntPtr.Zero);
    }

    [Fact]
    public void GetUnmanagedFunction_AfterDispose_FunctionStillWorks()
    {
        // Arrange
        GetCurrentProcessIdDelegate? function;
        using (var library = new UnmanagedLibrary("kernel32.dll"))
        {
            function = library.GetUnmanagedFunction<GetCurrentProcessIdDelegate>("GetCurrentProcessId");
        }

        // Act - Function pointer should still be valid even after library is disposed
        // Note: This is dangerous in real code, but tests the behavior
        var processId = function!();

        // Assert
        processId.Should().BeGreaterThan(0u);
    }

    [Fact]
    public void GetUnmanagedFunction_WithCaseSensitiveName_ReturnsCorrectFunction()
    {
        // Arrange
        using var library = new UnmanagedLibrary("kernel32.dll");

        // Act
        var correctCase = library.GetUnmanagedFunction<GetCurrentProcessIdDelegate>("GetCurrentProcessId");
        var wrongCase = library.GetUnmanagedFunction<GetCurrentProcessIdDelegate>("getcurrentprocessid");

        // Assert
        correctCase.Should().NotBeNull();
        wrongCase.Should().BeNull(); // Function names are case-sensitive
    }

    [Fact]
    public void GetUnmanagedFunction_LoadedWithDataFileFlag_LoadsAsDataFile()
    {
        // Arrange & Act
        using var library = new UnmanagedLibrary("kernel32.dll", LoadLibraryFlags.LOAD_LIBRARY_AS_DATAFILE);

        // Assert - Library loaded successfully even with DATAFILE flag
        library.Should().NotBeNull();

        // Note: GetProcAddress behavior with LOAD_LIBRARY_AS_DATAFILE varies by Windows version
        // On some versions it may still work, on others it returns null
        var function = library.GetUnmanagedFunction<GetCurrentProcessIdDelegate>("GetCurrentProcessId");
        // We just verify no exception is thrown
    }
}
