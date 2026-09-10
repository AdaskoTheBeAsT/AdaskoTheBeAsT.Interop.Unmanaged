using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace AdaskoTheBeAsT.Interop.Unmanaged;

public sealed partial class UnmanagedLibrary
{
    private const string Invoke = "Invoke";

    private static readonly ConditionalWeakTable<Type, Lazy<Type>> ProxyDelegateTypes = new();

#if NET8_0_OR_GREATER
    [RequiresDynamicCode("Creates an IL-emitted unmanaged call thunk.")]
    [RequiresUnreferencedCode("Inspects delegate signatures and nested struct fields.")]
#endif
    private static T? CreateRawDelegate<T>(IntPtr ptr, CallingConvention conv)
        where T : class
    {
        EnsureDynamicCode();
        var delegateType = typeof(T);
        var method = RawCallSignature.Validate(delegateType, conv);
        var returnType = method.ReturnType;
        var paramTypes = method.GetParameters().Select(parameter => parameter.ParameterType).ToArray();
        var invoke = new DynamicMethod(Invoke, returnType, paramTypes, typeof(UnmanagedLibrary));
        var il = invoke.GetILGenerator();
        for (var index = 0; index < paramTypes.Length; index++)
        {
            il.Emit(OpCodes.Ldarg, index);
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

    private static void EnsureDynamicCode()
    {
#if NET8_0_OR_GREATER
        if (!RuntimeFeature.IsDynamicCodeSupported)
        {
            throw new PlatformNotSupportedException(
                "This API requires runtime code generation. Use TryGetExport with an unmanaged function pointer, "
                + "PinConcreteDelegate, or an UnmanagedCallersOnly callback.");
        }
#endif
    }

#if NET8_0_OR_GREATER
    [RequiresDynamicCode("Creates a callback proxy delegate type.")]
    [RequiresUnreferencedCode("Inspects the runtime delegate's Invoke signature.")]
#endif
    private static IntPtr GetFunctionPointerForGenericDelegate(Delegate del, out object binder)
    {
        EnsureDynamicCode();
        var delegateType = del.GetType();
        var method = delegateType.GetMethod(Invoke)!;
        var proxyType = ProxyDelegateTypes.GetValue(
            delegateType,
            static type => new Lazy<Type>(
                () => CreateProxyDelegateType(type),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;

        // Invoke the original delegate to preserve multicast and open/closed bindings.
        var repProxy = Delegate.CreateDelegate(proxyType, del, method);
        var result = Marshal.GetFunctionPointerForDelegate(repProxy);
        binder = Tuple.Create(del, repProxy);
        return result;
    }

#if NET8_0_OR_GREATER
    [RequiresDynamicCode("Emits a callback proxy assembly.")]
    [RequiresUnreferencedCode("Copies the runtime delegate signature and interop metadata.")]
#endif
    private static Type CreateProxyDelegateType(Type delegateType)
    {
        var name = "UnmanagedCallback_" + Guid.NewGuid().ToString("N");
        var proxyAssembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName(name),
#if NET8_0_OR_GREATER
            AssemblyBuilderAccess.RunAndCollect);
#else
            AssemblyBuilderAccess.Run);
#endif
        var proxyModule = proxyAssembly.DefineDynamicModule(name);
        var proxyTypeBuilder = proxyModule.DefineType(
            name,
            TypeAttributes.AutoClass | TypeAttributes.AnsiClass | TypeAttributes.Sealed | TypeAttributes.Public,
            typeof(MulticastDelegate));

        ApplyUnmanagedFunctionPointerAttribute(delegateType, proxyTypeBuilder);
        DefineDelegateMembers(proxyTypeBuilder, delegateType.GetMethod(Invoke)!);
        return proxyTypeBuilder.CreateTypeInfo();
    }

    private static void ApplyUnmanagedFunctionPointerAttribute(Type delegateType, TypeBuilder proxyTypeBuilder)
    {
        // Preserve the calling convention and all fields affecting native callback marshaling.
        var ufp = delegateType.GetCustomAttribute<UnmanagedFunctionPointerAttribute>();
        if (ufp == null)
        {
            return;
        }

        var ufpAttributeType = typeof(UnmanagedFunctionPointerAttribute);
        const BindingFlags ufpFieldBindingFlags =
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        var ufpCtor = ufpAttributeType.GetConstructor([typeof(CallingConvention)]);
        var charSetField = ufpAttributeType.GetField(nameof(UnmanagedFunctionPointerAttribute.CharSet), ufpFieldBindingFlags);
        var bestFitMappingField = ufpAttributeType.GetField(nameof(UnmanagedFunctionPointerAttribute.BestFitMapping), ufpFieldBindingFlags);
        var throwOnUnmappableCharField = ufpAttributeType.GetField(nameof(UnmanagedFunctionPointerAttribute.ThrowOnUnmappableChar), ufpFieldBindingFlags);
        var setLastErrorField = ufpAttributeType.GetField(nameof(UnmanagedFunctionPointerAttribute.SetLastError), ufpFieldBindingFlags);
        if (ufpCtor == null
            || charSetField == null
            || bestFitMappingField == null
            || throwOnUnmappableCharField == null
            || setLastErrorField == null)
        {
            return;
        }

        proxyTypeBuilder.SetCustomAttribute(
            new CustomAttributeBuilder(
                ufpCtor,
                [ufp.CallingConvention],
                namedFields:
                [
                    charSetField,
                    bestFitMappingField,
                    throwOnUnmappableCharField,
                    setLastErrorField,
                ],
                fieldValues:
                [
                    ufp.CharSet,
                    ufp.BestFitMapping,
                    ufp.ThrowOnUnmappableChar,
                    ufp.SetLastError,
                ]));
    }

    private static void DefineDelegateMembers(TypeBuilder proxyTypeBuilder, MethodInfo method)
    {
        const MethodAttributes methodAttributes =
            MethodAttributes.Public
            | MethodAttributes.HideBySig
            | MethodAttributes.NewSlot
            | MethodAttributes.Virtual;
        proxyTypeBuilder
            .DefineConstructor(
                MethodAttributes.Public
                | MethodAttributes.HideBySig
                | MethodAttributes.SpecialName
                | MethodAttributes.RTSpecialName,
                CallingConventions.Standard,
                [typeof(object), typeof(IntPtr)])
            .SetImplementationFlags(MethodImplAttributes.Runtime);

        var parameters = method.GetParameters();
        var invoke = proxyTypeBuilder.DefineMethod(
            Invoke,
            methodAttributes,
            method.ReturnType,
            parameters.Select(parameter => parameter.ParameterType).ToArray());
        invoke.SetImplementationFlags(MethodImplAttributes.Runtime);
        CopyParameterMetadata(invoke, method.ReturnParameter, 0);
        for (var index = 0; index < parameters.Length; index++)
        {
            CopyParameterMetadata(invoke, parameters[index], index + 1);
        }
    }

    private static void CopyParameterMetadata(MethodBuilder method, ParameterInfo parameter, int position)
    {
        var builder = method.DefineParameter(position, parameter.Attributes, parameter.Name);
        var marshalAs = parameter.GetCustomAttributesData()
            .FirstOrDefault(attribute => attribute.AttributeType == typeof(MarshalAsAttribute));
        if (marshalAs == null)
        {
            return;
        }

        builder.SetCustomAttribute(
            new CustomAttributeBuilder(
                marshalAs.Constructor,
                marshalAs.ConstructorArguments.Select(argument => argument.Value!).ToArray(),
                marshalAs.NamedArguments.Select(argument => (FieldInfo)argument.MemberInfo).ToArray(),
                marshalAs.NamedArguments.Select(argument => argument.TypedValue.Value!).ToArray()));
    }
}
