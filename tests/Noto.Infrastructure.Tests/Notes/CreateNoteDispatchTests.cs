using Noto.Core;
using Noto.Core.Commands;
using Noto.Core.Diagnostics;
using Noto.Core.Folders;
using Noto.Core.Notes;
using Noto.Infrastructure.Storage;
using Noto.Infrastructure.Tests.Storage;
using Noto.UseCases.Commands;
using Noto.UseCases.Folders;
using Noto.UseCases.Notes;
using Xunit;

namespace Noto.Infrastructure.Tests.Notes;

/// <summary>
/// #21's reference path, end to end against real SQLite: <c>CreateNote</c> dispatched through the registry it is wired
/// with in the composition root, the row written, and <c>NoteCreated</c> published after it, carrying that row's id.
/// </summary>
public sealed class CreateNoteDispatchTests : IDisposable
{
    private readonly TempDatabase _temp = new();
    private readonly SqliteNoteRepository _notes;
    private readonly SqliteFolderRepository _folders;
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 10, 13, 9, 0, 0, TimeSpan.Zero));
    private readonly List<ExceptionSummary> _subscriberFailures = [];
    private readonly EventHub _events;
    private readonly CommandDispatcher _dispatcher;

    public CreateNoteDispatchTests()
    {
        var database = new NotoDatabase(_temp.DatabasePath);
        database.Initialize();
        _notes = new SqliteNoteRepository(database);
        _folders = new SqliteFolderRepository(database);
        _events = new EventHub(_subscriberFailures.Add);

        // As App.xaml.cs registers it.
        var registry = new CommandRegistry()
            .Register<CreateNote, CommandResult<NoteId>, NoteCreated>(
                new CreateNoteHandler(_notes, _clock).Handle,
                static (command, result) => new NoteCreated(result.Value, command.FolderId));
        _dispatcher = new CommandDispatcher(registry, _events);
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void A_dispatched_create_writes_the_note_then_announces_it_with_its_id_and_folder()
    {
        FolderId folder = new CreateFolderHandler(_folders, _clock).Handle(new CreateFolder("Inbox")).Value;
        var told = new List<NoteCreated>();
        using var subscription = _events.Subscribe<NoteCreated>(e =>
        {
            // The row is already there when the event arrives: the mutation precedes its announcement.
            Assert.NotNull(_notes.FindActive(e.NoteId));
            told.Add(e);
        });

        CommandResult<NoteId> result = _dispatcher.Dispatch(new CreateNote(folder, "Dispatched"));

        Assert.True(result.IsSuccess);
        Assert.Equal("Dispatched", _notes.FindActive(result.Value)!.Content);
        Assert.Equal([new NoteCreated(result.Value, folder)], told);
    }

    [Fact]
    public void A_create_that_fails_writes_nothing_and_announces_nothing()
    {
        FolderId folder = new CreateFolderHandler(_folders, _clock).Handle(new CreateFolder("Gone")).Value;
        Assert.True(new DeleteFolderHandler(_folders, _clock).Handle(new DeleteFolder(folder)).IsSuccess);
        var told = new List<NoteCreated>();
        using var subscription = _events.Subscribe<NoteCreated>(told.Add);

        CommandResult<NoteId> result = _dispatcher.Dispatch(new CreateNote(folder, "Nowhere"));

        Assert.False(result.IsSuccess);
        Assert.Equal(CommandFailureReason.InvalidState, result.Failure!.Reason);
        Assert.Empty(_notes.ListInFolder(folder));
        Assert.Empty(told);
    }

    [Fact]
    public void A_subscriber_that_throws_leaves_the_note_created_and_is_reported()
    {
        using var subscription = _events.Subscribe<NoteCreated>(_ => throw new InvalidOperationException());

        FolderId folder = new CreateFolderHandler(_folders, _clock).Handle(new CreateFolder("Inbox")).Value;

        CommandResult<NoteId> result = _dispatcher.Dispatch(new CreateNote(folder, "Kept"));

        Assert.True(result.IsSuccess);
        Assert.Equal("Kept", _notes.FindActive(result.Value)!.Content);
        Assert.Single(_subscriberFailures);
    }

    /// <summary>A clock that does not move.</summary>
    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
