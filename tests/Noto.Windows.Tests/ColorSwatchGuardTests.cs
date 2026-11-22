using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// F14 (#155) in the source: a card shows a swatch after each <c>#rrggbb</c> code, the code's text unchanged; the
/// coloured square itself is never read (its button is, by name); a link's text gets none. What it draws and what
/// UI Automation reads is Invoke-Visual.ps1 case 16's. A swatch is a button that copies its colour in the chosen format
/// (#257): Invoke-Links.ps1 case 11.
/// </summary>
public sealed class ColorSwatchGuardTests
{
    private static readonly string Reader = File.ReadAllText(SourcePath("src", "Noto.Windows", "MarkdownReadView.cs"));

    [Fact]
    public void Each_code_is_followed_by_a_swatch_and_the_text_is_kept_whole()
    {
        // Every piece is the source's own text, formatted as the whole run is; a swatch follows each code.
        Assert.Matches(@"foreach \(ColorCode colour in ColorCode\.Find\(text\)\)\s*\{\s*int after = colour\.Index \+ colour\.Length;\s*var piece = new Run \{ Text = text\[at\.\.after\] \};\s*Format\(piece\);\s*paragraph\.Inlines\.Add\(piece\);\s*paragraph\.Inlines\.Add\(Swatch\(colour, text, markup\)\);", Reader);
        Assert.Matches(@"var rest = new Run \{ Text = text\[at\.\.\] \};\s*Format\(rest\);", Reader);

        // A link's text returns before swatches: a link is one Hyperlink, never split.
        Assert.True(Reader.IndexOf("paragraph.Inlines.Add(hyperlink);", StringComparison.Ordinal) < Reader.IndexOf("ColorCode.Find(text)", StringComparison.Ordinal));
    }

    [Fact]
    public void A_swatch_is_a_named_button_that_copies_its_colour_and_never_starts_editing()
    {
        string swatch = Regex.Match(Reader, @"private InlineUIContainer Swatch\([\s\S]*?\n    \}").Value;

        // A button: click, Enter and Space all raise Click, which copies the code in the chosen format (#257).
        Assert.Contains("var button = new Button", swatch, StringComparison.Ordinal);
        Assert.Contains("Style = (Style)resources[\"SubtleButtonStyle\"],", swatch, StringComparison.Ordinal);
        Assert.Contains("button.Click += (_, _) => CopyColor?.Invoke(colour.Format(ColorCopyFormat, text));", swatch, StringComparison.Ordinal);
        Assert.Contains("return new InlineUIContainer { Child = button };", swatch, StringComparison.Ordinal);
        Assert.DoesNotContain("IsTabStop", swatch, StringComparison.Ordinal);

        // Named with the code as written, so assistive technology says what it copies.
        Assert.Contains("string written = text.Substring(colour.Index, colour.Length);", swatch, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetName(button, $\"Copy colour {written}\");", swatch, StringComparison.Ordinal);

        // The tap is the swatch's: the stack's tap-to-edit never sees it.
        Assert.Contains("button.Tapped += (_, e) => e.Handled = true;", swatch, StringComparison.Ordinal);

        // The window copies through the clipboard and says what was copied on the stack's polite status line.
        string main = File.ReadAllText(SourcePath("src", "Noto.Windows", "MainWindow.xaml.cs"));
        Assert.Contains("MarkdownReadView.CopyColor = CopyColor;", main, StringComparison.Ordinal);
        string copy = Regex.Match(File.ReadAllText(SourcePath("src", "Noto.Windows", "MainWindow.Links.cs")), @"private void CopyColor\(string value\)[\s\S]*?\n    \}").Value;
        Assert.Contains("package.SetText(value);", copy, StringComparison.Ordinal);
        Assert.Contains("Clipboard.SetContent(package);", copy, StringComparison.Ordinal);
        Assert.Contains("catch (COMException)", copy, StringComparison.Ordinal);
        Assert.Contains("ShowNoteStatus($\"Copied {value}\");", copy, StringComparison.Ordinal);

        // The format is the setting, from the first frame and again whenever Settings changes it.
        string app = File.ReadAllText(SourcePath("src", "Noto.Windows", "App.xaml.cs"));
        Assert.Equal(2, Regex.Count(app, @"MarkdownReadView\.ColorCopyFormat = preferences\.ColorCopyFormat;"));

        // A Settings drop-down with the four formats, named for assistive technology.
        var page = XDocument.Load(SourcePath("src", "Noto.Windows", "SettingsPage.xaml"));
        XElement box = page.Descendants().Single(e => (string?)e.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "ColorCopyFormatSetting");
        Assert.Equal("Colour copy format", (string?)box.Attribute("AutomationProperties.Name"));
        Assert.Equal(["AsWritten", "Hex", "Rgb", "Hsl"], box.Elements().Select(i => (string?)i.Attribute("Tag")));
        Assert.Contains("p.SetColorCopyFormat(Selected<ColorCopyFormat>(ColorCopyFormatSetting))", File.ReadAllText(SourcePath("src", "Noto.Windows", "SettingsPage.xaml.cs")), StringComparison.Ordinal);

        // What it does in the running app: the links harness's case 11.
        string harness = File.ReadAllText(SourcePath("tools", "validation", "Invoke-Links.ps1"));
        Assert.Contains("Add-Result $run '11 a swatch is a button named for its colour;", harness, StringComparison.Ordinal);
    }

    [Fact]
    public void A_swatch_is_seen_and_never_read()
    {
        string swatch = Regex.Match(Reader, @"private InlineUIContainer Swatch\([\s\S]*?\n    \}").Value;

        Assert.Contains("AutomationProperties.SetAccessibilityView(swatch, AccessibilityView.Raw);", swatch, StringComparison.Ordinal);

        // Its size follows the note text; its edge is the markup colour, or the row's own in high contrast.
        Assert.Contains("double size = Math.Round(FontSize * 0.75);", swatch, StringComparison.Ordinal);
        Assert.Contains("BorderBrush = edge ?? Foreground,", swatch, StringComparison.Ordinal);
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
