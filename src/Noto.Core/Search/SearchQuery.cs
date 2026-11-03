using System.Globalization;
using System.Text;

namespace Noto.Core.Search;

/// <summary>
/// What the user typed into Search, as ADR-018 §2 reads it: terms separated by white space, each matching the start
/// of a word, ignoring case and diacritics, all of them required.
/// </summary>
/// <remarks>
/// The one place the meaning lives. <see cref="ToFtsMatch"/> is what the index is asked; <see cref="SearchText"/>
/// marks the same matches in the text shown, from <see cref="Words"/>.
/// </remarks>
public sealed class SearchQuery
{
    private SearchQuery(string text, IReadOnlyList<string> terms, IReadOnlyList<string> words)
    {
        Text = text;
        Terms = terms;
        Words = words;
    }

    /// <summary>The text as typed.</summary>
    public string Text { get; }

    /// <summary>The white-space separated terms that hold at least one letter or digit, as typed.</summary>
    public IReadOnlyList<string> Terms { get; }

    /// <summary>Every word of every term, folded (<see cref="Fold"/>): what a word start must begin with.</summary>
    public IReadOnlyList<string> Words { get; }

    /// <summary>Nothing to search for: no letter or digit was typed.</summary>
    public bool IsEmpty => Terms.Count == 0;

    public static SearchQuery Parse(string? text)
    {
        text ??= string.Empty;
        var terms = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Where(t => t.Any(IsWordChar)).ToList();
        var words = terms.SelectMany(WordsOf).Distinct(StringComparer.Ordinal).ToList();
        return new SearchQuery(text, terms, words);
    }

    /// <summary>
    /// The FTS5 query: each term a quoted string with a prefix star, a <c>"</c> doubled, joined by spaces (AND). No
    /// user text is ever FTS5 syntax (ADR-003). Empty for an empty query, which must not be run.
    /// </summary>
    public string ToFtsMatch() =>
        string.Join(' ', Terms.Select(t => "\"" + t.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"*"));

    /// <summary>A character FTS5's unicode61 tokenizer keeps in a word: a letter, a number, or a private-use mark.</summary>
    public static bool IsWordChar(char c) => char.GetUnicodeCategory(c) switch
    {
        UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter
            or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter
            or UnicodeCategory.DecimalDigitNumber or UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber
            or UnicodeCategory.PrivateUse => true,
        _ => false,
    };

    /// <summary>A character as the index compares it: lower case, without its diacritic (é → e, Å → a).</summary>
    public static char Fold(char c)
    {
        if (c < 0x80)
        {
            return char.ToLowerInvariant(c);
        }

        string decomposed = c.ToString().Normalize(NormalizationForm.FormD);
        return char.ToLowerInvariant(decomposed[0]);
    }

    private static IEnumerable<string> WordsOf(string term)
    {
        var word = new StringBuilder();
        foreach (char c in term)
        {
            if (IsWordChar(c))
            {
                word.Append(Fold(c));
            }
            else if (word.Length > 0)
            {
                yield return word.ToString();
                word.Clear();
            }
        }

        if (word.Length > 0)
        {
            yield return word.ToString();
        }
    }
}
