# ADR 0006: PE export-name rewrites, import-descriptor compaction, verified string patches, and LargeAddressAware apphost stamping

- Status: Accepted
- Date: 2026-11-17
- Deciders: @AdaskoTheBeAsT
- Release: v5.0.0

## Context

`PeImageEditor` (introduced in [ADR 0003](0003-verified-pe-image-editor.md)) handled import module rewrites, delay-load module rewrites, import-by-name symbol rewrites, and raw `PeVerifiedBytePatch` slices on unmanaged DLLs. Real-world legacy calculation engines on Windows exposed four additional offline PE editing requirements:

1. **Export directory module name (`DataDirectory[0]`):** Multi-DLL C++, Delphi, and Fortran libraries inspect or advertise their internal `IMAGE_EXPORT_DIRECTORY.Name` header string. Without first-class export-header rewriting, callers had to maintain fragile file-offset byte patches whenever a companion DLL was renamed per worker.
2. **Whole `IMAGE_IMPORT_DESCRIPTOR` removal (`DataDirectory[1]`):** Legacy 32-bit and 64-bit native binaries frequently link optional desktop DLLs (`oledlg.dll`, `winmm.dll`, `odbc32.dll`) whose symbols are never reached in headless server workflows, yet Windows `LoadLibraryExW` or process creation fails before user code runs on minimal Windows Server / Nano Server images where those system DLLs do not exist. Redirecting individual imported symbols does not help when the imported DLL itself is absent from the OS image.
3. **Verified ASCII, UTF-16LE, and Borland Delphi `ShortString` patching:** Callers patching embedded `.INI` / `.CFG` / companion-DLL string literals repeatedly hand-built byte arrays for NUL-terminated ASCII, NUL-terminated UTF-16LE, and length-prefixed Pascal `ShortString` (`[lengthByte, ascii...]`) slots, including base images whose fixed-length `ShortString` value had already been customized in an earlier deployment.
4. **Executable validation (`IMAGE_FILE_DLL` unset) and `IMAGE_FILE_LARGE_ADDRESS_AWARE` (`0x0020`):** Out-of-process 32-bit worker hosts (`win-x86` `.NET` apphosts and helper `.exe` tools) default to a 2 GB virtual-address ceiling unless COFF `Characteristics` carries `0x0020`, causing address-space fragmentation failures under heavy native heap/DLL loads. Meanwhile, default `PeImageValidationOptions` intentionally rejects non-DLL images (`RequireDll = true`), and saving a mutated signed PE without explicitly stripping invalidated Authenticode metadata leaves a broken signature directory.

## Decision

1. **Extend `PeImageEditor` and `PeHeaderParser` with export-directory, header-metadata, and bidirectional offset translation primitives:**
   - Expose `Characteristics`, `TimeDateStamp`, `SizeOfImage`, and `GetExportModuleName()` on `PeImageEditor`, plus `FileOffsetToRva(int fileOffset)` verifying `0 <= fileOffset < imageLength` (`ArgumentOutOfRangeException` outside image bounds) and resolving strictly to a unique file-backed section RVA (`BadImageFormatException` for headers, overlays, gaps, or ambiguous overlapping raw sections).
   - Add `RewriteExportModuleName(originalModuleName, replacementModuleName)` (and `IsolatedModuleSpecification.RewriteExportModuleName` for `IsolatedLibrarySet` integration), validating `DataDirectory[0]`, enforcing `PeNameValidator` ASCII filename rules and `replacement.Length <= original.Length`, accepting either `originalModuleName` or zero-padded `replacementModuleName` case-insensitively for idempotence, and NUL-padding trailing bytes through the original terminator.
2. **Add witness-verified `RemoveImportModule(PeImportModuleRemoval)` descriptor compaction:**
   - Walk bounded 20-byte `IMAGE_IMPORT_DESCRIPTOR` entries in `DataDirectory[1]` until the all-zero terminator, rejecting truncated or section-crossing descriptor tables with `BadImageFormatException`.
   - Remove all descriptors matching `PeImportModuleRemoval.ModuleName` (ASCII case-insensitive), compact subsequent descriptors left contiguously, zero-fill vacated trailing 20-byte slots, and shrink `DataDirectory[1].Size` when appropriate while leaving surviving ILT/IAT thunks untouched at their existing RVAs.
   - Require `WitnessRemainingModuleName` (verified to differ from `ModuleName` and to remain present in the import table) for idempotent re-application when `ModuleName` has already been removed; otherwise throw `BadImageFormatException` so a misspelled `ModuleName` never silently succeeds as a no-op.
   - Integrate `IsolatedModuleSpecification.ImportModuleRemovals` into `IsolatedLibrarySet` preflight *before* import symbol and module-name rewrites.
3. **Provide verified string-patching helpers (`ReplaceVerifiedAsciiStringAtRva`, `ReplaceVerifiedUtf16StringAtRva`, `ReplaceVerifiedShortStringAtRva`):**
   - Reject embedded NULs, non-ASCII characters (for ASCII/`ShortString`), and `replacement.Length > expectedOriginal.Length`.
   - Verify NUL terminators when `requireNullTerminator = true`, zero-fill unused trailing character units, and accept either the exact original string or the zero-padded replacement string for idempotence.
   - For Delphi/Pascal `ShortString` slots (`1..255` printable ASCII bytes `0x20..0x7E` prefixed by length byte), optionally accept any existing printable-ASCII `ShortString` of exact `expectedOriginal.Length` when `allowAnyPrintableAsciiOfSameLength = true`.
4. **Support executable PE validation, `SetLargeAddressAware`, Authenticode mutation guards, and opt-in `buildTransitive` `win-x86` apphost stamping:**
   - Add `PeImageValidationOptions.ForExecutable(expectedMachine, expectedFileLength)` (`RequireDll = false`, `RequireUnmanagedImage = true`).
   - Add `PeImageEditor.SetLargeAddressAware(bool enabled = true)` to toggle COFF `Characteristics` bit `0x0020`, and enforce across all mutating `PeImageEditor` operations that `SaveIfChanged` throws `InvalidOperationException` if a signed image (`DataDirectory[4]` non-zero or `FORCE_INTEGRITY` set) was modified without calling `ClearAuthenticodeAndRecalculateChecksum()`.
   - Ship `buildTransitive/AdaskoTheBeAsT.Interop.Unmanaged.targets` inside the NuGet package with opt-in `<EnableLargeAddressAwareAppHost>false</EnableLargeAddressAwareAppHost>` (default `false`). When enabled on `win-x86` (`RuntimeIdentifier == 'win-x86'` or `PlatformTarget == 'x86'`), inline MSBuild C# tasks stamp COFF `0x0020` and recalculate the PE checksum on the generated `.exe` apphost after `_CreateAppHost` and `_CopyOutOfDatePublishedItems` without invoking external `editbin.exe`.

## Consequences

- Positive: Companion export headers, Nano Server dead-import descriptors, ASCII/UTF-16/Delphi `ShortString` slots, and 32-bit `LARGEADDRESSAWARE` flags can all be patched deterministically and idempotently across all five TFMs with zero external NuGet or SDK tool dependencies.
- Positive: Witness-gated `RemoveImportModule` prevents silent configuration typos while preserving safe warm-restart idempotence, and modified Authenticode images cannot be saved accidentally with a stale signature directory.
- Trade-off: `RemoveImportModule` removes load-time import descriptors only; callers remain responsible for ensuring no executed code path invokes thunks belonging to a removed module.
