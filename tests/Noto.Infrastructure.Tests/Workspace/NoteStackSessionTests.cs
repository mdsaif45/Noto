using Microsoft.Data.Sqlite;
using Noto.Core;
using Noto.Core.Commands;
using Noto.Core.Folders;
using Noto.Core.Notes;
using Noto.Core.Settings;
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
/// The note stack's session (ADR-016) against real SQLite: one card edited at a time, and what the user typed is never
/// lost. Every way out of editing saves first; a save that fails keeps the card editing with its text.
/// </summary>
public sealed class NoteStackSessionTests : IDisposable
{
    private readonly TempDatabase _temp = new();
    private readonly NotoDatabase _database;
    private readonly SqliteNoteRepository _notes;
    private readonly SqliteFolderRepository _folders;
    private readonly Clock _clock = new(new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero));
    private readonly NoteStackSession _stack;
    private readonly EventHub _events = new(_ => { });
    private readonly FolderId _folder;
    private readonly FolderId _other;
    private readonly NoteId _a;
    private readonly NoteId _b;
    private readonly NoteId _c;

    public NoteStackSessionTests()
    {
        _database = new NotoDatabase(_temp.DatabasePath);
        _database.Initialize();
        _notes = new SqliteNoteRepository(_database);
        _folders = new SqliteFolderRepository(_database);

        var dispatcher = new CommandDispatcher(
            new CommandRegistry()
                .Register<UpdateNoteContent, CommandResult>(new UpdateNoteContentHandler(_notes, _clock).Handle)
                .Register<CreateNote, CommandResult<NoteId>>(new CreateNoteHandler(_notes, _clock).Handle)
                .Register<DeleteNote, CommandResult>(new DeleteNoteHandler(_notes, _clock).Handle)
                .Register<MoveNoteToFolder, CommandResult, NoteMoved>(
                    new MoveNoteToFolderHandler(_notes, _clock).Handle,
                    static (command, _) => new NoteMoved(command.NoteId, command.TargetFolderId)),
            _events);
        _stack = new NoteStackSession(new ListNotesInFolderQuery(_notes), dispatcher);

        _folder = new CreateFolderHandler(_folders, _clock).Handle(new CreateFolder("Stack")).Value;
        _other = new CreateFolderHandler(_folders, _clock).Handle(new CreateFolder("Other")).Value;
        _a = Create("Alpha\nsecond line");
        _b = Create("Beta");
        _c = Create("Gamma");

        _stack.Open(_folder);
        _ = _stack.Read();
    }

    public void Dispose() => _temp.Dispose();

    // ---- reading -------------------------------------------------------------------------------------

    [Fact]
    public void Opening_a_folder_reads_its_notes_in_order_and_edits_nothing()
    {
        Assert.Equal([_a, _b, _c], _stack.Notes.Select(n => n.Id));
        Assert.Null(_stack.Editing);
        Assert.False(_stack.IsDirty);
    }

    [Fact]
    public void A_card_that_is_gone_after_a_read_stops_editing()
    {
        Assert.True(_stack.BeginEdit(_b).IsSuccess);
        Sql("UPDATE Notes SET DeletedAt = '2026-11-02T09:00:00.0000000+00:00' WHERE Id = $p;", _b.Value);

        _ = _stack.Read();

        Assert.Null(_stack.Editing);
        Assert.Equal([_a, _c], _stack.Notes.Select(n => n.Id));
    }

    // ---- editing -------------------------------------------------------------------------------------

    [Fact]
    public void Editing_starts_from_the_stored_text_and_is_clean()
    {
        Assert.True(_stack.BeginEdit(_a).IsSuccess);

        Assert.Equal(_a, _stack.Editing);
        Assert.Equal("Alpha\nsecond line", _stack.Draft);
        Assert.Equal("Alpha\nsecond line", _stack.Baseline);
        Assert.False(_stack.IsDirty);
    }

    [Fact]
    public void Editing_a_note_that_is_not_in_the_stack_is_refused_and_changes_nothing()
    {
        Assert.True(_stack.BeginEdit(_a).IsSuccess);
        _stack.UpdateDraft(_a, "typed");

        CommandResult result = _stack.BeginEdit(NoteId.New());

        Assert.Equal(CommandFailureReason.NotFound, result.Failure!.Reason);
        Assert.Equal(_a, _stack.Editing);
        Assert.Equal("typed", _stack.Draft);
        Assert.Equal("Alpha\nsecond line", Stored(_a));
    }

    [Fact]
    public void Text_for_a_card_that_is_not_the_editing_one_is_ignored()
    {
        Assert.True(_stack.BeginEdit(_a).IsSuccess);

        _stack.UpdateDraft(_b, "late text for another card");

        Assert.Equal("Alpha\nsecond line", _stack.Draft);
        Assert.False(_stack.IsDirty);
    }

    [Fact]
    public void Typing_and_undoing_is_clean_and_leaving_then_writes_nothing()
    {
        Assert.True(_stack.BeginEdit(_a).IsSuccess);
        string before = Snapshot();

        _stack.UpdateDraft(_a, "changed");
        Assert.True(_stack.IsDirty);
        _stack.UpdateDraft(_a, "Alpha\nsecond line");
        Assert.False(_stack.IsDirty);

        Assert.True(_stack.EndEdit().IsSuccess);
        Assert.Equal(before, Snapshot());
        Assert.Null(_stack.Editing);
    }

    [Fact]
    public void Leaving_a_card_saves_its_text_and_stops_editing()
    {
        Assert.True(_stack.BeginEdit(_a).IsSuccess);
        _stack.UpdateDraft(_a, "Alpha edited");

        Assert.True(_stack.EndEdit().IsSuccess);

        Assert.Equal("Alpha edited", Stored(_a));
        Assert.Null(_stack.Editing);
        Assert.Equal(string.Empty, _stack.Draft);
    }

    [Fact]
    public void Editing_another_card_saves_the_first()
    {
        Assert.True(_stack.BeginEdit(_a).IsSuccess);
        _stack.UpdateDraft(_a, "Alpha edited");

        Assert.True(_stack.BeginEdit(_b).IsSuccess);

        Assert.Equal("Alpha edited", Stored(_a));
        Assert.Equal(_b, _stack.Editing);
        Assert.Equal("Beta", _stack.Draft);
    }

    [Fact]
    public void Editing_a_card_again_after_saving_it_starts_from_what_was_saved_not_from_the_earlier_read()
    {
        Assert.True(_stack.BeginEdit(_a).IsSuccess);
        _stack.UpdateDraft(_a, "Alpha edited");
        Assert.True(_stack.BeginEdit(_b).IsSuccess);

        Assert.True(_stack.BeginEdit(_a).IsSuccess);

        Assert.Equal("Alpha edited", _stack.Draft);
        Assert.Equal("Alpha edited", _stack.Baseline);
        Assert.Equal("Alpha edited", _stack.Notes.Single(n => n.Id == _a).Content);
    }

    [Fact]
    public void Saving_keeps_the_card_editing_and_clean()
    {
        Assert.True(_stack.BeginEdit(_a).IsSuccess);
        _stack.UpdateDraft(_a, "kept editing");

        Assert.True(_stack.Save().IsSuccess);

        Assert.Equal(_a, _stack.Editing);
        Assert.False(_stack.IsDirty);
        Assert.Equal("kept editing", Stored(_a));
    }

    [Fact]
    public void A_clean_save_writes_nothing()
    {
        Assert.True(_stack.BeginEdit(_a).IsSuccess);
        string before = Snapshot();

        Assert.True(_stack.Save().IsSuccess);

        Assert.Equal(before, Snapshot());
    }

    // ---- a save that fails ----------------------------------------------------------------------------

    [Fact]
    public void A_refused_save_keeps_the_card_editing_with_its_text_and_every_way_out_stays()
    {
        Assert.True(_stack.BeginEdit(_a).IsSuccess);
        _stack.UpdateDraft(_a, "typed before it was binned elsewhere");
        Sql("UPDATE Notes SET DeletedAt = '2026-11-02T09:00:00.0000000+00:00' WHERE Id = $p;", _a.Value);

        Assert.Equal(CommandFailureReason.InvalidState, _stack.Save().Failure!.Reason);
        Assert.False(_stack.EndEdit().IsSuccess);
        Assert.False(_stack.BeginEdit(_b).IsSuccess);
        Assert.False(_stack.Create(NewNotePlacement.Bottom, null).IsSuccess);

        Assert.Equal(_a, _stack.Editing);
        Assert.Equal("typed before it was binned elsewhere", _stack.Draft);
        Assert.True(_stack.IsDirty);
        Assert.Equal(3, Count("SELECT COUNT(*) FROM Notes;"));
    }

    [Fact]
    public void A_save_the_store_fails_is_thrown_and_the_text_stays_unsaved_and_editing()
    {
        Assert.True(_stack.BeginEdit(_a).IsSuccess);
        _stack.UpdateDraft(_a, "typed");
        Sql("CREATE TRIGGER inject BEFORE UPDATE OF Content ON Notes BEGIN SELECT RAISE(ABORT, 'injected'); END;");

        Assert.Throws<StorageException>(() => _stack.EndEdit());
        Assert.Throws<StorageException>(() => _stack.BeginEdit(_b));

        Assert.Equal(_a, _stack.Editing);
        Assert.Equal("typed", _stack.Draft);
        Assert.True(_stack.IsDirty);
        Assert.Equal("Alpha\nsecond line", Stored(_a));

        Sql("DROP TRIGGER inject;");
        Assert.True(_stack.EndEdit().IsSuccess);
        Assert.Equal("typed", Stored(_a));
    }

    [Fact]
    public void Discarding_stops_editing_and_writes_nothing()
    {
        Assert.True(_stack.BeginEdit(_a).IsSuccess);
        _stack.UpdateDraft(_a, "thrown away");
        string before = Snapshot();

        _stack.DiscardEdit();

        Assert.Null(_stack.Editing);
        Assert.False(_stack.IsDirty);
        Assert.Equal(before, Snapshot());
    }

    // ---- creating ------------------------------------------------------------------------------------

    [Fact]
    public void Creating_saves_the_card_being_edited_then_edits_one_new_empty_note_at_the_placement()
    {
        Assert.True(_stack.BeginEdit(_b).IsSuccess);
        _stack.UpdateDraft(_b, "Beta edited");

        CommandResult<NoteId> created = _stack.Create(NewNotePlacement.BelowSelected, _b);

        Assert.True(created.IsSuccess);
        Assert.Equal("Beta edited", Stored(_b));
        Assert.Equal([_a, _b, created.Value, _c], _stack.Notes.Select(n => n.Id));
        Assert.Equal(created.Value, _stack.Editing);
        Assert.Equal(string.Empty, _stack.Draft);
        Assert.False(_stack.IsDirty);
    }

    [Fact]
    public void Creating_with_no_folder_open_is_refused()
    {
        _stack.Close();

        Assert.Equal(CommandFailureReason.InvalidState, _stack.Create(NewNotePlacement.Bottom, null).Failure!.Reason);
        Assert.Equal(3, Count("SELECT COUNT(*) FROM Notes;"));
    }

    // ---- deleting ------------------------------------------------------------------------------------

    [Fact]
    public void Deleting_the_card_being_edited_bins_it_and_its_unsaved_text_goes_with_it()
    {
        Assert.True(_stack.BeginEdit(_b).IsSuccess);
        _stack.UpdateDraft(_b, "never saved");

        Assert.True(_stack.Delete(_b).IsSuccess);

        Assert.Null(_stack.Editing);
        Assert.Equal([_a, _c], _stack.Notes.Select(n => n.Id));
        Assert.Equal("Beta", Stored(_b));
        Assert.NotNull(Scalar("SELECT DeletedAt FROM Notes WHERE Id = $p;", _b.Value));
    }

    [Fact]
    public void Deleting_another_card_keeps_the_one_being_edited_with_its_text()
    {
        Assert.True(_stack.BeginEdit(_a).IsSuccess);
        _stack.UpdateDraft(_a, "still typing");

        Assert.True(_stack.Delete(_c).IsSuccess);

        Assert.Equal(_a, _stack.Editing);
        Assert.Equal("still typing", _stack.Draft);
        Assert.Equal([_a, _b], _stack.Notes.Select(n => n.Id));
    }

    [Fact]
    public void A_refused_delete_keeps_the_card_editing()
    {
        Assert.True(_stack.BeginEdit(_a).IsSuccess);
        _stack.UpdateDraft(_a, "typed");

        Assert.False(_stack.Delete(NoteId.New()).IsSuccess);

        Assert.Equal(_a, _stack.Editing);
        Assert.Equal("typed", _stack.Draft);
    }

    // ---- moving (B11) ---------------------------------------------------------------------------------

    [Fact]
    public void A_moved_note_lands_at_the_end_of_its_destination_and_leaves_the_stack_with_its_identity_and_content()
    {
        NoteId x = Create("Already there", _other);

        Assert.True(_stack.Move(_b, _other).IsSuccess);

        Assert.Equal([_a, _c], _stack.Notes.Select(n => n.Id));
        Assert.Equal([x, _b], _notes.ListInFolder(_other).Select(n => n.Id));
        Assert.Equal("Beta", Stored(_b));
        Assert.Equal([_a, _c], _notes.ListInFolder(_folder).Select(n => n.Id));
    }

    [Fact]
    public void Moving_the_card_being_edited_saves_it_first()
    {
        Assert.True(_stack.BeginEdit(_b).IsSuccess);
        _stack.UpdateDraft(_b, "typed, then moved");

        Assert.True(_stack.Move(_b, _other).IsSuccess);

        Assert.Equal("typed, then moved", Stored(_b));
        Assert.Equal(_other.Value, Scalar("SELECT FolderId FROM Notes WHERE Id = $p;", _b.Value));
        Assert.Null(_stack.Editing);
    }

    [Fact]
    public void A_failed_save_moves_nothing_and_keeps_the_card_editing()
    {
        Assert.True(_stack.BeginEdit(_b).IsSuccess);
        _stack.UpdateDraft(_b, "typed");
        Sql("CREATE TRIGGER inject BEFORE UPDATE OF Content ON Notes BEGIN SELECT RAISE(ABORT, 'injected'); END;");

        Assert.Throws<StorageException>(() => _stack.Move(_b, _other));

        Assert.Equal(_folder.Value, Scalar("SELECT FolderId FROM Notes WHERE Id = $p;", _b.Value));
        Assert.Equal(_b, _stack.Editing);
        Assert.Equal("typed", _stack.Draft);
    }

    [Fact]
    public void A_refused_save_moves_nothing()
    {
        Assert.True(_stack.BeginEdit(_b).IsSuccess);
        _stack.UpdateDraft(_b, "typed");
        Sql("UPDATE Notes SET DeletedAt = '2026-11-02T09:00:00.0000000+00:00' WHERE Id = $p;", _b.Value);

        Assert.Equal(CommandFailureReason.InvalidState, _stack.Move(_b, _other).Failure!.Reason);

        Assert.Equal(_folder.Value, Scalar("SELECT FolderId FROM Notes WHERE Id = $p;", _b.Value));
        Assert.Equal(_b, _stack.Editing);
    }

    [Fact]
    public void A_save_refused_while_the_note_could_still_move_moves_nothing()
    {
        // The save is refused for a reason that leaves the note movable, so only the session's own check stops it.
        var dispatcher = new CommandDispatcher(
            new CommandRegistry()
                .Register<UpdateNoteContent, CommandResult>(_ => CommandResult.Failed(CommandFailure.InvalidInput("refused")))
                .Register<MoveNoteToFolder, CommandResult>(new MoveNoteToFolderHandler(_notes, _clock).Handle),
            new EventHub(_ => { }));
        var stack = new NoteStackSession(new ListNotesInFolderQuery(_notes), dispatcher);
        stack.Open(_folder);
        _ = stack.Read();
        Assert.True(stack.BeginEdit(_b).IsSuccess);
        stack.UpdateDraft(_b, "typed");

        Assert.Equal(CommandFailureReason.InvalidInput, stack.Move(_b, _other).Failure!.Reason);

        Assert.Equal(_folder.Value, Scalar("SELECT FolderId FROM Notes WHERE Id = $p;", _b.Value));
        Assert.Equal(_b, stack.Editing);
        Assert.Equal("typed", stack.Draft);
    }

    [Fact]
    public void The_recent_list_setting_refuses_a_value_that_is_not_such_a_list()
    {
        var settings = new SqliteSettingsStore(_database, NullStorageLog.Instance);
        settings.Load();

        Assert.False(settings.Write(SettingKeys.NotesRecentMoveFolders, "garbage"));
        Assert.False(settings.Write(SettingKeys.NotesRecentMoveFolders, $"{_other},{_other}"));
        Assert.True(settings.Write(SettingKeys.NotesRecentMoveFolders, $"{_other},{_folder}"));
    }

    [Fact]
    public void Moving_another_card_keeps_the_one_being_edited_with_its_text()
    {
        Assert.True(_stack.BeginEdit(_a).IsSuccess);
        _stack.UpdateDraft(_a, "still typing");

        Assert.True(_stack.Move(_c, _other).IsSuccess);

        Assert.Equal(_a, _stack.Editing);
        Assert.Equal("still typing", _stack.Draft);
    }

    [Fact]
    public void A_move_to_the_open_folder_is_not_a_move_and_writes_nothing()
    {
        string before = Snapshot();

        Assert.True(_stack.Move(_a, _folder).IsSuccess);

        Assert.Equal(before, Snapshot());
        Assert.Equal([_a, _b, _c], _stack.Notes.Select(n => n.Id));
    }

    [Fact]
    public void A_move_into_a_binned_folder_is_refused_and_changes_nothing()
    {
        Sql("UPDATE Folders SET DeletedAt = '2026-11-02T09:00:00.0000000+00:00' WHERE Id = $p;", _other.Value);
        string before = Snapshot();

        Assert.Equal(CommandFailureReason.InvalidState, _stack.Move(_a, _other).Failure!.Reason);

        Assert.Equal(before, Snapshot());
        Assert.Equal([_a, _b, _c], _stack.Notes.Select(n => n.Id));
    }

    [Fact]
    public void Moving_twice_keeps_both_orders_whole()
    {
        Assert.True(_stack.Move(_a, _other).IsSuccess);
        Assert.True(_stack.Move(_c, _other).IsSuccess);

        Assert.Equal([_b], _notes.ListInFolder(_folder).Select(n => n.Id));
        Assert.Equal([_a, _c], _notes.ListInFolder(_other).Select(n => n.Id));
        Assert.Equal(2L, Count($"SELECT COUNT(DISTINCT SortOrder) FROM Notes WHERE FolderId = '{_other.Value}';"));
    }

    [Fact]
    public void A_move_is_announced_after_it_commits_and_a_refused_one_is_not()
    {
        var told = new List<NoteMoved>();
        using var subscription = _events.Subscribe<NoteMoved>(e =>
        {
            Assert.Equal(e.FolderId.Value, Scalar("SELECT FolderId FROM Notes WHERE Id = $p;", e.NoteId.Value));
            told.Add(e);
        });

        Assert.True(_stack.Move(_a, _other).IsSuccess);
        Assert.False(_stack.Move(_b, FolderId.New()).IsSuccess);
        Assert.True(_stack.Move(_c, _folder).IsSuccess);

        Assert.Equal([new NoteMoved(_a, _other)], told);
    }

    // ---- recent folders (B11) ------------------------------------------------------------------------

    [Fact]
    public void Each_move_records_its_destination_as_the_most_recent_and_it_survives_a_restart()
    {
        WorkspacePreferences preferences = Preferences();
        FolderId third = new CreateFolderHandler(_folders, _clock).Handle(new CreateFolder("Third")).Value;
        using var subscription = _events.Subscribe<NoteMoved>(e => preferences.RememberMoveTarget(e.FolderId, _folders.ListActive()));

        Assert.True(_stack.Move(_a, _other).IsSuccess);
        Assert.True(_stack.Move(_b, third).IsSuccess);
        Assert.True(_stack.Move(_c, _other).IsSuccess);

        Assert.Equal([_other, third], preferences.RecentMoveFolders);
        Assert.Equal([_other, third], Preferences().RecentMoveFolders);
    }

    [Fact]
    public void A_refused_move_records_nothing()
    {
        WorkspacePreferences preferences = Preferences();
        using var subscription = _events.Subscribe<NoteMoved>(e => preferences.RememberMoveTarget(e.FolderId, _folders.ListActive()));

        Assert.False(_stack.Move(_a, FolderId.New()).IsSuccess);

        Assert.Empty(preferences.RecentMoveFolders);
    }

    [Fact]
    public void A_folder_no_longer_active_falls_out_the_next_time_the_list_is_written()
    {
        WorkspacePreferences preferences = Preferences();
        FolderId third = new CreateFolderHandler(_folders, _clock).Handle(new CreateFolder("Third")).Value;
        Assert.True(preferences.RememberMoveTarget(_other, _folders.ListActive()));
        Assert.True(preferences.RememberMoveTarget(third, _folders.ListActive()));
        Sql("UPDATE Folders SET DeletedAt = '2026-11-02T09:00:00.0000000+00:00' WHERE Id = $p;", _other.Value);

        Assert.Equal([third, _other], preferences.RecentMoveFolders);
        Assert.True(preferences.RememberMoveTarget(_folder, _folders.ListActive()));

        Assert.Equal([_folder, third], preferences.RecentMoveFolders);
    }

    [Fact]
    public void A_corrupt_stored_list_reads_as_empty_and_is_replaced_by_the_next_move()
    {
        Sql("INSERT INTO Settings (Key, Value) VALUES ('notes.recent-move-folders', 'garbage');");
        WorkspacePreferences preferences = Preferences();

        Assert.Empty(preferences.RecentMoveFolders);
        Assert.True(preferences.RememberMoveTarget(_other, _folders.ListActive()));

        Assert.Equal([_other], preferences.RecentMoveFolders);
    }

    // ---- neighbours ----------------------------------------------------------------------------------

    [Fact]
    public void Neighbours_stop_at_either_end()
    {
        Assert.Equal(_b, _stack.Neighbour(_a, 1));
        Assert.Equal(_a, _stack.Neighbour(_b, -1));
        Assert.Null(_stack.Neighbour(_a, -1));
        Assert.Null(_stack.Neighbour(_c, 1));
        Assert.Null(_stack.Neighbour(NoteId.New(), 1));
    }

    [Fact]
    public void After_a_removal_focus_goes_to_the_next_card_else_the_previous_else_nowhere()
    {
        Assert.Equal(_b, _stack.AfterRemoval(_a));
        Assert.Equal(_c, _stack.AfterRemoval(_b));
        Assert.Equal(_b, _stack.AfterRemoval(_c));

        Assert.True(_stack.Delete(_b).IsSuccess);
        Assert.True(_stack.Delete(_c).IsSuccess);

        Assert.Null(_stack.AfterRemoval(_a));
    }

    [Fact]
    public void Closing_the_folder_forgets_its_notes_and_edits_nothing()
    {
        Assert.True(_stack.BeginEdit(_a).IsSuccess);
        Assert.True(_stack.EndEdit().IsSuccess);

        _stack.Close();

        Assert.Null(_stack.Folder);
        Assert.Empty(_stack.Notes);
        Assert.Null(_stack.Editing);
        Assert.Empty(_stack.Read());
    }

    // ---- helpers -------------------------------------------------------------------------------------

    private NoteId Create(string content, FolderId? folder = null)
    {
        _clock.Advance(TimeSpan.FromSeconds(1));
        return new CreateNoteHandler(_notes, _clock).Handle(new CreateNote(folder ?? _folder, content)).Value;
    }

    private WorkspacePreferences Preferences()
    {
        var settings = new SqliteSettingsStore(_database, NullStorageLog.Instance);
        settings.Load();
        return new WorkspacePreferences(settings);
    }

    private string? Stored(NoteId note) => Scalar("SELECT Content FROM Notes WHERE Id = $p;", note.Value) as string;

    private string Snapshot()
    {
        using SqliteConnection connection = _database.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT Id || '|' || SortOrder || '|' || UpdatedAt || '|' || IFNULL(DeletedAt, '') || '|' || Content FROM Notes ORDER BY Id;";
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

    private object? Scalar(string sql, string? p = null)
    {
        using SqliteConnection connection = _database.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        if (p is not null)
        {
            command.Parameters.AddWithValue("$p", p);
        }

        object? value = command.ExecuteScalar();
        return value is DBNull ? null : value;
    }

    private long Count(string sql) => (long)Scalar(sql)!;

    private sealed class Clock(DateTimeOffset start) : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = start;

        public void Advance(TimeSpan by) => UtcNow += by;
    }
}
