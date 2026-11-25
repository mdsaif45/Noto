using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// Theme files (#269, ADR-023) in the source: chosen, installed, revealed and removed from Settings › Appearance; applied
/// on the design system's own Light and Dark brushes, before the first frame and whenever the choice changes; never on
/// high contrast; and copied again by what reads colours outside XAML (the editor's RTF, a card's runs). What it looks
/// like running is Invoke-Visual.ps1's case 19.
/// </summary>
public sealed class ThemeGuardTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private static readonly string Windows = Path.GetDirectoryName(SourcePath("src", "Noto.Windows", "App.xaml.cs"))!;
    private static readonly string App = Code("App.xaml.cs");
    private static readonly string Colours = Code("ThemeColors.cs");
    private static readonly string Page = Code("SettingsPage.Themes.cs");
    private static readonly XDocument PageXaml = XDocument.Load(Path.Join(Windows, "SettingsPage.xaml"));

    [Fact]
    public void Appearance_has_a_mode_row_and_a_theme_row_with_install_show_in_explorer_and_remove()
    {
        // ADR-023 §8: the Light/Dark/System row is Mode, as Windows names it; its id is kept for the harnesses.
        XElement mode = Named("ThemeSetting");
        Assert.Equal("Mode", (string?)mode.Attribute("AutomationProperties.Name"));
        Assert.Contains(mode.Parent!.Descendants(Xaml + "TextBlock"), t => (string?)t.Attribute("Text") == "Mode");

        XElement theme = Named("ThemeFileSetting");
        Assert.Equal("Theme", (string?)theme.Attribute("AutomationProperties.Name"));
        Assert.Equal("OnThemeFileChanged", (string?)theme.Attribute("SelectionChanged"));
        Assert.Same(theme.Parent, Named("InstallThemeButton").Ancestors(Xaml + "Grid").First());

        foreach ((string button, string click) in new[] { ("InstallThemeButton", "OnInstallThemeClick"), ("RevealThemeButton", "OnRevealThemeClick"), ("RemoveThemeButton", "OnRemoveThemeClick") })
        {
            XElement element = Named(button);
            Assert.Equal(click, (string?)element.Attribute("Click"));
            Assert.False(string.IsNullOrWhiteSpace((string?)element.Attribute("AutomationProperties.Name")));
        }

        // Listed off the UI thread each time Settings opens; Default first, an unreadable file listed but not choosable.
        Assert.Contains("_ = ShowThemes();", Code("SettingsPage.xaml.cs"), StringComparison.Ordinal);
        Assert.Contains("_themes = await _context.Themes.List();", Page, StringComparison.Ordinal);
        Assert.Contains("ThemeFileSetting.Items.Add(new ComboBoxItem { Content = Core.Themes.ThemeFile.DefaultName, Tag = string.Empty });", Page, StringComparison.Ordinal);
        Assert.Contains("IsEnabled = theme.Name is not null,", Page, StringComparison.Ordinal);

        // Chosen through the typed preference only; a same-name install asks first, Cancel the default; removing the
        // chosen theme goes back to Default.
        Assert.Contains("Write(p => ThemeFileSetting.SelectedItem is ComboBoxItem item && p.SetThemeFile(FileOf(item)));", Page, StringComparison.Ordinal);
        Assert.Matches(@"Title = \$""A theme named \{check\.Theme!\.Name\} is installed\. Replace it\?"",[\s\S]*DefaultButton = ContentDialogButton\.Close,", Page);
        Assert.Matches(@"if \(!_context\.Themes\.Remove\(file\)\)[\s\S]*Write\(p => p\.SetThemeFile\(string\.Empty\)\);", Page);
        Assert.Matches(@"if \(check\.Refusal is \{ \} refusal\)\s*\{\s*ShowNotice\(\$""That theme can't be installed\. \{refusal\}""\);\s*return;", Page);
    }

    [Fact]
    public void The_picker_belongs_to_the_window_and_removal_is_the_recycle_bins_with_its_safety()
    {
        string pick = Body(Colours, "Pick");
        Assert.Contains("picker.FileTypeFilter.Add(ThemeFile.Extension);", pick, StringComparison.Ordinal);
        Assert.Contains("InitializeWithWindow.Initialize(picker, _window());", pick, StringComparison.Ordinal);

        Assert.Contains("ShellRecycleBin.Recycle([_store.PathOf(file)], WindowHandle.FromHwnd(_window())).Recycled == 1", Colours, StringComparison.Ordinal);
        Assert.Contains("ShellLaunch.ShowInFolder(_store.PathOf(file))", Colours, StringComparison.Ordinal);
    }

    [Fact]
    public void The_theme_is_applied_before_the_first_frame_and_whenever_its_setting_changes()
    {
        // Recorded before any theme, applied before the mode, both before the window is shown (ADR-023 §6: no flash).
        int created = App.IndexOf("var themes = new ThemeColors(new ThemeStore(paths.ThemesDirectory),", StringComparison.Ordinal);
        int applied = App.IndexOf("themes.Apply(preferences.ThemeFile, main.Drawer);", StringComparison.Ordinal);
        int mode = App.IndexOf("ApplyTheme(main, preferences.Theme);", StringComparison.Ordinal);
        Assert.True(created > 0 && created < applied && applied < mode && mode < App.IndexOf("_window.Activate();", StringComparison.Ordinal));
        Assert.Matches(@"if \(themes\.Notice is \{ \} themeNotice\)\s*\{\s*main\.ShowStartNotice\(themeNotice\);", App);
        Assert.Contains("_themes.Apply(preferences.ThemeFile, _main.Drawer);", Body(App, "OnSettingChanged"), StringComparison.Ordinal);

        // The note colours' names follow the drawer's mode.
        Assert.Contains("NotePalette.Dark = drawer.ActualTheme == ElementTheme.Dark;", Body(App, "ApplyTheme"), StringComparison.Ordinal);
    }

    [Fact]
    public void Only_light_and_dark_brushes_are_written_and_high_contrast_never()
    {
        // ADR-023 §3, rule 2: the HighContrast dictionary is never read or written; nothing from a file reaches XAML.
        Assert.Contains("(ResourceDictionary)colours.ThemeDictionaries[mode.ToString()]", Colours, StringComparison.Ordinal);
        Assert.Equal(["Light", "Dark"], Enum.GetNames<Noto.Core.Themes.ThemeMode>());
        Assert.DoesNotContain("HighContrast\"", Colours, StringComparison.Ordinal);
        Assert.DoesNotContain("XamlReader", Colours, StringComparison.Ordinal);

        // The focus ring and the search match are never a theme's target.
        foreach (string fixedKey in new[] { "BorderFocusBrush\"]", "SearchMatch" })
        {
            Assert.DoesNotMatch($@"\(""[\w.]+"", \[[^\]]*{Regex.Escape(fixedKey)}", Colours);
        }

        // Only what a theme sets is written, and only what was written is set back (Default is the recorded colours).
        string apply = Body(Colours, "Apply");
        Assert.Matches(@"if \(touched\.Contains\(target\.Key\)\)\s*\{\s*Set\(target, palette\[target\.Key\]\.ToColor\(\)\);\s*_written\.Add\(target\);\s*\}\s*else if \(_written\.Remove\(target\)\)\s*\{\s*Set\(target, target\.Own\);", apply);

        // The trace names the theme and counts, never a path, and says whether the drawer holds the very brush set.
        Assert.Contains("Noto.Theme applied={theme?.Name ?? ThemeFile.DefaultName} warnings={theme?.Warnings.Count ?? 0} fallbacks={fallbacks} shared=", apply, StringComparison.Ordinal);
        Assert.Contains("Noto.Theme refused reason=", apply, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"Trace\.WriteLine\([^;]*\b(file|path)\b", Colours);
    }

    [Fact]
    public void What_copies_colours_draws_again_when_a_theme_is_applied()
    {
        // ADR-023 §6: the editor's RTF palette is a snapshot, and a card's runs are built from brushes.
        Assert.Contains("Changed?.Invoke(this, EventArgs.Empty);", Body(Colours, "Apply"), StringComparison.Ordinal);
        Assert.Contains("private void OnThemeColorsChanged(object? sender, EventArgs e) => RestyleAll();", Code("NoteEditorHost.xaml.cs"), StringComparison.Ordinal);
        Assert.Contains("private void OnThemeColorsChanged(object? sender, EventArgs e) => Render();", Code("MarkdownReadView.cs"), StringComparison.Ordinal);
        Assert.Contains("Unloaded += (_, _) => ThemeColors.Changed -= OnThemeColorsChanged;", Code("MarkdownReadView.cs"), StringComparison.Ordinal);

        // ADR-023 §7: quotes and task boxes take their own tokens, in the editor and the read view alike; none in high
        // contrast in the read view, where the row's own text colour wins.
        foreach (string view in new[] { "MainWindow.xaml", "NoteEditorHost.xaml" })
        {
            string xaml = File.ReadAllText(Path.Join(Windows, view));
            Assert.Contains("QuoteBrush=\"{ThemeResource TextQuoteBrush}\"", xaml, StringComparison.Ordinal);
            Assert.Contains("TaskBrush=\"{ThemeResource TaskMarkBrush}\"", xaml, StringComparison.Ordinal);
        }

        Assert.Contains("Brush? quote = highContrast ? null : QuoteBrush;", Code("MarkdownReadView.cs"), StringComparison.Ordinal);
        Assert.Contains("Foreground = NoteEditor.TaskBrush ?? NoteEditor.Foreground,", Code("NoteEditorHost.xaml.cs"), StringComparison.Ordinal);
        Assert.Contains("MarkdownSpanKind.Quote => Look.Italic | Look.Quote,", Code("NoteEditorHost.Styling.cs"), StringComparison.Ordinal);
    }

    private static XElement Named(string name) => PageXaml.Descendants().Single(e => (string?)e.Attribute(X + "Name") == name);

    private static string Code(string file) => File.ReadAllText(Path.Join(Windows, file));

    private static string Body(string source, string method)
    {
        var declaration = Regex.Match(source, $@"\n    (private|internal|public)( static)?( async)? [\w?<>,()\s]+ {method}\(");
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
