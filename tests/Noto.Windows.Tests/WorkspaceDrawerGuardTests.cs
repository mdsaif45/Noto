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
/// deactivation comes from <c>WM_ACTIVATEAPP</c> and is decided after the fact
/// only while its generation holds, topmost does not depend on docking, and
/// pin is never stored.
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
    public void A_deactivation_comes_from_WM_ACTIVATEAPP_not_from_reading_the_foreground()
    {
        Assert.Contains("_docked.AppActivationChanged += (_, active) => OnAppActivationChanged(active);", App, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\.Activated \+=", Code(App));
        Assert.DoesNotContain("ForegroundIsThisProcess", Code(App), StringComparison.Ordinal);
        Assert.DoesNotContain("GetForegroundWindow", Code(App), StringComparison.Ordinal);
    }

    [Fact]
    public void A_deactivation_is_recorded_now_and_decided_after_the_fact_with_its_generation()
    {
        string handler = Body(App, "OnAppActivationChanged");

        Assert.True(Index(handler, "_coordinator.OnAppActivated()") < Index(handler, "_coordinator.OnAppDeactivated()"),
            "A return of activation is handled on its own path, before any deactivation.");
        int left = Index(handler, "_coordinator.OnAppDeactivated()");
        int posted = handler.IndexOf("TryEnqueue(", left, StringComparison.Ordinal);

        Assert.True(posted > left, "The generation is taken inside the activation change, before anything is posted.");
        Assert.True(posted < Index(handler, "_coordinator.OnDeactivationRequested(generation,"),
            "The deactivation is decided on the dispatcher, carrying its generation.");
    }

    [Fact]
    public void A_deactivation_hides_only_while_its_generation_is_current()
    {
        Assert.Contains("Handle(WorkspaceRequest.Deactivated, requestTime, _activation.IsCurrent(generation),", Body(Coordinator, "OnDeactivationRequested"), StringComparison.Ordinal);
        Assert.Contains("Handle(request, requestTime, generationCurrent: false,", Body(Coordinator, "OnActivationRequested"), StringComparison.Ordinal);
        Assert.Contains("GenerationCurrent: generationCurrent", Body(Coordinator, "Handle"), StringComparison.Ordinal);
        Assert.Contains("_activation.Deactivated()", Body(Coordinator, "OnAppDeactivated"), StringComparison.Ordinal);
        Assert.Contains("_activation.Activated()", Body(Coordinator, "OnAppActivated"), StringComparison.Ordinal);
    }

    [Fact]
    public void An_activation_is_recorded_now_and_reconciled_after_the_fact_with_its_generation()
    {
        string handler = Body(App, "OnAppActivationChanged");
        int returned = Index(handler, "_coordinator.OnAppActivated()");
        int posted = handler.IndexOf("TryEnqueue(", returned, StringComparison.Ordinal);

        Assert.True(posted > returned, "The generation is taken inside the activation change, before anything is posted.");
        Assert.True(posted < Index(handler, "_coordinator.OnActivationReturned(returned,"),
            "The reconciliation is decided on the dispatcher, carrying its generation.");
        Assert.Contains("Handle(WorkspaceRequest.Activated, requestTime, _activation.IsCurrent(generation),", Body(Coordinator, "OnActivationReturned"), StringComparison.Ordinal);
    }

    [Fact]
    public void Bringing_the_window_forward_makes_every_pending_deactivation_stale()
    {
        string handle = Body(Coordinator, "Handle");
        int forward = Index(handle, "if (WorkspaceToggle.BringsForward(action))");

        Assert.True(forward < Index(handle, "_activation.BroughtForward()"));
        Assert.True(Index(handle, "_activation.BroughtForward()") < Index(handle, "switch (action)"),
            "Recorded before the window is touched, so a refused foreground cannot skip it.");
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
