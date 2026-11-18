using Noto.Core;
using Noto.Core.Commands;
using Noto.Core.Notes;
using Noto.UseCases.Commands;

namespace Noto.UseCases.Notes;

/// <summary>
/// Duplicates a note (parity B22, #232): a new note in the same folder with its text, colour and mode, directly below
/// it. One step to undo: delete the copy.
/// </summary>
public sealed record DuplicateNote(NoteId NoteId) : ICommand<CommandResult<NoteId>>;

/// <summary>
/// Handles <see cref="DuplicateNote"/>.
/// </summary>
/// <remarks>
/// <para>
/// The copy is not pinned and not folded: it is a new note, which is never either (CreateNote). Pinned notes sort
/// first (O2), so the copy of a pinned note goes to the top of the unpinned notes, the nearest place below it a new
/// note can have: <see cref="NewNotePlacementRule"/>'s "below the selected note", which already says so.
/// </para>
/// <para>
/// Pictures are shared, not copied (ADR-019 §4): the copy's text names the same <c>attachments/</c> files, and the
/// sweep keeps a file while any note, binned ones included, refers to it.
/// </para>
/// </remarks>
public sealed class DuplicateNoteHandler(INoteRepository notes, IClock clock)
{
    private readonly INoteRepository _notes = notes ?? throw new ArgumentNullException(nameof(notes));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    public CommandResult<NoteId> Handle(DuplicateNote command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!NoteMutationGuard.TryLoadActive(_notes, command.NoteId, "duplicated", out Note original, out var failure))
        {
            return CommandResult.Failed<NoteId>(failure.Failure!);
        }

        DateTimeOffset now = _clock.UtcNow;
        var copy = new Note
        {
            Id = NoteId.New(),
            Content = original.Content,
            FolderId = original.FolderId,
            ColorKey = original.ColorKey,
            Mode = original.Mode,
            CreatedAt = now,
            UpdatedAt = now,
        };

        NotePlacement placement = NewNotePlacementRule.Resolve(
            NewNotePlacement.BelowSelected, original.Id, _notes.ListInFolder(original.FolderId));

        // One transaction: the position and the insert (AddAt).
        _notes.AddAt(copy, placement);

        return CommandResult.Success(copy.Id);
    }
}
