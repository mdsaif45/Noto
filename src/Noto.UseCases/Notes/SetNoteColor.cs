using Noto.Core;
using Noto.Core.Commands;
using Noto.Core.Notes;

namespace Noto.UseCases.Notes;

/// <summary>
/// Sets or clears a note's palette colour (design §6, parity B15).
/// </summary>
/// <param name="ColorKey">
/// One of <see cref="NoteColor.Keys"/> (<c>note1</c>…<c>note6</c>), or
/// <see langword="null"/> to clear — which is what <c>Ctrl+0</c> does.
/// </param>
public sealed record SetNoteColor(NoteId NoteId, string? ColorKey);

/// <summary>
/// Handles <see cref="SetNoteColor"/>.
/// </summary>
public sealed class SetNoteColorHandler(INoteRepository notes, IClock clock)
{
    private readonly INoteRepository _notes = notes ?? throw new ArgumentNullException(nameof(notes));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    public CommandResult Handle(SetNoteColor command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!NoteMutationGuard.TryLoadActive(_notes, command.NoteId, "recoloured", out Note note, out var failure))
        {
            return failure;
        }

        // Validated BEFORE anything is written. ColorKey is a TEXT column with
        // no CHECK constraint, so an unvalidated value would persist silently
        // and the palette would stop being a closed set (contract §11 row 9).
        // No normalising, no aliases: the stored value is an identifier, and
        // quietly turning "NOTE1" or "yellow" into note1 would invent meaning
        // the design deliberately does not give it.
        if (!NoteColor.IsValid(command.ColorKey))
        {
            // The rejected value is deliberately NOT echoed. It is caller input
            // of unbounded length and content, and a caller that passed the
            // wrong variable would put that into a message which may be logged
            // (principle 10). Naming the valid set is more useful anyway.
            return CommandResult.Failed(
                CommandFailure.InvalidInput(
                    $"Not a note palette key. Expected one of {string.Join(", ", NoteColor.Keys)}, or null to clear."));
        }

        if (string.Equals(note.ColorKey, command.ColorKey, StringComparison.Ordinal))
        {
            // Already that colour — including null to null. No write, no stamp.
            return CommandResult.Success();
        }

        _notes.SetColor(command.NoteId, command.ColorKey, _clock.UtcNow);

        return CommandResult.Success();
    }
}
