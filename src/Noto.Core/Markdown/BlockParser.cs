namespace Noto.Core.Markdown;

/// <summary>One line that is not part of a code fence: which block it is, and its spans (markdown-model.md §2).</summary>
internal static class BlockParser
{
    /// <summary>The highest ATX heading level, as in CommonMark and <see cref="Notes.NoteTitle"/>.</summary>
    private const int MaxHeadingLevel = 6;

    /// <summary>The most digits an ordered list marker may have (CommonMark).</summary>
    private const int MaxOrderedDigits = 9;

    public static MarkdownBlock Line(string source, int start, int end)
    {
        var spans = new List<MarkdownSpan>();
        int first = SkipBlanks(source, start, end);

        if (first == end)
        {
            return Done(MarkdownBlockKind.Blank, start, end, spans);
        }

        int shallow = ShallowStart(source, start, end);

        if (shallow >= 0 && Separator(source, shallow, end) is { } marks)
        {
            spans.Add(new MarkdownSpan(MarkdownSpanKind.Separator, shallow, marks - shallow));
            return Done(MarkdownBlockKind.Separator, start, end, spans);
        }

        if (shallow >= 0 && source[shallow] == '#' && Heading(source, start, shallow, end, spans) is { } level)
        {
            return Done(MarkdownBlockKind.Heading, start, end, spans, level: level);
        }

        if (shallow >= 0 && source[shallow] == '>')
        {
            int text = shallow + 1 < end && source[shallow + 1] == ' ' ? shallow + 2 : shallow + 1;
            spans.Add(new MarkdownSpan(MarkdownSpanKind.Markup, start, text - start));
            Add(spans, MarkdownSpanKind.Quote, text, end);
            InlineParser.Parse(source, text, end, spans);
            return Done(MarkdownBlockKind.Quote, start, end, spans);
        }

        if (ListMarker(source, first, end) is { } marker)
        {
            return ListItem(source, start, first, end, marker, spans);
        }

        InlineParser.Parse(source, start, end, spans);
        return Done(MarkdownBlockKind.Paragraph, start, end, spans);
    }

    /// <summary>
    /// The first non-blank of a line indented by at most three spaces, or -1. A tab, or four spaces, is deeper: a
    /// heading, quote, fence or separator marker there is text (CommonMark's indented-code rule, as NoteTitle).
    /// </summary>
    public static int ShallowStart(string source, int start, int end)
    {
        int at = start;

        while (at < end && source[at] == ' ')
        {
            at++;
        }

        return at - start <= 3 && (at == end || source[at] != '\t') ? at : -1;
    }

    /// <summary>The length of the run of <paramref name="mark"/> starting at <paramref name="at"/>.</summary>
    public static int Run(string source, int at, int end, char mark)
    {
        int i = at;

        while (i < end && source[i] == mark)
        {
            i++;
        }

        return i - at;
    }

    /// <summary>The end of a separator's marks when the line is one: three or more of one of - * _, blanks between, nothing else.</summary>
    private static int? Separator(string source, int at, int end)
    {
        char mark = source[at];

        if (mark is not ('-' or '*' or '_'))
        {
            return null;
        }

        int count = 0;
        int last = at;

        for (int i = at; i < end; i++)
        {
            if (source[i] == mark)
            {
                count++;
                last = i;
            }
            else if (source[i] is not (' ' or '\t'))
            {
                return null;
            }
        }

        return count >= 3 ? last + 1 : null;
    }

    /// <summary>The level when the line is an ATX heading, adding its markup and text spans.</summary>
    private static int? Heading(string source, int start, int at, int end, List<MarkdownSpan> spans)
    {
        int level = Run(source, at, end, '#');
        int after = at + level;

        if (level > MaxHeadingLevel || (after < end && source[after] is not (' ' or '\t')))
        {
            return null;
        }

        int text = SkipBlanks(source, after, end);
        int textEnd = TrimEnd(source, text, end);

        // An optional closing run is decoration only when blanks precede it ("# C#" keeps its '#').
        int closing = textEnd;
        while (closing > text && source[closing - 1] == '#')
        {
            closing--;
        }

        // The closing run, with the blanks before it, is markup up to markupEnd.
        int markupEnd = textEnd;
        if (closing == text)
        {
            // Nothing but hashes: an empty heading, all markup.
            textEnd = text;
        }
        else if (closing < textEnd && source[closing - 1] is ' ' or '\t')
        {
            textEnd = TrimEnd(source, text, closing);
        }

        spans.Add(new MarkdownSpan(MarkdownSpanKind.Markup, start, text - start));

        if (textEnd > text)
        {
            spans.Add(new MarkdownSpan(MarkdownSpanKind.Heading, text, textEnd - text, level));
            InlineParser.Parse(source, text, textEnd, spans);
        }

        Add(spans, MarkdownSpanKind.Markup, textEnd, markupEnd);
        return level;
    }

    /// <summary>The end of a list marker and the blanks after it, when the line starts with one.</summary>
    private static int? ListMarker(string source, int at, int end)
    {
        int after;

        if (source[at] is '-' or '*' or '+')
        {
            after = at + 1;
        }
        else
        {
            int digits = 0;
            while (at + digits < end && char.IsAsciiDigit(source[at + digits]))
            {
                digits++;
            }

            if (digits is 0 or > MaxOrderedDigits || at + digits >= end || source[at + digits] is not ('.' or ')'))
            {
                return null;
            }

            after = at + digits + 1;
        }

        if (after < end && source[after] is not (' ' or '\t'))
        {
            return null;
        }

        return SkipBlanks(source, after, end);
    }

    private static MarkdownBlock ListItem(string source, int start, int first, int end, int markerEnd, List<MarkdownSpan> spans)
    {
        var kind = source[first] is '-' or '*' or '+' ? MarkdownBlockKind.BulletItem : MarkdownBlockKind.OrderedItem;
        spans.Add(new MarkdownSpan(MarkdownSpanKind.ListMarker, first, markerEnd - first));
        int indent = Columns(source, start, first);

        bool box = markerEnd + 2 < end && source[markerEnd] == '[' && source[markerEnd + 1] is ' ' or 'x' or 'X' && source[markerEnd + 2] == ']'
            && (markerEnd + 3 == end || source[markerEnd + 3] is ' ' or '\t');

        if (!box)
        {
            InlineParser.Parse(source, markerEnd, end, spans);
            return Done(kind, start, end, spans, indent: indent);
        }

        bool done = source[markerEnd + 1] != ' ';
        spans.Add(new MarkdownSpan(MarkdownSpanKind.TaskBox, markerEnd, 3));
        int text = SkipBlanks(source, markerEnd + 3, end);

        if (done)
        {
            Add(spans, MarkdownSpanKind.TaskDone, text, end);
        }

        InlineParser.Parse(source, markerEnd + 3, end, spans);
        return Done(MarkdownBlockKind.TaskItem, start, end, spans, indent: indent, isChecked: done);
    }

    private static MarkdownBlock Done(
        MarkdownBlockKind kind, int start, int end, List<MarkdownSpan> spans, int level = 0, int indent = 0, bool isChecked = false)
    {
        spans.Sort(static (a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.Length != b.Length ? b.Length.CompareTo(a.Length) : a.Kind.CompareTo(b.Kind));
        return new MarkdownBlock(kind, start, end - start, level, indent, isChecked, spans);
    }

    private static void Add(List<MarkdownSpan> spans, MarkdownSpanKind kind, int start, int end)
    {
        if (end > start)
        {
            spans.Add(new MarkdownSpan(kind, start, end - start));
        }
    }

    private static int SkipBlanks(string source, int at, int end)
    {
        while (at < end && source[at] is ' ' or '\t')
        {
            at++;
        }

        return at;
    }

    private static int TrimEnd(string source, int start, int end)
    {
        while (end > start && source[end - 1] is ' ' or '\t')
        {
            end--;
        }

        return end;
    }

    /// <summary>Leading whitespace in columns; a tab reaches the next multiple of four.</summary>
    private static int Columns(string source, int start, int end)
    {
        int columns = 0;

        for (int i = start; i < end; i++)
        {
            columns = source[i] == '\t' ? (columns / 4 + 1) * 4 : columns + 1;
        }

        return columns;
    }
}
