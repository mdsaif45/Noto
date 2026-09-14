using Microsoft.Data.Sqlite;
using Noto.Core.Storage;
using Noto.Infrastructure.Storage;
using Xunit;

namespace Noto.Infrastructure.Tests.Storage;

/// <summary>
/// Migrations are the most dangerous code in Noto — a migration that works on a
/// fresh install and corrupts an existing database destroys data that ADR-002
/// says cannot be recovered from a server.
/// </summary>
/// <remarks>
/// Every test uses a temporary database in its own directory. Nothing here ever
/// touches <c>%LOCALAPPDATA%\Noto</c>, so running the suite cannot damage a
/// developer's real notes.
/// </remarks>
public sealed class MigrationTests : IDisposable
{
    private readonly TempDatabase _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Fresh_database_reaches_the_latest_schema()
    {
        var database = new NotoDatabase(_temp.DatabasePath);

        database.Initialize();

        using var connection = database.OpenConnection();
        Assert.Equal(MigrationRunner.LatestVersion, MigrationRunner.GetSchemaVersion(connection));
    }

    [Fact]
    public void Fresh_database_creates_every_expected_table()
    {
        var database = new NotoDatabase(_temp.DatabasePath);
        database.Initialize();

        using var connection = database.OpenConnection();
        var tables = QueryTableNames(connection);

        Assert.Contains("Folders", tables);
        Assert.Contains("Notes", tables);
        Assert.Contains("Tags", tables);
        Assert.Contains("NoteTags", tables);
        Assert.Contains("Settings", tables);
    }

    [Fact]
    public void Tables_deferred_to_later_issues_are_not_created()
    {
        // Guards against scope creep in the schema: creating a table before the
        // feature that needs it invites code that half-supports it.
        var database = new NotoDatabase(_temp.DatabasePath);
        database.Initialize();

        using var connection = database.OpenConnection();
        var tables = QueryTableNames(connection);

        Assert.DoesNotContain("Attachments", tables);
        Assert.DoesNotContain("NotesFts", tables);
        Assert.DoesNotContain("NotePresentations", tables);
        Assert.DoesNotContain("ContextBindings", tables);
    }

    [Fact]
    public void Running_migrations_twice_changes_nothing()
    {
        var database = new NotoDatabase(_temp.DatabasePath);

        database.Initialize();

        using var connection = database.OpenConnection();
        int applied = new MigrationRunner().Run(connection);

        Assert.Equal(0, applied);
        Assert.Equal(MigrationRunner.LatestVersion, MigrationRunner.GetSchemaVersion(connection));
    }

    [Fact]
    public void An_older_database_upgrades_to_the_latest_schema()
    {
        // The upgrade path, not just the fresh path. ADR-003 requires this:
        // a migration correct on an empty database can still corrupt one with
        // data in it.
        using (var connection = new SqliteConnection($"Data Source={_temp.DatabasePath}"))
        {
            connection.Open();
            SetSchemaVersion(connection, 0);
        }

        var database = new NotoDatabase(_temp.DatabasePath);
        database.Initialize();

        using var verify = database.OpenConnection();
        Assert.Equal(MigrationRunner.LatestVersion, MigrationRunner.GetSchemaVersion(verify));
    }

    [Fact]
    public void A_failing_migration_rolls_back_and_leaves_the_database_unchanged()
    {
        // The claim that matters most. Never assert rollback safety without
        // proving it.
        var database = new NotoDatabase(_temp.DatabasePath);
        database.Initialize();

        using var connection = database.OpenConnection();
        int versionBefore = MigrationRunner.GetSchemaVersion(connection);
        var tablesBefore = QueryTableNames(connection);

        // A migration whose first statement succeeds and whose second fails,
        // so a non-transactional runner would leave the first applied.
        var broken = new Migration(
            versionBefore + 1,
            "deliberately broken",
            """
            CREATE TABLE ShouldNotSurvive (Id TEXT PRIMARY KEY NOT NULL) STRICT;
            THIS IS NOT VALID SQL;
            """);

        var runner = new MigrationRunner([broken]);

        var error = Assert.Throws<StorageException>(() => runner.Run(connection));
        Assert.Equal(StorageFailure.MigrationFailed, error.Reason);

        // Version unchanged.
        Assert.Equal(versionBefore, MigrationRunner.GetSchemaVersion(connection));

        // And the half-created table is gone — this is the part a
        // non-transactional runner would fail.
        var tablesAfter = QueryTableNames(connection);
        Assert.DoesNotContain("ShouldNotSurvive", tablesAfter);
        Assert.Equal(tablesBefore.OrderBy(t => t), tablesAfter.OrderBy(t => t));
    }

    [Fact]
    public void A_database_from_a_newer_build_is_refused_rather_than_modified()
    {
        var database = new NotoDatabase(_temp.DatabasePath);
        database.Initialize();

        using var connection = database.OpenConnection();
        SetSchemaVersion(connection, MigrationRunner.LatestVersion + 99);

        var error = Assert.Throws<StorageException>(() => new MigrationRunner().Run(connection));

        Assert.Equal(StorageFailure.SchemaTooNew, error.Reason);
        Assert.Equal(MigrationRunner.LatestVersion + 99, MigrationRunner.GetSchemaVersion(connection));
    }

    [Fact]
    public void Migration_versions_are_contiguous_and_start_at_one()
    {
        // Cheap structural guard: a duplicated or skipped version would make
        // the upgrade path silently wrong.
        var versions = MigrationRunner.Migrations.Select(m => m.Version).ToArray();

        Assert.Equal(versions, versions.Distinct().ToArray());
        Assert.Equal(versions, versions.OrderBy(v => v).ToArray());
        Assert.Equal(Enumerable.Range(1, versions.Length), versions);
    }

    private static List<string> QueryTableNames(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';";

        var names = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static void SetSchemaVersion(SqliteConnection connection, int version)
    {
        using var command = connection.CreateCommand();
        command.CommandText = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"PRAGMA user_version = {version};");
        command.ExecuteNonQuery();
    }

}
