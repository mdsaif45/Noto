using Microsoft.Data.Sqlite;
using Noto.Core.Folders;
using Noto.Core.Notes;
using Noto.Core.Storage;

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
            using var command = connection.CreateCommand();

            // Root (FolderId IS NULL) is its own scope, not a catch-all (O1),
            // so the two cases need different SQL: "FolderId = NULL" matches
            // nothing in SQL's three-valued logic.
            //
            // Deleted notes are excluded because they keep their SortOrder but
            // never participate in ordering (I6). MAX over an empty set is
            // NULL, which is exactly "the scope is empty".
            command.CommandText = folderId is null
                ? """
                  SELECT MAX(SortOrder)
                  FROM Notes
                  WHERE FolderId IS NULL AND DeletedAt IS NULL;
                  """
                : """
                  SELECT MAX(SortOrder)
                  FROM Notes
                  WHERE FolderId = $folderId AND DeletedAt IS NULL;
                  """;

            if (folderId is { } scope)
            {
                command.Parameters.AddWithValue("$folderId", scope.Value);
            }

            object? result = command.ExecuteScalar();

            return result is null or DBNull ? null : Convert.ToDouble(result, System.Globalization.CultureInfo.InvariantCulture);
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
            double sortOrder = ResolvePosition(connection, transaction, targetFolderId, placement, movingNote: id, updatedAt);

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

            FolderId? scope = ReadScope(connection, transaction, id);
            double current = ReadSortOrder(connection, transaction, id);

            if (AlreadyInPosition(connection, transaction, id, scope, placement, current))
            {
                // Same position: write nothing, stamp nothing (contract §5).
                // Rolling back rather than committing an empty transaction
                // keeps "no-op" literally true at the database.
                transaction.Rollback();
                return false;
            }

            double sortOrder = ResolvePosition(connection, transaction, scope, placement, movingNote: id, updatedAt);

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

    // ---- ordering (O1-O6) ------------------------------------------------

    /// <summary>
    /// The gap below which a midpoint is no longer meaningfully between its
    /// neighbours, so the scope is renormalised instead.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Implementation-defined.</b> The contract fixes the observable
    /// behaviour — ordering stays correct, renormalisation is atomic and yields
    /// 1, 2, 3… — and states that the numeric trigger is deliberately not part
    /// of it (contract §5, U5).
    /// </para>
    /// <para>
    /// Chosen well above the point where <c>double</c> actually fails: with ~52
    /// bits of mantissa, midpoints between neighbours around 1.0 stop being
    /// distinguishable after roughly 50 halvings. Renormalising at 1e-9 leaves a
    /// wide margin, so the ordering is rebuilt long before two notes could
    /// collide.
    /// </para>
    /// </remarks>
    private const double RenormalisationThreshold = 1e-9;

    /// <summary>Where a renormalised scope starts, then 2, 3, … (O6).</summary>
    private const double RenormalisationStart = 1;

    /// <summary>
    /// Turns a <see cref="NotePlacement"/> into a concrete <c>SortOrder</c>,
    /// renormalising the scope first if the gap is exhausted.
    /// </summary>
    /// <remarks>
    /// The single place ordering values are produced, shared by
    /// <see cref="Move"/> and <see cref="Reorder"/> so the two cannot drift.
    /// </remarks>
    private static double ResolvePosition(
        SqliteConnection connection,
        SqliteTransaction transaction,
        FolderId? scope,
        NotePlacement placement,
        NoteId movingNote,
        DateTimeOffset updatedAt)
    {
        (double? before, double? after) = Neighbours(connection, transaction, scope, placement, movingNote);

        // An end of the scope: O3's min - 1 / max + 1. No renumbering.
        if (before is null && after is null)
        {
            return RenormalisationStart;
        }

        if (before is null)
        {
            return after!.Value - 1;
        }

        if (after is null)
        {
            return before.Value + 1;
        }

        double gap = after.Value - before.Value;

        // Tested against the gap this insertion would LEAVE BEHIND, not the one
        // it finds. Checking `gap` alone lets a gap of 2e-9 through, which then
        // writes a midpoint 1e-9 away from its neighbour — under the threshold,
        // and the next insertion there has nothing left to split.
        if (gap / 2 > RenormalisationThreshold)
        {
            // Midpoint: one row written, not the whole scope (design §7).
            return before.Value + (gap / 2);
        }

        // O6: the gap is exhausted. Rewrite the scope to 1, 2, 3... inside this
        // transaction, then take the midpoint of the now-separated neighbours.
        Renormalise(connection, transaction, scope, movingNote, updatedAt);

        (before, after) = Neighbours(connection, transaction, scope, placement, movingNote);

        return (before, after) switch
        {
            (null, null) => RenormalisationStart,
            (null, { } a) => a - 1,
            ({ } b, null) => b + 1,
            ({ } b, { } a) => b + ((a - b) / 2),
        };
    }

    /// <summary>
    /// The <c>SortOrder</c> values a placement sits between, excluding the note
    /// being moved.
    /// </summary>
    /// <remarks>
    /// The moving note is excluded so that its own current position never acts
    /// as its own neighbour, which would make "move after my predecessor" a
    /// no-op by accident. Deleted rows are excluded because they never
    /// participate in ordering (I6).
    /// </remarks>
    private static (double? Before, double? After) Neighbours(
        SqliteConnection connection,
        SqliteTransaction transaction,
        FolderId? scope,
        NotePlacement placement,
        NoteId movingNote)
    {
        if (placement.AtEnd)
        {
            return (ScopeExtreme(connection, transaction, scope, movingNote, highest: true), null);
        }

        if (placement.AfterSibling is not { } sibling)
        {
            // First position: nothing before, the current minimum after.
            return (null, ScopeExtreme(connection, transaction, scope, movingNote, highest: false));
        }

        double anchor = ReadSortOrder(connection, transaction, sibling);

        using var command = connection.CreateCommand();
        command.Transaction = transaction;

        // The next active sibling strictly after the anchor. Ties on SortOrder
        // fall back to Id, matching O2's total order, so "after" is
        // unambiguous even when two rows share a value.
        command.CommandText = scope is null
            ? """
              SELECT MIN(SortOrder) FROM Notes
              WHERE FolderId IS NULL AND DeletedAt IS NULL AND Id <> $moving
                AND (SortOrder > $anchor OR (SortOrder = $anchor AND Id > $sibling));
              """
            : """
              SELECT MIN(SortOrder) FROM Notes
              WHERE FolderId = $folderId AND DeletedAt IS NULL AND Id <> $moving
                AND (SortOrder > $anchor OR (SortOrder = $anchor AND Id > $sibling));
              """;

        command.Parameters.AddWithValue("$anchor", anchor);
        command.Parameters.AddWithValue("$sibling", sibling.Value);
        command.Parameters.AddWithValue("$moving", movingNote.Value);
        if (scope is { } folder)
        {
            command.Parameters.AddWithValue("$folderId", folder.Value);
        }

        object? next = command.ExecuteScalar();

        return (anchor, next is null or DBNull ? null : ToDouble(next));
    }

    /// <summary>The highest or lowest active <c>SortOrder</c> in a scope.</summary>
    private static double? ScopeExtreme(
        SqliteConnection connection,
        SqliteTransaction transaction,
        FolderId? scope,
        NoteId excluding,
        bool highest)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;

        string aggregate = highest ? "MAX" : "MIN";

        command.CommandText = scope is null
            ? $"SELECT {aggregate}(SortOrder) FROM Notes WHERE FolderId IS NULL AND DeletedAt IS NULL AND Id <> $moving;"
            : $"SELECT {aggregate}(SortOrder) FROM Notes WHERE FolderId = $folderId AND DeletedAt IS NULL AND Id <> $moving;";

        command.Parameters.AddWithValue("$moving", excluding.Value);
        if (scope is { } folder)
        {
            command.Parameters.AddWithValue("$folderId", folder.Value);
        }

        object? result = command.ExecuteScalar();

        return result is null or DBNull ? null : ToDouble(result);
    }

    /// <summary>
    /// O6: rewrites the scope's <c>SortOrder</c> to 1, 2, 3… preserving the
    /// current order, inside the caller's transaction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The moving note is excluded</b>, so what this rewrites is the scope
    /// <i>minus</i> that note. It is about to be positioned explicitly by
    /// <see cref="ResolvePosition"/>, and including it would assign a value
    /// that the very next statement overwrites.
    /// </para>
    /// <para>
    /// The consequence is worth stating plainly, because the method name alone
    /// suggests otherwise: after the whole operation the scope is
    /// <b>not</b> contiguous. Renormalisation restores 1, 2, 3… and the
    /// insertion then places the moving note between two of them:
    /// </para>
    /// <code>
    ///   before      A=1.0  X=1.0000000005  B=2.0   M=9.0   (gap exhausted)
    ///   renormalise A=1    X=2             B=3     M=9.0   &lt;- O6, this method
    ///   place M     A=1    M=1.5           X=2     B=3     &lt;- O3 midpoint
    /// </code>
    /// <para>
    /// That satisfies the contract: O6 governs what renormalisation produces,
    /// O3 governs the insertion that follows, and O5 says the resulting gaps
    /// are harmless. What matters observably is that the order is correct and
    /// the neighbours are once again far enough apart to split.
    /// </para>
    /// <para>
    /// Every row it rewrites is a row it changes, so each takes the
    /// transaction's single timestamp (contract §4).
    /// </para>
    /// </remarks>
    private static void Renormalise(
        SqliteConnection connection,
        SqliteTransaction transaction,
        FolderId? scope,
        NoteId excluding,
        DateTimeOffset updatedAt)
    {
        var ids = new List<string>();

        using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;

            // O2's ordering, so renormalising preserves what the user sees.
            read.CommandText = scope is null
                ? """
                  SELECT Id FROM Notes
                  WHERE FolderId IS NULL AND DeletedAt IS NULL AND Id <> $moving
                  ORDER BY SortOrder ASC, Id ASC;
                  """
                : """
                  SELECT Id FROM Notes
                  WHERE FolderId = $folderId AND DeletedAt IS NULL AND Id <> $moving
                  ORDER BY SortOrder ASC, Id ASC;
                  """;

            read.Parameters.AddWithValue("$moving", excluding.Value);
            if (scope is { } folder)
            {
                read.Parameters.AddWithValue("$folderId", folder.Value);
            }

            using var reader = read.ExecuteReader();
            while (reader.Read())
            {
                ids.Add(reader.GetString(0));
            }
        }

        double position = RenormalisationStart;

        foreach (string id in ids)
        {
            using var write = connection.CreateCommand();
            write.Transaction = transaction;
            write.CommandText =
                """
                UPDATE Notes SET SortOrder = $sortOrder, UpdatedAt = $updatedAt WHERE Id = $id;
                """;
            write.Parameters.AddWithValue("$sortOrder", position);
            write.Parameters.AddWithValue("$updatedAt", Format(updatedAt));
            write.Parameters.AddWithValue("$id", id);
            write.ExecuteNonQuery();

            position++;
        }
    }

    /// <summary>Whether the note already sits where the placement asks.</summary>
    /// <remarks>
    /// Compared by neighbours rather than by value: the question is not "is the
    /// number already right" but "is anything actually between the note and
    /// where it is being asked to go".
    /// </remarks>
    private static bool AlreadyInPosition(
        SqliteConnection connection,
        SqliteTransaction transaction,
        NoteId id,
        FolderId? scope,
        NotePlacement placement,
        double current)
    {
        if (placement.AfterSibling is { } sibling && sibling == id)
        {
            // "After itself" is meaningless; treat it as staying put rather
            // than computing a position from its own value.
            return true;
        }

        (double? before, double? after) = Neighbours(connection, transaction, scope, placement, id);

        // Already after `before` and before `after` means nothing would move.
        bool afterLowerBound = before is null || current > before.Value;
        bool beforeUpperBound = after is null || current < after.Value;

        return afterLowerBound && beforeUpperBound;
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

        return result is null or DBNull ? 0 : ToDouble(result);
    }

    private static double ToDouble(object value) =>
        Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString(TimestampFormat, System.Globalization.CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(
            value,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind);
}
