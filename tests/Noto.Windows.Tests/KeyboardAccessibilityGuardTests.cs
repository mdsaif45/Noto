using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// Phase B6 (workspace-management.md, "B6 precisely"): the keyboard map has one owner per chord per scope, no automatic
/// key-tip popups, and every action that changes something is announced once.
/// </summary>
public sealed class KeyboardAccessibilityGuardTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly string Windows = Path.GetDirectoryName(SourcePath("src", "Noto.Windows", "App.xaml.cs"))!;
    private static readonly string Main = Code(Path.Join(Windows, "MainWindow.xaml.cs"));
    private static readonly string Bin = Code(Path.Join(Windows, "RecycleBinPage.xaml.cs"));
    private static readonly XDocument MainXaml = XDocument.Load(Path.Join(Windows, "MainWindow.xaml"));

    [Fact]
    public void Every_element_hosting_accelerators_hides_the_automatic_key_tip()
    {
        // F1: WinUI pops a key-tip tooltip ("Ctrl+N") for an accelerator unless its host says Hidden.
        var hosts = MainXaml.Descendants()
            .Where(e => e.Elements().Any(c => c.Name.LocalName.EndsWith(".KeyboardAccelerators", StringComparison.Ordinal)))
            .ToList();

        Assert.Equal(3, hosts.Count);
        Assert.All(hosts, h => Assert.Equal("Hidden", (string?)h.Attribute("KeyboardAcceleratorPlacementMode")));

        // The one accelerator added in code goes onto the root, which is one of those hosts.
        Assert.Contains("WorkspaceRoot.KeyboardAccelerators.Add(openSettings);", Main, StringComparison.Ordinal);
        Assert.Equal("Hidden", (string?)MainXaml.Descendants().Single(e => (string?)e.Attribute(X + "Name") == "WorkspaceRoot").Attribute("KeyboardAcceleratorPlacementMode"));
    }

    [Fact]
    public void No_chord_has_two_owners_in_one_scope()
    {
        foreach (XElement host in MainXaml.Descendants().Where(e => e.Name.LocalName.EndsWith(".KeyboardAccelerators", StringComparison.Ordinal)))
        {
            var chords = host.Elements().Select(a => $"{(string?)a.Attribute("Modifiers")}+{(string?)a.Attribute("Key")}").ToList();
            Assert.Equal(chords.Count, chords.Distinct().Count());
        }

        // Ctrl+N means "new" in both lists' scopes, owned by each scope's own accelerator, never by the root.
        XElement root = MainXaml.Descendants().Single(e => (string?)e.Attribute(X + "Name") == "WorkspaceRoot");
        Assert.DoesNotContain(root.Elements().Where(c => c.Name.LocalName.EndsWith(".KeyboardAccelerators", StringComparison.Ordinal)).Elements(), a => (string?)a.Attribute("Key") == "N");
    }

    [Theory]
    [InlineData("CreateNoteInOpenFolder", "Note created")]
    [InlineData("DeleteCard", "Note moved to the Recycle Bin")]
    [InlineData("MoveCard", "Moved to {target.Name}")]
    [InlineData("PinCard", "pinned ? \"Pinned\" : \"Unpinned\"")]
    [InlineData("FoldCard", "folded ? \"Folded\" : \"Unfolded\"")]
    [InlineData("FoldAll", "All notes folded")]
    [InlineData("ReorderCard", "Moved(direction)")]
    [InlineData("OnNoteDragCompleted", "Not moved: pin it")]
    [InlineData("PinFolder", "pinned ? \"Pinned\" : \"Unpinned\", FolderList")]
    [InlineData("MoveFolder", "Moved(direction), FolderList")]
    [InlineData("OnFolderDragCompleted", "Not moved: pin it")]
    [InlineData("DeleteFolder", "Folder moved to the Recycle Bin")]
    public void Every_action_that_changes_something_is_announced_once(string method, string announcement)
    {
        string body = Body(Main, method);

        Assert.Contains(announcement, body, StringComparison.Ordinal);
        Assert.True(Regex.Count(body, @"\bAnnounce\(") <= 2, $"{method} announces more than its outcome and one refusal");
    }

    [Fact]
    public void The_bins_actions_announce_and_enter_on_an_entry_it_cannot_restore_is_not_silent()
    {
        Assert.Contains("Announce(done);", Body(Bin, "Act"), StringComparison.Ordinal);
        Assert.Contains("Announce(", Body(Bin, "ConfirmEmpty"), StringComparison.Ordinal);

        string keys = Body(Bin, "OnListPreviewKeyDown");
        Assert.Matches(@"else if \(e\.Key == VirtualKey\.Enter\)\s*\{\s*e\.Handled = true;\s*Announce\(""Its folder is in the Recycle Bin\.", keys);
    }

    [Fact]
    public void A_failed_save_is_said_through_an_assertive_live_region_not_an_announcement()
    {
        XElement notice = MainXaml.Descendants().Single(e => (string?)e.Attribute(X + "Name") == "NoteNoticeText");
        Assert.Equal("Assertive", (string?)notice.Attribute("AutomationProperties.LiveSetting"));
        Assert.DoesNotContain("Announce(", Body(Main, "TrySaveCard"), StringComparison.Ordinal);
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
