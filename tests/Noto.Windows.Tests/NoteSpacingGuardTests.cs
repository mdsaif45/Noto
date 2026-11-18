using System.Xml.Linq;
using Noto.Core.Workspace;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// #234 in the source: the note text's line height and paragraph spacing come from one place (NoteSpacing, over Core's
/// NoteText), which the editor and every card's read view both read, and apply live; the overlays placed by line
/// rectangles (pictures, file shortcuts, checkboxes) keep the paragraph spacing below them; Settings offers exactly
/// Core's presets. What it draws is Invoke-Visual.ps1 case 18's to measure.
/// </summary>
public sealed class NoteSpacingGuardTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private static readonly string Windows = Path.GetDirectoryName(SourcePath("src", "Noto.Windows", "App.xaml.cs"))!;
    private static readonly string Spacing = Read("NoteSpacing.cs");
    private static readonly string Reader = Read("MarkdownReadView.cs");
    private static readonly string Host = Read("NoteEditorHost.xaml.cs");
    private static readonly string Styling = Read("NoteEditorHost.Styling.cs");
    private static readonly string Images = Read("NoteEditorHost.Images.cs");
    private static readonly string Shortcuts = Read("NoteEditorHost.Shortcuts.cs");

    [Fact]
    public void The_values_are_cores_read_in_one_place()
    {
        Assert.Contains("internal static double LineMultiple => NoteText.LineHeight(LineHeight);", Spacing, StringComparison.Ordinal);
        Assert.Contains("internal static double SpaceAfter(double size) => NoteText.SpaceAfter(Paragraphs, size);", Spacing, StringComparison.Ordinal);
        Assert.Contains("internal static bool SpacesHeadings => NoteText.SpacesHeadings(Paragraphs);", Spacing, StringComparison.Ordinal);

        // No view has numbers of its own: a card looks the same reading and editing.
        foreach (string code in new[] { Reader, Host, Styling })
        {
            Assert.DoesNotContain("1.15", code, StringComparison.Ordinal);
            Assert.DoesNotContain("NoteText.LineHeight(", code, StringComparison.Ordinal);
            Assert.DoesNotContain("NoteText.SpaceAfter(", code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_read_view_spaces_each_paragraph_from_the_shared_values_and_renders_again_when_they_change()
    {
        Assert.Contains("double after = NoteSpacing.SpaceAfter(FontSize);", Reader, StringComparison.Ordinal);
        Assert.Contains("paragraph.LineHeight = NoteSpacing.LineMultiple * size;", Reader, StringComparison.Ordinal);

        // A heading's lines are a multiple of its own size; a picture's paragraph only ever grows, so it is never cut.
        Assert.Contains("Space(paragraph, block.Kind == MarkdownBlockKind.Heading ? HeadingSize(block.Level) : FontSize, pictures);", Reader, StringComparison.Ordinal);
        Assert.Contains("pictures ? LineStackingStrategy.MaxHeight : LineStackingStrategy.BlockLineHeight", Reader, StringComparison.Ordinal);

        // Code and plain text too: a line a paragraph, as in the editor, when there is spacing.
        Assert.Contains("Space(plain, FontSize);", Reader, StringComparison.Ordinal);

        Assert.Contains("NoteSpacing.Changed += OnSpacingChanged;", Reader, StringComparison.Ordinal);
        Assert.Contains("Unloaded += (_, _) => NoteSpacing.Changed -= OnSpacingChanged;", Reader, StringComparison.Ordinal);
    }

    [Fact]
    public void The_editor_styles_from_the_shared_values_and_restyles_when_they_change()
    {
        Assert.Contains("LineHeight: NoteSpacing.LineMultiple,", Host, StringComparison.Ordinal);
        Assert.Contains("SpaceAfter: NoteSpacing.SpaceAfter(FontSize),", Host, StringComparison.Ordinal);
        Assert.Contains("SpacesHeadings: NoteSpacing.SpacesHeadings);", Host, StringComparison.Ordinal);
        Assert.Contains("NoteSpacing.Changed += OnSpacingChanged;", Host, StringComparison.Ordinal);
        Assert.Contains("Unloaded += (_, _) => NoteSpacing.Changed -= OnSpacingChanged;", Host, StringComparison.Ordinal);
        Assert.Contains("private void OnSpacingChanged(object? sender, EventArgs e) => RestyleAll();", Host, StringComparison.Ordinal);

        // Loading (RTF) and restyling (TOM) give a line the same height and the block the same space after.
        Assert.Contains("rtf.Append(@\"\\sl-\").Append(Twips(palette.LineHeight * (heading ? palette.HeadingSize(level) : palette.BodySize))).Append(@\"\\slmult0\");", Styling, StringComparison.Ordinal);
        Assert.Contains("rtf.Append(@\"\\sa\").Append(Twips(palette.SpaceAfter));", Styling, StringComparison.Ordinal);
        Assert.Contains("paragraph.SetLineSpacing(LineSpacingRule.Exactly, Points(palette.LineHeight * (heading ? palette.HeadingSize(block.Level) : palette.BodySize)));", Styling, StringComparison.Ordinal);
        Assert.Contains("paragraph.SetLineSpacing(LineSpacingRule.Single, 1);", Styling, StringComparison.Ordinal);
        Assert.Contains("paragraph.SpaceAfter = lastLine == block.Start ? Points(palette.SpaceAfter) : 0;", Styling, StringComparison.Ordinal);
        Assert.Equal(2, Styling.Split("palette.SpacesHeadings").Length - 1);
    }

    [Fact]
    public void Overlays_placed_by_line_rectangles_keep_the_paragraph_spacing_below_them()
    {
        // A picture's and a Standard shortcut's details' line is given their room and the spacing; each sits above it.
        Assert.Contains("float space = (float)((height + (2 * ImageGap) + after) * 0.75);", Images, StringComparison.Ordinal);
        Assert.Contains("rect.Bottom - after - height - ImageGap + top", Images, StringComparison.Ordinal);
        Assert.Contains("float below = (float)(((large ? DetailsHeight : 0) + after) * 0.75);", Shortcuts, StringComparison.Ordinal);
        Assert.Contains("last.Bottom - after - DetailsHeight + top", Shortcuts, StringComparison.Ordinal);

        // A checkbox and a Compact shortcut's icon are centred on the line's text, not the space below it.
        Assert.Contains("rect.Height = TextHeight(rect, task.End, after);", Host, StringComparison.Ordinal);
        Assert.Contains("TextHeight(name, shortcut.Line.End, after)", Shortcuts, StringComparison.Ordinal);
        Assert.Equal(3, (Images + Shortcuts + Host).Split("NoteSpacing.SpaceAfter(FontSize);").Length - 1);
    }

    [Fact]
    public void Settings_offers_exactly_cores_presets_after_the_text_size()
    {
        XDocument page = XDocument.Load(Path.Join(Windows, "SettingsPage.xaml"));
        var names = page.Descendants(Xaml + "ComboBox").Select(c => (string?)c.Attribute(X + "Name")).ToList();
        string code = Read("SettingsPage.xaml.cs");

        List<string?> Tags(string name) => page.Descendants(Xaml + "ComboBox").Single(c => (string?)c.Attribute(X + "Name") == name)
            .Elements(Xaml + "ComboBoxItem").Select(i => (string?)i.Attribute("Tag")).ToList();

        Assert.Equal(Enum.GetNames<TextSpacing>(), Tags("LineHeightSetting"));
        Assert.Equal(Enum.GetNames<TextSpacing>(), Tags("ParagraphSpacingSetting"));
        Assert.Equal(names.IndexOf("NoteSizeSetting") + 1, names.IndexOf("LineHeightSetting"));
        Assert.Equal(names.IndexOf("LineHeightSetting") + 1, names.IndexOf("ParagraphSpacingSetting"));

        Assert.Contains("Select(LineHeightSetting, preferences.LineHeight.ToString());", code, StringComparison.Ordinal);
        Assert.Contains("Select(ParagraphSpacingSetting, preferences.ParagraphSpacing.ToString());", code, StringComparison.Ordinal);
        Assert.Contains("Write(p => LineHeightSetting.SelectedItem is not null && p.SetLineHeight(Selected<TextSpacing>(LineHeightSetting)));", code, StringComparison.Ordinal);
        Assert.Contains("Write(p => ParagraphSpacingSetting.SelectedItem is not null && p.SetParagraphSpacing(Selected<TextSpacing>(ParagraphSpacingSetting)));", code, StringComparison.Ordinal);

        // Applied from the first frame, as the font and size are.
        Assert.Contains("NoteSpacing.Apply(preferences.LineHeight, preferences.ParagraphSpacing);", Read("App.xaml.cs"), StringComparison.Ordinal);
    }

    private static string Read(string file) => File.ReadAllText(Path.Join(Windows, file));

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
