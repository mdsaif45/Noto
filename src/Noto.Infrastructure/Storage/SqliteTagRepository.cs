using Microsoft.Data.Sqlite;
using Noto.Core.Notes;
using Noto.Core.Storage;
using Noto.Core.Tags;

namespace Noto.Infrastructure.Storage;

/// <summary>
/// The SQLite implementation of <see cref="ITagRepository"/>.
/// </summary>
/// <remarks>
/// <para>
/// Hand-written SQL over <c>Microsoft.Data.Sqlite</c>, with no ORM, matching
/// the note and folder repositories (ADR-003). Every SQLite concern stops here.
/// </para>
/// <para>
/// <b>No timestamp appears anywhere in this file.</b> <c>Tags</c> has only
/// <c>CreatedAt</c>, written once on insert; <c>NoteTags</c> has no timestamp
/// columns at all. That is contract §4 made structural.
/// </para>
/// </remarks>
public sealed class SqliteTagRepository(NotoDatabase database) : ITagRepository
{
    private readonly NotoDatabase _database = database
        ?? throw new ArgumentNullException(nameof(database));

    public TagId? FindIdByName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        try
        {
            using var connection = _database.OpenConnection();
            using var command = connection.CreateCommand();

            // Name is declared COLLATE NOCASE, so `=` is already the
            // case-insensitive comparison the contract requires (design §5);
            // the same collation backs UX_Tags_Name. Whitespace is NOT handled
            // by collation — the caller normalises first (§7a).
            command.CommandText = "SELECT Id FROM Tags WHERE Name = $name;";
            command.Parameters.AddWithValue("$name", name);

            object? result = command.ExecuteScalar();

            return result is null or DBNull ? null : TagId.From((string)result);
        }
        catch (SqliteException ex)
        {
            throw new StorageException(
                StorageFailure.Unknown,
                "Could not look up a tag by name.",
                ex);
        }
    }

    public Tag? Find(TagId id)
    {
        try
        {
            using var connection = _database.OpenConnection();
            using var command = connection.CreateCommand();

            command.CommandText =
                "SELECT Id, Name, ColorKey, CreatedAt FROM Tags WHERE Id = $id;";
            command.Parameters.AddWithValue("$id", id.Value);

            using var reader = command.ExecuteReader();

            if (!reader.Read())
            {
                return null;
            }

            return ReadTag(reader);
        }
        catch (SqliteException ex)
        {
            throw new StorageException(
                StorageFailure.Unknown,
                $"Could not read tag '{id}'.",
                ex);
        }
    }

    public IReadOnlyList<Tag> ListAll()
    {
        try
        {
            using var connection = _database.OpenConnection();
            using var command = connection.CreateCommand();

            // U13a (§5a): Name COLLATE NOCASE ASC. The column is already
            // declared COLLATE NOCASE and UX_Tags_Name indexes it with the same
            // collation, so the index serves this order directly — and the
            // order tags are listed in agrees with the order two names collide
            // in, rather than being a second, separate rule.
            //
            // No tie-break, and none is needed: Name is unique
            // case-insensitively, so no two tags compare equal here.
            //
            // No DeletedAt filter: Tags has no such column. DeleteTag is the
            // engine's only hard delete (§8), so every row is live by
            // construction.
            command.CommandText =
                """
                SELECT Id, Name, ColorKey, CreatedAt
                FROM Tags
                ORDER BY Name COLLATE NOCASE ASC;
                """;

            var tags = new List<Tag>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                tags.Add(ReadTag(reader));
            }

            return tags;
        }
        catch (SqliteException ex)
        {
            throw new StorageException(
                StorageFailure.Unknown,
                "Could not list the tags.",
                ex);
        }
    }

    /// <summary>
    /// Maps one row to a <see cref="Tag"/>.
    /// </summary>
    /// <remarks>
    /// Shared by <see cref="Find"/> and <see cref="ListAll"/>, which select the
    /// same columns in the same order. Two copies would be two places for a
    /// column index to drift.
    /// </remarks>
    private static Tag ReadTag(SqliteDataReader reader) => new()
    {
        Id = TagId.From(reader.GetString(0)),
        Name = reader.GetString(1),
        ColorKey = reader.IsDBNull(2) ? null : reader.GetString(2),
        CreatedAt = Timestamps.Parse(reader.GetString(3)),
    };

    public void Add(Tag tag)
    {
        ArgumentNullException.ThrowIfNull(tag);

        try
        {
            using var connection = _database.OpenConnection();
            using var command = connection.CreateCommand();

            // No UpdatedAt column: CreatedAt is the only timestamp a tag has.
            command.CommandText =
                """
                INSERT INTO Tags (Id, Name, ColorKey, CreatedAt)
                VALUES ($id, $name, $colorKey, $createdAt);
                """;

            command.Parameters.AddWithValue("$id", tag.Id.Value);
            command.Parameters.AddWithValue("$name", tag.Name);
            command.Parameters.AddWithValue("$colorKey", (object?)tag.ColorKey ?? DBNull.Value);
            command.Parameters.AddWithValue("$createdAt", Timestamps.Format(tag.CreatedAt));

            command.ExecuteNonQuery();
        }
        catch (SqliteException ex)
        {
            throw new StorageException(
                StorageFailure.WriteFailed,
                $"Could not insert tag '{tag.Id}'.",
                ex);
        }
    }

    public void Rename(TagId id, string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        try
        {
            using var connection = _database.OpenConnection();
            using var command = connection.CreateCommand();

            // Name only. There is no UpdatedAt to stamp (§11 row 20).
            command.CommandText = "UPDATE Tags SET Name = $name WHERE Id = $id;";
            command.Parameters.AddWithValue("$name", name);
            command.Parameters.AddWithValue("$id", id.Value);

            command.ExecuteNonQuery();
        }
        catch (SqliteException ex)
        {
            throw new StorageException(
                StorageFailure.WriteFailed,
                $"Could not rename tag '{id}'.",
                ex);
        }
    }

    public void Delete(TagId id)
    {
        try
        {
            using var connection = _database.OpenConnection();
            using var transaction = connection.BeginTransaction();

            // The relationships are deleted EXPLICITLY, before the tag.
            //
            // NoteTags.TagId declares ON DELETE CASCADE and NotoDatabase turns
            // foreign keys on per connection, so deleting the tag alone would
            // usually clear them. "Usually" is the problem: that makes the
            // contract's "removes NoteTags, leaves notes intact" (§11 row 21)
            // depend on a pragma rather than on this statement. Doing it
            // explicitly means the rule holds whatever the connection state,
            // and the cascade stays a backstop rather than the mechanism.
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM NoteTags WHERE TagId = $id;";
                command.Parameters.AddWithValue("$id", id.Value);
                command.ExecuteNonQuery();
            }

            using (var command = connection.CreateCommand())
            {
                // A HARD delete — the row goes. Tags has no DeletedAt, and §8
                // makes this the engine's only hard delete.
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM Tags WHERE Id = $id;";
                command.Parameters.AddWithValue("$id", id.Value);
                command.ExecuteNonQuery();
            }

            // One transaction (§10): a tag removed while its relationships
            // survived would leave rows pointing at nothing.
            transaction.Commit();
        }
        catch (SqliteException ex)
        {
            throw new StorageException(
                StorageFailure.WriteFailed,
                $"Could not delete tag '{id}'.",
                ex);
        }
    }

    public bool HasRelationship(NoteId noteId, TagId tagId)
    {
        try
        {
            using var connection = _database.OpenConnection();
            using var command = connection.CreateCommand();

            command.CommandText =
                "SELECT 1 FROM NoteTags WHERE NoteId = $noteId AND TagId = $tagId;";
            command.Parameters.AddWithValue("$noteId", noteId.Value);
            command.Parameters.AddWithValue("$tagId", tagId.Value);

            return command.ExecuteScalar() is not null;
        }
        catch (SqliteException ex)
        {
            throw new StorageException(
                StorageFailure.Unknown,
                "Could not check a note–tag relationship.",
                ex);
        }
    }

    public void Assign(NoteId noteId, TagId tagId)
    {
        try
        {
            using var connection = _database.OpenConnection();
            using var command = connection.CreateCommand();

            // A plain INSERT, not INSERT OR IGNORE. The handler has already
            // established the relationship is absent (§7), and OR IGNORE would
            // hide a genuine constraint failure — including the foreign-key
            // violation that catches a tag deleted concurrently.
            //
            // The Notes row is NOT touched: tagging does not modify the note
            // (contract §4).
            command.CommandText =
                "INSERT INTO NoteTags (NoteId, TagId) VALUES ($noteId, $tagId);";
            command.Parameters.AddWithValue("$noteId", noteId.Value);
            command.Parameters.AddWithValue("$tagId", tagId.Value);

            command.ExecuteNonQuery();
        }
        catch (SqliteException ex)
        {
            throw new StorageException(
                StorageFailure.WriteFailed,
                "Could not assign a tag to a note.",
                ex);
        }
    }

    public void Remove(NoteId noteId, TagId tagId)
    {
        try
        {
            using var connection = _database.OpenConnection();
            using var command = connection.CreateCommand();

            command.CommandText =
                "DELETE FROM NoteTags WHERE NoteId = $noteId AND TagId = $tagId;";
            command.Parameters.AddWithValue("$noteId", noteId.Value);
            command.Parameters.AddWithValue("$tagId", tagId.Value);

            command.ExecuteNonQuery();
        }
        catch (SqliteException ex)
        {
            throw new StorageException(
                StorageFailure.WriteFailed,
                "Could not remove a tag from a note.",
                ex);
        }
    }
}
