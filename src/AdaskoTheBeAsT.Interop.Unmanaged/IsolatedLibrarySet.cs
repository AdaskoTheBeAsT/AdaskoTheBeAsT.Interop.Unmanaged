using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace AdaskoTheBeAsT.Interop.Unmanaged;

#pragma warning disable SA1121

/// <summary>
/// Stages, patches, and loads an isolated per-worker graph of native Windows PE DLLs so each worker receives
/// distinct <c>HMODULE</c> instances and private <c>.data</c>/<c>.bss</c> sections across both root and companion DLLs.
/// </summary>
/// <remarks>
/// <para>
/// All requested PE structural checks, collision checks, topological dependency ordering, and import/byte patches are
/// preflighted in memory before any worker directory or file is published.
/// </para>
/// <para>
/// Each staged worker directory contains a library ownership marker (<c>.unmanaged-isolation.marker</c>) and an
/// exclusive file lease (<c>.unmanaged-isolation.lease</c>) held for the lifetime of the <see cref="IsolatedLibrarySet"/>.
/// <see cref="SweepStaleWorkerDirectories"/> deletes only unlocked directories carrying a valid ownership marker and no active lease.
/// </para>
/// </remarks>
public sealed class IsolatedLibrarySet : IDisposable
{
    internal const string OwnershipMarkerFileName = ".unmanaged-isolation.marker";
    internal const string LeaseFileName = ".unmanaged-isolation.lease";
    internal const string OwnershipMarkerHeader = "AdaskoTheBeAsT.Interop.Unmanaged.IsolatedLibrarySet.v1";

    private static readonly ConcurrentDictionary<string, string> _activeCompanionModules =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, byte> _activeWorkerDirectories =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentBag<PinnedSetRetention> _pinnedProcessSets = [];

    private readonly string _workerDirectory;
    private readonly int _owningThreadId;
    private readonly bool _enforceOwningThreadAffinity;
    private readonly bool _deleteDirectoryOnDispose;
    private readonly bool _pinModulesForProcessLifetime;
    private readonly string[] _loadOrder;
    private readonly UnmanagedLibrary[] _orderedLibraries;
    private readonly string[] _reservedCompanionModuleNames;
    private readonly IReadOnlyDictionary<string, UnmanagedLibrary> _librariesByLogicalName;
    private readonly IReadOnlyDictionary<string, string> _stagedPathsByLogicalName;
    private readonly Action<string>? _onModuleUnloaded;
    private FileStream? _leaseStream;
    private int _disposed;

#pragma warning disable S107
    private IsolatedLibrarySet(
        string workerDirectory,
        int owningThreadId,
        bool enforceOwningThreadAffinity,
        bool deleteDirectoryOnDispose,
        bool pinModulesForProcessLifetime,
        string[] loadOrder,
        UnmanagedLibrary[] orderedLibraries,
        string[] reservedCompanionModuleNames,
        IReadOnlyDictionary<string, UnmanagedLibrary> librariesByLogicalName,
        IReadOnlyDictionary<string, string> stagedPathsByLogicalName,
        FileStream leaseStream,
        Action<string>? onModuleUnloaded)
#pragma warning restore S107
    {
        _workerDirectory = workerDirectory;
        _owningThreadId = owningThreadId;
        _enforceOwningThreadAffinity = enforceOwningThreadAffinity;
        _deleteDirectoryOnDispose = deleteDirectoryOnDispose;
        _pinModulesForProcessLifetime = pinModulesForProcessLifetime;
        _loadOrder = loadOrder;
        _orderedLibraries = orderedLibraries;
        _reservedCompanionModuleNames = reservedCompanionModuleNames;
        _librariesByLogicalName = librariesByLogicalName;
        _stagedPathsByLogicalName = stagedPathsByLogicalName;
        _leaseStream = leaseStream;
        _onModuleUnloaded = onModuleUnloaded;
    }

    /// <summary>
    /// Gets the full path of the private worker directory holding the staged DLL copies.
    /// </summary>
#pragma warning disable RCS1085 // Use auto-implemented property
    public string WorkerDirectory => _workerDirectory;
#pragma warning restore RCS1085 // Use auto-implemented property

    /// <summary>
    /// Gets the managed thread ID of the thread that created and loaded this <see cref="IsolatedLibrarySet"/>.
    /// </summary>
#pragma warning disable RCS1085 // Use auto-implemented property
    public int OwningThreadId => _owningThreadId;
#pragma warning restore RCS1085 // Use auto-implemented property

    /// <summary>
    /// Gets the logical module names in deterministic topological load order (dependencies first, dependents last).
    /// </summary>
    public IReadOnlyList<string> LoadOrder => _loadOrder;

    /// <summary>
    /// Gets the loaded <see cref="UnmanagedLibrary"/> instances keyed by logical module name (case-insensitive).
    /// </summary>
#pragma warning disable RCS1085 // Use auto-implemented property
    public IReadOnlyDictionary<string, UnmanagedLibrary> Libraries => _librariesByLogicalName;
#pragma warning restore RCS1085 // Use auto-implemented property

    /// <summary>
    /// Gets the staged worker file paths keyed by logical module name (case-insensitive).
    /// </summary>
#pragma warning disable RCS1085 // Use auto-implemented property
    public IReadOnlyDictionary<string, string> StagedFilePaths => _stagedPathsByLogicalName;
#pragma warning restore RCS1085 // Use auto-implemented property

    /// <summary>
    /// Gets the loaded <see cref="UnmanagedLibrary"/> for <paramref name="logicalName"/>. Ownership remains with this <see cref="IsolatedLibrarySet"/>.
    /// </summary>
    /// <param name="logicalName">Logical module name registered in <see cref="IsolatedLibrarySetOptions.Modules"/>.</param>
    /// <returns>The loaded <see cref="UnmanagedLibrary"/> instance.</returns>
    public UnmanagedLibrary this[string logicalName] => GetLibrary(logicalName);

    /// <summary>
    /// Validates, patches in memory, stages, and loads an isolated per-worker native library set on Windows.
    /// </summary>
    /// <param name="options">Isolation configuration and module specifications.</param>
    /// <returns>A loaded <see cref="IsolatedLibrarySet"/> owning the staged handles and active directory lease.</returns>
    public static IsolatedLibrarySet Create(IsolatedLibrarySetOptions options)
    {
        return CreateCore(options, stagingFaultHook: null, onModuleUnloaded: null);
    }

    /// <summary>
    /// Sweeps <paramref name="isolationRootDirectory"/> and best-effort deletes stale worker subdirectories previously
    /// created by <see cref="IsolatedLibrarySet"/> whose leases are no longer active.
    /// </summary>
    /// <param name="isolationRootDirectory">Dedicated isolation root directory to sweep.</param>
    /// <returns>A <see cref="StaleDirectorySweepResult"/> categorizing deleted, active, unknown, and locked directories.</returns>
    [SuppressMessage("Security", "SCS0018", Justification = "Strictly validates root path, rejects reparse points, and deletes only marker-verified subdirectories.")]
    public static StaleDirectorySweepResult SweepStaleWorkerDirectories(string isolationRootDirectory)
    {
        var fullRoot = ValidateAndNormalizeIsolationRoot(isolationRootDirectory, fullSourceDirectory: null);
        var deleted = new List<string>();
        var skippedActive = new List<string>();
        var skippedUnknown = new List<string>();
        var failed = new List<string>();

        if (!Directory.Exists(fullRoot))
        {
            return new StaleDirectorySweepResult(deleted, skippedActive, skippedUnknown, failed);
        }

        var rootInfo = new DirectoryInfo(fullRoot);
        if (IsReparsePoint(rootInfo.Attributes))
        {
            throw new InvalidOperationException($"Isolation root directory '{fullRoot}' cannot be a reparse point.");
        }

        string[] childDirectories;
        try
        {
            childDirectories = Directory.GetDirectories(fullRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = ex;
            return new StaleDirectorySweepResult(deleted, skippedActive, skippedUnknown, failed);
        }

        Array.Sort(childDirectories, StringComparer.OrdinalIgnoreCase);
        foreach (var child in childDirectories)
        {
            SweepSingleCandidateDirectory(child, fullRoot, deleted, skippedActive, skippedUnknown, failed);
        }

        return new StaleDirectorySweepResult(deleted, skippedActive, skippedUnknown, failed);
    }

    /// <summary>
    /// Retrieves a loaded <see cref="UnmanagedLibrary"/> by its <see cref="IsolatedModuleSpecification.LogicalName"/>.
    /// </summary>
    /// <param name="logicalName">Logical module name registered in <see cref="IsolatedLibrarySetOptions.Modules"/>.</param>
    /// <returns>The loaded <see cref="UnmanagedLibrary"/> instance.</returns>
    public UnmanagedLibrary GetLibrary(string logicalName)
    {
        if (string.IsNullOrWhiteSpace(logicalName))
        {
            throw new ArgumentException("Logical module name cannot be null or whitespace.", nameof(logicalName));
        }

        EnsureOwningThreadIfRequired();
        ThrowIfDisposed();
        for (var i = 0; i < _loadOrder.Length; i++)
        {
            if (string.Equals(_loadOrder[i], logicalName, StringComparison.OrdinalIgnoreCase))
            {
                return _orderedLibraries[i];
            }
        }

        throw new KeyNotFoundException($"No isolated module with logical name '{logicalName}' exists in this set.");
    }

    /// <summary>
    /// Verifies that this <see cref="IsolatedLibrarySet"/> and <paramref name="other"/> use distinct worker directories
    /// and completely disjoint Windows <c>HMODULE</c> base addresses across all loaded libraries.
    /// </summary>
    /// <param name="other">Another live <see cref="IsolatedLibrarySet"/> to compare against.</param>
    public void VerifyDistinctFrom(IsolatedLibrarySet other)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(other);
#endif
#if NET472_OR_GREATER
        if (other is null)
        {
            throw new ArgumentNullException(nameof(other));
        }
#endif

        ThrowIfDisposed();
        other.ThrowIfDisposed();
        if (ReferenceEquals(this, other)
            || string.Equals(_workerDirectory, other._workerDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Isolated library sets share the same worker directory '{_workerDirectory}'.");
        }

        var ownAddresses = new Dictionary<IntPtr, string>();
        foreach (var pair in _librariesByLogicalName)
        {
            ownAddresses[pair.Value.BaseAddress] = pair.Key;
        }

        foreach (var pair in other._librariesByLogicalName)
        {
            var otherAddr = pair.Value.BaseAddress;
            if (ownAddresses.TryGetValue(otherAddr, out var collidingName))
            {
                throw new InvalidOperationException(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Module '{0}' in '{1}' and module '{2}' in '{3}' resolved to the same HMODULE base address 0x{4:X}.",
                        collidingName,
                        _workerDirectory,
                        pair.Key,
                        other._workerDirectory,
                        otherAddr.ToInt64()));
            }
        }
    }

    /// <summary>
    /// Unloads all isolated modules in reverse topological order, releases process and file leases, and optionally
    /// performs best-effort cleanup of the worker directory. Idempotent on repeated calls.
    /// </summary>
    public void Dispose()
    {
        EnsureOwningThreadIfRequired();
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_pinModulesForProcessLifetime)
        {
            var pinnedLease = Interlocked.Exchange(ref _leaseStream, value: null);
            _pinnedProcessSets.Add(new PinnedSetRetention(_orderedLibraries, pinnedLease));
            return;
        }

        try
        {
            for (var i = _orderedLibraries.Length - 1; i >= 0; i--)
            {
                _orderedLibraries[i].Dispose();
                _onModuleUnloaded?.Invoke(_loadOrder[i]);
            }
        }
        finally
        {
            ReleaseReservations(_reservedCompanionModuleNames, _workerDirectory);
            var lease = Interlocked.Exchange(ref _leaseStream, value: null);
            lease?.Dispose();
            if (_deleteDirectoryOnDispose)
            {
                TryDeleteOwnedWorkerDirectoryWithRetry(_workerDirectory);
            }
        }
    }

    internal static IsolatedLibrarySet CreateCore(
        IsolatedLibrarySetOptions options,
        Action<int, string>? stagingFaultHook,
        Action<string>? onModuleUnloaded)
    {
        ValidateOptions(options, out var fullSourceDir, out var fullIsolationRoot, out var workerDirectory);
        if (!NativeLoader.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "IsolatedLibrarySet PE module isolation and Windows search-flag loading are supported only on Windows.");
        }

        var preparedModules = PreflightPrepareModules(options, fullSourceDir, out var reservedCompanionNames);
        var preparedCompanions = PreflightPrepareCompanions(options, fullSourceDir, workerDirectory, preparedModules);
        ValidateWorkerPathConstraints(options, workerDirectory, preparedModules, preparedCompanions);
        ReserveProcessIdentities(reservedCompanionNames, workerDirectory);
        return StageAndLoadReservedSet(
            options,
            fullIsolationRoot,
            workerDirectory,
            preparedModules,
            preparedCompanions,
            reservedCompanionNames,
            stagingFaultHook,
            onModuleUnloaded);
    }

#pragma warning disable S107
    private static IsolatedLibrarySet StageAndLoadReservedSet(
        IsolatedLibrarySetOptions options,
        string fullIsolationRoot,
        string workerDirectory,
        PreparedModule[] preparedModules,
        PreparedCompanionFile[] preparedCompanions,
        string[] reservedCompanionNames,
        Action<int, string>? stagingFaultHook,
        Action<string>? onModuleUnloaded)
#pragma warning restore S107
    {
        FileStream? leaseStream = null;
        var loadedLibraries = new List<UnmanagedLibrary>(preparedModules.Length);
        try
        {
            leaseStream = InitializeOwnedWorkerDirectoryAndAcquireLease(fullIsolationRoot, workerDirectory, options);
            StagePreparedCompanions(preparedCompanions, workerDirectory);
            var stagedPaths = StagePreparedModules(preparedModules, workerDirectory, stagingFaultHook);
            var librariesByName = LoadStagedModules(preparedModules, stagedPaths, options.LoadFlags, loadedLibraries, out var loadOrder);
#pragma warning disable IDE0305 // Simplify collection initialization
            return new IsolatedLibrarySet(
                workerDirectory,
                Environment.CurrentManagedThreadId,
                options.EnforceOwningThreadAffinity,
                options.DeleteDirectoryOnDispose,
                options.PinModulesForProcessLifetime,
                loadOrder,
                loadedLibraries.ToArray(),
                reservedCompanionNames,
                new ReadOnlyDictionary<string, UnmanagedLibrary>(librariesByName),
                new ReadOnlyDictionary<string, string>(stagedPaths),
                leaseStream,
                onModuleUnloaded);
#pragma warning restore IDE0305 // Simplify collection initialization
        }
        catch
        {
            for (var i = loadedLibraries.Count - 1; i >= 0; i--)
            {
                loadedLibraries[i].Dispose();
            }

            ReleaseReservations(reservedCompanionNames, workerDirectory);
            leaseStream?.Dispose();
            TryDeleteOwnedWorkerDirectoryWithRetry(workerDirectory);
            throw;
        }
    }

    private static Dictionary<string, string> StagePreparedModules(
        PreparedModule[] preparedModules,
        string workerDirectory,
        Action<int, string>? stagingFaultHook)
    {
        var stagedPaths = new Dictionary<string, string>(preparedModules.Length, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < preparedModules.Length; i++)
        {
            var module = preparedModules[i];
            var targetPath = Path.Combine(workerDirectory, module.TargetFileName);
            stagingFaultHook?.Invoke(i, targetPath);
            module.Editor.SaveIfChanged(targetPath);
            stagedPaths[module.Spec.LogicalName] = targetPath;
        }

        return stagedPaths;
    }

    private static Dictionary<string, UnmanagedLibrary> LoadStagedModules(
        PreparedModule[] preparedModules,
        Dictionary<string, string> stagedPaths,
        LoadLibraryFlags loadFlags,
        List<UnmanagedLibrary> loadedLibraries,
        out string[] loadOrder)
    {
        var librariesByName = new Dictionary<string, UnmanagedLibrary>(preparedModules.Length, StringComparer.OrdinalIgnoreCase);
        loadOrder = new string[preparedModules.Length];
        for (var i = 0; i < preparedModules.Length; i++)
        {
            var module = preparedModules[i];
            var library = new UnmanagedLibrary(stagedPaths[module.Spec.LogicalName], loadFlags);
            loadedLibraries.Add(library);
            librariesByName[module.Spec.LogicalName] = library;
            loadOrder[i] = module.Spec.LogicalName;
        }

        return librariesByName;
    }

    private static bool IsReparsePoint(FileAttributes attributes)
    {
        return attributes.HasFlag(FileAttributes.ReparsePoint);
    }

    private static void ValidateOptions(
        IsolatedLibrarySetOptions options,
        out string fullSourceDir,
        out string fullIsolationRoot,
        out string workerDirectory)
    {
        ValidateOptionFields(options);

        fullSourceDir = Path.GetFullPath(options.SourceDirectory);
        if (!Directory.Exists(fullSourceDir))
        {
            throw new DirectoryNotFoundException($"Source directory '{fullSourceDir}' does not exist.");
        }

        fullIsolationRoot = ValidateAndNormalizeIsolationRoot(options.IsolationRootDirectory, fullSourceDir);
        var nsSegment = string.IsNullOrEmpty(options.NamespaceId) ? "default" : options.NamespaceId;
        var childName = string.Format(
            CultureInfo.InvariantCulture,
            "worker-{0}-w{1:D4}-g{2:D4}",
            nsSegment,
            options.WorkerIndex,
            options.Generation);
        workerDirectory = Path.GetFullPath(Path.Combine(fullIsolationRoot, childName));
        EnsureDirectChildDirectoryPath(fullIsolationRoot, workerDirectory);
    }

    private static void ValidateOptionFields(IsolatedLibrarySetOptions options)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(options);
#endif
#if NET472_OR_GREATER
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }
#endif

#pragma warning disable CA2249,MA0001
        if (string.IsNullOrWhiteSpace(options.SourceDirectory) || options.SourceDirectory.IndexOf('\0') >= 0)
        {
            throw new ArgumentException("SourceDirectory cannot be null, whitespace, or contain NUL.", nameof(options));
        }
#pragma warning restore CA2249,MA0001

        if (options.WorkerIndex is < 0 or > 99)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.WorkerIndex, "WorkerIndex must be between 0 and 99 inclusive.");
        }

        if (options.Generation is < 0 or > 1295)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.Generation, "Generation must be between 0 and 1295 inclusive.");
        }

        WorkerModuleNaming.ValidateNamespaceId(options.NamespaceId, nameof(options));

        if (options.Modules is null || options.Modules.Count == 0)
        {
            throw new ArgumentException("IsolatedLibrarySetOptions.Modules must contain at least one module.", nameof(options));
        }

        if (options.CompanionFiles is null)
        {
            throw new ArgumentException("IsolatedLibrarySetOptions.CompanionFiles cannot be null.", nameof(options));
        }

        if (options.CompanionDirectories is null)
        {
            throw new ArgumentException("IsolatedLibrarySetOptions.CompanionDirectories cannot be null.", nameof(options));
        }

        if (options.MaxWorkerFilePathLength <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.MaxWorkerFilePathLength.Value,
                "MaxWorkerFilePathLength must be greater than zero when specified.");
        }
    }

    private static string ValidateAndNormalizeIsolationRoot(string? isolationRootDirectory, string? fullSourceDirectory)
    {
#pragma warning disable CA2249,MA0001
        if (isolationRootDirectory is null
            || string.IsNullOrWhiteSpace(isolationRootDirectory)
            || isolationRootDirectory.IndexOf('\0') >= 0)
        {
            throw new ArgumentException("IsolationRootDirectory cannot be null, whitespace, or contain NUL.", nameof(isolationRootDirectory));
        }
#pragma warning restore CA2249,MA0001

        var fullRoot = TrimTrailingSeparatorsExceptRoot(Path.GetFullPath(isolationRootDirectory));
        var pathRoot = Path.GetPathRoot(fullRoot);
        if (pathRoot?.Length > 0
            && string.Equals(TrimTrailingSeparatorsExceptRoot(pathRoot), fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"IsolationRootDirectory '{fullRoot}' cannot be a filesystem root.", nameof(isolationRootDirectory));
        }

        RejectProtectedDirectory(fullRoot, Environment.SystemDirectory, nameof(isolationRootDirectory));
        RejectProtectedDirectory(fullRoot, Environment.GetFolderPath(Environment.SpecialFolder.Windows), nameof(isolationRootDirectory));
        RejectProtectedDirectory(fullRoot, AppContext.BaseDirectory, nameof(isolationRootDirectory));

        if (fullSourceDirectory is not null)
        {
            var normalizedSource = TrimTrailingSeparatorsExceptRoot(fullSourceDirectory);
            if (PathsOverlap(fullRoot, normalizedSource))
            {
                throw new ArgumentException(
                    $"IsolationRootDirectory '{fullRoot}' must not equal or overlap SourceDirectory '{normalizedSource}'.",
                    nameof(isolationRootDirectory));
            }
        }

        return fullRoot;
    }

    private static void RejectProtectedDirectory(string candidateRoot, string? protectedDir, string paramName)
    {
        if (protectedDir is null || string.IsNullOrWhiteSpace(protectedDir))
        {
            return;
        }

        var normalizedProtected = TrimTrailingSeparatorsExceptRoot(Path.GetFullPath(protectedDir));
        if (string.Equals(candidateRoot, normalizedProtected, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"IsolationRootDirectory '{candidateRoot}' cannot target protected directory '{normalizedProtected}'.",
                paramName);
        }
    }

    private static bool PathsOverlap(string first, string second)
    {
        if (string.Equals(first, second, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var firstPrefix = first + Path.DirectorySeparatorChar;
        var secondPrefix = second + Path.DirectorySeparatorChar;
        return second.StartsWith(firstPrefix, StringComparison.OrdinalIgnoreCase)
            || first.StartsWith(secondPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string TrimTrailingSeparatorsExceptRoot(string fullPath)
    {
        var root = Path.GetPathRoot(fullPath) ?? string.Empty;
        var trimmed = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return trimmed.Length < root.Length ? root : trimmed;
    }

    private static void EnsureDirectChildDirectoryPath(string fullRoot, string candidateChild)
    {
        var parent = Path.GetDirectoryName(TrimTrailingSeparatorsExceptRoot(candidateChild));
        if (parent is null || !string.Equals(TrimTrailingSeparatorsExceptRoot(parent), fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Resolved worker directory '{candidateChild}' is not a direct child of '{fullRoot}'.");
        }
    }

    [SuppressMessage("Security", "SCS0018", Justification = "Reads validated bare filenames from SourceDirectory.")]
    private static PreparedModule[] PreflightPrepareModules(
        IsolatedLibrarySetOptions options,
        string fullSourceDir,
        out string[] reservedCompanionNames)
    {
        var count = options.Modules.Count;
        var nodes = new ModuleNode[count];
        var byLogical = new Dictionary<string, int>(count, StringComparer.OrdinalIgnoreCase);
        var bySourceFile = new Dictionary<string, int>(count, StringComparer.OrdinalIgnoreCase);
        var validation = PeImageValidationOptions.ForCurrentProcessArchitecture();

        for (var i = 0; i < count; i++)
        {
            var spec = options.Modules[i] ?? throw new ArgumentException("Module specification entry cannot be null.", nameof(options));
            if (byLogical.ContainsKey(spec.LogicalName))
            {
                throw new ArgumentException($"Duplicate logical module name '{spec.LogicalName}'.", nameof(options));
            }

            byLogical.Add(spec.LogicalName, i);
            if (bySourceFile.ContainsKey(spec.SourceFileName))
            {
                throw new ArgumentException($"Duplicate source module filename '{spec.SourceFileName}'.", nameof(options));
            }

            bySourceFile.Add(spec.SourceFileName, i);

            var sourcePath = Path.Combine(fullSourceDir, spec.SourceFileName);
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException($"Source native library '{sourcePath}' does not exist.", sourcePath);
            }

            var editor = PeImageEditor.FromFile(sourcePath, validation);
            var hasInitialMod = false;
            foreach (var removalItem in spec.ImportModuleRemovals)
            {
                var removal = removalItem ?? throw new ArgumentException($"Null ImportModuleRemovals entry on '{spec.LogicalName}'.", nameof(options));
                editor.RemoveImportModule(removal);
                hasInitialMod = true;
            }

            nodes[i] = new ModuleNode(
                i,
                spec,
                editor,
                editor.GetImportedModuleNames(),
                editor.GetDelayImportedModuleNames(),
                hasInitialMod);
        }

        BuildIntraSetEdges(nodes, bySourceFile);
        AssignAndValidateTargetFileNames(nodes, options, out reservedCompanionNames);
        var sortedIndices = TopologicalSort(nodes);

        var prepared = new PreparedModule[count];
        for (var orderPos = 0; orderPos < count; orderPos++)
        {
            var node = nodes[sortedIndices[orderPos]];
            ApplyPreflightPatches(node, nodes);
            prepared[orderPos] = new PreparedModule(node.Spec, node.TargetFileName, node.Editor);
        }

        return prepared;
    }

#pragma warning disable S3776
    private static void BuildIntraSetEdges(
        ModuleNode[] nodes,
        Dictionary<string, int> bySourceFile)
#pragma warning restore S3776
    {
        for (var i = 0; i < nodes.Length; i++)
        {
            var node = nodes[i];
            foreach (var importedName in node.OrdinaryImports)
            {
                if (!bySourceFile.TryGetValue(importedName, out var depIndex))
                {
                    continue;
                }

                if (depIndex == i)
                {
                    throw new InvalidOperationException($"Module '{node.Spec.SourceFileName}' cannot self-import.");
                }

                node.AddDependency(depIndex, importedName, isDelayImport: false);
                nodes[depIndex].IsImportedByPeer = true;
            }

            foreach (var delayName in node.DelayImports)
            {
                if (!bySourceFile.TryGetValue(delayName, out var depIndex))
                {
                    continue;
                }

                if (!node.Spec.RewriteDelayImports)
                {
                    throw new InvalidOperationException(
                        $"Module '{node.Spec.SourceFileName}' delay-imports '{nodes[depIndex].Spec.SourceFileName}'. "
                        + "Renaming ordinary import descriptors does not implicitly rewrite delay imports; "
                        + $"set RewriteDelayImports = true on '{node.Spec.LogicalName}' or configure an explicit policy.");
                }

                if (depIndex == i)
                {
                    throw new InvalidOperationException($"Module '{node.Spec.SourceFileName}' cannot delay-import itself.");
                }

                node.AddDependency(depIndex, delayName, isDelayImport: true);
                nodes[depIndex].IsImportedByPeer = true;
            }
        }
    }

    private static void AssignAndValidateTargetFileNames(
        ModuleNode[] nodes,
        IsolatedLibrarySetOptions options,
        out string[] reservedCompanionNames)
    {
        var seenTargets = new Dictionary<string, string>(nodes.Length, StringComparer.OrdinalIgnoreCase);
        var companions = new List<string>();

        foreach (var node in nodes)
        {
            var targetName = ResolveSingleTargetFileName(node, options);
            if (seenTargets.TryGetValue(targetName, out var existingSource))
            {
                throw new InvalidOperationException(
                    $"Target filename collision in IsolatedLibrarySet: both '{existingSource}' and '{node.Spec.SourceFileName}' map to '{targetName}'.");
            }

            seenTargets[targetName] = node.Spec.SourceFileName;
            node.TargetFileName = targetName;
            if (node.IsImportedByPeer || !string.Equals(targetName, node.Spec.SourceFileName, StringComparison.OrdinalIgnoreCase))
            {
                companions.Add(targetName);
            }
        }

#pragma warning disable IDE0305 // Simplify collection initialization
        reservedCompanionNames = companions.ToArray();
#pragma warning restore IDE0305 // Simplify collection initialization
    }

    private static string ResolveSingleTargetFileName(ModuleNode node, IsolatedLibrarySetOptions options)
    {
        if (node.Spec.ExplicitTargetFileName is not null)
        {
            var explicitName = node.Spec.ExplicitTargetFileName;
            WorkerModuleNaming.SplitValidatedDllFileName(
                explicitName,
                nameof(IsolatedModuleSpecification.ExplicitTargetFileName),
                out _,
                out _);
            if (node.IsImportedByPeer)
            {
                if (explicitName.Length > node.Spec.SourceFileName.Length)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(options),
                        explicitName.Length,
                        $"ExplicitTargetFileName '{explicitName}' exceeds SourceFileName '{node.Spec.SourceFileName}' length ({node.Spec.SourceFileName.Length}).");
                }

                if (string.Equals(explicitName, node.Spec.SourceFileName, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Imported companion module '{node.Spec.SourceFileName}' must use a distinct per-worker filename; keeping '{explicitName}' would allow the Windows loader to unify companion HMODULE instances across workers.");
                }
            }

            return explicitName;
        }

        if (node.IsImportedByPeer || node.Spec.AlwaysRenameWithWorkerSuffix)
        {
            return WorkerModuleNaming.FormatScopedFixedLengthName(
                node.Spec.SourceFileName,
                options.NamespaceId,
                options.WorkerIndex,
                options.Generation);
        }

        return node.Spec.SourceFileName;
    }

    private static int[] TopologicalSort(ModuleNode[] nodes)
    {
        var count = nodes.Length;
        var inDegree = new int[count];
        var dependents = new List<int>[count];
        for (var i = 0; i < count; i++)
        {
            dependents[i] = [];
        }

        for (var i = 0; i < count; i++)
        {
            foreach (var depIndex in nodes[i].AllDependencyIndices)
            {
                inDegree[i]++;
                dependents[depIndex].Add(i);
            }
        }

        var result = new int[count];
        var visited = new bool[count];
        for (var step = 0; step < count; step++)
        {
            var next = -1;
            for (var candidate = 0; candidate < count; candidate++)
            {
                if (!visited[candidate] && inDegree[candidate] == 0)
                {
                    next = candidate;
                    break;
                }
            }

            if (next < 0)
            {
                throw new InvalidOperationException(
                    "Circular dependency detected among modules in IsolatedLibrarySet; static PE import cycles cannot be topologically isolated.");
            }

            visited[next] = true;
            result[step] = next;
            foreach (var childIndex in dependents[next])
            {
                inDegree[childIndex]--;
            }
        }

        return result;
    }

    private static void ApplyPreflightPatches(ModuleNode node, ModuleNode[] allNodes)
    {
        var modified = node.HasInitialModification;
        foreach (var symItem in node.Spec.ImportSymbolRewrites)
        {
            var sym = symItem ?? throw new ArgumentException($"Null ImportSymbolRewrites entry on '{node.Spec.LogicalName}'.", nameof(node));
            node.Editor.RewriteImportSymbol(sym.ModuleName, sym.OriginalSymbolName, sym.ReplacementSymbolName);
            modified = true;
        }

        foreach (var edge in node.OrdinaryDependencyNames)
        {
            node.Editor.RewriteImportModuleName(edge.Value, allNodes[edge.Key].TargetFileName);
            modified = true;
        }

        foreach (var edge in node.DelayDependencyNames)
        {
            node.Editor.RewriteDelayImportModuleName(edge.Value, allNodes[edge.Key].TargetFileName);
            modified = true;
        }

        if (node.Spec.RewriteExportModuleName)
        {
            node.Editor.RewriteExportModuleName(node.Spec.SourceFileName, node.TargetFileName);
            modified = true;
        }

        foreach (var patchItem in node.Spec.VerifiedBytePatches)
        {
            var patch = patchItem ?? throw new ArgumentException($"Null VerifiedBytePatches entry on '{node.Spec.LogicalName}'.", nameof(node));
            node.Editor.ReplaceVerifiedBytesAtRva(patch.Rva, patch.ExpectedOriginalBuffer, patch.ReplacementBuffer);
            modified = true;
        }

        if (modified && node.Spec.ClearAuthenticodeAndRecalculateChecksum)
        {
            node.Editor.ClearAuthenticodeAndRecalculateChecksum();
        }
    }

    [SuppressMessage("Security", "SCS0018", Justification = "Strictly validates relative companion paths against traversal and reparse points.")]
    private static PreparedCompanionFile[] PreflightPrepareCompanions(
        IsolatedLibrarySetOptions options,
        string fullSourceDir,
        string workerDirectory,
        PreparedModule[] preparedModules)
    {
        if (options.CompanionFiles.Count == 0 && options.CompanionDirectories.Count == 0)
        {
            return [];
        }

        var reservedTargetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetFullPath(Path.Combine(workerDirectory, OwnershipMarkerFileName)),
            Path.GetFullPath(Path.Combine(workerDirectory, LeaseFileName)),
        };

        foreach (var module in preparedModules)
        {
            _ = reservedTargetPaths.Add(Path.GetFullPath(Path.Combine(workerDirectory, module.TargetFileName)));
        }

        var list = new List<PreparedCompanionFile>(options.CompanionFiles.Count);
        foreach (var dirSpecItem in options.CompanionDirectories)
        {
            var dirSpec = dirSpecItem ?? throw new ArgumentException("Null CompanionDirectories entry.", nameof(options));
            ExpandCompanionDirectorySpec(dirSpec, fullSourceDir, workerDirectory, reservedTargetPaths, list);
        }

        foreach (var fileSpecItem in options.CompanionFiles)
        {
            var fileSpec = fileSpecItem ?? throw new ArgumentException("Null CompanionFiles entry.", nameof(options));
            AddCompanionFileSpec(fileSpec, fullSourceDir, workerDirectory, reservedTargetPaths, list);
        }

#pragma warning disable IDE0305 // Simplify collection initialization
        return list.ToArray();
#pragma warning restore IDE0305 // Simplify collection initialization
    }

    [SuppressMessage("Security", "SCS0018", Justification = "Enumerates validated source companion subdirectory while rejecting traversal and reparse points.")]
    private static void ExpandCompanionDirectorySpec(
        IsolatedCompanionDirectorySpecification dirSpec,
        string fullSourceDir,
        string workerDirectory,
        HashSet<string> reservedTargetPaths,
        List<PreparedCompanionFile> list)
    {
        var sourceRel = ValidateRelativeSubPath(dirSpec.SourceRelativePath, nameof(dirSpec));
        var targetRel = dirSpec.TargetRelativePath is null
            ? sourceRel
            : ValidateRelativeSubPath(dirSpec.TargetRelativePath, nameof(dirSpec));
        ValidateSearchPattern(dirSpec.SearchPattern, nameof(dirSpec));

        var sourceSubDir = ResolveBoundedChildPath(fullSourceDir, sourceRel, nameof(dirSpec));
        if (!Directory.Exists(sourceSubDir))
        {
            throw new DirectoryNotFoundException($"Source companion directory '{sourceSubDir}' does not exist.");
        }

        EnsureNoReparsePointChain(fullSourceDir, sourceSubDir);
        if (dirSpec.Recursive)
        {
            foreach (var subDir in Directory.GetDirectories(sourceSubDir, "*", SearchOption.AllDirectories))
            {
                var subDirAttributes = new DirectoryInfo(subDir).Attributes;
                if (IsReparsePoint(subDirAttributes))
                {
                    throw new InvalidOperationException($"Companion source subdirectory '{subDir}' cannot be a reparse point.");
                }
            }
        }

        var searchOption = dirSpec.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = Directory.GetFiles(sourceSubDir, dirSpec.SearchPattern, searchOption);
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);

        var sourcePrefix = TrimTrailingSeparatorsExceptRoot(sourceSubDir) + Path.DirectorySeparatorChar;
        foreach (var sourceFile in files)
        {
            var fileInfo = new FileInfo(sourceFile);
            if (IsReparsePoint(fileInfo.Attributes))
            {
                throw new InvalidOperationException($"Companion source file '{sourceFile}' cannot be a reparse point.");
            }

            var fullSourceFile = Path.GetFullPath(sourceFile);
            if (!fullSourceFile.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Companion source file '{fullSourceFile}' escaped '{sourceSubDir}'.");
            }

            var relativeInsideTree = fullSourceFile.Substring(sourcePrefix.Length);
            var combinedTargetRel = Path.Combine(targetRel, relativeInsideTree);
            var targetFilePath = ResolveBoundedChildPath(workerDirectory, combinedTargetRel, nameof(dirSpec));
            RegisterUniqueCompanionTarget(reservedTargetPaths, targetFilePath);
            list.Add(new PreparedCompanionFile(fullSourceFile, targetFilePath, dirSpec.CopyMode, transformBytes: null));
        }
    }

    [SuppressMessage("Security", "SCS0018", Justification = "Validates relative companion file path inside SourceDirectory and WorkerDirectory.")]
    private static void AddCompanionFileSpec(
        IsolatedCompanionFileSpecification fileSpec,
        string fullSourceDir,
        string workerDirectory,
        HashSet<string> reservedTargetPaths,
        List<PreparedCompanionFile> list)
    {
        var sourceRel = ValidateRelativeSubPath(fileSpec.SourceRelativePath, nameof(fileSpec));
        var targetRel = fileSpec.TargetRelativePath is null
            ? sourceRel
            : ValidateRelativeSubPath(fileSpec.TargetRelativePath, nameof(fileSpec));

        var sourceFilePath = ResolveBoundedChildPath(fullSourceDir, sourceRel, nameof(fileSpec));
        if (!File.Exists(sourceFilePath))
        {
            throw new FileNotFoundException($"Source companion file '{sourceFilePath}' does not exist.", sourceFilePath);
        }

        EnsureNoReparsePointChain(fullSourceDir, Path.GetDirectoryName(sourceFilePath)!);
        if (IsReparsePoint(new FileInfo(sourceFilePath).Attributes))
        {
            throw new InvalidOperationException($"Source companion file '{sourceFilePath}' cannot be a reparse point.");
        }

        var targetFilePath = ResolveBoundedChildPath(workerDirectory, targetRel, nameof(fileSpec));
        RegisterUniqueCompanionTarget(reservedTargetPaths, targetFilePath);
        list.Add(new PreparedCompanionFile(sourceFilePath, targetFilePath, fileSpec.CopyMode, fileSpec.TransformBytes));
    }

    private static void RegisterUniqueCompanionTarget(HashSet<string> reservedTargetPaths, string targetFilePath)
    {
        if (!reservedTargetPaths.Add(targetFilePath))
        {
            throw new InvalidOperationException(
                $"Companion target path '{targetFilePath}' collides with another staged module, companion file, or internal isolation marker.");
        }
    }

    private static string ValidateRelativeSubPath(string? relativePath, string paramName)
    {
#pragma warning disable CA2249,MA0001
        if (relativePath is null || string.IsNullOrWhiteSpace(relativePath) || relativePath.IndexOf('\0') >= 0)
        {
            throw new ArgumentException("Companion relative path cannot be null, whitespace, or contain NUL.", paramName);
        }
#pragma warning restore CA2249,MA0001

#pragma warning disable CA2249,MA0001
        if (Path.IsPathRooted(relativePath)
            || relativePath.IndexOf(':') >= 0
            || relativePath[0] == '/'
            || relativePath[0] == '\\')
        {
            throw new ArgumentException($"Companion path '{relativePath}' must be a relative subpath without root or drive specifiers.", paramName);
        }
#pragma warning restore CA2249,MA0001

        var segments = relativePath.Split(['/', '\\'], StringSplitOptions.None);
        foreach (var segment in segments)
        {
            if (segment.Length == 0
                || string.Equals(segment, ".", StringComparison.Ordinal)
                || string.Equals(segment, "..", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Companion relative path '{relativePath}' cannot contain empty, '.', or '..' traversal segments.", paramName);
            }
        }

        return string.Join(Path.DirectorySeparatorChar.ToString(), segments);
    }

    private static void ValidateSearchPattern(string? searchPattern, string paramName)
    {
#pragma warning disable CA2249,MA0001
        if (searchPattern is null
            || string.IsNullOrWhiteSpace(searchPattern)
            || searchPattern.IndexOf('\0') >= 0
            || searchPattern.IndexOf('/') >= 0
            || searchPattern.IndexOf('\\') >= 0
            || string.Equals(searchPattern, "..", StringComparison.Ordinal))
        {
            throw new ArgumentException("Companion directory SearchPattern must be a non-empty bare file pattern.", paramName);
        }
#pragma warning restore CA2249,MA0001
    }

    private static string ResolveBoundedChildPath(string baseDirectory, string validatedRelativePath, string paramName)
    {
        var normalizedBase = TrimTrailingSeparatorsExceptRoot(Path.GetFullPath(baseDirectory));
        var candidate = Path.GetFullPath(Path.Combine(normalizedBase, validatedRelativePath));
        var prefix = normalizedBase + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Resolved companion path '{candidate}' escaped base directory '{normalizedBase}'.", paramName);
        }

        return candidate;
    }

    private static void EnsureNoReparsePointChain(string rootDirectory, string descendantDirectory)
    {
        var normalizedRoot = TrimTrailingSeparatorsExceptRoot(Path.GetFullPath(rootDirectory));
        var cursor = TrimTrailingSeparatorsExceptRoot(Path.GetFullPath(descendantDirectory));
        while (cursor.Length >= normalizedRoot.Length
               && cursor.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            if (Directory.Exists(cursor) && IsReparsePoint(new DirectoryInfo(cursor).Attributes))
            {
                throw new InvalidOperationException($"Directory '{cursor}' cannot be a reparse point.");
            }

            if (string.Equals(cursor, normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            var parent = Path.GetDirectoryName(cursor);
            if (parent is null)
            {
                break;
            }

            cursor = TrimTrailingSeparatorsExceptRoot(parent);
        }
    }

    private static void ValidateWorkerPathConstraints(
        IsolatedLibrarySetOptions options,
        string workerDirectory,
        PreparedModule[] preparedModules,
        PreparedCompanionFile[] preparedCompanions)
    {
        if (!options.RequireAsciiWorkerPaths && !options.MaxWorkerFilePathLength.HasValue)
        {
            return;
        }

        CheckSingleWorkerPathConstraint(workerDirectory, options);
        foreach (var module in preparedModules)
        {
            CheckSingleWorkerPathConstraint(Path.Combine(workerDirectory, module.TargetFileName), options);
        }

        foreach (var companion in preparedCompanions)
        {
            CheckSingleWorkerPathConstraint(companion.TargetFilePath, options);
        }
    }

    private static void CheckSingleWorkerPathConstraint(string candidatePath, IsolatedLibrarySetOptions options)
    {
        if (options.RequireAsciiWorkerPaths)
        {
            for (var i = 0; i < candidatePath.Length; i++)
            {
                if (candidatePath[i] > 0x7F)
                {
                    throw new ArgumentException(
                        $"Worker path '{candidatePath}' contains non-ASCII character U+{(int)candidatePath[i]:X4}, violating RequireAsciiWorkerPaths.",
                        nameof(options));
                }
            }
        }

        if (options.MaxWorkerFilePathLength.HasValue && candidatePath.Length > options.MaxWorkerFilePathLength.Value)
        {
            throw new PathTooLongException(
                $"Worker path '{candidatePath}' has length {candidatePath.Length}, exceeding MaxWorkerFilePathLength ({options.MaxWorkerFilePathLength.Value.ToString(CultureInfo.InvariantCulture)}).");
        }
    }

    [SuppressMessage("Security", "SCS0018", Justification = "Stages validated companion files into owned worker directory.")]
    [SuppressMessage("Security", "SEC0116", Justification = "Reads validated companion source files from SourceDirectory.")]
    private static void StagePreparedCompanions(PreparedCompanionFile[] preparedCompanions, string workerDirectory)
    {
        foreach (var companion in preparedCompanions)
        {
            var targetParent = Path.GetDirectoryName(companion.TargetFilePath)!;
            Directory.CreateDirectory(targetParent);
            EnsureNoReparsePointChain(workerDirectory, targetParent);

            if (companion.TransformBytes is not null)
            {
                var rawBytes = File.ReadAllBytes(companion.SourceFilePath);
                var transformed = companion.TransformBytes(rawBytes, workerDirectory)
                    ?? throw new InvalidOperationException("IsolatedCompanionFileSpecification.TransformBytes returned null.");

                if (companion.CopyMode == CompanionCopyMode.CopyIfDifferentSizeOrTimestamp
                    && PeImageEditor.FileMatchesBuffer(companion.TargetFilePath, transformed))
                {
                    continue;
                }

                PeImageEditor.WriteAtomically(companion.TargetFilePath, targetParent, transformed);
                continue;
            }

            CopyUntransformedCompanionAtomically(companion.SourceFilePath, companion.TargetFilePath, targetParent, companion.CopyMode);
        }
    }

    [SuppressMessage("Security", "SCS0018", Justification = "Atomically copies validated companion file inside owned worker directory.")]
    [SuppressMessage("Security", "SEC0112", Justification = "Uses sibling temporary file inside owned worker directory.")]
    [SuppressMessage("Security", "SEC0116", Justification = "Cleans up temporary file on failure.")]
    private static void CopyUntransformedCompanionAtomically(
        string sourceFilePath,
        string targetFilePath,
        string targetParent,
        CompanionCopyMode copyMode)
    {
        var sourceInfo = new FileInfo(sourceFilePath);
        if (copyMode == CompanionCopyMode.CopyIfDifferentSizeOrTimestamp && File.Exists(targetFilePath))
        {
            var targetInfo = new FileInfo(targetFilePath);
            if (!IsReparsePoint(targetInfo.Attributes)
                && targetInfo.Length == sourceInfo.Length
                && targetInfo.LastWriteTimeUtc == sourceInfo.LastWriteTimeUtc)
            {
                return;
            }
        }

        var tempPath = Path.Combine(
            targetParent,
            Path.GetFileName(targetFilePath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.Copy(sourceFilePath, tempPath, overwrite: false);
            File.SetAttributes(tempPath, FileAttributes.Normal);
            File.SetLastWriteTimeUtc(tempPath, sourceInfo.LastWriteTimeUtc);
#if NET8_0_OR_GREATER
            File.Move(tempPath, targetFilePath, overwrite: true);
#else
            if (File.Exists(targetFilePath))
            {
                File.Replace(tempPath, targetFilePath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, targetFilePath);
            }
#endif
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _ = ex;
                }
            }

            throw;
        }
    }

    private static void ReserveProcessIdentities(string[] companionNames, string workerDirectory)
    {
        if (!_activeWorkerDirectories.TryAdd(workerDirectory, 0))
        {
            throw new InvalidOperationException(
                $"Worker directory '{workerDirectory}' is already leased by an active IsolatedLibrarySet in this process. Specify a distinct NamespaceId, WorkerIndex, or Generation.");
        }

        var reservedCount = 0;
        try
        {
            foreach (var name in companionNames)
            {
                if (!_activeCompanionModules.TryAdd(name, workerDirectory))
                {
                    _activeCompanionModules.TryGetValue(name, out var ownerDir);
                    throw new InvalidOperationException(
                        $"Isolated companion module name '{name}' is already active in '{ownerDir}'. "
                        + "Separate pools and overlapping worker generations require distinct NamespaceId, Generation, or ExplicitTargetFileName values.");
                }

                reservedCount++;
            }
        }
        catch
        {
            for (var i = 0; i < reservedCount; i++)
            {
                _activeCompanionModules.TryRemove(companionNames[i], out _);
            }

            _activeWorkerDirectories.TryRemove(workerDirectory, out _);
            throw;
        }
    }

    private static void ReleaseReservations(string[] companionNames, string workerDirectory)
    {
        foreach (var name in companionNames)
        {
            _activeCompanionModules.TryRemove(name, out _);
        }

        _activeWorkerDirectories.TryRemove(workerDirectory, out _);
    }

    [SuppressMessage("Security", "SCS0018", Justification = "Creates validated child worker directory with ownership marker and exclusive lease.")]
    [SuppressMessage("Security", "SEC0112", Justification = "Acquires exclusive lease file inside validated direct child worker directory.")]
    private static FileStream InitializeOwnedWorkerDirectoryAndAcquireLease(
        string fullIsolationRoot,
        string workerDirectory,
        IsolatedLibrarySetOptions options)
    {
        Directory.CreateDirectory(fullIsolationRoot);
        if (IsReparsePoint(new DirectoryInfo(fullIsolationRoot).Attributes))
        {
            throw new InvalidOperationException($"Isolation root directory '{fullIsolationRoot}' cannot be a reparse point.");
        }

        var existedBefore = Directory.Exists(workerDirectory);
        Directory.CreateDirectory(workerDirectory);
        if (IsReparsePoint(new DirectoryInfo(workerDirectory).Attributes))
        {
            throw new InvalidOperationException($"Worker directory '{workerDirectory}' cannot be a reparse point.");
        }

        var markerPath = Path.Combine(workerDirectory, OwnershipMarkerFileName);
        if (existedBefore && !HasValidOwnershipMarker(markerPath) && DirectoryHasAnyEntries(workerDirectory))
        {
            throw new InvalidOperationException(
                $"Existing directory '{workerDirectory}' does not carry a valid IsolatedLibrarySet ownership marker; refusing to stage into an unowned directory.");
        }

        var leasePath = Path.Combine(workerDirectory, LeaseFileName);
        FileStream leaseStream;
        try
        {
            leaseStream = new FileStream(
                leasePath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 64,
                FileOptions.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"Worker directory '{workerDirectory}' is currently locked by another active IsolatedLibrarySet lease.",
                ex);
        }

        try
        {
            WriteOwnershipMarker(markerPath, options);
            return leaseStream;
        }
        catch
        {
            leaseStream.Dispose();
            throw;
        }
    }

    [SuppressMessage("Security", "SCS0018", Justification = "Writes fixed ownership marker file inside validated worker directory.")]
    [SuppressMessage("Security", "SEC0112", Justification = "Writes fixed marker filename inside validated worker directory.")]
    private static void WriteOwnershipMarker(string markerPath, IsolatedLibrarySetOptions options)
    {
        var markerFile = new FileInfo(markerPath);
        if (markerFile.Exists && IsReparsePoint(markerFile.Attributes))
        {
            throw new InvalidOperationException($"Ownership marker '{markerPath}' cannot be a reparse point.");
        }

#if NET10_0_OR_GREATER
        var pid = Environment.ProcessId;
#else
        var pid = Process.GetCurrentProcess().Id;
#endif
        var content = string.Format(
            CultureInfo.InvariantCulture,
            "{0}\npid={1}\nworker={2}\ngeneration={3}\nnamespace={4}\n",
            OwnershipMarkerHeader,
            pid,
            options.WorkerIndex,
            options.Generation,
            options.NamespaceId ?? string.Empty);
        var bytes = Encoding.UTF8.GetBytes(content);
        using var stream = new FileStream(markerPath, FileMode.Create, FileAccess.Write, FileShare.Read, bufferSize: 256);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush(flushToDisk: true);
    }

    [SuppressMessage("Security", "SCS0018", Justification = "Reads fixed ownership marker inside candidate worker directory.")]
    [SuppressMessage("Security", "SEC0112", Justification = "Reads fixed marker filename inside validated candidate worker directory.")]
    private static bool HasValidOwnershipMarker(string markerPath)
    {
        try
        {
            var info = new FileInfo(markerPath);
            if (!info.Exists || IsReparsePoint(info.Attributes) || info.Length > 4096)
            {
                return false;
            }

            using var reader = new StreamReader(
                new FileStream(markerPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite),
                Encoding.UTF8);
            var firstLine = reader.ReadLine();
            return string.Equals(firstLine, OwnershipMarkerHeader, StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = ex;
            return false;
        }
    }

    [SuppressMessage("Security", "SCS0018", Justification = "Checks emptiness of candidate worker directory.")]
    private static bool DirectoryHasAnyEntries(string directoryPath)
    {
        using var enumerator = Directory.EnumerateFileSystemEntries(directoryPath).GetEnumerator();
        return enumerator.MoveNext();
    }

    private static void SweepSingleCandidateDirectory(
        string candidatePath,
        string fullRoot,
        List<string> deleted,
        List<string> skippedActive,
        List<string> skippedUnknown,
        List<string> failed)
    {
        var fullCandidate = TrimTrailingSeparatorsExceptRoot(Path.GetFullPath(candidatePath));
        EnsureDirectChildDirectoryPath(fullRoot, fullCandidate);

        var dirInfo = new DirectoryInfo(fullCandidate);
        if (!dirInfo.Exists)
        {
            return;
        }

        var markerPath = Path.Combine(fullCandidate, OwnershipMarkerFileName);
        if (IsReparsePoint(dirInfo.Attributes) || !HasValidOwnershipMarker(markerPath))
        {
            skippedUnknown.Add(fullCandidate);
            return;
        }

        if (_activeWorkerDirectories.ContainsKey(fullCandidate))
        {
            skippedActive.Add(fullCandidate);
            return;
        }

        SweepVerifiedStaleCandidate(fullCandidate, markerPath, deleted, skippedActive, failed);
    }

    [SuppressMessage("Security", "SCS0018", Justification = "Acquires exclusive lease on marker-verified direct child directory before deleting stale entries.")]
    [SuppressMessage("Security", "SEC0112", Justification = "Opens fixed lease filename inside marker-verified direct child directory.")]
    private static void SweepVerifiedStaleCandidate(
        string fullCandidate,
        string markerPath,
        List<string> deleted,
        List<string> skippedActive,
        List<string> failed)
    {
        var leasePath = Path.Combine(fullCandidate, LeaseFileName);
        bool payloadDeleted;
        try
        {
            using var leaseStream = new FileStream(
                leasePath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 16,
                FileOptions.None);
            payloadDeleted = TryDeletePayloadEntriesWhileLeased(fullCandidate);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = ex;
            skippedActive.Add(fullCandidate);
            return;
        }

        if (payloadDeleted && TryFinalizeEmptyOwnedDirectory(fullCandidate, leasePath, markerPath))
        {
            deleted.Add(fullCandidate);
        }
        else
        {
            failed.Add(fullCandidate);
        }
    }

    [SuppressMessage("Security", "SCS0018", Justification = "Deletes only non-marker files inside lease-locked owned worker directory.")]
    [SuppressMessage("Security", "SEC0116", Justification = "Deletes only staged files inside marker-verified owned worker directory.")]
    private static bool TryDeletePayloadEntriesWhileLeased(string workerDirectory)
    {
        try
        {
            foreach (var subDir in Directory.GetDirectories(workerDirectory))
            {
                if (IsReparsePoint(new DirectoryInfo(subDir).Attributes))
                {
                    return false;
                }

                Directory.Delete(subDir, recursive: true);
            }

            foreach (var file in Directory.GetFiles(workerDirectory))
            {
                var fileName = Path.GetFileName(file);
                if (string.Equals(fileName, OwnershipMarkerFileName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(fileName, LeaseFileName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = ex;
            return false;
        }
    }

    [SuppressMessage("Security", "SCS0018", Justification = "Removes lease, ownership marker, and now-empty owned worker directory.")]
    [SuppressMessage("Security", "SEC0116", Justification = "Deletes fixed lease and marker files inside marker-verified owned worker directory.")]
    private static bool TryFinalizeEmptyOwnedDirectory(string workerDirectory, string leasePath, string markerPath)
    {
        try
        {
            if (File.Exists(leasePath))
            {
                File.SetAttributes(leasePath, FileAttributes.Normal);
                File.Delete(leasePath);
            }

            if (File.Exists(markerPath))
            {
                File.SetAttributes(markerPath, FileAttributes.Normal);
                File.Delete(markerPath);
            }

            Directory.Delete(workerDirectory, recursive: false);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = ex;
            return false;
        }
    }

    [SuppressMessage("Security", "SCS0018", Justification = "Deletes only marker-verified owned worker directory with short retry for Windows image-section teardown.")]
    private static void TryDeleteOwnedWorkerDirectoryWithRetry(string workerDirectory)
    {
        if (!Directory.Exists(workerDirectory)
            || IsReparsePoint(new DirectoryInfo(workerDirectory).Attributes))
        {
            return;
        }

        var markerPath = Path.Combine(workerDirectory, OwnershipMarkerFileName);
        if (!HasValidOwnershipMarker(markerPath))
        {
            return;
        }

        var leasePath = Path.Combine(workerDirectory, LeaseFileName);
        for (var attempt = 0; attempt < 6; attempt++)
        {
            if (!Directory.Exists(workerDirectory))
            {
                return;
            }

            if (TryDeletePayloadEntriesWhileLeased(workerDirectory)
                && TryFinalizeEmptyOwnedDirectory(workerDirectory, leasePath, markerPath))
            {
                return;
            }

            if (attempt < 5)
            {
                Thread.Sleep(15);
            }
        }
    }

    private void EnsureOwningThreadIfRequired()
    {
        if (!_enforceOwningThreadAffinity)
        {
            return;
        }

        var currentThreadId = Environment.CurrentManagedThreadId;
        if (currentThreadId != _owningThreadId)
        {
            throw new InvalidOperationException(
                $"This IsolatedLibrarySet enforces owning-thread affinity to managed thread {_owningThreadId.ToString(CultureInfo.InvariantCulture)} and cannot be accessed or disposed from thread {currentThreadId.ToString(CultureInfo.InvariantCulture)}.");
        }
    }

    private void ThrowIfDisposed()
    {
#if NET10_0_OR_GREATER
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, instance: this);
#endif
#if NET472_OR_GREATER
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(IsolatedLibrarySet));
        }
#endif
    }

    private sealed class ModuleNode(
        int index,
        IsolatedModuleSpecification spec,
        PeImageEditor editor,
        IReadOnlyList<string> ordinaryImports,
        IReadOnlyList<string> delayImports,
        bool hasInitialModification)
    {
        public int Index { get; } = index;

        public IsolatedModuleSpecification Spec { get; } = spec;

        public PeImageEditor Editor { get; } = editor;

        public IReadOnlyList<string> OrdinaryImports { get; } = ordinaryImports;

        public IReadOnlyList<string> DelayImports { get; } = delayImports;

        public bool HasInitialModification { get; } = hasInitialModification;

        public bool IsImportedByPeer { get; set; }

        public string TargetFileName { get; set; } = string.Empty;

        public HashSet<int> AllDependencyIndices { get; } = [];

        public Dictionary<int, string> OrdinaryDependencyNames { get; } = [];

        public Dictionary<int, string> DelayDependencyNames { get; } = [];

        public void AddDependency(int dependencyIndex, string exactNameInPe, bool isDelayImport)
        {
            _ = AllDependencyIndices.Add(dependencyIndex);
            var targetDict = isDelayImport ? DelayDependencyNames : OrdinaryDependencyNames;
            targetDict[dependencyIndex] = exactNameInPe;
        }
    }

    private sealed class PreparedModule(
        IsolatedModuleSpecification spec,
        string targetFileName,
        PeImageEditor editor)
    {
        public IsolatedModuleSpecification Spec { get; } = spec;

        public string TargetFileName { get; } = targetFileName;

        public PeImageEditor Editor { get; } = editor;
    }

    private sealed class PreparedCompanionFile(
        string sourceFilePath,
        string targetFilePath,
        CompanionCopyMode copyMode,
        Func<byte[], string, byte[]>? transformBytes)
    {
        public string SourceFilePath { get; } = sourceFilePath;

        public string TargetFilePath { get; } = targetFilePath;

        public CompanionCopyMode CopyMode { get; } = copyMode;

        public Func<byte[], string, byte[]>? TransformBytes { get; } = transformBytes;
    }

    [SuppressMessage("IDisposableAnalyzers.Correctness", "IDISP006", Justification = "Intentionally roots process-pinned module handles and lease stream for process lifetime without disposing.")]
    [SuppressMessage("Design", "CA1001", Justification = "Intentionally roots process-pinned module handles and lease stream for process lifetime without disposing.")]
    private sealed class PinnedSetRetention(
        UnmanagedLibrary[] libraries,
        FileStream? leaseStream)
    {
        public UnmanagedLibrary[] Libraries { get; } = libraries;

        public FileStream? LeaseStream { get; } = leaseStream;
    }
}
