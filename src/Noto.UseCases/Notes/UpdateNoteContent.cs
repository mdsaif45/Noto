using Noto.Core;
using Noto.Core.Commands;
using Noto.Core.Notes;

namespace Noto.UseCases.Notes;

/// <summary>
/// Replaces a note's content (design §6, parity B19).
/// </summary>
public sealed record UpdateNoteContent(NoteId NoteId, string Content);

/// <summary>
/// Handles <see cref="UpdateNoteContent"/>.
/// </summary>
public sealed class UpdateNoteContentHandler(INoteRepository notes, IClock clock)
{
    private readonly INoteRepository _notes = notes
        ?? throw new ArgumentNullException(nameof(notes));

    private readonly IClock _clock = clock
        ?? throw new ArgumentNullException(nameof(clock));

    public CommandResult Handle(UpdateNoteContent command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Content is null)
        {
            throw new ArgumentException("Content must not be null.", nameof(command));
        }

        // FindActive applies I1, so a note in the recycle bin comes back null
        // here — which would report NotFound and lose the distinction the
        // contract requires. The lifecycle state is read explicitly instead.
        NoteLifecycle state = _notes.GetLifecycle(command.NoteId);

        switch (state)
        {
            case NoteLifecycle.Missing:
                return CommandResult.Failed(
                    CommandFailure.NotFound($"No note '{command.NoteId}'."));

            case NoteLifecycle.Deleted:
                // I5: deleted entities are inert. Editing something in the bin
                // and then restoring it is the bug class this prevents.
                return CommandResult.Failed(
                    CommandFailure.InvalidState(
                        $"Note '{command.NoteId}' is deleted and cannot be edited."));

            case NoteLifecycle.Active:
            default:
                break;
        }

        // The title is not written: it is derived from Content on every read
        // (parity B16), so replacing the content re-derives it for free. There
        // is no second copy to keep in step.
        _notes.UpdateContent(command.NoteId, command.Content, _clock.UtcNow);

        return CommandResult.Success();
    }
}
