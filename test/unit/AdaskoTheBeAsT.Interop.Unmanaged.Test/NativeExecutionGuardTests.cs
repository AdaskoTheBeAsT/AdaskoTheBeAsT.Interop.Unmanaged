using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Threading;
using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Unmanaged.Test;

#pragma warning disable SA1121

[Collection(ProcessErrorModeTestCollection.Name)]
[Trait("Category", "Native")]
public class NativeExecutionGuardTests
{
    private const uint RoundingModeMask = 0x00000300;
    private const uint RoundingChopValue = 0x00000300;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint GetUIntFn();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void VoidFn();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate double DivideFn(
        double numerator,
        double denominator);

    [Fact]
    public void EnterScope_AppliesAndRestoresThreadErrorMode_WithoutMutatingProcessErrorMode()
    {
        if (TestHelpers.SkipIfNotWindows())
        {
            return;
        }

        // Pre-bind P/Invoke and assertion assemblies before inspecting process-wide error mode,
        // because .NET Framework's clr.dll temporarily modifies SetErrorMode during DLL loads.
        _ = WindowsExecutionNativeMethods.GetThreadErrorMode();
        0u.Should().Be(0u);

        var savedProcessMode = WindowsExecutionNativeMethods.SetErrorMode(0u);
        try
        {
            var threadBefore = WindowsExecutionNativeMethods.GetThreadErrorMode();
            uint ownerThreadId;
            uint activeThreadMode;
            uint processDuringScope;

            using (var scope = NativeExecutionGuard.EnterScope(
                       NativeErrorModes.FailCriticalErrors | NativeErrorModes.NoOpenFileErrorBox))
            {
                ownerThreadId = (uint)scope.OwnerThreadId;
                activeThreadMode = WindowsExecutionNativeMethods.GetThreadErrorMode();
                processDuringScope = WindowsExecutionNativeMethods.GetErrorMode();
            }

            var threadAfter = WindowsExecutionNativeMethods.GetThreadErrorMode();
            ownerThreadId.Should().Be((uint)Environment.CurrentManagedThreadId);
            (activeThreadMode & (uint)NativeErrorModes.FailCriticalErrors).Should().NotBe(0u);
            (activeThreadMode & (uint)NativeErrorModes.NoOpenFileErrorBox).Should().NotBe(0u);
            processDuringScope.Should().Be(0u);
            threadAfter.Should().Be(threadBefore);
        }
        finally
        {
            _ = WindowsExecutionNativeMethods.SetErrorMode(savedProcessMode);
        }
    }

    [Fact]
    public void EnterScope_RejectsUnsupportedNativeErrorModeFlags()
    {
        Action act = () => NativeExecutionGuard.EnterScope((NativeErrorModes)0x01000000u);
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("threadErrorMode");
    }

    [Fact]
    [SuppressMessage("IDisposableAnalyzers", "IDISP016", Justification = "Verifies post-dispose exception contract.")]
    [SuppressMessage("IDisposableAnalyzers", "IDISP017", Justification = "Verifies idempotent Dispose behavior.")]
    public void Scope_EnforcesOwningThreadAffinity_AndSupportsIdempotentDispose()
    {
        using var scope = NativeExecutionGuard.EnterScope();
        Exception? crossThreadRestoreError = null;
        Exception? crossThreadDisposeError = null;

        var worker = new Thread(() =>
        {
            try
            {
                scope.RestoreFloatingPointState();
            }
            catch (Exception ex)
            {
                crossThreadRestoreError = ex;
            }

            try
            {
                scope.Dispose();
            }
            catch (Exception ex)
            {
                crossThreadDisposeError = ex;
            }
        });
        worker.Start();
        worker.Join();

        crossThreadRestoreError.Should().BeOfType<InvalidOperationException>();
        crossThreadDisposeError.Should().BeOfType<InvalidOperationException>();

        scope.Dispose();
        scope.Dispose();
        Action afterDispose = scope.RestoreFloatingPointState;
        afterDispose.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void EnterScope_ClearsStickyFlagsRestoresCallerRoundingAndPreventsUnmaskedFpuTraps()
    {
        if (TestHelpers.SkipIfNotWindows())
        {
            return;
        }

        using var fixture = new NativeFixture();
        using var library = new UnmanagedLibrary(fixture.LibraryPath);
        var getCw = library.GetUnmanagedFunction<GetUIntFn>("fixture_get_fp_control_word")!;
        var getSw = library.GetUnmanagedFunction<GetUIntFn>("fixture_get_fp_status_word")!;
        var setChop = library.GetUnmanagedFunction<VoidFn>("fixture_set_fp_rounding_chop")!;
        var setNear = library.GetUnmanagedFunction<VoidFn>("fixture_set_fp_rounding_near")!;
        var corruptFpu = library.GetUnmanagedFunction<VoidFn>("fixture_corrupt_fpu_state")!;
        var divide = library.GetUnmanagedFunction<DivideFn>("fixture_divide_doubles")!;

        try
        {
            setChop();
            (getCw() & RoundingModeMask).Should().Be(RoundingChopValue);

            using (var scope = NativeExecutionGuard.EnterScope())
            {
                corruptFpu();
                scope.RestoreFloatingPointState();
                divide(1.0, 0.0).Should().Be(double.PositiveInfinity);
                (getSw() & 0x3Fu).Should().NotBe(0u);
                corruptFpu();
            }

            (getSw() & 0x3Fu).Should().Be(0u);
            (getCw() & RoundingModeMask).Should().Be(RoundingChopValue);
            divide(1.0, 0.0).Should().Be(double.PositiveInfinity);
            double.IsNaN(divide(0.0, 0.0)).Should().BeTrue();
        }
        finally
        {
            setNear();
            NativeExecutionGuard.ResetFloatingPointState();
        }
    }

    [Fact]
    public void ResetFloatingPointState_MasksExceptionsAndClearsStatusWithoutResettingRoundingMode()
    {
        if (TestHelpers.SkipIfNotWindows())
        {
            return;
        }

        using var fixture = new NativeFixture();
        using var library = new UnmanagedLibrary(fixture.LibraryPath);
        var getCw = library.GetUnmanagedFunction<GetUIntFn>("fixture_get_fp_control_word")!;
        var getSw = library.GetUnmanagedFunction<GetUIntFn>("fixture_get_fp_status_word")!;
        var setChop = library.GetUnmanagedFunction<VoidFn>("fixture_set_fp_rounding_chop")!;
        var setNear = library.GetUnmanagedFunction<VoidFn>("fixture_set_fp_rounding_near")!;
        var corruptFpu = library.GetUnmanagedFunction<VoidFn>("fixture_corrupt_fpu_state")!;
        var divide = library.GetUnmanagedFunction<DivideFn>("fixture_divide_doubles")!;

        try
        {
            corruptFpu();
            setChop();
            NativeExecutionGuard.ResetFloatingPointState();

            (getCw() & RoundingModeMask).Should().Be(RoundingChopValue);
            divide(1.0, 0.0).Should().Be(double.PositiveInfinity);
            double.IsNaN(divide(0.0, 0.0)).Should().BeTrue();
            NativeExecutionGuard.ResetFloatingPointState();
            (getSw() & 0x3Fu).Should().Be(0u);
        }
        finally
        {
            setNear();
            NativeExecutionGuard.ResetFloatingPointState();
        }
    }

    [Fact]
    public void SuppressProcessErrorDialogs_EnablesDialogSuppressionFlags()
    {
        if (TestHelpers.SkipIfNotWindows())
        {
            return;
        }

        var previous = WindowsExecutionNativeMethods.GetErrorMode();
        try
        {
            _ = NativeExecutionGuard.SuppressProcessErrorDialogs();
            var updated = WindowsExecutionNativeMethods.GetErrorMode();
            (updated & (uint)NativeErrorModes.SuppressErrorDialogs).Should()
                .Be((uint)NativeErrorModes.SuppressErrorDialogs);
        }
        finally
        {
            _ = WindowsExecutionNativeMethods.SetErrorMode(previous);
        }
    }
}
