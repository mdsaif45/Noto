using System.Reflection;
using Noto.Platform.Windows;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// The platform layer stays below the UI, and so does this test project.
/// </summary>
/// <remarks>
/// <para>
/// The boundary is the reason this project exists in the shape it does, so it
/// is asserted rather than left to review. <c>Noto.Platform.Windows</c> holds
/// Win32 interop and window arithmetic; the XAML layer sits above it and must
/// never be referenced from below (ADR-009).
/// </para>
/// <para>
/// <b>What this does NOT cover.</b> #20's remaining assertion — that no view
/// receives a repository, so every mutation goes through a command handler
/// (ADR-010) — needs to load <c>Noto.Windows</c> and read its constructors.
/// This project must not reference <c>Noto.Windows</c>, and weakening that to
/// satisfy #20 would breach the very boundary #20 exists to protect. It stays
/// open; see the report in the pull request that added this project.
/// </para>
/// </remarks>
public sealed class PlatformBoundaryTests
{
    private static readonly Assembly PlatformAssembly = typeof(WindowPlacement).Assembly;

    private static readonly Assembly TestAssembly = typeof(PlatformBoundaryTests).Assembly;

    [Fact]
    public void The_platform_layer_does_not_reference_the_ui()
    {
        // Mirrors Noto.Infrastructure.Tests.BoundaryTests, which asserts the
        // same forbidden direction one layer over. Roslyn elides unused
        // references, so only the FORBIDDEN direction is worth asserting —
        // a "references Core" test would fail for reasons unrelated to
        // layering.
        string[] forbidden = ["Noto.Windows", "Noto.UI", "Microsoft.UI", "Microsoft.WinUI"];

        var violations = PlatformAssembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(n => forbidden.Any(f => n.StartsWith(f, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Noto.Platform.Windows must not reference: {string.Join(", ", violations)}.");
    }

    [Fact]
    public void This_test_project_does_not_reference_the_ui()
    {
        // The project's own contract, enforced. A future runtime test that
        // reached for a WinUI type to "just check something" would fail here
        // rather than in review, and the message says why.
        string[] forbidden = ["Noto.Windows", "Noto.UI", "Microsoft.UI", "Microsoft.WinUI"];

        var violations = TestAssembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(n => forbidden.Any(f => n.StartsWith(f, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"""
             Noto.Windows.Tests must not reference: {string.Join(", ", violations)}.

             This project tests Win32 arithmetic and interop below the XAML
             layer. Reaching into Noto.Windows or WinUI would breach ADR-009's
             boundary — the same one #20 exists to enforce — and would make
             these tests depend on the UI they sit beneath.
             """);
    }

    [Fact]
    public void The_platform_layer_exposes_no_win32_handle_publicly()
    {
        // ADR-009 keeps coordinates out of Core; this keeps raw handles out of
        // everything above the platform layer. An HWND leaking through a
        // public signature is how IntPtr ends up in a view-model.
        var leaks =
            from type in PlatformAssembly.GetExportedTypes()
            from member in type.GetMembers(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            let signature = SignatureTypesOf(member)
            where signature.Any(t => t == typeof(IntPtr) || t == typeof(UIntPtr))
            select $"{type.Name}.{member.Name}";

        var violations = leaks.ToArray();

        Assert.True(
            violations.Length == 0,
            $"""
             A raw Win32 handle is exposed publicly: {string.Join(", ", violations)}.

             Handles stay inside Noto.Platform.Windows. Callers receive
             PixelRect, FrameInset and WindowPlacement — values the layers
             above can reason about without knowing Win32 exists.
             """);
    }

    private static IEnumerable<Type> SignatureTypesOf(MemberInfo member) => member switch
    {
        PropertyInfo p => [p.PropertyType],
        FieldInfo f => [f.FieldType],
        MethodInfo m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType),
        ConstructorInfo c => c.GetParameters().Select(p => p.ParameterType),
        _ => [],
    };
}
