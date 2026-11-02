using Noto.Core;
using Noto.Core.Commands;
using Noto.Core.Folders;
using Noto.Core.Notes;
using Noto.UseCases.Commands;

namespace Noto.UseCases.Notes;

/// <summary>Pins a note to the top of its list (design §6, parity B14).</summary>
public sealed record PinNote(NoteId NoteId) : ICommand<CommandResult>;

/// <summary>Unpins a note (design §6, parity B14).</summary>
public sealed record UnpinNote(NoteId NoteId) : ICommand<CommandResult>;

/// <summary>Collapses a note to its first line (design §6, parity B12).</summary>
public sealed record FoldNote(NoteId NoteId) : ICommand<CommandResult>;

/// <summary>Expands a folded note (design §6, parity B12).</summary>
public sealed record UnfoldNote(NoteId NoteId) : ICommand<CommandResult>;

/// <summary>Folds or unfolds every note in a folder (parity B13, G21, G22). The result is how many changed.</summary>
public sealed record FoldNotesInFolder(FolderId FolderId, bool Folded) : ICommand<CommandResult<int>>;

/// <summary>A note was pinned or unpinned: published after the write commits (ADR-010).</summary>
public sealed record NotePinChanged(NoteId NoteId, bool Pinned) : IEvent;

/// <summary>A note was folded or unfolded: published after the write commits (ADR-010).</summary>
public sealed record NoteFoldChanged(NoteId NoteId, bool Folded) : IEvent;

/// <summary>A folder's notes were folded or unfolded together: published after the write commits (ADR-010).</summary>
public sealed record NotesFoldedInFolder(FolderId FolderId, bool Folded, int Changed) : IEvent;

/// <summary>Handles <see cref="FoldNotesInFolder"/>.</summary>
/// <remarks>Only an active folder's notes are folded; one statement, so all or none.</remarks>
public sealed class FoldNotesInFolderHandler(INoteRepository notes, IClock clock)
{
    private readonly INoteRepository _notes = notes ?? throw new ArgumentNullException(nameof(notes));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    public CommandResult<int> Handle(FoldNotesInFolder command)
    {
        ArgumentNullException.ThrowIfNull(command);

        switch (_notes.GetFolderState(command.FolderId))
        {
            case FolderState.Missing:
                return CommandResult.Failed<int>(CommandFailure.NotFound($"No folder '{command.FolderId}'."));

            case FolderState.Deleted:
                return CommandResult.Failed<int>(
                    CommandFailure.InvalidState($"Folder '{command.FolderId}' is deleted; its notes cannot be folded."));

            case FolderState.Active:
            default:
                break;
        }

        return CommandResult.Success(_notes.SetFoldedInFolder(command.FolderId, command.Folded, _clock.UtcNow));
    }
}

/// <summary>
/// The precondition shared by every single-flag note command.
/// </summary>
/// <remarks>
/// Written once rather than five times. Each of these commands has the same
/// shape — check the lifecycle, do nothing if the note is already in the
/// requested state, otherwise write one field and stamp — and repeating it per
/// handler is how the five slowly stop agreeing with each other.
/// </remarks>
internal static class NoteMutationGuard
{
    /// <summary>
    /// Loads a note that may legally be mutated.
    /// </summary>
    /// <returns>
    /// The active note, or the failure the contract requires: `NotFound` when
    /// no such note exists, `InvalidState` when it is in the recycle bin and
    /// therefore inert (I5).
    /// </returns>
    public static bool TryLoadActive(
        INoteRepository notes,
        NoteId id,
        string verb,
        out Note note,
        out CommandResult failure)
    {
        Note? found = notes.FindActive(id);

        if (found is not null)
        {
            note = found;
            failure = default;
            return true;
        }

        note = null!;

        // FindActive applies I1, so it cannot distinguish "missing" from
        // "deleted" — and the contract maps those to different failures.
        failure = notes.GetLifecycle(id) == NoteLifecycle.Deleted
            ? CommandResult.Failed(
                CommandFailure.InvalidState($"Note '{id}' is deleted and cannot be {verb}."))
            : CommandResult.Failed(
                CommandFailure.NotFound($"No note '{id}'."));

        return false;
    }
}

/// <summary>Handles <see cref="PinNote"/>.</summary>
public sealed class PinNoteHandler(INoteRepository notes, IClock clock)
{
    private readonly INoteRepository _notes = notes ?? throw new ArgumentNullException(nameof(notes));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    public CommandResult Handle(PinNote command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!NoteMutationGuard.TryLoadActive(_notes, command.NoteId, "pinned", out Note note, out var failure))
        {
            return failure;
        }

        if (note.IsPinned)
        {
            // Already in the requested state: success, but no write and no
            // stamp (contract §4). Pinning something twice is not an error.
            return CommandResult.Success();
        }

        // IsPinned only. SortOrder is untouched, so unpinning later returns the
        // note to exactly where it sits in the sequence (contract §11 row 8).
        _notes.SetPinned(command.NoteId, isPinned: true, _clock.UtcNow);

        return CommandResult.Success();
    }
}

/// <summary>Handles <see cref="UnpinNote"/>.</summary>
public sealed class UnpinNoteHandler(INoteRepository notes, IClock clock)
{
    private readonly INoteRepository _notes = notes ?? throw new ArgumentNullException(nameof(notes));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    public CommandResult Handle(UnpinNote command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!NoteMutationGuard.TryLoadActive(_notes, command.NoteId, "unpinned", out Note note, out var failure))
        {
            return failure;
        }

        if (!note.IsPinned)
        {
            return CommandResult.Success();
        }

        // The note's SortOrder was never disturbed by pinning, so there is
        // nothing to restore — it simply stops being lifted by O2.
        _notes.SetPinned(command.NoteId, isPinned: false, _clock.UtcNow);

        return CommandResult.Success();
    }
}

/// <summary>Handles <see cref="FoldNote"/>.</summary>
public sealed class FoldNoteHandler(INoteRepository notes, IClock clock)
{
    private readonly INoteRepository _notes = notes ?? throw new ArgumentNullException(nameof(notes));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    public CommandResult Handle(FoldNote command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!NoteMutationGuard.TryLoadActive(_notes, command.NoteId, "folded", out Note note, out var failure))
        {
            return failure;
        }

        if (note.IsFolded)
        {
            return CommandResult.Success();
        }

        _notes.SetFolded(command.NoteId, isFolded: true, _clock.UtcNow);

        return CommandResult.Success();
    }
}

/// <summary>Handles <see cref="UnfoldNote"/>.</summary>
public sealed class UnfoldNoteHandler(INoteRepository notes, IClock clock)
{
    private readonly INoteRepository _notes = notes ?? throw new ArgumentNullException(nameof(notes));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    public CommandResult Handle(UnfoldNote command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!NoteMutationGuard.TryLoadActive(_notes, command.NoteId, "unfolded", out Note note, out var failure))
        {
            return failure;
        }

        if (!note.IsFolded)
        {
            return CommandResult.Success();
        }

        _notes.SetFolded(command.NoteId, isFolded: false, _clock.UtcNow);

        return CommandResult.Success();
    }
}
