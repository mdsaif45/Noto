using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// ADR-017's guards on the editor host: the RichEditBox shows the markdown source and nothing else, Noto owns undo and
/// the chord map, and the host decides no formatting rule. What it does at run time is Invoke-Editor.ps1's to prove.
/// </summary>
public sealed class EditorHostGuardTests
{
    private static readonly string Windows = Path.GetDirectoryName(SourcePath("src", "Noto.Windows", "App.xaml.cs"))!;
    private static readonly string Host = Code(Path.Join(Windows, "NoteEditorHost.xaml.cs"));
    private static readonly string Styling = Code(Path.Join(Windows, "NoteEditorHost.Styling.cs"));
    private static readonly XDocument HostXaml = XDocument.Load(Path.Join(Windows, "NoteEditorHost.xaml"));

    [Fact]
    public void RichEdits_own_undo_and_formatting_chords_are_off_and_its_undo_is_never_called()
    {
        Assert.Contains("NoteEditor.Document.UndoLimit = 0;", Host, StringComparison.Ordinal);
        XElement box = HostXaml.Root!.Descendants().Single(e => e.Name.LocalName == "EditorBox");
        Assert.Equal("All", (string?)box.Attribute("DisabledFormattingAccelerators"));

        string all = string.Join('\n', Directory.EnumerateFiles(Windows, "*.cs").Select(Code));
        Assert.DoesNotMatch(@"\.Document\.(Undo|Redo)\(", all);
        Assert.DoesNotMatch(@"UndoLimit = [1-9]", all);
    }

    [Fact]
    public void Nothing_puts_text_into_the_document_that_is_not_the_source()
    {
        // ADR-017 §1: no link fields, list numbering or embedded objects; text is read back whole, hidden markup included.
        // An image is its source text; the picture is drawn over the editor (ADR-019 §4), never put into the document.
        string editor = string.Join('\n', Directory.EnumerateFiles(Windows, "NoteEditorHost*.cs").Select(Code));
        Assert.DoesNotMatch(@"\.Link\s*=(?!>)|ListType|(?<!MarkdownCommands)\.InsertImage|InsertObject|TextGetOptions\.NoHidden", editor);
        Assert.Contains("GetText(TextGetOptions.UseLf, out string now)", Host, StringComparison.Ordinal);
        Assert.Contains("SetText(TextSetOptions.FormatRtf, MarkdownStyling.Rtf(_document, Palette()))", Host, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_formatting_command_is_a_Core_transform_and_the_host_decides_no_rule()
    {
        // The host maps commands to MarkdownCommands and applies the edit; it never inspects markup characters itself.
        foreach (string transform in new[] { "ToggleStrong", "ToggleEmphasis", "ToggleUnderline", "ToggleStrikethrough", "ToggleCode", "InsertLink", "CycleHeading", "ToggleBullets", "ToggleNumbers", "ToggleTasks", "ToggleTaskDone", "WrapInCodeBlock" })
        {
            Assert.Contains($"MarkdownCommands.{transform}", Host, StringComparison.Ordinal);
        }

        Assert.Contains("MarkdownCommands.ContinueList(_text, _selection)", Host, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"""\*\*""|""~~""|""==""|""- \[ \] ""|'#'", Host);
    }

    [Fact]
    public void A_command_enter_or_paste_first_reads_back_the_last_keystroke()
    {
        // Measured: TextChanged arrives after the key, so Enter right after typing saw the caret one character early.
        Assert.Matches(@"internal bool Run\(EditorCommand command\)\s*\{\s*Flush\(\);", Host);
        Assert.Matches(@"if \(e\.Key == VirtualKey\.Enter && !ctrl && !alt\)\s*\{\s*Flush\(\);", Host);
        Assert.Matches(@"Flush\(\);\s*TextSelection at = _selection;", Body(Host, "PasteAsync"));
    }

    [Fact]
    public void Every_save_and_the_close_first_read_back_the_last_keystroke()
    {
        // Measured: a key and Escape in one input batch left the card without the key. Leave, hide, autosave, session
        // end and close all save through TrySaveCard; the close decides whether anything is unsaved before it.
        string main = Code(Path.Join(Windows, "MainWindow.xaml.cs"));
        Assert.Matches(@"^\s*\{\s*_editor\?\.ReadBack\(\);\s*if \(!_stack\.IsDirty\)", Body(main, "TrySaveCard"));
        Assert.Matches(@"^\s*\{\s*_editor\?\.ReadBack\(\);\s*if \(!_stack\.IsDirty \|\| _stack\.Editing", Body(main, "OnAppWindowClosing"));
        Assert.Contains("internal void ReadBack() => Flush();", Host, StringComparison.Ordinal);
    }

    [Fact]
    public void Tab_nests_a_list_item_and_otherwise_moves_focus()
    {
        // The P1 accessibility contract: Tab never types a tab character; outside a list it moves focus.
        Assert.Matches(@"if \(e\.Key == VirtualKey\.Tab && !ctrl && !alt\)\s*\{\s*e\.Handled = true;\s*Flush\(\);\s*Func<string, TextSelection, TextEdit\?> shiftItems = shift \? MarkdownCommands\.OutdentListItems : MarkdownCommands\.IndentListItems;", Host);
        Assert.Matches(@"FocusManager\.TryMoveFocus\(shift \? FocusNavigationDirection\.Previous : FocusNavigationDirection\.Next", Host);
    }

    [Fact]
    public void Alt_Up_and_Down_move_lines_and_Ctrl_Alt_still_leaves_for_another_card()
    {
        // The leave chords are checked first: with Ctrl held, Alt+Up/Down never move a line.
        Assert.True(Host.IndexOf("VirtualKey.Down when ctrl && alt => EditorLeave.Next", StringComparison.Ordinal) < Host.IndexOf("if (alt && !ctrl && e.Key is VirtualKey.Up or VirtualKey.Down)", StringComparison.Ordinal));
        Assert.Contains("e.Key == VirtualKey.Up ? MarkdownCommands.MoveLinesUp : MarkdownCommands.MoveLinesDown", Host, StringComparison.Ordinal);
    }

    [Fact]
    public void Paste_converts_HTML_through_Core_only_when_formatted_and_survives_a_busy_clipboard()
    {
        string paste = Body(Host, "PasteAsync");
        Assert.Contains("if (formatted && clipboard.Contains(StandardDataFormats.Html))", paste, StringComparison.Ordinal);
        Assert.Contains("HtmlToMarkdown.Convert(", paste, StringComparison.Ordinal);
        Assert.Matches(@"catch \(COMException\)\s*\{\s*return;", paste);
        Assert.Contains("PasteAsync(formatted: true);", Body(Host, "OnPaste"), StringComparison.Ordinal);
        Assert.Matches(@"if \(ctrl && shift && !alt && e\.Key == VirtualKey\.V\)\s*\{\s*e\.Handled = true;\s*PasteAsync\(formatted: false\);", Host);
    }

    [Fact]
    public void A_click_toggles_a_task_only_on_its_drawn_checkbox()
    {
        // RichEdit's client coordinates start inside the border and padding; both the hit test and the overlay say so.
        string click = Body(Host, "OnPointerReleased");
        Assert.Contains("if (!_selection.IsCaret)", click, StringComparison.Ordinal);
        Assert.Contains("point.X - NoteEditor.BorderThickness.Left - NoteEditor.Padding.Left", click, StringComparison.Ordinal);
        Assert.Contains("at >= from && at <= box.End && MarkdownCommands.ToggleTaskDone(", click, StringComparison.Ordinal);
        Assert.Contains("NoteEditor.BorderThickness.Left + NoteEditor.Padding.Left", Body(Host, "PlaceTaskMarks"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_checkbox_overlay_takes_no_input_and_is_not_read_twice()
    {
        // Narrator reads a task from the source; the drawn boxes are decoration only.
        XElement marks = HostXaml.Root!.Descendants().Single(e => (string?)e.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "TaskMarks");
        Assert.Equal("False", (string?)marks.Attribute("IsHitTestVisible"));
        Assert.Equal("Raw", (string?)marks.Attribute("AutomationProperties.AccessibilityView"));
    }

    [Fact]
    public void Caret_keys_stay_in_the_editor_and_never_reach_the_stack()
    {
        // Measured: an unhandled Ctrl+End reached the ListView, which moved its selection and edited the last card.
        Match keys = Regex.Match(Host, @"NoteEditor\.KeyDown \+= \(_, e\) =>\s*\{\s*if \(e\.Key is (?<keys>[^)]+)\)\s*\{\s*e\.Handled = true;");
        Assert.True(keys.Success, "The editor's caret keys are not marked handled.");
        foreach (string key in new[] { "Left", "Right", "Up", "Down", "Home", "End", "PageUp", "PageDown" })
        {
            Assert.Contains($"VirtualKey.{key}", keys.Groups["keys"].Value, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AltGr_never_formats_and_the_editor_owns_its_chords()
    {
        Assert.Contains("if (ctrl && !alt && Chord(e.Key, shift) is { } command)", Host, StringComparison.Ordinal);

        // The implementation plan's map (§10), plus Ctrl+E and Ctrl+K for code and link.
        foreach (string chord in new[]
        {
            "(VirtualKey.Z, false) => EditorCommand.Undo", "(VirtualKey.B, false) => EditorCommand.Bold", "(VirtualKey.I, false) => EditorCommand.Italic",
            "(VirtualKey.U, false) => EditorCommand.Underline", "(VirtualKey.E, true) => EditorCommand.Strikethrough", "(VirtualKey.H, true) => EditorCommand.Heading",
            "(VirtualKey.L, false) => EditorCommand.Bullets", "(VirtualKey.L, true) => EditorCommand.Numbers", "(VirtualKey.T, false) => EditorCommand.Tasks",
            "((VirtualKey)0xBE, false) => EditorCommand.TaskDone", "(VirtualKey.R, true) => EditorCommand.ShowMarkup",
        })
        {
            Assert.Contains(chord, Host, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Composition_is_recorded_once_when_it_ends_and_nothing_is_restyled_during_it()
    {
        string changed = Body(Host, "OnUserChanged");
        Assert.Matches(@"if \(_composingFrom is not null\)\s*\{\s*_text = now;\s*_document = MarkdownDocument\.Parse\(now\);\s*ContentChanged\?\.Invoke\(this, EventArgs\.Empty\);\s*return;", changed);
        Assert.Contains("_history.Record(from, edit, EditKind.Other, _composingSelection);", Body(Host, "OnCompositionEnded"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_menu_runs_the_same_commands_as_the_keys()
    {
        string main = Code(Path.Join(Windows, "MainWindow.xaml.cs"));
        Assert.Matches(@"Enum\.TryParse\(name, out EditorCommand command\)\)\s*\{\s*_ = editor\.Run\(command\);\s*editor\.TakeFocus\(\);", Body(main, "OnFormatClick"));

        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var items = XDocument.Load(Path.Join(Windows, "MainWindow.xaml")).Descendants(xaml + "MenuFlyoutItem")
            .Where(i => (string?)i.Attribute("Click") == "OnFormatClick")
            .Select(i => (string?)i.Attribute("Tag"))
            .ToList();
        Assert.Equal(["Bold", "Italic", "Underline", "Strikethrough", "Code", "Link", "Heading", "Bullets", "Numbers", "Tasks", "TaskDone", "CodeBlock", "ShowMarkup"], items);
    }

    [Fact]
    public void A_large_change_sets_the_whole_note_styled_in_one_call_never_in_place()
    {
        // Measured on a ~100 KB note: a paste over all of it took 6.5 s in place and restyling it span by span 7.3 s,
        // the UI frozen; setting it whole, styled, as opening does, about 200 ms (the performance and stability slice).
        Assert.Contains("private const int WholeNote = 2_048;", Host, StringComparison.Ordinal);

        string apply = Body(Host, "Apply");
        Assert.Contains("bool whole = edit.Length >= WholeNote || edit.Replacement.Length >= WholeNote;", apply, StringComparison.Ordinal);
        Assert.Matches(@"if \(!whole\)\s*\{\s*Set\(\(\) =>\s*\{\s*NoteEditor\.Document\.GetRange\(edit\.Start, edit\.Start \+ edit\.Length\)\.SetText\(", apply);
        Assert.Contains("Commit(edit.ApplyTo(before), whole);", apply, StringComparison.Ordinal);

        Assert.Matches(@"if \(whole\)\s*\{\s*SetWhole\(_selection\);\s*\}\s*else\s*\{\s*Restyle\(region\);\s*\}", Body(Host, "Commit"));
        Assert.Matches(@"if \(region\.Length >= WholeNote\)\s*\{\s*SetWhole\(_selection\);\s*return;\s*\}", Body(Host, "Restyle"));
        Assert.Matches(@"NoteEditor\.Document\.SetText\(TextSetOptions\.FormatRtf, MarkdownStyling\.Rtf\(_document, Palette\(\)\)\);\s*NoteEditor\.Document\.Selection\.SetRange\(keep\.Start, keep\.End\);", Body(Host, "SetWhole"));

        // A theme switch or Show markup keeps the caret where it is, read from the control first.
        Assert.Matches(@"_selection = CurrentSelection\(\);\s*Restyle\(\(0, _text\.Length\)\);", Body(Host, "RestyleAll"));
    }

    [Fact]
    public void UI_Automations_SetValue_makes_only_the_change_as_typing_does()
    {
        // A screen reader, dictation or a test sets the whole value; the editor applies only what differs.
        Assert.Matches(@"NoteEditor\.ValueSink = value =>\s*\{\s*string now = Normalize\(value\);\s*if \(TextDiff\.Between\(_text, now, TextSelection\.Caret\(now\.Length\)\) is \{ \} edit\)\s*\{\s*Apply\(edit, record: true\);", Host);
        Assert.DoesNotContain("new TextEdit(0, _text.Length", Host, StringComparison.Ordinal);
    }

    private static string Code(string path) => Regex.Replace(File.ReadAllText(path), @"^\s*//.*$", string.Empty, RegexOptions.Multiline);

    private static string Body(string source, string method)
    {
        var declaration = Regex.Match(source, $@"(private|internal|public)(?: static)?(?: async)? [\w<>?,\[\] ]+? {method}\(");
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
