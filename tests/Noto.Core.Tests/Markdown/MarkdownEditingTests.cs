using Noto.Core.Markdown;
using Xunit;
using static Noto.Core.Tests.Markdown.Md;

namespace Noto.Core.Tests.Markdown;

/// <summary>
/// What an edit does to formatting (markdown-model.md §4): re-parse, and style what the parse says. Each case is the
/// source after one edit, and what that source parses as.
/// </summary>
public sealed class MarkdownEditingTests
{
    [Theory]
    [InlineData("**bol", "")]
    [InlineData("**bold*", "Markup[*] Emphasis[bold] Markup[*]")]
    [InlineData("**bold**", "Markup[**] Strong[bold] Markup[**]")]
    [InlineData("[link](htt", "")]
    [InlineData("`co", "")]
    [InlineData("~~stri", "")]
    [InlineData("#", "Markup[#]")]
    [InlineData("# ", "Markup[# ]")]
    [InlineData("- [", "ListMarker[- ]")]
    [InlineData("- [ ]", "ListMarker[- ] TaskBox[[ ]]")]
    public void Partially_typed_syntax_is_text_until_it_is_complete(string typed, string shape)
    {
        Assert.Equal(shape, Shape(typed));
    }

    [Fact]
    public void Typing_just_inside_a_closing_marker_extends_the_span()
    {
        // **bold|**  ->  **boldx**
        Assert.Equal("Markup[**] Strong[boldx] Markup[**]", Shape(Edit("**bold**", 6, 0, "x")));
    }

    [Fact]
    public void Typing_just_after_a_closing_marker_is_plain_text()
    {
        // **bold**|  ->  **bold**x
        Assert.Equal("Markup[**] Strong[bold] Markup[**]", Shape(Edit("**bold**", 8, 0, "x")));
    }

    [Fact]
    public void Typing_just_after_an_opening_marker_starts_the_span()
    {
        // **|bold**  ->  **xbold**
        Assert.Equal("Markup[**] Strong[xbold] Markup[**]", Shape(Edit("**bold**", 2, 0, "x")));
    }

    [Fact]
    public void Inserting_inside_a_span_grows_it_by_the_insertion()
    {
        MarkdownSpan before = MarkdownDocument.Parse("a **bold** b").Spans.Single(s => s.Kind == MarkdownSpanKind.Strong);
        MarkdownSpan after = MarkdownDocument.Parse(Edit("a **bold** b", 5, 0, "xyz")).Spans.Single(s => s.Kind == MarkdownSpanKind.Strong);

        Assert.Equal((before.Start, before.Length + 3), (after.Start, after.Length));
    }

    [Fact]
    public void Deleting_some_visible_characters_shrinks_the_span()
    {
        Assert.Equal("Markup[**] Strong[bd] Markup[**]", Shape(Edit("**bold**", 3, 2, string.Empty)));
    }

    [Fact]
    public void Deleting_all_of_a_spans_text_leaves_marks_that_parse_by_where_they_are()
    {
        // The hazard P2.3's emptied-span rule prevents: alone on a line, "****" is a separator; inside a line, text.
        Assert.Equal(MarkdownBlockKind.Separator, Block(Edit("**bold**", 2, 4, string.Empty)).Kind);
        Assert.Equal(string.Empty, Shape(Edit("a **bold** b", 4, 4, string.Empty)));
    }

    [Fact]
    public void Deleting_one_marker_gives_CommonMarks_result_never_an_error()
    {
        // **bold** -> *bold**: emphasis, then a '*' of text.
        Assert.Equal("Markup[*] Emphasis[bold] Markup[*]", Shape(Edit("**bold**", 0, 1, string.Empty)));
    }

    [Fact]
    public void Deleting_a_selection_across_formatted_and_plain_text_leaves_the_partnerless_marks_as_text()
    {
        // "plain **bold** tail" with "ain **bo" selected and deleted: "plld** tail".
        Assert.Equal(string.Empty, Shape(Edit("plain **bold** tail", 2, 8, string.Empty)));
    }

    [Fact]
    public void Formatting_never_applies_inside_code()
    {
        Assert.Equal("Markup[`] Code[**x**] Markup[`]", Shape("`**x**`"));
        Assert.Equal("Markup[```] CodeBlock[# x] Markup[```]", Shape("```\n# x\n```"));
        Assert.Equal("Markup[`] Code[a **x** b] Markup[`]", Shape(Edit("`a b`", 3, 0, "**x** ")));
    }

    [Fact]
    public void Typing_an_unclosed_fence_turns_the_rest_into_code_until_it_is_closed()
    {
        const string note = "intro\n# Heading\n**bold**";
        string opened = Edit(note, 6, 0, "```\n");

        MarkdownDocument document = MarkdownDocument.Parse(opened);

        Assert.Equal([MarkdownBlockKind.Paragraph, MarkdownBlockKind.CodeFence], document.Blocks.Select(b => b.Kind));
        Assert.DoesNotContain(document.Spans, s => s.Kind is MarkdownSpanKind.Heading or MarkdownSpanKind.Strong);
    }

    // ---------------------------------------------------------------- ChangedRegion

    [Fact]
    public void An_edit_inside_a_line_changes_that_line_only()
    {
        const string before = "# Title\nsome **bold** text\n- [ ] task\nlast line";
        string after = Edit(before, 18, 0, "er");

        (int start, int length) = MarkdownDocument.ChangedRegion(MarkdownDocument.Parse(before), MarkdownDocument.Parse(after));

        Assert.Equal("some **bolerd** text", after.Substring(start, length));
    }

    [Fact]
    public void Splitting_a_line_changes_the_two_lines_it_becomes()
    {
        const string before = "first\nsome **bold** text\nlast";
        string after = Edit(before, 10, 0, "\n");

        (int start, int length) = MarkdownDocument.ChangedRegion(MarkdownDocument.Parse(before), MarkdownDocument.Parse(after));

        Assert.Equal("some\n **bold** text", after.Substring(start, length));
    }

    [Fact]
    public void Opening_a_fence_changes_everything_it_now_reaches()
    {
        const string before = "intro\n# Heading\n**bold**";
        string after = Edit(before, 6, 0, "```\n");

        (int start, int length) = MarkdownDocument.ChangedRegion(MarkdownDocument.Parse(before), MarkdownDocument.Parse(after));

        Assert.Equal((6, after.Length - 6), (start, length));
    }

    [Fact]
    public void A_replaced_character_is_restyled_even_when_the_spans_are_the_same()
    {
        // "**a**b" -> "**a**c": the same spans, but in the editor the new 'c' takes its neighbour's format, the hidden
        // '*'. Its line must be restyled, so the region is decided by text as well as spans.
        (int start, int length) = MarkdownDocument.ChangedRegion(MarkdownDocument.Parse("x\n**a**b\ny"), MarkdownDocument.Parse("x\n**a**c\ny"));

        Assert.Equal((2, 6), (start, length));
    }

    [Fact]
    public void Nothing_changed_is_an_empty_region()
    {
        MarkdownDocument document = MarkdownDocument.Parse("a\n**b**");

        Assert.Equal(0, MarkdownDocument.ChangedRegion(document, MarkdownDocument.Parse("a\n**b**")).Length);
    }

    [Fact]
    public void A_line_turned_into_a_heading_changes_that_line()
    {
        const string before = "one\nTitle\nthree";
        string after = Edit(before, 4, 0, "# ");

        (int start, int length) = MarkdownDocument.ChangedRegion(MarkdownDocument.Parse(before), MarkdownDocument.Parse(after));

        Assert.Equal("# Title", after.Substring(start, length));
    }
}
