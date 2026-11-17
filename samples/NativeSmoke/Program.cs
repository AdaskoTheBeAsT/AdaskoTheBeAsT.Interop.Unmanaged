using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using AdaskoTheBeAsT.Interop.Unmanaged;

namespace NativeSmoke;

#pragma warning disable SA1121

[SuppressMessage("Sonar", "S6640", Justification = "Exercises exact C fixture signatures using unmanaged function pointers, with the module owner undisposed.")]
internal static class Program
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Binary(int first, int second);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Unary(int value);

    private static unsafe int Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: NativeSmoke <absolute fixture library path>");
            return 2;
        }

        using var library = new UnmanagedLibrary(args[0]);
        var address = library.GetRequiredExport("fixture_add");
        if (OperatingSystem.IsWindows())
        {
            VerifyWindowsFeatures(library, args[0]);
        }

        using (NativeExecutionGuard.EnterScope())
        {
            var add = (delegate* unmanaged[Cdecl]<int, int, int>)address;
            Check(add(20, 22));
            Check(library.GetUnmanagedFunction<Binary>("fixture_add")!(20, 22));
        }

        using var handle = UnmanagedLibrary.LoadLibrary(args[0]);
        Check(UnmanagedLibrary.GetUnmanagedFunction<Binary>(handle, "fixture_add")!(20, 22));
        if (!UnmanagedLibrary.TryGetExport(handle, "fixture_invoke", out var invokeAddress))
        {
            return 4;
        }

        var invoke = (delegate* unmanaged[Cdecl]<IntPtr, int, int>)invokeAddress;
        Check(invoke((IntPtr)(delegate* unmanaged[Cdecl]<int, int>)&Increment, 41));
        using (var scope = UnmanagedLibrary.PinConcreteDelegate<Unary>(value => value + 1))
        {
            Check(invoke(scope.Ptr, 41));
        }

        if (RuntimeFeature.IsDynamicCodeSupported)
        {
            CheckDynamicCallbacks(invokeAddress);
        }
        else
        {
            CheckDynamicApisAreRejected(address);
        }

        LifetimeChecks.Run(library);
        Console.WriteLine("Native loading, exports, callbacks, and synchronized callback unregistration passed.");
        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Increment(int value)
    {
        return value + 1;
    }

    private static void Check(int result)
    {
        if (result != 42)
        {
            throw new InvalidOperationException("Native smoke test returned an unexpected result.");
        }
    }

    [SupportedOSPlatform("windows")]
    private static void VerifyWindowsFeatures(UnmanagedLibrary library, string fixturePath)
    {
        if (library.BaseAddress == IntPtr.Zero)
        {
            throw new InvalidOperationException("Expected non-zero module BaseAddress on Windows.");
        }

        var loadedPath = library.GetLoadedFilePath();
        var fixtureAdd = library.GetRequiredExport("fixture_add");
        if (string.IsNullOrWhiteSpace(loadedPath)
            || !library.ContainsAddress(fixtureAdd)
            || !UnmanagedLibrary.TryGetLoadedModuleFromAddress(fixtureAdd, out var modBase, out _)
            || modBase != library.BaseAddress
            || UnmanagedLibrary.GetRequiredSystemExport("kernel32.dll", "CompareStringW") == IntPtr.Zero)
        {
            throw new InvalidOperationException("Module provenance or system export verification failed.");
        }

        var identity = LoadedModuleMemory.Inspect(library);
        LoadedModuleMemory.VerifyIdentity(library, identity);

        var editor = PeImageEditor.FromFile(fixturePath, PeImageValidationOptions.ForCurrentProcessArchitecture());
        if (editor.SizeOfImage == 0 || editor.TimeDateStamp != identity.TimeDateStamp || editor.GetExportModuleName() is null)
        {
            throw new InvalidOperationException("Expected matching PE metadata and export directory name.");
        }

        VerifySlotCacheScratchAndByteStringHelpers();

        if (!string.Equals(WorkerModuleNaming.FormatFixedLengthName("DFORRT.dll", 0), "DFOR00.dll", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Unexpected WorkerModuleNaming output.");
        }
    }

    private static void VerifySlotCacheScratchAndByteStringHelpers()
    {
        var allocator = new WorkerSlotAllocator(maxExclusive: 4, excludedSlots: [0], preferThreadStickySlot: true);
        using (var slot = allocator.Acquire())
        {
            if (slot.SlotIndex != 1)
            {
                throw new InvalidOperationException("Unexpected initial WorkerSlotAllocator index.");
            }
        }

        Span<byte> shortBuf = stackalloc byte[12];
        NativeByteString.WriteShortStringAscii("CFG01", shortBuf);
        if (!string.Equals(NativeByteString.ReadShortStringAscii(shortBuf), "CFG01", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("NativeByteString ShortString round-trip failed.");
        }

        var cache = PinnedLibraryCache.CreateOrdinalIgnoreCase<string>();
        if (!string.Equals(cache.GetOrLoad("k1", static key => key.ToUpperInvariant()), "K1", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("PinnedLibraryCache GetOrLoad failed.");
        }

        using var scratch = new UnmanagedScratchBuffer(16);
        using var pointerLease = scratch.AcquirePointerLease();
        if (pointerLease.Pointer == IntPtr.Zero || pointerLease.ByteLength != 16)
        {
            throw new InvalidOperationException("UnmanagedScratchBuffer PointerLease failed.");
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The known callback is Func<int,int>; its signature is preserved by this concrete call site.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Called only when RuntimeFeature.IsDynamicCodeSupported is true.")]
    [DynamicDependency("Invoke", typeof(Func<int, int>))]
    private static unsafe void CheckDynamicCallbacks(IntPtr address)
    {
        using var scope = UnmanagedLibrary.PinDelegate<Func<int, int>>(value => value + 1);
        var invoke = (delegate* unmanaged[Cdecl]<IntPtr, int, int>)address;
        Check(invoke(scope.Ptr, 41));
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Negative test: the runtime capability guard throws before reflection.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Negative test: these APIs must throw before attempting code generation on NativeAOT.")]
    private static void CheckDynamicApisAreRejected(IntPtr address)
    {
        try
        {
            _ = UnmanagedLibrary.GetDelegateForFunctionPointer<Func<int, int, int>>(address, CallingConvention.Cdecl);
            throw new InvalidOperationException("The dynamic thunk API did not reject NativeAOT.");
        }
        catch (PlatformNotSupportedException error)
        {
            Console.WriteLine(error.Message);
        }

        try
        {
            using var scope = UnmanagedLibrary.PinDelegate<Func<int, int>>(value => value + 1);
            throw new InvalidOperationException("The generic callback API did not reject NativeAOT.");
        }
        catch (PlatformNotSupportedException error)
        {
            Console.WriteLine(error.Message);
        }
    }
}
