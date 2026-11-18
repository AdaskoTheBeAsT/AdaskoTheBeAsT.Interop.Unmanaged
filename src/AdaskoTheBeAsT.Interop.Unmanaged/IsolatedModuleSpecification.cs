using System;
using System.Collections.Generic;
using System.IO;

namespace AdaskoTheBeAsT.Interop.Unmanaged;

/// <summary>
/// Declarative configuration for a single native PE DLL inside an <see cref="IsolatedLibrarySet"/>.
/// </summary>
public sealed class IsolatedModuleSpecification
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IsolatedModuleSpecification"/> class using the file stem as <see cref="LogicalName"/>.
    /// </summary>
    /// <param name="sourceFileName">Bare ASCII DLL filename inside <see cref="IsolatedLibrarySetOptions.SourceDirectory"/>.</param>
    public IsolatedModuleSpecification(string sourceFileName)
        : this(ExtractDefaultLogicalName(sourceFileName), sourceFileName)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IsolatedModuleSpecification"/> class.
    /// </summary>
    /// <param name="logicalName">Unique logical lookup key within the <see cref="IsolatedLibrarySet"/>.</param>
    /// <param name="sourceFileName">Bare ASCII DLL filename inside <see cref="IsolatedLibrarySetOptions.SourceDirectory"/>.</param>
    public IsolatedModuleSpecification(
        string logicalName,
        string sourceFileName)
    {
#pragma warning disable CA2249,MA0001
        if (string.IsNullOrWhiteSpace(logicalName)
            || logicalName.IndexOf('\0') >= 0)
        {
            throw new ArgumentException(
                "Logical module name cannot be null, whitespace, or contain NUL.",
                nameof(logicalName));
        }
#pragma warning restore CA2249,MA0001

        WorkerModuleNaming.SplitValidatedDllFileName(sourceFileName, nameof(sourceFileName), out _, out _);
        LogicalName = logicalName;
        SourceFileName = sourceFileName;
    }

    /// <summary>
    /// Gets the logical lookup key used with <see cref="IsolatedLibrarySet.GetLibrary"/>.
    /// </summary>
    public string LogicalName { get; }

    /// <summary>
    /// Gets the bare source DLL filename inside <see cref="IsolatedLibrarySetOptions.SourceDirectory"/>.
    /// </summary>
    public string SourceFileName { get; }

    /// <summary>
    /// Gets an optional explicit bare target DLL filename in the isolated worker directory.
    /// For imported companion modules, the explicit name must not exceed <see cref="SourceFileName"/> length
    /// and must differ from <see cref="SourceFileName"/> so the Windows loader does not unify the module across workers.
    /// </summary>
    public string? ExplicitTargetFileName { get; init; }

    /// <summary>
    /// Gets a value indicating whether this module should receive a worker-scoped filename even when no other module
    /// in the set imports it. Modules imported by another module in the set are always renamed automatically unless
    /// <see cref="ExplicitTargetFileName"/> is supplied.
    /// </summary>
    public bool AlwaysRenameWithWorkerSuffix { get; init; }

    /// <summary>
    /// Gets a value indicating whether delay-load import descriptors (<c>IMAGE_DELAYLOAD_DESCRIPTOR</c>) referencing
    /// other modules in the set should also be rewritten. Defaults to <see langword="false"/> so delay-import rewriting
    /// is never assumed implicitly.
    /// </summary>
    public bool RewriteDelayImports { get; init; }

    /// <summary>
    /// Gets a value indicating whether <c>IMAGE_EXPORT_DIRECTORY.Name</c> (<c>DataDirectory[0]</c>) should be rewritten
    /// from <see cref="SourceFileName"/> to the staged worker target filename when this module carries an export table.
    /// Defaults to <see langword="false"/>.
    /// </summary>
    public bool RewriteExportModuleName { get; init; }

    /// <summary>
    /// Gets a value indicating whether <see cref="PeImageEditor.ClearAuthenticodeAndRecalculateChecksum"/> should be applied
    /// whenever the staged copy is modified. Defaults to <see langword="true"/>.
    /// </summary>
    public bool ClearAuthenticodeAndRecalculateChecksum { get; init; } = true;

    /// <summary>
    /// Gets optional whole <c>IMAGE_IMPORT_DESCRIPTOR</c> module entries to compact out of this module's ordinary import table.
    /// </summary>
    public IReadOnlyList<PeImportModuleRemoval> ImportModuleRemovals { get; init; } = [];

    /// <summary>
    /// Gets optional verified <c>IMAGE_IMPORT_BY_NAME</c> symbol rewrites to apply to this module's private copy.
    /// </summary>
    public IReadOnlyList<PeImportSymbolRewrite> ImportSymbolRewrites { get; init; } = [];

    /// <summary>
    /// Gets optional verified RVA byte or dynamic-load literal patches (for example, verified <c>LoadLibrary</c> literals)
    /// to apply to this module's private copy.
    /// </summary>
    public IReadOnlyList<PeVerifiedBytePatch> VerifiedBytePatches { get; init; } = [];

    private static string ExtractDefaultLogicalName(string sourceFileName)
    {
        WorkerModuleNaming.SplitValidatedDllFileName(sourceFileName, nameof(sourceFileName), out var stem, out _);
        return Path.GetFileName(stem);
    }
}
