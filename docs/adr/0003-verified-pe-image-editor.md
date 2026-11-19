# ADR 0003: Zero-dependency verified PE32 and PE32+ image editor

- Status: Accepted
- Date: 2026-11-17
- Deciders: @AdaskoTheBeAsT
- Release: v5.0.0

## Context

Hosting legacy unmanaged calculation DLLs on Windows frequently requires deterministic binary edits on private staged copies before calling `LoadLibraryEx`:

- Rewriting `IMAGE_IMPORT_DESCRIPTOR.Name` or delay-load (`IMAGE_DELAYLOAD_DESCRIPTOR.DllNameRVA`) module strings so each worker copy binds to a worker-private companion runtime DLL (`DFORRT.dll` -> `DFOR00.dll`).
- Rewriting specific `IMAGE_IMPORT_BY_NAME` symbol names when redirecting unused GUI/INI imports unavailable on minimal Windows Server / Nano Server images.
- Applying binary-verified RVA patches (for example, verified `LoadLibraryA` string literals or compiler-specific sharing-table bytes) and recalculating `OptionalHeader.CheckSum` after stripping invalidated Authenticode directory entries on private copies.

Downstream applications previously duplicated ad-hoc `PEReader` and raw byte-offset helpers. Naive byte-scan-and-replace approaches risk corrupting code or relocation bytes when an upstream SDK updates a DLL, and shortened NUL-padded ASCII imports cannot be re-identified by their original string alone on warm restarts unless explicit idempotence metadata is supplied. In addition, `System.Reflection.PortableExecutable` is not available in-box across `.NET Framework 4.7.2–4.8.1` without adding external NuGet dependencies.

## Decision

1. **Implement a self-contained, bounds-checked COFF/PE32/PE32+ parser and editor (`PeImageEditor`):**
   - Parse DOS (`MZ`), PE (`PE\0\0`), COFF file headers, optional headers (`PE32` `0x10B` and `PE32+` `0x20B`), data directories, and section tables directly from a cloned byte buffer (`PeHeaderParser`, `PeHeaderInfo`, `PeSectionSlice`) with checked arithmetic and zero external NuGet dependencies across all five TFMs.
   - Validate by default (`PeImageValidationOptions`) that the image is an unmanaged DLL (`IMAGE_FILE_DLL` set and CLR runtime directory zero) and optionally matches a required `PeMachine` (`I386`, `Amd64`, `Arm64`, or `ForCurrentProcessArchitecture()`).
2. **Require section-backed RVA translation and explicit idempotence metadata; forbid blind byte scanning:**
   - Translate all table and string RVAs strictly through raw-backed section slices (`VirtualAddress`, bounded `SizeOfRawData`, and `PointerToRawData`), rejecting header/unmapped RVAs, truncated descriptors, missing NUL terminators, path separators or non-ASCII characters in PE names (`PeNameValidator`), and ambiguous duplicate matches.
   - Enforce `replacement.Length <= original.Length` on in-place ASCII module and symbol rewrites, zero-padding trailing bytes up to and including the original NUL terminator.
   - Support idempotent re-application on already-staged images only when explicit provenance metadata is provided (`alreadyPatchedModuleName` for shortened module names, `PeImportSymbolRewrite.AlreadyPatchedSymbolName` + `ExpectedPatchedBytes` covering the zero-padded tail, and `PeVerifiedBytePatch` comparing against `ExpectedOriginal` or `Replacement`), throwing `BadImageFormatException` on any unexpected byte layout.
3. **Protect source binaries and stage atomically:**
   - `ClearAuthenticodeAndRecalculateChecksum()` mutates only the in-memory editor buffer (clearing `IMAGE_DIRECTORY_ENTRY_SECURITY` and `IMAGE_DLLCHARACTERISTICS_FORCE_INTEGRITY` and computing the standard 16-bit folding PE checksum).
   - `SaveIfChanged(destinationPath)` rejects paths resolving to the source file (`InvalidOperationException`), skips disk writes when the destination file already matches byte-for-byte, and otherwise writes to a sibling temporary file with `Flush(flushToDisk: true)` before atomically moving/replacing the destination.

## Consequences

- Positive: PE32 (`x86`) and PE32+ (`x64`/`ARM64`) patching works identically on `net11.0`, `net10.0`, `net481`, `net48`, and `net472`, as well as under trimming and NativeAOT, with zero external package dependencies.
- Positive: Upstream DLL layout changes fail fast with descriptive `BadImageFormatException` diagnostics during preflight rather than silently patching the wrong offset or corrupting source binaries.
- Trade-off: `PeImageEditor` intentionally provides verified low-level primitives rather than heuristic signature discovery; callers patching `LoadLibrary` string literals or compiler-specific tables must supply verified RVAs and expected bytes for each supported native binary.
