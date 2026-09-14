using Noto.Core;
using Noto.Core.Commands;
using Noto.Core.Notes;

namespace Noto.UseCases.Notes;

/// <summary>
/// Repositions a note within its scope (design §6, parity B8/B10).
/// </summary>
/// <param name="AfterNoteId">
/// The active sibling to sit immediately after, or <see langword="null"/> to
/// place the note first.
/// </param>
/// <remarks>
/// No folder argument: the scope is the note's current <c>FolderId</c> (U9).
/// No <c>SortOrder</c> argument either — the engine computes it (contract §5),
/// which is what keeps the O6 renormalisation trigger out of the caller.
/// </remarks>
public sealed record ReorderNote(NoteId NoteId, NoteId? AfterNoteId);

/// <summary>
/// Handles <see cref="ReorderNote"/>.
/// </summary>
/// <remarks>
/// Atomic with any renormalisation it triggers (design §9).
/// </remarks>
public sealed class ReorderNoteHandler(INoteRepository notes, IClock clock)
{
    private readonly INoteRepository _notes = notes
        ?? throw new ArgumentNullException(nameof(notes));

    private readonly IClock _clock = clock
        ?? throw new ArgumentNullException(nameof(clock));

    public CommandResult Handle(ReorderNote command)
    {
        ArgumentNullException.ThrowIfNull(command);

        Note? note = _notes.FindActive(command.NoteId);

        if (note is null)
        {
            // Separated so a deleted note reports InvalidState (I5) rather than
            // NotFound — FindActive alone cannot tell the two apart.
            return _notes.GetLifecycle(command.NoteId) == NoteLifecycle.Deleted
                ? CommandResult.Failed(
                    CommandFailure.InvalidState(
                        $"Note '{command.NoteId}' is deleted and cannot be reordered."))
                : CommandResult.Failed(
                    CommandFailure.NotFound($"No note '{command.NoteId}'."));
        }

        NotePlacement placement;

        if (command.AfterNoteId is { } target)
        {
            // The target must be an ACTIVE sibling in the SAME scope. Deleted
            // rows never participate in ordering (I6), and a target in another
            // folder would silently move the note across scopes — which is
            // MoveNoteToFolder's job, not this one.
            if (!_notes.IsActiveSiblingIn(target, note.FolderId))
            {
                return CommandResult.Failed(
                    CommandFailure.InvalidInput(
                        $"Note '{target}' is not an active sibling in the same scope."));
            }

            placement = NotePlacement.After(target);
        }
        else
        {
            placement = NotePlacement.First;
        }

        // The repository reports whether anything actually moved. A
        // same-position reorder writes nothing and stamps nothing (contract
        // §5), so it is a success with no side effect rather than a failure.
        _notes.Reorder(command.NoteId, placement, _clock.UtcNow);

        return CommandResult.Success();
    }
}
