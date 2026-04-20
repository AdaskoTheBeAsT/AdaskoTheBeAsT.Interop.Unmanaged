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
        Action act = static () =>
        {
            using var library = new UnmanagedLibrary(null!);
            GC.KeepAlive(library);
        };

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*cannot be null or whitespace*")
            .WithParameterName("fileName");
    }

    [Fact]
    public void Constructor_WithEmptyFileName_ThrowsArgumentException()
    {
        // Act
        Action act = static () =>
        {
            using var library = new UnmanagedLibrary(string.Empty);
            GC.KeepAlive(library);
        };

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*cannot be null or whitespace*")
            .WithParameterName("fileName");
    }

    [Fact]
    public void Constructor_WithWhitespaceFileName_ThrowsArgumentException()
    {
        // Act
        Action act = static () =>
        {
            using var library = new UnmanagedLibrary("   ");
            GC.KeepAlive(library);
        };

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
        Action act = () =>
        {
            using var library = new UnmanagedLibrary(nonExistentDll);
            GC.KeepAlive(library);
        };

        // Assert
        act.Should().Throw<Win32Exception>()
            .WithMessage($"*Failed to load library*{nonExistentDll}*");
    }

    [Fact]
    public void Constructor_WithValidDll_LoadsSuccessfully()
    {
        if (TestHelpers.SkipIfNotWindows())
        {
            return;
        }

        // Act
        using var library = new UnmanagedLibrary("kernel32.dll");

        // Assert
        library.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithCustomFlags_LoadsSuccessfully()
    {
        if (TestHelpers.SkipIfNotWindows())
        {
            return;
        }

        // Arrange
        const LoadLibraryFlags flags = LoadLibraryFlags.LOAD_LIBRARY_SEARCH_SYSTEM32;

        // Act
        using var library = new UnmanagedLibrary("kernel32.dll", flags);

        // Assert
        library.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithDataFileFlag_LoadsSuccessfully()
    {
        if (TestHelpers.SkipIfNotWindows())
        {
            return;
        }

        // Arrange
        const LoadLibraryFlags flags = LoadLibraryFlags.LOAD_LIBRARY_AS_DATAFILE;

        // Act
        using var library = new UnmanagedLibrary("kernel32.dll", flags);

        // Assert
        library.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithMultipleFlags_LoadsSuccessfully()
    {
        if (TestHelpers.SkipIfNotWindows())
        {
            return;
        }

        // Arrange
        const LoadLibraryFlags flags = LoadLibraryFlags.LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR
                    | LoadLibraryFlags.LOAD_LIBRARY_SEARCH_SYSTEM32;

        // Act
        using var library = new UnmanagedLibrary("kernel32.dll", flags);

        // Assert
        library.Should().NotBeNull();
    }

    [Fact]
    public void Dispose_CalledOnce_DisposesSuccessfully()
    {
        if (TestHelpers.SkipIfNotWindows())
        {
            return;
        }

        // Arrange
#pragma warning disable CA2000 // Dispose objects before losing scope
        var library = new UnmanagedLibrary("kernel32.dll");
#pragma warning restore CA2000 // Dispose objects before losing scope

        // Act
        Action act = () => library.Dispose();

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_DoesNotThrow()
    {
        if (TestHelpers.SkipIfNotWindows())
        {
            return;
        }

        // Arrange
#pragma warning disable CA2000 // Dispose objects before losing scope
        var library = new UnmanagedLibrary("kernel32.dll");
#pragma warning restore CA2000 // Dispose objects before losing scope

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
        if (TestHelpers.SkipIfNotWindows())
        {
            return;
        }

        // Act
        using (var library = new UnmanagedLibrary("kernel32.dll"))
        {
            // Assert
            library.Should().NotBeNull();
        }
    }

    [Fact]
    public void Constructor_LoadedLibrary_ExposesValidSafeHandleThroughLookup()
    {
        if (TestHelpers.SkipIfNotWindows())
        {
            return;
        }

        using var library = new UnmanagedLibrary("kernel32.dll");
        var found = library.TryGetExport("GetCurrentProcessId", out var addr);
        library.Should().NotBeNull();
        found.Should().BeTrue();
        addr.Should().NotBe(IntPtr.Zero);
    }
}
