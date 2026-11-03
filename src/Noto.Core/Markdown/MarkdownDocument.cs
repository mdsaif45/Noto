namespace Noto.Core.Markdown;

/// <summary>
/// A note's markdown, described: its blocks and formatting spans, with offsets into the exact source parsed
/// (markdown-model.md; ADR-017 §3).
/// </summary>
/// <remarks>
/// <para>
/// Read-only by construction. There is no way to turn the model back into text, so parsing can never change the
/// source: the only change a note's text ever sees is the user's own edit (invariant I1).
/// </para>
/// <para>
/// Every string parses (I4); malformed or unfinished markup is text. The same string always gives the same blocks
/// and spans (I3). Inline markup never crosses a line; only a code fence carries state from line to line (I7).
/// </para>
/// </remarks>
public sealed class MarkdownDocument
{
    private MarkdownDocument(string source, List<MarkdownBlock> blocks)
    {
        Source = source;
        Blocks = blocks;
        Spans = [.. blocks.SelectMany(b => b.Spans)];
    }

    /// <summary>The source, the same instance that was parsed.</summary>
    public string Source { get; }

    /// <summary>The blocks, in source order, covering every line.</summary>
    public IReadOnlyList<MarkdownBlock> Blocks { get; }

    /// <summary>Every block's spans: by start, then longest first, then kind.</summary>
    public IReadOnlyList<MarkdownSpan> Spans { get; }

    /// <summary>
    /// Where the destination of the link at <paramref name="position"/> is written (the caret in its text, or just after
    /// it): its target span, or for a bare address the address itself. <see langword="null"/> when there is no link.
    /// </summary>
    public MarkdownSpan? LinkAt(int position)
    {
        foreach (MarkdownBlock block in Blocks.Where(b => b.Start <= position && position <= b.End))
        {
            foreach (MarkdownSpan link in block.Spans.Where(s => s.Kind == MarkdownSpanKind.Link && s.Start <= position && position <= s.End))
            {
                return block.Spans.Where(s => s.Kind == MarkdownSpanKind.LinkTarget && s.Start == link.End + 2).Cast<MarkdownSpan?>().FirstOrDefault() ?? link;
            }
        }

        return null;
    }

    /// <summary>Describes <paramref name="source"/>. Never throws for any string.</summary>
    public static MarkdownDocument Parse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var blocks = new List<MarkdownBlock>();
        (char Mark, int Length)? fence = null;
        int fenceStart = 0;
        List<MarkdownSpan> fenceSpans = [];
        int lineStart = 0;

        while (true)
        {
            int lineEnd = LineEnd(source, lineStart);

            if (fence is { } open)
            {
                if (IsClosingFence(source, lineStart, lineEnd, open))
                {
                    fenceSpans.Add(new MarkdownSpan(MarkdownSpanKind.Markup, lineStart, lineEnd - lineStart));
                    blocks.Add(Block(MarkdownBlockKind.CodeFence, fenceStart, lineEnd, fenceSpans));
                    fence = null;
                }
                else if (lineEnd > lineStart)
                {
                    fenceSpans.Add(new MarkdownSpan(MarkdownSpanKind.CodeBlock, lineStart, lineEnd - lineStart));
                }
            }
            else if (OpeningFence(source, lineStart, lineEnd) is { } opened)
            {
                fence = opened;
                fenceStart = lineStart;
                fenceSpans = [new MarkdownSpan(MarkdownSpanKind.Markup, lineStart, lineEnd - lineStart)];
            }
            else
            {
                blocks.Add(BlockParser.Line(source, lineStart, lineEnd));
            }

            if (lineEnd == source.Length)
            {
                // An unclosed fence runs to the end of the note (CommonMark).
                if (fence is not null)
                {
                    blocks.Add(Block(MarkdownBlockKind.CodeFence, fenceStart, lineEnd, fenceSpans));
                }

                break;
            }

            // CRLF is one break, not two.
            lineStart = source[lineEnd] == '\r' && lineEnd + 1 < source.Length && source[lineEnd + 1] == '\n' ? lineEnd + 2 : lineEnd + 1;
        }

        return new MarkdownDocument(source, blocks);
    }

    /// <summary>
    /// The range of <paramref name="after"/>'s source whose styling can differ from <paramref name="before"/>'s: what
    /// an editor restyles after an edit. Blocks that match are skipped from the front, and from the back aligned by
    /// the change in length; a block matches when its kind, attributes, text and spans are the same. Text counts even
    /// when the spans do not change: a replaced character takes its neighbour's format in the editor, so its block is
    /// restyled.
    /// </summary>
    /// <returns>The range in <paramref name="after"/>'s source; its length is 0 when nothing needs restyling.</returns>
    public static (int Start, int Length) ChangedRegion(MarkdownDocument before, MarkdownDocument after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        IReadOnlyList<MarkdownBlock> was = before.Blocks;
        IReadOnlyList<MarkdownBlock> now = after.Blocks;
        int shift = after.Source.Length - before.Source.Length;
        int shared = Math.Min(was.Count, now.Count);

        int front = 0;
        while (front < shared && Same(before, was[front], after, now[front], 0))
        {
            front++;
        }

        int back = 0;
        while (back < shared - front && Same(before, was[was.Count - 1 - back], after, now[now.Count - 1 - back], shift))
        {
            back++;
        }

        int last = now.Count - 1 - back;

        if (front > last)
        {
            return (front < now.Count ? now[front].Start : after.Source.Length, 0);
        }

        return (now[front].Start, now[last].End - now[front].Start);
    }

    private static bool Same(MarkdownDocument a, MarkdownBlock x, MarkdownDocument b, MarkdownBlock y, int shift)
    {
        if (x.Kind != y.Kind || x.Level != y.Level || x.Indent != y.Indent || x.IsChecked != y.IsChecked
            || x.Start + shift != y.Start || x.Length != y.Length || x.Spans.Count != y.Spans.Count
            || !a.Source.AsSpan(x.Start, x.Length).SequenceEqual(b.Source.AsSpan(y.Start, y.Length)))
        {
            return false;
        }

        for (int i = 0; i < x.Spans.Count; i++)
        {
            if (x.Spans[i] with { Start = x.Spans[i].Start + shift } != y.Spans[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The line's end: its first break character, or the end of the source.</summary>
    private static int LineEnd(string source, int start)
    {
        int end = source.AsSpan(start).IndexOfAny('\r', '\n');
        return end < 0 ? source.Length : start + end;
    }

    /// <summary>
    /// A fence's mark and length when the line opens one: at most three spaces, then three or more backticks with no
    /// backtick after them, or three or more tildes.
    /// </summary>
    private static (char Mark, int Length)? OpeningFence(string source, int start, int end)
    {
        int at = BlockParser.ShallowStart(source, start, end);

        if (at < 0 || at >= end || source[at] is not ('`' or '~'))
        {
            return null;
        }

        char mark = source[at];
        int run = BlockParser.Run(source, at, end, mark);

        if (run < 3 || (mark == '`' && source.AsSpan(at + run, end - at - run).Contains('`')))
        {
            return null;
        }

        return (mark, run);
    }

    /// <summary>The line closes <paramref name="fence"/>: at most three spaces, the same mark at least as long, then only blanks.</summary>
    private static bool IsClosingFence(string source, int start, int end, (char Mark, int Length) fence)
    {
        int at = BlockParser.ShallowStart(source, start, end);

        if (at < 0 || at >= end || source[at] != fence.Mark)
        {
            return false;
        }

        int run = BlockParser.Run(source, at, end, fence.Mark);
        return run >= fence.Length && source.AsSpan(at + run, end - at - run).IndexOfAnyExcept(' ', '\t') < 0;
    }

    private static MarkdownBlock Block(MarkdownBlockKind kind, int start, int end, List<MarkdownSpan> spans) =>
        new(kind, start, end - start, 0, 0, false, spans);
}
