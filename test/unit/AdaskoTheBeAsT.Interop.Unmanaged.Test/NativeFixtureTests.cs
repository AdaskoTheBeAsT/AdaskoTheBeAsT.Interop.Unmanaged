using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Unmanaged.Test;

[Trait("Category", "Native")]
public class NativeFixtureTests
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Binary(int first, int second);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int StdCallBinary(int first, int second);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Unary(int value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GenericUnary<T>(T value);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int StdCallUnary<T>(T value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int RefCallback<T>(ref T value, out int copy);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool TextCallback<T>([MarshalAs(UnmanagedType.LPWStr)] T value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int InvokeRef(IntPtr callback, int value, out int copy);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte InvokeText(IntPtr callback);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int InvokeCallback(IntPtr callback, int value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void RegisterCallback(IntPtr callback);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void UnregisterCallback();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetValue();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void Increment(ref int value);

    private delegate Pair TransformPair(Pair value);

    [Fact]
    public void AbsoluteUnicodePath_LoadsAndInvokesNativeExports()
    {
        using var fixture = new NativeFixture();
        using var library = new UnmanagedLibrary(fixture.LibraryPath);
        library.GetUnmanagedFunction<Binary>("fixture_add")!(20, 22).Should().Be(42);
        library.GetUnmanagedFunction<StdCallBinary>("fixture_add_stdcall")!(20, 22).Should().Be(42);
        library.GetUnmanagedFunction<Binary>("FIXTURE_ADD").Should().BeNull();
        library.TryGetExport("missing", out var missing).Should().BeFalse();
        missing.Should().Be(IntPtr.Zero);

        library.TryGetExport("fixture_add", out var pointer).Should().BeTrue();
        UnmanagedLibrary.GetDelegateForFunctionPointer<Func<int, int, int>>(pointer, CallingConvention.Cdecl)!(20, 22)
            .Should().Be(42);
        library.TryGetExport("fixture_increment", out pointer).Should().BeTrue();
        var value = 41;
        library.GetUnmanagedFunction<Increment>("fixture_increment")!(ref value);
        value.Should().Be(42);
        library.TryGetExport("fixture_pair", out pointer).Should().BeTrue();
        var pair = UnmanagedLibrary.GetDelegateForFunctionPointer<TransformPair>(pointer, CallingConvention.Cdecl)!(
            new Pair { First = 20, Second = 21 });
        pair.First.Should().Be(21);
        pair.Second.Should().Be(22);
        library.TryGetExport("fixture_echo", out pointer).Should().BeTrue();
        UnmanagedLibrary.GetDelegateForFunctionPointer<Func<IntPtr, IntPtr>>(pointer, CallingConvention.Cdecl)!(new IntPtr(42))
            .Should().Be(new IntPtr(42));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    [InlineData("\0")]
    [InlineData("fixture_add\0suffix")]
    public void Lookup_RejectsInvalidNamesAcrossAllOverloads(string? name)
    {
        using var fixture = new NativeFixture();
        using var library = new UnmanagedLibrary(fixture.LibraryPath);
        using var handle = UnmanagedLibrary.LoadLibrary(fixture.LibraryPath);
        var lookups = new Action[]
        {
            () => library.GetUnmanagedFunction<Binary>(name!),
            () => library.TryGetExport(name!, out _),
            () => UnmanagedLibrary.GetUnmanagedFunction<Binary>(handle, name!),
            () => UnmanagedLibrary.TryGetExport(handle, name!, out _),
        };
        foreach (var lookup in lookups)
        {
            lookup.Should().Throw<ArgumentException>().WithParameterName("functionName");
        }
    }

    [Fact]
    [SuppressMessage("IDisposableAnalyzers", "IDISP016", Justification = "Verifies the public disposed-owner error contract.")]
    [SuppressMessage("IDisposableAnalyzers", "IDISP017", Justification = "Explicit early disposal is the behavior under test.")]
    public void DisposedOwners_RejectAllLookupOverloads()
    {
        using var fixture = new NativeFixture();
        using var library = new UnmanagedLibrary(fixture.LibraryPath);
        using var handle = UnmanagedLibrary.LoadLibrary(fixture.LibraryPath);
        library.Dispose();
        handle.Dispose();
        var lookups = new Action[]
        {
            () => library.GetUnmanagedFunction<Binary>("fixture_add"),
            () => library.TryGetExport("fixture_add", out _),
            () => UnmanagedLibrary.GetUnmanagedFunction<Binary>(handle, "fixture_add"),
            () => UnmanagedLibrary.TryGetExport(handle, "fixture_add", out _),
        };
        foreach (var lookup in lookups)
        {
            lookup.Should().Throw<ObjectDisposedException>();
        }
    }

    [Fact]
    public void Disposal_UnloadsTheModuleAndResetsItsState()
    {
        using var fixture = new NativeFixture();
        using (var first = new UnmanagedLibrary(fixture.LibraryPath))
        {
            var counter = first.GetUnmanagedFunction<GetValue>("fixture_counter")!;
            counter().Should().Be(1);
            counter().Should().Be(2);
        }

        using var second = new UnmanagedLibrary(fixture.LibraryPath);
        second.GetUnmanagedFunction<GetValue>("fixture_counter")!().Should().Be(1);
    }

    [Fact]
    public void LookupRacingDisposal_FinishesOrThrowsDisposed()
    {
        using var fixture = new NativeFixture();
        for (var iteration = 0; iteration < 100; iteration++)
        {
            using var library = new UnmanagedLibrary(fixture.LibraryPath);
            Parallel.Invoke(
                () =>
                {
                    try
                    {
                        // Lookup-time protection does not extend to invocation after disposal.
                        library.TryGetExport("fixture_add", out var pointer).Should().BeTrue();
                        pointer.Should().NotBe(IntPtr.Zero);
                    }
                    catch (ObjectDisposedException error)
                    {
                        // Disposal won the race before DangerousAddRef.
                        error.Message.Should().NotBeNullOrEmpty();
                    }
                },
                library.Dispose);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CallbackScope_RemainsCallableFromNativeAfterCollection(bool generic)
    {
        using var fixture = new NativeFixture();
        using var library = new UnmanagedLibrary(fixture.LibraryPath);
        using var scope = CreateScope(generic);
        Collect();

        library.GetUnmanagedFunction<InvokeCallback>("fixture_invoke")!(scope.Ptr, 41).Should().Be(42);
    }

    [Fact]
    public void RetainedCallback_RemainsRootedUntilUnregistered()
    {
        using var fixture = new NativeFixture();
        using var library = new UnmanagedLibrary(fixture.LibraryPath);
        var register = library.GetUnmanagedFunction<RegisterCallback>("fixture_register")!;
        var unregister = library.GetUnmanagedFunction<UnregisterCallback>("fixture_unregister")!;
        var invoke = library.GetUnmanagedFunction<Unary>("fixture_invoke_registered")!;
        var pointer = CreateCallback(out var binder);
        register(pointer);
        try
        {
            Collect();
            invoke(41).Should().Be(42);
        }
        finally
        {
            unregister();
            GC.KeepAlive(binder);
        }

        invoke(41).Should().Be(-1);
    }

    [Fact]
    public void NativeCallbacks_PreserveMarshalingRefOutAndMulticast()
    {
        using var fixture = new NativeFixture();
        using var library = new UnmanagedLibrary(fixture.LibraryPath);
        using var text = UnmanagedLibrary.PinDelegate<TextCallback<string>>(value => string.Equals(value, "AB", StringComparison.Ordinal));
        library.GetUnmanagedFunction<InvokeText>("fixture_invoke_text")!(text.Ptr).Should().Be(1);
        using var byRef = UnmanagedLibrary.PinDelegate<RefCallback<int>>((ref int value, out int copy) =>
        {
            copy = ++value;
            return value;
        });
        library.GetUnmanagedFunction<InvokeRef>("fixture_invoke_ref")!(byRef.Ptr, 41, out var copied).Should().Be(42);
        copied.Should().Be(42);
        using var stdcall = UnmanagedLibrary.PinDelegate<StdCallUnary<int>>(value => value + 1);
        library.GetUnmanagedFunction<InvokeCallback>("fixture_invoke_stdcall")!(stdcall.Ptr, 41).Should().Be(42);

        var first = 0;
        GenericUnary<int> callback = value => first = value;
        callback += value => value + 1;
        using var multicast = UnmanagedLibrary.PinDelegate(callback);
        library.GetUnmanagedFunction<InvokeCallback>("fixture_invoke")!(multicast.Ptr, 41).Should().Be(42);
        first.Should().Be(41);
    }

    [Fact]
    public void AbsolutePath_ResolvesAdjacentDependency()
    {
        using var fixture = new NativeFixture();
        using var library = new UnmanagedLibrary(fixture.GetPath("fixture_dependent"));
        library.GetUnmanagedFunction<GetValue>("fixture_answer")!().Should().Be(42);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("invalid")]
    [InlineData("dependency")]
    [SuppressMessage("Security", "SCS0018", Justification = "Changes only files copied into the test-owned random fixture directory.")]
    [SuppressMessage("Security", "SEC0116", Justification = "Paths belong to this test's temporary fixture, not user input.")]
    public void LoadFailures_PreserveUsefulDiagnostics(string failure)
    {
        using var fixture = new NativeFixture();
        var path = fixture.GetPath("missing");
        if (string.Equals(failure, "invalid", StringComparison.Ordinal))
        {
            File.WriteAllText(path, "This is not a native library.");
        }
        else if (string.Equals(failure, "dependency", StringComparison.Ordinal))
        {
            File.Delete(fixture.GetPath("fixture_dependency"));
            path = fixture.GetPath("fixture_dependent");
        }

        Action load = () =>
        {
            using var library = new UnmanagedLibrary(path);
        };
        var error = load.Should().Throw<Win32Exception>().Which;
        error.Message.Should().Contain(path);
        if (TestHelpers.IsWindows())
        {
            error.NativeErrorCode.Should().NotBe(0);
            error.Message.Should().Contain(new Win32Exception(error.NativeErrorCode).Message);
        }
        else
        {
            error.NativeErrorCode.Should().Be(0);
            error.InnerException.Should().NotBeNull();
        }
    }

    [Fact]
    public void ResourceLoad_DoesNotExposeExecutableExports()
    {
        if (TestHelpers.SkipIfNotWindows())
        {
            return;
        }

        using var fixture = new NativeFixture();
        using var library = new UnmanagedLibrary(fixture.LibraryPath, LoadLibraryFlags.LOAD_LIBRARY_AS_DATAFILE);
        library.TryGetExport("fixture_add", out var pointer).Should().BeFalse();
        pointer.Should().Be(IntPtr.Zero);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static DelegatePin CreateScope(bool generic)
    {
        var offset = int.Parse("1", System.Globalization.CultureInfo.InvariantCulture);
        return generic
            ? UnmanagedLibrary.PinDelegate<GenericUnary<int>>(value => value + offset)
            : UnmanagedLibrary.PinDelegate<Unary>(value => value + offset);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static IntPtr CreateCallback(out object binder)
    {
        var offset = int.Parse("1", System.Globalization.CultureInfo.InvariantCulture);
        return UnmanagedLibrary.GetFunctionPointerForDelegate<GenericUnary<int>>(value => value + offset, out binder);
    }

    [SuppressMessage("Sonar", "S1215", Justification = "Forced collection is required to verify callback rooting.")]
    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Pair
    {
        public int First;

        public int Second;
    }
}
