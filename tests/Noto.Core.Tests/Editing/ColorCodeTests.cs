using Noto.Core.Editing;
using Xunit;

namespace Noto.Core.Tests.Editing;

/// <summary>F14 (#155): which <c>#rrggbb</c> codes in a note get a swatch, and the colour each names.</summary>
public sealed class ColorCodeTests
{
    [Fact]
    public void A_code_is_found_with_its_place_and_colour()
    {
        ColorCode code = Assert.Single(ColorCode.Find("Brand red is #E81123, not pink."));

        Assert.Equal((13, 7), (code.Index, code.Length));
        Assert.Equal((0xE8, 0x11, 0x23), (code.R, code.G, code.B));
    }

    [Theory]
    [InlineData("#ff0000")]
    [InlineData("(#00ff00)")]
    [InlineData("`#0000FF`")]
    [InlineData("colours: #abcdef, #123456.")]
    [InlineData("- #ffffff")]
    public void Codes_standing_on_their_own_are_found(string text)
    {
        Assert.NotEmpty(ColorCode.Find(text));
    }

    [Theory]
    [InlineData("#abcde")]        // five digits
    [InlineData("#abcdef12")]     // eight: not this format
    [InlineData("#abcdeg")]       // not hex
    [InlineData("issue#abcdef")]  // part of a word
    [InlineData("&#123456;")]     // a character reference
    [InlineData("page/#abcdef")]  // a path's fragment
    [InlineData("##abcdef")]      // a second #
    [InlineData("# abcdef")]      // a heading
    public void Anything_else_is_not_a_code(string text)
    {
        Assert.Empty(ColorCode.Find(text));
    }

    [Fact]
    public void Several_codes_are_found_in_order()
    {
        Assert.Equal([0, 9], ColorCode.Find("#000000, #FFFFFF").Select(c => c.Index));
    }
}
