using System.Text.RegularExpressions;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// The <c>noto://</c> link and the command line (#192, ADR-022) in the source: read once by the one parser, handed to
/// a running Noto through the activation pipe, done by the window's existing paths after Noto is shown, and only ever
/// creating or showing. What a real launch does is <c>tools/validation/Invoke-Automation.ps1</c>.
/// </summary>
public sealed class AutomationGuardTests
{
    private static readonly string App = Code(File.ReadAllText(SourcePath("src", "Noto.Windows", "App.xaml.cs")));
    private static readonly string Main = Code(File.ReadAllText(SourcePath("src", "Noto.Windows", "MainWindow.xaml.cs")));
    private static readonly string Automation = Code(File.ReadAllText(SourcePath("src", "Noto.Windows", "MainWindow.Automation.cs")));

    [Fact]
    public void A_launch_reads_its_command_line_once_with_the_one_parser_and_a_login_start_never_does()
    {
        Assert.Contains("AutomationRequest? automation = login ? null : ReadAutomation(commandLine);", App, StringComparison.Ordinal);
        Assert.Contains("AutomationParse parsed = NotoCommandLine.Parse(commandLine[1..]);", App, StringComparison.Ordinal);
        Assert.Equal(1, Regex.Count(App, @"NotoCommandLine\.Parse\("));

        // A refusal is traced by its reason, never with the text.
        Assert.Contains("Noto.Automation refused reason={parsed.Refusal} from=start", App, StringComparison.Ordinal);
        Assert.Contains("login ? ClaimForLogin(paths) : ClaimDataRoot(paths, automation)", App, StringComparison.Ordinal);
    }

    [Fact]
    public void A_second_launch_hands_its_request_over_the_activation_pipe()
    {
        string claim = Method(App, "ClaimDataRoot");

        Assert.Contains("ActivationRequest handoff = ActivationRequest.For(automation);", claim, StringComparison.Ordinal);
        Assert.Contains("InstanceOwnership.Claim(_instanceKey, ProcessIdentity.IsElevated(), handoff, out _ownership)", claim, StringComparison.Ordinal);
        Assert.Contains("kind={handoff.Kind}", claim, StringComparison.Ordinal);
    }

    [Fact]
    public void The_running_Noto_queues_a_links_request_beside_the_launch_bounded_and_does_it_after_showing_itself()
    {
        Assert.Contains("ActivationPipeServer.Start(_instanceKey!, request => OnLaunchRequested(ui, request),", App, StringComparison.Ordinal);

        string received = Method(App, "OnLaunchRequested");
        Assert.Contains("request.Kind == ActivationKind.Uri && NotoUri.Parse(request.Payload).Request is { } automation", received, StringComparison.Ordinal);
        Assert.Contains("if (_automation.Count < MaxWaitingAutomation)", received, StringComparison.Ordinal);
        Assert.True(received.IndexOf("_automation.Enqueue(automation);", StringComparison.Ordinal) < received.IndexOf("_pendingLaunch.Offer(", StringComparison.Ordinal));

        // Shown first, as any launch is; then each request, in order.
        string run = Method(App, "RunPendingLaunch");
        int shown = run.IndexOf("_coordinator.OnActivationRequested(WorkspaceRequest.Launch, requestTime)", StringComparison.Ordinal);
        int done = run.IndexOf("_main?.RunAutomation(automation);", StringComparison.Ordinal);
        Assert.True(shown >= 0 && done > shown);
        Assert.Contains("while (_automation.TryDequeue(out AutomationRequest? automation))", run, StringComparison.Ordinal);
        Assert.DoesNotContain("WorkspaceRequest.Toggle", run, StringComparison.Ordinal);
    }

    [Fact]
    public void A_first_start_does_its_request_once_the_folder_list_has_loaded_after_the_welcome_is_decided()
    {
        Assert.Matches(@"if \(automation is not null\)\s*\{\s*main\.RunAutomation\(automation\);\s*\}", App);
        Assert.Matches(@"Refresh\(\);\s*OpenLastFolderAtStart\(\);\s*OpenWelcomeAtStart\(\);\s*RunAutomationAtStart\(\);", Method(Main, "OnPaneFirstLoaded"));

        string run = Method(Automation, "RunAutomation");
        Assert.True(run.IndexOf("if (!_automationReady)", StringComparison.Ordinal) < run.IndexOf("switch (request)", StringComparison.Ordinal));

        // The welcome is the user's first steps: a link never interrupts it.
        Assert.True(run.IndexOf("if (WelcomeSurface is not null)", StringComparison.Ordinal) < run.IndexOf("switch (request)", StringComparison.Ordinal));
    }

    [Fact]
    public void Each_request_goes_through_the_path_the_window_already_has()
    {
        string run = Method(Automation, "RunAutomation");

        Assert.Contains("case AutomationRequest.NewNote note:", run, StringComparison.Ordinal);
        Assert.Contains("NewNoteFromShortcut(content: request.Text);", Method(Automation, "NewNoteFromAutomation"), StringComparison.Ordinal);
        Assert.Contains("OpenFromSearch(target, null, 0);", run, StringComparison.Ordinal);
        Assert.Contains("LeaveForFolderList()", run, StringComparison.Ordinal);
        Assert.Contains("_commands.Dispatch(new CreateFolder(name))", Method(Automation, "NewFolderFromAutomation"), StringComparison.Ordinal);
        Assert.Contains("OpenSearchFromShortcut();", run, StringComparison.Ordinal);
        Assert.Contains("SearchSurface?.ShowQuery(search.Query)", run, StringComparison.Ordinal);
        Assert.Contains("internal void ShowQuery(string query) => SearchBox.Text = query;", File.ReadAllText(SourcePath("src", "Noto.Windows", "SearchPage.xaml.cs")), StringComparison.Ordinal);

        // The new-note path carries the text to the command, with its placement rule.
        Assert.Contains("CommandResult<NoteId> result = _stack.Create(NewNotePlacement, selected, content, NewNoteMode, NewNoteColor);", Main, StringComparison.Ordinal);
        Assert.Contains(
            "_commands.Dispatch(new CreateNote(folder, content, placement, selected, mode, colorKey))",
            File.ReadAllText(SourcePath("src", "Noto.UseCases", "Workspace", "NoteStackSession.cs")),
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_link_only_ever_creates_or_shows()
    {
        // Nothing that deletes, moves, renames, restores or overwrites; nothing that exports, reads a file or runs one.
        Assert.DoesNotMatch(@"new (Delete|Purge|Move|Rename|Restore|Reorder|Empty|Update|Set)\w*\(", Automation);
        Assert.DoesNotMatch(@"\b(ShellLaunch|Process\.Start|OpenLink|QueueOpenLink|Export|File\.|Directory\.|Portability)\b", Automation);
    }

    [Fact]
    public void A_folders_menu_copies_its_link_by_id()
    {
        string menu = Method(Main, "OnFolderListContextRequested");

        Assert.Contains("menu.Items.Add(MenuItem(\"Copy link\", string.Empty, () => CopyFolderLink(folder.Id)));", menu, StringComparison.Ordinal);

        string copy = Method(Automation, "CopyFolderLink");
        Assert.Contains("package.SetText(NotoUri.Format(new AutomationRequest.OpenFolder(folderId)));", copy, StringComparison.Ordinal);
        Assert.Contains("catch (COMException)", copy, StringComparison.Ordinal);
        Assert.Contains("ShowStatus(\"Link copied.\");", copy, StringComparison.Ordinal);
    }

    /// <summary>A method's body, from its declaration to its closing brace.</summary>
    private static string Method(string source, string name)
    {
        Match declaration = Regex.Match(source, $@"(private|internal|public)(?: static)? [\w<>?,\[\] ]+? {name}\(");
        Assert.True(declaration.Success, $"Not found: {name}");
        int open = source.IndexOf('{', declaration.Index);
        int depth = 0;

        for (int i = open; i < source.Length; i++)
        {
            depth += source[i] switch { '{' => 1, '}' => -1, _ => 0 };

            if (depth == 0)
            {
                return source[declaration.Index..(i + 1)];
            }
        }

        throw new InvalidOperationException($"Unbalanced: {name}");
    }

    // Comments out, so a guard is met by code; "://" inside a string is kept.
    private static string Code(string source) => Regex.Replace(source, @"(?<!:)//.*", string.Empty);

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
