using System.Globalization;
using Microsoft.Data.Sqlite;
using Noto.Core.Storage;

namespace Noto.Infrastructure.Storage;

/// <summary>
/// Takes a consistent copy of the database immediately before a migration that
/// transforms user data.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is not Noto's backup feature.</b> It is a narrowly scoped safety net
/// for one moment: the instant before a migration rewrites rows the user wrote.
/// There is deliberately no scheduler, no retention policy, no restore command,
/// no UI and no manager. Those belong to the backup feature, which is a
/// separate milestone.
/// </para>
/// <para>
/// <b>Why not a file copy.</b> The database runs in WAL mode, so committed data
/// can live in <c>-wal</c> rather than the main file. Copying the <c>.db</c>
/// alone can capture a torn, older, or internally inconsistent state, and
/// copying all three files while a connection is open is a race. SQLite's own
/// backup API reads through the engine and produces a consistent single-file
/// snapshot, which is what <c>SqliteConnection.BackupDatabase</c> exposes.
/// </para>
/// </remarks>
public static class MigrationSafetyCopy
{
    /// <summary>
    /// Copies <paramref name="sourceConnection"/>'s database beside it, named
    /// for the version being migrated away from.
    /// </summary>
    /// <returns>The path written.</returns>
    public static string Create(
        SqliteConnection sourceConnection,
        string databasePath,
        int fromVersion,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(sourceConnection);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        string directory = Path.GetDirectoryName(databasePath)
            ?? throw new StorageException(
                StorageFailure.CannotOpen,
                $"Cannot determine a directory for '{databasePath}'.");

        DateTimeOffset now = (timeProvider ?? TimeProvider.System).GetUtcNow();

        // Sortable, filename-safe, and it says what it is without needing a
        // lookup: the schema version it was taken before.
        string name = string.Create(
            CultureInfo.InvariantCulture,
            $"{Path.GetFileNameWithoutExtension(databasePath)}-premigration-v{fromVersion}-{now:yyyyMMdd'T'HHmmss}.db");

        string destination = Path.Join(directory, name);

        try
        {
            Directory.CreateDirectory(directory);

            // Removing a same-second leftover keeps a retried migration
            // deterministic rather than failing on an existing file.
            if (File.Exists(destination))
            {
                File.Delete(destination);
            }

            using var target = new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = destination,
                    Mode = SqliteOpenMode.ReadWriteCreate,
                }.ToString());

            target.Open();

            // Reads through the engine, so it sees committed WAL content and
            // produces a consistent snapshot in one file.
            sourceConnection.BackupDatabase(target);

            return destination;
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            // Failing to protect the data means not proceeding to modify it.
            // The path is infrastructure, not user content, so naming it is safe
            // and without it this is undiagnosable.
            throw new StorageException(
                StorageFailure.CannotOpen,
                $"Could not write the pre-migration safety copy to '{destination}'. "
                + "The migration was not attempted and the database is unchanged.",
                ex);
        }
    }
}
