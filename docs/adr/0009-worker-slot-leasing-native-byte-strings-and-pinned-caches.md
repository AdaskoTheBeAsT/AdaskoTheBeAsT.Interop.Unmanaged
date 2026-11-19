# ADR 0009: Bounded two-digit worker slot leasing, fixed-buffer native byte strings, and pinned library caches

- Status: Accepted
- Date: 2026-11-17
- Deciders: @AdaskoTheBeAsT
- Release: v5.0.0

## Context

Across multiple native calculation engines built on `AdaskoTheBeAsT.Interop.Unmanaged`, four pure-interop building blocks were previously re-implemented in downstream application repositories despite having zero domain, COM, or thread-scheduler dependencies:

1. **Bounded `00..99` worker slot allocation with thread stickiness:** Fixed-length 8.3 PE import rewrites (`WorkerModuleNaming.FormatFixedLengthName("DFORRT.dll", slot)` -> `DFOR00.dll` .. `DFOR99.dll`) require unique integer slot leases in `[0, maxExclusive)` (`maxExclusive <= 100`), optionally reserving specific slots (`excludedSlots`, such as slot `0` for a warmup/diagnostic template) and preferring the same slot previously leased on the current OS/STA worker thread to maximize filesystem and CPU cache locality without cross-allocator static interference.
2. **Fixed-width byte-buffer string marshaling (C/PowerBASIC NUL-padded, Fortran space-padded, Borland Delphi `ShortString`):** Legacy native struct fields and common-block buffers frequently use fixed-size byte arrays encoded as NUL-terminated/zero-padded ASCII or Latin-1 (`ISO-8859-1`), Fortran blank-padded (`0x20`) `CHARACTER*N` fields without NUL terminators, or Pascal length-prefixed `ShortString[N]` (`[lengthByte, chars...]`). Off-by-one terminator bugs, leftover garbage bytes past the terminator, or accidental UTF-8 multi-byte expansion silently corrupt adjacent struct fields.
3. **Single-flight process-lifetime handle caches (`PinnedLibraryCache`):** Engines that cannot safely call `FreeLibrary` cache loaded handle bundles keyed by staged DLL path or worker slot (`OrdinalIgnoreCase` on Windows). Naive `ConcurrentDictionary.GetOrAdd` invokes the loader factory concurrently under contention (racing file staging or duplicate `LoadLibraryExW` initialization), while naive `ConcurrentDictionary<TKey, Lazy<THandles>>` permanently caches transient load exceptions so all subsequent callers rethrow a poisoned `Lazy<T>` forever.
4. **Zeroed unmanaged scratch buffers with SafeBuffer lifetime guarding (`UnmanagedScratchBuffer`):** Reusable native sessions pass scratch byte arrays (`256 B`–`64 KB`) as raw `IntPtr` arguments to native routines. Raw `Marshal.AllocHGlobal` pointers lack bounds metadata, leak easily on exception paths, and risk use-after-free unless protected by `SafeBuffer.AcquirePointer` / `ReleasePointer` reference counting.

## Decision

1. **Provide `WorkerSlotAllocator` and idempotent `WorkerSlotLease`:**
   - Validate `1 <= maxExclusive <= 100` and optional `excludedSlots` (ensuring at least one allocatable slot remains).
   - Maintain per-instance slot state under an internal lock with optional thread-sticky preference (`preferThreadStickySlot = true`) backed by a `[ThreadStatic] ConditionalWeakTable<WorkerSlotAllocator, StrongBox<int>>` so stickiness is scoped per allocator instance and per thread without rooting disposed/collected allocators.
   - Implement `Acquire()`, `TryAcquire(out WorkerSlotLease?)`, `AvailableCount`, `ActiveCount`, and idempotent `WorkerSlotLease.Dispose()` (`Interlocked.Exchange` one-shot guard) so double-dispose can never release another thread's re-acquired slot.
2. **Provide static, zero-dependency `NativeByteString` encoders and decoders:**
   - Expose `WriteNullTerminatedAscii`, `WriteNullTerminatedLatin1`, `ReadNullTerminatedAscii`, `ReadNullTerminatedLatin1`, `WriteSpacePaddedAscii`, `ReadSpacePaddedAscii`, `WriteShortStringAscii`, and `ReadShortStringAscii`, with `Span<byte>` / `ReadOnlySpan<byte>` overloads on `net10.0` / `net11.0` (`#if NET5_0_OR_GREATER`) and `byte[], int offset, int count` overloads across all five TFMs (`net11.0`, `net10.0`, `net481`, `net48`, `net472`).
   - Enforce strict bounds and character-range validation: reject embedded NULs (`0x00`), reject `> 0x7F` for ASCII and `> 0xFF` for Latin-1, require room for the trailing NUL (`maxLength - 1`) or `ShortString` length prefix (`min(capacity - 1, 255)`), and deterministically zero-pad (or `0x20`-pad for Fortran) every remaining byte in the fixed field so stale buffer data never leaks into native structs.
3. **Provide thread-safe, single-flight, fault-evicting `PinnedLibraryCache<TKey, THandles>` and `PinnedLibraryCache` factory:**
   - Back `GetOrLoad(key, loader)` with `ConcurrentDictionary<TKey, Lazy<Entry>>` (`LazyThreadSafetyMode.ExecutionAndPublication`) so concurrent callers for the same key coalesce onto a single `loader(key)` execution while distinct keys load in parallel, and wrap `THandles` inside a non-generic internal `Entry` reference so `THandles` carries no `DynamicallyAccessedMembers` trim/AOT warnings (`IL2091`).
   - On `loader` exception or `null` return, atomically evict *only* the faulted `Lazy<Entry>` instance (`ICollection<KeyValuePair<...>>.Remove` comparing exact `KeyValuePair` identity across all five TFMs) before propagating the exception (`InvalidOperationException` on `null`), allowing subsequent retries for that key to succeed cleanly.
   - Keep `PinnedLibraryCache` intentionally non-`IDisposable` to match process-lifetime handle semantics (`Count` counts only materialized entries; `TryGet` returns `true` only after `IsValueCreated` succeeds without triggering or awaiting an in-flight load).
4. **Provide `UnmanagedScratchBuffer` backed by `SafeBuffer` and ref-counted `PointerLease`:**
   - Allocate unmanaged heap memory (`Marshal.AllocHGlobal`) wrapped in an internal `SafeBuffer` subclass (`Initialize((ulong)byteLength)`), zero-initializing by default (`zeroMemory = true`), exposing `ByteLength`, bounds-checked `Clear()` and `Clear(offset, length)`, `AsSpan()` (`#if NET5_0_OR_GREATER`), and `AcquirePointerLease()` returning an idempotent `PointerLease` struct backed by `SafeBuffer.AcquirePointer` / `ReleasePointer`.

## Consequences

- Positive: Downstream engines share audited, trim/NativeAOT-clean implementations of `00..99` worker slot leasing, fixed-field ASCII/Latin-1/Fortran/Delphi string marshaling, single-flight fault-evicting pinned handle caches, and ref-counted unmanaged scratch buffers across `net11.0`, `net10.0`, `net481`, `net48`, and `net472` without adding external NuGet packages.
- Trade-off: `PinnedLibraryCache<TKey, THandles>` intentionally retains materialized `THandles` entries for the lifetime of the cache instance; callers managing unloadable per-session handles should dispose `UnmanagedLibrary` / `IsolatedLibrarySet` directly rather than placing them in `PinnedLibraryCache`.
