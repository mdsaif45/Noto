using Noto.Core;
using Noto.Core.Commands;
using Noto.Core.Notes;
using Noto.UseCases.Commands;

namespace Noto.UseCases.Notes;

/// <summary>
/// Permanently deletes a note that is in the recycle bin (deletion-semantics §13, case E).
/// </summary>
public sealed record PurgeNote(NoteId NoteId) : ICommand<CommandResult>;

/// <summary>A note has been permanently deleted. Raised only after the row is gone.</summary>
public sealed record NotePurged(NoteId NoteId) : IEvent;

/// <summary>Handles <see cref="PurgeNote"/>.</summary>
/// <remarks>
/// Purging a note that is already gone is <c>NotFound</c>, as every command reports a missing target (contract §6).
/// The effect is idempotent: a second purge changes nothing and, failing, announces nothing.
/// </remarks>
public sealed class PurgeNoteHandler(INoteRepository notes)
{
    private readonly INoteRepository _notes = notes ?? throw new ArgumentNullException(nameof(notes));

    public CommandResult Handle(PurgeNote command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (RecycleBinRules.RefusePurge(command.NoteId, _notes.GetLifecycle(command.NoteId)) is { } refused)
        {
            return CommandResult.Failed(refused);
        }

        // The repository removes only a note still in the bin; if it is no longer there, nothing was removed.
        return _notes.Purge(command.NoteId)
            ? CommandResult.Success()
            : CommandResult.Failed(CommandFailure.NotFound($"No note '{command.NoteId}' in the recycle bin."));
    }
}
