using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// F7 (#150) in the source: a note's colour is chosen from the card's menu, its bar and Ctrl+0 to Ctrl+6, through the
/// one session path; it is painted by the card's own template, so it follows the drawer's theme; it is named in the
/// card's help text and said aloud, so it is never only a colour. What it looks like is the visual harness's to show,
/// and its contrast is DesignSystemGuardTests'. Its style, Background or Bar (#191), is a Settings choice applied live.
/// </summary>
public sealed class NoteColourGuardTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private static readonly string Windows = Path.GetDirectoryName(SourcePath("src", "Noto.Windows", "App.xaml.cs"))!;
    private static readonly string Main = Code(Path.Join(Windows, "MainWindow.xaml.cs"));
    private static readonly string Palette = Code(Path.Join(Windows, "NotePalette.cs"));
    private static readonly string Card = Code(Path.Join(Windows, "NoteCard.cs"));
    private static readonly XDocument NoteRow = XDocument.Load(SourcePath("src", "Noto.UI", "Styles", "NoteRow.xaml"));

    [Fact]
    public void The_palette_is_the_stored_keys_in_shortcut_order_each_with_a_name()
    {
        // NoteColor.Keys is note1..note6 in Ctrl+1..6 order (ADR-011); the names follow it one for one.
        var keys = Regex.Matches(Palette, @"\(""(note\d)"", ""(\w+)""\)").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(Noto.Core.Notes.NoteColor.Keys, keys);
        Assert.Contains("public static string StateOf(string? key) => NameOf(key) is null ? \"NoColor\" : \"Note\" + key![^1];", Palette, StringComparison.Ordinal);
    }

    [Fact]
    public void The_card_template_paints_each_colour_from_theme_resources_and_none_by_default()
    {
        XElement template = NoteRow.Descendants(Xaml + "ControlTemplate").Single(t => (string?)t.Attribute(X + "Key") == "NoteCardTemplate");
        XElement group = template.Descendants(Xaml + "VisualStateGroup").Single(g => (string?)g.Attribute(X + "Name") == "ColorStates");
        var states = group.Elements(Xaml + "VisualState").ToList();

        Assert.Equal(["NoColor", "Note1", "Note2", "Note3", "Note4", "Note5", "Note6"], states.Select(s => (string?)s.Attribute(X + "Name")));
        Assert.Empty(states[0].Descendants(Xaml + "Setter"));

        for (int n = 1; n <= 6; n++)
        {
            var setters = states[n].Descendants(Xaml + "Setter").ToDictionary(s => (string)s.Attribute("Target")!, s => (string)s.Attribute("Value")!);
            Assert.Equal($"{{ThemeResource Note{n}TintBrush}}", setters["ColorTint.Background"]);
            Assert.Equal($"{{ThemeResource Note{n}MarkerBrush}}", setters["ColorMarker.Background"]);
        }

        // The tint and the marker sit under the content, so they never take a click or cover the text.
        var root = template.Elements(Xaml + "Grid").Single();
        var children = root.Elements().Select(e => (string?)e.Attribute(X + "Name")).ToList();
        Assert.True(children.IndexOf("ColorTint") < children.IndexOf("Content") && children.IndexOf("ColorMarker") < children.IndexOf("Content"));
    }

    [Fact]
    public void A_colour_is_chosen_from_the_keys_the_menu_and_the_bar_through_one_path()
    {
        // Ctrl+0..6 on the way down, as folding is, so a card and its editor behave the same; before the fold chords.
        string keys = Body(Main, "OnNoteListPreviewKeyDown");
        Assert.True(Index(keys, "TryColorKey(e.Key, out string? colorKey)") < Index(keys, "Ctrl+Alt+Left folds"));
        Assert.Matches(@"if \(IsControlDown\(\) && !IsAltDown\(\) && !IsShiftDown\(\) && TryColorKey\(e\.Key, out string\? colorKey\)\)", keys);

        // The card's menu and its bar list the same items; each one, and each key, goes through ColorCard.
        Assert.Contains("var colour = new MenuFlyoutSubItem { Text = \"Colour\" };", Main, StringComparison.Ordinal);
        Assert.Contains("foreach (MenuFlyoutItemBase item in ColorItems(card))", Body(Main, "OnCardColorClick"), StringComparison.Ordinal);
        Assert.Contains("item.Click += (_, _) => ColorCard(card.Id, key);", Body(Main, "ColorItems"), StringComparison.Ordinal);
        Assert.Equal(1, Regex.Count(Main, @"_stack\.SetColor\("));

        // Applied to the card's container, both when a container is (re)used and when the colour changes.
        Assert.Contains("VisualStateManager.GoToState(args.ItemContainer, NotePalette.StateOf(colored.ColorKey), useTransitions: false);", Body(Main, "OnRowContainerContentChanging"), StringComparison.Ordinal);
        Assert.Contains("VisualStateManager.GoToState(container, NotePalette.StateOf(colorKey), useTransitions: false);", Body(Main, "ColorCard"), StringComparison.Ordinal);

        // Never only a colour: named in the help text and said aloud.
        Assert.Contains("NotePalette.NameOf(ColorKey)", Card, StringComparison.Ordinal);
        Assert.Contains("Announce(NotePalette.NameOf(colorKey) is { } name ? $\"Colour: {name}\" : \"No colour\");", Body(Main, "ColorCard"), StringComparison.Ordinal);

        // The command is registered with its handler.
        Assert.Contains(".Register<SetNoteColor, CommandResult>(new SetNoteColorHandler(notes, clock).Handle)", Code(Path.Join(Windows, "App.xaml.cs")), StringComparison.Ordinal);
    }

    [Fact]
    public void The_bar_style_hides_only_the_tint_so_the_marker_and_high_contrast_are_as_they_were()
    {
        // #191: Background is #150's look (no setters); Bar collapses the tint and touches nothing else, so the marker
        // keeps each colour's brush, and high contrast, whose tint is already transparent and whose marker is the window
        // text colour (DesignSystemGuardTests), draws the same in both.
        XElement template = NoteRow.Descendants(Xaml + "ControlTemplate").Single(t => (string?)t.Attribute(X + "Key") == "NoteCardTemplate");
        XElement group = template.Descendants(Xaml + "VisualStateGroup").Single(g => (string?)g.Attribute(X + "Name") == "ColorStyleStates");
        var states = group.Elements(Xaml + "VisualState").ToList();

        Assert.Equal(["BackgroundStyle", "BarStyle"], states.Select(s => (string?)s.Attribute(X + "Name")));
        Assert.Empty(states[0].Descendants(Xaml + "Setter"));
        var bar = Assert.Single(states[1].Descendants(Xaml + "Setter"));
        Assert.Equal("ColorTint.Visibility", (string?)bar.Attribute("Target"));
        Assert.Equal("Collapsed", (string?)bar.Attribute("Value"));

        // No style-specific paint anywhere: the palette, high contrast's included, is #150's.
        Assert.DoesNotContain("Style", string.Concat(XDocument.Load(SourcePath("src", "Noto.UI", "Themes", "Colors.xaml")).Descendants()
            .Select(e => (string?)e.Attribute(X + "Key")).Where(k => k is not null && k.StartsWith("Note", StringComparison.Ordinal))), StringComparison.Ordinal);
    }

    [Fact]
    public void The_colour_style_is_applied_live_to_every_card_and_never_changes_what_is_said()
    {
        Assert.Contains("public static string StateOf(NoteColorStyle style) => style == NoteColorStyle.Bar ? \"BarStyle\" : \"BackgroundStyle\";", Palette, StringComparison.Ordinal);

        // Every realised card at once, without rebuilding the list, and every card realised later.
        string apply = Body(Main, "ApplyNoteColorStyle");
        Assert.Contains("_noteColorStyle = style;", apply, StringComparison.Ordinal);
        Assert.Contains("VisualStateManager.GoToState(container, NotePalette.StateOf(style), useTransitions: false);", apply, StringComparison.Ordinal);
        Assert.DoesNotContain("Notes.Clear", apply, StringComparison.Ordinal);
        Assert.Contains("VisualStateManager.GoToState(args.ItemContainer, NotePalette.StateOf(_noteColorStyle), useTransitions: false);", Body(Main, "OnRowContainerContentChanging"), StringComparison.Ordinal);

        // From the first frame, and whenever the setting changes.
        string app = Code(Path.Join(Windows, "App.xaml.cs"));
        Assert.Contains("main.ApplyNoteColorStyle(preferences.NoteColorStyle);", app, StringComparison.Ordinal);
        Assert.Contains("_main.ApplyNoteColorStyle(preferences.NoteColorStyle);", Body(app, "OnSettingChanged"), StringComparison.Ordinal);

        // The colour's name is the card's help text in both styles: what is said does not know the style.
        Assert.DoesNotContain("NoteColorStyle", Card, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_offers_background_and_bar_named_for_assistive_technology()
    {
        XDocument page = XDocument.Load(Path.Join(Windows, "SettingsPage.xaml"));
        XElement box = page.Descendants(Xaml + "ComboBox").Single(e => (string?)e.Attribute(X + "Name") == "NoteColorStyleSetting");

        Assert.Equal("Note colour style", (string?)box.Attribute("AutomationProperties.Name"));
        Assert.Equal("OnNoteColorStyleChanged", (string?)box.Attribute("SelectionChanged"));
        Assert.Equal(Enum.GetNames<Noto.Core.Notes.NoteColorStyle>(), box.Elements(Xaml + "ComboBoxItem").Select(i => (string?)i.Attribute("Tag")));

        // In the Appearance group, right after the theme.
        var names = page.Descendants(Xaml + "ComboBox").Select(e => (string?)e.Attribute(X + "Name")).ToList();
        Assert.Equal(names.IndexOf("ThemeSetting") + 1, names.IndexOf("NoteColorStyleSetting"));

        string code = Code(Path.Join(Windows, "SettingsPage.xaml.cs"));
        Assert.Contains("Select(NoteColorStyleSetting, preferences.NoteColorStyle.ToString());", code, StringComparison.Ordinal);
        Assert.Contains("Write(p => NoteColorStyleSetting.SelectedItem is not null && p.SetNoteColorStyle(Selected<NoteColorStyle>(NoteColorStyleSetting)));", code, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- helpers

    private static int Index(string text, string value)
    {
        int index = text.IndexOf(value, StringComparison.Ordinal);
        Assert.True(index >= 0, $"Not found: {value}");
        return index;
    }

    private static string Code(string path) => File.ReadAllText(path);

    private static string Body(string source, string method)
    {
        var declaration = Regex.Match(source, $@"\n    (private|internal|public)( static)? [\w?<>,()\s]+ {method}\(");
        Assert.True(declaration.Success, $"Not found: {method}");
        int open = source.IndexOf('{', declaration.Index);
        int depth = 0;

        for (int i = open; i < source.Length; i++)
        {
            depth += source[i] == '{' ? 1 : source[i] == '}' ? -1 : 0;

            if (depth == 0)
            {
                return source[open..(i + 1)];
            }
        }

        throw new InvalidOperationException($"Unbalanced body for {method}");
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
