using Noto.Core.Folders;
using Noto.Core.Notes;
using Xunit;

namespace Noto.Core.Tests.Notes;

/// <summary>Where a new note goes (B5, J8; create-and-rename §2.3): every setting, every edge.</summary>
public sealed class NewNotePlacementRuleTests
{
    private static readonly FolderId Folder = FolderId.New();

    // Display order (O2): pinned first.
    private static readonly Note P1 = N(pinned: true);
    private static readonly Note P2 = N(pinned: true);
    private static readonly Note U1 = N(pinned: false);
    private static readonly Note U2 = N(pinned: false);
    private static readonly Note U3 = N(pinned: false);
    private static readonly IReadOnlyList<Note> Siblings = [P1, P2, U1, U2, U3];

    [Fact]
    public void Bottom_is_the_default_and_the_end()
    {
        Assert.Equal(NewNotePlacement.Bottom, default(NewNotePlacement));
        Assert.Equal(NotePlacement.Last, Resolve(NewNotePlacement.Bottom, U2.Id));
    }

    [Fact]
    public void Top_is_first_whatever_is_selected()
    {
        Assert.Equal(NotePlacement.First, Resolve(NewNotePlacement.Top, null));
        Assert.Equal(NotePlacement.First, Resolve(NewNotePlacement.Top, U3.Id));
    }

    [Fact]
    public void Below_a_note_is_after_it() =>
        Assert.Equal(NotePlacement.After(U2.Id), Resolve(NewNotePlacement.BelowSelected, U2.Id));

    [Fact]
    public void Above_a_note_is_after_the_unpinned_note_before_it() =>
        Assert.Equal(NotePlacement.After(U1.Id), Resolve(NewNotePlacement.AboveSelected, U2.Id));

    [Fact]
    public void Above_the_first_unpinned_note_is_first_not_after_a_pinned_one() =>
        Assert.Equal(NotePlacement.First, Resolve(NewNotePlacement.AboveSelected, U1.Id));

    [Theory]
    [InlineData(NewNotePlacement.AboveSelected)]
    [InlineData(NewNotePlacement.BelowSelected)]
    public void A_pinned_selection_places_the_note_at_the_top_of_the_unpinned_notes(NewNotePlacement setting)
    {
        // A new note is never pinned, so it cannot sit among the pinned ones.
        Assert.Equal(NotePlacement.First, Resolve(setting, P2.Id));
        Assert.Equal(NotePlacement.First, Resolve(setting, P1.Id));
    }

    [Theory]
    [InlineData(NewNotePlacement.AboveSelected)]
    [InlineData(NewNotePlacement.BelowSelected)]
    public void No_selection_or_a_selection_not_in_the_folder_falls_back_to_the_end(NewNotePlacement setting)
    {
        Assert.Equal(NotePlacement.Last, Resolve(setting, null));
        Assert.Equal(NotePlacement.Last, Resolve(setting, NoteId.New()));
    }

    [Fact]
    public void An_empty_folder_takes_any_placement_safely()
    {
        Assert.Equal(NotePlacement.First, NewNotePlacementRule.Resolve(NewNotePlacement.Top, null, []));
        Assert.Equal(NotePlacement.Last, NewNotePlacementRule.Resolve(NewNotePlacement.AboveSelected, NoteId.New(), []));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(99)]
    public void An_unknown_setting_fails_safe_to_the_end(int value) =>
        Assert.Equal(NotePlacement.Last, Resolve((NewNotePlacement)value, U2.Id));

    [Fact]
    public void The_folder_is_required() =>
        Assert.Throws<ArgumentNullException>(() => NewNotePlacementRule.Resolve(NewNotePlacement.Top, null, null!));

    private static NotePlacement Resolve(NewNotePlacement setting, NoteId? selected) =>
        NewNotePlacementRule.Resolve(setting, selected, Siblings);

    private static Note N(bool pinned) => new()
    {
        Id = NoteId.New(),
        Content = "n",
        FolderId = Folder,
        IsPinned = pinned,
        CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch,
    };
}
