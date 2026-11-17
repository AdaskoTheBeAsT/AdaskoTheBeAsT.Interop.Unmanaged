#if NETFRAMEWORK
using System.ComponentModel;

namespace System.Diagnostics.CodeAnalysis;

[AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
[EditorBrowsable(EditorBrowsableState.Never)]
[ExcludeFromCodeCoverage]
internal sealed class NotNullWhenAttribute : Attribute
{
    public NotNullWhenAttribute(bool returnValue)
    {
        ReturnValue = returnValue;
    }

    public bool ReturnValue { get; }
}
#endif
