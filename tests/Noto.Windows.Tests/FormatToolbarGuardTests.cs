using System.Xml.Linq;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// F8 (#151) in the source: the quick-formatting toolbar runs the editor's own commands (ADR-017 §3, no second
/// formatting path), never takes focus from the text, and a highlight is drawn from the theme in the editor and on the
/// card. What a real selection does is Invoke-Editor.ps1's case 21.
/// </summary>
public sealed class FormatToolbarGuardTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly string Windows = Path.GetDirectoryName(SourcePath("src", "Noto.Windows", "NoteEditorHost.xaml"))!;

    [Fact]
    public void Its_buttons_are_the_editors_commands_and_never_take_focus()
    {
        XElement bar = XDocument.Load(Path.Join(Windows, "NoteEditorHost.xaml")).Descendants(Xaml + "Popup").Single(p => (string?)p.Attribute(X + "Name") == "FormatBar");
        var buttons = bar.Descendants(Xaml + "Button").ToList();

        Assert.Equal(["Bold", "Italic", "Highlight", "Strikethrough", "Underline", "Link", "Code", "Bullets"], buttons.Select(b => (string?)b.Attribute("Tag")));
        Assert.All(buttons, b =>
        {
            Assert.Equal("False", (string?)b.Attribute("IsTabStop"));
            Assert.Equal("False", (string?)b.Attribute("AllowFocusOnInteraction"));
            Assert.Equal("OnToolbarClick", (string?)b.Attribute("Click"));
            Assert.NotNull(b.Attribute("AutomationProperties.Name"));
        });

        string toolbar = File.ReadAllText(Path.Join(Windows, "NoteEditorHost.Toolbar.cs"));
        Assert.Contains("Enum.TryParse(name, out EditorCommand command)", toolbar, StringComparison.Ordinal);
        Assert.Contains("_ = Run(command);", toolbar, StringComparison.Ordinal);
        Assert.Contains("if (!ShowsFormatToolbar || _selection.IsCaret || NoteEditor.FocusState == FocusState.Unfocused)", toolbar, StringComparison.Ordinal);
    }

    [Fact]
    public void A_highlight_is_the_cores_command_and_drawn_from_the_theme_in_the_editor_and_on_the_card()
    {
        string host = File.ReadAllText(Path.Join(Windows, "NoteEditorHost.xaml.cs"));
        Assert.Contains("EditorCommand.Highlight => MarkdownCommands.ToggleHighlight,", host, StringComparison.Ordinal);

        string styling = File.ReadAllText(Path.Join(Windows, "NoteEditorHost.Styling.cs"));
        Assert.Contains("MarkdownSpanKind.Highlight => Look.Mark,", styling, StringComparison.Ordinal);

        Assert.Contains("MarkBrush=\"{ThemeResource MarkBrush}\"", File.ReadAllText(Path.Join(Windows, "NoteEditorHost.xaml")), StringComparison.Ordinal);
        Assert.Contains("MarkBrush=\"{ThemeResource MarkBrush}\"", File.ReadAllText(Path.Join(Windows, "MainWindow.xaml")), StringComparison.Ordinal);
        Assert.Contains("new TextHighlighter { Background = MarkBrush, Foreground = MarkTextBrush }", File.ReadAllText(Path.Join(Windows, "MarkdownReadView.cs")), StringComparison.Ordinal);
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
