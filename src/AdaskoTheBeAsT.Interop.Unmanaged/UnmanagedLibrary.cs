using System;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Text;

namespace AdaskoTheBeAsT.Interop.Unmanaged;

/// <summary>
/// Represents a loaded native dynamic library and exposes its exported functions as managed delegates.
/// </summary>
/// <remarks>
/// Works on Windows (via <c>LoadLibraryEx</c>/<c>GetProcAddress</c>/<c>FreeLibrary</c>), Linux and
/// macOS (via <c>dlopen</c>/<c>dlsym</c>/<c>dlclose</c>). On non-Windows platforms the
/// <see cref="LoadLibraryFlags"/> argument is ignored and the library is loaded with
/// <c>RTLD_NOW</c>.
/// This type owns the loaded module handle and frees it when disposed. Any function pointer or
/// object obtained from the library becomes unsafe to use after the module is unloaded.
/// </remarks>
public sealed class UnmanagedLibrary : IDisposable
{
    private const string Invoke = "Invoke";

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
    /// macOS, <paramref name="fileName"/> is passed to <c>dlopen</c> which searches standard
    /// locations such as <c>LD_LIBRARY_PATH</c>/<c>DYLD_LIBRARY_PATH</c> for bare names.
    /// </param>
    /// <param name="flags">Flags passed to <c>LoadLibraryEx</c> on Windows. Ignored on Linux and macOS.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="fileName"/> is <see langword="null"/>, empty, or whitespace.
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
    /// macOS, <paramref name="fileName"/> is passed to <c>dlopen</c> which searches standard
    /// locations such as <c>LD_LIBRARY_PATH</c>/<c>DYLD_LIBRARY_PATH</c> for bare names.
    /// </param>
    /// <param name="flags">Flags passed to <c>LoadLibraryEx</c> on Windows. Ignored on Linux and macOS.</param>
    /// <returns>A safe handle for the loaded module.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="fileName"/> is <see langword="null"/>, empty, or whitespace.
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
        if (safeLibraryHandle == null || safeLibraryHandle.IsClosed)
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
    /// Thrown when <paramref name="functionName"/> is <see langword="null"/>, empty, or whitespace.
    /// </exception>
    /// <remarks>
    /// Keep <paramref name="safeLibraryHandle"/> alive for at least as long as the returned
    /// delegate or any objects created by the delegate may be used.
    /// </remarks>
    public static TDelegate? GetUnmanagedFunction<TDelegate>(SafeLibraryHandle safeLibraryHandle, string functionName)
        where TDelegate : Delegate
    {
        if (safeLibraryHandle == null)
        {
            throw new ArgumentNullException(nameof(safeLibraryHandle));
        }

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
    /// Thrown when <paramref name="functionName"/> is <see langword="null"/>, empty, or whitespace.
    /// </exception>
    /// <remarks>
    /// This overload does not allocate a managed delegate and is the preferred way to obtain a
    /// pointer that will be cast to a C# unmanaged function pointer
    /// (for example <c>delegate* unmanaged[Stdcall]&lt;uint&gt;</c>) on <c>net5.0</c> or newer.
    /// Keep <paramref name="safeLibraryHandle"/> alive for as long as the returned address is used.
    /// </remarks>
    public static bool TryGetExport(SafeLibraryHandle safeLibraryHandle, string functionName, out IntPtr address)
    {
        if (safeLibraryHandle == null)
        {
            throw new ArgumentNullException(nameof(safeLibraryHandle));
        }

        ValidateTextArgument(functionName, nameof(functionName));

#pragma warning disable S3869
        address = NativeLoader.GetExport(safeLibraryHandle.DangerousGetHandle(), functionName);
#pragma warning restore S3869
        return address != IntPtr.Zero;
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
    /// Thrown when <typeparamref name="T"/> is not a delegate type.
    /// </exception>
    /// <remarks>
    /// The delegate type should describe the exact parameter and return types of the unmanaged
    /// export. A mismatched signature or calling convention can corrupt the process.
    /// <para>
    /// Unlike <see cref="Marshal.GetDelegateForFunctionPointer{TDelegate}(IntPtr)"/>, this method
    /// emits a raw <c>calli</c> and does not perform parameter marshaling. All parameter and
    /// return types must already be native-compatible (primitives, <see cref="IntPtr"/>, or
    /// blittable structs). For types that require marshaling (for example <c>string</c> to
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
    /// <c>delegate* unmanaged[Stdcall]&lt;uint&gt;</c>. They are zero-overhead, verifiable,
    /// AOT-friendly, and carry the calling convention as part of the type. Obtain the raw
    /// <see cref="IntPtr"/> with <see cref="TryGetExport(string, out IntPtr)"/> (or the static
    /// overload) and cast it directly to the desired function pointer type inside an
    /// <c>unsafe</c> block.
    /// </description>
    /// </item>
    /// </list>
    /// This method remains useful when you need to pick the unmanaged calling convention at
    /// runtime for a signature that contains only native-compatible types.
    /// </para>
    /// </remarks>
    public static T? GetDelegateForFunctionPointer<T>(IntPtr ptr, CallingConvention conv)
        where T : class
    {
        if (ptr == IntPtr.Zero)
        {
            throw new ArgumentException("Value cannot be zero.", nameof(ptr));
        }

        var delegateType = EnsureDelegateType<T>();
        var method = delegateType.GetMethod(Invoke);
        var returnType = method!.ReturnType;
        var paramTypes =
            method
            .GetParameters()
            .Select(x => x.ParameterType)
            .ToArray();
        var invoke = new DynamicMethod(Invoke, returnType, paramTypes, typeof(Delegate));
        var il = invoke.GetILGenerator();
        for (int i = 0; i < paramTypes.Length; i++)
        {
            il.Emit(OpCodes.Ldarg, i);
        }

        if (IntPtr.Size == sizeof(int))
        {
            il.Emit(OpCodes.Ldc_I4, ptr.ToInt32());
        }
        else
        {
            il.Emit(OpCodes.Ldc_I8, ptr.ToInt64());
        }

        il.Emit(OpCodes.Conv_I);
        il.EmitCalli(OpCodes.Calli, conv, returnType, paramTypes);
        il.Emit(OpCodes.Ret);
        return invoke.CreateDelegate(delegateType) as T;
    }

#pragma warning disable MA0051
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
    /// For delegates that cannot be marshaled directly (open generic delegate types such as
    /// <c>Func&lt;T, TResult&gt;</c>), this method creates a runtime proxy delegate and stores
    /// both delegates inside <paramref name="binder"/>. If the source delegate type declares
    /// an <see cref="UnmanagedFunctionPointerAttribute"/>, that attribute is copied onto the
    /// proxy so the native calling convention is preserved.
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
    public static IntPtr GetFunctionPointerForDelegate<T>(T delegateCallback, out object binder)
        where T : class, Delegate
    {
        if (delegateCallback == null)
        {
            throw new ArgumentNullException(nameof(delegateCallback));
        }

        Delegate del = delegateCallback;
        IntPtr result;

        try
        {
            result = Marshal.GetFunctionPointerForDelegate(del);
            binder = del;
        }
        catch (ArgumentException)
        {
            // generic type delegate
            var delegateType = typeof(T);
            var method = delegateType.GetMethod("Invoke");
            var returnType = method!.ReturnType;
            var paramTypes =
                method
                .GetParameters()
                .Select((x) => x.ParameterType)
                .ToArray();

            // builder a friendly name for our assembly, module, and proxy type
            var nameBuilder = new StringBuilder();
            nameBuilder.Append(delegateType.Name);
            foreach (var pType in paramTypes)
            {
                nameBuilder
                    .Append('`')
                    .Append(pType.Name);
            }

            var name = nameBuilder.ToString();

            // check if we've previously proxied this type before
            var proxyAssemblyExist =
                Array.Find(
                    AppDomain
                        .CurrentDomain
                        .GetAssemblies(),
                    (x) => x.GetName().Name?.Equals(name, StringComparison.OrdinalIgnoreCase) ?? false);

            Type? proxyType;
            if (proxyAssemblyExist == null)
            {
                // create a proxy assembly
                var proxyAssembly = AssemblyBuilder.DefineDynamicAssembly(
                    new AssemblyName(name),
                    AssemblyBuilderAccess.Run);
                var proxyModule = proxyAssembly.DefineDynamicModule(name);

                // begin creating the proxy type
                var proxyTypeBuilder = proxyModule.DefineType(
                    name,
                    TypeAttributes.AutoClass | TypeAttributes.AnsiClass | TypeAttributes.Sealed | TypeAttributes.Public,
                    typeof(MulticastDelegate));

                // If the source delegate type declares an [UnmanagedFunctionPointer] attribute,
                // propagate it to the proxy so the runtime thunk uses the intended calling
                // convention (e.g. Cdecl). Otherwise the proxy would default to StdCall/WinApi
                // which can cause stack imbalance crashes for Cdecl targets.
                var ufp = delegateType.GetCustomAttribute<UnmanagedFunctionPointerAttribute>();
                if (ufp != null)
                {
                    var ufpCtor = typeof(UnmanagedFunctionPointerAttribute)
                        .GetConstructor([typeof(CallingConvention)]);
                    if (ufpCtor != null)
                    {
                        proxyTypeBuilder.SetCustomAttribute(
                            new CustomAttributeBuilder(ufpCtor, [ufp.CallingConvention]));
                    }
                }

                // implement the basic methods of a delegate as the compiler does
                const MethodAttributes methodAttributes =
                    MethodAttributes.Public
                    | MethodAttributes.HideBySig
                    | MethodAttributes.NewSlot
                    | MethodAttributes.Virtual;
                proxyTypeBuilder
                    .DefineConstructor(
                        MethodAttributes.FamANDAssem
                        | MethodAttributes.Family
                        | MethodAttributes.HideBySig
                        | MethodAttributes.RTSpecialName,
                        CallingConventions.Standard,
                        [typeof(object), typeof(IntPtr)])
                    .SetImplementationFlags(
                        MethodImplAttributes.Runtime);

                proxyTypeBuilder
                    .DefineMethod(
                        "BeginInvoke",
                        methodAttributes,
                        typeof(IAsyncResult),
                        paramTypes)
                    .SetImplementationFlags(
                        MethodImplAttributes.Runtime);
                proxyTypeBuilder
                    .DefineMethod(
                        "EndInvoke",
                        methodAttributes,
                        returnType: null,
                        [typeof(IAsyncResult)])
                    .SetImplementationFlags(
                        MethodImplAttributes.Runtime);
                proxyTypeBuilder
                    .DefineMethod(
                        "Invoke",
                        methodAttributes,
                        returnType,
                        paramTypes)
                    .SetImplementationFlags(
                        MethodImplAttributes.Runtime);

                // create & wrap an instance of the proxy type
                proxyType = proxyTypeBuilder.CreateTypeInfo();
            }
            else
            {
                // pull the type from an existing proxy assembly
                proxyType = proxyAssemblyExist!.GetType(name);
            }

            // marshal and bind the proxy so the pointer doesn't become invalid
            var repProxy = Delegate.CreateDelegate(proxyType!, del.Target, del.Method);
            result = Marshal.GetFunctionPointerForDelegate(repProxy);
            binder = Tuple.Create(del, repProxy);
        }

        return result;
    }
#pragma warning restore MA0051

    /// <summary>
    /// Looks up an exported function in the loaded module and marshals it as a managed delegate.
    /// </summary>
    /// <typeparam name="TDelegate">Delegate type that matches the unmanaged signature.</typeparam>
    /// <param name="functionName">Case-sensitive export name to look up.</param>
    /// <returns>The requested delegate, or <see langword="null"/> when the export is not found.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="functionName"/> is <see langword="null"/>, empty, or whitespace.
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
    /// Thrown when <paramref name="functionName"/> is <see langword="null"/>, empty, or whitespace.
    /// </exception>
    /// <remarks>
    /// This method does not allocate a managed delegate and is the preferred way to obtain a
    /// pointer that will be cast to a C# unmanaged function pointer
    /// (for example <c>delegate* unmanaged[Stdcall]&lt;uint&gt;</c>) on <c>net5.0</c> or newer.
    /// Keep this <see cref="UnmanagedLibrary"/> instance alive for as long as the returned address is used.
    /// </remarks>
    public bool TryGetExport(string functionName, out IntPtr address)
    {
        ValidateTextArgument(functionName, nameof(functionName));

#pragma warning disable S3869
        address = NativeLoader.GetExport(_safeLibraryHandle.DangerousGetHandle(), functionName);
#pragma warning restore S3869
        return address != IntPtr.Zero;
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

    private static Type EnsureDelegateType<T>()
        where T : class
    {
        var delegateType = typeof(T);
        if (!typeof(Delegate).IsAssignableFrom(delegateType))
        {
            throw new InvalidOperationException("The type argument must be a delegate type.");
        }

        return delegateType;
    }

    private static void ValidateTextArgument(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", paramName);
        }
    }
}
