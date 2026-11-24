using Noto.Core.Commands;
using Noto.Core.Folders;
using Noto.Core.Notes;
using Noto.Core.Storage;
using Noto.UseCases.Folders;
using Noto.UseCases.Notes;
using Xunit;

namespace Noto.Infrastructure.Tests.Folders;

/// <summary>
/// <c>DuplicateFolder</c> against real SQLite (#232; parity C12): "&lt;name&gt; copy" right after the original, holding a
/// copy of each active note in order with its pin and fold, binned notes left out, all in one transaction.
/// </summary>
public sealed class DuplicateFolderTests : IDisposable
{
    private readonly FolderTestContext _db = new();

    public void Dispose() => _db.Dispose();

    private FolderId Folder(string name) =>
        new CreateFolderHandler(_db.Folders, _db.Clock).Handle(new CreateFolder(name)).Value;

    private NoteId Note(FolderId folder, string content, NoteMode mode = NoteMode.Markdown) =>
        new CreateNoteHandler(_db.Notes, _db.Clock).Handle(new CreateNote(folder, content, Mode: mode)).Value;

    private CommandResult<FolderId> Duplicate(FolderId id) =>
        new DuplicateFolderHandler(_db.Folders, _db.Notes, _db.Clock).Handle(new DuplicateFolder(id));

    private IEnumerable<string> FolderNames() => _db.Folders.ListActive().Select(f => f.Name);

    [Fact]
    public void The_copy_holds_each_active_note_in_order_with_its_text_colour_mode_pin_and_fold()
    {
        FolderId work = Folder("Work");
        NoteId a = Note(work, "a");
        NoteId b = Note(work, "b", NoteMode.Code);
        NoteId c = Note(work, "c");
        NoteId binned = Note(work, "binned");
        Assert.True(new PinNoteHandler(_db.Notes, _db.Clock).Handle(new PinNote(c)).IsSuccess);
        Assert.True(new FoldNoteHandler(_db.Notes, _db.Clock).Handle(new FoldNote(a)).IsSuccess);
        Assert.True(new SetNoteColorHandler(_db.Notes, _db.Clock).Handle(new SetNoteColor(b, "note5")).IsSuccess);
        Assert.True(new DeleteNoteHandler(_db.Notes, _db.Clock).Handle(new DeleteNote(binned)).IsSuccess);
        _db.Clock.Advance(TimeSpan.FromHours(1));

        CommandResult<FolderId> result = Duplicate(work);

        Assert.True(result.IsSuccess);
        var original = _db.Notes.ListInFolder(work);
        var copies = _db.Notes.ListInFolder(result.Value);
        Assert.Equal(
            original.Select(n => (n.Content, n.ColorKey, n.Mode, n.IsPinned, n.IsFolded)),
            copies.Select(n => (n.Content, n.ColorKey, n.Mode, n.IsPinned, n.IsFolded)));
        Assert.Equal(["c", "a", "b"], copies.Select(n => n.Content));
        Assert.All(copies, n => Assert.Equal((_db.Clock.UtcNow, _db.Clock.UtcNow), (n.CreatedAt, n.UpdatedAt)));
        Assert.Empty(copies.Select(n => n.Id).Intersect(original.Select(n => n.Id)));

        // Binned notes stay where they are: in the original's bin only.
        Assert.DoesNotContain(_db.Notes.ListDeleted(), n => n.FolderId == result.Value);
        Assert.Equal(3, original.Count);
    }

    [Fact]
    public void The_copy_is_named_copy_then_copy_2_and_sits_right_after_the_original()
    {
        FolderId work = Folder("Work");
        FolderId home = Folder("Home");

        FolderId first = Duplicate(work).Value;
        FolderId second = Duplicate(work).Value;

        Assert.Equal(["Work", "Work copy 2", "Work copy", "Home"], FolderNames());
        Assert.Equal("Work copy", _db.NameOf(first));
        Assert.Equal("Work copy 2", _db.NameOf(second));
        Assert.Equal(home, _db.Folders.ListActive()[^1].Id);
    }

    [Fact]
    public void The_copy_keeps_the_colour_and_a_pinned_folders_copy_is_the_first_unpinned_one()
    {
        FolderId work = Folder("Work");
        FolderId home = Folder("Home");
        Assert.True(new PinFolderHandler(_db.Folders, _db.Clock).Handle(new PinFolder(home)).IsSuccess);
        Sql($"UPDATE Folders SET ColorKey = 'note2' WHERE Id = '{home.Value}';");

        FolderId copy = Duplicate(home).Value;

        Assert.Equal(["Home", "Home copy", "Work"], FolderNames());
        Assert.False(_db.IsPinnedOf(copy));
        Assert.Equal("note2", _db.ReadFolderColumn(copy, "ColorKey"));
        Assert.Equal(work, _db.Folders.ListActive()[^1].Id);
    }

    [Fact]
    public void The_copy_keeps_the_note_sort()
    {
        FolderId home = Folder("Home");
        Sql($"UPDATE Folders SET NoteSort = 'Name' WHERE Id = '{home.Value}';");

        FolderId copy = Duplicate(home).Value;

        Assert.Equal("Name", _db.ReadFolderColumn(copy, "NoteSort"));
    }

    [Fact]
    public void An_empty_folder_is_copied_empty()
    {
        FolderId empty = Folder("Empty");

        FolderId copy = Duplicate(empty).Value;

        Assert.Equal("Empty copy", _db.NameOf(copy));
        Assert.Empty(_db.Notes.ListInFolder(copy));
    }

    [Fact]
    public void A_missing_folder_is_NotFound_and_a_binned_one_InvalidState()
    {
        FolderId binned = Folder("Binned");
        Assert.True(new DeleteFolderHandler(_db.Folders, _db.Clock).Handle(new DeleteFolder(binned)).IsSuccess);

        Assert.Equal(CommandFailureReason.NotFound, Duplicate(FolderId.New()).Failure!.Reason);
        Assert.Equal(CommandFailureReason.InvalidState, Duplicate(binned).Failure!.Reason);
        Assert.Empty(_db.Folders.ListActive());
    }

    [Fact]
    public void A_note_that_cannot_be_written_leaves_no_folder_and_no_note()
    {
        FolderId work = Folder("Work");
        Note(work, "a");
        Note(work, "b");
        // The second note's insert fails: the folder and the first note written before it must go too.
        Sql("CREATE TRIGGER inject BEFORE INSERT ON Notes WHEN NEW.Content = 'b' BEGIN SELECT RAISE(ABORT, 'injected'); END;");

        Assert.Throws<StorageException>(() => Duplicate(work));

        Assert.Equal(["Work"], FolderNames());
        Assert.Equal(2, Count("SELECT COUNT(*) FROM Notes;"));
    }

    private void Sql(string sql)
    {
        using var connection = _db.Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private long Count(string sql)
    {
        using var connection = _db.Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)command.ExecuteScalar()!;
    }
}
