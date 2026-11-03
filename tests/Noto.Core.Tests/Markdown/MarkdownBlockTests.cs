using Noto.Core.Markdown;
using Xunit;
using static Noto.Core.Tests.Markdown.Md;

namespace Noto.Core.Tests.Markdown;

/// <summary>Block rules (markdown-model.md §2): one block per line, except a fenced code block.</summary>
public sealed class MarkdownBlockTests
{
    [Fact]
    public void The_empty_note_is_one_blank_block()
    {
        MarkdownDocument document = MarkdownDocument.Parse(string.Empty);

        MarkdownBlock block = Assert.Single(document.Blocks);
        Assert.Equal((MarkdownBlockKind.Blank, 0, 0), (block.Kind, block.Start, block.Length));
        Assert.Empty(document.Spans);
    }

    [Theory]
    [InlineData("plain words")]
    [InlineData("  leading spaces stay text")]
    [InlineData("a # not a heading")]
    public void Plain_text_is_a_paragraph_with_no_spans(string source)
    {
        Assert.Equal(MarkdownBlockKind.Paragraph, Block(source).Kind);
        Assert.Equal(string.Empty, Shape(source));
    }

    [Theory]
    [InlineData("", 1)]
    [InlineData("   ", 1)]
    [InlineData("a\n\nb", 3)]
    [InlineData("a\n", 2)]
    public void Every_line_is_a_block_including_blank_and_final_empty_lines(string source, int blocks)
    {
        Assert.Equal(blocks, MarkdownDocument.Parse(source).Blocks.Count);
    }

    [Theory]
    [InlineData("# One", 1, "Markup[# ] Heading1[One]")]
    [InlineData("###### Six", 6, "Markup[###### ] Heading6[Six]")]
    [InlineData("   ## Indented three", 2, "Markup[   ## ] Heading2[Indented three]")]
    [InlineData("## Title ##", 2, "Markup[## ] Heading2[Title] Markup[ ##]")]
    [InlineData("## Title ##   ", 2, "Markup[## ] Heading2[Title] Markup[ ##]")]
    [InlineData("# C#", 1, "Markup[# ] Heading1[C#]")]
    [InlineData("#", 1, "Markup[#]")]
    [InlineData("## ##", 2, "Markup[## ] Markup[##]")]
    [InlineData("#\tTab", 1, "Markup[#\t] Heading1[Tab]")]
    [InlineData("# **Bold** title", 1, "Markup[# ] Heading1[**Bold** title] Markup[**] Strong[Bold] Markup[**]")]
    public void Atx_headings_one_to_six_with_their_markers_as_markup(string source, int level, string shape)
    {
        MarkdownBlock block = Block(source);

        Assert.Equal((MarkdownBlockKind.Heading, level), (block.Kind, block.Level));
        Assert.Equal(shape, Shape(source));
    }

    [Theory]
    [InlineData("####### Seven")]
    [InlineData("#Title")]
    [InlineData("    # Four spaces")]
    [InlineData("\t# Tab")]
    public void Not_a_heading_as_in_CommonMark_and_NoteTitle(string source)
    {
        Assert.Equal(MarkdownBlockKind.Paragraph, Block(source).Kind);
    }

    [Theory]
    [InlineData("> quoted", "Markup[> ] Quote[quoted]")]
    [InlineData(">tight", "Markup[>] Quote[tight]")]
    [InlineData("  > *em*", "Markup[  > ] Quote[*em*] Markup[*] Emphasis[em] Markup[*]")]
    [InlineData(">", "Markup[>]")]
    [InlineData("> - item", "Markup[> ] Quote[- item]")]
    public void A_quote_is_its_marker_then_inline_text(string source, string shape)
    {
        Assert.Equal(MarkdownBlockKind.Quote, Block(source).Kind);
        Assert.Equal(shape, Shape(source));
    }

    [Theory]
    [InlineData("- item", MarkdownBlockKind.BulletItem, 0, "ListMarker[- ]")]
    [InlineData("* item", MarkdownBlockKind.BulletItem, 0, "ListMarker[* ]")]
    [InlineData("+ item", MarkdownBlockKind.BulletItem, 0, "ListMarker[+ ]")]
    [InlineData("-", MarkdownBlockKind.BulletItem, 0, "ListMarker[-]")]
    [InlineData("    - nested", MarkdownBlockKind.BulletItem, 4, "ListMarker[- ]")]
    [InlineData("\t- tabbed", MarkdownBlockKind.BulletItem, 4, "ListMarker[- ]")]
    [InlineData("  \t- mixed", MarkdownBlockKind.BulletItem, 4, "ListMarker[- ]")]
    [InlineData("1. one", MarkdownBlockKind.OrderedItem, 0, "ListMarker[1. ]")]
    [InlineData("10) ten", MarkdownBlockKind.OrderedItem, 0, "ListMarker[10) ]")]
    [InlineData("123456789. nine digits", MarkdownBlockKind.OrderedItem, 0, "ListMarker[123456789. ]")]
    [InlineData("2.", MarkdownBlockKind.OrderedItem, 0, "ListMarker[2.]")]
    public void List_items_with_their_marker_and_indent(string source, MarkdownBlockKind kind, int indent, string shape)
    {
        MarkdownBlock block = Block(source);

        Assert.Equal((kind, indent), (block.Kind, block.Indent));
        Assert.Equal(shape, Shape(source));
    }

    [Theory]
    [InlineData("-item")]
    [InlineData("1.one")]
    [InlineData("1234567890. ten digits")]
    [InlineData("a. letter")]
    [InlineData("**bold** start")]
    public void Not_a_list_item(string source)
    {
        Assert.Equal(MarkdownBlockKind.Paragraph, Block(source).Kind);
    }

    [Theory]
    [InlineData("- [ ] open", false, "ListMarker[- ] TaskBox[[ ]]")]
    [InlineData("- [x] done", true, "ListMarker[- ] TaskBox[[x]] TaskDone[done]")]
    [InlineData("* [X] done", true, "ListMarker[* ] TaskBox[[X]] TaskDone[done]")]
    [InlineData("1. [ ] ordered", false, "ListMarker[1. ] TaskBox[[ ]]")]
    [InlineData("- [ ]", false, "ListMarker[- ] TaskBox[[ ]]")]
    [InlineData("  - [x] **nested**", true, "ListMarker[- ] TaskBox[[x]] TaskDone[**nested**] Markup[**] Strong[nested] Markup[**]")]
    public void Task_items_with_their_box_and_a_done_items_text(string source, bool isChecked, string shape)
    {
        MarkdownBlock block = Block(source);

        Assert.Equal((MarkdownBlockKind.TaskItem, isChecked), (block.Kind, block.IsChecked));
        Assert.Equal(shape, Shape(source));
    }

    [Theory]
    [InlineData("- [] no space")]
    [InlineData("- [y] other letter")]
    [InlineData("- [ ]tight")]
    [InlineData("[ ] no marker")]
    public void Not_a_task_item(string source)
    {
        Assert.NotEqual(MarkdownBlockKind.TaskItem, Block(source).Kind);
    }

    [Theory]
    [InlineData("---", "Separator[---]")]
    [InlineData("***", "Separator[***]")]
    [InlineData("___", "Separator[___]")]
    [InlineData("- - -", "Separator[- - -]")]
    [InlineData("  * * *  ", "Separator[* * *]")]
    [InlineData("------", "Separator[------]")]
    public void Separators_outrank_list_items(string source, string shape)
    {
        Assert.Equal(MarkdownBlockKind.Separator, Block(source).Kind);
        Assert.Equal(shape, Shape(source));
    }

    [Theory]
    [InlineData("--")]
    [InlineData("---a")]
    [InlineData("-*-")]
    [InlineData("    ---")]
    public void Not_a_separator(string source)
    {
        Assert.NotEqual(MarkdownBlockKind.Separator, Block(source).Kind);
    }

    [Fact]
    public void A_fence_is_one_block_whose_lines_are_never_parsed()
    {
        const string source = "```csharp\n# not a heading\n**not bold**\n\n```\nafter";
        MarkdownDocument document = MarkdownDocument.Parse(source);

        Assert.Equal([MarkdownBlockKind.CodeFence, MarkdownBlockKind.Paragraph], document.Blocks.Select(b => b.Kind));
        Assert.Equal("Markup[```csharp] CodeBlock[# not a heading] CodeBlock[**not bold**] Markup[```]", Shape(document));
        Assert.Equal(source.IndexOf("\nafter", StringComparison.Ordinal), document.Blocks[0].End);
    }

    [Theory]
    [InlineData("~~~\ncode\n~~~", 1)]
    [InlineData("````\ncode\n```\nstill code\n`````", 1)]
    [InlineData("```\ncode\n```   ", 1)]
    [InlineData("   ```\ncode\n   ```", 1)]
    public void A_fence_closes_with_the_same_mark_at_least_as_long(string source, int blocks)
    {
        MarkdownDocument document = MarkdownDocument.Parse(source);

        Assert.Equal(blocks, document.Blocks.Count);
        Assert.Equal(MarkdownBlockKind.CodeFence, document.Blocks[0].Kind);
    }

    [Theory]
    [InlineData("```\ncode\n~~~\nmore")]
    [InlineData("```\ncode\n``` x")]
    [InlineData("```\ncode\n    ```")]
    [InlineData("```\nnever closed\n# still code")]
    public void An_unclosed_fence_runs_to_the_end_of_the_note(string source)
    {
        MarkdownDocument document = MarkdownDocument.Parse(source);

        MarkdownBlock block = Assert.Single(document.Blocks);
        Assert.Equal((MarkdownBlockKind.CodeFence, source.Length), (block.Kind, block.End));
        Assert.DoesNotContain(document.Spans, s => s.Kind is MarkdownSpanKind.Heading);
    }

    [Theory]
    [InlineData("``")]
    [InlineData("``` has ` backtick")]
    [InlineData("    ```")]
    public void Not_a_fence(string source)
    {
        Assert.NotEqual(MarkdownBlockKind.CodeFence, Block(source).Kind);
    }

    [Fact]
    public void Deferred_constructs_read_as_text()
    {
        // Setext underline, table row, HTML, entity, ::mark::, #rrggbb, autolink, reference link, link title.
        string[] sources =
        [
            "Title\n===",
            "| a | b |",
            "<b>html</b>",
            "&amp;",
            "::marked::",
            "#ff8800",
            "<https://example.com>",
            "[ref][1]",
            "[t](u \"title\")",
        ];

        foreach (string source in sources)
        {
            MarkdownDocument document = MarkdownDocument.Parse(source);
            Assert.All(document.Blocks, b => Assert.Equal(MarkdownBlockKind.Paragraph, b.Kind));
            Assert.Empty(document.Spans);
        }
    }
}
