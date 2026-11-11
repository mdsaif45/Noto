using System.Text;
using Noto.Core.Folders;
using Noto.Infrastructure.Portability;
using Xunit;

namespace Noto.Infrastructure.Tests.Portability;

/// <summary>
/// Files dropped on a folder (F9, #152), on a real database and real files: each Markdown or text file a note, each
/// picture a note holding a copy, first in the folder in the order dropped, in one transaction; the files only read.
/// </summary>
public sealed class DropImportTests : IDisposable
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

    private readonly Workspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public void Each_file_is_a_note_first_in_the_folder_in_the_order_dropped_and_the_files_are_unchanged()
    {
        FolderId folder = _workspace.Folder("Work");
        _workspace.Note(folder, "Already here", order: 1);
        string from = _workspace.Scratch("drop");
        string list = Write(from, "list.md", "# List\r\n- milk\n![dot](pictures/dot.png)");
        Directory.CreateDirectory(Path.Join(from, "pictures"));
        File.WriteAllBytes(Path.Join(from, "pictures", "dot.png"), Png);
        string plain = Write(from, "plain.txt", "﻿Plain text ✓");
        string photo = Path.Join(from, "Harbour at dusk.png");
        File.WriteAllBytes(photo, Png);
        var before = new[] { list, plain, photo, Path.Join(from, "pictures", "dot.png") }.ToDictionary(f => f, f => (File.ReadAllBytes(f), File.GetLastWriteTimeUtc(f)));

        FilesImport import = MarkdownImporter.ImportFiles(_workspace.Database, _workspace.Images, [list, plain, photo], folder, Workspace.Time);

        Assert.Equal(3, import.Notes.Count);
        Assert.Equal(2, import.Images);
        Assert.Empty(import.Skipped);
        Assert.Empty(import.ImageIssues);

        string[] contents = [.. Contents(folder)];
        Assert.Equal(4, contents.Length);
        Assert.StartsWith("# List\r\n- milk\n![dot](attachments/", contents[0], StringComparison.Ordinal);
        Assert.Equal("Plain text ✓", contents[1]);
        Assert.StartsWith("Harbour at dusk\n![Harbour at dusk.png](attachments/", contents[2], StringComparison.Ordinal);
        Assert.Equal("Already here", contents[3]);
        Assert.Equal(import.Notes.Select(n => n.Value), Ids(folder).Take(3));

        foreach ((string file, (byte[] bytes, DateTime written)) in before)
        {
            Assert.Equal(bytes, File.ReadAllBytes(file));
            Assert.Equal(written, File.GetLastWriteTimeUtc(file));
        }
    }

    [Fact]
    public void A_file_that_is_not_a_note_is_skipped_and_says_why_and_the_rest_are_imported()
    {
        FolderId folder = _workspace.Folder("Work");
        string from = _workspace.Scratch("mixed");
        string doc = Write(from, "report.docx", "not a note");
        string latin1 = Path.Join(from, "old.txt");
        File.WriteAllBytes(latin1, [0x63, 0x61, 0x66, 0xE9]);
        string fake = Write(from, "fake.png", "not a picture");
        string ok = Write(from, "ok.md", "OK");

        FilesImport import = MarkdownImporter.ImportFiles(_workspace.Database, _workspace.Images, [doc, latin1, from, fake, ok], folder, Workspace.Time);

        Assert.Single(import.Notes);
        Assert.Equal(
            [("report.docx", ImportIssueKind.NotANote), ("old.txt", ImportIssueKind.NotText), (Path.GetFileName(from), ImportIssueKind.NotANote), ("fake.png", ImportIssueKind.ImageNotAnImage)],
            import.Skipped.Select(s => (s.File, s.Kind)));
        Assert.Equal(["OK"], Contents(folder));
    }

    [Fact]
    public void A_folder_gone_imports_nothing_and_leaves_no_picture_behind()
    {
        FolderId folder = _workspace.Folder("Gone", deleted: true);
        string from = _workspace.Scratch("gone");
        string photo = Path.Join(from, "p.png");
        File.WriteAllBytes(photo, Png);
        string before = _workspace.Dump();

        Assert.Throws<PortabilityException>(() => MarkdownImporter.ImportFiles(_workspace.Database, _workspace.Images, [photo, Write(from, "n.md", "n")], folder, Workspace.Time));

        Assert.Equal(before, _workspace.Dump());
    }

    private static string Write(string directory, string name, string text)
    {
        string path = Path.Join(directory, name);
        File.WriteAllText(path, text, new UTF8Encoding(false));
        return path;
    }

    private List<string> Contents(FolderId folder) => Rows(folder, "Content");

    private List<string> Ids(FolderId folder) => Rows(folder, "Id");

    private List<string> Rows(FolderId folder, string column)
    {
        using var connection = _workspace.Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {column} FROM Notes WHERE FolderId = $f AND DeletedAt IS NULL ORDER BY IsPinned DESC, SortOrder, Id;";
        command.Parameters.AddWithValue("$f", folder.Value);
        using var reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read())
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }
}
