using Noto.Core.Commands;
using Noto.Core.Notes;
using Noto.Core.Tags;
using Noto.UseCases.Tags;
using Xunit;

namespace Noto.Infrastructure.Tests.Tags;

/// <summary>
/// Proves the relationship no-ops perform <b>no persistence write</b>, rather
/// than merely leaving the database looking unchanged.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a spy is necessary here, when the rest of Slice 5 uses real SQLite.</b>
/// Contract §7 requires "no persistence write" on both no-ops. For the folder
/// and note commands that rule is observable through state, because a redundant
/// write would restamp <c>UpdatedAt</c> — which is exactly how Slice 4's pin
/// defect was caught.
/// </para>
/// <para>
/// Tags have no such tell. <c>NoteTags</c> carries no timestamps and the note
/// row is deliberately untouched (§4), so a redundant
/// <c>INSERT OR IGNORE</c> on an existing relationship changes <b>nothing
/// observable</b>: same row count, same note, same everything. Measured — a
/// handler with no existence check plus an <c>INSERT OR IGNORE</c> repository
/// passed all 110 state-based tests.
/// </para>
/// <para>
/// The write itself is therefore the only observable difference, so these tests
/// count repository calls. This is the narrow case where asserting on an
/// interaction is more honest than asserting on state.
/// </para>
/// </remarks>
public sealed class TagWriteObservationTests : IDisposable
{
    private readonly TagTestContext _context = new();

    public void Dispose() => _context.Dispose();

    [Fact]
    public void Assign_does_not_call_the_repository_when_the_relationship_exists()
    {
        var note = _context.SeedNote();
        var tag = _context.SeedTag("Work");
        _context.SeedRelationship(note, tag);

        var spy = new CountingTagRepository(_context.Tags);
        var handler = new AssignTagToNoteHandler(_context.Notes, spy);

        var result = handler.Handle(new AssignTagToNote(note, tag));

        Assert.True(result.IsSuccess);
        Assert.Equal(0, spy.Assigns);
        Assert.Equal(0, spy.Removes);
    }

    [Fact]
    public void Assign_calls_the_repository_exactly_once_when_the_relationship_is_new()
    {
        // The counterpart, so the no-op cannot be "fixed" by never writing.
        var note = _context.SeedNote();
        var tag = _context.SeedTag("Work");

        var spy = new CountingTagRepository(_context.Tags);
        var handler = new AssignTagToNoteHandler(_context.Notes, spy);

        Assert.True(handler.Handle(new AssignTagToNote(note, tag)).IsSuccess);

        Assert.Equal(1, spy.Assigns);
        Assert.True(_context.Tags.HasRelationship(note, tag));
    }

    [Fact]
    public void Repeated_assign_writes_exactly_once_across_many_calls()
    {
        var note = _context.SeedNote();
        var tag = _context.SeedTag("Work");

        var spy = new CountingTagRepository(_context.Tags);
        var handler = new AssignTagToNoteHandler(_context.Notes, spy);

        for (int i = 0; i < 6; i++)
        {
            Assert.True(handler.Handle(new AssignTagToNote(note, tag)).IsSuccess);
        }

        Assert.Equal(1, spy.Assigns);
        Assert.Equal(1, _context.RelationshipCount());
    }

    [Fact]
    public void Remove_does_not_call_the_repository_when_the_relationship_is_absent()
    {
        var note = _context.SeedNote();
        var tag = _context.SeedTag("Work");

        var spy = new CountingTagRepository(_context.Tags);
        var handler = new RemoveTagFromNoteHandler(_context.Notes, spy);

        var result = handler.Handle(new RemoveTagFromNote(note, tag));

        Assert.True(result.IsSuccess);
        Assert.Equal(0, spy.Removes);
    }

    [Fact]
    public void Remove_does_not_call_the_repository_for_a_tag_that_does_not_exist()
    {
        // U12b's no-op path: success, and demonstrably no write.
        var note = _context.SeedNote();

        var spy = new CountingTagRepository(_context.Tags);
        var handler = new RemoveTagFromNoteHandler(_context.Notes, spy);

        Assert.True(handler.Handle(new RemoveTagFromNote(note, TagId.New())).IsSuccess);

        Assert.Equal(0, spy.Removes);
    }

    [Fact]
    public void Remove_calls_the_repository_exactly_once_when_the_relationship_exists()
    {
        var note = _context.SeedNote();
        var tag = _context.SeedTag("Work");
        _context.SeedRelationship(note, tag);

        var spy = new CountingTagRepository(_context.Tags);
        var handler = new RemoveTagFromNoteHandler(_context.Notes, spy);

        Assert.True(handler.Handle(new RemoveTagFromNote(note, tag)).IsSuccess);

        Assert.Equal(1, spy.Removes);
        Assert.False(_context.Tags.HasRelationship(note, tag));
    }

    [Fact]
    public void A_deleted_note_never_reaches_a_write()
    {
        // I5 before idempotency, proved at the write boundary: the relationship
        // exists, so a handler that checked it first would both succeed and
        // write.
        var note = _context.SeedNote();
        var tag = _context.SeedTag("Work");
        _context.SeedRelationship(note, tag);
        _context.Notes.SoftDelete(note, _context.Clock.UtcNow);

        var spy = new CountingTagRepository(_context.Tags);

        Assert.Equal(
            CommandFailureReason.InvalidState,
            new AssignTagToNoteHandler(_context.Notes, spy)
                .Handle(new AssignTagToNote(note, tag)).Failure!.Reason);

        Assert.Equal(
            CommandFailureReason.InvalidState,
            new RemoveTagFromNoteHandler(_context.Notes, spy)
                .Handle(new RemoveTagFromNote(note, tag)).Failure!.Reason);

        Assert.Equal(0, spy.Assigns);
        Assert.Equal(0, spy.Removes);
    }

    /// <summary>
    /// Forwards every call to the real repository, counting the two writes.
    /// </summary>
    /// <remarks>
    /// A decorator rather than a stub: the reads still hit real SQLite, so the
    /// handler's decisions are made against genuine data and only the write
    /// count is added.
    /// </remarks>
    private sealed class CountingTagRepository(ITagRepository inner) : ITagRepository
    {
        public int Assigns { get; private set; }

        public int Removes { get; private set; }

        public TagId? FindIdByName(string name) => inner.FindIdByName(name);

        public Tag? Find(TagId id) => inner.Find(id);

        public void Add(Tag tag) => inner.Add(tag);

        public void Rename(TagId id, string name) => inner.Rename(id, name);

        public void Delete(TagId id) => inner.Delete(id);

        public bool HasRelationship(NoteId noteId, TagId tagId) =>
            inner.HasRelationship(noteId, tagId);

        public IReadOnlyList<Tag> ListAll() => inner.ListAll();

        public void Assign(NoteId noteId, TagId tagId)
        {
            Assigns++;
            inner.Assign(noteId, tagId);
        }

        public void Remove(NoteId noteId, TagId tagId)
        {
            Removes++;
            inner.Remove(noteId, tagId);
        }
    }
}
