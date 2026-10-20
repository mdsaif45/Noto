using Noto.Core;
using Noto.Core.Commands;
using Noto.Core.Folders;
using Noto.Core.Notes;
using Noto.UseCases.Commands;

namespace Noto.UseCases.Notes;

/// <summary>
/// Brings a note out of the recycle bin into a chosen live folder, in one transaction (deletion-semantics §13).
/// </summary>
/// <remarks>
/// How a note whose own folder is in the bin comes back without that folder: into a folder the user can see (I7).
/// Into its own folder it keeps its place (I6); into another it lands at the end, as a move does (O4).
/// </remarks>
public sealed record RestoreNoteToFolder(NoteId NoteId, FolderId FolderId) : ICommand<CommandResult>;

/// <summary>A note has been restored into a folder. Raised only after the restore is written.</summary>
public sealed record NoteRestored(NoteId NoteId, FolderId FolderId) : IEvent;

/// <summary>Handles <see cref="RestoreNoteToFolder"/>.</summary>
public sealed class RestoreNoteToFolderHandler(INoteRepository notes, IClock clock)
{
    private readonly INoteRepository _notes = notes ?? throw new ArgumentNullException(nameof(notes));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    public CommandResult Handle(RestoreNoteToFolder command)
    {
        ArgumentNullException.ThrowIfNull(command);

        CommandFailure? refused = RecycleBinRules.RefuseRestoreInto(
            command.NoteId,
            _notes.GetLifecycle(command.NoteId),
            command.FolderId,
            _notes.GetFolderState(command.FolderId));

        if (refused is not null)
        {
            return CommandResult.Failed(refused);
        }

        // The repository re-checks both preconditions inside its transaction; a refusal there means one changed.
        return _notes.RestoreInto(command.NoteId, command.FolderId, _clock.UtcNow)
            ? CommandResult.Success()
            : CommandResult.Failed(CommandFailure.InvalidState(
                $"Note '{command.NoteId}' could not be restored into folder '{command.FolderId}': one of them changed."));
    }
}
