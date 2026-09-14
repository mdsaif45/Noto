using Microsoft.Data.Sqlite;
using Noto.Core.Identifiers;
using Noto.Core.Storage;
using Noto.Infrastructure.Storage;
using Xunit;

namespace Noto.Infrastructure.Tests.Storage;

/// <summary>
/// The database behaves the way ADR-002 promises: data survives, constraints
/// hold, and the pragmas are actually applied.
/// </summary>
public sealed class PersistenceTests : IDisposable
{
    private readonly TempDatabase _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Data_survives_closing_and_reopening()
    {
        // The whole point of a local-first store.
        string id = Ulid.NewId();
        var database = new NotoDatabase(_temp.DatabasePath);
        database.Initialize();

        using (var connection = database.OpenConnection())
        {
            using var insert = connection.CreateCommand();
            insert.CommandText =
                """
                INSERT INTO Notes (Id, Title, Content, CreatedAt, UpdatedAt)
                VALUES ($id, $title, $content, $now, $now);
                """;
            insert.Parameters.AddWithValue("$id", id);
            insert.Parameters.AddWithValue("$title", "Survives a restart");
            insert.Parameters.AddWithValue("$content", "# heading\n\n- [ ] a task");
            insert.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            insert.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();

        // A completely new instance, as if the application had restarted.
        var reopened = new NotoDatabase(_temp.DatabasePath);
        reopened.Initialize();

        using var verify = reopened.OpenConnection();
        using var select = verify.CreateCommand();
        select.CommandText = "SELECT Title, Content FROM Notes WHERE Id = $id;";
        select.Parameters.AddWithValue("$id", id);

        using var reader = select.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("Survives a restart", reader.GetString(0));
        Assert.Equal("# heading\n\n- [ ] a task", reader.GetString(1));
    }

    [Fact]
    public void Foreign_keys_are_enforced()
    {
        // OFF by default in SQLite. Without the pragma the schema's declared
        // relationships would be documentation rather than constraints.
        var database = new NotoDatabase(_temp.DatabasePath);
        database.Initialize();

        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Notes (Id, FolderId, Title, Content, CreatedAt, UpdatedAt)
            VALUES ($id, $folder, '', '', $now, $now);
            """;
        command.Parameters.AddWithValue("$id", Ulid.NewId());
        command.Parameters.AddWithValue("$folder", "NOT-A-REAL-FOLDER-ID");
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));

        var error = Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());
        Assert.Contains("FOREIGN KEY", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Deleting_a_folder_detaches_its_notes_rather_than_deleting_them()
    {
        // ON DELETE SET NULL, deliberately. Deleting a folder must never
        // silently destroy the notes inside it.
        var database = new NotoDatabase(_temp.DatabasePath);
        database.Initialize();

        string folderId = Ulid.NewId();
        string noteId = Ulid.NewId();
        string now = DateTimeOffset.UtcNow.ToString("O");

        using var connection = database.OpenConnection();

        Execute(connection,
            "INSERT INTO Folders (Id, Name, CreatedAt, UpdatedAt) VALUES ($id, 'Work', $now, $now);",
            ("$id", folderId), ("$now", now));

        Execute(connection,
            """
            INSERT INTO Notes (Id, FolderId, Title, Content, CreatedAt, UpdatedAt)
            VALUES ($id, $folder, 'kept', '', $now, $now);
            """,
            ("$id", noteId), ("$folder", folderId), ("$now", now));

        Execute(connection, "DELETE FROM Folders WHERE Id = $id;", ("$id", folderId));

        using var select = connection.CreateCommand();
        select.CommandText = "SELECT FolderId FROM Notes WHERE Id = $id;";
        select.Parameters.AddWithValue("$id", noteId);

        using var reader = select.ExecuteReader();
        Assert.True(reader.Read());
        Assert.True(reader.IsDBNull(0));
    }

    [Fact]
    public void Deleting_a_note_removes_its_tag_links_but_not_the_tags()
    {
        var database = new NotoDatabase(_temp.DatabasePath);
        database.Initialize();

        string noteId = Ulid.NewId();
        string tagId = Ulid.NewId();
        string now = DateTimeOffset.UtcNow.ToString("O");

        using var connection = database.OpenConnection();

        Execute(connection,
            "INSERT INTO Notes (Id, Title, Content, CreatedAt, UpdatedAt) VALUES ($id, '', '', $now, $now);",
            ("$id", noteId), ("$now", now));

        Execute(connection,
            "INSERT INTO Tags (Id, Name, CreatedAt) VALUES ($id, 'work', $now);",
            ("$id", tagId), ("$now", now));

        Execute(connection,
            "INSERT INTO NoteTags (NoteId, TagId) VALUES ($note, $tag);",
            ("$note", noteId), ("$tag", tagId));

        Execute(connection, "DELETE FROM Notes WHERE Id = $id;", ("$id", noteId));

        Assert.Equal(0, ScalarCount(connection, "SELECT COUNT(*) FROM NoteTags;"));
        Assert.Equal(1, ScalarCount(connection, "SELECT COUNT(*) FROM Tags;"));
    }

    [Fact]
    public void Tag_names_are_case_insensitively_unique()
    {
        // 'work' and 'Work' becoming two tags is a papercut every tagging
        // system eventually has to fix.
        var database = new NotoDatabase(_temp.DatabasePath);
        database.Initialize();

        string now = DateTimeOffset.UtcNow.ToString("O");
        using var connection = database.OpenConnection();

        Execute(connection,
            "INSERT INTO Tags (Id, Name, CreatedAt) VALUES ($id, 'work', $now);",
            ("$id", Ulid.NewId()), ("$now", now));

        using var duplicate = connection.CreateCommand();
        duplicate.CommandText = "INSERT INTO Tags (Id, Name, CreatedAt) VALUES ($id, 'WORK', $now);";
        duplicate.Parameters.AddWithValue("$id", Ulid.NewId());
        duplicate.Parameters.AddWithValue("$now", now);

        Assert.Throws<SqliteException>(() => duplicate.ExecuteNonQuery());
    }

    [Fact]
    public void Write_ahead_logging_is_enabled()
    {
        var database = new NotoDatabase(_temp.DatabasePath);
        database.Initialize();

        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode;";

        Assert.Equal("wal", command.ExecuteScalar()?.ToString(), ignoreCase: true);
    }

    [Fact]
    public void Opening_a_nonexistent_directory_fails_with_a_useful_error()
    {
        // A directory that does not exist. Built from the temp root so the
        // test cannot touch anything real, and normalised so the segments
        // cannot be reinterpreted.
        string missing = Path.GetFullPath(
            Path.Combine(_temp.DatabasePath, "no", "such", "directory", "noto.db"));

        var database = new NotoDatabase(missing);

        var error = Assert.Throws<StorageException>(() => database.OpenConnection());

        Assert.Equal(StorageFailure.CannotOpen, error.Reason);

        // The originating exception must survive — infrastructure preserves
        // diagnostic detail rather than swallowing it.
        Assert.NotNull(error.InnerException);
    }

    [Fact]
    public void A_transaction_rolls_back_on_failure()
    {
        var database = new NotoDatabase(_temp.DatabasePath);
        database.Initialize();

        using var connection = database.OpenConnection();
        string now = DateTimeOffset.UtcNow.ToString("O");

        using (var transaction = connection.BeginTransaction())
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText =
                "INSERT INTO Notes (Id, Title, Content, CreatedAt, UpdatedAt) VALUES ($id, 'rolled back', '', $now, $now);";
            insert.Parameters.AddWithValue("$id", Ulid.NewId());
            insert.Parameters.AddWithValue("$now", now);
            insert.ExecuteNonQuery();

            transaction.Rollback();
        }

        Assert.Equal(0, ScalarCount(connection, "SELECT COUNT(*) FROM Notes;"));
    }

    private static void Execute(
        SqliteConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        command.ExecuteNonQuery();
    }

    private static int ScalarCount(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }
}
