using Microsoft.Data.Sqlite;
using Noto.Core.Identifiers;
using Noto.Core.Storage;
using Noto.Infrastructure.Storage;
using Xunit;

namespace Noto.Infrastructure.Tests.Storage;

/// <summary>
/// Migration 002 is the first migration to transform data the user wrote, so
/// its upgrade path is tested against <b>populated</b> v1 databases, not just
/// empty ones.
/// </summary>
/// <remarks>
/// A migration correct on a fresh install and destructive on an existing
/// database is the worst bug this product can have (ADR-002: the data cannot be
/// recovered from a server).
/// </remarks>
public sealed class MigrationV2Tests : IDisposable
{
    private readonly TempDatabase _temp = new();

    public void Dispose() => _temp.Dispose();

    // ---- helpers -------------------------------------------------------

    /// <summary>Builds a database at schema v1 only, so v2 can be tested as an upgrade.</summary>
    private SqliteConnection OpenAtV1()
    {
        var connection = new SqliteConnection($"Data Source={_temp.DatabasePath}");
        connection.Open();
        Execute(connection, "PRAGMA foreign_keys = ON;");

        new MigrationRunner(MigrationRunner.Migrations.Where(m => m.Version <= 1).ToArray())
            .Run(connection);

        return connection;
    }

    private void UpgradeToLatest() => new NotoDatabase(_temp.DatabasePath).Initialize();

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

    private static string? ScalarString(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        object? value = command.ExecuteScalar();
        return value is null or DBNull ? null : value.ToString();
    }

    private static long ScalarLong(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static List<string> Columns(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT name FROM pragma_table_info('{table}');";

        var names = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static string InsertV1Note(
        SqliteConnection connection, string title, string content, string? folderId = null)
    {
        string id = Ulid.NewId();
        string now = DateTimeOffset.UtcNow.ToString("O");

        Execute(connection,
            """
            INSERT INTO Notes (Id, FolderId, Title, Content, CreatedAt, UpdatedAt)
            VALUES ($id, $folder, $title, $content, $now, $now);
            """,
            ("$id", id),
            ("$folder", (object?)folderId ?? DBNull.Value),
            ("$title", title),
            ("$content", content),
            ("$now", now));

        return id;
    }

    private static string ContentOf(SqliteConnection connection, string noteId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Content FROM Notes WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", noteId);
        return (string)command.ExecuteScalar()!;
    }

    // ---- schema shape --------------------------------------------------

    [Fact]
    public void Fresh_database_reaches_v2()
    {
        UpgradeToLatest();

        using var connection = new NotoDatabase(_temp.DatabasePath).OpenConnection();
        Assert.Equal(2, MigrationRunner.GetSchemaVersion(connection));
        Assert.Equal(2, MigrationRunner.LatestVersion);
    }

    [Fact]
    public void Notes_no_longer_has_a_title_column()
    {
        UpgradeToLatest();

        using var connection = new NotoDatabase(_temp.DatabasePath).OpenConnection();
        var columns = Columns(connection, "Notes");

        Assert.DoesNotContain("Title", columns);

        // Everything else survives the rebuild.
        foreach (string expected in new[]
                 {
                     "Id", "FolderId", "Content", "ColorKey", "IsPinned",
                     "IsFolded", "SortOrder", "CreatedAt", "UpdatedAt", "DeletedAt",
                 })
        {
            Assert.Contains(expected, columns);
        }
    }

    [Fact]
    public void Folders_gains_pinning_and_soft_deletion()
    {
        UpgradeToLatest();

        using var connection = new NotoDatabase(_temp.DatabasePath).OpenConnection();
        var columns = Columns(connection, "Folders");

        Assert.Contains("IsPinned", columns);
        Assert.Contains("DeletedAt", columns);
    }

    [Fact]
    public void Existing_folders_become_unpinned_and_active()
    {
        string folderId = Ulid.NewId();
        string now = DateTimeOffset.UtcNow.ToString("O");

        using (var v1 = OpenAtV1())
        {
            Execute(v1,
                "INSERT INTO Folders (Id, Name, CreatedAt, UpdatedAt) VALUES ($id, 'Work', $now, $now);",
                ("$id", folderId), ("$now", now));
        }

        SqliteConnection.ClearAllPools();
        UpgradeToLatest();

        using var connection = new NotoDatabase(_temp.DatabasePath).OpenConnection();
        Assert.Equal(0, ScalarLong(connection, $"SELECT IsPinned FROM Folders WHERE Id = '{folderId}';"));
        Assert.Null(ScalarString(connection, $"SELECT DeletedAt FROM Folders WHERE Id = '{folderId}';"));
    }

    // ---- title transformation ------------------------------------------

    [Fact]
    public void A_title_that_differs_from_the_content_is_preserved_as_a_heading()
    {
        string id;
        using (var v1 = OpenAtV1())
        {
            id = InsertV1Note(v1, "Shopping List", "Milk\nEggs");
        }

        SqliteConnection.ClearAllPools();
        UpgradeToLatest();

        using var connection = new NotoDatabase(_temp.DatabasePath).OpenConnection();
        Assert.Equal("# Shopping List\n\nMilk\nEggs", ContentOf(connection, id));
    }

    [Fact]
    public void A_title_already_present_as_a_heading_is_not_duplicated()
    {
        string id;
        using (var v1 = OpenAtV1())
        {
            id = InsertV1Note(v1, "Shopping List", "# Shopping List\nMilk\nEggs");
        }

        SqliteConnection.ClearAllPools();
        UpgradeToLatest();

        using var connection = new NotoDatabase(_temp.DatabasePath).OpenConnection();
        Assert.Equal("# Shopping List\nMilk\nEggs", ContentOf(connection, id));
    }

    [Fact]
    public void A_title_matching_the_first_line_verbatim_is_not_duplicated()
    {
        string id;
        using (var v1 = OpenAtV1())
        {
            id = InsertV1Note(v1, "Shopping List", "Shopping List\nMilk");
        }

        SqliteConnection.ClearAllPools();
        UpgradeToLatest();

        using var connection = new NotoDatabase(_temp.DatabasePath).OpenConnection();
        Assert.Equal("Shopping List\nMilk", ContentOf(connection, id));
    }

    /// <remarks>
    /// <b>The migration's definition of "blank" is deliberately narrow:</b>
    /// space, tab, CR and LF. It is not Unicode whitespace normalisation.
    /// <para>
    /// SQL <c>TRIM()</c> strips spaces only, which a test caught — a tab-only
    /// title would otherwise have become an empty heading. Extending to the
    /// full Unicode whitespace set would mean shipping a character classifier
    /// inside a migration, which has to be right once, on data it cannot
    /// inspect. These four characters cover what a user can type into a
    /// single-line title field; anything more exotic is preserved verbatim
    /// rather than silently discarded, which is the safer failure.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("    ")]
    [InlineData("\t")]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData(" \t\r\n ")]
    public void An_empty_or_whitespace_title_changes_nothing(string title)
    {
        string id;
        using (var v1 = OpenAtV1())
        {
            id = InsertV1Note(v1, title, "Milk\nEggs");
        }

        SqliteConnection.ClearAllPools();
        UpgradeToLatest();

        using var connection = new NotoDatabase(_temp.DatabasePath).OpenConnection();
        Assert.Equal("Milk\nEggs", ContentOf(connection, id));
    }

    [Fact]
    public void A_title_on_empty_content_becomes_the_content()
    {
        string id;
        using (var v1 = OpenAtV1())
        {
            id = InsertV1Note(v1, "Just a title", string.Empty);
        }

        SqliteConnection.ClearAllPools();
        UpgradeToLatest();

        using var connection = new NotoDatabase(_temp.DatabasePath).OpenConnection();

        // The title is the only content the user had. Losing it would be the
        // exact data loss this migration exists to avoid.
        Assert.Contains("Just a title", ContentOf(connection, id), StringComparison.Ordinal);
    }

    [Fact]
    public void A_title_containing_markdown_characters_survives_verbatim()
    {
        string id;
        using (var v1 = OpenAtV1())
        {
            id = InsertV1Note(v1, "50% *off* — [read] #1", "body");
        }

        SqliteConnection.ClearAllPools();
        UpgradeToLatest();

        using var connection = new NotoDatabase(_temp.DatabasePath).OpenConnection();

        // Not escaped or reinterpreted — the migration preserves, it does not parse.
        Assert.Equal("# 50% *off* — [read] #1\n\nbody", ContentOf(connection, id));
    }

    [Fact]
    public void Unicode_and_single_quotes_in_a_title_survive()
    {
        string id;
        using (var v1 = OpenAtV1())
        {
            id = InsertV1Note(v1, "Wintermöhre's plan 日本語", "body");
        }

        SqliteConnection.ClearAllPools();
        UpgradeToLatest();

        using var connection = new NotoDatabase(_temp.DatabasePath).OpenConnection();
        Assert.Equal("# Wintermöhre's plan 日本語\n\nbody", ContentOf(connection, id));
    }

    [Fact]
    public void A_single_line_content_matching_the_title_is_not_duplicated()
    {
        // No newline at all: the "first line" is the whole content.
        string id;
        using (var v1 = OpenAtV1())
        {
            id = InsertV1Note(v1, "Reminder", "Reminder");
        }

        SqliteConnection.ClearAllPools();
        UpgradeToLatest();

        using var connection = new NotoDatabase(_temp.DatabasePath).OpenConnection();
        Assert.Equal("Reminder", ContentOf(connection, id));
    }

    // ---- relationships and other data ----------------------------------

    [Fact]
    public void A_populated_v1_database_survives_the_upgrade_intact()
    {
        string folderId = Ulid.NewId();
        string tagId = Ulid.NewId();
        string noteId;
        string now = DateTimeOffset.UtcNow.ToString("O");

        using (var v1 = OpenAtV1())
        {
            Execute(v1,
                "INSERT INTO Folders (Id, Name, CreatedAt, UpdatedAt) VALUES ($id, 'Work', $now, $now);",
                ("$id", folderId), ("$now", now));

            noteId = InsertV1Note(v1, "Title", "Body", folderId);

            Execute(v1, "INSERT INTO Tags (Id, Name, CreatedAt) VALUES ($id, 'urgent', $now);",
                ("$id", tagId), ("$now", now));

            Execute(v1, "INSERT INTO NoteTags (NoteId, TagId) VALUES ($n, $t);",
                ("$n", noteId), ("$t", tagId));

            Execute(v1, "INSERT INTO Settings (Key, Value) VALUES ('theme', 'dark');");
        }

        SqliteConnection.ClearAllPools();
        UpgradeToLatest();

        using var connection = new NotoDatabase(_temp.DatabasePath).OpenConnection();

        Assert.Equal(1, ScalarLong(connection, "SELECT COUNT(*) FROM Folders;"));
        Assert.Equal(1, ScalarLong(connection, "SELECT COUNT(*) FROM Notes;"));
        Assert.Equal(1, ScalarLong(connection, "SELECT COUNT(*) FROM Tags;"));
        Assert.Equal("dark", ScalarString(connection, "SELECT Value FROM Settings WHERE Key = 'theme';"));

        // The relationship that the rebuild would silently destroy with foreign
        // keys enabled. Measured: it goes to 0 without the pragma handling.
        Assert.Equal(1, ScalarLong(connection, "SELECT COUNT(*) FROM NoteTags;"));
        Assert.Equal(noteId, ScalarString(connection, "SELECT NoteId FROM NoteTags;"));

        // The note still points at its folder after the table was rebuilt.
        Assert.Equal(folderId, ScalarString(connection, $"SELECT FolderId FROM Notes WHERE Id = '{noteId}';"));
    }

    [Fact]
    public void Every_note_tag_relationship_survives_the_upgrade()
    {
        // The invariant foreign_key_check CANNOT prove. It answers "are the
        // current references valid?", not "did the migration preserve the
        // relationships that existed before?" — and a rebuild that silently
        // empties NoteTags leaves a structurally valid database.
        //
        // So compare the full set, before and after.
        var expected = new HashSet<string>(StringComparer.Ordinal);
        string now = DateTimeOffset.UtcNow.ToString("O");

        using (var v1 = OpenAtV1())
        {
            var noteIds = new List<string>();
            var tagIds = new List<string>();

            for (int i = 0; i < 5; i++)
            {
                noteIds.Add(InsertV1Note(v1, $"Title {i}", $"Body {i}"));

                string tagId = Ulid.NewId();
                Execute(v1, "INSERT INTO Tags (Id, Name, CreatedAt) VALUES ($id, $name, $now);",
                    ("$id", tagId), ("$name", $"tag{i}"), ("$now", now));
                tagIds.Add(tagId);
            }

            // A deliberately uneven mesh, so a bug that drops a subset shows up.
            foreach (var (noteIndex, tagIndex) in new[]
                     {
                         (0, 0), (0, 1), (0, 2),
                         (1, 1),
                         (2, 0), (2, 4),
                         (4, 3),
                     })
            {
                Execute(v1, "INSERT INTO NoteTags (NoteId, TagId) VALUES ($n, $t);",
                    ("$n", noteIds[noteIndex]), ("$t", tagIds[tagIndex]));

                expected.Add($"{noteIds[noteIndex]}|{tagIds[tagIndex]}");
            }
        }

        SqliteConnection.ClearAllPools();
        UpgradeToLatest();

        using var connection = new NotoDatabase(_temp.DatabasePath).OpenConnection();

        var actual = new HashSet<string>(StringComparer.Ordinal);
        using (var read = connection.CreateCommand())
        {
            read.CommandText = "SELECT NoteId, TagId FROM NoteTags;";
            using var reader = read.ExecuteReader();
            while (reader.Read())
            {
                actual.Add($"{reader.GetString(0)}|{reader.GetString(1)}");
            }
        }

        Assert.Equal(expected.Count, actual.Count);
        Assert.True(expected.SetEquals(actual), "NoteTags relationships changed across the migration.");
    }

    [Fact]
    public void An_integrity_failure_rolls_back_and_restores_enforcement()
    {
        // VerifyForeignKeyIntegrity throws StorageException, not SqliteException,
        // so it takes a different catch path from a SQL error. That path must
        // still roll back and still restore enforcement.
        var database = new NotoDatabase(_temp.DatabasePath);
        database.Initialize();

        using var connection = database.OpenConnection();
        int versionBefore = MigrationRunner.GetSchemaVersion(connection);

        var danglingReference = new Migration(
            versionBefore + 1,
            "creates a dangling foreign key",
            "INSERT INTO NoteTags (NoteId, TagId) VALUES ('ghost-note', 'ghost-tag');",
            RequiresForeignKeysDisabled: true,
            TransformsUserData: true);

        var error = Assert.Throws<StorageException>(
            () => new MigrationRunner([danglingReference]).Run(connection));

        Assert.Equal(StorageFailure.MigrationFailed, error.Reason);

        // The dangling row was not committed.
        Assert.Equal(0, ScalarLong(connection, "SELECT COUNT(*) FROM NoteTags;"));
        Assert.Equal(versionBefore, MigrationRunner.GetSchemaVersion(connection));

        // And enforcement came back despite the non-SqliteException path.
        Assert.Equal(1, ScalarLong(connection, "PRAGMA foreign_keys;"));
    }

    [Fact]
    public void Foreign_keys_remain_valid_and_enforced_after_the_upgrade()
    {
        using (var v1 = OpenAtV1())
        {
            InsertV1Note(v1, "t", "c");
        }

        SqliteConnection.ClearAllPools();
        UpgradeToLatest();

        using var connection = new NotoDatabase(_temp.DatabasePath).OpenConnection();

        // No dangling references left by the rebuild.
        Assert.Equal(0, ScalarLong(connection, "SELECT COUNT(*) FROM pragma_foreign_key_check;"));

        // And enforcement is back on — the migration must not leave it off.
        Assert.Equal(1, ScalarLong(connection, "PRAGMA foreign_keys;"));

        using var violate = connection.CreateCommand();
        violate.CommandText =
            "INSERT INTO Notes (Id, FolderId, Content, CreatedAt, UpdatedAt) VALUES ($id, 'nope', '', $n, $n);";
        violate.Parameters.AddWithValue("$id", Ulid.NewId());
        violate.Parameters.AddWithValue("$n", DateTimeOffset.UtcNow.ToString("O"));

        Assert.Throws<SqliteException>(() => violate.ExecuteNonQuery());
    }

    [Fact]
    public void Many_notes_all_survive_with_their_ordering_and_flags()
    {
        var ids = new List<string>();

        using (var v1 = OpenAtV1())
        {
            for (int i = 0; i < 50; i++)
            {
                string id = Ulid.NewId();
                string now = DateTimeOffset.UtcNow.ToString("O");
                Execute(v1,
                    """
                    INSERT INTO Notes (Id, Title, Content, IsPinned, SortOrder, CreatedAt, UpdatedAt)
                    VALUES ($id, $title, $content, $pinned, $order, $now, $now);
                    """,
                    ("$id", id),
                    ("$title", $"Title {i}"),
                    ("$content", $"Body {i}"),
                    ("$pinned", i % 2),
                    ("$order", (double)i),
                    ("$now", now));
                ids.Add(id);
            }
        }

        SqliteConnection.ClearAllPools();
        UpgradeToLatest();

        using var connection = new NotoDatabase(_temp.DatabasePath).OpenConnection();

        Assert.Equal(50, ScalarLong(connection, "SELECT COUNT(*) FROM Notes;"));
        Assert.Equal(25, ScalarLong(connection, "SELECT COUNT(*) FROM Notes WHERE IsPinned = 1;"));

        // SortOrder survived the rebuild for every row.
        Assert.Equal(
            ids.Count,
            ScalarLong(connection, "SELECT COUNT(DISTINCT SortOrder) FROM Notes;"));
    }

    // ---- idempotency and safety ----------------------------------------

    [Fact]
    public void Running_against_an_already_migrated_database_changes_nothing()
    {
        string id;
        using (var v1 = OpenAtV1())
        {
            id = InsertV1Note(v1, "Shopping List", "Milk");
        }

        SqliteConnection.ClearAllPools();
        UpgradeToLatest();

        using (var first = new NotoDatabase(_temp.DatabasePath).OpenConnection())
        {
            Assert.Equal("# Shopping List\n\nMilk", ContentOf(first, id));
        }

        SqliteConnection.ClearAllPools();
        UpgradeToLatest();   // second run

        using var second = new NotoDatabase(_temp.DatabasePath).OpenConnection();

        // Not transformed twice.
        Assert.Equal("# Shopping List\n\nMilk", ContentOf(second, id));
        Assert.Equal(2, MigrationRunner.GetSchemaVersion(second));
    }

    [Fact]
    public void A_safety_copy_is_written_before_a_data_transforming_migration()
    {
        using (var v1 = OpenAtV1())
        {
            InsertV1Note(v1, "Shopping List", "Milk");
        }

        SqliteConnection.ClearAllPools();
        UpgradeToLatest();

        string directory = Path.GetDirectoryName(_temp.DatabasePath)!;
        var copies = Directory.GetFiles(directory, "*premigration-v1*.db");

        Assert.Single(copies);

        // The copy is a usable database still at v1, holding the pre-migration
        // data — that is the whole point of taking it.
        using var copy = new SqliteConnection($"Data Source={copies[0]}");
        copy.Open();

        Assert.Equal(1, MigrationRunner.GetSchemaVersion(copy));
        Assert.Contains("Title", Columns(copy, "Notes"));
        Assert.Equal("Shopping List", ScalarString(copy, "SELECT Title FROM Notes LIMIT 1;"));
    }

    [Fact]
    public void A_fresh_database_needs_no_safety_copy()
    {
        // Nothing to protect: migration 001 creates tables on an empty file.
        UpgradeToLatest();

        string directory = Path.GetDirectoryName(_temp.DatabasePath)!;
        Assert.Empty(Directory.GetFiles(directory, "*premigration*"));
    }

    [Fact]
    public void A_failing_v2_style_migration_rolls_back_and_restores_foreign_keys()
    {
        using (var v1 = OpenAtV1())
        {
            InsertV1Note(v1, "kept", "body");
        }

        SqliteConnection.ClearAllPools();

        using var connection = new NotoDatabase(_temp.DatabasePath).OpenConnection();

        var broken = new Migration(
            2,
            "deliberately broken rebuild",
            """
            CREATE TABLE Notes_new (Id TEXT PRIMARY KEY NOT NULL) STRICT;
            DROP TABLE Notes;
            THIS IS NOT VALID SQL;
            """,
            RequiresForeignKeysDisabled: true,
            TransformsUserData: true);

        var error = Assert.Throws<StorageException>(
            () => new MigrationRunner([broken]).Run(connection));

        Assert.Equal(StorageFailure.MigrationFailed, error.Reason);

        // The original table, its Title column and its data are all still there.
        Assert.Equal(1, MigrationRunner.GetSchemaVersion(connection));
        Assert.Contains("Title", Columns(connection, "Notes"));
        Assert.Equal("kept", ScalarString(connection, "SELECT Title FROM Notes LIMIT 1;"));

        // And enforcement was restored despite the failure — the finally block.
        Assert.Equal(1, ScalarLong(connection, "PRAGMA foreign_keys;"));
    }
}
