using System.Reflection;
using System.Runtime.CompilerServices;
using Noto.Platform.Windows;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// The window-handle boundary: how an HWND enters the platform layer without a
/// raw handle ever appearing in a public signature.
/// </summary>
/// <remarks>
/// The raw-handle rule itself is <see cref="PlatformBoundaryTests"/>, unchanged.
/// These pin the shape that lets the rule hold while a handle still crosses.
/// </remarks>
public sealed class WindowHandleBoundaryTests
{
    private static readonly Type Handle = typeof(WindowHandle);

    private const BindingFlags PublicDeclared =
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    [Fact]
    public void The_handle_cannot_be_constructed_publicly()
    {
        // No public constructor and no public factory: the only way in is the
        // internal FromHwnd, so an arbitrary caller cannot mint a handle.
        Assert.Empty(Handle.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.DoesNotContain(
            Handle.GetMethods(BindingFlags.Public | BindingFlags.Static),
            m => m.ReturnType == Handle);
    }

    [Fact]
    public void The_handle_exposes_nothing_but_a_description()
    {
        // Its only public member is the ToString override. No property, field
        // or method can hand the native value back out.
        MemberInfo[] declared = Handle.GetMembers(PublicDeclared);

        Assert.Equal(["ToString"], declared.Select(m => m.Name));
    }

    [Fact]
    public void The_handle_keeps_reference_equality_so_its_hash_reveals_nothing()
    {
        // A value type's generated GetHashCode derives from the field — the
        // HWND. Reference equality keeps the hash unrelated to it.
        Assert.False(Handle.IsValueType);
        Assert.Null(Handle.GetMethod(nameof(Equals), PublicDeclared, [typeof(object)]));
        Assert.Null(Handle.GetMethod(nameof(GetHashCode), PublicDeclared, Type.EmptyTypes));
    }

    [Fact]
    public void Describing_a_handle_does_not_print_its_value()
    {
        WindowHandle handle = WindowHandle.FromHwnd(0x1234);

        Assert.Equal(nameof(WindowHandle), handle.ToString());
        Assert.DoesNotContain("1234", handle.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("4660", handle.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_zero_handle_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => WindowHandle.FromHwnd(0));
    }

    [Fact]
    public void The_handle_reaches_the_native_value_internally()
    {
        // What the platform's own Win32 calls read. Internal, reachable here
        // only through the test project's InternalsVisibleTo grant.
        Assert.Equal((nint)0x1234, WindowHandle.FromHwnd(0x1234).Hwnd);
    }

    [Fact]
    public void Frame_measurement_accepts_the_handle_not_a_raw_value()
    {
        MethodInfo measure = typeof(WindowFrame).GetMethod(nameof(WindowFrame.MeasureInset), BindingFlags.Public | BindingFlags.Static)!;

        Assert.Equal([typeof(WindowHandle)], measure.GetParameters().Select(p => p.ParameterType));
        Assert.Equal(typeof(FrameInset), measure.ReturnType);
    }

    [Fact]
    public void Internals_are_granted_to_exactly_the_application_and_this_test_project()
    {
        // The grant that lets Noto.Windows (assembly "Noto") construct a
        // handle also exposes every other internal, and C# cannot narrow it.
        // Pinning the list means widening it is a visible change, not a
        // one-line edit nobody notices.
        string[] granted = [.. typeof(WindowPlacement).Assembly
            .GetCustomAttributes<InternalsVisibleToAttribute>()
            .Select(a => a.AssemblyName)
            .Order(StringComparer.Ordinal)];

        Assert.Equal(["Noto", "Noto.Windows.Tests"], granted);
    }
}
