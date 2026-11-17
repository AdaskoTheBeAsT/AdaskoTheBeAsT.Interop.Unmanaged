namespace AdaskoTheBeAsT.Interop.Unmanaged;

/// <summary>
/// Configures structural and provenance checks performed when opening a PE32 or PE32+ image in <see cref="PeImageEditor"/>.
/// </summary>
public sealed class PeImageValidationOptions
{
    /// <summary>
    /// Gets the exact expected byte length of the source file, or <see langword="null"/> when length is not pinned.
    /// </summary>
    public long? ExpectedFileLength { get; init; }

    /// <summary>
    /// Gets the expected COFF machine constant (see <see cref="PeMachine"/>), or <see langword="null"/> to accept any supported PE machine.
    /// </summary>
    public ushort? ExpectedMachine { get; init; }

    /// <summary>
    /// Gets a value indicating whether the image must carry the <c>IMAGE_FILE_DLL</c> COFF characteristic. Defaults to <see langword="true"/>.
    /// </summary>
    public bool RequireDll { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether managed CLI/CLR assemblies (<c>CorHeader != null</c>) must be rejected. Defaults to <see langword="true"/>.
    /// </summary>
    public bool RequireUnmanagedImage { get; init; } = true;

    /// <summary>
    /// Creates validation options requiring an unmanaged DLL matching <see cref="PeMachine.CurrentProcessArchitecture"/>.
    /// </summary>
    /// <param name="expectedFileLength">Optional exact expected byte length of the source binary.</param>
    /// <returns>A configured <see cref="PeImageValidationOptions"/> instance.</returns>
    public static PeImageValidationOptions ForCurrentProcessArchitecture(long? expectedFileLength = null)
    {
        return new PeImageValidationOptions
        {
            ExpectedFileLength = expectedFileLength,
            ExpectedMachine = PeMachine.CurrentProcessArchitecture,
            RequireDll = true,
            RequireUnmanagedImage = true,
        };
    }

    /// <summary>
    /// Creates validation options accepting an unmanaged native executable (<c>.exe</c> or apphost) without requiring <c>IMAGE_FILE_DLL</c>.
    /// </summary>
    /// <param name="expectedMachine">Optional expected COFF machine constant (see <see cref="PeMachine"/>).</param>
    /// <param name="expectedFileLength">Optional exact expected byte length of the source binary.</param>
    /// <returns>A configured <see cref="PeImageValidationOptions"/> instance.</returns>
    public static PeImageValidationOptions ForExecutable(
        ushort? expectedMachine = null,
        long? expectedFileLength = null)
    {
        return new PeImageValidationOptions
        {
            ExpectedFileLength = expectedFileLength,
            ExpectedMachine = expectedMachine,
            RequireDll = false,
            RequireUnmanagedImage = true,
        };
    }
}
