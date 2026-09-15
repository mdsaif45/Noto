using Noto.Core.Commands;
using Noto.Core.Tags;
using Noto.UseCases.Tags;
using Xunit;

namespace Noto.Infrastructure.Tests.Tags;

/// <summary>
/// <c>RenameTag</c> against real SQLite (contract §11 row 20, §7a).
/// </summary>
public sealed class RenameTagTests : IDisposable
{
    private readonly TagTestContext _context = new();

    public void Dispose() => _context.Dispose();

    private RenameTagHandler Handler() => new(_context.Tags);

    [Fact]
    public void Renames_a_tag()
    {
        var id = _context.SeedTag("Old");

        var result = Handler().Handle(new RenameTag(id, "New"));

        Assert.True(result.IsSuccess);
        Assert.Equal("New", _context.NameOf(id));
    }

    [Fact]
    public void Reports_not_found_for_an_unknown_tag()
    {
        var result = Handler().Handle(new RenameTag(TagId.New(), "New"));

        Assert.False(result.IsSuccess);
        Assert.Equal(CommandFailureReason.NotFound, result.Failure!.Reason);
    }

    [Fact]
    public void Checks_existence_before_the_name()
    {
        // A missing tag is NotFound whatever the caller passed, matching every
        // other rename in the engine.
        var result = Handler().Handle(new RenameTag(TagId.New(), "   "));

        Assert.Equal(CommandFailureReason.NotFound, result.Failure!.Reason);
    }

    // ---- §7a normalisation ------------------------------------------------

    [Theory]
    [InlineData(" New", "New")]
    [InlineData("New ", "New")]
    [InlineData("  New  ", "New")]
    [InlineData("\tNew\r\n", "New")]
    public void Trims_before_persisting(string input, string stored)
    {
        var id = _context.SeedTag("Old");

        Handler().Handle(new RenameTag(id, input));

        Assert.Equal(stored, _context.NameOf(id));
    }

    [Fact]
    public void Preserves_internal_whitespace()
    {
        var id = _context.SeedTag("Old");

        Handler().Handle(new RenameTag(id, "  Work  Item  "));

        Assert.Equal("Work  Item", _context.NameOf(id));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData(" \r\n ")]
    public void Rejects_a_name_that_is_empty_after_trimming(string name)
    {
        var id = _context.SeedTag("Old");

        var result = Handler().Handle(new RenameTag(id, name));

        Assert.False(result.IsSuccess);
        Assert.Equal(CommandFailureReason.InvalidInput, result.Failure!.Reason);
        Assert.Equal("Old", _context.NameOf(id));
    }

    // ---- uniqueness -------------------------------------------------------

    [Theory]
    [InlineData("Taken")]
    [InlineData("taken")]
    [InlineData("TAKEN")]
    [InlineData(" Taken ")]
    [InlineData("\ttaken\t")]
    public void Rejects_a_collision_with_another_tag(string attempt)
    {
        _context.SeedTag("Taken");
        var id = _context.SeedTag("Mine");

        var result = Handler().Handle(new RenameTag(id, attempt));

        Assert.False(result.IsSuccess);
        Assert.Equal(CommandFailureReason.DuplicateName, result.Failure!.Reason);
        Assert.Equal("Mine", _context.NameOf(id));
    }

    [Fact]
    public void Allows_renaming_a_tag_to_its_own_current_name()
    {
        // The self-collision case. FindIdByName returns THIS tag's id, which is
        // not a collision — comparing by id rather than by string is what makes
        // the distinction exact.
        var id = _context.SeedTag("Work");

        var result = Handler().Handle(new RenameTag(id, "Work"));

        Assert.True(result.IsSuccess);
        Assert.Equal("Work", _context.NameOf(id));
    }

    [Theory]
    [InlineData(" Work ")]
    [InlineData("work")]
    [InlineData("WORK")]
    public void Allows_a_self_rename_that_only_changes_case_or_whitespace(string attempt)
    {
        // These all resolve to the same tag, so none is a DuplicateName. A
        // case-only rename is a legitimate edit — "work" to "Work".
        var id = _context.SeedTag("Work");

        var result = Handler().Handle(new RenameTag(id, attempt));

        Assert.True(result.IsSuccess);
        Assert.Equal(attempt.Trim(), _context.NameOf(id));
    }

    // ---- unrelated state --------------------------------------------------

    [Fact]
    public void Preserves_created_at()
    {
        var id = _context.SeedTag("Old");
        string created = _context.CreatedAtOf(id)!;

        _context.Clock.Advance(TimeSpan.FromHours(5));

        Handler().Handle(new RenameTag(id, "New"));

        Assert.Equal(created, _context.CreatedAtOf(id));
    }

    [Fact]
    public void Preserves_the_colour_key()
    {
        var id = _context.SeedTag("Old", colorKey: "note3");

        Handler().Handle(new RenameTag(id, "New"));

        using var connection = _context.Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ColorKey FROM Tags WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id.Value);

        Assert.Equal("note3", (string)command.ExecuteScalar()!);
    }

    [Fact]
    public void Leaves_other_tags_untouched()
    {
        var other = _context.SeedTag("Other");
        var id = _context.SeedTag("Old");

        Handler().Handle(new RenameTag(id, "New"));

        Assert.Equal("Other", _context.NameOf(other));
    }

    [Fact]
    public void Persists_across_a_reopen()
    {
        var id = _context.SeedTag("Old");

        Handler().Handle(new RenameTag(id, "  New  "));

        var reopened = new Noto.Infrastructure.Storage.NotoDatabase(_context.DatabasePath);
        var repository = new Noto.Infrastructure.Storage.SqliteTagRepository(reopened);

        Assert.Equal("New", repository.Find(id)!.Name);
    }
}
