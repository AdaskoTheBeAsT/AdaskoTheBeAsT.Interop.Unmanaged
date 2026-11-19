using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;

namespace AdaskoTheBeAsT.Interop.Unmanaged;

/// <summary>
/// Thread-safe bounded integer slot allocator (<c>0..maxExclusive - 1</c>) for leasing short fixed-length worker
/// module suffixes, supporting excluded slot indices, explicit preferred-slot hints, and optional thread-sticky slot reuse.
/// </summary>
public sealed class WorkerSlotAllocator
{
    [ThreadStatic]
    private static ConditionalWeakTable<WorkerSlotAllocator, StrongBox<int>>? _threadStickySlots;

#if NET9_0_OR_GREATER
    private readonly Lock _syncRoot = new();
#else
    private readonly object _syncRoot = new();
#endif
    private readonly int _maxExclusive;
    private readonly bool _preferThreadStickySlot;
    private readonly bool[] _excluded;
    private readonly bool[] _occupied;
    private readonly int _capacity;
    private int _availableCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkerSlotAllocator"/> class.
    /// </summary>
    /// <param name="maxExclusive">Exclusive upper bound of slot indices (<c>0..maxExclusive - 1</c>). Defaults to <c>100</c>.</param>
    /// <param name="excludedSlots">Optional slot indices in <c>[0, maxExclusive)</c> permanently excluded from allocation.</param>
    /// <param name="preferThreadStickySlot">
    /// When <see langword="true"/>, a thread without an explicit <c>preferredSlot</c> preferentially re-acquires the last slot
    /// it released on this allocator instance when that slot is still free.
    /// </param>
    public WorkerSlotAllocator(
        int maxExclusive = 100,
        IEnumerable<int>? excludedSlots = null,
        bool preferThreadStickySlot = false)
    {
        if (maxExclusive <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxExclusive),
                maxExclusive,
                "maxExclusive must be greater than zero.");
        }

        _maxExclusive = maxExclusive;
        _preferThreadStickySlot = preferThreadStickySlot;
        _excluded = new bool[maxExclusive];
        _occupied = new bool[maxExclusive];

        var excludedCount = 0;
        if (excludedSlots is not null)
        {
            foreach (var slot in excludedSlots)
            {
                if (slot < 0
                    || slot >= maxExclusive)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(excludedSlots),
                        slot,
                        $"Excluded slot index {slot.ToString(CultureInfo.InvariantCulture)} must be between 0 and {(maxExclusive - 1).ToString(CultureInfo.InvariantCulture)}.");
                }

                if (!_excluded[slot])
                {
                    _excluded[slot] = true;
                    excludedCount++;
                }
            }
        }

        _capacity = maxExclusive - excludedCount;
        _availableCount = _capacity;
    }

    /// <summary>
    /// Gets the total number of non-excluded slots managed by this allocator.
    /// </summary>
#pragma warning disable RCS1085 // Use auto-implemented property
    public int Capacity => _capacity;
#pragma warning restore RCS1085 // Use auto-implemented property

    /// <summary>
    /// Gets the number of non-excluded slots currently available for acquisition.
    /// </summary>
    public int AvailableCount
    {
        get
        {
            lock (_syncRoot)
            {
                return _availableCount;
            }
        }
    }

    /// <summary>
    /// Acquires an available worker slot lease, throwing <see cref="InvalidOperationException"/> when all non-excluded slots are occupied.
    /// </summary>
    /// <param name="preferredSlot">Optional preferred slot index in <c>[0, maxExclusive)</c>.</param>
    /// <returns>A <see cref="WorkerSlotLease"/> releasing the slot on disposal.</returns>
    public WorkerSlotLease Acquire(int? preferredSlot = null)
    {
        if (TryClaimSlotIndex(preferredSlot, out var slotIndex))
        {
            return new WorkerSlotLease(this, slotIndex);
        }

        throw new InvalidOperationException(
            $"All {_capacity.ToString(CultureInfo.InvariantCulture)} non-excluded worker slots in this WorkerSlotAllocator are currently leased.");
    }

    /// <summary>
    /// Attempts to acquire an available worker slot lease without blocking or throwing on exhaustion.
    /// </summary>
    /// <param name="lease">Receives the acquired <see cref="WorkerSlotLease"/> when successful; otherwise <see langword="null"/>.</param>
    /// <param name="preferredSlot">Optional preferred slot index in <c>[0, maxExclusive)</c>.</param>
    /// <returns><see langword="true"/> when a slot was leased; <see langword="false"/> when all non-excluded slots are occupied.</returns>
    public bool TryAcquire(
        [NotNullWhen(true)] out WorkerSlotLease? lease,
        int? preferredSlot = null)
    {
        if (TryClaimSlotIndex(preferredSlot, out var slotIndex))
        {
            lease = new WorkerSlotLease(this, slotIndex);
            return true;
        }

        lease = null;
        return false;
    }

    internal void ReleaseSlot(int slotIndex)
    {
        lock (_syncRoot)
        {
            if (!_occupied[slotIndex])
            {
                return;
            }

            _occupied[slotIndex] = false;
            _availableCount++;
        }

        if (_preferThreadStickySlot)
        {
            RecordThreadStickySlot(this, slotIndex);
        }
    }

    private static bool TryGetThreadStickySlot(
        WorkerSlotAllocator allocator,
        out int slotIndex)
    {
        var table = _threadStickySlots;
        if (table is not null
            && table.TryGetValue(allocator, out var box))
        {
            slotIndex = box.Value;
            return true;
        }

        slotIndex = -1;
        return false;
    }

    private static void RecordThreadStickySlot(
        WorkerSlotAllocator allocator,
        int slotIndex)
    {
        var table = _threadStickySlots;
        if (table is null)
        {
#if NET8_0_OR_GREATER
            table = [];
#else
            table = new ConditionalWeakTable<WorkerSlotAllocator, StrongBox<int>>();
#endif
            _threadStickySlots = table;
        }

        if (table.TryGetValue(allocator, out var box))
        {
            box.Value = slotIndex;
        }
        else
        {
            table.Add(allocator, new StrongBox<int>(slotIndex));
        }
    }

    private bool TryClaimSlotIndex(
        int? preferredSlot,
        out int slotIndex)
    {
        if (preferredSlot.HasValue
            && (preferredSlot.Value < 0 || preferredSlot.Value >= _maxExclusive))
        {
            throw new ArgumentOutOfRangeException(
                nameof(preferredSlot),
                preferredSlot.Value,
                $"Preferred slot index {preferredSlot.Value.ToString(CultureInfo.InvariantCulture)} must be between 0 and {(_maxExclusive - 1).ToString(CultureInfo.InvariantCulture)}.");
        }

        lock (_syncRoot)
        {
            if (_availableCount == 0)
            {
                slotIndex = -1;
                return false;
            }

            if (preferredSlot.HasValue
                && IsFreeUnsafe(preferredSlot.Value))
            {
                slotIndex = MarkOccupiedUnsafe(preferredSlot.Value);
                return true;
            }

            if (_preferThreadStickySlot
                && TryGetThreadStickySlot(this, out var stickySlot)
                && IsFreeUnsafe(stickySlot))
            {
                slotIndex = MarkOccupiedUnsafe(stickySlot);
                return true;
            }

            for (var i = 0; i < _maxExclusive; i++)
            {
                if (IsFreeUnsafe(i))
                {
                    slotIndex = MarkOccupiedUnsafe(i);
                    return true;
                }
            }

            slotIndex = -1;
            return false;
        }
    }

    private bool IsFreeUnsafe(int slotIndex)
    {
        return slotIndex >= 0
               && slotIndex < _maxExclusive
               && !_excluded[slotIndex]
               && !_occupied[slotIndex];
    }

    private int MarkOccupiedUnsafe(int slotIndex)
    {
        _occupied[slotIndex] = true;
        _availableCount--;
        return slotIndex;
    }
}
