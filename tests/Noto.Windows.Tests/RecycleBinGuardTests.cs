using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// The Recycle Bin surface and folder deletion (Phase B5), checked in source as the other wiring guards are. What they
/// do at run time is Invoke-NoteStack.ps1's; the rules are RecycleBin's and P1.1's, tested against real SQLite.
/// </summary>
public sealed class RecycleBinGuardTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly string Windows = Path.GetDirectoryName(SourcePath("src", "Noto.Windows", "App.xaml.cs"))!;
    private static readonly string Main = Code(Path.Join(Windows, "MainWindow.xaml.cs"));
    private static readonly string Page = Code(Path.Join(Windows, "RecycleBinPage.xaml.cs"));
    private static readonly XDocument PageXaml = XDocument.Load(Path.Join(Windows, "RecycleBinPage.xaml"));
    private static readonly XDocument MainXaml = XDocument.Load(Path.Join(Windows, "MainWindow.xaml"));

    [Fact]
    public void The_bin_is_built_only_while_open_from_the_folder_list_and_released_when_left()
    {
        string open = Body(Main, "OpenRecycleBin");
        Assert.Contains("PaneRoot.Visibility != Visibility.Visible", open, StringComparison.Ordinal);
        Assert.Equal(1, Regex.Count(Main, @"new RecycleBinPage\(\)"));
        Assert.DoesNotContain(MainXaml.Descendants(), e => e.Name.LocalName == "RecycleBinPage");

        string close = Body(Main, "CloseRecycleBin");
        Assert.Matches(@"\(\(Panel\)page\.Parent\)\.Children\.Remove\(page\);\s*page\.KeyDown -= OnRecycleBinKeyDown;\s*page\.BackRequested -= OnRecycleBinBackRequested;\s*RecycleBinSurface = null;", close);
        Assert.Equal(2, Regex.Count(Main, @"RecycleBinSurface = "));
    }

    [Fact]
    public void Escape_in_the_bin_follows_the_stored_behaviour_and_settings_returns_to_it()
    {
        Assert.Contains("e.Handled = OnEscape(WorkspaceSurface.RecycleBin);", Body(Main, "OnRecycleBinKeyDown"), StringComparison.Ordinal);
        Assert.Matches(@"case EscapeAction\.LeaveRecycleBin:\s*CloseRecycleBin\(\);\s*return true;", Main);
        Assert.Matches(@"UIElement from = WelcomeSurface is \{ \} welcome \? welcome\s*: RecycleBinSurface is \{ \} bin \? bin\s*:", Body(Main, "OpenSettings"));
    }

    [Fact]
    public void A_folder_is_deleted_only_after_its_confirmation_and_cancel_writes_nothing()
    {
        string confirm = Body(Main, "ConfirmDeleteFolder");

        // Only the Delete button's click deletes; Cancel only closes; Cancel has focus when it opens.
        Assert.Matches(@"cancel\.Click \+= \(_, _\) => flyout\.Hide\(\);", confirm);
        Assert.Matches(@"delete\.Click \+= \(_, _\) =>\s*\{\s*flyout\.Hide\(\);\s*DeleteFolder\(id\);\s*\};", confirm);
        Assert.Contains("flyout.Opened += (_, _) => _ = cancel.Focus(FocusState.Programmatic);", confirm, StringComparison.Ordinal);
        Assert.Equal(1, Regex.Count(Main, @"\bDeleteFolder\(id\);"));
        Assert.Contains("Bin.DeleteFolder(FolderId.From(id))", Body(Main, "DeleteFolder"), StringComparison.Ordinal);

        // From the folder list only, never from the stack (where a card may hold unsaved text).
        Assert.Contains("_state != PaneState.Loaded", confirm, StringComparison.Ordinal);
        Assert.DoesNotContain("ConfirmDeleteFolder", Body(Main, "OnNoteListContextRequested"), StringComparison.Ordinal);
    }

    [Fact]
    public void What_cannot_be_undone_asks_first_with_cancel_as_the_default()
    {
        string confirm = Body(Page, "Confirm");
        Assert.Contains("DefaultButton = ContentDialogButton.Close,", confirm, StringComparison.Ordinal);
        Assert.Contains("CloseButtonText = \"Cancel\",", confirm, StringComparison.Ordinal);
        Assert.Contains("return await dialog.ShowAsync() == ContentDialogResult.Primary;", confirm, StringComparison.Ordinal);

        Assert.Matches(@"if \(await Confirm\([^\n]*\)\)\s*\{\s*Act\(item, \(\) => _bin\.Purge\(item\.Entry\)", Body(Page, "ConfirmPurge"));
        Assert.Matches(@"if \(!await Confirm\([^\n]*\)\)\s*\{\s*return;\s*\}", Body(Page, "ConfirmEmpty"));
        Assert.Equal(1, Regex.Count(Page, @"_bin\.Purge\("));
        Assert.Equal(1, Regex.Count(Page, @"_bin\.Empty\("));

        // Restore never asks.
        Assert.DoesNotContain("Confirm(", Body(Page, "Restore"), StringComparison.Ordinal);
    }

    [Fact]
    public void Restore_is_offered_only_where_the_engine_allows_it()
    {
        Assert.Contains("if (item.Entry.CanRestore)", Body(Page, "OnListContextRequested"), StringComparison.Ordinal);
        Assert.Contains("e.Key == VirtualKey.Enter && item.Entry.CanRestore", Body(Page, "OnListPreviewKeyDown"), StringComparison.Ordinal);
        Assert.Contains("BinRestoreButton.IsEnabled = Selected is { Entry.CanRestore: true };", Page, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_bin_control_is_named_and_each_entry_carries_its_state_on_its_container()
    {
        Assert.Equal("RecycleBinRoot", (string?)PageXaml.Root!.Attribute("AutomationProperties.AutomationId"));
        foreach (string name in new[] { "BinBackButton", "RecycleBinList", "BinRestoreButton", "BinPurgeButton", "EmptyBinButton" })
        {
            XElement element = PageXaml.Descendants().Single(e => (string?)e.Attribute(X + "Name") == name);
            Assert.False(string.IsNullOrEmpty((string?)element.Attribute("AutomationProperties.Name")), name);
        }

        Assert.Contains("AutomationProperties.SetHelpText(args.ItemContainer, item.Detail);", Page, StringComparison.Ordinal);
        Assert.Equal("Assertive", (string?)PageXaml.Descendants().Single(e => (string?)e.Attribute(X + "Name") == "BinNoticeText").Attribute("AutomationProperties.LiveSetting"));
        Assert.Equal("Recycle Bin", (string?)MainXaml.Descendants().Single(e => (string?)e.Attribute(X + "Name") == "RecycleBinButton").Attribute("AutomationProperties.Name"));
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
