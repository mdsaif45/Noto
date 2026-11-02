using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// The note stack's structure (ADR-016), checked in its source as the other wiring guards are: this project must not
/// reference <c>Noto.Windows</c>. What the stack does at run time is <c>Invoke-NoteStack.ps1</c>'s; its rules are
/// <c>NoteStackSession</c>'s, tested against real SQLite.
/// </summary>
public sealed class NoteStackGuardTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly string Windows = Path.GetDirectoryName(SourcePath("src", "Noto.Windows", "App.xaml.cs"))!;
    private static readonly string Main = Code(Path.Join(Windows, "MainWindow.xaml.cs"));
    private static readonly XDocument MainXaml = XDocument.Load(Path.Join(Windows, "MainWindow.xaml"));

    [Fact]
    public void Only_the_editor_host_references_its_inner_control()
    {
        // ADR-016 §3: P2 replaces the host's inside, so nothing outside it may reach the TextBox.
        var reaching = Directory.EnumerateFiles(Windows, "*.*")
            .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".xaml", StringComparison.Ordinal))
            .Where(f => !Path.GetFileName(f).StartsWith("NoteEditorHost.", StringComparison.Ordinal))
            .Where(f => Regex.IsMatch(File.ReadAllText(f), @"\bNoteEditor\b"))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(reaching.Count == 0, "References the editor's inner control: " + string.Join(", ", reaching));
        Assert.Matches(@"<TextBox\s+x:Name=""NoteEditor""", File.ReadAllText(Path.Join(Windows, "NoteEditorHost.xaml")));
    }

    [Fact]
    public void Only_the_card_being_edited_holds_an_editor_and_a_bar_and_it_alone_hides_its_read_view()
    {
        XElement template = Card();

        Assert.Equal("{x:Bind IsEditing, Mode=OneWay}", (string?)Named(template, "CardEditor").Attribute(X + "Load"));
        Assert.Equal("{x:Bind IsEditing, Mode=OneWay}", (string?)Named(template, "CardBar").Attribute(X + "Load"));
        Assert.Equal("{x:Bind IsReading, Mode=OneWay}", (string?)Named(template, "CardReadView").Attribute(X + "Load"));
        Assert.Equal("NoteEditorHost", Named(template, "CardEditor").Name.LocalName);
        Assert.Single(template.Descendants(), e => e.Name.LocalName == "NoteEditorHost");
    }

    [Fact]
    public void A_cards_name_is_its_title_and_its_states_are_its_help_text()
    {
        XElement root = Card().Elements().Single();

        Assert.Equal("{x:Bind DisplayTitle, Mode=OneWay}", (string?)root.Attribute("AutomationProperties.Name"));
        Assert.Equal("{x:Bind HelpText}", (string?)root.Attribute("AutomationProperties.HelpText"));
    }

    [Fact]
    public void An_editor_loads_the_stacks_text_for_its_card_so_a_recycled_container_loses_nothing()
    {
        // The text lives in the session, not the control: a realised editor loads it, every change goes back to it,
        // and an editor for a card that is not the editing one loads nothing.
        string loaded = Body(Main, "OnEditorHostLoaded");
        Assert.Contains("_stack.Editing?.Value != card.Id", loaded, StringComparison.Ordinal);
        Assert.Contains("host.Load(NoteId.From(card.Id), _stack.Draft);", loaded, StringComparison.Ordinal);
        Assert.Contains("_stack.UpdateDraft(note, host.Markdown);", Body(Main, "OnEditorContentChanged"), StringComparison.Ordinal);
    }

    [Fact]
    public void Every_way_out_of_editing_saves_first_and_a_failed_save_stays()
    {
        // Leaving the folder, editing another card and creating a note each end the current edit, and stop if it fails.
        Assert.Matches(@"^\{\s*if \(!TryEndEdit\(\)\)\s*\{\s*return;", Body(Main, "LeaveFolder"));
        Assert.Contains("if (!TryEndEdit())", Body(Main, "EditCard"), StringComparison.Ordinal);
        Assert.Contains("|| !TryEndEdit()", Body(Main, "CreateNoteInOpenFolder"), StringComparison.Ordinal);
        Assert.Matches(@"if \(!TrySaveCard\(\)\)\s*\{\s*return false;", Body(Main, "TryEndEdit"));

        // A successful save clears a failure's notice, however the user then left: it no longer applies.
        Assert.Matches(@"Noto\.Save completed[^\n]*\n\s*HideNoteNotice\(\);\s*return true;", Body(Main, "TrySaveCard"));

        // Hiding and closing save without leaving.
        Assert.Contains("internal bool SaveBeforeHide() => TrySaveCard();", Main, StringComparison.Ordinal);
        Assert.Contains("TrySaveCard(CloseAgainToDiscard)", Body(Main, "OnAppWindowClosing"), StringComparison.Ordinal);
    }

    [Fact]
    public void Enter_and_F2_edit_only_from_a_card_never_from_the_editor()
    {
        // Keys typed in the editor tunnel through the list's PreviewKeyDown: Enter there is a new line.
        Assert.Contains(
            "if (e.Key is VirtualKey.Enter or VirtualKey.F2 && e.OriginalSource is ListViewItem { Content: NoteCard card })",
            Body(Main, "OnNoteListPreviewKeyDown"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Every_card_action_is_in_its_menu_with_its_key()
    {
        string menu = Body(Main, "OnNoteListContextRequested");

        Assert.Contains(@"MenuItem(""Edit"", ""Enter"",", menu, StringComparison.Ordinal);
        Assert.Contains(@"MenuItem(""New note"", ""Ctrl+N"",", menu, StringComparison.Ordinal);
        Assert.Contains(@"MenuItem(""Move to the Recycle Bin"", ""Ctrl+Alt+Backspace"",", menu, StringComparison.Ordinal);
        Assert.Equal("OnNoteListContextRequested", (string?)Named(MainXaml.Root!, "NoteList").Attribute("ContextRequested"));

        // A keyboard request (Shift+F10, the Menu key) comes from the row's container, whose item is its Content: both
        // menus find their row that way first, or no menu opens from the keyboard (measured on the runtime case).
        Assert.Contains("ItemOf<NoteCard>(args.OriginalSource) is not { } card", menu, StringComparison.Ordinal);
        Assert.Contains("source is ListViewItem { Content: T item } ? item : (source as FrameworkElement)?.DataContext as T;", Main, StringComparison.Ordinal);

        // The folder list's menu, likewise.
        string folders = Body(Main, "OnFolderListContextRequested");
        Assert.Contains(@"MenuItem(""Open"", ""Ctrl+Down"", EnterSelectedFolder)", folders, StringComparison.Ordinal);
        Assert.Contains("ItemOf<FolderListItem>(args.OriginalSource) is not { } folder", folders, StringComparison.Ordinal);
        Assert.Contains(@"MenuItem(""Rename"", ""F2"", BeginRename)", folders, StringComparison.Ordinal);
        Assert.Contains(@"MenuItem(""New folder"", ""Ctrl+N"", FocusInput)", folders, StringComparison.Ordinal);
        Assert.Equal("OnFolderListContextRequested", (string?)Named(MainXaml.Root!, "FolderList").Attribute("ContextRequested"));
    }

    [Fact]
    public void Moving_is_one_path_from_the_key_and_the_menu_saves_first_and_never_offers_the_cards_own_folder()
    {
        // Ctrl+Shift+M (G16) on the stack's root, so it works from a card and from its editor.
        XElement move = MainXaml.Root!.Descendants().Single(e => e.Name.LocalName == "KeyboardAccelerator" && (string?)e.Attribute("Invoked") == "OnMoveAccelerator");
        Assert.Equal("M", (string?)move.Attribute("Key"));
        Assert.Equal("Control,Shift", (string?)move.Attribute("Modifiers"));
        Assert.Equal("NoteRoot", (string?)move.Ancestors().First(e => e.Attribute(X + "Name") is not null).Attribute(X + "Name"));

        // The key's menu and the card menu's "Move to" are built by the same method.
        Assert.Contains("MoveDestinations(id)", Body(Main, "OnMoveAccelerator"), StringComparison.Ordinal);
        Assert.Contains("MoveDestinations(card.Id)", Body(Main, "OnNoteListContextRequested"), StringComparison.Ordinal);

        // Recent first, then all others; the card's own folder is excluded from both.
        string destinations = Body(Main, "MoveDestinations");
        Assert.Contains("RecentFolders.Shown(RecentMoveFolders?.Invoke() ?? [], folders, _openFolderId)", destinations, StringComparison.Ordinal);
        Assert.Contains("folders.Where(f => f.Id != _openFolderId)", destinations, StringComparison.Ordinal);
        Assert.True(destinations.IndexOf("recent", StringComparison.Ordinal) < destinations.IndexOf("\"All folders\"", StringComparison.Ordinal));

        // The card being edited is saved first, and the move goes through the session.
        string moveCard = Body(Main, "MoveCard");
        Assert.Contains("(_stack.Editing?.Value == id && !TryEndEdit())", moveCard, StringComparison.Ordinal);
        Assert.Contains("_stack.Move(note, target.Id)", moveCard, StringComparison.Ordinal);
        Assert.True(moveCard.IndexOf("_stack.AfterRemoval(note)", StringComparison.Ordinal) < moveCard.IndexOf("_stack.Move(", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------- helpers

    private static XElement Card() =>
        Named(MainXaml.Root!, "NoteList").Descendants().Single(e => e.Name.LocalName == "DataTemplate");

    private static XElement Named(XElement scope, string name) =>
        scope.Descendants().Single(e => (string?)e.Attribute(X + "Name") == name);

    private static string Code(string path) => Regex.Replace(File.ReadAllText(path), @"^\s*//.*$", string.Empty, RegexOptions.Multiline);

    private static string Body(string source, string method)
    {
        var declaration = Regex.Match(source, $@"(private|internal)(?: static)? [\w<>?,\[\] ]+? {method}\(");
        Assert.True(declaration.Success, $"Not found: {method}");
        int open = source.IndexOf('{', declaration.Index);
        int depth = 0;

        for (int i = open; i < source.Length; i++)
        {
            depth += source[i] switch { '{' => 1, '}' => -1, _ => 0 };

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
