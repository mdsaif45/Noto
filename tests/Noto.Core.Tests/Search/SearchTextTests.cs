using Noto.Core.Search;
using Xunit;

namespace Noto.Core.Tests.Search;

public sealed class SearchTextTests
{
    private static IReadOnlyList<TextRange> Matches(string text, string query) => SearchText.Matches(text, SearchQuery.Parse(query));

    [Fact]
    public void A_match_is_a_word_start_beginning_with_a_query_word()
    {
        Assert.Equal([new TextRange(2, 4)], Matches("a meeting", "meet"));
        Assert.Empty(Matches("a meeting", "ting"));
    }

    [Fact]
    public void Case_and_diacritics_are_ignored_and_the_text_keeps_its_own()
    {
        Assert.Equal([new TextRange(4, 4), new TextRange(15, 4)], Matches("the CAFÉ, then Café", "cafe"));
        Assert.Equal([new TextRange(0, 3)], Matches("Résumé", "res"));
    }

    [Fact]
    public void Every_query_word_is_marked_and_the_longest_wins_at_a_word_start()
    {
        Assert.Equal([new TextRange(0, 5), new TextRange(7, 4)], Matches("weekly sync", "wee weekl sync"));
    }

    [Fact]
    public void Punctuation_and_markup_separate_words()
    {
        Assert.Equal([new TextRange(2, 4), new TextRange(9, 3)], Matches("**bold** foo-bar", "bold foo"));
        Assert.Equal([new TextRange(9, 3), new TextRange(14, 3)], Matches("see [the RFC](rfc.md)", "rfc"));
    }

    [Fact]
    public void Digits_start_words_too()
    {
        Assert.Equal([new TextRange(4, 4)], Matches("Q4: 2026 plan", "2026"));
    }

    [Fact]
    public void An_empty_query_matches_nothing()
    {
        Assert.Empty(Matches("anything", "  "));
        Assert.False(SearchText.MatchesAll("anything", SearchQuery.Parse("")));
    }

    [Theory]
    [InlineData("Weekly sync notes", "sync week", true)]
    [InlineData("Weekly sync notes", "sync month", false)]
    [InlineData("Weekly sync notes", "ync", false)]
    [InlineData("Café plans", "cafe", true)]
    public void MatchesAll_needs_every_word_at_a_word_start(string text, string query, bool all)
    {
        Assert.Equal(all, SearchText.MatchesAll(text, SearchQuery.Parse(query)));
    }

    [Fact]
    public void The_caret_goes_to_the_first_match_or_the_start()
    {
        Assert.Equal(9, SearchText.FirstMatch("# Title\n\nfind me here", SearchQuery.Parse("find")));
        Assert.Equal(0, SearchText.FirstMatch("nothing to see", SearchQuery.Parse("absent")));
    }

    [Fact]
    public void The_snippet_is_the_first_matching_line_after_the_title_without_its_markup()
    {
        string content = "# Groceries\n\n- [ ] eggs\n- [x] **fresh** milk\nbread";

        Assert.Equal("fresh milk", SearchText.Snippet(content, SearchQuery.Parse("milk")));
    }

    [Fact]
    public void A_match_only_in_the_title_shows_the_first_line_after_it()
    {
        Assert.Equal("eggs", SearchText.Snippet("# Groceries\n\n- eggs\n- milk", SearchQuery.Parse("groc")));
    }

    [Fact]
    public void A_note_of_one_line_has_no_snippet()
    {
        Assert.Equal(string.Empty, SearchText.Snippet("Just a title", SearchQuery.Parse("just")));
        Assert.Equal(string.Empty, SearchText.Snippet("   \n  ", SearchQuery.Parse("x")));
    }

    [Fact]
    public void A_long_line_is_cut_around_its_first_match()
    {
        string line = new string('a', 50) + " needle " + new string('b', 200);
        string snippet = SearchText.Snippet("Title\n" + line, SearchQuery.Parse("needle"));

        Assert.StartsWith("…", snippet, StringComparison.Ordinal);
        Assert.EndsWith("…", snippet, StringComparison.Ordinal);
        Assert.Contains("needle", snippet, StringComparison.Ordinal);
        Assert.True(snippet.Length <= SearchText.SnippetLength + 2, $"{snippet.Length}");
        Assert.Single(SearchText.Matches(snippet, SearchQuery.Parse("needle")));

        // It keeps some of what comes before the match: 30 characters from its start, trimmed.
        Assert.StartsWith("…" + new string('a', 29) + " needle", snippet, StringComparison.Ordinal);
    }

    [Fact]
    public void A_long_line_matching_early_is_cut_at_its_end_only()
    {
        string line = "needle " + new string('b', 200);
        string snippet = SearchText.Snippet("Title\n" + line, SearchQuery.Parse("needle"));

        Assert.StartsWith("needle", snippet, StringComparison.Ordinal);
        Assert.EndsWith("…", snippet, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("# Title\n\nfirst\nsecond", "first\nsecond")]
    [InlineData("\n\n  Title\nbody", "body")]
    [InlineData("Only a title", "")]
    [InlineData("", "")]
    [InlineData("Title\r\n\r\nbody\r\nmore", "body\r\nmore")]
    public void Copy_without_the_title_drops_the_first_non_blank_line(string content, string rest)
    {
        Assert.Equal(rest, SearchText.WithoutTitle(content));
    }
}
