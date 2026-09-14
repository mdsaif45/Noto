using Noto.Core.Commands;
using Noto.Core.Notes;

namespace Noto.UseCases.Notes;

/// <summary>
/// Reads one note by id (design §6).
/// </summary>
/// <remarks>
/// <para>
/// A query, not a command: reading goes direct, and wrapping it in command
/// ceremony buys nothing (ADR-010). It therefore <b>never</b> stamps
/// <c>UpdatedAt</c> or writes anything (contract §4).
/// </para>
/// <para>
/// It still returns <see cref="CommandResult{T}"/>, because a missing id must
/// surface as <see cref="CommandFailureReason.NotFound"/> rather than a null
/// the caller has to interpret — <c>null</c> is never an overloaded failure
/// channel (contract §6).
/// </para>
/// </remarks>
public sealed class GetNoteQuery(INoteRepository notes)
{
    private readonly INoteRepository _notes = notes
        ?? throw new ArgumentNullException(nameof(notes));

    /// <summary>
    /// Reads the note.
    /// </summary>
    /// <returns>
    /// The note, or <see cref="CommandFailureReason.NotFound"/> when no note
    /// has that id.
    /// </returns>
    /// <remarks>
    /// A note in the recycle bin is returned. Invariant I1 ("active by
    /// default") governs the queries that <i>list</i> notes; addressing one
    /// directly by id is how the bin is inspected and how restore confirms what
    /// it is restoring. Whether a note is deleted is visible on
    /// <see cref="Note.DeletedAt"/>, so no caller is misled.
    /// </remarks>
    public CommandResult<Note> Execute(NoteId id)
    {
        Note? note = _notes.Find(id);

        return note is null
            ? CommandResult.Failed<Note>(CommandFailure.NotFound($"No note '{id}'."))
            : CommandResult.Success(note);
    }
}
