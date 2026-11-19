using System;

namespace AdaskoTheBeAsT.Interop.Unmanaged;

/// <summary>
/// Declarative specification for staging a single non-DLL companion file (such as <c>.ini</c>, <c>.cfg</c>, <c>.mem</c>,
/// <c>.dcp</c>, <c>.mdb</c>, or <c>.manifest</c>) into an <see cref="IsolatedLibrarySet"/> worker directory before <c>DllMain</c> runs.
/// </summary>
public sealed class IsolatedCompanionFileSpecification
{
    /// <summary>
    /// Gets the relative path of the source file inside <see cref="IsolatedLibrarySetOptions.SourceDirectory"/>.
    /// Path traversal segments (<c>..</c>) and rooted paths are rejected.
    /// </summary>
    public required string SourceRelativePath { get; init; }

    /// <summary>
    /// Gets an optional relative destination path inside <see cref="IsolatedLibrarySet.WorkerDirectory"/>.
    /// Defaults to <see cref="SourceRelativePath"/> when <see langword="null"/>.
    /// </summary>
    public string? TargetRelativePath { get; init; }

    /// <summary>
    /// Gets the copy policy controlling whether unchanged files are skipped or overwritten.
    /// Defaults to <see cref="CompanionCopyMode.CopyIfDifferentSizeOrTimestamp"/>.
    /// </summary>
    public CompanionCopyMode CopyMode { get; init; } = CompanionCopyMode.CopyIfDifferentSizeOrTimestamp;

    /// <summary>
    /// Gets an optional byte transform invoked during staging with <c>(sourceBytes, workerDirectory)</c> to produce
    /// worker-customized configuration bytes (for example rewriting paths inside an <c>.ini</c> or <c>.manifest</c> file).
    /// </summary>
    public Func<byte[], string, byte[]>? TransformBytes { get; init; }
}
