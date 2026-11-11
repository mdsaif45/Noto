using System.Text.RegularExpressions;
using Noto.Core.Settings;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// F17 (#156) and F18 (#157) in the source: the new-note shortcuts are off by default, only ever show Noto, and make the
/// note through the window's own Ctrl+N path, the clipboard's through the editor's own paste. What a real press does is
/// Invoke-Settings.ps1's cases 17 and 18.
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
        Assert.Contains("_main?.NewNoteFromShortcut(fromClipboard: action == GlobalAction.NoteFromClipboard);", newNote, StringComparison.Ordinal);

        // The recorder's suspension is checked first, for every action.
        Assert.True(pressed.IndexOf("_hotkeySuspended", StringComparison.Ordinal) < pressed.IndexOf("GlobalAction.NewNote", StringComparison.Ordinal));

        string window = File.ReadAllText(SourcePath("src", "Noto.Windows", "MainWindow.NewNoteShortcut.cs"));
        Assert.Matches(@"DispatcherQueuePriority\.Low, \(\) =>\s*\{\s*string\? before = _stack\.Editing is \{ \} editing \? editing\.Value : null;\s*CreateNoteInOpenFolder\(\);", window);
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
        Assert.Contains("_main?.NewNoteFromShortcut(fromClipboard: true);", tray, StringComparison.Ordinal);
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
