using Noto.Core;
using Noto.Core.Commands;
using Noto.Core.Notes;
using Noto.Infrastructure.Storage;
using Noto.Infrastructure.Tests.Storage;
using Noto.UseCases.Notes;
using Xunit;

namespace Noto.Infrastructure.Tests.Notes;

/// <summary>
/// <c>GetNote</c> against real SQLite (contract §11, query Q1).
/// </summary>
public sealed class GetNoteTests : IDisposable
{
    private readonly TempDatabase _temp = new();
    private readonly NotoDatabase _database;
    private readonly SqliteNoteRepository _notes;
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 9, 15, 10, 30, 0, TimeSpan.Zero));

    public GetNoteTests()
    {
        _database = new NotoDatabase(_temp.DatabasePath);
        _database.Initialize();
        _notes = new SqliteNoteRepository(_database);
    }

    public void Dispose() => _temp.Dispose();

    private NoteId Create(string content) =>
        new CreateNoteHandler(_notes, _clock).Handle(new CreateNote(null, content)).Value;

    private GetNoteQuery Query() => new(_notes);

    [Fact]
    public void Reads_an_existing_note()
    {
        var id = Create("# Title\n\nbody");

        var result = Query().Execute(id);

        Assert.True(result.IsSuccess);
        Assert.Equal(id, result.Value.Id);
        Assert.Equal("# Title\n\nbody", result.Value.Content);
        Assert.Equal("Title", result.Value.Title);
    }

    [Fact]
    public void Returns_every_field()
    {
        var id = Create("x");

        Note note = Query().Execute(id).Value;

        Assert.Equal(id, note.Id);
        Assert.Null(note.FolderId);
        Assert.Null(note.ColorKey);
        Assert.False(note.IsPinned);
        Assert.False(note.IsFolded);
        Assert.Equal(_clock.UtcNow, note.CreatedAt);
        Assert.Equal(_clock.UtcNow, note.UpdatedAt);
        Assert.Null(note.DeletedAt);
    }

    [Fact]
    public void A_missing_id_is_NotFound_rather_than_null()
    {
        // Contract §6: null is never an overloaded failure channel. A caller
        // must be able to tell "no such note" from "a note with no content".
        var result = Query().Execute(NoteId.New());

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Failure);
        Assert.Equal(CommandFailureReason.NotFound, result.Failure!.Reason);
    }

    [Fact]
    public void A_failed_read_has_no_value_to_misuse()
    {
        var result = Query().Execute(NoteId.New());

        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void Reading_does_not_change_updated_at()
    {
        // Contract §4: queries are not commands and stamp nothing. A read that
        // touched UpdatedAt would reorder "sort by modified" simply because
        // someone looked at a note.
        var id = Create("x");
        DateTimeOffset before = Query().Execute(id).Value.UpdatedAt;

        for (int i = 0; i < 5; i++)
        {
            Query().Execute(id);
        }

        Assert.Equal(before, Query().Execute(id).Value.UpdatedAt);
    }

    [Fact]
    public void Reading_writes_nothing_at_all()
    {
        var id = Create("x");

        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Notes;";
        long before = (long)command.ExecuteScalar()!;

        Query().Execute(id);
        Query().Execute(NoteId.New());

        Assert.Equal(before, (long)command.ExecuteScalar()!);
    }

    [Fact]
    public void A_deleted_note_is_NotFound()
    {
        // Invariant I1: GetNote is an ordinary query, so it filters
        // DeletedAt IS NULL. The recycle bin has exactly one surface —
        // ListDeletedNotes (I2) — which arrives in Slice 3.
        var id = Create("in the bin");
        SoftDelete(id);

        var result = Query().Execute(id);

        Assert.False(result.IsSuccess);
        Assert.Equal(CommandFailureReason.NotFound, result.Failure!.Reason);
    }

    [Fact]
    public void A_deleted_note_is_indistinguishable_from_a_missing_one()
    {
        // Both report NotFound. The bin is not reachable by guessing at the
        // ordinary query surface.
        var id = Create("in the bin");
        SoftDelete(id);

        var deleted = Query().Execute(id);
        var missing = Query().Execute(NoteId.New());

        Assert.Equal(missing.Failure!.Reason, deleted.Failure!.Reason);
    }

    [Fact]
    public void An_active_sibling_is_unaffected_when_another_note_is_deleted()
    {
        // The filter must not be so broad that it hides live notes.
        var deleted = Create("gone");
        var live = Create("still here");
        SoftDelete(deleted);

        Assert.False(Query().Execute(deleted).IsSuccess);
        Assert.True(Query().Execute(live).IsSuccess);
    }

    [Fact]
    public void Content_round_trips_including_newlines_and_unicode()
    {
        const string content = "line one\r\nline two\rline three\nÜnïcödé 🗒️";

        var id = Create(content);

        Assert.Equal(content, Query().Execute(id).Value.Content);
    }

    private void SoftDelete(NoteId id)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Notes SET DeletedAt = $now WHERE Id = $id;";
        command.Parameters.AddWithValue("$now", _clock.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$id", id.Value);
        command.ExecuteNonQuery();
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
