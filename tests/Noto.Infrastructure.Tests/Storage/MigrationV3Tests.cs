using System.Globalization;
using Microsoft.Data.Sqlite;
using Noto.Core;
using Noto.Core.Folders;
using Noto.Core.Notes;
using Noto.Infrastructure.Storage;
using Noto.UseCases.Notes;
using Xunit;

namespace Noto.Infrastructure.Tests.Storage;

/// <summary>
/// Migration 003 (I7): an active note with no folder is repaired into the recycle bin, explicitly, and from then on the
/// database refuses one. Built as an upgrade from a real v2 database.
/// </summary>
public sealed class MigrationV3Tests : IDisposable
{
    private const string Old = "2026-01-01T00:00:00.0000000+00:00";
    private readonly TempDatabase _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void An_active_note_without_a_folder_is_moved_to_the_recycle_bin_with_its_content()
    {
        string orphan, foldered, binnedOrphan, folder;
        using (SqliteConnection v2 = OpenAtV2())
        {
            folder = InsertFolder(v2);
            orphan = InsertNote(v2, folderId: null, deletedAt: null, content: "# Lost\n\nstill here");
            foldered = InsertNote(v2, folderId: folder, deletedAt: null, content: "fine");
            binnedOrphan = InsertNote(v2, folderId: null, deletedAt: Old, content: "already binned");
        }

        DateTimeOffset before = DateTimeOffset.UtcNow.AddSeconds(-1);
        Upgrade();
        DateTimeOffset after = DateTimeOffset.UtcNow.AddSeconds(1);

        using SqliteConnection db = Open();

        // Repaired: in the bin, stamped now in Noto's own timestamp form, content untouched, still without a folder.
        string? deletedAt = Scalar(db, "SELECT DeletedAt FROM Notes WHERE Id = $id;", orphan);
        Assert.NotNull(deletedAt);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{7}\+00:00$", deletedAt);
        DateTimeOffset stamped = DateTimeOffset.Parse(deletedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        Assert.InRange(stamped, before, after);
        Assert.Equal(deletedAt, Scalar(db, "SELECT UpdatedAt FROM Notes WHERE Id = $id;", orphan));
        Assert.Equal("# Lost\n\nstill here", Scalar(db, "SELECT Content FROM Notes WHERE Id = $id;", orphan));
        Assert.Null(Scalar(db, "SELECT FolderId FROM Notes WHERE Id = $id;", orphan));

        // Untouched: a note in a folder, and a folderless note that was already in the bin (its own DeletedAt kept).
        Assert.Null(Scalar(db, "SELECT DeletedAt FROM Notes WHERE Id = $id;", foldered));
        Assert.Equal(Old, Scalar(db, "SELECT DeletedAt FROM Notes WHERE Id = $id;", binnedOrphan));
        Assert.Equal(0L, Count(db, "SELECT COUNT(*) FROM Notes WHERE DeletedAt IS NULL AND FolderId IS NULL;"));
    }

    [Fact]
    public void A_repaired_note_comes_back_only_into_a_folder_the_user_chooses()
    {
        string orphan, folder;
        using (SqliteConnection v2 = OpenAtV2())
        {
            folder = InsertFolder(v2);
            orphan = InsertNote(v2, folderId: null, deletedAt: null, content: "lost");
        }

        var database = Upgrade();
        var notes = new SqliteNoteRepository(database);
        var clock = new FixedClock();
        NoteId id = NoteId.From(orphan);
        FolderId home = FolderId.From(folder);

        Assert.Contains(notes.ListDeleted(), n => n.Id == id);
        Assert.False(new RestoreNoteHandler(notes, clock).Handle(new RestoreNote(id)).IsSuccess);
        Assert.True(new RestoreNoteToFolderHandler(notes, clock).Handle(new RestoreNoteToFolder(id, home)).IsSuccess);
        Assert.Contains(notes.ListInFolder(home), n => n.Id == id);
    }

    [Fact]
    public void The_upgraded_database_refuses_every_write_that_leaves_an_active_note_without_a_folder()
    {
        string note, binned;
        using (SqliteConnection v2 = OpenAtV2())
        {
            string folder = InsertFolder(v2);
            note = InsertNote(v2, folderId: folder, deletedAt: null, content: "n");
            binned = InsertNote(v2, folderId: null, deletedAt: Old, content: "b");
        }

        Upgrade();
        using SqliteConnection db = Open();

        Assert.ThrowsAny<SqliteException>(() => InsertNote(db, folderId: null, deletedAt: null, content: "x"));
        Assert.ThrowsAny<SqliteException>(() => Exec(db, "UPDATE Notes SET FolderId = NULL WHERE Id = $id;", note));
        Assert.ThrowsAny<SqliteException>(() => Exec(db, "UPDATE Notes SET DeletedAt = NULL WHERE Id = $id;", binned));

        // Still allowed: a deleted note may have no folder, and an active note may change folders.
        InsertNote(db, folderId: null, deletedAt: Old, content: "binned, no folder");
        string other = InsertFolder(db);
        Exec(db, $"UPDATE Notes SET FolderId = '{other}' WHERE Id = $id;", note);
        Assert.Equal(0L, Count(db, "SELECT COUNT(*) FROM Notes WHERE DeletedAt IS NULL AND FolderId IS NULL;"));
    }

    [Fact]
    public void It_runs_behind_a_safety_copy_and_a_second_run_changes_nothing()
    {
        using (SqliteConnection v2 = OpenAtV2())
        {
            InsertNote(v2, folderId: null, deletedAt: null, content: "lost");
        }

        Upgrade();
        string firstStamp;
        using (SqliteConnection db = Open())
        {
            firstStamp = Scalar(db, "SELECT DeletedAt FROM Notes;", null)!;
            Assert.Equal(MigrationRunner.LatestVersion, MigrationRunner.GetSchemaVersion(db));
            Assert.Equal(3, MigrationRunner.LatestVersion);
        }

        string directory = Path.GetDirectoryName(_temp.DatabasePath)!;
        Assert.NotEmpty(Directory.EnumerateFiles(directory, "*premigration-v2-*.db", SearchOption.AllDirectories));

        _temp.ReleaseOwnPools();
        Upgrade();
        using SqliteConnection again = Open();
        Assert.Equal(firstStamp, Scalar(again, "SELECT DeletedAt FROM Notes;", null));
    }

    // ---- helpers ---------------------------------------------------------

    private SqliteConnection OpenAtV2()
    {
        var connection = new SqliteConnection($"Data Source={_temp.DatabasePath}");
        connection.Open();
        Exec(connection, "PRAGMA foreign_keys = ON;", null);
        new MigrationRunner(MigrationRunner.Migrations.Where(m => m.Version <= 2).ToArray()).Run(connection);
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
        Exec(connection, "PRAGMA foreign_keys = ON;", null);
        return connection;
    }

    private static string InsertFolder(SqliteConnection connection)
    {
        string id = FolderId.New().Value;
        Exec(connection, $"INSERT INTO Folders (Id, Name, SortOrder, CreatedAt, UpdatedAt) VALUES ($id, 'Home', 0, '{Old}', '{Old}');", id);
        return id;
    }

    private static string InsertNote(SqliteConnection connection, string? folderId, string? deletedAt, string content)
    {
        string id = NoteId.New().Value;
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Notes (Id, FolderId, Content, SortOrder, CreatedAt, UpdatedAt, DeletedAt)
            VALUES ($id, $folderId, $content, 0, $old, $old, $deletedAt);
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$folderId", (object?)folderId ?? DBNull.Value);
        command.Parameters.AddWithValue("$content", content);
        command.Parameters.AddWithValue("$old", Old);
        command.Parameters.AddWithValue("$deletedAt", (object?)deletedAt ?? DBNull.Value);
        command.ExecuteNonQuery();
        return id;
    }

    private static void Exec(SqliteConnection connection, string sql, string? id)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        if (id is not null)
        {
            command.Parameters.AddWithValue("$id", id);
        }

        command.ExecuteNonQuery();
    }

    private static string? Scalar(SqliteConnection connection, string sql, string? id)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        if (id is not null)
        {
            command.Parameters.AddWithValue("$id", id);
        }

        return command.ExecuteScalar() is string s ? s : null;
    }

    private static long Count(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)command.ExecuteScalar()!;
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);
    }
}
