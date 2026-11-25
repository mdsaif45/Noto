using System.Globalization;
using System.Text;
using Noto.Core.Links;
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

    public static TextEdit? ToggleHighlight(string text, TextSelection selection) => ToggleInline(text, selection, "::", MarkdownSpanKind.Highlight);

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
    public static TextEdit? ToggleTaskDone(string text, TextSelection selection) =>
        ToggleTaskDone(text, selection, selection, checkedToBottom: false);

    /// <summary>
    /// <see cref="ToggleTaskDone(string, TextSelection)"/>, and with <paramref name="checkedToBottom"/> (the setting Move
    /// checked tasks to the bottom, D23, #244) each task it checks moves, with its nested lines, to the end of its own
    /// list; each it unchecks moves back to just after the list's last open item. One edit, so one undo step with the
    /// check, and the selection follows the lines it was on.
    /// </summary>
    public static TextEdit? ToggleTaskDone(string text, TextSelection selection, bool checkedToBottom) =>
        ToggleTaskDone(text, selection, selection, checkedToBottom);

    /// <summary>
    /// A click on a task's box (#244): the task whose box starts at <paramref name="box"/> is checked or unchecked as
    /// <see cref="ToggleTaskDone(string, TextSelection, bool)"/> does, and the caret stays where the click put it,
    /// following its line when the task moves.
    /// </summary>
    public static TextEdit? ToggleTaskAt(string text, int box, TextSelection selection, bool checkedToBottom) =>
        ToggleTaskDone(text, TextSelection.Caret(box), selection, checkedToBottom);

    /// <summary>The tasks <paramref name="at"/> touches toggled, <paramref name="keep"/> the selection carried along.</summary>
    private static TextEdit? ToggleTaskDone(string text, TextSelection at, TextSelection keep, bool checkedToBottom)
    {
        ArgumentNullException.ThrowIfNull(text);
        MarkdownDocument doc = MarkdownDocument.Parse(text);
        var tasks = BlocksIn(doc, at).Where(b => b.Kind == MarkdownBlockKind.TaskItem).ToList();

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
        var toggled = new TextEdit(first, last - first, new string(result, first, last - first), keep);
        return checkedToBottom ? MoveToggledTasks(text, new string(result), doc, tasks, check, keep) ?? toggled : toggled;
    }

    /// <summary>One line as moving a checked task reads it (#244).</summary>
    /// <param name="Wall">A line no list goes past: blank, a heading, a separator or a code fence's.</param>
    /// <param name="Item">A list item's line.</param>
    /// <param name="Indent">Its leading whitespace in columns.</param>
    /// <param name="List">Which list an item belongs to, as CommonMark tells lists apart: its bullet, or its number's <c>.</c> or <c>)</c>.</param>
    /// <param name="Done">A checked task, after the toggle.</param>
    /// <param name="Number">Where a numbered item's number starts in its line.</param>
    /// <param name="Digits">How long that number is; 0 for a bullet.</param>
    private readonly record struct ListLine(bool Wall, bool Item, int Indent, char List, bool Done, int Number, int Digits);

    /// <summary>
    /// The toggle of <paramref name="tasks"/> (<paramref name="toggled"/> is <paramref name="before"/> with it made) with
    /// each task moved within its own list (#244): its siblings are the items at its indent in the same list, each with
    /// the more-indented lines under it, up to the first line that is a wall, less indented, or another list. A checked
    /// task goes after its last sibling; an unchecked one just after the last sibling that is not a checked task. Nested
    /// tasks move first, within their parent, which then moves whole. A numbered list keeps its numbers in their places:
    /// the lines move, the numbers stay, so it still counts as it did. <see langword="null"/> when nothing moves.
    /// </summary>
    private static TextEdit? MoveToggledTasks(string before, string toggled, MarkdownDocument doc, List<MarkdownBlock> tasks, bool check, TextSelection keep)
    {
        string[] lines = toggled.Split('\n');
        var info = new ListLine[lines.Length];
        var toggledStarts = tasks.Select(t => t.Start).ToHashSet();
        var taskLines = new List<int>();
        int line = 0;
        int at = 0;

        foreach (MarkdownBlock block in doc.Blocks)
        {
            line += before.AsSpan(at, block.Start - at).Count('\n');
            at = block.Start;
            bool mine = toggledStarts.Contains(block.Start);
            ListLine read = Read(before, block, mine ? check : block.IsChecked);

            for (int i = line; i <= line + before.AsSpan(block.Start, block.Length).Count('\n'); i++)
            {
                info[i] = read;
            }

            if (mine)
            {
                taskLines.Add(line);
            }
        }

        // order[k] is the line that is k-th now; the selection's ends are kept as (line, column) while lines move.
        int[] order = Enumerable.Range(0, lines.Length).ToArray();
        var ends = new[] { Locate(before, keep.Anchor), Locate(before, keep.Active) };
        bool moved = false;

        foreach (int task in taskLines.OrderByDescending(l => info[l].Indent).ThenBy(l => l))
        {
            moved |= MoveTask(task, order, info, lines, ends);
        }

        if (!moved)
        {
            return null;
        }

        int Offset((int Line, int Column) end)
        {
            int offset = 0;
            for (int k = 0; order[k] != end.Line; k++)
            {
                offset += lines[order[k]].Length + 1;
            }

            return offset + end.Column;
        }

        return TextDiff.Between(before, string.Join('\n', order.Select(i => lines[i])), new TextSelection(Offset(ends[0]), Offset(ends[1])));
    }

    private static ListLine Read(string text, MarkdownBlock block, bool done)
    {
        if (block.Kind is MarkdownBlockKind.Blank or MarkdownBlockKind.Heading or MarkdownBlockKind.Separator or MarkdownBlockKind.CodeFence)
        {
            return new ListLine(Wall: true, Item: false, 0, default, false, 0, 0);
        }

        if (block.Kind is MarkdownBlockKind.BulletItem or MarkdownBlockKind.OrderedItem or MarkdownBlockKind.TaskItem)
        {
            MarkdownSpan marker = block.Spans.First(s => s.Kind == MarkdownSpanKind.ListMarker);
            int digits = 0;
            while (char.IsAsciiDigit(text[marker.Start + digits]))
            {
                digits++;
            }

            bool task = block.Kind == MarkdownBlockKind.TaskItem;
            return new ListLine(Wall: false, Item: true, block.Indent, text[marker.Start + digits], task && done, marker.Start - block.Start, digits);
        }

        int lead = block.Start;
        while (lead < block.End && text[lead] is ' ' or '\t')
        {
            lead++;
        }

        return new ListLine(Wall: false, Item: false, BlockParser.Columns(text, block.Start, lead), default, false, 0, 0);
    }

    /// <summary>Moves the task on <paramref name="task"/> within its list, as <see cref="MoveToggledTasks"/> says. Whether it moved.</summary>
    private static bool MoveTask(int task, int[] order, ListLine[] info, string[] lines, (int Line, int Column)[] ends)
    {
        ListLine head = info[task];
        bool Sibling(int k) => info[order[k]] is { Wall: false, Item: true } l && l.Indent == head.Indent && l.List == head.List;
        bool Child(int k) => info[order[k]] is { Wall: false } l && l.Indent > head.Indent;

        int position = Array.IndexOf(order, task);
        int start = position;
        for (int k = position - 1; k >= 0 && (Sibling(k) || Child(k)); k--)
        {
            start = Sibling(k) ? k : start;
        }

        int end = position + 1;
        while (end < order.Length && (Sibling(end) || Child(end)))
        {
            end++;
        }

        var units = new List<List<int>>();
        for (int k = start; k < end; k++)
        {
            if (Sibling(k))
            {
                units.Add([]);
            }

            units[^1].Add(order[k]);
        }

        string[] numbers = [.. units.Select(u => lines[u[0]].Substring(info[u[0]].Number, info[u[0]].Digits))];
        int from = units.FindIndex(u => u[0] == task);
        List<int> unit = units[from];
        units.RemoveAt(from);
        int to = head.Done ? units.Count : units.FindLastIndex(u => !info[u[0]].Done) + 1;
        units.Insert(to, unit);

        if (to == from)
        {
            return false;
        }

        int next = start;
        foreach (int moved in units.SelectMany(u => u))
        {
            order[next++] = moved;
        }

        if (head.Digits > 0)
        {
            Renumber(units, numbers, info, lines, ends);
        }

        return true;
    }

    /// <summary>
    /// The k-th item of <paramref name="units"/> takes the k-th of <paramref name="numbers"/>, so a numbered list's
    /// numbering stays where it was after a move; a selection end on a renumbered marker keeps its place.
    /// </summary>
    private static void Renumber(List<List<int>> units, string[] numbers, ListLine[] info, string[] lines, (int Line, int Column)[] ends)
    {
        for (int k = 0; k < units.Count; k++)
        {
            int first = units[k][0];
            ListLine item = info[first];
            lines[first] = string.Concat(lines[first].AsSpan(0, item.Number), numbers[k], lines[first].AsSpan(item.Number + item.Digits));
            info[first] = item with { Digits = numbers[k].Length };

            for (int e = 0; e < ends.Length; e++)
            {
                if (ends[e].Line == first && ends[e].Column > item.Number)
                {
                    int column = ends[e].Column >= item.Number + item.Digits ? ends[e].Column + numbers[k].Length - item.Digits : Math.Min(ends[e].Column, item.Number + numbers[k].Length);
                    ends[e] = (first, column);
                }
            }
        }
    }

    /// <summary>The line holding <paramref name="offset"/>, and its column there.</summary>
    private static (int Line, int Column) Locate(string text, int offset) =>
        (text.AsSpan(0, offset).Count('\n'), offset - LineStart(text, offset));

    /// <summary>
    /// Removes every checked task in the note, each with its line break (F19, #158; parity D22, G34): one edit, so one
    /// undo step. Unchecked tasks, a task in a code block and every other line stay as they are. The caret keeps its
    /// place in the text that stays; inside a removed task, it goes to where that line was.
    /// </summary>
    /// <returns>The edit, or <see langword="null"/> when no task is checked.</returns>
    public static TextEdit? ClearCheckedTasks(string text, TextSelection selection)
    {
        ArgumentNullException.ThrowIfNull(text);
        var done = MarkdownDocument.Parse(text).Blocks.Where(b => b.Kind == MarkdownBlockKind.TaskItem && b.IsChecked).ToList();

        if (done.Count == 0)
        {
            return null;
        }

        // Each removed range is the task's line and the break after it; the last line of the note takes the break
        // before it instead, so no empty line is left at the end.
        // Ranges that touch are merged, so each character is removed, and counted, once.
        var removed = new List<(int Start, int End)>();
        foreach (MarkdownBlock task in done)
        {
            (int start, int end) = task.End < text.Length ? (task.Start, task.End + 1) : (Math.Max(0, task.Start - 1), task.End);
            if (removed.Count > 0 && start <= removed[^1].End)
            {
                removed[^1] = (removed[^1].Start, Math.Max(end, removed[^1].End));
            }
            else
            {
                removed.Add((start, end));
            }
        }

        return Removed(text, removed, selection);
    }

    /// <summary>
    /// Clear formatting (D21, #235): the inline markup of every span the selection touches goes, its text stays, as one
    /// edit, so one undo step. Bold, italic, strikethrough, underline, highlight and code lose their markers; a link keeps
    /// its text and loses its target. A bare web address has no markup and stays a link; a picture is not formatting and
    /// stays, its caption too; headings, list markers, task boxes, quotes and fences are lines, not inline markup, and stay.
    /// A caret clears the spans around it, as the toggles act on the word at the caret.
    /// </summary>
    /// <remarks>
    /// A span the selection cuts through is cleared whole, not split around the selection: a marker only opens before
    /// text and closes after it (CommonMark's flanking rules), so markers put back at a cut beside a space or a comma would
    /// read as text, and the part meant to stay formatted would lose it anyway. One undo brings the whole span back.
    /// </remarks>
    /// <returns>The edit, or <see langword="null"/> when the selection touches no formatting.</returns>
    public static TextEdit? ClearFormatting(string text, TextSelection selection)
    {
        ArgumentNullException.ThrowIfNull(text);
        IReadOnlyList<MarkdownSpan> spans = MarkdownDocument.Parse(text).Spans;
        var images = spans.Where(s => s.Kind == MarkdownSpanKind.Image).ToList();
        var removed = new List<(int Start, int End)>();

        foreach (MarkdownSpan span in spans.Where(s => s.Kind is MarkdownSpanKind.Strong or MarkdownSpanKind.Emphasis or MarkdownSpanKind.Strikethrough
            or MarkdownSpanKind.Underline or MarkdownSpanKind.Highlight or MarkdownSpanKind.Code or MarkdownSpanKind.Link))
        {
            // Each span's own markers touch its text: the markup ending where it starts, and the markup starting where it ends.
            // A span in a picture's caption is the picture's; a link around a picture has the picture as its text, and goes.
            MarkdownSpan? open = spans.Where(s => s.Kind == MarkdownSpanKind.Markup && s.End == span.Start).Cast<MarkdownSpan?>().FirstOrDefault();
            MarkdownSpan? close = spans.Where(s => s.Kind == MarkdownSpanKind.Markup && s.Start == span.End).Cast<MarkdownSpan?>().FirstOrDefault();

            if (open is not { } before || close is not { } after || images.Any(i => i.Start < span.Start && span.End <= i.End))
            {
                continue;
            }

            int end = after.End;
            if (span.Kind == MarkdownSpanKind.Link)
            {
                // "[text](target)": a bare address beside emphasis touches that emphasis's markers, not a '['.
                if (text[before.Start] != '[' || spans.Where(s => s.Kind == MarkdownSpanKind.LinkTarget && s.Start == after.End).Cast<MarkdownSpan?>().FirstOrDefault() is not { } target)
                {
                    continue;
                }

                end = target.End + 1;
            }

            bool touched = selection.IsCaret
                ? span.Start <= selection.Start && selection.Start <= span.End
                : selection.Start < end && before.Start < selection.End;

            if (touched)
            {
                removed.Add((before.Start, before.End));
                removed.Add((after.Start, end));
            }
        }

        if (removed.Count == 0)
        {
            return null;
        }

        removed.Sort();
        return Removed(text, removed, selection);
    }

    /// <summary>
    /// The edit that removes <paramref name="removed"/> (in order, none overlapping) from <paramref name="text"/>, the
    /// selection kept on the text that stays; an end inside a removed range goes to where that range was.
    /// </summary>
    private static TextEdit Removed(string text, List<(int Start, int End)> removed, TextSelection selection)
    {
        int first = removed[0].Start;
        int last = removed[^1].End;

        var kept = new StringBuilder();
        int at = first;
        foreach ((int start, int end) in removed)
        {
            kept.Append(text, at, start - at);
            at = end;
        }

        kept.Append(text, at, last - at);

        int Mapped(int offset)
        {
            int shift = 0;
            foreach ((int start, int end) in removed)
            {
                if (offset >= end)
                {
                    shift += end - start;
                }
                else if (offset > start)
                {
                    shift += offset - start;
                }
            }

            return offset - shift;
        }

        return new TextEdit(first, last - first, kept.ToString(), new TextSelection(Mapped(selection.Anchor), Mapped(selection.Active)));
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
    /// A web or mail address pasted over a selection on one line makes the selection that link's text (D53), the caret
    /// after it. <see langword="null"/> for anything else: the paste is an ordinary one.
    /// </summary>
    public static TextEdit? LinkSelection(string text, TextSelection selection, string pasted)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(pasted);
        string address = pasted.Trim();

        if (selection.IsCaret || LinkTarget.Classify(address) is not (LinkKind.Web or LinkKind.Mail)
            || text.AsSpan(selection.Start, selection.Length).IndexOfAny('\r', '\n') >= 0)
        {
            return null;
        }

        string link = "[" + text[selection.Start..selection.End] + "](" + address + ")";
        return new TextEdit(selection.Start, selection.Length, link, TextSelection.Caret(selection.Start + link.Length));
    }

    /// <summary>
    /// An image Noto keeps, on a line of its own at the selection (ADR-019 §4): <c>![name](reference)</c>, with a line
    /// break added before it or after it where the line goes on. The caret goes after the image's line.
    /// </summary>
    public static TextEdit InsertImage(string text, TextSelection selection, string reference, string name)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(name);

        string alt = name.Replace("[", "\\[", StringComparison.Ordinal).Replace("]", "\\]", StringComparison.Ordinal).ReplaceLineEndings(" ");
        bool before = selection.Start > 0 && text[selection.Start - 1] != '\n';
        bool after = selection.End < text.Length && text[selection.End] != '\n';
        string image = (before ? "\n" : string.Empty) + "![" + alt + "](" + reference + ")" + (after ? "\n" : string.Empty);
        int caret = selection.Start + image.Length - (after ? 1 : 0);
        return new TextEdit(selection.Start, selection.Length, image, TextSelection.Caret(caret));
    }

    /// <summary>
    /// The Format menu's separator (D16, #194): a <c>---</c> line at the selection's end, the selected text kept, and
    /// the caret at the start of the line after it, where the rest of the caret's line goes. A blank line goes before
    /// it unless the line above is blank already: <c>---</c> right under text underlines that text as a heading to
    /// other Markdown readers (CommonMark's setext heading), and the note's Markdown is what they read.
    /// </summary>
    public static TextEdit InsertSeparator(string text, TextSelection selection)
    {
        ArgumentNullException.ThrowIfNull(text);

        int at = selection.End;
        int line = LineStart(text, at);
        string above = at > line ? text[line..at] : line > 0 ? text[LineStart(text, line - 1)..(line - 1)] : string.Empty;
        string separator = (at > line ? "\n" : string.Empty) + (string.IsNullOrWhiteSpace(above) ? string.Empty : "\n") + "---\n";
        return new TextEdit(at, 0, separator, TextSelection.Caret(at + separator.Length));
    }

    /// <summary>
    /// Links to local files and folders (D34), one per line, replacing the selection: each reads as its name and opens
    /// its full path. They go on lines of their own, as an image does, with a line break added before or after where
    /// the line goes on: a line that is one file link is drawn as a shortcut (#196). The caret goes after the last.
    /// </summary>
    public static TextEdit LinkPaths(string text, TextSelection selection, IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(paths);

        string links = string.Join('\n', paths.Select(p =>
        {
            string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(p));
            string label = (name.Length > 0 ? name : p).Replace("[", "\\[", StringComparison.Ordinal).Replace("]", "\\]", StringComparison.Ordinal);
            return "[" + label + "](" + LinkTarget.ForPath(p) + ")";
        }));

        bool before = selection.Start > 0 && text[selection.Start - 1] != '\n';
        bool after = selection.End < text.Length && text[selection.End] != '\n';
        string inserted = (before ? "\n" : string.Empty) + links + (after ? "\n" : string.Empty);
        int caret = selection.Start + inserted.Length - (after ? 1 : 0);
        return new TextEdit(selection.Start, selection.Length, inserted, TextSelection.Caret(caret));
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

    /// <summary>
    /// Tab in a list (G36): indents each selected list item by its own marker's width, so it nests under the item
    /// above as Markdown reads it. <see langword="null"/> when a selected line is not a list item: Tab then moves focus.
    /// </summary>
    public static TextEdit? IndentListItems(string text, TextSelection selection) => ShiftListItems(text, selection, indent: true);

    /// <summary>Shift+Tab in a list (G37): outdents each selected list item by up to its marker's width, or one tab.</summary>
    public static TextEdit? OutdentListItems(string text, TextSelection selection) => ShiftListItems(text, selection, indent: false);

    private static TextEdit? ShiftListItems(string text, TextSelection selection, bool indent)
    {
        ArgumentNullException.ThrowIfNull(text);
        var blocks = BlocksIn(MarkdownDocument.Parse(text), selection).Where(b => b.Kind != MarkdownBlockKind.Blank).ToList();

        if (blocks.Count == 0 || blocks.Any(b => b.Kind is not (MarkdownBlockKind.BulletItem or MarkdownBlockKind.OrderedItem or MarkdownBlockKind.TaskItem)))
        {
            return null;
        }

        int start = blocks[0].Start;
        int end = blocks[^1].End;
        var result = new StringBuilder();
        int at = start;
        int anchorShift = 0;
        int activeShift = 0;
        bool changed = false;

        foreach (MarkdownBlock block in blocks)
        {
            MarkdownSpan marker = block.Spans.First(s => s.Kind == MarkdownSpanKind.ListMarker);
            string lead = text[block.Start..marker.Start];
            string shifted = indent ? lead + new string(' ', marker.Length) : Outdent(lead, marker.Length);
            int delta = shifted.Length - lead.Length;
            changed |= delta != 0;

            result.Append(text, at, block.Start - at).Append(shifted);
            anchorShift += selection.Anchor > block.Start ? delta : 0;
            activeShift += selection.Active > block.Start ? delta : 0;
            at = marker.Start;
        }

        if (!changed)
        {
            return null;
        }

        result.Append(text, at, end - at);
        var after = new TextSelection(Math.Max(start, selection.Anchor + anchorShift), Math.Max(start, selection.Active + activeShift));
        return new TextEdit(start, end - start, result.ToString(), after);
    }

    /// <summary>The indent with one level removed: a leading tab, or up to <paramref name="width"/> spaces.</summary>
    private static string Outdent(string lead, int width)
    {
        if (lead.StartsWith('\t'))
        {
            return lead[1..];
        }

        int spaces = 0;
        while (spaces < lead.Length && spaces < width && lead[spaces] == ' ')
        {
            spaces++;
        }

        return lead[spaces..];
    }

    /// <summary>Alt+Up (G38): the selected lines change places with the line above. <see langword="null"/> at the top.</summary>
    public static TextEdit? MoveLinesUp(string text, TextSelection selection) => MoveLines(text, selection, up: true);

    /// <summary>Alt+Down (G39): the selected lines change places with the line below. <see langword="null"/> at the bottom.</summary>
    public static TextEdit? MoveLinesDown(string text, TextSelection selection) => MoveLines(text, selection, up: false);

    private static TextEdit? MoveLines(string text, TextSelection selection, bool up)
    {
        ArgumentNullException.ThrowIfNull(text);

        int first = LineStart(text, selection.Start);

        // A selection that ends at the start of a line does not take that line with it.
        int lastAt = selection.Length > 0 && selection.End > first && selection.End == LineStart(text, selection.End) ? selection.End - 1 : selection.End;
        int last = LineEnd(text, lastAt);

        if (up)
        {
            if (first == 0)
            {
                return null;
            }

            int above = LineStart(text, first - 1);
            string moved = text[first..last];
            string other = text[above..(first - 1)];
            int shift = -(other.Length + 1);
            return Moved(above, last - above, moved + "\n" + other, selection, shift);
        }

        if (last == text.Length)
        {
            return null;
        }

        int belowEnd = LineEnd(text, last + 1);
        string down = text[first..last];
        string below = text[(last + 1)..belowEnd];
        int forward = below.Length + 1;
        return Moved(first, belowEnd - first, below + "\n" + down, selection, forward);
    }

    /// <summary>
    /// The edit of a line move, the selection carried with the lines and kept inside them: a selection that ended at
    /// the start of the next line did not take that line along, so its end stays with the moved block.
    /// </summary>
    private static TextEdit Moved(int start, int length, string replacement, TextSelection selection, int shift) =>
        new(start, length, replacement, new TextSelection(
            Math.Clamp(selection.Anchor + shift, start, start + replacement.Length),
            Math.Clamp(selection.Active + shift, start, start + replacement.Length)));

    /// <summary>The start of the line holding <paramref name="at"/>: just after the break before it, or 0.</summary>
    private static int LineStart(string text, int at) =>
        at <= 0 ? 0 : text.LastIndexOf('\n', Math.Min(at, text.Length) - 1) + 1;

    private static int LineEnd(string text, int at) => text.IndexOf('\n', Math.Min(at, text.Length)) is var e and >= 0 ? e : text.Length;

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
