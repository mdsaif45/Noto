using Noto.Core.Commands;
using Noto.Core.Notes;
using Noto.Core.Tags;

namespace Noto.UseCases.Tags;

/// <summary>Applies a tag to a note (design §6, contract §11 row 22).</summary>
public sealed record AssignTagToNote(NoteId NoteId, TagId TagId);

/// <summary>Removes a tag from a note (design §6, contract §11 row 23).</summary>
public sealed record RemoveTagFromNote(NoteId NoteId, TagId TagId);

/// <summary>
/// The note precondition shared by both relationship commands.
/// </summary>
/// <remarks>
/// <para>
/// Steps 1 and 2 of §7's ladder, written once. Both commands need exactly this
/// and nothing more; the tag check differs between them (see U12b) and so stays
/// in the handler that needs it.
/// </para>
/// <para>
/// <b>I5 is evaluated before idempotency</b> (§7). A deleted note plus an
/// already-assigned tag is <c>InvalidState</c>, not success — deletion-semantics
/// I5 names tagging explicitly: *"a deleted note cannot be edited, moved,
/// reordered, pinned or tagged"*. Checking the relationship first would let a
/// deleted note report success and quietly break that.
/// </para>
/// </remarks>
internal static class TagRelationshipGuard
{
    public static bool TryEnsureNoteActive(
        INoteRepository notes,
        NoteId id,
        string verb,
        out CommandResult failure)
    {
        switch (notes.GetLifecycle(id))
        {
            case NoteLifecycle.Active:
                failure = default;
                return true;

            case NoteLifecycle.Deleted:
                failure = CommandResult.Failed(
                    CommandFailure.InvalidState($"Note '{id}' is deleted and cannot be {verb}."));
                return false;

            case NoteLifecycle.Missing:
            default:
                failure = CommandResult.Failed(CommandFailure.NotFound($"No note '{id}'."));
                return false;
        }
    }
}

/// <summary>Handles <see cref="AssignTagToNote"/>.</summary>
/// <remarks>
/// <para>
/// Idempotent (§7, U11a): assigning a tag the note already carries succeeds,
/// writes nothing and stamps nothing. <c>Assign(A,B)</c> twice reaches the same
/// state as once, which is what makes the command safe to retry.
/// </para>
/// <para>
/// <b>The note row is never modified</b> (contract §4): <c>NoteTags</c> has no
/// timestamps and tagging is not an edit of the note. This holds on the
/// successful path too, not only on the no-op — a tag assignment must not make
/// a note look recently modified.
/// </para>
/// </remarks>
public sealed class AssignTagToNoteHandler(INoteRepository notes, ITagRepository tags)
{
    private readonly INoteRepository _notes = notes
        ?? throw new ArgumentNullException(nameof(notes));

    private readonly ITagRepository _tags = tags
        ?? throw new ArgumentNullException(nameof(tags));

    public CommandResult Handle(AssignTagToNote command)
    {
        ArgumentNullException.ThrowIfNull(command);

        // §7 ladder, steps 1-2: note exists, note active.
        if (!TagRelationshipGuard.TryEnsureNoteActive(
            _notes, command.NoteId, "tagged", out var failure))
        {
            return failure;
        }

        // Step 3: the tag must exist. Assign has to reach an end state that
        // REQUIRES the tag, so a missing one is a genuine NotFound — unlike
        // RemoveTagFromNote, where a missing tag already satisfies the goal
        // (§7, U12b).
        if (_tags.Find(command.TagId) is null)
        {
            return CommandResult.Failed(CommandFailure.NotFound($"No tag '{command.TagId}'."));
        }

        // Step 4: already as requested — success, no write, no stamp.
        if (_tags.HasRelationship(command.NoteId, command.TagId))
        {
            return CommandResult.Success();
        }

        _tags.Assign(command.NoteId, command.TagId);

        return CommandResult.Success();
    }
}

/// <summary>Handles <see cref="RemoveTagFromNote"/>.</summary>
/// <remarks>
/// <para>
/// Idempotent (§7, U11b): removing a tag the note does not carry succeeds,
/// writes nothing and stamps nothing.
/// </para>
/// <para>
/// <b>The tag's existence is deliberately not checked</b> (§7, U12b). A tag
/// that does not exist cannot be related to the note, so the requested end
/// state — this note does not carry that tag — already holds. Returning
/// <c>NotFound</c> would report failure for a condition the command was asked
/// to bring about. This is the one place the two relationship commands are
/// asymmetric, and the asymmetry is in the contract rather than a shortcut.
/// </para>
/// </remarks>
public sealed class RemoveTagFromNoteHandler(INoteRepository notes, ITagRepository tags)
{
    private readonly INoteRepository _notes = notes
        ?? throw new ArgumentNullException(nameof(notes));

    private readonly ITagRepository _tags = tags
        ?? throw new ArgumentNullException(nameof(tags));

    public CommandResult Handle(RemoveTagFromNote command)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Steps 1-2 only. I5 still applies to the note: a deleted note cannot
        // be untagged any more than it can be tagged.
        if (!TagRelationshipGuard.TryEnsureNoteActive(
            _notes, command.NoteId, "untagged", out var failure))
        {
            return failure;
        }

        // No tag-existence check (U12b). HasRelationship returns false for a
        // tag that does not exist, which lands on the same no-op branch as an
        // absent relationship — the two cases are indistinguishable to the
        // caller, and the contract says they should be.
        if (!_tags.HasRelationship(command.NoteId, command.TagId))
        {
            return CommandResult.Success();
        }

        _tags.Remove(command.NoteId, command.TagId);

        return CommandResult.Success();
    }
}
