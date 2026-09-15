using Noto.Core;
using Noto.Core.Commands;
using Noto.Core.Notes;

namespace Noto.UseCases.Notes;

/// <summary>
/// Moves a note to the recycle bin (design §6, parity B23).
/// </summary>
/// <remarks>
/// A <b>soft</b> delete. SideNotes has no trash — recovery there is backups
/// only — so this is a BETTER row Noto adds because ADR-002 means nothing can
/// be recovered from a server. It is also why B7 needs no blocking confirmation
/// dialog: the action is reversible.
/// </remarks>
public sealed record DeleteNote(NoteId NoteId);

/// <summary>Handles <see cref="DeleteNote"/>.</summary>
public sealed class DeleteNoteHandler(INoteRepository notes, IClock clock)
{
    private readonly INoteRepository _notes = notes ?? throw new ArgumentNullException(nameof(notes));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    public CommandResult Handle(DeleteNote command)
    {
        ArgumentNullException.ThrowIfNull(command);

        switch (_notes.GetLifecycle(command.NoteId))
        {
            case NoteLifecycle.Missing:
                return CommandResult.Failed(
                    CommandFailure.NotFound($"No note '{command.NoteId}'."));

            case NoteLifecycle.Deleted:
                // Deliberately NOT idempotent. Contract §11 row 3 lists
                // InvalidState among this command's failures, and I5 makes a
                // deleted entity inert: restore and purge are its only legal
                // transitions, so deleting it again is not one of them.
                return CommandResult.Failed(
                    CommandFailure.InvalidState(
                        $"Note '{command.NoteId}' is already deleted."));

            case NoteLifecycle.Active:
            default:
                break;
        }

        _notes.SoftDelete(command.NoteId, _clock.UtcNow);

        return CommandResult.Success();
    }
}
