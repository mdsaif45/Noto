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

        Assert.Equal(content, _notes.Find(id)!.Content);
    }

    [Fact]
    public void Derives_the_title_without_storing_it()
    {
        // Migration 002 removed Notes.Title (parity B16, conflict C1). The
        // title must come from content on every read, so there is exactly one
        // source of truth.
        var id = Handler().Handle(new CreateNote(null, "# Derived\n\nbody")).Value;

        Assert.Equal("Derived", _notes.Find(id)!.Title);

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

        Note note = _notes.Find(id)!;

        Assert.Equal(_clock.UtcNow, note.CreatedAt);
        Assert.Equal(_clock.UtcNow, note.UpdatedAt);
        Assert.Equal(note.CreatedAt, note.UpdatedAt);
    }

    [Fact]
    public void Applies_the_documented_defaults()
    {
        var id = Handler().Handle(new CreateNote(null, "x")).Value;

        Note note = _notes.Find(id)!;

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

        Assert.Equal(folder, _notes.Find(id)!.FolderId);
    }

    [Fact]
    public void An_empty_note_is_valid()
    {
        // Parity B26 has a placeholder for empty notes, so emptiness is a
        // supported state rather than an error.
        var result = Handler().Handle(new CreateNote(null, string.Empty));

        Assert.True(result.IsSuccess);

        Note note = _notes.Find(result.Value)!;
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
    public void A_deleted_folder_is_NotFound()
    {
        // Invariant I5: a deleted folder is inert, so it cannot receive a new
        // note. NotFound rather than InvalidState deliberately — distinguishing
        // them would disclose that a deleted folder exists, and the caller's
        // response is the same either way.
        FolderId folder = InsertFolder("Archive", deleted: true);

        var result = Handler().Handle(new CreateNote(folder, "x"));

        Assert.False(result.IsSuccess);
        Assert.Equal(CommandFailureReason.NotFound, result.Failure!.Reason);
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

        Assert.Equal("durable", new SqliteNoteRepository(reopened).Find(id)!.Content);
    }

    [Fact]
    public void Two_notes_created_in_sequence_are_ordered_by_id()
    {
        // Until reordering lands in Slice 2, the Id tiebreak is what orders
        // notes — and ULIDs make it creation order (design §7 rule 2).
        var first = Handler().Handle(new CreateNote(null, "a")).Value;
        var second = Handler().Handle(new CreateNote(null, "b")).Value;

        Assert.True(string.CompareOrdinal(first.Value, second.Value) < 0);
    }

    [Fact]
    public void A_null_command_throws()
    {
        Assert.Throws<ArgumentNullException>(() => Handler().Handle(null!));
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
