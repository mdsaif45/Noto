using Microsoft.Data.Sqlite;
using Noto.Core;
using Noto.Core.Commands;
using Noto.Core.Folders;
using Noto.Core.Notes;
using Noto.Core.Storage;
using Noto.Core.Tags;
using Noto.Infrastructure.Storage;
using Noto.Infrastructure.Tests.Storage;
using Noto.UseCases.Folders;
using Noto.UseCases.Notes;
using Noto.UseCases.Tags;
using Xunit;

namespace Noto.Infrastructure.Tests.Notes;

/// <summary>
/// P1.1, against real SQLite: permanent deletion, restoring into a chosen folder and emptying the bin
/// (deletion-semantics §13). Every scenario ends by checking I7 on the whole database.
/// </summary>
public sealed class RecycleBinPurgeTests : IDisposable
{
    private readonly TempDatabase _temp = new();
    private readonly NotoDatabase _database;
    private readonly SqliteNoteRepository _notes;
    private readonly SqliteFolderRepository _folders;
    private readonly SqliteTagRepository _tags;
    private readonly Clock _clock = new(new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero));

    public RecycleBinPurgeTests()
    {
        _database = new NotoDatabase(_temp.DatabasePath);
        _database.Initialize();
        _notes = new SqliteNoteRepository(_database);
        _folders = new SqliteFolderRepository(_database);
        _tags = new SqliteTagRepository(_database);
    }

    public void Dispose() => _temp.Dispose();

    // ---- 1. restore a deleted note into a live folder ---------------------

    [Fact]
    public void A_binned_note_restored_into_another_live_folder_is_listed_there_at_the_end()
    {
        FolderId from = Folder("From");
        FolderId to = Folder("To");
        NoteId first = Note("already here", to);
        NoteId moved = Note("binned", from);
        Delete(moved);

        Assert.True(RestoreInto(moved, to).IsSuccess);

        Assert.Equal([first, moved], Listed(to));
        Assert.Empty(Listed(from));
        Assert.Equal(to, _notes.FindActive(moved)!.FolderId);
        AssertI7();
    }

    [Fact]
    public void A_binned_note_restored_into_its_own_folder_keeps_its_place()
    {
        FolderId folder = Folder("Work");
        NoteId a = Note("a", folder);
        NoteId b = Note("b", folder);
        NoteId c = Note("c", folder);
        Delete(b);

        Assert.True(RestoreInto(b, folder).IsSuccess);

        // I6: its SortOrder was kept for exactly this.
        Assert.Equal([a, b, c], Listed(folder));
        AssertI7();
    }

    [Fact]
    public void A_note_whose_folder_is_binned_comes_back_into_a_live_one_without_its_folder()
    {
        FolderId gone = Folder("Gone");
        FolderId live = Folder("Live");
        NoteId note = Note("rescue me", gone);
        DeleteFolder(gone);

        Assert.True(RestoreInto(note, live).IsSuccess);

        Assert.Equal([note], Listed(live));
        Assert.Equal(FolderLifecycle.Deleted, _folders.GetLifecycle(gone));
        AssertI7();
    }

    [Fact]
    public void The_restore_stamps_the_note_with_one_instant()
    {
        FolderId folder = Folder("Work");
        NoteId note = Note("n", folder);
        Delete(note);
        _clock.Advance(TimeSpan.FromHours(3));

        RestoreInto(note, folder);

        Assert.Equal(_clock.UtcNow, _notes.FindActive(note)!.UpdatedAt);
        Assert.Null(_notes.FindActive(note)!.DeletedAt);
    }

    // ---- 2. restore into a binned or missing folder, or of the wrong note --

    [Fact]
    public void A_restore_into_a_binned_folder_is_refused_and_changes_nothing()
    {
        FolderId binned = Folder("Binned");
        FolderId home = Folder("Home");
        NoteId note = Note("n", home);
        Delete(note);
        DeleteFolder(binned);
        string before = Snapshot();

        var result = RestoreInto(note, binned);

        Assert.Equal(CommandFailureReason.InvalidState, result.Failure!.Reason);
        Assert.Equal(before, Snapshot());
        AssertI7();
    }

    [Fact]
    public void A_restore_into_a_missing_folder_is_NotFound_and_changes_nothing()
    {
        NoteId note = Note("n", Folder("Home"));
        Delete(note);
        string before = Snapshot();

        var result = RestoreInto(note, FolderId.New());

        Assert.Equal(CommandFailureReason.NotFound, result.Failure!.Reason);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void A_restore_of_a_missing_note_is_NotFound() =>
        Assert.Equal(CommandFailureReason.NotFound, RestoreInto(NoteId.New(), Folder("Home")).Failure!.Reason);

    [Fact]
    public void A_restore_of_an_active_note_is_refused_and_moves_nothing()
    {
        FolderId home = Folder("Home");
        FolderId other = Folder("Other");
        NoteId note = Note("live", home);
        string before = Snapshot();

        var result = RestoreInto(note, other);

        Assert.Equal(CommandFailureReason.InvalidState, result.Failure!.Reason);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void The_repository_refuses_a_binned_target_by_itself()
    {
        // The handler checks first; the repository holds the same line inside its transaction (no check-then-act gap).
        NoteId note = Note("n", Folder("Home"));
        FolderId binned = Folder("Binned");
        Delete(note);
        DeleteFolder(binned);

        Assert.False(_notes.RestoreInto(note, binned, _clock.UtcNow));
        Assert.Equal(NoteLifecycle.Deleted, _notes.GetLifecycle(note));
        AssertI7();
    }

    [Fact]
    public void The_repository_refuses_to_restore_an_active_note_by_itself()
    {
        // The repository's own guard, not the handler's: restoring an active note into another folder would move it
        // without any move command (and a repeated restore would succeed). It changes nothing.
        FolderId home = Folder("Home");
        FolderId other = Folder("Other");
        NoteId note = Note("live", home);
        string before = Snapshot();

        Assert.False(_notes.RestoreInto(note, other, _clock.UtcNow));

        Assert.Equal(before, Snapshot());
        Assert.Equal([note], Listed(home));
    }

    // ---- 3./4. permanently deleting a folder -------------------------------

    [Fact]
    public void A_binned_folder_an_active_note_lives_in_is_never_purged()
    {
        // Only legacy data can hold this state now (Case C's restore path is closed), so it is written directly.
        FolderId folder = Folder("Legacy");
        NoteId note = Note("still active", folder);
        MarkFolderDeletedOnly(folder);
        string before = Snapshot();

        var result = PurgeFolder(folder);

        Assert.Equal(CommandFailureReason.InvalidState, result.Failure!.Reason);
        Assert.Equal(before, Snapshot());
        Assert.NotNull(_notes.FindActive(note));
    }

    [Fact]
    public void A_binned_folder_with_only_binned_notes_is_purged_with_them()
    {
        FolderId folder = Folder("Old");
        NoteId a = Note("a", folder);
        NoteId b = Note("b", folder);
        Delete(a);
        DeleteFolder(folder);
        FolderId keep = Folder("Keep");
        NoteId kept = Note("untouched", keep);
        NoteId binnedElsewhere = Note("binned elsewhere", keep);
        Delete(binnedElsewhere);

        Assert.True(PurgeFolder(folder).IsSuccess);

        Assert.Equal(FolderLifecycle.Missing, _folders.GetLifecycle(folder));
        Assert.Equal(NoteLifecycle.Missing, _notes.GetLifecycle(a));
        Assert.Equal(NoteLifecycle.Missing, _notes.GetLifecycle(b));
        Assert.NotNull(_notes.FindActive(kept));
        Assert.Equal(NoteLifecycle.Deleted, _notes.GetLifecycle(binnedElsewhere));
        AssertI7();
    }

    [Fact]
    public void A_live_folder_is_never_purged()
    {
        FolderId folder = Folder("Live");
        Note("n", folder);
        string before = Snapshot();

        Assert.Equal(CommandFailureReason.InvalidState, PurgeFolder(folder).Failure!.Reason);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void A_missing_folder_purge_is_NotFound() =>
        Assert.Equal(CommandFailureReason.NotFound, PurgeFolder(FolderId.New()).Failure!.Reason);

    // ---- 5. restore after a related purge -----------------------------------

    [Fact]
    public void A_note_purged_with_its_folder_cannot_be_restored()
    {
        FolderId folder = Folder("Old");
        NoteId note = Note("n", folder);
        DeleteFolder(folder);
        Assert.True(PurgeFolder(folder).IsSuccess);

        Assert.Equal(CommandFailureReason.NotFound, RestoreInto(note, Folder("New")).Failure!.Reason);
        Assert.Equal(CommandFailureReason.NotFound, Restore(note).Failure!.Reason);
    }

    [Fact]
    public void A_note_rescued_before_its_folder_is_purged_survives_the_purge()
    {
        FolderId old = Folder("Old");
        FolderId live = Folder("Live");
        NoteId rescued = Note("rescued", old);
        NoteId lost = Note("lost", old);
        DeleteFolder(old);
        Assert.True(RestoreInto(rescued, live).IsSuccess);

        Assert.True(PurgeFolder(old).IsSuccess);

        Assert.Equal([rescued], Listed(live));
        Assert.Equal(NoteLifecycle.Missing, _notes.GetLifecycle(lost));
        AssertI7();
    }

    // ---- 6./7. repeats -------------------------------------------------------

    [Fact]
    public void A_second_restore_is_refused_and_changes_nothing()
    {
        FolderId folder = Folder("Home");
        NoteId note = Note("n", folder);
        Delete(note);
        Assert.True(RestoreInto(note, folder).IsSuccess);
        string before = Snapshot();

        Assert.Equal(CommandFailureReason.InvalidState, RestoreInto(note, folder).Failure!.Reason);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void A_second_purge_is_NotFound_and_changes_nothing()
    {
        NoteId note = Note("n", Folder("Home"));
        Delete(note);
        Assert.True(PurgeNote(note).IsSuccess);
        string before = Snapshot();

        Assert.Equal(CommandFailureReason.NotFound, PurgeNote(note).Failure!.Reason);
        Assert.Equal(before, Snapshot());
    }

    // ---- permanently deleting a note ----------------------------------------

    [Fact]
    public void A_purge_removes_exactly_that_note_and_its_tag_links()
    {
        FolderId folder = Folder("Home");
        NoteId gone = Note("gone", folder);
        NoteId other = Note("other", folder);
        TagId tag = new CreateTagHandler(_tags, _clock).Handle(new CreateTag("t")).Value;
        Assert.True(new AssignTagToNoteHandler(_notes, _tags).Handle(new AssignTagToNote(gone, tag)).IsSuccess);
        Assert.True(new AssignTagToNoteHandler(_notes, _tags).Handle(new AssignTagToNote(other, tag)).IsSuccess);
        Delete(gone);

        Assert.True(PurgeNote(gone).IsSuccess);

        Assert.Equal(NoteLifecycle.Missing, _notes.GetLifecycle(gone));
        Assert.Equal(0L, Scalar("SELECT COUNT(*) FROM NoteTags WHERE NoteId = $p;", gone.Value));
        Assert.Equal(1L, Scalar("SELECT COUNT(*) FROM NoteTags WHERE NoteId = $p;", other.Value));
        Assert.NotNull(_notes.FindActive(other));
    }

    [Fact]
    public void An_active_note_is_never_purged_by_the_command_or_the_repository()
    {
        NoteId note = Note("live", Folder("Home"));

        Assert.Equal(CommandFailureReason.InvalidState, PurgeNote(note).Failure!.Reason);
        Assert.False(_notes.Purge(note));
        Assert.NotNull(_notes.FindActive(note));
    }

    [Fact]
    public void A_missing_note_purge_is_NotFound() =>
        Assert.Equal(CommandFailureReason.NotFound, PurgeNote(NoteId.New()).Failure!.Reason);

    // ---- 8. emptying the bin ---------------------------------------------------

    [Fact]
    public void Emptying_removes_every_binned_note_and_folder_and_touches_nothing_live()
    {
        FolderId live = Folder("Live");
        NoteId liveNote = Note("live", live);
        NoteId binnedNote = Note("binned", live);
        Delete(binnedNote);
        FolderId binnedFolder = Folder("Binned");
        NoteId insideBinned = Note("inside", binnedFolder);
        DeleteFolder(binnedFolder);

        var result = Empty();

        Assert.True(result.IsSuccess);
        Assert.Equal((2, 1, 0), (result.Value.NotesPurged, result.Value.FoldersPurged, result.Value.FoldersKept.Count));
        Assert.Equal([liveNote], Listed(live));
        Assert.Equal(NoteLifecycle.Missing, _notes.GetLifecycle(binnedNote));
        Assert.Equal(NoteLifecycle.Missing, _notes.GetLifecycle(insideBinned));
        Assert.Equal(FolderLifecycle.Missing, _folders.GetLifecycle(binnedFolder));
        Assert.Empty(_notes.ListDeleted());
        Assert.Empty(_folders.ListDeleted());
        AssertI7();
    }

    [Fact]
    public void Emptying_twice_succeeds_and_the_second_time_removes_nothing()
    {
        NoteId note = Note("n", Folder("Home"));
        Delete(note);
        Assert.True(Empty().IsSuccess);
        string before = Snapshot();

        var second = Empty();

        Assert.True(second.IsSuccess);
        Assert.Equal((0, 0, 0), (second.Value.NotesPurged, second.Value.FoldersPurged, second.Value.FoldersKept.Count));
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void Emptying_keeps_and_reports_a_binned_folder_an_active_note_lives_in()
    {
        FolderId legacy = Folder("Legacy");
        NoteId active = Note("active", legacy);
        NoteId binned = Note("binned", legacy);
        Delete(binned);
        MarkFolderDeletedOnly(legacy);
        FolderId plain = Folder("Plain");
        DeleteFolder(plain);

        var result = Empty();

        Assert.Equal([legacy], result.Value.FoldersKept);
        Assert.Equal(1, result.Value.FoldersPurged);
        Assert.Equal(FolderLifecycle.Deleted, _folders.GetLifecycle(legacy));
        Assert.NotNull(_notes.FindActive(active));
        Assert.Equal(NoteLifecycle.Missing, _notes.GetLifecycle(binned));
        Assert.Equal(FolderLifecycle.Missing, _folders.GetLifecycle(plain));
    }

    [Fact]
    public void The_kept_folders_are_reported_in_a_deterministic_order()
    {
        var kept = new List<FolderId>();
        for (int i = 0; i < 4; i++)
        {
            FolderId f = Folder($"Legacy {i}");
            Note("active", f);
            MarkFolderDeletedOnly(f);
            kept.Add(f);
        }

        Assert.Equal(kept.OrderBy(f => f.Value, StringComparer.Ordinal), Empty().Value.FoldersKept);
    }

    // ---- 9. a failure inside the transaction leaves the database as it was -----

    [Fact]
    public void A_failure_while_emptying_rolls_the_whole_bin_back()
    {
        FolderId folder = Folder("F");
        NoteId note = Note("n", folder);
        Delete(note);
        DeleteFolder(Folder("G"));
        string before = Snapshot();
        Sql("CREATE TRIGGER inject BEFORE DELETE ON Folders BEGIN SELECT RAISE(ABORT, 'injected'); END;");

        Assert.Throws<StorageException>(() => Empty());

        // The notes were deleted first in the same transaction; the folder failure takes them back.
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void A_failure_while_purging_a_folder_keeps_its_notes()
    {
        FolderId folder = Folder("F");
        Note("n", folder);
        DeleteFolder(folder);
        string before = Snapshot();
        Sql("CREATE TRIGGER inject BEFORE DELETE ON Folders BEGIN SELECT RAISE(ABORT, 'injected'); END;");

        Assert.Throws<StorageException>(() => PurgeFolder(folder));

        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void A_failure_while_restoring_into_another_folder_leaves_the_note_binned()
    {
        FolderId from = Folder("From");
        FolderId to = Folder("To");
        NoteId note = Note("n", from);
        Delete(note);
        string before = Snapshot();
        Sql("CREATE TRIGGER inject BEFORE UPDATE OF DeletedAt ON Notes BEGIN SELECT RAISE(ABORT, 'injected'); END;");

        Assert.Throws<StorageException>(() => RestoreInto(note, to));

        Assert.Equal(before, Snapshot());
    }

    // ---- I7 across the existing commands -----------------------------------------

    [Fact]
    public void Deleting_and_restoring_folders_and_notes_in_any_order_never_breaks_I7()
    {
        FolderId a = Folder("A");
        FolderId b = Folder("B");
        NoteId n1 = Note("1", a);
        NoteId n2 = Note("2", a);
        NoteId n3 = Note("3", b);

        Delete(n1);
        AssertI7();
        DeleteFolder(a);
        AssertI7();
        Assert.False(Restore(n1).IsSuccess);
        AssertI7();
        Assert.True(RestoreInto(n2, b).IsSuccess);
        AssertI7();
        Assert.True(new RestoreFolderHandler(_folders, _clock).Handle(new RestoreFolder(a)).IsSuccess);
        AssertI7();
        Assert.True(Restore(n1).IsSuccess || _notes.GetLifecycle(n1) == NoteLifecycle.Active);
        DeleteFolder(b);
        AssertI7();
        Assert.True(Empty().IsSuccess);
        AssertI7();
        Assert.NotNull(_notes.FindActive(n1));
        Assert.Equal(NoteLifecycle.Missing, _notes.GetLifecycle(n3));
    }

    // ---- helpers ---------------------------------------------------------------

    /// <summary>
    /// I7 on the whole database: no active note lives in a folder that is binned or missing. A note at the root has
    /// no folder to test (its surface is a P1.4 question), and is not counted here.
    /// </summary>
    private void AssertI7()
    {
        long stranded = Scalar(
            """
            SELECT COUNT(*) FROM Notes n
            WHERE n.DeletedAt IS NULL AND n.FolderId IS NOT NULL
              AND NOT EXISTS (SELECT 1 FROM Folders f WHERE f.Id = n.FolderId AND f.DeletedAt IS NULL);
            """);
        Assert.True(stranded == 0, $"I7 broken: {stranded} active note(s) in a binned or missing folder.");
    }

    /// <summary>Every row of both tables, so "changes nothing" is checked on the database, not on one field.</summary>
    private string Snapshot()
    {
        using SqliteConnection connection = _database.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT 'F|' || Id || '|' || Name || '|' || SortOrder || '|' || IFNULL(DeletedAt, '-') || '|' || UpdatedAt FROM Folders
            UNION ALL
            SELECT 'N|' || Id || '|' || IFNULL(FolderId, '-') || '|' || SortOrder || '|' || IFNULL(DeletedAt, '-') || '|' || UpdatedAt || '|' || Content FROM Notes
            UNION ALL
            SELECT 'T|' || NoteId || '|' || TagId FROM NoteTags
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

    private FolderId Folder(string name) =>
        new CreateFolderHandler(_folders, _clock).Handle(new CreateFolder(name)).Value;

    private NoteId Note(string content, FolderId folder)
    {
        _clock.Advance(TimeSpan.FromSeconds(1));
        return new CreateNoteHandler(_notes, _clock).Handle(new CreateNote(folder, content)).Value;
    }

    private void Delete(NoteId id) =>
        Assert.True(new DeleteNoteHandler(_notes, _clock).Handle(new DeleteNote(id)).IsSuccess);

    private void DeleteFolder(FolderId id) =>
        Assert.True(new DeleteFolderHandler(_folders, _clock).Handle(new DeleteFolder(id)).IsSuccess);

    private CommandResult Restore(NoteId id) => new RestoreNoteHandler(_notes, _clock).Handle(new RestoreNote(id));

    private CommandResult RestoreInto(NoteId id, FolderId folder) =>
        new RestoreNoteToFolderHandler(_notes, _clock).Handle(new RestoreNoteToFolder(id, folder));

    private CommandResult PurgeNote(NoteId id) => new PurgeNoteHandler(_notes).Handle(new PurgeNote(id));

    private CommandResult PurgeFolder(FolderId id) => new PurgeFolderHandler(_folders).Handle(new PurgeFolder(id));

    private CommandResult<RecycleBinPurge> Empty() => new EmptyRecycleBinHandler(_folders).Handle(new EmptyRecycleBin());

    private List<NoteId> Listed(FolderId folder) => [.. new ListNotesInFolderQuery(_notes).Execute(folder).Select(n => n.Id)];

    // The legacy state: a folder in the bin with an active note in it, as Case C used to leave it.
    private void MarkFolderDeletedOnly(FolderId id) =>
        Sql("UPDATE Folders SET DeletedAt = '2026-01-01T00:00:00.0000000+00:00' WHERE Id = $p;", id.Value);

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

    private long Scalar(string sql, string? p = null)
    {
        using SqliteConnection connection = _database.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        if (p is not null)
        {
            command.Parameters.AddWithValue("$p", p);
        }

        return (long)command.ExecuteScalar()!;
    }

    private sealed class Clock(DateTimeOffset start) : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = start;

        public void Advance(TimeSpan by) => UtcNow += by;
    }
}
