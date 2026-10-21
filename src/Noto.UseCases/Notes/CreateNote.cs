using Noto.Core;
using Noto.Core.Commands;
using Noto.Core.Folders;
using Noto.Core.Notes;
using Noto.UseCases.Commands;

namespace Noto.UseCases.Notes;

/// <summary>
/// Creates a note (design §6, parity B1).
/// </summary>
/// <param name="FolderId">
/// The destination: always a folder. An active note with no folder is reachable from no surface (I7,
/// deletion-semantics §13), so the command cannot express one.
/// </param>
/// <param name="Content">Markdown source. Empty is valid — B26 has a placeholder for it.</param>
/// <param name="Placement">Where it goes in the folder (B5, J8). <see cref="NewNotePlacement.Bottom"/> by default.</param>
/// <param name="Selected">The selected note, for the placements relative to it.</param>
public sealed record CreateNote(
    FolderId FolderId,
    string Content,
    NewNotePlacement Placement = NewNotePlacement.Bottom,
    NoteId? Selected = null) : ICommand<CommandResult<NoteId>>;

/// <summary>
/// A note was created (ADR-010, #21): the reference event, published by the dispatcher after <see cref="CreateNote"/>
/// succeeds. Identities only, never the note's content.
/// </summary>
/// <param name="NoteId">The new note.</param>
/// <param name="FolderId">Where it was created.</param>
public sealed record NoteCreated(NoteId NoteId, FolderId FolderId) : IEvent;

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

        // Two distinct outcomes, as the contract's CreateNote row requires:
        // a folder that never existed is NotFound, while one in the recycle
        // bin is InvalidState — it exists but is inert (invariant I5).
        FolderId folderId = command.FolderId;
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
            // pinned, not folded, not deleted.
        };

        // B5, J8: the setting decides where in the folder the note goes. The
        // folder's notes are read in display order only when the placement
        // needs them; an unknown setting or a selection that is not one of
        // them falls back to the end (NewNotePlacementRule).
        NotePlacement placement = command.Placement == NewNotePlacement.Bottom
            ? NotePlacement.Last
            : NewNotePlacementRule.Resolve(command.Placement, command.Selected, _notes.ListInFolder(command.FolderId));

        if (placement.AtEnd)
        {
            // O3: at the END of its scope, which is max + 1, exactly as before
            // the setting existed. An empty scope starts at the documented base
            // rather than at max+1 of nothing. Ordering is scoped per folder
            // (O1), so the scope is read from the note's destination.
            double? highest = _notes.MaxSortOrder(command.FolderId);
            _notes.Add(note with { SortOrder = highest is { } max ? max + 1 : InitialSortOrder });
        }
        else
        {
            // First, or after a sibling: the position and the insert are one
            // transaction, renormalising the scope if its gap is spent (O6).
            _notes.AddAt(note, placement);
        }

        return CommandResult.Success(note.Id);
    }
}
