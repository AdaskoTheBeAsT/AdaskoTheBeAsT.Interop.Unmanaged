# Controlled native fixture

Requires CMake 3.20+ and MSVC, Clang, or GCC. Unit-test builds run CMake automatically.
Each TFM/architecture has its own build directory, avoiding parallel build races.
Libraries are copied to `native/` beside the test executable and then to unique
Unicode temporary directories for each native test.

Standalone example:

```powershell
cmake -S test/native -B artifacts/native -DCMAKE_BUILD_TYPE=Release
cmake --build artifacts/native --config Release
```

On Windows pass `-A Win32` for x86 or `-A x64` for x64 to the configure command.
MSVC's multi-configuration generator ignores `CMAKE_BUILD_TYPE`; use `--config`.
Do not reuse a CMake build directory across architectures.

`fixture` exports deterministic integer, pointer, struct, ref, callback,
floating-point control/status (Windows MSVC), and blocking operations. Its static
counter proves unload/reload by resetting. `fixture_dependent` links
`fixture_dependency`; loader-relative rpaths on Unix keep dependency tests
independent of machine search paths. Windows x86 explicitly aliases the stdcall
export to its portable undecorated name.

`isoparent`, `isochild`, `isoleaf0`, and `isodelay` (delay-loaded on Windows MSVC)
form a stateful multi-library dependency graph with mutable `.data`/`.bss` counters,
redirectable import symbols, patchable ASCII, UTF-16LE, and length-prefixed Pascal
`ShortString` literals, and optional `DLL_PROCESS_DETACH` unload-order recording
for `PeImageEditor` and `IsolatedLibrarySet` integration tests.

On Windows, `isocompact` and `isounused` exercise whole-descriptor
`PeImageEditor.RemoveImportModule` compaction; `isohelper` provides an unmanaged
PE executable (`IMAGE_FILE_DLL` unset) for `PeImageValidationOptions.ForExecutable`
and `SetLargeAddressAware`; and `isohook` + `isohookdep` (`iso_hook_dep.def` with
named export and `@7 NONAME` ordinal export) exercise `LoadedModuleMemory` runtime
IAT redirection, `.data`/`.bss` scratch clearing, `.rdata` constant patching, and
pre-`DllMain(DLL_PROCESS_ATTACH)` companion file staging (`iso_boot.cfg`).

`fixture_unregister` prevents new callbacks and waits for active calls to finish.
Registration/unregistration must be serialized, and unregister must not run from
inside a callback. Blocking/concurrent callback shutdown is exercised in the
separate `NativeSmoke` process with a timeout. Failed shutdown terminates that
process rather than unloading a module with native work still active.
No test intentionally calls an unloaded export.

Native binaries are test outputs only and are rejected by package-content checks.
