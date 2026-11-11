using Noto.Core.Workspace;
using Xunit;

namespace Noto.Core.Tests.Workspace;

/// <summary>F13 (#154): the note text's fonts and sizes, and the steps the size shortcuts take (G41, G42).</summary>
public sealed class NoteTextTests
{
    [Theory]
    [InlineData(14, 1, 15)]
    [InlineData(16, 1, 18)]
    [InlineData(24, 1, 24)]   // held at the top
    [InlineData(14, -1, 13)]
    [InlineData(18, -1, 16)]
    [InlineData(10, -1, 10)]  // held at the bottom
    [InlineData(17, 1, 18)]   // between steps: the next step up
    [InlineData(17, -1, 16)]  // between steps: the next step down
    public void A_size_steps_to_the_next_size_in_its_direction_and_holds_at_the_ends(double size, int direction, double expected)
    {
        Assert.Equal(expected, NoteText.Step(size, direction));
    }

    [Fact]
    public void Every_step_is_a_size_that_may_be_stored_and_the_default_is_one_of_them()
    {
        Assert.True(NoteText.IsSize(NoteText.DefaultSize));
        Assert.All(NoteText.Sizes, size => Assert.True(NoteText.IsSize(NoteText.Step(size, 1)) && NoteText.IsSize(NoteText.Step(size, -1))));
        Assert.Equal(NoteText.Sizes.Order(), NoteText.Sizes);
        Assert.False(NoteText.IsSize(17));
        Assert.False(NoteText.IsSize(double.NaN));
    }

    [Fact]
    public void A_font_is_the_default_or_one_of_the_listed_families_exactly()
    {
        Assert.True(NoteText.IsFont(NoteText.DefaultFont));
        Assert.All(NoteText.Fonts, font => Assert.True(NoteText.IsFont(font)));
        Assert.False(NoteText.IsFont("georgia"));
        Assert.False(NoteText.IsFont("Comic Sans MS"));
        Assert.DoesNotContain(NoteText.DefaultFont, NoteText.Fonts);
    }
}
