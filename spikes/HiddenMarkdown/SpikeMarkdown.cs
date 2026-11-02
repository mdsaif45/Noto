using System.Text.RegularExpressions;

namespace Noto.Spike.HiddenMarkdown;

internal enum SpanKind
{
    Markup,
    Strong,
    Emphasis,
    Strike,
    Underline,
    Mark,
    Code,
    CodeBlock,
    Link,
    Heading,
    Quote,
    ListMarker,
    TaskMarker,
    TaskDone,
    Rule,
}

internal readonly record struct Span(int Start, int Length, SpanKind Kind, int Level = 0)
{
    public int End => Start + Length;
}

/// <summary>
/// Spike-grade parser for SideNotes' markdown subset: line-based blocks, regex inlines, absolute offsets into the
/// LF-normalised source. Good enough to produce realistic span counts for the measurements; P2.2 writes the real one.
/// </summary>
internal static class SpikeMarkdown
{
    private static readonly Regex Heading = new(@"^(#{1,5}) +", RegexOptions.Compiled);
    private static readonly Regex Quote = new(@"^> ?", RegexOptions.Compiled);
    private static readonly Regex Task = new(@"^(\s*[-*] \[([ xX])\] )", RegexOptions.Compiled);
    private static readonly Regex List = new(@"^(\s*(?:[-*+]|\d+[.)]) +)", RegexOptions.Compiled);
    private static readonly Regex Rule = new(@"^(?:-{3,}|\*{3,}|_{3,})\s*$", RegexOptions.Compiled);
    private static readonly Regex Fence = new(@"^```", RegexOptions.Compiled);

    private static readonly Regex Code = new(@"`[^`]+`", RegexOptions.Compiled);
    private static readonly Regex Link = new(@"\[([^\]]+)\]\(([^)\s]+)\)", RegexOptions.Compiled);

    private static readonly (Regex Pattern, int Delimiter, SpanKind Kind)[] Paired =
    [
        (new(@"\*\*(?=\S)(.+?)(?<=\S)\*\*", RegexOptions.Compiled), 2, SpanKind.Strong),
        (new(@"~~(?=\S)(.+?)(?<=\S)~~", RegexOptions.Compiled), 2, SpanKind.Strike),
        (new(@"==(?=\S)(.+?)(?<=\S)==", RegexOptions.Compiled), 2, SpanKind.Underline),
        (new(@"::(?=\S)(.+?)(?<=\S)::", RegexOptions.Compiled), 2, SpanKind.Mark),
        (new(@"(?<![*\w])[*_](?=[^\s*_])(.+?)(?<=[^\s*_])[*_](?![*\w])", RegexOptions.Compiled), 1, SpanKind.Emphasis),
    ];

    public static List<Span> Parse(string text)
    {
        var spans = new List<Span>();
        bool inFence = false;
        int start = 0;

        while (start <= text.Length)
        {
            int end = text.IndexOf('\n', start);
            if (end < 0)
            {
                end = text.Length;
            }

            ParseLine(text, start, end, ref inFence, spans);
            start = end + 1;
        }

        return spans;
    }

    /// <summary>One line. <paramref name="inFence"/> carries the only cross-line state this subset has.</summary>
    public static void ParseLine(string text, int start, int end, ref bool inFence, List<Span> spans)
    {
        string line = text[start..end];

        if (Fence.IsMatch(line))
        {
            spans.Add(new Span(start, line.Length, SpanKind.Markup));
            inFence = !inFence;
            return;
        }

        if (inFence)
        {
            if (line.Length > 0)
            {
                spans.Add(new Span(start, line.Length, SpanKind.CodeBlock));
            }

            return;
        }

        if (Rule.IsMatch(line))
        {
            spans.Add(new Span(start, line.Length, SpanKind.Rule));
            return;
        }

        int body = 0;
        Match m;

        if ((m = Heading.Match(line)).Success)
        {
            spans.Add(new Span(start, m.Length, SpanKind.Markup));
            spans.Add(new Span(start + m.Length, line.Length - m.Length, SpanKind.Heading, m.Groups[1].Length));
            body = m.Length;
        }
        else if ((m = Quote.Match(line)).Success)
        {
            spans.Add(new Span(start, m.Length, SpanKind.Markup));
            spans.Add(new Span(start + m.Length, line.Length - m.Length, SpanKind.Quote));
            body = m.Length;
        }
        else if ((m = Task.Match(line)).Success)
        {
            spans.Add(new Span(start, m.Length, SpanKind.TaskMarker));
            if (m.Groups[2].Value is "x" or "X")
            {
                spans.Add(new Span(start + m.Length, line.Length - m.Length, SpanKind.TaskDone));
            }

            body = m.Length;
        }
        else if ((m = List.Match(line)).Success)
        {
            spans.Add(new Span(start, m.Length, SpanKind.ListMarker));
            body = m.Length;
        }

        Inline(line, body, start, spans);
    }

    private static void Inline(string line, int from, int offset, List<Span> spans)
    {
        char[] masked = line.ToCharArray();
        for (int i = 0; i < from; i++)
        {
            masked[i] = '\0';
        }

        foreach (Match c in Code.Matches(line, from))
        {
            spans.Add(new Span(offset + c.Index, 1, SpanKind.Markup));
            spans.Add(new Span(offset + c.Index + 1, c.Length - 2, SpanKind.Code));
            spans.Add(new Span(offset + c.Index + c.Length - 1, 1, SpanKind.Markup));
            Array.Fill(masked, '\0', c.Index, c.Length);
        }

        foreach (Match l in Link.Matches(new string(masked)))
        {
            Group label = l.Groups[1];
            spans.Add(new Span(offset + l.Index, 1, SpanKind.Markup));
            spans.Add(new Span(offset + label.Index, label.Length, SpanKind.Link));
            int tail = label.Index + label.Length;
            spans.Add(new Span(offset + tail, l.Index + l.Length - tail, SpanKind.Markup));
            Array.Fill(masked, '\0', l.Index, 1);
            Array.Fill(masked, '\0', tail, l.Index + l.Length - tail);
        }

        foreach ((Regex pattern, int d, SpanKind kind) in Paired)
        {
            foreach (Match p in pattern.Matches(new string(masked)))
            {
                spans.Add(new Span(offset + p.Index, d, SpanKind.Markup));
                spans.Add(new Span(offset + p.Index + d, p.Length - 2 * d, kind));
                spans.Add(new Span(offset + p.Index + p.Length - d, d, SpanKind.Markup));
                Array.Fill(masked, '\0', p.Index, d);
                Array.Fill(masked, '\0', p.Index + p.Length - d, d);
            }
        }
    }

    /// <summary>The source with every Markup span removed: what a reader should see and hear.</summary>
    public static string Visible(string text, List<Span> spans)
    {
        var hidden = new bool[text.Length];
        foreach (Span s in spans.Where(s => s.Kind == SpanKind.Markup))
        {
            Array.Fill(hidden, true, s.Start, s.Length);
        }

        return new string(text.Where((_, i) => !hidden[i]).ToArray());
    }

    public static string NormaliseLineEnds(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');
}
