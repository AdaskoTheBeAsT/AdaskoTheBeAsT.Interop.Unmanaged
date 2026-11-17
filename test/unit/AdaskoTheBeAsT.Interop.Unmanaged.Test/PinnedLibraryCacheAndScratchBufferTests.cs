using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Unmanaged.Test;

#pragma warning disable SA1121

public class PinnedLibraryCacheAndScratchBufferTests
{
    [Fact]
    public async Task PinnedLibraryCache_SingleFlightUnderSixteenThreadContention_AndParallelDistinctKeysAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var cache = PinnedLibraryCache.CreateOrdinalIgnoreCase<DummyHandles>();
        cache.Count.Should().Be(0);
        cache.TryGet("worker-a", out _).Should().BeFalse();

        var invocationCount = 0;
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var results = new DummyHandles[16];

        var tasks = new Task[16];
        for (var i = 0; i < tasks.Length; i++)
        {
            var idx = i;
            var keyVariant = (idx & 1) == 0 ? "Worker-A" : "worker-a";
            tasks[idx] = Task.Run(
                () =>
                {
                    results[idx] = cache.GetOrLoad(keyVariant, k =>
                    {
                        Interlocked.Increment(ref invocationCount);
                        entered.TrySetResult(true);
#pragma warning disable VSTHRD002
                        release.Task.GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
                        return new DummyHandles(k);
                    });
                },
                ct);
        }

        await entered.Task;
        release.TrySetResult(true);
        await Task.WhenAll(tasks);

        invocationCount.Should().Be(1);
        cache.Count.Should().Be(1);
        for (var i = 1; i < results.Length; i++)
        {
            results[i].Should().BeSameAs(results[0]);
        }

        var second = cache.GetOrLoad("worker-b", k => new DummyHandles(k));
        second.Should().NotBeSameAs(results[0]);
        cache.Count.Should().Be(2);
        cache.TryGet("WORKER-A", out var fetched).Should().BeTrue();
        fetched.Should().BeSameAs(results[0]);
    }

    [Fact]
    public void PinnedLibraryCache_EvictsFaultedAndNullEntries_AllowingSubsequentRetryToSucceed()
    {
        var cache = new PinnedLibraryCache<string, DummyHandles>(StringComparer.Ordinal);
        var attempts = 0;

        Action firstFail = () => cache.GetOrLoad("flaky", _ =>
        {
            attempts++;
            throw new IOException("Transient staging error.");
        });
        firstFail.Should().Throw<IOException>();
        cache.Count.Should().Be(0);
        cache.TryGet("flaky", out _).Should().BeFalse();

        var recovered = cache.GetOrLoad("flaky", key =>
        {
            attempts++;
            return new DummyHandles(key);
        });
        attempts.Should().Be(2);
        recovered.Tag.Should().Be("flaky");
        cache.Count.Should().Be(1);

        Action nullReturn = () => cache.GetOrLoad("bad-null", _ => null!);
        nullReturn.Should().Throw<InvalidOperationException>();
        cache.TryGet("bad-null", out _).Should().BeFalse();

        Action nullKey = () => cache.GetOrLoad(null!, k => new DummyHandles(k));
        Action nullLoader = () => cache.GetOrLoad("k", null!);
        nullKey.Should().Throw<ArgumentNullException>();
        nullLoader.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    [SuppressMessage("IDisposableAnalyzers", "IDISP001", Justification = "PointerLease is explicitly disposed twice in finally to verify idempotence.")]
    [SuppressMessage("IDisposableAnalyzers", "IDISP004", Justification = "Post-dispose AcquirePointerLease throws before returning a lease.")]
    [SuppressMessage("IDisposableAnalyzers", "IDISP016", Justification = "Verifies ObjectDisposedException after Dispose.")]
    [SuppressMessage("IDisposableAnalyzers", "IDISP017", Justification = "Explicit Dispose tests post-dispose guard.")]
    public void UnmanagedScratchBuffer_ZeroInitializesClearsSlicesAndRetainsPointerLeaseIdempotently()
    {
        Action zeroLen = () =>
        {
            using var bad = new UnmanagedScratchBuffer(0);
        };
        zeroLen.Should().Throw<ArgumentOutOfRangeException>();

        var buffer = new UnmanagedScratchBuffer(32, zeroMemory: true);
        buffer.ByteLength.Should().Be(32);

        var lease = buffer.AcquirePointerLease();
        try
        {
            lease.Pointer.Should().NotBe(IntPtr.Zero);
            lease.ByteLength.Should().Be(32);
            for (var i = 0; i < 32; i++)
            {
                Marshal.ReadByte(lease.Pointer, i).Should().Be(0);
                Marshal.WriteByte(lease.Pointer, i, (byte)(i + 1));
            }

            buffer.Clear(byteOffset: 4, byteCount: 8);
            Marshal.ReadByte(lease.Pointer, 3).Should().Be(4);
            for (var i = 4; i < 12; i++)
            {
                Marshal.ReadByte(lease.Pointer, i).Should().Be(0);
            }

            Marshal.ReadByte(lease.Pointer, 12).Should().Be(13);

            buffer.Clear();
            for (var i = 0; i < 32; i++)
            {
                Marshal.ReadByte(lease.Pointer, i).Should().Be(0);
            }

            Action badSlice = () => buffer.Clear(byteOffset: 28, byteCount: 8);
            badSlice.Should().Throw<ArgumentOutOfRangeException>();
        }
        finally
        {
            lease.Dispose();
            lease.Dispose();
        }

        buffer.Dispose();
        Action clearAfterDispose = () => buffer.Clear();
        Action leaseAfterDispose = () => _ = buffer.AcquirePointerLease();
        clearAfterDispose.Should().Throw<ObjectDisposedException>();
        leaseAfterDispose.Should().Throw<ObjectDisposedException>();
    }

    private sealed class DummyHandles
    {
        public DummyHandles(string tag)
        {
            Tag = tag;
        }

        public string Tag { get; }
    }
}
