using Xunit;
using static Noto.Core.Tests.Markdown.Md;

namespace Noto.Core.Tests.Markdown;

/// <summary>Images (ADR-019 §4): one Image span over the whole construct, its markers Markup and its source a LinkTarget.</summary>
public sealed class MarkdownImageTests
{
    [Theory]
    [InlineData("![alt](src)", "Image[![alt](src)] Markup[![] Markup[](] LinkTarget[src] Markup[)]")]
    [InlineData("![](attachments/a.png)", "Image[![](attachments/a.png)] Markup[![] Markup[](] LinkTarget[attachments/a.png] Markup[)]")]
    [InlineData("see ![a *b*](x.png) now", "Image[![a *b*](x.png)] Markup[![] Markup[*] Emphasis[b] Markup[*] Markup[](] LinkTarget[x.png] Markup[)]")]
    public void An_image_is_its_markers_its_alt_text_and_its_source(string source, string shape)
    {
        Assert.Equal(shape, Shape(source));
    }

    [Theory]
    [InlineData("![alt](src with space)")]
    [InlineData("![alt](unclosed")]
    [InlineData("![alt] (src)")]
    [InlineData("![alt]()")]
    [InlineData("!alt](src)")]
    public void Not_an_image_is_text(string source)
    {
        Assert.Equal(string.Empty, Shape(source));
    }

    [Fact]
    public void A_link_may_hold_an_image_and_an_address_in_an_image_is_not_a_link()
    {
        Assert.Equal(
            "Markup[[] Link[![i](a.png)] Image[![i](a.png)] Markup[![] Markup[](] LinkTarget[a.png] Markup[)] Markup[](] LinkTarget[https://x.com] Markup[)]",
            Shape("[![i](a.png)](https://x.com)"));
        Assert.Equal("Image[![https://x.com](a.png)] Markup[![] Markup[](] LinkTarget[a.png] Markup[)]", Shape("![https://x.com](a.png)"));
        Assert.Equal("Image[![see https://x.com](a.png)] Markup[![] Markup[](] LinkTarget[a.png] Markup[)]", Shape("![see https://x.com](a.png)"));
    }
}
