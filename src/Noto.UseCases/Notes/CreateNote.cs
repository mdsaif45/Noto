using Noto.Core;
using Noto.Core.Commands;
using Noto.Core.Folders;
using Noto.Core.Notes;

namespace Noto.UseCases.Notes;

/// <summary>
/// Creates a note (design §6, parity B1).
/// </summary>
/// <param name="FolderId">
/// The destination, or <see langword="null"/> for the root scope.
/// </param>
/// <param name="Content">Markdown source. Empty is valid — B26 has a placeholder for it.</param>
public sealed record CreateNote(FolderId? FolderId, string Content);

/// <summary>
/// Handles <see cref="CreateNote"/>.
/// </summary>
/// <remarks>
/// One handler per command, with no <c>NoteService</c> gathering twelve methods:
/// that god-object is what makes ADR-010's "every mutation goes through a
/// command" unenforceable (design §6).
/// </remarks>
public sealed class CreateNoteHandler(INoteRepository notes, IClock clock)
{
    private readonly INoteRepository _notes = notes
        ?? throw new ArgumentNullException(nameof(notes));

    private readonly IClock _clock = clock
        ?? throw new ArgumentNullException(nameof(clock));

    /// <summary>
    /// Creates the note.
    /// </summary>
    /// <returns>
    /// The new <see cref="NoteId"/>, or why it could not be created.
    /// </returns>
    public CommandResult<NoteId> Handle(CreateNote command)
    {
        ArgumentNullException.ThrowIfNull(command);

        // A null Content is a programming error — the record declares it
        // non-nullable — so it fails loudly rather than becoming a business
        // outcome. An *empty* note is entirely legitimate (B26).
        if (command.Content is null)
        {
            throw new ArgumentException("Content must not be null.", nameof(command));
        }

        if (command.FolderId is { } folderId && !_notes.ActiveFolderExists(folderId))
        {
            // Deliberately NotFound for both "no such folder" and "that folder
            // is in the bin": distinguishing them would leak the existence of a
            // deleted folder, and the caller's response is the same either way
            // — refresh the folder list.
            return CommandResult.Failed<NoteId>(
                CommandFailure.NotFound($"No active folder '{folderId}'."));
        }

        // One timestamp, used for both fields: a note that reports being
        // modified a tick after it was created is a lie the UI would surface
        // under "sort by modified" (contract §4).
        DateTimeOffset now = _clock.UtcNow;

        var note = new Note
        {
            Id = NoteId.New(),
            Content = command.Content,
            FolderId = command.FolderId,
            CreatedAt = now,
            UpdatedAt = now,

            // Remaining fields take their documented defaults: no colour, not
            // pinned, not folded, not deleted. SortOrder is assigned by the
            // ordering rules in Slice 2; until reordering exists, every note
            // sorts by the Id tiebreak, which is creation order (design §7
            // rule 2, ADR-012).
        };

        _notes.Add(note);

        return CommandResult.Success(note.Id);
    }
}
