using System;

namespace AdaskoTheBeAsT.Interop.Unmanaged;

/// <summary>
/// Factory methods for creating <see cref="PinnedLibraryCache{TKey, THandles}"/> instances.
/// </summary>
public static class PinnedLibraryCache
{
    /// <summary>
    /// Creates a case-insensitive string-keyed <see cref="PinnedLibraryCache{TKey, THandles}"/> using <see cref="StringComparer.OrdinalIgnoreCase"/>.
    /// </summary>
    /// <typeparam name="THandles">Reference type holding process-pinned native module handles and bound delegates.</typeparam>
    /// <returns>A new case-insensitive string-keyed <see cref="PinnedLibraryCache{TKey, THandles}"/>.</returns>
    public static PinnedLibraryCache<string, THandles> CreateOrdinalIgnoreCase<THandles>()
        where THandles : class
    {
        return new PinnedLibraryCache<string, THandles>(StringComparer.OrdinalIgnoreCase);
    }
}
