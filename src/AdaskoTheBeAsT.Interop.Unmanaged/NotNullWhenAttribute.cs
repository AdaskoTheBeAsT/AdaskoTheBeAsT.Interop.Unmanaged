#if NETFRAMEWORK
using System.ComponentModel;

#pragma warning disable IDE0130
namespace System.Diagnostics.CodeAnalysis;
#pragma warning restore IDE0130

[AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
[EditorBrowsable(EditorBrowsableState.Never)]
[ExcludeFromCodeCoverage]
internal sealed class NotNullWhenAttribute(bool returnValue) : Attribute
{
    public bool ReturnValue { get; } = returnValue;
}
#endif
