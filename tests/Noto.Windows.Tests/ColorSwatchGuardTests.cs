using System.Text.RegularExpressions;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// F14 (#155) in the source: a card shows a swatch after each <c>#rrggbb</c> code, the code's text unchanged; the
/// swatch is hidden from assistive technology, which reads the code; a link's text gets none. What it draws and what
/// UI Automation reads is Invoke-Visual.ps1 case 16's. "Show colour chips" (#245) turns them off, live: case 18.
/// </summary>
public sealed class ColorSwatchGuardTests
{
    private static readonly string Reader = File.ReadAllText(SourcePath("src", "Noto.Windows", "MarkdownReadView.cs"));

    [Fact]
    public void Each_code_is_followed_by_a_swatch_and_the_text_is_kept_whole()
    {
        // Every piece is the source's own text, formatted as the whole run is; a swatch follows each code.
        Assert.Matches(@"foreach \(ColorCode colour in ColorCode\.Find\(text\)\)\s*\{\s*int after = colour\.Index \+ colour\.Length;\s*var piece = new Run \{ Text = text\[at\.\.after\] \};\s*Format\(piece\);\s*paragraph\.Inlines\.Add\(piece\);\s*paragraph\.Inlines\.Add\(Swatch\(colour, markup\)\);", Reader);
        Assert.Matches(@"var rest = new Run \{ Text = text\[at\.\.\] \};\s*Format\(rest\);", Reader);

        // A link's text returns before swatches: a link is one Hyperlink, never split.
        Assert.True(Reader.IndexOf("paragraph.Inlines.Add(hyperlink);", StringComparison.Ordinal) < Reader.IndexOf("ColorCode.Find(text)", StringComparison.Ordinal));
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

    [Fact]
    public void Show_colour_chips_reaches_every_card_live_and_the_editor_has_none_to_hide()
    {
        // #245 (F14, J18): off, a code is its text alone; the switch renders every card in the tree again, recycled ones
        // included, and a card that comes back after a change renders again.
        Assert.Matches(@"if \(!_chips\)\s*\{\s*paragraph\.Inlines\.Add\(run\);\s*return;\s*\}\s*// A #rrggbb code shows a swatch", Reader);
        Assert.Contains("_chips = ShowsColorChips;", Reader, StringComparison.Ordinal);
        Assert.Matches(@"_showsColorChips = value;\s*ColorChipsChanged\?\.Invoke\(\);", Reader);
        Assert.Matches(@"Loaded \+= \(_, _\) =>\s*\{\s*ColorChipsChanged -= Render;\s*ColorChipsChanged \+= Render;\s*if \(_chips != ShowsColorChips\)\s*\{\s*Render\(\);", Reader);
        Assert.Contains("Unloaded += (_, _) => ColorChipsChanged -= Render;", Reader, StringComparison.Ordinal);

        // Set from the stored value before the first frame, and again whenever Settings changes it.
        string app = File.ReadAllText(SourcePath("src", "Noto.Windows", "App.xaml.cs"));
        Assert.Equal(2, Regex.Count(app, @"MarkdownReadView\.ShowsColorChips = preferences\.ColorChips;"));
        Assert.Matches(@"key == SettingKeys\.AppearanceColorChips\)\s*\{\s*MarkdownReadView\.ShowsColorChips = preferences\.ColorChips;", app);

        // The editor shows a code as typed (#155): no chip is drawn there, so there is nothing for the switch to hide.
        string editor = string.Join('\n', Directory.EnumerateFiles(Path.GetDirectoryName(SourcePath("src", "Noto.Windows", "App.xaml.cs"))!, "NoteEditorHost*.cs").Select(File.ReadAllText));
        Assert.DoesNotContain("ColorCode", editor, StringComparison.Ordinal);

        // A Settings switch, named for assistive technology, shown and written through the typed preferences.
        string page = File.ReadAllText(SourcePath("src", "Noto.Windows", "SettingsPage.xaml"));
        Assert.Matches(@"<ToggleSwitch\s+x:Name=""ColorChipsSetting""[^>]*Toggled=""OnColorChipsToggled""\s+AutomationProperties\.Name=""Show colour chips""", page);
        string code = File.ReadAllText(SourcePath("src", "Noto.Windows", "SettingsPage.xaml.cs"));
        Assert.Contains("ColorChipsSetting.IsOn = preferences.ColorChips;", code, StringComparison.Ordinal);
        Assert.Contains("Write(p => p.SetColorChips(ColorChipsSetting.IsOn));", code, StringComparison.Ordinal);

        // What it draws, live, and the new note colour with it: the visual harness's case 18.
        string harness = File.ReadAllText(SourcePath("tools", "validation", "Invoke-Visual.ps1"));
        Assert.Contains("Add-Result $run \"18 ${theme}: New note colour Blue makes Ctrl+N's note blue;", harness, StringComparison.Ordinal);
        Assert.Contains("Set-ColourStyle 'Blue' 'NewNoteColorSetting'", harness, StringComparison.Ordinal);
        Assert.Contains("(Find-ById 'ColorChipsSetting').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()", harness, StringComparison.Ordinal);
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
