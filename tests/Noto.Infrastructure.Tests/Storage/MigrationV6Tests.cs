using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using Noto.Core.Folders;
using Noto.Infrastructure.Storage;
using Xunit;

namespace Noto.Infrastructure.Tests.Storage;

/// <summary>
/// Migration 006 (#242): every folder gains a note sort, Manual for every folder already there, and nothing else about
/// any folder or note changes. Built as an upgrade from a real v5 database. Additive, so it takes no safety copy.
/// </summary>
public sealed class MigrationV6Tests : IDisposable
{
    private const string Old = "2026-01-01T00:00:00.0000000+00:00";

    private readonly TempDatabase _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Every_folder_is_manual_and_nothing_else_about_it_or_its_notes_changes()
    {
        List<string> folders;
        List<string> notes;
        using (SqliteConnection v5 = OpenAtV5())
        {
            string home = InsertFolder(v5, "Home", deleted: false);
            InsertFolder(v5, "Binned", deleted: true);
            InsertNote(v5, home, "# Kept\r\nbyte for byte 🎉");
            folders = Rows(v5, "SELECT rowid, Id, Name, ColorKey, IsPinned, IsCollapsed, SortOrder, CreatedAt, UpdatedAt, DeletedAt FROM Folders ORDER BY rowid;");
            notes = Rows(v5, "SELECT rowid, Id, FolderId, hex(CAST(Content AS BLOB)), IsPinned, SortOrder, CreatedAt, UpdatedAt, Mode FROM Notes ORDER BY rowid;");
        }

        Upgrade();

        using SqliteConnection db = Open();
        Assert.True(MigrationRunner.LatestVersion >= 6);
        Assert.Equal(MigrationRunner.LatestVersion, MigrationRunner.GetSchemaVersion(db));
        Assert.Equal(folders, Rows(db, "SELECT rowid, Id, Name, ColorKey, IsPinned, IsCollapsed, SortOrder, CreatedAt, UpdatedAt, DeletedAt FROM Folders ORDER BY rowid;"));
        Assert.Equal(notes, Rows(db, "SELECT rowid, Id, FolderId, hex(CAST(Content AS BLOB)), IsPinned, SortOrder, CreatedAt, UpdatedAt, Mode FROM Notes ORDER BY rowid;"));
        Assert.Equal(["Manual|", "Manual|"], Rows(db, "SELECT NoteSort FROM Folders ORDER BY rowid;"));
    }

    [Fact]
    public void The_repository_reads_an_upgraded_folder_as_manual_and_writes_a_sort_by_its_name()
    {
        string id;
        using (SqliteConnection v5 = OpenAtV5())
        {
            id = InsertFolder(v5, "Home", deleted: false);
        }

        var folders = new SqliteFolderRepository(Upgrade());
        FolderId folder = FolderId.From(id);
        Assert.Equal(NoteSort.Manual, folders.FindActive(folder)!.NoteSort);

        DateTimeOffset now = new(2026, 11, 22, 8, 0, 0, TimeSpan.Zero);
        folders.SetNoteSort(folder, NoteSort.Modified, now);

        Folder sorted = Assert.Single(folders.ListActive());
        Assert.Equal(NoteSort.Modified, sorted.NoteSort);
        Assert.Equal(now, sorted.UpdatedAt);
        using SqliteConnection db = Open();
        Assert.Equal(["Modified|"], Rows(db, "SELECT NoteSort FROM Folders;"));
    }

    [Fact]
    public void A_fresh_database_has_the_column_and_no_upgrade_takes_a_safety_copy()
    {
        using (SqliteConnection v5 = OpenAtV5())
        {
            InsertFolder(v5, "Home", deleted: false);
        }

        Upgrade();

        using SqliteConnection db = Open();
        Assert.Equal(["1|"], Rows(db, "SELECT COUNT(*) FROM pragma_table_info('Folders') WHERE name = 'NoteSort' AND \"notnull\" = 1 AND dflt_value = '''Manual''';"));
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(_temp.DatabasePath)!, "*premigration-*.db"));
    }

    // ---- helpers ---------------------------------------------------------

    private SqliteConnection OpenAtV5()
    {
        var connection = new SqliteConnection($"Data Source={_temp.DatabasePath}");
        connection.Open();
        using (SqliteCommand pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys = ON;";
            pragma.ExecuteNonQuery();
        }

        new MigrationRunner(MigrationRunner.Migrations.Where(m => m.Version <= 5).ToArray()).Run(connection);
        return connection;
    }

    private NotoDatabase Upgrade()
    {
        _temp.ReleaseOwnPools();
        var database = new NotoDatabase(_temp.DatabasePath);
        database.Initialize();
        return database;
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection($"Data Source={_temp.DatabasePath}");
        connection.Open();
        return connection;
    }

    /// <summary>Every row <paramref name="sql"/> selects, its columns joined, NULL spelled out.</summary>
    private static List<string> Rows(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        using SqliteDataReader reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read())
        {
            var row = new StringBuilder();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                row.Append(reader.IsDBNull(i) ? "<null>" : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture)).Append('|');
            }

            rows.Add(row.ToString());
        }

        return rows;
    }

    private static string InsertFolder(SqliteConnection connection, string name, bool deleted)
    {
        string id = FolderId.New().Value;
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Folders (Id, Name, ColorKey, IsPinned, SortOrder, CreatedAt, UpdatedAt, DeletedAt)
            VALUES ($id, $name, 'note2', 1, 3.5, $old, $old, $deletedAt);
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$old", Old);
        command.Parameters.AddWithValue("$deletedAt", deleted ? Old : DBNull.Value);
        command.ExecuteNonQuery();
        return id;
    }

    private static void InsertNote(SqliteConnection connection, string folderId, string content)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Notes (Id, FolderId, Content, IsPinned, SortOrder, CreatedAt, UpdatedAt)
            VALUES ($id, $folderId, $content, 0, 1.5, $old, $old);
            """;
        command.Parameters.AddWithValue("$id", Noto.Core.Notes.NoteId.New().Value);
        command.Parameters.AddWithValue("$folderId", folderId);
        command.Parameters.AddWithValue("$content", content);
        command.Parameters.AddWithValue("$old", Old);
        command.ExecuteNonQuery();
    }
}
