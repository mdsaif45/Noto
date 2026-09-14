using Noto.Core.Notes;
using Xunit;

namespace Noto.Core.Tests.Notes;

/// <summary>
/// The title algorithm frozen in core-note-engine-contract.md §3.
/// </summary>
/// <remarks>
/// Every case below is named by the contract or derives from CommonMark, which
/// ADR-004 makes normative. The title is user-visible everywhere — folded notes
/// (B12), lists, note URLs — so a silent change here is a product change.
/// </remarks>
public sealed class NoteTitleTests
{
    // ---- the first non-empty line ---------------------------------------

    [Fact]
    public void Plain_first_line_is_the_title()
    {
        Assert.Equal("Shopping list", NoteTitle.From("Shopping list\nmilk\neggs"));
    }

    [Fact]
    public void Leading_blank_lines_are_skipped()
    {
        // "first NON-EMPTY line" (B16), not "first line".
        Assert.Equal("Real title", NoteTitle.From("\n\n\nReal title\nbody"));
    }

    [Fact]
    public void Whitespace_only_lines_are_skipped()
    {
        Assert.Equal("Real title", NoteTitle.From("   \n\t\n  \nReal title"));
    }

    [Fact]
    public void Surrounding_whitespace_is_trimmed()
    {
        Assert.Equal("Title", NoteTitle.From("   Title   \nbody"));
    }

    [Fact]
    public void Only_the_first_non_empty_line_is_used()
    {
        Assert.Equal("First", NoteTitle.From("First\nSecond\nThird"));
    }

    // ---- line terminators: LF, CRLF, CR ---------------------------------

    [Theory]
    [InlineData("Title\nbody")]       // LF
    [InlineData("Title\r\nbody")]     // CRLF
    [InlineData("Title\rbody")]       // CR
    public void All_three_line_terminators_are_recognised(string content)
    {
        // CommonMark §2.1 defines all three as line endings, and notes arrive
        // from editors and clipboards that use each.
        Assert.Equal("Title", NoteTitle.From(content));
    }

    [Fact]
    public void Crlf_is_one_terminator_not_two()
    {
        // Treating CR and LF separately would see a phantom empty line between
        // them — harmless here, but it would skip a line elsewhere.
        Assert.Equal("Title", NoteTitle.From("\r\nTitle\r\nbody"));
    }

    // ---- ATX headings ----------------------------------------------------

    [Theory]
    [InlineData("# Title", "Title")]
    [InlineData("## Title", "Title")]
    [InlineData("### Title", "Title")]
    [InlineData("#### Title", "Title")]
    [InlineData("##### Title", "Title")]
    [InlineData("###### Title", "Title")]
    public void Atx_heading_markers_are_stripped(string content, string expected)
    {
        Assert.Equal(expected, NoteTitle.From(content));
    }

    [Fact]
    public void Hash_without_a_following_space_is_not_a_heading()
    {
        // CommonMark §4.2 requires a space or tab after the opening sequence,
        // so "#Title" is a paragraph and the '#' is part of the text.
        Assert.Equal("#Title", NoteTitle.From("#Title"));
    }

    [Fact]
    public void Seven_hashes_is_not_a_heading()
    {
        // CommonMark caps ATX headings at six levels.
        Assert.Equal("####### Title", NoteTitle.From("####### Title"));
    }

    [Theory]
    [InlineData(" # Title")]
    [InlineData("  # Title")]
    [InlineData("   # Title")]
    public void Up_to_three_leading_spaces_still_forms_a_heading(string content)
    {
        Assert.Equal("Title", NoteTitle.From(content));
    }

    [Fact]
    public void Four_leading_spaces_is_an_indented_code_block()
    {
        // At four spaces CommonMark switches to an indented code block, so the
        // '#' is literal. The line is still trimmed as a title.
        Assert.Equal("# Title", NoteTitle.From("    # Title"));
    }

    [Fact]
    public void A_closing_hash_sequence_is_stripped()
    {
        Assert.Equal("Title", NoteTitle.From("# Title #"));
    }

    [Fact]
    public void A_long_closing_hash_sequence_is_stripped()
    {
        Assert.Equal("Title", NoteTitle.From("## Title ######"));
    }

    [Fact]
    public void A_trailing_hash_that_is_part_of_a_word_is_kept()
    {
        // CommonMark only treats a closing sequence as decoration when it is
        // preceded by a space — otherwise "# C#" would lose its language.
        Assert.Equal("C#", NoteTitle.From("# C#"));
    }

    [Fact]
    public void A_heading_with_a_tab_after_the_marker_is_stripped()
    {
        Assert.Equal("Title", NoteTitle.From("#\tTitle"));
    }

    // ---- headings with no text ------------------------------------------

    [Fact]
    public void A_bare_hash_is_an_empty_heading()
    {
        // Contract §3: "content that is only a heading" is a required case.
        Assert.Equal(string.Empty, NoteTitle.From("#"));
    }

    [Fact]
    public void A_heading_of_only_markers_is_empty()
    {
        Assert.Equal(string.Empty, NoteTitle.From("## ##"));
    }

    [Fact]
    public void Content_that_is_only_a_heading_still_yields_its_text()
    {
        Assert.Equal("Only a heading", NoteTitle.From("# Only a heading"));
    }

    // ---- Setext is NOT recognised ---------------------------------------

    [Fact]
    public void Setext_underline_is_not_a_heading_marker()
    {
        // Contract §3: the title is line-based, so the underline is a separate
        // line the algorithm never reaches. The title is the text itself.
        Assert.Equal("Title", NoteTitle.From("Title\n====="));
    }

    [Fact]
    public void Setext_dash_underline_is_not_a_heading_marker()
    {
        Assert.Equal("Title", NoteTitle.From("Title\n-----"));
    }

    [Fact]
    public void A_setext_underline_alone_is_returned_verbatim()
    {
        // Nothing special: it is simply the first non-empty line.
        Assert.Equal("=====", NoteTitle.From("=====\nbody"));
    }

    // ---- empty and null --------------------------------------------------

    [Fact]
    public void Empty_content_yields_an_empty_title()
    {
        Assert.Equal(string.Empty, NoteTitle.From(string.Empty));
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("\n\n\n")]
    [InlineData(" \t \r\n \t ")]
    public void Whitespace_only_content_yields_an_empty_title(string content)
    {
        Assert.Equal(string.Empty, NoteTitle.From(content));
    }

    [Fact]
    public void Null_content_yields_an_empty_title_never_null()
    {
        // Contract §3: the title is NEVER null. Every consumer would otherwise
        // need its own null check, and one of them would forget.
        Assert.Equal(string.Empty, NoteTitle.From(null));
    }

    // ---- no length cap ---------------------------------------------------

    [Fact]
    public void A_very_long_line_is_not_truncated()
    {
        // Contract §3: there is no domain-level cap. Truncation is a display
        // concern; capping here would make the domain lossy for a presentation
        // reason.
        string longLine = new('a', 10_000);

        Assert.Equal(longLine, NoteTitle.From(longLine));
        Assert.Equal(10_000, NoteTitle.From(longLine).Length);
    }

    // ---- unicode ---------------------------------------------------------

    [Fact]
    public void Unicode_survives_verbatim()
    {
        Assert.Equal("日本語のメモ 🗒️", NoteTitle.From("日本語のメモ 🗒️\nbody"));
    }
}
