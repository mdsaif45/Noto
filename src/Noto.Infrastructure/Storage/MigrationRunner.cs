using Microsoft.Data.Sqlite;
using Noto.Core.Storage;
using Noto.Infrastructure.Storage.Migrations;

namespace Noto.Infrastructure.Storage;

/// <summary>One ordered, irreversible schema change.</summary>
/// <param name="Version">1-based, contiguous, and never reused.</param>
/// <param name="Description">What it does, for logs and diagnostics.</param>
/// <param name="Sql">The statements, applied inside a transaction.</param>
/// <param name="RequiresForeignKeysDisabled">
/// Whether foreign key enforcement must be suspended while this migration runs.
/// </param>
/// <param name="TransformsUserData">
/// Whether this migration rewrites rows the user authored, rather than only
/// creating or extending empty structures.
/// </param>
/// <remarks>
/// <para>
/// <b>On <c>RequiresForeignKeysDisabled</c>.</b> SQLite's table-rebuild pattern
/// (create new, copy, drop old, rename) fires <c>ON DELETE</c> actions when the
/// old table is dropped. With enforcement on, rebuilding <c>Notes</c> cascades
/// into <c>NoteTags</c> and destroys every tag relationship — measured, not
/// assumed: a probe rebuilt a table with one child row and the child count went
/// from 1 to 0, while <c>PRAGMA foreign_key_check</c> still reported clean.
/// </para>
/// <para>
/// <c>PRAGMA foreign_keys</c> is a <b>no-op inside a transaction</b>, also
/// measured. So the runner must toggle it around the transaction, which is why
/// this is a property of the migration rather than something its SQL can do.
/// </para>
/// <para>
/// <b>On <c>TransformsUserData</c>.</b> Migration 001 created tables on an
/// empty file; there was nothing to lose. A migration that rewrites note
/// content can lose it, so those run behind a pre-migration safety copy. The
/// two flags are independent: a migration could rebuild a table without
/// touching user content, or rewrite content without rebuilding.
/// </para>
/// </remarks>
public sealed record Migration(
    int Version,
    string Description,
    string Sql,
    bool RequiresForeignKeysDisabled = false,
    bool TransformsUserData = false);

/// <summary>
/// Applies pending migrations, tracked by <c>PRAGMA user_version</c> (ADR-003).
/// </summary>
/// <remarks>
/// <para>
/// This is the most dangerous code in Noto. A migration that works on a fresh
/// install and corrupts an existing database is the worst bug this product can
/// have, because ADR-002 means the data cannot be recovered from a server.
/// </para>
/// <para>
/// Three rules hold it together:
/// </para>
/// <list type="number">
///   <item>migrations are <b>append-only</b> — a shipped migration is never edited</item>
///   <item>each runs in a <b>transaction</b>, with the version bump inside it,
///         so a failure leaves the database exactly as it was</item>
///   <item>a database from a <b>newer</b> Noto is refused rather than modified</item>
/// </list>
/// <para>
/// <c>PRAGMA user_version</c> is used rather than a version table because it is
/// SQLite's own mechanism and is readable before any table exists.
/// </para>
/// </remarks>
public sealed class MigrationRunner
{
    private readonly IStorageLog _log;
    private readonly IReadOnlyList<Migration> _migrations;

    public MigrationRunner(IStorageLog? log = null)
        : this(Migrations, log)
    {
    }

    /// <summary>
    /// Runs a supplied migration list instead of the shipped one.
    /// </summary>
    /// <remarks>
    /// Exists so tests exercise <b>this</b> runner rather than a copy of its
    /// logic — a duplicated runner in a test can pass while the real one is
    /// broken.
    /// </remarks>
    public MigrationRunner(IReadOnlyList<Migration> migrations, IStorageLog? log = null)
    {
        ArgumentNullException.ThrowIfNull(migrations);
        _migrations = migrations;
        _log = log ?? NullStorageLog.Instance;
    }

    /// <summary>All shipped migrations, in order. Append only.</summary>
    public static IReadOnlyList<Migration> Migrations { get; } =
    [
        new(1, "Initial schema: folders, notes, tags", SchemaV1.Sql),
        new(2, "Align with parity: drop Notes.Title, add Folders.IsPinned and DeletedAt",
            SchemaV2.Sql, RequiresForeignKeysDisabled: true, TransformsUserData: true),
    ];

    /// <summary>The version a fully migrated database reports.</summary>
    public static int LatestVersion => Migrations.Count == 0 ? 0 : Migrations[^1].Version;

    /// <summary>Reads the schema version of an open database.</summary>
    public static int GetSchemaVersion(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Applies every pending migration. Does nothing if already current.
    /// </summary>
    /// <returns>How many migrations were applied.</returns>
    public int Run(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        int current = GetSchemaVersion(connection);
        int target = _migrations.Count == 0 ? 0 : _migrations[^1].Version;

        if (current > target)
        {
            // An older Noto opening a newer database. Continuing could write
            // data the newer schema cannot represent, so refuse.
            throw new StorageException(
                StorageFailure.SchemaTooNew,
                $"The database schema is version {current}, but this build understands "
                + $"version {target}. Update Noto to open it.");
        }

        var pending = _migrations.Where(m => m.Version > current).OrderBy(m => m.Version).ToArray();

        if (pending.Length == 0)
        {
            return 0;
        }

        _log.MigrationStarting(current, target, pending.Length);

        foreach (var migration in pending)
        {
            Apply(connection, migration);
        }

        _log.MigrationCompleted(target);
        return pending.Length;
    }

    private static void VerifyForeignKeyIntegrity(
        SqliteConnection connection, SqliteTransaction transaction, Migration migration)
    {
        using var check = connection.CreateCommand();
        check.Transaction = transaction;
        check.CommandText = "SELECT COUNT(*) FROM pragma_foreign_key_check;";

        long violations = Convert.ToInt64(
            check.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);

        if (violations > 0)
        {
            throw new StorageException(
                StorageFailure.MigrationFailed,
                $"Migration {migration.Version} left {violations} foreign key violation(s). "
                + "Rolled back; the database is unchanged.");
        }
    }

    private void Apply(SqliteConnection connection, Migration migration)
    {
        // Toggled OUTSIDE the transaction: PRAGMA foreign_keys is silently
        // ignored inside one. Restored in the finally block so enforcement is
        // never left off, including when the migration throws.
        if (migration.RequiresForeignKeysDisabled)
        {
            SetForeignKeys(connection, enabled: false);
        }

        try
        {
            ApplyCore(connection, migration);
        }
        finally
        {
            if (migration.RequiresForeignKeysDisabled)
            {
                SetForeignKeys(connection, enabled: true);
            }
        }
    }

    private static void SetForeignKeys(SqliteConnection connection, bool enabled)
    {
        using var command = connection.CreateCommand();
        command.CommandText = enabled ? "PRAGMA foreign_keys = ON;" : "PRAGMA foreign_keys = OFF;";
        command.ExecuteNonQuery();
    }

    private void ApplyCore(SqliteConnection connection, Migration migration)
    {
        using var transaction = connection.BeginTransaction();

        try
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = migration.Sql;
                command.ExecuteNonQuery();
            }

            // The version bump is inside the same transaction, so the schema
            // and its recorded version can never disagree.
            using (var versionCommand = connection.CreateCommand())
            {
                versionCommand.Transaction = transaction;

                // PRAGMA does not accept parameters. The value is an int from a
                // compile-time constant list, never user input, so interpolation
                // is safe here — and it is the only place in Noto where SQL is
                // not parameterised.
                versionCommand.CommandText = string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"PRAGMA user_version = {migration.Version};");
                versionCommand.ExecuteNonQuery();
            }

            // A rebuild that leaves a dangling reference must not be committed.
            // Checked inside the transaction so a failure rolls the whole
            // migration back.
            if (migration.RequiresForeignKeysDisabled)
            {
                VerifyForeignKeyIntegrity(connection, transaction, migration);
            }

            transaction.Commit();
            _log.MigrationApplied(migration.Version, migration.Description);
        }
        catch (SqliteException ex)
        {
            transaction.Rollback();

            _log.MigrationFailed(migration.Version, ex);

            throw new StorageException(
                StorageFailure.MigrationFailed,
                $"Migration {migration.Version} ('{migration.Description}') failed and was "
                + "rolled back. The database is unchanged.",
                ex);
        }
        catch (Exception ex)
        {
            // Anything else — notably the StorageException that
            // VerifyForeignKeyIntegrity throws, which is not a SqliteException.
            //
            // Disposing the transaction would roll back anyway, but rolling back
            // explicitly means the guarantee does not depend on a `using` that a
            // later edit might remove without realising it is load-bearing.
            transaction.Rollback();

            _log.MigrationFailed(migration.Version, ex);
            throw;
        }
    }
}
