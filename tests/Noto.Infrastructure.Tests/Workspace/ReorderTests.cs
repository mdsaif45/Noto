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
/// Phase B3 against real SQLite (workspace-management.md, "B3 precisely"): the Reposition rule and the engine's one
/// order agree. Every move stays inside its pin group, lands exactly where it was asked, and writes nothing at an edge.
/// </summary>
public sealed class ReorderTests : IDisposable
{
    private readonly TempDatabase _temp = new();
    private readonly NotoDatabase _database;
    private readonly SqliteNoteRepository _notes;
    private readonly SqliteFolderRepository _folders;
    private readonly Clock _clock = new(new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero));
    private readonly EventHub _events = new(_ => { });
    private readonly CommandDispatcher _dispatcher;
    private readonly NoteStackSession _stack;
    private readonly FolderId _folder;
    private readonly NoteId _a;
    private readonly NoteId _b;
    private readonly NoteId _c;
    private readonly NoteId _d;
    private readonly NoteId _e;

    public ReorderTests()
    {
        _database = new NotoDatabase(_temp.DatabasePath);
        _database.Initialize();
        _notes = new SqliteNoteRepository(_database);
        _folders = new SqliteFolderRepository(_database);
        _dispatcher = new CommandDispatcher(
            new CommandRegistry()
                .Register<UpdateNoteContent, CommandResult>(new UpdateNoteContentHandler(_notes, _clock).Handle)
                .Register<PinNote, CommandResult>(new PinNoteHandler(_notes, _clock).Handle)
                .Register<ReorderNote, CommandResult, NoteReordered>(new ReorderNoteHandler(_notes, _clock).Handle, static (c, _) => new NoteReordered(c.NoteId))
                .Register<ReorderFolder, CommandResult, FolderReordered>(new ReorderFolderHandler(_folders, _clock).Handle, static (c, _) => new FolderReordered(c.FolderId))
                .Register<PinFolder, CommandResult>(new PinFolderHandler(_folders, _clock).Handle),
            _events);
        _stack = new NoteStackSession(new ListNotesInFolderQuery(_notes), _dispatcher);

        _folder = CreateFolder("Stack");
        _a = Create("A");
        _b = Create("B");
        _c = Create("C");
        _d = Create("D");
        _e = Create("E");

        _stack.Open(_folder);
        _ = _stack.Read();
    }

    public void Dispose() => _temp.Dispose();

    // ---- one group -------------------------------------------------------------------------------------

    [Theory]
    [InlineData("C", RepositionDirection.Up, "ACBDE")]
    [InlineData("B", RepositionDirection.Up, "BACDE")]
    [InlineData("B", RepositionDirection.Down, "ACBDE")]
    [InlineData("D", RepositionDirection.Down, "ABCED")]
    [InlineData("D", RepositionDirection.Top, "DABCE")]
    [InlineData("B", RepositionDirection.Bottom, "ACDEB")]
    public void Each_direction_lands_exactly_where_asked_and_leaves_the_others_in_order(string item, RepositionDirection direction, string expected)
    {
        Assert.True(_stack.Reorder(Id(item), direction).IsSuccess);

        Assert.Equal(expected, Display());
        Assert.Equal(expected, string.Concat(_stack.Notes.Select(n => n.Content)));
    }

    [Theory]
    [InlineData("A", RepositionDirection.Up)]
    [InlineData("A", RepositionDirection.Top)]
    [InlineData("E", RepositionDirection.Down)]
    [InlineData("E", RepositionDirection.Bottom)]
    public void At_an_edge_nothing_is_written_and_nothing_is_announced(string item, RepositionDirection direction)
    {
        var told = new List<NoteReordered>();
        using var subscription = _events.Subscribe<NoteReordered>(told.Add);
        string before = Snapshot();

        Assert.False(_stack.CanReorder(Id(item), direction));
        Assert.True(_stack.Reorder(Id(item), direction).IsSuccess);

        Assert.Equal(before, Snapshot());
        Assert.Empty(told);
    }

    // ---- pinned and unpinned share one SortOrder ----------------------------------------------------------

    [Fact]
    public void Moves_stay_inside_the_pin_group_over_the_shared_SortOrder()
    {
        // Pin B and D: displayed B D | A C E, SortOrders still 1..5.
        Pin(_b);
        Pin(_d);
        Assert.Equal("BDACE", Display());

        Assert.True(_stack.Reorder(_e, RepositionDirection.Up).IsSuccess);
        Assert.Equal("BDAEC", Display());

        Assert.True(_stack.Reorder(_e, RepositionDirection.Top).IsSuccess);
        Assert.Equal("BDEAC", Display());

        Assert.True(_stack.Reorder(_d, RepositionDirection.Up).IsSuccess);
        Assert.Equal("DBEAC", Display());

        Assert.True(_stack.Reorder(_d, RepositionDirection.Bottom).IsSuccess);
        Assert.Equal("BDEAC", Display());
    }

    [Fact]
    public void The_pin_boundary_is_never_crossed_and_nothing_is_written_trying()
    {
        Pin(_c);
        string before = Snapshot();

        Assert.True(_stack.Reorder(_a, RepositionDirection.Up).IsSuccess);
        Assert.True(_stack.Reorder(_c, RepositionDirection.Down).IsSuccess);

        Assert.Equal(before, Snapshot());
        Assert.Equal("CABDE", Display());
    }

    // ---- the editing card, and many moves --------------------------------------------------------------

    [Fact]
    public void The_card_being_edited_keeps_editing_with_its_text_when_it_moves()
    {
        Assert.True(_stack.BeginEdit(_c).IsSuccess);
        _stack.UpdateDraft(_c, "typed, then moved up");

        Assert.True(_stack.Reorder(_c, RepositionDirection.Up).IsSuccess);
        Assert.True(_stack.Reorder(_a, RepositionDirection.Bottom).IsSuccess);

        Assert.Equal(_c, _stack.Editing);
        Assert.Equal("typed, then moved up", _stack.Draft);
        Assert.Equal("C", Note(_c).Content);
        Assert.Equal("CBDEA", Display());
    }

    [Fact]
    public void Many_moves_into_one_gap_renormalise_and_keep_one_strict_order()
    {
        // Each round moves the third card up, into the gap between the first two: that gap halves every round until
        // it runs out and the scope is renormalised (O6). Each round must still put exactly that card second.
        for (int round = 0; round < 70; round++)
        {
            NoteId third = _stack.Notes[2].Id;

            Assert.True(_stack.Reorder(third, RepositionDirection.Up).IsSuccess);

            Assert.Equal(third, _stack.Notes[1].Id);
        }

        List<double> orders = [.. _notes.ListInFolder(_folder).Select(n => n.SortOrder)];
        Assert.Equal(5, orders.Distinct().Count());
        Assert.Equal(orders.OrderBy(o => o), orders);
        Assert.Equal(Display(), string.Concat(_stack.Notes.Select(n => n.Content)));
    }

    [Fact]
    public void A_move_is_announced_after_it_commits()
    {
        var told = new List<NoteReordered>();
        using var subscription = _events.Subscribe<NoteReordered>(e =>
        {
            Assert.Equal("BACDE", Display());
            told.Add(e);
        });

        Assert.True(_stack.Reorder(_b, RepositionDirection.Up).IsSuccess);

        Assert.Equal([new NoteReordered(_b)], told);
    }

    [Fact]
    public void A_note_that_is_not_in_the_stack_does_not_move()
    {
        string before = Snapshot();

        Assert.True(_stack.Reorder(NoteId.New(), RepositionDirection.Up).IsSuccess);
        Assert.False(_stack.CanReorder(NoteId.New(), RepositionDirection.Down));

        Assert.Equal(before, Snapshot());
    }

    // ---- folders ----------------------------------------------------------------------------------------

    [Fact]
    public void Folders_move_within_their_pin_group_through_the_same_rule()
    {
        FolderId two = CreateFolder("Two"), three = CreateFolder("Three"), four = CreateFolder("Four");
        Assert.True(_dispatcher.Dispatch(new PinFolder(three)).IsSuccess);
        Assert.Equal([three, _folder, two, four], FolderOrder());

        MoveFolder(four, RepositionDirection.Top);
        Assert.Equal([three, four, _folder, two], FolderOrder());

        MoveFolder(_folder, RepositionDirection.Down);
        Assert.Equal([three, four, two, _folder], FolderOrder());

        // The pinned folder is alone in its group; the first unpinned one cannot rise above it.
        Assert.False(FolderReposition.For(_folders.ListActive(), three, RepositionDirection.Down).Moves);
        Assert.False(FolderReposition.For(_folders.ListActive(), four, RepositionDirection.Up).Moves);
    }

    [Fact]
    public void A_folder_move_is_announced_after_it_commits()
    {
        FolderId two = CreateFolder("Two");
        var told = new List<FolderReordered>();
        using var subscription = _events.Subscribe<FolderReordered>(e =>
        {
            Assert.Equal([two, _folder], FolderOrder());
            told.Add(e);
        });

        MoveFolder(two, RepositionDirection.Up);

        Assert.Equal([new FolderReordered(two)], told);
    }

    // ---- helpers ------------------------------------------------------------------------------------

    private void MoveFolder(FolderId folder, RepositionDirection direction)
    {
        RepositionTarget<FolderId> target = FolderReposition.For(_folders.ListActive(), folder, direction);
        Assert.True(target.Moves);
        Assert.True(_dispatcher.Dispatch(new ReorderFolder(folder, target.After)).IsSuccess);
    }

    private void Pin(NoteId note)
    {
        Assert.True(_dispatcher.Dispatch(new PinNote(note)).IsSuccess);
        _ = _stack.Read();
    }

    private NoteId Id(string content) => _notes.ListInFolder(_folder).Single(n => n.Content == content).Id;

    private string Display() => string.Concat(_notes.ListInFolder(_folder).Select(n => n.Content));

    private List<FolderId> FolderOrder() => [.. _folders.ListActive().Select(f => f.Id)];

    private Note Note(NoteId id) => _notes.FindActive(id)!;

    private FolderId CreateFolder(string name)
    {
        _clock.Advance(TimeSpan.FromSeconds(1));
        return new CreateFolderHandler(_folders, _clock).Handle(new CreateFolder(name)).Value;
    }

    private NoteId Create(string content)
    {
        _clock.Advance(TimeSpan.FromSeconds(1));
        return new CreateNoteHandler(_notes, _clock).Handle(new CreateNote(_folder, content)).Value;
    }

    private string Snapshot()
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
