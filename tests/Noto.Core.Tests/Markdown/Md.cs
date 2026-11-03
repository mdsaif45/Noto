using Noto.Core.Markdown;
using Xunit;

namespace Noto.Core.Tests.Markdown;

/// <summary>Readable views of a parse, for assertions.</summary>
internal static class Md
{
    /// <summary>Every span as Kind[text], in document order; a heading's kind carries its level.</summary>
    public static string Shape(string source) => Shape(MarkdownDocument.Parse(source));

    public static string Shape(MarkdownDocument document) =>
        string.Join(" ", document.Spans.Select(s => $"{s.Kind}{(s.Level > 0 ? s.Level : string.Empty)}[{document.Source.Substring(s.Start, s.Length)}]"));

    /// <summary>The only block of a one-line source.</summary>
    public static MarkdownBlock Block(string source) => Assert.Single(MarkdownDocument.Parse(source).Blocks);

    /// <summary>The source with the edit made: what the editor holds after it.</summary>
    public static string Edit(string source, int start, int removed, string inserted) =>
        string.Concat(source.AsSpan(0, start), inserted, source.AsSpan(start + removed));
}
