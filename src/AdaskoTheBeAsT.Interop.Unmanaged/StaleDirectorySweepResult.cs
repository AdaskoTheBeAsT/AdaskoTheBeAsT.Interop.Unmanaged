using System.Collections.Generic;

namespace AdaskoTheBeAsT.Interop.Unmanaged;

/// <summary>
/// Reports the outcome of <see cref="IsolatedLibrarySet.SweepStaleWorkerDirectories"/>.
/// </summary>
public sealed class StaleDirectorySweepResult
{
    internal StaleDirectorySweepResult(
        IReadOnlyList<string> deletedDirectories,
        IReadOnlyList<string> skippedActiveDirectories,
        IReadOnlyList<string> skippedUnknownDirectories,
        IReadOnlyList<string> failedDirectories)
    {
        DeletedDirectories = deletedDirectories;
        SkippedActiveDirectories = skippedActiveDirectories;
        SkippedUnknownDirectories = skippedUnknownDirectories;
        FailedDirectories = failedDirectories;
    }

    /// <summary>
    /// Gets library-owned stale worker directories that were completely deleted.
    /// </summary>
    public IReadOnlyList<string> DeletedDirectories { get; }

    /// <summary>
    /// Gets library-owned worker directories skipped because an active <see cref="IsolatedLibrarySet"/> lease was held.
    /// </summary>
    public IReadOnlyList<string> SkippedActiveDirectories { get; }

    /// <summary>
    /// Gets subdirectories skipped because they lacked a valid ownership marker or were reparse points.
    /// </summary>
    public IReadOnlyList<string> SkippedUnknownDirectories { get; }

    /// <summary>
    /// Gets library-owned stale directories whose cleanup failed partway because one or more files remained locked.
    /// Their ownership markers are preserved for subsequent sweeps.
    /// </summary>
    public IReadOnlyList<string> FailedDirectories { get; }
}
