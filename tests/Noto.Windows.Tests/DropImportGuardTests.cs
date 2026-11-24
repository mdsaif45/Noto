using System.Text.RegularExpressions;
using System.Xml.Linq;
using Noto.Platform.Windows;
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

    [Fact]
    public void Imported_files_go_to_the_windows_recycle_bin_only_after_the_stored_import_and_only_when_the_setting_is_on()
    {
        // #267 (J29): both drops recycle what the import stored, once it has returned (its write committed), before the
        // result is said, so the status can count them; the Open Bar's files have no copies of Noto's own.
        foreach (string import in new[] { "ImportOnStack", "ImportOnRow" })
        {
            string body = Regex.Match(Drop, $@"private async Task {import}\([\s\S]*?\n    }}\n").Value;
            int stored = body.IndexOf("await Import(files, ", StringComparison.Ordinal);
            int recycled = body.IndexOf("(int Recycled, int Kept)? moved = RecycleImported(import, copies);", StringComparison.Ordinal);
            int said = body.IndexOf("Said(import, ", StringComparison.Ordinal);
            Assert.True(stored > 0 && stored < recycled && recycled < said, $"{import}: stored {stored}, recycled {recycled}, said {said}");
            Assert.Contains(", moved, ", body[said..], StringComparison.Ordinal);
        }

        Assert.Contains("await ImportOnStack(dropped.Paths, folder, dropped.Copies);", Drop, StringComparison.Ordinal);
        Assert.Contains("await ImportOnRow(dropped.Paths, row, dropped.Copies);", Drop, StringComparison.Ordinal);

        // Off, nothing is touched; on, only the stored notes' sources, Noto's own copies left out, and the trace counts.
        string recycle = Regex.Match(Drop, @"private \(int Recycled, int Kept\)\? RecycleImported\([\s\S]*?\n    }\n").Value;
        Assert.Matches(@"\{\s*if \(!RecyclesImportedFiles \|\| ImportedFiles\.ToRecycle\(import\.Sources, copies\) is not \{ Count: > 0 \} files\)\s*\{\s*return null;", recycle);
        Assert.Contains("ShellRecycleBin.Recycle(files, ", recycle, StringComparison.Ordinal);
        Assert.Contains("Trace.WriteLine($\"Noto.Import recycled={moved.Recycled} kept={moved.Kept}\");", recycle, StringComparison.Ordinal);
        Assert.Equal(1, Regex.Count(recycle, @"Trace\."));

        // The drop is the only caller: never a picture dropped into a note, the data import, a paste, or Undo.
        string windows = Path.GetDirectoryName(SourcePath("src", "Noto.Windows", "MainWindow.Drop.cs"))!;
        var callers = Directory.EnumerateFiles(windows, "*.cs").Where(f => File.ReadAllText(f).Contains("ShellRecycleBin", StringComparison.Ordinal)).Select(Path.GetFileName);
        Assert.Equal(["MainWindow.Drop.cs"], callers);
        Assert.DoesNotContain("ShellRecycleBin", Regex.Match(Drop, @"private void OnUndoImportClick\([\s\S]*?\n    }\n").Value, StringComparison.Ordinal);

        // The importer gives each stored note's source, only from the files it read into notes.
        string importer = File.ReadAllText(SourcePath("src", "Noto.Infrastructure", "Portability", "MarkdownImporter.cs"));
        Assert.Equal(2, Regex.Count(importer, @"sources\.Add\(file\);"));
        Assert.Contains("SaveFirst(database, folder, notes, time.GetUtcNow()), copied.Count, skipped, imageIssues, sources);", importer, StringComparison.Ordinal);
    }

    [Fact]
    public void The_recycle_checks_each_files_drive_type_and_asks_the_shell_to_recycle_never_to_delete()
    {
        // #267: where there is no Recycle Bin the shell deletes outright, silently, so every file is checked first: the
        // drive's type through DriveInfo, judged by Core's rule (a local fixed drive only), before the shell is called.
        string bin = File.ReadAllText(SourcePath("src", "Noto.Platform.Windows", "ShellRecycleBin.cs"));
        Assert.Contains("new DriveInfo(root).DriveType", bin, StringComparison.Ordinal);
        Assert.Contains("ImportedFiles.MayRecycle(file, AttributesOf(file), DriveOf(file)) && ToBin(file, owner)", bin, StringComparison.Ordinal);
        Assert.Contains("fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI | FOF_WANTNUKEWARNING,", bin, StringComparison.Ordinal);
        Assert.Contains("wFunc = FO_DELETE,", bin, StringComparison.Ordinal);
        Assert.Contains("&& !Path.Exists(file);", bin, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"File\.Delete|Directory\.Delete|Trace\.|Log", bin);

        string rule = File.ReadAllText(SourcePath("src", "Noto.Core", "Portability", "ImportedFiles.cs"));
        Assert.Contains("&& drive == DriveType.Fixed", rule, StringComparison.Ordinal);
    }

    [Fact]
    public void A_read_only_file_a_folder_and_a_missing_file_are_kept_where_they_are_and_never_given_to_the_shell()
    {
        string folder = Directory.CreateTempSubdirectory("noto-recycle-").FullName;
        string locked = Path.Join(folder, "read-only.md");
        File.WriteAllText(locked, "kept");
        File.SetAttributes(locked, FileAttributes.ReadOnly);
        try
        {
            (int recycled, int kept) = ShellRecycleBin.Recycle([locked, folder, Path.Join(folder, "gone.md")], owner: null);

            Assert.Equal((0, 3), (recycled, kept));
            Assert.True(File.Exists(locked));
            Assert.True(Directory.Exists(folder));
        }
        finally
        {
            File.SetAttributes(locked, FileAttributes.Normal);
            Directory.Delete(folder, recursive: true);
        }
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
