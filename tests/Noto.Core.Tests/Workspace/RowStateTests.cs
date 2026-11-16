using Noto.Core.Workspace;
using Xunit;

namespace Noto.Core.Tests.Workspace;

/// <summary>#201: a row's state words, one source for its name and its help text, in a fixed order.</summary>
public sealed class RowStateTests
{
    [Theory]
    [InlineData(false, false, 0, null, null, "")]
    [InlineData(true, false, 0, null, null, "Pinned")]
    [InlineData(false, true, 0, null, null, "Folded")]               // a one-line note folded hides nothing
    [InlineData(false, true, 1, null, null, "Folded, 1 line hidden")]
    [InlineData(false, true, 3, null, null, "Folded, 3 lines hidden")]
    [InlineData(false, false, 3, null, null, "")]                    // lines are hidden only while folded
    [InlineData(false, false, 0, "Blue", "", "Blue")]
    [InlineData(false, false, 0, "", "Code", "Code")]
    [InlineData(true, true, 2, "Green", "Plain text", "Pinned, Folded, 2 lines hidden, Green, Plain text")]
    public void The_state_is_pinned_then_folded_then_the_other_words_given(bool pinned, bool folded, int hidden, string? colour, string? mode, string expected)
    {
        Assert.Equal(expected, RowState.Of(pinned, folded, hidden, colour, mode));
    }

    [Theory]
    [InlineData("Work", "", "Work")]
    [InlineData("Work", "Pinned", "Work, Pinned")]
    [InlineData("Weekly sync", "Folded, 3 lines hidden", "Weekly sync, Folded, 3 lines hidden")]
    [InlineData("Draft", "Pinned, Folded, 2 lines hidden, Green", "Draft, Pinned, Folded, 2 lines hidden, Green")]
    public void A_name_is_the_title_then_the_state_when_there_is_one(string title, string state, string expected)
    {
        Assert.Equal(expected, RowState.Name(title, state));
    }

    [Fact]
    public void A_folders_help_text_and_name_share_the_state_and_only_the_help_text_counts()
    {
        string state = RowState.Of(pinned: true, folded: false, hiddenLines: 0);
        Assert.Equal("Pinned, 2 notes", RowState.Of(pinned: true, folded: false, hiddenLines: 0, "2 notes"));
        Assert.Equal("Work, Pinned", RowState.Name("Work", state));
    }
}
