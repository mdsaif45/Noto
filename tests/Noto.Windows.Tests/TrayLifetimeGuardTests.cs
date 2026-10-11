using System.Text.RegularExpressions;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// How the application wires the tray and its lifetime (#16 slice 7, ADR-014),
/// checked in its source.
/// </summary>
/// <remarks>
/// Like <see cref="WorkspaceDrawerGuardTests"/>: this project must not
/// reference <c>Noto.Windows</c>, so the wiring is read rather than loaded.
/// These pin what a runtime run can show only by luck or not at all: the icon
/// is an input surface whose every request goes to the coordinator or the one
/// close path; there is exactly one shutdown transition; the session's end
/// saves synchronously; and cleanup runs once, icon first.
/// </remarks>
public sealed class TrayLifetimeGuardTests
{
    private static readonly string App = File.ReadAllText(SourcePath("src", "Noto.Windows", "App.xaml.cs"));
    private static readonly string Tray = File.ReadAllText(SourcePath("src", "Noto.Platform.Windows", "TrayIcon.cs"));

    [Fact]
    public void A_tray_selection_is_a_launch_through_the_one_coordinator()
    {
        string activate = Body(App, "ActivateFromTray");

        Assert.Contains("_coordinator.OnActivationRequested(WorkspaceRequest.Launch, requestTime)", activate, StringComparison.Ordinal);
        Assert.DoesNotContain("WorkspaceRequest.Toggle", activate, StringComparison.Ordinal);
        Assert.Contains("ActivateFromTray(receivedAt,", Body(App, "OnTrayInvoked"), StringComparison.Ordinal);
        Assert.Contains("tray.Invoked += (_, e) => OnTrayInvoked(e.ReceivedAt);", Body(App, "StartTray"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_selection_time_is_read_locally_not_from_the_message()
    {
        Assert.DoesNotContain("GetMessageTime", Code(Tray), StringComparison.Ordinal);
        Assert.Contains("int receivedAt = Environment.TickCount;", Body(Tray, "OnCallback"), StringComparison.Ordinal);
    }

    [Fact]
    public void Each_menu_choice_takes_the_path_that_already_owns_it()
    {
        string command = Body(App, "OnTrayCommand");

        Assert.Matches(@"case TrayCommand\.Show:\s*ActivateFromTray\(", command);
        Assert.Matches(@"case TrayCommand\.Hide:\s*OnDismissRequested\(\);", command);
        Assert.Contains("_main?.SetPinned(!_coordinator.Pinned)", command, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"Pinned\s*=(?!=)", command);
        Assert.Matches(@"case TrayCommand\.Quit:\s*Quit\(\);", command);
        Assert.DoesNotContain("WindowActivation", command, StringComparison.Ordinal);
    }

    [Fact]
    public void Quit_is_the_close_alt_f4_makes()
    {
        string quit = Code(Body(App, "Quit"));

        Assert.Contains("WindowClose.Request(_windowHandle)", quit, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\bExit\(|\.Close\(\)|Destroy", quit);
        Assert.True(
            Index(quit, "WindowClose.Request(") < Index(quit, "if (!_closing)"),
            "A cancelled close is detected after the request, which runs Closing synchronously.");
    }

    [Fact]
    public void There_is_exactly_one_shutdown_transition()
    {
        string code = Code(App);

        Assert.Equal(1, Regex.Count(code, @"_closing = true;"));
        Assert.Equal(1, Regex.Count(code, @"_coordinator\?\.BeginShutdown\(\)"));
        Assert.Equal(1, Regex.Count(code, @"_activationPipe\?\.BeginShutdown\(\)"));

        string closing = Regex.Match(code, @"_window\.AppWindow\.Closing \+= [\s\S]*?\n        \};").Value;
        Assert.Contains("if (!e.Cancel && !_closing)", closing, StringComparison.Ordinal);
        Assert.Contains("_closing = true;", closing, StringComparison.Ordinal);
    }

    [Fact]
    public void Cleanup_runs_once_on_closed_icon_first()
    {
        string closed = Regex.Match(Code(App), @"_window\.Closed \+= [\s\S]*?\n        \};").Value;
        int tray = Index(closed, "_tray?.Dispose()");
        int hotkey = Index(closed, "_hotkey?.Dispose()");
        int pipe = Index(closed, "_activationPipe?.Dispose()");
        int ownership = Index(closed, "_ownership?.Dispose()");

        Assert.True(tray < hotkey && hotkey < pipe && pipe < ownership, "Icon, hotkey, pipe, then the mutex.");
        Assert.Equal(1, Regex.Count(Code(App), @"_tray\?\.Dispose\(\)"));
    }

    [Fact]
    public void The_session_query_saves_synchronously_before_it_answers()
    {
        string ending = Code(Body(App, "OnSessionEnding"));

        Assert.True(Index(ending, "SaveBeforeHide()") < Index(ending, "e.Block("));
        Assert.DoesNotContain("TryEnqueue", ending, StringComparison.Ordinal);
        Assert.DoesNotContain("_closing", ending, StringComparison.Ordinal);
    }

    [Fact]
    public void The_session_end_saves_then_asks_the_one_close_path()
    {
        string ended = Code(Body(App, "OnSessionEnded"));

        Assert.True(Index(ended, "SaveBeforeHide()") < Index(ended, "WindowClose.Post(_windowHandle)"));
        Assert.DoesNotContain("TryEnqueue", ended, StringComparison.Ordinal);
        Assert.DoesNotContain("BeginShutdown", ended, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"_closing\s*=(?!=)", ended);
    }

    [Fact]
    public void Tray_input_is_ignored_once_closing()
    {
        Assert.Contains("if (_closing)", Body(App, "ActivateFromTray"), StringComparison.Ordinal);
        Assert.Contains("_closing", Body(App, "OnTrayMenuRequested"), StringComparison.Ordinal);
        Assert.Contains("if (_closing)", Body(App, "OnTrayCommand"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_icon_exists_only_for_the_owner_and_after_the_coordinator()
    {
        string launch = Body(App, "OnLaunched");
        int tray = Index(launch, "StartTray();");

        Assert.True(Index(launch, "ClaimDataRoot(") < tray, "A second launch never shows an icon.");
        Assert.True(Index(launch, "new WindowCoordinator(") < tray, "Every callback finds the coordinator wired.");
        Assert.True(tray < Index(launch, "_coordinator?.MarkReady()"), "Startup callbacks meet the startup rule.");
    }

    [Fact]
    public void The_menu_choice_is_returned_never_taken_from_a_command_message()
    {
        Assert.Contains("TPM_RETURNCMD", Body(Tray, "ShowMenu"), StringComparison.Ordinal);
        Assert.DoesNotContain("WM_COMMAND", Code(Tray), StringComparison.Ordinal);
    }

    [Fact]
    public void A_critical_session_end_is_never_refused()
    {
        Assert.Contains("ShouldBlock(args.Critical,", Body(Tray, "OnQueryEndSession"), StringComparison.Ordinal);
    }

    /// <summary>The source without line comments, so a comment cannot satisfy or fail a guard.</summary>
    private static string Code(string source) => Regex.Replace(source, @"//.*", string.Empty);

    /// <summary>A member's text, from its declaration to the next member at the same depth.</summary>
    private static string Body(string source, string member)
    {
        Match declaration = Regex.Match(source, $@"\n    (?:protected |private |public |internal )[^\n(]*\b{member}\(");
        Assert.True(declaration.Success, $"{member} was not found.");

        int start = declaration.Index;
        int end = source.IndexOf("\n    }\n", start, StringComparison.Ordinal);
        int expressionEnd = source.IndexOf(";\n\n", start, StringComparison.Ordinal);

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
