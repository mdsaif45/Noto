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
        XElement box = HostXaml.Root!.Elements().Single();
        Assert.Equal("All", (string?)box.Attribute("DisabledFormattingAccelerators"));

        string all = string.Join('\n', Directory.EnumerateFiles(Windows, "*.cs").Select(Code));
        Assert.DoesNotMatch(@"\.Document\.(Undo|Redo)\(", all);
        Assert.DoesNotMatch(@"UndoLimit = [1-9]", all);
    }

    [Fact]
    public void Nothing_puts_text_into_the_document_that_is_not_the_source()
    {
        // ADR-017 §1: no link fields, list numbering or embedded objects; text is read back whole, hidden markup included.
        string editor = Host + Styling;
        Assert.DoesNotMatch(@"\.Link\s*=(?!>)|ListType|InsertImage|TextGetOptions\.NoHidden", editor);
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
        Assert.Matches(@"e\.Handled = true;\s*Flush\(\);\s*DataPackageView clipboard", Host);
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
