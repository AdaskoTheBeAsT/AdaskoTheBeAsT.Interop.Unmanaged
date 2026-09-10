using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace AdaskoTheBeAsT.Interop.Unmanaged;

internal static class RawCallSignature
{
#if NET8_0_OR_GREATER
    [RequiresUnreferencedCode("Inspects delegate signatures and nested field metadata.")]
#endif
    public static MethodInfo Validate(Type delegateType, CallingConvention conv)
    {
        if (delegateType.BaseType != typeof(MulticastDelegate)
            || delegateType.IsAbstract
            || delegateType.ContainsGenericParameters)
        {
            throw new InvalidOperationException("The type argument must be a concrete, closed delegate type.");
        }

        if (conv is not (CallingConvention.Cdecl or CallingConvention.StdCall or CallingConvention.Winapi))
        {
            throw new ArgumentOutOfRangeException(nameof(conv), conv, "Only Cdecl, StdCall, and Winapi are supported.");
        }

        var method = delegateType.GetMethod("Invoke", BindingFlags.Public | BindingFlags.Instance);
        if (method == null || method.CallingConvention.HasFlag(CallingConventions.VarArgs))
        {
            throw new InvalidOperationException("The delegate type must have a fixed Invoke signature.");
        }

        if (delegateType.GetCustomAttribute<UnmanagedFunctionPointerAttribute>()?.SetLastError == true
            || !IsSupportedParameter(method.ReturnParameter, isReturn: true)
            || method.GetParameters().Any(parameter => !IsSupportedParameter(parameter, isReturn: false)))
        {
            throw new NotSupportedException(
                "Raw calli requires native-compatible types without marshaling or SetLastError. "
                + "Use Marshal.GetDelegateForFunctionPointer for marshaled signatures.");
        }

        return method;
    }

#if NET8_0_OR_GREATER
    [RequiresUnreferencedCode("Inspects nested field metadata.")]
#endif
    private static bool IsSupportedParameter(ParameterInfo parameter, bool isReturn)
    {
        if (parameter.IsDefined(typeof(MarshalAsAttribute), inherit: false))
        {
            return false;
        }

        var type = parameter.ParameterType;
        if (type.IsByRef)
        {
            // A raw calli cannot provide the marshaler's managed-byref pinning contract.
            return false;
        }

        return (isReturn && type == typeof(void)) || IsSupportedValue(type);
    }

    [SuppressMessage("Sonar", "S3011", Justification = "Inspect private field types, without reading values, to reject hidden managed references.")]
#if NET8_0_OR_GREATER
    [RequiresUnreferencedCode("Inspects all instance fields, including private fields, recursively.")]
#endif
    private static bool IsSupportedValue(Type type)
    {
        if (type.IsPointer || type == typeof(IntPtr) || type == typeof(UIntPtr))
        {
            return true;
        }

        if (type.IsEnum)
        {
            return IsSupportedValue(Enum.GetUnderlyingType(type));
        }

        if (type.IsPrimitive)
        {
            return type != typeof(bool) && type != typeof(char) && type != typeof(void);
        }

        if (!type.IsValueType
            || type.IsAutoLayout
            || type == typeof(decimal)
            || Nullable.GetUnderlyingType(type) != null)
        {
            return false;
        }

        var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        return fields.Length != 0
            && fields.All(field => !field.IsDefined(typeof(MarshalAsAttribute), inherit: false)
                && IsSupportedValue(field.FieldType));
    }
}
