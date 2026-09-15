using System.Reflection;
using Xunit;

namespace Noto.Infrastructure.Tests;

/// <summary>
/// Infrastructure implements what the domain defines, and nothing above it.
/// </summary>
public sealed class BoundaryTests
{
    private static readonly Assembly InfrastructureAssembly = typeof(AssemblyMarker).Assembly;

    // Deliberately NOT asserted: "this layer references Noto.Core".
    // Roslyn elides assembly references that no code actually uses, so until
    // this project consumes a Core type the reference is absent from metadata
    // and such a test fails for a reason that has nothing to do with layering.
    // The project reference is declared in the .csproj; the invariant worth
    // enforcing is the FORBIDDEN direction, below.

    [Fact]
    public void Infrastructure_does_not_reference_the_ui()
    {
        string[] forbidden = ["Noto.Windows", "Microsoft.UI"];

        var violations = InfrastructureAssembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(n => forbidden.Any(f => n.StartsWith(f, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Noto.Infrastructure must not reference: {string.Join(", ", violations)}.");
    }
}
