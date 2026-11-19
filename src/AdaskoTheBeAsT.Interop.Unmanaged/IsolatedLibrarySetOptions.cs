using System.Collections.Generic;

namespace AdaskoTheBeAsT.Interop.Unmanaged;

/// <summary>
/// Configures staging, PE import rewriting, namespace/generation scoping, thread affinity, and cleanup for an <see cref="IsolatedLibrarySet"/>.
/// </summary>
public sealed class IsolatedLibrarySetOptions
{
    /// <summary>
    /// Gets the directory containing the unpatched source PE DLLs. Source files are read-only inputs and are never modified.
    /// </summary>
    public required string SourceDirectory { get; init; }

    /// <summary>
    /// Gets the dedicated root directory under which library-owned per-worker subdirectories are created.
    /// Must not be a filesystem root, system directory, or overlap <see cref="SourceDirectory"/>.
    /// </summary>
    public required string IsolationRootDirectory { get; init; }

    /// <summary>
    /// Gets the zero-based worker slot index (<c>0..99</c>).
    /// </summary>
    public required int WorkerIndex { get; init; }

    /// <summary>
    /// Gets an optional 1–4 character ASCII alphanumeric namespace tag (<c>[A-Za-z0-9]</c>) distinguishing separate pools
    /// inside the same process so companion module names and worker directories remain unique across pools.
    /// </summary>
    public string? NamespaceId { get; init; }

    /// <summary>
    /// Gets the zero-based worker recycle generation (<c>0..1295</c>) used to avoid module-name and path collisions
    /// across overlapping worker recycles.
    /// </summary>
    public int Generation { get; init; }

    /// <summary>
    /// Gets a value indicating whether <see cref="IsolatedLibrarySet.Dispose"/> deletes the worker directory on a best-effort basis
    /// after unloading all modules. Defaults to <see langword="true"/>.
    /// </summary>
    public bool DeleteDirectoryOnDispose { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether lookups and <see cref="IsolatedLibrarySet.Dispose"/> must run on the same managed thread
    /// that created and loaded the <see cref="IsolatedLibrarySet"/> (for native runtimes with first-thread TLS affinity).
    /// Defaults to <see langword="false"/>.
    /// </summary>
    public bool EnforceOwningThreadAffinity { get; init; }

    /// <summary>
    /// Gets the Windows <see cref="LoadLibraryFlags"/> passed when loading staged modules by absolute path.
    /// Defaults to <see cref="LoadLibraryFlags.LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR"/> combined with
    /// <see cref="LoadLibraryFlags.LOAD_LIBRARY_SEARCH_DEFAULT_DIRS"/> without mutating the process current directory or DLL search path.
    /// </summary>
    public LoadLibraryFlags LoadFlags { get; init; } =
        LoadLibraryFlags.LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LoadLibraryFlags.LOAD_LIBRARY_SEARCH_DEFAULT_DIRS;

    /// <summary>
    /// Gets the modules belonging to this isolated set. Transitive intra-set dependencies are ordered topologically automatically.
    /// </summary>
    public IReadOnlyList<IsolatedModuleSpecification> Modules { get; init; } = [];

    /// <summary>
    /// Gets optional non-DLL companion files staged into the worker directory before any native module is loaded.
    /// </summary>
    public IReadOnlyList<IsolatedCompanionFileSpecification> CompanionFiles { get; init; } = [];

    /// <summary>
    /// Gets optional non-DLL companion directory trees staged into the worker directory before any native module is loaded.
    /// </summary>
    public IReadOnlyList<IsolatedCompanionDirectorySpecification> CompanionDirectories { get; init; } = [];

    /// <summary>
    /// Gets an optional maximum allowed character length for the worker directory and every staged module or companion file path
    /// (for example <c>120</c> for legacy Jet 4.0 / Fortran <c>CHARACTER*128</c> consumers, or <c>259</c> for legacy <c>MAX_PATH</c>).
    /// Checked during preflight before files are written or loaded.
    /// </summary>
    public int? MaxWorkerFilePathLength { get; init; }

    /// <summary>
    /// Gets a value indicating whether the worker directory and all staged module/companion file paths must contain only
    /// 7-bit ASCII characters (<c>0x01..0x7F</c>) for ANSI-only native runtimes. Defaults to <see langword="false"/>.
    /// </summary>
    public bool RequireAsciiWorkerPaths { get; init; }

    /// <summary>
    /// Gets a value indicating whether loaded native modules, process-wide companion reservations, and the worker directory
    /// lease should remain pinned for the lifetime of the current process when <see cref="IsolatedLibrarySet.Dispose"/> is called
    /// (for legacy DLLs that spawn unmanaged background threads or broken <c>DLL_PROCESS_DETACH</c> handlers).
    /// Defaults to <see langword="false"/>.
    /// </summary>
    public bool PinModulesForProcessLifetime { get; init; }
}
