using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// Slice 1.3 in the source: only the content moves, inside the docked rectangle and after the window is docked; the
/// hide waits for the content; a show reverses a hide; reduced motion is read per transition with no subscription,
/// timer or polling; motion holds no input and changes no focus; values are WinUI's, by key, and Noto's tokens.
/// What the running application draws is the motion harness's to prove (Invoke-Motion.ps1).
/// </summary>
public sealed class MotionGuardTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private static readonly string Windows = Path.GetDirectoryName(SourcePath("src", "Noto.Windows", "App.xaml.cs"))!;
    private static readonly string Motion = Code(Path.Join(Windows, "ContentMotion.cs"));
    private static readonly string Coordinator = Code(Path.Join(Windows, "WindowCoordinator.cs"));
    private static readonly string Main = Code(Path.Join(Windows, "MainWindow.xaml.cs"));
    private static readonly XDocument MainXaml = XDocument.Load(Path.Join(Windows, "MainWindow.xaml"));
    private static readonly XDocument Tokens = XDocument.Load(SourcePath("src", "Noto.UI", "Tokens", "Tokens.xaml"));

    [Fact]
    public void A_show_prepares_the_content_then_docks_the_window_then_moves_the_content()
    {
        // The first frame shows the content at its starting place; it moves only once the window is at its docked
        // rectangle, so the whole transition runs inside it.
        string show = Case(Coordinator, "WorkspaceAction.Show");

        int prepare = Index(show, "motion.PrepareEnter(docked.Edge);");
        int shown = Index(show, "window.AppWindow.Show(activateWindow: false);");
        int docked = Index(show, "BringForward(redock: true);");
        int enter = Index(show, "motion.Enter(docked.Edge, fromCurrent: false);");

        Assert.True(prepare < shown && shown < docked && docked < enter, $"{prepare} {shown} {docked} {enter}");
    }

    [Fact]
    public void Motion_never_moves_the_window_itself()
    {
        string[] windowMoves = ["AppWindow", "MoveAndResize", "SetWindowPos", "MoveOwn", "WindowDocking", "Redock"];
        var moves = windowMoves.Where(m => Motion.Contains(m, StringComparison.Ordinal)).ToList();
        Assert.True(moves.Count == 0, "Motion moves the window: " + string.Join(", ", moves));

        // A reversal brings the never-hidden window forward without re-docking it; the exit's completion only hides.
        string reverse = Case(Coordinator, "WorkspaceAction.Reverse");
        Assert.Contains("BringForward(redock: false);", reverse, StringComparison.Ordinal);
        Assert.DoesNotContain("redock: true", reverse, StringComparison.Ordinal);
        Assert.DoesNotMatch("MoveAndResize|Redock|redock: true", Body(Coordinator, "OnExitCompleted"));

        // What moves is the content, inside the root's unmoving background.
        Assert.Contains("Motion = new ContentMotion(WorkspaceContent);", Main, StringComparison.Ordinal);
        XElement root = Named(MainXaml, "WorkspaceRoot");
        Assert.NotNull(root.Attribute("Background"));
        Assert.Equal("WorkspaceContent", (string?)root.Elements(Xaml + "Grid").Single().Attribute(X + "Name"));
    }

    [Fact]
    public void Bringing_noto_forward_ends_any_pending_hide_and_a_reversal_brings_the_content_back_from_where_it_is()
    {
        // Every bring-forward action (show, restore, focus, reverse) ends a hide in motion before acting.
        Assert.Matches(@"if \(WorkspaceToggle\.BringsForward\(action\)\)\s*\{\s*_activation\.BroughtForward\(\);\s*_ = _hide\.Reverse\(\);", Coordinator);
        Assert.Contains("motion.Enter(docked.Edge, fromCurrent: true);", Case(Coordinator, "WorkspaceAction.Reverse"), StringComparison.Ordinal);
        Assert.Contains("Hiding: _hide.Hiding", Coordinator, StringComparison.Ordinal);
    }

    [Fact]
    public void The_exits_completion_never_restamps_the_transition_and_never_touches_a_closing_window()
    {
        string completed = Body(Coordinator, "OnExitCompleted");

        // The hide was decided when the exit began: a press during the exit, delivered after it, is not stale.
        Assert.DoesNotContain("_lastTransitionEnd", completed, StringComparison.Ordinal);
        Assert.Equal(1, Regex.Count(Coordinator, @"_lastTransitionEnd = Environment\.TickCount;"));

        // A close that began meanwhile owns the window: no hide, no second save.
        Assert.Matches(@"^\{\s*if \(_shuttingDown\)\s*\{[^}]*return;\s*\}\s*HideStep step = _hide\.Complete", completed);
    }

    [Fact]
    public void The_window_hides_only_when_the_exit_completes()
    {
        string hide = Body(Coordinator, "Hide");

        Assert.Matches(@"case HideStep\.Animate:[\s\S]*motion\.Exit\(docked\.Edge, \(\) => OnExitCompleted\(generation\)\)", hide);
        Assert.DoesNotMatch(@"case HideStep\.Animate:[^;]*AppWindow\.Hide", hide);
        Assert.Matches(@"case HideStep\.HideNow:\s*window\.AppWindow\.Hide\(\);\s*break;", Body(Coordinator, "OnExitCompleted"));
        Assert.Matches(@"case HideStep\.Restore:\s*motion\.Enter\(docked\.Edge, fromCurrent: true\);", Body(Coordinator, "OnExitCompleted"));
    }

    [Fact]
    public void Reduced_motion_is_read_per_transition_with_no_subscription_timer_or_polling()
    {
        Assert.Contains("public bool Enabled => _motion is not null && MotionPlan.Animate(_system.AnimationsEnabled);", Motion, StringComparison.Ordinal);
        Assert.Matches(@"if \(!Enabled\)\s*\{\s*Settle\(\);\s*return;", Body(Motion, "PrepareEnter"));
        Assert.Matches(@"if \(!Enabled\)\s*\{[^}]*Settle\(\);\s*return;", Body(Motion, "Enter"));
        Assert.Matches(@"if \(!Enabled\)\s*\{\s*return false;", Body(Motion, "Exit"));

        string[] loops = ["_system.", "Timer", "CompositionTarget", "Rendering", "Task.Delay", "Thread.Sleep", "while ("];
        var found = loops.Where(l => l != "_system." ? Motion.Contains(l, StringComparison.Ordinal) : Regex.IsMatch(Motion, @"_system\.(?!AnimationsEnabled)")).ToList();
        Assert.True(found.Count == 0, "Motion subscribes, waits or polls: " + string.Join(", ", found));
        Assert.Contains("_hide.Begin(saveBeforeHide, motion.Enabled)", Coordinator, StringComparison.Ordinal);
    }

    [Fact]
    public void Motion_holds_no_input_and_changes_no_focus()
    {
        string[] locks = ["IsHitTestVisible", "IsEnabled", "IsTabStop", ".Focus(", "AllowFocus", "TabIndex"];
        var held = locks.Where(l => Motion.Contains(l, StringComparison.Ordinal)).ToList();
        Assert.True(held.Count == 0, "Motion holds input or focus: " + string.Join(", ", held));
    }

    [Fact]
    public void Each_transition_takes_its_own_direction_duration_and_distance()
    {
        Assert.Contains("MotionPlan.EnterFrom(edge, _motion!.DrawerEnterDistance)", Body(Motion, "Enter"), StringComparison.Ordinal);
        Assert.Contains("_motion.DrawerEnter,", Body(Motion, "Enter"), StringComparison.Ordinal);
        Assert.Contains("MotionPlan.ExitTo(edge, _motion!.DrawerExitDistance)", Body(Motion, "Exit"), StringComparison.Ordinal);
        Assert.Contains("_motion.DrawerExit,", Body(Motion, "Exit"), StringComparison.Ordinal);
        Assert.Contains("MotionPlan.SurfaceFrom(forward, _motion!.SurfaceDistance)", Body(Motion, "EnterSurface"), StringComparison.Ordinal);
        Assert.Contains("_motion.Surface,", Body(Motion, "EnterSurface"), StringComparison.Ordinal);

        // No duration or distance is written in code: WinUI's values by key, Noto's distances as tokens.
        Assert.DoesNotMatch(@"FromMilliseconds|FromSeconds|\b(83|167|250)\b|\b(16|24)f?\b", Motion);
    }

    [Fact]
    public void The_motion_tokens_name_winuis_own_resources_and_noto_distances()
    {
        var values = Tokens.Descendants().Where(e => e.Attribute(X + "Key") is not null).ToDictionary(e => (string)e.Attribute(X + "Key")!, e => e.Value);

        Assert.Equal("ControlNormalAnimationDuration", values["MotionDrawerEnterDuration"]);
        Assert.Equal("ControlFastAnimationDuration", values["MotionDrawerExitDuration"]);
        Assert.Equal("ControlFastAnimationDuration", values["MotionSurfaceDuration"]);
        Assert.Equal("ControlFastOutSlowInKeySpline", values["MotionEasing"]);
        Assert.Equal("24", values["MotionDrawerEnterDistance"]);
        Assert.Equal("16", values["MotionDrawerExitDistance"]);
        Assert.Equal("16", values["MotionSurfaceDistance"]);

        // Only roles with a consumer: nothing for the 83 ms Faster duration.
        Assert.DoesNotContain(values.Values, v => v == "ControlFasterAnimationDuration");
    }

    [Fact]
    public void Each_navigation_moves_its_surface_in_its_own_direction_after_showing_it()
    {
        (string Method, string Surface, bool Forward)[] sites =
        [
            ("EnterSelectedFolder", "NoteRoot", true),
            ("OpenNote", "EditorRoot", true),
            ("OpenSettings", "page", true),
            ("LeaveFolder", "PaneRoot", false),
            ("LeaveEditor", "NoteRoot", false),
            ("CloseSettings", "back", false),
        ];

        foreach (var (method, surface, forward) in sites)
        {
            string body = Body(Main, method);
            string call = $"Motion.EnterSurface({surface}, forward: {(forward ? "true" : "false")});";

            Assert.True(body.Contains(call, StringComparison.Ordinal), $"{method}: {call}");
            Assert.True(Index(body, $"{surface}.Visibility = Visibility.Visible;") < Index(body, call), method);
        }

        Assert.Equal(sites.Length, Regex.Count(Main, @"Motion\.EnterSurface\("));
    }

    // ---------------------------------------------------------------- helpers

    private static string Case(string source, string label)
    {
        int start = Index(source, $"case {label}:");
        int end = source.IndexOf("break;", start, StringComparison.Ordinal);
        return source[start..end];
    }

    private static int Index(string text, string value)
    {
        int index = text.IndexOf(value, StringComparison.Ordinal);
        Assert.True(index >= 0, $"Not found: {value}");
        return index;
    }

    private static XElement Named(XDocument document, string name) => document.Descendants().Single(e => (string?)e.Attribute(X + "Name") == name);

    private static string Code(string path) => Regex.Replace(File.ReadAllText(path), @"^\s*//.*$", string.Empty, RegexOptions.Multiline);

    private static string Body(string source, string method)
    {
        var declaration = Regex.Match(source, $@"\n    (private|internal|public)( static)? [\w?<>,()\s]+ {method}\(");
        Assert.True(declaration.Success, $"Not found: {method}");
        int open = source.IndexOf('{', declaration.Index);
        int depth = 0;

        for (int i = open; i < source.Length; i++)
        {
            depth += source[i] == '{' ? 1 : source[i] == '}' ? -1 : 0;

            if (depth == 0)
            {
                return source[open..(i + 1)];
            }
        }

        throw new InvalidOperationException($"Unbalanced body for {method}");
    }

    /// <summary>Walks up to the repository root; a guard that silently skips is worse than none.</summary>
    private static string SourcePath(params string[] parts)
    {
        string relative = Path.Join(parts);

        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Join(directory.FullName, relative);

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Could not locate {relative} by walking up from {AppContext.BaseDirectory}");
    }
}
