using Noto.Core.Folders;
using Noto.Core.Notes;
using Noto.Core.Tags;
using Noto.Infrastructure.Storage;
using Noto.Infrastructure.Tests.Folders;
using Noto.Infrastructure.Tests.Storage;

namespace Noto.Infrastructure.Tests.Queries;

/// <summary>
/// A real SQLite database plus the three repositories, shared by the Slice 6
/// query tests.
/// </summary>
/// <remarks>
/// <para>
/// Every seeding helper writes <b>raw SQL</b>, bypassing the command layer.
/// That is the point: a query test must be able to plant rows in a deliberately
/// wrong order, with chosen timestamps and chosen <c>SortOrder</c> values, so
/// that an implementation missing its <c>ORDER BY</c> cannot pass by accident.
/// Seeding through the commands would order the rows for it.
/// </para>
/// </remarks>
public sealed class QueryTestContext : IDisposable
{
    private readonly TempDatabase _temp = new();

    public QueryTestContext()
    {
        DatabasePath = _temp.DatabasePath;
        Database = new NotoDatabase(DatabasePath);
        Database.Initialize();
        Notes = new SqliteNoteRepository(Database);
        Folders = new SqliteFolderRepository(Database);
        Tags = new SqliteTagRepository(Database);
    }

    public string DatabasePath { get; }

    public NotoDatabase Database { get; }

    public SqliteNoteRepository Notes { get; }

    public SqliteFolderRepository Folders { get; }

    public SqliteTagRepository Tags { get; }

    public MutableClock Clock { get; } = new(new DateTimeOffset(2026, 9, 15, 10, 30, 0, TimeSpan.Zero));

    public void Dispose() => _temp.Dispose();

    public FolderId SeedFolder(
        string name = "folder",
        double sortOrder = 0,
        bool pinned = false,
        DateTimeOffset? deletedAt = null)
    {
        var id = FolderId.New();

        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Folders
                (Id, Name, ColorKey, IsPinned, IsCollapsed,
                 SortOrder, CreatedAt, UpdatedAt, DeletedAt)
            VALUES
                ($id, $name, NULL, $isPinned, 0, $sortOrder, $now, $now, $deletedAt);
            """;
        command.Parameters.AddWithValue("$id", id.Value);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$isPinned", pinned ? 1 : 0);
        command.Parameters.AddWithValue("$sortOrder", sortOrder);
        command.Parameters.AddWithValue("$now", Clock.UtcNow.ToString("O"));
        command.Parameters.AddWithValue(
            "$deletedAt", deletedAt is { } d ? d.ToString("O") : DBNull.Value);
        command.ExecuteNonQuery();

        return id;
    }

    /// <summary>
    /// Inserts a note. <paramref name="updatedAt"/> is settable so Q5's
    /// ordering can be exercised independently of insertion order.
    /// </summary>
    public NoteId SeedNote(
        FolderId? folderId,
        double sortOrder = 0,
        bool pinned = false,
        string content = "note",
        DateTimeOffset? updatedAt = null,
        DateTimeOffset? deletedAt = null)
    {
        var id = NoteId.New();
        DateTimeOffset stamp = updatedAt ?? Clock.UtcNow;

        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Notes
                (Id, FolderId, Content, ColorKey, IsPinned, IsFolded,
                 SortOrder, CreatedAt, UpdatedAt, DeletedAt)
            VALUES
                ($id, $folderId, $content, NULL, $isPinned, 0,
                 $sortOrder, $created, $updated, $deletedAt);
            """;
        command.Parameters.AddWithValue("$id", id.Value);
        command.Parameters.AddWithValue("$folderId", folderId is { } f ? f.Value : DBNull.Value);
        command.Parameters.AddWithValue("$content", content);
        command.Parameters.AddWithValue("$isPinned", pinned ? 1 : 0);
        command.Parameters.AddWithValue("$sortOrder", sortOrder);
        command.Parameters.AddWithValue("$created", Clock.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$updated", stamp.ToString("O"));
        command.Parameters.AddWithValue(
            "$deletedAt", deletedAt is { } d ? d.ToString("O") : DBNull.Value);
        command.ExecuteNonQuery();

        return id;
    }

    public TagId SeedTag(string name)
    {
        var id = TagId.New();

        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Tags (Id, Name, ColorKey, CreatedAt)
            VALUES ($id, $name, NULL, $now);
            """;
        command.Parameters.AddWithValue("$id", id.Value);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$now", Clock.UtcNow.ToString("O"));
        command.ExecuteNonQuery();

        return id;
    }

    /// <summary>
    /// Inserts a note with a <b>caller-chosen id</b>.
    /// </summary>
    /// <remarks>
    /// <see cref="SeedNote"/> mints a fresh ULID, and ULIDs are
    /// creation-ordered — so notes seeded in sequence always have ascending
    /// ids, and insertion order equals id order. A tie-break test written that
    /// way passes whether or not the query orders by <c>Id</c>. Choosing the
    /// ids lets a test seed a tie in DESCENDING id order, where the two differ.
    /// </remarks>
    public NoteId SeedNoteWithId(
        NoteId id,
        FolderId? folderId,
        double sortOrder = 0,
        bool pinned = false)
    {
        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Notes
                (Id, FolderId, Content, ColorKey, IsPinned, IsFolded,
                 SortOrder, CreatedAt, UpdatedAt, DeletedAt)
            VALUES
                ($id, $folderId, 'note', NULL, $isPinned, 0,
                 $sortOrder, $now, $now, NULL);
            """;
        command.Parameters.AddWithValue("$id", id.Value);
        command.Parameters.AddWithValue("$folderId", folderId is { } f ? f.Value : DBNull.Value);
        command.Parameters.AddWithValue("$isPinned", pinned ? 1 : 0);
        command.Parameters.AddWithValue("$sortOrder", sortOrder);
        command.Parameters.AddWithValue("$now", Clock.UtcNow.ToString("O"));
        command.ExecuteNonQuery();

        return id;
    }

    /// <summary>Inserts a folder with a caller-chosen id, for the same reason.</summary>
    public FolderId SeedFolderWithId(FolderId id, double sortOrder = 0, bool pinned = false)
    {
        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Folders
                (Id, Name, ColorKey, IsPinned, IsCollapsed,
                 SortOrder, CreatedAt, UpdatedAt, DeletedAt)
            VALUES
                ($id, 'folder', NULL, $isPinned, 0, $sortOrder, $now, $now, NULL);
            """;
        command.Parameters.AddWithValue("$id", id.Value);
        command.Parameters.AddWithValue("$isPinned", pinned ? 1 : 0);
        command.Parameters.AddWithValue("$sortOrder", sortOrder);
        command.Parameters.AddWithValue("$now", Clock.UtcNow.ToString("O"));
        command.ExecuteNonQuery();

        return id;
    }

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

    public double SortOrderOf(NoteId id)
    {
        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT SortOrder FROM Notes WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id.Value);

        return Convert.ToDouble(
            command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    public double SortOrderOf(FolderId id)
    {
        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT SortOrder FROM Folders WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id.Value);

        return Convert.ToDouble(
            command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }
}
