using System;
using System.ComponentModel;
using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Unmanaged.Test;

public class UnmanagedLibraryConstructorTests
{
    [Fact]
    public void Constructor_WithNullFileName_ThrowsArgumentException()
    {
        // Act
        Action act = () => new UnmanagedLibrary(null!);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*cannot be null or whitespace*")
            .WithParameterName("fileName");
    }

    [Fact]
    public void Constructor_WithEmptyFileName_ThrowsArgumentException()
    {
        // Act
        Action act = () => new UnmanagedLibrary(string.Empty);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*cannot be null or whitespace*")
            .WithParameterName("fileName");
    }

    [Fact]
    public void Constructor_WithWhitespaceFileName_ThrowsArgumentException()
    {
        // Act
        Action act = () => new UnmanagedLibrary("   ");

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*cannot be null or whitespace*")
            .WithParameterName("fileName");
    }

    [Fact]
    public void Constructor_WithNonExistentDll_ThrowsWin32Exception()
    {
        // Arrange
        var nonExistentDll = $"NonExistent_{Guid.NewGuid()}.dll";

        // Act
        Action act = () => new UnmanagedLibrary(nonExistentDll);

        // Assert
        act.Should().Throw<Win32Exception>()
            .WithMessage($"*Failed to load library*{nonExistentDll}*");
    }

    [Fact]
    public void Constructor_WithValidDll_LoadsSuccessfully()
    {
        // Act
        using var library = new UnmanagedLibrary("kernel32.dll");

        // Assert
        library.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithCustomFlags_LoadsSuccessfully()
    {
        // Arrange
        var flags = LoadLibraryFlags.LOAD_LIBRARY_SEARCH_SYSTEM32;

        // Act
        using var library = new UnmanagedLibrary("kernel32.dll", flags);

        // Assert
        library.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithDataFileFlag_LoadsSuccessfully()
    {
        // Arrange
        var flags = LoadLibraryFlags.LOAD_LIBRARY_AS_DATAFILE;

        // Act
        using var library = new UnmanagedLibrary("kernel32.dll", flags);

        // Assert
        library.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithMultipleFlags_LoadsSuccessfully()
    {
        // Arrange
        var flags = LoadLibraryFlags.LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR
                    | LoadLibraryFlags.LOAD_LIBRARY_SEARCH_SYSTEM32;

        // Act
        using var library = new UnmanagedLibrary("kernel32.dll", flags);

        // Assert
        library.Should().NotBeNull();
    }

    [Fact]
    public void Dispose_CalledOnce_DisposesSuccessfully()
    {
        // Arrange
        var library = new UnmanagedLibrary("kernel32.dll");

        // Act
        Action act = () => library.Dispose();

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_DoesNotThrow()
    {
        // Arrange
        var library = new UnmanagedLibrary("kernel32.dll");

        // Act
        Action act = () =>
        {
            library.Dispose();
            library.Dispose();
            library.Dispose();
        };

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void UsingStatement_DisposesLibraryProperly()
    {
        // Act
        using (var library = new UnmanagedLibrary("kernel32.dll"))
        {
            // Assert
            library.Should().NotBeNull();
        }
    }
}
