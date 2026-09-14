using System.Reflection;
using Xunit;

namespace Noto.Core.Tests;

/// <summary>
/// Enforces the ADR-009 boundary mechanically.
/// </summary>
/// <remarks>
/// <para>
/// ADR-009 states two rules. An architectural rule that is not enforced is a
/// comment, and both of these will otherwise be violated within weeks — not
/// maliciously, but because adding a field to a domain type is always the
/// locally convenient move and the cost only appears later.
/// </para>
/// <para>
/// These rules are what make deferring contextual notes to M6 safe. If they
/// erode, the deferral stops being safe and nobody notices until M6.
/// </para>
/// <para>
/// This is a first cut covering the boundary that exists today. Issue #20
/// broadens it — notably to the ADR-010 rule that no repository write is
/// reachable outside a command handler, which cannot be written until
/// handlers exist.
/// </para>
/// </remarks>
public sealed class ArchitectureBoundaryTests
{
    private static readonly Assembly CoreAssembly = typeof(AssemblyMarker).Assembly;

    /// <summary>Assemblies the domain must never reference, and why.</summary>
    private static readonly (string Prefix, string Reason)[] ForbiddenReferences =
    [
        ("Microsoft.UI", "WinUI — the domain must not know about the UI framework"),
        ("Microsoft.Windows", "Windows App SDK — the domain must stay platform-free"),
        ("Microsoft.WinUI", "WinUI"),
        ("WinRT", "Windows Runtime interop"),
        ("Noto.Windows", "the WinUI application — dependencies point inward"),
        ("Noto.Platform", "the Win32 interop layer"),
        ("Noto.Infrastructure", "persistence — Core defines interfaces, Infrastructure implements them"),
        ("Noto.UseCases", "the use-case layer sits above the domain, not below it"),
        ("Microsoft.Data.Sqlite", "storage — Core must not know how it is persisted"),
        ("Dapper", "storage"),
        ("System.Windows", "WPF / Windows Forms"),
        ("PresentationFramework", "WPF"),
    ];

    [Fact]
    public void Core_does_not_reference_platform_or_presentation_assemblies()
    {
        var referenced = CoreAssembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToArray();

        var violations = (
            from name in referenced
            from forbidden in ForbiddenReferences
            where name.StartsWith(forbidden.Prefix, StringComparison.OrdinalIgnoreCase)
            select $"{name} ({forbidden.Reason})")
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"""
             Noto.Core has gained a forbidden reference:

               {string.Join(Environment.NewLine + "  ", violations)}

             ADR-009: the domain depends on nothing platform-specific.
             Dependencies point inward. If the domain needs a capability, it
             defines an interface and an outer layer implements it.
             """);
    }

    /// <summary>
    /// Words that indicate a presentation concept leaking into the domain.
    /// </summary>
    private static readonly string[] PresentationTerms =
    [
        "Window", "Screen", "Monitor", "Display", "Hwnd",
        "Coordinate", "ZOrder", "Dpi", "Pixel", "Opacity", "Bounds",
    ];

    [Fact]
    public void Core_exposes_no_presentation_concept()
    {
        var violations = new List<string>();

        foreach (var type in CoreAssembly.GetExportedTypes())
        {
            foreach (var term in PresentationTerms)
            {
                if (type.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add($"type {type.FullName} contains '{term}'");
                }
            }

            foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                foreach (var term in PresentationTerms)
                {
                    if (member.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
                    {
                        violations.Add($"{type.Name}.{member.Name} contains '{term}'");
                    }
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            $"""
             Noto.Core has gained a presentation concept:

               {string.Join(Environment.NewLine + "  ", violations)}

             ADR-009: a note is not a sidebar note or a floating note. It is a
             note, which may be PRESENTED in those ways — several at once.
             Presentation state belongs in NotePresentations, referenced by the
             note, never a property of it.
             """);
    }

    [Fact]
    public void Core_targets_a_platform_neutral_framework()
    {
        // net9.0, never net9.0-windows. The build itself then makes a Windows
        // dependency impossible rather than merely discouraged.
        var target = CoreAssembly
            .GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>()
            ?.FrameworkName ?? string.Empty;

        Assert.DoesNotContain("windows", target, StringComparison.OrdinalIgnoreCase);
    }
}

