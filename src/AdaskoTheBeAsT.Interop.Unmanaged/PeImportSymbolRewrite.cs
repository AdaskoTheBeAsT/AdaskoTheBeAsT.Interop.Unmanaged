using System;

namespace AdaskoTheBeAsT.Interop.Unmanaged;

/// <summary>
/// Describes a verified same-or-shorter ASCII symbol rewrite inside an ordinary PE import descriptor (<c>IMAGE_IMPORT_BY_NAME</c>).
/// </summary>
public sealed class PeImportSymbolRewrite
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PeImportSymbolRewrite"/> class.
    /// </summary>
    /// <param name="moduleName">Imported module name owning the symbol entry (as recorded in the PE import descriptor).</param>
    /// <param name="originalSymbolName">Expected unpatched ASCII symbol name (also provides slot-length metadata for idempotence).</param>
    /// <param name="replacementSymbolName">Replacement ASCII symbol name whose length does not exceed <paramref name="originalSymbolName"/>.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when any name is null, empty, non-ASCII, or when <paramref name="moduleName"/> contains path characters.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="replacementSymbolName"/> is longer than <paramref name="originalSymbolName"/>.
    /// </exception>
    public PeImportSymbolRewrite(
        string moduleName,
        string originalSymbolName,
        string replacementSymbolName)
    {
        PeNameValidator.ValidateModuleNameArgument(moduleName, nameof(moduleName));
        PeNameValidator.ValidateSymbolNameArgument(originalSymbolName, nameof(originalSymbolName));
        PeNameValidator.ValidateSymbolNameArgument(replacementSymbolName, nameof(replacementSymbolName));
        if (replacementSymbolName.Length > originalSymbolName.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(replacementSymbolName),
                replacementSymbolName.Length,
                $"Replacement symbol '{replacementSymbolName}' cannot exceed original symbol '{originalSymbolName}' length ({originalSymbolName.Length}).");
        }

        ModuleName = moduleName;
        OriginalSymbolName = originalSymbolName;
        ReplacementSymbolName = replacementSymbolName;
    }

    /// <summary>
    /// Gets the imported module name owning the symbol entry.
    /// </summary>
    public string ModuleName { get; }

    /// <summary>
    /// Gets the expected original ASCII symbol name.
    /// </summary>
    public string OriginalSymbolName { get; }

    /// <summary>
    /// Gets the replacement ASCII symbol name.
    /// </summary>
    public string ReplacementSymbolName { get; }
}
