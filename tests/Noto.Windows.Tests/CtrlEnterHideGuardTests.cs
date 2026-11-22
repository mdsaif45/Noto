using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// Ctrl+Enter hides Noto (#255; parity A11, G13), checked in source as the other surfaces' keys are. At run time,
/// Invoke-Settings.ps1 case 24.
/// </summary>
public sealed class CtrlEnterHideGuardTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly string Windows = Path.GetDirectoryName(SourcePath("src", "Noto.Windows", "App.xaml.cs"))!;
    private static readonly string Main = Code(Path.Join(Windows, "MainWindow.xaml.cs"));
    private static readonly string Host = Code(Path.Join(Windows, "NoteEditorHost.xaml.cs"));
    private static readonly string Search = Code(Path.Join(Windows, "SearchPage.xaml.cs"));
    private static readonly string Settings = Code(Path.Join(Windows, "SettingsPage.xaml.cs"));
    private static readonly XDocument MainXaml = XDocument.Load(Path.Join(Windows, "MainWindow.xaml"));

    [Fact]
    public void The_root_takes_Ctrl_Enter_on_the_way_down_only_when_on_and_only_exactly_Ctrl()
    {
        // Preview, on the root: before the stack edits a card on Enter and before any control's own KeyDown.
        XElement root = MainXaml.Descendants().Single(e => (string?)e.Attribute(X + "Name") == "WorkspaceRoot");
        Assert.Equal("OnWorkspacePreviewKeyDown", (string?)root.Attribute("PreviewKeyDown"));

        // Off, or any other key or modifiers, returns before anything: today's behaviour. Ctrl+Alt is AltGr.
        string keys = Body(Main, "OnWorkspacePreviewKeyDown");
        Assert.Matches(@"^\{\s*if \(!CtrlEnterHides \|\| e\.Key != VirtualKey\.Enter \|\| !IsControlDown\(\) \|\| IsAltDown\(\) \|\| IsShiftDown\(\)\)\s*\{\s*return;\s*\}", keys);
        Assert.Contains("internal bool CtrlEnterHides { get; set; }", Main, StringComparison.Ordinal);
    }

    [Fact]
    public void It_hides_through_the_one_dismissal_unless_a_control_owns_Ctrl_Enter()
    {
        string keys = Body(Main, "OnWorkspacePreviewKeyDown");

        // The owners keep it: a link at the editor's caret, a selected search result, a listening recorder, the name boxes.
        Assert.Contains("_editor?.KeepsCtrlEnter() == true", keys, StringComparison.Ordinal);
        Assert.Contains("SearchSurface?.KeepsCtrlEnter == true", keys, StringComparison.Ordinal);
        Assert.Contains("SettingsSurface?.IsRecording == true", keys, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(e.OriginalSource, FolderNameInput)", keys, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(e.OriginalSource, RenameEditor)", keys, StringComparison.Ordinal);

        // Otherwise the same dismissal as Ctrl+W, which the coordinator runs after saving the note being edited.
        Assert.Matches(@"if \(!owned\)\s*\{\s*e\.Handled = true;\s*DismissRequested\?\.Invoke\(this, EventArgs\.Empty\);\s*\}", keys);
        Assert.Contains("DismissRequested?.Invoke(this, EventArgs.Empty);", Body(Main, "OnDismissAccelerator"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_editor_keeps_Ctrl_Enter_only_with_focus_and_the_caret_on_a_link()
    {
        string keeps = Body(Host, "KeepsCtrlEnter");
        Assert.Matches(@"if \(NoteEditor\.FocusState == FocusState\.Unfocused\)\s*\{\s*return false;\s*\}\s*Flush\(\);\s*return LinkTarget\.At\(_document, _selection\.Active\) is not null;", keeps);

        // Search keeps it while a result is selected (its Ctrl+Enter, #197); the recorder while it listens.
        Assert.Contains("internal bool KeepsCtrlEnter => Selected is not null;", Search, StringComparison.Ordinal);
        Assert.Contains("internal bool IsRecording => _recording;", Settings, StringComparison.Ordinal);
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
