using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// F9 (#152) in the source: files dropped on a folder or the stack go through the importer, the editor keeps its own
/// drops, and a drop's one Undo moves its notes to the Recycle Bin. What a real drop does is Invoke-DropImport.ps1's.
/// </summary>
public sealed class DropImportGuardTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly string Drop = File.ReadAllText(SourcePath("src", "Noto.Windows", "MainWindow.Drop.cs"));
    private static readonly XDocument Window = XDocument.Load(SourcePath("src", "Noto.Windows", "MainWindow.xaml"));

    [Fact]
    public void Both_lists_take_files_even_while_their_own_reorder_handles_the_drag_and_the_editor_keeps_its_drops()
    {
        foreach (string list in new[] { "NoteList", "FolderList" })
        {
            foreach (string routed in new[] { "DragEnterEvent", "DragOverEvent", "DropEvent" })
            {
                Assert.Matches($@"{list}\.AddHandler\(UIElement\.{routed}, new DragEventHandler\(\w+\), handledEventsToo: true\);", Drop);
            }
        }

        Assert.Contains("!InEditor(e.OriginalSource)", Drop, StringComparison.Ordinal);
        Assert.Contains("e.AcceptedOperation = DataPackageOperation.Copy;", Drop, StringComparison.Ordinal);
    }

    [Fact]
    public void A_drop_is_imported_by_the_importer_and_its_one_undo_bins_its_notes()
    {
        string portability = File.ReadAllText(SourcePath("src", "Noto.Windows", "DataPortability.cs"));
        Assert.Contains("Run(() => MarkdownImporter.ImportFiles(database, images, files, folder, TimeProvider.System));", portability, StringComparison.Ordinal);

        string undo = Regex.Match(Drop, @"private void OnUndoImportClick\([\s\S]*?\n    }\n").Value;
        Assert.Contains("_commands.Dispatch(new DeleteNote(note))", undo, StringComparison.Ordinal);

        foreach (string button in new[] { "UndoImportButton", "NoteUndoImportButton" })
        {
            XElement element = Window.Descendants().Single(e => (string?)e.Attribute(X + "Name") == button);
            Assert.Equal("Undo the import", (string?)element.Attribute("AutomationProperties.Name"));
            Assert.Equal("Collapsed", (string?)element.Attribute("Visibility"));
        }

        // Anything else said on the line takes the Undo away, as leaving or entering a folder does.
        string status = File.ReadAllText(SourcePath("src", "Noto.Windows", "MainWindow.Portability.cs"));
        Assert.Equal(2, Regex.Count(status, @"Status\(string message\)\s*\{\s*ForgetUndo\(\);"));
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
