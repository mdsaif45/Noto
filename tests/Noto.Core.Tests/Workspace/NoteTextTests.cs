using Noto.Core.Settings;
using Noto.Core.Workspace;
using Xunit;

namespace Noto.Core.Tests.Workspace;

/// <summary>
/// F13 (#154): the note text's fonts and sizes, and the steps the size shortcuts take (G41, G42); #234: what its line
/// height and paragraph spacing presets measure.
/// </summary>
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

    [Theory]
    [InlineData(TextSpacing.Compact, 1.15)]
    [InlineData(TextSpacing.Normal, 0)]     // the font's own: Noto's look before the setting
    [InlineData(TextSpacing.Relaxed, 1.5)]
    public void A_line_height_is_a_multiple_of_the_text_size_and_normal_keeps_the_fonts_own(TextSpacing spacing, double multiple)
    {
        Assert.Equal(multiple, NoteText.LineHeight(spacing));
    }

    [Theory]
    [InlineData(TextSpacing.Compact, 14, 0, false)]
    [InlineData(TextSpacing.Normal, 14, 0, true)]      // Noto's look before the setting: no space after, headings spaced
    [InlineData(TextSpacing.Relaxed, 14, 7, true)]
    [InlineData(TextSpacing.Relaxed, 24, 12, true)]    // half the size, at every size
    public void Paragraph_spacing_is_half_the_size_after_each_paragraph_relaxed_and_compact_spaces_nothing(TextSpacing spacing, double size, double after, bool headings)
    {
        Assert.Equal(after, NoteText.SpaceAfter(spacing, size));
        Assert.Equal(headings, NoteText.SpacesHeadings(spacing));
    }

    [Fact]
    public void Compact_is_tighter_and_relaxed_looser_than_normal()
    {
        Assert.True(NoteText.LineHeight(TextSpacing.Compact) < NoteText.LineHeight(TextSpacing.Relaxed));
        Assert.True(NoteText.LineHeight(TextSpacing.Relaxed) > 1.35);   // above the default family's own height
        Assert.True(NoteText.SpaceAfter(TextSpacing.Relaxed, NoteText.DefaultSize) > NoteText.SpaceAfter(TextSpacing.Normal, NoteText.DefaultSize));
    }

    [Fact]
    public void Both_settings_are_declared_normal_by_default_and_refuse_a_value_that_is_not_a_preset()
    {
        Assert.Equal([TextSpacing.Compact, TextSpacing.Normal, TextSpacing.Relaxed], Enum.GetValues<TextSpacing>());

        foreach ((SettingKey<TextSpacing> key, string name) in new[] { (SettingKeys.AppearanceLineHeight, "appearance.line-height"), (SettingKeys.AppearanceParagraphSpacing, "appearance.paragraph-spacing") })
        {
            Assert.Equal(name, key.Name);
            Assert.Equal(TextSpacing.Normal, key.Default);
            Assert.Equal(SettingValueKind.Enumeration, key.Kind);
            Assert.Contains(key, SettingKeys.All);
            Assert.All(Enum.GetValues<TextSpacing>(), v => Assert.True(key.IsValid(v)));
            Assert.False(key.IsValid((TextSpacing)3));
        }
    }
}
