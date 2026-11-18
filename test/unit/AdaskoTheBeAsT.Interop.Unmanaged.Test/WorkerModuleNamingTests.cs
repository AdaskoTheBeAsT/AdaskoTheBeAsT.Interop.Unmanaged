using System;
using System.Collections.Generic;
using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Unmanaged.Test;

public class WorkerModuleNamingTests
{
    [Theory]
    [InlineData("DFORRT.dll", 0, "DFOR00.dll")]
    [InlineData("DFORRT.dll", 9, "DFOR09.dll")]
    [InlineData("DFORRT.dll", 99, "DFOR99.dll")]
    [InlineData("runtime9620.dll", 0, "runtime9600.dll")]
    [InlineData("runtime9620.dll", 42, "runtime9642.dll")]
    [InlineData("ENGINE.DLL", 7, "ENGI07.DLL")]
    [InlineData("ab.dll", 5, "05.dll")]
    public void FormatFixedLengthName_PreservesExactLengthAndCaseOfExtension(
        string originalFileName,
        int workerIndex,
        string expected)
    {
        var actual = WorkerModuleNaming.FormatFixedLengthName(originalFileName, workerIndex);

        actual.Should().Be(expected);
        actual.Should().HaveLength(originalFileName.Length);
    }

    [Fact]
    public void FormatScopedFixedLengthName_ProducesUniqueFixedLengthNamesAcrossPoolsAndGenerations()
    {
        const string original = "isoparent.dll";
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var candidates = new[]
        {
            WorkerModuleNaming.FormatScopedFixedLengthName(original, namespaceId: null, workerIndex: 0, generation: 0),
            WorkerModuleNaming.FormatScopedFixedLengthName(original, namespaceId: "pA", workerIndex: 0, generation: 0),
            WorkerModuleNaming.FormatScopedFixedLengthName(original, namespaceId: "pB", workerIndex: 0, generation: 0),
            WorkerModuleNaming.FormatScopedFixedLengthName(original, namespaceId: "pA", workerIndex: 0, generation: 1),
            WorkerModuleNaming.FormatScopedFixedLengthName(original, namespaceId: "pA", workerIndex: 0, generation: 36),
            WorkerModuleNaming.FormatScopedFixedLengthName(original, namespaceId: "pA", workerIndex: 1, generation: 36),
            WorkerModuleNaming.FormatScopedFixedLengthName(
                original,
                namespaceId: null,
                workerIndex: 0,
                generation: 1295),
        };

        foreach (var name in candidates)
        {
            name.Should().HaveLength(original.Length);
            seen.Add(name).Should().BeTrue($"'{name}' should be unique across pool/generation combinations");
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100)]
    public void FormatFixedLengthName_RejectsOutOfRangeWorkerIndex(int workerIndex)
    {
        Action act = () => WorkerModuleNaming.FormatFixedLengthName("DFORRT.dll", workerIndex);
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName(nameof(workerIndex));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1296)]
    public void FormatScopedFixedLengthName_RejectsOutOfRangeGeneration(int generation)
    {
        Action act = () => WorkerModuleNaming.FormatScopedFixedLengthName("isoparent.dll", "pA", 0, generation);
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName(nameof(generation));
    }

    [Theory]
    [InlineData("ABCDE")]
    [InlineData("p_1")]
    [InlineData("p/1")]
    [InlineData("ż")]
    public void FormatScopedFixedLengthName_RejectsInvalidNamespaceId(string namespaceId)
    {
        Action act = () => WorkerModuleNaming.FormatScopedFixedLengthName("isoparent.dll", namespaceId, 0, 0);
        act.Should().Throw<ArgumentException>().WithParameterName(nameof(namespaceId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a.dll")]
    [InlineData(".dll")]
    [InlineData("engine.so")]
    [InlineData("sub\\DFORRT.dll")]
    [InlineData("sub/DFORRT.dll")]
    [InlineData("C:DFORRT.dll")]
    [InlineData("żółć.dll")]
    public void FormatFixedLengthName_RejectsInvalidOrTooShortDllFileNames(string? fileName)
    {
        Action act = () => WorkerModuleNaming.FormatFixedLengthName(fileName!, 0);
        act.Should().Throw<ArgumentException>().WithParameterName("originalFileName");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bad.token")]
    [InlineData("bad/token")]
    public void FormatFixedLengthToken_RejectsInvalidSuffixTokens(string? token)
    {
        Action act = () => WorkerModuleNaming.FormatFixedLengthToken("DFORRT.dll", token!);
        act.Should().Throw<ArgumentException>().WithParameterName("suffixToken");
    }
}
