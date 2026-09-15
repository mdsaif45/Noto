using Noto.Core.Tags;
using Noto.UseCases.Tags;
using Xunit;

namespace Noto.Infrastructure.Tests.Queries;

/// <summary>
/// Q4 — <c>ListTags</c> against real SQLite (contract §11 Q4, §5a U13a).
/// </summary>
public sealed class ListTagsTests : IDisposable
{
    private readonly QueryTestContext _context = new();

    public void Dispose() => _context.Dispose();

    private static readonly string[] AppleMangoZebra = ["Apple", "Mango", "Zebra"];

    private static readonly string[] LowerAppleMangoZebra = ["apple", "Mango", "Zebra"];

    private static readonly string[] AppleBananaCherry = ["Apple", "banana", "Cherry"];

    private static readonly string[] AppleZebra = ["Apple", "Zebra"];

    private ListTagsQuery Query() => new(_context.Tags);

    [Fact]
    public void Returns_an_empty_list_when_no_tags_exist()
    {
        Assert.Empty(Query().Execute());
    }

    [Fact]
    public void Returns_a_single_tag()
    {
        var id = _context.SeedTag("Work");

        Tag tag = Assert.Single(Query().Execute());

        Assert.Equal(id, tag.Id);
        Assert.Equal("Work", tag.Name);
    }

    [Fact]
    public void Returns_every_tag()
    {
        _context.SeedTag("Alpha");
        _context.SeedTag("Beta");
        _context.SeedTag("Gamma");

        Assert.Equal(3, Query().Execute().Count);
    }

    // ---- U13a ordering ----------------------------------------------------

    [Fact]
    public void Orders_by_name_ascending()
    {
        // Seeded in reverse alphabetical order on purpose: an implementation
        // with no ORDER BY returns insertion order and fails here.
        _context.SeedTag("Zebra");
        _context.SeedTag("Mango");
        _context.SeedTag("Apple");

        Assert.Equal(
            AppleMangoZebra,
            Query().Execute().Select(t => t.Name));
    }

    [Fact]
    public void Orders_case_insensitively()
    {
        // The point of COLLATE NOCASE. Under SQLite's default BINARY collation
        // every uppercase letter sorts before every lowercase one, so "apple"
        // would come after "Zebra" — this is what a dropped COLLATE produces.
        _context.SeedTag("Zebra");
        _context.SeedTag("apple");
        _context.SeedTag("Mango");

        Assert.Equal(
            LowerAppleMangoZebra,
            Query().Execute().Select(t => t.Name));
    }

    [Fact]
    public void Case_insensitive_ordering_interleaves_mixed_case_correctly()
    {
        // A stronger form: under BINARY these would come back
        // "Apple", "Cherry", "banana" — uppercase block first.
        _context.SeedTag("banana");
        _context.SeedTag("Cherry");
        _context.SeedTag("Apple");

        Assert.Equal(
            AppleBananaCherry,
            Query().Execute().Select(t => t.Name));
    }

    [Fact]
    public void Preserves_the_stored_case()
    {
        // Ordering is case-insensitive; the VALUE is not altered.
        _context.SeedTag("WoRk");

        Assert.Equal("WoRk", Assert.Single(Query().Execute()).Name);
    }

    [Fact]
    public void Orders_by_name_rather_than_by_creation()
    {
        // Distinguishes ORDER BY Name from ORDER BY Id / CreatedAt — the ids
        // are ULIDs, so creation order is ascending id order.
        var zebra = _context.SeedTag("Zebra");
        var apple = _context.SeedTag("Apple");

        var result = Query().Execute();

        Assert.Equal(new[] { apple, zebra }, result.Select(t => t.Id));
    }

    [Fact]
    public void Orders_numerals_and_letters_deterministically()
    {
        _context.SeedTag("beta");
        _context.SeedTag("2026");
        _context.SeedTag("Alpha");

        var names = Query().Execute().Select(t => t.Name).ToList();

        // Whatever the collation does with digits, the result must be stable
        // and must place the two words case-insensitively.
        Assert.Equal(3, names.Count);
        Assert.True(
            names.IndexOf("Alpha") < names.IndexOf("beta"),
            $"expected Alpha before beta, got: {string.Join(", ", names)}");
    }

    // ---- persistence ------------------------------------------------------

    [Fact]
    public void Persists_across_a_reopen()
    {
        _context.SeedTag("Zebra");
        _context.SeedTag("Apple");

        var reopened = new Noto.Infrastructure.Storage.NotoDatabase(_context.DatabasePath);
        var repository = new Noto.Infrastructure.Storage.SqliteTagRepository(reopened);

        Assert.Equal(
            AppleZebra,
            repository.ListAll().Select(t => t.Name));
    }

    [Fact]
    public void Returns_fully_mapped_tags()
    {
        var id = _context.SeedTag("Work");

        Tag tag = Assert.Single(Query().Execute());

        Assert.Equal(id, tag.Id);
        Assert.Equal("Work", tag.Name);
        Assert.Null(tag.ColorKey);
        Assert.Equal(_context.Clock.UtcNow, tag.CreatedAt);
    }
}
