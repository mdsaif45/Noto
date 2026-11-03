using System.Text;

namespace Noto.Core.Markdown;

/// <summary>
/// One line's inline markup (markdown-model.md §2): escapes, code spans, links, bare web addresses (ADR-019 §3), and
/// the emphasis family through CommonMark's delimiter-run algorithm. Nothing crosses the line; unmatched syntax is text.
/// </summary>
internal static class InlineParser
{
    /// <summary>What starts a bare web address (ADR-019 §3).</summary>
    private static readonly string[] AutolinkPrefixes = ["https://", "http://", "www."];

    private const string AsciiPunctuation = "!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~";

    public static void Parse(string source, int start, int end, List<MarkdownSpan> spans)
    {
        int first = spans.Count;
        var delimiters = new List<Delimiter>();
        var brackets = new List<Bracket>();
        int i = start;

        while (i < end)
        {
            char c = source[i];

            if (c == '\\' && i + 1 < end && AsciiPunctuation.Contains(source[i + 1], StringComparison.Ordinal))
            {
                spans.Add(new MarkdownSpan(MarkdownSpanKind.Markup, i, 1));
                i += 2;
            }
            else if (c == '`')
            {
                i = CodeSpan(source, i, end, spans);
            }
            else if (c == '!' && i + 1 < end && source[i + 1] == '[')
            {
                // Image syntax is P3's: the bracket opens nothing, but still closes its own ']'.
                brackets.Add(new Bracket(i + 1, delimiters.Count, IsImage: true));
                i += 2;
            }
            else if (c == '[')
            {
                brackets.Add(new Bracket(i, delimiters.Count, IsImage: false));
                i++;
            }
            else if (c == ']')
            {
                i = CloseBracket(source, i, end, brackets, delimiters, spans);
            }
            else if (c is '*' or '_' or '~' or '=')
            {
                i = DelimiterRun(source, start, i, end, delimiters);
            }
            else
            {
                i++;
            }
        }

        Emphasis(delimiters, 0, spans);
        Autolinks(source, start, end, spans, first);
    }

    /// <summary>
    /// Bare web addresses (GFM's extended autolinks): "http://", "https://" or "www." at the start of a word, through
    /// the next white space, without the punctuation that ends a sentence or an unbalanced ')'. Never inside code, a
    /// link or markup. The address is its own destination: a <see cref="MarkdownSpanKind.Link"/> with no markup.
    /// </summary>
    private static void Autolinks(string source, int start, int end, List<MarkdownSpan> spans, int first)
    {
        var taken = new bool[end - start];
        for (int k = first; k < spans.Count; k++)
        {
            if (spans[k].Kind is MarkdownSpanKind.Code or MarkdownSpanKind.Link or MarkdownSpanKind.LinkTarget or MarkdownSpanKind.Markup)
            {
                for (int p = spans[k].Start; p < spans[k].End; p++)
                {
                    taken[p - start] = true;
                }
            }
        }

        int i = start;
        while (i < end)
        {
            bool wordStart = i == start || char.IsWhiteSpace(source[i - 1]) || source[i - 1] is '*' or '_' or '~' or '(';
            int prefix = wordStart && !taken[i - start] ? AutolinkPrefix(source, i, end) : 0;

            if (prefix == 0)
            {
                i++;
                continue;
            }

            int stop = i + prefix;
            while (stop < end && !char.IsWhiteSpace(source[stop]) && source[stop] != '<' && !taken[stop - start])
            {
                stop++;
            }

            stop = TrimAutolink(source, i + prefix, stop);
            if (stop > i + prefix && char.IsLetterOrDigit(source[i + prefix]))
            {
                spans.Add(new MarkdownSpan(MarkdownSpanKind.Link, i, stop - i));
            }

            i = Math.Max(stop, i + 1);
        }
    }

    private static int AutolinkPrefix(string source, int at, int end)
    {
        foreach (string prefix in AutolinkPrefixes)
        {
            if (end - at > prefix.Length && source.AsSpan(at, prefix.Length).Equals(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return prefix.Length;
            }
        }

        return 0;
    }

    /// <summary>GFM's trailing rule: ?!.,:*_~ end a sentence, not an address; a ')' without its '(' is the text's.</summary>
    private static int TrimAutolink(string source, int from, int stop)
    {
        while (stop > from)
        {
            char last = source[stop - 1];
            if (last is '?' or '!' or '.' or ',' or ':' or '*' or '_' or '~' or ';' or '"' or '\'')
            {
                stop--;
            }
            else if (last == ')' && source.AsSpan(from, stop - from).Count('(') < source.AsSpan(from, stop - from).Count(')'))
            {
                stop--;
            }
            else
            {
                break;
            }
        }

        return stop;
    }

    /// <summary>A run of n backticks closed by the next run of exactly n on the line; otherwise the run is text.</summary>
    private static int CodeSpan(string source, int at, int end, List<MarkdownSpan> spans)
    {
        int length = BlockParser.Run(source, at, end, '`');
        int search = at + length;

        while (search < end)
        {
            int next = source.AsSpan(search, end - search).IndexOf('`');

            if (next < 0)
            {
                break;
            }

            int close = search + next;
            int run = BlockParser.Run(source, close, end, '`');

            if (run == length)
            {
                spans.Add(new MarkdownSpan(MarkdownSpanKind.Markup, at, length));
                spans.Add(new MarkdownSpan(MarkdownSpanKind.Code, at + length, close - at - length));
                spans.Add(new MarkdownSpan(MarkdownSpanKind.Markup, close, length));
                return close + length;
            }

            search = close + run;
        }

        return at + length;
    }

    /// <summary>
    /// A ']' makes a link with the nearest open '[' when "(destination)" follows and the text is not empty; otherwise
    /// that '[' and this ']' are text. A link's delimiters pair only inside it, and no link is inside another.
    /// </summary>
    private static int CloseBracket(string source, int at, int end, List<Bracket> brackets, List<Delimiter> delimiters, List<MarkdownSpan> spans)
    {
        if (brackets.Count == 0)
        {
            return at + 1;
        }

        Bracket open = brackets[^1];
        brackets.RemoveAt(brackets.Count - 1);

        if (open.IsImage || !open.IsActive || at == open.At + 1 || Destination(source, at + 1, end) is not { } target)
        {
            return at + 1;
        }

        spans.Add(new MarkdownSpan(MarkdownSpanKind.Markup, open.At, 1));
        spans.Add(new MarkdownSpan(MarkdownSpanKind.Link, open.At + 1, at - open.At - 1));
        spans.Add(new MarkdownSpan(MarkdownSpanKind.Markup, at, 2));
        spans.Add(new MarkdownSpan(MarkdownSpanKind.LinkTarget, at + 2, target - at - 2));
        spans.Add(new MarkdownSpan(MarkdownSpanKind.Markup, target, 1));

        Emphasis(delimiters, open.Delimiters, spans);
        delimiters.RemoveRange(open.Delimiters, delimiters.Count - open.Delimiters);

        for (int k = 0; k < brackets.Count; k++)
        {
            brackets[k] = brackets[k] with { IsActive = false };
        }

        return target + 1;
    }

    /// <summary>
    /// The position of the ')' closing "(destination)" at <paramref name="at"/>: no whitespace, balanced parentheses,
    /// escapes allowed, not empty. <see langword="null"/> when there is none on the line.
    /// </summary>
    private static int? Destination(string source, int at, int end)
    {
        if (at >= end || source[at] != '(')
        {
            return null;
        }

        int depth = 0;

        for (int i = at + 1; i < end; i++)
        {
            char c = source[i];

            if (c == '\\' && i + 1 < end && AsciiPunctuation.Contains(source[i + 1], StringComparison.Ordinal))
            {
                i++;
            }
            else if (char.IsWhiteSpace(c))
            {
                return null;
            }
            else if (c == '(')
            {
                depth++;
            }
            else if (c == ')')
            {
                if (depth == 0)
                {
                    return i > at + 1 ? i : null;
                }

                depth--;
            }
        }

        return null;
    }

    /// <summary>
    /// Registers a run of * _ ~ = that can open or close (CommonMark's flanking rules). ~ and = count only as runs of
    /// exactly two; any other run of them is text.
    /// </summary>
    private static int DelimiterRun(string source, int start, int at, int end, List<Delimiter> delimiters)
    {
        char mark = source[at];
        int length = BlockParser.Run(source, at, end, mark);

        if (mark is '~' or '=' && length != 2)
        {
            return at + length;
        }

        // The line's ends count as whitespace. Whole code points, so an emoji or a CJK character beside a run is
        // classified as itself; a lone surrogate decodes as U+FFFD, a symbol.
        Rune before = new(' ');
        Rune after = new(' ');

        if (at > start)
        {
            _ = Rune.DecodeLastFromUtf16(source.AsSpan(start, at - start), out before, out _);
        }

        if (at + length < end)
        {
            _ = Rune.DecodeFromUtf16(source.AsSpan(at + length, end - at - length), out after, out _);
        }

        bool beforeSpace = Rune.IsWhiteSpace(before);
        bool afterSpace = Rune.IsWhiteSpace(after);
        bool beforePunct = IsPunctuation(before);
        bool afterPunct = IsPunctuation(after);
        bool left = !afterSpace && (!afterPunct || beforeSpace || beforePunct);
        bool right = !beforeSpace && (!beforePunct || afterSpace || afterPunct);

        bool canOpen = mark == '_' ? left && (!right || beforePunct) : left;
        bool canClose = mark == '_' ? right && (!left || afterPunct) : right;

        if (canOpen || canClose)
        {
            delimiters.Add(new Delimiter(mark, at, length, canOpen, canClose, delimiters.Count - 1));
        }

        return at + length;
    }

    /// <summary>
    /// CommonMark's "process emphasis" over the delimiters from <paramref name="bottom"/> on: each closer takes the
    /// nearest opener of its mark, both runs give up one character (two when both have two), and every delimiter
    /// between them becomes text.
    /// </summary>
    private static void Emphasis(List<Delimiter> delimiters, int bottom, List<MarkdownSpan> spans)
    {
        // Where a failed search for a closer of this mark (and kind, for * and _) stopped: no later search of the
        // same kind can succeed below it, which keeps a long line of unmatched marks linear.
        Dictionary<int, int>? floor = null;

        for (int c = bottom; c < delimiters.Count; c++)
        {
            Delimiter closer = delimiters[c];

            if (!closer.CanClose || closer.Remaining == 0)
            {
                continue;
            }

            int key = closer.Mark * 8 + (closer.CanOpen ? 4 : 0) + closer.Length % 3;
            int lowest = floor is not null && floor.TryGetValue(key, out int f) ? f : bottom - 1;
            int o = closer.Previous;

            while (o > lowest)
            {
                Delimiter opener = delimiters[o];

                if (opener.Mark == closer.Mark && opener.CanOpen && opener.Remaining > 0 && !RuleOfThree(opener, closer))
                {
                    break;
                }

                o = opener.Previous;
            }

            if (o <= lowest)
            {
                (floor ??= [])[key] = c - 1;

                if (!closer.CanOpen)
                {
                    delimiters[c] = closer with { Remaining = 0 };
                }

                continue;
            }

            Delimiter matched = delimiters[o];
            int use = closer.Mark is '~' or '=' || (matched.Remaining >= 2 && closer.Remaining >= 2) ? 2 : 1;
            int open = matched.At + matched.Remaining - use;

            spans.Add(new MarkdownSpan(MarkdownSpanKind.Markup, open, use));
            spans.Add(new MarkdownSpan(MarkdownSpanKind.Markup, closer.At, use));

            if (closer.At > open + use)
            {
                spans.Add(new MarkdownSpan(Kind(closer.Mark, use), open + use, closer.At - open - use));
            }

            delimiters[o] = matched with { Remaining = matched.Remaining - use };
            delimiters[c] = closer with { At = closer.At + use, Remaining = closer.Remaining - use, Previous = o };

            // Whatever is left of the closer may close again.
            if (closer.Remaining - use > 0)
            {
                c--;
            }
        }
    }

    /// <summary>
    /// CommonMark's rule of three: when either run could both open and close, a pair whose lengths sum to a multiple
    /// of three does not match unless both are multiples of three.
    /// </summary>
    private static bool RuleOfThree(Delimiter opener, Delimiter closer) =>
        opener.Mark is '*' or '_'
        && (opener.CanClose || closer.CanOpen)
        && (opener.Length + closer.Length) % 3 == 0
        && !(opener.Length % 3 == 0 && closer.Length % 3 == 0);

    private static MarkdownSpanKind Kind(char mark, int use) => mark switch
    {
        '~' => MarkdownSpanKind.Strikethrough,
        '=' => MarkdownSpanKind.Underline,
        _ => use == 2 ? MarkdownSpanKind.Strong : MarkdownSpanKind.Emphasis,
    };

    /// <summary>Unicode punctuation as CommonMark counts it: the P and S categories.</summary>
    private static bool IsPunctuation(Rune rune) => Rune.IsPunctuation(rune) || Rune.IsSymbol(rune);

    /// <summary>A run of one mark. <see cref="Length"/> is the run's original length, for the rule of three.</summary>
    private readonly record struct Delimiter(char Mark, int At, int Length, bool CanOpen, bool CanClose, int Previous)
    {
        public int Remaining { get; init; } = Length;
    }

    private readonly record struct Bracket(int At, int Delimiters, bool IsImage)
    {
        public bool IsActive { get; init; } = true;
    }
}
