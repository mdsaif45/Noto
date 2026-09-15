using Noto.Core.Folders;
using Xunit;

namespace Noto.Infrastructure.Tests.Folders;

/// <summary>
/// The persistence-layer half of invariant I5, proved without the command
/// layer.
/// </summary>
/// <remarks>
/// <para>
/// The handlers check the lifecycle before writing, so in normal use these
/// paths are already guarded. These tests call the repository <b>directly</b>
/// because the guard has to hold on its own: the note repository keeps
/// <c>AND DeletedAt IS NULL</c> on its single-field updates rather than relying
/// on its callers, and the folder repository must match it — the folder
/// cascades and <c>Reorder</c> already do.
/// </para>
/// <para>
/// What this defends against is the window between the command's check and its
/// write. Last-write-wins is the frozen concurrency model (§10), and this
/// clause is what keeps "last write" from resurrecting a binned folder.
/// </para>
/// </remarks>
public sealed class FolderRepositoryGuardTests : IDisposable
{
    private readonly FolderTestContext _context = new();

    public void Dispose() => _context.Dispose();

    [Fact]
    public void Rename_cannot_modify_a_deleted_folder()
    {
        DateTimeOffset deletedAt = _context.Clock.UtcNow;
        var id = _context.SeedFolder("Original", deletedAt: deletedAt);

        string nameBefore = _context.NameOf(id);
        string updatedBefore = _context.UpdatedAtOf(id);
        string deletedBefore = _context.DeletedAtOf(id)!;

        _context.Clock.Advance(TimeSpan.FromHours(1));

        _context.Folders.Rename(id, "Renamed", _context.Clock.UtcNow);

        Assert.Equal(nameBefore, _context.NameOf(id));
        Assert.Equal(updatedBefore, _context.UpdatedAtOf(id));
        Assert.Equal(deletedBefore, _context.DeletedAtOf(id));
    }

    [Fact]
    public void SetPinned_cannot_modify_a_deleted_folder()
    {
        DateTimeOffset deletedAt = _context.Clock.UtcNow;
        var id = _context.SeedFolder("Binned", pinned: false, deletedAt: deletedAt);

        string updatedBefore = _context.UpdatedAtOf(id);
        string deletedBefore = _context.DeletedAtOf(id)!;

        _context.Clock.Advance(TimeSpan.FromHours(1));

        _context.Folders.SetPinned(id, isPinned: true, _context.Clock.UtcNow);

        Assert.False(_context.IsPinnedOf(id));
        Assert.Equal(updatedBefore, _context.UpdatedAtOf(id));
        Assert.Equal(deletedBefore, _context.DeletedAtOf(id));
    }

    [Fact]
    public void SetPinned_cannot_unpin_a_deleted_folder_either()
    {
        // Both directions, so the guard cannot be half-applied.
        var id = _context.SeedFolder("Binned", pinned: true, deletedAt: _context.Clock.UtcNow);

        _context.Clock.Advance(TimeSpan.FromHours(1));

        _context.Folders.SetPinned(id, isPinned: false, _context.Clock.UtcNow);

        Assert.True(_context.IsPinnedOf(id));
    }

    [Fact]
    public void Rename_still_works_on_an_active_folder()
    {
        // The guard must not have been added by breaking the ordinary path.
        var id = _context.SeedFolder("Original");

        DateTimeOffset renamedAt = _context.Clock.Advance(TimeSpan.FromMinutes(5));

        _context.Folders.Rename(id, "Renamed", renamedAt);

        Assert.Equal("Renamed", _context.NameOf(id));
        Assert.Equal(renamedAt.ToString("O"), _context.UpdatedAtOf(id));
    }

    [Fact]
    public void SetPinned_still_works_on_an_active_folder()
    {
        var id = _context.SeedFolder("Active", pinned: false);

        DateTimeOffset pinnedAt = _context.Clock.Advance(TimeSpan.FromMinutes(5));

        _context.Folders.SetPinned(id, isPinned: true, pinnedAt);

        Assert.True(_context.IsPinnedOf(id));
        Assert.Equal(pinnedAt.ToString("O"), _context.UpdatedAtOf(id));
    }

    [Fact]
    public void FindActive_returns_an_active_folder()
    {
        var id = _context.SeedFolder("Visible", sortOrder: 3, pinned: true);

        Folder? folder = _context.Folders.FindActive(id);

        Assert.NotNull(folder);
        Assert.Equal(id, folder!.Id);
        Assert.Equal("Visible", folder.Name);
        Assert.True(folder.IsPinned);
        Assert.Equal(3, folder.SortOrder);
        Assert.Null(folder.DeletedAt);
    }

    [Fact]
    public void FindActive_applies_I1_and_hides_a_deleted_folder()
    {
        // I1: ordinary reads are active-only. The bin is reached through
        // ListDeleted alone (I2) — there is no includeDeleted flag.
        var id = _context.SeedFolder("Binned", deletedAt: _context.Clock.UtcNow);

        Assert.Null(_context.Folders.FindActive(id));
        Assert.Contains(_context.Folders.ListDeleted(), f => f.Id == id);
    }

    [Fact]
    public void FindActive_returns_null_for_an_unknown_folder()
    {
        Assert.Null(_context.Folders.FindActive(FolderId.New()));
    }
}
