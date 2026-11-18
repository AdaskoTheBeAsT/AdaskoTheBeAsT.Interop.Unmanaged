using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
#if NETFRAMEWORK
using System.Security.Permissions;
#endif

namespace AdaskoTheBeAsT.Interop.Unmanaged;

#pragma warning disable SA1121

/// <summary>
/// Reusable unmanaged heap scratch buffer (<see cref="SafeBuffer"/>) with deterministic zero-initialization,
/// bounded <see cref="Clear()"/> helpers, and RAII reference-counted pointer leases (<see cref="AcquirePointerLease"/>).
/// </summary>
#if NETFRAMEWORK
[SecurityPermission(SecurityAction.LinkDemand, UnmanagedCode = true)]
#endif
public sealed class UnmanagedScratchBuffer : SafeBuffer
{
    private static readonly byte[] ZeroChunk = new byte[4096];

    private readonly int _byteLength;

    /// <summary>
    /// Initializes a new instance of the <see cref="UnmanagedScratchBuffer"/> class.
    /// </summary>
    /// <param name="byteLength">Positive unmanaged buffer byte length to allocate.</param>
    /// <param name="zeroMemory">When <see langword="true"/> (default), zero-fills the allocated unmanaged buffer before returning.</param>
    public UnmanagedScratchBuffer(int byteLength, bool zeroMemory = true)
        : base(ownsHandle: true)
    {
        if (byteLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteLength), byteLength, "Scratch buffer byte length must be greater than zero.");
        }

        _byteLength = byteLength;
        var allocated = Marshal.AllocHGlobal(byteLength);
        SetHandle(allocated);
        Initialize((ulong)byteLength);

        if (zeroMemory)
        {
            try
            {
                ZeroRange(allocated, 0, byteLength);
            }
            catch
            {
                Marshal.FreeHGlobal(allocated);
                SetHandleAsInvalid();
                throw;
            }
        }
    }

    /// <summary>
    /// Gets the size of this unmanaged scratch buffer in bytes.
    /// </summary>
#pragma warning disable RCS1085 // Use auto-implemented property
    public new int ByteLength => _byteLength;
#pragma warning restore RCS1085 // Use auto-implemented property

    /// <summary>
    /// Zeroes the entire scratch buffer.
    /// </summary>
    public void Clear()
    {
        Clear(0, _byteLength);
    }

    /// <summary>
    /// Zeroes <paramref name="byteCount"/> bytes starting at <paramref name="byteOffset"/> inside this scratch buffer.
    /// </summary>
    /// <param name="byteOffset">Zero-based byte offset inside the buffer.</param>
    /// <param name="byteCount">Non-negative number of bytes to clear.</param>
    public void Clear(int byteOffset, int byteCount)
    {
        if (byteOffset < 0 || byteOffset > _byteLength)
        {
            throw new ArgumentOutOfRangeException(nameof(byteOffset), byteOffset, "Byte offset is out of buffer bounds.");
        }

        if (byteCount < 0 || byteOffset > _byteLength - byteCount)
        {
            throw new ArgumentOutOfRangeException(nameof(byteCount), byteCount, "Byte count exceeds buffer bounds.");
        }

        var addedRef = false;
        try
        {
            DangerousAddRef(ref addedRef);
            if (byteCount > 0)
            {
#pragma warning disable S3869
                ZeroRange(DangerousGetHandle(), byteOffset, byteCount);
#pragma warning restore S3869
            }
        }
        finally
        {
            if (addedRef)
            {
                DangerousRelease();
            }
        }
    }

    /// <summary>
    /// Acquires a reference-counted pointer lease keeping this <see cref="UnmanagedScratchBuffer"/> alive across native calls.
    /// </summary>
    /// <returns>A stack-bound <see cref="PointerLease"/> releasing the <see cref="SafeHandle"/> reference on disposal.</returns>
    public PointerLease AcquirePointerLease()
    {
        var addedRef = false;
        DangerousAddRef(ref addedRef);
#pragma warning disable S3869
        var ptr = DangerousGetHandle();
#pragma warning restore S3869
        return new PointerLease(this, ptr, _byteLength);
    }

    /// <inheritdoc />
    [ExcludeFromCodeCoverage]
    protected override bool ReleaseHandle()
    {
        Marshal.FreeHGlobal(handle);
        return true;
    }

    private static void ZeroRange(IntPtr basePointer, int byteOffset, int byteCount)
    {
        var cursor = byteOffset;
        var end = byteOffset + byteCount;
        while (cursor < end)
        {
            var chunk = Math.Min(ZeroChunk.Length, end - cursor);
            var target = IntPtr.Size == 8
                ? new IntPtr(checked(basePointer.ToInt64() + cursor))
                : new IntPtr(checked(basePointer.ToInt32() + cursor));
            Marshal.Copy(ZeroChunk, 0, target, chunk);
            cursor += chunk;
        }
    }

    /// <summary>
    /// Stack-bound RAII lease holding a reference count on an <see cref="UnmanagedScratchBuffer"/>.
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    public ref struct PointerLease
    {
        private UnmanagedScratchBuffer? _owner;
        private IntPtr _pointer;

        internal PointerLease(UnmanagedScratchBuffer owner, IntPtr pointer, int byteLength)
        {
            _owner = owner;
            _pointer = pointer;
            ByteLength = byteLength;
        }

#pragma warning disable IDE0251,MA0102
        /// <summary>
        /// Gets the native pointer to the start of the scratch buffer, or <see cref="IntPtr.Zero"/> once disposed.
        /// </summary>
        public IntPtr Pointer => _pointer;
#pragma warning restore IDE0251,MA0102

        /// <summary>
        /// Gets the total byte length of the leased scratch buffer.
        /// </summary>
        public int ByteLength { get; }

        /// <summary>
        /// Releases the <see cref="SafeHandle"/> reference acquired by <see cref="AcquirePointerLease"/>. Idempotent on repeated calls.
        /// </summary>
        public void Dispose()
        {
            var owner = _owner;
            if (owner is null)
            {
                return;
            }

            _owner = null;
            _pointer = IntPtr.Zero;
            owner.DangerousRelease();
        }
    }
}
