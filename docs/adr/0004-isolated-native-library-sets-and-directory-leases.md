# ADR 0004: Per-worker native DLL graph isolation and lease-based stale directory sweeping

- Status: Accepted
- Date: 2026-11-17
- Deciders: @AdaskoTheBeAsT
- Release: v5.0.0

## Context

Non-thread-safe native calculation engines (legacy C/C++, Delphi, PowerBASIC, and Fortran DLLs) keep mutable calculation state in `.data`/`.bss` sections and often depend on stateful companion DLLs (for example `Engine.dll -> DFORRT.dll` or multi-level `parent -> child -> leaf` graphs):

1. **Windows loader module-name unification:** Copying only `Engine.dll` into per-worker directories gives distinct top-level `HMODULE` handles, but implicit imports to `DFORRT.dll` still resolve to the first `DFORRT.dll` loaded in the process unless each worker's companion DLL carries a process-unique filename (`DFOR00.dll`, `DFOR01.dll`) and dependent import descriptors are rewritten to match.
2. **Multi-pool and recycle-generation collisions:** A bare `workerIndex` (`00..99`) is not unique when a host runs multiple worker pools or overlaps old and new worker generations during pool recycling.
3. **Loader search-path safety and teardown order:** Mutating `Environment.CurrentDirectory` or calling `SetDllDirectory` races across threads, and unloading dependencies before dependents during `DLL_PROCESS_DETACH` risks access violations in `DllMain`. Some legacy runtimes also bind TLS state to the thread that originally loaded the module.
4. **Crash-leftover staging directories:** If a worker process terminates abruptly (`FailFast`, power loss, or kill), staged worker directories remain under the isolation root. Blindly deleting subdirectories at startup risks removing user data or tearing down directories still owned by another live process or pool.

## Decision

1. **Stage and load isolated module graphs deterministically via `IsolatedLibrarySet` and `WorkerModuleNaming`:**
   - Provide `WorkerModuleNaming.FormatFixedLengthName` and `FormatScopedFixedLengthName(originalFileName, namespaceId, workerIndex, generation)` (up to 4 alphanumeric `NamespaceId` characters + compact base-36 `Generation` + 2-digit `WorkerIndex`) to construct deterministic, fixed-length ASCII companion filenames.
   - Track active worker directories and active renamed companion module names in process-wide case-insensitive reservation sets (`ActiveWorkerDirectories` and `ActiveCompanionNames`), failing fast with `InvalidOperationException` before writing files if another live `IsolatedLibrarySet` already owns the same directory or companion name.
2. **Preflight all PE edits in memory and keep patch boundaries explicit:**
   - Validate the dependency DAG (`Dependencies`), topologically sort modules (dependencies before dependents), validate every module's PE architecture against the current process (`PeImageValidationOptions.ForCurrentProcessArchitecture()`), and apply all requested `PeImageEditor` transforms in memory before publishing files to the worker directory.
   - Apply `ImportSymbolRewrites` before `RewriteImportModuleName` so symbol rewrites keyed by the original companion module name succeed even when that companion module is also being renamed for the worker.
   - Keep `RewriteDelayImports` and `VerifiedBytePatches` explicit on `IsolatedModuleSpecification` rather than implying that ordinary import-table rewrites cover delay-load descriptors or dynamic `LoadLibrary` literals.
   - Roll back any loaded handles, staged files, lease handles, and process-wide reservations if preflight, file staging, or `LoadLibraryEx` fails mid-creation.
3. **Use safe per-load search flags, reverse-topological unload, and optional thread affinity:**
   - Load staged modules via `LoadLibraryFlags.LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LoadLibraryFlags.LOAD_LIBRARY_SEARCH_DEFAULT_DIRS` (default) without modifying the process current directory or process-wide DLL search path.
   - Unload libraries in strict reverse-topological order (`dependents` before `dependencies`) on idempotent `Dispose()`, optionally enforcing `EnforceOwningThreadAffinity` on `GetLibrary`, indexer access, and `Dispose()`.
4. **Guard `SweepStaleWorkerDirectories` with both an ownership marker and an exclusive OS file lease:**
   - Write `.unmanaged-isolation.marker` and hold an open `.unmanaged-isolation.lease` file (`FileShare.None`) in each active worker directory for the lifetime of `IsolatedLibrarySet`.
   - In `IsolatedLibrarySet.SweepStaleWorkerDirectories(isolationRootDirectory)`, validate the root path (rejecting filesystem roots and reparse points), inspect only immediate child directories containing `.unmanaged-isolation.marker`, skip reparse points and directories reserved in-process or whose `.unmanaged-isolation.lease` cannot be exclusively acquired, and return structured counts in `StaleDirectorySweepResult`.

## Consequences

- Positive: Multiple workers, pools, and recycle generations can execute stateful multi-DLL native calculation graphs concurrently in one Windows process with verified distinct `HMODULE` addresses (`VerifyDistinctFrom`) and zero shared `.data`/`.bss` state.
- Positive: Startup crash recovery safely cleans orphaned worker directories without risking deletion of unrelated directories or live worker sets in the same or another process.
- Trade-off: `IsolatedLibrarySet` is Windows-only (`[SupportedOSPlatform("windows")]`) because PE import-table rewriting and `LoadLibraryEx` module-name unification rules are specific to the Windows PE loader.
