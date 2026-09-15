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
    /// The note, or <see cref="CommandFailureReason.NotFound"/> when no
    /// <b>active</b> note has that id.
    /// </returns>
    /// <remarks>
    /// Invariant I1 applies: this is an ordinary query, so a note in the
    /// recycle bin is <b>not</b> returned — it is reported as
    /// <see cref="CommandFailureReason.NotFound"/>, exactly like an id that
    /// never existed. The bin has one explicit surface,
    /// <c>ListDeletedNotes</c> (I2), which arrives in Slice 3.
    /// </remarks>
    public CommandResult<Note> Execute(NoteId id)
    {
        Note? note = _notes.FindActive(id);

        return note is null
            ? CommandResult.Failed<Note>(CommandFailure.NotFound($"No note '{id}'."))
            : CommandResult.Success(note);
    }
}
