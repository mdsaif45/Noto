using Noto.Core;
using Noto.Core.Commands;
using Noto.Core.Folders;
using Noto.Core.Notes;

namespace Noto.UseCases.Notes;

/// <summary>
/// Moves a note to another folder, or to root (design §6, parity B11).
/// </summary>
/// <param name="TargetFolderId">
/// The destination, or <see langword="null"/> for the root scope.
/// </param>
public sealed record MoveNoteToFolder(NoteId NoteId, FolderId? TargetFolderId);

/// <summary>
/// Handles <see cref="MoveNoteToFolder"/>.
/// </summary>
/// <remarks>
/// One of the five atomic operations (design §9): the folder change and the new
/// <c>SortOrder</c> commit together or not at all.
/// </remarks>
public sealed class MoveNoteToFolderHandler(INoteRepository notes, IClock clock)
{
    private readonly INoteRepository _notes = notes
        ?? throw new ArgumentNullException(nameof(notes));

    private readonly IClock _clock = clock
        ?? throw new ArgumentNullException(nameof(clock));

    public CommandResult Handle(MoveNoteToFolder command)
    {
        ArgumentNullException.ThrowIfNull(command);

        switch (_notes.GetLifecycle(command.NoteId))
        {
            case NoteLifecycle.Missing:
                return CommandResult.Failed(
                    CommandFailure.NotFound($"No note '{command.NoteId}'."));

            case NoteLifecycle.Deleted:
                // I5: a deleted note cannot be moved.
                return CommandResult.Failed(
                    CommandFailure.InvalidState(
                        $"Note '{command.NoteId}' is deleted and cannot be moved."));

            case NoteLifecycle.Active:
            default:
                break;
        }

        // Note the asymmetry with the note's own state above: the note's
        // CURRENT folder may be deleted and the move still proceeds (Case C —
        // an active note may live in a deleted folder). Only the DESTINATION
        // is validated.
        if (command.TargetFolderId is { } target)
        {
            switch (_notes.GetFolderState(target))
            {
                case FolderState.Missing:
                    return CommandResult.Failed(
                        CommandFailure.NotFound($"No folder '{target}'."));

                case FolderState.Deleted:
                    return CommandResult.Failed(
                        CommandFailure.InvalidState(
                            $"Folder '{target}' is deleted and cannot receive a note."));

                case FolderState.Active:
                default:
                    break;
            }
        }

        // O4: a new SortOrder in the target scope, in the same transaction as
        // the folder change. The note lands at the end, which is where B11's
        // "move to folder" puts it — the user is filing it, not positioning it.
        _notes.Move(command.NoteId, command.TargetFolderId, NotePlacement.Last, _clock.UtcNow);

        return CommandResult.Success();
    }
}
