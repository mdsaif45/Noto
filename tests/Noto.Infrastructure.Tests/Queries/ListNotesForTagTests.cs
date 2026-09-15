using Noto.Core.Notes;
using Noto.Core.Tags;
using Noto.UseCases.Tags;
using Xunit;

namespace Noto.Infrastructure.Tests.Queries;

/// <summary>
/// Q5 — <c>ListNotesForTag</c> against real SQLite
/// (contract §11 Q5, §5a U13b, I1).
/// </summary>
public sealed class ListNotesForTagTests : IDisposable
{
    private readonly QueryTestContext _context = new();

    public void Dispose() => _context.Dispose();

    private ListNotesForTagQuery Query() => new(_context.Notes);

    private static DateTimeOffset At(int hour) =>
        new(2026, 9, 15, hour, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Returns_an_empty_list_when_the_tag_has_no_notes()
    {
        var tag = _context.SeedTag("Unused");
        _context.SeedNote(null);

        Assert.Empty(Query().Execute(tag));
    }

    [Fact]
    public void Returns_an_empty_list_for_a_tag_that_does_not_exist()
    {
        // No failure channel — §11 Q5 has an empty failure column, and a tag
        // that does not exist simply has no relationships. Same reasoning as
        // U12b, where RemoveTagFromNote does not validate the tag either.
        _context.SeedNote(null);

        Assert.Empty(Query().Execute(TagId.New()));
    }

    [Fact]
    public void Returns_one_note_for_a_tag()
    {
        var tag = _context.SeedTag("Work");
        var note = _context.SeedNote(null);
        _context.SeedRelationship(note, tag);

        Assert.Equal(new[] { note }, Query().Execute(tag).Select(n => n.Id));
    }

    [Fact]
    public void Returns_every_note_carrying_the_tag()
    {
        var tag = _context.SeedTag("Work");
        var first = _context.SeedNote(null, updatedAt: At(9));
        var second = _context.SeedNote(null, updatedAt: At(8));
        _context.SeedRelationship(first, tag);
        _context.SeedRelationship(second, tag);

        Assert.Equal(2, Query().Execute(tag).Count);
    }

    // ---- the relationship, not mere existence ----------------------------

    [Fact]
    public void Does_not_return_notes_that_merely_exist()
    {
        // The join must decide membership. An implementation that dropped it
        // would return this untagged note.
        var tag = _context.SeedTag("Work");
        var tagged = _context.SeedNote(null);
        _context.SeedNote(null, content: "untagged");
        _context.SeedRelationship(tagged, tag);

        Assert.Equal(new[] { tagged }, Query().Execute(tag).Select(n => n.Id));
    }

    [Fact]
    public void Does_not_return_notes_belonging_to_another_tag()
    {
        var work = _context.SeedTag("Work");
        var home = _context.SeedTag("Home");
        var inWork = _context.SeedNote(null);
        var inHome = _context.SeedNote(null);
        _context.SeedRelationship(inWork, work);
        _context.SeedRelationship(inHome, home);

        Assert.Equal(new[] { inWork }, Query().Execute(work).Select(n => n.Id));
        Assert.Equal(new[] { inHome }, Query().Execute(home).Select(n => n.Id));
    }

    [Fact]
    public void A_note_with_several_tags_appears_under_each()
    {
        var work = _context.SeedTag("Work");
        var urgent = _context.SeedTag("Urgent");
        var note = _context.SeedNote(null);
        _context.SeedRelationship(note, work);
        _context.SeedRelationship(note, urgent);

        Assert.Equal(new[] { note }, Query().Execute(work).Select(n => n.Id));
        Assert.Equal(new[] { note }, Query().Execute(urgent).Select(n => n.Id));
    }

    [Fact]
    public void Returns_the_note_once_even_with_several_tags()
    {
        // Guards against a join that multiplies rows.
        var work = _context.SeedTag("Work");
        var urgent = _context.SeedTag("Urgent");
        var note = _context.SeedNote(null);
        _context.SeedRelationship(note, work);
        _context.SeedRelationship(note, urgent);

        Assert.Single(Query().Execute(work));
    }

    // ---- I1 ---------------------------------------------------------------

    [Fact]
    public void Excludes_deleted_notes()
    {
        var tag = _context.SeedTag("Work");
        var active = _context.SeedNote(null, updatedAt: At(9));
        var deleted = _context.SeedNote(null, updatedAt: At(10), deletedAt: _context.Clock.UtcNow);
        _context.SeedRelationship(active, tag);
        _context.SeedRelationship(deleted, tag);

        var result = Query().Execute(tag);

        Assert.Equal(new[] { active }, result.Select(n => n.Id));
        Assert.DoesNotContain(deleted, result.Select(n => n.Id));
    }

    // ---- U13b ordering ----------------------------------------------------

    [Fact]
    public void Orders_by_updated_at_descending()
    {
        // Seeded oldest-first so insertion order is the REVERSE of the
        // expected result: an implementation with no ORDER BY fails here.
        var tag = _context.SeedTag("Work");
        var oldest = _context.SeedNote(null, updatedAt: At(8));
        var middle = _context.SeedNote(null, updatedAt: At(12));
        var newest = _context.SeedNote(null, updatedAt: At(16));

        _context.SeedRelationship(oldest, tag);
        _context.SeedRelationship(middle, tag);
        _context.SeedRelationship(newest, tag);

        Assert.Equal(
            new[] { newest, middle, oldest },
            Query().Execute(tag).Select(n => n.Id));
    }

    [Fact]
    public void Ordering_ignores_sort_order()
    {
        // U13b explicitly rejects SortOrder: these notes span folders and
        // SortOrder is folder-scoped (O1). Here SortOrder runs OPPOSITE to
        // UpdatedAt, so an implementation ordering by SortOrder inverts the
        // result.
        var tag = _context.SeedTag("Work");
        var work = _context.SeedFolder("Work");
        var home = _context.SeedFolder("Home");

        var newest = _context.SeedNote(work, sortOrder: 99, updatedAt: At(16));
        var oldest = _context.SeedNote(home, sortOrder: 1, updatedAt: At(8));

        _context.SeedRelationship(newest, tag);
        _context.SeedRelationship(oldest, tag);

        Assert.Equal(new[] { newest, oldest }, Query().Execute(tag).Select(n => n.Id));
    }

    [Fact]
    public void Ordering_ignores_pinning()
    {
        // O2's pinned partition belongs to the folder views, not to this one.
        // §5a gives Q5 one rule: UpdatedAt DESC.
        var tag = _context.SeedTag("Work");
        var pinnedButOld = _context.SeedNote(null, pinned: true, updatedAt: At(8));
        var plainButNew = _context.SeedNote(null, updatedAt: At(16));

        _context.SeedRelationship(pinnedButOld, tag);
        _context.SeedRelationship(plainButNew, tag);

        Assert.Equal(
            new[] { plainButNew, pinnedButOld },
            Query().Execute(tag).Select(n => n.Id));
    }

    [Fact]
    public void Returns_notes_from_several_folders_and_from_root()
    {
        var tag = _context.SeedTag("Work");
        var folder = _context.SeedFolder("Work");

        var inFolder = _context.SeedNote(folder, updatedAt: At(16));
        var atRoot = _context.SeedNote(null, updatedAt: At(8));

        _context.SeedRelationship(inFolder, tag);
        _context.SeedRelationship(atRoot, tag);

        Assert.Equal(new[] { inFolder, atRoot }, Query().Execute(tag).Select(n => n.Id));
    }

    [Fact]
    public void Notes_sharing_an_updated_at_are_all_returned()
    {
        // §5a leaves the relative order of tied timestamps UNSPECIFIED, so
        // this asserts membership only. Asserting an order here would be
        // testing an invention rather than the contract — and SQLite does not
        // guarantee one.
        var tag = _context.SeedTag("Work");
        var first = _context.SeedNote(null, updatedAt: At(10));
        var second = _context.SeedNote(null, updatedAt: At(10));
        var later = _context.SeedNote(null, updatedAt: At(14));

        _context.SeedRelationship(first, tag);
        _context.SeedRelationship(second, tag);
        _context.SeedRelationship(later, tag);

        var result = Query().Execute(tag).Select(n => n.Id).ToList();

        Assert.Equal(3, result.Count);
        Assert.Equal(later, result[0]);
        Assert.Contains(first, result);
        Assert.Contains(second, result);
    }

    // ---- persistence ------------------------------------------------------

    [Fact]
    public void Persists_across_a_reopen()
    {
        var tag = _context.SeedTag("Work");
        var newest = _context.SeedNote(null, updatedAt: At(16));
        var oldest = _context.SeedNote(null, updatedAt: At(8));
        _context.SeedRelationship(newest, tag);
        _context.SeedRelationship(oldest, tag);

        var reopened = new Noto.Infrastructure.Storage.NotoDatabase(_context.DatabasePath);
        var repository = new Noto.Infrastructure.Storage.SqliteNoteRepository(reopened);

        Assert.Equal(
            new[] { newest, oldest },
            repository.ListForTag(tag).Select(n => n.Id));
    }

    [Fact]
    public void Returns_fully_mapped_notes()
    {
        var tag = _context.SeedTag("Work");
        var folder = _context.SeedFolder("Work");
        var id = _context.SeedNote(folder, sortOrder: 3, content: "# Title", updatedAt: At(11));
        _context.SeedRelationship(id, tag);

        Note note = Assert.Single(Query().Execute(tag));

        Assert.Equal(id, note.Id);
        Assert.Equal(folder, note.FolderId);
        Assert.Equal("# Title", note.Content);
        Assert.Equal(At(11), note.UpdatedAt);
        Assert.Null(note.DeletedAt);
    }
}
