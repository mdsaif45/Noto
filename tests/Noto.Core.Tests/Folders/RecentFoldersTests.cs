using Noto.Core.Folders;
using Xunit;

namespace Noto.Core.Tests.Folders;

/// <summary>The folders notes were most recently moved to (workspace-management.md §3).</summary>
public sealed class RecentFoldersTests
{
    private static readonly FolderId A = FolderId.New();
    private static readonly FolderId B = FolderId.New();
    private static readonly FolderId C = FolderId.New();

    [Fact]
    public void A_move_puts_its_destination_first()
    {
        Assert.Equal([C, A, B], RecentFolders.Push([A, B], C));
    }

    [Fact]
    public void A_destination_already_listed_moves_to_the_front_once()
    {
        Assert.Equal([B, A, C], RecentFolders.Push([A, B, C], B));
    }

    [Fact]
    public void Moving_to_the_most_recent_again_changes_nothing()
    {
        Assert.Equal([A, B], RecentFolders.Push([A, B], A));
    }

    [Fact]
    public void At_most_five_are_kept_the_oldest_dropped()
    {
        FolderId[] five = [FolderId.New(), FolderId.New(), FolderId.New(), FolderId.New(), FolderId.New()];

        IReadOnlyList<FolderId> pushed = RecentFolders.Push(five, A);

        Assert.Equal(RecentFolders.Capacity, pushed.Count);
        Assert.Equal([A, five[0], five[1], five[2], five[3]], pushed);
    }

    [Fact]
    public void The_stored_form_round_trips_in_order()
    {
        string stored = RecentFolders.Format([B, A, C]);

        Assert.Equal($"{B},{A},{C}", stored);
        Assert.Equal([B, A, C], RecentFolders.Parse(stored));
        Assert.True(RecentFolders.IsValid(stored));
    }

    [Fact]
    public void Empty_is_a_valid_empty_list()
    {
        Assert.True(RecentFolders.IsValid(string.Empty));
        Assert.Empty(RecentFolders.Parse(string.Empty));
        Assert.Equal(string.Empty, RecentFolders.Format([]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-an-id")]
    [InlineData(",")]
    [InlineData(" ")]
    public void Anything_malformed_reads_as_empty_and_is_invalid(string? stored)
    {
        Assert.False(RecentFolders.IsValid(stored));
        Assert.Empty(RecentFolders.Parse(stored));
    }

    [Fact]
    public void A_list_this_type_could_not_have_written_is_invalid_whole()
    {
        string sixIds = string.Join(',', Enumerable.Range(0, 6).Select(_ => FolderId.New().Value));
        string duplicate = $"{A},{A}";
        string oneBad = $"{A},nope";

        Assert.False(RecentFolders.IsValid(sixIds));
        Assert.False(RecentFolders.IsValid(duplicate));
        Assert.False(RecentFolders.IsValid(oneBad));
        Assert.Empty(RecentFolders.Parse(oneBad));
    }

    [Fact]
    public void Shown_keeps_order_skips_folders_no_longer_active_and_the_notes_own_folder()
    {
        Folder a = Folder(A, "A"), c = Folder(C, "C");

        IReadOnlyList<Folder> shown = RecentFolders.Shown([C, B, A], [a, c], excluding: null);
        Assert.Equal([c, a], shown);

        Assert.Equal([c], RecentFolders.Shown([C, B, A], [a, c], excluding: A));
    }

    private static Folder Folder(FolderId id, string name) => new()
    {
        Id = id,
        Name = name,
        CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch,
    };
}
