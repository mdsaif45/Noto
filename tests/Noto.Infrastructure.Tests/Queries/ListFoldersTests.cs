using Noto.Core.Folders;
using Noto.UseCases.Folders;
using Xunit;

namespace Noto.Infrastructure.Tests.Queries;

/// <summary>
/// Q3 — <c>ListFolders</c> against real SQLite (contract §11 Q3, §5a, O2, I1).
/// </summary>
public sealed class ListFoldersTests : IDisposable
{
    private readonly QueryTestContext _context = new();

    public void Dispose() => _context.Dispose();

    private ListFoldersQuery Query() => new(_context.Folders);

    [Fact]
    public void Returns_an_empty_list_when_no_folders_exist()
    {
        Assert.Empty(Query().Execute());
    }

    [Fact]
    public void Returns_an_empty_list_when_every_folder_is_deleted()
    {
        _context.SeedFolder("gone", deletedAt: _context.Clock.UtcNow);
        _context.SeedFolder("also gone", deletedAt: _context.Clock.UtcNow);

        Assert.Empty(Query().Execute());
    }

    [Fact]
    public void Returns_the_active_folders()
    {
        var a = _context.SeedFolder("A", sortOrder: 1);
        var b = _context.SeedFolder("B", sortOrder: 2);

        Assert.Equal(new[] { a, b }, Query().Execute().Select(f => f.Id));
    }

    // ---- I1 ---------------------------------------------------------------

    [Fact]
    public void Excludes_deleted_folders()
    {
        var active = _context.SeedFolder("active", sortOrder: 1);
        var deleted = _context.SeedFolder("binned", sortOrder: 2, deletedAt: _context.Clock.UtcNow);

        var result = Query().Execute();

        Assert.Equal(new[] { active }, result.Select(f => f.Id));
        Assert.DoesNotContain(deleted, result.Select(f => f.Id));

        // The bin still has it — I2's explicit method is the only way there.
        Assert.Contains(deleted, _context.Folders.ListDeleted().Select(f => f.Id));
    }

    // ---- O2 ordering ------------------------------------------------------

    [Fact]
    public void Orders_by_sort_order_ascending()
    {
        // Seeded in descending order, so a missing ORDER BY fails here.
        var third = _context.SeedFolder("C", sortOrder: 30);
        var first = _context.SeedFolder("A", sortOrder: 10);
        var second = _context.SeedFolder("B", sortOrder: 20);

        Assert.Equal(new[] { first, second, third }, Query().Execute().Select(f => f.Id));
    }

    [Fact]
    public void Pinned_folders_come_first()
    {
        // The pinned folder has the highest SortOrder, so dropping the pinned
        // clause puts it last.
        var plainLow = _context.SeedFolder("A", sortOrder: 1);
        var plainHigh = _context.SeedFolder("B", sortOrder: 2);
        var pinned = _context.SeedFolder("Z", sortOrder: 99, pinned: true);

        Assert.Equal(
            new[] { pinned, plainLow, plainHigh },
            Query().Execute().Select(f => f.Id));
    }

    [Fact]
    public void Pinned_folders_are_ordered_among_themselves_by_sort_order()
    {
        var pinnedSecond = _context.SeedFolder("P2", sortOrder: 20, pinned: true);
        var pinnedFirst = _context.SeedFolder("P1", sortOrder: 10, pinned: true);
        var plain = _context.SeedFolder("plain", sortOrder: 5);

        Assert.Equal(
            new[] { pinnedFirst, pinnedSecond, plain },
            Query().Execute().Select(f => f.Id));
    }

    [Fact]
    public void Ties_on_sort_order_break_by_id()
    {
        // Ids seeded in DESCENDING order. Fresh ULIDs are creation-ordered, so
        // seeding them in sequence makes insertion order equal id order and the
        // test passes with or without the `Id ASC` clause.
        var high = FolderId.From("7ZZZZZZZZZZZZZZZZZZZZZZZZZ");
        var low = FolderId.From("7AAAAAAAAAAAAAAAAAAAAAAAAA");

        _context.SeedFolderWithId(high, sortOrder: 7);
        _context.SeedFolderWithId(low, sortOrder: 7);

        Assert.Equal(new[] { low, high }, Query().Execute().Select(f => f.Id));
    }

    [Fact]
    public void A_deleted_folder_does_not_participate_in_ordering()
    {
        var first = _context.SeedFolder("A", sortOrder: 1);
        _context.SeedFolder("hidden", sortOrder: 2, deletedAt: _context.Clock.UtcNow);
        var third = _context.SeedFolder("C", sortOrder: 3);

        Assert.Equal(new[] { first, third }, Query().Execute().Select(f => f.Id));
    }

    // ---- persistence and mapping -----------------------------------------

    [Fact]
    public void Persists_across_a_reopen()
    {
        var pinned = _context.SeedFolder("pinned", sortOrder: 9, pinned: true);
        var plain = _context.SeedFolder("plain", sortOrder: 1);

        var reopened = new Noto.Infrastructure.Storage.NotoDatabase(_context.DatabasePath);
        var repository = new Noto.Infrastructure.Storage.SqliteFolderRepository(reopened);

        Assert.Equal(new[] { pinned, plain }, repository.ListActive().Select(f => f.Id));
    }

    [Fact]
    public void Returns_fully_mapped_folders()
    {
        var id = _context.SeedFolder("Work", sortOrder: 4, pinned: true);

        Folder folder = Assert.Single(Query().Execute());

        Assert.Equal(id, folder.Id);
        Assert.Equal("Work", folder.Name);
        Assert.Equal(4, folder.SortOrder);
        Assert.True(folder.IsPinned);
        Assert.Null(folder.DeletedAt);
    }
}
