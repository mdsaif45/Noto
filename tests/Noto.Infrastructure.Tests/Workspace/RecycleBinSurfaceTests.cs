using Microsoft.Data.Sqlite;
using Noto.Core;
using Noto.Core.Commands;
using Noto.Core.Folders;
using Noto.Core.Notes;
using Noto.Infrastructure.Storage;
using Noto.Infrastructure.Tests.Storage;
using Noto.UseCases.Commands;
using Noto.UseCases.Folders;
using Noto.UseCases.Notes;
using Noto.UseCases.Workspace;
using Xunit;

namespace Noto.Infrastructure.Tests.Workspace;

/// <summary>
/// Phase B5 against real SQLite (workspace-management.md, "B5 precisely"): the Recycle Bin's entries and actions go
/// through the engine's own P1.1 commands. Deleting a folder is soft and takes its notes; nothing is destroyed but by
/// an explicit purge or empty; I7 holds throughout.
/// </summary>
public sealed class RecycleBinSurfaceTests : IDisposable
{
    private readonly TempDatabase _temp = new();
    private readonly NotoDatabase _database;
    private readonly SqliteNoteRepository _notes;
    private readonly SqliteFolderRepository _folders;
    private readonly Clock _clock = new(new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero));
    private readonly EventHub _events = new(_ => { });
    private readonly RecycleBin _bin;
    private readonly FolderId _work;
    private readonly FolderId _home;
    private readonly NoteId _plan;
    private readonly NoteId _todo;
    private readonly NoteId _milk;

    public RecycleBinSurfaceTests()
    {
        _database = new NotoDatabase(_temp.DatabasePath);
        _database.Initialize();
        _notes = new SqliteNoteRepository(_database);
        _folders = new SqliteFolderRepository(_database);
        var dispatcher = new CommandDispatcher(
            new CommandRegistry()
                .Register<DeleteNote, CommandResult>(new DeleteNoteHandler(_notes, _clock).Handle)
                .Register<DeleteFolder, CommandResult, FolderDeleted>(new DeleteFolderHandler(_folders, _clock).Handle, static (c, _) => new FolderDeleted(c.FolderId))
                .Register<RestoreFolder, CommandResult, FolderRestored>(new RestoreFolderHandler(_folders, _clock).Handle, static (c, _) => new FolderRestored(c.FolderId))
                .Register<RestoreNote, CommandResult>(new RestoreNoteHandler(_notes, _clock).Handle)
                .Register<RestoreNoteToFolder, CommandResult, NoteRestored>(new RestoreNoteToFolderHandler(_notes, _clock).Handle, static (c, _) => new NoteRestored(c.NoteId, c.FolderId))
                .Register<PurgeNote, CommandResult, NotePurged>(new PurgeNoteHandler(_notes).Handle, static (c, _) => new NotePurged(c.NoteId))
                .Register<PurgeFolder, CommandResult, FolderPurged>(new PurgeFolderHandler(_folders).Handle, static (c, _) => new FolderPurged(c.FolderId))
                .Register<EmptyRecycleBin, CommandResult<RecycleBinPurge>, RecycleBinEmptied>(new EmptyRecycleBinHandler(_folders).Handle, static (_, r) => new RecycleBinEmptied(r.Value.NotesPurged, r.Value.FoldersPurged, r.Value.FoldersKept.Count)),
            _events);
        _bin = new RecycleBin(
            new ListDeletedFoldersQuery(_folders),
            new ListDeletedNotesQuery(_notes),
            new ListFoldersQuery(_folders, _notes),
            new ListNotesInFolderQuery(_notes),
            dispatcher);
        _deleteNote = note => dispatcher.Dispatch(new DeleteNote(note));

        _work = CreateFolder("Work");
        _home = CreateFolder("Home");
        _plan = Create(_work, "Plan");
        _todo = Create(_work, "Todo");
        _milk = Create(_home, "Milk");
    }

    private readonly Func<NoteId, CommandResult> _deleteNote;

    public void Dispose() => _temp.Dispose();

    // ---- deleting a folder --------------------------------------------------------------------------

    [Fact]
    public void Deleting_a_folder_is_soft_takes_its_notes_and_destroys_nothing()
    {
        Assert.Equal(2, _bin.NotesIn(_work));
        long rows = Count("SELECT COUNT(*) FROM Notes;") + Count("SELECT COUNT(*) FROM Folders;");

        Advance();
        Assert.True(_bin.DeleteFolder(_work).IsSuccess);

        Assert.Equal(rows, Count("SELECT COUNT(*) FROM Notes;") + Count("SELECT COUNT(*) FROM Folders;"));
        Assert.Null(_folders.FindActive(_work));
        Assert.Null(_notes.FindActive(_plan));
        Assert.Null(_notes.FindActive(_todo));
        Assert.NotNull(_notes.FindActive(_milk));
        Assert.Equal(1L, Count("SELECT COUNT(DISTINCT DeletedAt) FROM Notes WHERE FolderId = (SELECT Id FROM Folders WHERE Name = 'Work') AND DeletedAt IS NOT NULL;"));
        AssertI7();
    }

    [Fact]
    public void Deleting_a_folder_is_announced_after_it_commits_and_a_second_delete_is_refused_without_writing()
    {
        var told = new List<FolderDeleted>();
        using var subscription = _events.Subscribe<FolderDeleted>(e =>
        {
            Assert.Null(_folders.FindActive(e.FolderId));
            told.Add(e);
        });

        Assert.True(_bin.DeleteFolder(_work).IsSuccess);
        string before = Snapshot();

        Assert.Equal(CommandFailureReason.InvalidState, _bin.DeleteFolder(_work).Failure!.Reason);

        Assert.Equal(before, Snapshot());
        Assert.Equal([new FolderDeleted(_work)], told);
    }

    // ---- the entries --------------------------------------------------------------------------------

    [Fact]
    public void Entries_are_most_recent_first_folders_with_their_count_notes_with_their_folder_and_its_state()
    {
        Advance();
        Assert.True(_deleteNote(_milk).IsSuccess);
        Advance();
        Assert.True(_bin.DeleteFolder(_work).IsSuccess);

        IReadOnlyList<RecycleBinEntry> entries = _bin.Entries();

        Assert.Equal(["Work", "Plan", "Todo", "Milk"], entries.Select(e => e.Title));
        RecycleBinEntry work = entries[0];
        Assert.Equal((RecycleBinEntryKind.Folder, 2, true), (work.Kind, work.NoteCount, work.CanRestore));

        RecycleBinEntry plan = entries[1];
        Assert.Equal((RecycleBinEntryKind.Note, "Work", true, false), (plan.Kind, plan.FolderName, plan.FolderInBin, plan.CanRestore));

        RecycleBinEntry milk = entries[3];
        Assert.Equal(("Home", false, true), (milk.FolderName, milk.FolderInBin, milk.CanRestore));
    }

    [Fact]
    public void An_empty_bin_has_no_entries()
    {
        Assert.Empty(_bin.Entries());
    }

    // ---- restore ------------------------------------------------------------------------------------

    [Fact]
    public void Restoring_a_folder_brings_back_every_note_of_it_in_the_bin()
    {
        Advance();
        Assert.True(_deleteNote(_plan).IsSuccess);
        Advance();
        Assert.True(_bin.DeleteFolder(_work).IsSuccess);

        Assert.True(_bin.Restore(_bin.Entries().Single(e => e.Title == "Work")).IsSuccess);

        Assert.NotNull(_folders.FindActive(_work));
        Assert.NotNull(_notes.FindActive(_plan));
        Assert.NotNull(_notes.FindActive(_todo));
        Assert.Empty(_bin.Entries());
        AssertI7();
    }

    [Fact]
    public void Restoring_a_note_whose_folder_is_live_puts_it_back_there()
    {
        Assert.True(_deleteNote(_milk).IsSuccess);

        Assert.True(_bin.Restore(_bin.Entries().Single()).IsSuccess);

        Assert.Equal(_home, _notes.FindActive(_milk)!.FolderId);
        AssertI7();
    }

    [Fact]
    public void A_note_whose_folder_is_in_the_bin_is_not_restored_there_but_can_be_restored_elsewhere()
    {
        Assert.True(_bin.DeleteFolder(_work).IsSuccess);
        RecycleBinEntry plan = _bin.Entries().Single(e => e.Title == "Plan");
        string before = Snapshot();

        Assert.False(plan.CanRestore);
        Assert.Equal(CommandFailureReason.InvalidState, _bin.Restore(plan).Failure!.Reason);
        Assert.Equal(before, Snapshot());

        Assert.True(_bin.RestoreTo(plan, _home).IsSuccess);
        Assert.Equal(_home, _notes.FindActive(_plan)!.FolderId);
        Assert.Null(_folders.FindActive(_work));
        AssertI7();
    }

    [Fact]
    public void A_folder_is_never_restored_into_another_folder()
    {
        Assert.True(_bin.DeleteFolder(_work).IsSuccess);

        Assert.Equal(CommandFailureReason.InvalidInput, _bin.RestoreTo(_bin.Entries()[0], _home).Failure!.Reason);
        Assert.Null(_folders.FindActive(_work));
    }

    // ---- purge and empty ----------------------------------------------------------------------------

    [Fact]
    public void Purging_a_note_removes_only_it()
    {
        Assert.True(_deleteNote(_milk).IsSuccess);

        Assert.True(_bin.Purge(_bin.Entries().Single()).IsSuccess);

        Assert.Equal(0L, Count($"SELECT COUNT(*) FROM Notes WHERE Id = '{_milk.Value}';"));
        Assert.NotNull(_folders.FindActive(_home));
        Assert.Empty(_bin.Entries());
    }

    [Fact]
    public void Purging_a_folder_removes_it_with_its_notes_in_the_bin_and_nothing_else()
    {
        Assert.True(_bin.DeleteFolder(_work).IsSuccess);

        Assert.True(_bin.Purge(_bin.Entries().Single(e => e.Kind == RecycleBinEntryKind.Folder)).IsSuccess);

        Assert.Equal(0L, Count($"SELECT COUNT(*) FROM Folders WHERE Id = '{_work.Value}';"));
        Assert.Equal(0L, Count($"SELECT COUNT(*) FROM Notes WHERE FolderId = '{_work.Value}';"));
        Assert.NotNull(_notes.FindActive(_milk));
        AssertI7();
    }

    [Fact]
    public void Purging_something_no_longer_in_the_bin_is_refused_and_changes_nothing()
    {
        Assert.True(_deleteNote(_milk).IsSuccess);
        RecycleBinEntry milk = _bin.Entries().Single();
        Assert.True(_bin.Restore(milk).IsSuccess);
        string before = Snapshot();

        Assert.False(_bin.Purge(milk).IsSuccess);

        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void Emptying_the_bin_purges_everything_in_it_and_keeps_every_active_note()
    {
        Assert.True(_deleteNote(_milk).IsSuccess);
        Assert.True(_bin.DeleteFolder(_work).IsSuccess);
        var told = new List<RecycleBinEmptied>();
        using var subscription = _events.Subscribe<RecycleBinEmptied>(told.Add);

        CommandResult<RecycleBinPurge> result = _bin.Empty();

        Assert.True(result.IsSuccess);
        Assert.Equal((3, 1, 0), (result.Value.NotesPurged, result.Value.FoldersPurged, result.Value.FoldersKept.Count));
        Assert.Empty(_bin.Entries());
        Assert.NotNull(_folders.FindActive(_home));
        Assert.Equal([new RecycleBinEmptied(3, 1, 0)], told);
        AssertI7();
    }

    [Fact]
    public void Restore_and_purge_are_announced_after_they_commit()
    {
        Assert.True(_bin.DeleteFolder(_work).IsSuccess);
        Assert.True(_deleteNote(_milk).IsSuccess);
        var told = new List<object>();
        using var restored = _events.Subscribe<FolderRestored>(e =>
        {
            Assert.NotNull(_folders.FindActive(e.FolderId));
            told.Add(e);
        });
        using var purged = _events.Subscribe<NotePurged>(e =>
        {
            Assert.Equal(0L, Count($"SELECT COUNT(*) FROM Notes WHERE Id = '{e.NoteId.Value}';"));
            told.Add(e);
        });

        Assert.True(_bin.Restore(_bin.Entries().Single(e => e.Title == "Work")).IsSuccess);
        Assert.True(_bin.Purge(_bin.Entries().Single()).IsSuccess);

        Assert.Equal([new FolderRestored(_work), new NotePurged(_milk)], told);
    }

    // ---- helpers ------------------------------------------------------------------------------------

    /// <summary>I7: every active note is in a live folder.</summary>
    private void AssertI7() =>
        Assert.Equal(0L, Count("SELECT COUNT(*) FROM Notes n WHERE n.DeletedAt IS NULL AND NOT EXISTS (SELECT 1 FROM Folders f WHERE f.Id = n.FolderId AND f.DeletedAt IS NULL);"));

    private void Advance() => _clock.Advance(TimeSpan.FromSeconds(1));

    private FolderId CreateFolder(string name)
    {
        Advance();
        return new CreateFolderHandler(_folders, _clock).Handle(new CreateFolder(name)).Value;
    }

    private NoteId Create(FolderId folder, string content)
    {
        Advance();
        return new CreateNoteHandler(_notes, _clock).Handle(new CreateNote(folder, content)).Value;
    }

    private string Snapshot()
    {
        using SqliteConnection connection = _database.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT 'N|' || Id || '|' || FolderId || '|' || IFNULL(DeletedAt, '') || '|' || UpdatedAt FROM Notes
            UNION ALL
            SELECT 'F|' || Id || '|' || IFNULL(DeletedAt, '') || '|' || UpdatedAt FROM Folders
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

    private long Count(string sql)
    {
        using SqliteConnection connection = _database.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)command.ExecuteScalar()!;
    }

    private sealed class Clock(DateTimeOffset start) : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = start;

        public void Advance(TimeSpan by) => UtcNow += by;
    }
}
