namespace AdaskoTheBeAsT.Interop.Unmanaged;

/// <summary>
/// Describes a whole <c>IMAGE_IMPORT_DESCRIPTOR</c> module entry to compact out of a PE image's ordinary import directory table.
/// </summary>
public sealed class PeImportModuleRemoval
{
    /// <summary>
    /// Gets the ASCII module name (case-insensitive) whose <c>IMAGE_IMPORT_DESCRIPTOR</c> entries should be removed.
    /// </summary>
    public required string ModuleName { get; init; }

    /// <summary>
    /// Gets an optional import module name expected to remain present in the ordinary import table when
    /// <see cref="ModuleName"/> has already been removed, enabling idempotent re-application on already-compacted images
    /// without silently ignoring mistyped module names.
    /// </summary>
    public string? WitnessRemainingModuleName { get; init; }
}
