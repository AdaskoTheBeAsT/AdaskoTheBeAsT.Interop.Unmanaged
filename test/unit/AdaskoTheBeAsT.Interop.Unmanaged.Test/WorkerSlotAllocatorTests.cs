using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Unmanaged.Test;

#pragma warning disable SA1121

public class WorkerSlotAllocatorTests
{
    [Fact]
    public void GenerationsAdvancePerSlotAndWrapAfter1296Leases()
    {
        var allocator = new WorkerSlotAllocator(maxExclusive: 2);
        for (var generation = 0; generation < 1297; generation++)
        {
            using var lease = allocator.Acquire(preferredSlot: 1);
            lease.SlotIndex.Should().Be(1);
            lease.Generation.Should().Be(generation % 1296);
        }

        allocator.TryAcquire(out var first).Should().BeTrue();
        using (first)
        {
            first!.SlotIndex.Should().Be(0);
            first.Generation.Should().Be(0);
        }

        using var independent = new WorkerSlotAllocator(maxExclusive: 2).Acquire(preferredSlot: 1);
        independent.Generation.Should().Be(0);
    }

    [Fact]
    [SuppressMessage("IDisposableAnalyzers", "IDISP016", Justification = "Verifies idempotent double-Dispose.")]
    [SuppressMessage(
        "IDisposableAnalyzers",
        "IDISP017",
        Justification = "Explicit Dispose tests slot recycling and double-dispose safety.")]
    public void AcquireAndTryAcquire_ScanLowestSlotHonorPreferredHintSkipExcludedAndHandleDoubleDispose()
    {
        var allocator = new WorkerSlotAllocator(maxExclusive: 5, excludedSlots: [0, 3, 3]);
        allocator.Capacity.Should().Be(3);
        allocator.AvailableCount.Should().Be(3);

        using var first = allocator.Acquire();
        first.SlotIndex.Should().Be(1);
        allocator.AvailableCount.Should().Be(2);

        using var preferredHit = allocator.Acquire(preferredSlot: 4);
        preferredHit.SlotIndex.Should().Be(4);

        using var excludedPreferredFallback = allocator.Acquire(preferredSlot: 3);
        excludedPreferredFallback.SlotIndex.Should().Be(2);
        allocator.AvailableCount.Should().Be(0);

        allocator.TryAcquire(out var exhausted).Should().BeFalse();
        using (exhausted)
        {
            exhausted.Should().BeNull();
        }

        Action exhaustedThrow = () =>
        {
            using var never = allocator.Acquire();
        };
        exhaustedThrow.Should().Throw<InvalidOperationException>();

        preferredHit.Dispose();
        allocator.AvailableCount.Should().Be(1);
        using var reacquired = allocator.Acquire();
        reacquired.SlotIndex.Should().Be(4);
        reacquired.Generation.Should().Be(1);

        preferredHit.Dispose();
        allocator.AvailableCount.Should().Be(0);
    }

    [Fact]
    [SuppressMessage(
        "IDisposableAnalyzers",
        "IDISP017",
        Justification = "Explicit Dispose tests thread-sticky slot recycling.")]
    public void PreferThreadStickySlot_ReusesLastReleasedSlotPerAllocatorWithoutCrossContamination()
    {
        var allocatorA = new WorkerSlotAllocator(maxExclusive: 10, preferThreadStickySlot: true);
        var allocatorB = new WorkerSlotAllocator(maxExclusive: 10, preferThreadStickySlot: true);
        var observedA = -1;
        var observedB = -1;
        var fallbackA = -1;

        var dedicatedWorker = new Thread(() =>
        {
            var leaseA = allocatorA.Acquire(preferredSlot: 7);
            leaseA.Dispose();

            var leaseB = allocatorB.Acquire(preferredSlot: 4);
            leaseB.Dispose();

            using (var stickyA = allocatorA.Acquire())
            {
                observedA = stickyA.SlotIndex;
            }

            using (var stickyB = allocatorB.Acquire())
            {
                observedB = stickyB.SlotIndex;
            }

            using var occupy7FromMainHint = allocatorA.Acquire(preferredSlot: 7);
            using var nextFallback = allocatorA.Acquire();
            fallbackA = nextFallback.SlotIndex;
        });

        dedicatedWorker.Start();
        dedicatedWorker.Join();

        observedA.Should().Be(7);
        observedB.Should().Be(4);
        fallbackA.Should().Be(0);
    }

    [Fact]
    public void Validation_RejectsInvalidBoundsAndOutOfRangeSlots()
    {
        Action zeroMax = () => _ = new WorkerSlotAllocator(maxExclusive: 0);
        Action negMax = () => _ = new WorkerSlotAllocator(maxExclusive: -3);
        Action negExcluded = () => _ = new WorkerSlotAllocator(maxExclusive: 5, excludedSlots: [-1]);
        Action highExcluded = () => _ = new WorkerSlotAllocator(maxExclusive: 5, excludedSlots: [5]);

        zeroMax.Should().Throw<ArgumentOutOfRangeException>();
        negMax.Should().Throw<ArgumentOutOfRangeException>();
        negExcluded.Should().Throw<ArgumentOutOfRangeException>();
        highExcluded.Should().Throw<ArgumentOutOfRangeException>();

        var allocator = new WorkerSlotAllocator(maxExclusive: 4);
#pragma warning disable IDISP001 // Dispose created
        Action negPref = () => allocator.TryAcquire(out _, preferredSlot: -1);
        Action highPref = () => allocator.TryAcquire(out _, preferredSlot: 4);
#pragma warning restore IDISP001 // Dispose created
        negPref.Should().Throw<ArgumentOutOfRangeException>();
        highPref.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ParallelContentionAcrossThirtyTwoThreads_NeverAssignsDuplicateActiveSlots()
    {
        var allocator = new WorkerSlotAllocator(maxExclusive: 24, excludedSlots: [12], preferThreadStickySlot: true);
        var activeSlots = new ConcurrentDictionary<int, int>();

        Parallel.For(
            0,
            32,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = 32,
            },
            worker =>
            {
                for (var iter = 0; iter < 250; iter++)
                {
                    if (!allocator.TryAcquire(out var lease, preferredSlot: worker % 24))
                    {
                        continue;
                    }

                    using (lease)
                    {
                        lease.SlotIndex.Should().NotBe(12);
                        activeSlots.TryAdd(lease.SlotIndex, worker).Should().BeTrue();
                        Thread.SpinWait(20);
                        activeSlots.TryRemove(lease.SlotIndex, out _).Should().BeTrue();
                    }
                }
            });

        allocator.AvailableCount.Should().Be(allocator.Capacity);
    }
}
