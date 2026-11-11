using System.Text.RegularExpressions;
using Noto.Core.Settings;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// F17 (#156) in the source: the new-note shortcut is off by default, only ever shows Noto, and makes the note through
/// the window's own Ctrl+N path. What a real press does is Invoke-Settings.ps1's case 17.
/// </summary>
public sealed class NewNoteShortcutGuardTests
{
    private static readonly string App = File.ReadAllText(SourcePath("src", "Noto.Windows", "App.xaml.cs"));

    [Fact]
    public void It_is_off_by_default_with_the_planned_chord()
    {
        Assert.False(SettingKeys.NewNoteHotkeyEnabled.Default);
        Assert.Equal("Ctrl+Shift+Win+N", SettingKeys.NewNoteHotkeyBinding.Default);
    }

    [Fact]
    public void A_press_shows_Noto_never_hides_it_and_the_window_makes_the_note_as_CtrlN_does()
    {
        string pressed = Regex.Match(App, @"private void OnHotkeyPressed\(GlobalAction action, int messageTime\)[\s\S]*?\n    }\n").Value;
        string newNote = Regex.Match(pressed, @"if \(action == GlobalAction\.NewNote\)[\s\S]*?return;\s*\}").Value;

        Assert.Contains("_coordinator.OnActivationRequested(WorkspaceRequest.Launch, messageTime)", newNote, StringComparison.Ordinal);
        Assert.DoesNotContain("WorkspaceRequest.Toggle", newNote, StringComparison.Ordinal);
        Assert.Contains("_main?.NewNoteFromShortcut();", newNote, StringComparison.Ordinal);

        // The recorder's suspension is checked first, for every action.
        Assert.True(pressed.IndexOf("_hotkeySuspended", StringComparison.Ordinal) < pressed.IndexOf("GlobalAction.NewNote", StringComparison.Ordinal));

        string window = File.ReadAllText(SourcePath("src", "Noto.Windows", "MainWindow.NewNoteShortcut.cs"));
        Assert.Contains("DispatcherQueuePriority.Low, CreateNoteInOpenFolder);", window, StringComparison.Ordinal);
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
