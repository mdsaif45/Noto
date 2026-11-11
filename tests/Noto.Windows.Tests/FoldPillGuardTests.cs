using System.Xml.Linq;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// F6 (#149) in the source: a folded card says how much it hides and unfolds in one click; the card being edited folds
/// from its bar. What a real card does is Invoke-NoteStack.ps1's case 47 to show.
/// </summary>
public sealed class FoldPillGuardTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly string Window = File.ReadAllText(SourcePath("src", "Noto.Windows", "MainWindow.xaml.cs"));
    private static readonly XDocument Template = XDocument.Load(SourcePath("src", "Noto.Windows", "MainWindow.xaml"));
    private static readonly string Card = File.ReadAllText(SourcePath("src", "Noto.Windows", "NoteCard.cs"));

    [Fact]
    public void A_folded_card_shows_its_hidden_lines_on_a_pill_that_unfolds_it()
    {
        XElement pill = Named("UnfoldPill");

        Assert.Equal("{x:Bind UnfoldPillVisibility, Mode=OneWay}", (string?)pill.Attribute("Visibility"));
        Assert.Equal("{x:Bind HiddenLinesText, Mode=OneWay}", (string?)pill.Attribute("Content"));
        Assert.Equal("{x:Bind UnfoldName, Mode=OneWay}", (string?)pill.Attribute("AutomationProperties.Name"));
        Assert.Equal("OnUnfoldPillClick", (string?)pill.Attribute("Click"));
        Assert.Contains("FoldCard(card.Id, folded: false);", Handler("OnUnfoldPillClick"), StringComparison.Ordinal);

        // Counted by the domain's title rule, and only while folded: typing in an unfolded note never counts lines.
        Assert.Contains("public int HiddenLines => IsFolded ? NoteTitle.LinesBelow(Content) : 0;", Card, StringComparison.Ordinal);
        Assert.Contains("$\"folded, {Lines(HiddenLines)} hidden\"", Card, StringComparison.Ordinal);
    }

    [Fact]
    public void The_card_being_edited_folds_from_its_bar_through_the_keys_own_path()
    {
        XElement fold = Named("CardFoldButton");

        Assert.Equal("Fold", (string?)fold.Attribute("AutomationProperties.Name"));
        Assert.Equal("OnFoldCardClick", (string?)fold.Attribute("Click"));
        Assert.Contains("FoldCard(card.Id, folded: true);", Handler("OnFoldCardClick"), StringComparison.Ordinal);
    }

    private static XElement Named(string name) => Template.Descendants().Single(e => (string?)e.Attribute(X + "Name") == name);

    private static string Handler(string name)
    {
        int start = Window.IndexOf($"private void {name}(", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Not found: {name}");
        return Window[start..Window.IndexOf("\n    }\n", start, StringComparison.Ordinal)];
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
