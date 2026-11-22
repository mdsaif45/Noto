using Noto.Core.Folders;
using Xunit;

namespace Noto.Core.Tests.Folders;

/// <summary>The folder chooser of a note from a global shortcut (#243; parity B6, J9).</summary>
public sealed class FolderChoiceTests
{
    private static readonly Folder Work = Folder("Work");
    private static readonly Folder Home = Folder("Home");
    private static readonly Folder Ideas = Folder("Ideas");
    private static readonly Folder Binned = Folder("Binned");

    [Fact]
    public void The_recently_opened_come_first_most_recent_first_then_the_rest_in_folder_list_order_each_once()
    {
        IReadOnlyList<Folder> ordered = FolderChoice.Order([Ideas.Id, Home.Id], [Work, Home, Ideas]);

        Assert.Equal([Ideas, Home, Work], ordered);
    }

    [Fact]
    public void A_recent_folder_no_longer_there_is_passed_over_and_with_no_history_it_is_the_folder_list()
    {
        Assert.Equal([Home, Work, Ideas], FolderChoice.Order([Binned.Id, Home.Id], [Work, Home, Ideas]));
        Assert.Equal([Work, Home, Ideas], FolderChoice.Order([], [Work, Home, Ideas]));
        Assert.Empty(FolderChoice.Order([Home.Id], []));
    }

    [Theory]
    [InlineData("o", new[] { "Work", "Home" })]
    [InlineData("WOR", new[] { "Work" })]
    [InlineData("  me ", new[] { "Home" })]
    [InlineData("dea", new[] { "Ideas" })]
    [InlineData("zzz", new string[0])]
    public void Typing_keeps_the_names_that_contain_it_ignoring_case_in_the_given_order(string query, string[] expected)
    {
        Assert.Equal(expected, FolderChoice.Filter([Work, Home, Ideas], query).Select(f => f.Name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_typed_keeps_every_folder(string? query)
    {
        Assert.Equal([Ideas, Work, Home], FolderChoice.Filter([Ideas, Work, Home], query));
    }

    private static Folder Folder(string name) => new()
    {
        Id = FolderId.New(),
        Name = name,
        CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch,
    };
}
