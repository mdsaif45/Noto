using System.Text.RegularExpressions;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// Links' wiring (ADR-019 §2, §3), checked in source as the other surfaces' are. What a link is, is Core's
/// (<c>LinkTargetTests</c>); what opening does, Platform's (<c>ShellLaunchTests</c>); at run time, Invoke-Links.ps1's.
/// </summary>
public sealed class LinkGuardTests
{
    private static readonly string Windows = Path.GetDirectoryName(SourcePath("src", "Noto.Windows", "App.xaml.cs"))!;
    private static readonly string Host = Code(Path.Join(Windows, "NoteEditorHost.xaml.cs"));
    private static readonly string Main = Code(Path.Join(Windows, "MainWindow.xaml.cs"));
    private static readonly string Links = Code(Path.Join(Windows, "MainWindow.Links.cs"));
    private static readonly string ReadView = Code(Path.Join(Windows, "MarkdownReadView.cs"));
    private static readonly string Images = Code(Path.Join(Windows, "NoteEditorHost.Images.cs"));
    private static readonly string Shortcuts = Code(Path.Join(Windows, "NoteEditorHost.Shortcuts.cs"));

    [Fact]
    public void Ctrl_Click_and_Ctrl_Enter_open_a_link_and_a_plain_click_only_places_the_caret()
    {
        Assert.Matches(@"if \(IsDown\(VirtualKey\.Control\) && LinkTarget\.At\(_document, at\) is \{ \} destination\)\s*\{\s*LinkOpenRequested\?\.Invoke\(this, destination\);\s*return;", Body(Host, "OnPointerReleased"));
        Assert.Matches(@"if \(e\.Key == VirtualKey\.Enter && ctrl && !alt && !shift\)\s*\{\s*Flush\(\);\s*if \(LinkTarget\.At\(_document, _selection\.Active\) is \{ \} destination\)\s*\{\s*e\.Handled = true;\s*LinkOpenRequested\?\.Invoke\(this, destination\);", Host);
    }

    [Fact]
    public void Paste_prefers_copied_files_then_a_web_address_over_a_selection()
    {
        string paste = Body(Host, "PasteAsync");
        Assert.Matches(@"if \(paths\.Count > 0\)\s*\{\s*AddFiles\(paths\);\s*return;\s*\}\s*TextEdit\? edit = plain is not null && asMarkdown \? MarkdownCommands\.LinkSelection\(_text, at, plain\) : null;", paste);
        Assert.Contains(".Where(Path.IsPathFullyQualified)", paste, StringComparison.Ordinal);

        // Files that are not images are linked, as before images (D34).
        Assert.Contains("Apply(MarkdownCommands.LinkPaths(_text, _selection, others), record: true);", Body(Images, "AddFiles"), StringComparison.Ordinal);
    }

    [Fact]
    public void Every_link_opens_through_one_checked_path_that_asks_before_a_program()
    {
        Assert.Contains("host.LinkOpenRequested += OnEditorLinkOpenRequested;", Main, StringComparison.Ordinal);
        Assert.Contains("MarkdownReadView.OpenLink = QueueOpenLink;", Main, StringComparison.Ordinal);

        // Never inside an input handler: the launch runs after the input that asked for it (measured: a stuck UI thread).
        Assert.Contains("private void OnEditorLinkOpenRequested(object? sender, string destination) => QueueOpenLink(destination);", Links, StringComparison.Ordinal);
        Assert.Contains("internal void QueueOpenLink(string destination) => _ = DispatcherQueue.TryEnqueueGuarded(() => OpenLink(destination));", Links, StringComparison.Ordinal);
        Assert.Contains("hyperlink.Click += (_, _) => OpenLink?.Invoke(destination);", ReadView, StringComparison.Ordinal);

        string open = Body(Links, "OpenLink");
        Assert.Contains("LinkCheck check = ShellLaunch.Inspect(destination);", open, StringComparison.Ordinal);
        Assert.Matches(@"if \(check\.AsksFirst\)\s*\{\s*var dialog = new ContentDialog", open);
        Assert.Contains("DefaultButton = ContentDialogButton.Close,", open, StringComparison.Ordinal);
        Assert.Matches(@"confirmed = await dialog\.ShowAsync\(\) == ContentDialogResult\.Primary;\s*if \(!confirmed\)\s*\{\s*return;", open);
        Assert.Contains("switch (ShellLaunch.OpenLink(destination, confirmed))", open, StringComparison.Ordinal);

        // No other way to the shell from the windows layer.
        string all = string.Join('\n', Directory.EnumerateFiles(Windows, "*.cs").Select(Code));
        Assert.DoesNotMatch(@"Process\.Start|Launcher\.Launch", all);
        Assert.Equal(1, Regex.Count(all, @"ShellLaunch\.OpenLink\("));
    }

    [Fact]
    public void A_double_click_on_a_shortcut_opens_it_through_the_same_checked_path_as_ctrl_click()
    {
        // #196 (D34): the editor only asks; the window queues it and ShellLaunch checks it (gone, asks first).
        Assert.Contains("NoteEditor.AddHandler(DoubleTappedEvent, new DoubleTappedEventHandler(OnDoubleTapped), handledEventsToo: true);", Host, StringComparison.Ordinal);

        string tapped = Body(Shortcuts, "OnDoubleTapped");
        Assert.Matches(@"if \(_showMarkup \|\| IsDown\(VirtualKey\.Control\)\)\s*\{\s*return;", tapped);
        Assert.Contains("FileShortcuts.In(_document).Where(s => s.Line.Start <= at && at <= s.Line.End)", tapped, StringComparison.Ordinal);
        Assert.Matches(@"HideToolbar\(\);\s*LinkOpenRequested\?\.Invoke\(this, shortcut\.Destination\);\s*\}$", tapped);

        // Never launched from the editor itself.
        Assert.DoesNotMatch(@"ShellLaunch\.|Process\.Start|Launcher\.Launch", Shortcuts);
    }

    [Fact]
    public void Shortcuts_are_drawn_over_the_editor_from_their_lines_and_their_room_is_reset_with_the_style()
    {
        // ADR-017 §1: the line keeps its source; an indent and space below make room, and the overlay draws in it.
        Assert.Matches(@"if \(_showMarkup\)\s*\{\s*return;\s*\}\s*PlaceShortcuts\(\);", Body(Host, "PlaceTaskMarks"));
        string place = Body(Shortcuts, "PlaceShortcuts");
        Assert.Contains("var shortcuts = FileShortcuts.In(_document).Take(ShortcutLimit + 1).ToList();", place, StringComparison.Ordinal);
        Assert.Contains("bool large = FileLinks == FileLinkStyle.Standard;", place, StringComparison.Ordinal);
        Assert.Matches(@"line\.SetIndents\(0, indent, 0\);\s*line\.SpaceAfter = below;", place);
        Assert.Contains("AutomationProperties.SetAccessibilityView(element, AccessibilityView.Raw);", place, StringComparison.Ordinal);
        Assert.Contains("TaskMarks.Children.Add(element);", place, StringComparison.Ordinal);

        // An edit, Show markup or a theme change restyles a line: its indent goes with its space below.
        Assert.Matches(@"paragraph\.SpaceAfter = 0;\s*paragraph\.SetIndents\(0, 0, 0\);", Code(Path.Join(Windows, "NoteEditorHost.Styling.cs")));

        // The style applies to the editor open now, at once.
        Assert.Matches(@"NoteEditorHost\.FileLinks = style;\s*_editor\?\.ShowFileLinks\(\);", Body(Links, "ApplyFileLinks"));
        Assert.Contains("internal void ShowFileLinks() => QueueTaskMarks();", Shortcuts, StringComparison.Ordinal);
    }

    [Fact]
    public void A_shortcuts_icon_is_the_shells_read_off_the_ui_thread_and_kept_never_the_files_content()
    {
        // The seam: one platform call, SHGetFileInfo, for the icon and the type's name; never a thumbnail, never the file.
        string icons = Code(SourcePath("src", "Noto.Platform.Windows", "ShellIcons.cs"));
        Assert.Contains("uint flags = SHGFI_ICON | SHGFI_TYPENAME | (large ? SHGFI_LARGEICON : SHGFI_SMALLICON);", icons, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"File\.|FileStream|Thumbnail|IShellItemImageFactory|SHGFI_USEFILEATTRIBUTES|Trace\.|Log", icons);

        string windows = string.Join('\n', Directory.EnumerateFiles(Windows, "*.cs").Select(Code));
        Assert.Equal(1, Regex.Count(windows, @"ShellIcons\.Of\("));
        Assert.DoesNotMatch(@"SHGetFileInfo|GetThumbnailAsync", windows);

        // Off the UI thread, once per target and size; its size is the file system's, never its content.
        string describe = Body(Shortcuts, "Describe");
        Assert.Matches(@"await Task\.Run\(\(\) =>\s*\{\s*ShellIcon\? shell = ShellIcons\.Of\(path, large\);", describe);
        Assert.Contains("new FileInfo(path).Length", describe, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"File\.(Open|Read)|FileStream|Trace\.", Shortcuts.Replace("MarkdownImporter.Text(path)", string.Empty, StringComparison.Ordinal));
        Assert.Matches(@"if \(_shortcuts\.TryGetValue\(\(path, large\), out Shortcut\? known\)\)\s*\{\s*return known;\s*\}\s*_shortcuts\[\(path, large\)\] = null;\s*_ = Describe\(path, large\);", Body(Shortcuts, "Described"));
    }

    [Fact]
    public void The_importing_and_file_link_settings_are_applied_from_the_first_frame()
    {
        string app = Code(Path.Join(Windows, "App.xaml.cs"));
        Assert.Contains("main.ApplyFileLinks(preferences.FileLinks);", app, StringComparison.Ordinal);
        Assert.Contains("NoteEditorHost.PictureDrops = preferences.PictureDrop;", app, StringComparison.Ordinal);
        Assert.Contains("NoteEditorHost.TextFileDrops = preferences.TextFileDrop;", app, StringComparison.Ordinal);

        // Each choice's tag is the setting's member name, as the page parses it; the default is first.
        var page = System.Xml.Linq.XDocument.Load(Path.Join(Windows, "SettingsPage.xaml"));
        string[] Tags(string box) => [.. page.Descendants().Single(e => (string?)e.Attribute(System.Xml.Linq.XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == box)
            .Elements().Select(i => (string?)i.Attribute("Tag") ?? string.Empty)];
        Assert.Equal(["Standard", "Compact"], Tags("FileLinksSetting"));
        Assert.Equal(["KeepCopy", "Shortcut"], Tags("PictureDropSetting"));
        Assert.Equal(["Shortcut", "InsertText"], Tags("TextFileDropSetting"));
    }

    [Fact]
    public void A_card_link_is_never_a_tab_stop()
    {
        // The stack's keys stay the stack's; the keyboard opens a link from the editor (Ctrl+Enter).
        Assert.Contains("new Hyperlink { IsTabStop = false, UnderlineStyle = UnderlineStyle.Single }", ReadView, StringComparison.Ordinal);
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
