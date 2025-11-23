using System;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Text;

namespace AdaskoTheBeAsT.Interop.Unmanaged;

/// <summary>
/// Utility class to wrap an unmanaged DLL and be responsible for freeing it.
/// </summary>
/// <remarks>This is a managed wrapper over the native LoadLibrary, GetProcAddress, and
/// FreeLibrary calls.
/// </remarks>
public sealed class UnmanagedLibrary : IDisposable
{
    private const string Invoke = "Invoke";

    /// <summary>
    /// Unmanaged resource. CLR will ensure SafeHandles get freed, without requiring a finalizer on this class.
    /// </summary>
    private readonly SafeLibraryHandle _safeLibraryHandle;

    /// <summary>
    /// Initializes a new instance of the <see cref="UnmanagedLibrary"/> class.
    /// Constructor to load a dll and be responsible for freeing it.
    /// </summary>
    /// <param name="fileName">full path name of dll to load.</param>
    /// <param name="flags">Flags to pass to LoadLibraryEx.</param>
    /// <exception cref="System.IO.FileNotFoundException">if fileName can't be found.</exception>
    /// <remarks>Throws exceptions on failure. Most common failure would be file-not-found, or
    /// that the file is not a  loadable image.</remarks>
    public UnmanagedLibrary(
        string fileName,
        LoadLibraryFlags flags = LoadLibraryFlags.LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR
                                 | LoadLibraryFlags.LOAD_LIBRARY_SEARCH_SYSTEM32)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(fileName));
        }

        _safeLibraryHandle = NativeMethods.LoadLibraryEx(
            fileName,
            IntPtr.Zero,
            flags);

        if (_safeLibraryHandle.IsInvalid)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                $"Failed to load library '{fileName}'.");
        }
    }

    public static SafeLibraryHandle LoadLibrary(
        string fileName,
        LoadLibraryFlags flags = LoadLibraryFlags.LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR
                                 | LoadLibraryFlags.LOAD_LIBRARY_SEARCH_SYSTEM32)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(fileName));
        }

        var safeLibraryHandle = NativeMethods.LoadLibraryEx(
            fileName,
            IntPtr.Zero,
            flags);

        if (safeLibraryHandle.IsInvalid)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                $"Failed to load library '{fileName}'.");
        }

        return safeLibraryHandle;
    }

    public static void FreeLibrary(SafeLibraryHandle? safeLibraryHandle)
    {
        if (safeLibraryHandle == null)
        {
            return;
        }

        if (!safeLibraryHandle.IsClosed)
        {
#pragma warning disable IDISP007
            safeLibraryHandle.Dispose();
#pragma warning restore IDISP007
        }
    }

    public static TDelegate? GetUnmanagedFunction<TDelegate>(SafeLibraryHandle safeLibraryHandle, string functionName)
        where TDelegate : Delegate
    {
        var p = NativeMethods.GetProcAddress(safeLibraryHandle, functionName);

        // Failure is a common case, especially for adaptive code.
        if (p == IntPtr.Zero)
        {
            return null;
        }

        return Marshal.GetDelegateForFunctionPointer<TDelegate>(p);
    }

    /// <summary>
    /// https://www.codeproject.com/Tips/441743/A-look-at-marshalling-delegates-in-NET.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="ptr"></param>
    /// <param name="conv"></param>
    /// <returns></returns>
    public static T? GetDelegateForFunctionPointer<T>(IntPtr ptr, CallingConventions conv)
        where T : class
    {
        var delegateType = typeof(T);
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

        il.EmitCalli(OpCodes.Calli, conv, returnType, paramTypes, []);
        il.Emit(OpCodes.Ret);
        return invoke.CreateDelegate(delegateType) as T;
    }

#pragma warning disable MA0051
    public static IntPtr GetFunctionPointerForDelegate<T>(T delegateCallback, out object binder)
        where T : class, Delegate
    {
        var del = delegateCallback as Delegate;
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
    /// Dynamically lookup a function in the dll via kernel32!GetProcAddress.
    /// </summary>
    /// <typeparam name="TDelegate">Delegate signature.</typeparam>
    /// <param name="functionName">raw name of the function in the export table.</param>
    /// <returns>null if function is not found. Else a delegate to the unmanaged function.
    /// </returns>
    /// <remarks>GetProcAddress results are valid as long as the dll is not yet unloaded. This
    /// is very, very dangerous to use since you need to ensure that the dll is not unloaded
    /// until after you're done with any objects implemented by the dll. For example, if you
    /// get a delegate that then gets an IUnknown implemented by this dll,
    /// you can not dispose this library until that IUnknown is collected. Else, you may free
    /// the library and then the CLR may call release on that IUnknown and it will crash.</remarks>
    public TDelegate? GetUnmanagedFunction<TDelegate>(string functionName)
        where TDelegate : Delegate
    {
        var p = NativeMethods.GetProcAddress(_safeLibraryHandle, functionName);

        // Failure is a common case, especially for adaptive code.
        if (p == IntPtr.Zero)
        {
            return null;
        }

        return Marshal.GetDelegateForFunctionPointer<TDelegate>(p);
    }

    /// <summary>
    /// Call FreeLibrary on the unmanaged dll. All function pointers
    /// handed out from this class become invalid after this.
    /// </summary>
    /// <remarks>This is very dangerous because it suddenly invalidates
    /// everything retrieved from this dll. This includes any functions
    /// handed out via GetProcAddress, and potentially any objects returned
    /// from those functions (which may have an implementation in the
    /// dll).
    /// </remarks>
    public void Dispose()
    {
        if (!_safeLibraryHandle.IsClosed)
        {
            _safeLibraryHandle.Dispose();
        }
    }
}
