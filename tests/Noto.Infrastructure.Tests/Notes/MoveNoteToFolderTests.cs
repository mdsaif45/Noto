using Microsoft.Data.Sqlite;
using Noto.Core;
using Noto.Core.Commands;
using Noto.Core.Folders;
using Noto.Core.Notes;
using Noto.Infrastructure.Storage;
using Noto.Infrastructure.Tests.Storage;
using Noto.UseCases.Notes;
using Xunit;

namespace Noto.Infrastructure.Tests.Notes;

/// <summary>
/// <c>MoveNoteToFolder</c> against real SQLite (contract §11, command 5).
/// </summary>
/// <remarks>
/// One of the five atomic operations (design §9): the folder change and the new
/// <c>SortOrder</c> commit together.
/// </remarks>
public sealed class MoveNoteToFolderTests : IDisposable
{
    private readonly TempDatabase _temp = new();
    private readonly NotoDatabase _database;
    private readonly SqliteNoteRepository _notes;
    private readonly MutableClock _clock = new(new DateTimeOffset(2026, 9, 15, 10, 30, 0, TimeSpan.Zero));

    public MoveNoteToFolderTests()
    {
        _database = new NotoDatabase(_temp.DatabasePath);
        _database.Initialize();
        _notes = new SqliteNoteRepository(_database);
    }

    public void Dispose() => _temp.Dispose();

    private NoteId Create(string content, FolderId? folder = null) =>
        new CreateNoteHandler(_notes, _clock).Handle(new CreateNote(folder, content)).Value;

    private CommandResult Move(NoteId id, FolderId? target) =>
        new MoveNoteToFolderHandler(_notes, _clock).Handle(new MoveNoteToFolder(id, target));

    // ---- the three directions --------------------------------------------

    [Fact]
    public void Root_to_folder()
    {
        FolderId folder = InsertFolder("Work");
        var id = Create("note");

        Assert.True(Move(id, folder).IsSuccess);

        Assert.Equal(folder, _notes.FindActive(id)!.FolderId);
    }

    [Fact]
    public void Folder_to_root()
    {
        FolderId folder = InsertFolder("Work");
        var id = Create("note", folder);

        Assert.True(Move(id, null).IsSuccess);

        Assert.Null(_notes.FindActive(id)!.FolderId);
    }

    [Fact]
    public void Folder_to_another_folder()
    {
        FolderId from = InsertFolder("From");
        FolderId to = InsertFolder("To");
        var id = Create("note", from);

        Assert.True(Move(id, to).IsSuccess);

        Assert.Equal(to, _notes.FindActive(id)!.FolderId);
    }

    // ---- ordering at the destination (O4) --------------------------------

    [Fact]
    public void The_note_lands_at_the_end_of_the_destination_scope()
    {
        FolderId folder = InsertFolder("Work");
        var a = Create("a", folder);
        var b = Create("b", folder);
        var moved = Create("moved");

        Move(moved, folder);

        Assert.Equal([a, b, moved], OrderedIds(folder));
    }

    [Fact]
    public void The_new_sort_order_is_computed_in_the_destination_scope()
    {
        // O4: a new SortOrder in the TARGET scope. Carrying the old value over
        // would place the note arbitrarily among its new siblings.
        FolderId folder = InsertFolder("Work");
        Create("a", folder);
        Create("b", folder);

        var moved = Create("moved");
        double before = _notes.FindActive(moved)!.SortOrder;

        Move(moved, folder);

        double after = _notes.FindActive(moved)!.SortOrder;
        Assert.NotEqual(before, after);
        Assert.True(after > _notes.FindActive(OrderedIds(folder)[0])!.SortOrder);
    }

    [Fact]
    public void Moving_out_leaves_the_source_scope_ordered()
    {
        // O5: deleting or removing leaves gaps, and gaps are harmless. The
        // remaining notes must not be renumbered or reordered.
        FolderId folder = InsertFolder("Work");
        var a = Create("a", folder);
        var b = Create("b", folder);
        var c = Create("c", folder);

        Move(b, null);

        Assert.Equal([a, c], OrderedIds(folder));
    }

    [Fact]
    public void A_note_moved_into_an_empty_folder_starts_that_scope()
    {
        FolderId folder = InsertFolder("Empty");
        var id = Create("note");

        Move(id, folder);

        Assert.Equal([id], OrderedIds(folder));
    }

    // ---- preconditions ----------------------------------------------------

    [Fact]
    public void A_missing_note_is_NotFound()
    {
        FolderId folder = InsertFolder("Work");

        var result = Move(NoteId.New(), folder);

        Assert.False(result.IsSuccess);
        Assert.Equal(CommandFailureReason.NotFound, result.Failure!.Reason);
    }

    [Fact]
    public void A_deleted_note_is_InvalidState()
    {
        FolderId folder = InsertFolder("Work");
        var id = Create("note");
        SoftDeleteNote(id);

        var result = Move(id, folder);

        Assert.False(result.IsSuccess);
        Assert.Equal(CommandFailureReason.InvalidState, result.Failure!.Reason);
    }

    [Fact]
    public void A_missing_destination_is_NotFound()
    {
        var id = Create("note");

        var result = Move(id, FolderId.New());

        Assert.False(result.IsSuccess);
        Assert.Equal(CommandFailureReason.NotFound, result.Failure!.Reason);
    }

    [Fact]
    public void A_deleted_destination_is_InvalidState()
    {
        FolderId folder = InsertFolder("Archive");
        SoftDeleteFolder(folder);
        var id = Create("note");

        var result = Move(id, folder);

        Assert.False(result.IsSuccess);
        Assert.Equal(CommandFailureReason.InvalidState, result.Failure!.Reason);
    }

    [Fact]
    public void An_active_note_in_a_deleted_source_folder_can_be_moved_out()
    {
        // Case C: the folder is deleted but the note is active. Only the
        // DESTINATION is validated — this is precisely how a user rescues a
        // note from a deleted folder.
        FolderId deletedFolder = InsertFolder("Archive");
        var id = Create("rescue me", deletedFolder);
        SoftDeleteFolder(deletedFolder);

        Assert.True(Move(id, null).IsSuccess);

        Assert.Null(_notes.FindActive(id)!.FolderId);
    }

    // ---- state preservation ----------------------------------------------

    [Fact]
    public void Everything_except_folder_order_and_timestamp_is_preserved()
    {
        FolderId folder = InsertFolder("Work");
        var id = Create("# Title\n\nbody");
        Note original = _notes.FindActive(id)!;

        _clock.Advance(TimeSpan.FromHours(1));
        Move(id, folder);

        Note moved = _notes.FindActive(id)!;
        Assert.Equal(original.Content, moved.Content);
        Assert.Equal(original.Title, moved.Title);
        Assert.Equal(original.CreatedAt, moved.CreatedAt);
        Assert.Equal(original.IsPinned, moved.IsPinned);
        Assert.Equal(original.IsFolded, moved.IsFolded);
        Assert.Equal(original.ColorKey, moved.ColorKey);
        Assert.Null(moved.DeletedAt);
    }

    [Fact]
    public void A_move_stamps_updated_at()
    {
        FolderId folder = InsertFolder("Work");
        var id = Create("note");

        _clock.Advance(TimeSpan.FromHours(3));
        Move(id, folder);

        Assert.Equal(_clock.UtcNow, _notes.FindActive(id)!.UpdatedAt);
    }

    [Fact]
    public void A_rejected_move_changes_nothing()
    {
        FolderId folder = InsertFolder("Work");
        var id = Create("note", folder);
        Note original = _notes.FindActive(id)!;

        _clock.Advance(TimeSpan.FromHours(1));
        Move(id, FolderId.New());

        Note after = _notes.FindActive(id)!;
        Assert.Equal(original.FolderId, after.FolderId);
        Assert.Equal(original.SortOrder, after.SortOrder);
        Assert.Equal(original.UpdatedAt, after.UpdatedAt);
    }

    // ---- atomicity (design §9) -------------------------------------------

    [Fact]
    public void The_folder_change_and_the_new_order_commit_together()
    {
        // Design §9 lists MoveNoteToFolder as atomic: "folder change + new
        // SortOrder". Reading the row after the commit must never show one
        // applied without the other.
        FolderId folder = InsertFolder("Work");
        Create("existing", folder);
        var moved = Create("moved");

        Move(moved, folder);

        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT FolderId, SortOrder FROM Notes WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", moved.Value);

        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(folder.Value, reader.GetString(0));
        Assert.True(reader.GetDouble(1) > 0, "the note kept a stale root SortOrder");
    }

    [Fact]
    public void A_failed_move_rolls_back_completely()
    {
        // Forces the UPDATE inside the transaction to fail by pointing the note
        // at a folder that passes the handler's checks and is then removed, so
        // the foreign key rejects the write. Nothing may survive.
        FolderId folder = InsertFolder("Work");
        var id = Create("note");
        Note original = _notes.FindActive(id)!;

        HardDeleteFolder(folder);

        // Specifically a StorageException: a different exception type would
        // mean the test proved rollback for the wrong reason.
        var thrown = Assert.Throws<Noto.Core.Storage.StorageException>(() =>
            _notes.Move(id, folder, NotePlacement.Last, _clock.UtcNow.AddHours(1)));

        Assert.Equal(Noto.Core.Storage.StorageFailure.WriteFailed, thrown.Reason);

        Note after = _notes.FindActive(id)!;
        Assert.Equal(original.FolderId, after.FolderId);
        Assert.Equal(original.SortOrder, after.SortOrder);
        Assert.Equal(original.UpdatedAt, after.UpdatedAt);
        Assert.Equal(original.Content, after.Content);
    }

    [Fact]
    public void The_move_survives_closing_and_reopening()
    {
        FolderId folder = InsertFolder("Work");
        var id = Create("note");

        Move(id, folder);

        SqliteConnection.ClearAllPools();
        var reopened = new NotoDatabase(_temp.DatabasePath);
        reopened.Initialize();

        Assert.Equal(folder, new SqliteNoteRepository(reopened).FindActive(id)!.FolderId);
    }

    [Fact]
    public void A_null_command_throws()
    {
        var handler = new MoveNoteToFolderHandler(_notes, _clock);

        Assert.Throws<ArgumentNullException>(() => handler.Handle(null!));
    }

    // ---- helpers ----------------------------------------------------------

    private List<NoteId> OrderedIds(FolderId? folder)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();

        command.CommandText = folder is null
            ? """
              SELECT Id FROM Notes WHERE FolderId IS NULL AND DeletedAt IS NULL
              ORDER BY IsPinned DESC, SortOrder ASC, Id ASC;
              """
            : """
              SELECT Id FROM Notes WHERE FolderId = $folderId AND DeletedAt IS NULL
              ORDER BY IsPinned DESC, SortOrder ASC, Id ASC;
              """;

        if (folder is { } scope)
        {
            command.Parameters.AddWithValue("$folderId", scope.Value);
        }

        var ids = new List<NoteId>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            ids.Add(NoteId.From(reader.GetString(0)));
        }

        return ids;
    }

    private FolderId InsertFolder(string name)
    {
        var id = FolderId.New();

        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Folders (Id, Name, SortOrder, CreatedAt, UpdatedAt)
            VALUES ($id, $name, 0, $now, $now);
            """;
        command.Parameters.AddWithValue("$id", id.Value);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$now", _clock.UtcNow.ToString("O"));
        command.ExecuteNonQuery();

        return id;
    }

    private void SoftDeleteNote(NoteId id) =>
        Execute("UPDATE Notes SET DeletedAt = $now WHERE Id = $id;", id.Value);

    private void SoftDeleteFolder(FolderId id) =>
        Execute("UPDATE Folders SET DeletedAt = $now WHERE Id = $id;", id.Value);

    private void HardDeleteFolder(FolderId id)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Folders WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id.Value);
        command.ExecuteNonQuery();
    }

    private void Execute(string sql, string id)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$now", _clock.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    private sealed class MutableClock(DateTimeOffset start) : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = start;

        public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
    }
}
