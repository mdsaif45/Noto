using Microsoft.Data.Sqlite;
using Noto.Core;
using Noto.Core.Folders;
using Noto.Core.Notes;
using Noto.Infrastructure.Storage;
using Noto.Infrastructure.Tests.Storage;

namespace Noto.Infrastructure.Tests.Folders;

/// <summary>
/// A real SQLite database plus the folder and note repositories, shared by the
/// Slice 4 test classes.
/// </summary>
/// <remarks>
/// <para>
/// Real SQLite rather than a fake repository, for the same reason the note
/// tests use it: the behaviour worth proving — that the cascade actually writes
/// the rows it should and leaves alone the ones it should not — is exactly what
/// a fake cannot prove (ADR-003).
/// </para>
/// <para>
/// Shared because six folder test classes need the same seeding and the same
/// raw-column readers. Each class still constructs its own instance, so the
/// per-class isolation that <see cref="TempDatabase"/> provides is unchanged.
/// </para>
/// </remarks>
public sealed class FolderTestContext : IDisposable
{
    private readonly TempDatabase _temp = new();

    public FolderTestContext()
    {
        DatabasePath = _temp.DatabasePath;
        Database = new NotoDatabase(DatabasePath);
        Database.Initialize();
        Folders = new SqliteFolderRepository(Database);
        Notes = new SqliteNoteRepository(Database);
    }

    /// <summary>The database file, so a test can reopen it independently.</summary>
    public string DatabasePath { get; }

    public NotoDatabase Database { get; }

    public SqliteFolderRepository Folders { get; }

    public SqliteNoteRepository Notes { get; }

    /// <summary>A clock the tests advance explicitly, so stamps are checkable.</summary>
    public MutableClock Clock { get; } = new(new DateTimeOffset(2026, 9, 15, 10, 30, 0, TimeSpan.Zero));

    public void Dispose() => _temp.Dispose();

    /// <summary>
    /// Inserts a folder directly, bypassing the command layer.
    /// </summary>
    /// <remarks>
    /// Raw SQL on purpose: a test for <c>DeleteFolder</c> must be able to set
    /// up a deleted folder without depending on <c>DeleteFolder</c> being
    /// correct, or it proves nothing when both are wrong the same way.
    /// </remarks>
    public FolderId SeedFolder(
        string name = "Folder",
        double sortOrder = 0,
        bool pinned = false,
        DateTimeOffset? deletedAt = null,
        DateTimeOffset? createdAt = null)
    {
        var id = FolderId.New();
        DateTimeOffset stamp = createdAt ?? Clock.UtcNow;

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
        command.Parameters.AddWithValue("$now", stamp.ToString("O"));
        command.Parameters.AddWithValue(
            "$deletedAt", deletedAt is { } d ? d.ToString("O") : DBNull.Value);
        command.ExecuteNonQuery();

        return id;
    }

    /// <summary>Inserts a note directly, bypassing the command layer.</summary>
    public NoteId SeedNote(
        FolderId? folderId,
        string content = "note",
        DateTimeOffset? deletedAt = null)
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
                ($id, $folderId, $content, NULL, 0, 0, 0, $now, $now, $deletedAt);
            """;
        command.Parameters.AddWithValue("$id", id.Value);
        command.Parameters.AddWithValue(
            "$folderId", folderId is { } f ? f.Value : DBNull.Value);
        command.Parameters.AddWithValue("$content", content);
        command.Parameters.AddWithValue("$now", Clock.UtcNow.ToString("O"));
        command.Parameters.AddWithValue(
            "$deletedAt", deletedAt is { } d ? d.ToString("O") : DBNull.Value);
        command.ExecuteNonQuery();

        return id;
    }

    /// <summary>Reads one raw column of a folder row, deleted or not.</summary>
    /// <remarks>
    /// Raw because the repository's own reads apply I1, so they cannot observe
    /// a deleted row — and several of these tests exist precisely to check what
    /// happened to one.
    /// </remarks>
    public object? ReadFolderColumn(FolderId id, string column) =>
        ReadColumn("Folders", column, id.Value);

    /// <summary>Reads one raw column of a note row, deleted or not.</summary>
    public object? ReadNoteColumn(NoteId id, string column) =>
        ReadColumn("Notes", column, id.Value);

    /// <summary><c>DeletedAt</c> as text, or <see langword="null"/> when active.</summary>
    public string? DeletedAtOf(NoteId id) => ReadNoteColumn(id, "DeletedAt") as string;

    /// <summary><c>DeletedAt</c> as text, or <see langword="null"/> when active.</summary>
    public string? DeletedAtOf(FolderId id) => ReadFolderColumn(id, "DeletedAt") as string;

    public double SortOrderOf(FolderId id) =>
        Convert.ToDouble(ReadFolderColumn(id, "SortOrder"), System.Globalization.CultureInfo.InvariantCulture);

    public string UpdatedAtOf(FolderId id) => (string)ReadFolderColumn(id, "UpdatedAt")!;

    public string CreatedAtOf(FolderId id) => (string)ReadFolderColumn(id, "CreatedAt")!;

    public string NameOf(FolderId id) => (string)ReadFolderColumn(id, "Name")!;

    public bool IsPinnedOf(FolderId id) =>
        Convert.ToInt64(ReadFolderColumn(id, "IsPinned"), System.Globalization.CultureInfo.InvariantCulture) != 0;

    /// <summary>The active folders, in O2 order.</summary>
    public IReadOnlyList<FolderId> ActiveFolderOrder()
    {
        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id FROM Folders
            WHERE DeletedAt IS NULL
            ORDER BY SortOrder ASC, Id ASC;
            """;

        var ids = new List<FolderId>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            ids.Add(FolderId.From(reader.GetString(0)));
        }

        return ids;
    }

    private object? ReadColumn(string table, string column, string id)
    {
        // The table and column names come from this file's own call sites and
        // are compile-time constants, never test input.
        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {column} FROM {table} WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);

        object? value = command.ExecuteScalar();

        return value is DBNull ? null : value;
    }
}

/// <summary>
/// A clock the test moves on demand.
/// </summary>
/// <remarks>
/// Distinguishing "this row was stamped by the operation under test" from "this
/// row was stamped when it was seeded" needs two different instants, so a fixed
/// clock cannot express it.
/// </remarks>
public sealed class MutableClock(DateTimeOffset start) : IClock
{
    public DateTimeOffset UtcNow { get; private set; } = start;

    /// <summary>Moves the clock forward and returns the new instant.</summary>
    public DateTimeOffset Advance(TimeSpan by) => UtcNow += by;
}
