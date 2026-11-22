using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// #195 (parity D27, D28, D45) in the source: a note's mode is chosen from its card's menu through the one session path,
/// and a new note's from Settings; the editor reads the text in that mode (Code monospace with spell check off, Code
/// and plain text never styled, formatted or given hidden markup), and so does the card's read view; the mode is named
/// in the card's help text. What it looks like and that it survives a restart is the editor harness's to show.
/// </summary>
public sealed class NoteModeGuardTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly string Windows = Path.GetDirectoryName(SourcePath("src", "Noto.Windows", "App.xaml.cs"))!;
    private static readonly string Host = Code(Path.Join(Windows, "NoteEditorHost.xaml.cs"));
    private static readonly string Toolbar = Code(Path.Join(Windows, "NoteEditorHost.Toolbar.cs"));
    private static readonly string Main = Code(Path.Join(Windows, "MainWindow.xaml.cs"));
    private static readonly string App = Code(Path.Join(Windows, "App.xaml.cs"));
    private static readonly string Card = Code(Path.Join(Windows, "NoteCard.cs"));
    private static readonly string ReadView = Code(Path.Join(Windows, "MarkdownReadView.cs"));
    private static readonly string Page = Code(Path.Join(Windows, "SettingsPage.xaml.cs"));
    private static readonly XDocument MainXaml = XDocument.Load(Path.Join(Windows, "MainWindow.xaml"));
    private static readonly XDocument PageXaml = XDocument.Load(Path.Join(Windows, "SettingsPage.xaml"));

    // ---------------------------------------------------------------- the editor

    [Fact]
    public void Code_turns_spell_check_off_and_markdown_and_plain_text_keep_it()
    {
        // D45. Set whenever the mode is: on Load, and on a change while editing.
        Assert.Matches(@"_mode = mode;\s*NoteEditor\.IsSpellCheckEnabled = mode != NoteMode\.Code;", Body(Host, "UseMode"));
        Assert.Matches(@"_history\.Clear\(\);\s*UseMode\(mode\);", Body(Host, "Load"));
        Assert.Contains("UseMode(mode);", Body(Host, "SetMode"), StringComparison.Ordinal);
        Assert.Equal(2, Regex.Count(Host, @"\bUseMode\(mode\);"));
    }

    [Fact]
    public void Code_is_written_in_the_code_font_with_a_fallback_and_the_others_in_the_note_font()
    {
        Assert.Contains("BodyFont: _mode == NoteMode.Code ? CodeFont : FontFamily.Source.Split(',')[0].Trim(),", Host, StringComparison.Ordinal);
        Assert.Contains("CodeFont: CodeFont,", Host, StringComparison.Ordinal);
        Assert.Matches(@"CodeFont =\s*File\.Exists\(Path\.Join\(Environment\.GetFolderPath\(Environment\.SpecialFolder\.Fonts\), ""CascadiaMono\.ttf""\)\) \? ""Cascadia Mono"" : ""Consolas"";", Host);

        // The card's read view uses the design system's code family, whose own fallback is Consolas.
        Assert.Contains("<FontFamily x:Key=\"FontFamilyCode\">Cascadia Mono, Consolas</FontFamily>", File.ReadAllText(SourcePath("src", "Noto.UI", "Tokens", "Tokens.xaml")), StringComparison.Ordinal);
    }

    [Fact]
    public void Code_and_plain_text_are_never_styled_because_every_parse_is_the_modes()
    {
        // The one parse the styling, the checkboxes, the pictures and the links all read: in Code and plain text it has
        // no span, so nothing is bold, hidden, drawn or linked, and the text shows exactly as typed.
        string editor = string.Join('\n', Directory.EnumerateFiles(Windows, "NoteEditorHost*.cs").Select(Code));
        var parses = Regex.Matches(editor, @"MarkdownDocument\.Parse\(([^)]*)\)").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(["string.Empty", "_text, _mode", "_text, _mode", "now, _mode", "from, _mode", "now, _mode", "now, _mode"], parses);

        // A change of mode while editing parses and styles the whole note again, keeping the caret (RestyleAll).
        Assert.Matches(@"Flush\(\);\s*UseMode\(mode\);\s*_document = MarkdownDocument\.Parse\(_text, _mode\);\s*HideToolbar\(\);\s*RestyleAll\(\);", Body(Host, "SetMode"));
    }

    [Fact]
    public void Formatting_lists_html_paste_and_the_toolbar_are_markdowns_only()
    {
        // A command would type Markdown into a code note: only undo, redo and Show markup run outside Markdown.
        string run = Body(Host, "Run");
        Assert.True(Index(run, "if (_mode != NoteMode.Markdown)") > Index(run, "case EditorCommand.ShowMarkup:"));
        Assert.True(Index(run, "if (_mode != NoteMode.Markdown)") < Index(run, "Func<string, TextSelection, TextEdit?> transform"));

        Assert.Contains("TextEdit? edit = shift || _mode != NoteMode.Markdown ? null : MarkdownCommands.ContinueList(_text, _selection);", Host, StringComparison.Ordinal);

        string paste = Body(Host, "PasteAsync");
        Assert.Matches(@"bool asMarkdown = _mode == NoteMode\.Markdown;\s*formatted &= asMarkdown;", paste);
        Assert.Contains("if (asMarkdown && clipboard.Contains(StandardDataFormats.StorageItems))", paste, StringComparison.Ordinal);
        Assert.Contains("if (asMarkdown && paths.Count == 0", paste, StringComparison.Ordinal);
        Assert.Contains("plain is not null && asMarkdown ? MarkdownCommands.LinkSelection(", paste, StringComparison.Ordinal);

        Assert.Contains("if (!ShowsFormatToolbar || _mode != NoteMode.Markdown || _selection.IsCaret", Body(Toolbar, "ShowToolbar"), StringComparison.Ordinal);

        // The card's Format button shows for Markdown only.
        XElement format = Named(MainXaml, "CardFormatButton");
        Assert.Equal("{x:Bind FormatVisibility, Mode=OneWay}", (string?)format.Attribute("Visibility"));
        Assert.Contains("public Visibility FormatVisibility => Mode == NoteMode.Markdown ? Visibility.Visible : Visibility.Collapsed;", Card, StringComparison.Ordinal);
    }

    [Fact]
    public void Tab_in_code_types_the_indent_and_shift_tab_still_moves_focus()
    {
        string keys = Body(Host, "OnPreviewKeyDown");
        Assert.Matches(@"if \(_mode != NoteMode\.Markdown\)\s*\{\s*shiftItems = _mode == NoteMode\.Code && !shift \? NoteModes\.IndentCode : static \(_, _\) => null;\s*\}", keys);
        Assert.True(Index(keys, "shiftItems = _mode == NoteMode.Code") < Index(keys, "FocusManager.TryMoveFocus(shift ? FocusNavigationDirection.Previous"));
    }

    // ---------------------------------------------------------------- the card

    [Fact]
    public void The_card_reads_its_mode_titles_raw_and_names_a_mode_that_is_not_markdown()
    {
        Assert.Contains("_mode = note.Mode;", Card, StringComparison.Ordinal);
        Assert.Contains("public string DisplayTitle => NoteTitle.From(Content, Mode) is { Length: > 0 } title ? title : UntitledLabel;", Card, StringComparison.Ordinal);
        Assert.Contains("Mode == NoteMode.Markdown ? string.Empty : NameOf(Mode)", Card, StringComparison.Ordinal);
        foreach (string changed in new[] { "Mode", "DisplayTitle", "ReadText", "HelpText", "FormatVisibility" })
        {
            Assert.Contains($"Changed(nameof({changed}));", Card, StringComparison.Ordinal);
        }

        // The read view: the text as typed outside Markdown, Code in the code family.
        XElement view = Named(MainXaml, "CardReadView");
        Assert.Equal("{x:Bind Mode, Mode=OneWay}", (string?)view.Attribute("Mode"));
        Assert.Contains("if ((source.Length > RenderedLimit && _paper is null) || Mode != NoteMode.Markdown)", ReadView, StringComparison.Ordinal);
        Assert.Matches(@"if \(Mode == NoteMode\.Code\)\s*\{\s*text\.FontFamily = \(FontFamily\)Application\.Current\.Resources\[""FontFamilyCode""\];", ReadView);

        // The editor is loaded in the card's mode.
        Assert.Contains("host.Load(NoteId.From(card.Id), _stack.Draft, card.Mode);", Body(Main, "AttachEditor"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_mode_is_chosen_from_the_cards_menu_through_one_path_checked_and_said_aloud()
    {
        string menu = Body(Main, "OnNoteListContextRequested");
        Assert.Contains("var mode = new MenuFlyoutSubItem { Text = \"Mode\" };", menu, StringComparison.Ordinal);
        Assert.True(Index(menu, "menu.Items.Add(colour);") < Index(menu, "menu.Items.Add(mode);"));

        string items = Body(Main, "ModeItems");
        Assert.Contains("foreach (NoteMode mode in Enum.GetValues<NoteMode>())", items, StringComparison.Ordinal);
        Assert.Contains("GroupName = \"NoteMode\",", items, StringComparison.Ordinal);
        Assert.Contains("IsChecked = card.Mode == mode,", items, StringComparison.Ordinal);
        Assert.Contains("item.Click += (_, _) => ModeCard(card.Id, mode);", items, StringComparison.Ordinal);
        Assert.Contains("NoteMode.PlainText => \"Plain text\",", Card, StringComparison.Ordinal);

        string change = Body(Main, "ModeCard");
        Assert.Equal(1, Regex.Count(Main, @"_stack\.SetMode\("));
        Assert.Contains("card.Mode = mode;", change, StringComparison.Ordinal);
        Assert.Contains("Accessible.SetRowState(container, card.HelpText);", change, StringComparison.Ordinal);
        Assert.Matches(@"if \(_editor is \{ Note: \{ \} shown \} editor && shown\.Value == id\)\s*\{\s*editor\.SetMode\(mode\);", change);
        Assert.Contains("Announce($\"Mode: {NoteCard.NameOf(mode)}\");", change, StringComparison.Ordinal);

        Assert.Contains(".Register<SetNoteMode, CommandResult>(new SetNoteModeHandler(notes, clock).Handle)", App, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- new notes (D28)

    [Fact]
    public void New_notes_are_is_a_settings_row_applied_live_by_every_creation_path()
    {
        XElement box = Named(PageXaml, "NewNoteModeSetting");
        Assert.Equal("ComboBox", box.Name.LocalName);
        Assert.Equal("New notes are", (string?)box.Attribute("AutomationProperties.Name"));
        Assert.NotNull(box.Attribute("AutomationProperties.HelpText"));
        Assert.Equal(["Markdown", "Code", "PlainText"], box.Elements().Select(i => (string?)i.Attribute("Tag")));
        Assert.Equal(["Markdown", "Code", "Plain text"], box.Elements().Select(i => (string?)i.Attribute("Content")));

        Assert.Contains("Select(NewNoteModeSetting, preferences.NewNoteMode.ToString());", Body(Page, "ShowStored"), StringComparison.Ordinal);
        Assert.Contains("Write(p => NewNoteModeSetting.SelectedItem is not null && p.SetNewNoteMode(Selected<NoteMode>(NewNoteModeSetting)));", Page, StringComparison.Ordinal);

        // From the first frame, and again when it changes; Ctrl+N, the new-note shortcuts and the clipboard's all create
        // through CreateNoteInOpenFolder.
        Assert.Contains("NewNoteMode = preferences.NewNoteMode,", App, StringComparison.Ordinal);
        Assert.Contains("_stack.Create(NewNotePlacement, selected, content, NewNoteMode, NewNoteColor)", Body(Main, "CreateNoteInOpenFolder"), StringComparison.Ordinal);
        Assert.Equal(1, Regex.Count(Main, @"_stack\.Create\("));
    }

    [Fact]
    public void The_editor_harness_proves_code_mode_at_run_time_and_across_a_restart()
    {
        string harness = File.ReadAllText(SourcePath("tools", "validation", "Invoke-Editor.ps1"));
        Assert.Contains("Add-Result $run '25 Code from the menu", harness, StringComparison.Ordinal);
        Assert.Contains("Card-Mode 'A plain note with no markup at all.' 'Code'", harness, StringComparison.Ordinal);
        Assert.Matches(@"Stop-Noto \$script:noto[\s\S]*\(Stored-Mode \$CodeModeNoteId\) -eq 'Code'", harness);
    }

    // ---------------------------------------------------------------- helpers

    private static XElement Named(XDocument document, string name) =>
        document.Descendants().Single(e => (string?)e.Attribute(X + "Name") == name);

    private static int Index(string text, string value)
    {
        int index = text.IndexOf(value, StringComparison.Ordinal);
        Assert.True(index >= 0, $"Not found: {value}");
        return index;
    }

    private static string Code(string path) => Regex.Replace(File.ReadAllText(path), @"^\s*//.*$", string.Empty, RegexOptions.Multiline);

    private static string Body(string source, string method)
    {
        var declaration = Regex.Match(source, $@"\n    (private|internal|public)( static)?( async)? [\w?<>,()\s]+ {method}\(");
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
