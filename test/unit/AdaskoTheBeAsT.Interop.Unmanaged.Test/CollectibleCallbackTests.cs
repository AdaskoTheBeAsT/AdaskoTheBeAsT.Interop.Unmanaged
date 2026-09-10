#if NET8_0_OR_GREATER
using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Unmanaged.Test;

[Trait("Category", "Native")]
public class CollectibleCallbackTests
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int PluginCallback<T>(T value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NativeInvoke(IntPtr pointer, int value);

    public static Delegate CreateCallback()
    {
        var offset = int.Parse("1", System.Globalization.CultureInfo.InvariantCulture);
        return new PluginCallback<int>(value => value + offset);
    }

    [Fact]
    [SuppressMessage("Sonar", "S1215", Justification = "Repeated GC verifies collectible context and emitted type reachability.")]
    public void ReleasedCallback_DoesNotRootCollectibleContext()
    {
        using var fixture = new NativeFixture();
        using var library = new UnmanagedLibrary(fixture.LibraryPath);
        var weak = InvokeCollectibleCallback(library);
        for (var attempt = 0; attempt < 20 && weak.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        weak.IsAlive.Should().BeFalse();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference InvokeCollectibleCallback(UnmanagedLibrary library)
    {
        var context = new AssemblyLoadContext("callback-test", isCollectible: true);
        var assembly = context.LoadFromAssemblyPath(typeof(CollectibleCallbackTests).Assembly.Location);
        var type = assembly.GetType(typeof(CollectibleCallbackTests).FullName!)!;
        var callback = (Delegate)type.GetMethod(
            nameof(CreateCallback),
            BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)!.Invoke(null, null)!;
        using (var scope = UnmanagedLibrary.PinDelegate(callback))
        {
            library.GetUnmanagedFunction<NativeInvoke>("fixture_invoke")!(scope.Ptr, 41).Should().Be(42);
        }

        var weak = new WeakReference(context);
        context.Unload();
        return weak;
    }
}
#endif
