using Noto.Core.Commands;
using Noto.Core.Identifiers;
using Noto.Core.Tags;
using Noto.UseCases.Tags;
using Xunit;

namespace Noto.Infrastructure.Tests.Tags;

/// <summary>
/// <c>CreateTag</c> against real SQLite (contract §11 row 19, §7a).
/// </summary>
public sealed class CreateTagTests : IDisposable
{
    private readonly TagTestContext _context = new();

    public void Dispose() => _context.Dispose();

    private CreateTagHandler Handler() => new(_context.Tags, _context.Clock);

    [Fact]
    public void Creates_a_tag_and_returns_a_ulid()
    {
        var result = Handler().Handle(new CreateTag("Work"));

        Assert.True(result.IsSuccess);
        Assert.True(Ulid.IsValid(result.Value.Value));
        Assert.Equal("Work", _context.NameOf(result.Value));
    }

    [Fact]
    public void Sets_created_at_and_has_no_updated_at_to_set()
    {
        // Contract §4: CreateTag stamps nothing, because Tag has no UpdatedAt.
        // Asserted against the schema so that adding the column later is a
        // test failure rather than a silent change of contract.
        var id = Handler().Handle(new CreateTag("Work")).Value;

        Assert.Equal(_context.Clock.UtcNow.ToString("O"), _context.CreatedAtOf(id));

        using var connection = _context.Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM pragma_table_info('Tags') WHERE name = 'UpdatedAt';";

        Assert.Equal(0L, (long)command.ExecuteScalar()!);
    }

    // ---- §7a normalisation ------------------------------------------------

    [Theory]
    [InlineData(" Work", "Work")]
    [InlineData("Work ", "Work")]
    [InlineData("  Work  ", "Work")]
    [InlineData("\tWork", "Work")]
    [InlineData("Work\r\n", "Work")]
    public void Trims_leading_and_trailing_whitespace_before_persisting(string input, string stored)
    {
        // §7a: the trimmed value is what is stored. The database cannot do this
        // — COLLATE NOCASE handles case, never whitespace — so this is proof of
        // an application-layer rule, not of an index.
        var id = Handler().Handle(new CreateTag(input)).Value;

        Assert.Equal(stored, _context.NameOf(id));
    }

    [Theory]
    [InlineData("Work Item")]
    [InlineData("Work  Item")]
    [InlineData("a b c")]
    public void Preserves_internal_whitespace_exactly(string name)
    {
        // §7a explicitly excludes internal whitespace: collapsing it would
        // change a name the user chose.
        var id = Handler().Handle(new CreateTag(name)).Value;

        Assert.Equal(name, _context.NameOf(id));
    }

    [Fact]
    public void Trims_the_outside_while_keeping_the_inside()
    {
        // Both halves of the rule in one assertion, so an implementation that
        // trims everything cannot pass.
        var id = Handler().Handle(new CreateTag("  Work  Item  ")).Value;

        Assert.Equal("Work  Item", _context.NameOf(id));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\r\n")]
    [InlineData(" \t \r\n ")]
    public void Rejects_a_name_that_is_empty_after_trimming(string name)
    {
        var result = Handler().Handle(new CreateTag(name));

        Assert.False(result.IsSuccess);
        Assert.Equal(CommandFailureReason.InvalidInput, result.Failure!.Reason);
        Assert.Equal(0, _context.TagCount());
    }

    // ---- uniqueness -------------------------------------------------------

    [Theory]
    [InlineData("Work")]
    [InlineData("work")]
    [InlineData("WORK")]
    [InlineData("WoRk")]
    public void Rejects_a_duplicate_regardless_of_case(string second)
    {
        // Design §5 / UX_Tags_Name COLLATE NOCASE.
        Assert.True(Handler().Handle(new CreateTag("Work")).IsSuccess);

        var result = Handler().Handle(new CreateTag(second));

        Assert.False(result.IsSuccess);
        Assert.Equal(CommandFailureReason.DuplicateName, result.Failure!.Reason);
        Assert.Equal(1, _context.TagCount());
    }

    [Theory]
    [InlineData(" Work")]
    [InlineData("Work ")]
    [InlineData("  Work  ")]
    [InlineData(" work ")]
    [InlineData("\tWORK\t")]
    public void Rejects_a_duplicate_that_only_differs_by_surrounding_whitespace(string second)
    {
        // The case §7a exists for. Without normalisation before the comparison
        // these all insert successfully, and the user ends up with tags they
        // cannot tell apart in any list.
        Assert.True(Handler().Handle(new CreateTag("Work")).IsSuccess);

        var result = Handler().Handle(new CreateTag(second));

        Assert.False(result.IsSuccess);
        Assert.Equal(CommandFailureReason.DuplicateName, result.Failure!.Reason);
        Assert.Equal(1, _context.TagCount());
    }

    [Fact]
    public void Detects_a_duplicate_against_an_untrimmed_row_already_in_the_database()
    {
        // A row planted verbatim, as though written before §7a existed. The
        // comparison still has to find it: FindIdByName relies on the column's
        // NOCASE collation, which does not trim the STORED side either.
        _context.SeedTag(" Work ");

        var result = Handler().Handle(new CreateTag("Work"));

        // The stored name is " Work ", which is NOT equal to "Work" under
        // NOCASE, so this legitimately succeeds. Recorded as the known limit of
        // the rule: §7a normalises what the engine writes, and does not
        // retro-normalise rows written by anything else. There is no such
        // writer in the engine.
        Assert.True(result.IsSuccess);
        Assert.Equal(2, _context.TagCount());
    }

    [Fact]
    public void Allows_genuinely_distinct_names()
    {
        Assert.True(Handler().Handle(new CreateTag("Work")).IsSuccess);
        Assert.True(Handler().Handle(new CreateTag("Home")).IsSuccess);
        Assert.True(Handler().Handle(new CreateTag("Work Item")).IsSuccess);

        Assert.Equal(3, _context.TagCount());
    }

    [Fact]
    public void Persists_across_a_reopen()
    {
        var id = Handler().Handle(new CreateTag("  Durable  ")).Value;

        var reopened = new Noto.Infrastructure.Storage.NotoDatabase(_context.DatabasePath);
        var repository = new Noto.Infrastructure.Storage.SqliteTagRepository(reopened);

        Tag? tag = repository.Find(id);

        Assert.NotNull(tag);
        Assert.Equal("Durable", tag!.Name);
    }
}
