using System;
using System.Reflection;
using System.Runtime.InteropServices;
using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Unmanaged.Test;

public class InputValidationTests
{
    private delegate void StringCallback(string value);

    private delegate void StructCallback(ReferenceValue value);

    private delegate void AutoLayoutCallback(AutoLayoutValue value);

    private delegate void NestedCallback(NestedValue value);

    private delegate void MarshaledCallback([MarshalAs(UnmanagedType.I4)] int value);

    private delegate ref int RefReturnCallback();

    private delegate void RefParameterCallback(ref int value);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    [InlineData("\0")]
    [InlineData("library.dll\0suffix")]
    public void Load_RejectsInvalidNames(string? name)
    {
        Action construct = () =>
        {
            using var library = new UnmanagedLibrary(name!);
        };
        Action load = () =>
        {
            using var handle = UnmanagedLibrary.LoadLibrary(name!);
        };

        construct.Should().Throw<ArgumentException>().WithParameterName("fileName");
        load.Should().Throw<ArgumentException>().WithParameterName("fileName");
    }

    [Theory]
    [InlineData(typeof(object))]
    [InlineData(typeof(Delegate))]
    [InlineData(typeof(MulticastDelegate))]
    public void RawCall_RejectsNonConcreteDelegates(Type type)
    {
        var method = typeof(UnmanagedLibrary).GetMethod(
            nameof(UnmanagedLibrary.GetDelegateForFunctionPointer),
            BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)!
            .MakeGenericMethod(type);
        Action create = () => method.Invoke(null, [new IntPtr(1), CallingConvention.Cdecl]);

        create.Should().Throw<System.Reflection.TargetInvocationException>()
            .WithInnerException<InvalidOperationException>();
    }

    [Theory]
    [InlineData(CallingConvention.FastCall)]
    [InlineData(CallingConvention.ThisCall)]
    [InlineData((CallingConvention)0)]
    [InlineData((CallingConvention)99)]
    public void RawCall_RejectsUnsupportedConvention(CallingConvention convention)
    {
        Action create = () => UnmanagedLibrary.GetDelegateForFunctionPointer<Action>(new IntPtr(1), convention);

        create.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("conv");
    }

    [Fact]
    public void RawCall_RejectsSignaturesRequiringMarshaling()
    {
        AssertUnsupported<StringCallback>();
        AssertUnsupported<StructCallback>();
        AssertUnsupported<AutoLayoutCallback>();
        AssertUnsupported<NestedCallback>();
        AssertUnsupported<MarshaledCallback>();
        AssertUnsupported<RefReturnCallback>();
        AssertUnsupported<RefParameterCallback>();
        AssertUnsupported<Action<bool>>();
        AssertUnsupported<Action<char>>();
        AssertUnsupported<Action<decimal>>();
        AssertUnsupported<Action<int?>>();
        AssertUnsupported<Func<string>>();
        AssertUnsupported<Action<object>>();
        AssertUnsupported<Action<int[]>>();
    }

    [Fact]
    public void CallbackScopes_ValidateTheirInputs()
    {
        Action pin = () => UnmanagedLibrary.PinDelegate<Action>(null!);
        Action concrete = () => UnmanagedLibrary.PinConcreteDelegate<Action>(null!);
        Action generic = () => UnmanagedLibrary.PinConcreteDelegate<Func<int, int>>(value => value);

        pin.Should().Throw<ArgumentNullException>().WithParameterName("callback");
        concrete.Should().Throw<ArgumentNullException>().WithParameterName("callback");
        generic.Should().Throw<ArgumentException>().WithParameterName("callback");
    }

    private static void AssertUnsupported<T>()
        where T : class
    {
        // This address is used only for validation. No delegate is invoked.
        Action create = () => UnmanagedLibrary.GetDelegateForFunctionPointer<T>(new IntPtr(1), CallingConvention.Cdecl);
        create.Should().Throw<NotSupportedException>();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ReferenceValue
    {
        public string Text;
    }

    [StructLayout(LayoutKind.Auto)]
    private struct AutoLayoutValue
    {
        public int Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NestedValue
    {
        public ReferenceValue Value;
    }
}
