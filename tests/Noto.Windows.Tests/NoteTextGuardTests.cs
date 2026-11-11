using System.Globalization;
using System.Xml.Linq;
using Noto.Core.Workspace;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// F13 (#154) in the source: the note text's family and size are set on each card, which the read view and the editor
/// inherit and scale their headings from; a chosen family falls back to the design system's; Ctrl+= and Ctrl+- step
/// the stored size from every surface and the editor; Settings offers exactly the values Core allows. What it draws is
/// Invoke-Settings.ps1 case 16's to show.
/// </summary>
public sealed class NoteTextGuardTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private static readonly string Windows = Path.GetDirectoryName(SourcePath("src", "Noto.Windows", "App.xaml.cs"))!;
    private static readonly string Main = File.ReadAllText(Path.Join(Windows, "MainWindow.xaml.cs"));
    private static readonly string Reader = File.ReadAllText(Path.Join(Windows, "MarkdownReadView.cs"));
    private static readonly string Host = File.ReadAllText(Path.Join(Windows, "NoteEditorHost.xaml.cs"));

    [Fact]
    public void A_chosen_family_is_followed_by_the_design_systems_so_a_missing_one_falls_back()
    {
        // D51: the view never names a family alone; the design system's comes after it, as XAML falls back in order.
        Assert.Contains("_noteFont = font.Length == 0 ? body : new Microsoft.UI.Xaml.Media.FontFamily($\"{font}, {body.Source}\");", Main, StringComparison.Ordinal);
    }

    [Fact]
    public void The_family_and_size_are_set_on_every_card_and_the_read_view_and_editor_scale_from_them()
    {
        // Every realised card now, and every card a container is (re)used for.
        Assert.Contains("ApplyNoteText(container);", Main, StringComparison.Ordinal);
        Assert.Matches(@"VisualStateManager\.GoToState\(args\.ItemContainer, NotePalette\.StateOf\(colored\.ColorKey\), useTransitions: false\);\s*ApplyNoteText\(args\.ItemContainer\);", Main);

        // The read view: headings a step above the size it inherits, rendered again when it changes.
        Assert.Contains("run.FontSize = HeadingSize(span.Level);", Reader, StringComparison.Ordinal);
        Assert.Contains("return Math.Round(FontSize * heading / body);", Reader, StringComparison.Ordinal);
        Assert.Contains("RegisterPropertyChangedCallback(FontSizeProperty, (_, _) => Render());", Reader, StringComparison.Ordinal);
        Assert.Contains("RegisterPropertyChangedCallback(FontFamilyProperty, (_, _) => Render());", Reader, StringComparison.Ordinal);
        Assert.DoesNotContain("Resources[span.Level switch", Reader, StringComparison.Ordinal);

        // The editor: its family and size, and headings scaled the same way, restyled when they change.
        Assert.Contains("BodyFont: FontFamily.Source.Split(',')[0].Trim(),", Host, StringComparison.Ordinal);
        Assert.Contains("BodySize: FontSize,", Host, StringComparison.Ordinal);
        Assert.Contains("Heading1Size: Scaled(\"FontSizeHeading\"),", Host, StringComparison.Ordinal);
        Assert.Contains("RegisterPropertyChangedCallback(FontSizeProperty, (_, _) => RestyleAll());", Host, StringComparison.Ordinal);
        Assert.Contains("RegisterPropertyChangedCallback(FontFamilyProperty, (_, _) => RestyleAll());", Host, StringComparison.Ordinal);
    }

    [Fact]
    public void Ctrl_plus_and_minus_step_the_stored_size_from_the_root_and_the_editor_hands_them_on()
    {
        // G41, G42: Ctrl+=, Ctrl++ (Shift), keypad + up; Ctrl+- and keypad - down; on the root, so every surface has them.
        foreach (string chord in new[]
        {
            @"\(\(VirtualKey\)0xBB, VirtualKeyModifiers\.Control, 1\)",
            @"\(\(VirtualKey\)0xBB, VirtualKeyModifiers\.Control \| VirtualKeyModifiers\.Shift, 1\)",
            @"\(VirtualKey\.Add, VirtualKeyModifiers\.Control, 1\)",
            @"\(\(VirtualKey\)0xBD, VirtualKeyModifiers\.Control, -1\)",
            @"\(VirtualKey\.Subtract, VirtualKeyModifiers\.Control, -1\)",
        })
        {
            Assert.Matches(chord, Main);
        }

        Assert.Matches(@"StepNoteSizeBy\(direction\);\s*\};\s*WorkspaceRoot\.KeyboardAccelerators\.Add\(size\);", Main);
        Assert.Contains("(VirtualKey)0xBB or (VirtualKey)0xBD or VirtualKey.Add or VirtualKey.Subtract", Host, StringComparison.Ordinal);

        // Said aloud: the size is not only seen.
        Assert.Contains("Announce(string.Create(CultureInfo.CurrentCulture, $\"Text size {size}\"));", Main, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_offers_exactly_the_families_and_sizes_core_allows()
    {
        XDocument page = XDocument.Load(Path.Join(Windows, "SettingsPage.xaml"));

        List<string?> Tags(string name) => page.Descendants(Xaml + "ComboBox").Single(c => (string?)c.Attribute(X + "Name") == name)
            .Elements(Xaml + "ComboBoxItem").Select(i => (string?)i.Attribute("Tag")).ToList();

        Assert.Equal(new[] { NoteText.DefaultFont }.Concat(NoteText.Fonts), Tags("NoteFontSetting"));
        Assert.Equal(NoteText.Sizes.Select(s => s.ToString(CultureInfo.InvariantCulture)), Tags("NoteSizeSetting"));
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
