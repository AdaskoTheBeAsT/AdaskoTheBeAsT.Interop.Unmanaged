using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Unmanaged.Test;

#pragma warning disable SA1121

public class NativeByteStringTests
{
    [Fact]
    public void NullTerminatedAsciiAndLatin1_RoundTripZeroPadAndValidateBytes()
    {
        var buffer = new byte[12];
        for (var i = 0; i < buffer.Length; i++)
        {
            buffer[i] = 0xFF;
        }

        var written = NativeByteString.WriteNullTerminatedAscii("COIL01", buffer, offset: 2, count: 8);
        written.Should().Be(6);
        buffer[0].Should().Be(0xFF);
        buffer[1].Should().Be(0xFF);
        buffer[8].Should().Be(0);
        buffer[9].Should().Be(0);
        buffer[10].Should().Be(0xFF);
        NativeByteString.ReadNullTerminatedAscii(buffer, offset: 2, count: 8).Should().Be("COIL01");

        var latin1Bytes = new byte[]
        {
            (byte)'C',
            (byte)'a',
            (byte)'f',
            0xE9,
            0x00,
            0x7F,
        };
        NativeByteString.ReadNullTerminatedLatin1(latin1Bytes, 0, latin1Bytes.Length).Should().Be("Café");
        Action nonAscii = () => NativeByteString.ReadNullTerminatedAscii(latin1Bytes, 0, latin1Bytes.Length);
        nonAscii.Should().Throw<DecoderFallbackException>();

        Action overflow = () => NativeByteString.WriteNullTerminatedAscii("12345", new byte[5], 0, 5);
        Action embeddedNul = () => NativeByteString.WriteNullTerminatedAscii("AB\0CD", new byte[8], 0, 8);
        Action unicodeInput = () => NativeByteString.WriteNullTerminatedAscii("żółć", new byte[8], 0, 8);
        overflow.Should().Throw<ArgumentOutOfRangeException>();
        embeddedNul.Should().Throw<ArgumentException>();
        unicodeInput.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SpacePaddedFortranAndPascalShortString_RoundTripAndValidateCapacity()
    {
        var fortran = new byte[10];
        NativeByteString.WriteSpacePaddedAscii("  R410A", fortran, 0, fortran.Length);
        fortran[7].Should().Be(0x20);
        fortran[9].Should().Be(0x20);
        NativeByteString.ReadSpacePaddedAscii(fortran, 0, fortran.Length).Should().Be("  R410A");

        NativeByteString.WriteSpacePaddedAscii(string.Empty, fortran, 0, fortran.Length);
        NativeByteString.ReadSpacePaddedAscii(fortran, 0, fortran.Length).Should().BeEmpty();

        var shortBuf = new byte[16];
        for (var i = 0; i < shortBuf.Length; i++)
        {
            shortBuf[i] = 0xAA;
        }

        NativeByteString.WriteShortStringAscii("FAN.INI", shortBuf, offset: 1, capacity: 12);
        shortBuf[0].Should().Be(0xAA);
        shortBuf[1].Should().Be(7);
        shortBuf[9].Should().Be(0);
        shortBuf[12].Should().Be(0);
        shortBuf[13].Should().Be(0xAA);
        NativeByteString.ReadShortStringAscii(shortBuf, offset: 1, maxLength: 12).Should().Be("FAN.INI");

        Action capTooBig = () => NativeByteString.WriteShortStringAscii("A", new byte[300], 0, 257);
        Action truncatedShort = () => NativeByteString.ReadShortStringAscii([5, (byte)'A', (byte)'B'], 0, 3);
        capTooBig.Should().Throw<ArgumentOutOfRangeException>();
        truncatedShort.Should().Throw<ArgumentException>();
    }

#if NET10_0_OR_GREATER
    [Fact]
    public void SpanOverloads_RoundTripCStringFortranAndShortString()
    {
        Span<byte> cbuf = stackalloc byte[10];
        cbuf.Fill(0xEE);
        NativeByteString.WriteNullTerminatedAscii("PUMP_9".AsSpan(), cbuf).Should().Be(6);
        cbuf[6].Should().Be(0);
        cbuf[9].Should().Be(0);
        NativeByteString.ReadNullTerminatedAscii(cbuf).Should().Be("PUMP_9");

        ReadOnlySpan<byte> latinSpan = [(byte)'Z', 0xFC, (byte)'g', 0];
        NativeByteString.ReadNullTerminatedLatin1(latinSpan).Should().Be("Züg");

        Span<byte> fbuf = stackalloc byte[8];
        NativeByteString.WriteSpacePaddedAscii("GLYCOL".AsSpan(), fbuf);
        fbuf[6].Should().Be(0x20);
        fbuf[7].Should().Be(0x20);
        NativeByteString.ReadSpacePaddedAscii(fbuf).Should().Be("GLYCOL");

        Span<byte> sbuf = stackalloc byte[10];
        sbuf.Fill(0xCC);
        NativeByteString.WriteShortStringAscii("DB00".AsSpan(), sbuf);
        sbuf[0].Should().Be(4);
        sbuf[5].Should().Be(0);
        NativeByteString.ReadShortStringAscii(sbuf).Should().Be("DB00");
    }
#endif

#if NET10_0_OR_GREATER
    [Fact]
    [SuppressMessage("Security", "SCS0001", Justification = "Invokes dotnet CLI on test-generated temporary project.")]
    [SuppressMessage("Security", "SCS0018", Justification = "Uses random temporary directory owned by this test.")]
    [SuppressMessage("Security", "SEC0116", Justification = "Writes temporary test project files.")]
    public void BuildTransitiveTarget_SetsLargeAddressAwareOnWinX86AppHostOnlyWhenEnabled()
    {
        if (TestHelpers.SkipIfNotWindows()
            || IntPtr.Size != 8)
        {
            return;
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "unm-laa-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var targetsPath = FindBuildTransitiveTargetsPath();
            WriteMinimalLaaProject(tempDir, targetsPath);

            RunDotnet(
                tempDir,
                "build -c Release -r win-x86 --no-self-contained -p:EnableLargeAddressAwareAppHost=false -o out-disabled");
            var disabledExe = Path.Combine(tempDir, "out-disabled", "LaaProbe.exe");
            var disabledEditor = PeImageEditor.FromFile(
                disabledExe,
                PeImageValidationOptions.ForExecutable(PeMachine.I386));
            (disabledEditor.Characteristics & 0x0020).Should().Be(0);

            RunDotnet(
                tempDir,
                "build -c Release -r win-x86 --no-self-contained -p:EnableLargeAddressAwareAppHost=true -o out-enabled");
            var builtExe = Path.Combine(tempDir, "out-enabled", "LaaProbe.exe");
            var builtEditor = PeImageEditor.FromFile(builtExe, PeImageValidationOptions.ForExecutable(PeMachine.I386));
            (builtEditor.Characteristics & 0x0020).Should().Be(0x0020);

            RunDotnet(
                tempDir,
                "publish -c Release -r win-x86 --no-self-contained -p:EnableLargeAddressAwareAppHost=true -o pub-enabled");
            var pubExe = Path.Combine(tempDir, "pub-enabled", "LaaProbe.exe");
            var pubEditor = PeImageEditor.FromFile(pubExe, PeImageValidationOptions.ForExecutable(PeMachine.I386));
            (pubEditor.Characteristics & 0x0020).Should().Be(0x0020);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [SuppressMessage(
        "Security",
        "SCS0018",
        Justification = "Resolves repository buildTransitive targets file from BaseDirectory.")]
    private static string FindBuildTransitiveTargetsPath()
    {
        var cursor = new DirectoryInfo(AppContext.BaseDirectory);
        while (cursor is not null)
        {
            var candidate = Path.Combine(
                cursor.FullName,
                "src",
                "AdaskoTheBeAsT.Interop.Unmanaged",
                "buildTransitive",
                "AdaskoTheBeAsT.Interop.Unmanaged.targets");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            cursor = cursor.Parent;
        }

        throw new FileNotFoundException("Could not locate buildTransitive/AdaskoTheBeAsT.Interop.Unmanaged.targets.");
    }

    [SuppressMessage("Security", "SCS0018", Justification = "Writes temporary test project files inside tempDir.")]
    [SuppressMessage("Security", "SEC0116", Justification = "Writes temporary test project files inside tempDir.")]
    private static void WriteMinimalLaaProject(
        string tempDir,
        string targetsPath)
    {
        File.WriteAllText(Path.Combine(tempDir, "Directory.Build.props"), "<Project />");
        File.WriteAllText(Path.Combine(tempDir, "Directory.Build.targets"), "<Project />");
        File.WriteAllText(
            Path.Combine(tempDir, "Program.cs"),
            "internal static class Program { private static int Main() => 0; }");
        var csproj = $"""
                      <Project Sdk="Microsoft.NET.Sdk">
                        <PropertyGroup>
                          <OutputType>Exe</OutputType>
                          <TargetFramework>net10.0</TargetFramework>
                          <AssemblyName>LaaProbe</AssemblyName>
                          <ImplicitUsings>disable</ImplicitUsings>
                          <Nullable>enable</Nullable>
                        </PropertyGroup>
                        <Import Project="{targetsPath}" />
                      </Project>
                      """;
        File.WriteAllText(Path.Combine(tempDir, "LaaProbe.csproj"), csproj);
    }

    [SuppressMessage("Security", "SCS0001", Justification = "Executes dotnet build/publish on test-generated project.")]
    [SuppressMessage(
        "Security",
        "SEC0031",
        Justification = "Executes trusted dotnet CLI with constant test arguments.")]
    [SuppressMessage(
        "Security",
        "SEC0032",
        Justification = "Constructs ProcessStartInfo for trusted dotnet CLI test invocation.")]
    private static void RunDotnet(
        string workingDirectory,
        string arguments)
    {
        var psi = new ProcessStartInfo("dotnet", arguments)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(120_000).Should().BeTrue();
        proc.ExitCode.Should().Be(0, stdout + Environment.NewLine + stderr);
    }
#endif
}
