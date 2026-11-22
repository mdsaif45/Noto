using Microsoft.Data.Sqlite;
using Noto.Core;
using Noto.Core.Commands;
using Noto.Core.Folders;
using Noto.Core.Notes;
using Noto.Core.Ordering;
using Noto.Infrastructure.Storage;
using Noto.Infrastructure.Tests.Storage;
using Noto.UseCases.Commands;
using Noto.UseCases.Folders;
using Noto.UseCases.Notes;
using Noto.UseCases.Workspace;
using Xunit;

namespace Noto.Infrastructure.Tests.Workspace;

/// <summary>
/// A folder's note sort against real SQLite (#242; parity C10): chosen through the session, kept on the folder, applied
/// to the stack with the pinned first; moving by hand is refused in any sort but Manual, and no sort ever writes a note,
/// so Manual shows the user's order again.
/// </summary>
public sealed class FolderSortTests : IDisposable
{
    private readonly TempDatabase _temp = new();
    private readonly NotoDatabase _database;
    private readonly SqliteNoteRepository _notes;
    private readonly SqliteFolderRepository _folders;
    private readonly Clock _clock = new(new DateTimeOffset(2026, 11, 22, 9, 0, 0, TimeSpan.Zero));
    private readonly NoteStackSession _stack;
    private readonly FolderId _folder;

    public FolderSortTests()
    {
        _database = new NotoDatabase(_temp.DatabasePath);
        _database.Initialize();
        _notes = new SqliteNoteRepository(_database);
        _folders = new SqliteFolderRepository(_database);
        var dispatcher = new CommandDispatcher(
            new CommandRegistry()
                .Register<UpdateNoteContent, CommandResult>(new UpdateNoteContentHandler(_notes, _clock).Handle)
                .Register<PinNote, CommandResult>(new PinNoteHandler(_notes, _clock).Handle)
                .Register<ReorderNote, CommandResult, NoteReordered>(new ReorderNoteHandler(_notes, _clock).Handle, static (c, _) => new NoteReordered(c.NoteId))
                .Register<SetFolderNoteSort, CommandResult>(new SetFolderNoteSortHandler(_folders, _clock).Handle),
            new EventHub(_ => { }));
        _stack = new NoteStackSession(new ListNotesInFolderQuery(_notes), dispatcher);

        _clock.Advance(TimeSpan.FromSeconds(1));
        _folder = new CreateFolderHandler(_folders, _clock).Handle(new CreateFolder("Stack")).Value;

        // Created in this order, so oldest first: "mango", "Apple", "cherry", "banana".
        foreach (string content in new[] { "mango", "Apple", "cherry", "banana" })
        {
            _clock.Advance(TimeSpan.FromMinutes(1));
            _ = new CreateNoteHandler(_notes, _clock).Handle(new CreateNote(_folder, content));
        }

        _stack.Open(_folder);
        _ = _stack.Read();
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Name_sorts_the_stack_keeps_the_choice_on_the_folder_and_writes_no_note()
    {
        string manual = Shown();
        string notes = NoteRows();

        Assert.True(_stack.SetSort(NoteSort.Name).IsSuccess);

        Assert.Equal(NoteSort.Name, _stack.Sort);
        Assert.Equal("Apple banana cherry mango", Shown());
        Assert.Equal(NoteSort.Name, _folders.FindActive(_folder)!.NoteSort);
        Assert.Equal(notes, NoteRows());

        // Reopened, as the window does with the folder's stored sort, the stack is sorted again.
        _stack.Close();
        Assert.Equal(NoteSort.Manual, _stack.Sort);
        _stack.Open(_folder, _folders.FindActive(_folder)!.NoteSort);
        _ = _stack.Read();
        Assert.Equal("Apple banana cherry mango", Shown());

        // Manual restores the order the user made, exactly.
        Assert.True(_stack.SetSort(NoteSort.Manual).IsSuccess);
        Assert.Equal(manual, Shown());
        Assert.Equal(notes, NoteRows());
    }

    [Fact]
    public void Pinned_notes_stay_first_in_every_order()
    {
        Assert.True(_stack.SetPinned(Id("mango"), pinned: true).IsSuccess);

        Assert.True(_stack.SetSort(NoteSort.Name).IsSuccess);
        Assert.Equal("mango Apple banana cherry", Shown());

        Assert.True(_stack.SetSort(NoteSort.Created).IsSuccess);
        Assert.Equal("mango banana cherry Apple", Shown());
    }

    [Fact]
    public void Modified_puts_the_note_saved_last_first_once_the_stack_is_read()
    {
        Assert.True(_stack.SetSort(NoteSort.Modified).IsSuccess);
        Assert.Equal("banana cherry Apple mango", Shown());

        _clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(_stack.BeginEdit(Id("mango")).IsSuccess);
        _stack.UpdateDraft(Id("mango"), "mango tango");

        // A save keeps the card where it is while it is being written in; the next read places it.
        Assert.True(_stack.Save().IsSuccess);
        Assert.Equal("banana cherry Apple mango tango", Shown());
        Assert.True(_stack.EndEdit().IsSuccess);
        _ = _stack.Read();
        Assert.Equal("mango tango banana cherry Apple", Shown());
    }

    [Fact]
    public void Moving_by_hand_is_refused_in_a_sort_and_writes_nothing()
    {
        Assert.True(_stack.SetSort(NoteSort.Name).IsSuccess);
        string notes = NoteRows();
        NoteId cherry = Id("cherry");

        foreach (RepositionDirection direction in Enum.GetValues<RepositionDirection>())
        {
            Assert.False(_stack.CanReorder(cherry, direction));
            CommandResult moved = _stack.Reorder(cherry, direction);
            Assert.Equal(CommandFailureReason.InvalidState, moved.Failure!.Reason);
            Assert.Equal("Sorted by name: choose Manual to arrange notes yourself", moved.Failure.Message);
        }

        Assert.Equal(CommandFailureReason.InvalidState, _stack.Drop(cherry, 0).Failure!.Reason);
        Assert.Equal(notes, NoteRows());

        // Manual allows it again.
        Assert.True(_stack.SetSort(NoteSort.Manual).IsSuccess);
        Assert.True(_stack.CanReorder(cherry, RepositionDirection.Top));
    }

    [Fact]
    public void The_same_sort_again_writes_nothing_and_a_binned_folder_is_refused()
    {
        DateTimeOffset stamped = _folders.FindActive(_folder)!.UpdatedAt;
        _clock.Advance(TimeSpan.FromMinutes(1));

        var handler = new SetFolderNoteSortHandler(_folders, _clock);
        Assert.True(handler.Handle(new SetFolderNoteSort(_folder, NoteSort.Manual)).IsSuccess);
        Assert.Equal(stamped, _folders.FindActive(_folder)!.UpdatedAt);

        Assert.Equal(CommandFailureReason.InvalidInput, handler.Handle(new SetFolderNoteSort(_folder, (NoteSort)99)).Failure!.Reason);

        _folders.SoftDeleteWithNotes(_folder, _clock.UtcNow);
        Assert.Equal(CommandFailureReason.InvalidState, handler.Handle(new SetFolderNoteSort(_folder, NoteSort.Name)).Failure!.Reason);
        Assert.Equal(CommandFailureReason.NotFound, handler.Handle(new SetFolderNoteSort(FolderId.New(), NoteSort.Name)).Failure!.Reason);
    }

    private NoteId Id(string content) => _notes.ListInFolder(_folder).Single(n => n.Content == content).Id;

    private string Shown() => string.Join(' ', _stack.Notes.Select(n => n.Content));

    /// <summary>Every note's order and stamps, as stored: what a sort must never write.</summary>
    private string NoteRows()
    {
        using SqliteConnection connection = _database.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT Id || '|' || IsPinned || '|' || SortOrder || '|' || UpdatedAt FROM Notes ORDER BY Id;";
        using SqliteDataReader reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read())
        {
            rows.Add(reader.GetString(0));
        }

        return string.Join("\n", rows);
    }

    private sealed class Clock(DateTimeOffset start) : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = start;

        public void Advance(TimeSpan by) => UtcNow += by;
    }
}
