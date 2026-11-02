using Noto.Core.Notes;
using Noto.Core.Ordering;
using Xunit;

namespace Noto.Core.Tests.Ordering;

/// <summary>Where focus goes when an item leaves a list: the next, else the previous, else nowhere.</summary>
public sealed class RemovalTests
{
    private static readonly NoteId A = NoteId.New(), B = NoteId.New(), C = NoteId.New();

    [Fact]
    public void The_next_item_when_there_is_one()
    {
        Assert.Equal(B, Removal.Next([A, B, C], A, x => x));
        Assert.Equal(C, Removal.Next([A, B, C], B, x => x));
    }

    [Fact]
    public void The_previous_item_when_it_was_the_last()
    {
        Assert.Equal(B, Removal.Next([A, B, C], C, x => x));
    }

    [Fact]
    public void Nowhere_when_it_was_the_only_one_or_is_not_in_the_list()
    {
        Assert.Null(Removal.Next([A], A, x => x));
        Assert.Null(Removal.Next([A, B], C, x => x));
        Assert.Null(Removal.Next(Array.Empty<NoteId>(), A, x => x));
    }
}
