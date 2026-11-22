using System.Text.RegularExpressions;
using System.Xml.Linq;
using Noto.Core.Settings;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// F17 (#156) and F18 (#157) in the source: the new-note shortcuts are off by default, only ever show Noto, and make the
/// note through the window's own Ctrl+N path, the clipboard's through the editor's own paste; with Ask which folder on
/// (#243), once a folder is chosen. What a real press does is Invoke-Settings.ps1's cases 17, 18 and 24.
/// </summary>
public sealed class NewNoteShortcutGuardTests
{
    private static readonly string App = File.ReadAllText(SourcePath("src", "Noto.Windows", "App.xaml.cs"));

    [Fact]
    public void It_is_off_by_default_with_the_planned_chord()
    {
        Assert.False(SettingKeys.NewNoteHotkeyEnabled.Default);
        Assert.Equal("Ctrl+Shift+Win+N", SettingKeys.NewNoteHotkeyBinding.Default);

        // F18 (#157): the clipboard's, G4's planned default.
        Assert.False(SettingKeys.ClipboardNoteHotkeyEnabled.Default);
        Assert.Equal("Ctrl+Shift+Win+V", SettingKeys.ClipboardNoteHotkeyBinding.Default);

        // F21 (#159): Search's, G3's planned default.
        Assert.False(SettingKeys.SearchHotkeyEnabled.Default);
        Assert.Equal("Ctrl+Shift+Win+F", SettingKeys.SearchHotkeyBinding.Default);

        // #198: change side's and keep open's (G5, G6), S for side and K for keep.
        Assert.False(SettingKeys.ChangeSideHotkeyEnabled.Default);
        Assert.Equal("Ctrl+Shift+Win+S", SettingKeys.ChangeSideHotkeyBinding.Default);
        Assert.False(SettingKeys.KeepOpenHotkeyEnabled.Default);
        Assert.Equal("Ctrl+Shift+Win+K", SettingKeys.KeepOpenHotkeyBinding.Default);
    }

    [Fact]
    public void The_change_side_and_keep_open_shortcuts_neither_show_nor_hide_Noto_and_take_the_settings_and_pin_paths()
    {
        // #198: the side is Settings › Side's own write, which the store announces and the dock applies; keep open is the
        // header's toggle, as the tray's and Ctrl+D. Neither asks the coordinator to show or hide.
        string pressed = Regex.Match(App, @"private void OnHotkeyPressed\(GlobalAction action, int messageTime\)[\s\S]*?\n    }\n").Value;
        string side = Regex.Match(pressed, @"if \(action == GlobalAction\.ChangeSide\)[\s\S]*?return;\s*\}").Value;
        string keep = Regex.Match(pressed, @"if \(action == GlobalAction\.KeepOpen\)[\s\S]*?return;\s*\}").Value;

        Assert.Contains("preferences.SetEdge(preferences.Edge == WorkspaceEdge.Left ? WorkspaceEdge.Right : WorkspaceEdge.Left)", side, StringComparison.Ordinal);
        Assert.Contains("_main?.RefreshSettings();", side, StringComparison.Ordinal);
        Assert.Contains("_main?.SetPinned(!_coordinator.Pinned);", keep, StringComparison.Ordinal);

        foreach (string branch in new[] { side, keep })
        {
            Assert.DoesNotContain("OnActivationRequested", branch, StringComparison.Ordinal);
            Assert.DoesNotContain("BringToForeground", branch, StringComparison.Ordinal);
        }

        // After the recorder's suspension, which covers every action.
        Assert.True(pressed.IndexOf("_hotkeySuspended", StringComparison.Ordinal) < pressed.IndexOf("GlobalAction.ChangeSide", StringComparison.Ordinal));
    }

    [Fact]
    public void The_search_shortcut_shows_Noto_never_hides_it_and_opens_Search_as_CtrlF_does()
    {
        string pressed = Regex.Match(App, @"private void OnHotkeyPressed\(GlobalAction action, int messageTime\)[\s\S]*?\n    }\n").Value;
        string search = Regex.Match(pressed, @"if \(action == GlobalAction\.Search\)[\s\S]*?return;\s*\}").Value;

        Assert.Contains("_coordinator.OnActivationRequested(WorkspaceRequest.Launch, messageTime)", search, StringComparison.Ordinal);
        Assert.DoesNotContain("WorkspaceRequest.Toggle", search, StringComparison.Ordinal);
        Assert.Contains("_main?.OpenSearchFromShortcut();", search, StringComparison.Ordinal);

        string window = File.ReadAllText(SourcePath("src", "Noto.Windows", "MainWindow.Search.cs"));
        Assert.Contains("_ = DispatcherQueue.TryEnqueueGuarded(DispatcherQueuePriority.Low, OpenSearch);", window, StringComparison.Ordinal);
    }

    [Fact]
    public void A_press_shows_Noto_never_hides_it_and_the_window_makes_the_note_as_CtrlN_does()
    {
        string pressed = Regex.Match(App, @"private void OnHotkeyPressed\(GlobalAction action, int messageTime\)[\s\S]*?\n    }\n").Value;
        string newNote = Regex.Match(pressed, @"if \(action is GlobalAction\.NewNote or GlobalAction\.NoteFromClipboard\)[\s\S]*?return;\s*\}").Value;

        Assert.Contains("_coordinator.OnActivationRequested(WorkspaceRequest.Launch, messageTime)", newNote, StringComparison.Ordinal);
        Assert.DoesNotContain("WorkspaceRequest.Toggle", newNote, StringComparison.Ordinal);
        Assert.Contains("_main?.NewNoteFromGlobalShortcut(fromClipboard: action == GlobalAction.NoteFromClipboard);", newNote, StringComparison.Ordinal);

        // The recorder's suspension is checked first, for every action.
        Assert.True(pressed.IndexOf("_hotkeySuspended", StringComparison.Ordinal) < pressed.IndexOf("GlobalAction.NewNote", StringComparison.Ordinal));

        string window = File.ReadAllText(SourcePath("src", "Noto.Windows", "MainWindow.NewNoteShortcut.cs"));
        Assert.Matches(@"DispatcherQueuePriority\.Low, \(\) =>\s*\{\s*string\? before = _stack\.Editing is \{ \} editing \? editing\.Value : null;\s*CreateNoteInOpenFolder\(content\);", window);
    }

    [Fact]
    public void A_note_from_the_clipboard_is_a_new_note_and_the_editors_own_paste_from_the_shortcut_and_the_tray()
    {
        // F18 (#157): no second paste path; Ctrl+V's, with its Markdown from HTML, its pictures and its links.
        string window = File.ReadAllText(SourcePath("src", "Noto.Windows", "MainWindow.NewNoteShortcut.cs"));
        Assert.Contains("if (fromClipboard && _stack.Editing is { } created && created.Value != before)", window, StringComparison.Ordinal);
        Assert.Contains("internal void PasteClipboard() => PasteAsync(formatted: true);", File.ReadAllText(SourcePath("src", "Noto.Windows", "NoteEditorHost.xaml.cs")), StringComparison.Ordinal);

        string tray = Regex.Match(App, @"case TrayCommand\.NoteFromClipboard:[\s\S]*?break;").Value;
        Assert.Contains("ActivateFromTray(", tray, StringComparison.Ordinal);
        Assert.Contains("_main?.NewNoteFromGlobalShortcut(fromClipboard: true);", tray, StringComparison.Ordinal);
    }

    [Fact]
    public void The_plus_buttons_menu_offers_a_new_note_and_one_from_the_clipboard_by_the_trays_path()
    {
        // #187 (B3, D43): a click still makes a note; its context menu (right-click, Shift+F10, Menu key, touch hold) adds
        // New note from clipboard, through the very call the tray and the shortcut make.
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement button = XDocument.Load(SourcePath("src", "Noto.Windows", "MainWindow.xaml")).Descendants()
            .Single(e => (string?)e.Attribute(x + "Name") == "NewNoteButton");
        Assert.Equal("OnNewNoteClick", (string?)button.Attribute("Click"));
        Assert.Contains("New note from clipboard", (string?)button.Attribute("AutomationProperties.HelpText"), StringComparison.Ordinal);

        XElement menu = button.Elements().Single(e => e.Name.LocalName == "Button.ContextFlyout").Elements().Single();
        Assert.Equal("MenuFlyout", menu.Name.LocalName);
        Assert.Equal("OnNewNoteMenuOpening", (string?)menu.Attribute("Opening"));
        Assert.Equal(
            ["New note|OnNewNoteClick", "New note from clipboard|OnNewNoteFromClipboardClick"],
            menu.Elements().Select(i => $"{(string?)i.Attribute("Text")}|{(string?)i.Attribute("Click")}"));

        string window = File.ReadAllText(SourcePath("src", "Noto.Windows", "MainWindow.NewNoteShortcut.cs"));
        Assert.Contains("private void OnNewNoteFromClipboardClick(object sender, RoutedEventArgs e) => NewNoteFromShortcut(fromClipboard: true);", window, StringComparison.Ordinal);

        // Offered only when Ctrl+V would paste something; a clipboard another app holds leaves it offered.
        string opening = Regex.Match(window, @"private void OnNewNoteMenuOpening\([\s\S]*?\n    }\n").Value;
        foreach (string format in new[] { "Text", "Html", "StorageItems", "Bitmap" })
        {
            Assert.Contains($"clipboard.Contains(StandardDataFormats.{format})", opening, StringComparison.Ordinal);
        }

        Assert.Matches(@"catch \(COMException\)\s*\{\s*NewNoteFromClipboardItem\.IsEnabled = true;", Regex.Replace(opening, @"^\s*//.*$", string.Empty, RegexOptions.Multiline));
    }

    [Fact]
    public void Ask_which_folder_is_off_by_default_and_only_the_global_shortcuts_and_the_tray_ask()
    {
        // #243 (B6, J9): off, every path makes the note as before.
        Assert.False(SettingKeys.NotesAskFolder.Default);
        Assert.Equal("notes.ask-folder", SettingKeys.NotesAskFolder.Name);
        Assert.Contains(SettingKeys.NotesAskFolder, SettingKeys.All);

        string ask = File.ReadAllText(SourcePath("src", "Noto.Windows", "MainWindow.AskFolder.cs"));
        Assert.Matches(@"if \(!AskFolder\)\s*\{\s*NewNoteFromShortcut\(fromClipboard\);\s*return;\s*\}\s*_ = DispatcherQueue\.TryEnqueueGuarded\(DispatcherQueuePriority\.Low, \(\) => AskFolderForNote\(fromClipboard\)\);", Method(ask, "NewNoteFromGlobalShortcut"));

        // The paths that ask: the two shortcuts' press and the tray's New note from clipboard. Nothing else calls it.
        string windows = Path.GetDirectoryName(SourcePath("src", "Noto.Windows", "App.xaml.cs"))!;
        string all = string.Join('\n', Directory.EnumerateFiles(windows, "*.cs").Select(File.ReadAllText));
        Assert.Equal(2, Regex.Count(all, @"NewNoteFromGlobalShortcut\(fromClipboard: "));
        Assert.Equal(2, Regex.Count(App, @"_main\?\.NewNoteFromGlobalShortcut\("));

        // The paths that never ask: the + button's menu, Ctrl+N and the + button (the open folder), a noto:// link (the
        // folder it names, or where Ctrl+N would put it).
        string shortcut = File.ReadAllText(SourcePath("src", "Noto.Windows", "MainWindow.NewNoteShortcut.cs"));
        string main = File.ReadAllText(SourcePath("src", "Noto.Windows", "MainWindow.xaml.cs"));
        string automation = File.ReadAllText(SourcePath("src", "Noto.Windows", "MainWindow.Automation.cs"));
        Assert.Contains("private void OnNewNoteFromClipboardClick(object sender, RoutedEventArgs e) => NewNoteFromShortcut(fromClipboard: true);", shortcut, StringComparison.Ordinal);
        Assert.Contains("private void OnNewNoteClick(object sender, RoutedEventArgs e) => CreateNoteInOpenFolder();", main, StringComparison.Ordinal);
        Assert.Contains("CreateNoteInOpenFolder();", Method(main, "OnCreateNoteAccelerator"), StringComparison.Ordinal);
        Assert.Contains("NewNoteFromShortcut(content: request.Text);", Method(automation, "NewNoteFromAutomation"), StringComparison.Ordinal);
        foreach (string source in new[] { shortcut, main, automation })
        {
            Assert.DoesNotContain("AskFolderForNote", source, StringComparison.Ordinal);
            Assert.DoesNotContain("NewNoteFromGlobalShortcut", source, StringComparison.Ordinal);
        }

        // Applied live by the composition root, as the other new-note settings are.
        Assert.Contains("AskFolder = preferences.AskFolder,", App, StringComparison.Ordinal);
        Assert.Contains("_main.AskFolder = preferences.AskFolder;", App, StringComparison.Ordinal);
    }

    [Fact]
    public void The_chooser_lists_recent_folders_first_filters_as_typed_and_makes_the_note_through_the_shortcuts_own_path()
    {
        string chooser = Method(File.ReadAllText(SourcePath("src", "Noto.Windows", "MainWindow.AskFolder.cs")), "AskFolderForNote");

        // The recent opened folders first (Core's order), filtered by Core's rule as typed; one chooser at a time; none
        // over the welcome tour; with no folder to offer, the shortcut's own path says what to do.
        Assert.Contains("FolderChoice.Order(RecentOpenedFolders?.Invoke() ?? [], Execute(_listFolders.Execute))", chooser, StringComparison.Ordinal);
        Assert.Contains("FolderChoice.Filter(folders, box.Text)", chooser, StringComparison.Ordinal);
        Assert.Contains("box.TextChanged += (_, _) => Show();", chooser, StringComparison.Ordinal);
        Assert.Matches(@"if \(WelcomeSurface is not null \|\| _folderChooser is not null\)\s*\{\s*return;", chooser);
        Assert.Matches(@"if \(folders\.Count == 0\)\s*\{\s*NewNoteFromShortcut\(fromClipboard\);", chooser);

        // Named and identified for screen readers and the harness; the count is a live status that announces itself.
        Assert.Contains(@"AutomationProperties.SetName(box, ""Folder for the new note"");", chooser, StringComparison.Ordinal);
        Assert.Contains(@"AutomationProperties.SetAutomationId(box, ""AskFolderBox"");", chooser, StringComparison.Ordinal);
        Assert.Contains(@"AutomationProperties.SetName(list, ""Folders"");", chooser, StringComparison.Ordinal);
        Assert.Contains(@"AutomationProperties.SetAutomationId(list, ""AskFolderList"");", chooser, StringComparison.Ordinal);
        Assert.Contains(@"AutomationProperties.SetAutomationId(status, ""AskFolderStatus"");", chooser, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);", chooser, StringComparison.Ordinal);
        Assert.Contains("Accessible.SetAnnounces(status, true);", chooser, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetName(item, folder.Name);", chooser, StringComparison.Ordinal);
        Assert.Contains("flyout.Opened += (_, _) => _ = box.Focus(FocusState.Programmatic);", chooser, StringComparison.Ordinal);

        // The keyboard stays in the box: Up and Down choose, Enter makes the note, Escape cancels.
        Assert.Matches(@"case VirtualKey\.Down:\s*e\.Handled = Move\(\+1\);", chooser);
        Assert.Matches(@"case VirtualKey\.Up:\s*e\.Handled = Move\(-1\);", chooser);
        Assert.Matches(@"case VirtualKey\.Enter:\s*e\.Handled = true;\s*Choose\(list\.SelectedItem\);", chooser);
        Assert.Matches(@"case VirtualKey\.Escape:\s*e\.Handled = true;\s*flyout\.Hide\(\);", chooser);

        // Only a choice makes a note, once the chooser has closed, through the shortcut's own path (placement, mode,
        // clipboard).
        Assert.Matches(@"if \(chosen is \{ \} folder\)\s*\{\s*_ = DispatcherQueue\.TryEnqueueGuarded\(DispatcherQueuePriority\.Low, \(\) => NewNoteFromShortcut\(fromClipboard, into: folder\)\);", chooser);
        Assert.Single(Regex.Matches(chooser, @"NewNoteFromShortcut\(fromClipboard, into: "));

        // That path enters the chosen folder after leaving Settings, Search and the Recycle Bin, by Back's own way in.
        string shortcut = Method(File.ReadAllText(SourcePath("src", "Noto.Windows", "MainWindow.NewNoteShortcut.cs")), "NewNoteFromShortcut");
        Assert.Matches(@"if \(into is \{ \} chosen && _openFolderId != chosen\)\s*\{\s*OpenFolder\(chosen\);\s*if \(_openFolderId != chosen\)", shortcut);
        Assert.True(shortcut.IndexOf("CloseRecycleBin();", StringComparison.Ordinal) < shortcut.IndexOf("OpenFolder(chosen);", StringComparison.Ordinal));
        Assert.True(shortcut.IndexOf("OpenFolder(chosen);", StringComparison.Ordinal) < shortcut.IndexOf("CreateNoteInOpenFolder(content);", StringComparison.Ordinal));
    }

    [Fact]
    public void Settings_offers_ask_which_folder_in_General_as_a_named_switch()
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement toggle = XDocument.Load(SourcePath("src", "Noto.Windows", "SettingsPage.xaml")).Descendants()
            .Single(e => (string?)e.Attribute(x + "Name") == "AskFolderSetting");
        Assert.Equal("ToggleSwitch", toggle.Name.LocalName);
        Assert.Equal("Ask which folder for a note from a shortcut", (string?)toggle.Attribute("AutomationProperties.Name"));
        Assert.Equal("OnAskFolderToggled", (string?)toggle.Attribute("Toggled"));
        Assert.Contains(toggle.Ancestors(), a => a.Elements().Any(h => (string?)h.Attribute("AutomationProperties.HeadingLevel") == "Level2" && (string?)h.Attribute("Text") == "General"));

        string page = File.ReadAllText(SourcePath("src", "Noto.Windows", "SettingsPage.xaml.cs"));
        Assert.Contains("AskFolderSetting.IsOn = preferences.AskFolder;", page, StringComparison.Ordinal);
        Assert.Contains("Write(p => p.SetAskFolder(AskFolderSetting.IsOn));", page, StringComparison.Ordinal);
    }

    /// <summary>A method's text, from its declaration to its closing brace at the class's indent.</summary>
    private static string Method(string source, string name) =>
        Regex.Match(source, $@"(private|internal|public) [\w?<>,\s]+ {name}\([\s\S]*?\n    }}\n").Value is { Length: > 0 } found
            ? found
            : throw new InvalidOperationException($"Not found: {name}");

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
