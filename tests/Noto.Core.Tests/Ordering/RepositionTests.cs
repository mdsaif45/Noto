using Noto.Core.Notes;
using Noto.Core.Ordering;
using Xunit;

namespace Noto.Core.Tests.Ordering;

/// <summary>Up, down, top and bottom within the pin group, as the engine's After-or-first (workspace-management.md, "B3 precisely").</summary>
public sealed class RepositionTests
{
    private sealed record Item(NoteId Id, bool Pinned, double Sort);

    // Unpinned only: A1 B2 C3 D4.
    private static readonly Item A = new(NoteId.New(), false, 1), B = new(NoteId.New(), false, 2), C = new(NoteId.New(), false, 3), D = new(NoteId.New(), false, 4);
    private static readonly Item[] Plain = [A, B, C, D];

    // Interleaved: pinned P1 (3) and P2 (5) over unpinned U1 (1), U2 (2), U3 (4), U4 (6). Displayed pinned first.
    private static readonly Item P1 = new(NoteId.New(), true, 3), P2 = new(NoteId.New(), true, 5);
    private static readonly Item U1 = new(NoteId.New(), false, 1), U2 = new(NoteId.New(), false, 2), U3 = new(NoteId.New(), false, 4), U4 = new(NoteId.New(), false, 6);
    private static readonly Item[] Mixed = [P1, P2, U1, U2, U3, U4];

    [Fact]
    public void Up_lands_just_after_the_previous_items_predecessor()
    {
        Assert.Equal(new RepositionTarget<NoteId>(true, A.Id), Of(Plain, C, RepositionDirection.Up));
    }

    [Fact]
    public void Up_onto_the_first_item_lands_first()
    {
        Assert.Equal(new RepositionTarget<NoteId>(true, null), Of(Plain, B, RepositionDirection.Up));
    }

    [Fact]
    public void Down_lands_after_the_next_item()
    {
        Assert.Equal(new RepositionTarget<NoteId>(true, C.Id), Of(Plain, B, RepositionDirection.Down));
    }

    [Fact]
    public void Top_lands_first_and_bottom_after_the_last()
    {
        Assert.Equal(new RepositionTarget<NoteId>(true, null), Of(Plain, C, RepositionDirection.Top));
        Assert.Equal(new RepositionTarget<NoteId>(true, D.Id), Of(Plain, B, RepositionDirection.Bottom));
    }

    [Theory]
    [InlineData(0, RepositionDirection.Up)]
    [InlineData(0, RepositionDirection.Top)]
    [InlineData(3, RepositionDirection.Down)]
    [InlineData(3, RepositionDirection.Bottom)]
    public void At_an_edge_nothing_moves(int index, RepositionDirection direction)
    {
        Assert.False(Of(Plain, Plain[index], direction).Moves);
    }

    [Fact]
    public void An_item_not_in_the_scope_does_not_move()
    {
        Assert.False(Of(Plain, new Item(NoteId.New(), false, 9), RepositionDirection.Up).Moves);
    }

    // ---- the pin boundary and the shared SortOrder ------------------------------------------------------

    [Theory]
    [InlineData(2, RepositionDirection.Up)]    // first unpinned: never above a pinned one
    [InlineData(2, RepositionDirection.Top)]
    [InlineData(1, RepositionDirection.Down)]  // last pinned: never below an unpinned one
    [InlineData(1, RepositionDirection.Bottom)]
    [InlineData(0, RepositionDirection.Up)]
    public void The_pin_boundary_is_never_crossed(int index, RepositionDirection direction)
    {
        Assert.False(Of(Mixed, Mixed[index], direction).Moves);
    }

    [Fact]
    public void Up_within_the_unpinned_group_lands_after_a_pinned_predecessor_when_that_is_what_precedes_in_SortOrder()
    {
        // U4 (6) up: before U3 (4), whose SortOrder predecessor is P1 (3), a pinned note. U4 lands between 3 and 4.
        Assert.Equal(new RepositionTarget<NoteId>(true, P1.Id), Of(Mixed, U4, RepositionDirection.Up));
    }

    [Fact]
    public void Up_to_the_first_unpinned_place_lands_first_when_nothing_precedes_it()
    {
        // U2 (2) up: before U1 (1), which has no predecessor at all.
        Assert.Equal(new RepositionTarget<NoteId>(true, null), Of(Mixed, U2, RepositionDirection.Up));
    }

    [Fact]
    public void Within_the_pinned_group_moves_follow_the_same_rule()
    {
        Assert.Equal(new RepositionTarget<NoteId>(true, P2.Id), Of(Mixed, P1, RepositionDirection.Down));

        // P2 (5) top: before P1 (3), whose SortOrder predecessor is U2 (2). P2 lands between 2 and 3.
        Assert.Equal(new RepositionTarget<NoteId>(true, U2.Id), Of(Mixed, P2, RepositionDirection.Top));
    }

    [Fact]
    public void Bottom_of_the_unpinned_group_is_after_its_last_item()
    {
        Assert.Equal(new RepositionTarget<NoteId>(true, U4.Id), Of(Mixed, U1, RepositionDirection.Bottom));
    }

    [Fact]
    public void Equal_SortOrders_are_ordered_by_id_as_O2_orders_them()
    {
        NoteId low = NoteId.From("01J00000000000000000000001"), high = NoteId.From("01J00000000000000000000002");
        Item first = new(low, false, 5), second = new(high, false, 5), third = new(NoteId.From("01J00000000000000000000003"), false, 7);

        // third up: before second (5, high id), whose predecessor is first (5, low id).
        Assert.Equal(new RepositionTarget<NoteId>(true, low), Of([first, second, third], third, RepositionDirection.Up));

        // second up: before first, which has no predecessor.
        Assert.Equal(new RepositionTarget<NoteId>(true, null), Of([first, second, third], second, RepositionDirection.Up));
    }

    private static RepositionTarget<NoteId> Of(IReadOnlyList<Item> displayed, Item item, RepositionDirection direction) =>
        Reposition.Of(displayed, item.Id, direction, x => x.Id, x => x.Pinned, x => x.Sort);
}
