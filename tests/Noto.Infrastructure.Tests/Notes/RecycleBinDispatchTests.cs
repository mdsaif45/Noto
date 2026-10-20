using Microsoft.Data.Sqlite;
using Noto.Core;
using Noto.Core.Commands;
using Noto.Core.Diagnostics;
using Noto.Core.Folders;
using Noto.Core.Notes;
using Noto.Core.Storage;
using Noto.Infrastructure.Storage;
using Noto.Infrastructure.Tests.Storage;
using Noto.UseCases.Commands;
using Noto.UseCases.Folders;
using Noto.UseCases.Notes;
using Xunit;

namespace Noto.Infrastructure.Tests.Notes;

/// <summary>
/// P1.1's commands through the dispatcher (ADR-010, #21), against real SQLite: command, handler, transactional write,
/// success, then exactly one typed event. A refusal or a failure announces nothing.
/// </summary>
public sealed class RecycleBinDispatchTests : IDisposable
{
    private readonly TempDatabase _temp = new();
    private readonly NotoDatabase _database;
    private readonly SqliteNoteRepository _notes;
    private readonly SqliteFolderRepository _folders;
    private readonly Clock _clock = new(new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero));
    private readonly List<ExceptionSummary> _subscriberFailures = [];
    private readonly EventHub _events;
    private readonly CommandDispatcher _dispatcher;
    private readonly List<IEvent> _told = [];

    public RecycleBinDispatchTests()
    {
        _database = new NotoDatabase(_temp.DatabasePath);
        _database.Initialize();
        _notes = new SqliteNoteRepository(_database);
        _folders = new SqliteFolderRepository(_database);
        _events = new EventHub(_subscriberFailures.Add);

        // As the composition root will register them (P1.3 wires the Recycle Bin surface).
        var registry = new CommandRegistry()
            .Register<PurgeNote, CommandResult, NotePurged>(
                new PurgeNoteHandler(_notes).Handle,
                static (command, _) => new NotePurged(command.NoteId))
            .Register<RestoreNoteToFolder, CommandResult, NoteRestored>(
                new RestoreNoteToFolderHandler(_notes, _clock).Handle,
                static (command, _) => new NoteRestored(command.NoteId, command.FolderId))
            .Register<PurgeFolder, CommandResult, FolderPurged>(
                new PurgeFolderHandler(_folders).Handle,
                static (command, _) => new FolderPurged(command.FolderId))
            .Register<EmptyRecycleBin, CommandResult<RecycleBinPurge>, RecycleBinEmptied>(
                new EmptyRecycleBinHandler(_folders).Handle,
                static (_, result) => new RecycleBinEmptied(result.Value.NotesPurged, result.Value.FoldersPurged, result.Value.FoldersKept.Count));
        _dispatcher = new CommandDispatcher(registry, _events);
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void A_purged_note_is_announced_once_after_its_row_is_gone()
    {
        NoteId note = BinnedNote();
        using var subscription = _events.Subscribe<NotePurged>(e =>
        {
            Assert.Equal(NoteLifecycle.Missing, _notes.GetLifecycle(e.NoteId));
            _told.Add(e);
        });

        Assert.True(_dispatcher.Dispatch(new PurgeNote(note)).IsSuccess);

        Assert.Equal([new NotePurged(note)], _told);
        Assert.Empty(_subscriberFailures);
    }

    [Fact]
    public void A_restored_note_is_announced_once_after_it_is_listed_in_its_folder()
    {
        NoteId note = BinnedNote();
        FolderId target = Folder("Target");
        using var subscription = _events.Subscribe<NoteRestored>(e =>
        {
            Assert.Contains(_notes.ListInFolder(e.FolderId), n => n.Id == e.NoteId);
            _told.Add(e);
        });

        Assert.True(_dispatcher.Dispatch(new RestoreNoteToFolder(note, target)).IsSuccess);

        Assert.Equal([new NoteRestored(note, target)], _told);
        Assert.Empty(_subscriberFailures);
    }

    [Fact]
    public void A_purged_folder_is_announced_once_after_it_is_gone()
    {
        FolderId folder = Folder("Old");
        Delete(folder);
        using var subscription = _events.Subscribe<FolderPurged>(e =>
        {
            Assert.Equal(FolderLifecycle.Missing, _folders.GetLifecycle(e.FolderId));
            _told.Add(e);
        });

        Assert.True(_dispatcher.Dispatch(new PurgeFolder(folder)).IsSuccess);

        Assert.Equal([new FolderPurged(folder)], _told);
        Assert.Empty(_subscriberFailures);
    }

    [Fact]
    public void An_emptied_bin_is_announced_once_with_its_counts_after_it_commits()
    {
        BinnedNote();
        FolderId folder = Folder("Old");
        Delete(folder);
        using var subscription = _events.Subscribe<RecycleBinEmptied>(e =>
        {
            Assert.Empty(_notes.ListDeleted());
            _told.Add(e);
        });

        Assert.True(_dispatcher.Dispatch(new EmptyRecycleBin()).IsSuccess);

        Assert.Equal([new RecycleBinEmptied(1, 1, 0)], _told);
        Assert.Empty(_subscriberFailures);
    }

    [Fact]
    public void A_refused_command_announces_nothing()
    {
        FolderId live = Folder("Live");
        NoteId active = Note(live);
        FolderId binned = Folder("Binned");
        Delete(binned);
        NoteId binnedNote = BinnedNote();
        using var a = _events.Subscribe<NotePurged>(_told.Add);
        using var b = _events.Subscribe<NoteRestored>(_told.Add);
        using var c = _events.Subscribe<FolderPurged>(_told.Add);

        Assert.False(_dispatcher.Dispatch(new PurgeNote(active)).IsSuccess);
        Assert.False(_dispatcher.Dispatch(new PurgeNote(NoteId.New())).IsSuccess);
        Assert.False(_dispatcher.Dispatch(new RestoreNoteToFolder(binnedNote, binned)).IsSuccess);
        Assert.False(_dispatcher.Dispatch(new RestoreNoteToFolder(active, live)).IsSuccess);
        Assert.False(_dispatcher.Dispatch(new PurgeFolder(live)).IsSuccess);

        Assert.Empty(_told);
    }

    [Fact]
    public void A_command_that_fails_inside_its_transaction_announces_nothing()
    {
        BinnedNote();
        FolderId folder = Folder("Old");
        Delete(folder);
        using var a = _events.Subscribe<RecycleBinEmptied>(_told.Add);
        using var b = _events.Subscribe<FolderPurged>(_told.Add);
        Sql("CREATE TRIGGER inject BEFORE DELETE ON Folders BEGIN SELECT RAISE(ABORT, 'injected'); END;");

        Assert.Throws<StorageException>(() => _dispatcher.Dispatch(new EmptyRecycleBin()));
        Assert.Throws<StorageException>(() => _dispatcher.Dispatch(new PurgeFolder(folder)));

        Assert.Empty(_told);
        Assert.Single(_notes.ListDeleted());
    }

    private FolderId Folder(string name) =>
        new CreateFolderHandler(_folders, _clock).Handle(new CreateFolder(name)).Value;

    private NoteId Note(FolderId folder) =>
        new CreateNoteHandler(_notes, _clock).Handle(new CreateNote(folder, "n")).Value;

    private NoteId BinnedNote()
    {
        NoteId note = Note(Folder("Home"));
        Assert.True(new DeleteNoteHandler(_notes, _clock).Handle(new DeleteNote(note)).IsSuccess);
        return note;
    }

    private void Delete(FolderId folder) =>
        Assert.True(new DeleteFolderHandler(_folders, _clock).Handle(new DeleteFolder(folder)).IsSuccess);

    private void Sql(string sql)
    {
        using SqliteConnection connection = _database.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private sealed class Clock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
