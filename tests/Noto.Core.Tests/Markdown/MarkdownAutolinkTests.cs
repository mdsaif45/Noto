using Xunit;
using static Noto.Core.Tests.Markdown.Md;

namespace Noto.Core.Tests.Markdown;

/// <summary>Bare web addresses (ADR-019 §3, GFM's extended autolinks): a Link span with no markup, its own destination.</summary>
public sealed class MarkdownAutolinkTests
{
    [Theory]
    [InlineData("see https://example.com/a?b=1 now", "Link[https://example.com/a?b=1]")]
    [InlineData("http://example.com", "Link[http://example.com]")]
    [InlineData("go to www.example.com.", "Link[www.example.com]")]
    [InlineData("(see https://example.com/x)", "Link[https://example.com/x]")]
    [InlineData("https://en.wikipedia.org/wiki/Foo_(bar)!", "Link[https://en.wikipedia.org/wiki/Foo_(bar)]")]
    [InlineData("first https://a.com, then https://b.com/x?", "Link[https://a.com] Link[https://b.com/x]")]
    [InlineData("HTTPS://EXAMPLE.COM", "Link[HTTPS://EXAMPLE.COM]")]
    [InlineData("a <https://x.com>", "")]
    [InlineData("- [ ] call https://x.com", "ListMarker[- ] TaskBox[[ ]] Link[https://x.com]")]
    public void A_bare_address_at_a_word_start_is_a_link_without_its_trailing_punctuation(string source, string shape)
    {
        Assert.Equal(shape, Shape(source).Replace("Markup[<] ", string.Empty, StringComparison.Ordinal).Trim());
    }

    [Theory]
    [InlineData("`https://example.com`", "Markup[`] Code[https://example.com] Markup[`]")]
    [InlineData("[site](https://example.com)", "Markup[[] Link[site] Markup[](] LinkTarget[https://example.com] Markup[)]")]
    [InlineData("[see https://x.com](https://y.com)", "Markup[[] Link[see https://x.com] Markup[](] LinkTarget[https://y.com] Markup[)]")]
    public void Never_inside_code_or_a_written_link(string source, string shape)
    {
        Assert.Equal(shape, Shape(source));
    }

    [Theory]
    [InlineData("xhttps://example.com")]
    [InlineData("https://")]
    [InlineData("https://.")]
    [InlineData("www.")]
    [InlineData("www.?")]
    [InlineData("ftp://example.com")]
    [InlineData("mailto:me@example.com")]
    public void Not_an_address_at_a_word_start_is_text(string source)
    {
        Assert.Equal(string.Empty, Shape(source));
    }

    [Fact]
    public void Emphasis_around_an_address_still_pairs()
    {
        Assert.Equal("Markup[*] Emphasis[https://x.com] Link[https://x.com] Markup[*]", Shape("*https://x.com*"));
    }
}
