using Noto.Core;
using Noto.Core.Commands;
using Noto.Core.Folders;
using Noto.Core.Notes;
using Noto.Core.Tags;
using Noto.UseCases.Notes;
using Xunit;

namespace Noto.UseCases.Tests.Notes;

/// <summary>
/// The handlers' last line of defence: their check passed, but by the time the repository writes, something changed
/// (another writer purged or restored first). The repository refuses inside its transaction, and the handler must
/// report that refusal, never a success.
/// </summary>
public sealed class RecycleBinRaceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_purge_the_repository_refuses_is_NotFound_not_success()
    {
        var notes = new RacingNotes { Lifecycle = NoteLifecycle.Deleted, Writes = false };

        CommandResult result = new PurgeNoteHandler(notes).Handle(new PurgeNote(NoteId.New()));

        Assert.False(result.IsSuccess);
        Assert.Equal(CommandFailureReason.NotFound, result.Failure!.Reason);
        Assert.Equal(1, notes.WriteAttempts);
    }

    [Fact]
    public void A_restore_into_a_folder_the_repository_refuses_is_InvalidState_not_success()
    {
        var notes = new RacingNotes { Lifecycle = NoteLifecycle.Deleted, Folder = FolderState.Active, Writes = false };

        CommandResult result = new RestoreNoteToFolderHandler(notes, new FixedClock()).Handle(new RestoreNoteToFolder(NoteId.New(), FolderId.New()));

        Assert.False(result.IsSuccess);
        Assert.Equal(CommandFailureReason.InvalidState, result.Failure!.Reason);
        Assert.Equal(1, notes.WriteAttempts);
    }

    [Fact]
    public void When_the_repository_writes_both_succeed()
    {
        var notes = new RacingNotes { Lifecycle = NoteLifecycle.Deleted, Folder = FolderState.Active, Writes = true };

        Assert.True(new PurgeNoteHandler(notes).Handle(new PurgeNote(NoteId.New())).IsSuccess);
        Assert.True(new RestoreNoteToFolderHandler(notes, new FixedClock()).Handle(new RestoreNoteToFolder(NoteId.New(), FolderId.New())).IsSuccess);
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    /// <summary>Reports the states it is told to, and writes (or refuses) as told. Nothing else is used.</summary>
    private sealed class RacingNotes : INoteRepository
    {
        public NoteLifecycle Lifecycle { get; init; }

        public FolderState Folder { get; init; }

        public bool Writes { get; init; }

        public int WriteAttempts { get; private set; }

        public NoteLifecycle GetLifecycle(NoteId id) => Lifecycle;

        public FolderState GetFolderState(FolderId id) => Folder;

        public bool Purge(NoteId id)
        {
            WriteAttempts++;
            return Writes;
        }

        public bool RestoreInto(NoteId id, FolderId folderId, DateTimeOffset updatedAt)
        {
            WriteAttempts++;
            return Writes;
        }

        public void Add(Note note) => throw new NotSupportedException();

        public Note? FindActive(NoteId id) => throw new NotSupportedException();

        public double? MaxSortOrder(FolderId? folderId) => throw new NotSupportedException();

        public void UpdateContent(NoteId id, string content, DateTimeOffset updatedAt) => throw new NotSupportedException();

        public void Move(NoteId id, FolderId targetFolderId, NotePlacement placement, DateTimeOffset updatedAt) => throw new NotSupportedException();

        public bool Reorder(NoteId id, NotePlacement placement, DateTimeOffset updatedAt) => throw new NotSupportedException();

        public bool IsActiveSiblingIn(NoteId id, FolderId? folderId) => throw new NotSupportedException();

        public void SetPinned(NoteId id, bool isPinned, DateTimeOffset updatedAt) => throw new NotSupportedException();

        public void SetFolded(NoteId id, bool isFolded, DateTimeOffset updatedAt) => throw new NotSupportedException();

        public void SetColor(NoteId id, string? colorKey, DateTimeOffset updatedAt) => throw new NotSupportedException();

        public void SoftDelete(NoteId id, DateTimeOffset deletedAt) => throw new NotSupportedException();

        public bool Restore(NoteId id, DateTimeOffset updatedAt) => throw new NotSupportedException();

        public IReadOnlyList<Note> ListDeleted() => throw new NotSupportedException();

        public IReadOnlyList<Note> ListInFolder(FolderId? folderId) => throw new NotSupportedException();

        public IReadOnlyList<Note> ListForTag(TagId tagId) => throw new NotSupportedException();
    }
}
