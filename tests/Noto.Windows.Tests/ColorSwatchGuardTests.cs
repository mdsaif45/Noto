using System.Text.RegularExpressions;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// F14 (#155) in the source: a card shows a swatch after each <c>#rrggbb</c> code, the code's text unchanged; the
/// swatch is hidden from assistive technology, which reads the code; a link's text gets none. What it draws and what
/// UI Automation reads is Invoke-Visual.ps1 case 16's.
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
