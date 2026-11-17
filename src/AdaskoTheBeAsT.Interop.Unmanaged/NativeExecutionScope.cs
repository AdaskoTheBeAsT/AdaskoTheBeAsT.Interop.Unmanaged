using System;
using System.Globalization;
using System.Threading;

namespace AdaskoTheBeAsT.Interop.Unmanaged;

/// <summary>
/// Thread-affine execution scope that captures the caller's floating-point control state and thread error mode,
/// clears pending floating-point exceptions before restoring control words, and restores thread error mode on disposal.
/// </summary>
public sealed class NativeExecutionScope : IDisposable
{
    private readonly int _ownerThreadId;
    private readonly bool _isWindows;
    private readonly bool _isX86;
    private readonly uint _savedPrimaryFpControl;
    private readonly uint _savedSecondaryFpControl;
    private readonly bool _hasSavedThreadErrorMode;
    private readonly uint _savedThreadErrorMode;
    private int _disposed;

    internal NativeExecutionScope(
        int ownerThreadId,
        bool isWindows,
        bool isX86,
        uint savedPrimaryFpControl,
        uint savedSecondaryFpControl,
        bool hasSavedThreadErrorMode,
        uint savedThreadErrorMode)
    {
        _ownerThreadId = ownerThreadId;
        _isWindows = isWindows;
        _isX86 = isX86;
        _savedPrimaryFpControl = savedPrimaryFpControl;
        _savedSecondaryFpControl = savedSecondaryFpControl;
        _hasSavedThreadErrorMode = hasSavedThreadErrorMode;
        _savedThreadErrorMode = savedThreadErrorMode;
    }

    /// <summary>
    /// Gets the managed thread ID of the thread that entered this execution scope.
    /// </summary>
#pragma warning disable RCS1085 // Use auto-implemented property
    public int OwnerThreadId => _ownerThreadId;
#pragma warning restore RCS1085 // Use auto-implemented property

    /// <summary>
    /// Clears pending floating-point status flags and restores the caller's captured floating-point control state
    /// without ending the scope. Must be called on <see cref="OwnerThreadId"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when called from a thread other than <see cref="OwnerThreadId"/>.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this scope has already been disposed.</exception>
    public void RestoreFloatingPointState()
    {
        EnsureOwningThread();
#if NET10_0_OR_GREATER
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, instance: this);
#endif
#if NET472_OR_GREATER
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(NativeExecutionScope));
        }
#endif
        RestoreFloatingPointCore();
    }

    /// <summary>
    /// Clears pending floating-point exceptions, restores the caller's captured floating-point control modes,
    /// and restores the caller's thread error mode. Idempotent when called repeatedly on <see cref="OwnerThreadId"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when called from a thread other than <see cref="OwnerThreadId"/>.
    /// </exception>
    public void Dispose()
    {
        EnsureOwningThread();
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        RestoreFloatingPointCore();
        if (_isWindows && _hasSavedThreadErrorMode)
        {
            _ = WindowsExecutionNativeMethods.SetThreadErrorMode(_savedThreadErrorMode, out _);
        }
    }

    private void EnsureOwningThread()
    {
        var currentThreadId = Environment.CurrentManagedThreadId;
        if (currentThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException(
                $"NativeExecutionScope is thread-affine to managed thread {_ownerThreadId.ToString(CultureInfo.InvariantCulture)} and cannot be used or disposed from thread {currentThreadId.ToString(CultureInfo.InvariantCulture)}.");
        }
    }

    private void RestoreFloatingPointCore()
    {
        if (!_isWindows)
        {
            return;
        }

        // Always clear pending x87/SSE sticky exception flags BEFORE restoring control masks so
        // previously raised status bits cannot trigger a hardware trap when masks are applied.
        _ = WindowsExecutionNativeMethods.ClearFloatingPointStatus();
        if (_isX86)
        {
            WindowsExecutionNativeMethods.WriteX86ControlWords(
                _savedPrimaryFpControl,
                WindowsExecutionNativeMethods.X87ControlPreserveMask,
                _savedSecondaryFpControl,
                WindowsExecutionNativeMethods.Sse2ControlPreserveMask);
            return;
        }

        if (WindowsExecutionNativeMethods.ControlFpSafe(
                out _,
                _savedPrimaryFpControl,
                WindowsExecutionNativeMethods.Sse2ControlPreserveMask) != 0)
        {
            _ = WindowsExecutionNativeMethods.ControlFpSafe(
                out _,
                _savedPrimaryFpControl,
                WindowsExecutionNativeMethods.BasicControlPreserveMask);
        }
    }
}
