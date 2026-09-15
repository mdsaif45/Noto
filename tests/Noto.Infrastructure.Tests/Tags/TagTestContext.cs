using Noto.Core;
using Noto.Core.Folders;
using Noto.Core.Notes;
using Noto.Core.Tags;
using Noto.Infrastructure.Storage;
using Noto.Infrastructure.Tests.Folders;
using Noto.Infrastructure.Tests.Storage;

namespace Noto.Infrastructure.Tests.Tags;

/// <summary>
/// A real SQLite database plus the tag and note repositories, shared by the
/// Slice 5 test classes.
/// </summary>
/// <remarks>
/// Real SQLite rather than a fake, matching the note and folder tests: the
/// behaviour worth proving — that <c>UX_Tags_Name</c> actually rejects a
/// case variant, that <c>DeleteTag</c> really removes the join rows and really
/// leaves the notes — is exactly what a fake cannot prove (ADR-003).
/// </remarks>
public sealed class TagTestContext : IDisposable
{
    private readonly TempDatabase _temp = new();

    public TagTestContext()
    {
        DatabasePath = _temp.DatabasePath;
        Database = new NotoDatabase(DatabasePath);
        Database.Initialize();
        Tags = new SqliteTagRepository(Database);
        Notes = new SqliteNoteRepository(Database);
    }

    /// <summary>The database file, so a test can reopen it independently.</summary>
    public string DatabasePath { get; }

    public NotoDatabase Database { get; }

    public SqliteTagRepository Tags { get; }

    public SqliteNoteRepository Notes { get; }

    public MutableClock Clock { get; } = new(new DateTimeOffset(2026, 9, 15, 10, 30, 0, TimeSpan.Zero));

    public void Dispose() => _temp.Dispose();

    /// <summary>
    /// Inserts a tag directly, bypassing the command layer.
    /// </summary>
    /// <remarks>
    /// Raw SQL on purpose, and it writes the name <b>verbatim</b>: a test for
    /// normalisation must be able to plant an untrimmed row without depending
    /// on the normalisation being correct.
    /// </remarks>
    public TagId SeedTag(string name = "tag", string? colorKey = null)
    {
        var id = TagId.New();

        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Tags (Id, Name, ColorKey, CreatedAt)
            VALUES ($id, $name, $colorKey, $now);
            """;
        command.Parameters.AddWithValue("$id", id.Value);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$colorKey", (object?)colorKey ?? DBNull.Value);
        command.Parameters.AddWithValue("$now", Clock.UtcNow.ToString("O"));
        command.ExecuteNonQuery();

        return id;
    }

    /// <summary>Inserts a note directly, bypassing the command layer.</summary>
    public NoteId SeedNote(FolderId? folderId = null, DateTimeOffset? deletedAt = null)
    {
        var id = NoteId.New();

        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Notes
                (Id, FolderId, Content, ColorKey, IsPinned, IsFolded,
                 SortOrder, CreatedAt, UpdatedAt, DeletedAt)
            VALUES
                ($id, $folderId, 'note', NULL, 0, 0, 0, $now, $now, $deletedAt);
            """;
        command.Parameters.AddWithValue("$id", id.Value);
        command.Parameters.AddWithValue("$folderId", folderId is { } f ? f.Value : DBNull.Value);
        command.Parameters.AddWithValue("$now", Clock.UtcNow.ToString("O"));
        command.Parameters.AddWithValue(
            "$deletedAt", deletedAt is { } d ? d.ToString("O") : DBNull.Value);
        command.ExecuteNonQuery();

        return id;
    }

    /// <summary>Creates the relationship directly, bypassing the command layer.</summary>
    public void SeedRelationship(NoteId noteId, TagId tagId)
    {
        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO NoteTags (NoteId, TagId) VALUES ($noteId, $tagId);";
        command.Parameters.AddWithValue("$noteId", noteId.Value);
        command.Parameters.AddWithValue("$tagId", tagId.Value);
        command.ExecuteNonQuery();
    }

    /// <summary>The raw persisted name, exactly as stored.</summary>
    public string? NameOf(TagId id) => Scalar("SELECT Name FROM Tags WHERE Id = $id;", id.Value) as string;

    /// <summary>The raw persisted <c>CreatedAt</c>.</summary>
    public string? CreatedAtOf(TagId id) =>
        Scalar("SELECT CreatedAt FROM Tags WHERE Id = $id;", id.Value) as string;

    /// <summary>Whether a tag row exists at all — the hard-delete assertion.</summary>
    public bool TagExists(TagId id) => Scalar("SELECT 1 FROM Tags WHERE Id = $id;", id.Value) is not null;

    /// <summary>Whether a note row exists at all.</summary>
    public bool NoteExists(NoteId id) => Scalar("SELECT 1 FROM Notes WHERE Id = $id;", id.Value) is not null;

    /// <summary>The note's raw <c>UpdatedAt</c>, deleted or not.</summary>
    public string UpdatedAtOf(NoteId id) =>
        (string)Scalar("SELECT UpdatedAt FROM Notes WHERE Id = $id;", id.Value)!;

    /// <summary>How many rows the join table holds in total.</summary>
    public long RelationshipCount()
    {
        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM NoteTags;";
        return (long)command.ExecuteScalar()!;
    }

    /// <summary>How many rows the join table holds for one tag.</summary>
    public long RelationshipCountFor(TagId id) =>
        (long)Scalar("SELECT COUNT(*) FROM NoteTags WHERE TagId = $id;", id.Value)!;

    /// <summary>How many tag rows exist in total.</summary>
    public long TagCount()
    {
        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Tags;";
        return (long)command.ExecuteScalar()!;
    }

    private object? Scalar(string sql, string id)
    {
        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", id);

        object? value = command.ExecuteScalar();

        return value is DBNull ? null : value;
    }
}
