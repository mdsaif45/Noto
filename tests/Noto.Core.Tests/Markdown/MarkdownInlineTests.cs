using Xunit;
using static Noto.Core.Tests.Markdown.Md;

namespace Noto.Core.Tests.Markdown;

/// <summary>
/// Inline rules (markdown-model.md §2). The emphasis cases are CommonMark's; each expected shape was checked against
/// a CommonMark reference implementation (markdown-it, CommonMark mode), translated into spans.
/// </summary>
public sealed class MarkdownInlineTests
{
    [Theory]
    [InlineData("**a**", "Markup[**] Strong[a] Markup[**]")]
    [InlineData("__a__", "Markup[__] Strong[a] Markup[__]")]
    [InlineData("*a*", "Markup[*] Emphasis[a] Markup[*]")]
    [InlineData("_a_", "Markup[_] Emphasis[a] Markup[_]")]
    [InlineData("~~gone~~", "Markup[~~] Strikethrough[gone] Markup[~~]")]
    [InlineData("==under==", "Markup[==] Underline[under] Markup[==]")]
    [InlineData("::marked::", "Markup[::] Highlight[marked] Markup[::]")]
    [InlineData("a ::b c:: d", "Markup[::] Highlight[b c] Markup[::]")]
    [InlineData("`code`", "Markup[`] Code[code] Markup[`]")]
    [InlineData("say **it** now", "Markup[**] Strong[it] Markup[**]")]
    public void Each_inline_style_is_its_markers_and_the_text_between(string source, string shape)
    {
        Assert.Equal(shape, Shape(source));
    }

    [Theory]
    [InlineData("~one~")]
    [InlineData("a ~~~three~~~")]
    [InlineData("=one=")]
    [InlineData("===three===")]
    [InlineData(":one:")]
    [InlineData(":::three:::")]
    [InlineData("std::vector and std::string")]
    [InlineData("fe80::1 and ::1")]
    [InlineData("at 10:30, see note:: here")]
    [InlineData("a == b ==")]
    [InlineData("a ** b **")]
    [InlineData("foo_bar_")]
    [InlineData("snake_case_name")]
    [InlineData("_中文_中文")]
    [InlineData("a ****")]
    [InlineData("a*\"foo\"*")]
    [InlineData("x*.y*")]
    public void Runs_that_cannot_open_or_close_are_text(string source)
    {
        Assert.Equal(string.Empty, Shape(source));
    }

    [Theory]
    [InlineData("***x***", "Markup[*] Emphasis[**x**] Markup[**] Strong[x] Markup[**] Markup[*]")]
    [InlineData("**a *b* c**", "Markup[**] Strong[a *b* c] Markup[*] Emphasis[b] Markup[*] Markup[**]")]
    [InlineData("*foo**bar**baz*", "Markup[*] Emphasis[foo**bar**baz] Markup[**] Strong[bar] Markup[**] Markup[*]")]
    [InlineData("__foo, __bar__, baz__", "Markup[__] Strong[foo, __bar__, baz] Markup[__] Strong[bar] Markup[__] Markup[__]")]
    [InlineData("*(*foo*)*", "Markup[*] Emphasis[(*foo*)] Markup[*] Emphasis[foo] Markup[*] Markup[*]")]
    [InlineData("*a **b* c**", "Markup[*] Emphasis[a **b* c*] Markup[*] Emphasis[*b* c] Markup[*] Emphasis[b] Markup[*] Markup[*] Markup[*]")]
    public void Nesting_follows_CommonMark(string source, string shape)
    {
        Assert.Equal(shape, Shape(source));
    }

    [Theory]
    [InlineData("**foo*", "Markup[*] Emphasis[foo] Markup[*]")]
    [InlineData("*foo**", "Markup[*] Emphasis[foo] Markup[*]")]
    [InlineData("*foo**bar*", "Markup[*] Emphasis[foo**bar] Markup[*]")]
    [InlineData("**foo**bar**", "Markup[**] Strong[foo] Markup[**]")]
    [InlineData("*a*b*c*", "Markup[*] Emphasis[a] Markup[*] Markup[*] Emphasis[c] Markup[*]")]
    [InlineData("*a _b* c_", "Markup[*] Emphasis[a _b] Markup[*]")]
    [InlineData("**a _b** c_", "Markup[**] Strong[a _b] Markup[**]")]
    public void Overlap_and_leftover_marks_follow_CommonMark_and_leftovers_are_text(string source, string shape)
    {
        Assert.Equal(shape, Shape(source));
    }

    [Theory]
    [InlineData("foo*bar*", "Markup[*] Emphasis[bar] Markup[*]")]
    [InlineData("x**y**z", "Markup[**] Strong[y] Markup[**]")]
    [InlineData("中**文**字", "Markup[**] Strong[文] Markup[**]")]
    [InlineData("**中文**中文", "Markup[**] Strong[中文] Markup[**]")]
    [InlineData("**👍**", "Markup[**] Strong[👍] Markup[**]")]
    [InlineData("**é**", "Markup[**] Strong[é] Markup[**]")]
    [InlineData("**👨‍👩‍👧 family**", "Markup[**] Strong[👨‍👩‍👧 family] Markup[**]")]
    public void Star_marks_work_inside_words_and_beside_any_script(string source, string shape)
    {
        Assert.Equal(shape, Shape(source));
    }

    [Theory]
    [InlineData("`a*b*c`", "Markup[`] Code[a*b*c] Markup[`]")]
    [InlineData("``a`b``", "Markup[``] Code[a`b] Markup[``]")]
    [InlineData("`[a](b)`", "Markup[`] Code[[a](b)] Markup[`]")]
    [InlineData("**a`**`b**", "Markup[**] Strong[a`**`b] Markup[`] Code[**] Markup[`] Markup[**]")]
    [InlineData("`unclosed", "")]
    [InlineData("``a`", "")]
    [InlineData("`a``b`", "Markup[`] Code[a``b] Markup[`]")]
    [InlineData("`\\*`", "Markup[`] Code[\\*] Markup[`]")]
    public void Code_spans_bind_first_and_nothing_inside_them_is_formatting(string source, string shape)
    {
        Assert.Equal(shape, Shape(source));
    }

    [Theory]
    [InlineData("[text](https://x.y)", "Markup[[] Link[text] Markup[](] LinkTarget[https://x.y] Markup[)]")]
    [InlineData("[a](b(c)d)", "Markup[[] Link[a] Markup[](] LinkTarget[b(c)d] Markup[)]")]
    [InlineData("[a [b] c](u)", "Markup[[] Link[a [b] c] Markup[](] LinkTarget[u] Markup[)]")]
    [InlineData("[**bold**](u)", "Markup[[] Link[**bold**] Markup[**] Strong[bold] Markup[**] Markup[](] LinkTarget[u] Markup[)]")]
    [InlineData("[a *b](u) c*", "Markup[[] Link[a *b] Markup[](] LinkTarget[u] Markup[)]")]
    [InlineData("*a [b* c](u)", "Markup[[] Link[b* c] Markup[](] LinkTarget[u] Markup[)]")]
    [InlineData("[outer [inner](a)](b)", "Markup[[] Link[inner] Markup[](] LinkTarget[a] Markup[)]")]
    [InlineData("[a `]` b](u)", "Markup[[] Link[a `]` b] Markup[`] Code[]] Markup[`] Markup[](] LinkTarget[u] Markup[)]")]
    [InlineData("[a](u\\)v)", "Markup[[] Link[a] Markup[](] LinkTarget[u\\)v] Markup[)]")]
    public void Links_with_their_text_and_destination(string source, string shape)
    {
        Assert.Equal(shape, Shape(source));
    }

    [Theory]
    [InlineData("[](u)")]
    [InlineData("[a](b c)")]
    [InlineData("[a]()")]
    [InlineData("[a](unclosed")]
    [InlineData("[a] (u)")]
    [InlineData("[a](b(c)")]
    public void Not_a_link_empty_text_spaces_and_unclosed_destinations_are_text(string source)
    {
        Assert.Equal(string.Empty, Shape(source));
    }

    [Theory]
    [InlineData("\\*not em\\*", "Markup[\\] Markup[\\]")]
    [InlineData("\\**x**", "Markup[\\] Markup[*] Emphasis[x] Markup[*]")]
    [InlineData("\\`not code`", "Markup[\\]")]
    [InlineData("\\[not](link)", "Markup[\\]")]
    [InlineData("a\\b", "")]
    [InlineData("end\\", "")]
    public void A_backslash_before_punctuation_makes_it_literal(string source, string shape)
    {
        Assert.Equal(shape, Shape(source));
    }

    [Fact]
    public void Inline_markup_never_crosses_a_line()
    {
        Assert.Equal(string.Empty, Shape("**open\nclose**"));
        Assert.Equal(string.Empty, Shape("`open\nclose`"));
        Assert.Equal(string.Empty, Shape("[open\nclose](u)"));
    }
}
