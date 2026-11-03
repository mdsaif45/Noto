using Noto.Core.Search;
using Xunit;

namespace Noto.Core.Tests.Search;

public sealed class SearchQueryTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("'")]
    [InlineData("-- * \"")]
    [InlineData(null)]
    public void Nothing_with_a_letter_or_digit_is_an_empty_query(string? text)
    {
        SearchQuery query = SearchQuery.Parse(text);

        Assert.True(query.IsEmpty);
        Assert.Empty(query.Terms);
        Assert.Equal(string.Empty, query.ToFtsMatch());
    }

    [Fact]
    public void Terms_are_split_on_white_space_and_punctuation_only_terms_dropped()
    {
        SearchQuery query = SearchQuery.Parse("  weekly\tsync - notes\n");

        Assert.Equal(["weekly", "sync", "notes"], query.Terms);
    }

    [Fact]
    public void Words_are_folded_to_lower_case_without_diacritics_and_split_inside_a_term()
    {
        SearchQuery query = SearchQuery.Parse("Café RÉSUMÉ foo-bar Ünïcode");

        Assert.Equal(["cafe", "resume", "foo", "bar", "unicode"], query.Words);
    }

    [Theory]
    [InlineData("it's", "\"it's\"*")]
    [InlineData("say \"hi\"", "\"say\"* \"\"\"hi\"\"\"*")]
    [InlineData("a* NEAR(b) AND -c", "\"a*\"* \"NEAR(b)\"* \"AND\"* \"-c\"*")]
    [InlineData("col:value", "\"col:value\"*")]
    public void Every_term_reaches_the_index_as_a_quoted_prefix_string(string text, string match)
    {
        // ADR-003: no user text is ever FTS5 syntax. Note "AND" alone has no special meaning inside quotes either.
        Assert.Equal(match, SearchQuery.Parse(text).ToFtsMatch());
    }

    [Fact]
    public void A_bare_operator_word_is_a_term_like_any_other()
    {
        Assert.Equal("\"AND\"* \"OR\"* \"NOT\"*", SearchQuery.Parse("AND OR NOT").ToFtsMatch());
    }

    [Theory]
    [InlineData('A', 'a')]
    [InlineData('é', 'e')]
    [InlineData('Å', 'a')]
    [InlineData('ß', 'ß')]
    [InlineData('7', '7')]
    public void Fold_lowers_and_removes_the_diacritic(char c, char folded)
    {
        Assert.Equal(folded, SearchQuery.Fold(c));
    }

    [Theory]
    [InlineData('a', true)]
    [InlineData('Ж', true)]
    [InlineData('漢', true)]
    [InlineData('7', true)]
    [InlineData('½', true)]
    [InlineData('-', false)]
    [InlineData('_', false)]
    [InlineData(' ', false)]
    [InlineData('*', false)]
    public void Word_characters_are_letters_and_numbers(char c, bool word)
    {
        Assert.Equal(word, SearchQuery.IsWordChar(c));
    }
}
