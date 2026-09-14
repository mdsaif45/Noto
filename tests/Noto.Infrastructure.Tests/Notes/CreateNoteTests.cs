using Microsoft.Data.Sqlite;
using Noto.Core;
using Noto.Core.Commands;
using Noto.Core.Folders;
using Noto.Core.Identifiers;
using Noto.Core.Notes;
using Noto.Infrastructure.Storage;
using Noto.Infrastructure.Tests.Storage;
using Noto.UseCases.Notes;
using Xunit;

namespace Noto.Infrastructure.Tests.Notes;

/// <summary>
/// <c>CreateNote</c> against real SQLite (contract §11, command 1).
/// </summary>
/// <remarks>
/// Real SQLite rather than a fake repository: the behaviour worth proving —
/// that a row is actually written, with the right columns, and survives a
/// reopen — is exactly what a fake cannot prove (ADR-003).
/// </remarks>
public sealed class CreateNoteTests : IDisposable
{
    private readonly TempDatabase _temp = new();
    private readonly NotoDatabase _database;
    private readonly SqliteNoteRepository _notes;
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 9, 15, 10, 30, 0, TimeSpan.Zero));

    public CreateNoteTests()
    {
        _database = new NotoDatabase(_temp.DatabasePath);
        _database.Initialize();
        _notes = new SqliteNoteRepository(_database);
    }

    public void Dispose() => _temp.Dispose();

    private CreateNoteHandler Handler() => new(_notes, _clock);

    [Fact]
    public void Creates_a_note_at_root_and_returns_its_id()
    {
        var result = Handler().Handle(new CreateNote(null, "First note"));

        Assert.True(result.IsSuccess);
        Assert.True(Ulid.IsValid(result.Value.Value));
    }

    [Fact]
    public void Persists_the_content_verbatim()
    {
        // ADR-004: content is markdown SOURCE. Newlines, markup and unicode
        // must survive byte for byte, or the storage format is not a contract.
        const string content = "# Heading\r\n\r\n- [ ] task\n\nUnicode: 日本語 🗒️";

        var id = Handler().Handle(new CreateNote(null, content)).Value;

        Assert.Equal(content, _notes.FindActive(id)!.Content);
    }

    [Fact]
    public void Derives_the_title_without_storing_it()
    {
        // Migration 002 removed Notes.Title (parity B16, conflict C1). The
        // title must come from content on every read, so there is exactly one
        // source of truth.
        var id = Handler().Handle(new CreateNote(null, "# Derived\n\nbody")).Value;

        Assert.Equal("Derived", _notes.FindActive(id)!.Title);

        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Notes') WHERE name = 'Title';";

        Assert.Equal(0L, (long)command.ExecuteScalar()!);
    }

    [Fact]
    public void Stamps_created_and_updated_with_one_timestamp()
    {
        // Contract §4: one timestamp captured once. A note reporting that it
        // was modified after it was created would be a lie under "sort by
        // modified" (parity C10).
        var id = Handler().Handle(new CreateNote(null, "x")).Value;

        Note note = _notes.FindActive(id)!;

        Assert.Equal(_clock.UtcNow, note.CreatedAt);
        Assert.Equal(_clock.UtcNow, note.UpdatedAt);
        Assert.Equal(note.CreatedAt, note.UpdatedAt);
    }

    [Fact]
    public void Applies_the_documented_defaults()
    {
        var id = Handler().Handle(new CreateNote(null, "x")).Value;

        Note note = _notes.FindActive(id)!;

        Assert.Null(note.FolderId);      // root scope
        Assert.Null(note.ColorKey);
        Assert.False(note.IsPinned);
        Assert.False(note.IsFolded);
        Assert.Null(note.DeletedAt);
        Assert.False(note.IsDeleted);
    }

    [Fact]
    public void Creates_a_note_inside_an_existing_folder()
    {
        FolderId folder = InsertFolder("Work");

        var id = Handler().Handle(new CreateNote(folder, "x")).Value;

        Assert.Equal(folder, _notes.FindActive(id)!.FolderId);
    }

    [Fact]
    public void An_empty_note_is_valid()
    {
        // Parity B26 has a placeholder for empty notes, so emptiness is a
        // supported state rather than an error.
        var result = Handler().Handle(new CreateNote(null, string.Empty));

        Assert.True(result.IsSuccess);

        Note note = _notes.FindActive(result.Value)!;
        Assert.Equal(string.Empty, note.Content);
        Assert.Equal(string.Empty, note.Title);
    }

    [Fact]
    public void An_unknown_folder_is_NotFound()
    {
        var result = Handler().Handle(new CreateNote(FolderId.New(), "x"));

        Assert.False(result.IsSuccess);
        Assert.Equal(CommandFailureReason.NotFound, result.Failure!.Reason);
    }

    [Fact]
    public void A_deleted_folder_is_InvalidState()
    {
        // The contract separates the two cases: a folder that never existed is
        // NotFound, while one in the recycle bin exists but is inert
        // (invariant I5), which is InvalidState.
        FolderId folder = InsertFolder("Archive", deleted: true);

        var result = Handler().Handle(new CreateNote(folder, "x"));

        Assert.False(result.IsSuccess);
        Assert.Equal(CommandFailureReason.InvalidState, result.Failure!.Reason);
    }

    [Fact]
    public void A_deleted_folder_cannot_receive_a_note()
    {
        FolderId folder = InsertFolder("Archive", deleted: true);

        Handler().Handle(new CreateNote(folder, "x"));

        Assert.Equal(0L, ScalarLong("SELECT COUNT(*) FROM Notes;"));
    }

    [Fact]
    public void A_failed_create_writes_nothing()
    {
        Handler().Handle(new CreateNote(FolderId.New(), "x"));

        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Notes;";

        Assert.Equal(0L, (long)command.ExecuteScalar()!);
    }

    [Fact]
    public void The_note_survives_closing_and_reopening()
    {
        var id = Handler().Handle(new CreateNote(null, "durable")).Value;

        SqliteConnection.ClearAllPools();

        var reopened = new NotoDatabase(_temp.DatabasePath);
        reopened.Initialize();

        Assert.Equal("durable", new SqliteNoteRepository(reopened).FindActive(id)!.Content);
    }

    // ---- O3: a new note is placed at the END of its scope ----------------

    [Fact]
    public void The_first_note_in_an_empty_root_scope_starts_the_order()
    {
        var id = Handler().Handle(new CreateNote(null, "first")).Value;

        Assert.Equal(0d, _notes.FindActive(id)!.SortOrder);
    }

    [Fact]
    public void A_second_root_note_is_placed_after_the_first()
    {
        // O3: insert at an end uses max + 1, so ordering does not depend on the
        // Id tiebreak.
        var first = Handler().Handle(new CreateNote(null, "a")).Value;
        var second = Handler().Handle(new CreateNote(null, "b")).Value;

        double firstOrder = _notes.FindActive(first)!.SortOrder;
        double secondOrder = _notes.FindActive(second)!.SortOrder;

        Assert.True(secondOrder > firstOrder, $"{secondOrder} should follow {firstOrder}");
        Assert.Equal(firstOrder + 1, secondOrder);
    }

    [Fact]
    public void The_first_note_in_an_empty_folder_starts_that_scope()
    {
        FolderId folder = InsertFolder("Work");

        var id = Handler().Handle(new CreateNote(folder, "first")).Value;

        Assert.Equal(0d, _notes.FindActive(id)!.SortOrder);
    }

    [Fact]
    public void A_second_folder_note_is_placed_after_the_first()
    {
        FolderId folder = InsertFolder("Work");

        var first = Handler().Handle(new CreateNote(folder, "a")).Value;
        var second = Handler().Handle(new CreateNote(folder, "b")).Value;

        Assert.Equal(
            _notes.FindActive(first)!.SortOrder + 1,
            _notes.FindActive(second)!.SortOrder);
    }

    [Fact]
    public void Root_and_folder_scopes_are_ordered_independently()
    {
        // O1: ordering is scoped per folder, and root is its own scope. A note
        // added to a folder must not be pushed along by root's contents.
        FolderId folder = InsertFolder("Work");

        Handler().Handle(new CreateNote(null, "root a"));
        Handler().Handle(new CreateNote(null, "root b"));
        Handler().Handle(new CreateNote(null, "root c"));

        var inFolder = Handler().Handle(new CreateNote(folder, "folder a")).Value;

        Assert.Equal(0d, _notes.FindActive(inFolder)!.SortOrder);
    }

    [Fact]
    public void An_existing_arbitrary_sort_order_is_respected()
    {
        // The scope's existing maximum decides the next position, whatever it
        // happens to be — the rule is max + 1, not "count of notes".
        InsertNoteWithSortOrder(sortOrder: 41.5);

        var id = Handler().Handle(new CreateNote(null, "after")).Value;

        Assert.Equal(42.5, _notes.FindActive(id)!.SortOrder);
    }

    [Fact]
    public void A_negative_existing_sort_order_is_respected()
    {
        // O3's other end is min - 1, so negative values are ordinary.
        InsertNoteWithSortOrder(sortOrder: -3);

        var id = Handler().Handle(new CreateNote(null, "after")).Value;

        Assert.Equal(-2d, _notes.FindActive(id)!.SortOrder);
    }

    [Fact]
    public void A_deleted_sibling_does_not_affect_placement()
    {
        // I6: deleted rows keep their SortOrder but never participate in
        // ordering, so a deleted note with a huge value must not push new
        // notes past it.
        InsertNoteWithSortOrder(sortOrder: 1000, deleted: true);

        var id = Handler().Handle(new CreateNote(null, "first live note")).Value;

        Assert.Equal(0d, _notes.FindActive(id)!.SortOrder);
    }

    [Fact]
    public void A_null_command_throws()
    {
        Assert.Throws<ArgumentNullException>(() => Handler().Handle(null!));
    }

    private long ScalarLong(string sql)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)command.ExecuteScalar()!;
    }

    private void InsertNoteWithSortOrder(double sortOrder, bool deleted = false)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Notes (Id, Content, SortOrder, CreatedAt, UpdatedAt, DeletedAt)
            VALUES ($id, 'existing', $sortOrder, $now, $now, $deletedAt);
            """;
        command.Parameters.AddWithValue("$id", NoteId.New().Value);
        command.Parameters.AddWithValue("$sortOrder", sortOrder);
        command.Parameters.AddWithValue("$now", _clock.UtcNow.ToString("O"));
        command.Parameters.AddWithValue(
            "$deletedAt", deleted ? _clock.UtcNow.ToString("O") : DBNull.Value);
        command.ExecuteNonQuery();
    }

    private FolderId InsertFolder(string name, bool deleted = false)
    {
        var id = FolderId.New();

        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Folders (Id, Name, SortOrder, CreatedAt, UpdatedAt, DeletedAt)
            VALUES ($id, $name, 0, $now, $now, $deletedAt);
            """;
        command.Parameters.AddWithValue("$id", id.Value);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$now", _clock.UtcNow.ToString("O"));
        command.Parameters.AddWithValue(
            "$deletedAt", deleted ? _clock.UtcNow.ToString("O") : DBNull.Value);
        command.ExecuteNonQuery();

        return id;
    }

    /// <summary>A clock that does not move, so timestamps are assertable.</summary>
    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
