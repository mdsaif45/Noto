using Noto.Core.Commands;
using Noto.Core.Folders;
using Noto.UseCases.Folders;
using Xunit;

namespace Noto.Infrastructure.Tests.Folders;

/// <summary>
/// <c>PinFolder</c> and <c>UnpinFolder</c> against real SQLite
/// (contract §11, commands 16 and 17).
/// </summary>
public sealed class PinFolderTests : IDisposable
{
    private readonly FolderTestContext _context = new();

    public void Dispose() => _context.Dispose();

    private PinFolderHandler Pin() => new(_context.Folders, _context.Clock);

    private UnpinFolderHandler Unpin() => new(_context.Folders, _context.Clock);

    [Fact]
    public void Pins_an_active_folder()
    {
        var id = _context.SeedFolder();

        var result = Pin().Handle(new PinFolder(id));

        Assert.True(result.IsSuccess);
        Assert.True(_context.IsPinnedOf(id));
    }

    [Fact]
    public void Unpins_a_pinned_folder()
    {
        var id = _context.SeedFolder(pinned: true);

        var result = Unpin().Handle(new UnpinFolder(id));

        Assert.True(result.IsSuccess);
        Assert.False(_context.IsPinnedOf(id));
    }

    [Fact]
    public void Pinning_reports_not_found_for_an_unknown_folder()
    {
        var result = Pin().Handle(new PinFolder(FolderId.New()));

        Assert.Equal(CommandFailureReason.NotFound, result.Failure!.Reason);
    }

    [Fact]
    public void Unpinning_reports_not_found_for_an_unknown_folder()
    {
        var result = Unpin().Handle(new UnpinFolder(FolderId.New()));

        Assert.Equal(CommandFailureReason.NotFound, result.Failure!.Reason);
    }

    [Fact]
    public void Pinning_a_deleted_folder_is_invalid_state()
    {
        var id = _context.SeedFolder(deletedAt: _context.Clock.UtcNow);

        var result = Pin().Handle(new PinFolder(id));

        Assert.Equal(CommandFailureReason.InvalidState, result.Failure!.Reason);
        Assert.False(_context.IsPinnedOf(id));
    }

    [Fact]
    public void Unpinning_a_deleted_folder_is_invalid_state()
    {
        var id = _context.SeedFolder(pinned: true, deletedAt: _context.Clock.UtcNow);

        var result = Unpin().Handle(new UnpinFolder(id));

        Assert.Equal(CommandFailureReason.InvalidState, result.Failure!.Reason);
        Assert.True(_context.IsPinnedOf(id));
    }

    [Fact]
    public void Pinning_does_not_change_sort_order()
    {
        // The sensitivity test for the classic mistake: implementing "pin" as
        // "move to the top". Pinning is a display partition (O2) laid over the
        // sequence, and a pin that moved the folder would destroy the position
        // unpinning is supposed to return it to.
        var id = _context.SeedFolder(sortOrder: 42);

        Pin().Handle(new PinFolder(id));

        Assert.Equal(42, _context.SortOrderOf(id));
    }

    [Fact]
    public void Unpinning_does_not_change_sort_order()
    {
        var id = _context.SeedFolder(sortOrder: 42, pinned: true);

        Unpin().Handle(new UnpinFolder(id));

        Assert.Equal(42, _context.SortOrderOf(id));
    }

    [Fact]
    public void Pinning_does_not_reorder_the_collection()
    {
        // The stronger form: not only does the pinned folder keep its own
        // value, the whole sequence is unchanged. A "pin moves to top"
        // implementation that renumbered siblings would pass the single-row
        // check above while still being wrong.
        var first = _context.SeedFolder("A", sortOrder: 1);
        var middle = _context.SeedFolder("B", sortOrder: 2);
        var last = _context.SeedFolder("C", sortOrder: 3);

        Pin().Handle(new PinFolder(last));

        Assert.Equal(new[] { first, middle, last }, _context.ActiveFolderOrder());
        Assert.Equal(1, _context.SortOrderOf(first));
        Assert.Equal(2, _context.SortOrderOf(middle));
        Assert.Equal(3, _context.SortOrderOf(last));
    }

    [Fact]
    public void Pinning_stamps_updated_at_and_preserves_created_at()
    {
        var created = _context.Clock.UtcNow;
        var id = _context.SeedFolder();

        DateTimeOffset pinnedAt = _context.Clock.Advance(TimeSpan.FromMinutes(1));

        Pin().Handle(new PinFolder(id));

        Assert.Equal(pinnedAt.ToString("O"), _context.UpdatedAtOf(id));
        Assert.Equal(created.ToString("O"), _context.CreatedAtOf(id));
    }

    [Fact]
    public void Pinning_leaves_the_name_untouched()
    {
        var id = _context.SeedFolder("Keep me");

        Pin().Handle(new PinFolder(id));

        Assert.Equal("Keep me", _context.NameOf(id));
    }

    [Fact]
    public void Pinning_an_already_pinned_folder_writes_nothing_and_stamps_nothing()
    {
        // Contract §4: "each entity row it changes" — a command that changes no
        // row stamps nothing. The contract states this as a general
        // consequence, not a per-command exception, which is why it also covers
        // same-position reorder (§5) and both tag no-ops (§7).
        //
        // The UpdatedAt assertion is the point of this test. An earlier version
        // asserted only success and IsPinned, and passed while the handler was
        // restamping UpdatedAt on every redundant pin.
        var id = _context.SeedFolder(pinned: true);
        string before = _context.UpdatedAtOf(id);

        _context.Clock.Advance(TimeSpan.FromHours(1));

        var result = Pin().Handle(new PinFolder(id));

        Assert.True(result.IsSuccess);
        Assert.True(_context.IsPinnedOf(id));
        Assert.Equal(before, _context.UpdatedAtOf(id));
    }

    [Fact]
    public void Unpinning_an_already_unpinned_folder_writes_nothing_and_stamps_nothing()
    {
        // The symmetric case. Both directions are no-ops, so neither can drift
        // into stamping on its own.
        var id = _context.SeedFolder(pinned: false);
        string before = _context.UpdatedAtOf(id);

        _context.Clock.Advance(TimeSpan.FromHours(1));

        var result = Unpin().Handle(new UnpinFolder(id));

        Assert.True(result.IsSuccess);
        Assert.False(_context.IsPinnedOf(id));
        Assert.Equal(before, _context.UpdatedAtOf(id));
    }

    [Fact]
    public void A_repeated_pin_never_stamps_however_often_it_is_called()
    {
        // The stronger form: the clock moves between every call, so an
        // implementation that wrote even once would leave a different
        // UpdatedAt behind.
        var id = _context.SeedFolder(pinned: false);

        DateTimeOffset firstPin = _context.Clock.Advance(TimeSpan.FromMinutes(1));
        Pin().Handle(new PinFolder(id));

        string afterRealPin = _context.UpdatedAtOf(id);
        Assert.Equal(firstPin.ToString("O"), afterRealPin);

        for (int i = 0; i < 5; i++)
        {
            _context.Clock.Advance(TimeSpan.FromMinutes(1));
            Assert.True(Pin().Handle(new PinFolder(id)).IsSuccess);
        }

        Assert.Equal(afterRealPin, _context.UpdatedAtOf(id));
    }

    [Fact]
    public void The_no_op_survives_a_reopen()
    {
        // The absence of a write is a persistence fact, not a cache artefact.
        var id = _context.SeedFolder(pinned: true);
        string before = _context.UpdatedAtOf(id);

        _context.Clock.Advance(TimeSpan.FromHours(1));
        Pin().Handle(new PinFolder(id));

        var reopened = new Noto.Infrastructure.Storage.NotoDatabase(_context.DatabasePath);
        var repository = new Noto.Infrastructure.Storage.SqliteFolderRepository(reopened);

        Folder folder = repository.FindActive(id)!;

        Assert.True(folder.IsPinned);
        Assert.Equal(before, folder.UpdatedAt.ToString("O"));
    }
}
