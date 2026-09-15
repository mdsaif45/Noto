using Noto.Core.Commands;
using Noto.Core.Notes;
using Noto.Core.Tags;
using Noto.UseCases.Tags;
using Xunit;

namespace Noto.Infrastructure.Tests.Tags;

/// <summary>
/// <c>AssignTagToNote</c> and <c>RemoveTagFromNote</c> against real SQLite
/// (contract §11 rows 22–23, §7 incl. U11a/U11b and U12b).
/// </summary>
public sealed class TagRelationshipTests : IDisposable
{
    private readonly TagTestContext _context = new();

    public void Dispose() => _context.Dispose();

    private AssignTagToNoteHandler Assign() => new(_context.Notes, _context.Tags);

    private RemoveTagFromNoteHandler Remove() => new(_context.Notes, _context.Tags);

    // ==================================================================
    // AssignTagToNote
    // ==================================================================

    [Fact]
    public void Assign_creates_the_relationship()
    {
        var note = _context.SeedNote();
        var tag = _context.SeedTag("Work");

        var result = Assign().Handle(new AssignTagToNote(note, tag));

        Assert.True(result.IsSuccess);
        Assert.True(_context.Tags.HasRelationship(note, tag));
        Assert.Equal(1, _context.RelationshipCount());
    }

    [Fact]
    public void Assign_does_not_stamp_the_note()
    {
        // Contract §4: "NoteTags has no timestamps, and the note row is not
        // changed". This holds on the SUCCESSFUL path, not only the no-op —
        // tagging a note must not make it look recently modified (C10).
        var note = _context.SeedNote();
        var tag = _context.SeedTag("Work");

        string before = _context.UpdatedAtOf(note);
        _context.Clock.Advance(TimeSpan.FromHours(2));

        Assign().Handle(new AssignTagToNote(note, tag));

        Assert.Equal(before, _context.UpdatedAtOf(note));
    }

    [Fact]
    public void Assign_is_idempotent_and_writes_nothing_the_second_time()
    {
        // U11a: already assigned -> success, no duplicate row, no write.
        var note = _context.SeedNote();
        var tag = _context.SeedTag("Work");

        Assert.True(Assign().Handle(new AssignTagToNote(note, tag)).IsSuccess);
        string afterFirst = _context.UpdatedAtOf(note);

        _context.Clock.Advance(TimeSpan.FromHours(2));

        var second = Assign().Handle(new AssignTagToNote(note, tag));

        Assert.True(second.IsSuccess);
        Assert.Equal(1, _context.RelationshipCount());
        Assert.Equal(afterFirst, _context.UpdatedAtOf(note));
    }

    [Fact]
    public void Assign_repeated_many_times_leaves_exactly_one_row()
    {
        var note = _context.SeedNote();
        var tag = _context.SeedTag("Work");

        for (int i = 0; i < 5; i++)
        {
            _context.Clock.Advance(TimeSpan.FromMinutes(1));
            Assert.True(Assign().Handle(new AssignTagToNote(note, tag)).IsSuccess);
        }

        Assert.Equal(1, _context.RelationshipCount());
    }

    [Fact]
    public void Assign_reports_not_found_for_a_missing_note()
    {
        var tag = _context.SeedTag("Work");

        var result = Assign().Handle(new AssignTagToNote(NoteId.New(), tag));

        Assert.Equal(CommandFailureReason.NotFound, result.Failure!.Reason);
        Assert.Equal(0, _context.RelationshipCount());
    }

    [Fact]
    public void Assign_reports_invalid_state_for_a_deleted_note()
    {
        // I5 names tagging explicitly: a deleted note cannot be tagged.
        var note = _context.SeedNote(deletedAt: _context.Clock.UtcNow);
        var tag = _context.SeedTag("Work");

        var result = Assign().Handle(new AssignTagToNote(note, tag));

        Assert.Equal(CommandFailureReason.InvalidState, result.Failure!.Reason);
        Assert.Equal(0, _context.RelationshipCount());
    }

    [Fact]
    public void Assign_reports_not_found_for_a_missing_tag()
    {
        // Step 3 of the §7 ladder. Assign must reach an end state that
        // REQUIRES the tag, so a missing one is a genuine failure — unlike
        // Remove (U12b).
        var note = _context.SeedNote();

        var result = Assign().Handle(new AssignTagToNote(note, TagId.New()));

        Assert.Equal(CommandFailureReason.NotFound, result.Failure!.Reason);
        Assert.Equal(0, _context.RelationshipCount());
    }

    [Fact]
    public void Assign_checks_the_note_lifecycle_before_idempotency()
    {
        // §7: "A deleted note plus an already-assigned tag is InvalidState, not
        // success." The relationship exists, so an implementation that checked
        // it first would return success and silently break I5.
        var note = _context.SeedNote();
        var tag = _context.SeedTag("Work");
        _context.SeedRelationship(note, tag);

        _context.Notes.SoftDelete(note, _context.Clock.UtcNow);

        var result = Assign().Handle(new AssignTagToNote(note, tag));

        Assert.False(result.IsSuccess);
        Assert.Equal(CommandFailureReason.InvalidState, result.Failure!.Reason);
    }

    [Fact]
    public void Assign_checks_the_note_before_the_tag()
    {
        // Both are NotFound, so this pins the ORDER rather than the reason:
        // the message must name the note, not the tag.
        var result = Assign().Handle(new AssignTagToNote(NoteId.New(), TagId.New()));

        Assert.Equal(CommandFailureReason.NotFound, result.Failure!.Reason);
        Assert.Contains("note", result.Failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Assign_supports_many_tags_on_one_note_and_many_notes_on_one_tag()
    {
        var noteA = _context.SeedNote();
        var noteB = _context.SeedNote();
        var work = _context.SeedTag("Work");
        var home = _context.SeedTag("Home");

        Assert.True(Assign().Handle(new AssignTagToNote(noteA, work)).IsSuccess);
        Assert.True(Assign().Handle(new AssignTagToNote(noteA, home)).IsSuccess);
        Assert.True(Assign().Handle(new AssignTagToNote(noteB, work)).IsSuccess);

        Assert.Equal(3, _context.RelationshipCount());
        Assert.Equal(2, _context.RelationshipCountFor(work));
    }

    // ==================================================================
    // RemoveTagFromNote
    // ==================================================================

    [Fact]
    public void Remove_deletes_the_relationship()
    {
        var note = _context.SeedNote();
        var tag = _context.SeedTag("Work");
        _context.SeedRelationship(note, tag);

        var result = Remove().Handle(new RemoveTagFromNote(note, tag));

        Assert.True(result.IsSuccess);
        Assert.False(_context.Tags.HasRelationship(note, tag));
        Assert.Equal(0, _context.RelationshipCount());
    }

    [Fact]
    public void Remove_does_not_stamp_the_note()
    {
        var note = _context.SeedNote();
        var tag = _context.SeedTag("Work");
        _context.SeedRelationship(note, tag);

        string before = _context.UpdatedAtOf(note);
        _context.Clock.Advance(TimeSpan.FromHours(2));

        Remove().Handle(new RemoveTagFromNote(note, tag));

        Assert.Equal(before, _context.UpdatedAtOf(note));
    }

    [Fact]
    public void Remove_leaves_the_tag_and_the_note_in_place()
    {
        // Only the join row goes.
        var note = _context.SeedNote();
        var tag = _context.SeedTag("Work");
        _context.SeedRelationship(note, tag);

        Remove().Handle(new RemoveTagFromNote(note, tag));

        Assert.True(_context.TagExists(tag));
        Assert.True(_context.NoteExists(note));
    }

    [Fact]
    public void Remove_is_idempotent_when_the_relationship_is_absent()
    {
        // U11b: absent -> success, no write, no stamp.
        var note = _context.SeedNote();
        var tag = _context.SeedTag("Work");

        string before = _context.UpdatedAtOf(note);
        _context.Clock.Advance(TimeSpan.FromHours(2));

        var result = Remove().Handle(new RemoveTagFromNote(note, tag));

        Assert.True(result.IsSuccess);
        Assert.Equal(before, _context.UpdatedAtOf(note));
        Assert.Equal(0, _context.RelationshipCount());
    }

    [Fact]
    public void Remove_twice_succeeds_both_times()
    {
        var note = _context.SeedNote();
        var tag = _context.SeedTag("Work");
        _context.SeedRelationship(note, tag);

        Assert.True(Remove().Handle(new RemoveTagFromNote(note, tag)).IsSuccess);
        Assert.True(Remove().Handle(new RemoveTagFromNote(note, tag)).IsSuccess);
    }

    [Fact]
    public void Remove_succeeds_for_a_tag_that_does_not_exist()
    {
        // U12b — THE distinguishing test. A tag that does not exist cannot be
        // related to the note, so the requested end state already holds.
        // NotFound here would report failure for a condition the command was
        // asked to bring about.
        var note = _context.SeedNote();

        var result = Remove().Handle(new RemoveTagFromNote(note, TagId.New()));

        Assert.True(result.IsSuccess);
        Assert.Null(result.Failure);
    }

    [Fact]
    public void Remove_with_a_missing_tag_writes_nothing()
    {
        var note = _context.SeedNote();
        var other = _context.SeedTag("Other");
        var otherNote = _context.SeedNote();
        _context.SeedRelationship(otherNote, other);

        string before = _context.UpdatedAtOf(note);
        _context.Clock.Advance(TimeSpan.FromHours(1));

        Remove().Handle(new RemoveTagFromNote(note, TagId.New()));

        Assert.Equal(before, _context.UpdatedAtOf(note));
        Assert.Equal(1, _context.RelationshipCount());
    }

    [Fact]
    public void Remove_reports_not_found_for_a_missing_note()
    {
        var tag = _context.SeedTag("Work");

        var result = Remove().Handle(new RemoveTagFromNote(NoteId.New(), tag));

        Assert.Equal(CommandFailureReason.NotFound, result.Failure!.Reason);
    }

    [Fact]
    public void Remove_reports_invalid_state_for_a_deleted_note()
    {
        // I5 applies to untagging too: a deleted note is inert.
        var note = _context.SeedNote(deletedAt: _context.Clock.UtcNow);
        var tag = _context.SeedTag("Work");

        var result = Remove().Handle(new RemoveTagFromNote(note, tag));

        Assert.Equal(CommandFailureReason.InvalidState, result.Failure!.Reason);
    }

    [Fact]
    public void Remove_checks_the_note_lifecycle_before_idempotency()
    {
        // A deleted note with an existing relationship must be InvalidState,
        // not a successful removal.
        var note = _context.SeedNote();
        var tag = _context.SeedTag("Work");
        _context.SeedRelationship(note, tag);

        _context.Notes.SoftDelete(note, _context.Clock.UtcNow);

        var result = Remove().Handle(new RemoveTagFromNote(note, tag));

        Assert.Equal(CommandFailureReason.InvalidState, result.Failure!.Reason);
        Assert.Equal(1, _context.RelationshipCount());
    }

    [Fact]
    public void Remove_deletes_only_the_named_pair()
    {
        var noteA = _context.SeedNote();
        var noteB = _context.SeedNote();
        var work = _context.SeedTag("Work");
        var home = _context.SeedTag("Home");

        _context.SeedRelationship(noteA, work);
        _context.SeedRelationship(noteA, home);
        _context.SeedRelationship(noteB, work);

        Remove().Handle(new RemoveTagFromNote(noteA, work));

        Assert.False(_context.Tags.HasRelationship(noteA, work));
        Assert.True(_context.Tags.HasRelationship(noteA, home));
        Assert.True(_context.Tags.HasRelationship(noteB, work));
        Assert.Equal(2, _context.RelationshipCount());
    }

    [Fact]
    public void A_round_trip_leaves_no_trace_on_the_note()
    {
        var note = _context.SeedNote();
        var tag = _context.SeedTag("Work");
        string before = _context.UpdatedAtOf(note);

        _context.Clock.Advance(TimeSpan.FromHours(1));
        Assign().Handle(new AssignTagToNote(note, tag));
        _context.Clock.Advance(TimeSpan.FromHours(1));
        Remove().Handle(new RemoveTagFromNote(note, tag));

        Assert.Equal(before, _context.UpdatedAtOf(note));
        Assert.Equal(0, _context.RelationshipCount());
    }

    [Fact]
    public void Relationships_persist_across_a_reopen()
    {
        var note = _context.SeedNote();
        var tag = _context.SeedTag("Work");

        Assign().Handle(new AssignTagToNote(note, tag));

        var reopened = new Noto.Infrastructure.Storage.NotoDatabase(_context.DatabasePath);
        var repository = new Noto.Infrastructure.Storage.SqliteTagRepository(reopened);

        Assert.True(repository.HasRelationship(note, tag));
    }
}
