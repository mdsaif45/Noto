using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// The visual refresh's decisions, held by the build: notes are cards and lists are rows, every surface is headed
/// alike, the editor is the card's own surface, selection is shown by shape and stays inside the focus ring, and a
/// picture or a checkbox in a card follows the theme. The tokens, contrast and high contrast are
/// <c>DesignSystemGuardTests</c>'; what a running window shows is <c>Invoke-Visual.ps1</c>'s, measured.
/// </summary>
public sealed class VisualRefreshGuardTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly string Windows = Path.GetDirectoryName(SourcePath("src", "Noto.Windows", "App.xaml.cs"))!;
    private static readonly string Styles = Path.Join(Path.GetDirectoryName(Path.GetDirectoryName(SourcePath("src", "Noto.UI", "Themes", "Generic.xaml")))!, "Styles");

    [Fact]
    public void Notes_in_the_stack_are_cards_and_search_results_and_the_bin_stay_rows()
    {
        Assert.Equal("{StaticResource NoteCardStyle}", ListStyle("MainWindow.xaml", "NoteList"));
        Assert.Equal("{StaticResource FolderRowStyle}", ListStyle("MainWindow.xaml", "FolderList"));
        Assert.Equal("{StaticResource NoteRowStyle}", ListStyle("SearchPage.xaml", "ResultList"));
        Assert.Equal("{StaticResource NoteRowStyle}", ListStyle("RecycleBinPage.xaml", "RecycleBinList"));

        // A card: its own fill, a subtle edge, the container radius (the focus ring follows it), card padding and the
        // gap below it; selected, the accent edge and no pill, which a card's edge makes redundant.
        XElement template = Template("NoteRow.xaml", "NoteCardTemplate");
        XElement root = template.Descendants(Xaml + "Grid").First();
        Assert.Equal("{ThemeResource SurfaceCardBrush}", (string?)root.Attribute("Background"));
        Assert.Equal("{ThemeResource BorderSubtleBrush}", (string?)root.Attribute("BorderBrush"));
        Assert.Equal("{StaticResource RadiusMedium}", (string?)root.Attribute("CornerRadius"));
        Assert.All(
            template.Descendants(Xaml + "Border").Where(b => (string?)b.Attribute(X + "Name") == "FocusRing").DescendantsAndSelf(Xaml + "Border"),
            b => Assert.Equal("{StaticResource RadiusMedium}", (string?)b.Attribute("CornerRadius")));
        Assert.DoesNotContain(template.Descendants(), e => (string?)e.Attribute(X + "Name") == "SelectionIndicator");
        foreach (string state in new[] { "Selected", "PointerOverSelected", "PressedSelected" })
        {
            Assert.Contains(("Root.BorderBrush", "{ThemeResource SelectionIndicatorBrush}"), Setters(template, state));
        }

        // In high contrast a card at rest is the window's own colour, edged in window text; only hover and selection
        // take Highlight.
        string colours = File.ReadAllText(Path.Join(Path.GetDirectoryName(Styles)!, "Themes", "Colors.xaml"));
        Assert.Contains("<SolidColorBrush x:Key=\"SurfaceCardBrush\" Color=\"{ThemeResource SystemColorWindowColor}\" />", colours, StringComparison.Ordinal);

        // A group of settings is a card too: one surface for every raised container.
        Assert.Equal("{ThemeResource SurfaceCardBrush}", Setter(Style("Settings.xaml", "SettingsGroupStyle"), "Background"));

        XElement style = Style("NoteRow.xaml", "NoteCardStyle");
        Assert.Equal("{StaticResource CardPadding}", Setter(style, "Padding"));
        Assert.Equal("{StaticResource CardSpacing}", Setter(style, "Margin"));
        Assert.Equal("{StaticResource NoteCardTemplate}", Setter(style, "Template"));
    }

    [Fact]
    public void Selection_is_shown_by_shape_inside_the_focus_ring_and_never_by_weight()
    {
        XElement row = XDocument.Load(Path.Join(Styles, "ListRow.xaml")).Descendants(Xaml + "ControlTemplate").Single();
        XElement pill = row.Descendants(Xaml + "Border").Single(b => (string?)b.Attribute(X + "Name") == "SelectionIndicator");
        Assert.Equal("{StaticResource SelectionIndicatorMargin}", (string?)pill.Attribute("Margin"));

        // Weight changed the row's width as it moved (reflow); the pill and the fill are the cue in every template.
        Assert.DoesNotContain(
            Directory.EnumerateFiles(Styles, "*.xaml").SelectMany(f => XDocument.Load(f).Descendants(Xaml + "Setter")),
            s => ((string?)s.Attribute("Target"))?.EndsWith(".FontWeight", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Every_surface_is_headed_by_Back_and_one_title_style()
    {
        XDocument window = XDocument.Load(Path.Join(Windows, "MainWindow.xaml"));
        XElement back = window.Descendants(Xaml + "Button").Single(b => (string?)b.Attribute(X + "Name") == "BackButton");
        Assert.Equal("{StaticResource HeaderButtonStyle}", (string?)back.Attribute("Style"));
        Assert.Equal("Back to folders", (string?)back.Attribute("AutomationProperties.Name"));
        Assert.Equal("", (string?)back.Descendants(Xaml + "FontIcon").Single().Attribute("Glyph"));
        Assert.Contains("NoteFolderTitle.Text = item.Name;", File.ReadAllText(Path.Join(Windows, "MainWindow.xaml.cs")), StringComparison.Ordinal);

        foreach (string view in new[] { "MainWindow.xaml", "SettingsPage.xaml", "RecycleBinPage.xaml" })
        {
            var titles = XDocument.Load(Path.Join(Windows, view)).Descendants(Xaml + "TextBlock")
                .Where(t => (string?)t.Attribute("AutomationProperties.HeadingLevel") == "Level1").ToList();
            XElement title = Assert.Single(titles);
            Assert.Equal("{StaticResource SurfaceTitleStyle}", (string?)title.Attribute("Style"));
        }

        XElement surfaceTitle = Style("FolderPane.xaml", "SurfaceTitleStyle");
        Assert.Equal("{StaticResource FontSizeHeading}", Setter(surfaceTitle, "FontSize"));
        Assert.Equal("{StaticResource FontWeightEmphasis}", Setter(surfaceTitle, "FontWeight"));
        Assert.Equal("CharacterEllipsis", Setter(surfaceTitle, "TextTrimming"));
    }

    [Fact]
    public void Every_icon_leads_its_label_by_one_gap()
    {
        // A folder row and the Recycle Bin button are the same pair, an icon then its label; one token spaces both
        // (review: the button's gap was a step narrower than the rows').
        XDocument window = XDocument.Load(Path.Join(Windows, "MainWindow.xaml"));
        XElement folderRow = window.Descendants(Xaml + "ListView").Single(l => (string?)l.Attribute(X + "Name") == "FolderList").Descendants(Xaml + "Grid").First();
        XElement binButton = window.Descendants(Xaml + "Button").Single(b => (string?)b.Attribute(X + "Name") == "RecycleBinButton").Descendants(Xaml + "StackPanel").Single();

        Assert.Equal("{StaticResource IconLabelSpacing}", (string?)folderRow.Attribute("ColumnSpacing"));
        Assert.Equal("{StaticResource IconLabelSpacing}", (string?)binButton.Attribute("Spacing"));
    }

    [Fact]
    public void Decoration_is_not_read_aloud()
    {
        // The folder glyph, the pin and fold markers and the application's name repeat what the row's name, its help
        // text and the window title already say: a screen reader skips them.
        XDocument window = XDocument.Load(Path.Join(Windows, "MainWindow.xaml"));
        var decoration = window.Descendants(Xaml + "FontIcon").Where(g => !g.Ancestors(Xaml + "Button").Any() && !g.Ancestors(Xaml + "ToggleButton").Any())
            .Concat(window.Descendants(Xaml + "TextBlock").Where(t => (string?)t.Attribute("Style") == "{StaticResource AppTitleStyle}"))
            .ToList();

        Assert.True(decoration.Count >= 5, $"decoration found: {decoration.Count}");
        Assert.All(decoration, d => Assert.Equal("Raw", (string?)d.Attribute("AutomationProperties.AccessibilityView")));
    }

    [Fact]
    public void The_editor_is_the_cards_own_surface_with_no_box_or_padding_of_its_own()
    {
        XElement editor = Style("FolderPane.xaml", "NoteEditorStyle");
        Assert.Equal("{ThemeResource SurfaceCardBrush}", Setter(editor, "Background"));
        Assert.Equal("0", Setter(editor, "BorderThickness"));
        Assert.Equal("0", Setter(editor, "Padding"));

        // The focused and hovered editor draws the input surface, which is the card's fill in Light and Dark: no box
        // appears when editing starts.
        string colours = File.ReadAllText(Path.Join(Path.GetDirectoryName(Styles)!, "Themes", "Colors.xaml"));
        Assert.Equal(2, Regex.Count(colours, @"<StaticResource x:Key=""SurfaceCardColor"" ResourceKey=""SurfaceInputColor"" />"));
    }

    [Fact]
    public void A_cards_pictures_and_checkboxes_follow_the_theme()
    {
        string files = File.ReadAllText(Path.Join(Windows, "ImageFiles.cs"));
        Assert.Contains("CornerRadius = (CornerRadius)Application.Current.Resources[\"RadiusSmall\"],", files, StringComparison.Ordinal);
        Assert.Contains("Background = new ImageBrush { ImageSource = bitmap, Stretch = Stretch.Uniform },", files, StringComparison.Ordinal);
        Assert.Contains("return (ImageFiles.Picture(shown.Bitmap, drawnWidth, drawnHeight), drawnHeight);", File.ReadAllText(Path.Join(Windows, "NoteEditorHost.Images.cs")), StringComparison.Ordinal);

        string readView = File.ReadAllText(Path.Join(Windows, "MarkdownReadView.cs"));
        Assert.Contains("Border picture = ImageFiles.Picture(bitmap, width, height);", readView, StringComparison.Ordinal);
        Assert.Contains("var image = new Viewbox { Child = picture, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly,", readView, StringComparison.Ordinal);
        Assert.Matches(@"if \(over\.Any\(s => s\.Kind == MarkdownSpanKind\.TaskBox\)\)\s*\{\s*run\.FontFamily = \(FontFamily\)Application\.Current\.Resources\[""FontFamilySymbol""\];", readView);
    }

    private static string? ListStyle(string view, string list) =>
        (string?)XDocument.Load(Path.Join(Windows, view)).Descendants(Xaml + "ListView").Single(l => (string?)l.Attribute(X + "Name") == list).Attribute("ItemContainerStyle");

    private static XElement Template(string file, string key) =>
        XDocument.Load(Path.Join(Styles, file)).Descendants(Xaml + "ControlTemplate").Single(t => (string?)t.Attribute(X + "Key") == key);

    private static XElement Style(string file, string key) =>
        XDocument.Load(Path.Join(Styles, file)).Descendants(Xaml + "Style").Single(s => (string?)s.Attribute(X + "Key") == key);

    private static string? Setter(XElement style, string property) =>
        (string?)style.Elements(Xaml + "Setter").SingleOrDefault(s => (string?)s.Attribute("Property") == property)?.Attribute("Value");

    private static List<(string Target, string Value)> Setters(XElement template, string state) =>
        template.Descendants(Xaml + "VisualState").Single(s => (string?)s.Attribute(X + "Name") == state)
            .Descendants(Xaml + "Setter").Select(s => ((string?)s.Attribute("Target") ?? string.Empty, (string?)s.Attribute("Value") ?? string.Empty)).ToList();

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
