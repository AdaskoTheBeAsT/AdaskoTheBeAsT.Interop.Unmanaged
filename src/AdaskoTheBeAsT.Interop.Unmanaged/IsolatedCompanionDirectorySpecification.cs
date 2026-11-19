namespace AdaskoTheBeAsT.Interop.Unmanaged;

/// <summary>
/// Declarative specification for staging a companion directory tree of non-DLL lookup assets into an
/// <see cref="IsolatedLibrarySet"/> worker directory before native modules are loaded.
/// </summary>
public sealed class IsolatedCompanionDirectorySpecification
{
    /// <summary>
    /// Gets the relative directory path inside <see cref="IsolatedLibrarySetOptions.SourceDirectory"/>.
    /// Path traversal segments (<c>..</c>) and rooted paths are rejected.
    /// </summary>
    public required string SourceRelativePath { get; init; }

    /// <summary>
    /// Gets an optional relative destination directory path inside <see cref="IsolatedLibrarySet.WorkerDirectory"/>.
    /// Defaults to <see cref="SourceRelativePath"/> when <see langword="null"/>.
    /// </summary>
    public string? TargetRelativePath { get; init; }

    /// <summary>
    /// Gets the filename search pattern matched inside <see cref="SourceRelativePath"/>. Defaults to <c>"*"</c>.
    /// </summary>
    public string SearchPattern { get; init; } = "*";

    /// <summary>
    /// Gets a value indicating whether subdirectories under <see cref="SourceRelativePath"/> are staged recursively.
    /// Defaults to <see langword="true"/>.
    /// </summary>
    public bool Recursive { get; init; } = true;

    /// <summary>
    /// Gets the copy policy controlling whether unchanged files are skipped or overwritten.
    /// Defaults to <see cref="CompanionCopyMode.CopyIfDifferentSizeOrTimestamp"/>.
    /// </summary>
    public CompanionCopyMode CopyMode { get; init; } = CompanionCopyMode.CopyIfDifferentSizeOrTimestamp;
}
