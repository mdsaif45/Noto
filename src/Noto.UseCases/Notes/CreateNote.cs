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
    /// <summary>
    /// Where the first note in an empty scope sits.
    /// </summary>
    /// <remarks>
    /// The schema defaults <c>SortOrder</c> to 0, so starting there keeps a
    /// scope's first note consistent with a row written by any other path.
    /// The value itself carries no meaning — only the relative order does
    /// (design §7) — and leaving room below it costs nothing, because O3's
    /// other end-insertion is min−1.
    /// </remarks>
    private const double InitialSortOrder = 0;

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

        if (command.FolderId is { } folderId)
        {
            // Two distinct outcomes, as the contract's CreateNote row requires:
            // a folder that never existed is NotFound, while one in the recycle
            // bin is InvalidState — it exists but is inert (invariant I5).
            switch (_notes.GetFolderState(folderId))
            {
                case FolderState.Missing:
                    return CommandResult.Failed<NoteId>(
                        CommandFailure.NotFound($"No folder '{folderId}'."));

                case FolderState.Deleted:
                    return CommandResult.Failed<NoteId>(
                        CommandFailure.InvalidState(
                            $"Folder '{folderId}' is deleted and cannot receive a note."));

                case FolderState.Active:
                default:
                    break;
            }
        }

        // One timestamp, used for both fields: a note that reports being
        // modified a tick after it was created is a lie the UI would surface
        // under "sort by modified" (contract §4).
        DateTimeOffset now = _clock.UtcNow;

        // O3: a new note goes at the END of its scope, which is max + 1. An
        // empty scope starts at the documented base rather than at max+1 of
        // nothing. Ordering is scoped per folder, and root is its own scope
        // (O1), so the scope is read from the note's destination.
        double? highest = _notes.MaxSortOrder(command.FolderId);
        double sortOrder = highest is { } max ? max + 1 : InitialSortOrder;

        var note = new Note
        {
            Id = NoteId.New(),
            Content = command.Content,
            FolderId = command.FolderId,
            SortOrder = sortOrder,
            CreatedAt = now,
            UpdatedAt = now,

            // Remaining fields take their documented defaults: no colour, not
            // pinned, not folded, not deleted.
        };

        _notes.Add(note);

        return CommandResult.Success(note.Id);
    }
}
