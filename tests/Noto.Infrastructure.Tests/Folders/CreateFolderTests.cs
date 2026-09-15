using Noto.Core.Commands;
using Noto.Core.Folders;
using Noto.Core.Identifiers;
using Noto.UseCases.Folders;
using Xunit;

namespace Noto.Infrastructure.Tests.Folders;

/// <summary>
/// <c>CreateFolder</c> against real SQLite (contract §11, command 12).
/// </summary>
public sealed class CreateFolderTests : IDisposable
{
    private readonly FolderTestContext _context = new();

    public void Dispose() => _context.Dispose();

    private CreateFolderHandler Handler() => new(_context.Folders, _context.Clock);

    [Fact]
    public void Creates_a_folder_and_returns_a_ulid()
    {
        var result = Handler().Handle(new CreateFolder("Work"));

        Assert.True(result.IsSuccess);
        Assert.True(Ulid.IsValid(result.Value.Value));
        Assert.Equal("Work", _context.NameOf(result.Value));
    }

    [Fact]
    public void Stamps_created_and_updated_with_one_timestamp()
    {
        // Contract §11 row 12: UpdatedAt = CreatedAt on creation. A folder
        // reporting that it was modified after it was created is a lie under
        // "sort by modified".
        var id = Handler().Handle(new CreateFolder("Work")).Value;

        Assert.Equal(_context.CreatedAtOf(id), _context.UpdatedAtOf(id));
        Assert.Equal(_context.Clock.UtcNow.ToString("O"), _context.CreatedAtOf(id));
    }

    [Fact]
    public void Creates_the_folder_active_and_unpinned()
    {
        var id = Handler().Handle(new CreateFolder("Work")).Value;

        Assert.Null(_context.DeletedAtOf(id));
        Assert.False(_context.IsPinnedOf(id));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\r\n")]
    [InlineData("   \t  ")]
    public void Rejects_an_empty_or_whitespace_name(string name)
    {
        // Design §14: an empty folder name is rejected. Whitespace counts —
        // a folder named " " is indistinguishable from an unnamed one.
        var result = Handler().Handle(new CreateFolder(name));

        Assert.False(result.IsSuccess);
        Assert.Equal(CommandFailureReason.InvalidInput, result.Failure!.Reason);
    }

    [Fact]
    public void Writes_nothing_when_the_name_is_rejected()
    {
        Handler().Handle(new CreateFolder("   "));

        Assert.Empty(_context.ActiveFolderOrder());
    }

    [Fact]
    public void Allows_duplicate_names()
    {
        // Case G: folder names are NOT unique. This is the sensitivity test
        // for accidentally introducing a uniqueness constraint — either as a
        // check in the handler or as a UNIQUE index in the schema.
        var first = Handler().Handle(new CreateFolder("Same"));
        var second = Handler().Handle(new CreateFolder("Same"));
        var third = Handler().Handle(new CreateFolder("Same"));

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.True(third.IsSuccess);

        Assert.Equal(3, _context.ActiveFolderOrder().Count);
        Assert.NotEqual(first.Value, second.Value);
    }

    [Fact]
    public void Places_the_first_folder_at_the_documented_base()
    {
        var id = Handler().Handle(new CreateFolder("First")).Value;

        Assert.Equal(0, _context.SortOrderOf(id));
    }

    [Fact]
    public void Places_each_new_folder_at_the_end()
    {
        // O3: a new folder goes at the end of the collection, max + 1.
        var first = Handler().Handle(new CreateFolder("A")).Value;
        var second = Handler().Handle(new CreateFolder("B")).Value;
        var third = Handler().Handle(new CreateFolder("C")).Value;

        Assert.True(_context.SortOrderOf(second) > _context.SortOrderOf(first));
        Assert.True(_context.SortOrderOf(third) > _context.SortOrderOf(second));

        Assert.Equal(new[] { first, second, third }, _context.ActiveFolderOrder());
    }

    [Fact]
    public void Ignores_deleted_folders_when_placing_at_the_end()
    {
        // I6: deleted rows keep their SortOrder but never participate in
        // ordering. A deleted folder sitting at 100 must not push the next
        // new folder to 101.
        _context.SeedFolder("binned", sortOrder: 100, deletedAt: _context.Clock.UtcNow);
        var active = _context.SeedFolder("active", sortOrder: 5);

        var created = Handler().Handle(new CreateFolder("New")).Value;

        Assert.Equal(6, _context.SortOrderOf(created));
        Assert.Equal(new[] { active, created }, _context.ActiveFolderOrder());
    }

    [Fact]
    public void Persists_across_a_reopen()
    {
        var id = Handler().Handle(new CreateFolder("Durable")).Value;

        // A second repository over the same file, proving the row is on disk
        // rather than in this connection's state.
        var reopened = new Noto.Infrastructure.Storage.NotoDatabase(_context.DatabasePath);
        var repository = new Noto.Infrastructure.Storage.SqliteFolderRepository(reopened);

        Assert.Equal(FolderLifecycle.Active, repository.GetLifecycle(id));
        Assert.Equal("Durable", _context.NameOf(id));
    }
}
