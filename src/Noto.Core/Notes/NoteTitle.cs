namespace Noto.Core.Notes;

/// <summary>
/// Derives a note's title from its content (parity B16).
/// </summary>
/// <remarks>
/// <para>
/// The title is <b>computed, never stored</b>. A stored copy would be a second
/// source of truth that must be kept in sync on every keystroke and can
/// silently diverge (core-note-engine-design.md §5, conflict C1). Migration 002
/// removed the column for exactly this reason.
/// </para>
/// <para>
/// The algorithm is the frozen one in core-note-engine-contract.md §3, step for
/// step. It is deliberately deterministic and free of any Markdown library:
/// only the heading rules below matter here, and ADR-004 fixes the flavour as
/// CommonMark, which defines them normatively.
/// </para>
/// </remarks>
public static class NoteTitle
{
    /// <summary>The longest ATX heading CommonMark recognises.</summary>
    private const int MaxHeadingLevel = 6;

    /// <summary>Indentation at which a line becomes an indented code block.</summary>
    private const int IndentedCodeThreshold = 4;

    /// <summary>
    /// Derives the title: the first non-empty line, with ATX heading markers
    /// removed.
    /// </summary>
    /// <returns>
    /// The title, or <see cref="string.Empty"/> when the content has no
    /// non-empty line. <b>Never <see langword="null"/></b>.
    /// </returns>
    /// <remarks>
    /// There is deliberately <b>no length cap</b>. Truncation is a display
    /// concern; capping here would make the domain lossy for a presentation
    /// reason (contract §3).
    /// </remarks>
    public static string From(string? content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return string.Empty;
        }

        foreach (var line in EnumerateLines(content))
        {
            if (line.IsWhiteSpace())
            {
                continue;
            }

            return StripAtxHeading(line);
        }

        // Whitespace-only content has no non-empty line.
        return string.Empty;
    }

    /// <summary>
    /// Splits on LF, CRLF and CR — all three are line terminators in CommonMark
    /// §2.1, and notes arrive from editors and clipboards that use each.
    /// </summary>
    private static IEnumerable<string> EnumerateLines(string content)
    {
        int start = 0;

        for (int i = 0; i < content.Length; i++)
        {
            char c = content[i];

            if (c is not ('\n' or '\r'))
            {
                continue;
            }

            yield return content[start..i];

            // CRLF is one terminator, not two: stepping over the LF keeps it
            // from being read as an extra empty line.
            if (c == '\r' && i + 1 < content.Length && content[i + 1] == '\n')
            {
                i++;
            }

            start = i + 1;
        }

        if (start < content.Length)
        {
            yield return content[start..];
        }
    }

    /// <summary>
    /// Removes a CommonMark ATX heading's markers, per CommonMark §4.2.
    /// </summary>
    /// <remarks>
    /// Setext headings (<c>Title</c> over <c>=====</c>) are deliberately
    /// <b>not</b> recognised: the title is line-based, so the underline is a
    /// separate line the algorithm never reaches (contract §3).
    /// </remarks>
    private static string StripAtxHeading(ReadOnlySpan<char> line)
    {
        ReadOnlySpan<char> trimmed = line.Trim();

        // Four or more leading spaces make the line an indented code block, so
        // its '#' is literal text rather than a heading marker.
        int indent = line.Length - line.TrimStart().Length;
        if (indent >= IndentedCodeThreshold)
        {
            return trimmed.ToString();
        }

        int hashes = 0;
        while (hashes < trimmed.Length && trimmed[hashes] == '#')
        {
            hashes++;
        }

        // Not a heading: no '#' at all, or more than six ("####### x" is a
        // paragraph in CommonMark, not a level-7 heading).
        if (hashes is 0 or > MaxHeadingLevel)
        {
            return trimmed.ToString();
        }

        ReadOnlySpan<char> rest = trimmed[hashes..];

        // The opening sequence must be followed by a space/tab or end of line.
        // "#Title" is therefore a paragraph, and its '#' is part of the title.
        if (rest.Length > 0 && rest[0] is not (' ' or '\t'))
        {
            return trimmed.ToString();
        }

        rest = rest.Trim();

        // An optional closing sequence of '#' is decoration and is removed —
        // but only when it is itself space-separated, so "# C#" keeps its '#'.
        int end = rest.Length;
        while (end > 0 && rest[end - 1] == '#')
        {
            end--;
        }

        if (end == 0)
        {
            // Nothing but hashes: "#" or "## ##" — a valid, empty heading.
            return string.Empty;
        }

        if (end < rest.Length && rest[end - 1] is ' ' or '\t')
        {
            rest = rest[..end].TrimEnd();
        }

        return rest.ToString();
    }
}
