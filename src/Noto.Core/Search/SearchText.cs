using System.Text;
using Noto.Core.Markdown;

namespace Noto.Core.Search;

/// <summary>A run of matched characters in a shown text.</summary>
public readonly record struct TextRange(int Start, int Length);

/// <summary>
/// Where a query matches a text, by the rule the index uses (ADR-018 §2): a word start that begins with one of the
/// query's words, ignoring case and diacritics. Used for highlighting, for the caret of an opened result, for the
/// title-first order and for folder names, which are not indexed.
/// </summary>
public static class SearchText
{
    /// <summary>The longest a snippet is shown, in characters.</summary>
    public const int SnippetLength = 120;

    /// <summary>How far before the first match a long snippet starts.</summary>
    private const int SnippetLead = 30;

    /// <summary>Each match in <paramref name="text"/>, in order, never overlapping.</summary>
    public static IReadOnlyList<TextRange> Matches(string text, SearchQuery query) => Scan(text, query).ToList();

    /// <summary>Whether every word of the query starts a word in <paramref name="text"/>: a title or folder name match.</summary>
    public static bool MatchesAll(string text, SearchQuery query)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(query);
        return !query.IsEmpty && query.Words.All(w => Enumerable.Range(0, text.Length).Any(i =>
            SearchQuery.IsWordChar(text[i]) && (i == 0 || !SearchQuery.IsWordChar(text[i - 1])) && StartsWith(text, i, w)));
    }

    /// <summary>Where an opened note's caret goes: the first match in its source, or its start.</summary>
    public static int FirstMatch(string content, SearchQuery query) =>
        Scan(content, query).Select(m => m.Start).FirstOrDefault();

    /// <summary>
    /// The line shown under a result's title: the first line after the title that matches, else the first line after
    /// it; its markup left out, and a long one cut to <see cref="SnippetLength"/> around its first match.
    /// </summary>
    public static string Snippet(string content, SearchQuery query)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(query);

        // Line by line, stopping at the first that matches: a result list builds a hundred of these per keystroke.
        string[] lines = content.Split('\n');
        int title = Array.FindIndex(lines, l => !string.IsNullOrWhiteSpace(l));
        string? first = null;

        for (int i = title + 1; title >= 0 && i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                continue;
            }

            bool matches = Scan(lines[i], query).Any();
            if (!matches && first is not null)
            {
                continue;
            }

            string shown = Plain(lines[i].TrimEnd('\r'));
            if (shown.Length == 0)
            {
                continue;
            }

            if (matches && Scan(shown, query).Any())
            {
                return Cut(shown, query);
            }

            first ??= shown;
        }

        return first is null ? string.Empty : Cut(first, query);
    }

    /// <summary>
    /// A note's text without its title line (E3, Ctrl+Shift+C): everything after the first non-blank line, with the
    /// blank lines that followed it dropped. A note of one line has nothing left.
    /// </summary>
    public static string WithoutTitle(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        string[] lines = content.Split('\n');
        int title = Array.FindIndex(lines, l => !string.IsNullOrWhiteSpace(l));
        return title < 0 ? string.Empty : string.Join('\n', lines.Skip(title + 1)).TrimStart('\r', '\n', ' ', '\t');
    }

    private static string Cut(string line, SearchQuery query)
    {
        if (line.Length <= SnippetLength)
        {
            return line;
        }

        int match = Matches(line, query) is [var first, ..] ? first.Start : 0;
        int start = match <= SnippetLead ? 0 : match - SnippetLead;
        int length = Math.Min(SnippetLength, line.Length - start);
        string cut = line.Substring(start, length).Trim();
        return (start > 0 ? "…" : string.Empty) + cut + (start + length < line.Length ? "…" : string.Empty);
    }

    /// <summary>A line as the read view shows it: markup, link targets, list markers and task boxes left out.</summary>
    private static string Plain(string line)
    {
        MarkdownDocument doc = MarkdownDocument.Parse(line);
        var hidden = new bool[line.Length];

        foreach (MarkdownSpan span in doc.Blocks.SelectMany(b => b.Spans))
        {
            if (span.Kind is MarkdownSpanKind.Markup or MarkdownSpanKind.LinkTarget or MarkdownSpanKind.ListMarker or MarkdownSpanKind.TaskBox)
            {
                for (int i = span.Start; i < span.End; i++)
                {
                    hidden[i] = true;
                }
            }
        }

        var shown = new StringBuilder(line.Length);
        for (int i = 0; i < line.Length; i++)
        {
            if (!hidden[i])
            {
                shown.Append(line[i]);
            }
        }

        return shown.ToString().Trim();
    }

    private static IEnumerable<TextRange> Scan(string text, SearchQuery query)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(query);

        for (int i = 0; i < text.Length; i++)
        {
            if (!SearchQuery.IsWordChar(text[i]) || (i > 0 && SearchQuery.IsWordChar(text[i - 1])))
            {
                continue;
            }

            int longest = 0;
            foreach (string word in query.Words)
            {
                if (word.Length > longest && StartsWith(text, i, word))
                {
                    longest = word.Length;
                }
            }

            if (longest > 0)
            {
                yield return new TextRange(i, longest);
                i += longest - 1;
            }
        }
    }

    private static bool StartsWith(string text, int at, string word)
    {
        if (at + word.Length > text.Length)
        {
            return false;
        }

        for (int k = 0; k < word.Length; k++)
        {
            if (!SearchQuery.IsWordChar(text[at + k]) || SearchQuery.Fold(text[at + k]) != word[k])
            {
                return false;
            }
        }

        return true;
    }
}
