using Noto.Core.Markdown;

namespace Noto.Core.Portability;

/// <summary>
/// The images a note's text refers to, as written (ADR-020 §5): every <c>![name](source)</c>'s source, where it is in
/// the text, so an import can copy each picture in and rewrite only the sources it must.
/// </summary>
public static class ImageSources
{
    /// <summary>Each image's source in <paramref name="content"/>, in order, with where it is.</summary>
    public static IReadOnlyList<(int Start, int Length, string Source)> In(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return [.. MarkdownDocument.Parse(content).Blocks
            .SelectMany(block => block.Spans
                .Where(s => s.Kind == MarkdownSpanKind.Image)
                .Select(image => block.Spans
                    .Where(s => s.Kind == MarkdownSpanKind.LinkTarget && s.Start > image.Start && s.End < image.End)
                    .Cast<MarkdownSpan?>()
                    .LastOrDefault()))
            .OfType<MarkdownSpan>()
            .Select(s => (s.Start, s.Length, content.Substring(s.Start, s.Length)))];
    }

    /// <summary>
    /// <paramref name="content"/> with each image source <paramref name="map"/> gives a new one for replaced, and
    /// everything else, line endings included, exactly as it was. A source mapped to itself or to
    /// <see langword="null"/> is left alone.
    /// </summary>
    public static string Rewrite(string content, Func<string, string?> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        var sources = In(content);
        if (sources.Count == 0)
        {
            return content;
        }

        var text = new System.Text.StringBuilder(content.Length);
        int at = 0;
        foreach ((int start, int length, string source) in sources)
        {
            text.Append(content, at, start - at).Append(map(source) is { } replaced && replaced != source ? replaced : source);
            at = start + length;
        }

        return text.Append(content, at, content.Length - at).ToString();
    }
}
