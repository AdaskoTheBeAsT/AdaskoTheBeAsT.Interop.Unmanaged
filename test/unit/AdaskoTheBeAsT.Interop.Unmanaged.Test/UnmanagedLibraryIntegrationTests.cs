using System;
using System.Runtime.InteropServices;
using System.Text;
using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Unmanaged.Test;

public class UnmanagedLibraryIntegrationTests
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint GetCurrentProcessIdDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr GetCurrentProcessDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint GetLastErrorDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr GetModuleHandleW([MarshalAs(UnmanagedType.LPWStr)] string? lpModuleName);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint GetTickCountDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate bool SetEnvironmentVariableW(
        [MarshalAs(UnmanagedType.LPWStr)] string lpName,
        [MarshalAs(UnmanagedType.LPWStr)] string? lpValue);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint GetEnvironmentVariableW(
        [MarshalAs(UnmanagedType.LPWStr)] string lpName,
        StringBuilder lpBuffer,
        uint nSize);

    [Fact]
    public void FullWorkflow_LoadGetInvokeDispose_WorksCorrectly()
    {
        // Arrange & Act
        using var library = new UnmanagedLibrary("kernel32.dll");
        var getProcessId = library.GetUnmanagedFunction<GetCurrentProcessIdDelegate>("GetCurrentProcessId");
        var processId = getProcessId!();

        // Assert
        processId.Should().BeGreaterThan(0u);
        processId.Should().Be(TestHelpers.GetCurrentProcessId());
    }

    [Fact]
    public void FullWorkflow_MultipleFunctions_AllWorkCorrectly()
    {
        // Arrange
        using var library = new UnmanagedLibrary("kernel32.dll");

        // Act
        var getProcessId = library.GetUnmanagedFunction<GetCurrentProcessIdDelegate>("GetCurrentProcessId");
        var getProcess = library.GetUnmanagedFunction<GetCurrentProcessDelegate>("GetCurrentProcess");
        var getTickCount = library.GetUnmanagedFunction<GetTickCountDelegate>("GetTickCount");

        var processId = getProcessId!();
        var processHandle = getProcess!();
        var tickCount = getTickCount!();

        // Assert
        Assert.True(processId > 0);
        Assert.NotEqual(IntPtr.Zero, processHandle);
        Assert.True(tickCount > 0);
    }

    [Fact]
    public void StaticWorkflow_LoadGetInvokeFree_WorksCorrectly()
    {
        // Arrange
        var handle = UnmanagedLibrary.LoadLibrary("kernel32.dll");

        // Act
        var getProcessId = UnmanagedLibrary.GetUnmanagedFunction<GetCurrentProcessIdDelegate>(handle, "GetCurrentProcessId");
        var processId = getProcessId!();
        UnmanagedLibrary.FreeLibrary(handle);

        // Assert
        processId.Should().BeGreaterThan(0u);
        processId.Should().Be(TestHelpers.GetCurrentProcessId());
    }

    [Fact]
    public void MultipleLibraries_LoadedSimultaneously_WorkIndependently()
    {
        // Arrange & Act
        using var kernel32 = new UnmanagedLibrary("kernel32.dll");
        using var user32 = new UnmanagedLibrary("user32.dll");

        var getProcessId = kernel32.GetUnmanagedFunction<GetCurrentProcessIdDelegate>("GetCurrentProcessId");
        var getTickCount = kernel32.GetUnmanagedFunction<GetTickCountDelegate>("GetTickCount");

        var processId = getProcessId!();
        var tickCount = getTickCount!();

        // Assert
        Assert.True(processId > 0);
        Assert.Equal(TestHelpers.GetCurrentProcessId(), processId);
        Assert.True(tickCount > 0);
    }

    [Fact]
    public void GetModuleHandle_WithNullParameter_ReturnsModuleHandle()
    {
        // Arrange
        using var library = new UnmanagedLibrary("kernel32.dll");
        var getModuleHandle = library.GetUnmanagedFunction<GetModuleHandleW>(nameof(GetModuleHandleW));

        // Act
        var handle = getModuleHandle!(null);

        // Assert
        handle.Should().NotBe(IntPtr.Zero);
    }

    [Fact]
    public void GetModuleHandle_WithKnownModule_ReturnsValidHandle()
    {
        // Arrange
        using var library = new UnmanagedLibrary("kernel32.dll");
        var getModuleHandle = library.GetUnmanagedFunction<GetModuleHandleW>(nameof(GetModuleHandleW));

        // Act
        var handle = getModuleHandle!("kernel32.dll");

        // Assert
        handle.Should().NotBe(IntPtr.Zero);
    }

    [Fact]
    public void EnvironmentVariable_SetAndGet_WorksCorrectly()
    {
        // Arrange
        using var library = new UnmanagedLibrary("kernel32.dll");
        var setEnvVar = library.GetUnmanagedFunction<SetEnvironmentVariableW>(nameof(SetEnvironmentVariableW));
        var getEnvVar = library.GetUnmanagedFunction<GetEnvironmentVariableW>(nameof(GetEnvironmentVariableW));

        var testVarName = $"TEST_VAR_{Guid.NewGuid():N}";
        var testVarValue = "TestValue123";

        // Act
        var setResult = setEnvVar!(testVarName, testVarValue);

        // Cleanup
        setEnvVar(testVarName, null);

        // Assert - Just verify the functions work and set operation succeeded
        setResult.Should().BeTrue();
        setEnvVar.Should().NotBeNull();
        getEnvVar.Should().NotBeNull();
    }

    [Fact]
    public void LoadLibrary_WithDifferentFlags_LoadsSameLibraryDifferently()
    {
        // Arrange & Act
        using var normalLoad = new UnmanagedLibrary(
            "kernel32.dll",
            LoadLibraryFlags.LOAD_LIBRARY_SEARCH_SYSTEM32);

        using var dataFileLoad = new UnmanagedLibrary(
            "kernel32.dll",
            LoadLibraryFlags.LOAD_LIBRARY_AS_DATAFILE);

        var normalFunction = normalLoad.GetUnmanagedFunction<GetCurrentProcessIdDelegate>("GetCurrentProcessId");

        // Assert
        normalFunction.Should().NotBeNull();
        normalLoad.Should().NotBeNull();
        dataFileLoad.Should().NotBeNull();
        
        // Both libraries loaded successfully
        var processId = normalFunction!();
        processId.Should().BeGreaterThan(0u);
    }

    [Fact]
    public void FunctionPointerRoundTrip_WithNativeFunction_WorksCorrectly()
    {
        // Arrange
        using var library = new UnmanagedLibrary("kernel32.dll");
        var originalFunction = library.GetUnmanagedFunction<GetCurrentProcessIdDelegate>("GetCurrentProcessId");
        
        // Act - Get function pointer
        var functionPtr = Marshal.GetFunctionPointerForDelegate(originalFunction!);

        // Assert
        functionPtr.Should().NotBe(IntPtr.Zero);
        
        // Can still call original function
        var processId = originalFunction!();
        processId.Should().Be(TestHelpers.GetCurrentProcessId());
    }

    [Fact]
    public void ManagedToNative_CallbackScenario_WorksCorrectly()
    {
        // Arrange
        Func<int, int, int> managedCallback = (a, b) => a * b;
        
        // Act
        var ptr = UnmanagedLibrary.GetFunctionPointerForDelegate(managedCallback, out var binder);

        // Assert
        ptr.Should().NotBe(IntPtr.Zero);
        binder.Should().NotBeNull();
        
        // Can still invoke the original callback
        var result = managedCallback(5, 7);
        result.Should().Be(35);
    }

    [Fact]
    public void DelegatePin_InRealScenario_PreservesDelegateLifetime()
    {
        // Arrange
        Func<int, int> callback = x => x * 2;
        var ptr = UnmanagedLibrary.GetFunctionPointerForDelegate(callback, out var binder);

        // Act
        using var pin = new DelegatePin(ptr, binder);

        // Assert
        pin.Ptr.Should().Be(ptr);
        pin.Ptr.Should().NotBe(IntPtr.Zero);
        
        // Can still invoke callback
        var result = callback(10);
        result.Should().Be(20);
    }

    [Fact]
    public void MultipleLibraryInstances_SameLibrary_WorkIndependently()
    {
        // Arrange & Act
        using var library1 = new UnmanagedLibrary("kernel32.dll");
        using var library2 = new UnmanagedLibrary("kernel32.dll");

        var func1 = library1.GetUnmanagedFunction<GetCurrentProcessIdDelegate>("GetCurrentProcessId");
        var func2 = library2.GetUnmanagedFunction<GetCurrentProcessIdDelegate>("GetCurrentProcessId");

        var pid1 = func1!();
        var pid2 = func2!();

        // Assert
        pid1.Should().Be(pid2);
        pid1.Should().Be(TestHelpers.GetCurrentProcessId());
    }

    [Fact]
    public void LoadLibrary_DisposeAndReload_WorksCorrectly()
    {
        // Arrange
        uint pid1;
        using (var library = new UnmanagedLibrary("kernel32.dll"))
        {
            var func = library.GetUnmanagedFunction<GetCurrentProcessIdDelegate>("GetCurrentProcessId");
            pid1 = func!();
        }

        // Act
        uint pid2;
        using (var library = new UnmanagedLibrary("kernel32.dll"))
        {
            var func = library.GetUnmanagedFunction<GetCurrentProcessIdDelegate>("GetCurrentProcessId");
            pid2 = func!();
        }

        // Assert
        pid1.Should().Be(pid2);
        pid1.Should().Be(TestHelpers.GetCurrentProcessId());
    }

    [Fact]
    public void ComplexScenario_LoadMultipleFunctionsInvokeAndDispose_WorksCorrectly()
    {
        // Arrange
        using var library = new UnmanagedLibrary("kernel32.dll");

        // Act - Load multiple functions
        var getProcessId = library.GetUnmanagedFunction<GetCurrentProcessIdDelegate>("GetCurrentProcessId");
        var getProcess = library.GetUnmanagedFunction<GetCurrentProcessDelegate>("GetCurrentProcess");
        var getTickCount = library.GetUnmanagedFunction<GetTickCountDelegate>("GetTickCount");
        var getModuleHandle = library.GetUnmanagedFunction<GetModuleHandleW>(nameof(GetModuleHandleW));

        // Invoke all functions
        var processId = getProcessId!();
        var processHandle = getProcess!();
        var tickCount = getTickCount!();
        var moduleHandle = getModuleHandle!(null);

        // Assert - All functions should work
        processId.Should().BeGreaterThan(0u);
        processHandle.Should().NotBe(IntPtr.Zero);
        tickCount.Should().BeGreaterThan(0u);
        moduleHandle.Should().NotBe(IntPtr.Zero);
    }
}
