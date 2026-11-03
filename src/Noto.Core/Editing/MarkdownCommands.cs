using System.Globalization;
using System.Text;
using Noto.Core.Markdown;

namespace Noto.Core.Editing;

/// <summary>
/// The editor's formatting commands as pure transforms (ADR-017 §3): <c>(text, selection) → TextEdit?</c>, where
/// <see langword="null"/> means the command does not apply and nothing happens. Each reads the note through
/// <see cref="MarkdownDocument"/>, so "is this already bold" is the parser's answer, never a guess.
/// </summary>
public static class MarkdownCommands
{
    public static TextEdit? ToggleStrong(string text, TextSelection selection) => ToggleInline(text, selection, "**", MarkdownSpanKind.Strong);

    public static TextEdit? ToggleEmphasis(string text, TextSelection selection) => ToggleInline(text, selection, "*", MarkdownSpanKind.Emphasis);

    public static TextEdit? ToggleStrikethrough(string text, TextSelection selection) => ToggleInline(text, selection, "~~", MarkdownSpanKind.Strikethrough);

    public static TextEdit? ToggleUnderline(string text, TextSelection selection) => ToggleInline(text, selection, "==", MarkdownSpanKind.Underline);

    public static TextEdit? ToggleCode(string text, TextSelection selection) => ToggleInline(text, selection, "`", MarkdownSpanKind.Code);

    /// <summary>
    /// Wraps the selection, or the word at the caret, in <paramref name="marker"/>; inside such a span already, removes
    /// its markers instead. A caret outside any word inserts the pair with the caret between. Each selected line is
    /// wrapped on its own, since inline markup never crosses a line.
    /// </summary>
    private static TextEdit? ToggleInline(string text, TextSelection selection, string marker, MarkdownSpanKind kind)
    {
        ArgumentNullException.ThrowIfNull(text);
        int m = marker.Length;

        MarkdownSpan? existing = MarkdownDocument.Parse(text).Spans
            .Where(s => s.Kind == kind && selection.Start >= s.Start - m && selection.End <= s.End + m)
            .Select(s => (MarkdownSpan?)s)
            .LastOrDefault();

        if (existing is { } span)
        {
            int start = span.Start - m;
            int Shifted(int at) => Math.Clamp(at, span.Start, span.End) - m;
            return new TextEdit(start, span.Length + 2 * m, text.Substring(span.Start, span.Length), new TextSelection(Shifted(selection.Anchor), Shifted(selection.Active)));
        }

        (int from, int to) = selection.IsCaret ? Word(text, selection.Start) : (selection.Start, selection.End);

        if (from == to)
        {
            return new TextEdit(from, 0, marker + marker, TextSelection.Caret(from + m));
        }

        var wrapped = new StringBuilder();
        int lineStart = from;
        int lines = 0;
        int firstText = 0;
        int firstLength = 0;

        while (true)
        {
            int lineEnd = text.AsSpan(lineStart, to - lineStart).IndexOfAny('\r', '\n') is var b and >= 0 ? lineStart + b : to;
            ReadOnlySpan<char> line = text.AsSpan(lineStart, lineEnd - lineStart);
            ReadOnlySpan<char> core = line.Trim();

            if (core.IsEmpty)
            {
                wrapped.Append(line);
            }
            else
            {
                int lead = line.Length - line.TrimStart().Length;
                firstText = lines == 0 ? from + lead + m : firstText;
                firstLength = lines == 0 ? core.Length : firstLength;
                wrapped.Append(line[..lead]).Append(marker).Append(core).Append(marker).Append(line[(lead + core.Length)..]);
            }

            if (lineEnd == to)
            {
                break;
            }

            lines++;
            wrapped.Append(text[lineEnd]);
            lineStart = lineEnd + 1;
        }

        // One line: the caret keeps its place in the word, a selection stays on the text it chose (the markers are
        // hidden around it). Several lines: everything that was rewritten.
        string replacement = wrapped.ToString();
        TextSelection after = lines > 0
            ? TextSelection.Range(from, from + replacement.Length)
            : selection.IsCaret ? TextSelection.Caret(selection.Start + m) : TextSelection.Range(firstText, firstText + firstLength);

        return new TextEdit(from, to - from, replacement, after);
    }

    /// <summary>
    /// Cycles the caret's line, and every selected line, through heading levels 1, 2, 3 and back to text. The first
    /// line decides the next level for all of them.
    /// </summary>
    public static TextEdit? CycleHeading(string text, TextSelection selection) =>
        Lines(text, selection, (doc, blocks) =>
        {
            int level = blocks[0].Kind == MarkdownBlockKind.Heading ? blocks[0].Level : 0;
            int next = level is 0 or >= 3 ? (level == 0 ? 1 : 0) : level + 1;
            return blocks.Select(b => (b, next == 0 ? string.Empty : new string('#', next) + " ")).ToList();
        });

    /// <summary>Makes the lines bullet items, or plain text when every one already is.</summary>
    public static TextEdit? ToggleBullets(string text, TextSelection selection) =>
        ToggleList(text, selection, MarkdownBlockKind.BulletItem, _ => "- ");

    /// <summary>Makes the lines a numbered list from 1, or plain text when every one already is a numbered item.</summary>
    public static TextEdit? ToggleNumbers(string text, TextSelection selection) =>
        ToggleList(text, selection, MarkdownBlockKind.OrderedItem, n => (n + 1).ToString(CultureInfo.InvariantCulture) + ". ");

    /// <summary>Makes the lines open tasks, or plain text when every one already is a task.</summary>
    public static TextEdit? ToggleTasks(string text, TextSelection selection) =>
        ToggleList(text, selection, MarkdownBlockKind.TaskItem, _ => "- [ ] ");

    private static TextEdit? ToggleList(string text, TextSelection selection, MarkdownBlockKind kind, Func<int, string> marker) =>
        Lines(text, selection, (doc, blocks) =>
        {
            var content = blocks.Where(b => b.Kind != MarkdownBlockKind.Blank).ToList();
            bool off = content.Count > 0 && content.All(b => b.Kind == kind);
            int n = 0;
            return content.Select(b => (b, off ? string.Empty : marker(n++))).ToList();
        });

    /// <summary>Checks the unchecked tasks among the lines, or unchecks them all when every one is checked.</summary>
    public static TextEdit? ToggleTaskDone(string text, TextSelection selection)
    {
        ArgumentNullException.ThrowIfNull(text);
        MarkdownDocument doc = MarkdownDocument.Parse(text);
        var tasks = BlocksIn(doc, selection).Where(b => b.Kind == MarkdownBlockKind.TaskItem).ToList();

        if (tasks.Count == 0)
        {
            return null;
        }

        bool check = tasks.Any(t => !t.IsChecked);
        char[] result = text.ToCharArray();

        foreach (MarkdownBlock task in tasks)
        {
            MarkdownSpan box = task.Spans.First(s => s.Kind == MarkdownSpanKind.TaskBox);
            result[box.Start + 1] = check ? 'x' : ' ';
        }

        int first = tasks[0].Start;
        int last = tasks[^1].End;
        return new TextEdit(first, last - first, new string(result, first, last - first), selection);
    }

    /// <summary>Puts the lines between ``` fences.</summary>
    public static TextEdit? WrapInCodeBlock(string text, TextSelection selection)
    {
        ArgumentNullException.ThrowIfNull(text);
        MarkdownDocument doc = MarkdownDocument.Parse(text);
        var blocks = BlocksIn(doc, selection).ToList();
        int start = blocks[0].Start;
        int end = blocks[^1].End;
        string body = text[start..end];
        return new TextEdit(start, end - start, "```\n" + body + "\n```", TextSelection.Caret(start + 4 + body.Length));
    }

    /// <summary>
    /// Makes the selection, or the word at the caret, a link's text with the caret where its address goes; with
    /// nothing to use, inserts an empty link with the caret in its text.
    /// </summary>
    public static TextEdit? InsertLink(string text, TextSelection selection)
    {
        ArgumentNullException.ThrowIfNull(text);
        (int from, int to) = selection.IsCaret ? Word(text, selection.Start) : (selection.Start, selection.End);

        if (from == to)
        {
            return new TextEdit(from, 0, "[]()", TextSelection.Caret(from + 1));
        }

        if (text.AsSpan(from, to - from).IndexOfAny('\r', '\n') >= 0)
        {
            return null;
        }

        string label = text[from..to];
        return new TextEdit(from, to - from, "[" + label + "]()", TextSelection.Caret(from + label.Length + 3));
    }

    /// <summary>
    /// Enter at a caret in a list item: a new item of the same kind below, the text after the caret moving into it
    /// (an unchecked task, the next number). On an item with no text, ends the list instead: its marker is removed.
    /// <see langword="null"/> elsewhere, for an ordinary line break.
    /// </summary>
    public static TextEdit? ContinueList(string text, TextSelection selection)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (!selection.IsCaret)
        {
            return null;
        }

        MarkdownDocument doc = MarkdownDocument.Parse(text);
        MarkdownBlock? block = doc.Blocks.FirstOrDefault(b => b.Start <= selection.Start && selection.Start <= b.End);

        if (block is null || block.Kind is not (MarkdownBlockKind.BulletItem or MarkdownBlockKind.OrderedItem or MarkdownBlockKind.TaskItem))
        {
            return null;
        }

        MarkdownSpan marker = block.Spans.First(s => s.Kind == MarkdownSpanKind.ListMarker);
        int textStart = block.Kind == MarkdownBlockKind.TaskItem ? block.Spans.First(s => s.Kind == MarkdownSpanKind.TaskBox).End : marker.End;

        if (selection.Start < textStart)
        {
            return null;
        }

        if (text.AsSpan(textStart, block.End - textStart).IsWhiteSpace())
        {
            return new TextEdit(block.Start, block.Length, string.Empty, TextSelection.Caret(block.Start));
        }

        string indent = text[block.Start..marker.Start];
        string markerText = text[marker.Start..marker.End];

        if (block.Kind == MarkdownBlockKind.OrderedItem)
        {
            int digits = 0;
            while (char.IsAsciiDigit(markerText[digits]))
            {
                digits++;
            }

            long number = long.Parse(markerText.AsSpan(0, digits), CultureInfo.InvariantCulture) + 1;
            markerText = number.ToString(CultureInfo.InvariantCulture) + markerText[digits..];
        }

        string next = "\n" + indent + markerText + (block.Kind == MarkdownBlockKind.TaskItem ? "[ ] " : string.Empty);
        return new TextEdit(selection.Start, 0, next, TextSelection.Caret(selection.Start + next.Length));
    }

    /// <summary>
    /// Rewrites the line prefix of each block the selection touches. <paramref name="decide"/> gives each block's new
    /// prefix; the old one (heading hashes, list marker and task box) is replaced, the indent and the text are kept.
    /// </summary>
    private static TextEdit? Lines(string text, TextSelection selection, Func<MarkdownDocument, List<MarkdownBlock>, List<(MarkdownBlock Block, string Prefix)>> decide)
    {
        ArgumentNullException.ThrowIfNull(text);
        MarkdownDocument doc = MarkdownDocument.Parse(text);
        var blocks = BlocksIn(doc, selection).Where(b => b.Kind != MarkdownBlockKind.CodeFence).ToList();

        if (blocks.Count == 0)
        {
            return null;
        }

        var changes = decide(doc, blocks);
        if (changes.Count == 0)
        {
            return null;
        }

        int start = blocks[0].Start;
        int end = blocks[^1].End;
        var result = new StringBuilder();
        int at = start;
        int caretShift = 0;
        int anchorShift = 0;

        foreach ((MarkdownBlock block, string prefix) in changes)
        {
            (int oldStart, int oldEnd) = Prefix(text, block);
            result.Append(text, at, oldStart - at).Append(prefix);
            int delta = prefix.Length - (oldEnd - oldStart);

            if (selection.Active > oldStart)
            {
                caretShift += delta;
            }

            if (selection.Anchor > oldStart)
            {
                anchorShift += delta;
            }

            at = oldEnd;
        }

        result.Append(text, at, end - at);
        var after = new TextSelection(Math.Max(start, selection.Anchor + anchorShift), Math.Max(start, selection.Active + caretShift));
        return new TextEdit(start, end - start, result.ToString(), after);
    }

    /// <summary>The range a line's prefix occupies: after the indent, through the heading hashes or list marker and task box.</summary>
    private static (int Start, int End) Prefix(string text, MarkdownBlock block)
    {
        MarkdownSpan? marker = block.Spans.Where(s => s.Kind == MarkdownSpanKind.ListMarker).Select(s => (MarkdownSpan?)s).FirstOrDefault();

        if (marker is { } list)
        {
            int end = block.Spans.Where(s => s.Kind == MarkdownSpanKind.TaskBox).Select(s => s.End).DefaultIfEmpty(list.End).First();
            while (end < block.End && text[end] is ' ' or '\t')
            {
                end++;
            }

            return (list.Start, end);
        }

        if (block.Kind == MarkdownBlockKind.Heading)
        {
            MarkdownSpan hashes = block.Spans[0];
            int first = hashes.Start;
            while (text[first] == ' ')
            {
                first++;
            }

            return (first, hashes.End);
        }

        int lead = block.Start;
        while (lead < block.End && text[lead] is ' ' or '\t')
        {
            lead++;
        }

        return (lead, lead);
    }

    /// <summary>The blocks a selection touches: the caret's block, or every block from its start to its end.</summary>
    private static IEnumerable<MarkdownBlock> BlocksIn(MarkdownDocument doc, TextSelection selection) =>
        doc.Blocks.Where(b => b.End >= selection.Start && b.Start <= selection.End
            && !(selection.Length > 0 && b.Start == selection.End && b.Start > selection.Start));

    /// <summary>The word around <paramref name="at"/>: letters, digits and marks; empty when the caret is not in one.</summary>
    private static (int Start, int End) Word(string text, int at)
    {
        int start = at;
        while (start > 0 && IsWordChar(text[start - 1]))
        {
            start--;
        }

        int end = at;
        while (end < text.Length && IsWordChar(text[end]))
        {
            end++;
        }

        return (start, end);
    }

    private static bool IsWordChar(char c) =>
        char.IsLetterOrDigit(c) || char.IsSurrogate(c) || CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark;
}
