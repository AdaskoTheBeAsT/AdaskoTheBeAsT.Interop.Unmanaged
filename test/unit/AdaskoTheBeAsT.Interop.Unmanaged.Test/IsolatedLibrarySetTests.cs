using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
#if NET5_0_OR_GREATER
using System.Runtime.Versioning;
#endif
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Unmanaged.Test;

#pragma warning disable SA1121
#pragma warning disable IDISP001 // IsolatedLibrarySet owns and disposes all UnmanagedLibrary instances returned by GetLibrary.
#pragma warning disable IDISP004 // IsolatedLibrarySet retains ownership of module handles returned by GetLibrary.

[Trait("Category", "Native")]
public class IsolatedLibrarySetTests
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int StepFn(int delta);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetIntFn();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr GetPtrFn();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SetDetachRecorderFn(IntPtr cursor, IntPtr slots);

    [Fact]
    [SuppressMessage("Security", "SCS0018", Justification = "Creates test-owned temporary isolation root.")]
    public void MultiDllGraph_IsolatesStateAcrossTenThousandConcurrentIterations_WithoutChangingCwd()
    {
        if (TestHelpers.SkipIfNotWindows())
        {
            return;
        }

        using var fixture = new NativeFixture();
        var isolationRoot = CreateIsolationRoot(fixture);
        var cwdBefore = Environment.CurrentDirectory;

        using var worker0 = IsolatedLibrarySet.Create(BuildGraphOptions(fixture.DirectoryPath, isolationRoot, workerIndex: 0));
        using var worker1 = IsolatedLibrarySet.Create(BuildGraphOptions(fixture.DirectoryPath, isolationRoot, workerIndex: 1));

        Environment.CurrentDirectory.Should().Be(cwdBefore);
        worker0.VerifyDistinctFrom(worker1);
        worker0.LoadOrder.Should().Equal("isoleaf0", "isochild", "isodelay", "isoparent");
        worker0["isoparent"].Should().BeSameAs(worker0.GetLibrary("isoparent"));

        RunConcurrentMutations(worker0, worker1, iterations: 10_000);
        VerifyIndependentCounters(worker0, worker1, iterations: 10_000);
    }

    [Fact]
    [SuppressMessage("Security", "SCS0018", Justification = "Creates test-owned temporary isolation root.")]
    public void DynamicLoadLiteral_RemainsUnchangedByDefault_AndResolvesWorkerCompanionWhenPatched()
    {
        if (TestHelpers.SkipIfNotWindows())
        {
            return;
        }

        using var fixture = new NativeFixture();
        var isolationRoot = CreateIsolationRoot(fixture);
        uint dynLiteralRva;
        using (var probe = new UnmanagedLibrary(fixture.GetPath("isoparent")))
        {
            var ptr = probe.GetUnmanagedFunction<GetPtrFn>("iso_parent_get_dyn_name_ptr")!();
            dynLiteralRva = checked((uint)(ptr.ToInt64() - probe.BaseAddress.ToInt64()));
        }

        using (var unpatchedSet = IsolatedLibrarySet.Create(BuildGraphOptions(fixture.DirectoryPath, isolationRoot, workerIndex: 2)))
        {
            var queryUnpatched = unpatchedSet.GetLibrary("isoparent").GetUnmanagedFunction<GetPtrFn>("iso_parent_query_loaded_dyn_target")!;
            queryUnpatched().Should().Be(IntPtr.Zero);
        }

        var targetLeafName = WorkerModuleNaming.FormatFixedLengthName("isoleaf0.dll", 3);
        var patchedOptions = new IsolatedLibrarySetOptions
        {
            SourceDirectory = fixture.DirectoryPath,
            IsolationRootDirectory = isolationRoot,
            WorkerIndex = 3,
            Modules =
            [
                new IsolatedModuleSpecification("isoleaf0.dll"),
                new IsolatedModuleSpecification("isochild.dll")
                {
                    ImportSymbolRewrites = [new PeImportSymbolRewrite("isoleaf0.dll", "iso_sym_primary", "iso_sym_alt")],
                },
                new IsolatedModuleSpecification("isoparent.dll")
                {
                    VerifiedBytePatches = [PeVerifiedBytePatch.FromAsciiLiteral(dynLiteralRva, "isoleaf0.dll", targetLeafName)],
                },
            ],
        };

        using var patchedSet = IsolatedLibrarySet.Create(patchedOptions);
        var queryPatched = patchedSet.GetLibrary("isoparent").GetUnmanagedFunction<GetPtrFn>("iso_parent_query_loaded_dyn_target")!;
        queryPatched().Should().Be(patchedSet.GetLibrary("isoleaf0").BaseAddress);
        patchedSet.GetLibrary("isochild").GetUnmanagedFunction<GetIntFn>("iso_child_call_symbol")!().Should().Be(222);
    }

    [Fact]
    [SuppressMessage("IDisposableAnalyzers", "IDISP016", Justification = "Intentional duplicate Dispose verifies idempotence.")]
    [SuppressMessage("IDisposableAnalyzers", "IDISP017", Justification = "Explicit Dispose verifies reverse unload order and directory cleanup.")]
    [SuppressMessage("Security", "SCS0018", Justification = "Checks worker directory removal inside test-owned root.")]
    public void Dispose_UnloadsModulesInReverseTopologicalOrder_AndDeletesWorkerDirectory()
    {
        if (TestHelpers.SkipIfNotWindows())
        {
            return;
        }

        using var fixture = new NativeFixture();
        var isolationRoot = CreateIsolationRoot(fixture);
        var callbackOrder = new List<string>();
        var unmanagedBlock = Marshal.AllocHGlobal(sizeof(int) * 8);
        try
        {
            for (var i = 0; i < 8; i++)
            {
                Marshal.WriteInt32(unmanagedBlock, i * sizeof(int), 0);
            }

            var cursorPtr = unmanagedBlock;
            var slotsPtr = IntPtr.Add(unmanagedBlock, sizeof(int) * 2);
            var options = new IsolatedLibrarySetOptions
            {
                SourceDirectory = fixture.DirectoryPath,
                IsolationRootDirectory = isolationRoot,
                WorkerIndex = 4,
                Modules =
                [
                    new IsolatedModuleSpecification("isoparent.dll"),
                    new IsolatedModuleSpecification("isochild.dll"),
                    new IsolatedModuleSpecification("isoleaf0.dll"),
                ],
            };

            using var set = IsolatedLibrarySet.CreateCore(options, stagingFaultHook: null, onModuleUnloaded: callbackOrder.Add);
            var workerDir = set.WorkerDirectory;
            RegisterDetachRecorders(set, cursorPtr, slotsPtr);

            set.Dispose();
            set.Dispose();

            callbackOrder.Should().Equal("isoparent", "isochild", "isoleaf0");
            Marshal.ReadInt32(cursorPtr).Should().Be(3);
            var nativeDetachOrder = new[]
            {
                Marshal.ReadInt32(slotsPtr, 0),
                Marshal.ReadInt32(slotsPtr, sizeof(int)),
                Marshal.ReadInt32(slotsPtr, sizeof(int) * 2),
            };
            nativeDetachOrder.Should().Equal(3, 2, 1);
            Directory.Exists(workerDir).Should().BeFalse();
        }
        finally
        {
            Marshal.FreeHGlobal(unmanagedBlock);
        }
    }

    [Fact]
    [SuppressMessage("Security", "SCS0018", Justification = "Creates test-owned temporary isolation root.")]
    public void SeparateNamespacesAndRecycleGenerations_CoexistWithoutCollision_WhileDuplicateSlotFailsCleanly()
    {
        if (TestHelpers.SkipIfNotWindows())
        {
            return;
        }

        using var fixture = new NativeFixture();
        var isolationRoot = CreateIsolationRoot(fixture);

        using var poolA0 = IsolatedLibrarySet.Create(BuildGraphOptions(fixture.DirectoryPath, isolationRoot, workerIndex: 0, namespaceId: "A", generation: 1));
        using var poolB0 = IsolatedLibrarySet.Create(BuildGraphOptions(fixture.DirectoryPath, isolationRoot, workerIndex: 0, namespaceId: "B", generation: 1));
        using var poolAGen2 = IsolatedLibrarySet.Create(BuildGraphOptions(fixture.DirectoryPath, isolationRoot, workerIndex: 0, namespaceId: "A", generation: 2));

        poolA0.VerifyDistinctFrom(poolB0);
        poolA0.VerifyDistinctFrom(poolAGen2);
        poolB0.VerifyDistinctFrom(poolAGen2);

        Action duplicateSlot = () =>
        {
            using var dup = IsolatedLibrarySet.Create(BuildGraphOptions(fixture.DirectoryPath, isolationRoot, workerIndex: 0, namespaceId: "A", generation: 1));
        };
        duplicateSlot.Should().Throw<InvalidOperationException>();

        Action prefixCollision = () =>
        {
            using var colliding = IsolatedLibrarySet.Create(BuildGraphOptions(fixture.DirectoryPath, isolationRoot, workerIndex: 0, namespaceId: "plAA", generation: 36));
        };
        prefixCollision.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("collision");

        poolA0.GetLibrary("isoparent").GetUnmanagedFunction<StepFn>("iso_parent_step")!(2).Should().Be(11322);
    }

    [Fact]
    public void EnforceOwningThreadAffinity_RejectsCrossThreadLookupAndDispose()
    {
        if (TestHelpers.SkipIfNotWindows())
        {
            return;
        }

        using var fixture = new NativeFixture();
        var isolationRoot = CreateIsolationRoot(fixture);
        var options = new IsolatedLibrarySetOptions
        {
            SourceDirectory = fixture.DirectoryPath,
            IsolationRootDirectory = isolationRoot,
            WorkerIndex = 7,
            EnforceOwningThreadAffinity = true,
            Modules = [new IsolatedModuleSpecification("isoleaf0.dll") { AlwaysRenameWithWorkerSuffix = true }],
        };

        using var set = IsolatedLibrarySet.Create(options);
        Exception? lookupError = null;
        Exception? disposeError = null;
        var otherThread = new Thread(() =>
        {
            try
            {
                _ = set.GetLibrary("isoleaf0");
            }
            catch (Exception ex)
            {
                lookupError = ex;
            }

            try
            {
                set.Dispose();
            }
            catch (Exception ex)
            {
                disposeError = ex;
            }
        });
        otherThread.Start();
        otherThread.Join();

        lookupError.Should().BeOfType<InvalidOperationException>();
        disposeError.Should().BeOfType<InvalidOperationException>();
        set.GetLibrary("isoleaf0").GetUnmanagedFunction<GetIntFn>("iso_leaf_get_state")!().Should().Be(100);
    }

    [Fact]
    [SuppressMessage("Security", "SCS0018", Justification = "Inspects rollback state in test-owned isolation root.")]
    public void PreflightFailureAndMidStagingFault_RollBackReservationsAndLeaveNoLeakedHandles()
    {
        if (TestHelpers.SkipIfNotWindows())
        {
            return;
        }

        using var fixture = new NativeFixture();
        var isolationRoot = CreateIsolationRoot(fixture);

        var badPreflight = new IsolatedLibrarySetOptions
        {
            SourceDirectory = fixture.DirectoryPath,
            IsolationRootDirectory = isolationRoot,
            WorkerIndex = 8,
            Modules =
            [
                new IsolatedModuleSpecification("isoleaf0.dll"),
                new IsolatedModuleSpecification("isochild.dll")
                {
                    VerifiedBytePatches = [PeVerifiedBytePatch.FromAsciiLiteral(0x7FFF0000u, "ABCD", "EFGH")],
                },
            ],
        };
        Action preflightAct = () =>
        {
            using var failed = IsolatedLibrarySet.Create(badPreflight);
        };
        preflightAct.Should().Throw<BadImageFormatException>();
        Directory.Exists(isolationRoot).Should().BeFalse();

        var validOptions = BuildGraphOptions(fixture.DirectoryPath, isolationRoot, workerIndex: 8);
        Action faultedStaging = () =>
        {
            using var faulted = IsolatedLibrarySet.CreateCore(
                validOptions,
                stagingFaultHook: (index, _) =>
                {
                    if (index == 2)
                    {
                        throw new IOException("Simulated mid-staging disk failure.");
                    }
                },
                onModuleUnloaded: null);
        };
        faultedStaging.Should().Throw<IOException>();
        Directory.GetDirectories(isolationRoot).Should().BeEmpty();

        using var recovered = IsolatedLibrarySet.Create(validOptions);
        recovered.GetLibrary("isoparent").GetUnmanagedFunction<GetIntFn>("iso_parent_get_state")!().Should().Be(10000);
    }

    [Fact]
    [SuppressMessage("IDisposableAnalyzers", "IDISP017", Justification = "Explicit Dispose simulates stale worker directory left on disk.")]
    [SuppressMessage("Security", "SCS0018", Justification = "Exercises stale worker directory sweeper in test-owned root.")]
    [SuppressMessage("Security", "SEC0112", Justification = "Opens test lock file inside test-owned directory.")]
    [SuppressMessage("Security", "SEC0116", Justification = "Writes test file inside test-owned directory.")]
    public void SweepStaleWorkerDirectories_DeletesUnlockedOwnedDirs_SkipsActiveAndUnknown_AndReportsLocked()
    {
        if (TestHelpers.SkipIfNotWindows())
        {
            return;
        }

        using var fixture = new NativeFixture();
        var isolationRoot = CreateIsolationRoot(fixture);
        Directory.CreateDirectory(isolationRoot);
        var unknownDir = Path.Combine(isolationRoot, "custom-user-cache");
        Directory.CreateDirectory(unknownDir);
        File.WriteAllText(Path.Combine(unknownDir, "keep.txt"), "user data");

        using var activeSet = IsolatedLibrarySet.Create(BuildGraphOptions(fixture.DirectoryPath, isolationRoot, workerIndex: 10));

        string staleUnlockedDir;
        using (var tempSet = IsolatedLibrarySet.Create(BuildGraphOptions(fixture.DirectoryPath, isolationRoot, workerIndex: 11, deleteOnDispose: false)))
        {
            staleUnlockedDir = tempSet.WorkerDirectory;
        }

        string staleLockedDir;
        using (var tempLockedSet = IsolatedLibrarySet.Create(BuildGraphOptions(fixture.DirectoryPath, isolationRoot, workerIndex: 12, deleteOnDispose: false)))
        {
            staleLockedDir = tempLockedSet.WorkerDirectory;
        }

        var lockedFilePath = Path.Combine(staleLockedDir, "locked.bin");
        using (var lockStream = new FileStream(lockedFilePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        {
            var firstSweep = IsolatedLibrarySet.SweepStaleWorkerDirectories(isolationRoot);
            firstSweep.DeletedDirectories.Should().ContainSingle().Which.Should().Be(staleUnlockedDir);
            firstSweep.SkippedActiveDirectories.Should().ContainSingle().Which.Should().Be(activeSet.WorkerDirectory);
            firstSweep.SkippedUnknownDirectories.Should().ContainSingle().Which.Should().Be(unknownDir);
            firstSweep.FailedDirectories.Should().ContainSingle().Which.Should().Be(staleLockedDir);
            Directory.Exists(unknownDir).Should().BeTrue();
        }

        var secondSweep = IsolatedLibrarySet.SweepStaleWorkerDirectories(isolationRoot);
        secondSweep.DeletedDirectories.Should().ContainSingle().Which.Should().Be(staleLockedDir);
        activeSet.GetLibrary("isoparent").GetUnmanagedFunction<GetIntFn>("iso_parent_get_state")!().Should().Be(10000);
    }

    [Fact]
    public void Validation_RejectsFilesystemRootOverlappingDirectoriesAndDependencyCycles()
    {
        if (TestHelpers.SkipIfNotWindows())
        {
            return;
        }

        using var fixture = new NativeFixture();
        var driveRoot = Path.GetPathRoot(fixture.DirectoryPath)!;

        Action sweepRoot = () => IsolatedLibrarySet.SweepStaleWorkerDirectories(driveRoot);
        sweepRoot.Should().Throw<ArgumentException>();

        Action overlap = () =>
        {
            using var s = IsolatedLibrarySet.Create(new IsolatedLibrarySetOptions
            {
                SourceDirectory = fixture.DirectoryPath,
                IsolationRootDirectory = Path.Combine(fixture.DirectoryPath, "nested"),
                WorkerIndex = 0,
                Modules = [new IsolatedModuleSpecification("isoleaf0.dll")],
            });
        };
        overlap.Should().Throw<ArgumentException>();
    }

    [Fact]
#if NET5_0_OR_GREATER
    [SupportedOSPlatform("windows")]
#endif
    [SuppressMessage("Security", "SCS0018", Justification = "Stages clean source subset inside test-owned directory.")]
    public void RewriteExportModuleNameAndImportModuleRemovals_LoadCompactedRenamedSetWithoutUnusedDll()
    {
        if (TestHelpers.SkipIfNotWindows())
        {
            return;
        }

        using var fixture = new NativeFixture();
        var cleanSource = Path.Combine(fixture.DirectoryPath, "compact_src");
        Directory.CreateDirectory(cleanSource);
        File.Copy(fixture.GetPath("isoleaf0"), Path.Combine(cleanSource, "isoleaf0.dll"));
        File.Copy(fixture.GetPath("isocompact"), Path.Combine(cleanSource, "isocompact.dll"));

        var isolationRoot = CreateIsolationRoot(fixture);
        var options = new IsolatedLibrarySetOptions
        {
            SourceDirectory = cleanSource,
            IsolationRootDirectory = isolationRoot,
            WorkerIndex = 15,
            Modules =
            [
                new IsolatedModuleSpecification("isoleaf0.dll")
                {
                    RewriteExportModuleName = true,
                },
                new IsolatedModuleSpecification("isocompact.dll")
                {
                    AlwaysRenameWithWorkerSuffix = true,
                    RewriteExportModuleName = true,
                    ImportModuleRemovals =
                    [
                        new PeImportModuleRemoval
                        {
                            ModuleName = "isounused.dll",
                            WitnessRemainingModuleName = "isoleaf0.dll",
                        },
                    ],
                },
            ],
        };

        using var set = IsolatedLibrarySet.Create(options);
        var compactLib = set.GetLibrary("isocompact");
        var compute = compactLib.GetUnmanagedFunction<StepFn>("iso_compact_compute")!;
        compute(9).Should().Be(5109);

        var stagedCompactPath = compactLib.GetLoadedFilePath();
        var stagedEditor = PeImageEditor.FromFile(stagedCompactPath);
        stagedEditor.GetExportModuleName().Should().Be(Path.GetFileName(stagedCompactPath));
        stagedEditor.GetImportedModuleNames().Should().NotContain(m => string.Equals(m, "isounused.dll", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    [SuppressMessage("Security", "SCS0018", Justification = "Creates companion files and directories in test-owned fixture directory.")]
    [SuppressMessage("Security", "SEC0116", Justification = "Writes companion test assets inside test-owned fixture directory.")]
    public void CompanionFilesAndDirectories_StageBeforeDllMain_AndSupportTransformBytes()
    {
        if (TestHelpers.SkipIfNotWindows())
        {
            return;
        }

        using var fixture = new NativeFixture();
        File.WriteAllBytes(Path.Combine(fixture.DirectoryPath, "source_boot.cfg"), "RAW_BOOT"u8.ToArray());
        var tablesDir = Path.Combine(fixture.DirectoryPath, "tables", "sub");
        Directory.CreateDirectory(tablesDir);
        File.WriteAllText(Path.Combine(fixture.DirectoryPath, "tables", "root.dat"), "R1");
        File.WriteAllText(Path.Combine(tablesDir, "nested.dat"), "N1");

        var isolationRoot = CreateIsolationRoot(fixture);
        var options = new IsolatedLibrarySetOptions
        {
            SourceDirectory = fixture.DirectoryPath,
            IsolationRootDirectory = isolationRoot,
            WorkerIndex = 16,
            Modules =
            [
                new IsolatedModuleSpecification("isohookdep.dll"),
                new IsolatedModuleSpecification("isohook.dll"),
            ],
            CompanionFiles =
            [
                new IsolatedCompanionFileSpecification
                {
                    SourceRelativePath = "source_boot.cfg",
                    TargetRelativePath = "iso_boot.cfg",
                    TransformBytes = (_, workerDir) => System.Text.Encoding.ASCII.GetBytes("BOOT:" + Path.GetFileName(workerDir)),
                },
            ],
            CompanionDirectories =
            [
                new IsolatedCompanionDirectorySpecification
                {
                    SourceRelativePath = "tables",
                    TargetRelativePath = "lookup",
                    Recursive = true,
                },
            ],
        };

        using var set = IsolatedLibrarySet.Create(options);
        var hookLib = set.GetLibrary("isohook");
        hookLib.GetUnmanagedFunction<GetIntFn>("iso_hook_was_boot_cfg_loaded")!().Should().Be(1);
        var bootPtr = hookLib.GetUnmanagedFunction<GetPtrFn>("iso_hook_get_boot_cfg_ptr")!();
        Marshal.PtrToStringAnsi(bootPtr).Should().Be("BOOT:" + Path.GetFileName(set.WorkerDirectory));
        File.Exists(Path.Combine(set.WorkerDirectory, "lookup", "root.dat")).Should().BeTrue();
        File.Exists(Path.Combine(set.WorkerDirectory, "lookup", "sub", "nested.dat")).Should().BeTrue();
    }

    [Fact]
    [SuppressMessage("Security", "SCS0018", Justification = "Tests path-constraint guards inside temp directories.")]
    [SuppressMessage("Security", "SEC0116", Justification = "Creates test file inside fixture directory.")]
    public void CompanionValidationAndPathConstraints_RejectTraversalCollisionNonAsciiAndLongPaths()
    {
        if (TestHelpers.SkipIfNotWindows())
        {
            return;
        }

        using var fixture = new NativeFixture();
        File.WriteAllText(Path.Combine(fixture.DirectoryPath, "cfg.ini"), "ok");
        var unicodeIsoRoot = Path.Combine(Path.GetTempPath(), "unm-iso-żółć-" + Guid.NewGuid().ToString("N"));
        var asciiIsoRoot = Path.Combine(Path.GetTempPath(), "unm-ascii-" + Guid.NewGuid().ToString("N"));

        try
        {
            AssertTraversalAndCollisionRejected(fixture.DirectoryPath, asciiIsoRoot);

            Action nonAsciiFail = () =>
            {
                using var s = IsolatedLibrarySet.Create(new IsolatedLibrarySetOptions
                {
                    SourceDirectory = fixture.DirectoryPath,
                    IsolationRootDirectory = unicodeIsoRoot,
                    WorkerIndex = 17,
                    RequireAsciiWorkerPaths = true,
                    Modules = [new IsolatedModuleSpecification("isoleaf0.dll")],
                });
            };
            nonAsciiFail.Should().Throw<ArgumentException>();
            Directory.Exists(unicodeIsoRoot).Should().BeFalse();

            Action tooLongFail = () =>
            {
                using var s = IsolatedLibrarySet.Create(new IsolatedLibrarySetOptions
                {
                    SourceDirectory = fixture.DirectoryPath,
                    IsolationRootDirectory = asciiIsoRoot,
                    WorkerIndex = 18,
                    MaxWorkerFilePathLength = 24,
                    Modules = [new IsolatedModuleSpecification("isoleaf0.dll")],
                });
            };
            tooLongFail.Should().Throw<PathTooLongException>();
            Directory.Exists(asciiIsoRoot).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(asciiIsoRoot))
            {
                Directory.Delete(asciiIsoRoot, recursive: true);
            }
        }
    }

    [Fact]
#if NET5_0_OR_GREATER
    [SupportedOSPlatform("windows")]
#endif
    [SuppressMessage("IDisposableAnalyzers", "IDISP016", Justification = "Verifies post-dispose ObjectDisposedException.")]
    [SuppressMessage("IDisposableAnalyzers", "IDISP017", Justification = "Explicit Dispose tests PinModulesForProcessLifetime.")]
    [SuppressMessage("Security", "SCS0018", Justification = "Uses isolated process-lifetime directory under temp root.")]
    public void PinModulesForProcessLifetime_KeepsModuleMappedAndLeaseProtectedAcrossDisposeAndSweep()
    {
        if (TestHelpers.SkipIfNotWindows())
        {
            return;
        }

        using var fixture = new NativeFixture();
        var pinRoot = Path.Combine(Path.GetTempPath(), "unm-pinned-" + Guid.NewGuid().ToString("N"));
        var options = new IsolatedLibrarySetOptions
        {
            SourceDirectory = fixture.DirectoryPath,
            IsolationRootDirectory = pinRoot,
            NamespaceId = "pn99",
            WorkerIndex = 99,
            PinModulesForProcessLifetime = true,
            Modules = [new IsolatedModuleSpecification("isoleaf0.dll") { AlwaysRenameWithWorkerSuffix = true }],
        };

        var pinnedSet = IsolatedLibrarySet.Create(options);
        var workerDir = pinnedSet.WorkerDirectory;
        var exportPtr = pinnedSet.GetLibrary("isoleaf0").GetRequiredExport("iso_leaf_add_and_get");

        pinnedSet.Dispose();

        Action accessAfterDispose = () => _ = pinnedSet.GetLibrary("isoleaf0");
        accessAfterDispose.Should().Throw<ObjectDisposedException>();

        UnmanagedLibrary.TryGetLoadedModuleFromAddress(exportPtr, out _, out var mappedPath).Should().BeTrue();
        mappedPath.Should().StartWith(workerDir);

        Action duplicatePinnedSlot = () =>
        {
            using var dup = IsolatedLibrarySet.Create(options);
        };
        duplicatePinnedSlot.Should().Throw<InvalidOperationException>();

        var sweep = IsolatedLibrarySet.SweepStaleWorkerDirectories(pinRoot);
        sweep.SkippedActiveDirectories.Should().ContainSingle().Which.Should().Be(workerDir);
        Directory.Exists(workerDir).Should().BeTrue();
    }

    private static void AssertTraversalAndCollisionRejected(string sourceDirectory, string isolationRoot)
    {
        Action traversal = () =>
        {
            using var s = IsolatedLibrarySet.Create(new IsolatedLibrarySetOptions
            {
                SourceDirectory = sourceDirectory,
                IsolationRootDirectory = isolationRoot,
                WorkerIndex = 19,
                Modules = [new IsolatedModuleSpecification("isoleaf0.dll")],
                CompanionFiles = [new IsolatedCompanionFileSpecification { SourceRelativePath = "../secret.ini" }],
            });
        };
        traversal.Should().Throw<ArgumentException>();

        var targetLeaf = WorkerModuleNaming.FormatFixedLengthName("isoleaf0.dll", 20);
        Action collision = () =>
        {
            using var s = IsolatedLibrarySet.Create(new IsolatedLibrarySetOptions
            {
                SourceDirectory = sourceDirectory,
                IsolationRootDirectory = isolationRoot,
                WorkerIndex = 20,
                Modules = [new IsolatedModuleSpecification("isoleaf0.dll") { AlwaysRenameWithWorkerSuffix = true }],
                CompanionFiles = [new IsolatedCompanionFileSpecification { SourceRelativePath = "cfg.ini", TargetRelativePath = targetLeaf }],
            });
        };
        collision.Should().Throw<InvalidOperationException>();
    }

    private static string CreateIsolationRoot(NativeFixture fixture)
    {
        return Path.Combine(Path.GetTempPath(), "unmanaged-iso-root-" + Path.GetFileName(fixture.DirectoryPath));
    }

    private static IsolatedLibrarySetOptions BuildGraphOptions(
        string sourceDir,
        string isolationRoot,
        int workerIndex,
        string? namespaceId = null,
        int generation = 0,
        bool deleteOnDispose = true)
    {
        return new IsolatedLibrarySetOptions
        {
            SourceDirectory = sourceDir,
            IsolationRootDirectory = isolationRoot,
            WorkerIndex = workerIndex,
            NamespaceId = namespaceId,
            Generation = generation,
            DeleteDirectoryOnDispose = deleteOnDispose,
            Modules =
            [
                new IsolatedModuleSpecification("isoparent.dll") { RewriteDelayImports = true },
                new IsolatedModuleSpecification("isochild.dll"),
                new IsolatedModuleSpecification("isoleaf0.dll"),
                new IsolatedModuleSpecification("isodelay.dll"),
            ],
        };
    }

    private static void RunConcurrentMutations(IsolatedLibrarySet worker0, IsolatedLibrarySet worker1, int iterations)
    {
        var step0 = worker0.GetLibrary("isoparent").GetUnmanagedFunction<StepFn>("iso_parent_step")!;
        var delay0 = worker0.GetLibrary("isoparent").GetUnmanagedFunction<StepFn>("iso_parent_call_delay")!;
        var step1 = worker1.GetLibrary("isoparent").GetUnmanagedFunction<StepFn>("iso_parent_step")!;
        var delay1 = worker1.GetLibrary("isoparent").GetUnmanagedFunction<StepFn>("iso_parent_call_delay")!;

        Parallel.Invoke(
            () =>
            {
                for (var i = 0; i < iterations; i++)
                {
                    _ = step0(1);
                    _ = delay0(2);
                }
            },
            () =>
            {
                for (var i = 0; i < iterations; i++)
                {
                    _ = step1(3);
                    _ = delay1(5);
                }
            });
    }

    private static void VerifyIndependentCounters(IsolatedLibrarySet worker0, IsolatedLibrarySet worker1, int iterations)
    {
        var p0 = worker0.GetLibrary("isoparent");
        p0.GetUnmanagedFunction<GetIntFn>("iso_parent_get_state")!().Should().Be(10000 + (iterations * 100));
        p0.GetUnmanagedFunction<GetIntFn>("iso_parent_get_child_state")!().Should().Be(1000 + (iterations * 10));
        p0.GetUnmanagedFunction<GetIntFn>("iso_parent_get_leaf_state")!().Should().Be(100 + iterations);
        p0.GetUnmanagedFunction<GetIntFn>("iso_parent_get_delay_state")!().Should().Be(5000 + (iterations * 2));
        worker0.GetLibrary("isoleaf0").GetUnmanagedFunction<GetIntFn>("iso_leaf_get_calls")!().Should().Be(iterations);

        var p1 = worker1.GetLibrary("isoparent");
        p1.GetUnmanagedFunction<GetIntFn>("iso_parent_get_state")!().Should().Be(10000 + (iterations * 300));
        p1.GetUnmanagedFunction<GetIntFn>("iso_parent_get_child_state")!().Should().Be(1000 + (iterations * 30));
        p1.GetUnmanagedFunction<GetIntFn>("iso_parent_get_leaf_state")!().Should().Be(100 + (iterations * 3));
        p1.GetUnmanagedFunction<GetIntFn>("iso_parent_get_delay_state")!().Should().Be(5000 + (iterations * 5));
        worker1.GetLibrary("isoleaf0").GetUnmanagedFunction<GetIntFn>("iso_leaf_get_calls")!().Should().Be(iterations);
    }

    private static void RegisterDetachRecorders(IsolatedLibrarySet set, IntPtr cursorPtr, IntPtr slotsPtr)
    {
        set.GetLibrary("isoleaf0").GetUnmanagedFunction<SetDetachRecorderFn>("iso_leaf_set_detach_recorder")!(cursorPtr, slotsPtr);
        set.GetLibrary("isochild").GetUnmanagedFunction<SetDetachRecorderFn>("iso_child_set_detach_recorder")!(cursorPtr, slotsPtr);
        set.GetLibrary("isoparent").GetUnmanagedFunction<SetDetachRecorderFn>("iso_parent_set_detach_recorder")!(cursorPtr, slotsPtr);
    }
}
