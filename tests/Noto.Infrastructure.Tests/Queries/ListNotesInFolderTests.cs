using Noto.Core.Folders;
using Noto.Core.Notes;
using Noto.UseCases.Notes;
using Xunit;

namespace Noto.Infrastructure.Tests.Queries;

/// <summary>
/// Q2 — <c>ListNotesInFolder</c> against real SQLite
/// (contract §11 Q2, §5a, O1/O2, I1/I6).
/// </summary>
public sealed class ListNotesInFolderTests : IDisposable
{
    private readonly QueryTestContext _context = new();

    public void Dispose() => _context.Dispose();

    private ListNotesInFolderQuery Query() => new(_context.Notes);

    [Fact]
    public void Returns_an_empty_list_when_the_folder_holds_nothing()
    {
        var folder = _context.SeedFolder("Empty");

        Assert.Empty(Query().Execute(folder));
    }

    [Fact]
    public void Returns_an_empty_list_for_a_folder_that_does_not_exist()
    {
        // An absent folder is not a failure — §11 Q2 has an empty failure
        // column, so the answer is "no notes", not NotFound.
        Assert.Empty(Query().Execute(FolderId.New()));
    }

    [Fact]
    public void Returns_an_empty_list_when_root_holds_nothing()
    {
        _context.SeedNote(_context.SeedFolder("Elsewhere"));

        Assert.Empty(Query().Execute(null));
    }

    // ---- the root scope (O1) ---------------------------------------------

    [Fact]
    public void Returns_root_notes_when_the_folder_is_null()
    {
        // THE test for `FolderId IS NULL` versus `FolderId = NULL`. The second
        // matches nothing in SQL's three-valued logic, so a wrong
        // implementation returns an empty list here while every other test in
        // this class still passes.
        var first = _context.SeedNote(null, sortOrder: 1);
        var second = _context.SeedNote(null, sortOrder: 2);

        var result = Query().Execute(null);

        Assert.Equal(new[] { first, second }, result.Select(n => n.Id));
    }

    [Fact]
    public void Root_is_its_own_scope_and_not_a_catch_all()
    {
        // O1: a folder's notes never appear at root, and root notes never
        // appear in a folder.
        var folder = _context.SeedFolder("Work");
        var inFolder = _context.SeedNote(folder, sortOrder: 1);
        var atRoot = _context.SeedNote(null, sortOrder: 1);

        Assert.Equal(new[] { atRoot }, Query().Execute(null).Select(n => n.Id));
        Assert.Equal(new[] { inFolder }, Query().Execute(folder).Select(n => n.Id));
    }

    // ---- scoping ----------------------------------------------------------

    [Fact]
    public void Returns_only_the_notes_of_the_named_folder()
    {
        var work = _context.SeedFolder("Work");
        var home = _context.SeedFolder("Home");

        var inWork = _context.SeedNote(work, sortOrder: 1);
        _context.SeedNote(home, sortOrder: 1);
        _context.SeedNote(null, sortOrder: 1);

        Assert.Equal(new[] { inWork }, Query().Execute(work).Select(n => n.Id));
    }

    [Fact]
    public void Folders_do_not_leak_into_each_other()
    {
        var work = _context.SeedFolder("Work");
        var home = _context.SeedFolder("Home");

        var w1 = _context.SeedNote(work, sortOrder: 1);
        var w2 = _context.SeedNote(work, sortOrder: 2);
        var h1 = _context.SeedNote(home, sortOrder: 1);

        Assert.Equal(new[] { w1, w2 }, Query().Execute(work).Select(n => n.Id));
        Assert.Equal(new[] { h1 }, Query().Execute(home).Select(n => n.Id));
    }

    // ---- I1 / I6 ----------------------------------------------------------

    [Fact]
    public void Excludes_deleted_notes()
    {
        var folder = _context.SeedFolder("Work");
        var active = _context.SeedNote(folder, sortOrder: 1);
        var deleted = _context.SeedNote(folder, sortOrder: 2, deletedAt: _context.Clock.UtcNow);

        var result = Query().Execute(folder);

        Assert.Equal(new[] { active }, result.Select(n => n.Id));
        Assert.DoesNotContain(deleted, result.Select(n => n.Id));
    }

    [Fact]
    public void Excludes_deleted_notes_at_root_too()
    {
        var active = _context.SeedNote(null, sortOrder: 1);
        _context.SeedNote(null, sortOrder: 2, deletedAt: _context.Clock.UtcNow);

        Assert.Equal(new[] { active }, Query().Execute(null).Select(n => n.Id));
    }

    [Fact]
    public void A_deleted_note_does_not_participate_in_ordering()
    {
        // I6: the deleted row keeps its SortOrder but is absent, so the
        // surviving notes are adjacent in the result.
        var folder = _context.SeedFolder("Work");
        var first = _context.SeedNote(folder, sortOrder: 1);
        _context.SeedNote(folder, sortOrder: 2, deletedAt: _context.Clock.UtcNow);
        var third = _context.SeedNote(folder, sortOrder: 3);

        Assert.Equal(new[] { first, third }, Query().Execute(folder).Select(n => n.Id));
    }

    // ---- O2 ordering ------------------------------------------------------

    [Fact]
    public void Orders_by_sort_order_ascending()
    {
        // Seeded in DESCENDING order on purpose: an implementation with no
        // ORDER BY would return insertion order and fail here, while a test
        // seeded in the expected order would pass against it.
        var folder = _context.SeedFolder("Work");
        var third = _context.SeedNote(folder, sortOrder: 30);
        var first = _context.SeedNote(folder, sortOrder: 10);
        var second = _context.SeedNote(folder, sortOrder: 20);

        Assert.Equal(new[] { first, second, third }, Query().Execute(folder).Select(n => n.Id));
    }

    [Fact]
    public void Pinned_notes_come_first()
    {
        // O2's IsPinned DESC. The pinned note has the HIGHEST SortOrder, so an
        // implementation that dropped the pinned clause would put it last.
        var folder = _context.SeedFolder("Work");
        var unpinnedLow = _context.SeedNote(folder, sortOrder: 1);
        var unpinnedHigh = _context.SeedNote(folder, sortOrder: 2);
        var pinned = _context.SeedNote(folder, sortOrder: 99, pinned: true);

        Assert.Equal(
            new[] { pinned, unpinnedLow, unpinnedHigh },
            Query().Execute(folder).Select(n => n.Id));
    }

    [Fact]
    public void Pinned_notes_are_ordered_among_themselves_by_sort_order()
    {
        var folder = _context.SeedFolder("Work");
        var pinnedSecond = _context.SeedNote(folder, sortOrder: 20, pinned: true);
        var pinnedFirst = _context.SeedNote(folder, sortOrder: 10, pinned: true);
        var plain = _context.SeedNote(folder, sortOrder: 5);

        Assert.Equal(
            new[] { pinnedFirst, pinnedSecond, plain },
            Query().Execute(folder).Select(n => n.Id));
    }

    [Fact]
    public void Ties_on_sort_order_break_by_id()
    {
        // O2's final clause, and the ids are seeded in DESCENDING order on
        // purpose.
        //
        // Seeding with fresh ULIDs cannot test this: ULIDs are creation-ordered
        // (ADR-012), so insertion order always equals id order and the result
        // looks identical whether or not the query orders by Id. Measured — with
        // `Id ASC` SQLite returns AAA, MMM, ZZZ; without it, insertion order.
        // Only a tie seeded out of id order tells the two apart.
        var folder = _context.SeedFolder("Work");

        var high = NoteId.From("7ZZZZZZZZZZZZZZZZZZZZZZZZZ");
        var low = NoteId.From("7AAAAAAAAAAAAAAAAAAAAAAAAA");

        _context.SeedNoteWithId(high, folder, sortOrder: 5);
        _context.SeedNoteWithId(low, folder, sortOrder: 5);

        Assert.Equal(new[] { low, high }, Query().Execute(folder).Select(n => n.Id));
    }

    [Fact]
    public void The_id_tie_break_applies_within_the_pinned_partition_too()
    {
        var folder = _context.SeedFolder("Work");

        var pinnedHigh = NoteId.From("7ZZZZZZZZZZZZZZZZZZZZZZZZZ");
        var pinnedLow = NoteId.From("7AAAAAAAAAAAAAAAAAAAAAAAAA");
        var plain = NoteId.From("7BBBBBBBBBBBBBBBBBBBBBBBBB");

        _context.SeedNoteWithId(pinnedHigh, folder, sortOrder: 5, pinned: true);
        _context.SeedNoteWithId(pinnedLow, folder, sortOrder: 5, pinned: true);
        _context.SeedNoteWithId(plain, folder, sortOrder: 1);

        Assert.Equal(
            new[] { pinnedLow, pinnedHigh, plain },
            Query().Execute(folder).Select(n => n.Id));
    }

    [Fact]
    public void Pinning_does_not_change_the_underlying_sort_order()
    {
        // The display partition is applied by the query, not by rewriting
        // SortOrder — so unpinning restores the original position.
        var folder = _context.SeedFolder("Work");
        var a = _context.SeedNote(folder, sortOrder: 1);
        var b = _context.SeedNote(folder, sortOrder: 2, pinned: true);

        Assert.Equal(new[] { b, a }, Query().Execute(folder).Select(n => n.Id));
        Assert.Equal(2, _context.SortOrderOf(b));
    }

    // ---- persistence ------------------------------------------------------

    [Fact]
    public void Persists_across_a_reopen()
    {
        var folder = _context.SeedFolder("Work");
        var pinned = _context.SeedNote(folder, sortOrder: 9, pinned: true);
        var plain = _context.SeedNote(folder, sortOrder: 1);

        var reopened = new Noto.Infrastructure.Storage.NotoDatabase(_context.DatabasePath);
        var repository = new Noto.Infrastructure.Storage.SqliteNoteRepository(reopened);

        Assert.Equal(
            new[] { pinned, plain },
            repository.ListInFolder(folder).Select(n => n.Id));
    }

    [Fact]
    public void Returns_fully_mapped_notes()
    {
        var folder = _context.SeedFolder("Work");
        var id = _context.SeedNote(folder, sortOrder: 3, content: "# Title\n\nbody");

        Note note = Assert.Single(Query().Execute(folder));

        Assert.Equal(id, note.Id);
        Assert.Equal(folder, note.FolderId);
        Assert.Equal("# Title\n\nbody", note.Content);
        Assert.Equal(3, note.SortOrder);
        Assert.Null(note.DeletedAt);
    }
}
