using Noto.Core.Commands;
using Noto.Core.Notes;
using Noto.Core.Tags;
using Noto.UseCases.Tags;
using Xunit;

namespace Noto.Infrastructure.Tests.Tags;

/// <summary>
/// <c>DeleteTag</c> against real SQLite — the engine's only hard delete
/// (contract §11 row 21, §8, §10).
/// </summary>
public sealed class DeleteTagTests : IDisposable
{
    private readonly TagTestContext _context = new();

    public void Dispose() => _context.Dispose();

    private DeleteTagHandler Handler() => new(_context.Tags);

    [Fact]
    public void Hard_deletes_the_tag_row()
    {
        // HARD: the row is gone, not flagged. Tags has no DeletedAt to set.
        var id = _context.SeedTag("Work");

        var result = Handler().Handle(new DeleteTag(id));

        Assert.True(result.IsSuccess);
        Assert.False(_context.TagExists(id));
        Assert.Equal(0, _context.TagCount());
    }

    [Fact]
    public void The_schema_has_no_soft_delete_column_for_tags()
    {
        // §8 makes this the only hard delete, and the schema is what enforces
        // it: with no DeletedAt column, no implementation can soft-delete a tag
        // by accident.
        using var connection = _context.Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM pragma_table_info('Tags') WHERE name = 'DeletedAt';";

        Assert.Equal(0L, (long)command.ExecuteScalar()!);
    }

    [Fact]
    public void Removes_the_relationships()
    {
        var tag = _context.SeedTag("Work");
        var first = _context.SeedNote();
        var second = _context.SeedNote();
        _context.SeedRelationship(first, tag);
        _context.SeedRelationship(second, tag);

        Assert.Equal(2, _context.RelationshipCountFor(tag));

        Handler().Handle(new DeleteTag(tag));

        Assert.Equal(0, _context.RelationshipCount());
    }

    [Fact]
    public void Leaves_the_notes_intact()
    {
        // §11 row 21: "removes NoteTags, leaves notes intact". The notes are
        // content; the tag is a label.
        var tag = _context.SeedTag("Work");
        var first = _context.SeedNote();
        var second = _context.SeedNote();
        _context.SeedRelationship(first, tag);
        _context.SeedRelationship(second, tag);

        Handler().Handle(new DeleteTag(tag));

        Assert.True(_context.NoteExists(first));
        Assert.True(_context.NoteExists(second));
        Assert.NotNull(_context.Notes.FindActive(first));
        Assert.NotNull(_context.Notes.FindActive(second));
    }

    [Fact]
    public void Does_not_stamp_the_notes_it_unlinks()
    {
        // Deleting a tag is not an edit of the notes that carried it
        // (contract §4 — the note row is not changed).
        var tag = _context.SeedTag("Work");
        var note = _context.SeedNote();
        _context.SeedRelationship(note, tag);

        string before = _context.UpdatedAtOf(note);
        _context.Clock.Advance(TimeSpan.FromHours(3));

        Handler().Handle(new DeleteTag(tag));

        Assert.Equal(before, _context.UpdatedAtOf(note));
    }

    [Fact]
    public void Leaves_other_tags_and_their_relationships_untouched()
    {
        var target = _context.SeedTag("Target");
        var other = _context.SeedTag("Other");
        var note = _context.SeedNote();
        _context.SeedRelationship(note, target);
        _context.SeedRelationship(note, other);

        Handler().Handle(new DeleteTag(target));

        Assert.True(_context.TagExists(other));
        Assert.Equal(1, _context.RelationshipCountFor(other));
    }

    [Fact]
    public void Reports_not_found_for_an_unknown_tag()
    {
        var result = Handler().Handle(new DeleteTag(TagId.New()));

        Assert.False(result.IsSuccess);
        Assert.Equal(CommandFailureReason.NotFound, result.Failure!.Reason);
    }

    [Fact]
    public void Deleting_twice_reports_not_found_the_second_time()
    {
        // There is no soft-deleted state to be in, so the second call simply
        // finds nothing. No InvalidState exists for this command (§11 row 21).
        var id = _context.SeedTag("Work");

        Assert.True(Handler().Handle(new DeleteTag(id)).IsSuccess);

        var second = Handler().Handle(new DeleteTag(id));

        Assert.Equal(CommandFailureReason.NotFound, second.Failure!.Reason);
    }

    [Fact]
    public void Deletes_a_tag_with_no_relationships()
    {
        var id = _context.SeedTag("Unused");

        Assert.True(Handler().Handle(new DeleteTag(id)).IsSuccess);
        Assert.False(_context.TagExists(id));
    }

    [Fact]
    public void The_cascade_is_atomic()
    {
        // Both halves land together: no state exists where the tag is gone but
        // its join rows survive, which would leave rows pointing at nothing.
        var tag = _context.SeedTag("Work");
        var notes = Enumerable.Range(0, 5).Select(_ => _context.SeedNote()).ToList();

        foreach (NoteId note in notes)
        {
            _context.SeedRelationship(note, tag);
        }

        Handler().Handle(new DeleteTag(tag));

        Assert.False(_context.TagExists(tag));
        Assert.Equal(0, _context.RelationshipCount());
        Assert.All(notes, note => Assert.True(_context.NoteExists(note)));
    }

    [Fact]
    public void Persists_across_a_reopen()
    {
        var tag = _context.SeedTag("Work");
        var note = _context.SeedNote();
        _context.SeedRelationship(note, tag);

        Handler().Handle(new DeleteTag(tag));

        var reopened = new Noto.Infrastructure.Storage.NotoDatabase(_context.DatabasePath);
        var repository = new Noto.Infrastructure.Storage.SqliteTagRepository(reopened);

        Assert.Null(repository.Find(tag));
        Assert.Equal(0, _context.RelationshipCount());
        Assert.True(_context.NoteExists(note));
    }
}
