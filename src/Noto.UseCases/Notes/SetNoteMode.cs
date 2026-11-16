using Noto.Core;
using Noto.Core.Commands;
using Noto.Core.Notes;
using Noto.UseCases.Commands;

namespace Noto.UseCases.Notes;

/// <summary>
/// Sets how a note's text is read: Markdown, Code or plain text (#195; parity D27, D28). The text is never changed.
/// </summary>
public sealed record SetNoteMode(NoteId NoteId, NoteMode Mode) : ICommand<CommandResult>;

/// <summary>
/// Handles <see cref="SetNoteMode"/>.
/// </summary>
public sealed class SetNoteModeHandler(INoteRepository notes, IClock clock)
{
    private readonly INoteRepository _notes = notes ?? throw new ArgumentNullException(nameof(notes));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    public CommandResult Handle(SetNoteMode command)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Validated before anything is written: the column holds a member's name, and a value outside the enum would
        // be written as a number no reader knows.
        if (!Enum.IsDefined(command.Mode))
        {
            return CommandResult.Failed(
                CommandFailure.InvalidInput($"Not a note mode. Expected one of {string.Join(", ", Enum.GetNames<NoteMode>())}."));
        }

        if (!NoteMutationGuard.TryLoadActive(_notes, command.NoteId, "changed", out Note note, out var failure))
        {
            return failure;
        }

        if (note.Mode == command.Mode)
        {
            // Already in that mode: no write, no stamp.
            return CommandResult.Success();
        }

        _notes.SetMode(command.NoteId, command.Mode, _clock.UtcNow);

        return CommandResult.Success();
    }
}
