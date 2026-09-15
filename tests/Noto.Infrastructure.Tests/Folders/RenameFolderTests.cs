using Noto.Core.Commands;
using Noto.Core.Folders;
using Noto.UseCases.Folders;
using Xunit;

namespace Noto.Infrastructure.Tests.Folders;

/// <summary>
/// <c>RenameFolder</c> against real SQLite (contract §11, command 13).
/// </summary>
public sealed class RenameFolderTests : IDisposable
{
    private readonly FolderTestContext _context = new();

    public void Dispose() => _context.Dispose();

    private RenameFolderHandler Handler() => new(_context.Folders, _context.Clock);

    [Fact]
    public void Renames_an_active_folder()
    {
        var id = _context.SeedFolder("Old");

        var result = Handler().Handle(new RenameFolder(id, "New"));

        Assert.True(result.IsSuccess);
        Assert.Equal("New", _context.NameOf(id));
    }

    [Fact]
    public void Reports_not_found_for_an_unknown_folder()
    {
        var result = Handler().Handle(new RenameFolder(FolderId.New(), "New"));

        Assert.False(result.IsSuccess);
        Assert.Equal(CommandFailureReason.NotFound, result.Failure!.Reason);
    }

    [Fact]
    public void Reports_invalid_state_for_a_deleted_folder()
    {
        // I5: a deleted folder is inert. InvalidState, not NotFound — the two
        // are different instructions to the caller.
        var id = _context.SeedFolder("Binned", deletedAt: _context.Clock.UtcNow);

        var result = Handler().Handle(new RenameFolder(id, "New"));

        Assert.False(result.IsSuccess);
        Assert.Equal(CommandFailureReason.InvalidState, result.Failure!.Reason);
        Assert.Equal("Binned", _context.NameOf(id));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    public void Rejects_an_empty_or_whitespace_name(string name)
    {
        var id = _context.SeedFolder("Old");

        var result = Handler().Handle(new RenameFolder(id, name));

        Assert.False(result.IsSuccess);
        Assert.Equal(CommandFailureReason.InvalidInput, result.Failure!.Reason);
        Assert.Equal("Old", _context.NameOf(id));
    }

    [Fact]
    public void Checks_the_lifecycle_before_the_name()
    {
        // Contract §7 orders the precondition first, so a deleted folder
        // reports InvalidState whatever the caller passed as a name. Without
        // that ordering the same call would report InvalidInput and the caller
        // would never learn the folder is in the bin.
        var id = _context.SeedFolder("Binned", deletedAt: _context.Clock.UtcNow);

        var result = Handler().Handle(new RenameFolder(id, ""));

        Assert.Equal(CommandFailureReason.InvalidState, result.Failure!.Reason);
    }

    [Fact]
    public void Allows_a_duplicate_name()
    {
        // Case G: folder names are not unique, so renaming onto an existing
        // name succeeds. DuplicateName must NOT appear here.
        _context.SeedFolder("Taken");
        var id = _context.SeedFolder("Mine");

        var result = Handler().Handle(new RenameFolder(id, "Taken"));

        Assert.True(result.IsSuccess);
        Assert.Equal("Taken", _context.NameOf(id));
    }

    [Fact]
    public void Preserves_created_at_and_stamps_updated_at()
    {
        var created = _context.Clock.UtcNow;
        var id = _context.SeedFolder("Old");

        DateTimeOffset renamedAt = _context.Clock.Advance(TimeSpan.FromMinutes(5));

        Handler().Handle(new RenameFolder(id, "New"));

        Assert.Equal(created.ToString("O"), _context.CreatedAtOf(id));
        Assert.Equal(renamedAt.ToString("O"), _context.UpdatedAtOf(id));
    }

    [Fact]
    public void Leaves_sort_order_and_pinned_untouched()
    {
        // Contract §11 row 13: replace Name, stamp. Nothing else.
        var id = _context.SeedFolder("Old", sortOrder: 7, pinned: true);

        Handler().Handle(new RenameFolder(id, "New"));

        Assert.Equal(7, _context.SortOrderOf(id));
        Assert.True(_context.IsPinnedOf(id));
    }

    [Fact]
    public void Leaves_other_folders_untouched()
    {
        var other = _context.SeedFolder("Other", sortOrder: 3);
        var id = _context.SeedFolder("Old", sortOrder: 1);

        Handler().Handle(new RenameFolder(id, "New"));

        Assert.Equal("Other", _context.NameOf(other));
        Assert.Equal(3, _context.SortOrderOf(other));
    }
}
