# ADR 0008: Pre-DllMain companion file staging, path-constraint preflight, and process-lifetime module pinning

- Status: Accepted
- Date: 2026-11-17
- Deciders: @AdaskoTheBeAsT
- Release: v5.0.0

## Context

`IsolatedLibrarySet` ([ADR 0004](0004-isolated-native-library-sets-and-directory-leases.md)) isolated native DLL dependency graphs per worker, but production legacy engines introduced three tight lifecycle coupling constraints around the worker directory and `LoadLibraryExW`:

1. **Companion files and subdirectories read during `DllMain(DLL_PROCESS_ATTACH)`:** Many Fortran, Delphi, VB6, and C++ calculation DLLs open adjacent `.INI`, `.DAT`, `.MDB`, `.MANIFEST`, or `Data\...` files relative to their own module directory *inside `DllMain`* during `LoadLibraryExW`. Copying companion assets after `IsolatedLibrarySet.Create(...)` returns is already too late, and duplicating staging logic outside `IsolatedLibrarySet` loses transactional rollback, reparse-point checks, and collision validation against staged DLL filenames.
2. **Legacy ANSI `MAX_PATH` and fixed-buffer path ceilings:** Legacy 32-bit runtimes using `GetModuleFileNameA`, fixed `char[128]` / `char[260]` buffers, or Borland/Fortran ANSI file I/O silently truncate or corrupt stack buffers when the resolved worker file path exceeds a strict character budget or contains non-ASCII characters (for example localized Windows `%TEMP%` user profile folders).
3. **Process-lifetime pinning for unload-unsafe runtimes:** Certain native runtimes (`msvbvm60.dll` class registration, legacy C++/Delphi global destructors, or background Winsock/multimedia threads) crash with access violations during `DLL_PROCESS_DETACH` if `FreeLibrary` is ever called before process exit. Simply leaking handles or releasing `.unmanaged-isolation.lease` on `Dispose()` would either allow `SweepStaleWorkerDirectories` to delete backing files of a still-mapped DLL or let a second `IsolatedLibrarySet` reuse and overwrite the active worker slot in the same process.

## Decision

1. **Stage `CompanionFiles` and `CompanionDirectories` inside `IsolatedLibrarySet.Create` *before* loading any module:**
   - Add `CompanionCopyMode` (`CopyIfChanged`, `AlwaysOverwrite`, `CopyIfMissing`), `IsolatedCompanionFileSpecification` (`SourceRelativePath`, `TargetRelativePath`, `Optional`, `CopyMode`, `ClearReadOnlyAttribute`, `TransformBytes`), and `IsolatedCompanionDirectorySpecification` (`SourceRelativeDirectory`, `TargetRelativeDirectory`, `SearchPattern`, `Recursive`, `Optional`, `CopyMode`, `ClearReadOnlyAttribute`) on `IsolatedLibrarySetOptions`.
   - Validate all relative paths upfront before creating files: reject rooted paths, drive prefixes, `..` parent traversal segments, reparse points / symlinks / junctions anywhere in the source tree or staged target tree, duplicate target paths, reserved `.unmanaged-isolation.*` metadata filenames, and collisions with any staged DLL target filename.
   - Stage all companion files and recursive companion directory trees *after* acquiring `.unmanaged-isolation.lease` and publishing patched DLLs, and *strictly before* the first `LoadLibraryExW` call so `DllMain(DLL_PROCESS_ATTACH)` observes the complete, transformed worker filesystem.
   - Support atomic temporary-file-and-replace writes, optional `TransformBytes` in-memory customization (always evaluated and compared byte-for-byte under `CopyIfChanged`), and `ClearReadOnlyAttribute` so read-only source templates (`.MDB` / `.INI` checked out from read-only shares) become writable in the worker directory and clean up cleanly on rollback or stale-directory sweeps.
2. **Enforce `MaxWorkerFilePathLength` and `RequireAsciiWorkerPaths` during preflight:**
   - Add `MaxWorkerFilePathLength` (for example `240`) and `RequireAsciiWorkerPaths` on `IsolatedLibrarySetOptions`, checked across `WorkerDirectory`, every staged module path, and every resolved companion target path *before* writing files or calling `LoadLibraryExW`. Violations fail fast with descriptive `ArgumentException` messages naming the offending path and character length.
3. **Support `PinModulesForProcessLifetime` safely across `Dispose()` and `SweepStaleWorkerDirectories`:**
   - When `PinModulesForProcessLifetime = true` on `IsolatedLibrarySetOptions`, `IsolatedLibrarySet.Dispose()` marks the wrapper disposed (subsequent `GetLibrary` / indexer calls throw `ObjectDisposedException`), while intentionally transferring the loaded `UnmanagedLibrary` handles, the exclusive `.unmanaged-isolation.lease` `FileStream`, and the process-wide `ActiveWorkerDirectories` / `ActiveCompanionNames` reservations into an internal process-lifetime retention bag.
   - As a result, `FreeLibrary` is never invoked on unload-unsafe modules, `SweepStaleWorkerDirectories` continues to observe the held OS lease and skips the directory, and accidental duplicate creation of the same worker directory or companion DLL name in the live process still fails fast with `InvalidOperationException`.

## Consequences

- Positive: DLLs that read `.INI`, `.CFG`, `.DAT`, or SxS assets during `DllMain(DLL_PROCESS_ATTACH)` initialize deterministically inside transactional worker directories, with full rollback on staging or load failure.
- Positive: Path-length and ASCII-only constraints fail fast during preflight instead of causing silent native buffer truncation inside legacy ANSI runtimes, and unload-unsafe runtimes remain safely mapped and lease-protected until OS process termination.
- Trade-off: Worker slots created with `PinModulesForProcessLifetime = true` retain virtual address space, file handles, and companion-name reservations for the remainder of the process lifetime; callers recycling pinned engines beyond bounded slot counts should recycle the worker process rather than creating unbounded generations in-process.
