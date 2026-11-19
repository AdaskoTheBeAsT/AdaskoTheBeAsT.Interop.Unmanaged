using Xunit;

namespace AdaskoTheBeAsT.Interop.Unmanaged.Test;

[CollectionDefinition(Name, DisableParallelization = true)]
public static class ProcessErrorModeTestCollection
{
    public const string Name = "ProcessErrorMode";
}
