using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using Noto.Core.Folders;
using Noto.Core.Notes;
using Noto.Infrastructure.Storage;
using Xunit;

namespace Noto.Infrastructure.Tests.Storage;

/// <summary>
/// Migration 005 (#195): every note gains a mode, Markdown for every note already there, and nothing else about any note
/// changes: its content byte for byte, its other columns, its rowid and the search index. Built as an upgrade from a
/// real v4 database, behind the pre-migration safety copy.
/// </summary>
public sealed class MigrationV5Tests : IDisposable
{
    private const string Old = "2026-01-01T00:00:00.0000000+00:00";

    // Line endings of every kind, tabs, markup, non-BMP text, a long note, an empty one: what a migration must not touch.
    private static readonly string[] Contents =
    [
        "# Heading\r\n\r\n- [ ] task\r\n**bold** and `code`\r\n",
        "lone\rCR and\nLF\ttab   trailing   ",
        "日本語 🎉 Ünïcödé​ zero width",
        string.Empty,
        new string('x', 300_000) + "\nend",
    ];

    private readonly TempDatabase _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Every_note_is_markdown_and_nothing_else_about_it_changes()
    {
        List<string> before;
        using (SqliteConnection v4 = OpenAtV4())
        {
            string folder = InsertFolder(v4);
            foreach (string content in Contents)
            {
                InsertNote(v4, folder, content, deleted: false);
            }

            InsertNote(v4, null, "in the bin, no folder", deleted: true);
            before = Rows(v4);
        }

        Upgrade();

        using SqliteConnection db = Open();
        Assert.Equal(MigrationRunner.LatestVersion, MigrationRunner.GetSchemaVersion(db));
        Assert.True(MigrationRunner.LatestVersion >= 5);

        // Every column as it was, the content compared as bytes, the rowid included; and Mode is Markdown for every row.
        Assert.Equal(before, Rows(db));
        Assert.Equal(Contents.Length + 1L, Count(db, "SELECT COUNT(*) FROM Notes WHERE Mode = 'Markdown';"));
        Assert.Equal(0L, Count(db, "SELECT COUNT(*) FROM Notes WHERE Mode <> 'Markdown';"));
    }

    [Fact]
    public void The_repository_reads_every_upgraded_note_as_markdown_and_new_rows_default_to_it()
    {
        string folder;
        using (SqliteConnection v4 = OpenAtV4())
        {
            folder = InsertFolder(v4);
            InsertNote(v4, folder, "# Title\nbody", deleted: false);
        }

        var notes = new SqliteNoteRepository(Upgrade());
        Note note = Assert.Single(notes.ListInFolder(FolderId.From(folder)));
        Assert.Equal(NoteMode.Markdown, note.Mode);
        Assert.Equal("# Title\nbody", note.Content);
        Assert.Equal("Title", note.Title);

        // A row written without a mode, as an older writer would, is Markdown too.
        using SqliteConnection db = Open();
        InsertNote(db, folder, "raw insert", deleted: false);
        Assert.Equal(0L, Count(db, "SELECT COUNT(*) FROM Notes WHERE Mode <> 'Markdown';"));
    }

    [Fact]
    public void The_search_index_still_matches_the_notes_without_a_rebuild()
    {
        using (SqliteConnection v4 = OpenAtV4())
        {
            string folder = InsertFolder(v4);
            InsertNote(v4, folder, "a harbour at dawn", deleted: false);
        }

        Upgrade();

        using SqliteConnection db = Open();
        Assert.Equal(1L, Count(db, "SELECT COUNT(*) FROM NotesFts JOIN Notes n ON n.rowid = NotesFts.rowid WHERE NotesFts MATCH 'harbour';"));
        Assert.Equal(0L, Count(db, "SELECT COUNT(*) FROM pragma_integrity_check WHERE integrity_check <> 'ok';"));
    }

    [Fact]
    public void It_runs_behind_a_safety_copy_of_the_v4_database_and_a_second_run_changes_nothing()
    {
        using (SqliteConnection v4 = OpenAtV4())
        {
            InsertNote(v4, InsertFolder(v4), "kept", deleted: false);
        }

        Upgrade();

        string directory = Path.GetDirectoryName(_temp.DatabasePath)!;
        string copy = Assert.Single(Directory.EnumerateFiles(directory, "*premigration-v4-*.db"));

        // The copy is the database as it was: v4, no Mode column, the note in it.
        using (var safety = new SqliteConnection($"Data Source={copy}"))
        {
            safety.Open();
            Assert.Equal(4, MigrationRunner.GetSchemaVersion(safety));
            Assert.Equal(0L, Count(safety, "SELECT COUNT(*) FROM pragma_table_info('Notes') WHERE name = 'Mode';"));
            Assert.Equal(1L, Count(safety, "SELECT COUNT(*) FROM Notes WHERE Content = 'kept';"));
        }

        List<string> first;
        using (SqliteConnection db = Open())
        {
            first = Rows(db);
        }

        _temp.ReleaseOwnPools();
        Upgrade();
        using SqliteConnection again = Open();
        Assert.Equal(first, Rows(again));
        Assert.Single(Directory.EnumerateFiles(directory, "*premigration-*.db"));
    }

    [Fact]
    public void A_fresh_database_has_the_column_and_takes_no_safety_copy()
    {
        Upgrade();

        using SqliteConnection db = Open();
        Assert.Equal(1L, Count(db, "SELECT COUNT(*) FROM pragma_table_info('Notes') WHERE name = 'Mode' AND \"notnull\" = 1 AND dflt_value = '''Markdown''';"));
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(_temp.DatabasePath)!, "*premigration-*.db"));
    }

    // ---- helpers ---------------------------------------------------------

    private SqliteConnection OpenAtV4()
    {
        var connection = new SqliteConnection($"Data Source={_temp.DatabasePath}");
        connection.Open();
        Exec(connection, "PRAGMA foreign_keys = ON;");
        new MigrationRunner(MigrationRunner.Migrations.Where(m => m.Version <= 4).ToArray()).Run(connection);
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

    /// <summary>Every v4 column of every note, the content as its bytes in hex, with the rowid, in rowid order.</summary>
    private static List<string> Rows(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT rowid, Id, FolderId, hex(CAST(Content AS BLOB)), ColorKey, IsPinned, IsFolded, SortOrder, CreatedAt, UpdatedAt, DeletedAt
            FROM Notes ORDER BY rowid;
            """;
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

    private static string InsertFolder(SqliteConnection connection)
    {
        string id = FolderId.New().Value;
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "INSERT INTO Folders (Id, Name, SortOrder, CreatedAt, UpdatedAt) VALUES ($id, 'Home', 0, $old, $old);";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$old", Old);
        command.ExecuteNonQuery();
        return id;
    }

    private static void InsertNote(SqliteConnection connection, string? folderId, string content, bool deleted)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Notes (Id, FolderId, Content, ColorKey, IsPinned, SortOrder, CreatedAt, UpdatedAt, DeletedAt)
            VALUES ($id, $folderId, $content, 'note3', 1, 2.5, $old, $old, $deletedAt);
            """;
        command.Parameters.AddWithValue("$id", NoteId.New().Value);
        command.Parameters.AddWithValue("$folderId", (object?)folderId ?? DBNull.Value);
        command.Parameters.AddWithValue("$content", content);
        command.Parameters.AddWithValue("$old", Old);
        command.Parameters.AddWithValue("$deletedAt", deleted ? Old : DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static void Exec(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static long Count(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }
}
