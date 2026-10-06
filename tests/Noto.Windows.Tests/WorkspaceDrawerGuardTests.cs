using System.Text.RegularExpressions;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// How the application wires the drawer (#16 slice 6), checked in its source.
/// </summary>
/// <remarks>
/// Like <see cref="SingleInstanceWiringGuardTests"/>: this project must not
/// reference <c>Noto.Windows</c>, so the wiring is read rather than loaded.
/// These pin the rules a lucky timing would let a runtime run miss: the window
/// never hides itself, every dismissal goes to the one coordinator, a
/// deactivation is judged after the fact and never for Noto's own windows,
/// topmost does not depend on docking, and pin is never stored.
/// </remarks>
public sealed partial class WorkspaceDrawerGuardTests
{
    private static readonly string App = File.ReadAllText(SourcePath("App.xaml.cs"));
    private static readonly string Main = File.ReadAllText(SourcePath("MainWindow.xaml.cs"));
    private static readonly string Coordinator = File.ReadAllText(SourcePath("WindowCoordinator.cs"));

    [Fact]
    public void The_window_never_hides_itself()
    {
        Assert.DoesNotMatch(@"\.Hide\(\)", Code(Main));
        Assert.DoesNotContain("ShowWindow", Code(Main), StringComparison.Ordinal);
    }

    [Fact]
    public void Ctrl_W_and_a_hiding_Escape_ask_to_be_dismissed()
    {
        Assert.Contains("DismissRequested?.Invoke(", Body(Main, "OnDismissAccelerator"), StringComparison.Ordinal);
        Assert.Contains("DismissRequested?.Invoke(", Body(Main, "OnEscape"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_dismissal_goes_to_the_one_coordinator_as_a_dismissal()
    {
        Assert.Contains("_coordinator?.OnActivationRequested(WorkspaceRequest.Dismiss,", Body(App, "OnDismissRequested"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_deactivation_is_judged_after_the_fact_and_never_for_noto_s_own_windows()
    {
        string handler = Body(App, "OnWorkspaceActivated");

        Assert.True(Index(handler, "TryEnqueue(") < Index(handler, "ForegroundIsThisProcess()"),
            "The deactivation is posted to the dispatcher before it is judged.");
        Assert.True(Index(handler, "ForegroundIsThisProcess()") < Index(handler, "WorkspaceRequest.Deactivated"),
            "Activation moving to one of Noto's own windows is dropped before the coordinator sees it.");
    }

    [Fact]
    public void Topmost_is_set_at_launch_whether_or_not_docking_succeeded()
    {
        string launch = Body(App, "OnLaunched");
        int topmost = Index(launch, "WindowCoordinator.EnsureTopmost(_window)");

        Assert.True(Index(launch, "DockAtLaunch(") < topmost, "Topmost is set after docking is attempted.");
        Assert.True(topmost < Index(launch, "if (_docked is not null)"), "Topmost does not depend on docking having succeeded.");
    }

    [Fact]
    public void Every_path_that_shows_asserts_topmost()
    {
        string forward = Body(Coordinator, "BringForward");

        Assert.True(Index(forward, "EnsureTopmost(window)") < Index(forward, "BringToForeground("));
    }

    [Fact]
    public void Every_hide_saves_first()
    {
        string hide = Body(Coordinator, "Hide");

        Assert.True(Index(hide, "saveBeforeHide()") < Index(hide, "AppWindow.Hide()"));
    }

    [Fact]
    public void Deactivation_may_hide_only_with_the_setting_on_and_not_pinned()
    {
        Assert.Contains("HideOnDeactivation: hideOnDeactivation && !_pinned", Coordinator, StringComparison.Ordinal);
    }

    [Fact]
    public void Pin_is_never_stored()
    {
        Assert.DoesNotContain(".Write(", Body(Main, "OnPinToggled"), StringComparison.Ordinal);
        Assert.DoesNotMatch(@"Write\(|Settings", Regex.Match(App, @"main\.PinChanged \+=[\s\S]*?\};").Value);
    }

    /// <summary>The source without line comments, so a comment cannot satisfy or fail a guard.</summary>
    private static string Code(string source) => Regex.Replace(source, @"//.*", string.Empty);

    /// <summary>A member's text, from its declaration to the next member at the same depth.</summary>
    private static string Body(string source, string member)
    {
        Match declaration = Regex.Match(source, $@"\n    (?:protected |private |public |internal )[^\n]*\b{member}\(");
        Assert.True(declaration.Success, $"{member} was not found.");

        int start = declaration.Index;
        int end = source.IndexOf("\n    }\n", start, StringComparison.Ordinal);
        int expressionEnd = source.IndexOf(";\n\n", start, StringComparison.Ordinal);

        // An expression-bodied member ends at its semicolon.
        if (expressionEnd > start && (end < 0 || expressionEnd < end) && !source[start..expressionEnd].Contains('{', StringComparison.Ordinal))
        {
            return source[start..expressionEnd];
        }

        Assert.True(end > start, $"The end of {member} was not found.");
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
