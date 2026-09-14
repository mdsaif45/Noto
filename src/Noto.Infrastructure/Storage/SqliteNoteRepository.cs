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

            command.Parameters.AddWithValue("$id", note.Id.Value);
            command.Parameters.AddWithValue("$folderId", (object?)note.FolderId?.Value ?? DBNull.Value);
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

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString(TimestampFormat, System.Globalization.CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(
            value,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind);
}
