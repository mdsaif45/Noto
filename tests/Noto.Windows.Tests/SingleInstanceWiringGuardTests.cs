using System.Text.RegularExpressions;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// How the application wires A17 (ADR-013), checked in its source.
/// </summary>
/// <remarks>
/// <para>
/// This project must not reference <c>Noto.Windows</c> (ADR-009; see
/// <see cref="PlatformBoundaryTests"/>), so the composition root cannot be
/// loaded and inspected. Its source can be read. These guards pin the four
/// wiring rules the runtime harness can only show indirectly or by timing:
/// ownership before the database, the pipe before the window, the pipe's
/// thread never touching the window, and one coordinator.
/// </para>
/// <para>
/// The runtime harness (<c>tools/validation/Invoke-SingleInstance.ps1</c>)
/// validates the behaviour. These catch a reorder or a shortcut that a lucky
/// timing would let through.
/// </para>
/// </remarks>
public sealed partial class SingleInstanceWiringGuardTests
{
    private static readonly string App = File.ReadAllText(SourcePath("App.xaml.cs"));

    [Fact]
    public void The_data_root_is_claimed_before_the_database_is_created()
    {
        string launch = Body(App, "OnLaunched");

        Assert.True(
            Index(launch, "ClaimDataRoot(") < Index(launch, "new NotoDatabase("),
            "OnLaunched must claim the data root before it creates the database: a second launch must never open it.");
    }

    [Fact]
    public void The_activation_pipe_starts_before_the_database_and_the_window()
    {
        string launch = Body(App, "OnLaunched");
        int pipe = Index(launch, "StartActivationPipe(");

        Assert.True(Index(launch, "ClaimDataRoot(") < pipe, "The pipe is the owner's: it starts after the claim.");
        Assert.True(pipe < Index(launch, "new NotoDatabase("), "The pipe starts before the database, so a launch during startup is taken.");
        Assert.True(pipe < Index(launch, "new MainWindow("), "The pipe starts before the window, so a launch during startup is taken.");
    }

    [Fact]
    public void A_second_launch_exits_inside_the_claim_without_a_window()
    {
        string claim = Body(App, "ClaimDataRoot");

        Assert.Contains("Environment.Exit(", claim, StringComparison.Ordinal);
        Assert.DoesNotContain("Window", Regex.Replace(claim, @"//.*", string.Empty), StringComparison.Ordinal);
    }

    [Fact]
    public void The_pipe_thread_only_hands_the_request_to_the_ui_thread()
    {
        string onRequest = Body(App, "OnLaunchRequested");

        Assert.Contains("TryEnqueue(", onRequest, StringComparison.Ordinal);
        Assert.DoesNotContain("OnActivationRequested", onRequest, StringComparison.Ordinal);
        Assert.DoesNotContain("WindowActivation", onRequest, StringComparison.Ordinal);
        Assert.DoesNotContain("RunPendingLaunch()", onRequest, StringComparison.Ordinal);
    }

    [Fact]
    public void A_launch_goes_through_the_one_coordinator_as_a_launch()
    {
        string run = Body(App, "RunPendingLaunch");

        Assert.Contains("_coordinator.OnActivationRequested(WorkspaceRequest.Launch,", run, StringComparison.Ordinal);
        Assert.DoesNotContain("WorkspaceRequest.Toggle", run, StringComparison.Ordinal);
    }

    [Fact]
    public void There_is_exactly_one_window_coordinator()
    {
        string windowsSource = Path.GetDirectoryName(SourcePath("App.xaml.cs"))!;
        int created = Directory.EnumerateFiles(windowsSource, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Sum(f => Regex.Count(File.ReadAllText(f), @"new WindowCoordinator\("));

        Assert.Equal(1, created);
    }

    [Fact]
    public void A_close_that_goes_ahead_stops_the_pipe_before_ownership_is_released()
    {
        string launch = Body(App, "OnLaunched");

        Assert.True(
            Index(launch, "_activationPipe?.Dispose()") < Index(launch, "_ownership?.Dispose()"),
            "The pipe must close before the mutex is released, so a successor finds the pipe's name free.");
        Assert.Contains("_activationPipe?.BeginShutdown()", launch, StringComparison.Ordinal);
    }

    /// <summary>The text of a method's body, from its declaration to the next member at the same depth.</summary>
    private static string Body(string source, string method)
    {
        Match declaration = Regex.Match(source, $@"\n    (?:protected |private |public |internal )[^\n]*\b{method}\(");
        Assert.True(declaration.Success, $"{method} was not found in App.xaml.cs.");

        int start = declaration.Index;
        int end = source.IndexOf("\n    }\n", start, StringComparison.Ordinal);
        Assert.True(end > start, $"The end of {method} was not found.");

        return source[start..end];
    }

    private static int Index(string text, string value)
    {
        int index = text.IndexOf(value, StringComparison.Ordinal);
        Assert.True(index >= 0, $"'{value}' was not found.");
        return index;
    }

    /// <summary>Walks up to the repository root; a guard that silently skips is worse than none.</summary>
    private static string SourcePath(string file)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Join(directory.FullName, "src", "Noto.Windows", file);

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Could not locate src/Noto.Windows/{file} by walking up from {AppContext.BaseDirectory}");
    }
}
