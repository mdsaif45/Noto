using System.Text;
using Noto.Core.Portability;
using Noto.Infrastructure.Portability;
using Xunit;

namespace Noto.Infrastructure.Tests.Portability;

/// <summary>
/// Export and import on real databases and files (ADR-020 §4, §5): the Markdown written is each note's source byte for
/// byte, in a fixed shape with fixed names; importing it adds exactly that back and never touches what is there.
/// </summary>
public sealed class ExportImportTests : IDisposable
{
    private readonly Workspace _source = new();
    private readonly Workspace _target = new();

    public void Dispose()
    {
        _source.Dispose();
        _target.Dispose();
    }

    [Fact]
    public void Export_writes_each_note_byte_for_byte_with_its_images_beside_it()
    {
        string image = BackupRestoreTests.Seed(_source);
        ExportSummary summary = Export(_source, new ExportScope.All())!;

        Assert.Equal((3, 6, 1, 0), (summary.Folders, summary.Notes, summary.Images, summary.MissingImages));
        string root = summary.Directory;
        Assert.Equal(["Empty", "Hôme ✓", "Work"], Directory.EnumerateDirectories(root).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal("# Weekly sync\r\n\r\n- [ ] Tom\r\n- [x] room\r\n", File.ReadAllText(Path.Join(root, "Work", "Weekly sync.md")));
        Assert.Equal(Encoding.UTF8.GetBytes($"Trip\n![harbour]({image})\nÜberraschung 🎉"), File.ReadAllBytes(Path.Join(root, "Work", "Trip.md")));
        Assert.Equal(File.ReadAllBytes(_source.Images.PathOf(image)!), File.ReadAllBytes(Path.Join(root, "Work", image)));
        Assert.True(File.Exists(Path.Join(root, "Hôme ✓", "日本語のノート.md")));
        Assert.True(File.Exists(Path.Join(root, "Hôme ✓", "Untitled.md")));
        Assert.DoesNotContain(Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories), f => File.ReadAllText(f) == "binned");

        ExportIndex index = ExportIndex.Read(File.ReadAllText(Path.Join(root, ExportIndex.FileName)))!;
        Assert.Equal(["Work", "Hôme ✓", "Empty"], index.Folders.Select(f => f.Name));
        Assert.Equal([("Weekly sync.md", true, false), ("Trip.md", false, true)], index.Folders[0].Notes.Select(n => (n.File, n.Pinned, n.Folded)));
    }

    [Fact]
    public void Names_that_collide_or_that_Windows_forbids_are_made_unique_and_safe_in_order()
    {
        var a = _source.Folder("Plans", order: 1);
        var b = _source.Folder("plans", order: 2);
        _source.Folder("CON", order: 3);
        _source.Note(a, "Same\nfirst", order: 1);
        _source.Note(a, "Same\nsecond", order: 2);
        _source.Note(a, "# What? <now>: a/b\\c", order: 3);
        _source.Note(b, "aux.txt", order: 1);

        string root = Export(_source, new ExportScope.All())!.Directory;

        Assert.Equal(["CON_", "Plans", "plans (2)"], Directory.EnumerateDirectories(root).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal("Same\nfirst", File.ReadAllText(Path.Join(root, "Plans", "Same.md")));
        Assert.Equal("Same\nsecond", File.ReadAllText(Path.Join(root, "Plans", "Same (2).md")));
        Assert.True(File.Exists(Path.Join(root, "Plans", "What_ _now__ a_b_c.md")));
        Assert.True(File.Exists(Path.Join(root, "plans (2)", "aux.txt_.md")));
    }

    [Fact]
    public void The_same_workspace_always_exports_the_same_files()
    {
        BackupRestoreTests.Seed(_source);

        string first = Export(_source, new ExportScope.All())!.Directory;
        string second = Export(_source, new ExportScope.All())!.Directory;

        Assert.Equal(Tree(first), Tree(second));
        Assert.EndsWith(" (2)", second, StringComparison.Ordinal);
    }

    [Fact]
    public void A_folder_or_a_note_exports_alone_and_nothing_to_export_writes_nothing()
    {
        var work = _source.Folder("Work");
        var empty = _source.Folder("Empty");
        var other = _source.Folder("Other");
        var note = _source.Note(work, "One\ntext");
        _source.Note(work, "Two");
        _source.Note(other, "Three");
        string parent = _source.Scratch("exports");

        ExportSummary folder = MarkdownExporter.Export(_source.Database, _source.Paths.AttachmentsDirectory, new ExportScope.Folder(work.Value), parent, Workspace.Time)!;
        ExportSummary single = MarkdownExporter.Export(_source.Database, _source.Paths.AttachmentsDirectory, new ExportScope.Note(note.Value), parent, Workspace.Time)!;
        ExportSummary? none = MarkdownExporter.Export(_source.Database, _source.Paths.AttachmentsDirectory, new ExportScope.Folder(empty.Value), parent, Workspace.Time);

        Assert.Equal(["Work/One.md", "Work/Two.md"], Tree(folder.Directory).Keys.Where(k => k.EndsWith(".md", StringComparison.Ordinal)));
        Assert.Equal(["Work/One.md"], Tree(single.Directory).Keys.Where(k => k.EndsWith(".md", StringComparison.Ordinal)));
        Assert.Null(none);
        Assert.Equal(2, Directory.EnumerateDirectories(parent).Count());
    }

    [Fact]
    public void A_missing_image_is_counted_and_the_note_still_exports()
    {
        string image = _source.Image(0x01);
        var work = _source.Folder("Work");
        _source.Note(work, $"![gone]({image})");
        _source.Note(work, $"again ![gone]({image}) ![twice]({image})");
        File.Delete(_source.Images.PathOf(image)!);

        ExportSummary summary = Export(_source, new ExportScope.All())!;

        // One missing picture, however many notes and references name it.
        Assert.Equal((2, 0, 1), (summary.Notes, summary.Images, summary.MissingImages));
    }

    [Fact]
    public void A_cancelled_export_leaves_nothing()
    {
        BackupRestoreTests.Seed(_source);
        string parent = _source.Scratch("cancelled");

        Assert.Throws<OperationCanceledException>(() =>
            MarkdownExporter.Export(_source.Database, _source.Paths.AttachmentsDirectory, new ExportScope.All(), parent, Workspace.Time, cancellation: new CancellationToken(true)));
        Assert.Empty(Directory.EnumerateFileSystemEntries(parent));
    }

    [Fact]
    public void An_export_imports_back_with_its_text_order_state_and_images_unchanged()
    {
        string image = BackupRestoreTests.Seed(_source);
        string root = Export(_source, new ExportScope.All())!.Directory;

        ImportReport report = MarkdownImporter.Import(_target.Database, _target.Images, root, Workspace.Time);

        Assert.Equal((3, 6, 1), (report.Folders, report.Notes, report.Images));
        Assert.Empty(report.Skipped);
        Assert.Empty(report.ImageIssues);
        Assert.Equal(Notes(_source), Notes(_target));
        Assert.Equal(File.ReadAllBytes(_source.Images.PathOf(image)!), File.ReadAllBytes(_target.Images.PathOf(image)!));
        Assert.Equal(1, _target.Found("Überraschung"));
    }

    [Fact]
    public void A_backup_restored_exports_what_the_original_exported()
    {
        BackupRestoreTests.Seed(_source);
        string archive = Path.Join(_source.Scratch("backup"), "b.notobackup");
        BackupWriter.Create(_source.Database, _source.Paths.AttachmentsDirectory, archive, "test", Workspace.Time);
        (BackupManifest? manifest, _) = PendingRestore.Stage(_target.Paths, archive);
        PendingRestore.Commit(_target.Paths, manifest!);
        _target.Close();
        PendingRestore.ApplyIfPending(_target.Paths, Workspace.Time);
        _target.Reopen();

        Assert.Equal(Tree(Export(_source, new ExportScope.All())!.Directory), Tree(Export(_target, new ExportScope.All())!.Directory));
    }

    [Fact]
    public void Import_adds_beside_what_is_there_and_never_changes_it()
    {
        BackupRestoreTests.Seed(_source);
        string root = Export(_source, new ExportScope.All())!.Directory;
        string before = _source.Dump();

        ImportReport report = MarkdownImporter.Import(_source.Database, _source.Images, root, Workspace.Time);

        // Every note is new; the folders' names are taken, so each gains a number; the images already there are the same
        // bytes, so they are reused under their names and the text is unchanged.
        Assert.Equal((3, 6, 0), (report.Folders, report.Notes, report.Images));
        Assert.Equal(["Empty", "Empty (2)", "Hôme ✓", "Hôme ✓ (2)", "Work", "Work (2)"], Names(_source));
        Assert.StartsWith(before.Split("\n")[0], _source.Dump(), StringComparison.Ordinal);
        Assert.Equal(12, _source.Count("SELECT COUNT(*) FROM Notes WHERE DeletedAt IS NULL;"));
        Assert.Equal(2, _source.Count("SELECT COUNT(*) FROM Notes WHERE Content LIKE 'Trip%';"));
        Assert.All(OriginalRows(before), row => Assert.Contains(row, _source.Dump(), StringComparison.Ordinal));
    }

    [Fact]
    public void Plain_markdown_imports_by_directory_with_nested_ones_named_by_their_path()
    {
        string root = Path.Join(_target.Scratch("plain"), "Journal");
        Write(root, "top.md", "at the top");
        Write(root, Path.Join("Work", "b.md"), "b");
        Write(root, Path.Join("Work", "a.markdown"), "a");
        Write(root, Path.Join("Work", "2026", "deep.txt"), "deep");
        Write(root, Path.Join("Work", "ignored.docx"), "not a note");

        ImportReport report = MarkdownImporter.Import(_target.Database, _target.Images, root, Workspace.Time);

        Assert.Equal((3, 4), (report.Folders, report.Notes));
        Assert.Equal(["Journal", "Work", "Work / 2026"], Names(_target));
        Assert.Equal(["a", "b"], Contents(_target, "Work"));
    }

    [Fact]
    public void Images_inside_the_source_are_copied_in_and_anything_else_is_left_as_written_and_reported()
    {
        string root = _target.Scratch("pictures");
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
        Directory.CreateDirectory(Path.Join(root, "Work", "img"));
        File.WriteAllBytes(Path.Join(root, "Work", "img", "My photo.png"), png);
        File.WriteAllBytes(Path.Join(root, "Work", "img", "fake.png"), [0x4D, 0x5A, 0, 0]);
        File.WriteAllBytes(Path.Join(Path.GetDirectoryName(root)!, "outside.png"), png);
        Write(root, Path.Join("Work", "note.md"),
            "![a](img/My%20photo.png)\r\n![again](img/My%20photo.png)\n![web](https://example.com/x.png)\n![none](img/missing.png)\n![up](../../outside.png)\n![fake](img/fake.png)");

        ImportReport report = MarkdownImporter.Import(_target.Database, _target.Images, root, Workspace.Time);

        string content = Assert.Single(Contents(_target, "Work"));
        string reference = ImageSources.In(content)[0].Source;
        Assert.Matches(@"^attachments/[0-9A-HJKMNP-TV-Z]{26}\.png$", reference);
        Assert.Equal($"![a]({reference})\r\n![again]({reference})\n![web](https://example.com/x.png)\n![none](img/missing.png)\n![up](../../outside.png)\n![fake](img/fake.png)", content);
        Assert.Equal(png, File.ReadAllBytes(_target.Images.PathOf(reference)!));
        Assert.Equal(1, report.Images);
        Assert.Equal(
            [(ImportIssueKind.ImageMissing, "img/missing.png"), (ImportIssueKind.ImageOutsideSource, "../../outside.png"), (ImportIssueKind.ImageNotAnImage, "img/fake.png")],
            report.ImageIssues.Select(i => (i.Kind, i.Source!)));
    }

    [Fact]
    public void A_picture_that_is_missing_is_reported_once_however_many_notes_refer_to_it()
    {
        string root = _target.Scratch("once");
        Write(root, "a.md", "![x](img/gone.png)");
        Write(root, "b.md", "![x](img/gone.png) and ![x again](./img/gone.png)");

        ImportReport report = MarkdownImporter.Import(_target.Database, _target.Images, root, Workspace.Time);

        Assert.Equal([(ImportIssueKind.ImageMissing, "img/gone.png")], report.ImageIssues.Select(i => (i.Kind, i.Source!)));
    }

    [Fact]
    public void Text_is_kept_exactly_and_files_that_are_not_text_are_skipped_and_reported()
    {
        string root = _target.Scratch("text");
        Write(root, "crlf.md", "line one\r\nline two\r\n");
        File.WriteAllBytes(Path.Join(root, "bom.md"), [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("with a BOM ✓")]);
        File.WriteAllBytes(Path.Join(root, "empty.md"), []);
        File.WriteAllBytes(Path.Join(root, "latin1.md"), [0x63, 0x61, 0x66, 0xE9]);

        ImportReport report = MarkdownImporter.Import(_target.Database, _target.Images, root, Workspace.Time);

        Assert.Equal(3, report.Notes);
        Assert.Equal(["", "line one\r\nline two\r\n", "with a BOM ✓"], Contents(_target, Path.GetFileName(root)).Order(StringComparer.Ordinal));
        Assert.Equal([("latin1.md", ImportIssueKind.NotText)], report.Skipped.Select(s => (s.File, s.Kind)));

        // The byte order mark is not stored: what is read back cannot show it (SQLite drops a leading one when it
        // converts the text), but an export writes the stored bytes.
        Assert.Equal(0, _target.Count("SELECT COUNT(*) FROM Notes WHERE hex(Content) LIKE 'EFBBBF%';"));
    }

    [Fact]
    public void Files_in_an_attachments_folder_are_pictures_never_notes()
    {
        string root = _target.Scratch("attached");
        Write(root, "note.md", "a note");
        Write(root, Path.Join("attachments", "readme.md"), "beside the pictures");
        Write(root, Path.Join("Work", "attachments", "list.txt"), "beside the pictures too");

        ImportReport report = MarkdownImporter.Import(_target.Database, _target.Images, root, Workspace.Time);

        Assert.Equal(1, report.Notes);
        Assert.Equal(["a note"], Contents(_target, Path.GetFileName(root)));
    }

    [Fact]
    public void A_note_larger_than_the_limit_is_skipped()
    {
        string root = _target.Scratch("large");
        Write(root, "small.md", "small");
        using (var large = File.Create(Path.Join(root, "large.md")))
        {
            large.SetLength(MarkdownImporter.MaxNoteBytes + 1);
        }

        ImportReport report = MarkdownImporter.Import(_target.Database, _target.Images, root, Workspace.Time);

        Assert.Equal(1, report.Notes);
        Assert.Equal([("large.md", ImportIssueKind.TooLarge)], report.Skipped.Select(s => (s.File, s.Kind)));
    }

    [Fact]
    public void A_cancelled_import_adds_nothing_and_removes_the_images_it_copied()
    {
        string root = _target.Scratch("cancel");
        Directory.CreateDirectory(Path.Join(root, "img"));
        File.WriteAllBytes(Path.Join(root, "img", "p.png"), [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 9]);
        Write(root, "a.md", "![p](img/p.png)");
        Write(root, "b.md", "second");
        string before = _target.Dump();

        using var cancellation = new CancellationTokenSource();
        var progress = new Progress(p => { if (p.Done == 1) { cancellation.Cancel(); } });

        Assert.Throws<OperationCanceledException>(() => MarkdownImporter.Import(_target.Database, _target.Images, root, Workspace.Time, progress, cancellation.Token));
        Assert.Equal(before, _target.Dump());
    }

    [Fact]
    public void An_import_that_cannot_be_saved_adds_nothing()
    {
        string root = _target.Scratch("unsaved");
        Write(root, "a.md", "a");
        string before = _target.Dump();

        // The database file read-only: the transaction cannot commit.
        _target.Close();
        File.SetAttributes(_target.Paths.DatabaseFile, FileAttributes.ReadOnly);
        try
        {
            Assert.ThrowsAny<Exception>(() => MarkdownImporter.Import(_target.Database, _target.Images, root, Workspace.Time));
        }
        finally
        {
            File.SetAttributes(_target.Paths.DatabaseFile, FileAttributes.Normal);
        }

        Assert.Equal(before, _target.Dump());
    }

    [Fact]
    public void Many_notes_round_trip()
    {
        var folders = Enumerable.Range(0, 20).Select(i => _source.Folder($"Folder {i:00}", order: i)).ToList();
        for (int i = 0; i < 2_000; i++)
        {
            _source.Note(folders[i % 20], $"Note {i}\nbody {i} ✓", order: i);
        }

        ExportSummary summary = Export(_source, new ExportScope.All())!;
        ImportReport report = MarkdownImporter.Import(_target.Database, _target.Images, summary.Directory, Workspace.Time);

        Assert.Equal((20, 2_000), (report.Folders, report.Notes));
        Assert.Equal(Notes(_source), Notes(_target));
    }

    private static ExportSummary? Export(Workspace workspace, ExportScope scope) =>
        MarkdownExporter.Export(workspace.Database, workspace.Paths.AttachmentsDirectory, scope, workspace.Scratch("exports"), Workspace.Time);

    /// <summary>Every file under <paramref name="root"/>, by its relative path, with its bytes (the index's date left out).</summary>
    private static SortedDictionary<string, string> Tree(string root) =>
        new(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => Path.GetFileName(f) != ExportIndex.FileName)
            .ToDictionary(f => Path.GetRelativePath(root, f).Replace('\\', '/'), f => Convert.ToHexString(File.ReadAllBytes(f))), StringComparer.Ordinal);

    /// <summary>Each active folder's name with its active notes' content, pin and fold, in display order.</summary>
    private static List<string> Notes(Workspace workspace)
    {
        var rows = new List<string>();
        foreach (string folder in Names(workspace, displayOrder: true))
        {
            rows.Add("# " + folder);
            rows.AddRange(Contents(workspace, folder, withState: true));
        }

        return rows;
    }

    private static List<string> Names(Workspace workspace, bool displayOrder = false)
    {
        using var connection = workspace.Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = displayOrder
            ? "SELECT Name FROM Folders WHERE DeletedAt IS NULL ORDER BY IsPinned DESC, SortOrder, Id;"
            : "SELECT Name FROM Folders WHERE DeletedAt IS NULL ORDER BY Name COLLATE BINARY;";
        using var reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return displayOrder ? names : [.. names.Order(StringComparer.Ordinal)];
    }

    private static List<string> Contents(Workspace workspace, string folder, bool withState = false)
    {
        using var connection = workspace.Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT n.Content, n.IsPinned, n.IsFolded FROM Notes n JOIN Folders f ON f.Id = n.FolderId WHERE f.Name = $f AND f.DeletedAt IS NULL AND n.DeletedAt IS NULL ORDER BY n.IsPinned DESC, n.SortOrder, n.Id;";
        command.Parameters.AddWithValue("$f", folder);
        using var reader = command.ExecuteReader();
        var notes = new List<string>();
        while (reader.Read())
        {
            notes.Add(withState ? $"{reader.GetInt64(1)}{reader.GetInt64(2)} {reader.GetString(0)}" : reader.GetString(0));
        }

        return notes;
    }

    private static IEnumerable<string> OriginalRows(string dump) =>
        dump.Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(line => line.Contains('|', StringComparison.Ordinal));

    private static void Write(string root, string relative, string text)
    {
        string path = Path.Join(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, new UTF8Encoding(false));
    }

    private sealed class Progress(Action<PortabilityProgress> report) : IProgress<PortabilityProgress>
    {
        public void Report(PortabilityProgress value) => report(value);
    }
}
