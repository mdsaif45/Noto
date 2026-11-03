using Noto.Core.Editing;
using Noto.Core.Markdown;
using Xunit;

namespace Noto.Core.Tests.Editing;

/// <summary>Formatted paste: clipboard HTML as Noto's Markdown, and never anything that is not text.</summary>
public sealed class HtmlToMarkdownTests
{
    [Theory]
    [InlineData("<b>bold</b> and <strong>strong</strong>", "**bold** and **strong**")]
    [InlineData("<i>it</i> <em>em</em>", "*it* *em*")]
    [InlineData("<s>old</s> <del>gone</del> <u>under</u>", "~~old~~ ~~gone~~ ==under==")]
    [InlineData("run <code>ls -la</code>", "run `ls -la`")]
    [InlineData("<b>bold </b>next", "**bold** next")]
    [InlineData("a&amp;b &lt;tag&gt; &quot;q&quot; &#233; &#x263A;", "a&b <tag> \"q\" é ☺")]
    public void Inline_formatting_becomes_its_markers(string html, string markdown)
    {
        Assert.Equal(markdown, HtmlToMarkdown.Convert(html));
    }

    [Theory]
    [InlineData("<h1>Title</h1><p>Body</p>", "# Title\nBody")]
    [InlineData("<h3>Three</h3>", "### Three")]
    [InlineData("<p>one</p><p>two</p>", "one\ntwo")]
    [InlineData("line<br>break", "line\nbreak")]
    [InlineData("<p>a</p><hr><p>b</p>", "a\n---\nb")]
    [InlineData("<blockquote><p>quoted</p></blockquote>", "> quoted")]
    public void Blocks_become_lines(string html, string markdown)
    {
        Assert.Equal(markdown, HtmlToMarkdown.Convert(html));
    }

    [Theory]
    [InlineData("<ul><li>a</li><li>b</li></ul>", "- a\n- b")]
    [InlineData("<ol><li>a</li><li>b</li></ol>", "1. a\n2. b")]
    [InlineData("<ul><li>a<ul><li>nested</li></ul></li></ul>", "- a\n  - nested")]
    [InlineData("<ul><li><input type=\"checkbox\" checked> done</li><li><input type=\"checkbox\"> open</li></ul>", "- [x] done\n- [ ] open")]
    public void Lists_and_checklists_become_list_items(string html, string markdown)
    {
        Assert.Equal(markdown, HtmlToMarkdown.Convert(html));
    }

    [Fact]
    public void Preformatted_code_is_fenced_and_kept_exactly()
    {
        Assert.Equal("```\nif (a)\n    b();\n```", HtmlToMarkdown.Convert("<pre><code>if (a)\n    b();</code></pre>"));
    }

    [Theory]
    [InlineData("<a href=\"https://example.com/a\">site</a>", "[site](https://example.com/a)")]
    [InlineData("<a href='mailto:me@example.com'>mail</a>", "[mail](mailto:me@example.com)")]
    [InlineData("<a href=\"javascript:alert(1)\">click</a>", "click")]
    [InlineData("<a href=\"data:text/html,x\">data</a>", "data")]
    [InlineData("<a href=\"https://x.y/a b\">space</a>", "space")]
    [InlineData("<a href=\"https://x.y\"></a>after", "after")]
    [InlineData("<a href=\"file:///C:/Docs/a.pdf\">doc</a>", "[doc](file:///C:/Docs/a.pdf)")]
    [InlineData("<a href=\"file://server/share/a.pdf\">unc</a>", "unc")]
    [InlineData("<a href=\"ms-settings:privacy\">settings</a>", "settings")]
    public void Links_keep_safe_addresses_only(string html, string markdown)
    {
        Assert.Equal(markdown, HtmlToMarkdown.Convert(html));
    }

    [Theory]
    [InlineData("<script>alert('x')</script>text", "text")]
    [InlineData("<style>p{color:red}</style>text", "text")]
    [InlineData("<!-- comment -->text", "text")]
    [InlineData("a<!-- x > y <b> -->b", "ab")]
    [InlineData("<unknown>kept</unknown>", "kept")]
    [InlineData("<b>unclosed", "**unclosed")]
    [InlineData("stray </i> close", "stray close")]
    [InlineData("a < b", "a < b")]
    [InlineData("", "")]
    public void Malformed_and_unsafe_input_is_text_and_never_code(string html, string markdown)
    {
        Assert.Equal(markdown, HtmlToMarkdown.Convert(html));
    }

    [Fact]
    public void A_typical_browser_fragment_parses_as_the_formatting_it_had()
    {
        const string html = "<h2 style=\"margin:0\">Plan</h2><p>Ship the <b>beta</b> on <i>Friday</i>, see <a href=\"https://example.com/rfc\">the RFC</a>.</p><ul><li>fix startup</li><li>write notes</li></ul>";

        string markdown = HtmlToMarkdown.Convert(html);
        MarkdownDocument document = MarkdownDocument.Parse(markdown);

        Assert.Equal("## Plan\nShip the **beta** on *Friday*, see [the RFC](https://example.com/rfc).\n- fix startup\n- write notes", markdown);
        Assert.Contains(document.Spans, s => s.Kind == MarkdownSpanKind.Strong);
        Assert.Contains(document.Spans, s => s.Kind == MarkdownSpanKind.Link);
        Assert.Equal(2, document.Blocks.Count(b => b.Kind == MarkdownBlockKind.BulletItem));
    }

    [Fact]
    public void Random_tag_soup_never_throws()
    {
        var random = new Random(5);
        string[] pieces = ["<b>", "</b>", "<a href=\"https://x\">", "</a>", "<ul>", "<li>", "</ul>", "<pre>", "</pre>", "<br>", "text ", "&amp;", "<", ">", "<!--", "-->", "<h2>", "<input type=checkbox checked>", "\n", "  "];

        for (int i = 0; i < 2_000; i++)
        {
            string html = string.Concat(Enumerable.Range(0, random.Next(0, 30)).Select(_ => pieces[random.Next(pieces.Length)]));
            _ = HtmlToMarkdown.Convert(html);
        }
    }
}
