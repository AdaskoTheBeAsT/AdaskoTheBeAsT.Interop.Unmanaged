# ADR 0007: Loaded-module runtime IAT redirection, verified in-memory RVA primitives, and module provenance

- Status: Accepted
- Date: 2026-11-17
- Deciders: @AdaskoTheBeAsT
- Release: v5.0.0

## Context

Offline PE patching via `PeImageEditor` handles static module/symbol renames and constant byte patches on staged copies before `LoadLibraryExW`, but three runtime requirements only arise once a native DLL is mapped into process memory:

1. **Runtime IAT redirection to in-process stubs:** Shimming Win32, registry, shell, or OLE/COM imports inside a loaded legacy DLL (`SHGetFolderPathW`, `RegOpenKeyExA`, `OleCreate`, `CoCreateInstance`) to dynamic managed (`[UnmanagedCallersOnly]` / pinned delegate) or native function pointers requires target addresses known only at runtime inside the current process. Hardcoding raw IAT slot RVAs per binary build breaks whenever an upstream build relinks a DLL.
2. **Verified in-memory patching and reusable-session `.data`/`.bss` scratch clearing:** Long-lived worker sessions that reuse an already-loaded DLL across calculations frequently need to zero leaked mutable `.data`/`.bss` ranges between calls or write runtime pointers into verified RVAs without reloading the DLL. Performing raw `Unsafe.InitBlock` or `VirtualProtect` writes without first verifying the loaded module's `Machine`, `TimeDateStamp`, `SizeOfImage`, and section flags risks corrupting unrelated heap or code pages if a binary version drifts.
3. **Loaded-module path provenance and system export lookup:** When multiple isolated workers or registration-free COM servers load identically named or worker-suffixed DLLs in one process, diagnostics must verify *which* on-disk path backs a given `HMODULE` or native function/vtable pointer (`GetModuleFileNameW`, `GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT)`), and runtime shims routinely need required exports from `System32` DLLs (`kernel32.dll`, `ole32.dll`, `advapi32.dll`) using `LOAD_LIBRARY_SEARCH_SYSTEM32`.

## Decision

1. **Introduce Windows-only `LoadedModuleIdentity` and `LoadedModuleMemory` (`[SupportedOSPlatform("windows")]`):**
   - `LoadedModuleMemory.Inspect(handle | library)` and `VerifyIdentity(...)` reject disposed handles (`ObjectDisposedException`) and data-file / image-resource low-tag mappings (`(raw & 3) != 0`, throwing `InvalidOperationException`), then parse the mapped PE headers in memory (`BaseAddress`, `ushort Machine`, `uint TimeDateStamp`, `uint SizeOfImage`) and verify expected `Machine`, `TimeDateStamp`, and `SizeOfImage` values (`BadImageFormatException` on mismatch).
   - `TryRedirectImportFunction`, `RedirectImportFunction`, and `TryRedirectImportOrdinal` walk mapped `DataDirectory[1]` (`IMAGE_IMPORT_DESCRIPTOR`) entries matching `importedModuleName` (ASCII case-insensitive), require `OriginalFirstThunk != 0` (the unbound import lookup table, throwing `NotSupportedException` if stripped so ambiguous post-bind IAT guesses are never attempted), match either `IMAGE_IMPORT_BY_NAME.Name` (case-sensitive ordinal ASCII) or ordinal thunk entries (`IMAGE_SNAP_BY_ORDINAL32` / `IMAGE_SNAP_BY_ORDINAL64`), temporarily flip the single IAT slot (`4` bytes on `PE32`, `8` bytes on `PE32+`) to `PAGE_READWRITE` via `VirtualProtect` when currently read-only (`PAGE_READONLY` under CFG / `.rdata` IAT merging), write `replacementFunction` atomically (`Volatile.Write`), restore the exact prior page protection, and return `originalFunction` so callers can chain or restore the original target.
   - `WriteVerifiedBytesAtRva` validates `[rva .. rva + length)` against a single mapped section slice, accepts either `expectedOriginal` or `replacement` for idempotence (`BadImageFormatException` otherwise), applies scoped `VirtualProtect` (`PAGE_EXECUTE_READWRITE` when `IMAGE_SCN_MEM_EXECUTE` is set, `PAGE_READWRITE` otherwise) only when the section lacks `IMAGE_SCN_MEM_WRITE`, copies `replacement`, restores page protection, and calls `FlushInstructionCache` when executable pages were touched.
   - `ClearWritableBytesAtRva` restricts zeroing strictly to sections already carrying `IMAGE_SCN_MEM_WRITE` (`.data` / `.bss`), throwing `InvalidOperationException` on read-only or executable sections rather than ever relaxing page protections for scratch resets.
2. **Add module provenance and `System32` export helpers on `UnmanagedLibrary` (`[SupportedOSPlatform("windows")]`):**
   - Add `library.GetLoadedFilePath()` and `UnmanagedLibrary.GetLoadedFilePath(handle)` using dynamically grown `GetModuleFileNameW` buffers (`260` up to `32,768` UTF-16 code units) so `\\?\` long paths and Unicode worker directories never truncate at `MAX_PATH`.
   - Add `library.ContainsAddress(address)` checking `[BaseAddress, BaseAddress + SizeOfImage)` from the mapped PE header.
   - Add `UnmanagedLibrary.TryGetLoadedModuleFromAddress(address, out moduleBaseAddress, out moduleFilePath)` using `GetModuleHandleExW` with `GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT` (`0x00000006`) so probing a function or COM vtable pointer never leaks module reference counts or returns false positives on `IntPtr.Zero` / unmapped heap pointers.
   - Add `UnmanagedLibrary.GetRequiredSystemExport(systemModuleName, exportName)` validating a bare module name via `PeNameValidator` (rejecting paths, drive letters, and directory separators) and resolving from `LOAD_LIBRARY_SEARCH_SYSTEM32` without unloading standard system modules prematurely.

## Consequences

- Positive: Runtime IAT shims work cleanly across both `PE32` (`x86`) and `PE32+` (`x64`/`ARM64`) without hardcoded IAT RVAs, and in-memory RVA writes / `.bss` scratch resets enforce header and section invariants before touching process memory.
- Positive: Provenance helpers allow callers and diagnostics to prove worker isolation at runtime (`ContainsAddress`, `GetLoadedFilePath`, `TryGetLoadedModuleFromAddress`) without manual Win32 P/Invoke boilerplate.
- Trade-off: `LoadedModuleMemory` does not synchronize concurrent native callers; the host must quiesce native execution on the target module while redirecting IAT entries, patching RVAs, or clearing writable scratch state, and must keep replacement callback delegates rooted for as long as the IAT slot points to them.
