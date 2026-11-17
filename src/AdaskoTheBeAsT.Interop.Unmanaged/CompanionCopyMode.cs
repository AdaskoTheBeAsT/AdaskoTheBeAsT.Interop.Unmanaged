namespace AdaskoTheBeAsT.Interop.Unmanaged;

/// <summary>
/// Controls when companion non-DLL assets are written into an <see cref="IsolatedLibrarySet"/> worker directory.
/// </summary>
public enum CompanionCopyMode
{
    /// <summary>
    /// Skips copying an untransformed companion file when the destination file already exists with identical byte length
    /// and UTC last-write timestamp (or identical byte content when <see cref="IsolatedCompanionFileSpecification.TransformBytes"/> is supplied).
    /// </summary>
    CopyIfDifferentSizeOrTimestamp = 0,

    /// <summary>
    /// Atomically replaces the staged companion file on every <see cref="IsolatedLibrarySet.Create"/> call.
    /// </summary>
    OverwriteAlways = 1,
}
