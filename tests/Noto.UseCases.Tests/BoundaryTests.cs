using System.Reflection;
using Xunit;

namespace Noto.UseCases.Tests;

/// <summary>
/// The use-case layer sits above the domain and below the platform.
/// </summary>
public sealed class BoundaryTests
{
    private static readonly Assembly ApplicationAssembly = typeof(AssemblyMarker).Assembly;

    // Deliberately NOT asserted: "this layer references Noto.Core".
    // Roslyn elides assembly references that no code actually uses, so until
    // this project consumes a Core type the reference is absent from metadata
    // and such a test fails for a reason that has nothing to do with layering.
    // The project reference is declared in the .csproj; the invariant worth
    // enforcing is the FORBIDDEN direction, below.

    [Fact]
    public void Application_does_not_reference_the_ui_or_platform()
    {
        string[] forbidden = ["Noto.Windows", "Noto.Platform.Windows", "Microsoft.UI"];

        var violations = ApplicationAssembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(n => forbidden.Any(f => n.StartsWith(f, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Noto.UseCases must not reference: {string.Join(", ", violations)}. "
            + "Use cases are invoked by the UI, not the other way round (ADR-010).");
    }
}
