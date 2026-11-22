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
        int slotIndex,
        int generation)
    {
        _owner = owner;
        SlotIndex = slotIndex;
        Generation = generation;
    }

    /// <summary>
    /// Gets the leased zero-based worker slot index.
    /// </summary>
    public int SlotIndex { get; }

    /// <summary>
    /// Gets the zero-based acquisition generation for this slot within its allocator.
    /// Wraps after 1296 acquisitions; use one allocator per isolation namespace.
    /// </summary>
    public int Generation { get; }

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
