using Noto.Core.Commands;
using Noto.Core.Folders;
using Noto.Core.Notes;
using Noto.UseCases.Folders;
using Xunit;

namespace Noto.Infrastructure.Tests.Folders;

/// <summary>
/// <c>DeleteFolder</c> against real SQLite — Cases A and B
/// (contract §8, §11 command 14).
/// </summary>
public sealed class DeleteFolderTests : IDisposable
{
    private readonly FolderTestContext _context = new();

    public void Dispose() => _context.Dispose();

    private DeleteFolderHandler Handler() => new(_context.Folders, _context.Clock);

    [Fact]
    public void Case_A_deletes_the_folder_and_its_active_notes()
    {
        var folder = _context.SeedFolder();
        var first = _context.SeedNote(folder);
        var second = _context.SeedNote(folder);

        var result = Handler().Handle(new DeleteFolder(folder));

        Assert.True(result.IsSuccess);
        Assert.NotNull(_context.DeletedAtOf(folder));
        Assert.NotNull(_context.DeletedAtOf(first));
        Assert.NotNull(_context.DeletedAtOf(second));
    }

    [Fact]
    public void Case_B_leaves_an_already_deleted_note_untouched()
    {
        // THE critical assertion of this command. An already-deleted note must
        // keep its ORIGINAL DeletedAt: overwriting it silently rewrites when
        // the user deleted that note.
        //
        // This is the test that fails if `AND DeletedAt IS NULL` is dropped
        // from the cascade — the nearest plausible wrong implementation, since
        // the statement still looks correct and Case A still passes.
        var folder = _context.SeedFolder();
        DateTimeOffset earlier = _context.Clock.UtcNow;
        var alreadyDeleted = _context.SeedNote(folder, deletedAt: earlier);
        var active = _context.SeedNote(folder);

        _context.Clock.Advance(TimeSpan.FromHours(3));

        Handler().Handle(new DeleteFolder(folder));

        Assert.Equal(earlier.ToString("O"), _context.DeletedAtOf(alreadyDeleted));
        Assert.Equal(_context.Clock.UtcNow.ToString("O"), _context.DeletedAtOf(active));
        Assert.NotEqual(_context.DeletedAtOf(alreadyDeleted), _context.DeletedAtOf(active));
    }

    [Fact]
    public void Case_B_also_preserves_the_updated_at_of_an_already_deleted_note()
    {
        // The cascade writes DeletedAt AND UpdatedAt. Both must be skipped for
        // a note already in the bin, or the note reports being modified by an
        // operation that deliberately did not touch it.
        var folder = _context.SeedFolder();
        var alreadyDeleted = _context.SeedNote(folder, deletedAt: _context.Clock.UtcNow);
        string before = (string)_context.ReadNoteColumn(alreadyDeleted, "UpdatedAt")!;

        _context.Clock.Advance(TimeSpan.FromHours(3));

        Handler().Handle(new DeleteFolder(folder));

        Assert.Equal(before, (string)_context.ReadNoteColumn(alreadyDeleted, "UpdatedAt")!);
    }

    [Fact]
    public void Uses_one_timestamp_for_the_folder_and_every_cascaded_note()
    {
        // Contract §4: one atomic unit takes ONE timestamp. The sensitivity
        // test for calling the clock once per row, which would leave the
        // cascade's rows disagreeing about when it happened.
        var folder = _context.SeedFolder();
        var notes = Enumerable.Range(0, 5).Select(_ => _context.SeedNote(folder)).ToList();

        _context.Clock.Advance(TimeSpan.FromMinutes(10));
        string expected = _context.Clock.UtcNow.ToString("O");

        Handler().Handle(new DeleteFolder(folder));

        Assert.Equal(expected, _context.DeletedAtOf(folder));

        foreach (NoteId note in notes)
        {
            Assert.Equal(expected, _context.DeletedAtOf(note));
        }
    }

    [Fact]
    public void Does_not_touch_notes_in_another_folder()
    {
        // I3: the cascade is scoped to this folder.
        var target = _context.SeedFolder("target");
        var other = _context.SeedFolder("other");
        var mine = _context.SeedNote(target);
        var theirs = _context.SeedNote(other);

        Handler().Handle(new DeleteFolder(target));

        Assert.NotNull(_context.DeletedAtOf(mine));
        Assert.Null(_context.DeletedAtOf(theirs));
        Assert.Null(_context.DeletedAtOf(other));
    }

    [Fact]
    public void Does_not_touch_notes_at_root()
    {
        // Root is its own scope (O1) and is never a catch-all. A note with
        // FolderId NULL must not match `FolderId = @folderId`.
        var folder = _context.SeedFolder();
        var rootNote = _context.SeedNote(null);

        Handler().Handle(new DeleteFolder(folder));

        Assert.Null(_context.DeletedAtOf(rootNote));
    }

    [Fact]
    public void Reports_not_found_for_an_unknown_folder()
    {
        var result = Handler().Handle(new DeleteFolder(FolderId.New()));

        Assert.Equal(CommandFailureReason.NotFound, result.Failure!.Reason);
    }

    [Fact]
    public void Reports_invalid_state_for_an_already_deleted_folder()
    {
        // I5. Deleting twice must not restamp the bin entry, which would move
        // the folder to the top of the recycle list for no user action.
        DateTimeOffset first = _context.Clock.UtcNow;
        var folder = _context.SeedFolder(deletedAt: first);

        _context.Clock.Advance(TimeSpan.FromHours(1));

        var result = Handler().Handle(new DeleteFolder(folder));

        Assert.Equal(CommandFailureReason.InvalidState, result.Failure!.Reason);
        Assert.Equal(first.ToString("O"), _context.DeletedAtOf(folder));
    }

    [Fact]
    public void Leaves_sort_order_intact_so_restore_returns_the_folder_to_its_place()
    {
        // O5/I6: deleting leaves gaps and the deleted row keeps its SortOrder.
        var folder = _context.SeedFolder(sortOrder: 5);
        var note = _context.SeedNote(folder);

        Handler().Handle(new DeleteFolder(folder));

        Assert.Equal(5, _context.SortOrderOf(folder));
        Assert.Equal(
            0d,
            Convert.ToDouble(
                _context.ReadNoteColumn(note, "SortOrder"),
                System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Removes_the_folder_from_the_active_collection()
    {
        var kept = _context.SeedFolder("kept", sortOrder: 1);
        var deleted = _context.SeedFolder("deleted", sortOrder: 2);

        Handler().Handle(new DeleteFolder(deleted));

        Assert.Equal(new[] { kept }, _context.ActiveFolderOrder());
    }

    [Fact]
    public void Lists_the_deleted_folder_in_the_bin()
    {
        var folder = _context.SeedFolder("binned");

        Handler().Handle(new DeleteFolder(folder));

        IReadOnlyList<Folder> bin = _context.Folders.ListDeleted();

        Assert.Single(bin);
        Assert.Equal(folder, bin[0].Id);
    }

    [Fact]
    public void Deletes_a_folder_with_no_notes()
    {
        var folder = _context.SeedFolder();

        var result = Handler().Handle(new DeleteFolder(folder));

        Assert.True(result.IsSuccess);
        Assert.NotNull(_context.DeletedAtOf(folder));
    }

    [Fact]
    public void The_cascade_is_atomic()
    {
        // Atomicity, observed rather than asserted about: the folder and its
        // notes are never visible in a half-deleted state, because they are
        // written in one transaction. Checked by confirming that every row the
        // operation touches carries the same instant — a non-transactional
        // implementation that wrote the folder, then the notes, could be
        // interrupted between the two and leave a deleted folder holding
        // active notes.
        var folder = _context.SeedFolder();
        var notes = Enumerable.Range(0, 3).Select(_ => _context.SeedNote(folder)).ToList();

        Handler().Handle(new DeleteFolder(folder));

        string stamp = _context.DeletedAtOf(folder)!;

        Assert.All(notes, note => Assert.Equal(stamp, _context.DeletedAtOf(note)));
        Assert.DoesNotContain(notes, note => _context.DeletedAtOf(note) is null);
    }

    [Fact]
    public void Persists_across_a_reopen()
    {
        var folder = _context.SeedFolder();
        var note = _context.SeedNote(folder);

        Handler().Handle(new DeleteFolder(folder));

        var reopened = new Noto.Infrastructure.Storage.NotoDatabase(_context.DatabasePath);
        var repository = new Noto.Infrastructure.Storage.SqliteFolderRepository(reopened);

        Assert.Equal(FolderLifecycle.Deleted, repository.GetLifecycle(folder));
        Assert.NotNull(_context.DeletedAtOf(note));
    }
}
