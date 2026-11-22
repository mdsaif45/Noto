using System.Text.RegularExpressions;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// #256 (parity B26) in the source: an empty note being edited shows its mode's placeholder, drawn by RichEditBox
/// outside the document and heard as help text, only while the note holds nothing; an empty card reads "Empty note",
/// quiet, and its row is named so. What a real note does is Invoke-NoteStack.ps1's case 53 to show.
/// </summary>
public sealed class EmptyNoteGuardTests
{
    private static readonly string Windows = Path.GetDirectoryName(SourcePath("src", "Noto.Windows", "App.xaml.cs"))!;
    private static readonly string Host = File.ReadAllText(Path.Join(Windows, "NoteEditorHost.xaml.cs"));
    private static readonly string Card = File.ReadAllText(Path.Join(Windows, "NoteCard.cs"));
    private static readonly string ReadView = File.ReadAllText(Path.Join(Windows, "MarkdownReadView.cs"));

    [Fact]
    public void The_placeholder_is_the_modes_shown_only_while_empty_and_heard_as_help_text()
    {
        string show = Body(Host, "ShowPlaceholder");
        Assert.Contains("string hint = _text.Length == 0 ? NoteModes.Placeholder(_mode) : string.Empty;", show, StringComparison.Ordinal);
        Assert.Matches(@"if \(NoteEditor\.PlaceholderText != hint\)\s*\{\s*NoteEditor\.PlaceholderText = hint;\s*AutomationProperties\.SetHelpText\(NoteEditor, hint\);", show);

        // Followed on every change, on Load and on a change of mode.
        Assert.Contains("ContentChanged += (_, _) => ShowPlaceholder();", Host, StringComparison.Ordinal);
        Assert.Matches(@"_text = Normalize\(markdown\);\s*_document = MarkdownDocument\.Parse\(_text, _mode\);\s*ShowPlaceholder\(\);", Body(Host, "Load"));
        Assert.Matches(@"RestyleAll\(\);\s*ShowPlaceholder\(\);", Body(Host, "SetMode"));
    }

    [Fact]
    public void The_placeholder_never_reaches_the_text_the_value_or_the_name()
    {
        // Only the placeholder and the help text are given it: never the document, the source or the Value pattern.
        string editor = string.Join('\n', Directory.EnumerateFiles(Windows, "NoteEditorHost*.cs").Select(File.ReadAllText));
        Assert.Equal(1, Regex.Count(editor, @"NoteModes\.Placeholder\("));
        Assert.Equal(1, Regex.Count(editor, @"PlaceholderText = "));
        Assert.Contains("NoteEditor.ValueSource = () => _text;", Host, StringComparison.Ordinal);

        // The editor keeps the platform's placeholder: no template of its own hides it, and its name stays "Note content".
        string styles = File.ReadAllText(SourcePath("src", "Noto.UI", "Styles", "FolderPane.xaml"));
        string style = styles[styles.IndexOf("<Style x:Key=\"NoteEditorStyle\"", StringComparison.Ordinal)..];
        Assert.DoesNotContain("\"Template\"", style[..style.IndexOf("</Style>", StringComparison.Ordinal)], StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Note content\"", File.ReadAllText(Path.Join(Windows, "NoteEditorHost.xaml")), StringComparison.Ordinal);
    }

    [Fact]
    public void The_placeholder_is_drawn_in_the_secondary_text_colour_in_every_theme()
    {
        string colors = File.ReadAllText(SourcePath("src", "Noto.UI", "Themes", "Colors.xaml"));
        foreach (string key in new[] { "TextControlPlaceholderForeground", "TextControlPlaceholderForegroundPointerOver", "TextControlPlaceholderForegroundFocused" })
        {
            // Light, Dark and HighContrast.
            Assert.Equal(3, Regex.Count(colors, $"<StaticResource x:Key=\"{key}\" ResourceKey=\"TextSecondaryBrush\" />"));
        }
    }

    [Fact]
    public void An_empty_card_reads_empty_note_quietly_and_its_row_is_named_so()
    {
        Assert.Contains("public const string EmptyLabel = \"Empty note\";", Card, StringComparison.Ordinal);
        Assert.Contains("string.IsNullOrWhiteSpace(Content) ? EmptyLabel : NoteTitle.From(Content, Mode) is { Length: > 0 } title ? title : UntitledLabel;", Card, StringComparison.Ordinal);
        Assert.Contains("public string ReadText => string.IsNullOrWhiteSpace(Content) ? string.Empty : IsFolded ? DisplayTitle : Content;", Card, StringComparison.Ordinal);

        // The row's name is the card's ToString, its title first (#201), so it says "Empty note" too.
        Assert.Contains("public override string ToString() => RowState.Name(DisplayTitle, HelpText);", Card, StringComparison.Ordinal);

        // The read view: the label in the secondary colour, the row's own colour in high contrast.
        string render = Body(ReadView, "Render");
        Assert.True(render.IndexOf("if (string.IsNullOrWhiteSpace(source))", StringComparison.Ordinal) < render.IndexOf("if (source.Length > RenderedLimit", StringComparison.Ordinal));
        Assert.Contains("var empty = new Run { Text = NoteCard.EmptyLabel };", render, StringComparison.Ordinal);
        Assert.Contains("Tint(empty, new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast ? null : MarkupBrush);", render, StringComparison.Ordinal);
    }

    private static string Body(string source, string method)
    {
        Match match = Regex.Match(source, $@"(private|internal|public) [^\n(]*\b{Regex.Escape(method)}\(");
        Assert.True(match.Success, $"Not found: {method}");
        int start = match.Index;
        return source[start..source.IndexOf("\n    }\n", start, StringComparison.Ordinal)];
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
