#if NETFRAMEWORK
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

#pragma warning disable MA0048
#pragma warning disable SA1402
#pragma warning disable SA1649

#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace System.Runtime.CompilerServices;
#pragma warning restore IDE0130 // Namespace does not match folder structure

#pragma warning disable MA0182
[EditorBrowsable(EditorBrowsableState.Never)]
[ExcludeFromCodeCoverage]
internal static class IsExternalInit;

[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field | AttributeTargets.Property,
    Inherited = false)]
[EditorBrowsable(EditorBrowsableState.Never)]
[ExcludeFromCodeCoverage]
internal sealed class RequiredMemberAttribute : Attribute;

[AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = false)]
[EditorBrowsable(EditorBrowsableState.Never)]
[ExcludeFromCodeCoverage]
internal sealed class CompilerFeatureRequiredAttribute(string featureName) : Attribute
{
    public const string RefStructs = nameof(RefStructs);

    public const string RequiredMembers = nameof(RequiredMembers);

    public string FeatureName { get; } = featureName;

    public bool IsOptional { get; init; }
}
#pragma warning restore MA0182

#endif
