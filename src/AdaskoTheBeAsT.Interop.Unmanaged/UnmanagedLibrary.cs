using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
#if NET10_0_OR_GREATER
using System.Runtime.Versioning;
#endif

namespace AdaskoTheBeAsT.Interop.Unmanaged;

#pragma warning disable SA1121

/// <summary>
/// Represents a loaded native dynamic library and exposes its exported functions as managed delegates.
/// </summary>
/// <remarks>
/// Works on Windows (via <c>LoadLibraryEx</c>/<c>GetProcAddress</c>/<c>FreeLibrary</c>), Linux and
/// macOS (via <c>NativeLibrary</c> on modern .NET or a legacy <c>dlopen</c> fallback).
/// Non-Windows loading ignores <see cref="LoadLibraryFlags"/> and performs a normal load;
/// only the legacy fallback explicitly specifies <c>RTLD_NOW</c>.
/// This type owns the loaded module handle and frees it when disposed. Any function pointer or
/// object obtained from the library becomes unsafe to use after the module is unloaded.
/// </remarks>
/// <remarks>
/// Loads a native dynamic library and transfers ownership of the resulting module handle to this instance.
/// </remarks>
/// <param name="fileName">
/// Module name or fully qualified path of the library to load. With the default flags on
/// Windows, the system searches <c>System32</c> for bare module names and uses the DLL
/// directory for dependency resolution when a fully qualified path is provided. On Linux and
/// macOS, the platform runtime controls name resolution. Relative paths with directory
/// components are not automatically converted to absolute paths.
/// </param>
/// <param name="flags">Flags passed to <c>LoadLibraryEx</c> on Windows. Ignored on Linux and macOS.</param>
/// <exception cref="ArgumentException">
/// Thrown when <paramref name="fileName"/> is null, empty, whitespace-only, or contains NUL.
/// </exception>
/// <exception cref="Win32Exception">Thrown when the library cannot be loaded.</exception>
public sealed partial class UnmanagedLibrary(
    string fileName,
    LoadLibraryFlags flags = LoadLibraryFlags.LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR
                             | LoadLibraryFlags.LOAD_LIBRARY_SEARCH_SYSTEM32) : IDisposable
{
    private static readonly ConcurrentDictionary<string, IntPtr> _pinnedSystemModules =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Unmanaged resource. CLR will ensure SafeHandles get freed, without requiring a finalizer on this class.
    /// </summary>
    private readonly SafeLibraryHandle _safeLibraryHandle = LoadLibraryCore(fileName, flags);

    /// <summary>
    /// Gets the mapped module base address on Windows.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    /// <exception cref="PlatformNotSupportedException">
    /// Thrown on non-Windows platforms where loader handles are opaque cookies rather than mapped PE image bases.
    /// </exception>
    /// <remarks>
    /// The returned address remains valid only while this <see cref="UnmanagedLibrary"/> instance remains alive
    /// and undisposed. Handle reference counting guards only the address query itself and does not grant a
    /// permanent pointer lease across subsequent memory accesses; callers must prevent concurrent disposal while
    /// dereferencing module memory.
    /// </remarks>
    public IntPtr BaseAddress => GetBaseAddress(_safeLibraryHandle);

    internal SafeLibraryHandle SafeHandle => _safeLibraryHandle;

    /// <summary>
    /// Loads a native dynamic library and returns a safe handle that owns the module.
    /// </summary>
    /// <param name="fileName">
    /// Module name or fully qualified path of the library to load. With the default flags on
    /// Windows, the system searches <c>System32</c> for bare module names and uses the DLL
    /// directory for dependency resolution when a fully qualified path is provided. On Linux and
    /// macOS, the platform runtime controls name resolution. Relative paths with directory
    /// components are not automatically converted to absolute paths.
    /// </param>
    /// <param name="flags">Flags passed to <c>LoadLibraryEx</c> on Windows. Ignored on Linux and macOS.</param>
    /// <returns>A safe handle for the loaded module.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="fileName"/> is null, empty, whitespace-only, or contains NUL.
    /// </exception>
    /// <exception cref="Win32Exception">Thrown when the library cannot be loaded.</exception>
    public static SafeLibraryHandle LoadLibrary(
        string fileName,
        LoadLibraryFlags flags = LoadLibraryFlags.LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR
                                 | LoadLibraryFlags.LOAD_LIBRARY_SEARCH_SYSTEM32)
    {
        return LoadLibraryCore(fileName, flags);
    }

    /// <summary>
    /// Releases a module handle previously returned by <see cref="LoadLibrary(string, LoadLibraryFlags)"/>.
    /// </summary>
    /// <param name="safeLibraryHandle">
    /// Handle to release. If <see langword="null"/> or already closed, the method does nothing.
    /// </param>
    public static void FreeLibrary(SafeLibraryHandle? safeLibraryHandle)
    {
        if (safeLibraryHandle?.IsClosed != false)
        {
            return;
        }

#pragma warning disable IDISP007
        safeLibraryHandle.Dispose();
#pragma warning restore IDISP007
    }

    /// <summary>
    /// Gets the mapped module base address for a loaded Windows module handle.
    /// </summary>
    /// <param name="safeLibraryHandle">Handle for the loaded module.</param>
    /// <returns>The mapped module base address with any low-bit data-file or image-resource loader tags cleared.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="safeLibraryHandle"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// Thrown when <paramref name="safeLibraryHandle"/> has been closed or disposed.
    /// </exception>
    /// <exception cref="PlatformNotSupportedException">
    /// Thrown on non-Windows platforms where loader handles are opaque cookies rather than mapped PE image bases.
    /// </exception>
    /// <remarks>
    /// The returned address remains valid only while <paramref name="safeLibraryHandle"/> remains undisposed.
    /// Handle reference counting protects only the lookup itself and does not establish a pointer lease across
    /// subsequent memory reads or writes; callers must coordinate module lifetime against concurrent disposal.
    /// </remarks>
    public static IntPtr GetBaseAddress(SafeLibraryHandle safeLibraryHandle)
    {
        ThrowIfNull(safeLibraryHandle, nameof(safeLibraryHandle));

        var addedRef = false;
        try
        {
            safeLibraryHandle.DangerousAddRef(ref addedRef);
            if (!NativeLoader.IsWindows())
            {
                throw new PlatformNotSupportedException(
                    "Module base address introspection is supported only on Windows where module handles map to the PE image base.");
            }

#pragma warning disable S3869
            var rawHandle = safeLibraryHandle.DangerousGetHandle();
#pragma warning restore S3869
            const long loaderTagMask = ~3L;
            return IntPtr.Size == 8
                ? new IntPtr(rawHandle.ToInt64() & loaderTagMask)
                : new IntPtr(rawHandle.ToInt32() & ~3);
        }
        finally
        {
            if (addedRef)
            {
                safeLibraryHandle.DangerousRelease();
            }
        }
    }

    /// <summary>
    /// Looks up an exported function on a loaded module handle and marshals it as a managed delegate.
    /// </summary>
    /// <typeparam name="TDelegate">Delegate type that matches the unmanaged signature.</typeparam>
    /// <param name="safeLibraryHandle">Handle for the loaded module that owns the export.</param>
    /// <param name="functionName">Case-sensitive export name to look up.</param>
    /// <returns>The requested delegate, or <see langword="null"/> when the export is not found.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="safeLibraryHandle"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="functionName"/> is null, empty, whitespace-only, or contains NUL.
    /// </exception>
    /// <remarks>
    /// Keep <paramref name="safeLibraryHandle"/> alive for at least as long as the returned
    /// delegate or any objects created by the delegate may be used.
    /// </remarks>
    public static TDelegate? GetUnmanagedFunction<TDelegate>(
        SafeLibraryHandle safeLibraryHandle,
        string functionName)
        where TDelegate : Delegate
    {
        ThrowIfNull(safeLibraryHandle, nameof(safeLibraryHandle));
        ValidateTextArgument(functionName, nameof(functionName));
        return GetUnmanagedFunctionCore<TDelegate>(safeLibraryHandle, functionName);
    }

    /// <summary>
    /// Tries to look up an exported symbol on a loaded module handle and returns its raw address.
    /// </summary>
    /// <param name="safeLibraryHandle">Handle for the loaded module that owns the export.</param>
    /// <param name="functionName">Case-sensitive export name to look up.</param>
    /// <param name="address">
    /// When this method returns, contains the native address of the export, or
    /// <see cref="IntPtr.Zero"/> when the export was not found.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the export was found; <see langword="false"/> otherwise.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="safeLibraryHandle"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="functionName"/> is null, empty, whitespace-only, or contains NUL.
    /// </exception>
    /// <remarks>
    /// This overload does not allocate a managed delegate and is the preferred way to obtain a
    /// pointer that will be cast to a C# unmanaged function pointer
    /// (for example <c>delegate* unmanaged[Stdcall]&lt;uint&gt;</c>) on <c>net5.0</c> or newer.
    /// Keep <paramref name="safeLibraryHandle"/> alive for as long as the returned address is used.
    /// </remarks>
    public static bool TryGetExport(
        SafeLibraryHandle safeLibraryHandle,
        string functionName,
        out IntPtr address)
    {
        ThrowIfNull(safeLibraryHandle, nameof(safeLibraryHandle));
        ValidateTextArgument(functionName, nameof(functionName));

        var addedRef = false;
        try
        {
            safeLibraryHandle.DangerousAddRef(ref addedRef);
#pragma warning disable S3869
            address = NativeLoader.GetExport(safeLibraryHandle.DangerousGetHandle(), functionName);
#pragma warning restore S3869
            return address != IntPtr.Zero;
        }
        finally
        {
            if (addedRef)
            {
                safeLibraryHandle.DangerousRelease();
            }
        }
    }

    /// <summary>
    /// Looks up a required exported symbol on a loaded module handle and returns its raw address.
    /// </summary>
    /// <param name="safeLibraryHandle">Handle for the loaded module that owns the export.</param>
    /// <param name="functionName">Case-sensitive export name to look up.</param>
    /// <returns>The native address of the requested export.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="safeLibraryHandle"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="functionName"/> is null, empty, whitespace-only, or contains NUL.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// Thrown when <paramref name="safeLibraryHandle"/> has been closed or disposed.
    /// </exception>
    /// <exception cref="EntryPointNotFoundException">
    /// Thrown when <paramref name="functionName"/> is not exported by the loaded module.
    /// </exception>
    /// <remarks>
    /// The returned address remains valid only while <paramref name="safeLibraryHandle"/> remains alive and
    /// undisposed. Handle reference counting guards only the lookup itself and does not establish a permanent
    /// pointer lease across subsequent invocations; callers must prevent concurrent disposal while calling the export.
    /// </remarks>
    public static IntPtr GetRequiredExport(
        SafeLibraryHandle safeLibraryHandle,
        string functionName)
    {
        if (TryGetExport(safeLibraryHandle, functionName, out var address))
        {
            return address;
        }

        var moduleIdentifier = !string.IsNullOrWhiteSpace(safeLibraryHandle.ModuleName)
            ? safeLibraryHandle.ModuleName
            : "loaded module";
        throw new EntryPointNotFoundException(
            $"Unable to find an entry point named '{functionName}' in native module '{moduleIdentifier}'.");
    }

    /// <summary>
    /// Gets the full filesystem path reported by the Windows loader for <paramref name="safeLibraryHandle"/>.
    /// </summary>
    /// <param name="safeLibraryHandle">Handle for the loaded Windows module.</param>
    /// <returns>The full path returned by <c>GetModuleFileNameW</c>, expanding dynamically beyond <c>MAX_PATH</c> when necessary.</returns>
#if NET10_0_OR_GREATER
    [SupportedOSPlatform("windows")]
#endif
    public static string GetLoadedFilePath(SafeLibraryHandle safeLibraryHandle)
    {
        ThrowIfNull(safeLibraryHandle, nameof(safeLibraryHandle));

        var addedRef = false;
        try
        {
            safeLibraryHandle.DangerousAddRef(ref addedRef);
            if (!NativeLoader.IsWindows())
            {
                throw new PlatformNotSupportedException(
                    "Loaded module path introspection via GetModuleFileNameW is supported only on Windows.");
            }

#pragma warning disable S3869
            var rawHandle = safeLibraryHandle.DangerousGetHandle();
#pragma warning restore S3869
            if (rawHandle == IntPtr.Zero
                || (rawHandle.ToInt64() & 3L) != 0L)
            {
                throw new InvalidOperationException(
                    "GetLoadedFilePath requires a standard mapped Windows module handle and rejects data-file or image-resource mappings.");
            }

            return QueryModuleFileName(rawHandle);
        }
        finally
        {
            if (addedRef)
            {
                safeLibraryHandle.DangerousRelease();
            }
        }
    }

    /// <summary>
    /// Resolves which loaded Windows module backs a native code or data <paramref name="address"/>, safely incrementing
    /// the target module's reference count while reading its path.
    /// </summary>
    /// <param name="address">Native code, export, callback, or vtable address to inspect.</param>
    /// <param name="moduleBaseAddress">Receives the owning module's base address when found; otherwise <see cref="IntPtr.Zero"/>.</param>
    /// <param name="moduleFilePath">Receives the owning module's loaded filesystem path when found; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if <paramref name="address"/> belongs to a loaded Windows module; otherwise <see langword="false"/>.</returns>
#if NET10_0_OR_GREATER
    [SupportedOSPlatform("windows")]
#endif
    public static bool TryGetLoadedModuleFromAddress(
        IntPtr address,
        out IntPtr moduleBaseAddress,
        out string? moduleFilePath)
    {
        if (!NativeLoader.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Address-to-module provenance introspection is supported only on Windows.");
        }

        if (address == IntPtr.Zero
            || !WindowsNativeMethods.GetModuleHandleExW(
                WindowsNativeMethods.GetModuleHandleExFlagFromAddress,
                address,
                out var moduleHandle)
            || moduleHandle == IntPtr.Zero)
        {
            moduleBaseAddress = IntPtr.Zero;
            moduleFilePath = null;
            return false;
        }

        try
        {
            moduleBaseAddress = moduleHandle;
            moduleFilePath = QueryModuleFileName(moduleHandle);
            return true;
        }
        finally
        {
            _ = WindowsNativeMethods.FreeLibrary(moduleHandle);
        }
    }

    /// <summary>
    /// Resolves a required exported symbol from a bare Windows <c>System32</c> DLL using <c>LOAD_LIBRARY_SEARCH_SYSTEM32</c>,
    /// preventing working-directory DLL planting and keeping the system module pinned for process lifetime.
    /// </summary>
    /// <param name="systemModuleName">Bare Windows system DLL filename (for example <c>"kernel32.dll"</c> or <c>"ole32.dll"</c>) without path separators.</param>
    /// <param name="exportName">Case-sensitive ASCII export name.</param>
    /// <returns>The non-zero native address of the requested export.</returns>
#if NET10_0_OR_GREATER
    [SupportedOSPlatform("windows")]
#endif
    public static IntPtr GetRequiredSystemExport(
        string systemModuleName,
        string exportName)
    {
        ValidateBareSystemModuleName(systemModuleName, nameof(systemModuleName));
        ValidateTextArgument(exportName, nameof(exportName));

        if (!NativeLoader.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "System32 export resolution via LOAD_LIBRARY_SEARCH_SYSTEM32 is supported only on Windows.");
        }

        var moduleHandle = _pinnedSystemModules.GetOrAdd(
            systemModuleName,
            static name => NativeLoader.Load(name, LoadLibraryFlags.LOAD_LIBRARY_SEARCH_SYSTEM32));
        var exportAddress = NativeLoader.GetExport(moduleHandle, exportName);
        if (exportAddress == IntPtr.Zero)
        {
            throw new EntryPointNotFoundException(
                $"Unable to find an entry point named '{exportName}' in Windows system module '{systemModuleName}'.");
        }

        return exportAddress;
    }

    /// <summary>
    /// Creates a managed delegate that invokes an unmanaged function pointer using the specified
    /// unmanaged calling convention.
    /// </summary>
    /// <typeparam name="T">Delegate type that describes the unmanaged signature.</typeparam>
    /// <param name="ptr">Pointer to the unmanaged function.</param>
    /// <param name="conv">
    /// Unmanaged calling convention to use for the emitted indirect call (<c>calli</c>).
    /// </param>
    /// <returns>The created delegate.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="ptr"/> is <see cref="IntPtr.Zero"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <typeparamref name="T"/> is not a concrete, closed delegate type.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">The convention is not Cdecl, StdCall, or Winapi.</exception>
    /// <exception cref="NotSupportedException">The signature requires marshaling or is not in the supported raw ABI subset.</exception>
    /// <exception cref="PlatformNotSupportedException">Runtime code generation is unavailable.</exception>
    /// <remarks>
    /// The delegate type should describe the exact parameter and return types of the unmanaged
    /// export. A mismatched signature or calling convention can corrupt the process.
    /// <para>
    /// Unlike <see cref="Marshal.GetDelegateForFunctionPointer{TDelegate}(IntPtr)"/>, this method
    /// emits a raw <c>calli</c> and does not perform parameter marshaling. All parameter and
    /// return types must already be native-compatible: numeric primitives other than bool/char,
    /// pointers, IntPtr/UIntPtr, enums, or nonempty sequential/explicit structs composed of these.
    /// Managed ref/out parameters and returns, decimal, nullable values, MarshalAs, and SetLastError
    /// are rejected. Use byte for an 8-bit native Boolean, int for Windows BOOL, and ushort for a
    /// UTF-16 code unit; native widths depend on the API. For marshaling (for example string to
    /// <c>LPWStr</c>) prefer <see cref="Marshal.GetDelegateForFunctionPointer{TDelegate}(IntPtr)"/>
    /// with an <see cref="UnmanagedFunctionPointerAttribute"/> on the delegate type instead.
    /// </para>
    /// <para>
    /// <b>Preferred alternatives:</b>
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// Use <see cref="Marshal.GetDelegateForFunctionPointer{TDelegate}(IntPtr)"/> when you need
    /// automatic parameter marshaling and the calling convention is known at compile time
    /// (declare it via <see cref="UnmanagedFunctionPointerAttribute"/> on the delegate).
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// On <c>net5.0</c> and newer, prefer C# unmanaged function pointers, for example
    /// <c>delegate* unmanaged[Stdcall]&lt;uint&gt;</c>. They avoid delegate allocation and
    /// support AOT, but require an exact signature, calling convention, and module lifetime. Obtain the raw
    /// <see cref="IntPtr"/> with <see cref="TryGetExport(string, out IntPtr)"/> (or the static
    /// overload) and cast it directly to the desired function pointer type inside an
    /// <see langword="unsafe"/> block.
    /// </description>
    /// </item>
    /// </list>
    /// This method remains useful when you need to pick the unmanaged calling convention at
    /// runtime for a signature that contains only native-compatible types.
    /// </para>
    /// </remarks>
#if NET8_0_OR_GREATER
    [RequiresDynamicCode(
        "Creates an IL-emitted unmanaged call thunk. Use TryGetExport and an unmanaged function pointer instead.")]
    [RequiresUnreferencedCode(
        "Inspects delegate signatures and nested struct fields. Preserve all signature metadata.")]
#endif
    public static T? GetDelegateForFunctionPointer<T>(
        IntPtr ptr,
        CallingConvention conv)
        where T : class
    {
        if (ptr == IntPtr.Zero)
        {
            throw new ArgumentException("Value cannot be zero.", nameof(ptr));
        }

        return CreateRawDelegate<T>(ptr, conv);
    }

    /// <summary>
    /// Creates an unmanaged function pointer for a managed delegate and returns a keep-alive object
    /// for the callback lifetime.
    /// </summary>
    /// <typeparam name="T">Delegate type.</typeparam>
    /// <param name="delegateCallback">Managed delegate to expose as an unmanaged callback.</param>
    /// <param name="binder">
    /// Object that keeps the delegate alive. Hold a reference to this object for as long as
    /// unmanaged code may call the returned pointer.
    /// </param>
    /// <returns>Function pointer that can be passed to unmanaged code.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="delegateCallback"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// For delegates that cannot be marshaled directly (constructed generic delegate types such as
    /// <c>Func&lt;T, TResult&gt;</c>), this method creates a runtime proxy delegate and stores
    /// both delegates inside <paramref name="binder"/>. If the source delegate type declares
    /// an <see cref="UnmanagedFunctionPointerAttribute"/>, that attribute is copied onto the
    /// proxy so the native calling convention is preserved. Parameter and return-value
    /// marshaling attributes and the full invocation list are also preserved.
    /// <para>
    /// <b>Preferred alternatives:</b>
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// Declare a concrete (non-generic) delegate type annotated with
    /// <see cref="UnmanagedFunctionPointerAttribute"/> (for example
    /// <c>[UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Callback(int a, int b);</c>)
    /// and call <see cref="Marshal.GetFunctionPointerForDelegate(Delegate)"/> directly. That
    /// avoids IL emission entirely and is more AOT-friendly.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// On <c>net5.0</c> and newer, prefer <c>System.Runtime.InteropServices.UnmanagedCallersOnlyAttribute</c>
    /// on a <see langword="static"/> method together with a C# unmanaged function pointer
    /// (<c>delegate* unmanaged[Cdecl]&lt;int, int, int&gt;</c>). This allocates no delegate,
    /// needs no keep-alive <paramref name="binder"/>, and is fully AOT-compatible.
    /// </description>
    /// </item>
    /// </list>
    /// Use this method only when you need to pass a closure (capturing delegate) or a generic
    /// delegate type to unmanaged code on older runtimes that cannot use the options above.
    /// </para>
    /// </remarks>
#if NET8_0_OR_GREATER
    [RequiresDynamicCode(
        "Constructed generic callbacks require a runtime proxy. Use PinConcreteDelegate or UnmanagedCallersOnly instead.")]
    [RequiresUnreferencedCode(
        "Generic callback proxies inspect the runtime delegate type and its marshaling metadata.")]
#endif
    public static IntPtr GetFunctionPointerForDelegate<T>(
        T delegateCallback,
        out object binder)
        where T : class, Delegate
    {
        ThrowIfNull(delegateCallback, nameof(delegateCallback));
        Delegate del = delegateCallback;

        if (del.GetType().IsGenericType)
        {
            return GetFunctionPointerForGenericDelegate(del, out binder);
        }

        var result = Marshal.GetFunctionPointerForDelegate(del);
        binder = del;
        return result;
    }

    /// <summary>
    /// Creates a callback pointer and a scope that roots its managed callback.
    /// </summary>
    /// <typeparam name="TDelegate">The callback delegate type.</typeparam>
    /// <param name="callback">The callback to expose to native code.</param>
    /// <returns>A keep-alive scope for the callback pointer.</returns>
    /// <remarks>
    /// Dispose is only a keep-alive boundary. Before ending the scope, unregister any retained
    /// native callback and wait for in-flight callbacks. Translate callback exceptions into the
    /// native API's error convention; do not let them escape across the native boundary.
    /// The native module owner must separately remain undisposed while its code may execute.
    /// </remarks>
#if NET8_0_OR_GREATER
    [RequiresDynamicCode(
        "Constructed generic callbacks require a runtime proxy. Use PinConcreteDelegate or UnmanagedCallersOnly instead.")]
    [RequiresUnreferencedCode(
        "Generic callback proxies inspect the runtime delegate type and its marshaling metadata.")]
#endif
    public static DelegatePin PinDelegate<TDelegate>(TDelegate callback)
        where TDelegate : class, Delegate
    {
        ThrowIfNull(callback, nameof(callback));
        var pointer = GetFunctionPointerForDelegate(callback, out var binder);
        return new DelegatePin(pointer, binder);
    }

    /// <summary>
    /// Roots a concrete, non-generic callback without generating a proxy or emitting IL.
    /// </summary>
    /// <typeparam name="TDelegate">A concrete delegate type with native marshaling support.</typeparam>
    /// <param name="callback">Callback to root for the lifetime scope.</param>
    /// <returns>A keep-alive scope; disposing it does not unregister native callbacks.</returns>
    /// <remarks>
    /// NativeAOT requires marshaling support for the concrete delegate signature at compile time.
    /// UnmanagedCallersOnly and raw function pointers avoid delegate marshaling entirely.
    /// </remarks>
    public static DelegatePin PinConcreteDelegate<TDelegate>(TDelegate callback)
        where TDelegate : class, Delegate
    {
        ThrowIfNull(callback, nameof(callback));
        if (typeof(TDelegate) == typeof(Delegate)
            || typeof(TDelegate) == typeof(MulticastDelegate)
            || callback.GetType().IsGenericType)
        {
            throw new ArgumentException("A concrete, non-generic callback delegate is required.", nameof(callback));
        }

        return new DelegatePin(Marshal.GetFunctionPointerForDelegate<TDelegate>(callback), callback);
    }

    /// <summary>
    /// Looks up an exported function in the loaded module and marshals it as a managed delegate.
    /// </summary>
    /// <typeparam name="TDelegate">Delegate type that matches the unmanaged signature.</typeparam>
    /// <param name="functionName">Case-sensitive export name to look up.</param>
    /// <returns>The requested delegate, or <see langword="null"/> when the export is not found.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="functionName"/> is null, empty, whitespace-only, or contains NUL.
    /// </exception>
    /// <remarks>
    /// Keep this instance alive for at least as long as the returned delegate or any objects
    /// created by the delegate may be used. Using a delegate after the library has been unloaded
    /// may appear to work for some system DLLs, but it is not supported.
    /// </remarks>
    public TDelegate? GetUnmanagedFunction<TDelegate>(string functionName)
        where TDelegate : Delegate
    {
        ValidateTextArgument(functionName, nameof(functionName));
        return GetUnmanagedFunctionCore<TDelegate>(_safeLibraryHandle, functionName);
    }

    /// <summary>Gets an exported function as a delegate, or throws if it is missing.</summary>
    /// <typeparam name="TDelegate">Delegate type matching the native signature.</typeparam>
    /// <param name="functionName">Case-sensitive export name to look up.</param>
    /// <returns>The requested delegate. Keep the library open while using it.</returns>
    /// <exception cref="ArgumentException">The export name is invalid.</exception>
    /// <exception cref="ObjectDisposedException">The library has been disposed.</exception>
    /// <exception cref="EntryPointNotFoundException">The loaded library does not export the name.</exception>
    public TDelegate GetRequiredUnmanagedFunction<TDelegate>(string functionName)
        where TDelegate : Delegate =>
        Marshal.GetDelegateForFunctionPointer<TDelegate>(GetRequiredExport(functionName));

    /// <summary>
    /// Tries to look up an exported symbol in the loaded module and returns its raw address.
    /// </summary>
    /// <param name="functionName">Case-sensitive export name to look up.</param>
    /// <param name="address">
    /// When this method returns, contains the native address of the export, or
    /// <see cref="IntPtr.Zero"/> when the export was not found.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the export was found; <see langword="false"/> otherwise.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="functionName"/> is null, empty, whitespace-only, or contains NUL.
    /// </exception>
    /// <remarks>
    /// This method does not allocate a managed delegate and is the preferred way to obtain a
    /// pointer that will be cast to a C# unmanaged function pointer
    /// (for example <c>delegate* unmanaged[Stdcall]&lt;uint&gt;</c>) on <c>net5.0</c> or newer.
    /// Keep this <see cref="UnmanagedLibrary"/> instance alive for as long as the returned address is used.
    /// </remarks>
    public bool TryGetExport(
        string functionName,
        out IntPtr address)
    {
        return TryGetExport(_safeLibraryHandle, functionName, out address);
    }

    /// <summary>
    /// Looks up a required exported symbol in the loaded module and returns its raw address.
    /// </summary>
    /// <param name="functionName">Case-sensitive export name to look up.</param>
    /// <returns>The native address of the requested export.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="functionName"/> is null, empty, whitespace-only, or contains NUL.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// Thrown when this <see cref="UnmanagedLibrary"/> instance has been disposed.
    /// </exception>
    /// <exception cref="EntryPointNotFoundException">
    /// Thrown when <paramref name="functionName"/> is not exported by the loaded module.
    /// </exception>
    /// <remarks>
    /// The returned address remains valid only while this <see cref="UnmanagedLibrary"/> instance remains alive
    /// and undisposed. Handle reference counting guards only the lookup itself and does not grant a permanent
    /// pointer lease across subsequent invocations; callers must coordinate module lifetime against concurrent disposal.
    /// </remarks>
    public IntPtr GetRequiredExport(string functionName)
    {
        return GetRequiredExport(_safeLibraryHandle, functionName);
    }

    /// <summary>
    /// Gets the full filesystem path reported by the Windows loader for this loaded library.
    /// </summary>
    /// <returns>The full path returned by <c>GetModuleFileNameW</c>.</returns>
#if NET10_0_OR_GREATER
    [SupportedOSPlatform("windows")]
#endif
    public string GetLoadedFilePath()
    {
        return GetLoadedFilePath(_safeLibraryHandle);
    }

    /// <summary>
    /// Determines whether <paramref name="address"/> resides within the mapped Windows module owned by this <see cref="UnmanagedLibrary"/>.
    /// </summary>
    /// <param name="address">Native code or data address to test.</param>
    /// <returns><see langword="true"/> if <paramref name="address"/> maps to this loaded module; otherwise <see langword="false"/>.</returns>
#if NET10_0_OR_GREATER
    [SupportedOSPlatform("windows")]
#endif
    public bool ContainsAddress(IntPtr address)
    {
        var addedRef = false;
        try
        {
            _safeLibraryHandle.DangerousAddRef(ref addedRef);
            if (!NativeLoader.IsWindows())
            {
                throw new PlatformNotSupportedException(
                    "Address-to-module provenance introspection is supported only on Windows.");
            }

#pragma warning disable S3869
            var rawHandle = _safeLibraryHandle.DangerousGetHandle();
#pragma warning restore S3869
            if (address == IntPtr.Zero
                || rawHandle == IntPtr.Zero
                || (rawHandle.ToInt64() & 3L) != 0L)
            {
                return false;
            }

            if (!WindowsNativeMethods.GetModuleHandleExW(
                    WindowsNativeMethods.GetModuleHandleExFlagFromAddress,
                    address,
                    out var ownerModule)
                || ownerModule == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                return ownerModule == rawHandle;
            }
            finally
            {
                _ = WindowsNativeMethods.FreeLibrary(ownerModule);
            }
        }
        finally
        {
            if (addedRef)
            {
                _safeLibraryHandle.DangerousRelease();
            }
        }
    }

    /// <summary>
    /// Releases the loaded library handle.
    /// </summary>
    /// <remarks>
    /// After disposal, function pointers previously retrieved from this instance should be treated
    /// as invalid. The method is safe to call multiple times.
    /// </remarks>
    public void Dispose()
    {
        if (!_safeLibraryHandle.IsClosed)
        {
            _safeLibraryHandle.Dispose();
        }
    }

    private static SafeLibraryHandle LoadLibraryCore(
        string fileName,
        LoadLibraryFlags flags)
    {
        ValidateTextArgument(fileName, nameof(fileName));

        var handle = NativeLoader.Load(fileName, flags);
        return new SafeLibraryHandle(handle, ownsHandle: true, fileName);
    }

    private static string QueryModuleFileName(IntPtr moduleHandle)
    {
        const int maxCapacity = 32768;
        var capacity = 260;
        while (true)
        {
            var buffer = Marshal.AllocHGlobal(capacity * sizeof(char));
            try
            {
                var copied = WindowsNativeMethods.GetModuleFileNameW(moduleHandle, buffer, (uint)capacity);
                if (copied == 0)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                if (copied < (uint)capacity)
                {
                    return Marshal.PtrToStringUni(buffer, (int)copied) ?? string.Empty;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            if (capacity >= maxCapacity)
            {
                throw new PathTooLongException(
                    $"Loaded module path exceeded the maximum supported Windows path buffer ({maxCapacity} characters).");
            }

            capacity = Math.Min(maxCapacity, capacity * 2);
        }
    }

    private static void ValidateBareSystemModuleName(
        string? value,
        string paramName)
    {
        ValidateTextArgument(value, paramName);
#pragma warning disable CA2249,MA0001
        if (value!.IndexOf('/') >= 0
            || value.IndexOf('\\') >= 0
            || value.IndexOf(':') >= 0
            || string.Equals(value, ".", StringComparison.Ordinal)
            || string.Equals(value, "..", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "System module name must be a bare file name without directory or drive separators.",
                paramName);
        }
#pragma warning restore CA2249,MA0001
    }

    private static TDelegate? GetUnmanagedFunctionCore<TDelegate>(
        SafeLibraryHandle safeLibraryHandle,
        string functionName)
        where TDelegate : Delegate
    {
        var addedRef = false;
        try
        {
            safeLibraryHandle.DangerousAddRef(ref addedRef);
#pragma warning disable S3869
            var p = NativeLoader.GetExport(safeLibraryHandle.DangerousGetHandle(), functionName);
#pragma warning restore S3869

            // Failure is a common case, especially for adaptive code.
            if (p == IntPtr.Zero)
            {
                return null;
            }

            return Marshal.GetDelegateForFunctionPointer<TDelegate>(p);
        }
        finally
        {
            if (addedRef)
            {
                safeLibraryHandle.DangerousRelease();
            }
        }
    }

    private static void ValidateTextArgument(
        string? value,
        string paramName)
    {
        if (value == null
            || string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", paramName);
        }

#if NET8_0_OR_GREATER
        if (value.Contains('\0', StringComparison.Ordinal))
#else
        if (value.IndexOf('\0') >= 0)
#endif
        {
            throw new ArgumentException("Value cannot contain a NUL character.", paramName);
        }
    }

    [SuppressMessage(
        "Roslynator",
        "RCS1256:Invalid argument null check",
        Justification = "paramName is the caller's public-API parameter, not this helper's")]
    private static void ThrowIfNull<T>(
        T? value,
        string paramName)
        where T : class
    {
#if NET8_0_OR_GREATER
#pragma warning disable S3236
        ArgumentNullException.ThrowIfNull(value, paramName);
#pragma warning restore S3236
#else
        if (value is null)
        {
            throw new ArgumentNullException(paramName);
        }
#endif
    }
}
