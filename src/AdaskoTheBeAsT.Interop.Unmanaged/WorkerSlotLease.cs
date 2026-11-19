using System;
using System.Threading;

namespace AdaskoTheBeAsT.Interop.Unmanaged;

/// <summary>
/// Represents an active lease on a bounded worker slot index allocated by <see cref="WorkerSlotAllocator"/>.
/// </summary>
public sealed class WorkerSlotLease : IDisposable
{
    private readonly WorkerSlotAllocator _owner;
    private int _disposed;

    internal WorkerSlotLease(
        WorkerSlotAllocator owner,
        int slotIndex)
    {
        _owner = owner;
        SlotIndex = slotIndex;
    }

    /// <summary>
    /// Gets the leased zero-based worker slot index.
    /// </summary>
    public int SlotIndex { get; }

    /// <summary>
    /// Releases the leased worker slot back to its <see cref="WorkerSlotAllocator"/>. Idempotent on repeated calls.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _owner.ReleaseSlot(SlotIndex);
    }
}
