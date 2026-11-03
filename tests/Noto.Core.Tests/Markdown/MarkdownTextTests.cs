using Noto.Core.Markdown;
using Xunit;
using static Noto.Core.Tests.Markdown.Md;

namespace Noto.Core.Tests.Markdown;

/// <summary>Offsets into the exact source: line endings, Unicode, long lines (invariants I1, I2, I5, I6).</summary>
public sealed class MarkdownTextTests
{
    private const string Lf = "# Title\n- [x] **done** item\n```\ncode *x*\n```\n> quote ~~old~~\n";

    [Fact]
    public void The_source_is_the_same_instance_never_rewritten()
    {
        string source = Lf.Replace("\n", "\r\n", StringComparison.Ordinal);

        MarkdownDocument document = MarkdownDocument.Parse(source);

        Assert.Same(source, document.Source);
        Assert.Contains("\r\n", document.Source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public void Lf_crlf_and_lone_cr_give_the_same_structure(string newline)
    {
        string source = Lf.Replace("\n", newline, StringComparison.Ordinal);

        MarkdownDocument document = MarkdownDocument.Parse(source);

        Assert.Equal(Shape(Lf), Shape(document));
        Assert.Equal(
            MarkdownDocument.Parse(Lf).Blocks.Select(b => (b.Kind, b.Level, b.IsChecked)),
            document.Blocks.Select(b => (b.Kind, b.Level, b.IsChecked)));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r")]
    public void With_one_character_breaks_the_offsets_are_the_editors_one_to_one(string newline)
    {
        // RichEdit stores one character per break (spike finding 2): an LF or CR source parses at its offsets.
        string source = Lf.Replace("\n", newline, StringComparison.Ordinal);

        Assert.Equal(
            MarkdownDocument.Parse(Lf).Spans.Select(s => (s.Kind, s.Start, s.Length)),
            MarkdownDocument.Parse(source).Spans.Select(s => (s.Kind, s.Start, s.Length)));
    }

    [Fact]
    public void A_crlf_sources_offsets_are_its_own_and_never_cover_a_break()
    {
        string source = Lf.Replace("\n", "\r\n", StringComparison.Ordinal);
        MarkdownDocument document = MarkdownDocument.Parse(source);

        Assert.All(document.Spans, s => Assert.DoesNotContain(source.Substring(s.Start, s.Length), c => c is '\r' or '\n'));
        Assert.Equal("**done**", source.Substring(document.Blocks[1].Spans.First(s => s.Kind == MarkdownSpanKind.TaskDone).Start, 8));
    }

    [Fact]
    public void Offsets_are_utf16_code_units_so_text_before_a_span_shifts_it_by_its_code_units()
    {
        // "👍" is two code units, "é" (e + combining acute) two characters, "漢" one: the span moves by exactly that.
        foreach (string prefix in new[] { "👍", "é", "漢", "👨‍👩‍👧" })
        {
            MarkdownSpan strong = MarkdownDocument.Parse(prefix + " **x**").Spans.Single(s => s.Kind == MarkdownSpanKind.Strong);

            Assert.Equal(prefix.Length + 3, strong.Start);
        }
    }

    [Fact]
    public void A_lone_surrogate_is_text_and_breaks_nothing()
    {
        string source = "**a\uD83D** \uDC4D *b*";

        Assert.Equal("Markup[**] Strong[a\uD83D] Markup[**] Markup[*] Emphasis[b] Markup[*]", Shape(source));
    }

    [Fact]
    public void Tabs_and_trailing_spaces_stay_where_they_are()
    {
        const string source = "\t**a**\t  ";

        MarkdownSpan strong = MarkdownDocument.Parse(source).Spans.Single(s => s.Kind == MarkdownSpanKind.Strong);

        Assert.Equal((3, 1), (strong.Start, strong.Length));
    }

    [Fact]
    public void A_very_long_line_of_unmatched_marks_parses_quickly_and_whole()
    {
        // 100 000 marks, none of which can pair: each failed search raises its floor, which keeps this linear.
        string source = string.Concat(Enumerable.Repeat("*a_", 33_334));
        var clock = System.Diagnostics.Stopwatch.StartNew();

        MarkdownDocument document = MarkdownDocument.Parse(source);

        Assert.True(clock.ElapsedMilliseconds < 2_000, $"took {clock.ElapsedMilliseconds} ms");
        Assert.All(document.Spans, s => Assert.InRange(s.End, 0, source.Length));
    }
}
