# ADR 0005: Thread-affine FPU and error-mode execution guards and raw export ergonomics

- Status: Accepted
- Date: 2026-11-17
- Deciders: @AdaskoTheBeAsT
- Release: v5.0.0

## Context

Two operational hazards and one ergonomic gap repeatedly surfaced when calling unmanaged DLLs from high-throughput .NET worker threads:

1. **Floating-point control-word corruption (`x87` / `MXCSR`):** Legacy 32-bit Delphi, Borland C++, and Fortran DLLs frequently unmask hardware floating-point exceptions (`#ZE`, `#OE`, `#IE`) inside `DllMain` or exported routines and leave sticky exception flags raised in the FPU/SSE status word. On return to managed code, ordinary IEEE-754 operations (`1.0 / 0.0` or `double.NaN`) assume masked traps and can fault the process (`STATUS_FLOAT_DIVIDE_BY_ZERO` / `STATUS_FLOAT_INVALID_OPERATION`). Conversely, calling `_fpreset()` indiscriminately clobbers the caller's rounding mode (`_MCW_RC`) and x86 precision mode (`_MCW_PC`), and unmasking saved exception bits *before* clearing sticky status flags triggers an immediate hardware trap.
2. **Modal Windows error dialogs vs process-wide `SetErrorMode` races:** Legacy DLLs can block headless services with system critical-error or missing-file dialog boxes unless `SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX | SEM_NOOPENFILEERRORBOX` (`0x8007`) is active. However, mutating process-wide `SetErrorMode` inside scoped calls races across threads (and interacts with `.NET Framework` `clr.dll`, which temporarily mutates process `SetErrorMode` during internal `LoadLibrary` calls).
3. **Raw export lookup boilerplate and module base introspection:** Modern .NET callers binding 15–30 `delegate* unmanaged` function pointers per module repeated `TryGetExport` + `EntryPointNotFoundException` plumbing at every call site, and hosts zeroing leaked `.bss` scratch slots or verifying mapped ranges needed the loaded module's base address without misinterpreting low Windows loader data-file tag bits.

## Decision

1. **Provide thread-affine `NativeExecutionGuard` and `NativeExecutionScope`:**
   - `NativeExecutionGuard.EnterScope(NativeErrorMode threadErrorMode = NativeErrorMode.SuppressErrorDialogs)` captures the current thread's `Environment.CurrentManagedThreadId`, saves the thread's FPU/SSE exception-mask state and Windows thread error mode (`GetThreadErrorMode` / `SetThreadErrorMode`), and masks all FPU/SSE hardware exceptions (`_MCW_EM`) while leaving rounding (`_MCW_RC`) and x86 precision (`_MCW_PC`) untouched.
   - On Windows `x86`, invoke `ucrtbase!__control87_2` so both the x87 FPU control word and SSE2 `MXCSR` exception masks are captured and restored independently; on Windows `x64` and `ARM64`, invoke `ucrtbase!_controlfp_s`. On non-Windows runtimes, `EnterScope` and `ResetFloatingPointState` act as safe thread-affine no-ops.
   - Always call `ucrtbase!_clearfp()` *before* restoring saved control-word masks in `RestoreFloatingPointState()` and `Dispose()` so sticky status bits raised by native code cannot trigger an immediate trap upon mask restoration.
   - Enforce owning-thread affinity on `NativeExecutionScope.RestoreFloatingPointState()` and `Dispose()` (`InvalidOperationException` on cross-thread use) and make `Dispose()` idempotent.
   - Keep process-wide `SetErrorMode` strictly opt-in via `NativeExecutionGuard.SuppressProcessErrorDialogs()`, never as a side effect of `EnterScope`.
2. **Add `GetRequiredExport` and Windows-only `BaseAddress` / `GetBaseAddress` on `UnmanagedLibrary`:**
   - Add `library.GetRequiredExport(functionName)` and `UnmanagedLibrary.GetRequiredExport(safeLibraryHandle, functionName)` backed by `SafeHandle` `DangerousAddRef` / `DangerousRelease` lookup protection, throwing `EntryPointNotFoundException` with both the requested symbol name and module path/handle label when the export is absent.
   - Add Windows-only (`[SupportedOSPlatform("windows")]`) `library.BaseAddress` and `UnmanagedLibrary.GetBaseAddress(safeLibraryHandle)`, masking off the low two Windows loader tag bits (`handle & ~(nint)3`) used by `LOAD_LIBRARY_AS_DATAFILE` / `LOAD_LIBRARY_AS_IMAGE_RESOURCE` mappings and throwing `ObjectDisposedException` when the owner or handle is disposed.
   - Document explicitly that raw export pointers and base addresses remain valid only while the owning `UnmanagedLibrary` or `SafeLibraryHandle` stays undisposed and is protected from concurrent disposal.

## Consequences

- Positive: Managed floating-point code following native `DllMain` or P/Invoke calls is protected from unmasked x87/SSE traps without losing caller rounding or x86 precision settings, and thread-scoped dialog suppression avoids cross-thread `SetErrorMode` races.
- Positive: `delegate* unmanaged` consumers get concise, allocation-free required-export resolution and accurate Windows image base addresses compatible with trimming and NativeAOT.
- Trade-off: `NativeExecutionScope` must be disposed on the thread that entered it (`InvalidOperationException` otherwise), so scopes cannot flow across `await` continuations that switch threads.
