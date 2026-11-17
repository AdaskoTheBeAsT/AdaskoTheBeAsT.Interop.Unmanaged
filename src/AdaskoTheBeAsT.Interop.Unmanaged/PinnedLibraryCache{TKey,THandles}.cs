using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace AdaskoTheBeAsT.Interop.Unmanaged;

/// <summary>
/// Thread-safe, single-flight, fault-evicting cache for process-pinned native library handle bundles that cannot safely
/// be unloaded via <c>FreeLibrary</c> across worker session recyclings.
/// </summary>
/// <typeparam name="TKey">Non-null lookup key type (for example a normalized source or worker directory path).</typeparam>
/// <typeparam name="THandles">Reference type holding the loaded library handles and bound export delegates.</typeparam>
public sealed class PinnedLibraryCache<TKey, THandles>
    where TKey : notnull
    where THandles : class
{
    private readonly ConcurrentDictionary<TKey, Lazy<Entry>> _entries;

    /// <summary>
    /// Initializes a new instance of the <see cref="PinnedLibraryCache{TKey, THandles}"/> class.
    /// </summary>
    /// <param name="comparer">Optional key equality comparer.</param>
    public PinnedLibraryCache(IEqualityComparer<TKey>? comparer = null)
    {
        _entries = comparer is null
            ? new ConcurrentDictionary<TKey, Lazy<Entry>>()
            : new ConcurrentDictionary<TKey, Lazy<Entry>>(comparer);
    }

    /// <summary>
    /// Gets the number of materialized handle entries currently retained in this cache.
    /// </summary>
    public int Count
    {
        get
        {
            var count = 0;
            foreach (var pair in _entries)
            {
                if (pair.Value.IsValueCreated)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>
    /// Returns the cached <typeparamref name="THandles"/> instance for <paramref name="key"/>, or executes <paramref name="loader"/>
    /// once across concurrent callers for <paramref name="key"/>, evicting the entry if <paramref name="loader"/> throws so later retries can recover.
    /// </summary>
    /// <param name="key">Cache key.</param>
    /// <param name="loader">Single-flight factory invoked to load and initialize handles when <paramref name="key"/> is not yet cached.</param>
    /// <returns>The loaded process-pinned <typeparamref name="THandles"/> instance.</returns>
    public THandles GetOrLoad(
        TKey key,
        Func<TKey, THandles> loader)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(key);
#endif
#if NET472_OR_GREATER
        if (key is null)
        {
            throw new ArgumentNullException(nameof(key));
        }
#endif

#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(loader);
#endif
#if NET472_OR_GREATER
        if (loader is null)
        {
            throw new ArgumentNullException(nameof(loader));
        }
#endif

        var candidate = new Lazy<Entry>(
            () =>
            {
                var loaded = loader(key);
                if (loaded is null)
                {
                    throw new InvalidOperationException("PinnedLibraryCache loader callback returned null.");
                }

                return new Entry { Value = loaded };
            },
            LazyThreadSafetyMode.ExecutionAndPublication);

        var winningEntry = _entries.GetOrAdd(key, candidate);
        try
        {
            return winningEntry.Value.Value;
        }
        catch
        {
            EvictFaultedEntry(key, winningEntry);
            throw;
        }
    }

    /// <summary>
    /// Attempts to retrieve an already-materialized <typeparamref name="THandles"/> entry for <paramref name="key"/> without triggering loading.
    /// </summary>
    /// <param name="key">Cache key.</param>
    /// <param name="handles">Receives the cached <typeparamref name="THandles"/> when materialized; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if a materialized entry exists for <paramref name="key"/>; otherwise <see langword="false"/>.</returns>
    public bool TryGet(
        TKey key,
        [NotNullWhen(true)] out THandles? handles)
    {
        if (key is null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        if (_entries.TryGetValue(key, out var lazy) && lazy.IsValueCreated)
        {
            handles = lazy.Value.Value;
            return true;
        }

        handles = null;
        return false;
    }

    private void EvictFaultedEntry(TKey key, Lazy<Entry> faultedEntry)
    {
        var pair = new KeyValuePair<TKey, Lazy<Entry>>(key, faultedEntry);
#if NET10_0_OR_GREATER
        _ = _entries.TryRemove(pair);
#else
        _ = ((ICollection<KeyValuePair<TKey, Lazy<Entry>>>)_entries).Remove(pair);
#endif
    }

    private sealed class Entry
    {
        public THandles Value { get; init; } = null!;
    }
}
