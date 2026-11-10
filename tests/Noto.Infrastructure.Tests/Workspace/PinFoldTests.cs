using Microsoft.Data.Sqlite;
using Noto.Core;
using Noto.Core.Commands;
using Noto.Core.Folders;
using Noto.Core.Notes;
using Noto.Core.Storage;
using Noto.Infrastructure.Storage;
using Noto.Infrastructure.Tests.Storage;
using Noto.UseCases.Commands;
using Noto.UseCases.Folders;
using Noto.UseCases.Notes;
using Noto.UseCases.Workspace;
using Xunit;

namespace Noto.Infrastructure.Tests.Workspace;

/// <summary>
/// Phase B2 against real SQLite (workspace-management.md §3, "B2 precisely"): pin and fold through the stack's
/// session. Pinning is O2's partition over the shared SortOrder, never a second order. Folding never touches content.
/// The card being edited never loses its text.
/// </summary>
public sealed class PinFoldTests : IDisposable
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
    private readonly FolderId _other;
    private readonly NoteId _a;
    private readonly NoteId _b;
    private readonly NoteId _c;
    private readonly NoteId _d;

    public PinFoldTests()
    {
        _database = new NotoDatabase(_temp.DatabasePath);
        _database.Initialize();
        _notes = new SqliteNoteRepository(_database);
        _folders = new SqliteFolderRepository(_database);
        _dispatcher = new CommandDispatcher(Registry(new UpdateNoteContentHandler(_notes, _clock).Handle, new UnfoldNoteHandler(_notes, _clock).Handle), _events);
        _stack = new NoteStackSession(new ListNotesInFolderQuery(_notes), _dispatcher);

        _folder = CreateFolder("Stack");
        _other = CreateFolder("Other");
        _a = Create("Alpha");
        _b = Create("Beta");
        _c = Create("Gamma");
        _d = Create("Delta");

        _stack.Open(_folder);
        _ = _stack.Read();
    }

    public void Dispose() => _temp.Dispose();

    // ---- pin: O2's partition over one SortOrder --------------------------------------------------------

    [Fact]
    public void Pinning_the_first_note_keeps_it_first_and_pinned()
    {
        Assert.True(_stack.SetPinned(_a, true).IsSuccess);

        Assert.Equal([_a, _b, _c, _d], Order());
        Assert.True(Note(_a).IsPinned);
    }

    [Fact]
    public void Pinning_a_middle_note_brings_it_to_the_top_and_leaves_the_others_in_order()
    {
        Assert.True(_stack.SetPinned(_c, true).IsSuccess);

        Assert.Equal([_c, _a, _b, _d], Order());
        Assert.Equal(Order(), _stack.Notes.Select(n => n.Id));
    }

    [Fact]
    public void Pinning_the_last_note_brings_it_to_the_top()
    {
        Assert.True(_stack.SetPinned(_d, true).IsSuccess);

        Assert.Equal([_d, _a, _b, _c], Order());
    }

    [Fact]
    public void Pinning_never_writes_SortOrder_so_unpinning_returns_the_note_to_its_place()
    {
        double before = Note(_c).SortOrder;

        Assert.True(_stack.SetPinned(_c, true).IsSuccess);
        Assert.Equal(before, Note(_c).SortOrder);
        Assert.True(_stack.SetPinned(_c, false).IsSuccess);

        Assert.Equal([_a, _b, _c, _d], Order());
        Assert.False(Note(_c).IsPinned);
    }

    [Fact]
    public void Several_pinned_notes_keep_their_SortOrder_among_themselves()
    {
        Assert.True(_stack.SetPinned(_d, true).IsSuccess);
        Assert.True(_stack.SetPinned(_b, true).IsSuccess);

        Assert.Equal([_b, _d, _a, _c], Order());
    }

    [Fact]
    public void Pinning_changes_nothing_but_the_flag_and_its_stamp()
    {
        string before = Row(_c, withStamp: false);

        Assert.True(_stack.SetPinned(_c, true).IsSuccess);

        // Id|IsPinned|IsFolded|SortOrder|Content: only IsPinned differs.
        Assert.Equal(before.Replace($"{_c.Value}|0|", $"{_c.Value}|1|", StringComparison.Ordinal), Row(_c, withStamp: false));
    }

    [Fact]
    public void The_card_being_edited_keeps_editing_with_its_text_when_pinned_or_unpinned()
    {
        Assert.True(_stack.BeginEdit(_c).IsSuccess);
        _stack.UpdateDraft(_c, "typed, then pinned");

        Assert.True(_stack.SetPinned(_c, true).IsSuccess);
        Assert.Equal(_c, _stack.Editing);
        Assert.Equal("typed, then pinned", _stack.Draft);

        Assert.True(_stack.SetPinned(_c, false).IsSuccess);
        Assert.Equal(_c, _stack.Editing);
        Assert.Equal("typed, then pinned", _stack.Draft);
        Assert.Equal("Gamma", Note(_c).Content);
    }

    [Fact]
    public void Pinning_a_binned_note_is_refused_and_changes_nothing()
    {
        Bin(_c);
        string before = Snapshot();

        Assert.Equal(CommandFailureReason.InvalidState, _stack.SetPinned(_c, true).Failure!.Reason);

        Assert.Equal(before, Snapshot());
    }

    // ---- fold ---------------------------------------------------------------------------------------

    [Fact]
    public void Folding_and_unfolding_an_unpinned_note_is_stored_and_touches_no_content()
    {
        Assert.True(_stack.SetFolded(_b, true).IsSuccess);
        Assert.True(Note(_b).IsFolded);
        Assert.True(_stack.Notes.Single(n => n.Id == _b).IsFolded);

        Assert.True(_stack.SetFolded(_b, false).IsSuccess);
        Assert.False(Note(_b).IsFolded);
        Assert.False(_stack.Notes.Single(n => n.Id == _b).IsFolded);
        Assert.Equal("Beta", Note(_b).Content);
        Assert.Equal([_a, _b, _c, _d], Order());
    }

    [Fact]
    public void Folding_a_pinned_note_keeps_it_pinned_and_first()
    {
        Assert.True(_stack.SetPinned(_c, true).IsSuccess);

        Assert.True(_stack.SetFolded(_c, true).IsSuccess);

        Assert.True(Note(_c).IsPinned);
        Assert.True(Note(_c).IsFolded);
        Assert.Equal([_c, _a, _b, _d], Order());
    }

    [Fact]
    public void Pinning_and_unpinning_a_folded_note_keeps_it_folded()
    {
        Assert.True(_stack.SetFolded(_d, true).IsSuccess);

        Assert.True(_stack.SetPinned(_d, true).IsSuccess);
        Assert.True(Note(_d).IsFolded);
        Assert.True(_stack.Notes.Single(n => n.Id == _d).IsFolded);

        Assert.True(_stack.SetPinned(_d, false).IsSuccess);
        Assert.True(Note(_d).IsFolded);
        Assert.Equal([_a, _b, _c, _d], Order());
    }

    // ---- colour (F7, #150): B15, G23, G24 ---------------------------------------------------------------

    [Fact]
    public void Colouring_a_note_stores_its_key_and_clearing_it_stores_none()
    {
        Assert.True(_stack.SetColor(_c, "note3").IsSuccess);
        Assert.Equal("note3", Note(_c).ColorKey);
        Assert.Equal("note3", _stack.Notes.Single(n => n.Id == _c).ColorKey);

        Assert.True(_stack.SetColor(_c, null).IsSuccess);
        Assert.Null(Note(_c).ColorKey);
        Assert.Null(_stack.Notes.Single(n => n.Id == _c).ColorKey);
        Assert.Equal([_a, _b, _c, _d], Order());
    }

    [Fact]
    public void Colouring_the_card_being_edited_keeps_it_editing_with_its_text()
    {
        Assert.True(_stack.BeginEdit(_b).IsSuccess);
        _stack.UpdateDraft(_b, "typed, then coloured");

        Assert.True(_stack.SetColor(_b, "note1").IsSuccess);

        Assert.Equal("note1", Note(_b).ColorKey);
        Assert.Equal(_b, _stack.Editing);
        Assert.Equal("typed, then coloured", _stack.Draft);
    }

    [Fact]
    public void A_key_outside_the_palette_is_refused_and_changes_nothing()
    {
        Assert.True(_stack.SetColor(_a, "note2").IsSuccess);

        Assert.Equal(CommandFailureReason.InvalidInput, _stack.SetColor(_a, "yellow").Failure!.Reason);

        Assert.Equal("note2", Note(_a).ColorKey);
        Assert.Equal("note2", _stack.Notes.Single(n => n.Id == _a).ColorKey);
    }

    [Fact]
    public void Folding_the_card_being_edited_saves_it_and_leaves_editing_first()
    {
        Assert.True(_stack.BeginEdit(_b).IsSuccess);
        _stack.UpdateDraft(_b, "typed, then folded");

        Assert.True(_stack.SetFolded(_b, true).IsSuccess);

        Assert.Equal("typed, then folded", Note(_b).Content);
        Assert.True(Note(_b).IsFolded);
        Assert.Null(_stack.Editing);
    }

    [Fact]
    public void A_failed_save_folds_nothing_and_keeps_the_card_editing_with_its_text()
    {
        Assert.True(_stack.BeginEdit(_b).IsSuccess);
        _stack.UpdateDraft(_b, "typed");
        Sql("CREATE TRIGGER inject BEFORE UPDATE OF Content ON Notes BEGIN SELECT RAISE(ABORT, 'injected'); END;");

        Assert.Throws<StorageException>(() => _stack.SetFolded(_b, true));

        Assert.False(Note(_b).IsFolded);
        Assert.Equal(_b, _stack.Editing);
        Assert.Equal("typed", _stack.Draft);
    }

    [Fact]
    public void A_refused_save_folds_nothing()
    {
        var stack = Session(updateContent: _ => CommandResult.Failed(CommandFailure.InvalidInput("refused")));
        Assert.True(stack.BeginEdit(_b).IsSuccess);
        stack.UpdateDraft(_b, "typed");

        Assert.Equal(CommandFailureReason.InvalidInput, stack.SetFolded(_b, true).Failure!.Reason);

        Assert.False(Note(_b).IsFolded);
        Assert.Equal(_b, stack.Editing);
    }

    [Fact]
    public void Folding_another_card_or_unfolding_keeps_the_card_being_edited_with_its_text()
    {
        Assert.True(_stack.SetFolded(_d, true).IsSuccess);
        Assert.True(_stack.BeginEdit(_a).IsSuccess);
        _stack.UpdateDraft(_a, "still typing");

        Assert.True(_stack.SetFolded(_c, true).IsSuccess);
        Assert.True(_stack.SetFolded(_d, false).IsSuccess);

        Assert.Equal(_a, _stack.Editing);
        Assert.Equal("still typing", _stack.Draft);
    }

    [Fact]
    public void Unfolding_the_card_being_edited_keeps_it_editing_with_its_text()
    {
        Assert.True(_stack.BeginEdit(_a).IsSuccess);
        _stack.UpdateDraft(_a, "still typing");

        Assert.True(_stack.SetFolded(_a, false).IsSuccess);

        Assert.Equal(_a, _stack.Editing);
        Assert.Equal("still typing", _stack.Draft);
        Assert.Equal("Alpha", Note(_a).Content);
    }

    [Fact]
    public void Editing_a_folded_card_unfolds_it_and_the_unfold_is_stored()
    {
        Assert.True(_stack.SetFolded(_c, true).IsSuccess);

        Assert.True(_stack.BeginEdit(_c).IsSuccess);

        Assert.False(Note(_c).IsFolded);
        Assert.False(_stack.Notes.Single(n => n.Id == _c).IsFolded);
        Assert.Equal(_c, _stack.Editing);
        Assert.Equal("Gamma", _stack.Draft);
    }

    [Fact]
    public void A_refused_unfold_edits_nothing()
    {
        Assert.True(_stack.SetFolded(_c, true).IsSuccess);
        var stack = Session(unfold: _ => CommandResult.Failed(CommandFailure.InvalidState("refused")));

        Assert.Equal(CommandFailureReason.InvalidState, stack.BeginEdit(_c).Failure!.Reason);

        Assert.Null(stack.Editing);
        Assert.True(Note(_c).IsFolded);
    }

    [Fact]
    public void Editing_an_unfolded_card_writes_nothing()
    {
        string before = Snapshot();

        Assert.True(_stack.BeginEdit(_c).IsSuccess);

        Assert.Equal(before, Snapshot());
    }

    // ---- fold all -----------------------------------------------------------------------------------

    [Fact]
    public void Fold_all_folds_every_card_of_the_open_folder_and_no_other_folders()
    {
        NoteId elsewhere = Create("Elsewhere", _other);
        NoteId binned = Create("Binned");
        Bin(binned);
        string binnedRow = Row(binned);

        Assert.True(_stack.SetAllFolded(true).IsSuccess);

        Assert.Equal(binnedRow, Row(binned));
        Assert.All(_notes.ListInFolder(_folder), n => Assert.True(n.IsFolded));
        Assert.All(_stack.Notes, n => Assert.True(n.IsFolded));
        Assert.False(Note(elsewhere).IsFolded);
        Assert.Equal([_a, _b, _c, _d], Order());
    }

    [Fact]
    public void Fold_all_stamps_only_the_cards_that_change_and_a_second_one_writes_nothing()
    {
        Assert.True(_stack.SetFolded(_b, true).IsSuccess);
        string b = Row(_b);

        Assert.True(_stack.SetAllFolded(true).IsSuccess);
        Assert.Equal(b, Row(_b));
        string after = Snapshot();

        Assert.True(_stack.SetAllFolded(true).IsSuccess);
        Assert.Equal(after, Snapshot());
    }

    [Fact]
    public void Fold_all_saves_the_card_being_edited_and_leaves_editing_and_a_failed_save_folds_nothing()
    {
        Assert.True(_stack.BeginEdit(_a).IsSuccess);
        _stack.UpdateDraft(_a, "typed, then all folded");

        Assert.True(_stack.SetAllFolded(true).IsSuccess);
        Assert.Equal("typed, then all folded", Note(_a).Content);
        Assert.Null(_stack.Editing);

        Assert.True(_stack.SetAllFolded(false).IsSuccess);
        Assert.True(_stack.BeginEdit(_b).IsSuccess);
        _stack.UpdateDraft(_b, "typed");
        Sql("CREATE TRIGGER inject BEFORE UPDATE OF Content ON Notes BEGIN SELECT RAISE(ABORT, 'injected'); END;");

        Assert.Throws<StorageException>(() => _stack.SetAllFolded(true));
        Assert.All(_notes.ListInFolder(_folder), n => Assert.False(n.IsFolded));
        Assert.Equal(_b, _stack.Editing);
    }

    [Fact]
    public void Unfold_all_keeps_the_card_being_edited_editing_with_its_text()
    {
        Assert.True(_stack.SetFolded(_c, true).IsSuccess);
        Assert.True(_stack.SetFolded(_d, true).IsSuccess);
        Assert.True(_stack.BeginEdit(_a).IsSuccess);
        _stack.UpdateDraft(_a, "still typing");

        Assert.True(_stack.SetAllFolded(false).IsSuccess);

        Assert.All(_notes.ListInFolder(_folder), n => Assert.False(n.IsFolded));
        Assert.Equal(_a, _stack.Editing);
        Assert.Equal("still typing", _stack.Draft);
    }

    [Fact]
    public void Fold_all_in_a_binned_folder_is_refused_and_changes_nothing()
    {
        Sql("UPDATE Folders SET DeletedAt = '2026-11-02T09:00:00.0000000+00:00' WHERE Id = $p;", _folder.Value);
        string before = Snapshot();

        Assert.Equal(CommandFailureReason.InvalidState, _stack.SetAllFolded(true).Failure!.Reason);
        Assert.Equal(CommandFailureReason.NotFound, _dispatcher.Dispatch(new FoldNotesInFolder(FolderId.New(), true)).Failure!.Reason);

        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void Fold_all_reports_how_many_cards_changed()
    {
        Assert.True(_stack.SetFolded(_a, true).IsSuccess);

        Assert.Equal(3, _dispatcher.Dispatch(new FoldNotesInFolder(_folder, true)).Value);
        Assert.Equal(0, _dispatcher.Dispatch(new FoldNotesInFolder(_folder, true)).Value);
        Assert.Equal(4, _dispatcher.Dispatch(new FoldNotesInFolder(_folder, false)).Value);
    }

    [Fact]
    public void Fold_all_with_no_folder_open_is_refused()
    {
        _stack.Close();

        Assert.Equal(CommandFailureReason.InvalidState, _stack.SetAllFolded(true).Failure!.Reason);
    }

    // ---- folders ------------------------------------------------------------------------------------

    [Fact]
    public void Pinning_a_folder_sorts_it_first_and_unpinning_returns_it()
    {
        FolderId third = CreateFolder("Third");

        Assert.True(_dispatcher.Dispatch(new PinFolder(third)).IsSuccess);
        Assert.Equal([third, _folder, _other], _folders.ListActive().Select(f => f.Id));

        Assert.True(_dispatcher.Dispatch(new UnpinFolder(third)).IsSuccess);
        Assert.Equal([_folder, _other, third], _folders.ListActive().Select(f => f.Id));
    }

    // ---- events: after commit, never on a refusal -----------------------------------------------------

    [Fact]
    public void Pin_and_fold_are_announced_after_they_commit_and_refusals_are_not()
    {
        var told = new List<object>();
        using var pins = _events.Subscribe<NotePinChanged>(e =>
        {
            Assert.Equal(e.Pinned, Note(e.NoteId).IsPinned);
            told.Add(e);
        });
        using var folds = _events.Subscribe<NoteFoldChanged>(e =>
        {
            Assert.Equal(e.Folded, Note(e.NoteId).IsFolded);
            told.Add(e);
        });
        using var all = _events.Subscribe<NotesFoldedInFolder>(e =>
        {
            Assert.All(_notes.ListInFolder(e.FolderId), n => Assert.Equal(e.Folded, n.IsFolded));
            told.Add(e);
        });
        using var folderPins = _events.Subscribe<FolderPinChanged>(e =>
        {
            Assert.Equal(e.Pinned, _folders.FindActive(e.FolderId)!.IsPinned);
            told.Add(e);
        });

        Assert.True(_stack.SetPinned(_b, true).IsSuccess);
        Assert.True(_stack.SetFolded(_c, true).IsSuccess);
        Assert.True(_stack.SetAllFolded(true).IsSuccess);
        Assert.True(_dispatcher.Dispatch(new PinFolder(_other)).IsSuccess);
        Assert.False(_stack.SetPinned(NoteId.New(), true).IsSuccess);
        Assert.False(_stack.SetFolded(NoteId.New(), true).IsSuccess);

        Assert.Equal(
            [new NotePinChanged(_b, true), new NoteFoldChanged(_c, true), new NotesFoldedInFolder(_folder, true, 3), new FolderPinChanged(_other, true)],
            told);
    }

    // ---- helpers ------------------------------------------------------------------------------------

    private CommandRegistry Registry(Func<UpdateNoteContent, CommandResult> updateContent, Func<UnfoldNote, CommandResult> unfold) =>
        new CommandRegistry()
            .Register<UpdateNoteContent, CommandResult>(updateContent)
            .Register<PinNote, CommandResult, NotePinChanged>(new PinNoteHandler(_notes, _clock).Handle, static (c, _) => new NotePinChanged(c.NoteId, true))
            .Register<UnpinNote, CommandResult, NotePinChanged>(new UnpinNoteHandler(_notes, _clock).Handle, static (c, _) => new NotePinChanged(c.NoteId, false))
            .Register<FoldNote, CommandResult, NoteFoldChanged>(new FoldNoteHandler(_notes, _clock).Handle, static (c, _) => new NoteFoldChanged(c.NoteId, true))
            .Register<UnfoldNote, CommandResult, NoteFoldChanged>(unfold, static (c, _) => new NoteFoldChanged(c.NoteId, false))
            .Register<SetNoteColor, CommandResult>(new SetNoteColorHandler(_notes, _clock).Handle)
            .Register<FoldNotesInFolder, CommandResult<int>, NotesFoldedInFolder>(new FoldNotesInFolderHandler(_notes, _clock).Handle, static (c, r) => new NotesFoldedInFolder(c.FolderId, c.Folded, r.Value))
            .Register<PinFolder, CommandResult, FolderPinChanged>(new PinFolderHandler(_folders, _clock).Handle, static (c, _) => new FolderPinChanged(c.FolderId, true))
            .Register<UnpinFolder, CommandResult, FolderPinChanged>(new UnpinFolderHandler(_folders, _clock).Handle, static (c, _) => new FolderPinChanged(c.FolderId, false));

    private NoteStackSession Session(Func<UpdateNoteContent, CommandResult>? updateContent = null, Func<UnfoldNote, CommandResult>? unfold = null)
    {
        var stack = new NoteStackSession(
            new ListNotesInFolderQuery(_notes),
            new CommandDispatcher(
                Registry(updateContent ?? new UpdateNoteContentHandler(_notes, _clock).Handle, unfold ?? new UnfoldNoteHandler(_notes, _clock).Handle),
                new EventHub(_ => { })));
        stack.Open(_folder);
        _ = stack.Read();
        return stack;
    }

    private FolderId CreateFolder(string name)
    {
        _clock.Advance(TimeSpan.FromSeconds(1));
        return new CreateFolderHandler(_folders, _clock).Handle(new CreateFolder(name)).Value;
    }

    private NoteId Create(string content, FolderId? folder = null)
    {
        _clock.Advance(TimeSpan.FromSeconds(1));
        return new CreateNoteHandler(_notes, _clock).Handle(new CreateNote(folder ?? _folder, content)).Value;
    }

    private List<NoteId> Order() => [.. _notes.ListInFolder(_folder).Select(n => n.Id)];

    private Note Note(NoteId id) => _notes.FindActive(id)!;

    private void Bin(NoteId id) => Sql("UPDATE Notes SET DeletedAt = '2026-11-02T09:00:00.0000000+00:00' WHERE Id = $p;", id.Value);

    private string Row(NoteId id, bool withStamp = true)
    {
        using SqliteConnection connection = _database.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = withStamp
            ? "SELECT Id || '|' || IsPinned || '|' || IsFolded || '|' || SortOrder || '|' || UpdatedAt || '|' || Content FROM Notes WHERE Id = $p;"
            : "SELECT Id || '|' || IsPinned || '|' || IsFolded || '|' || SortOrder || '|' || Content FROM Notes WHERE Id = $p;";
        command.Parameters.AddWithValue("$p", id.Value);
        return (string)command.ExecuteScalar()!;
    }

    private string Snapshot()
    {
        using SqliteConnection connection = _database.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT 'N|' || Id || '|' || IsPinned || '|' || IsFolded || '|' || SortOrder || '|' || UpdatedAt || '|' || IFNULL(DeletedAt, '') || '|' || Content FROM Notes
            UNION ALL
            SELECT 'F|' || Id || '|' || IsPinned || '|' || SortOrder || '|' || UpdatedAt FROM Folders
            ORDER BY 1;
            """;
        using SqliteDataReader reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read())
        {
            rows.Add(reader.GetString(0));
        }

        return string.Join("\n", rows);
    }

    private void Sql(string sql, string? p = null)
    {
        using SqliteConnection connection = _database.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        if (p is not null)
        {
            command.Parameters.AddWithValue("$p", p);
        }

        command.ExecuteNonQuery();
    }

    private sealed class Clock(DateTimeOffset start) : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = start;

        public void Advance(TimeSpan by) => UtcNow += by;
    }
}
