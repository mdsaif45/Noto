using Noto.Core.Commands;
using Noto.Core.Folders;
using Noto.UseCases.Folders;
using Xunit;

namespace Noto.Infrastructure.Tests.Folders;

/// <summary>
/// <c>ReorderFolder</c> against real SQLite (contract §11, command 18).
/// </summary>
/// <remarks>
/// The folder collection is a single ordering scope, so these exercise the same
/// O1–O6 rules the note tests do — through the shared engine, not a second
/// implementation.
/// </remarks>
public sealed class ReorderFolderTests : IDisposable
{
    private readonly FolderTestContext _context = new();

    public void Dispose() => _context.Dispose();

    private ReorderFolderHandler Handler() => new(_context.Folders, _context.Clock);

    [Fact]
    public void Moves_a_folder_to_the_front()
    {
        var a = _context.SeedFolder("A", sortOrder: 1);
        var b = _context.SeedFolder("B", sortOrder: 2);
        var c = _context.SeedFolder("C", sortOrder: 3);

        var result = Handler().Handle(new ReorderFolder(c, null));

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { c, a, b }, _context.ActiveFolderOrder());
    }

    [Fact]
    public void Moves_a_folder_after_an_active_sibling()
    {
        var a = _context.SeedFolder("A", sortOrder: 1);
        var b = _context.SeedFolder("B", sortOrder: 2);
        var c = _context.SeedFolder("C", sortOrder: 3);

        Handler().Handle(new ReorderFolder(a, b));

        Assert.Equal(new[] { b, a, c }, _context.ActiveFolderOrder());
    }

    [Fact]
    public void Moves_a_folder_to_the_end_by_naming_the_last_sibling()
    {
        // "Last" is expressed as "after the final folder" — the contract's
        // signature is ReorderFolder(folderId, afterFolderId?), so there is no
        // separate end token at the command layer.
        var a = _context.SeedFolder("A", sortOrder: 1);
        var b = _context.SeedFolder("B", sortOrder: 2);
        var c = _context.SeedFolder("C", sortOrder: 3);

        Handler().Handle(new ReorderFolder(a, c));

        Assert.Equal(new[] { b, c, a }, _context.ActiveFolderOrder());
    }

    [Fact]
    public void Placing_first_in_a_single_folder_collection_is_a_no_op()
    {
        var only = _context.SeedFolder("Only", sortOrder: 1);
        string before = _context.UpdatedAtOf(only);

        _context.Clock.Advance(TimeSpan.FromHours(1));

        var result = Handler().Handle(new ReorderFolder(only, null));

        Assert.True(result.IsSuccess);
        Assert.Equal(before, _context.UpdatedAtOf(only));
    }

    [Fact]
    public void A_same_position_reorder_writes_nothing_and_stamps_nothing()
    {
        // Contract §5: the no-op writes nothing. The sensitivity test for an
        // implementation that always writes — it would pass every ordering
        // assertion while restamping UpdatedAt on a folder that never moved.
        var a = _context.SeedFolder("A", sortOrder: 1);
        var b = _context.SeedFolder("B", sortOrder: 2);

        string beforeA = _context.UpdatedAtOf(a);
        string beforeB = _context.UpdatedAtOf(b);
        double orderB = _context.SortOrderOf(b);

        _context.Clock.Advance(TimeSpan.FromHours(1));

        // B is already immediately after A.
        var result = Handler().Handle(new ReorderFolder(b, a));

        Assert.True(result.IsSuccess);
        Assert.Equal(beforeA, _context.UpdatedAtOf(a));
        Assert.Equal(beforeB, _context.UpdatedAtOf(b));
        Assert.Equal(orderB, _context.SortOrderOf(b));
    }

    [Fact]
    public void Reordering_a_folder_after_itself_is_a_no_op()
    {
        var a = _context.SeedFolder("A", sortOrder: 1);
        var b = _context.SeedFolder("B", sortOrder: 2);
        string before = _context.UpdatedAtOf(b);

        _context.Clock.Advance(TimeSpan.FromHours(1));

        var result = Handler().Handle(new ReorderFolder(b, b));

        Assert.True(result.IsSuccess);
        Assert.Equal(before, _context.UpdatedAtOf(b));
        Assert.Equal(new[] { a, b }, _context.ActiveFolderOrder());
    }

    [Fact]
    public void Takes_the_midpoint_between_two_neighbours()
    {
        var a = _context.SeedFolder("A", sortOrder: 1);
        var b = _context.SeedFolder("B", sortOrder: 2);
        var c = _context.SeedFolder("C", sortOrder: 3);

        Handler().Handle(new ReorderFolder(c, a));

        Assert.Equal(1.5, _context.SortOrderOf(c));
        Assert.Equal(new[] { a, c, b }, _context.ActiveFolderOrder());

        // One row written, not the whole scope (design §7).
        Assert.Equal(1, _context.SortOrderOf(a));
        Assert.Equal(2, _context.SortOrderOf(b));
    }

    [Fact]
    public void Stamps_updated_at_when_the_folder_actually_moves()
    {
        var a = _context.SeedFolder("A", sortOrder: 1);
        var b = _context.SeedFolder("B", sortOrder: 2);

        DateTimeOffset moved = _context.Clock.Advance(TimeSpan.FromHours(1));

        Handler().Handle(new ReorderFolder(a, b));

        Assert.Equal(moved.ToString("O"), _context.UpdatedAtOf(a));
    }

    [Fact]
    public void Renormalises_when_the_gap_is_exhausted()
    {
        // O6. Repeatedly inserting into the same spot halves the gap until a
        // midpoint is no longer meaningful; the scope is then rewritten to
        // 1, 2, 3... inside the same transaction and ordering is preserved.
        var first = _context.SeedFolder("first", sortOrder: 1);
        var last = _context.SeedFolder("last", sortOrder: 2);

        var movers = new List<FolderId>();

        for (int i = 0; i < 60; i++)
        {
            var mover = _context.SeedFolder($"m{i}", sortOrder: 1000 + i);
            movers.Add(mover);

            var result = Handler().Handle(new ReorderFolder(mover, first));
            Assert.True(result.IsSuccess);
        }

        IReadOnlyList<FolderId> order = _context.ActiveFolderOrder();

        // Everything is still present and distinct...
        Assert.Equal(62, order.Count);
        Assert.Equal(62, order.Distinct().Count());

        // ...the anchors are still at the ends...
        Assert.Equal(first, order[0]);
        Assert.Equal(last, order[^1]);

        // ...and the most recently moved folder sits immediately after the
        // anchor, which is what the operation asked for every time.
        Assert.Equal(movers[^1], order[1]);

        // No two folders share a SortOrder, which is what renormalisation
        // exists to guarantee.
        var values = order.Select(_context.SortOrderOf).ToList();
        Assert.Equal(values.Count, values.Distinct().Count());
        Assert.Equal(values.OrderBy(v => v).ToList(), values);
    }

    [Fact]
    public void Reports_not_found_for_an_unknown_folder()
    {
        var result = Handler().Handle(new ReorderFolder(FolderId.New(), null));

        Assert.Equal(CommandFailureReason.NotFound, result.Failure!.Reason);
    }

    [Fact]
    public void Reordering_a_deleted_folder_is_invalid_state()
    {
        // I5/I6: a deleted folder is inert and never participates in ordering.
        var deleted = _context.SeedFolder("gone", sortOrder: 5, deletedAt: _context.Clock.UtcNow);
        var active = _context.SeedFolder("here", sortOrder: 1);

        var result = Handler().Handle(new ReorderFolder(deleted, active));

        Assert.False(result.IsSuccess);
        Assert.Equal(CommandFailureReason.InvalidState, result.Failure!.Reason);
        Assert.Equal(5, _context.SortOrderOf(deleted));
    }

    [Fact]
    public void Reordering_after_a_deleted_sibling_is_invalid_input()
    {
        // The anchor must be ACTIVE. Anchoring to a row the user cannot see
        // would compute a position from a deleted folder's SortOrder.
        var mover = _context.SeedFolder("mover", sortOrder: 1);
        var deleted = _context.SeedFolder("gone", sortOrder: 9, deletedAt: _context.Clock.UtcNow);

        string before = _context.UpdatedAtOf(mover);
        _context.Clock.Advance(TimeSpan.FromHours(1));

        var result = Handler().Handle(new ReorderFolder(mover, deleted));

        Assert.False(result.IsSuccess);
        Assert.Equal(CommandFailureReason.InvalidInput, result.Failure!.Reason);
        Assert.Equal(before, _context.UpdatedAtOf(mover));
        Assert.Equal(1, _context.SortOrderOf(mover));
    }

    [Fact]
    public void Reordering_after_an_unknown_folder_is_invalid_input()
    {
        var mover = _context.SeedFolder("mover", sortOrder: 1);

        var result = Handler().Handle(new ReorderFolder(mover, FolderId.New()));

        Assert.Equal(CommandFailureReason.InvalidInput, result.Failure!.Reason);
    }

    [Fact]
    public void Ignores_deleted_folders_when_computing_neighbours()
    {
        // I6: a deleted folder sitting numerically between two active ones
        // must not act as a neighbour.
        var a = _context.SeedFolder("A", sortOrder: 1);
        _context.SeedFolder("hidden", sortOrder: 2, deletedAt: _context.Clock.UtcNow);
        var c = _context.SeedFolder("C", sortOrder: 3);

        Handler().Handle(new ReorderFolder(c, a));

        Assert.Equal(new[] { a, c }, _context.ActiveFolderOrder());
    }

    [Fact]
    public void Does_not_change_pinned_state()
    {
        var a = _context.SeedFolder("A", sortOrder: 1, pinned: true);
        var b = _context.SeedFolder("B", sortOrder: 2);

        Handler().Handle(new ReorderFolder(a, b));

        Assert.True(_context.IsPinnedOf(a));
        Assert.False(_context.IsPinnedOf(b));
    }

    [Fact]
    public void Pinning_does_not_affect_the_ordering_sequence()
    {
        // O2 partitions the DISPLAY by IsPinned; the underlying sequence is one
        // scope. Reordering a pinned folder works exactly as for an unpinned
        // one — the sensitivity test for "pinned folders are a separate scope".
        var a = _context.SeedFolder("A", sortOrder: 1, pinned: true);
        var b = _context.SeedFolder("B", sortOrder: 2);
        var c = _context.SeedFolder("C", sortOrder: 3, pinned: true);

        Handler().Handle(new ReorderFolder(c, a));

        Assert.Equal(new[] { a, c, b }, _context.ActiveFolderOrder());
    }

    [Fact]
    public void Persists_across_a_reopen()
    {
        var a = _context.SeedFolder("A", sortOrder: 1);
        var b = _context.SeedFolder("B", sortOrder: 2);

        Handler().Handle(new ReorderFolder(b, null));

        double reordered = _context.SortOrderOf(b);

        var reopened = new Noto.Infrastructure.Storage.NotoDatabase(_context.DatabasePath);
        using var connection = reopened.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT SortOrder FROM Folders WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", b.Value);

        Assert.Equal(
            reordered,
            Convert.ToDouble(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture));
        Assert.True(reordered < _context.SortOrderOf(a));
    }
}
