using System.Xml.Linq;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// #161, measured with Narrator: every live region is announced when its text changes, and a row's state is its item
/// status as well as its help text. What Narrator says is Invoke-Narrator.ps1's to show.
/// </summary>
public sealed class AccessibleGuardTests
{
    private static readonly XNamespace Local = "using:Noto";

    [Fact]
    public void Every_live_text_is_announced_when_it_changes()
    {
        string windows = Path.GetDirectoryName(SourcePath("src", "Noto.Windows", "MainWindow.xaml"))!;
        var silent = Directory.EnumerateFiles(windows, "*.xaml")
            .SelectMany(f => XDocument.Load(f).Descendants().Select(e => (File: Path.GetFileName(f), Element: e)))
            .Where(x => x.Element.Name.LocalName == "TextBlock" && x.Element.Attribute("AutomationProperties.LiveSetting") is not null)
            .Where(x => (string?)x.Element.Attribute(Local + "Accessible.Announces") != "True")
            .Select(x => $"{x.File}: {(string?)x.Element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))}")
            .ToList();

        Assert.True(silent.Count == 0, "Live text never announced: " + string.Join(", ", silent));
    }

    [Fact]
    public void A_rows_state_is_set_as_its_item_status_too()
    {
        string accessible = File.ReadAllText(SourcePath("src", "Noto.Windows", "Accessible.cs"));
        Assert.Contains("AutomationProperties.SetItemStatus(row, state);", accessible, StringComparison.Ordinal);
        Assert.Contains("RaiseAutomationEvent(AutomationEvents.LiveRegionChanged)", accessible, StringComparison.Ordinal);
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
