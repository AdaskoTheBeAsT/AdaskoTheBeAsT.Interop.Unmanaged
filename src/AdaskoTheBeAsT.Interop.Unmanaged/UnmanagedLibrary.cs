using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace AdaskoTheBeAsT.Interop.Unmanaged;

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
public sealed partial class UnmanagedLibrary : IDisposable
{
    /// <summary>
    /// Unmanaged resource. CLR will ensure SafeHandles get freed, without requiring a finalizer on this class.
    /// </summary>
    private readonly SafeLibraryHandle _safeLibraryHandle;

    /// <summary>
    /// Loads a native dynamic library and transfers ownership of the resulting module handle to this instance.
    /// </summary>
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
    public UnmanagedLibrary(
        string fileName,
        LoadLibraryFlags flags = LoadLibraryFlags.LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR
                                 | LoadLibraryFlags.LOAD_LIBRARY_SEARCH_SYSTEM32)
    {
        _safeLibraryHandle = LoadLibraryCore(fileName, flags);
    }

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
    public static TDelegate? GetUnmanagedFunction<TDelegate>(SafeLibraryHandle safeLibraryHandle, string functionName)
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
    public static bool TryGetExport(SafeLibraryHandle safeLibraryHandle, string functionName, out IntPtr address)
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
    [RequiresDynamicCode("Creates an IL-emitted unmanaged call thunk. Use TryGetExport and an unmanaged function pointer instead.")]
    [RequiresUnreferencedCode("Inspects delegate signatures and nested struct fields. Preserve all signature metadata.")]
#endif
    public static T? GetDelegateForFunctionPointer<T>(IntPtr ptr, CallingConvention conv)
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
    [RequiresDynamicCode("Constructed generic callbacks require a runtime proxy. Use PinConcreteDelegate or UnmanagedCallersOnly instead.")]
    [RequiresUnreferencedCode("Generic callback proxies inspect the runtime delegate type and its marshaling metadata.")]
#endif
    public static IntPtr GetFunctionPointerForDelegate<T>(T delegateCallback, out object binder)
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
    [RequiresDynamicCode("Constructed generic callbacks require a runtime proxy. Use PinConcreteDelegate or UnmanagedCallersOnly instead.")]
    [RequiresUnreferencedCode("Generic callback proxies inspect the runtime delegate type and its marshaling metadata.")]
#endif
    public static DelegatePin PinDelegate<TDelegate>(TDelegate callback)
        where TDelegate : Delegate
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
        where TDelegate : Delegate
    {
        ThrowIfNull(callback, nameof(callback));
        if (callback.GetType().IsGenericType)
        {
            throw new ArgumentException("A concrete, non-generic callback delegate is required.", nameof(callback));
        }

        return new DelegatePin(Marshal.GetFunctionPointerForDelegate(callback), callback);
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
    public bool TryGetExport(string functionName, out IntPtr address)
    {
        return TryGetExport(_safeLibraryHandle, functionName, out address);
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

    private static SafeLibraryHandle LoadLibraryCore(string fileName, LoadLibraryFlags flags)
    {
        ValidateTextArgument(fileName, nameof(fileName));

        var handle = NativeLoader.Load(fileName, flags);
        return new SafeLibraryHandle(handle, ownsHandle: true);
    }

    private static TDelegate? GetUnmanagedFunctionCore<TDelegate>(SafeLibraryHandle safeLibraryHandle, string functionName)
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

    private static void ValidateTextArgument(string? value, string paramName)
    {
        if (value == null || string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", paramName);
        }

        if (value.IndexOf('\0') >= 0)
        {
            throw new ArgumentException("Value cannot contain a NUL character.", paramName);
        }
    }

    [SuppressMessage("Roslynator", "RCS1256:Invalid argument null check", Justification = "paramName is the caller's public-API parameter, not this helper's")]
    private static void ThrowIfNull<T>(T? value, string paramName)
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
