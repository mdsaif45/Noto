using Noto.Core;
using Noto.Core.Commands;
using Noto.Core.Notes;
using Noto.UseCases.Commands;

namespace Noto.UseCases.Notes;

/// <summary>
/// Brings a note back out of the recycle bin (design §6, parity B23).
/// </summary>
public sealed record RestoreNote(NoteId NoteId) : ICommand<CommandResult>;

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

        // I7 (deletion-semantics §13): not into a folder that is itself in the
        // bin, where the note would come back invisible. The repository decides
        // it in the same statement as the write; the note is still in the bin,
        // so the only reason it can refuse is its folder. The user restores the
        // folder (Case D) or restores the note into another folder
        // (RestoreNoteToFolder).
        return _notes.Restore(command.NoteId, _clock.UtcNow)
            ? CommandResult.Success()
            : CommandResult.Failed(RecycleBinRules.FolderInBin(command.NoteId));
    }
}
