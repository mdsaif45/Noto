using Microsoft.Data.Sqlite;
using Noto.Core.Folders;
using Noto.Core.Notes;
using Noto.Core.Storage;
using Noto.Core.Tags;

namespace Noto.Infrastructure.Storage;

/// <summary>
/// The SQLite implementation of <see cref="INoteRepository"/>.
/// </summary>
/// <remarks>
/// <para>
/// Hand-written SQL over <c>Microsoft.Data.Sqlite</c>, with no ORM: the schema
/// is small, fixed and already expressed in migrations, and an ORM would add a
/// dependency plus a second description of the same tables (ADR-003).
/// </para>
/// <para>
/// Every SQLite concern stops here. Failures become
/// <see cref="StorageException"/>, rows become domain entities, and no
/// <c>Sqlite*</c> type crosses back out.
/// </para>
/// </remarks>
public sealed class SqliteNoteRepository(NotoDatabase database) : INoteRepository
{
    /// <summary>ISO-8601 round-trip, the format every timestamp column uses.</summary>
    private const string TimestampFormat = "O";

    private readonly NotoDatabase _database = database
        ?? throw new ArgumentNullException(nameof(database));

    public void Add(Note note)
    {
        ArgumentNullException.ThrowIfNull(note);

        try
        {
            using var connection = _database.OpenConnection();
            using var command = connection.CreateCommand();

            command.CommandText =
                """
                INSERT INTO Notes
                    (Id, FolderId, Content, ColorKey, IsPinned, IsFolded,
                     SortOrder, CreatedAt, UpdatedAt, DeletedAt)
                VALUES
                    ($id, $folderId, $content, $colorKey, $isPinned, $isFolded,
                     $sortOrder, $createdAt, $updatedAt, $deletedAt);
                """;

            // Pattern-matched rather than written as `note.FolderId?.Value`,
            // because two different members called `Value` meet on that
            // expression: Nullable<FolderId>.Value, which throws when the
            // folder is absent, and FolderId.Value, the ULID string. The
            // null-conditional binds to the first and the access to the
            // second, which is safe but reads as though it might not be — and
            // a null-analyser cannot tell the two apart either. Naming the
            // unwrapped folder removes the ambiguity for both readers.
            command.Parameters.AddWithValue("$id", note.Id.Value);
            command.Parameters.AddWithValue(
                "$folderId",
                note.FolderId is { } folder ? folder.Value : DBNull.Value);
            command.Parameters.AddWithValue("$content", note.Content);
            command.Parameters.AddWithValue("$colorKey", (object?)note.ColorKey ?? DBNull.Value);
            command.Parameters.AddWithValue("$isPinned", note.IsPinned ? 1 : 0);
            command.Parameters.AddWithValue("$isFolded", note.IsFolded ? 1 : 0);
            command.Parameters.AddWithValue("$sortOrder", note.SortOrder);
            command.Parameters.AddWithValue("$createdAt", Format(note.CreatedAt));
            command.Parameters.AddWithValue("$updatedAt", Format(note.UpdatedAt));
            command.Parameters.AddWithValue(
                "$deletedAt",
                note.DeletedAt is { } deleted ? Format(deleted) : DBNull.Value);

            command.ExecuteNonQuery();
        }
        catch (SqliteException ex)
        {
            // The id is infrastructure, not user content, so naming it is safe
            // — and without it the error is undiagnosable. The note's CONTENT
            // never appears here (principle 10).
            throw new StorageException(
                StorageFailure.WriteFailed,
                $"Could not insert note '{note.Id}'.",
                ex);
        }
    }

    public Note? FindActive(NoteId id)
    {
        try
        {
            using var connection = _database.OpenConnection();
            using var command = connection.CreateCommand();

            // Invariant I1: the DeletedAt filter lives here, in the query, so
            // that a caller cannot forget it. The recycle bin is reached only
            // through ListDeletedNotes (I2).
            command.CommandText =
                """
                SELECT Id, FolderId, Content, ColorKey, IsPinned, IsFolded,
                       SortOrder, CreatedAt, UpdatedAt, DeletedAt
                FROM Notes
                WHERE Id = $id AND DeletedAt IS NULL;
                """;
            command.Parameters.AddWithValue("$id", id.Value);

            using var reader = command.ExecuteReader();

            return reader.Read() ? ReadNote(reader) : null;
        }
        catch (SqliteException ex)
        {
            throw new StorageException(
                StorageFailure.Unknown,
                $"Could not read note '{id}'.",
                ex);
        }
    }

    public NoteLifecycle GetLifecycle(NoteId id)
    {
        try
        {
            using var connection = _database.OpenConnection();
            using var command = connection.CreateCommand();

            // Selecting DeletedAt rather than filtering on it: the caller must
            // tell "no such note" from "that note is in the bin", because the
            // contract maps them to different failures.
            command.CommandText = "SELECT DeletedAt FROM Notes WHERE Id = $id;";
            command.Parameters.AddWithValue("$id", id.Value);

            using var reader = command.ExecuteReader();

            if (!reader.Read())
            {
                return NoteLifecycle.Missing;
            }

            return reader.IsDBNull(0) ? NoteLifecycle.Active : NoteLifecycle.Deleted;
        }
        catch (SqliteException ex)
        {
            throw new StorageException(
                StorageFailure.Unknown,
                $"Could not check note '{id}'.",
                ex);
        }
    }

    public FolderState GetFolderState(FolderId id)
    {
        try
        {
            using var connection = _database.OpenConnection();
            using var command = connection.CreateCommand();

            // Selecting DeletedAt rather than filtering on it: the caller must
            // tell "no such folder" from "that folder is in the bin", because
            // the contract maps them to different failures.
            command.CommandText =
                """
                SELECT DeletedAt
                FROM Folders
                WHERE Id = $id;
                """;
            command.Parameters.AddWithValue("$id", id.Value);

            using var reader = command.ExecuteReader();

            if (!reader.Read())
            {
                return FolderState.Missing;
            }

            return reader.IsDBNull(0) ? FolderState.Active : FolderState.Deleted;
        }
        catch (SqliteException ex)
        {
            throw new StorageException(
                StorageFailure.Unknown,
                $"Could not check folder '{id}'.",
                ex);
        }
    }

    public double? MaxSortOrder(FolderId? folderId)
    {
        try
        {
            using var connection = _database.OpenConnection();

            // Root (FolderId IS NULL) is its own scope, not a catch-all (O1),
            // which SortOrderScope.NotesIn encodes. Deleted notes are excluded
            // because they keep their SortOrder but never participate in
            // ordering (I6).
            return SortOrderEngine.MaxSortOrder(connection, ScopeFor(folderId));
        }
        catch (SqliteException ex)
        {
            throw new StorageException(
                StorageFailure.Unknown,
                "Could not read the ordering scope.",
                ex);
        }
    }

    public void UpdateContent(NoteId id, string content, DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(content);

        try
        {
            using var connection = _database.OpenConnection();
            using var command = connection.CreateCommand();

            command.CommandText =
                """
                UPDATE Notes
                SET Content = $content, UpdatedAt = $updatedAt
                WHERE Id = $id AND DeletedAt IS NULL;
                """;
            command.Parameters.AddWithValue("$content", content);
            command.Parameters.AddWithValue("$updatedAt", Format(updatedAt));
            command.Parameters.AddWithValue("$id", id.Value);

            command.ExecuteNonQuery();
        }
        catch (SqliteException ex)
        {
            // The id is infrastructure; the CONTENT never reaches the message
            // (principle 10).
            throw new StorageException(
                StorageFailure.WriteFailed,
                $"Could not update note '{id}'.",
                ex);
        }
    }

    public void Move(NoteId id, FolderId? targetFolderId, NotePlacement placement, DateTimeOffset updatedAt)
    {
        try
        {
            using var connection = _database.OpenConnection();
            using var transaction = connection.BeginTransaction();

            // O4: the folder change and the new SortOrder are one unit. A
            // commit between them would leave the note in the target scope
            // holding a value that means nothing there.
            double sortOrder = SortOrderEngine.ResolvePosition(
                connection,
                transaction,
                ScopeFor(targetFolderId),
                ToEnginePlacement(placement),
                id.Value,
                updatedAt);

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText =
                    """
                    UPDATE Notes
                    SET FolderId = $folderId, SortOrder = $sortOrder, UpdatedAt = $updatedAt
                    WHERE Id = $id AND DeletedAt IS NULL;
                    """;
                command.Parameters.AddWithValue(
                    "$folderId",
                    targetFolderId is { } folder ? folder.Value : DBNull.Value);
                command.Parameters.AddWithValue("$sortOrder", sortOrder);
                command.Parameters.AddWithValue("$updatedAt", Format(updatedAt));
                command.Parameters.AddWithValue("$id", id.Value);
                command.ExecuteNonQuery();
            }

            transaction.Commit();
        }
        catch (SqliteException ex)
        {
            throw new StorageException(
                StorageFailure.WriteFailed,
                $"Could not move note '{id}'.",
                ex);
        }
    }

    public bool Reorder(NoteId id, NotePlacement placement, DateTimeOffset updatedAt)
    {
        try
        {
            using var connection = _database.OpenConnection();
            using var transaction = connection.BeginTransaction();

            FolderId? folderScope = ReadScope(connection, transaction, id);
            SortOrderScope scope = ScopeFor(folderScope);
            double current = ReadSortOrder(connection, transaction, id);

            if (SortOrderEngine.AlreadyInPosition(
                connection, transaction, scope, ToEnginePlacement(placement), id.Value, current))
            {
                // Same position: write nothing, stamp nothing (contract §5).
                // Rolling back rather than committing an empty transaction
                // keeps "no-op" literally true at the database.
                transaction.Rollback();
                return false;
            }

            double sortOrder = SortOrderEngine.ResolvePosition(
                connection, transaction, scope, ToEnginePlacement(placement), id.Value, updatedAt);

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText =
                    """
                    UPDATE Notes
                    SET SortOrder = $sortOrder, UpdatedAt = $updatedAt
                    WHERE Id = $id AND DeletedAt IS NULL;
                    """;
                command.Parameters.AddWithValue("$sortOrder", sortOrder);
                command.Parameters.AddWithValue("$updatedAt", Format(updatedAt));
                command.Parameters.AddWithValue("$id", id.Value);
                command.ExecuteNonQuery();
            }

            transaction.Commit();
            return true;
        }
        catch (SqliteException ex)
        {
            throw new StorageException(
                StorageFailure.WriteFailed,
                $"Could not reorder note '{id}'.",
                ex);
        }
    }

    public bool IsActiveSiblingIn(NoteId id, FolderId? folderId)
    {
        try
        {
            using var connection = _database.OpenConnection();
            using var command = connection.CreateCommand();

            command.CommandText = folderId is null
                ? "SELECT 1 FROM Notes WHERE Id = $id AND FolderId IS NULL AND DeletedAt IS NULL;"
                : "SELECT 1 FROM Notes WHERE Id = $id AND FolderId = $folderId AND DeletedAt IS NULL;";

            command.Parameters.AddWithValue("$id", id.Value);
            if (folderId is { } scope)
            {
                command.Parameters.AddWithValue("$folderId", scope.Value);
            }

            return command.ExecuteScalar() is not null;
        }
        catch (SqliteException ex)
        {
            throw new StorageException(
                StorageFailure.Unknown,
                $"Could not check note '{id}'.",
                ex);
        }
    }

    public void SetPinned(NoteId id, bool isPinned, DateTimeOffset updatedAt) =>
        // Note the SET list: IsPinned and UpdatedAt only. SortOrder is
        // deliberately absent — see the interface remarks.
        UpdateNoteField(id, "IsPinned", isPinned ? 1 : 0, updatedAt, "pin");

    public void SetFolded(NoteId id, bool isFolded, DateTimeOffset updatedAt) =>
        UpdateNoteField(id, "IsFolded", isFolded ? 1 : 0, updatedAt, "fold");

    public void SetColor(NoteId id, string? colorKey, DateTimeOffset updatedAt) =>
        UpdateNoteField(id, "ColorKey", (object?)colorKey ?? DBNull.Value, updatedAt, "colour");

    public void SoftDelete(NoteId id, DateTimeOffset deletedAt)
    {
        try
        {
            using var connection = _database.OpenConnection();
            using var command = connection.CreateCommand();

            // UPDATE, never DELETE. The row is the source of truth the recycle
            // bin lists and restore brings back; removing it would make B23
            // unimplementable and the loss unrecoverable (ADR-002).
            //
            // Siblings are untouched: O5 says deleting leaves gaps, and this
            // row keeps its own SortOrder so restore returns it roughly where
            // it was (I6).
            //
            // One instant for both columns — a note whose UpdatedAt trails its
            // DeletedAt would be claiming it changed after it was binned.
            command.CommandText =
                """
                UPDATE Notes
                SET DeletedAt = $now, UpdatedAt = $now
                WHERE Id = $id AND DeletedAt IS NULL;
                """;
            command.Parameters.AddWithValue("$now", Format(deletedAt));
            command.Parameters.AddWithValue("$id", id.Value);

            command.ExecuteNonQuery();
        }
        catch (SqliteException ex)
        {
            throw new StorageException(
                StorageFailure.WriteFailed,
                $"Could not delete note '{id}'.",
                ex);
        }
    }

    public void Restore(NoteId id, DateTimeOffset updatedAt)
    {
        try
        {
            using var connection = _database.OpenConnection();
            using var command = connection.CreateCommand();

            // DeletedAt and UpdatedAt only. SortOrder, FolderId and every other
            // field survive untouched, so the note comes back as it was rather
            // than being reconstructed — including a FolderId that may still
            // point at a deleted folder (Case C).
            command.CommandText =
                """
                UPDATE Notes
                SET DeletedAt = NULL, UpdatedAt = $now
                WHERE Id = $id AND DeletedAt IS NOT NULL;
                """;
            command.Parameters.AddWithValue("$now", Format(updatedAt));
            command.Parameters.AddWithValue("$id", id.Value);

            command.ExecuteNonQuery();
        }
        catch (SqliteException ex)
        {
            throw new StorageException(
                StorageFailure.WriteFailed,
                $"Could not restore note '{id}'.",
                ex);
        }
    }

    public IReadOnlyList<Note> ListDeleted()
    {
        try
        {
            using var connection = _database.OpenConnection();
            using var command = connection.CreateCommand();

            // The I2 inversion: DeletedAt IS NOT NULL. Ordered most-recently
            // binned first, which is the order a bin is read in; the Id
            // tiebreak keeps it total (O2's reasoning, applied here).
            command.CommandText =
                """
                SELECT Id, FolderId, Content, ColorKey, IsPinned, IsFolded,
                       SortOrder, CreatedAt, UpdatedAt, DeletedAt
                FROM Notes
                WHERE DeletedAt IS NOT NULL
                ORDER BY DeletedAt DESC, Id ASC;
                """;

            var notes = new List<Note>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                notes.Add(ReadNote(reader));
            }

            return notes;
        }
        catch (SqliteException ex)
        {
            throw new StorageException(
                StorageFailure.Unknown,
                "Could not read the recycle bin.",
                ex);
        }
    }

    /// <summary>
    /// Updates one column of one active note, plus its timestamp.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A single-row write, so no transaction: design §9 lists the five
    /// operations that need one, and none of these is among them. Adding one
    /// for symmetry would be machinery without a purpose.
    /// </para>
    /// <para>
    /// The column name is supplied only by this class's own callers, never by
    /// input — it names a compile-time constant in each case — while the value
    /// is always parameterised.
    /// </para>
    /// </remarks>
    private void UpdateNoteField(
        NoteId id,
        string column,
        object value,
        DateTimeOffset updatedAt,
        string operation)
    {
        try
        {
            using var connection = _database.OpenConnection();
            using var command = connection.CreateCommand();

            // The DeletedAt filter is belt-and-braces: the handler has already
            // rejected a deleted note with InvalidState (I5). It is here so the
            // repository cannot be the thing that edits something in the bin.
            command.CommandText =
                $"""
                 UPDATE Notes
                 SET {column} = $value, UpdatedAt = $updatedAt
                 WHERE Id = $id AND DeletedAt IS NULL;
                 """;
            command.Parameters.AddWithValue("$value", value);
            command.Parameters.AddWithValue("$updatedAt", Format(updatedAt));
            command.Parameters.AddWithValue("$id", id.Value);

            command.ExecuteNonQuery();
        }
        catch (SqliteException ex)
        {
            throw new StorageException(
                StorageFailure.WriteFailed,
                $"Could not {operation} note '{id}'.",
                ex);
        }
    }

    /// <summary>
    /// Translates the domain's placement into the engine's row-level one.
    /// </summary>
    /// <remarks>
    /// The boundary where strong ids become plain row ids. The engine orders
    /// rows in a table; giving it <c>NoteId</c> would tie one algorithm to one
    /// domain, so the translation lives here rather than there.
    /// </remarks>
    public IReadOnlyList<Note> ListInFolder(FolderId? folderId)
    {
        try
        {
            using var connection = _database.OpenConnection();
            using var command = connection.CreateCommand();

            // Two predicates rather than one, and this is not a style choice:
            // `FolderId = NULL` matches NOTHING in SQL's three-valued logic, so
            // a single parameterised `= $folderId` would silently return an
            // empty root scope rather than failing. Root is its own scope (O1),
            // not a catch-all, and `IS NULL` is the only way to say so.
            //
            // O2 in full: pinned first, then SortOrder, then Id as the
            // deterministic tie-break (§5a, ADR-012 — ULIDs are
            // creation-ordered). I1 excludes deleted notes, which is also I6:
            // a deleted row keeps its SortOrder but never participates.
            command.CommandText = folderId is null
                ? """
                  SELECT Id, FolderId, Content, ColorKey, IsPinned, IsFolded,
                         SortOrder, CreatedAt, UpdatedAt, DeletedAt
                  FROM Notes
                  WHERE FolderId IS NULL AND DeletedAt IS NULL
                  ORDER BY IsPinned DESC, SortOrder ASC, Id ASC;
                  """
                : """
                  SELECT Id, FolderId, Content, ColorKey, IsPinned, IsFolded,
                         SortOrder, CreatedAt, UpdatedAt, DeletedAt
                  FROM Notes
                  WHERE FolderId = $folderId AND DeletedAt IS NULL
                  ORDER BY IsPinned DESC, SortOrder ASC, Id ASC;
                  """;

            if (folderId is { } scope)
            {
                command.Parameters.AddWithValue("$folderId", scope.Value);
            }

            var notes = new List<Note>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                notes.Add(ReadNote(reader));
            }

            return notes;
        }
        catch (SqliteException ex)
        {
            throw new StorageException(
                StorageFailure.Unknown,
                "Could not list the notes in a folder.",
                ex);
        }
    }

    public IReadOnlyList<Note> ListForTag(TagId tagId)
    {
        try
        {
            using var connection = _database.OpenConnection();
            using var command = connection.CreateCommand();

            // Membership comes from NoteTags and nothing else — a note is
            // returned because the join row exists, never merely because the
            // note does.
            //
            // UpdatedAt DESC (§5a, U13b). Deliberately NOT SortOrder: these
            // notes span folders and SortOrder is scoped per folder (O1), so
            // the values are not comparable across the result.
            //
            // No secondary tie-break, deliberately. Two notes can share an
            // UpdatedAt (§4 gives one transaction's rows one timestamp) and the
            // contract does not say how they order. Adding `Id ASC` here would
            // be inventing a requirement the gate declined to make.
            command.CommandText =
                """
                SELECT n.Id, n.FolderId, n.Content, n.ColorKey, n.IsPinned, n.IsFolded,
                       n.SortOrder, n.CreatedAt, n.UpdatedAt, n.DeletedAt
                FROM Notes n
                INNER JOIN NoteTags nt ON nt.NoteId = n.Id
                WHERE nt.TagId = $tagId AND n.DeletedAt IS NULL
                ORDER BY n.UpdatedAt DESC;
                """;

            command.Parameters.AddWithValue("$tagId", tagId.Value);

            var notes = new List<Note>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                notes.Add(ReadNote(reader));
            }

            return notes;
        }
        catch (SqliteException ex)
        {
            throw new StorageException(
                StorageFailure.Unknown,
                "Could not list the notes for a tag.",
                ex);
        }
    }

    /// <summary>
    /// The ordering scope for a note's folder, with root as its own scope (O1).
    /// </summary>
    /// <remarks>
    /// Pattern-matched rather than written as <c>folderId?.Value</c>, because
    /// two different members called <c>Value</c> meet on that expression:
    /// <c>Nullable&lt;FolderId&gt;.Value</c>, which throws when the folder is
    /// absent, and <c>FolderId.Value</c>, the ULID string. The null-conditional
    /// binds to the first and the access to the second, which is safe but reads
    /// as though it might not be — and a null-analyser cannot tell the two
    /// apart either. Naming the unwrapped folder removes the ambiguity for both
    /// readers, the same way <see cref="Add"/> does.
    /// </remarks>
    private static SortOrderScope ScopeFor(FolderId? folderId) =>
        SortOrderScope.NotesIn(folderId is { } folder ? folder.Value : null);

    private static SortOrderPlacement ToEnginePlacement(NotePlacement placement)
    {
        if (placement.AtEnd)
        {
            return SortOrderPlacement.Last;
        }

        return placement.AfterSibling is { } sibling
            ? SortOrderPlacement.After(sibling.Value)
            : SortOrderPlacement.First;
    }

    private static FolderId? ReadScope(SqliteConnection connection, SqliteTransaction transaction, NoteId id)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT FolderId FROM Notes WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id.Value);

        object? result = command.ExecuteScalar();

        return result is null or DBNull ? null : FolderId.From((string)result);
    }

    private static double ReadSortOrder(SqliteConnection connection, SqliteTransaction transaction, NoteId id)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT SortOrder FROM Notes WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id.Value);

        object? result = command.ExecuteScalar();

        return result is null or DBNull ? 0 : Convert.ToDouble(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static Note ReadNote(SqliteDataReader reader) => new()
    {
        Id = NoteId.From(reader.GetString(0)),
        FolderId = reader.IsDBNull(1) ? null : FolderId.From(reader.GetString(1)),
        Content = reader.GetString(2),
        ColorKey = reader.IsDBNull(3) ? null : reader.GetString(3),
        IsPinned = reader.GetInt64(4) != 0,
        IsFolded = reader.GetInt64(5) != 0,
        SortOrder = reader.GetDouble(6),
        CreatedAt = Parse(reader.GetString(7)),
        UpdatedAt = Parse(reader.GetString(8)),
        DeletedAt = reader.IsDBNull(9) ? null : Parse(reader.GetString(9)),
    };

    private static string Format(DateTimeOffset value) => Timestamps.Format(value);

    private static DateTimeOffset Parse(string value) => Timestamps.Parse(value);
}
