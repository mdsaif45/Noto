using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// Search's wiring (ADR-018 §3), checked in source as the other surfaces' are. What it does at run time is
/// Invoke-Search.ps1's; what a query means is Core's and Infrastructure's, tested against real SQLite.
/// </summary>
public sealed class SearchGuardTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly string Windows = Path.GetDirectoryName(SourcePath("src", "Noto.Windows", "App.xaml.cs"))!;
    private static readonly string Main = Code(Path.Join(Windows, "MainWindow.xaml.cs"));
    private static readonly string Search = Code(Path.Join(Windows, "MainWindow.Search.cs"));
    private static readonly string Page = Code(Path.Join(Windows, "SearchPage.xaml.cs"));
    private static readonly XDocument PageXaml = XDocument.Load(Path.Join(Windows, "SearchPage.xaml"));
    private static readonly XDocument MainXaml = XDocument.Load(Path.Join(Windows, "MainWindow.xaml"));

    [Fact]
    public void Search_is_built_only_while_open_and_released_when_left()
    {
        Assert.Equal(1, Regex.Count(Search + Main, @"new SearchPage\(\)"));
        Assert.DoesNotContain(MainXaml.Descendants(), e => e.Name.LocalName == "SearchPage");
        Assert.Matches(
            @"\(\(Panel\)page\.Parent\)\.Children\.Remove\(page\);\s*page\.KeyDown -= OnSearchKeyDown;\s*page\.BackRequested -= OnSearchBackRequested;\s*page\.OpenRequested -= OnSearchOpenRequested;\s*page\.CopyRequested -= OnSearchCopyRequested;\s*SearchSurface = null;",
            Body(Search, "CloseSearch"));
        Assert.Equal(2, Regex.Count(Search, @"SearchSurface = "));
    }

    [Fact]
    public void Ctrl_F_opens_Search_from_the_root_and_the_editor_hands_it_on()
    {
        Assert.Matches(@"new KeyboardAccelerator \{ Key = VirtualKey\.F, Modifiers = VirtualKeyModifiers\.Control \};\s*openSearch\.Invoked \+= OnSearchAccelerator;\s*WorkspaceRoot\.KeyboardAccelerators\.Add\(openSearch\);", Main);
        Assert.Matches(@"args\.Handled = true;\s*OpenSearch\(\);", Body(Main, "OnSearchAccelerator"));
        Assert.Contains("args.Key is (VirtualKey)0xBC or VirtualKey.F && args.Modifiers == VirtualKeyModifiers.Control", Code(Path.Join(Windows, "NoteEditorHost.xaml.cs")), StringComparison.Ordinal);
    }

    [Fact]
    public void Opening_Search_saves_first_and_never_covers_Settings_or_the_bin()
    {
        string open = Body(Search, "OpenSearch");
        Assert.Contains("if (Search is null || SettingsSurface is not null || RecycleBinSurface is not null || WelcomeSurface is not null)", open, StringComparison.Ordinal);
        Assert.True(open.IndexOf("_ = TrySaveCard();", StringComparison.Ordinal) is var save and >= 0 && save < open.IndexOf("new SearchPage()", StringComparison.Ordinal));
    }

    [Fact]
    public void Escape_in_Search_follows_the_stored_behaviour_and_Settings_returns_to_it()
    {
        Assert.Contains("e.Handled = OnEscape(WorkspaceSurface.Search);", Body(Search, "OnSearchKeyDown"), StringComparison.Ordinal);
        Assert.Matches(@"case EscapeAction\.LeaveSearch:\s*CloseSearch\(\);\s*return true;", Main);
        Assert.Contains(": SearchSurface is { } search ? search", Body(Main, "OpenSettings"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_result_opens_after_a_save_and_its_caret_lands_on_the_match()
    {
        string open = Body(Search, "OpenFromSearch");
        Assert.StartsWith("{\n        CloseSearch(restoreFocus: false);", open.ReplaceLineEndings("\n"), StringComparison.Ordinal);

        // A failed save on leaving keeps the stack, the card and its text: nothing else happens.
        Assert.Matches(@"LeaveFolder\(\);\s*if \(_openFolderId is not null\)\s*\{\s*return;\s*\}", open);

        // The edit is queued after the folder's read and focus, which EnterSelectedFolder queues first.
        Assert.True(open.IndexOf("EnterSelectedFolder();", StringComparison.Ordinal) < open.IndexOf("() => EditFromSearch(id, caret)", StringComparison.Ordinal));

        Assert.Matches(@"if \(_pendingCaretAt is \{ \} at\)\s*\{\s*_pendingCaretAt = null;\s*host\.PlaceCaretAt\(at\);", Body(Main, "AttachEditor"));
        Assert.Contains("_pendingCaretAt = null;", Body(Main, "StartEditor"), StringComparison.Ordinal);
    }

    [Fact]
    public void Quick_copy_hides_only_after_the_clipboard_took_the_text()
    {
        string copy = Body(Search, "OnSearchCopyRequested");
        Assert.Matches(@"catch \(COMException\)\s*\{\s*SearchSurface\?\.ShowNotice\([^;]+\);\s*return;\s*\}\s*CloseSearch\(\);\s*DismissRequested\?\.Invoke\(this, EventArgs\.Empty\);", copy);
    }

    [Fact]
    public void The_box_keeps_the_keyboard_and_Ctrl_C_copies_its_own_selection_first()
    {
        string keys = Body(Page, "OnBoxPreviewKeyDown");
        Assert.Contains("case VirtualKey.Down:", keys, StringComparison.Ordinal);
        Assert.Contains("case VirtualKey.Up:", keys, StringComparison.Ordinal);
        Assert.Contains("case VirtualKey.Enter when Selected is { } item:", keys, StringComparison.Ordinal);
        Assert.Contains("e.Key == VirtualKey.C && (IsDown(VirtualKey.Shift) || SearchBox.SelectionLength == 0) && TryCopy()", keys, StringComparison.Ordinal);
        Assert.Contains("IsDown(VirtualKey.Shift) ? SearchText.WithoutTitle(note.Content) : note.Content", Body(Page, "TryCopy"), StringComparison.Ordinal);
    }

    [Fact]
    public void What_was_searched_for_is_never_traced()
    {
        string run = Body(Page, "Run");
        Assert.DoesNotMatch(@"Trace\.WriteLine\([^;]*(SearchBox|Text|Query)", run);
        Assert.Contains("_search.Execute(SearchBox.Text, ScopeToggle.IsChecked == true ? _scope : null)", run, StringComparison.Ordinal);
    }

    [Fact]
    public void Matches_use_the_search_tokens_and_the_status_is_announced()
    {
        var highlighters = PageXaml.Descendants().Where(e => e.Name.LocalName == "TextHighlighter").ToList();
        Assert.Equal(2, highlighters.Count);
        Assert.All(highlighters, h =>
        {
            Assert.Equal("{ThemeResource SearchMatchBrush}", (string?)h.Attribute("Background"));
            Assert.Equal("{ThemeResource SearchMatchTextBrush}", (string?)h.Attribute("Foreground"));
        });

        XElement status = PageXaml.Descendants().Single(e => (string?)e.Attribute(X + "Name") == "SearchStatus");
        Assert.Equal("Polite", (string?)status.Attribute("AutomationProperties.LiveSetting"));

        // In all three themes; High Contrast in the user's own colours.
        string colors = File.ReadAllText(SourcePath("src", "Noto.UI", "Themes", "Colors.xaml"));
        Assert.Equal(3, Regex.Count(colors, @"x:Key=""SearchMatchBrush"""));
        Assert.Equal(3, Regex.Count(colors, @"x:Key=""SearchMatchTextBrush"""));
        Assert.Contains(@"<SolidColorBrush x:Key=""SearchMatchBrush"" Color=""{ThemeResource SystemColorHighlightColor}"" />", colors, StringComparison.Ordinal);
    }

    private static string Code(string path) => Regex.Replace(File.ReadAllText(path), @"^\s*//.*$", string.Empty, RegexOptions.Multiline);

    private static string Body(string source, string method)
    {
        var declaration = Regex.Match(source, $@"(private|internal|public)(?: static)?(?: async)? [\w<>?,\[\] ]+? {method}\(");
        Assert.True(declaration.Success, $"Not found: {method}");
        int open = source.IndexOf('{', declaration.Index);
        int depth = 0;

        for (int i = open; i < source.Length; i++)
        {
            depth += source[i] switch { '{' => 1, '}' => -1, _ => 0 };

            if (depth == 0)
            {
                return source[open..(i + 1)];
            }
        }

        throw new InvalidOperationException($"Unbalanced body for {method}");
    }

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
