using Noto.Core;
using Noto.Core.Commands;
using Noto.Core.Notes;

namespace Noto.UseCases.Notes;

/// <summary>
/// Brings a note back out of the recycle bin (design §6, parity B23).
/// </summary>
public sealed record RestoreNote(NoteId NoteId);

/// <summary>Handles <see cref="RestoreNote"/>.</summary>
/// <remarks>
/// Restoration is deliberately minimal: clear <c>DeletedAt</c>, stamp, done.
/// There is no placement step, because the note never lost its position —
/// I6 keeps a deleted row's <c>SortOrder</c> precisely so that "restore returns
/// a note roughly where it was" needs no logic at all.
/// </remarks>
public sealed class RestoreNoteHandler(INoteRepository notes, IClock clock)
{
    private readonly INoteRepository _notes = notes ?? throw new ArgumentNullException(nameof(notes));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    public CommandResult Handle(RestoreNote command)
    {
        ArgumentNullException.ThrowIfNull(command);

        switch (_notes.GetLifecycle(command.NoteId))
        {
            case NoteLifecycle.Missing:
                return CommandResult.Failed(
                    CommandFailure.NotFound($"No note '{command.NoteId}'."));

            case NoteLifecycle.Active:
                // Contract §6: InvalidState covers "RestoreNote/RestoreFolder
                // on a non-deleted one". Restoring something that was never in
                // the bin is a caller mistake, not a silent success.
                return CommandResult.Failed(
                    CommandFailure.InvalidState(
                        $"Note '{command.NoteId}' is not deleted."));

            case NoteLifecycle.Deleted:
            default:
                break;
        }

        // Case C: the note's FolderId is left exactly as it is, even when that
        // folder is itself still deleted. That state is legal by design, and
        // it is how a user pulls one note back out of a binned folder.
        _notes.Restore(command.NoteId, _clock.UtcNow);

        return CommandResult.Success();
    }
}
