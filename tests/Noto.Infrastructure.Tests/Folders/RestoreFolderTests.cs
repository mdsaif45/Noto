using Noto.Core.Commands;
using Noto.Core.Folders;
using Noto.Core.Notes;
using Noto.UseCases.Folders;
using Xunit;

namespace Noto.Infrastructure.Tests.Folders;

/// <summary>
/// <c>RestoreFolder</c> against real SQLite — Case D
/// (contract §8, §11 command 15).
/// </summary>
public sealed class RestoreFolderTests : IDisposable
{
    private readonly FolderTestContext _context = new();

    public void Dispose() => _context.Dispose();

    private RestoreFolderHandler Handler() => new(_context.Folders, _context.Clock);

    private DeleteFolderHandler Delete() => new(_context.Folders, _context.Clock);

    [Fact]
    public void Restores_a_deleted_folder()
    {
        var folder = _context.SeedFolder(deletedAt: _context.Clock.UtcNow);

        var result = Handler().Handle(new RestoreFolder(folder));

        Assert.True(result.IsSuccess);
        Assert.Null(_context.DeletedAtOf(folder));
    }

    [Fact]
    public void Case_D_restores_every_still_deleted_note_pointing_at_the_folder()
    {
        // Contract §8 Case D, stated there as SQL:
        //   UPDATE Notes SET DeletedAt = NULL
        //   WHERE FolderId = @F AND DeletedAt IS NOT NULL;
        //
        // Note A was deleted INDEPENDENTLY, before the folder. It still comes
        // back, because nothing records WHY a note was deleted and
        // deletion-semantics §5 forbids the column that would.
        var folder = _context.SeedFolder();
        var independentlyDeleted = _context.SeedNote(folder, content: "A");

        _context.Clock.Advance(TimeSpan.FromHours(1));
        _context.Notes.SoftDelete(independentlyDeleted, _context.Clock.UtcNow);

        var cascaded = _context.SeedNote(folder, content: "B");

        _context.Clock.Advance(TimeSpan.FromHours(1));
        Delete().Handle(new DeleteFolder(folder));

        Assert.NotNull(_context.DeletedAtOf(independentlyDeleted));
        Assert.NotNull(_context.DeletedAtOf(cascaded));

        _context.Clock.Advance(TimeSpan.FromHours(1));

        var result = Handler().Handle(new RestoreFolder(folder));

        Assert.True(result.IsSuccess);
        Assert.Null(_context.DeletedAtOf(folder));
        Assert.Null(_context.DeletedAtOf(cascaded));

        // The one that distinguishes Case D from the intuitive-but-wrong
        // reading. An implementation restoring only what the folder's deletion
        // had cascaded would leave this note in the bin.
        Assert.Null(_context.DeletedAtOf(independentlyDeleted));
    }

    [Fact]
    public void Case_D_leaves_unrelated_deleted_notes_in_the_bin()
    {
        // I3: restore is scoped. Notes in another folder and at root stay
        // deleted — the sensitivity test for a cascade that forgot its
        // FolderId predicate.
        var folder = _context.SeedFolder("target");
        var other = _context.SeedFolder("other");

        var mine = _context.SeedNote(folder, deletedAt: _context.Clock.UtcNow);
        var theirs = _context.SeedNote(other, deletedAt: _context.Clock.UtcNow);
        var atRoot = _context.SeedNote(null, deletedAt: _context.Clock.UtcNow);

        _context.Clock.Advance(TimeSpan.FromMinutes(5));
        Delete().Handle(new DeleteFolder(folder));

        _context.Clock.Advance(TimeSpan.FromMinutes(5));
        Handler().Handle(new RestoreFolder(folder));

        Assert.Null(_context.DeletedAtOf(mine));
        Assert.NotNull(_context.DeletedAtOf(theirs));
        Assert.NotNull(_context.DeletedAtOf(atRoot));

        // The other folder was never deleted, and the restore must not have
        // changed its state either way.
        Assert.Null(_context.DeletedAtOf(other));
    }

    [Fact]
    public void Uses_one_timestamp_for_the_folder_and_every_restored_note()
    {
        // Contract §4 again, on the restore side.
        var folder = _context.SeedFolder();
        var notes = Enumerable.Range(0, 5).Select(_ => _context.SeedNote(folder)).ToList();

        Delete().Handle(new DeleteFolder(folder));

        _context.Clock.Advance(TimeSpan.FromHours(2));
        string expected = _context.Clock.UtcNow.ToString("O");

        Handler().Handle(new RestoreFolder(folder));

        Assert.Equal(expected, (string)_context.ReadFolderColumn(folder, "UpdatedAt")!);

        foreach (NoteId note in notes)
        {
            Assert.Equal(expected, (string)_context.ReadNoteColumn(note, "UpdatedAt")!);
        }
    }

    [Fact]
    public void Reports_not_found_for_an_unknown_folder()
    {
        var result = Handler().Handle(new RestoreFolder(FolderId.New()));

        Assert.Equal(CommandFailureReason.NotFound, result.Failure!.Reason);
    }

    [Fact]
    public void Reports_invalid_state_for_a_folder_that_is_not_deleted()
    {
        // Contract §6: restoring something never in the bin is a caller
        // mistake. It also matters behaviourally — a silent success here would
        // drag every deleted note of a LIVE folder back out.
        var folder = _context.SeedFolder();
        var deletedNote = _context.SeedNote(folder, deletedAt: _context.Clock.UtcNow);

        var result = Handler().Handle(new RestoreFolder(folder));

        Assert.False(result.IsSuccess);
        Assert.Equal(CommandFailureReason.InvalidState, result.Failure!.Reason);
        Assert.NotNull(_context.DeletedAtOf(deletedNote));
    }

    [Fact]
    public void Case_G_allows_the_restored_name_to_collide()
    {
        // Folder names are not unique, so a folder restored into a collision
        // is fine. No DuplicateName anywhere.
        var existing = _context.SeedFolder("Same");
        var restored = _context.SeedFolder("Same", deletedAt: _context.Clock.UtcNow);

        var result = Handler().Handle(new RestoreFolder(restored));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, _context.ActiveFolderOrder().Count);
        Assert.Equal("Same", _context.NameOf(existing));
        Assert.Equal("Same", _context.NameOf(restored));
    }

    [Fact]
    public void Restores_the_folder_to_its_old_position()
    {
        // I6: the deleted row kept its SortOrder, so restore needs no
        // placement logic at all.
        var first = _context.SeedFolder("A", sortOrder: 1);
        var middle = _context.SeedFolder("B", sortOrder: 2);
        var last = _context.SeedFolder("C", sortOrder: 3);

        Delete().Handle(new DeleteFolder(middle));
        Assert.Equal(new[] { first, last }, _context.ActiveFolderOrder());

        Handler().Handle(new RestoreFolder(middle));

        Assert.Equal(new[] { first, middle, last }, _context.ActiveFolderOrder());
        Assert.Equal(2, _context.SortOrderOf(middle));
    }

    [Fact]
    public void Round_trips_a_delete_and_restore()
    {
        var folder = _context.SeedFolder("Work", sortOrder: 4, pinned: true);
        var note = _context.SeedNote(folder, content: "kept");

        Delete().Handle(new DeleteFolder(folder));
        _context.Clock.Advance(TimeSpan.FromMinutes(1));
        Handler().Handle(new RestoreFolder(folder));

        Assert.Null(_context.DeletedAtOf(folder));
        Assert.Null(_context.DeletedAtOf(note));
        Assert.Equal("Work", _context.NameOf(folder));
        Assert.Equal(4, _context.SortOrderOf(folder));
        Assert.True(_context.IsPinnedOf(folder));
        Assert.Equal("kept", (string)_context.ReadNoteColumn(note, "Content")!);
    }

    [Fact]
    public void Empties_the_bin_entry_for_the_folder()
    {
        var folder = _context.SeedFolder(deletedAt: _context.Clock.UtcNow);

        Handler().Handle(new RestoreFolder(folder));

        Assert.Empty(_context.Folders.ListDeleted());
    }

    [Fact]
    public void The_cascade_is_atomic()
    {
        var folder = _context.SeedFolder();
        var notes = Enumerable.Range(0, 3).Select(_ => _context.SeedNote(folder)).ToList();

        Delete().Handle(new DeleteFolder(folder));
        _context.Clock.Advance(TimeSpan.FromMinutes(1));
        Handler().Handle(new RestoreFolder(folder));

        // Every row the operation touched carries the same instant, and none
        // was left behind: a non-transactional implementation could be
        // interrupted between the folder and the notes.
        string stamp = (string)_context.ReadFolderColumn(folder, "UpdatedAt")!;

        Assert.All(notes, note => Assert.Null(_context.DeletedAtOf(note)));
        Assert.All(
            notes,
            note => Assert.Equal(stamp, (string)_context.ReadNoteColumn(note, "UpdatedAt")!));
    }

    [Fact]
    public void Persists_across_a_reopen()
    {
        var folder = _context.SeedFolder(deletedAt: _context.Clock.UtcNow);
        var note = _context.SeedNote(folder, deletedAt: _context.Clock.UtcNow);

        Handler().Handle(new RestoreFolder(folder));

        var reopened = new Noto.Infrastructure.Storage.NotoDatabase(_context.DatabasePath);
        var repository = new Noto.Infrastructure.Storage.SqliteFolderRepository(reopened);

        Assert.Equal(FolderLifecycle.Active, repository.GetLifecycle(folder));
        Assert.Null(_context.DeletedAtOf(note));
    }
}
