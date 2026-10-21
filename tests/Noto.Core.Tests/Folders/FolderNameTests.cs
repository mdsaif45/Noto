using Noto.Core.Folders;
using Xunit;

namespace Noto.Core.Tests.Folders;

/// <summary>The one normalisation a folder name goes through before it is stored (create-and-rename §2.1).</summary>
public sealed class FolderNameTests
{
    [Theory]
    [InlineData("  Work  ", "Work")]
    [InlineData("Work", "Work")]
    [InlineData("\tWork\t", "Work")]
    [InlineData("Two\twords", "Two words")]
    [InlineData("Two\r\nlines", "Two lines")]
    [InlineData("A\n\n\nB", "A B")]
    [InlineData("A\u2028B", "A B")]
    [InlineData("A\u2029B", "A B")]
    [InlineData("A\u0007B", "A B")]
    [InlineData("  keep  inner   spaces  ", "keep  inner   spaces")]
    [InlineData("Ünïcödé 📁", "Ünïcödé 📁")]
    public void A_name_is_trimmed_and_made_one_line(string typed, string stored) =>
        Assert.Equal(stored, FolderName.Normalise(typed));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    [InlineData("\u2028\u2029 ")]
    public void Nothing_left_is_no_name(string? typed) =>
        Assert.Null(FolderName.Normalise(typed));

    [Fact]
    public void Normalising_twice_changes_nothing()
    {
        string once = FolderName.Normalise(" a\tb \r\n c ")!;

        Assert.Equal(once, FolderName.Normalise(once));
    }

    [Fact]
    public void There_is_no_length_cap()
    {
        string long_ = new('x', 10_000);

        Assert.Equal(long_, FolderName.Normalise(long_));
    }
}
