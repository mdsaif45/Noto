using Microsoft.Data.Sqlite;
using Noto.Core.Folders;
using Noto.Core.Storage;

namespace Noto.Infrastructure.Storage;

/// <summary>
/// The SQLite implementation of <see cref="IFolderRepository"/>.
/// </summary>
/// <remarks>
/// <para>
/// Hand-written SQL over <c>Microsoft.Data.Sqlite</c>, with no ORM, matching
/// <see cref="SqliteNoteRepository"/> (ADR-003). Every SQLite concern stops
/// here: failures become <see cref="StorageException"/>, rows become domain
/// entities, and no <c>Sqlite*</c> type crosses back out.
/// </para>
/// <para>
/// Ordering is delegated to <see cref="SortOrderEngine"/> rather than
/// reimplemented. The folder collection is a single scope
/// (<see cref="SortOrderScope.AllFolders"/>), which is the degenerate case of
/// the same O1–O6 rules the notes use.
/// </para>
/// </remarks>
public sealed class SqliteFolderRepository(NotoDatabase database) : IFolderRepository
{
    private readonly NotoDatabase _database = database
        ?? throw new ArgumentNullException(nameof(database));

    public FolderLifecycle GetLifecycle(FolderId id)
    {
        try
        {
            using var connection = _database.OpenConnection();
            using var command = connection.CreateCommand();

            command.CommandText = "SELECT DeletedAt FROM Folders WHERE Id = $id;";
            command.Parameters.AddWithValue("$id", id.Value);

            using var reader = command.ExecuteReader();

            if (!reader.Read())
            {
                return FolderLifecycle.Missing;
            }

            return reader.IsDBNull(0) ? FolderLifecycle.Active : FolderLifecycle.Deleted;
        }
        catch (SqliteException ex)
        {
            throw new StorageException(
                StorageFailure.Unknown,
                $"Could not read the state of folder '{id}'.",
                ex);
        }
    }

    public Folder? FindActive(FolderId id)
    {
        try
        {
            using var connection = _database.OpenConnection();
            using var command = connection.CreateCommand();

            // Invariant I1: the DeletedAt filter lives here, in the query, so
            // that a caller cannot forget it. The recycle bin is reached only
            // through ListDeleted (I2).
            command.CommandText =
                """
                SELECT Id, Name, ColorKey, IsPinned, IsCollapsed,
                       SortOrder, CreatedAt, UpdatedAt, DeletedAt
                FROM Folders
                WHERE Id = $id AND DeletedAt IS NULL;
                """;
            command.Parameters.AddWithValue("$id", id.Value);

            using var reader = command.ExecuteReader();

            return reader.Read() ? ReadFolder(reader) : null;
        }
        catch (SqliteException ex)
        {
            throw new StorageException(
                StorageFailure.Unknown,
                $"Could not read folder '{id}'.",
                ex);
        }
    }

    public void Add(Folder folder)
    {
        ArgumentNullException.ThrowIfNull(folder);

        try
        {
            using var connection = _database.OpenConnection();
            using var command = connection.CreateCommand();

            command.CommandText =
                """
                INSERT INTO Folders
                    (Id, Name, ColorKey, IsPinned, IsCollapsed,
                     SortOrder, CreatedAt, UpdatedAt, DeletedAt)
                VALUES
                    ($id, $name, $colorKey, $isPinned, $isCollapsed,
                     $sortOrder, $createdAt, $updatedAt, $deletedAt);
                """;

            command.Parameters.AddWithValue("$id", folder.Id.Value);
            command.Parameters.AddWithValue("$name", folder.Name);
            command.Parameters.AddWithValue("$colorKey", (object?)folder.ColorKey ?? DBNull.Value);
            command.Parameters.AddWithValue("$isPinned", folder.IsPinned ? 1 : 0);
            command.Parameters.AddWithValue("$isCollapsed", folder.IsCollapsed ? 1 : 0);
            command.Parameters.AddWithValue("$sortOrder", folder.SortOrder);
            command.Parameters.AddWithValue("$createdAt", Timestamps.Format(folder.CreatedAt));
            command.Parameters.AddWithValue("$updatedAt", Timestamps.Format(folder.UpdatedAt));
            command.Parameters.AddWithValue(
                "$deletedAt",
                folder.DeletedAt is { } deletedAt ? Timestamps.Format(deletedAt) : DBNull.Value);

            command.ExecuteNonQuery();
        }
        catch (SqliteException ex)
        {
            throw new StorageException(
                StorageFailure.Unknown,
                $"Could not insert folder '{folder.Id}'.",
                ex);
        }
    }

    public void Rename(FolderId id, string name, DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(name);

        // Name only. No uniqueness check and no unique index: duplicate folder
        // names are legal (Case G), and adding one here would turn a supported
        // state into a failure.
        ExecuteSingleRowUpdate(
            "UPDATE Folders SET Name = $name, UpdatedAt = $updatedAt "
            + "WHERE Id = $id AND DeletedAt IS NULL;",
            id,
            updatedAt,
            command => command.Parameters.AddWithValue("$name", name),
            $"Could not rename folder '{id}'.");
    }

    public void SetPinned(FolderId id, bool isPinned, DateTimeOffset updatedAt)
    {
        // IsPinned only — SortOrder is deliberately absent from this statement.
        ExecuteSingleRowUpdate(
            "UPDATE Folders SET IsPinned = $isPinned, UpdatedAt = $updatedAt "
            + "WHERE Id = $id AND DeletedAt IS NULL;",
            id,
            updatedAt,
            command => command.Parameters.AddWithValue("$isPinned", isPinned ? 1 : 0),
            $"Could not set the pinned flag on folder '{id}'.");
    }

    public bool Reorder(FolderId id, FolderPlacement placement, DateTimeOffset updatedAt)
    {
        try
        {
            using var connection = _database.OpenConnection();
            using var transaction = connection.BeginTransaction();

            SortOrderScope scope = SortOrderScope.AllFolders;
            SortOrderPlacement enginePlacement = ToEnginePlacement(placement);
            double current = ReadSortOrder(connection, transaction, id);

            if (SortOrderEngine.AlreadyInPosition(
                connection, transaction, scope, enginePlacement, id.Value, current))
            {
                // Same position: write nothing, stamp nothing (contract §5).
                // Rolling back rather than committing an empty transaction
                // keeps "no-op" literally true at the database.
                transaction.Rollback();
                return false;
            }

            double sortOrder = SortOrderEngine.ResolvePosition(
                connection, transaction, scope, enginePlacement, id.Value, updatedAt);

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText =
                    """
                    UPDATE Folders
                    SET SortOrder = $sortOrder, UpdatedAt = $updatedAt
                    WHERE Id = $id AND DeletedAt IS NULL;
                    """;
                command.Parameters.AddWithValue("$sortOrder", sortOrder);
                command.Parameters.AddWithValue("$updatedAt", Timestamps.Format(updatedAt));
                command.Parameters.AddWithValue("$id", id.Value);
                command.ExecuteNonQuery();
            }

            transaction.Commit();
            return true;
        }
        catch (SqliteException ex)
        {
            throw new StorageException(
                StorageFailure.Unknown,
                $"Could not reorder folder '{id}'.",
                ex);
        }
    }

    public void SoftDeleteWithNotes(FolderId id, DateTimeOffset deletedAt)
    {
        try
        {
            using var connection = _database.OpenConnection();
            using var transaction = connection.BeginTransaction();

            // One timestamp, formatted once, used by both statements: the
            // folder and every note it takes with it report the same instant
            // (contract §4). Calling the clock per row would produce a cascade
            // whose rows disagree about when it happened.
            string stamp = Timestamps.Format(deletedAt);

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText =
                    """
                    UPDATE Folders
                    SET DeletedAt = $at, UpdatedAt = $at
                    WHERE Id = $id AND DeletedAt IS NULL;
                    """;
                command.Parameters.AddWithValue("$at", stamp);
                command.Parameters.AddWithValue("$id", id.Value);
                command.ExecuteNonQuery();
            }

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;

                // `AND DeletedAt IS NULL` is Case B, and it is not optional.
                // Without it this overwrites the DeletedAt of a note the user
                // had already deleted, silently rewriting when that happened —
                // and RestoreFolder would then be unable to tell the two apart
                // either, because nothing records why a note was deleted.
                command.CommandText =
                    """
                    UPDATE Notes
                    SET DeletedAt = $at, UpdatedAt = $at
                    WHERE FolderId = $folderId AND DeletedAt IS NULL;
                    """;
                command.Parameters.AddWithValue("$at", stamp);
                command.Parameters.AddWithValue("$folderId", id.Value);
                command.ExecuteNonQuery();
            }

            transaction.Commit();
        }
        catch (SqliteException ex)
        {
            throw new StorageException(
                StorageFailure.Unknown,
                $"Could not delete folder '{id}' and its notes.",
                ex);
        }
    }

    public void RestoreWithNotes(FolderId id, DateTimeOffset updatedAt)
    {
        try
        {
            using var connection = _database.OpenConnection();
            using var transaction = connection.BeginTransaction();

            string stamp = Timestamps.Format(updatedAt);

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText =
                    """
                    UPDATE Folders
                    SET DeletedAt = NULL, UpdatedAt = $at
                    WHERE Id = $id AND DeletedAt IS NOT NULL;
                    """;
                command.Parameters.AddWithValue("$at", stamp);
                command.Parameters.AddWithValue("$id", id.Value);
                command.ExecuteNonQuery();
            }

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;

                // Case D, exactly as contract §8 writes it: EVERY still-deleted
                // note pointing at this folder, including one the user deleted
                // individually beforehand. There is no provenance column to
                // narrow this by, by design (deletion-semantics §5).
                //
                // `FolderId = $folderId` also confines it to this folder: notes
                // at root (FolderId IS NULL) never match, because SQL's
                // three-valued logic makes NULL = anything unknown (I3).
                command.CommandText =
                    """
                    UPDATE Notes
                    SET DeletedAt = NULL, UpdatedAt = $at
                    WHERE FolderId = $folderId AND DeletedAt IS NOT NULL;
                    """;
                command.Parameters.AddWithValue("$at", stamp);
                command.Parameters.AddWithValue("$folderId", id.Value);
                command.ExecuteNonQuery();
            }

            transaction.Commit();
        }
        catch (SqliteException ex)
        {
            throw new StorageException(
                StorageFailure.Unknown,
                $"Could not restore folder '{id}' and its notes.",
                ex);
        }
    }

    public double? MaxSortOrder()
    {
        try
        {
            using var connection = _database.OpenConnection();

            // The engine already expresses "highest active SortOrder in a
            // scope", and the folder collection is simply one scope.
            return SortOrderEngine.MaxSortOrder(connection, SortOrderScope.AllFolders);
        }
        catch (SqliteException ex)
        {
            throw new StorageException(
                StorageFailure.Unknown,
                "Could not read the highest folder sort order.",
                ex);
        }
    }

    public bool IsActiveSibling(FolderId id)
    {
        try
        {
            using var connection = _database.OpenConnection();
            using var command = connection.CreateCommand();

            command.CommandText =
                "SELECT 1 FROM Folders WHERE Id = $id AND DeletedAt IS NULL;";
            command.Parameters.AddWithValue("$id", id.Value);

            return command.ExecuteScalar() is not null;
        }
        catch (SqliteException ex)
        {
            throw new StorageException(
                StorageFailure.Unknown,
                $"Could not check whether folder '{id}' is active.",
                ex);
        }
    }

    public IReadOnlyList<Folder> ListDeleted()
    {
        try
        {
            using var connection = _database.OpenConnection();
            using var command = connection.CreateCommand();

            // The I2 inversion for folders, mirroring the note bin.
            command.CommandText =
                """
                SELECT Id, Name, ColorKey, IsPinned, IsCollapsed,
                       SortOrder, CreatedAt, UpdatedAt, DeletedAt
                FROM Folders
                WHERE DeletedAt IS NOT NULL
                ORDER BY DeletedAt DESC, Id ASC;
                """;

            var folders = new List<Folder>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                folders.Add(ReadFolder(reader));
            }

            return folders;
        }
        catch (SqliteException ex)
        {
            throw new StorageException(
                StorageFailure.Unknown,
                "Could not read the folder recycle bin.",
                ex);
        }
    }

    public IReadOnlyList<Folder> ListActive()
    {
        try
        {
            using var connection = _database.OpenConnection();
            using var command = connection.CreateCommand();

            // O2 in full — the same rule as the note list, and stated in §5a as
            // applying to both. I1 excludes the bin, which is reached only
            // through ListDeleted (I2).
            command.CommandText =
                """
                SELECT Id, Name, ColorKey, IsPinned, IsCollapsed,
                       SortOrder, CreatedAt, UpdatedAt, DeletedAt
                FROM Folders
                WHERE DeletedAt IS NULL
                ORDER BY IsPinned DESC, SortOrder ASC, Id ASC;
                """;

            var folders = new List<Folder>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                folders.Add(ReadFolder(reader));
            }

            return folders;
        }
        catch (SqliteException ex)
        {
            throw new StorageException(
                StorageFailure.Unknown,
                "Could not list the folders.",
                ex);
        }
    }

    /// <summary>
    /// Maps one row to a <see cref="Folder"/>.
    /// </summary>
    /// <remarks>
    /// Shared by <see cref="FindActive"/> and <see cref="ListDeleted"/>, which
    /// select the same columns in the same order. Two copies would be two
    /// places for a column index to drift.
    /// </remarks>
    private static Folder ReadFolder(SqliteDataReader reader) => new()
    {
        Id = FolderId.From(reader.GetString(0)),
        Name = reader.GetString(1),
        ColorKey = reader.IsDBNull(2) ? null : reader.GetString(2),
        IsPinned = reader.GetInt64(3) != 0,
        IsCollapsed = reader.GetInt64(4) != 0,
        SortOrder = reader.GetDouble(5),
        CreatedAt = Timestamps.Parse(reader.GetString(6)),
        UpdatedAt = Timestamps.Parse(reader.GetString(7)),
        DeletedAt = reader.IsDBNull(8) ? null : Timestamps.Parse(reader.GetString(8)),
    };

    /// <summary>
    /// Translates the domain's placement into the engine's row-level one.
    /// </summary>
    /// <remarks>
    /// The boundary where strong ids become plain row ids. The engine orders
    /// rows in a table; giving it <c>FolderId</c> would tie one algorithm to one
    /// domain, so the translation lives here rather than there.
    /// </remarks>
    private static SortOrderPlacement ToEnginePlacement(FolderPlacement placement)
    {
        if (placement.AtEnd)
        {
            return SortOrderPlacement.Last;
        }

        return placement.AfterSibling is { } sibling
            ? SortOrderPlacement.After(sibling.Value)
            : SortOrderPlacement.First;
    }

    /// <summary>
    /// The shape every single-column folder update shares: set one field, stamp
    /// <c>UpdatedAt</c>, touch nothing else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Written once rather than twice. Every statement passed here carries
    /// <c>AND DeletedAt IS NULL</c>, which is the persistence-layer half of
    /// invariant I5: the command layer checks the lifecycle first, and this
    /// clause means a folder binned between that check and this write is still
    /// not modified. The note repository keeps the same guard on its own
    /// single-field updates, and the folder cascades and reorder carry it too.
    /// </para>
    /// <para>
    /// No row matching is therefore not an error here — it is the
    /// concurrent-deletion case, and last-write-wins applies (§10).
    /// </para>
    /// </remarks>
    private void ExecuteSingleRowUpdate(
        string sql,
        FolderId id,
        DateTimeOffset updatedAt,
        Action<SqliteCommand> bind,
        string failureMessage)
    {
        try
        {
            using var connection = _database.OpenConnection();
            using var command = connection.CreateCommand();

            command.CommandText = sql;
            bind(command);
            command.Parameters.AddWithValue("$updatedAt", Timestamps.Format(updatedAt));
            command.Parameters.AddWithValue("$id", id.Value);

            command.ExecuteNonQuery();
        }
        catch (SqliteException ex)
        {
            throw new StorageException(StorageFailure.Unknown, failureMessage, ex);
        }
    }

    private static double ReadSortOrder(
        SqliteConnection connection,
        SqliteTransaction transaction,
        FolderId id)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT SortOrder FROM Folders WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id.Value);

        object? result = command.ExecuteScalar();

        return result is null or DBNull
            ? 0
            : Convert.ToDouble(result, System.Globalization.CultureInfo.InvariantCulture);
    }
}
