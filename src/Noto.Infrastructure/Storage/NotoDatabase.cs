using Microsoft.Data.Sqlite;
using Noto.Core.Storage;

namespace Noto.Infrastructure.Storage;

/// <summary>
/// Opens and configures the Noto database (ADR-003).
/// </summary>
/// <remarks>
/// <para>
/// Deliberately small: open a connection, apply the pragmas, run migrations.
/// It is not a repository framework — repositories arrive with the entities
/// they serve (#13).
/// </para>
/// <para>
/// Connections are created per operation rather than held open.
/// <c>Microsoft.Data.Sqlite</c> pools them, so this is cheap, and it avoids a
/// long-lived connection holding a WAL read snapshot open indefinitely.
/// </para>
/// </remarks>
public sealed class NotoDatabase
{
    private readonly string _connectionString;
    private readonly IStorageLog _log;

    public NotoDatabase(string databasePath, IStorageLog? log = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        DatabasePath = databasePath;
        _log = log ?? NullStorageLog.Instance;

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,

            // Default. Serialized would add locking Noto does not need, since
            // each operation uses its own connection.
            Cache = SqliteCacheMode.Default,

            // Fail fast on a genuinely stuck lock rather than hanging the UI.
            // Applied again as a pragma for connections that bypass this.
            DefaultTimeout = 5,
        }.ToString();
    }

    public string DatabasePath { get; }

    /// <summary>
    /// Opens a configured connection. The caller disposes it.
    /// </summary>
    public SqliteConnection OpenConnection()
    {
        SqliteConnection? connection = null;

        try
        {
            connection = new SqliteConnection(_connectionString);
            connection.Open();
            ApplyPragmas(connection);
            return connection;
        }
        catch (SqliteException ex)
        {
            connection?.Dispose();

            // The path is infrastructure, not user content, so it is safe to
            // name — and without it this error is undiagnosable.
            throw new StorageException(
                StorageFailure.CannotOpen,
                $"Could not open the database at '{DatabasePath}'.",
                ex);
        }
    }

    /// <summary>
    /// Pragmas Noto sets, each with a reason. Defaults are left alone.
    /// </summary>
    private static void ApplyPragmas(SqliteConnection connection)
    {
        // foreign_keys — OFF by default in SQLite, for backwards compatibility.
        // Noto relies on cascade deletes (a note's attachments, a folder's
        // notes), so without this the schema's declared relationships are
        // documentation rather than constraints.
        Execute(connection, "PRAGMA foreign_keys = ON;");

        // journal_mode = WAL — readers do not block the writer, which keeps the
        // UI responsive while a save is in flight. It is persistent, so this is
        // effectively a one-time setting, and it is why backups must be proper
        // snapshots rather than file copies (data-model.md).
        Execute(connection, "PRAGMA journal_mode = WAL;");

        // synchronous = NORMAL — the deliberate durability decision.
        //
        //   FULL   : fsync on every commit. Survives OS crash and power loss.
        //   NORMAL : fsync at checkpoints. Survives application crash; a power
        //            loss can lose the most recent transactions.
        //   OFF    : no fsync. Can corrupt the database. Never.
        //
        // NORMAL is chosen because with WAL it is durable across the failure
        // Noto actually faces — the application crashing — while FULL would
        // fsync on every keystroke-triggered autosave. It is the documented
        // recommendation for WAL mode.
        //
        // This is a real trade: a power cut may cost the last few seconds of
        // typing. Accepted because autosave is continuous, so the window is
        // small, and because OFF (which risks the whole file) is the only
        // faster option.
        Execute(connection, "PRAGMA synchronous = NORMAL;");

        // busy_timeout — wait rather than failing instantly if another
        // connection holds a write lock. 5s is long enough for any local
        // contention and short enough to surface a genuine deadlock.
        Execute(connection, "PRAGMA busy_timeout = 5000;");

        // Deliberately NOT set, so the reason is recorded rather than rediscovered:
        //   cache_size    — the default is fine for a store of this size;
        //                   tuning it without a measured problem is guessing.
        //   mmap_size     — has caused corruption reports on some Windows
        //                   configurations; not worth it for local data.
        //   temp_store    — the default already uses memory where it matters.
        //   auto_vacuum   — VACUUM renumbers implicit rowids, which would
        //                   desynchronise the FTS5 index (ADR-012). It must be
        //                   deliberate and followed by a rebuild, never automatic.
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Brings the database to the current schema version. Safe to call on every
    /// start: it does nothing when already current.
    /// </summary>
    public void Initialize()
    {
        using var connection = OpenConnection();
        var runner = new MigrationRunner(_log);
        runner.Run(connection);
    }
}
